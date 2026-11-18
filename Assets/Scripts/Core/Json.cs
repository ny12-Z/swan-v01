using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Swan.Core
{
    /// <summary>JSON 值的种类。</summary>
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object
    }

    /// <summary>
    /// 极简 JSON 值。自带 行/列，让报错能指到 config.json 的具体位置。
    /// 不引入第三方库（交接包 §10.1b：不许引入依赖）。
    /// </summary>
    public sealed class JsonValue
    {
        public JsonKind Kind;
        public double Number;
        public bool Bool;
        public string Text;
        public List<JsonValue> Items;
        public List<KeyValuePair<string, JsonValue>> Members;
        public int Line;
        public int Column;

        public bool IsNull => Kind == JsonKind.Null;

        public JsonValue Member(string key)
        {
            if (Members == null) return null;
            for (int i = 0; i < Members.Count; i++)
            {
                if (Members[i].Key == key) return Members[i].Value;
            }
            return null;
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case JsonKind.Null: return "null";
                case JsonKind.Bool: return Bool ? "true" : "false";
                case JsonKind.Number: return Number.ToString("R", CultureInfo.InvariantCulture);
                case JsonKind.String: return "\"" + Text + "\"";
                case JsonKind.Array: return "[" + (Items?.Count ?? 0) + " 项]";
                default: return "{" + (Members?.Count ?? 0) + " 字段}";
            }
        }
    }

    /// <summary>JSON 语法错误。消息里一定带 文件名 + 行 + 列。</summary>
    public sealed class JsonFormatException : Exception
    {
        public readonly string SourceName;
        public readonly int Line;
        public readonly int Column;

        public JsonFormatException(string source, int line, int column, string message)
            : base($"{source}: 第 {line} 行 · 第 {column} 列: {message}")
        {
            SourceName = source;
            Line = line;
            Column = column;
        }
    }

    /// <summary>自己写的 JSON 读取器（交接包：不得引入第三方库）。</summary>
    public static class Json
    {
        public static JsonValue Parse(string text, string source = "<json>")
        {
            var parser = new Parser(text, source);
            parser.SkipWhitespace();
            var value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw parser.Error("JSON 结束后还有多余内容");
            return value;
        }

        sealed class Parser
        {
            readonly string _text;
            readonly string _source;
            int _index;
            int _line = 1;
            int _column = 1;

            public Parser(string text, string source)
            {
                _text = text ?? string.Empty;
                _source = source;
            }

            public bool AtEnd => _index >= _text.Length;

            public JsonFormatException Error(string message)
            {
                return new JsonFormatException(_source, _line, _column, message);
            }

            char Current => _text[_index];

            void Advance()
            {
                if (_text[_index] == '\n')
                {
                    _line++;
                    _column = 1;
                }
                else
                {
                    _column++;
                }
                _index++;
            }

            public void SkipWhitespace()
            {
                while (!AtEnd)
                {
                    var c = Current;
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') Advance();
                    else break;
                }
            }

            public JsonValue ParseValue()
            {
                if (AtEnd) throw Error("内容意外结束，缺少值");
                var c = Current;
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': return ParseKeyword("true", new JsonValue { Kind = JsonKind.Bool, Bool = true });
                    case 'f': return ParseKeyword("false", new JsonValue { Kind = JsonKind.Bool, Bool = false });
                    case 'n': return ParseKeyword("null", new JsonValue { Kind = JsonKind.Null });
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error($"无法识别的字符 '{c}'");
                }
            }

            JsonValue ParseKeyword(string word, JsonValue result)
            {
                for (int i = 0; i < word.Length; i++)
                {
                    if (AtEnd || Current != word[i]) throw Error($"期望关键字 {word}");
                    Advance();
                }
                result.Line = _line;
                result.Column = _column;
                return result;
            }

            JsonValue ParseNumber()
            {
                int start = _index;
                int line = _line, column = _column;
                if (!AtEnd && Current == '-') Advance();
                while (!AtEnd && Current >= '0' && Current <= '9') Advance();
                if (!AtEnd && Current == '.')
                {
                    Advance();
                    while (!AtEnd && Current >= '0' && Current <= '9') Advance();
                }
                if (!AtEnd && (Current == 'e' || Current == 'E'))
                {
                    Advance();
                    if (!AtEnd && (Current == '+' || Current == '-')) Advance();
                    while (!AtEnd && Current >= '0' && Current <= '9') Advance();
                }
                var raw = _text.Substring(start, _index - start);
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw new JsonFormatException(_source, line, column, $"不是合法数字: {raw}");
                return new JsonValue { Kind = JsonKind.Number, Number = number, Text = raw, Line = line, Column = column };
            }

            JsonValue ParseString()
            {
                int line = _line, column = _column;
                Advance(); // 开头的引号
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw new JsonFormatException(_source, line, column, "字符串没有结束引号");
                    var c = Current;
                    if (c == '"')
                    {
                        Advance();
                        break;
                    }
                    if (c == '\\')
                    {
                        Advance();
                        if (AtEnd) throw new JsonFormatException(_source, line, column, "转义符后内容结束");
                        var esc = Current;
                        Advance();
                        switch (esc)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_index + 4 > _text.Length) throw Error("\\u 后面不足 4 位十六进制");
                                var hex = _text.Substring(_index, 4);
                                if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                    throw Error($"\\u{hex} 不是十六进制");
                                for (int i = 0; i < 4; i++) Advance();
                                sb.Append((char)code);
                                break;
                            default: throw new JsonFormatException(_source, _line, _column, $"未知转义 \\{esc}");
                        }
                        continue;
                    }
                    sb.Append(c);
                    Advance();
                }
                return new JsonValue { Kind = JsonKind.String, Text = sb.ToString(), Line = line, Column = column };
            }

            JsonValue ParseArray()
            {
                int line = _line, column = _column;
                Advance(); // [
                var items = new List<JsonValue>();
                SkipWhitespace();
                if (!AtEnd && Current == ']')
                {
                    Advance();
                    return new JsonValue { Kind = JsonKind.Array, Items = items, Line = line, Column = column };
                }
                while (true)
                {
                    SkipWhitespace();
                    items.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd) throw new JsonFormatException(_source, line, column, "数组没有结束的 ]");
                    if (Current == ',')
                    {
                        Advance();
                        continue;
                    }
                    if (Current == ']')
                    {
                        Advance();
                        break;
                    }
                    throw Error($"数组里期望 , 或 ]，实际是 '{Current}'");
                }
                return new JsonValue { Kind = JsonKind.Array, Items = items, Line = line, Column = column };
            }

            JsonValue ParseObject()
            {
                int line = _line, column = _column;
                Advance(); // {
                var members = new List<KeyValuePair<string, JsonValue>>();
                SkipWhitespace();
                if (!AtEnd && Current == '}')
                {
                    Advance();
                    return new JsonValue { Kind = JsonKind.Object, Members = members, Line = line, Column = column };
                }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || Current != '"') throw Error("对象里期望以引号开头的字段名");
                    var keyLine = _line;
                    var key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || Current != ':') throw new JsonFormatException(_source, keyLine, _column, $"字段 \"{key.Text}\" 后面缺少冒号");
                    Advance();
                    SkipWhitespace();
                    var value = ParseValue();
                    for (int i = 0; i < members.Count; i++)
                    {
                        if (members[i].Key == key.Text)
                            throw new JsonFormatException(_source, keyLine, key.Column, $"字段重复: \"{key.Text}\"");
                    }
                    members.Add(new KeyValuePair<string, JsonValue>(key.Text, value));
                    SkipWhitespace();
                    if (AtEnd) throw new JsonFormatException(_source, line, column, "对象没有结束的 }");
                    if (Current == ',')
                    {
                        Advance();
                        continue;
                    }
                    if (Current == '}')
                    {
                        Advance();
                        break;
                    }
                    throw Error($"对象里期望 , 或 }}，实际是 '{Current}'");
                }
                return new JsonValue { Kind = JsonKind.Object, Members = members, Line = line, Column = column };
            }
        }
    }
}
