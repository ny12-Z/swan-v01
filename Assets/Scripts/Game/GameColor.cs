using Swan.Core;
using UnityEngine;

namespace Swan.Game
{
    /// <summary>
    /// 把 Core 的 #RRGGBB 文本转成 Unity 颜色。
    /// Core 不引用 UnityEngine，所以解析放在表现层；这里只做格式翻译，不藏任何配色。
    /// </summary>
    public static class GameColor
    {
        public static bool TryParse(string hex, out Color32 color)
        {
            if (ColorHex.TryParse(hex, out float r, out float g, out float b))
            {
                color = new Color32(
                    (byte)Mathf.RoundToInt(r * 255f),
                    (byte)Mathf.RoundToInt(g * 255f),
                    (byte)Mathf.RoundToInt(b * 255f),
                    255);
                return true;
            }

            color = new Color32(255, 255, 255, 255);
            return false;
        }

        public static Color32 Parse(string hex, Color32 fallback)
        {
            return TryParse(hex, out var color) ? color : fallback;
        }

        public static Color ToColor(string hex, Color fallback)
        {
            if (ColorHex.TryParse(hex, out float r, out float g, out float b)) return new Color(r, g, b, 1f);
            return fallback;
        }
    }
}
