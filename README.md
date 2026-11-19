# SWAN · 土地作者（可玩原型 v0.2）

一只猫、一片会自己演化的土地。你站在画面正中间，用 **WASD / 方向键**走动，
用鼠标点击身边 **12×12** 范围内的地块：种植、收获、挖掘、放置、采集、浇水、施肥、点热源。
土地的水分、养分、温度会随时间和你的动作变化，地形会按 25 宫格规则自己重判。

- 引擎：**Unity 6000.6.6f1**（本机已装；工程版本文件已对齐）
- 输入：Input System
- 美术：`种田素材.zip` 里的 Sprout Lands 16px 素材（角色 / 草地 / 泥土 / 水 / 植物 / 树 / 箱子 / 栅栏 / UI 图标）
- 唯一数值真相：`Assets/StreamingAssets/Data/` 下的 CSV + `config.json` + `map-preset.json` + `art.json`
- 打包产物：`Builds\SwanV01.exe`

---

## 1. 直接玩

双击：

```
Builds\SwanV01.exe
```

或双击工程根目录的 `开始游戏.bat`。

> 第一次启动会弹 7 步新手教程；看完点「开始游戏」。之后按 **F1** 可随时重看。
> 如果 exe 打不开，先看 `Docs/新手教程.md` 的「常见问题」。

### 键位

| 按键 | 作用 |
|---|---|
| **WASD / 方向键** | 移动主角（主角始终在画面正中） |
| **鼠标左键** | 点击 12×12 范围内的地块，执行当前动作 |
| **F** | 对鼠标悬停格执行当前动作（不用点） |
| **1–8** | 切换动作：种植 / 收获 / 挖掘 / 放置 / 采集 / 浇水 / 施肥 / 热源 |
| **Q / E** | 切换种子 / 切换放置物 |
| **Tab** | 背包 |
| **O** | 设置面板（小白改数值的入口） |
| **F1** | 新手教程 |
| **空格** | 暂停 / 继续 |
| **N** | 单步推进 1 个环境步 |
| **[ / ]** | 降低 / 提高倍速 |
| **F5** | 热重载数据（保留世界） |
| **R** | 按 map-preset 重建地图 |
| **滚轮** | 缩放 |
| **Esc** | 关闭面板 |

---

## 2. 在 Unity 里打开

1. Unity Hub 用 **6000.6.6f1** 打开 `swan-v01` 文件夹。
2. 菜单 **SWAN → 1 生成主场景 Main.unity**（场景是生成物，可随时重建）。
3. 打开 `Assets/Scenes/Main.unity`，按 Play。

打包：菜单 **SWAN → 2 打包 Windows**，输出 `Builds\SwanV01.exe`。

---

## 3. 小白怎么改游戏

**推荐：游戏里按 O。** 设置面板把最常改的数值做成了「− / +」：

- 主角：移动速度、精灵大小、行走动画帧率、碰撞半径
- 交互：范围宽 / 高（默认 12×12）、收获返还种子
- 时间与生长：环境步长、生长速度倍率、每天环境步数、野物再生概率/上限
- 镜头：视野、最大缩放
- 地图：种子、宽、高
- 界面：HUD 字号、面板不透明度、提示停留时间

改完点 **「保存并立即应用」**，会写回 `config.json` 并重新载入。
点 **「恢复默认」** 回到 `config.default.json`。

**文字版/进阶改法**：`Docs/改数值与美术.md`，包括：

- `config.json` 每个字段的意思
- 物品 / 结构 / 动作 / 采集物 / 教程 五张 CSV 怎么加行
- 植物为什么现在只开放 P01，怎么开放另外 5 种
- `art.json` 怎么换图、换树、换地面
- 改完在游戏里按 **F5** 还是 **R**

---

## 4. 这次加了什么

- **主角视角**：白猫角色、行走动画、阴影；相机严格跟随，主角永远在画面中心。
- **12×12 交互范围**：浅蓝边框 + 淡填充；范围外的格子光标变红，点了会提示「超出交互范围」。
- **8 种动作**：种植、收获、挖掘、放置、采集、浇水、施肥、热源；全部走 Core 层，可单测。
- **背包与掉落**：种子、产物、泥土、石头、木头、纤维、野果、蘑菇、花、鱼、堆肥、栅栏/箱子/营火/木地板。
- **野物**：按地形概率铺浆果/木头/蘑菇/花，可采集；环境步里会缓慢再生。
- **岩石挡路**：按 `rockChance` 铺岩石结构，挡路/挡种；按 **5 采集** 可搬走并换回石头。
- **美术接入**：16px 地面、水波、作物 4 阶段、猫 4 向行走、树/灌木/岩石/花、UI 图标、猫爪光标。
- **设置面板 + 教程 + 背包 + Toast**：全部 IMGUI，无需额外 UI 资产。
- **测试**：62 个 EditMode 测试（原 49 + 新增 13），覆盖移动/碰撞/范围/8 动作/背包/岩石/存档回读/美术目录。

修复的旧问题：

- 植物长大不重绘（脏格比较漏了 `Growth`）
- 显式尺寸世界没有野物（`SeedForage` 只在 preset 分支调用）
- 收获物品名对不上物品主键（`食物纤维` → `produce_p01`）
- 打包程序读 `UnityEngine.Input` 每帧抛异常（改 Input System）
- 12×12 范围层盖住植物（层级改为 Ground → Zone → Decor → Heat → Cursor）

---

## 5. 验证

| 项目 | 结果 |
|---|---|
| EditMode 测试 | **62 / 62 通过**（`Docs/tests_rock.xml`） |
| 打包 | `Build Finished, Result: Success`，`Builds\SwanV01.exe` |
| 冒烟运行 | 自动种植日志 `[SWAN] 种下草本种子（剩 7）`，截图 `Docs/screenshots/gameplay.png` |
| 教程 / 设置截图 | `Docs/screenshots/tutorial.png`、`settings.png` |
| 真实鼠标悬停 | 截图 `Docs/screenshots/click-out-of-range.png`：范围外光标变红、HUD 显示「超出交互范围」 |
| 岩石挡路/搬走 | 截图 `Docs/screenshots/rock-collect.png`：按 5 采集岩石，提示「搬走岩石，获得 石头 ×1」 |

跑测试（命令行）：

```
Unity.exe -batchmode -nographics -projectPath <工程> ^
  -runTests -testPlatform EditMode -testResults Docs\tests_rock.xml -logFile Logs\tests.log
```

打包：

```
Unity.exe -batchmode -nographics -quit -projectPath <工程> ^
  -executeMethod Swan.EditorTools.SceneBuilder.BuildWindows -logFile Logs\build.log
```

运行验收截图：

```
Builds\SwanV01.exe -force-d3d11 -swanSmokeSeconds 8 -swanShot shot.png ^
  -swanCursorCell 18,35 -swanAction 1 -swanInteract 1
```

> `-force-d3d11` 是本机远程/无显卡环境需要的；普通桌面双击 exe 不需要。

---

## 6. 目录

```
swan-v01/
  Assets/
    Scripts/Core/          纯数值层（noEngineReferences）：World、时钟、数据表、玩家/背包/动作
    Scripts/Game/          表现与胶水：Bootstrap、HUD、相机、玩家、景物、渲染
    Scripts/Editor/        场景生成 + 打包
    Tests/EditMode/        62 个测试
    StreamingAssets/Data/  config / map-preset / 10 张 CSV / 教程
    StreamingAssets/Art/   PNG + art.json
  Builds/SwanV01.exe
  Docs/                    教程、改法、交付说明、测试结果、截图
  开始游戏.bat
  打开数据文件夹.bat
```

分层规矩没变：`Game → Core`，`Editor → Game + Core`，`Tests → Core + Game`；
Core 里不许出现 `UnityEngine`，玩法数值不许写死在代码里。

---

## 7. 已知边界

- 脑图（zhixi）需要登录，当前网络环境读不到；实现以交接包文档、数值表和用户点名的
  挖掘/放置/收集为准。见 `Docs/交付说明.md`。
- 树/灌木/浆果丛是**景物**，只负责好看，不阻挡移动和种植；**岩石是结构**，会挡路。
- 岩石不是挖掘产物：站在旁边按 **5 采集** 搬走，换回 1 个石头。
- 只开放 P01 草本可种；另外 5 种植物在表里 `v01Active=0`，开启方法见改法文档。
- 暂无存档/读档；退出重开是地图种子决定的新世界。
- 暂无音效。

---

## 8. 旧版说明

v0.1「仪器版」的设计、曲线验收与 17 条实现决策仍在：

- `Docs/DECISIONS.md`
- `Docs/CONFLICTS.md`
- `Docs/curve-probe.txt`
- `design/gdd/`
