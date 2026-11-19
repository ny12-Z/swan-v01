using System;
using System.Diagnostics;
using System.IO;
using Debug = UnityEngine.Debug;
using Swan.Core;
using UnityEngine;

namespace Swan.Game
{
    /// <summary>
    /// 把设置面板改过的 config 写回磁盘。数据目录和游戏启动时读取的是同一个目录，
    /// 所以「保存 → 重新载入」不会出现两份配置。
    /// </summary>
    public static class ConfigIO
    {
        public const string FileName = "config.json";
        public const string DefaultFileName = "config.default.json";

        public static string ConfigPath(string directory) => Path.Combine(directory, FileName);

        public static string DefaultPath(string directory) => Path.Combine(directory, DefaultFileName);

        public static bool Save(EngineConfig config, string directory, out string message)
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(ConfigPath(directory), config.ToJson(), new System.Text.UTF8Encoding(false));
                message = $"已保存到 {ConfigPath(directory)}";
                return true;
            }
            catch (Exception ex)
            {
                message = $"保存失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>用 config.default.json 覆盖 config.json；调用方负责重新载入。</summary>
        public static bool RestoreDefault(string directory, out string message)
        {
            try
            {
                string source = DefaultPath(directory);
                if (!File.Exists(source))
                {
                    message = $"找不到默认配置：{source}";
                    return false;
                }
                File.Copy(source, ConfigPath(directory), true);
                message = "已恢复默认配置";
                return true;
            }
            catch (Exception ex)
            {
                message = $"恢复默认失败：{ex.Message}";
                return false;
            }
        }

        public static void OpenFolder(string directory)
        {
            try
            {
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{directory}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SWAN] 打开目录失败：{ex.Message}");
            }
        }
    }
}
