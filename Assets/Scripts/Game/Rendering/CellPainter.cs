using System;
using Swan.Core;
using Swan.Core.Data;
using Swan.Core.Sim;
using UnityEngine;

namespace Swan.Game.Rendering
{
    /// <summary>
    /// 把一格的数值画成像素。渲染层和「导出 PNG 证据」工具共用这一段，
    /// 所以截图里的图就是游戏里真的那张图，不是另画一张。
    ///
    /// 所有尺寸/颜色都是参数，代码里没有业务数值。
    /// </summary>
    public static class CellPainter
    {
        /// <summary>地形底色：整格填满。</summary>
        public static void PaintGround(Color32[] tile, int cellPixelSize, Color32 color)
        {
            int count = cellPixelSize * cellPixelSize;
            for (int i = 0; i < count; i++) tile[i] = color;
        }

        /// <summary>
        /// 叠加层：植物方块 + 热源标记。
        /// 植物边长从 plantMinSizeRatio（刚种下）线性长到 plantSizeRatio（成熟），
        /// 成熟时加一圈 marker 边框。两个比例都来自 config。
        /// </summary>
        public static void PaintDecor(
            Color32[] tile,
            int cellPixelSize,
            Cell cell,
            MapPalette palette,
            EngineConfig config)
        {
            Clear(tile, cellPixelSize);

            if (cell.HasPlant)
            {
                float ripe = config.RipeThreshold;
                float fraction = ripe <= 0f ? 1f : Mathf.Clamp01(cell.Growth / ripe);
                float sizeRatio = Mathf.Lerp(config.PlantMinSizeRatio, config.PlantSizeRatio, fraction);
                int side = Mathf.RoundToInt(cellPixelSize * sizeRatio);
                side = Mathf.Clamp(side, 1, cellPixelSize);
                int offset = (cellPixelSize - side) / 2;
                Color32 color = palette.PlantColor(cell.PlantTypeId);

                FillRect(tile, cellPixelSize, offset, offset, side, side, color);

                if (cell.Harvestable)
                {
                    Color32 marker = palette.Marker;
                    StrokeRect(tile, cellPixelSize, offset, offset, side, side, marker);
                }
            }

            if (cell.HasHeatSource)
            {
                Color32 marker = palette.Marker;
                int center = cellPixelSize / 2;
                int arm = Mathf.Max(1, cellPixelSize / 8);
                FillRect(tile, cellPixelSize, center - arm, center - arm, arm * 2 + 1, 1, marker);
                FillRect(tile, cellPixelSize, center - arm, center - arm, 1, arm * 2 + 1, marker);
            }
        }

        /// <summary>
        /// 「你现在控制的格」高亮框：四边描框 + 中心点，颜色 / 边宽 / 透明度全部来自 config。
        /// 它画在独立的精灵层上（不参与脏格重绘）：鼠标一直在动，每帧都要重画。
        /// </summary>
        public static void PaintCursor(Color32[] tile, int cellPixelSize, Color32 color, int thickness, float alpha)
        {
            Clear(tile, cellPixelSize);

            byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f);
            if (a == 0) return;
            var c = new Color32(color.r, color.g, color.b, a);

            int t = Mathf.Clamp(thickness, 1, Mathf.Max(1, cellPixelSize / 2));
            for (int i = 0; i < t; i++)
            {
                StrokeRect(tile, cellPixelSize, i, i, cellPixelSize - i * 2, cellPixelSize - i * 2, c);
            }

            int center = cellPixelSize / 2;
            FillRect(tile, cellPixelSize, center - 1, center, 3, 1, c);
            FillRect(tile, cellPixelSize, center, center - 1, 1, 3, c);
        }

        /// <summary>温度叠加层的一个像素：温度越高越红，alpha 越大。</summary>
        public static Color32 HeatPixel(Color32 heatColor, float temperature, float startTemp, float fullTemp)
        {
            float t = fullTemp <= startTemp ? 0f : Mathf.Clamp01((temperature - startTemp) / (fullTemp - startTemp));
            if (t <= 0f) return new Color32(0, 0, 0, 0);
            return new Color32(heatColor.r, heatColor.g, heatColor.b, (byte)Mathf.RoundToInt(t * 255f));
        }

        public static void Clear(Color32[] tile, int cellPixelSize)
        {
            int count = cellPixelSize * cellPixelSize;
            for (int i = 0; i < count; i++) tile[i] = new Color32(0, 0, 0, 0);
        }

        static void FillRect(Color32[] tile, int width, int x, int y, int w, int h, Color32 color)
        {
            int x1 = Mathf.Clamp(x, 0, width);
            int y1 = Mathf.Clamp(y, 0, width);
            int x2 = Mathf.Clamp(x + w, 0, width);
            int y2 = Mathf.Clamp(y + h, 0, width);
            for (int py = y1; py < y2; py++)
            {
                int row = py * width;
                for (int px = x1; px < x2; px++) tile[row + px] = color;
            }
        }

        static void StrokeRect(Color32[] tile, int width, int x, int y, int w, int h, Color32 color)
        {
            FillRect(tile, width, x, y, w, 1, color);
            FillRect(tile, width, x, y + h - 1, w, 1, color);
            FillRect(tile, width, x, y, 1, h, color);
            FillRect(tile, width, x + w - 1, y, 1, h, color);
        }
    }
}
