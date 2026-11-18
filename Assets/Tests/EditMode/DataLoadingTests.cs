using NUnit.Framework;
using Swan.Core;
using Swan.Core.Data;

namespace Swan.Tests
{
    /// <summary>
    /// 数据层：表能不能读、坏表会不会报错、报错里有没有「文件名 + 行号 + 列名」。
    ///
    /// 这些测试的意义是「以后改表的人不用读代码」：
    /// 表写错了，启动就炸，并且告诉他炸在哪一行哪一列。
    /// </summary>
    public class DataLoadingTests
    {
        [Test]
        public void 真实数据能全部加载()
        {
            DataBundle data = TestData.LoadReal();
            Assert.AreEqual(25, data.Terrain.Rows.Count, "地形表应有 25 行");
            Assert.AreEqual(6, data.Plants.Rows.Count, "植物类型表应有 6 行");
            Assert.AreEqual(12, data.Traits.Rows.Count, "植物性状表应有 12 行");
            Assert.AreEqual(10, data.Rules.Rows.Count, "世界法则表应有 10 行");
        }

        [Test]
        public void v01只开放P01养殖()
        {
            DataBundle data = TestData.LoadReal();
            int plantable = 0;
            foreach (var row in data.Plants.Rows)
            {
                if (row.PlantableInV01) plantable++;
            }
            Assert.AreEqual(1, plantable, "v0.1 只允许 P01（其余 v01Active=0）");
            Assert.IsTrue(data.Plants.Get("P01").PlantableInV01);
        }

        [Test]
        public void 地形关键行数值与文档一致()
        {
            DataBundle data = TestData.LoadReal();
            var common = data.Terrain.GetById(7);
            Assert.AreEqual("普通土地", common.Name);
            Assert.AreEqual(-1f, common.DWaterPerStep);
            Assert.AreEqual(-1f, common.DNutrientPerStep);

            var spring = data.Terrain.GetById(25);
            Assert.AreEqual(0f, spring.DWaterPerStep, "生命之泉的水不该减少");
            Assert.AreEqual(0f, spring.DNutrientPerStep);
        }

        [Test]
        public void 二十五宫格每一格都能查到且不重号()
        {
            DataBundle data = TestData.LoadReal();
            var ids = new System.Collections.Generic.HashSet<int>();
            foreach (var row in data.Terrain.Rows)
            {
                float water = row.WaterMax >= data.Config.WaterMax ? data.Config.WaterMax : (row.WaterMin + row.WaterMax) * 0.5f;
                float nutrient = row.NutrientMax >= data.Config.NutrientMax
                    ? data.Config.NutrientMax
                    : (row.NutrientMin + row.NutrientMax) * 0.5f;
                int resolved = data.Terrain.Resolve(water, nutrient).TerrainId;
                Assert.AreEqual(row.TerrainId, resolved, $"水{water}/养分{nutrient} 应命中 {row.Name}");
                ids.Add(resolved);
            }
            Assert.AreEqual(25, ids.Count, "25 个宫格不能重号");
        }

        [Test]
        public void 边界值归属上一档()
        {
            DataBundle data = TestData.LoadReal();
            // 水 20 落在 [20,40) 档，养分 30 也在 [20,40) → 普通土地
            Assert.AreEqual(7, data.Terrain.Resolve(20f, 30f).TerrainId);
            // 差一点点掉到下一档 → 旱地
            Assert.AreEqual(2, data.Terrain.Resolve(19.99f, 30f).TerrainId);
            // 水 40 归 [40,60) 档 + 养分 50 → 肥沃水田
            Assert.AreEqual(13, data.Terrain.Resolve(40f, 50f).TerrainId);
            // 100 是最后一档的闭区间端点，不能报「查不到地形」
            Assert.AreEqual(25, data.Terrain.Resolve(100f, 100f).TerrainId);
        }

        [Test]
        public void CSV解析支持引号内逗号与换行()
        {
            var warnings = new DataWarnings();
            var doc = CsvDocument.Parse("\uFEFFid,name,note\r\n1,\"a,b\",x\r\n2,\"第一行\n第二行\",y\r\n", "t.csv", warnings);
            Assert.AreEqual(2, doc.Rows.Count);
            Assert.AreEqual("a,b", doc.Rows[0].Raw("name"));
            Assert.AreEqual("第一行\n第二行", doc.Rows[1].Raw("name"));
            Assert.AreEqual("y", doc.Rows[1].Raw("note"));
        }

        [Test]
        public void 列数不对时报错带行号与文件名()
        {
            var warnings = new DataWarnings();
            var ex = Assert.Throws<DataLoadException>(() =>
                CsvDocument.Parse("id,name\n1,a\n2,b,多出来的一列\n", "坏表.csv", warnings));
            StringAssert.Contains("坏表.csv", ex.Message);
            StringAssert.Contains("列数", ex.Message);
        }

        [Test]
        public void 地形主键重复时报错()
        {
            string text = TestData.PatchCsv(
                System.IO.File.ReadAllText(System.IO.Path.Combine(TestData.Folder, TestData.TerrainFile)),
                "name", "terrainId", "2", "荒漠二号");
            // 把 terrainId 也改成重复的 1
            text = TestData.PatchCsv(text, "terrainId", "terrainId", "2", "1");

            var ex = Assert.Throws<DataLoadException>(() => TestData.LoadWith(TestData.TerrainFile, text));
            StringAssert.Contains("重复", ex.Message);
            StringAssert.Contains(TestData.TerrainFile, ex.Message);
        }

        [Test]
        public void 植物性状外键查不到时报错()
        {
            string text = System.IO.File.ReadAllText(System.IO.Path.Combine(TestData.Folder, TestData.PlantFile));
            text = TestData.PatchCsv(text, "initTraits", "typeId", "P01", "G99");

            var ex = Assert.Throws<DataLoadException>(() => TestData.LoadWith(TestData.PlantFile, text));
            StringAssert.Contains("G99", ex.Message);
        }

        [Test]
        public void 法则效果键未知时报错()
        {
            string text = System.IO.File.ReadAllText(System.IO.Path.Combine(TestData.Folder, TestData.RuleFile));
            text = TestData.PatchCsv(text, "effectKey", "ruleId", "L02", "notAnEffect");

            var ex = Assert.Throws<DataLoadException>(() => TestData.LoadWith(TestData.RuleFile, text));
            StringAssert.Contains("notAnEffect", ex.Message);
        }

        [Test]
        public void config少字段时报错()
        {
            string text = System.IO.File.ReadAllText(System.IO.Path.Combine(TestData.Folder, TestData.ConfigFile));
            // 删掉 envStepSeconds 这一行
            text = System.Text.RegularExpressions.Regex.Replace(text, "\"envStepSeconds\"[^,]*,\\s*", "");

            var ex = Assert.Throws<DataLoadException>(() => TestData.LoadWith(TestData.ConfigFile, text));
            StringAssert.Contains("envStepSeconds", ex.Message);
        }

        [Test]
        public void 同一个参数两个来源时必须报错()
        {
            string text = System.IO.File.ReadAllText(System.IO.Path.Combine(TestData.Folder, TestData.ConfigFile));
            text = TestData.PatchConfig(text, "waterOverflowThreshold", "96");

            var ex = Assert.Throws<DataLoadException>(() => TestData.LoadWith(TestData.ConfigFile, text));
            StringAssert.Contains("L01", ex.Message);
            StringAssert.Contains("两个来源", ex.Message);
        }

        [Test]
        public void 光标框线宽超过格子像素时要报错()
        {
            string text = System.IO.File.ReadAllText(System.IO.Path.Combine(TestData.Folder, TestData.ConfigFile));
            // 线宽 >= cellPixelSize 时整格都是边框，玩家反而看不见脚下的地形
            text = TestData.PatchConfig(text, "cursorLineThickness", "16");

            var ex = Assert.Throws<DataLoadException>(() => TestData.LoadWith(TestData.ConfigFile, text));
            StringAssert.Contains("cursorLineThickness", ex.Message);
        }

        [Test]
        public void 光标颜色写错时要报错并指出是哪一列()
        {
            string text = System.IO.File.ReadAllText(System.IO.Path.Combine(TestData.Folder, TestData.ConfigFile));
            text = TestData.PatchConfig(text, "cursorColor", "\"#GGGGGG\"");

            var ex = Assert.Throws<DataLoadException>(() => TestData.LoadWith(TestData.ConfigFile, text));
            StringAssert.Contains("cursorColor", ex.Message);
        }
    }
}
