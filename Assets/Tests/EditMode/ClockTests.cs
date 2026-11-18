using NUnit.Framework;
using Swan.Core;
using Swan.Core.Data;
using Swan.Core.Sim;

namespace Swan.Tests
{
    /// <summary>
    /// 双时钟（交接包 §4 / T3）：
    /// 视觉 tick 与环境步是两条独立的累加器，绝不能合并。
    /// 验收 A2/A3 的「x60 跑 60 秒 ≈ 180 步」「单步 5 次 = 5×单步量」都在这里钉死。
    /// </summary>
    public class ClockTests
    {
        DataBundle _data;

        [OneTimeSetUp]
        public void LoadData() => _data = TestData.LoadReal();

        World NewWorld() => new World(_data, 1, 1);

        SimulationClock NewClock(out World world)
        {
            world = NewWorld();
            return new SimulationClock(world, _data.Config);
        }

        [Test]
        public void 不到一个环境步就不推进世界()
        {
            var clock = NewClock(out var world);
            float env = _data.Config.EnvStepSeconds;

            Assert.AreEqual(0, clock.Advance(env * 0.5f));
            Assert.AreEqual(0, clock.Advance(env * 0.49f));
            Assert.AreEqual(1, clock.Advance(env * 0.02f), "累加够了就应该恰好推进 1 步");
            Assert.AreEqual(1, world.EnvStepCount);
        }

        [Test]
        public void 视觉tick按真实时间走且不受暂停影响()
        {
            var clock = NewClock(out _);
            clock.Pause();
            clock.Advance(1f);
            Assert.AreEqual(_data.Config.TickRate, clock.VisualTicks, "1 秒应该有 tickRate 次视觉 tick");
            Assert.AreEqual(0, clock.LastStepCount, "暂停时世界不许推进");
        }

        [Test]
        public void x60跑一秒约三步()
        {
            var clock = NewClock(out var world);
            clock.SetSpeed(60f);
            int steps = clock.Advance(1f);
            Assert.AreEqual(3, steps, "envStepSeconds=20 时 x60 一秒 = 3 步");
            Assert.AreEqual(3, world.EnvStepCount);
        }

        [Test]
        public void x60跑六十秒约一百八十步()
        {
            var clock = NewClock(out var world);
            clock.SetSpeed(60f);
            int total = 0;
            for (int i = 0; i < 60; i++) total += clock.Advance(1f);
            Assert.AreEqual(180, total, "验收 A2：x60 跑 60 秒 ≈ 6 个游戏日（180 步）");
            Assert.AreEqual(180, world.EnvStepCount);
        }

        [Test]
        public void 一帧最多推进MaxEnvStepsPerFrame步且不欠账()
        {
            var clock = NewClock(out _);
            clock.SetSpeed(60f);
            int steps = clock.Advance(600f);
            Assert.AreEqual(_data.Config.MaxEnvStepsPerFrame, steps, "单帧步数必须被夹住，否则会卡死");

            int next = clock.Advance(1f);
            Assert.AreEqual(3, next, "追不上时应该丢掉欠账，而不是下一帧补 300 步");
        }

        [Test]
        public void 单步五次就是五步()
        {
            var clock = NewClock(out var world);
            clock.Pause();
            for (int i = 0; i < 5; i++) clock.StepOnce();
            Assert.AreEqual(5, world.EnvStepCount, "验收 A3：单步 5 次 = 5 个环境步");
            Assert.IsTrue(clock.Paused, "单步不应该顺手把暂停取消掉");
        }

        [Test]
        public void 单步会清零进度不乱补()
        {
            var clock = NewClock(out var world);
            float env = _data.Config.EnvStepSeconds;

            clock.Advance(env * 0.9f);
            clock.StepOnce();
            Assert.AreEqual(1, world.EnvStepCount);
            Assert.AreEqual(0, clock.Advance(env * 0.5f), "单步之后进度应该从 0 重新算");
        }

        [Test]
        public void 切到高倍速不会立刻补跑一串()
        {
            var clock = NewClock(out _);
            clock.Advance(_data.Config.EnvStepSeconds * 0.9f);
            clock.SetSpeed(60f);
            Assert.LessOrEqual(clock.Advance(0.016f), 1, "换倍速的瞬间不该把旧零头变成一串步");
        }

        [Test]
        public void 热重载换envStepSeconds立刻生效()
        {
            var clock = NewClock(out _);
            string text = System.IO.File.ReadAllText(System.IO.Path.Combine(TestData.Folder, TestData.ConfigFile));
            text = TestData.PatchConfig(text, "envStepSeconds", "10");
            DataBundle reloaded = TestData.LoadWith(TestData.ConfigFile, text);

            clock.ApplyConfig(reloaded.Config);
            Assert.AreEqual(10f, (float)clock.EnvIntervalSeconds, 1e-4f);
        }

        [Test]
        public void 速度必须为正()
        {
            var clock = NewClock(out _);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => clock.SetSpeed(0f));
        }
    }
}
