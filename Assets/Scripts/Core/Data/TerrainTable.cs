using System;
using System.Collections.Generic;

namespace Swan.Core.Data
{
    public static class MathOps
    {
        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        public static float Min(params float[] values)
        {
            float result = float.PositiveInfinity;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] < result) result = values[i];
            }
            return result;
        }

        public static float Max(params float[] values)
        {
            float result = float.NegativeInfinity;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > result) result = values[i];
            }
            return result;
        }
    }

    /// <summary>地形表的一行。数值原样来自 CSV，代码里不许出现第二份。</summary>
    public sealed class TerrainRow
    {
        public int TerrainId;
        public int WaterBand;
        public int NutrientBand;
        public float WaterMin;
        public float WaterMax;
        public float NutrientMin;
        public float NutrientMax;
        public string Name;
        public string Desc;
        public float DWaterPerStep;
        public float DNutrientPerStep;
        public string ColorHex;
        public bool ShowInV01;
        public string Note;
        public int SourceLine;
        public string FileName;

        /// <summary>文档 §10.3 的地形命中式，原样照抄。</summary>
        public bool Matches(float water, float nutrient)
        {
            return water >= WaterMin && (water < WaterMax || WaterMax >= 100f)
                && nutrient >= NutrientMin && (nutrient < NutrientMax || NutrientMax >= 100f);
        }

        public override string ToString()
        {
            return $"{TerrainId}:{Name}";
        }
    }

    /// <summary>
    /// 地形表 = 25 宫格（5 档水 × 5 档养分）。加载时把「缺格 / 档位不连续 / 主轴重复」全部查出来，
    /// 免得运行到某个数值才发现查不到地形。
    /// </summary>
    public sealed class TerrainTable
    {
        public const string FileName = "数值表(地形属性表)-机读.csv";

        readonly List<TerrainRow> _rows;
        readonly Dictionary<int, TerrainRow> _byId;

        public IReadOnlyList<TerrainRow> Rows => _rows;

        TerrainTable(List<TerrainRow> rows)
        {
            _rows = rows;
            _byId = new Dictionary<int, TerrainRow>();
            foreach (var row in rows) _byId[row.TerrainId] = row;
        }

        public TerrainRow GetById(int terrainId)
        {
            if (_byId.TryGetValue(terrainId, out var row)) return row;
            throw new DataLoadException(FileName, 0, "terrainId", $"查不到地形 {terrainId}（表里没有这个主键）");
        }

        public bool TryGetById(int terrainId, out TerrainRow row)
        {
            return _byId.TryGetValue(terrainId, out row);
        }

        /// <summary>
        /// 按文档 §10.3 找出命中的地形。调用方必须先 clamp 到 [waterMin, waterMax] / [nutrientMin, nutrientMax]。
        /// 找不到 → 直接报错（静默返回 0 会让 bug 更难查）。
        /// </summary>
        public TerrainRow Resolve(float water, float nutrient)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Matches(water, nutrient)) return _rows[i];
            }
            throw new DataLoadException(FileName, 0, null,
                $"水={water:0.###} 养分={nutrient:0.###} 没有命中任何地形（25 宫格有洞）");
        }

        public static TerrainTable Load(CsvDocument doc, float waterMin, float waterMax, float nutrientMin, float nutrientMax, DataWarnings warnings)
        {
            var rows = new List<TerrainRow>();
            var ids = new HashSet<int>();
            var cells = new Dictionary<int, TerrainRow>();

            foreach (var row in doc.Rows)
            {
                var terrain = new TerrainRow
                {
                    TerrainId = row.Int("terrainId"),
                    WaterBand = row.Int("waterBand"),
                    NutrientBand = row.Int("nutrientBand"),
                    WaterMin = row.Float("waterMin"),
                    WaterMax = row.Float("waterMax"),
                    NutrientMin = row.Float("nutrientMin"),
                    NutrientMax = row.Float("nutrientMax"),
                    Name = row.Require("name"),
                    Desc = row.Raw("desc", string.Empty),
                    DWaterPerStep = row.Float("dWaterPerStep"),
                    DNutrientPerStep = row.Float("dNutrientPerStep"),
                    ColorHex = row.Require("v01Color"),
                    ShowInV01 = row.Bool("v01Show"),
                    Note = row.Raw("note", string.Empty),
                    SourceLine = row.Line,
                    FileName = doc.FileName
                };

                if (!ids.Add(terrain.TerrainId))
                    throw row.Error("terrainId", $"主键重复：{terrain.TerrainId}");
                if (terrain.WaterMin > terrain.WaterMax) throw row.Error("waterMin", "waterMin 大于 waterMax");
                if (terrain.NutrientMin > terrain.NutrientMax) throw row.Error("nutrientMin", "nutrientMin 大于 nutrientMax");
                if (terrain.WaterBand < 0 || terrain.NutrientBand < 0) throw row.Error("waterBand", "档位不能是负数");

                int key = terrain.WaterBand * 100 + terrain.NutrientBand;
                if (cells.TryGetValue(key, out var existing))
                    throw row.Error("waterBand", $"宫格重复：水档 {terrain.WaterBand} × 养分档 {terrain.NutrientBand} 已由地形 {existing.TerrainId} 占用");
                cells[key] = terrain;
                rows.Add(terrain);
            }

            ValidateAxis(rows, true, waterMin, waterMax);
            ValidateAxis(rows, false, nutrientMin, nutrientMax);
            ValidateCoverage(rows, waterMin, waterMax, nutrientMin, nutrientMax);

            return new TerrainTable(rows);
        }

        /// <summary>同一档位的区间必须一致，档位必须从 0 连续排下去（0,1,2,...）。</summary>
        static void ValidateAxis(List<TerrainRow> rows, bool water, float axisMin, float axisMax)
        {
            var bands = new SortedDictionary<int, KeyValuePair<float, float>>();
            foreach (var row in rows)
            {
                int band = water ? row.WaterBand : row.NutrientBand;
                float min = water ? row.WaterMin : row.NutrientMin;
                float max = water ? row.WaterMax : row.NutrientMax;
                if (bands.TryGetValue(band, out var existing))
                {
                    if (Math.Abs(existing.Key - min) > 1e-4f || Math.Abs(existing.Value - max) > 1e-4f)
                    {
                        throw new DataLoadException(row.FileName, row.SourceLine,
                            water ? "waterMin" : "nutrientMin",
                            $"档位 {band} 的区间前后不一致：之前是 [{existing.Key}, {existing.Value}]，这里是 [{min}, {max}]");
                    }
                }
                else
                {
                    bands[band] = new KeyValuePair<float, float>(min, max);
                }
            }

            int expectedBand = 0;
            float previousMax = axisMin;
            foreach (var pair in bands)
            {
                if (pair.Key != expectedBand)
                {
                    throw new DataLoadException(FileName, 0, water ? "waterBand" : "nutrientBand",
                        $"档位编号不连续：期望 {expectedBand}，实际出现 {pair.Key}");
                }
                if (Math.Abs(pair.Value.Key - previousMax) > 1e-4f)
                {
                    throw new DataLoadException(FileName, 0, water ? "waterMin" : "nutrientMin",
                        $"档位 {pair.Key} 的起点 {pair.Value.Key} 与上一档终点 {previousMax} 对不上（区间有缝或重叠）");
                }
                previousMax = pair.Value.Value;
                expectedBand++;
            }

            if (Math.Abs(previousMax - axisMax) > 1e-4f)
            {
                throw new DataLoadException(FileName, 0, water ? "waterMax" : "nutrientMax",
                    $"最后一档的终点是 {previousMax}，但配置的上限是 {axisMax}（覆盖不完整）");
            }
        }

        /// <summary>25 宫格必须填满；再用边界值实测命中式，确认没有落入缝里。</summary>
        static void ValidateCoverage(List<TerrainRow> rows, float waterMin, float waterMax, float nutrientMin, float nutrientMax)
        {
            var waterBands = new SortedDictionary<int, KeyValuePair<float, float>>();
            var nutrientBands = new SortedDictionary<int, KeyValuePair<float, float>>();
            foreach (var row in rows)
            {
                waterBands[row.WaterBand] = new KeyValuePair<float, float>(row.WaterMin, row.WaterMax);
                nutrientBands[row.NutrientBand] = new KeyValuePair<float, float>(row.NutrientMin, row.NutrientMax);
            }

            var table = new TerrainTable(rows);
            var missing = new List<string>();
            foreach (var w in waterBands)
            {
                foreach (var n in nutrientBands)
                {
                    // 用该宫格的代表值（区间中点，最后一档用上界）实测命中式。
                    float sampleWater = LastBandInclusive(w.Value.Key, w.Value.Value, waterMax)
                        ? waterMax
                        : (w.Value.Key + w.Value.Value) * 0.5f;
                    float sampleNutrient = LastBandInclusive(n.Value.Key, n.Value.Value, nutrientMax)
                        ? nutrientMax
                        : (n.Value.Key + n.Value.Value) * 0.5f;
                    int hits = 0;
                    TerrainRow hitRow = null;
                    foreach (var row in rows)
                    {
                        if (row.Matches(sampleWater, sampleNutrient))
                        {
                            hits++;
                            hitRow = row;
                        }
                    }
                    if (hits == 0) missing.Add($"水{w.Key}/养分{n.Key}（水={sampleWater} 养分={sampleNutrient}）");
                    else if (hits > 1)
                        throw new DataLoadException(FileName, hitRow.SourceLine, "waterMin",
                            $"区间重叠：水={sampleWater} 养分={sampleNutrient} 同时命中 {hits} 个地形（含 {hitRow.Name}）");
                }
            }

            if (missing.Count > 0)
            {
                throw new DataLoadException(FileName, 0, "waterMin",
                    "25 宫格有洞，以下组合查不到地形：" + string.Join("、", missing));
            }
        }

        static bool LastBandInclusive(float min, float max, float axisMax)
        {
            return Math.Abs(max - axisMax) < 1e-4f;
        }
    }
}
