using System;
using System.Collections.Generic;
using System.Globalization;

namespace Swan.Core
{
    /// <summary>
    /// config.json 读取器。规则：
    /// 1. 每个字段都必须显式出现（缺字段 = 报错，不在代码里兜底）；
    /// 2. 出现未知字段 = 报错（防止拼错字段名后以为调了值）；
    /// 3. 下划线开头的键是注释，忽略；
    /// 4. 报错一定带 config.json 的 行/列 和字段名。
    /// </summary>
    public static class ConfigLoader
    {
        public const string FileName = "config.json";

        public static EngineConfig Load(IFileSource source, DataWarnings warnings = null)
        {
            return LoadText(source.ReadText(FileName), warnings);
        }

        public static EngineConfig LoadText(string text, DataWarnings warnings = null)
        {
            JsonValue root;
            try
            {
                root = Json.Parse(text, FileName);
            }
            catch (JsonFormatException e)
            {
                throw new DataLoadException(FileName, e.Line, null, e.Message);
            }

            if (root.Kind != JsonKind.Object)
                throw new DataLoadException(FileName, root.Line, null, "config.json 的根必须是对象 { ... }");

            var config = new EngineConfig();
            var used = new HashSet<string>(StringComparer.Ordinal);

            float Num(string key)
            {
                var value = Require(root, key, used);
                if (value.Kind != JsonKind.Number)
                    throw Error(value, key, "必须是数字");
                return (float)value.Number;
            }

            int Int(string key)
            {
                var value = Require(root, key, used);
                if (value.Kind != JsonKind.Number)
                    throw Error(value, key, "必须是整数");
                var rounded = Math.Round(value.Number);
                if (Math.Abs(value.Number - rounded) > 1e-9)
                    throw Error(value, key, $"必须是整数，实际是 {value.Number}");
                return (int)rounded;
            }

            bool Bool(string key)
            {
                var value = Require(root, key, used);
                if (value.Kind != JsonKind.Bool)
                    throw Error(value, key, "必须是 true / false");
                return value.Bool;
            }

            string Text(string key)
            {
                var value = Require(root, key, used);
                if (value.Kind != JsonKind.String)
                    throw Error(value, key, "必须是字符串");
                return value.Text;
            }

            float[] Numbers(string key)
            {
                var value = Require(root, key, used);
                if (value.Kind != JsonKind.Array)
                    throw Error(value, key, "必须是数组，例如 [1, 10, 60]");
                var list = new List<float>();
                foreach (var item in value.Items)
                {
                    if (item.Kind != JsonKind.Number)
                        throw Error(item, key, "数组里必须全是数字");
                    list.Add((float)item.Number);
                }
                return list.ToArray();
            }

            config.TickRate = Num("tickRate");
            config.EnvStepSeconds = Num("envStepSeconds");
            config.MaxEnvStepsPerFrame = Int("maxEnvStepsPerFrame");
            config.GridWidth = Int("gridWidth");
            config.GridHeight = Int("gridHeight");
            config.CellPixelSize = Int("cellPixelSize");
            config.ZoomMin = Num("zoomMin");
            config.ZoomMax = Num("zoomMax");
            config.WaterMin = Num("waterMin");
            config.WaterMax = Num("waterMax");
            config.NutrientMin = Num("nutrientMin");
            config.NutrientMax = Num("nutrientMax");
            config.WaterOverflowThreshold = Num("waterOverflowThreshold");
            config.OverflowRatio = Num("overflowRatio");
            config.BaseTemperature = Num("baseTemperature");
            config.TempSpread = Num("tempSpread");
            config.TempReturn = Num("tempReturn");
            config.HeatSourceBoost = Num("heatSourceBoost");
            config.FireTempThreshold = Num("fireTempThreshold");
            config.PlantIntakeBase = Num("plantIntakeBase");
            config.GrowWaterMin = Num("growWaterMin");
            config.GrowNutrientMin = Num("growNutrientMin");
            config.GrowTempMin = Num("growTempMin");
            config.GrowTempMax = Num("growTempMax");
            config.RipeThreshold = Num("ripeThreshold");
            config.TerrainHysteresis = Num("terrainHysteresis");
            config.MapSeed = Int("mapSeed");
            config.MapBaselineWater = Num("mapBaselineWater");
            config.MapBaselineNutrient = Num("mapBaselineNutrient");
            config.MapJitterWater = Num("mapJitterWater");
            config.MapJitterNutrient = Num("mapJitterNutrient");
            config.FallbackTerrainColor = Text("fallbackTerrainColor");
            config.PlantSizeRatio = Num("plantSizeRatio");
            config.PlantMinSizeRatio = Num("plantMinSizeRatio");
            config.StackInitialGrowthTraits = Bool("stackInitialGrowthTraits");
            config.CameraOrthographicSize = Num("cameraOrthographicSize");
            config.CameraPanSpeedCellsPerSecond = Num("cameraPanSpeedCellsPerSecond");
            config.CameraZoomStep = Num("cameraZoomStep");
            config.BackgroundColor = Text("backgroundColor");
            config.HeatOverlayColor = Text("heatOverlayColor");
            config.HeatOverlayStartTemp = Num("heatOverlayStartTemp");
            config.HeatOverlayFullTemp = Num("heatOverlayFullTemp");
            config.MarkerColor = Text("markerColor");
            config.CursorColor = Text("cursorColor");
            config.CursorLineThickness = Int("cursorLineThickness");
            config.CursorPulseHz = Num("cursorPulseHz");
            config.CursorPulseAlphaMin = Num("cursorPulseAlphaMin");
            config.HudFontSize = Int("hudFontSize");
            config.HudMargin = Int("hudMargin");
            config.HudPanelAlpha = Num("hudPanelAlpha");
            config.SpeedLevels = Numbers("speedLevels");
            config.DefaultSpeed = Num("defaultSpeed");

            // 未知字段：防止把 envStepSecond（少个 s）当成调好了
            foreach (var member in root.Members)
            {
                if (member.Key.StartsWith("_")) continue;
                if (used.Contains(member.Key)) continue;
                throw new DataLoadException(FileName, member.Value.Line, member.Key,
                    $"未知字段（config.json 里没有这个可调参数，拼写错了？）");
            }

            ColorHex.ParseRequired(config.FallbackTerrainColor, FileName, root.Member("fallbackTerrainColor")?.Line ?? 0, "fallbackTerrainColor");
            ColorHex.ParseRequired(config.BackgroundColor, FileName, root.Member("backgroundColor")?.Line ?? 0, "backgroundColor");
            ColorHex.ParseRequired(config.HeatOverlayColor, FileName, root.Member("heatOverlayColor")?.Line ?? 0, "heatOverlayColor");
            ColorHex.ParseRequired(config.MarkerColor, FileName, root.Member("markerColor")?.Line ?? 0, "markerColor");
            ColorHex.ParseRequired(config.CursorColor, FileName, root.Member("cursorColor")?.Line ?? 0, "cursorColor");
            Validate(config);
            return config;
        }

        static JsonValue Require(JsonValue root, string key, HashSet<string> used)
        {
            var value = root.Member(key);
            if (value == null)
                throw new DataLoadException(FileName, root.Line, key,
                    $"缺少字段（config.json 必须显式写全，代码里不留默认数值）");
            used.Add(key);
            return value;
        }

        static DataLoadException Error(JsonValue value, string key, string message)
        {
            return new DataLoadException(FileName, value.Line, key, message + $"（实际值 {value}）");
        }

        static void Validate(EngineConfig config)
        {
            void Check(bool condition, string key, string message)
            {
                if (!condition) throw new DataLoadException(FileName, 0, key, message);
            }

            Check(config.TickRate > 0f, "tickRate", "必须大于 0");
            Check(config.EnvStepSeconds > 0f, "envStepSeconds", "必须大于 0");
            Check(config.MaxEnvStepsPerFrame >= 1, "maxEnvStepsPerFrame", "至少是 1");
            Check(config.GridWidth >= 1 && config.GridHeight >= 1, "gridWidth", "网格尺寸至少 1");
            Check(config.CellPixelSize >= 1, "cellPixelSize", "至少 1 像素");
            Check(config.ZoomMin > 0f, "zoomMin", "必须大于 0");
            Check(config.ZoomMax >= config.ZoomMin, "zoomMax", "不能小于 zoomMin");
            Check(config.WaterMin < config.WaterMax, "waterMin", "waterMin 必须小于 waterMax");
            Check(config.NutrientMin < config.NutrientMax, "nutrientMin", "nutrientMin 必须小于 nutrientMax");
            Check(config.WaterOverflowThreshold >= config.WaterMin && config.WaterOverflowThreshold <= config.WaterMax,
                "waterOverflowThreshold", "必须落在水区间内");
            Check(config.OverflowRatio >= 0f && config.OverflowRatio <= 1f, "overflowRatio", "必须在 0..1 之间");
            Check(config.TempSpread >= 0f, "tempSpread", "不能是负数");
            Check(config.TempReturn >= 0f, "tempReturn", "不能是负数");
            Check(config.PlantIntakeBase >= 0f, "plantIntakeBase", "不能是负数");
            Check(config.GrowTempMin < config.GrowTempMax, "growTempMin", "必须小于 growTempMax");
            Check(config.RipeThreshold > 0f, "ripeThreshold", "必须大于 0");
            Check(config.TerrainHysteresis >= 0f && config.TerrainHysteresis <= (config.WaterMax - config.WaterMin),
                "terrainHysteresis", "必须在 0 .. (waterMax-waterMin) 之间");
            Check(config.MapJitterWater >= 0f, "mapJitterWater", "不能是负数");
            Check(config.MapJitterNutrient >= 0f, "mapJitterNutrient", "不能是负数");
            Check(config.PlantSizeRatio > 0f && config.PlantSizeRatio <= 1f, "plantSizeRatio", "必须在 (0, 1] 之间");
            Check(config.PlantMinSizeRatio > 0f && config.PlantMinSizeRatio <= config.PlantSizeRatio,
                "plantMinSizeRatio", "必须在 (0, plantSizeRatio] 之间，否则新苗尺寸算出来是 0 像素或比成株还大");
            Check(config.CameraOrthographicSize > 0f, "cameraOrthographicSize", "必须大于 0");
            Check(config.CameraPanSpeedCellsPerSecond > 0f, "cameraPanSpeedCellsPerSecond", "必须大于 0");
            Check(config.CameraZoomStep > 0f, "cameraZoomStep", "必须大于 0");
            Check(config.CursorLineThickness >= 1 && config.CursorLineThickness < config.CellPixelSize,
                "cursorLineThickness", "必须在 1 到 cellPixelSize-1 之间，否则光标框会糊满整格或完全没有");
            Check(config.CursorPulseHz >= 0f, "cursorPulseHz", "不能是负数（0 = 常亮不呼吸）");
            Check(config.CursorPulseAlphaMin >= 0f && config.CursorPulseAlphaMin <= 1f, "cursorPulseAlphaMin",
                "必须在 0..1 之间（1 = 完全不呼吸，看起来会一直在闪）");
            Check(config.HeatOverlayStartTemp < config.HeatOverlayFullTemp, "heatOverlayStartTemp",
                "必须小于 heatOverlayFullTemp，否则叠加层从第一帧就是全红");
            Check(config.HudFontSize >= 8, "hudFontSize", "至少 8");
            Check(config.HudMargin >= 0, "hudMargin", "不能是负数");
            Check(config.HudPanelAlpha >= 0f && config.HudPanelAlpha <= 1f, "hudPanelAlpha", "必须在 0..1 之间");
            Check(config.SpeedLevels != null && config.SpeedLevels.Length > 0, "speedLevels", "至少要有一个速度档位");
            if (config.SpeedLevels != null)
            {
                foreach (var speed in config.SpeedLevels)
                {
                    Check(speed > 0f, "speedLevels", $"速度档位必须 > 0（实际 {speed}）");
                }
            }
            Check(config.DefaultSpeed > 0f, "defaultSpeed", "必须大于 0");
            bool defaultSpeedListed = false;
            if (config.SpeedLevels != null)
            {
                foreach (var speed in config.SpeedLevels)
                {
                    if (Math.Abs(speed - config.DefaultSpeed) <= 1e-4f) defaultSpeedListed = true;
                }
            }
            Check(defaultSpeedListed, "defaultSpeed", "必须出现在 speedLevels 里");
        }
    }
}
