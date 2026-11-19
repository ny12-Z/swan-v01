using System;
using System.Collections.Generic;
using Swan.Core.Data;

namespace Swan.Core.Sim
{
    /// <summary>
    /// 世界（格子状态 + 环境步流水）。纯 C#，不依赖引擎，EditMode 测试直接 new 出来跑。
    ///
    /// 双缓冲：一个环境步里所有读取都取「步前快照」，写入都落到 _next，步末整体换手。
    /// 这样七个子步骤的先后顺序就是唯一的时间语义，不会因为「谁先写到同一格」而出现隐藏耦合。
    ///
    /// 七步顺序见交接包 §5：
    ///   1 植物吸水(含固氮 L04) → 2 地形扣减(含蒸发倍率 L05) → 3 生长(含燃烧 L03)
    ///   → 4 水溢出(L01) → 5 温度扩散+回归 → 6 地形重判(deadband) → 7 收获标记 + 统计
    /// </summary>
    public sealed class World
    {
        public const int NoPlant = Cell.NoPlant;

        EngineConfig _config;
        DataBundle _data;
        RuleBook _rules;

        int _width;
        int _height;

        Cell[] _cells;          // 当前状态
        Cell[] _next;           // 本步写入目标
        float[] _waterDelta;    // 溢出的暂存量
        float[] _evapMul;       // 本步每格的蒸发倍率（L05）
        bool[] _dirty;          // 表现层脏标记
        readonly List<int> _dirtyList = new List<int>();
        Rng _forageRng;
        int _forageCount;

        // 植物修正值缓存：key = typeId + 性状签名，热重载时整体清空
        readonly Dictionary<long, TraitEffects> _effectCache = new Dictionary<long, TraitEffects>();

        readonly SimLog _log = new SimLog();
        WorldStats _stats;
        long _envSteps;
        float _totalHarvested;
        int _harvestCount;

        public int Width => _width;
        public int Height => _height;
        public int CellCount => _width * _height;
        public EngineConfig Config => _config;
        public DataBundle Data => _data;
        public RuleBook Rules => _rules;
        public SimLog Log => _log;
        public WorldStats Stats => _stats;
        public long EnvStepCount => _envSteps;
        public float TotalHarvested => _totalHarvested;
        public int HarvestCount => _harvestCount;
        public IReadOnlyList<int> DirtyIndices => _dirtyList;

        /// <param name="width">为空则用 config 的网格尺寸并套用地图预设（正式游戏）；传值只铺基准值（探针/测试用）。</param>
        public World(DataBundle data, int? width = null, int? height = null)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _config = data.Config;
            _rules = RuleBook.Build(data.Rules);

            bool usePreset = !width.HasValue;
            _width = width ?? _config.GridWidth;
            _height = height ?? _config.GridHeight;
            if (_width < 1 || _height < 1) throw new ArgumentException("网格尺寸必须 ≥ 1");

            AllocateBuffers();
            FillBaseline();

            if (usePreset) ApplyPreset();
            SeedForage();
            SeedRocks();
            RefreshStats();
            MarkAllDirty();
        }

        // ————————————————————————————— 查询 —————————————————————————————

        public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < _width && y < _height;

        public int IndexOf(int x, int y) => y * _width + x;

        public Cell GetCell(int x, int y)
        {
            if (!Contains(x, y)) throw new ArgumentOutOfRangeException($"坐标越界: ({x},{y})");
            return _cells[IndexOf(x, y)];
        }

        public Cell GetCellByIndex(int index) => _cells[index];

        public bool IsDirty(int index) => _dirty[index];

        // ————————————————————————————— 玩家交互（T7） —————————————————————————————

        /// <summary>种一株植物。默认种可种植列表里的第一种（表里只有 P01 是 v01Active=1）。</summary>
        public bool TryPlantDefault(int x, int y, out string message)
        {
            string defaultType = null;
            foreach (var row in _data.Plants.Rows)
            {
                if (!row.PlantableInV01) continue;
                defaultType = row.TypeId;
                break;
            }
            if (defaultType == null)
            {
                message = "植物类型表里没有 v01Active=1 的植物，没法种";
                return false;
            }
            return TryPlant(x, y, defaultType, out message);
        }

        public bool TryPlant(int x, int y, string typeId, out string message)
        {
            if (!Contains(x, y))
            {
                message = "坐标越界";
                return false;
            }

            int index = IndexOf(x, y);
            var cell = _cells[index];

            if (cell.HasPlant)
            {
                message = "这一格已经有植物了";
                return false;
            }
            if (cell.StructureId != 0)
            {
                message = "这一格有放置物，先收回来再种";
                return false;
            }
            if (!string.IsNullOrEmpty(cell.ForageItemId))
            {
                message = "这一格有野物，先采集再种";
                return false;
            }
            if (!_data.Plants.TryGet(typeId, out var type))
            {
                message = $"植物类型表里没有 {typeId}";
                return false;
            }
            if (!type.PlantableInV01)
            {
                message = $"{type.Name}({type.TypeId}) 在 v0.1 不开放种植（v01Active=0）";
                return false;
            }
            if (!type.CanGrowAt(cell.Water, cell.Nutrient))
            {
                message = $"{type.Name} 种不下去：需要水 {type.RequiredWaterMin}~{type.RequiredWaterMax}、养分 ≥ {type.RequiredNutrientMin}，" +
                          $"这格是水 {cell.Water:0.#}、养分 {cell.Nutrient:0.#}";
                return false;
            }

            cell.Plant(PlantTypeIdOf(type), CloneTraits(type.InitTraits));
            _cells[index] = cell;
            MarkDirty(index);
            _log.Add($"种下 {type.Name} @({x},{y})");
            message = $"种下 {type.Name}";
            return true;
        }

        public bool TryHarvest(int x, int y, out float yield, out string message)
        {
            yield = 0f;
            if (!Contains(x, y))
            {
                message = "坐标越界";
                return false;
            }

            int index = IndexOf(x, y);
            var cell = _cells[index];
            if (!cell.HasPlant)
            {
                message = "这一格没有植物";
                return false;
            }
            if (!cell.Harvestable)
            {
                message = $"还没熟：成长 {cell.Growth:0.#}/{_config.RipeThreshold:0.#}";
                return false;
            }

            var type = _data.Plants.RowByIndex(cell.PlantTypeId);
            var effects = EffectsFor(cell);
            yield = type.BaseYield + effects.YieldAdd;   // baseYield 来自表，YieldAdd 是性状累加
            _totalHarvested += yield;
            _harvestCount++;

            cell.ClearPlant();
            _cells[index] = cell;
            MarkDirty(index);
            _log.Add($"收获 {type.Name} @({x},{y}) → {yield:0.#} 个（累计 {_totalHarvested:0.#}）");
            message = $"收获 {yield:0.#} 个";
            return true;
        }

        public bool SetHeatSource(int x, int y, bool on)
        {
            if (!Contains(x, y)) return false;
            int index = IndexOf(x, y);
            var cell = _cells[index];
            if (cell.HasHeatSource == on) return false;
            cell.HasHeatSource = on;
            _cells[index] = cell;
            MarkDirty(index);
            _log.Add(on ? $"加热源 @({x},{y})" : $"移除热源 @({x},{y})");
            return true;
        }

        public bool ToggleHeatSource(int x, int y, out bool nowOn)
        {
            nowOn = false;
            if (!Contains(x, y)) return false;
            nowOn = !_cells[IndexOf(x, y)].HasHeatSource;
            return SetHeatSource(x, y, nowOn);
        }

        /// <summary>放一个结构。结构 id 来自结构表；这里只做占用检查，不碰背包。</summary>
        public bool TrySetStructure(int x, int y, int structureId, out string message)
        {
            if (!Contains(x, y))
            {
                message = "坐标越界";
                return false;
            }
            if (!_data.Structures.TryGetRow(structureId, out var structure))
            {
                message = $"结构表里没有 {structureId}";
                return false;
            }

            int index = IndexOf(x, y);
            var cell = _cells[index];
            if (cell.StructureId != 0)
            {
                message = "这一格已经有放置物了，先收回来";
                return false;
            }
            if (cell.HasPlant)
            {
                message = "这一格有作物，先收获再放";
                return false;
            }
            if (cell.HasHeatSource)
            {
                message = "这一格有热源，先关掉再放";
                return false;
            }
            if (!string.IsNullOrEmpty(cell.ForageItemId))
            {
                message = "这一格有野物，先采集再放";
                return false;
            }

            cell.StructureId = structure.StructureId;
            _cells[index] = cell;
            MarkDirty(index);
            _log.Add($"放置 {structure.Name} @({x},{y})");
            message = $"放置了{structure.Name}";
            return true;
        }

        public bool TryRemoveStructure(int x, int y, out string message)
        {
            if (!Contains(x, y))
            {
                message = "坐标越界";
                return false;
            }
            int index = IndexOf(x, y);
            var cell = _cells[index];
            if (cell.StructureId == 0)
            {
                message = "这一格没有放置物";
                return false;
            }

            var structure = _data.Structures.Get(cell.StructureId);
            cell.StructureId = 0;
            _cells[index] = cell;
            MarkDirty(index);
            _log.Add($"收回 {structure.Name} @({x},{y})");
            message = $"收回了{structure.Name}";
            return true;
        }

        public bool TryCollectForage(int x, int y, out string itemId, out int count, out string message)
        {
            itemId = null;
            count = 0;
            if (!Contains(x, y))
            {
                message = "坐标越界";
                return false;
            }

            int index = IndexOf(x, y);
            var cell = _cells[index];
            if (string.IsNullOrEmpty(cell.ForageItemId))
            {
                message = "这一格没有野物";
                return false;
            }

            itemId = cell.ForageItemId;
            count = 1;
            cell.ForageItemId = null;
            _cells[index] = cell;
            _forageCount = Math.Max(0, _forageCount - 1);
            MarkDirty(index);

            string name = _data.Items.TryGet(itemId, out var item) ? item.Name : itemId;
            _log.Add($"采集 {name} @({x},{y})");
            message = $"采集到{name}×{count}";
            return true;
        }

        /// <summary>浇水 / 施肥 / 挖掘共用的土质修改。会立即按新数值重判地形，点击后马上看到变化。</summary>
        public bool TryApplySoil(int x, int y, float waterDelta, float nutrientDelta, out string message)
        {
            if (!Contains(x, y))
            {
                message = "坐标越界";
                return false;
            }

            int index = IndexOf(x, y);
            var cell = _cells[index];
            cell.Water = ClampWater(cell.Water + waterDelta);
            cell.Nutrient = ClampNutrient(cell.Nutrient + nutrientDelta);
            cell.TerrainId = _data.Terrain.Resolve(cell.Water, cell.Nutrient).TerrainId;
            _cells[index] = cell;
            MarkDirty(index);

            message = $"水 {cell.Water:0.#} / 养分 {cell.Nutrient:0.#}（{_data.Terrain.GetById(cell.TerrainId).Name}）";
            return true;
        }

        public bool SetWater(int x, int y, float water)
        {
            return TryApplySoil(x, y, water - GetCell(x, y).Water, 0f, out _);
        }

        public bool SetNutrient(int x, int y, float nutrient)
        {
            return TryApplySoil(x, y, 0f, nutrient - GetCell(x, y).Nutrient, out _);
        }

        public int ForageCount => _forageCount;

        // ————————————————————————————— 探针/测试用的直接读写 —————————————————————————————

        /// <summary>直接设定一格的数值并重判地形（初始地图、对照曲线探针用）。</summary>
        public void SetCellValues(int x, int y, float water, float nutrient)
        {
            if (!Contains(x, y)) throw new ArgumentOutOfRangeException($"坐标越界: ({x},{y})");
            int index = IndexOf(x, y);
            var cell = _cells[index];
            cell.Water = ClampWater(water);
            cell.Nutrient = ClampNutrient(nutrient);
            cell.TerrainId = _data.Terrain.Resolve(cell.Water, cell.Nutrient).TerrainId;
            _cells[index] = cell;
            MarkDirty(index);
        }

        public void SetTemperature(int x, int y, float temperature)
        {
            int index = IndexOf(x, y);
            var cell = _cells[index];
            cell.Temperature = temperature;
            _cells[index] = cell;
            MarkDirty(index);
        }

        /// <summary>直接放一株植物（不检查门槛，地图预设/探针用）。</summary>
        public void SeedPlant(int x, int y, string typeId)
        {
            int index = IndexOf(x, y);
            var cell = _cells[index];
            var type = _data.Plants.Get(typeId);
            cell.Plant(PlantTypeIdOf(type), CloneTraits(type.InitTraits));
            _cells[index] = cell;
            MarkDirty(index);
            RefreshStats();
        }

        // ————————————————————————————— 环境步 —————————————————————————————

        public void StepEnvironment()
        {
            StepPlantIntakeAndFixation();   // 1
            StepTerrainDecay();             // 2
            StepGrowthAndBurn();            // 3
            StepOverflow();                 // 4
            StepTemperature();              // 5
            StepTerrainReevaluation();      // 6
            StepHarvestAndStats();          // 7
            Commit();
            StepForageRegrow();             // 野物缓慢再生（表现/采集用，不参与环境 7 步）
        }

        public void StepEnvironment(int times)
        {
            for (int i = 0; i < times; i++) StepEnvironment();
        }

        /// <summary>1 植物吸水 + 固氮（L04）。读 _cells，写 _next。</summary>
        void StepPlantIntakeAndFixation()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                Cell cell = _cells[i];
                if (cell.HasPlant)
                {
                    var effects = EffectsFor(cell);
                    cell.Water = _cells[i].Water - _config.PlantIntakeBase * effects.IntakeMul;
                    float fixation = NutrientDeltaFor(cell);
                    if (fixation != 0f) cell.Nutrient = _cells[i].Nutrient + fixation;
                }
                _next[i] = cell;
            }
        }

        /// <summary>2 地形扣减（表里的 dWaterPerStep / dNutrientPerStep）。读 _next，写 _next。</summary>
        void StepTerrainDecay()
        {
            ComputeEvaporationMultipliers();
            for (int i = 0; i < _next.Length; i++)
            {
                Cell cell = _next[i];
                var terrain = _data.Terrain.GetById(cell.TerrainId);
                float dWater = terrain.DWaterPerStep;
                if (dWater != 0f && _evapMul[i] != 1f) dWater *= _evapMul[i];
                cell.Water = ClampWater(cell.Water + dWater);
                cell.Nutrient = ClampNutrient(cell.Nutrient + terrain.DNutrientPerStep);
                _next[i] = cell;
            }
        }

        /// <summary>3 生长（含门槛）+ 燃烧（L03）。读 _next，写 _next。</summary>
        void StepGrowthAndBurn()
        {
            for (int i = 0; i < _next.Length; i++)
            {
                Cell cell = _next[i];
                if (!cell.HasPlant) continue;

                if (!_data.Plants.TryGetRow(cell.PlantTypeId, out var type))
                {
                    cell.ClearPlant();
                    _next[i] = cell;
                    continue;
                }

                // 燃烧：性状类触发看植物身上有没有这条性状（不要求性状自身 v01Active）
                if (_rules.HasBurn && cell.Temperature > _rules.BurnThreshold && HasTrait(cell, _rules.BurnTraitId))
                {
                    cell.ClearPlant();
                    cell.Nutrient = ClampNutrient(cell.Nutrient + _rules.BurnNutrientGain);
                    _next[i] = cell;
                    continue;
                }

                var effects = EffectsFor(cell);
                float perStep = type.GrowthPerStep
                                * (_config.StackInitialGrowthTraits ? effects.GrowthMul : 1f)
                                * _config.GrowthSpeedMultiplier;
                if (perStep > 0f && CanGrow(cell, type) && cell.Growth < _config.RipeThreshold)
                {
                    cell.Growth = Math.Min(cell.Growth + perStep, _config.RipeThreshold);
                    _next[i] = cell;
                }
            }
        }

        /// <summary>4 水溢出（L01）：超过阈值的水流向 4 邻最低格。</summary>
        void StepOverflow()
        {
            if (!_rules.HasOverflow) return;

            Array.Clear(_waterDelta, 0, _waterDelta.Length);
            bool any = false;

            for (int i = 0; i < _next.Length; i++)
            {
                float water = _next[i].Water;
                if (water <= _rules.OverflowThreshold) continue;

                int best = -1;
                float bestWater = float.PositiveInfinity;
                int x = i % _width;
                int y = i / _width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 1 ? 1 : d == 3 ? -1 : 0);
                    int ny = y + (d == 0 ? 1 : d == 2 ? -1 : 0);
                    if (nx < 0) nx = 0;
                    if (ny < 0) ny = 0;
                    if (nx >= _width) nx = _width - 1;
                    if (ny >= _height) ny = _height - 1;
                    int j = ny * _width + nx;
                    if (j == i) continue;
                    if (_next[j].Water < bestWater)
                    {
                        bestWater = _next[j].Water;
                        best = j;
                    }
                }

                if (best < 0 || bestWater >= water) continue;

                float transfer = (water - bestWater) * _rules.OverflowRatio;
                if (transfer <= 0f) continue;
                _waterDelta[i] -= transfer;
                _waterDelta[best] += transfer;
                any = true;
            }

            if (!any) return;
            for (int i = 0; i < _waterDelta.Length; i++)
            {
                if (_waterDelta[i] == 0f) continue;
                var cell = _next[i];
                cell.Water = ClampWater(cell.Water + _waterDelta[i]);
                _next[i] = cell;
            }
        }

        /// <summary>5 温度：向 4 邻扩散 + 回归基准温 + 热源加温。读 _cells 的旧温度。</summary>
        void StepTemperature()
        {
            for (int i = 0; i < _next.Length; i++)
            {
                float t = _cells[i].Temperature;
                int x = i % _width;
                int y = i / _width;

                float sum = 0f;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 1 ? 1 : d == 3 ? -1 : 0);
                    int ny = y + (d == 0 ? 1 : d == 2 ? -1 : 0);
                    if (nx < 0) nx = 0;
                    if (ny < 0) ny = 0;
                    if (nx >= _width) nx = _width - 1;
                    if (ny >= _height) ny = _height - 1;
                    sum += _cells[ny * _width + nx].Temperature;
                }

                float neighborAverage = sum * 0.25f;
                float next = t
                             + _config.TempSpread * (neighborAverage - t)
                             + _config.TempReturn * (_config.BaseTemperature - t);
                if (_cells[i].HasHeatSource) next += _config.HeatSourceBoost;

                var cell = _next[i];
                cell.Temperature = next;
                _next[i] = cell;
            }
        }

        /// <summary>6 地形重判：换档要跨过 deadband 才算数，避免在边界上反复横跳。</summary>
        void StepTerrainReevaluation()
        {
            for (int i = 0; i < _next.Length; i++)
            {
                Cell cell = _next[i];
                var candidate = _data.Terrain.Resolve(cell.Water, cell.Nutrient);
                if (candidate.TerrainId == cell.TerrainId) continue;

                float margin = CrossingMargin(cell, candidate);
                if (margin <= _config.TerrainHysteresis) continue;

                cell.TerrainId = candidate.TerrainId;
                _next[i] = cell;
            }
        }

        /// <summary>7 收获标记 + 全局统计。</summary>
        void StepHarvestAndStats()
        {
            for (int i = 0; i < _next.Length; i++)
            {
                Cell cell = _next[i];
                bool harvestable = cell.HasPlant && cell.Growth >= _config.RipeThreshold;
                if (cell.Harvestable != harvestable)
                {
                    cell.Harvestable = harvestable;
                    _next[i] = cell;
                }
            }
            RefreshStatsFromNext();
        }

        void Commit()
        {
            var swap = _cells;
            _cells = _next;
            _next = swap;
            _envSteps++;
            UpdateDirtyFromSwap();
        }

        // ————————————————————————————— 热重载 / 重置 —————————————————————————————

        /// <summary>
        /// 换一份数据（热重载）。网格尺寸不变时保留所有格子状态；
        /// 尺寸变了就重建地图，并在日志里说清楚。
        /// </summary>
        public void ApplyData(DataBundle newData)
        {
            if (newData == null) throw new ArgumentNullException(nameof(newData));

            // 格子里的植物 id 是按「旧表的下标」存的，换表前先记住旧表，才能按 typeId 重新对号
            var oldPlants = _data.Plants;
            bool sizeChanged = newData.Config.GridWidth != _width || newData.Config.GridHeight != _height;

            _data = newData;
            _config = newData.Config;
            _rules = RuleBook.Build(newData.Rules);
            _effectCache.Clear();

            if (sizeChanged)
            {
                _width = _config.GridWidth;
                _height = _config.GridHeight;
                AllocateBuffers();
                FillBaseline();
                ApplyPreset();
                SeedForage();
                SeedRocks();
                _envSteps = 0;
                RefreshStats();
                MarkAllDirty();
                _log.Add($"热重载：网格尺寸变成 {_width}×{_height}，地图已重建（旧状态无法保留）");
                return;
            }

            int removedPlants = 0;
            for (int i = 0; i < _cells.Length; i++)
            {
                Cell cell = _cells[i];
                cell.Water = ClampWater(cell.Water);
                cell.Nutrient = ClampNutrient(cell.Nutrient);

                if (cell.HasPlant)
                {
                    bool survives = oldPlants.TryGetRow(cell.PlantTypeId, out var oldType)
                                    && _data.Plants.TryGet(oldType.TypeId, out var newType)
                                    && newType.PlantableInV01;
                    if (survives)
                    {
                        cell.PlantTypeId = PlantTypeIdOf(_data.Plants.Get(oldPlants.RowByIndex(cell.PlantTypeId).TypeId));
                    }
                    else
                    {
                        cell.ClearPlant();
                        removedPlants++;
                    }
                }

                // 地形表可能改过区间/档位：按新表重判（这里不走 deadband，换了表就该立刻纠正）
                cell.TerrainId = _data.Terrain.Resolve(cell.Water, cell.Nutrient).TerrainId;

                // 结构/野物可能在新表里被删了：查不到就清掉，避免渲染层拿到悬空 id
                if (cell.StructureId != 0 && !_data.Structures.TryGetRow(cell.StructureId, out _))
                {
                    cell.StructureId = 0;
                }
                if (!string.IsNullOrEmpty(cell.ForageItemId) && !_data.Items.Contains(cell.ForageItemId))
                {
                    cell.ForageItemId = null;
                }

                _cells[i] = cell;
            }

            RefreshStats();
            MarkAllDirty();
            _log.Add($"热重载完成：{_data.Plants.Rows.Count} 种植物 / {_data.Terrain.Rows.Count} 种地形" +
                     (removedPlants > 0 ? $"，移除 {removedPlants} 株本版不开放的植物" : ""));
        }

        /// <summary>按当前 config + 预设重建初始地图（R 键）。</summary>
        public void RegenerateMap()
        {
            FillBaseline();
            ApplyPreset();
            SeedForage();
            SeedRocks();
            _envSteps = 0;
            RefreshStats();
            MarkAllDirty();
            _log.Add("地图已按当前配置重建");
        }

        // ————————————————————————————— 内部：初始化 —————————————————————————————

        void AllocateBuffers()
        {
            int count = _width * _height;
            _cells = new Cell[count];
            _next = new Cell[count];
            _waterDelta = new float[count];
            _evapMul = new float[count];
            _dirty = new bool[count];
            _dirtyList.Clear();
            for (int i = 0; i < count; i++) _evapMul[i] = 1f;
        }

        void FillBaseline()
        {
            var rng = new Rng((uint)_config.MapSeed);
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    float water = _config.MapBaselineWater + (rng.NextFloat() * 2f - 1f) * _config.MapJitterWater;
                    float nutrient = _config.MapBaselineNutrient + (rng.NextFloat() * 2f - 1f) * _config.MapJitterNutrient;
                    water = ClampWater(water);
                    nutrient = ClampNutrient(nutrient);
                    int terrainId = _data.Terrain.Resolve(water, nutrient).TerrainId;
                    _cells[IndexOf(x, y)] = Cell.Create(water, nutrient, _config.BaseTemperature, terrainId);
                    _next[IndexOf(x, y)] = _cells[IndexOf(x, y)];
                }
            }
        }

        void ApplyPreset()
        {
            var preset = _data.Preset;
            if (preset == null) return;

            foreach (var region in preset.Regions)
            {
                int r = region.Radius;
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx + dy * dy > r * r) continue;   // 圆形区域
                        int x = region.X + dx;
                        int y = region.Y + dy;
                        if (!Contains(x, y)) continue;
                        int index = IndexOf(x, y);
                        var cell = _cells[index];
                        cell.Water = ClampWater(region.Water);
                        cell.Nutrient = ClampNutrient(region.Nutrient);
                        cell.TerrainId = _data.Terrain.Resolve(cell.Water, cell.Nutrient).TerrainId;
                        _cells[index] = cell;
                        _next[index] = cell;
                    }
                }
            }

            foreach (var heat in preset.HeatSources)
            {
                if (!Contains(heat.X, heat.Y)) continue;
                int index = IndexOf(heat.X, heat.Y);
                var cell = _cells[index];
                cell.HasHeatSource = true;
                _cells[index] = cell;
                _next[index] = cell;
            }

            foreach (var plant in preset.Plants)
            {
                if (!Contains(plant.X, plant.Y)) continue;
                if (!_data.Plants.TryGet(plant.TypeId, out var type)) continue;
                int index = IndexOf(plant.X, plant.Y);
                var cell = _cells[index];
                cell.Plant(PlantTypeIdOf(type), CloneTraits(type.InitTraits));
                _cells[index] = cell;
                _next[index] = cell;
            }
        }

        // ————————————————————————————— 内部：野物 —————————————————————————————

        /// <summary>按采集物表给每个地形铺初始野物。用独立 seed，保证同一张地图每次一样。</summary>
        void SeedForage()
        {
            _forageCount = 0;
            if (_data.Forage == null) return;   // 单格探针只带最小数据包，没有采集物表
            _forageRng = new Rng((uint)_config.ForageSeed);
            for (int i = 0; i < _cells.Length; i++)
            {
                Cell cell = _cells[i];
                cell.ForageItemId = null;
                if (cell.HasPlant || cell.StructureId != 0 || cell.HasHeatSource) continue;
                if (!_data.Forage.TryGetForTerrain(cell.TerrainId, out var row)) continue;
                if (_forageRng.NextFloat() >= row.Chance) continue;
                cell.ForageItemId = row.ItemId;
                _cells[i] = cell;
                _next[i] = cell;
                _forageCount++;
            }
        }

        /// <summary>
        /// 按 config.rockChance 在地表铺岩石结构。岩石挡路/挡种，用「采集」可以搬走换回 stone。
        /// 确定性：同一个 rockSeed 永远得到同一批落点，方便复现和测试。
        /// </summary>
        void SeedRocks()
        {
            if (_config.RockChance <= 0f || _config.RockStructureId <= 0) return;
            if (_data.Structures == null || !_data.Structures.TryGetRow(_config.RockStructureId, out _)) return;

            var rng = new Rng((uint)_config.RockSeed);
            for (int i = 0; i < _cells.Length; i++)
            {
                Cell cell = _cells[i];
                if (cell.HasPlant || cell.StructureId != 0 || cell.HasHeatSource || cell.HasForage) continue;
                if (cell.Water > _config.WalkableMaxWater) continue;   // 深水里不铺石头
                if (rng.NextFloat() >= _config.RockChance) continue;

                cell.StructureId = _config.RockStructureId;
                _cells[i] = cell;
                _next[i] = cell;
                MarkDirty(i);
            }
        }

        /// <summary>环境步结束后，空地上以很低概率长出新的野物；总数不超过 config.forageMaxCount。</summary>
        void StepForageRegrow()
        {
            if (_data.Forage == null) return;
            if (_forageRng == null) _forageRng = new Rng((uint)_config.ForageSeed);
            if (_config.ForageRegrowChance <= 0f || _forageCount >= _config.ForageMaxCount) return;

            for (int i = 0; i < _cells.Length; i++)
            {
                if (_forageCount >= _config.ForageMaxCount) break;
                Cell cell = _cells[i];
                if (cell.HasPlant || cell.StructureId != 0 || cell.HasHeatSource || cell.HasForage) continue;
                if (!_data.Forage.TryGetForTerrain(cell.TerrainId, out var row)) continue;
                if (_forageRng.NextFloat() >= _config.ForageRegrowChance) continue;
                cell.ForageItemId = row.ItemId;
                _cells[i] = cell;
                _forageCount++;
                MarkDirty(i);
            }
        }

        // ————————————————————————————— 内部：工具 —————————————————————————————

        float ClampWater(float value) => MathOps.Clamp(value, _config.WaterMin, _config.WaterMax);

        float ClampNutrient(float value) => MathOps.Clamp(value, _config.NutrientMin, _config.NutrientMax);

        /// <summary>生长门槛：表里的 per-type 门槛与 config 的全局下限取更严格的那个。</summary>
        bool CanGrow(Cell cell, PlantTypeRow type)
        {
            float waterMin = Math.Max(_config.GrowWaterMin, type.RequiredWaterMin);
            float nutrientMin = Math.Max(_config.GrowNutrientMin, type.RequiredNutrientMin);
            if (cell.Water < waterMin) return false;
            if (cell.Water > type.RequiredWaterMax) return false;
            if (cell.Nutrient < nutrientMin) return false;
            if (cell.Temperature < _config.GrowTempMin) return false;
            if (cell.Temperature > _config.GrowTempMax) return false;
            return true;
        }

        /// <summary>跨过的那条档位边界有多远。没跨轴的返回 +∞（那一轴不构成阻碍）。</summary>
        float CrossingMargin(Cell cell, TerrainRow candidate)
        {
            var oldRow = _data.Terrain.GetById(cell.TerrainId);
            float water = AxisMargin(cell.Water, oldRow.WaterMin, oldRow.WaterMax, candidate.WaterMin, candidate.WaterMax);
            float nutrient = AxisMargin(cell.Nutrient, oldRow.NutrientMin, oldRow.NutrientMax, candidate.NutrientMin, candidate.NutrientMax);
            return Math.Min(water, nutrient);
        }

        static float AxisMargin(float value, float oldMin, float oldMax, float newMin, float newMax)
        {
            if (Math.Abs(oldMin - newMin) < 1e-4f && Math.Abs(oldMax - newMax) < 1e-4f) return float.PositiveInfinity;
            float edge = newMin > oldMin ? newMin : newMax;
            return Math.Abs(value - edge);
        }

        bool HasTrait(Cell cell, string traitId)
        {
            if (string.IsNullOrEmpty(traitId) || cell.Traits == null) return false;
            for (int i = 0; i < cell.Traits.Length; i++)
            {
                if (cell.Traits[i] == traitId) return true;
            }
            return false;
        }

        /// <summary>固氮类法则（L04）。数值预先从法则表读好，避免每格每步重新解析字符串。</summary>
        float NutrientDeltaFor(Cell cell)
        {
            var list = _rules.NutrientDeltas;
            if (list.Count == 0 || cell.Traits == null) return 0f;
            float sum = 0f;
            for (int r = 0; r < list.Count; r++)
            {
                var rule = list[r];
                if (HasTrait(cell, rule.Row.TriggerArg)) sum += rule.Value;
            }
            return sum;
        }

        /// <summary>防风类法则（L05）：半径内每株带触发性状的植物给一个倍率。</summary>
        void ComputeEvaporationMultipliers()
        {
            var list = _rules.EvaporationMuls;
            for (int i = 0; i < _evapMul.Length; i++) _evapMul[i] = 1f;
            if (list.Count == 0) return;

            for (int i = 0; i < _cells.Length; i++)
            {
                Cell cell = _cells[i];
                if (!cell.HasPlant || cell.Traits == null) continue;

                for (int r = 0; r < list.Count; r++)
                {
                    var rule = list[r];
                    if (!HasTrait(cell, rule.Row.TriggerArg)) continue;
                    int radius = rule.Row.Radius;
                    int cx = i % _width;
                    int cy = i / _width;
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int x = cx + dx;
                            int y = cy + dy;
                            if (!Contains(x, y)) continue;
                            _evapMul[IndexOf(x, y)] *= rule.Value;
                        }
                    }
                }
            }
        }

        TraitEffects EffectsFor(Cell cell)
        {
            long key = ((long)cell.PlantTypeId << 32) | (uint)TraitSignature(cell.Traits);
            if (_effectCache.TryGetValue(key, out var cached)) return cached;
            var effects = TraitEffects.Resolve(_data.Traits, cell.Traits);
            _effectCache[key] = effects;
            return effects;
        }

        static int TraitSignature(string[] traits)
        {
            if (traits == null) return 0;
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < traits.Length; i++)
                {
                    hash = hash * 31 + (traits[i]?.GetHashCode() ?? 0);
                }
                return hash;
            }
        }

        int PlantTypeIdOf(PlantTypeRow type) => _data.Plants.IndexOf(type.TypeId);

        static string[] CloneTraits(string[] traits)
        {
            if (traits == null || traits.Length == 0) return Array.Empty<string>();
            var clone = new string[traits.Length];
            Array.Copy(traits, clone, traits.Length);
            return clone;
        }

        // ————————————————————————————— 内部：脏格 / 统计 —————————————————————————————

        void MarkDirty(int index)
        {
            if (_dirty[index]) return;
            _dirty[index] = true;
            _dirtyList.Add(index);
        }

        public void ClearDirty()
        {
            for (int i = 0; i < _dirtyList.Count; i++) _dirty[_dirtyList[i]] = false;
            _dirtyList.Clear();
        }

        void MarkAllDirty()
        {
            _dirtyList.Clear();
            for (int i = 0; i < _dirty.Length; i++) _dirty[i] = false;
            for (int i = 0; i < _dirty.Length; i++) MarkDirty(i);
        }

        /// <summary>换手后新旧缓冲恰好是「步后 / 步前」，逐格比对得出要重画的格子。</summary>
        void UpdateDirtyFromSwap()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_dirty[i]) continue;
                Cell now = _cells[i];
                Cell before = _next[i];
                if (now.TerrainId != before.TerrainId
                    || now.HasHeatSource != before.HasHeatSource
                    || now.HasPlant != before.HasPlant
                    || now.PlantTypeId != before.PlantTypeId
                    || now.StructureId != before.StructureId
                    || !string.Equals(now.ForageItemId, before.ForageItemId)
                    || now.Harvestable != before.Harvestable
                    || Math.Abs(now.Growth - before.Growth) > 1e-4f
                    || Math.Abs(now.Temperature - before.Temperature) > 1e-4f)
                {
                    MarkDirty(i);
                }
            }
        }

        void RefreshStats()
        {
            for (int i = 0; i < _cells.Length; i++) _dirty[i] = false;
            RefreshStatsFromBuffer(_cells);
            _dirtyList.Clear();
        }

        void RefreshStatsFromNext() => RefreshStatsFromBuffer(_next);

        void RefreshStatsFromBuffer(Cell[] buffer)
        {
            float totalWater = 0f;
            int planted = 0;
            int harvestable = 0;

            for (int i = 0; i < buffer.Length; i++)
            {
                totalWater += buffer[i].Water;
                if (!buffer[i].HasPlant) continue;
                planted++;
                if (buffer[i].Harvestable) harvestable++;
            }

            int bestCount = 0;
            string bestName = null;
            foreach (var type in _data.Plants.Rows)
            {
                int id = PlantTypeIdOf(type);
                int count = 0;
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i].PlantTypeId == id) count++;
                }
                if (count > bestCount)
                {
                    bestCount = count;
                    bestName = type.Name;
                }
            }

            float coverage = buffer.Length == 0 ? 0f : totalWater / (buffer.Length * _config.WaterMax);
            _stats = new WorldStats
            {
                TotalWater = totalWater,
                AverageWater = buffer.Length == 0 ? 0f : totalWater / buffer.Length,
                WaterCoverage = coverage,
                MonocultureCells = bestCount,
                MonocultureName = bestName,
                HarvestableCells = harvestable,
                PlantedCells = planted
            };
        }
    }
}
