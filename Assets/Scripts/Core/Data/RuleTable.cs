using System;
using System.Collections.Generic;
using System.Globalization;

namespace Swan.Core.Data
{
    public enum RuleTrigger
    {
        CellWater,
        CellTemp,
        PlantTrait,
        PlantTraitAndTemp,
        StructureRunning,
        TreeCutInWindow,
        MonocultureCells,
        GlobalWaterRatio
    }

    public enum RuleCompare
    {
        Greater,
        Less,
        GreaterOrEqual,
        LessOrEqual,
        Equal,
        Has
    }

    public enum RuleEffect
    {
        OverflowToLowestNeighbor,
        WaterDelta,
        BurnPlant,
        NutrientDelta,
        EvaporationMul,
        TempAndWaterDelta,
        PumpWater,
        SpawnPest
    }

    public static class RuleVocabulary
    {
        static readonly Dictionary<string, RuleTrigger> Triggers = new Dictionary<string, RuleTrigger>(StringComparer.Ordinal)
        {
            { "cellWater", RuleTrigger.CellWater },
            { "cellTemp", RuleTrigger.CellTemp },
            { "plantTrait", RuleTrigger.PlantTrait },
            { "plantTraitAndTemp", RuleTrigger.PlantTraitAndTemp },
            { "structureRunning", RuleTrigger.StructureRunning },
            { "treeCutInWindow", RuleTrigger.TreeCutInWindow },
            { "monocultureCells", RuleTrigger.MonocultureCells },
            { "globalWaterRatio", RuleTrigger.GlobalWaterRatio }
        };

        static readonly Dictionary<string, RuleCompare> Compares = new Dictionary<string, RuleCompare>(StringComparer.Ordinal)
        {
            { ">", RuleCompare.Greater },
            { "<", RuleCompare.Less },
            { ">=", RuleCompare.GreaterOrEqual },
            { "<=", RuleCompare.LessOrEqual },
            { "==", RuleCompare.Equal },
            { "HAS", RuleCompare.Has }
        };

        static readonly Dictionary<string, RuleEffect> Effects = new Dictionary<string, RuleEffect>(StringComparer.Ordinal)
        {
            { "overflowToLowestNeighbor", RuleEffect.OverflowToLowestNeighbor },
            { "waterDelta", RuleEffect.WaterDelta },
            { "burnPlant", RuleEffect.BurnPlant },
            { "nutrientDelta", RuleEffect.NutrientDelta },
            { "evaporationMul", RuleEffect.EvaporationMul },
            { "tempAndWaterDelta", RuleEffect.TempAndWaterDelta },
            { "pumpWater", RuleEffect.PumpWater },
            { "spawnPest", RuleEffect.SpawnPest }
        };

        /// <summary>v0.1 真正实现了的效果。其余只读入、不执行（交接包 §10.3）。</summary>
        public static bool IsImplementedInV01(RuleEffect effect)
        {
            switch (effect)
            {
                case RuleEffect.OverflowToLowestNeighbor:
                case RuleEffect.WaterDelta:
                case RuleEffect.BurnPlant:
                case RuleEffect.NutrientDelta:
                case RuleEffect.EvaporationMul:
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryParseTrigger(string text, out RuleTrigger trigger) => Triggers.TryGetValue(text ?? string.Empty, out trigger);
        public static bool TryParseCompare(string text, out RuleCompare compare) => Compares.TryGetValue(text ?? string.Empty, out compare);
        public static bool TryParseEffect(string text, out RuleEffect effect) => Effects.TryGetValue(text ?? string.Empty, out effect);

        public static string ListTriggers()
        {
            var keys = new List<string>(Triggers.Keys);
            keys.Sort(StringComparer.Ordinal);
            return string.Join(", ", keys);
        }

        public static string ListCompares()
        {
            var keys = new List<string>(Compares.Keys);
            keys.Sort(StringComparer.Ordinal);
            return string.Join(", ", keys);
        }

        public static string ListEffects()
        {
            var keys = new List<string>(Effects.Keys);
            keys.Sort(StringComparer.Ordinal);
            return string.Join(", ", keys);
        }
    }

    public sealed class RuleRow
    {
        public string RuleId;
        public string Category;
        public string Name;
        public RuleTrigger Trigger;
        public string TriggerKeyText;
        public string TriggerArg;
        public RuleCompare Compare;
        public string CompareText;
        public float Threshold;
        public RuleEffect Effect;
        public string EffectKeyText;
        public string EffectValueText;
        public int Radius;
        public bool PerEnvStep;
        public bool ActiveInV01;
        public string Note;
        public int SourceLine;
        public string FileName;

        public bool ExecutedInV01 => ActiveInV01 && RuleVocabulary.IsImplementedInV01(Effect);

        /// <summary>effectValue 可能是 "5/-2" 这种多值。分隔符 / 或 ,。</summary>
        public float[] ParseEffectNumbers()
        {
            var parts = (EffectValueText ?? string.Empty).Split(new[] { '/', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                throw new DataLoadException(FileName, SourceLine, "effectValue", "空值，至少要有一个数");
            var numbers = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                {
                    throw new DataLoadException(FileName, SourceLine, "effectValue",
                        $"「{parts[i]}」不是合法数值（多值用 / 分隔）");
                }
            }
            return numbers;
        }

        public override string ToString()
        {
            return $"{RuleId}:{Name}";
        }
    }

    public sealed class RuleTable
    {
        public const string FileName = "数值表(世界法则表)-机读.csv";

        readonly List<RuleRow> _rows;
        readonly Dictionary<string, RuleRow> _byId;

        public IReadOnlyList<RuleRow> Rows => _rows;

        RuleTable(List<RuleRow> rows)
        {
            _rows = rows;
            _byId = new Dictionary<string, RuleRow>(StringComparer.Ordinal);
            foreach (var row in rows) _byId[row.RuleId] = row;
        }

        public RuleRow Get(string ruleId)
        {
            if (ruleId != null && _byId.TryGetValue(ruleId, out var row)) return row;
            throw new DataLoadException(FileName, 0, "ruleId", $"查不到法则「{ruleId}」");
        }

        public bool TryGet(string ruleId, out RuleRow row)
        {
            row = null;
            return ruleId != null && _byId.TryGetValue(ruleId, out row);
        }

        /// <summary>取第一条满足条件的法则（同一类效果可能有多条，调用方按需自取）。</summary>
        public RuleRow FirstExecuted(RuleEffect effect)
        {
            foreach (var row in _rows)
            {
                if (row.ExecutedInV01 && row.Effect == effect) return row;
            }
            return null;
        }

        public static RuleTable Load(CsvDocument doc, TraitTable traits, DataWarnings warnings)
        {
            var rows = new List<RuleRow>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in doc.Rows)
            {
                var ruleId = row.Require("ruleId");
                if (!seen.Add(ruleId)) throw row.Error("ruleId", $"主键重复：{ruleId}");

                var triggerText = row.Require("triggerKey");
                if (!RuleVocabulary.TryParseTrigger(triggerText, out var trigger))
                    throw row.Error("triggerKey", $"未知的 triggerKey「{triggerText}」。可用：{RuleVocabulary.ListTriggers()}");

                var compareText = row.Require("compare");
                if (!RuleVocabulary.TryParseCompare(compareText, out var compare))
                    throw row.Error("compare", $"未知的比较符「{compareText}」。可用：{RuleVocabulary.ListCompares()}");

                var effectText = row.Require("effectKey");
                if (!RuleVocabulary.TryParseEffect(effectText, out var effect))
                    throw row.Error("effectKey", $"未知的 effectKey「{effectText}」。可用：{RuleVocabulary.ListEffects()}");

                var rule = new RuleRow
                {
                    RuleId = ruleId,
                    Category = row.Raw("category", string.Empty),
                    Name = row.Raw("name", string.Empty),
                    Trigger = trigger,
                    TriggerKeyText = triggerText,
                    TriggerArg = row.Raw("triggerArg", string.Empty),
                    Compare = compare,
                    CompareText = compareText,
                    Threshold = row.Float("threshold"),
                    Effect = effect,
                    EffectKeyText = effectText,
                    EffectValueText = row.Require("effectValue"),
                    Radius = row.Int("radius", 0),
                    PerEnvStep = row.Bool("perEnvStep", true),
                    ActiveInV01 = row.Bool("v01Active"),
                    Note = row.Raw("note", string.Empty),
                    SourceLine = row.Line,
                    FileName = doc.FileName
                };

                // triggerArg 是性状时做外键校验
                if (trigger == RuleTrigger.PlantTrait || trigger == RuleTrigger.PlantTraitAndTemp)
                {
                    if (string.IsNullOrEmpty(rule.TriggerArg))
                        throw row.Error("triggerArg", "植物性状类法则必须填 traitId");
                    if (!traits.Contains(rule.TriggerArg))
                        throw row.Error("triggerArg", $"外键失效：性状表里没有「{rule.TriggerArg}」");
                }

                if (rule.Radius < 0) throw row.Error("radius", "不能是负数");
                rule.ParseEffectNumbers(); // 立刻校验一次，别等运行时才炸

                if (rule.ActiveInV01 && !RuleVocabulary.IsImplementedInV01(effect) && warnings != null)
                {
                    warnings.Add($"{doc.FileName}:{row.Line} 法则 {ruleId} 已启用（v01Active=1），但效果 {effectText} 在 v0.1 没有实现，本版不会执行");
                }

                if (!rule.PerEnvStep && rule.ActiveInV01 && warnings != null)
                {
                    warnings.Add($"{doc.FileName}:{row.Line} 法则 {ruleId} 标了 perEnvStep=0：v0.1 只有环境步，会按环境步结算");
                }

                rows.Add(rule);
            }

            if (rows.Count == 0) throw new DataLoadException(doc.FileName, 1, null, "法则表是空的");
            return new RuleTable(rows);
        }
    }
}
