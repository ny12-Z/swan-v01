using System;
using System.Collections.Generic;
using Swan.Core;
using Swan.Core.Data;
using UnityEngine;

namespace Swan.Game.Rendering
{
    /// <summary>
    /// 数值表里的颜色 → 画面上用的颜色。颜色全部来自表/config，代码里只有「查不到时用 fallback」。
    /// 热重载时整个调色板重建，所以改 CSV 的颜色也会立刻生效。
    /// </summary>
    public sealed class MapPalette
    {
        readonly Dictionary<int, Color32> _terrain = new Dictionary<int, Color32>();
        readonly Dictionary<int, Color32> _plant = new Dictionary<int, Color32>();
        readonly Color32 _fallback;

        public Color32 Background { get; private set; }
        public Color32 Heat { get; private set; }
        public Color32 Marker { get; private set; }
        /// <summary>「你现在控制的格」高亮框颜色（玩家位置提示）。</summary>
        public Color32 Cursor { get; private set; }

        MapPalette(DataBundle data)
        {
            _fallback = Parse(data.Config.FallbackTerrainColor, Color.gray);
            Background = Parse(data.Config.BackgroundColor, Color.black);
            Heat = Parse(data.Config.HeatOverlayColor, Color.red);
            Marker = Parse(data.Config.MarkerColor, Color.white);
            Cursor = Parse(data.Config.CursorColor, new Color32(255, 212, 0, 255));

            foreach (var row in data.Terrain.Rows)
            {
                _terrain[row.TerrainId] = Parse(row.ColorHex, _fallback);
            }

            foreach (var row in data.Plants.Rows)
            {
                _plant[data.Plants.IndexOf(row.TypeId)] = Parse(row.ColorHex, Color.green);
            }
        }

        public static MapPalette Build(DataBundle data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return new MapPalette(data);
        }

        public Color32 TerrainColor(int terrainId)
        {
            return _terrain.TryGetValue(terrainId, out var color) ? color : _fallback;
        }

        public Color32 PlantColor(int plantTypeId)
        {
            return _plant.TryGetValue(plantTypeId, out var color) ? color : _fallback;
        }

        public static Color32 Parse(string hex, Color32 fallback)
        {
            if (!ColorHex.TryParse(hex, out var r, out var g, out var b)) return fallback;
            return new Color32(ToByte(r), ToByte(g), ToByte(b), 255);
        }

        static byte ToByte(float value)
        {
            int v = (int)Math.Round(value * 255f);
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return (byte)v;
        }
    }
}
