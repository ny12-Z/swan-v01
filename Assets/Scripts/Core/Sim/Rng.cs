namespace Swan.Core.Sim
{
    /// <summary>
    /// 确定性随机数（xorshift32）。同一个 seed 永远给出同一张初始地图——
    /// 这既是「可复现 bug」的前提，也是对照曲线的前提。
    /// </summary>
    public sealed class Rng
    {
        uint _state;

        public Rng(uint seed)
        {
            // 0 是 xorshift 的退化态，换成黄金比例常数
            _state = seed == 0u ? 0x9E3779B9u : seed;
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>[0,1) 均匀分布。</summary>
        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        /// <summary>[min,max) 均匀分布。</summary>
        public float Range(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }
    }
}
