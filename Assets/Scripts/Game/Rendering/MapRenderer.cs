using System.Collections.Generic;
using Swan.Core;
using Swan.Core.Data;
using Swan.Core.Sim;
using UnityEngine;

namespace Swan.Game.Rendering
{
    /// <summary>
    /// 地图渲染：地形 / 装饰 / 交互范围 / 温度 / 光标，各自一个 SpriteRenderer。
    ///
    /// 为什么用纹理而不是 2500 个 GameObject：
    /// 1 格 = cellPixelSize 像素的方块；
    /// 脏格重绘只需要写那 16×16 个像素，环境和表现之间是明确的「脏 → 重绘」契约。
    /// 有美术目录时把 PNG 图块 blit 进同一张纹理，没有时退回色块；两条路径共用脏格机制。
    /// </summary>
    public sealed class MapRenderer : MonoBehaviour
    {
        const string GroundName = "Ground";
        const string DecorName = "Decor";
        const string ZoneName = "Zone";
        const string HeatName = "Heat";
        const string CursorName = "Cursor";

        /// <summary>
        /// 精灵材质由场景传入（场景生成器会创建 Assets/Materials/MapSprite.mat）。
        /// 之所以不能留空：SpriteRenderer 没有材质时会用「错误着色器」画成洋红，
        /// 而打包后 Shader.Find 可能因为剔除而找不到，所以优先用场景里序列化的引用。
        /// </summary>
        [SerializeField] Material _spriteMaterial;

        static Material _runtimeMaterial;

        World _world;
        DataBundle _data;
        MapPalette _palette;
        ArtCatalog _art;

        Texture2D _groundTexture;
        Texture2D _decorTexture;
        Texture2D _zoneTexture;
        Texture2D _heatTexture;
        Texture2D _cursorTexture;

        SpriteRenderer _groundRenderer;
        SpriteRenderer _decorRenderer;
        SpriteRenderer _zoneRenderer;
        SpriteRenderer _heatRenderer;
        SpriteRenderer _cursorRenderer;

        Color32[] _heatPixels;
        Color32[] _tile;
        Color32[] _zonePixels;
        Color32[] _cursorPixels;
        int _cursorThickness;
        /// <summary>SetPixels 只收 Color[]，这里放一块复用的转换缓冲，避免每格都 new 一次。</summary>
        Color[] _colorScratch;

        int _cellPixelSize;
        int _groundWidth;
        int _groundHeight;

        public SpriteRenderer GroundRenderer => _groundRenderer;

        public ArtCatalog Art => _art;

        void Awake()
        {
            EnsureLayerRenderers();
        }

        /// <summary>绑定世界与数据（首次进入、热重载后都调这个）。测试用这个重载。</summary>
        public void Bind(World world, DataBundle data)
        {
            Bind(world, data, null);
        }

        /// <summary>绑定世界、数据与美术目录。art 传 null 时自动尝试加载 StreamingAssets/Art。</summary>
        public void Bind(World world, DataBundle data, ArtCatalog art)
        {
            _world = world;
            _data = data;
            _palette = MapPalette.Build(data);
            _art = art;
            if (_art == null)
            {
                _art = ArtCatalog.TryLoad(DataLocator.ArtDirectory);
            }
            EnsureLayerRenderers();

            int px = data.Config.CellPixelSize;
            bool resize = _groundTexture == null
                          || _cellPixelSize != px
                          || _groundWidth != world.Width * px
                          || _groundHeight != world.Height * px;
            if (resize)
            {
                ReleaseTextures();
                CreateTextures(world.Width, world.Height, px);
            }

            RepaintAll();
        }

        /// <summary>只重画脏格（环境步推进后调用）。</summary>
        public void RepaintDirty()
        {
            if (_world == null || _groundTexture == null) return;
            var dirty = _world.DirtyIndices;
            if (dirty.Count == 0) return;

            for (int i = 0; i < dirty.Count; i++)
            {
                int index = dirty[i];
                int x = index % _world.Width;
                int y = index / _world.Width;
                PaintCell(x, y, _world.GetCellByIndex(index));
            }

            _groundTexture.Apply(false);
            _decorTexture.Apply(false);
            _world.ClearDirty();
        }

        /// <summary>温度叠加层：温度每步都在变，所以按整层刷新（50×50 像素，成本可忽略）。</summary>
        public void RepaintHeat()
        {
            if (_world == null || _heatTexture == null) return;
            var config = _data.Config;
            int count = _world.Width * _world.Height;
            for (int i = 0; i < count; i++)
            {
                var cell = _world.GetCellByIndex(i);
                _heatPixels[i] = CellPainter.HeatPixel(_palette.Heat, cell.Temperature,
                    config.HeatOverlayStartTemp, config.HeatOverlayFullTemp);
            }
            _heatTexture.SetPixels32(_heatPixels);
            _heatTexture.Apply(false);
        }

        /// <summary>
        /// 鼠标指向格：把「现在要操作的格」用高亮框标出来。
        /// 每帧调用（鼠标一直在动），只有 1 格像素在写，成本可以忽略。
        /// </summary>
        public void SetCursor(int x, int y, float alpha)
        {
            SetCursor(x, y, alpha, null);
        }

        /// <summary>可指定颜色：交互范围外用另一种颜色提醒。</summary>
        public void SetCursor(int x, int y, float alpha, Color32? overrideColor)
        {
            if (_cursorRenderer == null || _palette == null) return;

            int thickness = _data.Config.CursorLineThickness;
            if (_cursorTexture == null || thickness != _cursorThickness) RebuildCursorTexture(thickness);

            Color32 color = overrideColor ?? _palette.Cursor;
            CellPainter.PaintCursor(_cursorPixels, _cellPixelSize, color, thickness, alpha);
            _cursorTexture.SetPixels32(_cursorPixels);
            _cursorTexture.Apply(false);

            _cursorRenderer.enabled = true;
            _cursorRenderer.transform.localPosition = new Vector3(x, y, 0f);
        }

        public void HideCursor()
        {
            if (_cursorRenderer != null) _cursorRenderer.enabled = false;
        }

        /// <summary>
        /// 12×12 可交互范围：以玩家格为中心的一圈半透明高亮。
        /// 贴图按格像素分辨率画，边框只有 2px（一格 16px 的八分之一），
        /// 否则 50px/格 的屏幕上会出现一条一格粗的蓝带。只有玩家跨格时才重画。
        /// </summary>
        public void ShowZone(int centerX, int centerY, int width, int height, Color32 fill, Color32 border)
        {
            if (_world == null || _zoneTexture == null) return;
            int px = _cellPixelSize;
            int left = centerX - width / 2;
            int bottom = centerY - height / 2;
            int right = left + width - 1;
            int top = bottom + height - 1;
            int borderPixels = Mathf.Max(1, px / 8);

            int texWidth = _zoneTexture.width;
            int texHeight = _zoneTexture.height;
            for (int y = 0; y < texHeight; y++)
            {
                int row = y * texWidth;
                int cellY = y / px;
                for (int x = 0; x < texWidth; x++)
                {
                    int cellX = x / px;
                    bool inside = cellX >= left && cellX <= right && cellY >= bottom && cellY <= top;
                    if (!inside)
                    {
                        _zonePixels[row + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    int localX = x - left * px;
                    int localY = y - bottom * px;
                    int localRight = width * px - 1;
                    int localTop = height * px - 1;
                    bool edge = localX < borderPixels || localX > localRight - borderPixels
                        || localY < borderPixels || localY > localTop - borderPixels;
                    _zonePixels[row + x] = edge ? border : fill;
                }
            }

            _zoneTexture.SetPixels32(_zonePixels);
            _zoneTexture.Apply(false);
            _zoneRenderer.enabled = true;
        }

        public void HideZone()
        {
            if (_zoneRenderer != null) _zoneRenderer.enabled = false;
        }

#if UNITY_INCLUDE_TESTS
        /// <summary>测试用：光标层的像素（用来钉住「第一次 SetCursor 就真的画得出来」）。</summary>
        public Texture2D CursorTextureForTests => _cursorTexture;
#endif

        void RebuildCursorTexture(int thickness)
        {
            ReleaseCursorTexture();
            int px = _cellPixelSize;
            _cursorThickness = thickness;
            _cursorPixels = new Color32[px * px];
            _cursorTexture = CreateTexture(px, px);
            _cursorTexture.name = "SwanCursor";
            _cursorRenderer.sprite = CreateSprite(_cursorTexture, px);
        }

        public void RepaintAll()
        {
            if (_world == null || _groundTexture == null) return;

            int count = _world.Width * _world.Height;
            for (int i = 0; i < count; i++)
            {
                int x = i % _world.Width;
                int y = i / _world.Width;
                PaintCell(x, y, _world.GetCellByIndex(i));
            }

            _groundTexture.Apply(false);
            _decorTexture.Apply(false);
            _world.ClearDirty();
            RepaintHeat();
        }

        void PaintCell(int x, int y, Cell cell)
        {
            var config = _data.Config;
            MapPalette palette = _palette;
            int px = _cellPixelSize;

            bool paintedGround = false;
            if (_art != null && _art.TryGetGround(cell.TerrainId, out var ground))
            {
                paintedGround = _art.BlitTile(ground, _tile, px, 0, 0, px);
            }
            if (!paintedGround)
            {
                CellPainter.PaintGround(_tile, px, palette.TerrainColor(cell.TerrainId));
            }
            // 先把地形写进纹理，再让 _tile 被装饰层复用（否则会把植物画到地形层上）
            UploadTile(_groundTexture, x * px, y * px, px);

            bool paintedDecor = false;
            if (_art != null && cell.HasStructure && _data.Structures.TryGetRow(cell.StructureId, out var structure))
            {
                if (_art.TryGetStructureIcon(structure, out var structureIcon))
                {
                    CellPainter.Clear(_tile, px);
                    Color32? tint = null;
                    if (GameColor.TryParse(structure.TintHex, out var structureTint)) tint = structureTint;
                    paintedDecor = _art.Blit(structureIcon, _tile, px, 0, 0, px, 0, 0, tint);
                }
            }

            if (!paintedDecor && _art != null && cell.HasPlant
                && _data.Plants.TryGetRow(cell.PlantTypeId, out var plantType)
                && _art.TryGetPlant(plantType.TypeId, out var plantArt))
            {
                CellPainter.Clear(_tile, px);
                float ripe = config.RipeThreshold;
                float ratio = ripe <= 0f ? 1f : Mathf.Clamp01(cell.Growth / ripe);
                paintedDecor = _art.BlitPlant(plantArt, ratio, _tile, px, 0, 0, px);
            }

            if (!paintedDecor && _art != null && cell.HasForage
                && _data.Items.TryGet(cell.ForageItemId, out var forageItem)
                && _art.TryGetItemIcon(forageItem, out var forageIcon))
            {
                CellPainter.Clear(_tile, px);
                paintedDecor = _art.Blit(forageIcon, _tile, px, 0, 0, px);
            }

            if (!paintedDecor)
            {
                bool scattered = false;
                if (_art != null)
                {
                    CellPainter.Clear(_tile, px);
                    scattered = _art.TryBlitScatter(cell.TerrainId, x, y, _tile, px);
                }
                if (scattered)
                {
                    paintedDecor = true;
                }
                else
                {
                    CellPainter.PaintDecor(_tile, px, cell, palette, config);
                }
            }

            if (paintedDecor && cell.HasHeatSource)
            {
                CellPainter.PaintHeatMarker(_tile, px, palette.Marker);
            }

            UploadTile(_decorTexture, x * px, y * px, px);
        }

        /// <summary>把 _tile 里 px×px 的数据写进纹理（Color32 → Color 需要一次转换）。</summary>
        void UploadTile(Texture2D texture, int left, int bottom, int px)
        {
            int count = px * px;
            if (_colorScratch == null || _colorScratch.Length != count) _colorScratch = new Color[count];
            for (int i = 0; i < count; i++) _colorScratch[i] = _tile[i];
            texture.SetPixels(left, bottom, px, px, _colorScratch);
        }

        void EnsureLayerRenderers()
        {
            // 可交互范围在装饰层下面：否则植物/野物会被蓝色蒙一层，看起来像坏图。
            if (_groundRenderer == null) _groundRenderer = EnsureLayer(GroundName, 0);
            if (_zoneRenderer == null) _zoneRenderer = EnsureLayer(ZoneName, 1);
            if (_decorRenderer == null) _decorRenderer = EnsureLayer(DecorName, 2);
            if (_heatRenderer == null) _heatRenderer = EnsureLayer(HeatName, 3);
            if (_cursorRenderer == null) _cursorRenderer = EnsureLayer(CursorName, 4);
        }

        SpriteRenderer EnsureLayer(string layerName, int sortingOrder)
        {
            var child = transform.Find(layerName);
            GameObject go;
            if (child == null)
            {
                go = new GameObject(layerName);
                go.transform.SetParent(transform, false);
            }
            else
            {
                go = child.gameObject;
            }

            var renderer = go.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = sortingOrder;
            renderer.color = Color.white;
            renderer.sharedMaterial = ResolveMaterial();
            return renderer;
        }

        /// <summary>场景里给了材质就用它；没给（比如测试里临时建的）才自己造一个。</summary>
        Material ResolveMaterial()
        {
            if (_spriteMaterial != null) return _spriteMaterial;
            if (_runtimeMaterial == null) _runtimeMaterial = CreateRuntimeMaterial();
            return _runtimeMaterial;
        }

        static Material CreateRuntimeMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("[SWAN] 找不到精灵着色器，地图会画不出来（需要 Sprite-Unlit-Default 或 Sprites/Default）");
                return null;
            }

            return new Material(shader) { name = "SwanMapSprite", hideFlags = HideFlags.DontSave };
        }

        void CreateTextures(int mapWidth, int mapHeight, int cellPixelSize)
        {
            _cellPixelSize = cellPixelSize;
            _groundWidth = mapWidth * cellPixelSize;
            _groundHeight = mapHeight * cellPixelSize;

            _groundTexture = CreateTexture(_groundWidth, _groundHeight);
            _decorTexture = CreateTexture(_groundWidth, _groundHeight);
            _zoneTexture = CreateTexture(_groundWidth, _groundHeight);
            _heatTexture = CreateTexture(mapWidth, mapHeight);

            _heatPixels = new Color32[mapWidth * mapHeight];
            _zonePixels = new Color32[_groundWidth * _groundHeight];
            _tile = new Color32[cellPixelSize * cellPixelSize];

            _groundRenderer.sprite = CreateSprite(_groundTexture, cellPixelSize);
            _decorRenderer.sprite = CreateSprite(_decorTexture, cellPixelSize);
            // 可交互范围要画细边框，所以用整张格像素纹理；温度叠加仍是每格 1 像素
            _zoneRenderer.sprite = CreateSprite(_zoneTexture, cellPixelSize);
            _zoneRenderer.enabled = false;
            _heatRenderer.sprite = CreateSprite(_heatTexture, 1f);
            // 光标层是单独一张 1 格纹理：它的内容每帧都在变，不能和脏格重绘混在一起
            _cursorThickness = 0;
            HideCursor();
        }

        static Texture2D CreateTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                name = "SwanMap",
            };
            return texture;
        }

        static Sprite CreateSprite(Texture2D texture, float pixelsPerUnit)
        {
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                Vector2.zero, pixelsPerUnit, 0, SpriteMeshType.FullRect);
        }

        void ReleaseTextures()
        {
            ReleaseSprite(_groundRenderer);
            ReleaseSprite(_decorRenderer);
            ReleaseSprite(_zoneRenderer);
            ReleaseSprite(_heatRenderer);
            ReleaseSprite(_cursorRenderer);

            DestroyTexture(ref _groundTexture);
            DestroyTexture(ref _decorTexture);
            DestroyTexture(ref _zoneTexture);
            DestroyTexture(ref _heatTexture);
            ReleaseCursorTexture();

            _heatPixels = null;
            _zonePixels = null;
            _tile = null;
        }

        void ReleaseCursorTexture()
        {
            ReleaseSprite(_cursorRenderer);
            DestroyTexture(ref _cursorTexture);
            _cursorPixels = null;
            _cursorThickness = 0;
        }

        void ReleaseSprite(SpriteRenderer renderer)
        {
            if (renderer == null || renderer.sprite == null) return;
            var sprite = renderer.sprite;
            renderer.sprite = null;
            Destroy(sprite);
        }

        void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null) return;
            Destroy(texture);
            texture = null;
        }

        void OnDestroy()
        {
            ReleaseTextures();
        }
    }
}
