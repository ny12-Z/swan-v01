using NUnit.Framework;
using Swan.Core.Data;
using Swan.Core.Sim;
using Swan.Game.Rendering;
using UnityEngine;

namespace Swan.Tests
{
    /// <summary>
    /// 「玩家位置提示」的渲染层测试。
    /// 这里钉住的是一条踩过的坑：光标纹理是懒建的，如果 SetCursor 一开头就要求纹理已存在，
    /// 首帧会直接 return，画面上永远看不到黄框（而 HUD 却显示着光标格，很难怀疑到渲染层）。
    /// </summary>
    public class CursorTests
    {
        DataBundle _data;
        GameObject _go;
        MapRenderer _renderer;

        [SetUp]
        public void SetUp()
        {
            _data = TestData.LoadReal();
            _go = new GameObject("MapRendererUnderTest");
            _renderer = _go.AddComponent<MapRenderer>();
            _renderer.Bind(new World(_data, 8, 8), _data);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void 第一次调用就该把光标框画出来()
        {
            _renderer.SetCursor(2, 3, 1f);

            Texture2D texture = _renderer.CursorTextureForTests;
            Assert.IsNotNull(texture, "SetCursor 之后光标纹理应该已经建好了");

            Color32[] pixels = texture.GetPixels32();
            int opaque = 0;
            foreach (Color32 p in pixels)
            {
                if (p.a > 0) opaque++;
            }

            Assert.That(opaque, Is.GreaterThan(0), "光标框应该有可见像素，否则玩家还是找不到自己");
        }

        [Test]
        public void 光标框是空心框不是整块色块()
        {
            _renderer.SetCursor(0, 0, 1f);

            Color32[] pixels = _renderer.CursorTextureForTests.GetPixels32();
            int opaque = 0;
            foreach (Color32 p in pixels)
            {
                if (p.a > 0) opaque++;
            }

            // 整块填满会挡住脚下的地形和植物；描边 + 中心点必须远少于全格
            Assert.That(opaque, Is.LessThan(pixels.Length / 2),
                "光标应该是空心描边框，不能整格涂满");
        }

        [Test]
        public void 透明度为零时光标不可见()
        {
            _renderer.SetCursor(1, 1, 0f);

            Color32[] pixels = _renderer.CursorTextureForTests.GetPixels32();
            foreach (Color32 p in pixels)
            {
                Assert.AreEqual(0, p.a, "alpha=0 时不应该留下任何像素");
            }
        }

        [Test]
        public void 光标颜色来自配置()
        {
            _renderer.SetCursor(0, 0, 1f);

            Color32 expected = MapPalette.Build(_data).Cursor;
            Color32[] pixels = _renderer.CursorTextureForTests.GetPixels32();

            Color32 found = default;
            foreach (Color32 p in pixels)
            {
                if (p.a > 0)
                {
                    found = p;
                    break;
                }
            }

            Assert.AreEqual(expected.r, found.r, "光标颜色应该读 config.cursorColor");
            Assert.AreEqual(expected.g, found.g, "光标颜色应该读 config.cursorColor");
            Assert.AreEqual(expected.b, found.b, "光标颜色应该读 config.cursorColor");
        }
    }
}
