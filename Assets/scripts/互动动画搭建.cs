#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// 互动动作（锄地 / 斧头 / 浇水）的一键搭建工具。
///
/// 菜单：工具 / 搭建互动动画
///
/// 做的事：
///   1. 把 Assets/角色/Basic Charakter Actions.png 重切成 24 片（2 列 × 12 行，每片 48×48）
///      切片名：动作_{道具}_{方向}_{阶段}，例如 动作_锄头_下_举
///   2. 生成 24 个 AnimationClip 到 Assets/角色/动作动画/
///      每个「道具+方向」两份：
///        _准备 = 站着举着工具的样子（锄头/斧头用「落」帧，水壶用「举」帧），循环单帧
///        _使用 = 一次完整动作，1.4 秒：锄头/斧头 落→举→落 挥 3 遍（1.2 秒）再收住 0.2 秒；水壶 举→落 并保持到结尾
///   3. 生成 互动动画.controller：3 个子状态机（锄头/斧头/水壶），参数 道具 / 方向 / 播放
///   4. 在场景的「玩家」下建 互动 → 互动角色，挂 玩家互动 并接好所有引用
///
/// 布局约定（见 Basic Charakter Actions.png）：
///   行 1-4 = 锄头（下 / 上 / 左 / 右）    行 5-8 = 斧头（下 / 上 / 左 / 右）    行 9-12 = 水壶（下 / 上 / 左 / 右）
///   列 1 = 举（准备动作），列 2 = 落（使用动作）
/// 改动这里就能换排版或换名称，运行时脚本只认「道具」「方向」「播放」三个参数。
/// </summary>
public static class 互动动画搭建
{
    // ---------- 路径 ----------
    private const string 场景路径 = "Assets/Scenes/SampleScene.unity";
    private const string 动作贴图路径 = "Assets/角色/Basic Charakter Actions.png";
    private const string 动画目录 = "Assets/角色/动作动画";
    private const string 控制器路径 = 动画目录 + "/互动动画.controller";

    // ---------- 排版 ----------
    private const int 格子像素 = 48;      // 每片边长
    private const int 列数 = 2;           // 列 = 阶段：0 举，1 落
    private const int 行数 = 12;          // 行 = 道具 × 方向
    private const int 每道具行数 = 4;      // 每个道具占 4 行（四个方向）
    private const float 每单位像素 = 16f;  // 和走路图保持一致

    // ---------- 节奏 ----------
    private const int 准备帧率 = 12;       // 单帧，只是显示
    private const int 使用帧率 = 5;        // 每帧 0.2 秒
    private const float 单帧时长 = 0.2f;   // 举 ↔ 落 之间切换的时间
    private const float 动作总时长 = 1.4f; // 一次动作的长度：挥 3 遍 1.2 秒 + 收在「落」的姿势 0.2 秒
    private const int 挥动次数 = 3;        // 锄头 / 斧头 一个动作里挥几下

    private static readonly string[] 道具名 = { "锄头", "斧头", "水壶" };
    private static readonly string[] 方向名 = { "下", "上", "左", "右" };
    private static readonly string[] 阶段名 = { "举", "落" };

    // 哪些道具的「准备」姿势用落下的那一帧（锄头、斧头整把工具杵在地上；水壶保持举着）
    private static readonly bool[] 准备用落帧 = { true, true, false };

    // 哪些道具是一次性倾倒（水壶），其余是反复挥动（锄头 / 斧头）
    private const int 道具_水壶 = 2;

    [MenuItem("工具/搭建互动动画")]
    public static void Setup()
    {
        try
        {
            Dictionary<string, Sprite> 帧表 = 切片();
            if (帧表 == null) return;
            if (帧表.Count < 行数 * 列数)
            {
                Debug.LogError($"[互动动画] 切片数量不对：{帧表.Count} / {行数 * 列数}");
                return;
            }

            AnimatorController 控制器 = 生成动画(帧表);
            整理场景(控制器, 帧表);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[互动动画] 完成：24 个动作片段 + 互动动画.controller，「玩家」下已建 互动 → 互动角色");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[互动动画] 失败：" + e);
        }
    }

    // =====================================================================
    // 1. 切片
    // =====================================================================

    private static Dictionary<string, Sprite> 切片()
    {
        if (!File.Exists(动作贴图路径))
        {
            Debug.LogError($"[互动动画] 找不到贴图：{动作贴图路径}（先把 Basic Charakter Actions.png 拷到 Assets/角色/ 下）");
            return null;
        }

        var 导入器 = AssetImporter.GetAtPath(动作贴图路径) as TextureImporter;
        if (导入器 == null)
        {
            Debug.LogError($"[互动动画] {动作贴图路径} 不是 TextureImporter");
            return null;
        }

        导入器.textureType = TextureImporterType.Sprite;
        导入器.spriteImportMode = SpriteImportMode.Multiple;
        导入器.spritePixelsPerUnit = 每单位像素;
        导入器.filterMode = FilterMode.Point;          // 像素画必须点采样，否则糊
        导入器.wrapMode = TextureWrapMode.Clamp;
        导入器.mipmapEnabled = false;
        导入器.alphaIsTransparency = true;
        导入器.textureCompression = TextureImporterCompression.Uncompressed;
        导入器.npotScale = TextureImporterNPOTScale.None;
        导入器.SaveAndReimport();

        int 宽, 高;
        导入器.GetSourceTextureWidthAndHeight(out 宽, out 高);
        if (宽 != 列数 * 格子像素 || 高 != 行数 * 格子像素)
        {
            Debug.LogWarning($"[互动动画] 贴图尺寸是 {宽}×{高}，跟预设的 {列数 * 格子像素}×{行数 * 格子像素} 不一致，切片可能错位");
        }

        var 工厂 = new SpriteDataProviderFactories();
        工厂.Init();
        ISpriteEditorDataProvider 提供者 = 工厂.GetSpriteEditorDataProviderFromObject(导入器);
        提供者.InitSpriteEditorDataProvider();

        // 注意：这里不保留旧 spriteID —— 每次重跑都会连片段一起重建，引用不会断。
        var 切片们 = new List<SpriteRect>();
        for (int 行 = 0; 行 < 行数; 行++)
        {
            for (int 列 = 0; 列 < 列数; 列++)
            {
                string 名称 = 切片名(行, 列);
                var 片 = new SpriteRect
                {
                    name = 名称,
                    // rect 的原点在左下角，而第 0 行在最上面
                    rect = new Rect(列 * 格子像素, 高 - (行 + 1) * 格子像素, 格子像素, 格子像素),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                };
                切片们.Add(片);
            }
        }

        提供者.SetSpriteRects(切片们.ToArray());
        提供者.Apply();
        AssetDatabase.ImportAsset(动作贴图路径, ImportAssetOptions.ForceUpdate);

        var 结果 = new Dictionary<string, Sprite>();
        foreach (Sprite 图 in AssetDatabase.LoadAllAssetsAtPath(动作贴图路径).OfType<Sprite>())
        {
            结果[图.name] = 图;
        }
        return 结果;
    }

    /// <summary>切片名：动作_{道具}_{方向}_{阶段}</summary>
    private static string 切片名(int 行, int 列)
    {
        int 道具 = 行 / 每道具行数;
        int 方向 = 行 % 每道具行数;
        return $"动作_{道具名[道具]}_{方向名[方向]}_{阶段名[列]}";
    }

    private static Sprite 取帧(Dictionary<string, Sprite> 帧表, int 道具, int 方向, int 阶段)
    {
        string 名称 = $"动作_{道具名[道具]}_{方向名[方向]}_{阶段名[阶段]}";
        Sprite 图;
        if (!帧表.TryGetValue(名称, out 图))
        {
            Debug.LogError($"[互动动画] 缺少切片 {名称}");
            return null;
        }
        return 图;
    }

    // =====================================================================
    // 2. 动画片段 + 控制器
    // =====================================================================

    private static AnimatorController 生成动画(Dictionary<string, Sprite> 帧表)
    {
        确保目录(动画目录);

        // 幂等：先删掉上一次生成的片段和控制器
        var 旧资产 = AssetDatabase.FindAssets("t:AnimationClip", new[] { 动画目录 })
            .Concat(AssetDatabase.FindAssets("t:AnimatorController", new[] { 动画目录 }));
        foreach (string guid in 旧资产)
        {
            AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
        }

        var 控制器 = AnimatorController.CreateAnimatorControllerAtPath(控制器路径);
        控制器.AddParameter("道具", AnimatorControllerParameterType.Int);   // 0 锄头 1 斧头 2 水壶
        控制器.AddParameter("方向", AnimatorControllerParameterType.Int);   // 0 下 1 上 2 左 3 右
        控制器.AddParameter("播放", AnimatorControllerParameterType.Bool);  // 左键按下 → true

        AnimatorStateMachine 根 = 控制器.layers[0].stateMachine;

        for (int 道具 = 0; 道具 < 道具名.Length; 道具++)
        {
            AnimatorStateMachine 子机 = 根.AddStateMachine(道具名[道具], new Vector3(道具 * 720f, 0f, 0f));

            for (int 方向 = 0; 方向 < 方向名.Length; 方向++)
            {
                Sprite 举 = 取帧(帧表, 道具, 方向, 0);
                Sprite 落 = 取帧(帧表, 道具, 方向, 1);
                if (举 == null || 落 == null) continue;

                string 前缀 = $"{道具名[道具]}_{方向名[方向]}";

                // 「准备」= 站着举着工具的样子：锄头 / 斧头用落下那一帧（工具杵在地上），水壶保持举着
                Sprite 准备帧 = 准备用落帧[道具] ? 落 : 举;
                AnimationClip 准备片段 = 造片段(
                    $"{动画目录}/{前缀}_准备.anim",
                    new[] { 准备帧 },
                    new[] { 0f },
                    1f / 准备帧率,
                    准备帧率,
                    循环: true);

                // 「使用」= 一次完整的动作：
                //   锄头 / 斧头：落 → 举 → 落 挥 3 遍，最后一帧停在落（和准备姿势接上）
                //   水壶：举 → 落，然后把落下的那一帧一直拖到总时长（和锄头 / 斧头的动作一样长）
                取动作关键帧(道具, 举, 落, out Sprite[] 动作帧, out float[] 动作时间);
                AnimationClip 使用片段 = 造片段(
                    $"{动画目录}/{前缀}_使用.anim",
                    动作帧,
                    动作时间,
                    动作总时长,
                    使用帧率,
                    循环: false);

                var 准备 = 子机.AddState(方向名[方向] + "·准备", new Vector3(方向 * 260f, 0f, 0f));
                准备.motion = 准备片段;

                var 使用 = 子机.AddState(方向名[方向] + "·使用", new Vector3(方向 * 260f, 140f, 0f));
                使用.motion = 使用片段;

                // 准备 → 使用：脚本把「播放」打开就开演
                连一条(准备, 使用, AnimatorConditionMode.If, 0, "播放");

                // 使用 → 准备：① 脚本提前收（播放关掉）② 播到片尾自动回去
                连一条(使用, 准备, AnimatorConditionMode.IfNot, 0, "播放");
                var 播完 = 使用.AddTransition(准备);
                播完.hasExitTime = true;
                播完.exitTime = 1f;
                播完.duration = 0f;
                播完.hasFixedDuration = true;

                // 任何状态 → 本方向的准备：道具和方向对上、且没在播放时切过来
                var 任意到准备 = 根.AddAnyStateTransition(准备);
                任意到准备.AddCondition(AnimatorConditionMode.Equals, 道具, "道具");
                任意到准备.AddCondition(AnimatorConditionMode.Equals, 方向, "方向");
                任意到准备.AddCondition(AnimatorConditionMode.IfNot, 0, "播放");
                设过渡(任意到准备);
                任意到准备.canTransitionToSelf = false;

                if (方向 == 0) 子机.defaultState = 准备;
                if (道具 == 0 && 方向 == 0) 根.defaultState = 准备;
            }
        }

        AssetDatabase.SaveAssets();
        return 控制器;
    }

    /// <summary>
    /// 一次使用动作的关键帧。
    /// 锄头 / 斧头：落 → 举 → 落 反复 挥动次数 遍，最后一帧停在落；水壶：举 → 落，最后一帧拖到总时长。
    /// </summary>
    private static void 取动作关键帧(int 道具, Sprite 举, Sprite 落, out Sprite[] 帧, out float[] 时间)
    {
        if (道具 == 道具_水壶)
        {
            // 举 → 落，然后把落下的那一帧一直保持到结尾（Unity 会按「最后关键帧 + 1 帧」算片段长度，
            // 所以这里要显式写一个结尾关键帧，片段长度才是 动作总时长）
            帧 = new[] { 举, 落, 落 };
            时间 = new[] { 0f, 单帧时长, 动作总时长 - 单帧时长 };
            return;
        }

        int 数量 = 挥动次数 * 2 + 1;
        帧 = new Sprite[数量];
        时间 = new float[数量];
        for (int i = 0; i < 数量; i++)
        {
            帧[i] = (i % 2 == 0) ? 落 : 举;   // 落 → 举 → 落 → 举 …
            时间[i] = i * 单帧时长;
        }
    }

    /// <summary>一条 SpriteRenderer.m_Sprite 曲线（逐帧换图）的片段。</summary>
    private static AnimationClip 造片段(string 路径, Sprite[] 帧, float[] 时间, float 总时长, int 帧率, bool 循环)
    {
        var 片段 = new AnimationClip { frameRate = 帧率 };

        AnimationClipSettings 设置 = AnimationUtility.GetAnimationClipSettings(片段);
        设置.loopTime = 循环;
        设置.stopTime = 总时长;      // 片尾时间：最后一帧会一直保持到这里
        AnimationUtility.SetAnimationClipSettings(片段, 设置);

        EditorCurveBinding 绑定 = EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite");
        var 关键帧 = new ObjectReferenceKeyframe[帧.Length];
        for (int i = 0; i < 帧.Length; i++)
        {
            关键帧[i] = new ObjectReferenceKeyframe { time = 时间[i], value = 帧[i] };
        }
        AnimationUtility.SetObjectReferenceCurve(片段, 绑定, 关键帧);

        AssetDatabase.CreateAsset(片段, 路径);
        return 片段;
    }

    private static void 连一条(AnimatorState 起, AnimatorState 终, AnimatorConditionMode 条件, float 阈值, string 参数)
    {
        var 过渡 = 起.AddTransition(终);
        过渡.AddCondition(条件, 阈值, 参数);
        设过渡(过渡);
    }

    private static void 设过渡(AnimatorStateTransition 过渡)
    {
        过渡.hasExitTime = false;
        过渡.exitTime = 0f;
        过渡.duration = 0f;
        过渡.hasFixedDuration = true;
        过渡.interruptionSource = TransitionInterruptionSource.None;
    }

    // =====================================================================
    // 3. 场景层级
    // =====================================================================

    private static void 整理场景(AnimatorController 控制器, Dictionary<string, Sprite> 帧表)
    {
        EditorSceneManager.OpenScene(场景路径, OpenSceneMode.Single);

        GameObject 玩家 = GameObject.Find("玩家");
        if (玩家 == null)
        {
            Debug.LogError("[互动动画] 场景里找不到名字叫「玩家」的物体");
            return;
        }

        var 本体渲染 = 玩家.GetComponent<SpriteRenderer>();
        var 移动 = 玩家.GetComponent<玩家移动>();

        // --- 互动（只管逻辑，不渲染） ---
        Transform 互动 = 玩家.transform.Find("互动");
        if (互动 == null)
        {
            var 新物体 = new GameObject("互动");
            互动 = 新物体.transform;
            互动.SetParent(玩家.transform, false);
        }
        互动.localPosition = Vector3.zero;
        互动.localRotation = Quaternion.identity;
        互动.localScale = Vector3.one;

        // --- 互动角色（负责渲染 + 播动作） ---
        Transform 角色 = 互动.Find("互动角色");
        if (角色 == null)
        {
            var 新物体 = new GameObject("互动角色");
            角色 = 新物体.transform;
            角色.SetParent(互动, false);
        }
        角色.localPosition = Vector3.zero;
        角色.localRotation = Quaternion.identity;
        角色.localScale = Vector3.one;

        SpriteRenderer 渲染 = 角色.GetComponent<SpriteRenderer>();
        if (渲染 == null) 渲染 = 角色.gameObject.AddComponent<SpriteRenderer>();
        渲染.sprite = 取帧(帧表, 0, 0, 准备用落帧[0] ? 1 : 0);   // 和「准备」姿势一致
        渲染.enabled = false;                       // 互动时才显示
        渲染.color = Color.white;
        if (本体渲染 != null)                        // 跟本体同一层，避免被别的东西挡住
        {
            渲染.sortingLayerID = 本体渲染.sortingLayerID;
            渲染.sortingOrder = 本体渲染.sortingOrder;
        }

        Animator 动画器 = 角色.GetComponent<Animator>();
        if (动画器 == null) 动画器 = 角色.gameObject.AddComponent<Animator>();
        动画器.runtimeAnimatorController = 控制器;
        动画器.applyRootMotion = false;
        动画器.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // 渲染器关着也要继续播

        // --- 玩家互动（按键与状态机） ---
        var 控制 = 互动.GetComponent<玩家互动>();
        if (控制 == null) 控制 = 互动.gameObject.AddComponent<玩家互动>();

        var 序列化 = new SerializedObject(控制);
        序列化.FindProperty("动作动画器").objectReferenceValue = 动画器;
        序列化.FindProperty("动作渲染").objectReferenceValue = 渲染;
        序列化.FindProperty("角色渲染").objectReferenceValue = 本体渲染;
        序列化.FindProperty("移动").objectReferenceValue = 移动;
        序列化.FindProperty("动作时长").floatValue = 动作总时长;   // 和片段长度对齐，播完自动收
        序列化.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("[互动动画] 场景已保存：玩家/互动/互动角色");
    }

    private static void 确保目录(string 路径)
    {
        if (AssetDatabase.IsValidFolder(路径)) return;
        string 父 = Path.GetDirectoryName(路径).Replace('\\', '/');
        string 名 = Path.GetFileName(路径);
        确保目录(父);
        AssetDatabase.CreateFolder(父, 名);
    }
}

/// <summary>批处理用的入口（-executeMethod 传中文类名不可靠，所以再包一层 ASCII 名字）。</summary>
public static class InteractionAnimBatch
{
    public static void Run()
    {
        互动动画搭建.Setup();
    }
}
#endif
