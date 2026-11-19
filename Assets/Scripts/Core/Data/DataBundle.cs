using System;
using System.Collections.Generic;

namespace Swan.Core.Data
{
    /// <summary>
    /// 一次加载的全部数据：config + 4 张机读表 + 地图预设，外加交叉校验。
    /// 热重载 = 丢掉旧的 DataBundle、整个换新的；不做原地修补。
    /// </summary>
    public sealed class DataBundle
    {
        public EngineConfig Config;
        public TerrainTable Terrain;
        public TraitTable Traits;
        public PlantTypeTable Plants;
        public StructureTable Structures;
        public ItemTable Items;
        public ForageTable Forage;
        public ActionTable Actions;
        public TutorialTable Tutorial;
        public RuleTable Rules;
        public MapPreset Preset;

        readonly List<string> _warnings = new List<string>();
        public IReadOnlyList<string> Warnings => _warnings;

        /// <summary>加载顺序固定为 地形 → 性状 → 植物类型 → 法则（后面的表要引用前面的表）。</summary>
        public static DataBundle Load(IFileSource source)
        {
            var warnings = new DataWarnings();
            var bundle = new DataBundle();

            bundle.Config = ConfigLoader.Load(source, warnings);

            var terrainDoc = CsvDocument.Parse(source.ReadText(TerrainTable.FileName), TerrainTable.FileName, warnings);
            bundle.Terrain = TerrainTable.Load(terrainDoc, bundle.Config.WaterMin, bundle.Config.WaterMax,
                bundle.Config.NutrientMin, bundle.Config.NutrientMax, warnings);

            var traitDoc = CsvDocument.Parse(source.ReadText(TraitTable.FileName), TraitTable.FileName, warnings);
            bundle.Traits = TraitTable.Load(traitDoc, warnings);

            var plantDoc = CsvDocument.Parse(source.ReadText(PlantTypeTable.FileName), PlantTypeTable.FileName, warnings);
            bundle.Plants = PlantTypeTable.Load(plantDoc, bundle.Traits, warnings);

            var structureDoc = CsvDocument.Parse(source.ReadText(StructureTable.FileName), StructureTable.FileName, warnings);
            bundle.Structures = StructureTable.Load(structureDoc, warnings);

            var itemDoc = CsvDocument.Parse(source.ReadText(ItemTable.FileName), ItemTable.FileName, warnings);
            bundle.Items = ItemTable.Load(itemDoc, bundle.Structures, bundle.Plants, warnings);
            bundle.Structures.ValidateItems(bundle.Items);

            var forageDoc = CsvDocument.Parse(source.ReadText(ForageTable.FileName), ForageTable.FileName, warnings);
            bundle.Forage = ForageTable.Load(forageDoc, bundle.Terrain, bundle.Items, warnings);

            var actionDoc = CsvDocument.Parse(source.ReadText(ActionTable.FileName), ActionTable.FileName, warnings);
            bundle.Actions = ActionTable.Load(actionDoc, bundle.Items, warnings);

            var tutorialDoc = CsvDocument.Parse(source.ReadText(TutorialTable.FileName), TutorialTable.FileName, warnings);
            bundle.Tutorial = TutorialTable.Load(tutorialDoc, warnings);

            var ruleDoc = CsvDocument.Parse(source.ReadText(RuleTable.FileName), RuleTable.FileName, warnings);
            bundle.Rules = RuleTable.Load(ruleDoc, bundle.Traits, warnings);

            bundle.Preset = MapPreset.Load(source, bundle.Config, bundle.Plants, warnings);

            bundle.ValidateCrossReferences();
            bundle._warnings.AddRange(warnings.Items);
            return bundle;
        }

        /// <summary>
        /// 交叉校验：同一件事在两个地方都能调，就必须证明它们说的是同一句话。
        /// 见 DECISIONS D3：config.waterOverflowThreshold/overflowRatio 与 L01、fireTempThreshold 与 L03/G10。
        /// </summary>
        void ValidateCrossReferences()
        {
            var flow = RequireRule("L01", "水溢出");
            CompareWithConfig("L01", "threshold", flow.Threshold, "waterOverflowThreshold", Config.WaterOverflowThreshold, flow);
            var flowNumbers = flow.ParseEffectNumbers();
            CompareWithConfig("L01", "effectValue", flowNumbers[0], "overflowRatio", Config.OverflowRatio, flow);

            var burn = RequireRule("L03", "燃烧");
            CompareWithConfig("L03", "threshold", burn.Threshold, "fireTempThreshold", Config.FireTempThreshold, burn);

            if (Traits.Contains("G10"))
            {
                foreach (var row in Traits.Get("G10"))
                {
                    if (row.Effect != TraitEffectKey.AutoIgniteTemp) continue;
                    if (Math.Abs(row.Value - Config.FireTempThreshold) > 1e-4f)
                    {
                        throw new DataLoadException(TraitTable.FileName, row.SourceLine, "value",
                            $"性状 G10 的自燃温度是 {row.Value}，但 config.fireTempThreshold={Config.FireTempThreshold}、L03.threshold={burn.Threshold}。" +
                            "同一件事只能有一个数：改表就要一起改，或者把三处统一。");
                    }
                }
            }
        }

        RuleRow RequireRule(string ruleId, string what)
        {
            if (!Rules.TryGet(ruleId, out var rule))
            {
                throw new DataLoadException(RuleTable.FileName, 0, "ruleId",
                    $"缺少 {what}法则 {ruleId}（v0.1 的水溢出/燃烧依赖这一行，不能删）");
            }
            return rule;
        }

        void CompareWithConfig(string ruleId, string ruleColumn, float ruleValue, string configKey, float configValue, RuleRow rule)
        {
            if (Math.Abs(ruleValue - configValue) <= 1e-4f) return;
            throw new DataLoadException(RuleTable.FileName, rule.SourceLine, ruleColumn,
                $"同一个参数有两个来源：法则 {ruleId}.{ruleColumn}={ruleValue} 与 config.{configKey}={configValue}。\n" +
                "请让它们相等再启动（改表要同时改 config，反之亦然）。");
        }
    }
}
