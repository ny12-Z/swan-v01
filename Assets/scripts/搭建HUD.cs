#if UNITY_EDITOR
// 这是编辑器工具，只在 Unity 编辑器里编译；打包时会整段被剔除，不会泄漏 UnityEditor 依赖。

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 一键搭建「F 交互 HUD」。
///
/// 做的事：
///   1. 把两张 UI 素材的导入设置改成 Sprite / 点采样 / 不压缩，并按下面的矩形切好切片（名字见常量）。
///   2. 在场景里建层级：HUD（空物体）→ HUD画布（Canvas）→ 工具栏（面板）→ 3 个道具槽位 + F 按键提示。
///   3. 把工具栏上的 玩家HUD 组件的引用、槽位数组全部接好，然后保存场景。
///
/// 素材来源（已经拷进 Assets/UI，改名只是为了好读）：
///   Sprout Lands - Sprites - Basic pack/Objects/Basic_tools_and_meterials.png → Assets/UI/图标/工具栏图标.png
///   Sprout Lands - UI Pack/emojis-free/emoji style ui/Inventory_Spritesheet.png → Assets/UI/界面/背包界面.png
///
/// 菜单：工具 / 搭建HUD（重复执行会先删掉旧的 HUD 再重建，幂等）。
/// </summary>
public static class 搭建HUD
{
    // ---------- 路径 ----------
    private const string 场景路径 = "Assets/Scenes/SampleScene.unity";
    private const string 图标贴图 = "Assets/UI/图标/工具栏图标.png";
    private const string 界面贴图 = "Assets/UI/界面/背包界面.png";
    private const string 作物贴图 = "Assets/作物/贴图/Basic_Plants.png";   // 播种槽位的种子图标来自这里（由 搭建作物系统 切好）

    // ---------- 切片（左下角为原点的像素矩形，直接沿用素材作者切好的紧贴矩形，避免按 16 网格切掉描边） ----------
    private static readonly string[] 图标名 =
    {
        "图标_水壶", "图标_锄头", "图标_斧头", "图标_石头", "图标_种子", "图标_木头",
    };

    private static readonly Rect[] 图标矩形 =
    {
        new Rect(0f, 17f, 17f, 12f),   // 水壶
        new Rect(17f, 15f, 15f, 17f),  // 锄头
        new Rect(33f, 16f, 15f, 16f),  // 斧头
        new Rect(0f, 0f, 16f, 15f),    // 石头
        new Rect(16f, 0f, 15f, 16f),   // 种子
        new Rect(32f, 0f, 16f, 16f),   // 木头
    };

    private static readonly string[] 界面名 = { "HUD面板", "HUD槽位", "HUD槽位_深", "HUD按键" };

    private static readonly Rect[] 界面矩形 =
    {
        new Rect(0f, 0f, 112f, 30f),     // 圆角空白面板（九宫格用）
        new Rect(226f, 129f, 44f, 46f),  // 浅色道具槽
        new Rect(322f, 129f, 44f, 46f),  // 深色道具槽（做选中高亮层）
        new Rect(232f, 230f, 32f, 34f),  // 小方槽（做 F 键帽）
    };

    // 九宫格边框：左 / 下 / 右 / 上
    private static readonly Vector4[] 界面边框 =
    {
        new Vector4(7f, 7f, 7f, 7f),     // HUD面板
        new Vector4(10f, 10f, 10f, 10f), // HUD槽位
        new Vector4(10f, 10f, 10f, 10f), // HUD槽位_深
        new Vector4(8f, 8f, 8f, 8f),     // HUD按键
    };

    // ---------- 布局（屏幕像素，参考分辨率 1920×1080） ----------
    private const float 槽位尺寸 = 88f;
    private const float 图标尺寸 = 56f;
    private const float 槽位间隔 = 14f;
    private const float 面板内边距 = 18f;
    private const float 键帽尺寸 = 44f;
    private const float 键帽间隔 = 10f;
    private const float 工具栏边距 = 28f;

    private static readonly Color 高亮颜色 = new Color(1f, 0.83f, 0.45f, 1f);
    private static readonly Color 文字颜色 = new Color(0.32f, 0.22f, 0.16f, 1f);

    [MenuItem("工具/搭建HUD")]
    public static void Setup()
    {
        // 已经被别的工具（比如 搭建作物系统）打开过这个场景就别重复打开，免得丢掉内存里刚改的东西
        Scene 场景 = SceneManager.GetActiveScene();
        if (场景.path != 场景路径) 场景 = EditorSceneManager.OpenScene(场景路径);

        // 1) 两张贴图：改导入设置 + 切片
        切图(图标贴图, 图标名, 图标矩形, null);
        切图(界面贴图, 界面名, 界面矩形, 界面边框);

        Sprite 面板图 = 取切片(界面贴图, "HUD面板");
        Sprite 槽位图 = 取切片(界面贴图, "HUD槽位");
        Sprite 高亮图 = 取切片(界面贴图, "HUD槽位_深");
        Sprite 键帽图 = 取切片(界面贴图, "HUD按键");

        // 槽位顺序 = 互动道具 枚举顺序：锄头 0 / 斧头 1 / 水壶 2 / 播种 3
        互动道具[] 道具顺序 = { 互动道具.锄头, 互动道具.斧头, 互动道具.水壶, 互动道具.播种 };
        string[] 图标来源 = { 图标贴图, 图标贴图, 图标贴图, 作物贴图 };
        string[] 图标键 = { "图标_锄头", "图标_斧头", "图标_水壶", "图标_小麦种子" };
        string[] 槽位名 = { "槽位_锄头", "槽位_斧头", "槽位_水壶", "槽位_播种" };
        int 槽位数 = 道具顺序.Length;

        // 2) 清掉上一次生成的 HUD，保证重复执行不会叠加两份
        GameObject 旧HUD = GameObject.Find("HUD");
        if (旧HUD != null)
        {
            Object.DestroyImmediate(旧HUD);
        }

        // 3) HUD（空物体）→ HUD画布（Canvas）
        GameObject HUD根 = new GameObject("HUD");
        RectTransform 画布矩形 = 建物体("HUD画布", HUD根.transform);
        画布矩形.sizeDelta = new Vector2(1920f, 1080f);

        Canvas 画布 = 画布矩形.gameObject.AddComponent<Canvas>();
        画布.renderMode = RenderMode.ScreenSpaceOverlay;
        画布.sortingOrder = 10;   // 压在世界里的 Sprite 之上

        CanvasScaler 缩放器 = 画布矩形.gameObject.AddComponent<CanvasScaler>();
        缩放器.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        缩放器.referenceResolution = new Vector2(1920f, 1080f);
        缩放器.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        缩放器.matchWidthOrHeight = 0.5f;

        玩家HUD HUD脚本 = 画布矩形.gameObject.AddComponent<玩家HUD>();

        // 4) 工具栏面板：锚在左下角，尺寸随内容算
        float 工具栏宽 = 面板内边距 * 2f + (槽位尺寸 + 槽位间隔) * 槽位数 - 槽位间隔 + 键帽间隔 + 键帽尺寸;
        float 工具栏高 = 槽位尺寸 + 面板内边距 * 2f;

        RectTransform 工具栏 = 建物体("工具栏", 画布矩形);
        锚定左下角(工具栏, 工具栏宽, 工具栏高);
        工具栏.anchoredPosition = new Vector2(工具栏边距, 工具栏边距);

        Image 面板底图 = 加图片(工具栏, 面板图, 九宫格: true);
        面板底图.color = Color.white;

        CanvasGroup 整体透明 = 工具栏.gameObject.AddComponent<CanvasGroup>();
        整体透明.alpha = 1f;
        整体透明.interactable = false;
        整体透明.blocksRaycasts = false;

        // 5) 道具槽位：顺序 = 互动道具 枚举顺序（锄头 0 / 斧头 1 / 水壶 2 / 播种 3）
        HUD槽位[] 槽位数组 = new HUD槽位[槽位数];

        float 槽位中心Y = 面板内边距 + 槽位尺寸 * 0.5f;

        for (int i = 0; i < 槽位数; i++)
        {
            float 中心X = 面板内边距 + 槽位尺寸 * 0.5f + i * (槽位尺寸 + 槽位间隔);

            RectTransform 槽位矩形 = 建物体(槽位名[i], 工具栏);
            槽位矩形.anchorMin = new Vector2(0f, 0f);
            槽位矩形.anchorMax = new Vector2(0f, 0f);
            槽位矩形.pivot = new Vector2(0.5f, 0.5f);
            槽位矩形.sizeDelta = new Vector2(槽位尺寸, 槽位尺寸);
            槽位矩形.anchoredPosition = new Vector2(中心X, 槽位中心Y);

            Image 底框 = 加图片(槽位矩形, 槽位图, 九宫格: true);

            // 高亮层：铺满槽位，默认隐藏，由 玩家HUD 在选中时打开
            RectTransform 高亮矩形 = 建物体("高亮", 槽位矩形);
            铺满父物体(高亮矩形);
            Image 高亮 = 加图片(高亮矩形, 高亮图, 九宫格: true);
            高亮.color = 高亮颜色;
            高亮.enabled = false;

            // 图标：居中、等比缩放，选中时变亮
            RectTransform 图标矩形 = 建物体("图标", 槽位矩形);
            图标矩形.anchorMin = new Vector2(0.5f, 0.5f);
            图标矩形.anchorMax = new Vector2(0.5f, 0.5f);
            图标矩形.pivot = new Vector2(0.5f, 0.5f);
            图标矩形.sizeDelta = new Vector2(图标尺寸, 图标尺寸);
            图标矩形.anchoredPosition = Vector2.zero;
            Image 图标 = 加图片(图标矩形, 取切片(图标来源[i], 图标键[i]), 九宫格: false);
            图标.preserveAspect = true;

            槽位数组[i] = new HUD槽位
            {
                道具 = 道具顺序[i],
                底框 = 底框,
                高亮 = 高亮,
                图标 = 图标,
            };
        }

        // 6) 右侧 F 按键提示
        RectTransform 键帽矩形 = 建物体("按键提示", 工具栏);
        键帽矩形.anchorMin = new Vector2(0f, 0f);
        键帽矩形.anchorMax = new Vector2(0f, 0f);
        键帽矩形.pivot = new Vector2(0.5f, 0.5f);
        键帽矩形.sizeDelta = new Vector2(键帽尺寸, 键帽尺寸);
        键帽矩形.anchoredPosition = new Vector2(
            面板内边距 + 槽位尺寸 * 槽位数 + 槽位间隔 * (槽位数 - 1) + 键帽间隔 + 键帽尺寸 * 0.5f,
            槽位中心Y);

        加图片(键帽矩形, 键帽图, 九宫格: true);

        RectTransform 文字矩形 = 建物体("文字", 键帽矩形);
        铺满父物体(文字矩形);
        加文字(文字矩形, "F", 30, 文字颜色);

        // 7) 接线：引用、槽位数组、透明度分组
        SerializedObject 序列化 = new SerializedObject(HUD脚本);
        序列化.FindProperty("互动").objectReferenceValue = 找玩家互动();
        序列化.FindProperty("整体").objectReferenceValue = 整体透明;

        // 作物系统可能还没建（搭建作物系统 会再调一次这里），有就顺手接上
        作物系统 作物 = 作物系统.当前 != null ? 作物系统.当前 : Object.FindAnyObjectByType<作物系统>();
        SerializedProperty 作物字段 = 序列化.FindProperty("作物");
        if (作物字段 != null) 作物字段.objectReferenceValue = 作物;

        SerializedProperty 槽位列表 = 序列化.FindProperty("槽位列表");
        槽位列表.arraySize = 槽位数组.Length;
        for (int i = 0; i < 槽位数组.Length; i++)
        {
            SerializedProperty 项 = 槽位列表.GetArrayElementAtIndex(i);
            项.FindPropertyRelative("道具").enumValueIndex = (int)槽位数组[i].道具;
            项.FindPropertyRelative("底框").objectReferenceValue = 槽位数组[i].底框;
            项.FindPropertyRelative("高亮").objectReferenceValue = 槽位数组[i].高亮;
            项.FindPropertyRelative("图标").objectReferenceValue = 槽位数组[i].图标;
        }

        序列化.ApplyModifiedPropertiesWithoutUndo();

        // 8) 存场景
        EditorSceneManager.MarkSceneDirty(场景);
        EditorSceneManager.SaveScene(场景);

        Debug.Log($"[HUD] 完成：HUD → HUD画布（Canvas 1920×1080）→ 工具栏（{工具栏宽:0}×{工具栏高:0}）" +
                  $"，{槽位数组.Length} 个道具槽位 + F 按键提示，道具顺序 锄头/斧头/水壶/播种。");

        if (!Application.isBatchMode)
        {
            Selection.activeGameObject = HUD根;
        }
    }

    // ---------- 切图 ----------

    /// <summary>把贴图改成 Sprite（Multiple）并按给定矩形切片，同名切片直接重建。</summary>
    private static void 切图(string 路径, string[] 名字, Rect[] 矩形, Vector4[] 边框)
    {
        TextureImporter 导入器 = AssetImporter.GetAtPath(路径) as TextureImporter;
        if (导入器 == null)
        {
            Debug.LogError($"[HUD] 找不到贴图：{路径}");
            return;
        }

        导入器.textureType = TextureImporterType.Sprite;
        导入器.spriteImportMode = SpriteImportMode.Multiple;
        导入器.spritePixelsPerUnit = 100f;   // UI 用 100，九宫格边框才不会被放大成 6 倍
        导入器.filterMode = FilterMode.Point;
        导入器.wrapMode = TextureWrapMode.Clamp;
        导入器.mipmapEnabled = false;
        导入器.alphaIsTransparency = true;
        导入器.textureCompression = TextureImporterCompression.Uncompressed;
        导入器.npotScale = TextureImporterNPOTScale.None;
        导入器.SaveAndReimport();

        SpriteDataProviderFactories 工厂 = new SpriteDataProviderFactories();
        工厂.Init();
        ISpriteEditorDataProvider 提供者 = 工厂.GetSpriteEditorDataProviderFromObject(导入器);
        提供者.InitSpriteEditorDataProvider();

        SpriteRect[] 切片 = new SpriteRect[名字.Length];
        for (int i = 0; i < 名字.Length; i++)
        {
            切片[i] = new SpriteRect
            {
                name = 名字[i],
                rect = 矩形[i],
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                border = 边框 != null ? 边框[i] : Vector4.zero,
            };
        }

        提供者.SetSpriteRects(切片);
        提供者.Apply();
        AssetDatabase.ImportAsset(路径, ImportAssetOptions.ForceUpdate);
    }

    private static Sprite 取切片(string 贴图路径, string 名字)
    {
        foreach (Object 资产 in AssetDatabase.LoadAllAssetsAtPath(贴图路径))
        {
            if (资产 is Sprite 精灵 && 精灵.name == 名字)
            {
                return 精灵;
            }
        }

        Debug.LogError($"[HUD] 贴图 {贴图路径} 里没有叫「{名字}」的切片");
        return null;
    }

    // ---------- 建 UI 的小工具 ----------

    private static RectTransform 建物体(string 名字, Transform 父物体)
    {
        GameObject 物体 = new GameObject(名字, typeof(RectTransform));
        物体.transform.SetParent(父物体, false);
        return (RectTransform)物体.transform;
    }

    private static void 锚定左下角(RectTransform 矩形, float 宽, float 高)
    {
        矩形.anchorMin = new Vector2(0f, 0f);
        矩形.anchorMax = new Vector2(0f, 0f);
        矩形.pivot = new Vector2(0f, 0f);
        矩形.sizeDelta = new Vector2(宽, 高);
    }

    private static void 铺满父物体(RectTransform 矩形)
    {
        矩形.anchorMin = Vector2.zero;
        矩形.anchorMax = Vector2.one;
        矩形.pivot = new Vector2(0.5f, 0.5f);
        矩形.offsetMin = Vector2.zero;
        矩形.offsetMax = Vector2.zero;
    }

    private static Image 加图片(RectTransform 目标, Sprite 图, bool 九宫格)
    {
        Image 图片 = 目标.gameObject.AddComponent<Image>();
        图片.sprite = 图;
        图片.type = 九宫格 ? Image.Type.Sliced : Image.Type.Simple;
        图片.pixelsPerUnitMultiplier = 1f;
        图片.raycastTarget = false;   // HUD 只做显示，不挡鼠标
        return 图片;
    }

    private static Text 加文字(RectTransform 目标, string 内容, int 字号, Color 颜色)
    {
        Text 文字 = 目标.gameObject.AddComponent<Text>();

        // Unity 6 没有 Arial.ttf，内置字体叫 LegacyRuntime.ttf；它没有中文字形，所以 UI 文字只放 ASCII
        文字.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        文字.text = 内容;
        文字.fontSize = 字号;
        文字.color = 颜色;
        文字.alignment = TextAnchor.MiddleCenter;
        文字.raycastTarget = false;
        文字.horizontalOverflow = HorizontalWrapMode.Overflow;
        文字.verticalOverflow = VerticalWrapMode.Overflow;
        return 文字;
    }

    private static 玩家互动 找玩家互动()
    {
        GameObject 玩家 = GameObject.Find("玩家");
        if (玩家 == null)
        {
            Debug.LogWarning("[HUD] 场景里没找到「玩家」，HUD 的互动引用留空（运行时会自动找一次）");
            return null;
        }

        玩家互动 互动 = 玩家.GetComponentInChildren<玩家互动>(true);
        if (互动 == null)
        {
            Debug.LogWarning("[HUD]「玩家」下面没有 玩家互动 组件，HUD 的高亮不会跟着道具变");
        }

        return 互动;
    }
}

/// <summary>
/// 批处理入口：-executeMethod 传中文类名不可靠，所以用这个纯 ASCII 的壳子。
/// 用法：Unity.exe -batchmode -quit -projectPath &lt;项目&gt; -executeMethod HUDBatch.Run -logFile &lt;日志&gt;
/// </summary>
public static class HUDBatch
{
    public static void Run()
    {
        搭建HUD.Setup();
    }
}
#endif
