// 这个文件放在 scripts 里方便统一管理，但它只给编辑器用：
// 整段用 UNITY_EDITOR 包起来，打包成播放器时会被编译掉，不会引用到 UnityEditor。
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 一键把「瓦片地图/素材」里的贴图按 16×16 切好，生成瓦片资产，
/// 再生成三张调色板预制件（Ground / Decor / Collision）到「瓦片地图/瓦片调色板」。
/// 菜单：工具 / 一键准备瓦片地图。重复执行会覆盖旧资源，可以随时重跑。
/// </summary>
public static class TilemapSetup
{
    const string 素材目录 = "Assets/瓦片地图/素材";
    const string 瓦片目录 = "Assets/瓦片地图/瓦片";
    const string 调色板目录 = "Assets/瓦片地图/瓦片调色板";

    const int 格子像素 = 16;          // 一个瓦片 16×16 像素
    const float 每单位像素 = 16f;      // 和角色一致的 PPU，1 格 = 1 世界单位

    // 每张调色板放哪些贴图（按此顺序从上往下排列）
    static readonly string[] 地面贴图 = { "Grass", "Hills", "Water", "Tilled_Dirt", "Tilled_Dirt_v2", "Tilled_Dirt_Wide", "Tilled_Dirt_Wide_v2" };
    static readonly string[] 装饰贴图 = { "Fences", "Doors", "Wooden_House_Roof_Tilset", "Wooden_House_Walls_Tilset" };
    static readonly string[] 碰撞贴图 = { "Water", "Hills", "Fences", "Wooden_House_Walls_Tilset" };

    class 贴图信息
    {
        public string 名字;
        public int 列数;
        public int 行数;
        public List<Sprite> Sprites = new List<Sprite>();
        public List<Tile> 瓦片 = new List<Tile>();
    }

    [MenuItem("工具/一键准备瓦片地图")]
    public static void SetupAll()
    {
        创建目录(瓦片目录);
        创建目录(调色板目录);
        清空旧瓦片();

        var 表 = new Dictionary<string, 贴图信息>();
        foreach (var path in 素材贴图列表())
        {
            var info = 处理贴图(path);
            if (info != null)
                表[info.名字] = info;
        }

        foreach (var info in 表.Values)
            生成瓦片资产(info);

        生成调色板("Ground", 地面贴图, 表);
        生成调色板("Decor", 装饰贴图, 表);
        生成调色板("Collision", 碰撞贴图, 表);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int 瓦片数 = 表.Values.Sum(i => i.瓦片.Count(t => t != null));
        Debug.Log($"[瓦片地图] 完成：{表.Count} 张素材贴图，{瓦片数} 个瓦片；调色板 Ground / Decor / Collision 已生成到 {调色板目录}");
    }

    // ---------- 素材 ----------

    static List<string> 素材贴图列表()
    {
        if (!AssetDatabase.IsValidFolder(素材目录))
        {
            Debug.LogError($"[瓦片地图] 找不到素材目录：{素材目录}");
            return new List<string>();
        }

        return AssetDatabase.FindAssets("t:Texture2D", new[] { 素材目录 })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    static 贴图信息 处理贴图(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null)
        {
            Debug.LogError($"[瓦片地图] 不是贴图：{path}");
            return null;
        }

        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Multiple;
        ti.spritePixelsPerUnit = 每单位像素;
        ti.filterMode = FilterMode.Point;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = 2048;
        ti.SaveAndReimport(); // 先按新设置导入一次，下面切图用的数据提供器才是最新的

        ti.GetSourceTextureWidthAndHeight(out int 宽, out int 高);
        int 列 = 宽 / 格子像素;
        int 行 = 高 / 格子像素;
        if (列 == 0 || 行 == 0)
        {
            Debug.LogError($"[瓦片地图] {Path.GetFileName(path)} 尺寸 {宽}×{高} 小于一个瓦片，跳过。");
            return null;
        }

        string 前缀 = Path.GetFileNameWithoutExtension(path);

        // 用 Sprite Editor 的正式接口切图（TextureImporter.spritesheet 在 Unity 6 已失效）
        var 工厂 = new SpriteDataProviderFactories();
        工厂.Init();
        var 提供者 = 工厂.GetSpriteEditorDataProviderFromObject(ti);
        if (提供者 == null)
        {
            Debug.LogError($"[瓦片地图] {前缀} 拿不到 Sprite 数据提供器，跳过。");
            return null;
        }
        提供者.InitSpriteEditorDataProvider();

        // 同名的切片沿用原来的 spriteID，重复执行时场景/预制件里的引用不会断
        var 旧ID = new Dictionary<string, GUID>();
        foreach (var 矩形 in 提供者.GetSpriteRects())
        {
            if (!string.IsNullOrEmpty(矩形.name))
                旧ID[矩形.name] = 矩形.spriteID;
        }

        var 切片 = new List<SpriteRect>(列 * 行);
        for (int r = 0; r < 行; r++)
        {
            for (int c = 0; c < 列; c++)
            {
                string 名 = $"{前缀}_{r * 列 + c}";
                var 矩形 = new SpriteRect
                {
                    name = 名,
                    // Unity 的贴图坐标原点在左下角，第 0 行在最上面
                    rect = new Rect(c * 格子像素, 高 - (r + 1) * 格子像素, 格子像素, 格子像素),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                };
                if (旧ID.TryGetValue(名, out var id))
                    矩形.spriteID = id;
                切片.Add(矩形);
            }
        }

        提供者.SetSpriteRects(切片.ToArray());
        提供者.Apply();
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var sprites = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<Sprite>()
            .OrderBy(s => 序号(s.name))
            .ToList();

        Debug.Log($"[瓦片地图] {前缀}：{宽}×{高} → {列}×{行} = {列 * 行} 个瓦片，实际切出 {sprites.Count} 个 sprite");

        return new 贴图信息
        {
            名字 = 前缀,
            列数 = 列,
            行数 = 行,
            Sprites = sprites,
        };
    }

    // ---------- 瓦片资产 ----------

    static void 清空旧瓦片()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Tile", new[] { 瓦片目录 }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(p))
                AssetDatabase.DeleteAsset(p);
        }
    }

    static void 生成瓦片资产(贴图信息 info)
    {
        info.瓦片.Clear();
        for (int i = 0; i < info.Sprites.Count; i++)
        {
            var sprite = info.Sprites[i];
            string 资产路径 = $"{瓦片目录}/{sprite.name}.asset";
            // 碰撞层用到的贴图给 Sprite 碰撞体，其余保持无碰撞：
            // Tile 的 ColliderType 是 None 时，Tilemap Collider 2D 什么都生成不出来。
            var 碰撞类型 = Array.IndexOf(碰撞贴图, info.名字) >= 0 ? Tile.ColliderType.Sprite : Tile.ColliderType.None;

            var tile = AssetDatabase.LoadAssetAtPath<Tile>(资产路径);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprite;
                tile.colliderType = 碰撞类型;
                // 默认的 Tile 会带 LockColor 标记，那样 Tilemap.SetColor 会被忽略；这里清掉，方便以后按格染色。
                tile.flags = TileFlags.None;
                tile.name = sprite.name;
                AssetDatabase.CreateAsset(tile, 资产路径);
            }
            else
            {
                tile.sprite = sprite;
                tile.colliderType = 碰撞类型;
                tile.flags = TileFlags.None;
                EditorUtility.SetDirty(tile);
            }
            info.瓦片.Add(tile);
        }
    }

    // ---------- 调色板 ----------

    static void 生成调色板(string 名字, string[] 贴图名, Dictionary<string, 贴图信息> 表)
    {
        var 块列表 = new List<贴图信息>();
        foreach (var n in 贴图名)
        {
            if (表.TryGetValue(n, out var info) && info.瓦片.Count > 0)
                块列表.Add(info);
            else
                Debug.LogWarning($"[瓦片地图] 调色板 {名字} 找不到贴图：{n}");
        }
        if (块列表.Count == 0)
        {
            Debug.LogError($"[瓦片地图] 调色板 {名字} 没有可用贴图，跳过。");
            return;
        }

        // 用 Unity 自己的接口建调色板：它会顺手加上 Tile Palette 需要的 GridPalette 子资产
        string 预制件路径 = $"{调色板目录}/{名字}.prefab";
        AssetDatabase.DeleteAsset(预制件路径);

        var 预制件 = GridPaletteUtility.CreateNewPalette(调色板目录, 名字,
            GridLayout.CellLayout.Rectangle, GridPalette.CellSizing.Automatic,
            Vector3.one, GridLayout.CellSwizzle.XYZ);

        if (预制件 == null)
        {
            Debug.LogError($"[瓦片地图] 创建调色板失败：{预制件路径}");
            return;
        }

        // 往调色板的 Tilemap 上摆瓦片
        var 内容 = PrefabUtility.LoadPrefabContents(预制件路径);
        var tilemap = 内容.GetComponentInChildren<Tilemap>(true);
        if (tilemap == null)
        {
            Debug.LogError($"[瓦片地图] 调色板 {名字} 里没有 Tilemap");
            PrefabUtility.UnloadPrefabContents(内容);
            return;
        }

        tilemap.tileAnchor = new Vector3(0.5f, 0.5f, 0f);

        // 每张贴图按它自己的列×行摆放，块与块之间空一行
        int 总行数 = 块列表.Sum(b => b.行数) + 块列表.Count - 1;
        int 起始Y = 总行数 - 1;
        var 摆放 = new List<TileChangeData>();

        foreach (var b in 块列表)
        {
            for (int r = 0; r < b.行数; r++)
            {
                for (int c = 0; c < b.列数; c++)
                {
                    int index = r * b.列数 + c;
                    if (index >= b.瓦片.Count || b.瓦片[index] == null)
                        continue;

                    摆放.Add(new TileChangeData
                    {
                        position = new Vector3Int(c, 起始Y - r, 0),
                        tile = b.瓦片[index],
                        color = Color.white,
                        transform = Matrix4x4.identity,
                    });
                }
            }
            起始Y -= b.行数 + 1;
        }

        tilemap.SetTiles(摆放.ToArray(), true);
        tilemap.CompressBounds();

        PrefabUtility.SaveAsPrefabAsset(内容, 预制件路径);
        PrefabUtility.UnloadPrefabContents(内容);

        Debug.Log($"[瓦片地图] 调色板 {名字}：{块列表.Count} 块，{摆放.Count} 格 → {预制件路径}");
    }

    // ---------- 小工具 ----------

    static void 创建目录(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string 父 = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string 名 = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(父) && !AssetDatabase.IsValidFolder(父))
            创建目录(父);
        AssetDatabase.CreateFolder(父, 名);
    }

    static int 序号(string name)
    {
        int i = name.LastIndexOf('_');
        return i >= 0 && int.TryParse(name.Substring(i + 1), out int n) ? n : 0;
    }
}
#endif
