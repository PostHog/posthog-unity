using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PostHog
{
    /// <summary>
    /// Simple JSON serializer that handles Dictionary&lt;string, object&gt; properly.
    /// Unity's JsonUtility doesn't support dictionaries, so we need a custom solution.
    /// </summary>
    static class JsonSerializer
    {
        public static string Serialize(object obj)
        {
            var sb = new StringBuilder();
            SerializeValue(obj, sb);
            return sb.ToString();
        }

        public static string SerializeEvent(PostHogEvent evt)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"uuid\":{EscapeString(evt.Uuid)},");
            sb.Append($"\"event\":{EscapeString(evt.Event)},");
            sb.Append($"\"distinct_id\":{EscapeString(evt.DistinctId)},");
            sb.Append($"\"timestamp\":{EscapeString(evt.Timestamp)},");
            sb.Append("\"properties\":");
            SerializeValue(evt.Properties, sb);
            sb.Append("}");
            return sb.ToString();
        }

        public static string SerializeBatch(BatchPayload payload)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"api_key\":{EscapeString(payload.ApiKey)},");
            sb.Append($"\"sent_at\":{EscapeString(payload.SentAt)},");
            sb.Append("\"batch\":[");

            for (int i = 0; i < payload.Batch.Count; i++)
            {
                if (i > 0)
                    sb.Append(",");
                sb.Append(SerializeEvent(payload.Batch[i]));
            }

            sb.Append("]}");
            return sb.ToString();
        }

        static void SerializeValue(object value, StringBuilder sb)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            switch (value)
            {
                case string s:
                    sb.Append(EscapeString(s));
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case float f:
                    sb.Append(f.ToString(CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString(CultureInfo.InvariantCulture));
                    break;
                case decimal dec:
                    sb.Append(dec.ToString(CultureInfo.InvariantCulture));
                    break;
                case DateTime dt:
                    sb.Append(EscapeString(dt.ToString("o")));
                    break;
                case DateTimeOffset dto:
                    sb.Append(EscapeString(dto.ToString("o")));
                    break;
                case IDictionary<string, object> dict:
                    SerializeDictionary(dict, sb);
                    break;
                case IDictionary genericDict:
                    SerializeGenericDictionary(genericDict, sb);
                    break;
                case IList list:
                    SerializeList(list, sb);
                    break;
                default:
                    // For other types, try to convert to string
                    sb.Append(EscapeString(value.ToString()));
                    break;
            }
        }

        static void SerializeDictionary(IDictionary<string, object> dict, StringBuilder sb)
        {
            sb.Append("{");
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first)
                    sb.Append(",");
                first = false;
                sb.Append(EscapeString(kvp.Key));
                sb.Append(":");
                SerializeValue(kvp.Value, sb);
            }
            sb.Append("}");
        }

        static void SerializeGenericDictionary(IDictionary dict, StringBuilder sb)
        {
            sb.Append("{");
            bool first = true;
            foreach (DictionaryEntry entry in dict)
            {
                if (!first)
                    sb.Append(",");
                first = false;
                sb.Append(EscapeString(entry.Key?.ToString() ?? ""));
                sb.Append(":");
                SerializeValue(entry.Value, sb);
            }
            sb.Append("}");
        }

        static void SerializeList(IList list, StringBuilder sb)
        {
            sb.Append("[");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                    sb.Append(",");
                SerializeValue(list[i], sb);
            }
            sb.Append("]");
        }

        static string EscapeString(string s)
        {
            if (s == null)
                return "null";

            var sb = new StringBuilder();
            sb.Append("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    default:
                        if (c < 32)
                        {
                            sb.Append($"\\u{(int)c:x4}");
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }

        /// <summary>
        /// Simple JSON deserializer for basic types.
        /// </summary>
        public static Dictionary<string, object> DeserializeDictionary(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            var result = new Dictionary<string, object>();
            json = json.Trim();

            if (!json.StartsWith("{") || !json.EndsWith("}"))
                return null;

            // Remove outer braces
            json = json.Substring(1, json.Length - 2).Trim();

            if (string.IsNullOrEmpty(json))
                return result;

            int pos = 0;
            while (pos < json.Length)
            {
                // Skip whitespace
                while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                    pos++;
                if (pos >= json.Length)
                    break;

                // Parse key
                string key = ParseString(json, ref pos);
                if (key == null)
                    break;

                // Skip colon
                while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                    pos++;
                if (pos >= json.Length || json[pos] != ':')
                    break;
                pos++;

                // Skip whitespace
                while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                    pos++;

                // Parse value
                object value = ParseValue(json, ref pos);
                result[key] = value;

                // Skip comma
                while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                    pos++;
                if (pos < json.Length && json[pos] == ',')
                    pos++;
            }

            return result;
        }

        static string ParseString(string json, ref int pos)
        {
            if (pos >= json.Length || json[pos] != '"')
                return null;
            pos++; // Skip opening quote

            var sb = new StringBuilder();
            while (pos < json.Length)
            {
                char c = json[pos];
                if (c == '"')
                {
                    pos++;
                    return sb.ToString();
                }
                if (c == '\\' && pos + 1 < json.Length)
                {
                    pos++;
                    char escaped = json[pos];
                    switch (escaped)
                    {
                        case '"':
                            sb.Append('"');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        default:
                            sb.Append(escaped);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
                pos++;
            }
            return sb.ToString();
        }

        static object ParseValue(string json, ref int pos)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                pos++;
            if (pos >= json.Length)
                return null;

            char c = json[pos];

            // String
            if (c == '"')
            {
                return ParseString(json, ref pos);
            }

            // Object
            if (c == '{')
            {
                return ParseObject(json, ref pos);
            }

            // Array
            if (c == '[')
            {
                return ParseArray(json, ref pos);
            }

            // Boolean or null
            if (json.Substring(pos).StartsWith("true"))
            {
                pos += 4;
                return true;
            }
            if (json.Substring(pos).StartsWith("false"))
            {
                pos += 5;
                return false;
            }
            if (json.Substring(pos).StartsWith("null"))
            {
                pos += 4;
                return null;
            }

            // Number
            int start = pos;
            while (
                pos < json.Length
                && (
                    char.IsDigit(json[pos])
                    || json[pos] == '.'
                    || json[pos] == '-'
                    || json[pos] == 'e'
                    || json[pos] == 'E'
                    || json[pos] == '+'
                )
            )
            {
                pos++;
            }

            string numStr = json.Substring(start, pos - start);
            if (numStr.Contains(".") || numStr.Contains("e") || numStr.Contains("E"))
            {
                if (
                    double.TryParse(
                        numStr,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double d
                    )
                )
                    return d;
            }
            else
            {
                if (
                    long.TryParse(
                        numStr,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out long l
                    )
                )
                    return l;
            }

            return null;
        }

        static Dictionary<string, object> ParseObject(string json, ref int pos)
        {
            var result = new Dictionary<string, object>();
            pos++; // Skip '{'

            while (pos < json.Length)
            {
                while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                    pos++;
                if (pos >= json.Length || json[pos] == '}')
                {
                    pos++;
                    return result;
                }

                string key = ParseString(json, ref pos);
                if (key == null)
                    break;

                while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                    pos++;
                if (pos >= json.Length || json[pos] != ':')
                    break;
                pos++;

                object value = ParseValue(json, ref pos);
                result[key] = value;

                while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                    pos++;
                if (pos < json.Length && json[pos] == ',')
                    pos++;
            }

            return result;
        }

        static List<object> ParseArray(string json, ref int pos)
        {
            var result = new List<object>();
            pos++; // Skip '['

            while (pos < json.Length)
            {
                while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                    pos++;
                if (pos >= json.Length || json[pos] == ']')
                {
                    pos++;
                    return result;
                }

                object value = ParseValue(json, ref pos);
                result.Add(value);

                while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                    pos++;
                if (pos < json.Length && json[pos] == ',')
                    pos++;
            }

            return result;
        }
    }
}
