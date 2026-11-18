using System;
using System.IO;
using Swan.Core;

namespace Swan.Game
{
    /// <summary>
    /// 找到数据目录（config.json + 4 张机读表）。
    /// 开发期默认读 StreamingAssets/Data；打包后也在同一个相对位置，所以不用改代码。
    /// 命令行 `-swanDataDir &lt;path&gt;` 可以指到别处（自检脚本用）。
    /// </summary>
    public static class DataLocator
    {
        public const string DataFolderName = "Data";
        public const string CommandLineArg = "-swanDataDir";

        public static string ResolveDirectory()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], CommandLineArg, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            var basePath = UnityEngine.Application.streamingAssetsPath;
            return Path.Combine(basePath, DataFolderName);
        }

        public static IFileSource CreateSource(out string directory)
        {
            directory = ResolveDirectory();
            return new DirectoryFileSource(directory);
        }
    }
}
