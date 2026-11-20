// 搭建作物系统：一次性把「时间 tick + 作物（播种/成长/收获）+ 草地杂物瓦片」这一套准备好。
//
// 菜单：工具 / 搭建作物系统   （可以反复执行，产物覆盖重建，不会叠加）
// 批处理：Unity.exe -batchmode -quit -projectPath <项目> -executeMethod FarmBatch.Run
//
// 它做的事（按顺序）：
//   1) 把素材包里的 Basic_Plants.png / Basic_Grass_Biom_things.png 拷进 Assets；
//   2) 切图：作物图改成 图标_作物种子 / 成长_作物_1..4 / 收获_作物；杂物图改名 草地杂物_00..44；
//   3) 用杂物切片生成瓦片资产，并建一张 Tile Palette 用的调色板预制件「草地杂物」；
//   4) 生成 数据/作物 下的 小麦.asset、苹果.asset、环境规则.asset，和 作物视觉.prefab；
//   5) 重建 HUD（多一个「播种」槽），在场景里建两个根空物体：时间系统、作物系统，并把引用全接好。
//
// 生成的资产位置：
//   Assets/作物/贴图/Basic_Plants.png           作物图（12 片）
//   Assets/作物/预制件/作物视觉.prefab           作物 Sprite 的空壳
//   Assets/数据/作物/小麦.asset 苹果.asset        每种作物的规格书（4 帧 / 4 分钟）
//   Assets/数据/作物/环境规则.asset              每 4 刻掉 0.1 水+养、半径 3 有水每 4 刻 +0.2 水
//   Assets/瓦片地图/素材/Basic_Grass_Biom_things.png
//   Assets/瓦片地图/瓦片/草地杂物_00..44.asset
//   Assets/瓦片地图/瓦片调色板/草地杂物.prefab
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Tilemaps;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class 搭建作物系统
{
    // ---------- 路径 ----------
    private const string 场景路径 = "Assets/Scenes/SampleScene.unity";
    private const string 作物贴图 = "Assets/作物/贴图/Basic_Plants.png";
    private const string 杂物贴图 = "Assets/瓦片地图/素材/Basic_Grass_Biom_things.png";
    private const string 瓦片目录 = "Assets/瓦片地图/瓦片";
    private const string 调色板目录 = "Assets/瓦片地图/瓦片调色板";
    private const string 数据目录 = "Assets/数据/作物";
    private const string 预制件目录 = "Assets/作物/预制件";
    private const string 作物视觉预制件 = "Assets/作物/预制件/作物视觉.prefab";

    // 素材包在工程外面（和 Assets 同级的上层目录里）
    private static string 素材目录 => Path.GetFullPath(Path.Combine(
        Application.dataPath, "..", "..", "种田素材", "Sprout Lands - Sprites - Basic pack", "Objects"));

    // ---------- 数值 ----------
    private const int 格子像素 = 16;      // 两张图的单元格都是 16×16
    private const int 每单位像素 = 16;      // 和瓦片、角色保持同一个像素密度
    private const int 杂物列数 = 9;        // 调色板里每行摆几个杂物
    private const float 成熟秒数 = 240f;    // 4 分钟

    [MenuItem("工具/搭建作物系统")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[作物] 请先退出 Play 模式再执行");
            return;
        }

        // 1) 目录 + 把两张贴图拷进项目（连 .meta 一起拷，沿用素材包自带的切片信息）
        确保目录("Assets/作物/贴图");
        确保目录(预制件目录);
        确保目录(数据目录);
        确保目录("Assets/瓦片地图/素材");
        确保目录(瓦片目录);
        确保目录(调色板目录);

        拷入贴图("Basic_Plants.png", 作物贴图);
        拷入贴图("Basic_Grass_Biom_things.png", 杂物贴图);

        // 2) 切图
        切作物贴图();
        List<Sprite> 杂物图 = 切杂物贴图();

        // 3) 瓦片 + 调色板
        List<Tile> 杂物瓦片 = 生成杂物瓦片(杂物图);
        if (杂物瓦片.Count > 0) 建调色板("草地杂物", 杂物瓦片);

        // 4) 数据资产 + 预制件
        AssetDatabase.LoadAllAssetsAtPath(作物贴图);   // 确保切好的 sprite 已入库
        生成作物定义("小麦", 作物品种.小麦);
        生成作物定义("苹果", 作物品种.苹果);
        生成环境规则();
        生成作物视觉预制件();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 5) 场景：两个根空物体 + HUD 第 4 槽 + 全部引用
        整理场景();

        Debug.Log("[作物] 完成：时间系统（时间tick + 环境演化）、作物系统（+ 作物容器）、" +
                  "HUD 4 槽（锄头/斧头/水壶/播种）、草地杂物调色板、小麦 / 苹果 定义（4 分钟成熟）");
    }

    // ---------- 贴图 ----------

    /// <summary>从素材包拷一张 PNG（带 .meta）进项目，并立刻导入。</summary>
    private static void 拷入贴图(string 文件名, string 目标路径)
    {
        string 源 = Path.Combine(素材目录, 文件名);
        if (!File.Exists(源))
        {
            Debug.LogError($"[作物] 找不到素材：{源}");
            return;
        }

        string 目标绝对 = Path.GetFullPath(目标路径);
        File.Copy(源, 目标绝对, true);

        string 源meta = 源 + ".meta";
        if (File.Exists(源meta)) File.Copy(源meta, 目标绝对 + ".meta", true);

        AssetDatabase.ImportAsset(目标路径,
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Debug.Log($"[作物] 已拷入 {文件名} → {目标路径}");
    }

    /// <summary>一张切片的名字 / 像素矩形 / 轴心。</summary>
    private struct 切片规格
    {
        public string 名字;
        public Rect 矩形;
        public Vector2 轴心;
    }

    /// <summary>统一贴图导入设置（像素风：Point + 不压缩 + 无 mipmap）。</summary>
    private static TextureImporter 改贴图设置(string 路径)
    {
        TextureImporter 导入器 = AssetImporter.GetAtPath(路径) as TextureImporter;
        if (导入器 == null)
        {
            Debug.LogError($"[作物] 读不到贴图导入设置：{路径}");
            return null;
        }

        导入器.textureType = TextureImporterType.Sprite;
        导入器.spriteImportMode = SpriteImportMode.Multiple;
        导入器.spritePixelsPerUnit = 每单位像素;
        导入器.filterMode = FilterMode.Point;
        导入器.wrapMode = TextureWrapMode.Clamp;
        导入器.mipmapEnabled = false;
        导入器.alphaIsTransparency = true;
        导入器.textureCompression = TextureImporterCompression.Uncompressed;
        导入器.npotScale = TextureImporterNPOTScale.None;
        导入器.maxTextureSize = 2048;
        导入器.SaveAndReimport();

        return AssetImporter.GetAtPath(路径) as TextureImporter;
    }

    /// <summary>按规格表重切一张图（同名切片沿用旧 spriteID，重复执行不会断引用）。</summary>
    private static void 写入切片(string 路径, List<切片规格> 规格表)
    {
        TextureImporter 导入器 = 改贴图设置(路径);
        if (导入器 == null) return;

        SpriteDataProviderFactories 工厂 = new SpriteDataProviderFactories();
        工厂.Init();
        ISpriteEditorDataProvider 提供者 = 工厂.GetSpriteEditorDataProviderFromObject(导入器);
        if (提供者 == null)
        {
            Debug.LogError($"[作物] 拿不到 Sprite 数据提供器：{路径}");
            return;
        }

        提供者.InitSpriteEditorDataProvider();

        Dictionary<string, GUID> 旧ID = new Dictionary<string, GUID>();
        foreach (SpriteRect 旧 in 提供者.GetSpriteRects())
        {
            if (!string.IsNullOrEmpty(旧.name)) 旧ID[旧.name] = 旧.spriteID;
        }

        List<SpriteRect> 切片 = new List<SpriteRect>(规格表.Count);
        foreach (切片规格 规格 in 规格表)
        {
            SpriteRect 矩形 = new SpriteRect
            {
                name = 规格.名字,
                rect = 规格.矩形,
                alignment = SpriteAlignment.Custom,
                pivot = 规格.轴心,
                border = Vector4.zero,
            };

            if (旧ID.TryGetValue(规格.名字, out GUID id)) 矩形.spriteID = id;
            切片.Add(矩形);
        }

        提供者.SetSpriteRects(切片.ToArray());
        提供者.Apply();
        AssetDatabase.ImportAsset(路径, ImportAssetOptions.ForceUpdate);
    }

    /// <summary>
    /// 作物图：96×32，上排小麦、下排苹果，每行 6 格
    /// = 种子图标 / 4 个成长帧（从左到右）/ 收获物贴图（暂未使用，留给背包）。
    /// 成长帧轴心放到底边中点，这样长在格子里是从地面往上长。
    /// </summary>
    private static void 切作物贴图()
    {
        List<切片规格> 规格表 = new List<切片规格>();

        for (int 行 = 0; 行 < 2; 行++)
        {
            bool 上排 = 行 == 0;
            string 品种 = 上排 ? "小麦" : "苹果";
            int y = 上排 ? 格子像素 : 0;

            for (int 列 = 0; 列 < 6; 列++)
            {
                string 名字;
                Vector2 轴心;

                if (列 == 0)
                {
                    名字 = $"图标_{品种}种子";
                    轴心 = new Vector2(0.5f, 0.5f);
                }
                else if (列 == 5)
                {
                    名字 = $"收获_{品种}";
                    轴心 = new Vector2(0.5f, 0.5f);
                }
                else
                {
                    名字 = $"成长_{品种}_{列}";
                    轴心 = new Vector2(0.5f, 0f);
                }

                规格表.Add(new 切片规格
                {
                    名字 = 名字,
                    矩形 = new Rect(列 * 格子像素, y, 格子像素, 格子像素),
                    轴心 = 轴心,
                });
            }
        }

        写入切片(作物贴图, 规格表);
    }

    /// <summary>
    /// 杂物图：144×80，素材包自带的是「紧贴矩形」切片（大小不一，不是 16 网格），
    /// 照搬它的矩形，只把名字改成 草地杂物_00..，轴心压到底边中点。
    /// </summary>
    private static List<Sprite> 切杂物贴图()
    {
        AssetDatabase.ImportAsset(杂物贴图, ImportAssetOptions.ForceUpdate);

        List<Sprite> 原图 = AssetDatabase.LoadAllAssetsAtPath(杂物贴图)
            .OfType<Sprite>()
            .OrderByDescending(s => Mathf.RoundToInt(s.rect.y))
            .ThenBy(s => s.rect.x)
            .ToList();

        if (原图.Count == 0)
        {
            Debug.LogError("[作物] 杂物贴图没有切片，跳过");
            return new List<Sprite>();
        }

        List<切片规格> 规格表 = new List<切片规格>(原图.Count);
        for (int i = 0; i < 原图.Count; i++)
        {
            规格表.Add(new 切片规格
            {
                名字 = $"草地杂物_{i:00}",
                矩形 = 原图[i].rect,
                轴心 = new Vector2(0.5f, 0f),
            });
        }

        写入切片(杂物贴图, 规格表);

        return AssetDatabase.LoadAllAssetsAtPath(杂物贴图)
            .OfType<Sprite>()
            .Where(s => s.name.StartsWith("草地杂物_"))
            .OrderBy(s => s.name)
            .ToList();
    }

    // ---------- 瓦片 / 调色板 ----------

    /// <summary>每个杂物切片一个瓦片资产（纯装饰，不产生碰撞）。</summary>
    private static List<Tile> 生成杂物瓦片(List<Sprite> 图列表)
    {
        List<Tile> 结果 = new List<Tile>(图列表.Count);

        for (int i = 0; i < 图列表.Count; i++)
        {
            string 路径 = $"{瓦片目录}/草地杂物_{i:00}.asset";
            Tile 瓦片 = AssetDatabase.LoadAssetAtPath<Tile>(路径);

            if (瓦片 == null)
            {
                瓦片 = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(瓦片, 路径);
            }

            瓦片.sprite = 图列表[i];
            瓦片.colliderType = Tile.ColliderType.None;
            // 清掉 Tile 默认的 LockColor，否则以后想给这类瓦片按格染色会失效。
            瓦片.flags = TileFlags.None;
            EditorUtility.SetDirty(瓦片);

            结果.Add(瓦片);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[作物] 草地杂物瓦片：{结果.Count} 个");
        return 结果;
    }

    /// <summary>建一张能出现在 Tile Palette 下拉框里的调色板（必须带 GridPalette 子资产）。</summary>
    private static void 建调色板(string 名字, List<Tile> 瓦片列表)
    {
        string 路径 = $"{调色板目录}/{名字}.prefab";
        AssetDatabase.DeleteAsset(路径);

        GameObject 预制件 = GridPaletteUtility.CreateNewPalette(调色板目录, 名字,
            GridLayout.CellLayout.Rectangle, GridPalette.CellSizing.Automatic,
            Vector3.one, GridLayout.CellSwizzle.XYZ);

        if (预制件 == null)
        {
            Debug.LogError($"[作物] 建调色板失败：{路径}");
            return;
        }

        GameObject 内容 = PrefabUtility.LoadPrefabContents(路径);
        Tilemap 图 = 内容.GetComponentInChildren<Tilemap>(true);
        if (图 == null)
        {
            Debug.LogError($"[作物] 调色板 {名字} 里没有 Tilemap");
            PrefabUtility.UnloadPrefabContents(内容);
            return;
        }

        图.tileAnchor = new Vector3(0.5f, 0.5f, 0f);

        List<TileChangeData> 摆放 = new List<TileChangeData>(瓦片列表.Count);
        for (int i = 0; i < 瓦片列表.Count; i++)
        {
            摆放.Add(new TileChangeData
            {
                position = new Vector3Int(i % 杂物列数, -(i / 杂物列数), 0),
                tile = 瓦片列表[i],
                color = Color.white,
                transform = Matrix4x4.identity,
            });
        }

        图.SetTiles(摆放.ToArray(), true);
        图.CompressBounds();

        PrefabUtility.SaveAsPrefabAsset(内容, 路径);
        PrefabUtility.UnloadPrefabContents(内容);

        Debug.Log($"[作物] 调色板 {名字}：{摆放.Count} 个瓦片 → {路径}");
    }

    // ---------- 数据资产 / 预制件 ----------

    /// <summary>一种作物的规格书：4 个成长帧 + 种子图标 + 收获物贴图 + 4 分钟成熟。</summary>
    private static void 生成作物定义(string 品种名, 作物品种 品种)
    {
        string 路径 = $"{数据目录}/{品种名}.asset";
        作物定义 定义 = AssetDatabase.LoadAssetAtPath<作物定义>(路径);

        if (定义 == null)
        {
            定义 = ScriptableObject.CreateInstance<作物定义>();
            AssetDatabase.CreateAsset(定义, 路径);
        }

        List<Sprite> 图 = AssetDatabase.LoadAllAssetsAtPath(作物贴图).OfType<Sprite>().ToList();
        Sprite 取(string 名) => 图.FirstOrDefault(s => s.name == 名);

        定义.品种 = 品种;
        定义.显示名 = 品种名;
        定义.图标 = 取($"图标_{品种名}种子");
        定义.收获物 = 取($"收获_{品种名}");
        定义.成长帧 = new[]
        {
            取($"成长_{品种名}_1"),
            取($"成长_{品种名}_2"),
            取($"成长_{品种名}_3"),
            取($"成长_{品种名}_4"),
        };
        定义.成熟秒数 = 成熟秒数;

        EditorUtility.SetDirty(定义);
        AssetDatabase.SaveAssets();

        Debug.Log($"[作物] 作物定义 {品种名}：图标 {(定义.图标 != null ? "有" : "缺")}，" +
                  $"成长帧 {定义.成长帧.Count(s => s != null)} 个，成熟 {定义.成熟秒数:0} 秒");
    }

    /// <summary>环境规则：数值都在 asset 里，改数值不用改代码。</summary>
    private static void 生成环境规则()
    {
        string 路径 = $"{数据目录}/环境规则.asset";
        if (AssetDatabase.LoadAssetAtPath<环境规则>(路径) != null) return;

        环境规则 规则 = ScriptableObject.CreateInstance<环境规则>();
        AssetDatabase.CreateAsset(规则, 路径);
        AssetDatabase.SaveAssets();

        Debug.Log($"[作物] 环境规则：{路径}");
    }

    /// <summary>作物 Sprite 的空壳预制件（只有 SpriteRenderer）。</summary>
    private static void 生成作物视觉预制件()
    {
        Sprite 图 = AssetDatabase.LoadAllAssetsAtPath(作物贴图)
            .OfType<Sprite>()
            .FirstOrDefault(s => s.name == "成长_小麦_1");

        GameObject 临时 = new GameObject("作物视觉");
        SpriteRenderer 渲染 = 临时.AddComponent<SpriteRenderer>();
        渲染.sprite = 图;
        渲染.sortingOrder = 30;

        PrefabUtility.SaveAsPrefabAsset(临时, 作物视觉预制件);
        UnityEngine.Object.DestroyImmediate(临时);

        Debug.Log($"[作物] 作物视觉预制件：{作物视觉预制件}");
    }

    // ---------- 场景 ----------

    private static void 整理场景()
    {
        Scene 场景 = SceneManager.GetActiveScene();
        if (场景.path != 场景路径) 场景 = EditorSceneManager.OpenScene(场景路径);

        // 上面 Refresh 过，之前拿到的资产引用会变成假 null，这里必须按路径重新载入
        环境规则 规则 = 载入<环境规则>($"{数据目录}/环境规则.asset");
        作物定义 小麦 = 载入<作物定义>($"{数据目录}/小麦.asset");
        作物定义 苹果 = 载入<作物定义>($"{数据目录}/苹果.asset");
        GameObject 视觉 = 载入<GameObject>(作物视觉预制件);

        if (规则 == null || 小麦 == null || 苹果 == null || 视觉 == null)
        {
            Debug.LogError("[作物] 资产没准备好，跳过场景接线");
            return;
        }

        地图数据 地图 = UnityEngine.Object.FindAnyObjectByType<地图数据>(FindObjectsInactive.Include);

        // ---- 时间系统：时钟 + 环境演化 ----
        GameObject 时间根 = 找或建根物体("时间系统");
        时间tick 时钟 = 取或加<时间tick>(时间根);
        环境演化 演化 = 取或加<环境演化>(时间根);

        设置序列化(演化, 序列化 =>
        {
            序列化.FindProperty("数据").objectReferenceValue = 地图;
            序列化.FindProperty("时钟").objectReferenceValue = 时钟;
            序列化.FindProperty("规则").objectReferenceValue = 规则;
        });

        // ---- 作物系统：作物表 + 容器 ----
        GameObject 作物根 = 找或建根物体("作物系统");
        作物系统 作物 = 取或加<作物系统>(作物根);
        Transform 容器 = 找或建子物体(作物根.transform, "作物容器");

        设置序列化(作物, 序列化 =>
        {
            序列化.FindProperty("地图").objectReferenceValue = 地图;
            序列化.FindProperty("时钟").objectReferenceValue = 时钟;
            序列化.FindProperty("容器").objectReferenceValue = 容器;
            序列化.FindProperty("视觉预制件").objectReferenceValue = 视觉;
            序列化.FindProperty("排序值").intValue = -4;
            // 成长帧轴心在底边中点，所以往下移半格，作物正好站在格子底线上
            序列化.FindProperty("位置偏移").vector3Value = new Vector3(0f, -0.5f, 0f);
            序列化.FindProperty("当前品种").enumValueIndex = (int)作物品种.小麦;

            SerializedProperty 定义表 = 序列化.FindProperty("定义表");
            定义表.arraySize = 2;
            定义表.GetArrayElementAtIndex(0).objectReferenceValue = 小麦;
            定义表.GetArrayElementAtIndex(1).objectReferenceValue = 苹果;
        });

        // ---- 其它脚本上的「作物」引用 ----
        玩家互动 互动 = UnityEngine.Object.FindAnyObjectByType<玩家互动>(FindObjectsInactive.Include);
        if (互动 != null)
        {
            设置序列化(互动, 序列化 => 序列化.FindProperty("作物").objectReferenceValue = 作物);
        }

        foreach (单元格光标 光标 in UnityEngine.Object.FindObjectsByType<单元格光标>(
                     FindObjectsInactive.Include))
        {
            设置序列化(光标, 序列化 => 序列化.FindProperty("作物").objectReferenceValue = 作物);
        }

        // ---- HUD 第 4 个槽（锄头/斧头/水壶/播种），顺便接上 玩家HUD.作物 ----
        搭建HUD.Setup();

        EditorSceneManager.MarkSceneDirty(场景);
        EditorSceneManager.SaveScene(场景);

        Debug.Log($"[作物] 场景接线完成：时间系统（时间tick + 环境演化）、作物系统（作物系统 + 作物容器，品种 {小麦.显示名}/{苹果.显示名}）");
    }

    // ---------- 小工具 ----------

    private static T 载入<T>(string 路径) where T : UnityEngine.Object
    {
        T 资产 = AssetDatabase.LoadAssetAtPath<T>(路径);
        if (资产 == null) Debug.LogError($"[作物] 载入失败：{路径}");
        return 资产;
    }

    private static GameObject 找或建根物体(string 名字)
    {
        GameObject 现有 = GameObject.Find(名字);
        if (现有 != null) return 现有;

        return new GameObject(名字);
    }

    private static T 取或加<T>(GameObject 物体) where T : Component
    {
        T 组件 = 物体.GetComponent<T>();
        return 组件 != null ? 组件 : 物体.AddComponent<T>();
    }

    private static Transform 找或建子物体(Transform 父, string 名字)
    {
        Transform 现有 = 父.Find(名字);
        if (现有 != null) return 现有;

        GameObject 子 = new GameObject(名字);
        子.transform.SetParent(父, false);
        return 子.transform;
    }

    private static void 设置序列化(UnityEngine.Object 目标, Action<SerializedObject> 动作)
    {
        SerializedObject 序列化 = new SerializedObject(目标);
        动作(序列化);
        序列化.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(目标);
    }

    private static void 确保目录(string 路径)
    {
        string 正斜杠 = 路径.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(正斜杠)) return;

        string 父 = Path.GetDirectoryName(正斜杠);
        if (!string.IsNullOrEmpty(父)) 确保目录(父);

        string 新名 = Path.GetFileName(正斜杠);
        if (!string.IsNullOrEmpty(新名)) AssetDatabase.CreateFolder(父, 新名);
    }
}

/// <summary>批处理入口（-executeMethod 传中文类名不可靠，所以用这个 ASCII 名字）。</summary>
public static class FarmBatch
{
    public static void Run()
    {
        搭建作物系统.Setup();
    }
}
#endif
