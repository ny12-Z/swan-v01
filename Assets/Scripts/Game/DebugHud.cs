using System;
using System.Text;
using Swan.Core;
using Swan.Core.Data;
using Swan.Core.Sim;
using UnityEngine;

namespace Swan.Game
{
    /// <summary>
    /// 调试 HUD（仪器版的门面）。
    ///
    /// 用 IMGUI（OnGUI）而不是 uGUI：这一版 HUD 是「仪器面板」，不是美术；
    /// IMGUI 不需要任何预制体/字体资产就能跑，改了立刻生效，也不会污染场景。
    /// 它只读 World/Clock 的状态，不参与任何玩法计算（逻辑与表现分离）。
    ///
    /// 注意：HUD 里的文字全部由数据/状态拼出来，代码里没有业务数值。
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        public struct HoverCell
        {
            public bool InMap;
            public int X;
            public int Y;
            public Cell Cell;
        }

        /// <summary>启动/热重载时的一次性信息（数据来源、警告、耗时）。</summary>
        public sealed class GearReport
        {
            public string Source = "";
            public int WarningCount;
            public string FirstWarning = "";
            public float LoadSeconds;
            public string Note = "";
        }

        World _world;
        SimulationClock _clock;
        EngineConfig _config;
        DataBundle _data;
        GearReport _report = new GearReport();

        string _dataDirectory = "";
        string _error = "";
        string _note = "";
        HoverCell _hover;
        bool _visible = true;

        // 时钟面板高度 = 上次测量的结果，用来把数据面板排在它下面
        float _clockPanelHeight = 96f;

        GUIStyle _style;
        Texture2D _panelTexture;
        readonly StringBuilder _text = new StringBuilder(512);

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        public void Bind(World world, SimulationClock clock, DataBundle data, GearReport report, string dataDirectory)
        {
            _world = world;
            _clock = clock;
            _data = data;
            _config = data.Config;
            _dataDirectory = dataDirectory ?? "";
            if (report != null) _report = report;
        }

        public void SetHover(HoverCell hover) => _hover = hover;

        /// <summary>最近一次操作的反馈（种下/收获/改了热源/倍速）。</summary>
        public void SetNote(string note) => _note = note ?? "";

        public void ShowError(string message) => _error = message ?? "";

        public void ClearError() => _error = "";

        void Update()
        {
            // HUD 自己吃掉 F1：这是纯表现开关，不属于玩法输入
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame) _visible = !_visible;
        }

        void OnGUI()
        {
            if (!_visible) return;
            if (_world == null || _config == null) return;

            EnsureStyles();
            int margin = _config.HudMargin;

            DrawPanel(new Vector2(margin, margin), BuildClockAndStatsText());
            DrawPanel(new Vector2(margin, margin + _clockPanelHeight + margin / 2f), BuildDataText());

            string hoverText = BuildHoverText();
            Vector2 hoverSize = _style.CalcSize(new GUIContent(hoverText));
            float hoverX = Screen.width - hoverSize.x - margin * 2f;
            DrawPanel(new Vector2(hoverX, margin), hoverText);

            string help = BuildHelpText(_config);
            Vector2 helpSize = _style.CalcSize(new GUIContent(help));
            DrawPanel(new Vector2(margin, Screen.height - helpSize.y - margin * 3f), help);

            if (!string.IsNullOrEmpty(_error))
            {
                DrawPanel(new Vector2(margin, Screen.height * 0.35f),
                    "加载失败：\n" + _error,
                    new Color(0.45f, 0.06f, 0.06f, 0.92f));
            }
        }

        /// <summary>按键提示按 config 里实际的倍速档位拼，代码里不写死「1/2/3」。</summary>
        static string BuildHelpText(EngineConfig config)
        {
            int speedKeys = Mathf.Min(9, config.SpeedLevels == null ? 0 : config.SpeedLevels.Length);
            string speedHint = speedKeys <= 0
                ? ""
                : (speedKeys == 1 ? "1 倍速" : $"1..{speedKeys} 倍速") + " · ";
            return "空格 暂停/继续 · N 单步 · " + speedHint +
                   "F 种/收 · T 热源 · F5 热重载(保留状态) · R 重建地图 · WASD/方向键 平移 · 滚轮 缩放 · F1 隐藏HUD" +
                   "　|　黄色框 = 你现在控制的格";
        }

        void DrawPanel(Vector2 position, string text)
            => DrawPanel(position, text, new Color(0.03f, 0.04f, 0.05f, _config.HudPanelAlpha));

        void DrawPanel(Vector2 position, string text, Color background)
        {
            Vector2 size = _style.CalcSize(new GUIContent(text));
            float padding = 8f;
            var rect = new Rect(position.x, position.y, size.x + padding * 2f, size.y + padding * 2f);

            Color old = GUI.color;
            GUI.color = background;
            GUI.DrawTexture(rect, _panelTexture);
            GUI.color = old;

            var labelRect = new Rect(rect.x + padding, rect.y + padding, size.x + 1f, size.y + 1f);
            GUI.Label(labelRect, text, _style);
        }

        string BuildClockAndStatsText()
        {
            WorldStats stats = _world.Stats;
            _text.Clear();
            _text.Append("SWAN v0.1 · 数值骨架/仪器版");
            _text.Append("    ").Append(_clock.Paused ? "已暂停" : "运行中");
            _text.Append("  x").Append(_clock.Speed.ToString("0.##"));
            _text.Append('\n');

            _text.Append("环境步 ").Append(_world.EnvStepCount);
            _text.Append("   ·   距下一步 ").Append((_clock.EnvProgress * 100f).ToString("0")).Append("%");
            _text.Append("   ·   视觉 tick ").Append(_clock.VisualTicks);
            _text.Append('\n');

            _text.Append("总水量 ").Append(stats.TotalWater.ToString("0.0"));
            _text.Append("  ·  平均 ").Append(stats.AverageWater.ToString("0.00"));
            _text.Append("  ·  覆盖 ").Append((stats.WaterCoverage * 100f).ToString("0.0")).Append("%");
            _text.Append('\n');

            _text.Append("可收获 ").Append(stats.HarvestableCells);
            _text.Append("  ·  已种 ").Append(stats.PlantedCells);
            _text.Append("  ·  累计收获 ").Append(_world.TotalHarvested.ToString("0.##"));
            _text.Append("（").Append(_world.HarvestCount).Append(" 次）");

            string text = _text.ToString();
            _clockPanelHeight = _style.CalcSize(new GUIContent(text)).y + 16f;
            return text;
        }

        string BuildDataText()
        {
            _text.Clear();
            _text.Append("数据 ").Append(_config.GridWidth).Append('×').Append(_config.GridHeight);
            _text.Append("  ·  地形 ").Append(_data.Terrain.Rows.Count);
            _text.Append("  性状 ").Append(_data.Traits.Rows.Count);
            _text.Append("  植物 ").Append(_data.Plants.Rows.Count);
            _text.Append("  法则 ").Append(_data.Rules.Rows.Count);
            _text.Append('\n');

            _text.Append("来源 ").Append(string.IsNullOrEmpty(_dataDirectory) ? _report.Source : _dataDirectory);
            _text.Append('\n');

            _text.Append("上次加载 ").Append(_report.LoadSeconds.ToString("0.00")).Append(" 秒");
            _text.Append("  ·  警告 ").Append(_report.WarningCount);
            if (_report.WarningCount > 0 && !string.IsNullOrEmpty(_report.FirstWarning))
            {
                _text.Append('\n').Append("[警告] ").Append(_report.FirstWarning);
            }
            if (!string.IsNullOrEmpty(_report.Note))
            {
                _text.Append('\n').Append(_report.Note);
            }
            if (!string.IsNullOrEmpty(_note))
            {
                _text.Append('\n').Append("最近操作：").Append(_note);
            }
            return _text.ToString();
        }

        string BuildHoverText()
        {
            _text.Clear();
            if (!_hover.InMap)
            {
                _text.Append("光标：地图外");
                return _text.ToString();
            }

            Cell cell = _hover.Cell;
            TerrainRow terrain = _data.Terrain.GetById(cell.TerrainId);
            // 这就是「玩家现在在哪一格」：画面里那圈黄色高亮框指的就是它
            _text.Append("光标格 (").Append(_hover.X).Append(',').Append(_hover.Y).Append(')');
            if (cell.HasHeatSource) _text.Append("   [热源]");
            _text.Append('\n');

            _text.Append(terrain.Name);
            _text.Append("   温度 ").Append(cell.Temperature.ToString("0.0"));
            _text.Append('\n');

            _text.Append("水 ").Append(cell.Water.ToString("0.0"));
            _text.Append("   养分 ").Append(cell.Nutrient.ToString("0.0"));
            _text.Append('\n');

            if (cell.HasPlant && _data.Plants.TryGetRow(cell.PlantTypeId, out PlantTypeRow type))
            {
                _text.Append(type.TypeId).Append(' ').Append(type.Name);
                _text.Append("   生长 ").Append(cell.Growth.ToString("0.00"));
                _text.Append(" / ").Append(_config.RipeThreshold.ToString("0"));
                if (cell.Harvestable) _text.Append("   [可收]");
                _text.Append('\n');
                _text.Append("性状 ").Append(cell.Traits == null || cell.Traits.Length == 0
                    ? "（无）"
                    : string.Join("|", cell.Traits));
                if (cell.Growth >= _config.RipeThreshold) _text.Append("   已成熟");
            }
            else
            {
                _text.Append("无植物（F 种下 P01）");
            }

            return _text.ToString();
        }

        void EnsureStyles()
        {
            if (_style != null) return;

            if (_panelTexture == null)
            {
                _panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _panelTexture.SetPixel(0, 0, Color.white);
                _panelTexture.Apply(false);
            }

            // IMGUI 自带字体没有中文字形，优先用系统里有的中文字体
            Font font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Arial Unicode MS" },
                Mathf.Max(8, _config.HudFontSize));

            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(8, _config.HudFontSize),
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
                richText = false,
            };
            if (font != null) _style.font = font;
            _style.normal.textColor = new Color(0.92f, 0.95f, 1f, 1f);
        }

        void OnDestroy()
        {
            if (_panelTexture != null) Destroy(_panelTexture);
        }
    }
}
