using NUnit.Framework;

namespace Swan.Tests
{
    /// <summary>
    /// 浮点断言用的小工具。
    ///
    /// NUnit 的经典 Assert.AreEqual 没有「expected, actual, delta, message」这个重载，
    /// 写四个参数会被解析成别的签名并悄悄比错类型。统一走这里，语义明确：允许误差。
    /// </summary>
    public static class TestAssert
    {
        public static void Near(float expected, float actual, float tolerance, string message = null)
        {
            Assert.That(actual, Is.EqualTo(expected).Within(tolerance), message);
        }

        public static void Near(double expected, double actual, double tolerance, string message = null)
        {
            Assert.That(actual, Is.EqualTo(expected).Within(tolerance), message);
        }
    }
}
