using System;
using System.Diagnostics;
using System.Globalization;
using Swan.Core;
using Swan.Core.Data;
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
    /// 1. 把数据、世界、时钟、渲染、HUD 接起来；
    /// 2. 把键鼠输入翻译成对世界/时钟/相机的方法调用；
    /// 3. 每帧驱动时钟并让渲染跟上脏格。
    ///
    /// 它自己不写任何玩法规则：种/收/热源/重判/温度全在 Core 里，
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

        World _world;
        SimulationClock _clock;
        DataBundle _data;
        IFileSource _source;
        string _dataDirectory = "";

        MapRenderer _mapRenderer;
        Camera _mapCamera;
        MapCameraController _cameraController;
        DebugHud _hud;

        bool _isFirstLoad = true;
        DebugHud.HoverCell _hover;

        void Awake()
        {
            // 数值骨架版没有音效和动画，后台也让它跑（x60 观察用）
            Application.runInBackground = true;

            EnsureCamera();
            EnsureHud();
            EnsureRenderer();
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

            // 无人值守截图时鼠标可能不在窗口里：允许钉住光标格，好让证据图里也能看到那个黄框
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

            Debug.Log($"[SWAN] 冒烟运行 {_smokeSeconds:0.#} 秒后截图：{_shotPath}（倍速 x{speed:0.##}）");
        }

        void UpdateSmoke()
        {
            if (_smokeSeconds <= 0f) return;

            _smokeElapsed += Time.unscaledDeltaTime;
            if (_smokeElapsed < _smokeSeconds) return;

            if (!_shotTaken)
            {
                _shotTaken = true;
                ScreenCapture.CaptureScreenshot(_shotPath);
                Debug.Log($"[SWAN] 冒烟截图：{_shotPath}");
                return;
            }

            // 给截图一帧时间落盘，再退出
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

                if (_isFirstLoad || !keepWorldState || _world == null)
                {
                    _world = new World(data);
                    _clock = new SimulationClock(_world, data.Config);
                    _clock.SetSpeed(data.Config.DefaultSpeed);
                }
                else
                {
                    // 热重载：数值状态保留，参数立刻生效（不重启）
                    _world.ApplyData(data);
                    _clock.ApplyConfig(data.Config);
                    _clock.SetSpeed(NearestSpeedLevel(_data.Config, _clock.Speed));
                    _cameraController.ApplyConfig(_data.Config);
                    _mapRenderer.RepaintAll();
                }

                _isFirstLoad = false;

                _mapRenderer.Bind(_world, _data);
                _cameraController.Initialize(_data.Config, _world.Width, _world.Height);
                _hud.Bind(_world, _clock, _data, report, _dataDirectory);
                _hud.ClearError();

                Debug.Log($"[SWAN] 数据加载完成：{_dataDirectory}（{report.LoadSeconds:0.00} 秒，警告 {report.WarningCount}）");
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                _hud?.ShowError(exception.Message);
                Debug.LogError($"[SWAN] 数据加载失败：\n{exception}");
            }
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

            if (keyboard.spaceKey.wasPressedThisFrame) _clock.TogglePause();
            if (keyboard.nKey.wasPressedThisFrame) _clock.StepOnce();

            HandleSpeedKeys(keyboard);

            if (TryGetHoverCell(out int x, out int y))
            {
                if (keyboard.fKey.wasPressedThisFrame) PlantOrHarvest(x, y);
                if (keyboard.tKey.wasPressedThisFrame)
                {
                    if (_world.ToggleHeatSource(x, y, out bool nowOn))
                    {
                        SetNote(nowOn ? $"加热源 @({x},{y})" : $"移除热源 @({x},{y})");
                    }
                }
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                _world.RegenerateMap();
                _mapRenderer.RepaintAll();
                SetNote("地图已按当前配置重建");
            }

            Vector2 pan = ReadPan(keyboard);
            float zoom = ReadZoom();
            _cameraController.Tick(Time.deltaTime, pan, zoom);
        }

        void HandleSpeedKeys(Keyboard keyboard)
        {
            float[] levels = _data.Config.SpeedLevels;
            for (int i = 0; i < levels.Length && i < 9; i++)
            {
                if (!keyboard[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) continue;
                _clock.SetSpeed(levels[i]);
                SetNote($"倍速 x{levels[i]:0.##}");
            }
        }

        static Vector2 ReadPan(Keyboard keyboard)
        {
            var pan = Vector2.zero;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) pan.x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) pan.x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) pan.y -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) pan.y += 1f;
            return pan;
        }

        static float ReadZoom()
        {
            var mouse = Mouse.current;
            if (mouse == null) return 0f;
            return mouse.scroll.ReadValue().y;
        }

        void PlantOrHarvest(int x, int y)
        {
            Cell cell = _world.GetCell(x, y);
            if (cell.HasPlant)
            {
                _world.TryHarvest(x, y, out float _, out string harvestMessage);
                SetNote(harvestMessage);
                return;
            }

            _world.TryPlantDefault(x, y, out string plantMessage);
            SetNote(plantMessage);
        }

        void SetNote(string note)
        {
            if (_hud == null) return;
            _hud.SetNote(note);
            Debug.Log($"[SWAN] {note}");
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

            _hover.InMap = true;
            _hover.X = x;
            _hover.Y = y;
            _hover.Cell = _world.GetCell(x, y);
            _hud.SetHover(_hover);
            // 玩家位置提示：把「你现在控制的格」标出来（颜色/边宽/呼吸频率都在 config 里）
            _mapRenderer.SetCursor(x, y, CursorPulseAlpha());
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
