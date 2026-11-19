using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Swan.Core;
using Swan.Core.Data;
using Swan.Core.Gameplay;
using Swan.Core.Sim;
using Swan.Game.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Swan.Game
{
    /// <summary>
    /// 游戏内 HUD / 菜单 / 教程 / 设置。全部用 IMGUI：
    /// 不依赖预制体，场景生成器不用维护 UI 层级；对「小白改设置」来说，
    /// 最重要的是所有参数都能在游戏里点 ± 调、保存、立即生效。
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

        public sealed class GearReport
        {
            public string Source;
            public int WarningCount;
            public string FirstWarning;
            public float LoadSeconds;
            public string Note;
        }

        World _world;
        SimulationClock _clock;
        DataBundle _data;
        GearReport _report;
        FarmSystem _farm;
        ArtCatalog _art;
        string _dataDirectory;
        HoverCell _hover;

        bool _showTutorial = true;
        bool _showInventory;
        bool _showSettings;
        int _tutorialStep;
        Vector2 _settingsScroll;
        float _toastUntil;
        string _toast;
        string _error;
        bool _pointerOverUi;
        readonly List<Rect> _uiRects = new List<Rect>();

        Font _font;
        GUIStyle _panel;
        GUIStyle _panelDark;
        GUIStyle _label;
        GUIStyle _labelSmall;
        GUIStyle _helpPanel;
        GUIStyle _labelDim;
        GUIStyle _labelWrap;
        GUIStyle _title;
        GUIStyle _section;
        GUIStyle _button;
        GUIStyle _smallButton;
        GUIStyle _slot;
        GUIStyle _slotActive;
        GUIStyle _value;
        Texture2D _white;
        Texture2D _dim;
        Texture2D _accentTex;
        bool _stylesReady;
        bool _cursorInstalled;

        public bool PointerOverUi => _pointerOverUi;
        public bool AnyPanelOpen => _showTutorial || _showInventory || _showSettings;
        public Action ReloadRequested;

        public void Bind(World world, SimulationClock clock, DataBundle data, GearReport report, string dataDirectory)
        {
            Bind(world, clock, data, report, dataDirectory, null, null);
        }

        public void Bind(World world, SimulationClock clock, DataBundle data, GearReport report, string dataDirectory,
            FarmSystem farm, ArtCatalog art)
        {
            _world = world;
            _clock = clock;
            _data = data;
            _report = report;
            _dataDirectory = dataDirectory;
            _farm = farm;
            _art = art;
            if (_art != null && !_cursorInstalled)
            {
                Texture2D cursor = _art.CursorTexture;
                if (cursor != null)
                {
                    Cursor.SetCursor(cursor, new Vector2(2f, 2f), CursorMode.Auto);
                    _cursorInstalled = true;
                }
            }
        }

        public void SetHover(HoverCell hover) => _hover = hover;

        public void SetNote(string note)
        {
            if (string.IsNullOrEmpty(note)) return;
            SetToast(note);
        }

        public void ShowError(string message)
        {
            _error = message;
            SetToast(message);
        }

        public void ClearError() => _error = null;

        public void ToggleTutorial() => _showTutorial = !_showTutorial;
        public void OpenTutorial() => _showTutorial = true;
        public void ToggleInventory()
        {
            _showInventory = !_showInventory;
            if (_showInventory) _showSettings = false;
        }

        public void ToggleSettings()
        {
            _showSettings = !_showSettings;
            if (_showSettings) _showInventory = false;
        }

        public void ClosePanels()
        {
            _showInventory = false;
            _showSettings = false;
            _showTutorial = false;
        }

        public void SetToast(string message, float seconds = -1f)
        {
            _toast = message;
            float duration = seconds > 0f ? seconds : (_data != null ? _data.Config.ToastSeconds : 3f);
            _toastUntil = Time.unscaledTime + duration;
        }

        void Awake()
        {
            // 第一次进游戏自动打开教程；看过一次后按 F1 再打开。
            _showTutorial = PlayerPrefs.GetInt("swan.tutorialSeen", 0) == 0;
        }

        void Update()
        {
            if (!string.IsNullOrEmpty(_toast) && Time.unscaledTime > _toastUntil) _toast = null;
            if (!string.IsNullOrEmpty(_error) && Time.unscaledTime > _toastUntil) _error = null;

            if (AnyPanelOpen)
            {
                _pointerOverUi = true;
                return;
            }

            // 工程启用了 Input System，禁止再读旧的 UnityEngine.Input。
            Vector2 screen = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Vector2 mouse = new Vector2(screen.x, Screen.height - screen.y);
            _pointerOverUi = false;
            for (int i = 0; i < _uiRects.Count; i++)
            {
                if (_uiRects[i].Contains(mouse))
                {
                    _pointerOverUi = true;
                    break;
                }
            }
        }

        void OnGUI()
        {
            if (_data == null) return;
            EnsureStyles();
            _uiRects.Clear();

            DrawTopBar();
            DrawHoverCard();
            DrawHotbar();
            DrawHelp();
            DrawToast();

            if (_showTutorial) DrawTutorial();
            if (_showInventory) DrawInventory();
            if (_showSettings) DrawSettings();
        }

        // ————————————————————————————— 顶部 —————————————————————————————

        void DrawTopBar()
        {
            var config = _data.Config;
            float margin = config.HudMargin;
            var rect = new Rect(margin, margin, Mathf.Min(760f, Screen.width * 0.55f), 74f);
            _uiRects.Add(rect);
            GUI.Box(rect, GUIContent.none, _panel);

            int day = _clock != null ? 1 + (int)_clock.EnvStepsRun / Mathf.Max(1, config.EnvStepsPerDay) : 1;
            int stepOfDay = _clock != null ? (int)_clock.EnvStepsRun % Mathf.Max(1, config.EnvStepsPerDay) : 0;
            float dayProgress = stepOfDay / (float)Mathf.Max(1, config.EnvStepsPerDay);

            GUI.Label(new Rect(rect.x + 14, rect.y + 6, 300, 24), "SWAN · 土地作者", _title);
            string speed = _clock == null ? "—" :
                (_clock.Paused ? "已暂停" : $"×{_clock.Speed:0.#}");
            GUI.Label(new Rect(rect.x + 14, rect.y + 32, 420, 20),
                $"第 {day} 天   {stepOfDay:00}/{config.EnvStepsPerDay} 步   {speed}", _label);

            var bar = new Rect(rect.x + 14, rect.y + 56, rect.width - 28, 8);
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(bar, _white);
            GUI.color = ToColor(config.UiAccentColor);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(dayProgress), bar.height), _white);
            GUI.color = Color.white;

            if (_world != null)
            {
                GUI.Label(new Rect(rect.x + 430, rect.y + 32, 320, 20),
                    $"收获 {_world.TotalHarvested:0} · 野物 {_world.ForageCount}", _labelDim);
            }
        }

        // ————————————————————————————— 悬停卡 —————————————————————————————

        void DrawHoverCard()
        {
            var config = _data.Config;
            float width = 320f;
            float height = 206f;
            var rect = new Rect(Screen.width - width - config.HudMargin, config.HudMargin, width, height);
            _uiRects.Add(rect);
            GUI.Box(rect, GUIContent.none, _panel);

            if (!_hover.InMap)
            {
                GUI.Label(new Rect(rect.x + 14, rect.y + 12, rect.width - 28, 22), "鼠标指向地块", _title);
                GUI.Label(new Rect(rect.x + 14, rect.y + 42, rect.width - 28, 130),
                    "把鼠标移到地图上，这里会显示地形、水分、养分、温度、植物和可执行动作。", _labelDim);
                return;
            }

            Cell cell = _hover.Cell;
            string terrain = _data.Terrain.TryGetById(cell.TerrainId, out var row) ? row.Name : $"地形 {cell.TerrainId}";
            GUI.Label(new Rect(rect.x + 14, rect.y + 8, rect.width - 28, 24),
                $"{terrain}   ({_hover.X},{_hover.Y})", _title);

            string inRange = _farm != null && _farm.InRange(_hover.X, _hover.Y) ? "在交互范围内" : "超出交互范围";
            GUI.Label(new Rect(rect.x + 14, rect.y + 34, rect.width - 28, 20), inRange,
                _farm != null && _farm.InRange(_hover.X, _hover.Y) ? _label : _labelSmall);

            GUI.Label(new Rect(rect.x + 14, rect.y + 58, rect.width - 28, 20),
                $"水 {cell.Water:0.0}    养分 {cell.Nutrient:0.0}    温度 {cell.Temperature:0.0}", _label);

            string plant = "无";
            if (cell.HasPlant)
            {
                var type = _data.Plants.RowByIndex(cell.PlantTypeId);
                plant = $"{type.Name}  生长 {cell.Growth:0.0}/{config.RipeThreshold:0}";
                if (cell.Harvestable) plant += "  ✦可收获";
            }
            GUI.Label(new Rect(rect.x + 14, rect.y + 82, rect.width - 28, 20), $"植物：{plant}", _label);

            string structure = cell.HasStructure && _data.Structures.TryGetRow(cell.StructureId, out var s)
                ? s.Name : "无";
            string forage = string.IsNullOrEmpty(cell.ForageItemId)
                ? "无"
                : (_data.Items.TryGet(cell.ForageItemId, out var item) ? item.Name : cell.ForageItemId);
            GUI.Label(new Rect(rect.x + 14, rect.y + 106, rect.width - 28, 20), $"结构：{structure}", _label);
            GUI.Label(new Rect(rect.x + 14, rect.y + 130, rect.width - 28, 20), $"野物：{forage}", _label);
            GUI.Label(new Rect(rect.x + 14, rect.y + 154, rect.width - 28, 20),
                cell.HasHeatSource ? "热源：有（温度会升高）" : "热源：无", _labelDim);

            if (_farm != null)
            {
                GUI.Label(new Rect(rect.x + 14, rect.y + 178, rect.width - 28, 20),
                    $"当前动作：{_farm.CurrentAction?.Name ?? "无"}（F 或左键执行）", _labelDim);
            }
        }

        // ————————————————————————————— 动作栏 —————————————————————————————

        void DrawHotbar()
        {
            if (_farm == null) return;
            var config = _data.Config;
            var actions = _farm.Actions;
            int count = actions.Count;
            if (count == 0) return;

            float slot = 82f;
            float gap = 6f;
            float width = count * (slot + gap) + 18f;
            float height = 118f;
            float x = config.HudMargin;
            float y = Screen.height - height - config.HudMargin;
            var panelRect = new Rect(x, y, width, height);
            _uiRects.Add(panelRect);
            GUI.Box(panelRect, GUIContent.none, _panel);

            for (int i = 0; i < count; i++)
            {
                var action = actions[i];
                bool active = i == _farm.CurrentActionIndex;
                var slotRect = new Rect(x + 9 + i * (slot + gap), y + 9, slot, 70);
                if (active) _uiRects.Add(slotRect);
                GUI.Box(slotRect, GUIContent.none, active ? _slotActive : _slot);

                if (_art != null && _art.TryGetActionIcon(action.ActionId, out var icon)) DrawIcon(
                    new Rect(slotRect.x + 27, slotRect.y + 10, 28, 28), icon, Color.white);

                GUI.Label(new Rect(slotRect.x, slotRect.y + 42, slotRect.width, 18), $"{i + 1} {action.Name}", _labelSmall);
                if (GUI.Button(slotRect, GUIContent.none, GUIStyle.none))
                {
                    _farm.SelectAction(i);
                    SetToast($"当前动作：{action.Name}");
                }
            }

            string itemLine;
            if (_farm.CurrentAction != null && _farm.CurrentAction.SelectsItem)
            {
                var item = _farm.SelectedItem;
                itemLine = item == null
                    ? "没有可用物品"
                    : $"{item.Name} × {_farm.SelectedItemCount}   [Q/E 切换]";
            }
            else
            {
                itemLine = _farm.CurrentAction?.Description ?? string.Empty;
            }
            GUI.Label(new Rect(x + 12, y + 86, width - 24, 22), itemLine, _label);
        }

        // ————————————————————————————— 教程 / 背包 / 设置 —————————————————————————————

        void DrawTutorial()
        {
            _pointerOverUi = true;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _dim);
            float width = Mathf.Min(720f, Screen.width - 40f);
            float height = Mathf.Min(470f, Screen.height - 40f);
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            _uiRects.Add(rect);
            GUI.Box(rect, GUIContent.none, _panel);

            var steps = _data.Tutorial.Steps;
            if (steps.Count == 0)
            {
                GUI.Label(new Rect(rect.x + 20, rect.y + 20, rect.width - 40, 30), "教程数据为空", _title);
                if (GUI.Button(new Rect(rect.center.x - 70, rect.yMax - 60, 140, 36), "关闭", _button))
                    _showTutorial = false;
                return;
            }

            _tutorialStep = Mathf.Clamp(_tutorialStep, 0, steps.Count - 1);
            var step = steps[_tutorialStep];
            GUI.Label(new Rect(rect.x + 24, rect.y + 18, rect.width - 48, 30),
                $"新手教程  {_tutorialStep + 1}/{steps.Count}", _title);
            GUI.Label(new Rect(rect.x + 24, rect.y + 58, rect.width - 48, 32), step.Title, _section);
            GUI.Label(new Rect(rect.x + 24, rect.y + 100, rect.width - 48, 250), step.Text, _labelWrap);

            float by = rect.yMax - 58;
            if (GUI.Button(new Rect(rect.x + 24, by, 100, 36), "上一步", _button) && _tutorialStep > 0)
            {
                _tutorialStep--;
            }
            if (GUI.Button(new Rect(rect.x + 132, by, 100, 36), "下一步", _button) && _tutorialStep < steps.Count - 1)
            {
                _tutorialStep++;
            }
            if (GUI.Button(new Rect(rect.xMax - 144, by, 120, 36),
                    _tutorialStep == steps.Count - 1 ? "开始游戏" : "跳过", _button))
            {
                _showTutorial = false;
                PlayerPrefs.SetInt("swan.tutorialSeen", 1);
            }
        }

        void DrawInventory()
        {
            _pointerOverUi = true;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _dim);
            float width = Mathf.Min(760f, Screen.width - 40f);
            float height = Mathf.Min(520f, Screen.height - 40f);
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            _uiRects.Add(rect);
            GUI.Box(rect, GUIContent.none, _panel);
            GUI.Label(new Rect(rect.x + 20, rect.y + 14, rect.width - 40, 30), "背包", _title);
            GUI.Label(new Rect(rect.x + 20, rect.y + 46, rect.width - 40, 20),
                "采集、挖掘、收获都会进背包。种子和放置物在动作栏用 Q/E 切换。", _labelDim);

            var rows = _data.Items.Rows;
            int columns = 6;
            float slotW = 108f;
            float slotH = 74f;
            for (int i = 0; i < rows.Count; i++)
            {
                var item = rows[i];
                int count = _farm != null ? _farm.Inventory.Count(item.ItemId) : 0;
                int col = i % columns;
                int row = i / columns;
                var slot = new Rect(rect.x + 20 + col * slotW, rect.y + 76 + row * slotH, slotW - 8, slotH - 8);
                GUI.Box(slot, GUIContent.none, count > 0 ? _slot : _slot);
                if (_art != null && _art.TryGetItemIcon(item, out var icon))
                    DrawIcon(new Rect(slot.x + 8, slot.y + 8, 32, 32), icon, count > 0 ? Color.white : new Color(1, 1, 1, 0.35f));
                GUI.Label(new Rect(slot.x + 46, slot.y + 8, slot.width - 50, 40), item.Name, _labelSmall);
                GUI.Label(new Rect(slot.x + 46, slot.y + 36, slot.width - 50, 22), $"× {count}", _label);
            }

            if (GUI.Button(new Rect(rect.xMax - 124, rect.yMax - 52, 100, 34), "关闭", _button))
                _showInventory = false;
        }

        void DrawSettings()
        {
            _pointerOverUi = true;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _dim);
            float width = Mathf.Min(1020f, Screen.width - 30f);
            float height = Mathf.Min(700f, Screen.height - 30f);
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            _uiRects.Add(rect);
            GUI.Box(rect, GUIContent.none, _panel);
            GUI.Label(new Rect(rect.x + 20, rect.y + 12, rect.width - 40, 30), "设置 · 保存后立即生效", _title);
            GUI.Label(new Rect(rect.x + 20, rect.y + 44, rect.width - 40, 20),
                "点右下「保存并立即应用」会写回 config.json 并重新载入；也能直接用记事本改 Data/config.json。", _labelDim);

            var inner = new Rect(rect.x + 18, rect.y + 70, rect.width - 36, rect.height - 132);
            _settingsScroll = GUI.BeginScrollView(inner, _settingsScroll, new Rect(0, 0, inner.width - 20, 980));
            GUILayout.BeginArea(new Rect(0, 0, inner.width - 20, 980));
            var config = _data.Config;
            Section("主角");
            Stepper("移动速度（格/秒）", config.PlayerMoveCellsPerSecond, 0.5f, 0.5f, 20f, "0.0",
                v => config.PlayerMoveCellsPerSecond = v);
            Stepper("主角精灵大小", config.PlayerSpriteScale, 0.25f, 0.25f, 6f, "0.00",
                v => config.PlayerSpriteScale = v);
            Stepper("行走动画帧率", config.PlayerAnimationFps, 1f, 1f, 30f, "0",
                v => config.PlayerAnimationFps = v);
            Stepper("碰撞半径（格）", config.PlayerCollisionRadius, 0.02f, 0.05f, 0.49f, "0.00",
                v => config.PlayerCollisionRadius = v);

            Section("交互");
            Stepper("交互范围宽", config.InteractionWidth, 2f, 3f, 31f, "0",
                v => config.InteractionWidth = Mathf.RoundToInt(v));
            Stepper("交互范围高", config.InteractionHeight, 2f, 3f, 31f, "0",
                v => config.InteractionHeight = Mathf.RoundToInt(v));
            Stepper("收获返还种子", config.SeedReturnOnHarvest, 1f, 0f, 10f, "0",
                v => config.SeedReturnOnHarvest = Mathf.RoundToInt(v));

            Section("时间与生长");
            Stepper("环境步长（秒）", config.EnvStepSeconds, 1f, 1f, 120f, "0",
                v => config.EnvStepSeconds = v);
            Stepper("生长速度倍率", config.GrowthSpeedMultiplier, 0.1f, 0.1f, 20f, "0.0",
                v => config.GrowthSpeedMultiplier = v);
            Stepper("每天环境步数", config.EnvStepsPerDay, 1f, 1f, 96f, "0",
                v => config.EnvStepsPerDay = Mathf.RoundToInt(v));
            Stepper("野物再生概率/步", config.ForageRegrowChance, 0.0005f, 0f, 0.05f, "0.0000",
                v => config.ForageRegrowChance = v);
            Stepper("野物数量上限", config.ForageMaxCount, 5f, 0f, 600f, "0",
                v => config.ForageMaxCount = Mathf.RoundToInt(v));

            Section("镜头");
            Stepper("镜头视野（格）", config.CameraOrthographicSize, 0.5f, 2f, 30f, "0.0",
                v => config.CameraOrthographicSize = v);
            Stepper("最大缩放", config.ZoomMax, 0.25f, 1f, 10f, "0.00",
                v => config.ZoomMax = v);

            Section("地图（需重载）");
            Stepper("地图种子", config.MapSeed, 1f, 0f, 99999999f, "0",
                v => config.MapSeed = Mathf.RoundToInt(v));
            Stepper("地图宽", config.GridWidth, 2f, 16f, 200f, "0",
                v => config.GridWidth = Mathf.RoundToInt(v));
            Stepper("地图高", config.GridHeight, 2f, 16f, 200f, "0",
                v => config.GridHeight = Mathf.RoundToInt(v));
            Stepper("岩石密度（0=关闭）", config.RockChance, 0.002f, 0f, 0.2f, "0.000",
                v => config.RockChance = v);

            Section("界面");
            Stepper("HUD 字号", config.HudFontSize, 1f, 9f, 28f, "0",
                v => config.HudFontSize = Mathf.RoundToInt(v));
            Stepper("面板不透明度", config.HudPanelAlpha, 0.02f, 0.2f, 1f, "0.00",
                v => config.HudPanelAlpha = v);
            Stepper("提示停留（秒）", config.ToastSeconds, 0.2f, 0.5f, 10f, "0.0",
                v => config.ToastSeconds = v);

            GUILayout.Space(10);
            GUILayout.Label($"数据目录：{_dataDirectory}", _labelDim);
            if (_report != null)
                GUILayout.Label($"载入：{_report.Source} · 警告 {_report.WarningCount} · {_report.LoadSeconds:0.00}s", _labelDim);
            GUILayout.EndArea();
            GUI.EndScrollView();

            float by = rect.yMax - 50;
            if (GUI.Button(new Rect(rect.x + 20, by, 180, 34), "保存并立即应用", _button))
            {
                if (ConfigIO.Save(_data.Config, _dataDirectory, out var message))
                {
                    SetToast(message);
                    _showSettings = false;
                    ReloadRequested?.Invoke();
                }
                else
                {
                    ShowError(message);
                }
            }
            if (GUI.Button(new Rect(rect.x + 210, by, 150, 34), "恢复默认", _button))
            {
                if (ConfigIO.RestoreDefault(_dataDirectory, out var message))
                {
                    SetToast(message);
                    _showSettings = false;
                    ReloadRequested?.Invoke();
                }
                else
                {
                    ShowError(message);
                }
            }
            if (GUI.Button(new Rect(rect.x + 370, by, 150, 34), "打开数据目录", _button))
                ConfigIO.OpenFolder(_dataDirectory);
            if (GUI.Button(new Rect(rect.xMax - 120, by, 100, 34), "关闭", _button))
                _showSettings = false;
        }

        // ————————————————————————————— 底部提示 —————————————————————————————

        void DrawHelp()
        {
            var config = _data.Config;
            var rect = new Rect(Screen.width - 520 - config.HudMargin, Screen.height - 116 - config.HudMargin, 520, 60);
            _uiRects.Add(rect);
            GUI.Label(rect,
                "WASD / 方向键 移动　左键 点地块交互　1-8 选动作\nQ/E 切换种子·放置物　空格 暂停　N 单步　[ / ] 变速\nTab 背包　O 设置　F1 教程　F5 热重载　R 新地图",
                _helpPanel);
        }

        void DrawToast()
        {
            if (string.IsNullOrEmpty(_toast)) return;
            float width = Mathf.Min(720f, Screen.width - 80f);
            var rect = new Rect((Screen.width - width) * 0.5f, Screen.height - 230f, width, 46f);
            GUI.Box(rect, GUIContent.none, _panelDark);
            GUI.Label(new Rect(rect.x + 14, rect.y + 10, rect.width - 28, 28), _toast, _label);
        }

        // ————————————————————————————— 样式 —————————————————————————————

        void EnsureStyles()
        {
            if (_stylesReady && _font != null) return;
            _font = Font.CreateDynamicFontFromOSFont(new[]
            {
                "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Arial Unicode MS"
            }, 16);
            _white = MakeTexture(Color.white);
            _dim = MakeTexture(new Color(0f, 0f, 0f, 0.55f));
            _accentTex = MakeTexture(new Color(0.95f, 0.76f, 0.31f, 1f));

            var config = _data.Config;
            float alpha = Mathf.Clamp01(config.HudPanelAlpha);
            Color panel = ToColor(config.UiPanelColor);
            panel.a = alpha;
            Color button = ToColor(config.UiButtonColor);
            Color text = ToColor(config.UiTextColor);
            Color muted = ToColor(config.UiMutedColor);
            Color accent = ToColor(config.UiAccentColor);
            int baseSize = Mathf.Clamp(config.HudFontSize, 11, 24);

            _panel = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTexture(panel) },
                padding = new RectOffset(12, 12, 10, 10)
            };
            _panelDark = new GUIStyle(_panel)
            {
                normal = { background = MakeTexture(new Color(0.03f, 0.04f, 0.05f, 0.92f)) }
            };
            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = baseSize,
                font = _font,
                normal = { textColor = text },
                wordWrap = false
            };
            _labelSmall = new GUIStyle(_label) { fontSize = baseSize - 2 };
            _labelSmall.normal.textColor = muted;
            _helpPanel = new GUIStyle(_labelSmall)
            {
                padding = new RectOffset(10, 10, 6, 6),
                alignment = TextAnchor.MiddleRight
            };
            _helpPanel.normal.background = MakeTexture(new Color(panel.r, panel.g, panel.b, Mathf.Clamp01(alpha * 0.85f)));
            _labelDim = new GUIStyle(_labelSmall) { wordWrap = true };
            _labelWrap = new GUIStyle(_label) { wordWrap = true, fontSize = baseSize + 1, richText = true };
            _title = new GUIStyle(_label) { fontSize = baseSize + 7, fontStyle = FontStyle.Bold };
            _title.normal.textColor = accent;
            _section = new GUIStyle(_label) { fontSize = baseSize + 2, fontStyle = FontStyle.Bold };
            _section.normal.textColor = accent;
            _value = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };

            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = baseSize,
                font = _font,
                normal = { background = MakeTexture(button), textColor = text },
                hover = { background = MakeTexture(button * 1.25f), textColor = accent },
                active = { background = MakeTexture(button * 0.8f), textColor = accent },
                alignment = TextAnchor.MiddleCenter
            };
            _smallButton = new GUIStyle(_button) { fontSize = baseSize + 3, padding = new RectOffset(4, 4, 0, 0) };
            _slot = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTexture(button * 0.8f) },
                border = new RectOffset(2, 2, 2, 2)
            };
            _slotActive = new GUIStyle(_slot)
            {
                normal = { background = MakeTexture(button * 1.3f) },
                border = new RectOffset(2, 2, 2, 2)
            };
            _slotActive.border = new RectOffset(3, 3, 3, 3);
            GUI.skin.button.font = _font;
            _stylesReady = true;
        }

        void Section(string title)
        {
            GUILayout.Space(12);
            GUILayout.Label(title, _section);
        }

        void Stepper(string label, float value, float step, float min, float max, string format, Action<float> apply)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _label, GUILayout.Width(260));
            if (GUILayout.Button("−", _smallButton, GUILayout.Width(36), GUILayout.Height(28)))
                apply(Mathf.Clamp(value - step, min, max));
            GUILayout.Label(value.ToString(format, CultureInfo.InvariantCulture), _value, GUILayout.Width(90));
            if (GUILayout.Button("+", _smallButton, GUILayout.Width(36), GUILayout.Height(28)))
                apply(Mathf.Clamp(value + step, min, max));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        void DrawIcon(Rect rect, ArtCatalog.IconRef icon, Color tint)
        {
            if (_art == null || !_art.TryGetUv(icon, out var texture, out var uv)) return;
            Color old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, texture, uv);
            GUI.color = old;
        }

        static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "SwanUi",
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, false);
            return texture;
        }

        static Color ToColor(string hex)
        {
            if (ColorHex.TryParse(hex, out var r, out var g, out var b)) return new Color(r, g, b, 1f);
            return Color.white;
        }
    }
}
