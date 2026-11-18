using System;
using Swan.Core.Data;

namespace Swan.Core.Sim
{
    /// <summary>
    /// 双时钟（交接包 §4）：表现层 tick 与环境步是两件事，绝不能合并。
    ///
    /// - 视觉 tick：固定 TickRate 次/秒，只管「什么时候重画」，不影响数值。
    /// - 环境步：每 EnvStepSeconds 秒推进一次世界，速度倍率只改这个间隔。
    ///
    /// 这个类不引用引擎。时间只有两种来源：Advance(dt)，或者手动 StepOnce()。
    /// 所以「按倍速跑 60 秒应该推进多少步」可以在 EditMode 测试里逐帧喂 dt 精确验证。
    /// </summary>
    public sealed class SimulationClock
    {
        readonly World _world;
        EngineConfig _config;

        double _envAccumulator;
        double _visualAccumulator;
        long _envStepsRun;
        long _visualTicks;

        public bool Paused { get; private set; }

        /// <summary>环境步速度倍率（1 / 10 / 60）。</summary>
        public float Speed { get; private set; } = 1f;

        /// <summary>上一次 Advance 里世界推进了几个环境步。</summary>
        public int LastStepCount { get; private set; }

        public SimulationClock(World world, EngineConfig config)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public EngineConfig Config => _config;
        public double VisualTickIntervalSeconds => 1.0 / _config.TickRate;
        public double EnvIntervalSeconds => _config.EnvStepSeconds / Speed;
        public long EnvStepsRun => _envStepsRun;
        public long VisualTicks => _visualTicks;

        /// <summary>当前等待下一个环境步的进度（0..1），HUD 用。</summary>
        public float EnvProgress => _config.EnvStepSeconds <= 0f
            ? 0f
            : (float)Math.Min(1.0, _envAccumulator / (_config.EnvStepSeconds / Speed));

        /// <summary>热重载换了 config 后同步时钟参数（不重置已累计的时间）。</summary>
        public void ApplyConfig(EngineConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            // 速度倍率由外面裁剪到 config.speedLevels 里，这里不擅自改 Speed
        }

        public void Pause() => Paused = true;

        public void Resume() => Paused = false;

        public void TogglePause() => Paused = !Paused;

        public void SetSpeed(float speed)
        {
            if (speed <= 0f) throw new ArgumentOutOfRangeException(nameof(speed), "速度倍率必须 > 0");
            Speed = speed;
            // 换速度时不要把旧速度攒下的零头带过去，否则切到 x60 会立刻补跑一串
            _envAccumulator = Math.Min(_envAccumulator, EnvIntervalSeconds);
        }

        /// <summary>单步：立刻推进恰好 1 个环境步（无论是否暂停、无论当前倍速）。</summary>
        public void StepOnce()
        {
            _world.StepEnvironment();
            _envStepsRun++;
            _envAccumulator = 0.0;
            LastStepCount = 1;
        }

        /// <summary>
        /// 推进时钟。返回本次实际执行的环境步数。
        /// 每帧最多执行 MaxEnvStepsPerFrame 步，防止 x60 掉帧时雪崩。
        /// </summary>
        public int Advance(float deltaTime)
        {
            if (deltaTime < 0f) deltaTime = 0f;
            LastStepCount = 0;

            // 视觉 tick 永远按真实时间走：暂停时也要重画（悬停 HUD 还得跟手）
            _visualAccumulator += deltaTime;
            double visualInterval = VisualTickIntervalSeconds;
            if (visualInterval > 0.0)
            {
                while (_visualAccumulator >= visualInterval)
                {
                    _visualAccumulator -= visualInterval;
                    _visualTicks++;
                }
            }

            if (Paused) return 0;

            double envInterval = EnvIntervalSeconds;
            if (envInterval <= 0.0) return 0;

            _envAccumulator += deltaTime;
            int steps = 0;
            while (_envAccumulator >= envInterval && steps < _config.MaxEnvStepsPerFrame)
            {
                _world.StepEnvironment();
                _envAccumulator -= envInterval;
                steps++;
                _envStepsRun++;
            }

            // 追不上就把欠账扔掉：宁可世界慢一点，也不要一帧补 100 步卡死
            if (steps >= _config.MaxEnvStepsPerFrame && _envAccumulator >= envInterval)
            {
                _envAccumulator = 0.0;
            }

            LastStepCount = steps;
            return steps;
        }
    }
}
