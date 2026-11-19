using Swan.Core;
using Swan.Core.Gameplay;
using Swan.Game.Rendering;
using UnityEngine;

namespace Swan.Game
{
    /// <summary>
    /// 主角表现层：独立 SpriteRenderer，位置直接读 PlayerState 的连续坐标。
    /// 逻辑（移动/碰撞）在 Core，这里只负责选帧和朝向；表现不反向驱动玩法。
    /// </summary>
    public sealed class PlayerView : MonoBehaviour
    {
        SpriteRenderer _renderer;
        SpriteRenderer _shadow;
        ArtCatalog _art;
        PlayerState _player;
        EngineConfig _config;
        float _animTime;
        Sprite _fallback;

        public void Bind(PlayerState player, EngineConfig config, ArtCatalog art)
        {
            _player = player;
            _config = config;
            _art = art;
            EnsureRenderers();
            ApplyFrame(0, false);
            SyncPosition();
        }

        public void ApplyConfig(EngineConfig config)
        {
            _config = config;
            if (_renderer != null && _config != null)
            {
                _renderer.transform.localScale = Vector3.one * Mathf.Max(0.1f, _config.PlayerSpriteScale);
            }
        }

        void EnsureRenderers()
        {
            if (_renderer == null)
            {
                var child = transform.Find("Sprite");
                GameObject go = child != null ? child.gameObject : new GameObject("Sprite");
                if (child == null) go.transform.SetParent(transform, false);
                _renderer = go.GetComponent<SpriteRenderer>();
                if (_renderer == null) _renderer = go.AddComponent<SpriteRenderer>();
                _renderer.sortingOrder = 20;
            }

            if (_shadow == null)
            {
                var child = transform.Find("Shadow");
                GameObject go = child != null ? child.gameObject : new GameObject("Shadow");
                if (child == null) go.transform.SetParent(transform, false);
                _shadow = go.GetComponent<SpriteRenderer>();
                if (_shadow == null) _shadow = go.AddComponent<SpriteRenderer>();
                _shadow.sortingOrder = 19;
                _shadow.sprite = BuildShadow();
                _shadow.color = new Color(0f, 0f, 0f, 0.28f);
                _shadow.transform.localPosition = new Vector3(0f, -0.32f, 0f);
                _shadow.transform.localScale = new Vector3(1.25f, 0.8f, 1f);
            }
        }

        void LateUpdate()
        {
            if (_player == null) return;
            SyncPosition();

            bool moving = _player.Moving;
            if (moving) _animTime += Time.deltaTime;
            else _animTime = 0f;

            int frame = 0;
            if (moving && _config != null)
            {
                float fps = Mathf.Max(0.01f, _config.PlayerAnimationFps);
                frame = 1 + (int)(_animTime * fps) % 3;
            }

            ApplyFrame(_player.Facing, moving, frame);
        }

        void SyncPosition()
        {
            transform.position = new Vector3(_player.X, _player.Y, 0f);
        }

        void ApplyFrame(int facing, bool moving, int frame = 0)
        {
            if (_renderer == null) return;

            if (_art != null)
            {
                Sprite sprite = _art.GetPlayerSprite(facing, moving ? frame : 0);
                if (sprite != null)
                {
                    _renderer.sprite = sprite;
                    _renderer.transform.localScale = Vector3.one * Mathf.Max(0.1f,
                        _config != null ? _config.PlayerSpriteScale : 1f);
                    return;
                }
            }

            if (_fallback == null) _fallback = BuildFallback();
            _renderer.sprite = _fallback;
            _renderer.transform.localScale = Vector3.one * Mathf.Max(0.1f,
                _config != null ? _config.PlayerSpriteScale : 1f);
        }

        static Sprite BuildFallback()
        {
            var texture = new Texture2D(10, 14, TextureFormat.RGBA32, false);
            var pixels = new Color32[10 * 14];
            var body = new Color32(242, 225, 190, 255);
            for (int y = 0; y < 14; y++)
            {
                for (int x = 0; x < 10; x++)
                {
                    if (y > 2 && x > 0 && x < 9) pixels[y * 10 + x] = body;
                }
            }
            texture.SetPixels32(pixels);
            texture.filterMode = FilterMode.Point;
            texture.Apply(false, false);
            return Sprite.Create(texture, new Rect(0, 0, 10, 14), new Vector2(0.5f, 0.08f), 16f, 0,
                SpriteMeshType.FullRect);
        }

        static Sprite BuildShadow()
        {
            var texture = new Texture2D(16, 8, TextureFormat.RGBA32, false);
            var pixels = new Color32[16 * 8];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    float dx = (x - 7.5f) / 8f;
                    float dy = (y - 3.5f) / 4f;
                    if (dx * dx + dy * dy <= 1f) pixels[y * 16 + x] = new Color32(255, 255, 255, 255);
                }
            }
            texture.SetPixels32(pixels);
            texture.filterMode = FilterMode.Point;
            texture.Apply(false, false);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 8), new Vector2(0.5f, 0.5f), 16f, 0,
                SpriteMeshType.FullRect);
            sprite.name = "SwanShadow";
            return sprite;
        }
    }
}
