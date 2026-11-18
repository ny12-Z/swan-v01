using System;
using System.Collections.Generic;

namespace Swan.Core.Data
{
    public sealed class PlantTypeRow
    {
        public string TypeId;
        public string Name;
        public string NameEn;
        public int GrowthDays;
        public int GrowthSteps;
        public float GrowthPerStep;
        public string Size;
        public bool Passable;
        public float BaseYield;
        public string YieldItem;
        public string[] InitTraits;
        public float RequiredWaterMin;
        public float RequiredWaterMax;
        public float RequiredNutrientMin;
        public string ColorHex;
        public bool PlantableInV01;
        public string Note;
        public int SourceLine;
        public string FileName;

        /// <summary>按本表数值，从 0 长到成熟需要多少个环境步（HUD 提示用；不含产量/速率修正）。</summary>
        public float EstimatedStepsToRipe(float ripeThreshold)
        {
            if (GrowthPerStep <= 0f) return float.PositiveInfinity;
            return ripeThreshold / GrowthPerStep;
        }

        /// <summary>能不能在给定水/养分下生长（§8 门槛；温度门槛由 config 提供）。</summary>
        public bool CanGrowAt(float water, float nutrient)
        {
            return water >= RequiredWaterMin && water <= RequiredWaterMax && nutrient >= RequiredNutrientMin;
        }

        public override string ToString()
        {
            return $"{TypeId}:{Name}";
        }
    }

    /// <summary>植物类型表。v01Active=1 才允许种植/生成。</summary>
    public sealed class PlantTypeTable
    {
        public const string FileName = "数值表(植物类型表)-机读.csv";
        public const char TraitSeparator = '|';

        readonly List<PlantTypeRow> _rows;
        readonly Dictionary<string, PlantTypeRow> _byId;
        readonly Dictionary<string, int> _indexById;

        public IReadOnlyList<PlantTypeRow> Rows => _rows;

        PlantTypeTable(List<PlantTypeRow> rows)
        {
            _rows = rows;
            _byId = new Dictionary<string, PlantTypeRow>(StringComparer.Ordinal);
            _indexById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                _byId[rows[i].TypeId] = rows[i];
                // 格子里的 PlantTypeId 用「1 起的下标」，0 留给「没有植物」
                _indexById[rows[i].TypeId] = i + 1;
            }
        }

        /// <summary>typeId → 格子里存的整数 id（1 起；0 = 没有植物）。查不到直接报错。</summary>
        public int IndexOf(string typeId)
        {
            if (typeId != null && _indexById.TryGetValue(typeId, out var index)) return index;
            throw new DataLoadException(FileName, 0, "typeId", $"查不到植物类型「{typeId}」");
        }

        public bool TryGetRow(int plantTypeId, out PlantTypeRow row)
        {
            row = null;
            int index = plantTypeId - 1;
            if (index < 0 || index >= _rows.Count) return false;
            row = _rows[index];
            return true;
        }

        public PlantTypeRow RowByIndex(int plantTypeId)
        {
            if (TryGetRow(plantTypeId, out var row)) return row;
            throw new DataLoadException(FileName, 0, "typeId", $"格子里的植物 id {plantTypeId} 在新表里不存在");
        }

        public PlantTypeRow Get(string typeId)
        {
            if (typeId != null && _byId.TryGetValue(typeId, out var row)) return row;
            throw new DataLoadException(FileName, 0, "typeId", $"查不到植物类型「{typeId}」");
        }

        public bool TryGet(string typeId, out PlantTypeRow row)
        {
            row = null;
            return typeId != null && _byId.TryGetValue(typeId, out row);
        }

        public static PlantTypeTable Load(CsvDocument doc, TraitTable traits, DataWarnings warnings)
        {
            var rows = new List<PlantTypeRow>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in doc.Rows)
            {
                var typeId = row.Require("typeId");
                if (!seen.Add(typeId)) throw row.Error("typeId", $"主键重复：{typeId}");

                var initTraits = row.Split("initTraits", new[] { TraitSeparator, ';' });
                foreach (var traitId in initTraits)
                {
                    // 外键校验：性状表里查不到就报错，不当成无效字符吞掉
                    if (!traits.Contains(traitId))
                    {
                        throw row.Error("initTraits",
                            $"外键失效：性状表里没有「{traitId}」（同列分隔符用 '{TraitSeparator}'）");
                    }
                }

                var plant = new PlantTypeRow
                {
                    TypeId = typeId,
                    Name = row.Require("name"),
                    NameEn = row.Raw("nameEn", string.Empty),
                    GrowthDays = row.Int("growthDays"),
                    GrowthSteps = row.Int("growthSteps"),
                    GrowthPerStep = row.Float("growthPerStep"),
                    Size = row.Raw("size", string.Empty),
                    Passable = row.Bool("passable"),
                    BaseYield = row.Float("baseYield"),
                    YieldItem = row.Raw("yieldItem", string.Empty),
                    InitTraits = initTraits,
                    RequiredWaterMin = row.Float("requiredWaterMin"),
                    RequiredWaterMax = row.Float("requiredWaterMax"),
                    RequiredNutrientMin = row.Float("requiredNutrientMin"),
                    ColorHex = row.Require("v01Color"),
                    PlantableInV01 = row.Bool("v01Active"),
                    Note = row.Raw("note", string.Empty),
                    SourceLine = row.Line,
                    FileName = doc.FileName
                };

                if (plant.GrowthPerStep < 0f) throw row.Error("growthPerStep", "不能是负数");
                if (plant.BaseYield < 0f) throw row.Error("baseYield", "不能是负数");
                if (plant.RequiredWaterMin > plant.RequiredWaterMax)
                    throw row.Error("requiredWaterMin", "必须小于 requiredWaterMax");
                ColorHex.ParseRequired(plant.ColorHex, doc.FileName, row.Line, "v01Color");

                if (!plant.PlantableInV01 && warnings != null)
                {
                    warnings.Add($"{doc.FileName}:{row.Line} 植物 {typeId} 在 v0.1 不开放种植（v01Active=0），仅作为数据存在");
                }

                rows.Add(plant);
            }

            if (rows.Count == 0) throw new DataLoadException(doc.FileName, 1, null, "植物类型表是空的");
            return new PlantTypeTable(rows);
        }
    }
}
