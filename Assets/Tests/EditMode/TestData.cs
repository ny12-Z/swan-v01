using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Swan.Core;
using Swan.Core.Data;
using UnityEngine;

namespace Swan.Tests
{
    /// <summary>
    /// 测试用的数据装配：从真实的 StreamingAssets/Data 读，或者读出来再「局部改一行」。
    ///
    /// 为什么要能改一行：热重载自检（交接包 §10.5）本质是「把表改一个数，不重启，看行为变没变」，
    /// 测试必须能造出「改过的表」，否则只能靠人手改文件。
    /// </summary>
    public static class TestData
    {
        public const string ConfigFile = "config.json";
        public const string PresetFile = "map-preset.json";
        public const string TerrainFile = "数值表(地形属性表)-机读.csv";
        public const string PlantFile = "数值表(植物类型表)-机读.csv";
        public const string TraitFile = "数值表(植物性状表)-机读.csv";
        public const string RuleFile = "数值表(世界法则表)-机读.csv";

        static readonly string[] Files =
        {
            ConfigFile, PresetFile, TerrainFile, PlantFile, TraitFile, RuleFile,
        };

        public static string Folder => Path.Combine(Application.streamingAssetsPath, "Data");

        public static DataBundle LoadReal() => DataBundle.Load(new DirectoryFileSource(Folder));

        /// <summary>把真实数据全部读进内存，之后可以覆盖其中某一个文件。</summary>
        public static MemoryFileSource FromDisk()
        {
            var source = new MemoryFileSource();
            foreach (string file in Files)
            {
                string path = Path.Combine(Folder, file);
                if (!File.Exists(path)) throw new FileNotFoundException($"测试数据缺少文件：{path}", path);
                source.Add(file, File.ReadAllText(path, Encoding.UTF8));
            }
            return source;
        }

        /// <summary>在真实数据基础上，替换某个文件的文本。</summary>
        public static DataBundle LoadWith(string fileName, string text)
        {
            return DataBundle.Load(FromDisk().Add(fileName, text));
        }

        /// <summary>
        /// 按「列名 + 主键」改 CSV 里的一个格子。只用于改数值/文本格，
        /// 被改的列不能是带引号或逗号的内容（表里的 desc 不在这里改）。
        /// </summary>
        public static string PatchCsv(string text, string columnName, string keyColumn, string key, string value)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            string[] header = lines[0].Split(',');
            int column = Array.IndexOf(header, columnName);
            int keyIndex = Array.IndexOf(header, keyColumn);
            if (column < 0) throw new ArgumentException($"表头没有列 {columnName}");
            if (keyIndex < 0) throw new ArgumentException($"表头没有列 {keyColumn}");

            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                string[] cells = lines[i].Split(',');
                if (cells[keyIndex].Trim() != key) continue;
                cells[column] = value;
                lines[i] = string.Join(",", cells);
                return string.Join("\n", lines);
            }

            throw new ArgumentException($"表里没有 {keyColumn}={key}");
        }

        /// <summary>把 config.json 里某个键的值换掉（值里不要带逗号，speedLevels 这类数组别用这个）。</summary>
        public static string PatchConfig(string text, string key, string jsonValue)
        {
            int keyIndex = text.IndexOf('"' + key + '"', StringComparison.Ordinal);
            if (keyIndex < 0) throw new ArgumentException($"config.json 里没有键 {key}");

            int colon = text.IndexOf(':', keyIndex);
            int end = colon + 1;
            while (end < text.Length && text[end] != ',' && text[end] != '\n' && text[end] != '\r') end++;
            return text.Substring(0, colon + 1) + " " + jsonValue + text.Substring(end);
        }

        public static List<string> ReadAllLines(string fileName) =>
            new List<string>(File.ReadAllLines(Path.Combine(Folder, fileName), Encoding.UTF8));
    }
}
