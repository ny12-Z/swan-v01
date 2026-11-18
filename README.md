# SWAN v0.1 · 数值骨架 / 仪器版（Unity 实现）

一份可运行、可验证、可持续修的 v0.1 实现：50×50 格子地自己在走时间，土地会按表里的规则自己变，
你种一株 P01 草本，它长多久全由土地算出来。

- 引擎：**Unity 6000.6.9f1**（交接包写的是 6000.6.6f1，本机只装了 6000.6.9f1；URP 2D 模板 `urp-blank-17.2.6`）
- 输入：Input System（`activeInputHandler = 1`）
- 唯一数值真相：`Assets/StreamingAssets/Data/` 下的 4 张机读 CSV + `config.json` + `map-preset.json`
- 规则唯一验收标准：交接包 `docs/p01-对照曲线.md`（B1/B2 两条曲线）
  - 已裁定（2026-11-18）：**停长保留 + G02 对 P01 叠乘**。因此水/养分/地形三列与文档逐点一致，
    **生长列是文档的 2 倍**（1.11 → 2.22/步）。要回到文档原始生长口径：`config.json` 里
    `stackInitialGrowthTraits` 改成 `false`，并把 `CurveTests` 的生长列 ÷2。

---

## 1. 怎么跑起来

### 在编辑器里

1. 用 Unity Hub 打开本工程（`6000.6.9f1`）
2. 菜单 **SWAN → 1 生成主场景 Main.unity**（场景是生成物，不是手拖的黑盒；重跑一次就能还原）
3. 打开 `Assets/Scenes/Main.unity`，按 Play

> **打开工程会自动进主场景。** 如果上次关编辑器时停在空白未命名场景（batchmode 生成场景、
> Library 重建之后都会这样），`Assets/Scripts/Editor/SceneAutoOpen.cs` 会在编辑器加载后把
> `Assets/Scenes/Main.unity` 打开一次；你已经打开着别的保存过的场景时它不插手。
> 想手动来一遍：菜单 **SWAN → 0 打开主场景 Main.unity**。

### 直接跑打包好的程序

```
Builds\SwanV01.exe
```

打包：菜单 **SWAN → 2 打包 Windows**，或命令行见 §6。

### 操作

| 按键 | 作用 |
|---|---|
| 空格 | 暂停 / 继续 |
| N | 单步（恰好 1 个环境步，暂停状态下也能走） |
| 1 / 2 / 3 | 倍速 `config.speedLevels`（默认 1 / 10 / 60，档位与按键数量都由 config 决定） |
| F | 悬停格：有植物就收获，没植物就种下 P01 |
| T | 悬停格：开 / 关调试热源 |
| F5 | 热重载 `config.json` + 4 张表 + `map-preset.json`（保留世界状态，不重启） |
| R | 按 `map-preset.json` 重建地图 |
| WASD / 方向键 | 平移相机 |
| 滚轮 | 缩放（`cameraZoomStep` 每格乘性缩放，夹在 `zoomMin..zoomMax`） |
| F1 | 显示 / 隐藏 HUD |

**你现在在哪：** 地图上那圈**会呼吸的黄色高亮框**就是你正在控制的格（`config.cursorColor` / `cursorLineThickness` / `cursorPulseHz` / `cursorPulseAlphaMin`）。它跟着鼠标走，右上角 HUD 同时显示「光标格 (x,y)」的明细；鼠标移出地图时黄框消失、HUD 显示「光标：地图外」。

HUD 四块：时钟与全局统计 / 数据概况（来源、加载耗时、警告数）/ 光标格明细 / 按键提示 + 错误框。
加载失败时错误框是红底的，且按 F5 可以直接重试（世界为 null 也能重载）。

---

## 2. 改数值，不改代码

| 想改什么 | 改哪里 | 改完怎么生效 |
|---|---|---|
| 地形扣减、地形颜色、25 宫格分档 | `数值表(地形属性表)-机读.csv` | 游戏里按 F5 |
| 植物生长速度、产量、门槛 | `数值表(植物类型表)-机读.csv` | F5 |
| 性状数值（G01–G11） | `数值表(植物性状表)-机读.csv` | F5 |
| 法则（触发条件、阈值、效果） | `数值表(世界法则表)-机读.csv` | F5 |
| 时钟、门槛、温度、相机、HUD、颜色 | `config.json` | F5 |
| 初始地图（区域水/养分、热源、植物） | `map-preset.json` | F5 或 R |

**热重载保留世界**：植物按 `typeId` 名字重新映射，表里行序变了也不会串种；网格尺寸变了才重建地图。

数据加载自检（交接包 §10.5）已写成测试，见 `Assets/Tests/EditMode/HotReloadTests.cs`。

---

## 3. 目录与分层（谁可以引用谁）

```
Assets/
  Scripts/
    Core/            Swan.Core        —— 纯数值层，noEngineReferences = true（不许 using UnityEngine）
      Json.cs                       自己写的 JSON 解析（带 行/列 报错）
      Data/                         CSV 解析 + 4 张表 + config + map-preset + 交叉校验
      Sim/                          World（双缓冲，环境步 7 步流水）、SimulationClock、CurveProbe、Rng
    Game/            Swan.Game        —— 表现与胶水层（只读 Core，不写玩法规则）
      SwanBootstrap.cs              装配 + 输入 + 热重载 + 冒烟模式
      Rendering/                    MapRenderer（三层纹理）、CellPainter（像素绘制）、MapPalette
      MapCameraController.cs        相机平移/缩放/边界
      DebugHud.cs                   IMGUI 仪表盘（不碰 uGUI 资产）
      CommandLine.cs                只解析命令行字符串，玩法参数一律走 config
    Editor/          Swan.Editor      —— 场景生成 + 打包 + 证据导出（仅编辑器）
  Tests/EditMode/    Swan.Tests.EditMode —— 49 个测试
  StreamingAssets/Data/              唯一数值真相（4 张 CSV + 2 个 JSON）
Docs/                               验收证据（曲线表、截图）
tools/                              命令行脚本（跑测试 / 打包 / 冒烟）
```

依赖方向：`Game → Core`、`Editor → Game + Core`、`Tests → Core + Game`。**Core 反向引用 Game 会被 asmdef 直接拒绝编译。**

写新玩法时的三条硬规矩（也是评审的打回标准）：

1. **代码里不许出现业务数值**（20 / 95 / 0.5 之类）。要么从表读，要么从 `config.json` 读。
2. **不许 `if (terrainId == 3)` 这种地形特判**。地形的一切行为来自地形表的两列扣减 + 法则表。
3. **环境结算不许写在 `Update()` 里**。一切走 `SimulationClock → World.StepEnvironment()`，`Update` 只负责把 deltaTime 喂进去。

---

## 4. 核心机制速查

- **双时钟**：视觉 tick = `tickRate`（默认 4/s，永远按真实时间走，暂停也走）；环境步 = `envStepSeconds`（默认 20 s，按倍速推进）。两者**不合并**。
- **单步**：`N` 立刻推进**恰好 1 个环境步**，不依赖帧率，也不受 `maxEnvStepsPerFrame` 影响。
- **环境步 7 步流水**（`World.StepEnvironment`，全程双缓冲，读旧值写新值）：
  1. 植物吸水（`plantIntakeBase` × 性状乘子）+ 固氮法则（L04）
  2. 地形扣减（`dWaterPerStep` / `dNutrientPerStep`）
  3. 生长（门槛判定 + `growthPerStep` × 生长乘子）、烧伤（L03）
  4. 水溢出（`water > waterOverflowThreshold` → 4 邻最低，`transfer = (water-neighbor) × overflowRatio`）
  5. 温度（扩散 `tempSpread` + 回归 `tempReturn` + 热源 `heatSourceBoost`）
  6. 地形重判（25 宫格 + `terrainHysteresis` 防抖）
  7. 收获标记 + 全局统计
- **温度是真场**：每格一个浮点数，起始 `baseTemperature`，不参与地形命名。
- **生长门槛**：`max(config.growWaterMin, 植物表 requiredWaterMin)`，养分同理（见 DECISIONS D6）。

---

## 5. 验证（自测证据）

| 证据 | 内容 | 位置 |
|---|---|---|
| 单元测试 | 49 个 EditMode 测试：数据加载/校验、B1+B2 逐点曲线、双时钟、地形重判、植物闭环、热源、热重载、光标框渲染、打开工程自动进主场景 | `Logs/editmode-results.xml` |
| 对照曲线 | B1 / B2 / B2(防抖开) 三张表（水/养分/地形与文档逐点一致，生长列按裁定 ×2） | `Docs/curve-probe.txt` |
| 地图演化截图 | 0 / 30 / 120 步 + 热源叠加（编辑器里直接合成像素，不依赖 GPU） | `Docs/screenshots/01..04-*.png` |
| 真实运行截图 | 打包后的 exe 自动截图退出：6 秒版 / 60 步版（108 个环境步后地图旱化，中心生命之泉上的 P01 已「可收 1」） | `Docs/screenshots/11-player-smoke.png`、`12-player-smoke.png` |
| 玩家位置提示 | 黄框钉在 (25,25)：像素抽样确认 733 个光标黄像素，bbox 40×40 px = 正好一格（zoom 1） | `Docs/screenshots/15-player-cursor.png` |
| 事故留档 | 材质缺失导致的整屏洋红，保留下来做修复前后对照 | `Docs/screenshots/10-player-smoke.png` |
| 脚本冒烟 | `tools\smoke.ps1` 一条命令跑完「启动→跑 6 秒→截图→退出」 | `Docs/screenshots/13-script-smoke.png` |

跑一遍全部验证：

```
powershell -ExecutionPolicy Bypass -File tools\run-tests.ps1    # 49 个测试（约 40 秒）
powershell -ExecutionPolicy Bypass -File tools\build.ps1        # 打包到 Builds\SwanV01.exe
powershell -ExecutionPolicy Bypass -File tools\smoke.ps1        # 启动真实程序 + 自动截图退出
```

> 注：脚本刻意写成纯 ASCII——本机的 PowerShell 会把 UTF-8 源码按旧代码页解码，中文会变乱码。
> 若你的机器 `pwsh` 在 PATH 里，把上面的 `powershell` 换成 `pwsh` 也一样。

命令行（batchmode）等价命令：

```
# 测试（注意：-runTests 绝不能和 -quit 同用，否则 Unity 在跑测试前就退出）
Unity.exe -batchmode -projectPath <工程> -runTests -testPlatform EditMode ^
          -testResults Logs\editmode-results.xml -logFile Logs\tests.log

# 生成场景 / 导出证据 / 打包
Unity.exe -batchmode -quit -projectPath <工程> -executeMethod Swan.EditorTools.SceneBuilder.BuildMainScene
Unity.exe -batchmode -quit -projectPath <工程> -executeMethod Swan.EditorTools.EvidenceTools.ExportCurveProbe
Unity.exe -batchmode -quit -projectPath <工程> -executeMethod Swan.EditorTools.EvidenceTools.ExportMapSnapshots
Unity.exe -batchmode -quit -projectPath <工程> -executeMethod Swan.EditorTools.SceneBuilder.BuildWindows
```

### 程序自带的命令行参数（只给自动化用）

| 参数 | 含义 |
|---|---|
| `-swanSmokeSeconds <秒>` | 跑够这么多秒就截图并退出（无人值守冒烟） |
| `-swanShot <png>` | 截图路径 |
| `-swanSpeed <倍速>` | 起始倍速 |
| `-swanDataDir <目录>` | 换一套数据目录（不用 StreamingAssets） |
| `-swanCursorCell "x,y"` | 钉住光标格（无人值守截图时鼠标不在窗口里，也要能拍到黄框证据） |

---

## 6. 已知情况（不影响 v0.1 验收）

见 `Docs/CONFLICTS.md`（6 条待裁定，#1/#2 已裁定）与 `Docs/DECISIONS.md`（17 条实现决策及理由）。

一句话版：P01 在这张表下**长不到 100**（交接包已预期，不是 bug）；B2 曲线步 19–23 的地形名
按表算出来是「普通土地」，文档写的是「草原」（数字全部一致，只有名字不同）。

**批量模式启动即退出（`return code 1`，日志只到「Successfully changed project path」）**：
本机曾出现残留的 `Unity.Licensing.Client` 占着 `LicenseClient-MR` 管道。
先 `Stop-Process -Name Unity.Licensing.Client -Force`（连带 Unity Hub），再跑 `tools\run-tests.ps1` / `tools\build.ps1` 即可。

---

## 7. 交接包任务对照（T0–T8）

| 任务 | 要求 | 落在哪 | 怎么验 |
|---|---|---|---|
| T0 | 工程初始化、URP 2D、50×50、1 格 16px、相机可平移 | `SceneBuilder`、`MapRenderer`、`MapCameraController` | 打开场景按 Play，WASD 平移、滚轮缩放 |
| T1 | 读 4 张机读 CSV，主键/外键校验，报错到「文件名+行号+列名」 | `Core/Data/CsvDocument.cs`、`TerrainTable/TraitTable/PlantTypeTable/RuleTable` | `DataLoadingTests` 14 个测试 |
| T2 | `config.json` + 热重载（不重启生效） | `ConfigLoader`、`SwanBootstrap.LoadAll` | `HotReloadTests`；游戏里 F5 |
| T3 | 双时钟 + 暂停/单步/x1·x10·x60 + 悬停 HUD | `SimulationClock`、`DebugHud` | `ClockTests` 10 个测试；HUD 读数 |
| T4 | 环境步 7 步流水（双缓冲） | `World.StepEnvironment` | `CurveTests`（B1/B2 逐点） |
| T5 | 地形重判（25 宫格 + deadband）+ 脏格重绘 | `World.StepTerrainReevaluation`、`MapRenderer.RepaintDirty` | `TerrainAndPlantTests`；`Docs/screenshots/02..03` |
| T6 | 温度场 + 调试热源（T 键）+ 红色叠加 | `World.StepTemperature`、`CellPainter.HeatPixel` | `TerrainAndPlantTests` 热源两条；`04-heat-source.png` |
| T7 | P01 种/长/收（F 键，`baseYield + ΣyieldAdd`） | `World.TryPlant*/TryHarvest` | `保持水分就能长熟并收获`；真实运行可收到「可收 1」 |
| T8 | 全局统计 HUD | `World.RefreshStats`、`DebugHud` | HUD 第一块面板 |

## 8. 这份实现为什么「可维护 / 可修」

- **数值全在外部**：4 张 CSV + config 是唯一真相，代码只读配置；热重载改完立刻看到效果。
- **错误信息指到行**：CSV/JSON 报错一律带「文件名 + 行号 + 列名」，例如
  `数值表(地形属性表)-机读.csv 第 8 行 · 列 terrainId: 主键 7 重复了`。
- **双真相防呆**：`config.waterOverflowThreshold` 与法则 L01 阈值、`fireTempThreshold` 与 L03 阈值不一致时**启动即报错**，不让你悄悄改一处。
- **场景是生成物**：`SceneBuilder` 一条命令重建，不是手拖的不可复现资产。
- **49 个测试当护栏**：改数值/改逻辑后一条命令就能知道有没有踩到曲线。
