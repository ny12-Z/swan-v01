using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Swan.Core.Data;

namespace Swan.Core.Sim
{
    /// <summary>对照曲线上的一个采样点（一个环境步结束后的状态）。</summary>
    public sealed class CurveSample
    {
        public int Step;
        public float Water;
        public float Nutrient;
        public float Temperature;
        public float Growth;
        public string TerrainName;
        public string Traits;
    }

    /// <summary>
    /// 用 1×1 世界跑「单格对照曲线」（交接包 `p01-对照曲线.md` B1/B2）。
    /// 这是整个数值层唯一的验收标准：曲线对不上，就是代码错了，不是表错了。
    ///
    /// 之所以单独做成探针而不是「在正式地图里找一格看」：
    /// 正式地图 4 邻数值各不相同，水会溢出、温度会扩散，永远复现不出曲线的前提。
    /// </summary>
    public static class CurveProbe
    {
        /// <summary>
        /// 跑一条曲线。
        /// </summary>
        /// <param name="data">已加载的数据包（会被浅拷贝一份，只换 config，不影响调用方）。</param>
        /// <param name="startWater">初始水</param>
        /// <param name="startNutrient">初始养分</param>
        /// <param name="plantTypeId">植物类型（例如 "P01"）</param>
        /// <param name="steps">跑几个环境步</param>
        /// <param name="useHysteresis">是否启用地形换档防抖；对照曲线的前提是「忽略防抖」，传 false</param>
        public static List<CurveSample> Run(
            DataBundle data,
            float startWater,
            float startNutrient,
            string plantTypeId,
            int steps,
            bool useHysteresis)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (steps < 0) throw new ArgumentOutOfRangeException(nameof(steps));

            var config = data.Config.Clone();
            if (!useHysteresis) config.TerrainHysteresis = 0f;

            var probeData = new DataBundle
            {
                Config = config,
                Terrain = data.Terrain,
                Traits = data.Traits,
                Plants = data.Plants,
                Rules = data.Rules,
                Preset = data.Preset,
            };

            // 传尺寸 = 只铺基准值、不套地图预设，避免邻居/热源污染前提
            var world = new World(probeData, 1, 1);
            world.SetCellValues(0, 0, startWater, startNutrient);
            world.SetTemperature(0, 0, config.BaseTemperature);

            var type = data.Plants.Get(plantTypeId);
            if (!type.PlantableInV01)
            {
                throw new InvalidOperationException($"植物 {plantTypeId} 的 v01Active=0，本版不能种植，不能用来跑曲线");
            }

            world.SeedPlant(0, 0, plantTypeId);

            var samples = new List<CurveSample>(steps + 1);
            samples.Add(Snapshot(world, data, 0));

            for (int step = 1; step <= steps; step++)
            {
                world.StepEnvironment();
                samples.Add(Snapshot(world, data, step));
            }

            return samples;
        }

        static CurveSample Snapshot(World world, DataBundle data, int step)
        {
            Cell cell = world.GetCell(0, 0);
            var terrain = data.Terrain.GetById(cell.TerrainId);
            return new CurveSample
            {
                Step = step,
                Water = cell.Water,
                Nutrient = cell.Nutrient,
                Temperature = cell.Temperature,
                Growth = cell.Growth,
                TerrainName = terrain.Name,
                Traits = cell.Traits == null ? "" : string.Join("|", cell.Traits),
            };
        }

        /// <summary>把曲线排成表格（控制台/日志/README 证据用）。</summary>
        public static string Format(IEnumerable<CurveSample> samples)
        {
            var sb = new StringBuilder();
            sb.AppendLine("步数 |   水  | 养分  |  温度 | 生长  | 地形");
            sb.AppendLine("-----+-------+-------+-------+-------+----------");
            foreach (var s in samples)
            {
                sb.Append(s.Step.ToString("000", CultureInfo.InvariantCulture)).Append("  | ")
                  .Append(s.Water.ToString("0.0", CultureInfo.InvariantCulture)).Append(" | ")
                  .Append(s.Nutrient.ToString("0.0", CultureInfo.InvariantCulture)).Append(" | ")
                  .Append(s.Temperature.ToString("0.00", CultureInfo.InvariantCulture)).Append(" | ")
                  .Append(s.Growth.ToString("0.00", CultureInfo.InvariantCulture)).Append(" | ")
                  .Append(s.TerrainName)
                  .AppendLine();
            }
            return sb.ToString();
        }
    }
}
