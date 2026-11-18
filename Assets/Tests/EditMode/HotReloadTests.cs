using System.IO;
using System.Text;
using NUnit.Framework;
using Swan.Core;
using Swan.Core.Data;
using Swan.Core.Sim;

namespace Swan.Tests
{
    /// <summary>
    /// 热重载（T2 / 交接包 §10.5）：
    /// 不重启、不清状态，只换数据，行为要立刻变。
    /// 这三条自检如果过了，「调数值不用重开游戏」这件事才算真的成立。
    ///
    /// 注意：必须用真实尺寸的世界（new World(data)），
    /// 因为 ApplyData 判断「网格尺寸变了就重建地图」，1×1 的探针世界一换数据就会被当成重建。
    /// </summary>
    public class HotReloadTests
    {
        const float Tolerance = 1e-3f;

        DataBundle _data;

        [OneTimeSetUp]
        public void LoadData() => _data = TestData.LoadReal();

        static string ReadDataFile(string fileName) =>
            File.ReadAllText(Path.Combine(TestData.Folder, fileName), Encoding.UTF8);

        /// <summary>在真实数据上同时替换多个文件（热重载常常要改两个来源）。</summary>
        static DataBundle LoadPatched(string file1, string text1, string file2, string text2)
        {
            return DataBundle.Load(TestData.FromDisk().Add(file1, text1).Add(file2, text2));
        }

        [Test]
        public void 自检一_改地形扣水速度立刻变快()
        {
            var world = new World(_data);
            world.SetCellValues(0, 0, 30f, 30f);
            Assert.AreEqual(7, world.GetCell(0, 0).TerrainId, "30/30 先确认是普通土地");

            world.StepEnvironment();
            TestAssert.Near(29f, world.GetCell(0, 0).Water, Tolerance, "普通土地每步 −1");

            string terrain = TestData.PatchCsv(ReadDataFile(TestData.TerrainFile),
                "dWaterPerStep", "terrainId", "7", "-5");
            world.ApplyData(TestData.LoadWith(TestData.TerrainFile, terrain));

            TestAssert.Near(-5f, world.Data.Terrain.GetById(7).DWaterPerStep, Tolerance, "新表应该已经生效");
            world.SetCellValues(0, 0, 30f, 30f);
            world.StepEnvironment();
            TestAssert.Near(25f, world.GetCell(0, 0).Water, Tolerance, "热重载后应该一步掉 5 点水");
        }

        [Test]
        public void 自检二_改植物生长速度立刻生效()
        {
            var world = new World(_data);
            world.SetCellValues(0, 0, 50f, 50f);
            Assert.IsTrue(world.TryPlantDefault(0, 0, out _), "50/50 的格子应该能种下 P01");

            string plants = TestData.PatchCsv(ReadDataFile(TestData.PlantFile),
                "growthPerStep", "typeId", "P01", "10");
            world.ApplyData(TestData.LoadWith(TestData.PlantFile, plants));

            Assert.AreEqual(10f, world.Data.Plants.Get("P01").GrowthPerStep, Tolerance);
            for (int i = 0; i < 10; i++)
            {
                world.SetCellValues(0, 0, 50f, 50f);   // 照顾它，否则会停长
                world.StepEnvironment();
            }

            Assert.That(world.GetCell(0, 0).Growth, Is.GreaterThanOrEqualTo(_data.Config.RipeThreshold - 0.5f),
                "growthPerStep=10 时十步应该长熟");
            Assert.IsTrue(world.GetCell(0, 0).Harvestable);
        }

        [Test]
        public void 自检三_G02叠乘开关决定了速生是否存在()
        {
            var world = new World(_data);
            world.SetCellValues(0, 0, 50f, 50f);
            Assert.IsTrue(world.TryPlantDefault(0, 0, out _));

            // 默认 stackInitialGrowthTraits=true（创意总监裁定：叠乘）：G02(value=2) 生效，每步 1.11×2。
            world.SetCellValues(0, 0, 50f, 50f);
            world.StepEnvironment();
            float baseStep = world.Data.Plants.Get("P01").GrowthPerStep;
            float stacked = world.GetCell(0, 0).Growth;
            TestAssert.Near(baseStep * 2f, stacked, 0.05f, "叠乘打开：每步是表里的 growthPerStep × G02");

            // 把 G02 的 value 从 2 改成 1 → 速生消失，每步回到 1.11（§10.5 自检 #3）。
            string g02 = TestData.PatchCsv(ReadDataFile(TestData.TraitFile), "value", "traitId", "G02", "1");
            world.ApplyData(TestData.LoadWith(TestData.TraitFile, g02));
            float before = world.GetCell(0, 0).Growth;
            world.SetCellValues(0, 0, 50f, 50f);
            world.StepEnvironment();
            TestAssert.Near(world.Data.Plants.Get("P01").GrowthPerStep, world.GetCell(0, 0).Growth - before, 0.02f,
                "G02 改成 1 之后不该再有加速");

            // 再把叠乘开关关掉 → 每步也回到表里的 growthPerStep（两条路径互不依赖）。
            string configNoStack = TestData.PatchConfig(ReadDataFile(TestData.ConfigFile),
                "stackInitialGrowthTraits", "false");
            world.ApplyData(LoadPatched(TestData.ConfigFile, configNoStack, TestData.TraitFile, g02));
            before = world.GetCell(0, 0).Growth;
            world.SetCellValues(0, 0, 50f, 50f);
            world.StepEnvironment();
            TestAssert.Near(world.Data.Plants.Get("P01").GrowthPerStep, world.GetCell(0, 0).Growth - before, 0.02f,
                "关掉叠乘：P01 每步就是表里的 growthPerStep");
        }

        [Test]
        public void 热重载不会把植物弄丢或串种()
        {
            var world = new World(_data);
            world.SetCellValues(3, 3, 50f, 50f);
            Assert.IsTrue(world.TryPlantDefault(3, 3, out _));
            int plantTypeId = world.GetCell(3, 3).PlantTypeId;

            // 把植物表里 P01 的顺序相关字段改一下，再换一张「行序可能不同」的表。
            string plants = TestData.PatchCsv(ReadDataFile(TestData.PlantFile),
                "growthPerStep", "typeId", "P01", "1.5");
            world.ApplyData(TestData.LoadWith(TestData.PlantFile, plants));

            var cell = world.GetCell(3, 3);
            Assert.IsTrue(cell.HasPlant, "热重载不该清掉已种的植物");
            Assert.AreEqual("P01", world.Data.Plants.RowByIndex(cell.PlantTypeId).TypeId);
            Assert.AreEqual(plantTypeId, cell.PlantTypeId, "同名植物应该映射回同一个类型");
        }
    }
}
