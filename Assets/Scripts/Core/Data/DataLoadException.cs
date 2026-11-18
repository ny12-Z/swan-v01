using System;
using System.Collections.Generic;
using System.Text;

namespace Swan.Core
{
    /// <summary>
    /// 数据层错误。必须带上「文件名 + 行号 + 列名」，否则策划改表时会找不到北。
    /// 交接包 §10.2：静默跳过比崩掉更坏。
    /// </summary>
    public sealed class DataLoadException : Exception
    {
        public readonly string FileName;
        public readonly int Line;
        public readonly string Column;

        public DataLoadException(string fileName, int line, string column, string message)
            : base(Format(fileName, line, column, message))
        {
            FileName = fileName;
            Line = line;
            Column = column;
        }

        static string Format(string fileName, int line, string column, string message)
        {
            var sb = new StringBuilder();
            sb.Append(fileName);
            if (line > 0) sb.Append(" 第 ").Append(line).Append(" 行");
            if (!string.IsNullOrEmpty(column)) sb.Append(" · 列 ").Append(column);
            sb.Append(": ").Append(message);
            return sb.ToString();
        }
    }

    /// <summary>加载过程中的非致命提示（例如 BOM、未实现的性状）。照样打印，不吞掉。</summary>
    public sealed class DataWarnings
    {
        readonly List<string> _items = new List<string>();

        public IReadOnlyList<string> Items => _items;

        public void Add(string message)
        {
            _items.Add(message);
        }

        public void AddRange(IEnumerable<string> messages)
        {
            if (messages == null) return;
            _items.AddRange(messages);
        }
    }
}
