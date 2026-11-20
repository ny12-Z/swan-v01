#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// 一键搭建地图单元格系统（编辑器工具，不进包）：
///   1. 把两张 UI 用到的目录 / 光标图准备好；
///   2. 生成 4 种「单元格类型定义」资产 + 1 份「地图规则」资产（瓦片按名前缀自动收集）；
///   3. 生成「单元格光标」预制件；
///   4. 在场景里建 地图系统 物体，接好 地图数据 / 单元格显示 / 地图交互 / 单元格光标，并回填 玩家互动.地图；
///   5. 保存场景。
/// 重复执行是安全的：资产会复用并覆盖参数，场景里的 地图系统 会先删再建。
/// </summary>
public static class 搭建地图系统
{
    private const string 场景路径 = "Assets/Scenes/SampleScene.unity";
    private const string 数据目录 = "Assets/数据/地图";
    private const string 类型目录 = "Assets/数据/地图/单元格类型";
    private const string 规则路径 = "Assets/数据/地图/地图规则.asset";
    private const string 瓦片目录 = "Assets/瓦片地图/瓦片";
    private const string 光标目录 = "Assets/UI/网格";
    private const string 光标图片 = "Assets/UI/网格/单元格光标.png";
    private const string 预制体目录 = "Assets/预制体/地图";
    private const string 光标预制体 = "Assets/预制体/地图/单元格光标.prefab";

    private const string 地图物体 = "地图系统";
    private const string 玩家名字 = "玩家";
    private const string 下层名字 = "ground_under";
    private const string 上层名字 = "ground-above";
    private const string 碰撞名字 = "Collision";

    private const int 光标像素 = 16;

    /// <summary>一种类型的出厂参数，改这里就能改默认值。</summary>
    private class 类型配置
    {
        public 单元格类型 类型;
        public string 文件名;
        public string 显示名;
        public float 水分;
        public float 养分;
        public float 植被;
        public string[] 瓦片前缀;
        public bool 可通行 = true;
        public bool 参与初始识别 = true;
    }

    private static readonly 类型配置[] 配置表 =
    {
        new 类型配置
        {
            类型 = 单元格类型.草原, 文件名 = "草原", 显示名 = "草原",
            水分 = 50f, 养分 = 55f, 植被 = 100f,
            瓦片前缀 = new[] { "Grass", "Hills" },
        },
        new 类型配置
        {
            类型 = 单元格类型.旱地, 文件名 = "旱地", 显示名 = "旱地",
            水分 = 28f, 养分 = 30f, 植被 = 0f,
            瓦片前缀 = new[] { "Tilled" },
        },
        new 类型配置
        {
            // 和旱地共用 Tilled_Dirt，所以不参与初始识别
            类型 = 单元格类型.普通土地, 文件名 = "普通土地", 显示名 = "普通土地",
            水分 = 45f, 养分 = 60f, 植被 = 0f,
            瓦片前缀 = new[] { "Tilled" },
            参与初始识别 = false,
        },
        new 类型配置
        {
            类型 = 单元格类型.纯水, 文件名 = "纯水", 显示名 = "纯水",
            水分 = 95f, 养分 = 5f, 植被 = 0f,
            瓦片前缀 = new[] { "Water" },
            可通行 = false,
        },
    };

    [MenuItem("工具/搭建地图系统")]
    public static void Setup()
    {
        确保目录(数据目录);
        确保目录(类型目录);
        确保目录(光标目录);
        确保目录(预制体目录);

        生成光标贴图();
        AssetDatabase.Refresh();

        生成类型资产();
        生成规则资产();
        生成光标预制件();

        AssetDatabase.SaveAssets();
        // 注意：Refresh 会重新加载磁盘上的资产，这之前拿到的 C# 引用会变成「假 null」，
        // 所以接线阶段必须重新按路径 Load 一次（否则场景里存进去的引用全是 fileID: 0）。
        AssetDatabase.Refresh();

        整理场景();

        Debug.Log("[地图] 完成：4 种单元格类型 + 地图规则 + 单元格光标 + 场景里的「地图系统」（地图数据 / 单元格显示 / 地图交互 / 单元格光标），玩家互动.地图 已接好");
    }

    // ───────────────────────────── 资产 ─────────────────────────────

    private static void 生成光标贴图()
    {
        if (File.Exists(光标图片)) return;

        Texture2D 贴图 = new Texture2D(光标像素, 光标像素, TextureFormat.RGBA32, false);
        Color32[] 像素 = new Color32[光标像素 * 光标像素];
        Color32 白 = new Color32(255, 255, 255, 255);
        Color32 透明 = new Color32(255, 255, 255, 0);

        const int 边框 = 2;
        for (int y = 0; y < 光标像素; y++)
        {
            for (int x = 0; x < 光标像素; x++)
            {
                bool 是边框 = x < 边框 || y < 边框 || x >= 光标像素 - 边框 || y >= 光标像素 - 边框;
                像素[y * 光标像素 + x] = 是边框 ? 白 : 透明;
            }
        }

        贴图.SetPixels32(像素);
        贴图.Apply();
        File.WriteAllBytes(光标图片, 贴图.EncodeToPNG());
        Object.DestroyImmediate(贴图);

        AssetDatabase.ImportAsset(光标图片, ImportAssetOptions.ForceUpdate);

        TextureImporter 导入 = AssetImporter.GetAtPath(光标图片) as TextureImporter;
        if (导入 != null)
        {
            导入.textureType = TextureImporterType.Sprite;
            导入.spriteImportMode = SpriteImportMode.Single;
            导入.spritePixelsPerUnit = 16f;           // 16px = 1 格
            导入.filterMode = FilterMode.Point;
            导入.wrapMode = TextureWrapMode.Clamp;
            导入.mipmapEnabled = false;
            导入.alphaIsTransparency = true;
            导入.textureCompression = TextureImporterCompression.Uncompressed;
            导入.npotScale = TextureImporterNPOTScale.None;
            导入.SaveAndReimport();
        }
    }

    private static 单元格类型定义[] 生成类型资产()
    {
        List<单元格类型定义> 结果 = new List<单元格类型定义>();

        foreach (类型配置 配置 in 配置表)
        {
            string 路径 = 类型目录 + "/" + 配置.文件名 + ".asset";
            单元格类型定义 资产 = AssetDatabase.LoadAssetAtPath<单元格类型定义>(路径);
            if (资产 == null)
            {
                资产 = ScriptableObject.CreateInstance<单元格类型定义>();
                AssetDatabase.CreateAsset(资产, 路径);
            }

            资产.类型 = 配置.类型;
            资产.显示名 = 配置.显示名;
            资产.初始水分 = 配置.水分;
            资产.初始养分 = 配置.养分;
            资产.初始植被 = 配置.植被;
            资产.可通行 = 配置.可通行;
            资产.参与初始识别 = 配置.参与初始识别;
            资产.瓦片 = 收集瓦片(配置.瓦片前缀);

            EditorUtility.SetDirty(资产);
            结果.Add(资产);

            Debug.Log($"[地图] 类型 {配置.显示名}：{资产.瓦片.Length} 张瓦片，初始 {配置.水分}/{配置.养分}/{配置.植被}");
        }

        return 结果.ToArray();
    }

    /// <summary>接线阶段按路径重新取资产（Refresh 之后旧引用会失效）。</summary>
    private static 单元格类型定义[] 载入类型资产()
    {
        List<单元格类型定义> 结果 = new List<单元格类型定义>();
        foreach (类型配置 配置 in 配置表)
        {
            string 路径 = 类型目录 + "/" + 配置.文件名 + ".asset";
            单元格类型定义 资产 = AssetDatabase.LoadAssetAtPath<单元格类型定义>(路径);
            if (资产 != null)
            {
                结果.Add(资产);
            }
            else
            {
                Debug.LogError($"[地图] 载入类型资产失败：{路径}");
            }
        }
        return 结果.ToArray();
    }

    private static 地图规则 生成规则资产()
    {
        地图规则 资产 = AssetDatabase.LoadAssetAtPath<地图规则>(规则路径);
        if (资产 != null) return 资产;

        资产 = ScriptableObject.CreateInstance<地图规则>();
        AssetDatabase.CreateAsset(资产, 规则路径);
        EditorUtility.SetDirty(资产);
        return 资产;
    }

    private static TileBase[] 收集瓦片(string[] 前缀表)
    {
        List<TileBase> 结果 = new List<TileBase>();
        string[] 路径表 = AssetDatabase.FindAssets("t:Tile", new[] { 瓦片目录 });

        foreach (string guid in 路径表)
        {
            string 路径 = AssetDatabase.GUIDToAssetPath(guid);
            string 文件名 = Path.GetFileNameWithoutExtension(路径);

            foreach (string 前缀 in 前缀表)
            {
                if (文件名.StartsWith(前缀 + "_", System.StringComparison.Ordinal))
                {
                    TileBase 瓦片 = AssetDatabase.LoadAssetAtPath<TileBase>(路径);
                    if (瓦片 != null) 结果.Add(瓦片);
                    break;
                }
            }
        }

        结果.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return 结果.ToArray();
    }

    private static void 生成光标预制件()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(光标预制体) != null) return;

        Sprite 图形 = AssetDatabase.LoadAssetAtPath<Sprite>(光标图片);
        if (图形 == null)
        {
            Debug.LogError("[地图] 光标贴图没导入成 Sprite，跳过预制件生成");
            return;
        }

        GameObject 临时 = new GameObject("单元格光标");
        SpriteRenderer 渲染 = 临时.AddComponent<SpriteRenderer>();
        渲染.sprite = 图形;
        渲染.sortingOrder = 200;
        临时.AddComponent<单元格光标>();

        PrefabUtility.SaveAsPrefabAsset(临时, 光标预制体);
        Object.DestroyImmediate(临时);
    }

    // ───────────────────────────── 场景 ─────────────────────────────

    private static void 整理场景()
    {
        Scene 场景 = EditorSceneManager.OpenScene(场景路径, OpenSceneMode.Single);

        // Refresh 之后必须重新 Load，原因见 Setup 里的注释
        单元格类型定义[] 类型表 = 载入类型资产();
        地图规则 规则 = AssetDatabase.LoadAssetAtPath<地图规则>(规则路径);
        if (规则 == null) Debug.LogError($"[地图] 载入地图规则失败：{规则路径}");
        if (类型表.Length == 0) Debug.LogError("[地图] 一个单元格类型资产都没载入到");

        GameObject 旧系统 = GameObject.Find(地图物体);
        if (旧系统 != null) Object.DestroyImmediate(旧系统);

        GameObject 玩家 = GameObject.Find(玩家名字);
        if (玩家 == null)
        {
            Debug.LogError("[地图] 场景里找不到「玩家」，场景没改");
            return;
        }

        玩家移动 移动 = 玩家.GetComponent<玩家移动>();
        玩家互动 互动 = 玩家.GetComponentInChildren<玩家互动>(true);
        Tilemap 下层 = 找瓦片图(下层名字);
        Tilemap 上层 = 找瓦片图(上层名字);
        Tilemap 碰撞 = 找瓦片图(碰撞名字);

        GameObject 系统 = new GameObject(地图物体);
        地图数据 数据 = 系统.AddComponent<地图数据>();
        单元格显示 显示 = 系统.AddComponent<单元格显示>();
        地图交互 交互 = 系统.AddComponent<地图交互>();

        // 光标：实例化预制件并挂到系统下
        GameObject 光标预制 = AssetDatabase.LoadAssetAtPath<GameObject>(光标预制体);
        GameObject 光标 = null;
        if (光标预制 != null)
        {
            光标 = (GameObject)PrefabUtility.InstantiatePrefab(光标预制, 场景);
            光标.name = "单元格光标";
            光标.transform.SetParent(系统.transform, false);
        }

        // 地图数据
        设置序列化(数据, 序列化 =>
        {
            设对象数组(序列化.FindProperty("地表层"), new Object[] { 下层, 上层 });
            序列化.FindProperty("规则").objectReferenceValue = 规则;
            设对象数组(序列化.FindProperty("类型表"), 类型表);
        });

        // 显示层
        设置序列化(显示, 序列化 =>
        {
            序列化.FindProperty("数据").objectReferenceValue = 数据;
            设对象数组(序列化.FindProperty("地表层"), new Object[] { 下层, 上层 });
        });

        // 交互层
        设置序列化(交互, 序列化 =>
        {
            序列化.FindProperty("数据").objectReferenceValue = 数据;
            序列化.FindProperty("玩家").objectReferenceValue = 移动;
            序列化.FindProperty("角色").objectReferenceValue = 玩家.transform;
            序列化.FindProperty("碰撞层").objectReferenceValue = 碰撞;
            序列化.FindProperty("相机").objectReferenceValue = Camera.main;
        });

        // 光标
        if (光标 != null)
        {
            单元格光标 光标脚本 = 光标.GetComponent<单元格光标>();
            SpriteRenderer 光标渲染 = 光标.GetComponent<SpriteRenderer>();
            设置序列化(光标脚本, 序列化 =>
            {
                序列化.FindProperty("交互").objectReferenceValue = 交互;
                序列化.FindProperty("玩家").objectReferenceValue = 互动;
                序列化.FindProperty("图形").objectReferenceValue = 光标渲染;
            });
        }

        // 把地图接进玩家的互动脚本
        if (互动 != null)
        {
            设置序列化(互动, 序列化 =>
            {
                序列化.FindProperty("地图").objectReferenceValue = 交互;
            });
        }

        EditorSceneManager.MarkSceneDirty(场景);
        EditorSceneManager.SaveScene(场景);
    }

    private static Tilemap 找瓦片图(string 名字)
    {
        GameObject 物体 = GameObject.Find(名字);
        if (物体 != null)
        {
            Tilemap 图 = 物体.GetComponent<Tilemap>();
            if (图 != null) return 图;
        }

        Debug.LogWarning($"[地图] 找不到名叫「{名字}」的 Tilemap，相关引用会留空");
        return null;
    }

    private static void 设置序列化(Object 目标, System.Action<SerializedObject> 动作)
    {
        if (目标 == null) return;
        SerializedObject 序列化 = new SerializedObject(目标);
        动作(序列化);
        序列化.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(目标);
    }

    private static void 设对象数组(SerializedProperty 属性, Object[] 值)
    {
        if (属性 == null) return;
        属性.arraySize = 值.Length;
        for (int i = 0; i < 值.Length; i++)
        {
            属性.GetArrayElementAtIndex(i).objectReferenceValue = 值[i];
        }
    }

    private static void 确保目录(string 路径)
    {
        string[] 段 = 路径.Split('/');
        string 当前 = 段[0];
        for (int i = 1; i < 段.Length; i++)
        {
            string 下一段 = 当前 + "/" + 段[i];
            if (!AssetDatabase.IsValidFolder(下一段)) AssetDatabase.CreateFolder(当前, 段[i]);
            当前 = 下一段;
        }
    }
}

/// <summary>批处理入口：-executeMethod MapSystemBatch.Run（ASCII 名字，避免命令行传中文类名）。</summary>
public static class MapSystemBatch
{
    public static void Run()
    {
        搭建地图系统.Setup();
    }
}
#endif
