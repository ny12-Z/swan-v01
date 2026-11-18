using Swan.Core;
using UnityEngine;

namespace Swan.Game
{
    /// <summary>
    /// 相机：平移 + 缩放，全部按 deltaTime 走（帧率无关），边界夹在地图内。
    /// 输入值由 Bootstrap 传进来，这里只负责「镜头怎么动」，不读键鼠。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class MapCameraController : MonoBehaviour
    {
        Camera _camera;
        EngineConfig _config;
        int _mapWidth;
        int _mapHeight;
        float _zoom = 1f;

        public float Zoom => _zoom;

        public void Initialize(EngineConfig config, int mapWidth, int mapHeight)
        {
            EnsureCamera();
            _config = config;
            _mapWidth = mapWidth;
            _mapHeight = mapHeight;
            _zoom = 1f;
            ApplyZoom();
            _camera.backgroundColor = ToColor(config.BackgroundColor);
            transform.position = new Vector3(mapWidth * 0.5f, mapHeight * 0.5f, -10f);
            ClampPosition();
        }

        /// <summary>热重载换了 config 后同步镜头参数（不改变当前位置）。</summary>
        public void ApplyConfig(EngineConfig config)
        {
            _config = config;
            EnsureCamera();
            _camera.backgroundColor = ToColor(config.BackgroundColor);
            ApplyZoom();
            ClampPosition();
        }

        /// <param name="pan">方向输入，-1..1（+x 向右，+y 向上）</param>
        /// <param name="zoomDelta">滚轮量（正 = 拉近）</param>
        public void Tick(float deltaTime, Vector2 pan, float zoomDelta)
        {
            if (_config == null) return;

            if (Mathf.Abs(zoomDelta) > 0.0001f)
            {
                float factor = 1f + Mathf.Sign(zoomDelta) * _config.CameraZoomStep;
                _zoom = Mathf.Clamp(_zoom * Mathf.Max(0.01f, factor), _config.ZoomMin, _config.ZoomMax);
                ApplyZoom();
                ClampPosition();
            }

            if (pan.sqrMagnitude > 0.0001f)
            {
                // 平移速度按「格/秒」定义，缩放后依然保持同样的手感
                float speed = _config.CameraPanSpeedCellsPerSecond / _zoom;
                Vector3 position = transform.position;
                position.x += pan.x * speed * deltaTime;
                position.y += pan.y * speed * deltaTime;
                transform.position = position;
                ClampPosition();
            }
        }

        public void Focus(int cellX, int cellY)
        {
            transform.position = new Vector3(cellX + 0.5f, cellY + 0.5f, transform.position.z);
            ClampPosition();
        }

        void EnsureCamera()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.transform.rotation = Quaternion.identity;
        }

        void ApplyZoom()
        {
            _camera.orthographicSize = _config.CameraOrthographicSize / _zoom;
        }

        /// <summary>镜头不许露出地图外的黑边；地图比视野小时则居中。</summary>
        void ClampPosition()
        {
            if (_camera == null || _config == null) return;

            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            Vector3 position = transform.position;

            position.x = ClampAxis(position.x, halfWidth, _mapWidth);
            position.y = ClampAxis(position.y, halfHeight, _mapHeight);
            position.z = -10f;
            transform.position = position;
        }

        static float ClampAxis(float value, float halfExtent, int mapSize)
        {
            if (mapSize <= halfExtent * 2f) return mapSize * 0.5f;
            return Mathf.Clamp(value, halfExtent, mapSize - halfExtent);
        }

        static Color ToColor(string hex)
        {
            if (ColorHex.TryParse(hex, out var r, out var g, out var b)) return new Color(r, g, b, 1f);
            return Color.black;
        }
    }
}
