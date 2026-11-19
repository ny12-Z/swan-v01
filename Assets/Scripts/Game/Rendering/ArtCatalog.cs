using System;
using System.Collections.Generic;
using System.IO;
using Swan.Core;
using Swan.Core.Data;
using UnityEngine;

namespace Swan.Game.Rendering
{
    /// <summary>
    /// 运行时读取 StreamingAssets/Art 下的 PNG，并把 art.json 的像素坐标翻译成可用的图块/图标。
    /// 这样做的好处：美术资源不需要 Unity 的 Sprite 导入设置，换图只要换 PNG + 改 JSON。
    /// </summary>
    public sealed class ArtCatalog
    {
        public const string ManifestName = "art.json";

        public sealed class TileRef
        {
            public string File;
            public int X;
            public int Y;
            public int W;
            public int H;
            public Color32 Tint = new Color32(255, 255, 255, 255);
        }

        public sealed class PlantRef
        {
            public string TypeId;
            public string File;
            public int Row;
            public Color32 Tint = new Color32(255, 255, 255, 255);
            public int[] Stages = { 1, 2, 3, 4 };
            public int Cell;
        }

        public sealed class PlayerRef
        {
            public string Sheet;
            public int Cell = 48;
            public int X = 16;
            public int Y = 16;
            public int W = 16;
            public int H = 16;
            public int Frames = 4;
            public int Directions = 4;
        }

        public sealed class IconRef
        {
            public string File;
            public int X;
            public int Y;
            public int W = 16;
            public int H = 16;
        }

        /// <summary>地面小装饰（花/蘑菇/石头）。按格哈希决定是否出现，同一张地图每次一致。</summary>
        public sealed class ScatterRef
        {
            public IconRef Icon;
            public float Chance;
            public int[] Terrains = Array.Empty<int>();
        }

        /// <summary>树/灌木/石头等大件景物；用独立 SpriteRenderer 画，可以跨多格。</summary>
        public sealed class PropRef
        {
            public string File;
            public int X;
            public int Y;
            public int W = 16;
            public int H = 16;
            public float Chance;
            public int[] Terrains = Array.Empty<int>();
            public float OffsetX;
            public float OffsetY;
        }

        readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        readonly Dictionary<int, TileRef> _ground = new Dictionary<int, TileRef>();
        readonly List<ScatterRef> _scatter = new List<ScatterRef>();
        readonly List<PropRef> _props = new List<PropRef>();
        readonly Dictionary<string, Sprite> _propSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        readonly Dictionary<string, PlantRef> _plants = new Dictionary<string, PlantRef>(StringComparer.Ordinal);
        readonly Dictionary<string, IconRef> _actionIcons = new Dictionary<string, IconRef>(StringComparer.Ordinal);
        readonly Dictionary<int, Texture2D> _playerFrames = new Dictionary<int, Texture2D>();
        readonly Dictionary<string, Sprite> _playerSprites = new Dictionary<string, Sprite>();
        PlayerRef _player;
        string _cursorFile;
        string _root;
        public string Error { get; private set; }

        public bool HasArt => _ground.Count > 0;

        public static ArtCatalog TryLoad(string artDirectory)
        {
            try
            {
                return Load(artDirectory);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SWAN] 美术目录不可用，退回色块渲染：{ex.Message}");
                return null;
            }
        }

        public static ArtCatalog Load(string artDirectory)
        {
            if (string.IsNullOrEmpty(artDirectory) || !Directory.Exists(artDirectory))
            {
                throw new DirectoryNotFoundException($"找不到美术目录：{artDirectory}");
            }

            string manifest = Path.Combine(artDirectory, ManifestName);
            if (!File.Exists(manifest)) throw new FileNotFoundException($"找不到 {ManifestName}", manifest);

            var catalog = new ArtCatalog { _root = artDirectory };
            JsonValue root = Json.Parse(File.ReadAllText(manifest), manifest);

            JsonValue ground = root.Member("ground");
            if (ground != null && ground.Kind == JsonKind.Array && ground.Items != null)
            {
                foreach (var item in ground.Items)
                {
                    int id = Int(item, "id");
                    if (id <= 0) continue;
                    catalog._ground[id] = ReadTile(item);
                }
            }

            JsonValue plants = root.Member("plants");
            if (plants != null && plants.Kind == JsonKind.Array && plants.Items != null)
            {
                foreach (var item in plants.Items)
                {
                    string typeId = Text(item, "typeId");
                    if (string.IsNullOrEmpty(typeId)) continue;
                    var plant = new PlantRef
                    {
                        TypeId = typeId,
                        File = Text(item, "file"),
                        Row = Int(item, "row"),
                        Tint = Tint(item)
                    };
                    JsonValue stages = item.Member("stages");
                    if (stages != null && stages.Kind == JsonKind.Array && stages.Items != null && stages.Items.Count > 0)
                    {
                        plant.Stages = new int[stages.Items.Count];
                        for (int i = 0; i < stages.Items.Count; i++) plant.Stages[i] = (int)stages.Items[i].Number;
                    }
                    catalog._plants[typeId] = plant;
                }
            }

            JsonValue player = root.Member("player");
            if (player != null)
            {
                catalog._player = new PlayerRef
                {
                    Sheet = Text(player, "sheet"),
                    Cell = Int(player, "cell", 48),
                    X = Int(player, "spriteX", 16),
                    Y = Int(player, "spriteY", 16),
                    W = Int(player, "spriteW", 16),
                    H = Int(player, "spriteH", 16),
                    Frames = Int(player, "frames", 4),
                    Directions = Int(player, "directions", 4)
                };
            }

            JsonValue actions = root.Member("actions");
            if (actions != null && actions.Kind == JsonKind.Array && actions.Items != null)
            {
                foreach (var item in actions.Items)
                {
                    string id = Text(item, "id");
                    if (string.IsNullOrEmpty(id)) continue;
                    catalog._actionIcons[id] = new IconRef
                    {
                        File = Text(item, "file"),
                        X = Int(item, "x"),
                        Y = Int(item, "y"),
                        W = Int(item, "w", 16),
                        H = Int(item, "h", 16)
                    };
                }
            }

            JsonValue scatter = root.Member("scatter");
            if (scatter != null && scatter.Kind == JsonKind.Array && scatter.Items != null)
            {
                foreach (var item in scatter.Items)
                {
                    var entry = new ScatterRef
                    {
                        Icon = new IconRef
                        {
                            File = Text(item, "file"),
                            X = Int(item, "x"),
                            Y = Int(item, "y"),
                            W = Int(item, "w", 16),
                            H = Int(item, "h", 16)
                        },
                        Chance = Number(item, "chance", 0f)
                    };
                    JsonValue terrains = item.Member("terrains");
                    if (terrains != null && terrains.Kind == JsonKind.Array && terrains.Items != null)
                    {
                        entry.Terrains = new int[terrains.Items.Count];
                        for (int i = 0; i < terrains.Items.Count; i++)
                            entry.Terrains[i] = (int)terrains.Items[i].Number;
                    }
                    if (!string.IsNullOrEmpty(entry.Icon.File) && entry.Chance > 0f) catalog._scatter.Add(entry);
                }
            }

            JsonValue props = root.Member("props");
            if (props != null && props.Kind == JsonKind.Array && props.Items != null)
            {
                foreach (var item in props.Items)
                {
                    var entry = new PropRef
                    {
                        File = Text(item, "file"),
                        X = Int(item, "x"),
                        Y = Int(item, "y"),
                        W = Int(item, "w", 16),
                        H = Int(item, "h", 16),
                        Chance = Number(item, "chance", 0f),
                        OffsetX = Number(item, "offsetX", 0f),
                        OffsetY = Number(item, "offsetY", 0f)
                    };
                    JsonValue terrains = item.Member("terrains");
                    if (terrains != null && terrains.Kind == JsonKind.Array && terrains.Items != null)
                    {
                        entry.Terrains = new int[terrains.Items.Count];
                        for (int i = 0; i < terrains.Items.Count; i++)
                            entry.Terrains[i] = (int)terrains.Items[i].Number;
                    }
                    if (!string.IsNullOrEmpty(entry.File) && entry.Chance > 0f) catalog._props.Add(entry);
                }
            }

            JsonValue cursor = root.Member("cursor");
            if (cursor != null) catalog._cursorFile = Text(cursor, "file");

            return catalog;
        }

        static TileRef ReadTile(JsonValue item)
        {
            return new TileRef
            {
                File = Text(item, "file"),
                X = Int(item, "x"),
                Y = Int(item, "y"),
                W = Int(item, "w", 16),
                H = Int(item, "h", 16),
                Tint = Tint(item)
            };
        }

        static string Text(JsonValue item, string key)
        {
            var value = item.Member(key);
            return value != null && value.Kind == JsonKind.String ? value.Text : null;
        }

        static int Int(JsonValue item, string key, int fallback = 0)
        {
            var value = item.Member(key);
            return value != null && value.Kind == JsonKind.Number ? (int)value.Number : fallback;
        }

        static Color32 Tint(JsonValue item)
        {
            string text = Text(item, "tint");
            if (!string.IsNullOrEmpty(text) && GameColor.TryParse(text, out Color32 color)) return color;
            return new Color32(255, 255, 255, 255);
        }

        static float Number(JsonValue item, string key, float fallback = 0f)
        {
            var value = item.Member(key);
            return value != null && value.Kind == JsonKind.Number ? (float)value.Number : fallback;
        }

        public Texture2D GetTexture(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (_textures.TryGetValue(file, out var cached)) return cached;

            string path = Path.Combine(_root, file);
            if (!File.Exists(path))
            {
                Error = $"缺少图片：{file}";
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                UnityEngine.Object.Destroy(texture);
                Error = $"图片解不开：{file}";
                return null;
            }

            texture.name = file;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            _textures[file] = texture;
            return texture;
        }

        public bool TryGetGround(int terrainId, out TileRef tile) => _ground.TryGetValue(terrainId, out tile);

        public bool TryGetPlant(string typeId, out PlantRef plant) => _plants.TryGetValue(typeId, out plant);

        public bool TryGetItemIcon(ItemRow item, out IconRef icon)
        {
            icon = null;
            if (item == null || string.IsNullOrEmpty(item.IconFile)) return false;
            icon = new IconRef { File = item.IconFile, X = item.SrcX, Y = item.SrcY, W = item.SrcW, H = item.SrcH };
            return true;
        }

        public bool TryGetActionIcon(string actionId, out IconRef icon) => _actionIcons.TryGetValue(actionId, out icon);

        public bool TryGetStructureIcon(StructureRow structure, out IconRef icon)
        {
            icon = null;
            if (structure == null || string.IsNullOrEmpty(structure.SpriteFile)) return false;
            icon = new IconRef
            {
                File = structure.SpriteFile,
                X = structure.SrcX,
                Y = structure.SrcY,
                W = structure.SrcW,
                H = structure.SrcH
            };
            return true;
        }

        /// <summary>把一块图片直接写进表现层纹理（dest 是 Unity 的 bottom-up 像素数组）。</summary>
        public bool Blit(IconRef icon, Color32[] dest, int destWidth, int cellX, int cellY, int cellPixels,
            int pixelOffsetX = 0, int pixelOffsetY = 0, Color32? tintOverride = null)
        {
            if (icon == null) return false;
            Texture2D texture = GetTexture(icon.File);
            if (texture == null) return false;

            Color32[] src = texture.GetPixels32();
            int texW = texture.width;
            int texH = texture.height;
            // PNG 坐标是左上角起，Texture2D 像素是左下角起：先把源矩形翻到左下角系。
            int srcLeft = icon.X;
            int srcBottom = texH - icon.Y - icon.H;
            Color32 tint = tintOverride ?? new Color32(255, 255, 255, 255);
            bool any = false;

            for (int dy = 0; dy < icon.H; dy++)
            {
                int sy = srcBottom + dy;
                if (sy < 0 || sy >= texH) continue;
                int destY = cellY * cellPixels + dy + pixelOffsetY;
                if (destY < 0) continue;
                int row = destY * destWidth;
                for (int dx = 0; dx < icon.W; dx++)
                {
                    int sx = srcLeft + dx;
                    if (sx < 0 || sx >= texW) continue;
                    Color32 c = src[sy * texW + sx];
                    if (c.a == 0) continue;
                    int destX = cellX * cellPixels + dx + pixelOffsetX;
                    if (destX < 0 || destX >= destWidth) continue;
                    c = Multiply(c, tint);
                    if (c.a == 255 || dest[destY * destWidth + destX].a == 0)
                    {
                        dest[row + destX] = c;
                    }
                    else
                    {
                        dest[row + destX] = AlphaOver(c, dest[row + destX]);
                    }
                    any = true;
                }
            }

            return any;
        }

        public bool BlitTile(TileRef tile, Color32[] dest, int destWidth, int cellX, int cellY, int cellPixels)
        {
            if (tile == null) return false;
            return Blit(new IconRef { File = tile.File, X = tile.X, Y = tile.Y, W = tile.W, H = tile.H },
                dest, destWidth, cellX, cellY, cellPixels, 0, 0, tile.Tint);
        }

        /// <summary>按格坐标哈希撒一个地面小装饰；同格每次结果一致，不会闪。</summary>
        public bool TryBlitScatter(int terrainId, int cellX, int cellY, Color32[] dest, int cellPixels)
        {
            if (_scatter.Count == 0) return false;
            uint h = (uint)(cellX * 73856093) ^ (uint)(cellY * 19349663) ^ 0x9e3779b9u;
            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;
            float roll = (h & 0xFFFFu) / 65535f;

            for (int i = 0; i < _scatter.Count; i++)
            {
                var entry = _scatter[i];
                if (roll >= entry.Chance) continue;
                bool terrainOk = entry.Terrains == null || entry.Terrains.Length == 0;
                for (int t = 0; !terrainOk && t < entry.Terrains.Length; t++)
                    if (entry.Terrains[t] == terrainId) terrainOk = true;
                if (!terrainOk) continue;
                return Blit(entry.Icon, dest, cellPixels, 0, 0, cellPixels);
            }

            return false;
        }

        /// <summary>按格哈希挑一个大件景物（树/灌木/石头）；没有命中就返回 false。</summary>
        public bool TryGetProp(int terrainId, int cellX, int cellY, out PropRef prop)
        {
            prop = null;
            if (_props.Count == 0) return false;
            uint h = (uint)(cellX * 83492791) ^ (uint)(cellY * 2654435761) ^ 0x85ebca6bu;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            float roll = (h & 0xFFFFu) / 65535f;

            for (int i = 0; i < _props.Count; i++)
            {
                var entry = _props[i];
                if (roll >= entry.Chance) continue;
                bool terrainOk = entry.Terrains == null || entry.Terrains.Length == 0;
                for (int t = 0; !terrainOk && t < entry.Terrains.Length; t++)
                    if (entry.Terrains[t] == terrainId) terrainOk = true;
                if (!terrainOk) continue;
                prop = entry;
                return true;
            }
            return false;
        }

        public Sprite GetPropSprite(PropRef prop)
        {
            if (prop == null) return null;
            string key = prop.File + "|" + prop.X + "|" + prop.Y + "|" + prop.W + "|" + prop.H;
            if (_propSprites.TryGetValue(key, out var cached)) return cached;

            Texture2D atlas = GetTexture(prop.File);
            if (atlas == null) return null;
            int bottom = atlas.height - prop.Y - prop.H;
            if (bottom < 0 || prop.X < 0 || prop.X + prop.W > atlas.width || prop.H <= 0) return null;

            var texture = new Texture2D(prop.W, prop.H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = key
            };
            texture.SetPixels(atlas.GetPixels(prop.X, bottom, prop.W, prop.H));
            texture.Apply(false, false);

            var sprite = Sprite.Create(texture, new Rect(0, 0, prop.W, prop.H), new Vector2(0.5f, 0.06f), 16f);
            sprite.name = key;
            _propSprites[key] = sprite;
            return sprite;
        }

        public bool BlitPlant(PlantRef plant, float growthRatio, Color32[] dest, int destWidth, int cellX, int cellY,
            int cellPixels)
        {
            if (plant == null || plant.Stages == null || plant.Stages.Length == 0) return false;
            int stage = StageIndex(growthRatio, plant.Stages.Length);
            int column = plant.Stages[stage];
            int y = plant.Row * 16;
            var icon = new IconRef { File = plant.File, X = column * 16, Y = y, W = 16, H = 16 };
            return Blit(icon, dest, destWidth, cellX, cellY, cellPixels, 0, 0, plant.Tint);
        }

        static int StageIndex(float growthRatio, int stageCount)
        {
            if (stageCount <= 1) return 0;
            if (growthRatio < 0.25f) return 0;
            if (growthRatio < 0.55f) return Math.Min(1, stageCount - 1);
            if (growthRatio < 0.9f) return Math.Min(2, stageCount - 1);
            return stageCount - 1;
        }

        public Texture2D GetPlayerFrame(int direction, int frame)
        {
            if (_player == null) return null;
            direction = Mathf.Clamp(direction, 0, _player.Directions - 1);
            frame = Mathf.Clamp(frame, 0, _player.Frames - 1);
            int key = direction * 100 + frame;
            if (_playerFrames.TryGetValue(key, out var cached)) return cached;

            Texture2D sheet = GetTexture(_player.Sheet);
            if (sheet == null) return null;

            int left = frame * _player.Cell + _player.X;
            int top = direction * _player.Cell + _player.Y;
            int bottom = sheet.height - top - _player.H;
            var frameTexture = new Texture2D(_player.W, _player.H, TextureFormat.RGBA32, false);
            Color32[] pixels = sheet.GetPixels32();
            var copy = new Color32[_player.W * _player.H];
            for (int y = 0; y < _player.H; y++)
            {
                for (int x = 0; x < _player.W; x++)
                {
                    int sx = left + x;
                    int sy = bottom + y;
                    if (sx >= 0 && sx < sheet.width && sy >= 0 && sy < sheet.height)
                    {
                        copy[y * _player.W + x] = pixels[sy * sheet.width + sx];
                    }
                }
            }

            frameTexture.SetPixels32(copy);
            frameTexture.filterMode = FilterMode.Point;
            frameTexture.wrapMode = TextureWrapMode.Clamp;
            frameTexture.Apply(false, false);
            _playerFrames[key] = frameTexture;
            return frameTexture;
        }

        public Sprite GetPlayerSprite(int direction, int frame)
        {
            string key = $"{direction}:{frame}";
            if (_playerSprites.TryGetValue(key, out var cached)) return cached;
            Texture2D texture = GetPlayerFrame(direction, frame);
            if (texture == null) return null;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.08f), 16f, 0, SpriteMeshType.FullRect);
            _playerSprites[key] = sprite;
            return sprite;
        }

        public Texture2D CursorTexture => string.IsNullOrEmpty(_cursorFile) ? null : GetTexture(_cursorFile);

        public bool TryGetUv(IconRef icon, out Texture2D texture, out Rect uv)
        {
            texture = null;
            uv = default;
            if (icon == null) return false;
            texture = GetTexture(icon.File);
            if (texture == null) return false;
            uv = new Rect(
                (float)icon.X / texture.width,
                (float)(texture.height - icon.Y - icon.H) / texture.height,
                (float)icon.W / texture.width,
                (float)icon.H / texture.height);
            return true;
        }

        static Color32 Multiply(Color32 c, Color32 tint)
        {
            return new Color32(
                (byte)(c.r * tint.r / 255),
                (byte)(c.g * tint.g / 255),
                (byte)(c.b * tint.b / 255),
                c.a);
        }

        static Color32 AlphaOver(Color32 top, Color32 bottom)
        {
            int a = top.a;
            int inv = 255 - a;
            return new Color32(
                (byte)((top.r * a + bottom.r * inv) / 255),
                (byte)((top.g * a + bottom.g * inv) / 255),
                (byte)((top.b * a + bottom.b * inv) / 255),
                (byte)Mathf.Min(255, a + bottom.a * inv / 255));
        }
    }
}
