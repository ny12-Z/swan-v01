using System;
using System.Globalization;

namespace Swan.Game
{
    /// <summary>
    /// 命令行参数读取。
    ///
    /// 存在的意义是「可维护性」：打包出来的 exe 不需要改代码、也不需要点按钮，
    /// 就能用参数指定数据目录、加速倍速、跑多久之后截图——自动验收脚本靠它。
    ///
    /// 只做字符串解析，不碰打法（玩法参数一律走 config.json）。
    /// </summary>
    public static class CommandLine
    {
        static string[] _args;

        public static string[] Args => _args ?? (_args = Environment.GetCommandLineArgs());

        /// <summary>取 "-名字 值" 里的值；没有这个参数就返回 fallback。</summary>
        public static string Get(string name, string fallback = null)
        {
            string[] args = Args;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }

            return fallback;
        }

        public static bool Has(string name)
        {
            string[] args = Args;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        public static float GetFloat(string name, float fallback)
        {
            string text = Get(name);
            if (text == null) return fallback;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : fallback;
        }

        /// <summary>测试里改过参数后复位（编辑器里跑测试时参数是编辑器的）。</summary>
        public static void ResetCache() => _args = null;
    }
}
