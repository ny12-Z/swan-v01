using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Swan.Core;
using Swan.Core.Data;
using Swan.Core.Gameplay;
using Swan.Core.Sim;
using Swan.Game.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;
using Key = UnityEngine.InputSystem.Key;

namespace Swan.Game
{
    /// <summary>
    /// 装配与输入（唯一的「胶水层」）。
    ///
    /// 职责只有三件：
    /// 1. 把数据、世界、时钟、玩家、渲染、HUD 接起来；
    /// 2. 把键鼠输入翻译成玩家/动作/时钟/相机的方法调用；
    /// 3. 每帧驱动时钟并让渲染跟上脏格。
    ///
    /// 它自己不写任何玩法规则：移动/碰撞/范围/动作全在 Core 里，
    /// 这样「玩法能不能被测试」不取决于场景里挂了什么。
    /// </summary>
    public sealed class SwanBootstrap : MonoBehaviour
    {
        [Tooltip("留空则读 StreamingAssets/Data；也可以命令行 -swanDataDir 指定")]
        [SerializeField] string _dataDirectoryOverride = "";

        // 冒烟运行（打包后的自动验收用）：-swanSmokeSeconds <秒> -swanShot <png> -swanSpeed <倍速>
        float _smokeSeconds;
        string _shotPath;
        float _smokeElapsed;
        bool _shotTaken;
        bool _hasSmokeCursor;
        int _smokeCursorX;
        int _smokeCursorY;
        string _smokeAction;
        string _smokePanel;
        bool _smokeInteract;
        bool _smokeInteracted;

        World _world;
        SimulationClock _clock;
        DataBundle _data;
        IFileSource _source;
        string _dataDirectory = "";

        MapRenderer _mapRenderer;
        Camera _mapCamera;
        MapCameraController _cameraController;
        DebugHud _hud;
        PlayerView _playerView;
        SceneryView _scenery;

        Inventory _inventory;
        PlayerState _player;
        FarmSystem _farm;
        ArtCatalog _art;

        bool _isFirstLoad = true;
        DebugHud.HoverCell _hover;
        int _lastZoneX = int.MinValue;
        int _lastZoneY = int.MinValue;
        int _lastZoneW = int.MinValue;
        int _lastZoneH = int.MinValue;

        void Awake()
        {
            Application.runInBackground = true;

            EnsureCamera();
            EnsureHud();
            EnsureRenderer();
            EnsurePlayerView();
            EnsureScenery();
            LoadAll(keepWorldState: false);
            ReadSmokeArguments();
        }

        /// <summary>
        /// 冒烟参数（打包验收用，不参与玩法）：
        /// -swanSmokeSeconds 5 -swanShot shot.png -swanSpeed 60
        /// 跑满指定秒数后截图并退出，用来证明「exe 真的能起来、画面真的在动」。
        /// </summary>
        void ReadSmokeArguments()
        {
            _smokeSeconds = CommandLine.GetFloat("-swanSmokeSeconds", 0f);
            if (_smokeSeconds <= 0f) return;

            _shotPath = CommandLine.Get("-swanShot", "swan-smoke.png");
            float speed = CommandLine.GetFloat("-swanSpeed", 0f);
            if (speed > 0f && _clock != null) _clock.SetSpeed(speed);

            _smokeAction = CommandLine.Get("-swanAction");
            if (!string.IsNullOrEmpty(_smokeAction) && _farm != null) _farm.TrySelectActionByHotkey(_smokeAction, out _);

            // 无人值守截图时鼠标可能不在窗口里：允许钉住光标格，好让证据图里也能看到那个框
            string cursorCell = CommandLine.Get("-swanCursorCell");
            if (!string.IsNullOrWhiteSpace(cursorCell))
            {
                var parts = cursorCell.Split(',');
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), out _smokeCursorX)
                    && int.TryParse(parts[1].Trim(), out _smokeCursorY))
                {
                    _hasSmokeCursor = true;
                }
                else
                {
                    Debug.LogWarning($"[SWAN] -swanCursorCell 需要写成 \"x,y\"，收到的是：{cursorCell}");
                }
            }

            _smokePanel = CommandLine.Get("-swanPanel");
            _smokeInteract = CommandLine.GetFloat("-swanInteract", 0f) > 0f;

            _hud?.ClosePanels();
            if (_hud != null && !string.IsNullOrEmpty(_smokePanel))
            {
                switch (_smokePanel)
                {
                    case "tutorial": _hud.OpenTutorial(); break;
                    case "settings": _hud.ToggleSettings(); break;
                    case "inventory": _hud.ToggleInventory(); break;
                }
            }
            Debug.Log($"[SWAN] 冒烟运行 {_smokeSeconds:0.#} 秒后截图：{_shotPath}（倍速 x{speed:0.##}）");
        }

        void UpdateSmoke()
        {
            if (_smokeSeconds <= 0f) return;

            _smokeElapsed += Time.unscaledDeltaTime;

            // 自动验收：开局等 1 秒，让玩家在自己的格上执行一次当前动作（截图里能看到结果）。
            if (_smokeInteract && !_smokeInteracted && _smokeElapsed >= 1f && _farm != null)
            {
                _smokeInteracted = true;
                int x = _hasSmokeCursor ? _smokeCursorX : _player.CellX;
                int y = _hasSmokeCursor ? _smokeCursorY : _player.CellY;
                InteractAt(x, y);
            }

            if (_smokeElapsed < _smokeSeconds) return;

            if (!_shotTaken)
            {
                _shotTaken = true;
                ScreenCapture.CaptureScreenshot(_shotPath);
                Debug.Log($"[SWAN] 冒烟截图：{_shotPath}");
                return;
            }

            if (_smokeElapsed >= _smokeSeconds + 1f)
            {
                Debug.Log("[SWAN] 冒烟运行结束，退出");
                Application.Quit();
            }
        }

        void Update()
        {
            var keyboard = Keyboard.current;

            // F5 热重载：即使上次加载失败（世界还没建起来）也要能重试
            if (keyboard != null && keyboard.f5Key.wasPressedThisFrame)
            {
                LoadAll(keepWorldState: _world != null);
            }

            if (_world == null || _clock == null) return;

            HandleInput(keyboard);

            _clock.Advance(Time.deltaTime);

            // 脏格 → 重绘：数值变了的格子一定会被重画，没变的格子一个像素都不动
            if (_world.DirtyIndices.Count > 0) _mapRenderer.RepaintDirty();
            if (_clock.LastStepCount > 0) _mapRenderer.RepaintHeat();

            UpdateZone();
            UpdateHover();
            UpdateSmoke();
        }

        // ————————————————————————————— 装配 —————————————————————————————

        void LoadAll(bool keepWorldState)
        {
            var report = new DebugHud.GearReport();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (_source == null)
                {
                    _source = string.IsNullOrWhiteSpace(_dataDirectoryOverride)
                        ? DataLocator.CreateSource(out _dataDirectory)
                        : new DirectoryFileSource(_dataDirectoryOverride);
                    if (!string.IsNullOrWhiteSpace(_dataDirectoryOverride)) _dataDirectory = _dataDirectoryOverride;
                }

                DataBundle data = DataBundle.Load(_source);

                stopwatch.Stop();
                report.LoadSeconds = (float)stopwatch.Elapsed.TotalSeconds;
                report.Source = _dataDirectory;
                report.WarningCount = data.Warnings.Count;
                report.FirstWarning = data.Warnings.Count > 0 ? data.Warnings[0] : "";
                report.Note = keepWorldState ? "热重载：世界状态已保留（网格尺寸不变时）" : "";

                _data = data;
                if (_art == null) _art = ArtCatalog.TryLoad(ResolveArtDirectory());

                if (_isFirstLoad || !keepWorldState || _world == null)
                {
                    _world = new World(data);
                    _clock = new SimulationClock(_world, data.Config);
                    _clock.SetSpeed(data.Config.DefaultSpeed);
                    _inventory = new Inventory();
                    _inventory.GrantStartingItems(data.Items);
                    _player = new PlayerState();
                    _player.Spawn(_world, data.Config);
                }
                else
                {
                    // 热重载：数值状态保留，参数立刻生效（不重启）
                    _world.ApplyData(data);
                    _clock.ApplyConfig(data.Config);
                    _clock.SetSpeed(NearestSpeedLevel(_data.Config, _clock.Speed));
                    _cameraController.ApplyConfig(_data.Config);
                    _mapRenderer.RepaintAll();
                    if (!PlayerState.CanStand(_world, _player.X, _player.Y, data.Config)) _player.Spawn(_world, data.Config);
                }

                _farm = new FarmSystem(_world, data, _inventory, _player);
                _isFirstLoad = false;

                _mapRenderer.Bind(_world, _data, _art);
                _scenery.Bind(_world, _art);
                _cameraController.Initialize(_data.Config, _world.Width, _world.Height);
                _cameraController.Follow(_player);
                _playerView.Bind(_player, _data.Config, _art);
                _hud.Bind(_world, _clock, _data, report, _dataDirectory, _farm, _art);
                _hud.ReloadRequested = () => LoadAll(keepWorldState: true);
                _hud.ClearError();

                _lastZoneX = int.MinValue;
                UpdateZone();

                Debug.Log($"[SWAN] 数据加载完成：{_dataDirectory}（{report.LoadSeconds:0.00} 秒，警告 {report.WarningCount}）");
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                _hud?.ShowError(exception.Message);
                Debug.LogError($"[SWAN] 数据加载失败：\n{exception}");
            }
        }

        string ResolveArtDirectory()
        {
            if (string.IsNullOrWhiteSpace(_dataDirectoryOverride)) return DataLocator.ArtDirectory;
            string parent = Path.GetDirectoryName(_dataDirectoryOverride);
            return string.IsNullOrEmpty(parent) ? Path.Combine(_dataDirectoryOverride, "Art") : Path.Combine(parent, "Art");
        }

        void EnsureCamera()
        {
            _mapCamera = Camera.main;
            if (_mapCamera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                _mapCamera = cameraObject.AddComponent<Camera>();
            }

            _cameraController = _mapCamera.GetComponent<MapCameraController>();
            if (_cameraController == null) _cameraController = _mapCamera.gameObject.AddComponent<MapCameraController>();
        }

        void EnsureRenderer()
        {
            _mapRenderer = GetComponentInChildren<MapRenderer>();
            if (_mapRenderer == null)
            {
                var rendererObject = new GameObject("Map");
                rendererObject.transform.SetParent(transform, false);
                _mapRenderer = rendererObject.AddComponent<MapRenderer>();
            }
        }

        void EnsureHud()
        {
            _hud = GetComponent<DebugHud>();
            if (_hud == null) _hud = gameObject.AddComponent<DebugHud>();
        }

        void EnsureScenery()
        {
            _scenery = GetComponentInChildren<SceneryView>();
            if (_scenery == null)
            {
                var sceneryObject = new GameObject("Scenery");
                sceneryObject.transform.SetParent(transform, false);
                _scenery = sceneryObject.AddComponent<SceneryView>();
            }
        }

        void EnsurePlayerView()
        {
            _playerView = GetComponentInChildren<PlayerView>();
            if (_playerView == null)
            {
                var playerObject = new GameObject("Player");
                playerObject.transform.SetParent(transform, false);
                _playerView = playerObject.AddComponent<PlayerView>();
            }
        }

        static float NearestSpeedLevel(EngineConfig config, float speed)
        {
            float[] levels = config.SpeedLevels;
            if (levels == null || levels.Length == 0) return config.DefaultSpeed;

            float best = levels[0];
            float bestGap = Mathf.Abs(best - speed);
            for (int i = 1; i < levels.Length; i++)
            {
                float gap = Mathf.Abs(levels[i] - speed);
                if (gap < bestGap)
                {
                    best = levels[i];
                    bestGap = gap;
                }
            }
            return best;
        }

        // ————————————————————————————— 输入 —————————————————————————————

        void HandleInput(Keyboard keyboard)
        {
            if (keyboard == null) return;

            bool uiOpen = _hud != null && _hud.AnyPanelOpen;
            if (keyboard.escapeKey.wasPressedThisFrame) _hud?.ClosePanels();
            if (keyboard.f1Key.wasPressedThisFrame) _hud?.ToggleTutorial();
            if (keyboard.tabKey.wasPressedThisFrame) _hud?.ToggleInventory();
            if (keyboard.oKey.wasPressedThisFrame) _hud?.ToggleSettings();

            if (uiOpen) return;

            if (keyboard.spaceKey.wasPressedThisFrame) _clock.TogglePause();
            if (keyboard.nKey.wasPressedThisFrame) _clock.StepOnce();
            if (keyboard.leftBracketKey.wasPressedThisFrame) ChangeSpeed(-1);
            if (keyboard.rightBracketKey.wasPressedThisFrame) ChangeSpeed(1);

            HandleActionKeys(keyboard);
            if (keyboard.qKey.wasPressedThisFrame) CycleItem(-1);
            if (keyboard.eKey.wasPressedThisFrame) CycleItem(1);

            Vector2 move = ReadMove(keyboard);
            _player.Move(_world, move.x, move.y, Time.deltaTime, _data.Config);

            bool hover = TryGetHoverCell(out int x, out int y);
            if (hover && keyboard.fKey.wasPressedThisFrame) InteractAt(x, y);

            var mouse = Mouse.current;
            if (hover && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                bool overUi = _hud != null && _hud.PointerOverUi;
                Debug.Log($"[SWAN] 鼠标点击 ({x},{y}) 指针在UI={overUi}");
                if (!overUi) InteractAt(x, y);
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                _world.RegenerateMap();
                _mapRenderer.RepaintAll();
                _player.Spawn(_world, _data.Config);
                SetNote("地图已按当前配置重建");
            }

            float zoom = ReadZoom();
            _cameraController.Tick(Time.deltaTime, Vector2.zero, zoom);
        }

        void HandleActionKeys(Keyboard keyboard)
        {
            if (_farm == null) return;
            for (int i = 0; i < 8; i++)
            {
                var key = (Key)((int)Key.Digit1 + i);
                if (!keyboard[key].wasPressedThisFrame) continue;
                if (i >= _farm.Actions.Count) return;
                _farm.SelectAction(i);
                SetNote($"当前动作：{_farm.CurrentAction.Name}");
                return;
            }
        }

        void ChangeSpeed(int direction)
        {
            float[] levels = _data.Config.SpeedLevels;
            if (levels == null || levels.Length == 0) return;
            int index = 0;
            float best = Mathf.Abs(levels[0] - _clock.Speed);
            for (int i = 1; i < levels.Length; i++)
            {
                float gap = Mathf.Abs(levels[i] - _clock.Speed);
                if (gap < best)
                {
                    best = gap;
                    index = i;
                }
            }
            index = Mathf.Clamp(index + direction, 0, levels.Length - 1);
            _clock.SetSpeed(levels[index]);
            SetNote($"倍速 x{levels[index]:0.##}");
        }

        void CycleItem(int delta)
        {
            if (_farm == null || _farm.CurrentAction == null || !_farm.CurrentAction.SelectsItem) return;
            if (!_farm.CycleItem(delta)) return;
            var item = _farm.SelectedItem;
            if (item != null) SetNote($"选择：{item.Name}（背包 {_farm.SelectedItemCount}）");
        }

        static Vector2 ReadMove(Keyboard keyboard)
        {
            var move = Vector2.zero;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1f;
            return move;
        }

        static float ReadZoom()
        {
            var mouse = Mouse.current;
            if (mouse == null) return 0f;
            return mouse.scroll.ReadValue().y;
        }

        void InteractAt(int x, int y)
        {
            if (_farm == null) return;
            var result = _farm.Interact(x, y);
            if (result.Success)
            {
                SetNote(result.Message);
                if (result.ItemId != null && result.ItemCount > 0)
                {
                    _hud?.SetHover(HoverAt(x, y));
                }
            }
            else
            {
                SetNote(result.Message);
            }
        }

        void SetNote(string note)
        {
            if (_hud == null) return;
            _hud.SetNote(note);
            Debug.Log($"[SWAN] {note}");
        }

        // ————————————————————————————— 可交互范围 —————————————————————————————

        void UpdateZone()
        {
            if (_mapRenderer == null || _player == null || _data == null) return;
            var config = _data.Config;
            if (_player.CellX == _lastZoneX && _player.CellY == _lastZoneY
                && config.InteractionWidth == _lastZoneW && config.InteractionHeight == _lastZoneH) return;

            _lastZoneX = _player.CellX;
            _lastZoneY = _player.CellY;
            _lastZoneW = config.InteractionWidth;
            _lastZoneH = config.InteractionHeight;

            Color32 fill = Color.white;
            Color32 border = Color.white;
            if (GameColor.TryParse(config.ZoneColor, out var zone))
            {
                fill = new Color32(zone.r, zone.g, zone.b, (byte)Mathf.RoundToInt(Mathf.Clamp01(config.ZoneFillAlpha) * 255f));
                border = new Color32(zone.r, zone.g, zone.b, (byte)Mathf.RoundToInt(Mathf.Clamp01(config.ZoneBorderAlpha) * 255f));
            }
            _mapRenderer.ShowZone(_player.CellX, _player.CellY, config.InteractionWidth, config.InteractionHeight, fill, border);
        }

        // ————————————————————————————— 悬停 —————————————————————————————

        void UpdateHover()
        {
            if (_mapCamera == null) return;

            if (!TryGetHoverCell(out int x, out int y))
            {
                _hover = default;
                _hover.InMap = false;
                _hud.SetHover(_hover);
                _mapRenderer.HideCursor();
                return;
            }

            _hover = HoverAt(x, y);
            _hud.SetHover(_hover);

            bool inRange = _farm == null || _farm.InRange(x, y);
            Color32? cursorColor = null;
            if (!inRange && GameColor.TryParse(_data.Config.ZoneOutOfRangeColor, out var outColor)) cursorColor = outColor;
            _mapRenderer.SetCursor(x, y, CursorPulseAlpha(), cursorColor);
        }

        DebugHud.HoverCell HoverAt(int x, int y)
        {
            var hover = new DebugHud.HoverCell
            {
                InMap = true,
                X = x,
                Y = y,
                Cell = _world.GetCell(x, y)
            };
            return hover;
        }

        /// <summary>光标呼吸只是「看得见」的提示：按真实时间走，暂停时照样呼吸。</summary>
        float CursorPulseAlpha()
        {
            var config = _data.Config;
            float hz = config.CursorPulseHz;
            if (hz <= 0f) return 1f;
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * hz * Mathf.PI * 2f);
            return Mathf.Lerp(config.CursorPulseAlphaMin, 1f, wave);
        }

        bool TryGetHoverCell(out int x, out int y)
        {
            x = 0;
            y = 0;
            if (_world == null) return false;

            if (_hasSmokeCursor)
            {
                x = _smokeCursorX;
                y = _smokeCursorY;
                return _world.Contains(x, y);
            }

            var mouse = Mouse.current;
            if (mouse == null || _mapCamera == null) return false;

            Vector2 screen = mouse.position.ReadValue();
            Vector3 world = _mapCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_mapCamera.transform.position.z));
            x = Mathf.FloorToInt(world.x);
            y = Mathf.FloorToInt(world.y);
            return _world.Contains(x, y);
        }
    }
}
