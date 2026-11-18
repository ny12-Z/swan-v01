using NUnit.Framework;
using Swan.Core;
using Swan.Core.Data;
using Swan.Core.Sim;

namespace Swan.Tests
{
    /// <summary>
    /// 地形与植物（T5 / T6 / T7）：
    /// 地形名跟着数值走、换档有防抖、热源会升温也会降温、P01 能种能熟能收。
    /// </summary>
    public class TerrainAndPlantTests
    {
        DataBundle _data;

        [OneTimeSetUp]
        public void LoadData() => _data = TestData.LoadReal();

        /// <summary>1×1 探针世界：不套地图预设，邻居就是自己，温度与溢出都不打扰。</summary>
        World Probe() => new World(_data, 1, 1);

        [Test]
        public void 地形跟着数值立刻变()
        {
            var world = Probe();
            world.SetCellValues(0, 0, 30f, 30f);
            Assert.AreEqual(7, world.GetCell(0, 0).TerrainId, "20~40 水 / 20~40 养分 = 普通土地");

            world.SetCellValues(0, 0, 85f, 5f);
            Assert.AreEqual(21, world.GetCell(0, 0).TerrainId, "80~100 水 / 0~20 养分 = 纯水");

            world.SetCellValues(0, 0, 0f, 0f);
            Assert.AreEqual(1, world.GetCell(0, 0).TerrainId, "水 0 养分 0 = 荒漠");
        }

        [Test]
        public void 生命之泉的水不会减少()
        {
            var world = Probe();
            world.SetCellValues(0, 0, 100f, 100f);
            Assert.AreEqual(25, world.GetCell(0, 0).TerrainId);

            world.StepEnvironment();
            Assert.AreEqual(100f, world.GetCell(0, 0).Water, 1e-3f);
        }

        [Test]
        public void 换档防抖让边界来回抖的格不乱跳()
        {
            var world = Probe();
            float hysteresis = _data.Config.TerrainHysteresis;
            Assert.Greater(hysteresis, 0f, "这条测试依赖 terrainHysteresis > 0");

            // 20.5 还在普通土地；下一步扣到 19.5，已经掉出 [20,40)，
            // 但离边界只有 0.5，落在防抖带里，所以这一步不换档。
            world.SetCellValues(0, 0, 20f + hysteresis * 0.25f, 30f);
            Assert.AreEqual(7, world.GetCell(0, 0).TerrainId);

            world.StepEnvironment();
            Assert.AreEqual(7, world.GetCell(0, 0).TerrainId, "刚跨过边界，防抖带内不换档");

            world.StepEnvironment();
            Assert.AreEqual(7, world.GetCell(0, 0).TerrainId, "还在防抖带内");

            world.StepEnvironment();
            Assert.AreEqual(2, world.GetCell(0, 0).TerrainId, "离边界超过防抖带，才换成旱地");
        }

        [Test]
        public void 水或养分不够时种不下去且说明原因()
        {
            var world = Probe();
            world.SetCellValues(0, 0, 10f, 10f);

            Assert.IsFalse(world.TryPlantDefault(0, 0, out string message));
            StringAssert.Contains("水", message, "拒绝种植必须说清是哪条门槛没过");
            Assert.IsFalse(world.GetCell(0, 0).HasPlant);
        }

        [Test]
        public void 一格只能种一株()
        {
            var world = Probe();
            world.SetCellValues(0, 0, 50f, 50f);
            Assert.IsTrue(world.TryPlantDefault(0, 0, out _));

            Assert.IsFalse(world.TryPlantDefault(0, 0, out string message));
            StringAssert.Contains("已经有植物", message);
        }

        [Test]
        public void 没熟的植物收不了()
        {
            var world = Probe();
            world.SetCellValues(0, 0, 50f, 50f);
            Assert.IsTrue(world.TryPlantDefault(0, 0, out _));

            Assert.IsFalse(world.TryHarvest(0, 0, out float yield, out string message));
            Assert.AreEqual(0f, yield);
            StringAssert.Contains("还没熟", message);
        }

        [Test]
        public void 保持水分就能长熟并收获()
        {
            var world = Probe();
            world.SetCellValues(0, 0, 50f, 50f);
            Assert.IsTrue(world.TryPlantDefault(0, 0, out _));

            var type = _data.Plants.Get("P01");
            float perStep = type.GrowthPerStep;
            int needed = (int)System.Math.Ceiling(_data.Config.RipeThreshold / perStep);

            // 每步把水和养分补回去，模拟一个被照顾的格子；否则它会像对照曲线那样停长。
            for (int i = 0; i < needed; i++)
            {
                world.SetCellValues(0, 0, 50f, 50f);
                world.StepEnvironment();
            }

            var cell = world.GetCell(0, 0);
            Assert.GreaterOrEqual(cell.Growth, _data.Config.RipeThreshold, "水分充足时应该一直长到成熟");
            Assert.IsTrue(cell.Harvestable, "成熟后应该被标记为可收");

            Assert.IsTrue(world.TryHarvest(0, 0, out float yield, out string message));
            TestAssert.Near(type.BaseYield, yield, 1e-3f, "P01 没有 yieldAdd 性状，产量就是 baseYield");
            Assert.AreEqual(type.BaseYield, world.TotalHarvested, 1e-3f);
            Assert.AreEqual(1, world.HarvestCount);
            Assert.IsFalse(world.GetCell(0, 0).HasPlant, "收完这一格应该空了");
            StringAssert.Contains("收获", message);
        }

        [Test]
        public void 热源会升温到阈值以上再回归常温()
        {
            var world = Probe();
            var config = _data.Config;

            Assert.IsTrue(world.SetHeatSource(0, 0, true));
            world.StepEnvironment();
            float heated = world.GetCell(0, 0).Temperature;
            Assert.Greater(heated, config.FireTempThreshold, "热源一开就该越过着火阈值");
            TestAssert.Near(config.BaseTemperature + config.HeatSourceBoost, heated, 0.5f,
                "1×1 世界没有邻居可扩散，一步就是 base + heatSourceBoost");

            Assert.IsTrue(world.SetHeatSource(0, 0, false));
            int guard = 1000;
            while (world.GetCell(0, 0).Temperature - config.BaseTemperature > 1f && guard-- > 0)
            {
                world.StepEnvironment();
            }

            Assert.LessOrEqual(Mathf_Abs(world.GetCell(0, 0).Temperature - config.BaseTemperature), 1f,
                "热源移掉后温度应该回到常温（验收 A4）");
        }

        [Test]
        public void 热源会让邻格也热起来()
        {
            // 用真实地图尺寸，避免 1×1 世界的「邻居就是自己」这种特殊情况。
            var world = new World(_data);
            world.SetTemperature(10, 10, _data.Config.BaseTemperature);
            world.SetTemperature(11, 10, _data.Config.BaseTemperature);
            world.SetHeatSource(10, 10, true);

            // 扩散读的是上一步的温度：热源格第一步先自己升温，邻格第二步才被加热。
            world.StepEnvironment();
            world.StepEnvironment();
            float neighbor = world.GetCell(11, 10).Temperature;
            Assert.Greater(neighbor, _data.Config.BaseTemperature, "热源旁边的格应该被扩散加热");
            Assert.Less(neighbor, world.GetCell(10, 10).Temperature, "邻格不该比热源本身还热");
        }

        static float Mathf_Abs(float v) => v < 0f ? -v : v;
    }
}
