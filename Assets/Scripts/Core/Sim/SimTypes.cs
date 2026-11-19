using System;
using System.Collections.Generic;
using Swan.Core.Data;

namespace Swan.Core.Sim
{
    /// <summary>
    /// 一格的全部状态（交接包 §2）。纯数值，不引用任何引擎类型，方便单测。
    /// dirty 不放在 Cell 里：那是「渲染要不要重画」的表现层信息，放在 World 的并行数组里。
    /// </summary>
    public struct Cell
    {
        /// <summary>没有植物的标记值。用 0 而不是 null，避免在 struct 里放可空引用。</summary>
        public const int NoPlant = 0;

        public float Water;
        public float Nutrient;
        public float Temperature;
        public int TerrainId;
        public bool HasHeatSource;

        // —— 放置物 / 野物 ——
        /// <summary>结构表 id；0 = 空地。</summary>
        public int StructureId;
        /// <summary>采集物表里的物品 id；null/空 = 没有野物。</summary>
        public string ForageItemId;

        // —— 植物 ——
        public int PlantTypeId;
        public float Growth;
        public bool Harvestable;
        /// <summary>性状 id 数组（交接包要求必须是数组；同一株的性状不重复）。</summary>
        public string[] Traits;

        public bool HasPlant => PlantTypeId != NoPlant;
        public bool HasStructure => StructureId != 0;
        public bool HasForage => !string.IsNullOrEmpty(ForageItemId);

        public static Cell Create(float water, float nutrient, float temperature, int terrainId)
        {
            return new Cell
            {
                Water = water,
                Nutrient = nutrient,
                Temperature = temperature,
                TerrainId = terrainId,
                HasHeatSource = false,
                StructureId = 0,
                ForageItemId = null,
                PlantTypeId = NoPlant,
                Growth = 0f,
                Harvestable = false,
                Traits = null
            };
        }

        public void Plant(int plantTypeId, string[] traits)
        {
            PlantTypeId = plantTypeId;
            Growth = 0f;
            Harvestable = false;
            Traits = traits ?? Array.Empty<string>();
        }

        public void ClearPlant()
        {
            PlantTypeId = NoPlant;
            Growth = 0f;
            Harvestable = false;
            Traits = null;
        }
    }

    /// <summary>
    /// 一株植物按性状表算出来的修正值。每次现算（不缓存到 Cell），
    /// 这样热重载改了性状表数值，下一环境步就生效，不用重建世界。
    /// </summary>
    public struct TraitEffects
    {
        public float GrowthMul;
        public float IntakeMul;
        public float YieldAdd;
        public float NutrientPerStep;
        public float EvaporationMul;
        public float? AutoIgniteTemp;

        public static TraitEffects Neutral()
        {
            return new TraitEffects
            {
                GrowthMul = 1f,
                IntakeMul = 1f,
                YieldAdd = 0f,
                NutrientPerStep = 0f,
                EvaporationMul = 1f,
                AutoIgniteTemp = null
            };
        }

        /// <summary>
        /// 同 traitId 多行全部生效（表里 G04 就是两行）：乘法类连乘、加法类累加。
        /// v01Active=0 的性状只读入不执行。
        /// </summary>
        public static TraitEffects Resolve(TraitTable traits, string[] traitIds)
        {
            var effects = Neutral();
            if (traitIds == null) return effects;

            for (int i = 0; i < traitIds.Length; i++)
            {
                var rows = traits.Get(traitIds[i]);
                for (int r = 0; r < rows.Count; r++)
                {
                    var row = rows[r];
                    if (!row.ActiveInV01) continue;
                    switch (row.Effect)
                    {
                        case Data.TraitEffectKey.GrowthRateMul:
                            effects.GrowthMul *= row.Value;
                            break;
                        case Data.TraitEffectKey.PlantIntakeMul:
                            effects.IntakeMul *= row.Value;
                            break;
                        case Data.TraitEffectKey.YieldAdd:
                            effects.YieldAdd += row.Value;
                            break;
                        case Data.TraitEffectKey.SoilNutrientPerStep:
                            effects.NutrientPerStep += row.Value;
                            break;
                        case Data.TraitEffectKey.EvaporationMul:
                            effects.EvaporationMul *= row.Value;
                            break;
                        case Data.TraitEffectKey.AutoIgniteTemp:
                            effects.AutoIgniteTemp = row.Value;
                            break;
                        default:
                            // intakeRadius / tempAdd / pestGuard / mutationWeightMul：v0.1 不执行（表里 v01Active=0）
                            break;
                    }
                }
            }
            return effects;
        }
    }

    /// <summary>法则 + 预先解析好的数值（避免每格每步重复解析 effectValue 字符串）。</summary>
    public sealed class RuleValue
    {
        public Data.RuleRow Row;
        public float Value;

        public RuleValue(Data.RuleRow row, float value)
        {
            Row = row;
            Value = value;
        }
    }

    /// <summary>
    /// 把法则表里「v0.1 真的会执行」的行预先分好类。
    /// 避免每个格子每个环境步都去翻 10 行法则、再解析一次字符串。
    /// </summary>
    public sealed class RuleBook
    {
        public bool HasOverflow;
        public float OverflowThreshold;
        public float OverflowRatio;

        public bool HasBurn;
        public float BurnThreshold;
        public float BurnNutrientGain;
        public string BurnTraitId;

        public readonly List<RuleValue> NutrientDeltas = new List<RuleValue>();
        public readonly List<RuleValue> EvaporationMuls = new List<RuleValue>();

        public static RuleBook Build(Data.RuleTable rules)
        {
            var book = new RuleBook();
            foreach (var row in rules.Rows)
            {
                if (!row.ExecutedInV01) continue;
                var numbers = row.ParseEffectNumbers();
                switch (row.Effect)
                {
                    case Data.RuleEffect.OverflowToLowestNeighbor:
                        book.HasOverflow = true;
                        book.OverflowThreshold = row.Threshold;
                        book.OverflowRatio = numbers[0];
                        break;
                    case Data.RuleEffect.BurnPlant:
                        book.HasBurn = true;
                        book.BurnThreshold = row.Threshold;
                        book.BurnNutrientGain = numbers[0];
                        book.BurnTraitId = row.TriggerArg;
                        break;
                    case Data.RuleEffect.NutrientDelta:
                        book.NutrientDeltas.Add(new RuleValue(row, numbers[0]));
                        break;
                    case Data.RuleEffect.EvaporationMul:
                        book.EvaporationMuls.Add(new RuleValue(row, numbers[0]));
                        break;
                }
            }
            return book;
        }
    }

    /// <summary>游戏内滚动日志（给 HUD 看，也给排查用）。</summary>
    public sealed class SimLog
    {
        public const int Capacity = 200;

        readonly List<string> _lines = new List<string>();

        public IReadOnlyList<string> Lines => _lines;

        public void Add(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _lines.Add(message);
            if (_lines.Count > Capacity) _lines.RemoveRange(0, _lines.Count - Capacity);
        }

        public string LastLine => _lines.Count == 0 ? string.Empty : _lines[_lines.Count - 1];
    }

    /// <summary>全局统计（§5 第 7 步 / T8 HUD）。</summary>
    public struct WorldStats
    {
        /// <summary>全图水量总和。</summary>
        public float TotalWater;
        /// <summary>全图水量总和 / 格数。</summary>
        public float AverageWater;
        /// <summary>水量占上限的比例（0..1）。</summary>
        public float WaterCoverage;
        /// <summary>单一种植格数：同一种植物里数量最多的那一种。</summary>
        public int MonocultureCells;
        /// <summary>单一种植的植物名（HUD 显示用）。</summary>
        public string MonocultureName;
        /// <summary>已成熟、可以收的植物格数。</summary>
        public int HarvestableCells;
        /// <summary>有植物的总格数。</summary>
        public int PlantedCells;
    }
}
