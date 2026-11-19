using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Swan.Core
{
    /// <summary>
    /// 引擎/玩法可调参数。全部来自 config.json，代码里不允许再写第二份业务数值。
    /// 字段没有默认值：config.json 缺字段就报错，避免「代码偷偷兜底」。
    /// </summary>
    public sealed class EngineConfig
    {
        // —— 时钟（§9）——
        public float TickRate;                 // 视觉 tick / 秒
        public float EnvStepSeconds;           // 1 环境步 = 多少秒
        public int MaxEnvStepsPerFrame;        // 单帧最多补几个环境步（防卡顿雪崩）

        // —— 网格与相机（引擎参数，§9 未列，见 DECISIONS D2）——
        public int GridWidth;
        public int GridHeight;
        public int CellPixelSize;
        public float ZoomMin;
        public float ZoomMax;

        // —— 数值边界 ——
        public float WaterMin;
        public float WaterMax;
        public float NutrientMin;
        public float NutrientMax;

        // —— 水/溢出 ——
        public float WaterOverflowThreshold;
        public float OverflowRatio;

        // —— 温度 ——
        public float BaseTemperature;
        public float TempSpread;
        public float TempReturn;
        public float HeatSourceBoost;
        public float FireTempThreshold;

        // —— 植物 ——
        public float PlantIntakeBase;
        public float GrowWaterMin;
        public float GrowNutrientMin;
        public float GrowTempMin;
        public float GrowTempMax;
        public float RipeThreshold;
        /// <summary>全局生长倍率（1 = 按表里的生长曲线；调大用于快速试玩）。</summary>
        public float GrowthSpeedMultiplier;

        // —— 地形重判 ——
        public float TerrainHysteresis;

        // —— 初始地图 ——
        public int MapSeed;
        public float MapBaselineWater;
        public float MapBaselineNutrient;
        public float MapJitterWater;
        public float MapJitterNutrient;
        public string FallbackTerrainColor;
        public float PlantSizeRatio;
        /// <summary>植物刚种下时占格子的比例（生长比例越小越接近这个下限，否则新苗在像素级看不见）。</summary>
        public float PlantMinSizeRatio;

        // —— 规则解释开关（见 DECISIONS D1）——
        public bool StackInitialGrowthTraits;

        // —— 玩家与交互 ——
        public float PlayerMoveCellsPerSecond;
        public float PlayerCollisionRadius;
        public float PlayerSpriteScale;
        /// <summary>行走动画每秒播放的换帧次数（纯表现参数，进 config 是为了方便调手感）。</summary>
        public float PlayerAnimationFps;
        /// <summary>主角出生格；-1 = 自动从地图中心找一块能站的地。</summary>
        public int PlayerStartX;
        public int PlayerStartY;
        /// <summary>主角身边可点击的地块范围（默认 12×12）。</summary>
        public int InteractionWidth;
        public int InteractionHeight;
        /// <summary>水深超过这个值就过不去（也决定哪些格能站人）。</summary>
        public float WalkableMaxWater;
        /// <summary>水超过这个值就挖不动（挖泥会先排水）。</summary>
        public float DigMaxWater;
        /// <summary>收获时返还的种子数量；0 = 不返还。</summary>
        public int SeedReturnOnHarvest;
        public int ForageSeed;
        public float ForageRegrowChance;
        public int ForageMaxCount;

        // —— 岩石：按概率铺在地表，做成「挡路结构」，采集可搬走 ——
        public float RockChance;
        public int RockStructureId;
        public int RockSeed;

        // —— 表现层参数（相机/HUD/温度叠加层）。也是「数值」，所以一样进 config ——
        public float CameraOrthographicSize;
        public float CameraPanSpeedCellsPerSecond;
        public float CameraZoomStep;
        public string BackgroundColor;
        public string HeatOverlayColor;
        public float HeatOverlayStartTemp;
        public float HeatOverlayFullTemp;
        public string MarkerColor;
        /// <summary>「你现在控制的格」高亮框的颜色（玩家位置提示）。</summary>
        public string CursorColor;
        /// <summary>高亮框边宽，单位是格内像素（1 格 = cellPixelSize 像素）。</summary>
        public int CursorLineThickness;
        /// <summary>高亮框的呼吸频率（Hz）；0 = 常亮不呼吸。</summary>
        public float CursorPulseHz;
        /// <summary>呼吸到最暗时的不透明度（1 = 不呼吸）。</summary>
        public float CursorPulseAlphaMin;
        public int HudFontSize;
        public int HudMargin;
        public float HudPanelAlpha;
        /// <summary>12×12 交互区边框颜色。</summary>
        public string ZoneColor;
        public string ZoneOutOfRangeColor;
        public float ZoneFillAlpha;
        public float ZoneBorderAlpha;
        public int EnvStepsPerDay;
        public float ToastSeconds;
        public string UiPanelColor;
        public string UiButtonColor;
        public string UiTextColor;
        public string UiMutedColor;
        public string UiAccentColor;
        public string UiDangerColor;
        public float[] SpeedLevels;
        public float DefaultSpeed;

        public EngineConfig Clone()
        {
            var clone = (EngineConfig)MemberwiseClone();
            // 数组是引用类型，clone 必须另存一份，否则热重载会改到旧 config
            clone.SpeedLevels = SpeedLevels == null ? null : (float[])SpeedLevels.Clone();
            return clone;
        }

        /// <summary>把当前值序列化成 config.json 格式（保存/自检用）。</summary>
        public string ToJson()
        {
            var lines = new List<string>();
            void Raw(string key, string literal) => lines.Add("  \"" + key + "\": " + literal);
            void Num(string key, float value) => Raw(key, value.ToString("0.####", CultureInfo.InvariantCulture));
            void Int(string key, int value) => Raw(key, value.ToString(CultureInfo.InvariantCulture));
            void Bool(string key, bool value) => Raw(key, value ? "true" : "false");

            Num("tickRate", TickRate);
            Num("envStepSeconds", EnvStepSeconds);
            Int("maxEnvStepsPerFrame", MaxEnvStepsPerFrame);
            Int("gridWidth", GridWidth);
            Int("gridHeight", GridHeight);
            Int("cellPixelSize", CellPixelSize);
            Num("zoomMin", ZoomMin);
            Num("zoomMax", ZoomMax);
            Num("waterMin", WaterMin);
            Num("waterMax", WaterMax);
            Num("nutrientMin", NutrientMin);
            Num("nutrientMax", NutrientMax);
            Num("waterOverflowThreshold", WaterOverflowThreshold);
            Num("overflowRatio", OverflowRatio);
            Num("baseTemperature", BaseTemperature);
            Num("tempSpread", TempSpread);
            Num("tempReturn", TempReturn);
            Num("heatSourceBoost", HeatSourceBoost);
            Num("fireTempThreshold", FireTempThreshold);
            Num("plantIntakeBase", PlantIntakeBase);
            Num("growWaterMin", GrowWaterMin);
            Num("growNutrientMin", GrowNutrientMin);
            Num("growTempMin", GrowTempMin);
            Num("growTempMax", GrowTempMax);
            Num("ripeThreshold", RipeThreshold);
            Num("growthSpeedMultiplier", GrowthSpeedMultiplier);
            Num("terrainHysteresis", TerrainHysteresis);
            Int("mapSeed", MapSeed);
            Num("mapBaselineWater", MapBaselineWater);
            Num("mapBaselineNutrient", MapBaselineNutrient);
            Num("mapJitterWater", MapJitterWater);
            Num("mapJitterNutrient", MapJitterNutrient);
            Raw("fallbackTerrainColor", "\"" + FallbackTerrainColor + "\"");
            Num("plantSizeRatio", PlantSizeRatio);
            Num("plantMinSizeRatio", PlantMinSizeRatio);
            Bool("stackInitialGrowthTraits", StackInitialGrowthTraits);
            Num("playerMoveCellsPerSecond", PlayerMoveCellsPerSecond);
            Num("playerCollisionRadius", PlayerCollisionRadius);
            Num("playerSpriteScale", PlayerSpriteScale);
            Num("playerAnimationFps", PlayerAnimationFps);
            Int("playerStartX", PlayerStartX);
            Int("playerStartY", PlayerStartY);
            Int("interactionWidth", InteractionWidth);
            Int("interactionHeight", InteractionHeight);
            Num("walkableMaxWater", WalkableMaxWater);
            Num("digMaxWater", DigMaxWater);
            Int("seedReturnOnHarvest", SeedReturnOnHarvest);
            Int("forageSeed", ForageSeed);
            Num("forageRegrowChance", ForageRegrowChance);
            Int("forageMaxCount", ForageMaxCount);
            Num("rockChance", RockChance);
            Int("rockStructureId", RockStructureId);
            Int("rockSeed", RockSeed);
            Num("cameraOrthographicSize", CameraOrthographicSize);
            Num("cameraPanSpeedCellsPerSecond", CameraPanSpeedCellsPerSecond);
            Num("cameraZoomStep", CameraZoomStep);
            Raw("backgroundColor", "\"" + BackgroundColor + "\"");
            Raw("heatOverlayColor", "\"" + HeatOverlayColor + "\"");
            Num("heatOverlayStartTemp", HeatOverlayStartTemp);
            Num("heatOverlayFullTemp", HeatOverlayFullTemp);
            Raw("markerColor", "\"" + MarkerColor + "\"");
            Raw("cursorColor", "\"" + CursorColor + "\"");
            Int("cursorLineThickness", CursorLineThickness);
            Num("cursorPulseHz", CursorPulseHz);
            Num("cursorPulseAlphaMin", CursorPulseAlphaMin);
            Int("hudFontSize", HudFontSize);
            Int("hudMargin", HudMargin);
            Num("hudPanelAlpha", HudPanelAlpha);
            Raw("zoneColor", "\"" + ZoneColor + "\"");
            Raw("zoneOutOfRangeColor", "\"" + ZoneOutOfRangeColor + "\"");
            Num("zoneFillAlpha", ZoneFillAlpha);
            Num("zoneBorderAlpha", ZoneBorderAlpha);
            Int("envStepsPerDay", EnvStepsPerDay);
            Num("toastSeconds", ToastSeconds);
            Raw("uiPanelColor", "\"" + UiPanelColor + "\"");
            Raw("uiButtonColor", "\"" + UiButtonColor + "\"");
            Raw("uiTextColor", "\"" + UiTextColor + "\"");
            Raw("uiMutedColor", "\"" + UiMutedColor + "\"");
            Raw("uiAccentColor", "\"" + UiAccentColor + "\"");
            Raw("uiDangerColor", "\"" + UiDangerColor + "\"");
            var speeds = new List<string>();
            if (SpeedLevels != null)
            {
                foreach (var speed in SpeedLevels) speeds.Add(speed.ToString("0.####", CultureInfo.InvariantCulture));
            }
            Raw("speedLevels", "[" + string.Join(", ", speeds) + "]");
            Num("defaultSpeed", DefaultSpeed);
            return "{\n" + string.Join(",\n", lines) + "\n}\n";
        }
    }

    /// <summary>#RRGGBB → 0..1 的 RGB。Core 层不引用 UnityEngine，所以自己解析。</summary>
    public static class ColorHex
    {
        public static bool TryParse(string hex, out float r, out float g, out float b)
        {
            r = g = b = 0f;
            if (string.IsNullOrEmpty(hex)) return false;
            var text = hex.Trim();
            if (text.StartsWith("#")) text = text.Substring(1);
            if (text.Length != 6) return false;
            if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return false;
            r = ((value >> 16) & 0xFF) / 255f;
            g = ((value >> 8) & 0xFF) / 255f;
            b = (value & 0xFF) / 255f;
            return true;
        }

        public static void ParseRequired(string hex, string fileName, int line, string column)
        {
            if (!TryParse(hex, out _, out _, out _))
            {
                throw new DataLoadException(fileName, line, column, $"「{hex ?? "<空>"}」不是 #RRGGBB 颜色");
            }
        }
    }

    /// <summary>文件读取入口。Core 不依赖引擎，测试可以塞内存实现。</summary>
    public interface IFileSource
    {
        bool Exists(string fileName);
        string ReadText(string fileName);
        string DisplayPath(string fileName);
    }

    public sealed class DirectoryFileSource : IFileSource
    {
        readonly string _directory;

        public DirectoryFileSource(string directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        public string Directory => _directory;

        public bool Exists(string fileName)
        {
            return System.IO.File.Exists(System.IO.Path.Combine(_directory, fileName));
        }

        public string ReadText(string fileName)
        {
            var path = System.IO.Path.Combine(_directory, fileName);
            if (!System.IO.File.Exists(path))
                throw new DataLoadException(fileName, 0, null, $"文件不存在（目录: {_directory}）");
            return System.IO.File.ReadAllText(path, Encoding.UTF8);
        }

        public string DisplayPath(string fileName)
        {
            return System.IO.Path.Combine(_directory, fileName);
        }
    }

    /// <summary>测试用：内存里的文件名 → 文本。</summary>
    public sealed class MemoryFileSource : IFileSource
    {
        readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);

        public MemoryFileSource Add(string fileName, string text)
        {
            _files[fileName] = text;
            return this;
        }

        public bool Exists(string fileName) => _files.ContainsKey(fileName);

        public string ReadText(string fileName)
        {
            if (_files.TryGetValue(fileName, out var text)) return text;
            throw new DataLoadException(fileName, 0, null, "内存文件源里没有这个文件");
        }

        public string DisplayPath(string fileName) => "<memory>/" + fileName;
    }
}
