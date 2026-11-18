using System;
using System.Collections.Generic;

namespace Swan.Core.Data
{
    /// <summary>性状效果键。未知键 = 启动报错（交接包 §10.3）。</summary>
    public enum TraitEffectKey
    {
        YieldAdd,
        GrowthRateMul,
        PlantIntakeMul,
        SoilNutrientPerStep,
        IntakeRadius,
        EvaporationMul,
        TempAdd,
        PestGuard,
        AutoIgniteTemp,
        MutationWeightMul
    }

    public static class TraitEffectKeys
    {
        static readonly Dictionary<string, TraitEffectKey> Map = new Dictionary<string, TraitEffectKey>(StringComparer.Ordinal)
        {
            { "yieldAdd", TraitEffectKey.YieldAdd },
            { "growthRateMul", TraitEffectKey.GrowthRateMul },
            { "plantIntakeMul", TraitEffectKey.PlantIntakeMul },
            { "soilNutrientPerStep", TraitEffectKey.SoilNutrientPerStep },
            { "intakeRadius", TraitEffectKey.IntakeRadius },
            { "evaporationMul", TraitEffectKey.EvaporationMul },
            { "tempAdd", TraitEffectKey.TempAdd },
            { "pestGuard", TraitEffectKey.PestGuard },
            { "autoIgniteTemp", TraitEffectKey.AutoIgniteTemp },
            { "mutationWeightMul", TraitEffectKey.MutationWeightMul }
        };

        public static bool TryParse(string key, out TraitEffectKey effect)
        {
            return Map.TryGetValue(key ?? string.Empty, out effect);
        }

        public static string AllowedList()
        {
            var keys = new List<string>(Map.Keys);
            keys.Sort(StringComparer.Ordinal);
            return string.Join(", ", keys);
        }
    }

    public sealed class TraitRow
    {
        public string TraitId;
        public string TraitName;
        public string Category;
        public TraitEffectKey Effect;
        public string EffectKeyText;
        public float Value;
        public int Radius;
        public string Unit;
        public bool ActiveInV01;
        public string Note;
        public int SourceLine;
        public string FileName;

        public override string ToString()
        {
            return $"{TraitId}.{EffectKeyText}={Value}(r{Radius})";
        }
    }

    /// <summary>
    /// 性状表。同一个 traitId 可以有多个不同 effectKey 的行（表里 G04 就是两行），
    /// 所以内部是 id → 多行。查同 id 必须全部生效，不能只取第一行。
    /// </summary>
    public sealed class TraitTable
    {
        public const string FileName = "数值表(植物性状表)-机读.csv";

        readonly List<TraitRow> _rows;
        readonly Dictionary<string, List<TraitRow>> _byId;

        public IReadOnlyList<TraitRow> Rows => _rows;

        TraitTable(List<TraitRow> rows)
        {
            _rows = rows;
            _byId = new Dictionary<string, List<TraitRow>>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (!_byId.TryGetValue(row.TraitId, out var list))
                {
                    list = new List<TraitRow>();
                    _byId[row.TraitId] = list;
                }
                list.Add(row);
            }
        }

        public bool Contains(string traitId) => traitId != null && _byId.ContainsKey(traitId);

        public IReadOnlyList<TraitRow> Get(string traitId)
        {
            if (traitId != null && _byId.TryGetValue(traitId, out var list)) return list;
            throw new DataLoadException(FileName, 0, "traitId", $"查不到性状「{traitId}」");
        }

        public static TraitTable Load(CsvDocument doc, DataWarnings warnings)
        {
            var rows = new List<TraitRow>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in doc.Rows)
            {
                var traitId = row.Require("traitId");
                var effectText = row.Require("effectKey");
                if (!TraitEffectKeys.TryParse(effectText, out var effect))
                {
                    throw row.Error("effectKey",
                        $"未知的 effectKey「{effectText}」。v0.1 只认这些：{TraitEffectKeys.AllowedList()}");
                }

                var radius = row.Int("radius", 0);
                if (radius < 0) throw row.Error("radius", "不能是负数");

                var trait = new TraitRow
                {
                    TraitId = traitId,
                    TraitName = row.Raw("traitName", string.Empty),
                    Category = row.Raw("category", string.Empty),
                    Effect = effect,
                    EffectKeyText = effectText,
                    Value = row.Float("value"),
                    Radius = radius,
                    Unit = row.Raw("unit", string.Empty),
                    ActiveInV01 = row.Bool("v01Active"),
                    Note = row.Raw("note", string.Empty),
                    SourceLine = row.Line,
                    FileName = doc.FileName
                };

                // 同一性状 + 同一 effectKey 重复，几乎一定是复制粘贴写重了
                var key = trait.TraitId + "|" + trait.EffectKeyText;
                if (!seen.Add(key))
                {
                    throw row.Error("traitId",
                        $"重复行：性状 {trait.TraitId} 已经有 {trait.EffectKeyText} 这一条了（同 id 允许多条，但 effectKey 不能重复）");
                }

                if (!trait.ActiveInV01 && trait.Radius > 0 && warnings != null)
                {
                    warnings.Add($"{doc.FileName}:{row.Line} 性状 {trait.TraitId} 在 v0.1 未启用（v01Active=0），半径 {radius} 的效果不会执行");
                }

                rows.Add(trait);
            }

            if (rows.Count == 0) throw new DataLoadException(doc.FileName, 1, null, "性状表是空的");
            return new TraitTable(rows);
        }
    }
}
