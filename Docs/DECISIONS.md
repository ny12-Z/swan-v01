# 实现决策记录（DECISIONS）

只记录**交接包没写死、必须做一个选择**的地方，以及为什么这么选。
每条都写了「想换回去要改哪里」，方便后面修。

---

## D1 · G02 对 P01 **叠乘**（`stackInitialGrowthTraits = true`）+ 停长保留

- **决策**（创意总监 2026-11-18 裁定：「停长，叠乘」）：
  - **叠乘**：P01 每步生长 = `growthPerStep × G02.value` = 1.11 × 2 = **2.22**。
  - **停长**：水跌破 `growWaterMin` 就停止生长，**不改表**去让它长熟；ripe=100 在这套数值下达不到。
- **早期实现曾取「不叠乘」**：当时理由是对照曲线按 1.11/步手算。裁定后口径变为：
  - 水 / 养分 / 地形三列**与 `p01-对照曲线.md` 逐点一致**（生长值不回流到水、养分）；
  - 生长列 = 文档的 **2 倍**（B1 步7 由 6.66 → 13.32；B2 步24 由 25.53 → 51.06）。
- **想换回文档原始口径**：`config.json` → `"stackInitialGrowthTraits": false`，P01 回到每步 1.11，
  此时 `CurveTests` 的生长列需同步除以 2（测试里写死了每一拍）。
- **测试**：`CurveTests`（B1/B2 逐拍，生长列 2 倍）、`HotReloadTests.自检三_G02叠乘开关决定了速生是否存在`
  （默认 2.22；G02 改 1 → 1.11；关掉开关 → 1.11，两条路径独立）。

## D2 · 引擎/表现参数也进 `config.json`

- **决策**：网格 50×50、一格 16 像素、缩放范围、水/养分上下限、地图随机种子、相机速度、HUD 字号与颜色……全部写成 config 字段。
- **理由**：打回标准里有「代码里出现 20/95/0.5 这类业务数字」。把 50 和 16 硬编码同样会让「换一张 80×80、24px 的图」变成改代码。
- **代价**：`ConfigLoader` 要求字段**显式写全**（缺一个就报错「config.json 必须显式写全，代码里不留默认数值」），key 名和字段名必须同步。

## D3 · 双真相防呆：同一个参数有两个来源就启动报错

- **决策**：加载完表后交叉校验：`config.waterOverflowThreshold` vs 法则 L01 的阈值、`config.overflowRatio` vs L01 的效果值、`config.fireTempThreshold` vs L03 阈值、G10 的 `autoIgniteTemp` vs L03 阈值。不一致抛 `DataLoadException`，消息里点名两个文件。
- **理由**：交接包自己就标了「L01 阈值 100→95」和「config.waterOverflowThreshold=95」两处重复。改一处忘另一处，会让「曲线对得上但游戏里不溢出」这种鬼故事发生。
- **想放宽**：删 `DataBundle.ValidateCrossReferences()` 里对应的 Check；测试 `DataLoadingTests.同一个参数两个来源时必须报错` 会失败，提醒你确实放宽了。

## D4 · 25 宫格的「末档闭区间」两轴都用

- **决策**：命中式 `value >= min && (value < max || max >= 100)`；水轴用、养分轴也用。
- **理由**：§10.3 只写了水轴，但养分 100 同样会掉出 `[80,100)`，会「查不到地形」。两轴必须同一套规则，否则 100/100 的生命之泉没有地形。
- **测试**：`DataLoadingTests.二十五宫格每一格都能查到且不重号` + `边界值归属上一档`（20/40/60/80）。

## D5 · 地图初始值走 `config` 基准 + `map-preset.json` 区域

- **决策**：先把每格填成 `mapBaselineWater/mapBaselineNutrient`（30/30）加 `±mapJitter`（种子固定），再按 `map-preset.json` 的圆形区域覆盖，最后按数值重判地形。
- **理由**：交接包表 5 #3 把「起始地形名」悬置了。用「数值→地形」单向推导，就不会出现「名字对了数值不对」的二义性；`mapSeed` 固定意味着**同一个 bug 可以复现**。
- **想换**：只改 `map-preset.json`（区域中心/半径/水/养分），按 F5 或 R 立刻重建。

## D6 · 生长门槛取「表里每型门槛」与「config 全局下限」中更严的一个

- **决策**：`门槛水 = max(config.growWaterMin, 植物表 requiredWaterMin)`，养分同理；温度为 `[growTempMin, growTempMax]`。
- **理由**：两处都写了门槛（config 15/20，植物表 15/20），而 v0.1 只有 P01、两者恰好相等，看不出谁说话。取更严者 = 任何一处调高都会真实生效，不会出现「改了植物表没反应」的假配置。
- **注意**：这是唯一一处「取更严」而不是「表覆盖 config」的地方，写在这里就是为了以后不反复。

## D7 · 精灵材质必须序列化进场景，不能留 `null`

- **决策**：`MapRenderer` 暴露 `[SerializeField] Material _spriteMaterial`，`SceneBuilder` 生成 `Assets/Materials/MapSprite.mat`（URP `Sprite-Unlit-Default`，回退 `Sprites/Default`）并写进场景；打包时再把两个着色器加进 `Always Included Shaders`。
- **理由**：第一版留空 `sharedMaterial = null`，真实 exe 跑出来是**整屏洋红**（Unity 的错误着色器色）。另外 `Shader.Find` 在打包后可能因为没被任何资源引用而被剔除，所以「场景引用材质」+「Always Included」两道保险都要有。
- **证据**：`Docs/screenshots/10-player-smoke.png`（洋红，坏）vs `11-player-smoke.png`（正常，修好后）。

## D8 · HUD 用 IMGUI，不建 uGUI 资产

- **决策**：`DebugHud` 用 `OnGUI` + `GUI.color` 面板 + 系统字体（微软雅黑 → 黑体 → Noto Sans CJK）。
- **理由**：仪器版要的是「一眼看到所有数字」而不是可点 UI；IMGUI 不产生 .prefab/.asset 资产，也不会因为 UGUI 版本/Cavas 配置把仪表盘弄丢。中文字形靠 `Font.CreateDynamicFontFromOSFont` 拿系统字体，缺字体时退回默认字体而不崩。
- **代价**：IMGUI 字体没有 emoji 字形，标记一律用文字（`[热源]` `[可收]` `[警告]`）。

## D9 · 格子里的植物用「1 起表下标」，热重载按 typeId 名字重映射

- **决策**：`Cell.PlantTypeId` 是 0 = 无植物，否则是植物表里的 1 起下标；`ApplyData` 时先按旧 id 取到 `typeId` 字符串，再到新表里查下标。
- **理由**：热重载时表行序可能变（新增一行植物就全错位），用下标直接存会「串种」。名字是稳定的业务主键，下标只是运行时加速用的。
- **测试**：`HotReloadTests.热重载不会把植物弄丢或串种`。

## D10 · 脏标记（dirty）放在 `World` 的并行数组里，不放 `Cell`

- **决策**：`bool[] _dirty` + `List<int> _dirtyList` 由 `World` 持有并在交换缓冲时对比得出；`Cell` 结构体只有数值。
- **理由**：`dirty` 是「表现层要不要重画」的信息，塞进数值结构会让存档/序列化/测试都被污染，而且双缓冲时旧格和新格的 dirty 谁说了算很含糊。
- **代价**：多一个并行数组，需要保证任何写格子的路径都经过 `World`（目前全部如此）。

## D11 · L04 固氮 / L05 防风由「法则行」执行，性状表只给语义

- **决策**：G05（`soilNutrientPerStep`）与 L04（`plantTrait G05 → nutrientDelta`）不各加一次；`World` 执行 L04 的 `nutrientDelta`。L05 的 `evaporationMul` 同理，按半径连乘到蒸发系数。
- **理由**：表里同一件事有两个表述，各执行一次就会变成 +2。CSV 的 note 也写了「G07 本身不生效」。所以：**性状表描述能力，法则表描述结算**。
- **测试缺口（如实记录）**：v0.1 能种的 P01 只带 G02，不带 G05/G07，所以这两条法则在游戏里没有可触发的对象；
  目前只有 `World.NutrientDeltaFor` / `ComputeEvaporationMultipliers` 的实现，**没有单元测试覆盖**。
  等 v0.2 放开带 G05/G07 的植物时，第一件事就是补这两条测试。

## D12 · 场景是生成物（`SceneBuilder`），不手拖

- **决策**：`SWAN → 1 生成主场景` 从零建 `Main Camera` + `Swan` + `Map`（含材质引用）并存盘、写 Build Settings。
- **理由**：手拖场景在 git 里是不可读的 YAML 差异，新人也没法「一键还原」。生成器里改一行，重跑一次，场景必一致。

## D13 · `v01Show = 0` 的地形照样按表内 `v01Color` 渲染

- **决策**：地形 id 22–25（活水/富营养湖/黑水湖/生命之泉）的 `v01Show = 0`，颜色却都写 `#666666`；渲染**照表画灰**，不偷偷换颜色。
- **理由**：颜色也是数值表的一部分，改它就是改设计。截图里地图中心那 5 格灰色十字就是「生命之泉」，读 HUD 能看到名字——这正是仪器版该有的样子。想让它们好看，请在表里给颜色。

## D14 · 温度步读旧缓冲（扩散比想象中慢一步）

- **决策**：`StepTemperature` 从 `_cells`（上一帧结果）取邻格温度，把新值写进 `_next`。
- **理由**：双缓冲的语义就是「整步同时更新」。副作用是热源格自己要过 1 步才会被别人看见，所以「邻格变热」需要 2 步。测试里如实写了两步，并把原因写在注释里，避免后人误以为是 bug。

## D15 · 时钟：切倍速不补跑，追不上就清欠账

- **决策**：`SimulationClock.SetSpeed` 把累计器夹到不超过一个环境步；单帧步数超过 `maxEnvStepsPerFrame` 时清掉欠账（不跨帧雪崩）；`Advance` 返回本帧实际跑了几步。
- **理由**：从 x1 切到 x60 的瞬间若不清累计器，会立刻补跑一大串；掉帧时若保留欠账，会越欠越多、最后卡死。两者都会让「同一次运行、不同机器」跑出不一样的曲线。

## D16 · 冒烟/自动化入口单独一层 `CommandLine`，不碰玩法参数

- **决策**：`-swanSmokeSeconds` / `-swanShot` / `-swanSpeed` / `-swanDataDir` 只由 `CommandLine` 解析，玩法参数一律来自 `config.json`。
- **理由**：无人值守截图需要「跑 N 秒→截图→退出」，但绝不能因为加自动化就多一条改数值的暗门。

## D17 · 「玩家在哪」= 悬停格光标框，且全部参数进 config

- **背景**：创意总监指出「无法分辨玩家位置」。v0.1 没有角色实体，玩家唯一能操作的坐标就是**悬停格**（F 种/收、T 热源都作用在它上面），但画面上没有任何标记，鼠标一旦离开窗口就完全失去参照。
- **决策**：在 `MapRenderer` 里加第 4 个精灵层 `Cursor`（sortingOrder 3），把当前悬停格画成一圈**会呼吸的黄色描边 + 中心小十字**；参数 `cursorColor` / `cursorLineThickness`（格内像素）/ `cursorPulseHz` / `cursorPulseAlphaMin` 全在 `config.json`，改完 F5 即刻生效。呼吸按 `Time.unscaledTime` 走，暂停时照样呼吸（它是「看得见」的东西，不是模拟的一部分）。
- **理由**：不引入「玩家实体」这种设计改动（交接包禁止提前做角色/建造），只把已有交互坐标可视化；颜色/粗细/呼吸都可由创意总监调，代码里没有写死任何一个像素值。
- **顺带**：HUD 面板标题改为「光标格 (x,y)」，鼠标出界时显示「光标：地图外」，底部提示加「黄色框 = 你现在控制的格」；`-swanCursorCell "x,y"` 只给无人值守截图用（鼠标不可控时钉住光标格拍证据）。

## D18 · 打开工程自动进主场景：用编辑器钩子，不改 Library

- **背景**：用 batchmode 生成场景/打包之后，或者在空白未命名场景里关掉编辑器，`Library/LastSceneManagerSetup.txt` 会变成 `sceneSetups: []`；下次打开工程 Unity 就只给一个空白场景，看起来像工程坏了。
- **决策**：`Assets/Scripts/Editor/SceneAutoOpen.cs` 用 `[InitializeOnLoad]` + `EditorApplication.delayCall`，在编辑器加载后判断「当前活动场景是不是没保存过的空白场景」，是则打开 `Assets/Scenes/Main.unity`。判定抽成纯函数 `ShouldAutoOpen(activeScenePath, mainSceneExists)` 以便单测；三条保护=批量模式不动场景、一个编辑器会话只尝试一次（`SessionState`）、用户已打开别的保存过的场景就不插手。另加菜单 **SWAN → 0 打开主场景** 供手动恢复。
- **理由**：直接手写 `Library/LastSceneManagerSetup.txt` 能救一次，但 Library 是可重建的临时目录、换机器/清库就失效；勾子写在 `Assets` 里，跟着版本走，谁拉下来打开都自动进场景。不把「打开哪个场景」做成玩法参数，所以不进 `config.json`。
- **验证**：`SceneAutoOpenTests`（5 条）覆盖判定逻辑 + 主场景存在 + 主场景在 Build Settings 里启用。

---

1. **里程碑**：交接包要求做完 M1（T0–T2）就停下等确认。本次按创意总监的口头指示（「尝试制作一个游戏的初版 demo，其余你自行发挥」）一次做到 T8/M3，并在 `Docs/CONFLICTS.md` 里列出需要裁定的设计问题。
2. **引擎版本**：交接包写 Unity `6000.6.6f1`，本机只装了 `6000.6.9f1`，用它完成。首次导入时 Unity 自动把 `test-framework 1.6.0→1.8.0`、`inputsystem 1.20.0→1.20.1` 等升级到补丁版。
3. **新增依赖**：只加了 Unity 官方 `com.unity.test-framework`（测试框架）。没有引入任何第三方库，CSV/JSON 解析都是自己写的。
