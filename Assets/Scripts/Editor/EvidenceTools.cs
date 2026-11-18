using System;
using System.IO;
using Swan.Core;
using Swan.Core.Data;
using Swan.Core.Sim;
using Swan.Game;
using Swan.Game.Rendering;
using UnityEditor;
using UnityEngine;

namespace Swan.EditorTools
{
    /// <summary>
    /// 证据工具：把「数值层到底算得对不对」变成可以贴进交接单的文件。
    ///
    /// - 对照曲线：跑 p01-对照曲线 的 B1/B2，输出逐点数字（Docs/curve-probe.txt）
    /// - 地图快照：直接调 CellPainter 合成 PNG（Docs/screenshots/*.png），
    ///   不依赖 GPU/帧率/窗口，batchmode 也能出图，而且和游戏里画的是同一份代码。
    /// </summary>
    public static class EvidenceTools
    {
        const string OutputFolder = "Docs";
        const string ScreenshotFolder = OutputFolder + "/screenshots";
        const string PlantTypeId = "P01";

        [MenuItem("SWAN/A 导出对照曲线 (Docs/curve-probe.txt)", priority = 20)]
        public static void ExportCurveProbe()
        {
            DataBundle data = LoadData();
            Directory.CreateDirectory(OutputFolder);

            var text = new System.Text.StringBuilder();
            text.AppendLine("SWAN v0.1 对照曲线实测（由 EvidenceTools 生成，和 p01-对照曲线.md 逐点比）");
            text.AppendLine("口径说明：水 / 养分 / 地形 三列与文档逐点一致；生长列按创意总监裁定（G02 叠乘）为文档的 2 倍。");
            text.AppendLine("想回到文档原始生长口径：把 config.json 的 stackInitialGrowthTraits 改成 false。");
            text.AppendLine();

            text.AppendLine("B1 · 普通土地起手 30/30，无热源，防抖关闭");
            text.AppendLine(CurveProbe.Format(CurveProbe.Run(data, 30f, 30f, PlantTypeId, 8, useHysteresis: false)));
            text.AppendLine();

            text.AppendLine("B2 · 肥沃水田起手 50/50，无热源，防抖关闭");
            text.AppendLine(CurveProbe.Format(CurveProbe.Run(data, 50f, 50f, PlantTypeId, 25, useHysteresis: false)));
            text.AppendLine();

            text.AppendLine("B2 · 同一场景，防抖按 config.terrainHysteresis 生效（游戏里实际看到的：换档会晚几个点）");
            text.AppendLine(CurveProbe.Format(CurveProbe.Run(data, 50f, 50f, PlantTypeId, 25, useHysteresis: true)));

            string path = Path.Combine(OutputFolder, "curve-probe.txt");
            File.WriteAllText(path, text.ToString());
            Debug.Log($"[SWAN] 对照曲线已导出：{path}\n{text}");
        }

        [MenuItem("SWAN/B 导出地图截图 (Docs/screenshots)", priority = 21)]
        public static void ExportMapSnapshots()
        {
            DataBundle data = LoadData();
            Directory.CreateDirectory(ScreenshotFolder);

            var world = new World(data);
            Export(world, data, "01-initial.png");

            world.StepEnvironment(30);
            Export(world, data, "02-after-30-steps.png");

            world.StepEnvironment(90);
            Export(world, data, "03-after-120-steps.png");

            // 热源叠加：在地图中间偏下加一个热源，跑几步看红晕扩散
            world.SetHeatSource(world.Width / 2, world.Height / 2 - 6, true);
            world.StepEnvironment(20);
            Export(world, data, "04-heat-source.png");

            Debug.Log($"[SWAN] 地图截图已导出：{Path.GetFullPath(ScreenshotFolder)}（120 步后总水 {world.Stats.TotalWater:0.0}）");
        }

        static void Export(World world, DataBundle data, string fileName)
        {
            int px = data.Config.CellPixelSize;

            MapPalette palette = MapPalette.Build(data);
            int width = world.Width * px;
            int height = world.Height * px;

            var canvas = new Color32[width * height];
            var groundTile = new Color32[px * px];
            var decorTile = new Color32[px * px];

            for (int y = 0; y < world.Height; y++)
            {
                for (int x = 0; x < world.Width; x++)
                {
                    Cell cell = world.GetCell(x, y);
                    CellPainter.PaintGround(groundTile, px, palette.TerrainColor(cell.TerrainId));
                    CellPainter.PaintDecor(decorTile, px, cell, palette, data.Config);
                    Color32 heat = CellPainter.HeatPixel(palette.Heat, cell.Temperature,
                        data.Config.HeatOverlayStartTemp, data.Config.HeatOverlayFullTemp);

                    for (int py = 0; py < px; py++)
                    {
                        int row = (y * px + py) * width;
                        for (int px2 = 0; px2 < px; px2++)
                        {
                            Color32 pixel = Blend(groundTile[py * px + px2], decorTile[py * px + px2]);
                            canvas[row + x * px + px2] = Blend(pixel, heat);
                        }
                    }
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(canvas);
            texture.Apply(false);
            File.WriteAllBytes(Path.Combine(ScreenshotFolder, fileName), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        static Color32 Blend(Color32 bottom, Color32 top)
        {
            if (top.a == 0) return bottom;
            if (top.a == 255) return top;
            float a = top.a / 255f;
            return new Color32(
                (byte)Mathf.RoundToInt(top.r * a + bottom.r * (1f - a)),
                (byte)Mathf.RoundToInt(top.g * a + bottom.g * (1f - a)),
                (byte)Mathf.RoundToInt(top.b * a + bottom.b * (1f - a)),
                255);
        }

        static DataBundle LoadData()
        {
            IFileSource source = DataLocator.CreateSource(out string directory);
            Debug.Log($"[SWAN] 读数据：{directory}");
            return DataBundle.Load(source);
        }
    }
}
