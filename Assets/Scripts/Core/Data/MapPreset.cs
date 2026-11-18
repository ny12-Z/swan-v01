using System;
using System.Collections.Generic;

namespace Swan.Core.Data
{
    public sealed class MapRegion
    {
        public string Label;
        public int X;
        public int Y;
        public int Radius;
        public float Water;
        public float Nutrient;
    }

    public sealed class MapHeatSource
    {
        public int X;
        public int Y;
    }

    public sealed class MapPlant
    {
        public int X;
        public int Y;
        public string TypeId;
    }

    /// <summary>
    /// 初始地图预设（区域化地画水/养分、热源、初始植物）。
    /// 地图是数据不是代码：改这里就能换开局地形，不用改 C#。
    /// </summary>
    public sealed class MapPreset
    {
        public const string FileName = "map-preset.json";

        public readonly List<MapRegion> Regions = new List<MapRegion>();
        public readonly List<MapHeatSource> HeatSources = new List<MapHeatSource>();
        public readonly List<MapPlant> Plants = new List<MapPlant>();

        public static MapPreset Load(IFileSource source, EngineConfig config, PlantTypeTable plants, DataWarnings warnings)
        {
            var preset = new MapPreset();
            if (!source.Exists(FileName))
            {
                warnings?.Add($"{FileName}: 没有这个文件，地图将只用 config 的基准值生成");
                return preset;
            }

            JsonValue root;
            try
            {
                root = Json.Parse(source.ReadText(FileName), FileName);
            }
            catch (JsonFormatException e)
            {
                throw new DataLoadException(FileName, e.Line, null, e.Message);
            }

            if (root.Kind != JsonKind.Object)
                throw new DataLoadException(FileName, root.Line, null, "根必须是对象 { ... }");

            foreach (var member in root.Members)
            {
                if (member.Key.StartsWith("_")) continue;
                if (member.Key != "regions" && member.Key != "heatSources" && member.Key != "plants")
                    throw new DataLoadException(FileName, member.Value.Line, member.Key, "未知字段（只认 regions / heatSources / plants）");
            }

            void RequireRange(float value, string column, string what, JsonValue node)
            {
                float lo, hi;
                switch (what)
                {
                    case "water":
                        lo = config.WaterMin;
                        hi = config.WaterMax;
                        break;
                    case "nutrient":
                        lo = config.NutrientMin;
                        hi = config.NutrientMax;
                        break;
                    default:
                        return;
                }
                if (value < lo || value > hi)
                {
                    throw new DataLoadException(FileName, node.Line, column,
                        $"{column}={value} 超出允许范围 [{lo}, {hi}]");
                }
            }

            int Index(JsonValue node, string key, int max, bool allowMax)
            {
                var value = node.Member(key);
                if (value == null) throw new DataLoadException(FileName, node.Line, key, "缺少字段");
                if (value.Kind != JsonKind.Number) throw new DataLoadException(FileName, value.Line, key, "必须是整数");
                var rounded = Math.Round(value.Number);
                if (Math.Abs(value.Number - rounded) > 1e-9)
                    throw new DataLoadException(FileName, value.Line, key, $"必须是整数，实际是 {value.Number}");
                int result = (int)rounded;
                if (result < 0 || (allowMax ? result > max : result >= max))
                {
                    throw new DataLoadException(FileName, value.Line, key,
                        $"越界：{result}，允许 0..{(allowMax ? max : max - 1)}");
                }
                return result;
            }

            float Float(JsonValue node, string key)
            {
                var value = node.Member(key);
                if (value == null) throw new DataLoadException(FileName, node.Line, key, "缺少字段");
                if (value.Kind != JsonKind.Number) throw new DataLoadException(FileName, value.Line, key, "必须是数字");
                return (float)value.Number;
            }

            string Text(JsonValue node, string key)
            {
                var value = node.Member(key);
                if (value == null) throw new DataLoadException(FileName, node.Line, key, "缺少字段");
                if (value.Kind != JsonKind.String) throw new DataLoadException(FileName, value.Line, key, "必须是字符串");
                return value.Text;
            }

            var regions = root.Member("regions");
            if (regions != null && !regions.IsNull)
            {
                if (regions.Kind != JsonKind.Array) throw new DataLoadException(FileName, regions.Line, "regions", "必须是数组");
                foreach (var node in regions.Items)
                {
                    if (node.Kind != JsonKind.Object) throw new DataLoadException(FileName, node.Line, "regions", "数组元素必须是对象");
                    var region = new MapRegion
                    {
                        Label = node.Member("label") != null ? Text(node, "label") : string.Empty,
                        X = Index(node, "x", config.GridWidth, false),
                        Y = Index(node, "y", config.GridHeight, false),
                        Radius = Index(node, "radius", Math.Max(config.GridWidth, config.GridHeight), true),
                        Water = Float(node, "water"),
                        Nutrient = Float(node, "nutrient")
                    };
                    RequireRange(region.Water, "water", "water", node);
                    RequireRange(region.Nutrient, "nutrient", "nutrient", node);
                    preset.Regions.Add(region);
                }
            }

            var heatSources = root.Member("heatSources");
            if (heatSources != null && !heatSources.IsNull)
            {
                if (heatSources.Kind != JsonKind.Array) throw new DataLoadException(FileName, heatSources.Line, "heatSources", "必须是数组");
                foreach (var node in heatSources.Items)
                {
                    if (node.Kind != JsonKind.Object) throw new DataLoadException(FileName, node.Line, "heatSources", "数组元素必须是对象");
                    preset.HeatSources.Add(new MapHeatSource
                    {
                        X = Index(node, "x", config.GridWidth, false),
                        Y = Index(node, "y", config.GridHeight, false)
                    });
                }
            }

            var presetPlants = root.Member("plants");
            if (presetPlants != null && !presetPlants.IsNull)
            {
                if (presetPlants.Kind != JsonKind.Array) throw new DataLoadException(FileName, presetPlants.Line, "plants", "必须是数组");
                foreach (var node in presetPlants.Items)
                {
                    if (node.Kind != JsonKind.Object) throw new DataLoadException(FileName, node.Line, "plants", "数组元素必须是对象");
                    var typeId = Text(node, "typeId");
                    if (!plants.TryGet(typeId, out var plantType))
                        throw new DataLoadException(FileName, node.Line, "typeId", $"外键失效：植物类型表里没有「{typeId}」");
                    if (!plantType.PlantableInV01)
                        throw new DataLoadException(FileName, node.Line, "typeId",
                            $"植物 {typeId} 在 v0.1 不开放（v01Active=0），不能放进初始地图");
                    preset.Plants.Add(new MapPlant
                    {
                        X = Index(node, "x", config.GridWidth, false),
                        Y = Index(node, "y", config.GridHeight, false),
                        TypeId = typeId
                    });
                }
            }

            if (warnings != null && preset.Regions.Count == 0 && preset.HeatSources.Count == 0 && preset.Plants.Count == 0)
            {
                warnings.Add($"{FileName}: 三个数组都是空的，地图将只用 config 的基准值生成");
            }

            return preset;
        }
    }
}
