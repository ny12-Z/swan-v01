using NUnit.Framework;
using Swan.Core.Data;
using Swan.Core.Sim;

namespace Swan.Tests
{
    /// <summary>
    /// 数值层的唯一验收标准：`p01-对照曲线.md` 的 B1 / B2。
    ///
    /// 前提（缺一不可）：单格 4 邻同值、恒温 20、无热源、吸水 0.5、P01 性状 ["G02"]、
    /// 门槛 水≥20 养≥15 温 5~40、ripe=100、地形换档防抖关闭。
    ///
    /// 创意总监裁定（2026-11-18，与交接包默认值不同）：**停长 + 叠乘**。
    /// 停长 = 水跌破门槛就停，不改表让它长熟；叠乘 = G02 对 P01 再乘一次
    /// （config.stackInitialGrowthTraits = true）⇒ 生长列 = 文档的 2 倍，
    /// 水/养分/地形三列与文档逐点一致。要回到文档原始口径，把 config 里那个开关改成 false。
    ///
    /// 这里把整条曲线的每一拍都写死，而不是只抽查几个点：
    /// 「步 7 准时停长」这种要求，只有逐拍比对才抓得住。
    /// </summary>
    public class CurveTests
    {
        const float Tolerance = 1e-3f;
        const string Plant = "P01";

        DataBundle _data;

        [OneTimeSetUp]
        public void LoadData() => _data = TestData.LoadReal();

        struct Expected
        {
            public int Step;
            public float Water;
            public float Nutrient;
            public string Terrain;
            public float Growth;

            public Expected(int step, float water, float nutrient, string terrain, float growth)
            {
                Step = step;
                Water = water;
                Nutrient = nutrient;
                Terrain = terrain;
                Growth = growth;
            }
        }

        // B1：普通土地 30/30，每步 水−1.5（吸水0.5+地形1）、养分−1，生长 +2.22（=1.11 × G02 叠乘 2）。
        // 创意总监已裁定「停长 + 叠乘」：水/养分/地形三列与 p01-对照曲线.md 逐点一致，只有生长列按 2 倍。
        static readonly Expected[] B1 =
        {
            new Expected(0, 30.0f, 30.0f, "普通土地", 0.00f),
            new Expected(1, 28.5f, 29.0f, "普通土地", 2.22f),
            new Expected(2, 27.0f, 28.0f, "普通土地", 4.44f),
            new Expected(3, 25.5f, 27.0f, "普通土地", 6.66f),
            new Expected(4, 24.0f, 26.0f, "普通土地", 8.88f),
            new Expected(5, 22.5f, 25.0f, "普通土地", 11.10f),
            new Expected(6, 21.0f, 24.0f, "普通土地", 13.32f),
            // 水 19.5 < 门槛 20：本步不再生长（停长不是 bug，是裁定结果）
            new Expected(7, 19.5f, 23.0f, "旱地", 13.32f),
        };

        // B2：肥沃水田 50/50，每步 水−1.0、养分−0.2，生长 +2.22；
        // 水掉到 40 以下（步11末）换草原，此后每步 水−1.5、养分−1。
        static readonly Expected[] B2 =
        {
            new Expected(0, 50.0f, 50.0f, "肥沃水田", 0.00f),
            new Expected(1, 49.0f, 49.8f, "肥沃水田", 2.22f),
            new Expected(2, 48.0f, 49.6f, "肥沃水田", 4.44f),
            new Expected(3, 47.0f, 49.4f, "肥沃水田", 6.66f),
            new Expected(4, 46.0f, 49.2f, "肥沃水田", 8.88f),
            new Expected(5, 45.0f, 49.0f, "肥沃水田", 11.10f),
            new Expected(6, 44.0f, 48.8f, "肥沃水田", 13.32f),
            new Expected(7, 43.0f, 48.6f, "肥沃水田", 15.54f),
            new Expected(8, 42.0f, 48.4f, "肥沃水田", 17.76f),
            new Expected(9, 41.0f, 48.2f, "肥沃水田", 19.98f),
            new Expected(10, 40.0f, 48.0f, "肥沃水田", 22.20f),
            // 步11 用旧地形（肥沃水田）结算，步末水到 39 才重判为草原 —— 步12 起按草原结算
            new Expected(11, 39.0f, 47.8f, "草原", 24.42f),
            new Expected(12, 37.5f, 46.8f, "草原", 26.64f),
            new Expected(13, 36.0f, 45.8f, "草原", 28.86f),
            new Expected(14, 34.5f, 44.8f, "草原", 31.08f),
            new Expected(15, 33.0f, 43.8f, "草原", 33.30f),
            new Expected(16, 31.5f, 42.8f, "草原", 35.52f),
            new Expected(17, 30.0f, 41.8f, "草原", 37.74f),
            new Expected(18, 28.5f, 40.8f, "草原", 39.96f),
            // 步19 养分 39.8 掉出 [40,60) 档：按地形表，这里已经是「普通土地」。
            // 对照曲线文档在步20/23 写的仍是「草原」——两者扣减都是 −1/−1，所以水/养分/生长全对，
            // 只有名字不同。见 Docs/CONFLICTS.md #3：以表为准，等创意总监裁定。
            new Expected(19, 27.0f, 39.8f, "普通土地", 42.18f),
            new Expected(20, 25.5f, 38.8f, "普通土地", 44.40f),
            new Expected(21, 24.0f, 37.8f, "普通土地", 46.62f),
            new Expected(22, 22.5f, 36.8f, "普通土地", 48.84f),
            new Expected(23, 21.0f, 35.8f, "普通土地", 51.06f),
            // 水 19.5 < 门槛 20：第 24 步准时停长
            new Expected(24, 19.5f, 34.8f, "旱地", 51.06f),
        };

        [Test]
        public void B1_普通土地起手30比30_七步后停长()
        {
            AssertCurve(B1, 30f, 30f, 7, "B1");
        }

        [Test]
        public void B2_肥沃水田起手50比50_二十四步后停长()
        {
            AssertCurve(B2, 50f, 50f, 24, "B2");
        }

        void AssertCurve(Expected[] expected, float startWater, float startNutrient, int steps, string name)
        {
            var samples = CurveProbe.Run(_data, startWater, startNutrient, Plant, steps, useHysteresis: false);
            Assert.AreEqual(steps + 1, samples.Count, $"{name}: 采样点数不对");

            foreach (var e in expected)
            {
                CurveSample actual = samples[e.Step];
                TestAssert.Near(e.Water, actual.Water, Tolerance, $"{name} 步{e.Step}：水");
                TestAssert.Near(e.Nutrient, actual.Nutrient, Tolerance, $"{name} 步{e.Step}：养分");
                TestAssert.Near(e.Growth, actual.Growth, Tolerance, $"{name} 步{e.Step}：生长");
                Assert.AreEqual(e.Terrain, actual.TerrainName, $"{name} 步{e.Step}：地形");
                // 单格探针没有邻居也没有热源：温度必须钉在 baseTemperature，不许漂
                TestAssert.Near(_data.Config.BaseTemperature, actual.Temperature, Tolerance, $"{name} 步{e.Step}：温度");
            }
        }

        [Test]
        public void 停长之后生长值不许再涨()
        {
            var samples = CurveProbe.Run(_data, 30f, 30f, Plant, 7, useHysteresis: false);
            TestAssert.Near(samples[6].Growth, samples[7].Growth, Tolerance, "步7 水跌破门槛，应该停长");
        }
    }
}
