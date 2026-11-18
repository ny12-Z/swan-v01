using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Swan.Core.Data
{
    /// <summary>一列（表头 → 下标）。</summary>
    public sealed class CsvColumn
    {
        public readonly string Name;
        public readonly int Index;

        public CsvColumn(string name, int index)
        {
            Name = name;
            Index = index;
        }
    }

    /// <summary>
    /// 一行数据。所有取值器都按「列名」拿，不做列号硬编码（交接包 §10.1）。
    /// 空单元格 → 返回调用方给的默认值；默认值由表定义给，不给就报错，绝不当成 0 悄悄吞掉。
    /// </summary>
    public sealed class CsvRow
    {
        readonly CsvDocument _doc;
        readonly string[] _fields;

        public readonly string FileName;
        /// <summary>该行在文件里的行号（1 起，含表头行）。</summary>
        public readonly int Line;

        internal CsvRow(CsvDocument doc, string[] fields, int line)
        {
            _doc = doc;
            _fields = fields;
            FileName = doc.FileName;
            Line = line;
        }

        public bool Has(string column)
        {
            return _doc.TryGetColumn(column, out _);
        }

        public string Raw(string column, string fallback = null)
        {
            int index = _doc.RequireColumn(column, this);
            if (index >= _fields.Length) return fallback;
            var value = _fields[index];
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        public string Require(string column)
        {
            var value = Raw(column);
            if (value == null) throw Error(column, "必填列不能为空");
            return value;
        }

        public float Float(string column, float? fallback = null)
        {
            var value = Raw(column);
            if (value == null)
            {
                if (fallback.HasValue) return fallback.Value;
                throw Error(column, "必填数值列不能为空");
            }
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                throw Error(column, $"「{value}」不是合法数值");
            return result;
        }

        public int Int(string column, int? fallback = null)
        {
            var value = Raw(column);
            if (value == null)
            {
                if (fallback.HasValue) return fallback.Value;
                throw Error(column, "必填整数列不能为空");
            }
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                throw Error(column, $"「{value}」不是合法整数");
            return result;
        }

        public bool Bool(string column, bool? fallback = null)
        {
            var value = Raw(column);
            if (value == null)
            {
                if (fallback.HasValue) return fallback.Value;
                throw Error(column, "必填布尔列不能为空");
            }
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            throw Error(column, $"「{value}」不是 0/1 或 true/false");
        }

        /// <summary>把一列拆成字符串数组（分隔符支持 | 或 ; 或 , 由调用方指定）。</summary>
        public string[] Split(string column, char[] separators, string fallback = null)
        {
            var value = Raw(column, fallback);
            if (value == null) return Array.Empty<string>();
            var parts = value.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts;
        }

        public DataLoadException Error(string column, string message)
        {
            return new DataLoadException(FileName, Line, column, message);
        }

        public DataLoadException Error(string message)
        {
            return new DataLoadException(FileName, Line, null, message);
        }
    }

    /// <summary>
    /// 自写的 CSV 读取器（交接包 §10.1：不引第三方库）。
    /// 支持双引号包裹、"" 转义、字段内换行；空行跳过并记警告；列数不匹配直接报错。
    /// </summary>
    public sealed class CsvDocument
    {
        readonly List<CsvColumn> _columns = new List<CsvColumn>();
        readonly Dictionary<string, int> _columnIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<CsvRow> _rows = new List<CsvRow>();

        public string FileName { get; }
        public IReadOnlyList<CsvColumn> Columns => _columns;
        public IReadOnlyList<CsvRow> Rows => _rows;

        CsvDocument(string fileName)
        {
            FileName = fileName;
        }

        internal bool TryGetColumn(string name, out int index)
        {
            return _columnIndex.TryGetValue(name, out index);
        }

        internal int RequireColumn(string name, CsvRow row)
        {
            if (_columnIndex.TryGetValue(name, out var index)) return index;
            // 列名不存在属于「表结构变了」，报错时带上正在读的那一行，方便定位。
            throw new DataLoadException(FileName, row.Line, name, "表里没有这一列（表头可能被改过）");
        }

        public static CsvDocument Parse(string text, string fileName, DataWarnings warnings = null)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (text.Length > 0 && text[0] == '\uFEFF')
            {
                warnings?.Add($"{fileName}: 文件带 BOM，已自动忽略（建议存成 UTF-8 无 BOM）");
                text = text.Substring(1);
            }

            var doc = new CsvDocument(fileName);
            var rowLines = new List<int>();
            var rawRows = Tokenize(text, fileName, warnings, rowLines);
            if (rawRows.Count == 0)
                throw new DataLoadException(fileName, 1, null, "表是空的，至少要有表头");

            // 表头
            var header = rawRows[0];
            for (int i = 0; i < header.Length; i++)
            {
                var name = header[i]?.Trim() ?? string.Empty;
                if (name.Length == 0)
                    throw new DataLoadException(fileName, rowLines[0], null, $"表头第 {i + 1} 列的列名是空的");
                if (doc._columnIndex.ContainsKey(name))
                    throw new DataLoadException(fileName, rowLines[0], name, "表头列名重复");
                doc._columnIndex[name] = i;
                doc._columns.Add(new CsvColumn(name, i));
            }

            int expected = header.Length;
            for (int r = 1; r < rawRows.Count; r++)
            {
                var fields = rawRows[r];
                int line = rowLines[r];
                if (fields.Length != expected)
                {
                    string missing = fields.Length < expected
                        ? $"缺列 {header[fields.Length]?.Trim()}"
                        : "多出字段";
                    throw new DataLoadException(fileName, line, null,
                        $"列数不对：表头 {expected} 列，本行 {fields.Length} 列（{missing}）");
                }
                for (int i = 0; i < fields.Length; i++) fields[i] = fields[i]?.Trim();
                doc._rows.Add(new CsvRow(doc, fields, line));
            }

            return doc;
        }

        /// <summary>逐字符扫描：正确处理引号内的逗号与换行。行号写进调用方给的 rowLines。</summary>
        static List<string[]> Tokenize(string text, string fileName, DataWarnings warnings, List<int> rowLines)
        {
            var rows = new List<string[]>();
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            bool rowHasContent = false;
            int line = 1;
            int rowStartLine = 1;

            void EndField()
            {
                fields.Add(sb.ToString());
                sb.Length = 0;
            }

            void EndRow()
            {
                EndField();
                if (rowHasContent)
                {
                    rows.Add(fields.ToArray());
                    rowLines.Add(rowStartLine);
                }
                else if (fields.Count > 1)
                {
                    // 只有逗号的行，视为空行
                    warnings?.Add($"{fileName}: 第 {rowStartLine} 行是空行，已跳过");
                }
                fields.Clear();
                rowHasContent = false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        if (c == '\n') line++;
                        sb.Append(c);
                    }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        rowHasContent = true;
                        break;
                    case ',':
                        EndField();
                        rowHasContent = true;
                        break;
                    case '\r':
                        break;
                    case '\n':
                        EndRow();
                        line++;
                        rowStartLine = line;
                        break;
                    default:
                        sb.Append(c);
                        if (!char.IsWhiteSpace(c)) rowHasContent = true;
                        break;
                }
            }

            EndRow();
            return rows;
        }
    }
}
