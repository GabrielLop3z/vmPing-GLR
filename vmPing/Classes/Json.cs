using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace vmPing.Classes
{
    /// <summary>
    /// Serializador JSON mínimo (sin dependencias externas) usado por la API HTTP local.
    /// Soporta diccionarios, listas, tipos primitivos y DateTime (ISO-8601 UTC).
    /// </summary>
    public static class Json
    {
        public static string Serialize(object value)
        {
            var builder = new StringBuilder(512);
            WriteValue(builder, value, 0);
            return builder.ToString();
        }

        private static void WriteValue(StringBuilder builder, object value, int depth)
        {
            if (value == null || value == DBNull.Value)
            {
                builder.Append("null");
                return;
            }

            if (value is string text)
            {
                WriteString(builder, text);
                return;
            }

            if (value is bool boolean)
            {
                builder.Append(boolean ? "true" : "false");
                return;
            }

            if (value is DateTime dateTime)
            {
                var formatted = dateTime.Kind == DateTimeKind.Local
                    ? new DateTimeOffset(dateTime).ToString("o", CultureInfo.InvariantCulture)
                    : dateTime.ToString("o", CultureInfo.InvariantCulture);
                WriteString(builder, formatted);
                return;
            }

            if (value is DateTimeOffset dateTimeOffset)
            {
                WriteString(builder, dateTimeOffset.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                return;
            }

            if (value is Enum)
            {
                WriteString(builder, value.ToString());
                return;
            }

            if (value is IDictionary<string, object> dictionary)
            {
                WriteObject(builder, dictionary, depth);
                return;
            }

            if (value is IDictionary legacyDictionary)
            {
                builder.Append('{');
                var first = true;
                foreach (DictionaryEntry entry in legacyDictionary)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteString(builder, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                    builder.Append(':');
                    WriteValue(builder, entry.Value, depth + 1);
                }

                builder.Append('}');
                return;
            }

            if (value is IEnumerable enumerable)
            {
                builder.Append('[');
                var first = true;
                foreach (var item in enumerable)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteValue(builder, item, depth + 1);
                }

                builder.Append(']');
                return;
            }

            if (value is double doubleValue)
            {
                if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue))
                {
                    builder.Append("null");
                }
                else
                {
                    builder.Append(doubleValue.ToString("R", CultureInfo.InvariantCulture));
                }

                return;
            }

            if (value is float floatValue)
            {
                if (float.IsNaN(floatValue) || float.IsInfinity(floatValue))
                {
                    builder.Append("null");
                }
                else
                {
                    builder.Append(floatValue.ToString("R", CultureInfo.InvariantCulture));
                }

                return;
            }

            if (value is IFormattable formattable)
            {
                builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
            }

            WriteString(builder, value.ToString());
        }

        private static void WriteObject(StringBuilder builder, IDictionary<string, object> dictionary, int depth)
        {
            builder.Append('{');
            var first = true;

            foreach (var pair in dictionary)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                WriteString(builder, pair.Key ?? string.Empty);
                builder.Append(':');
                WriteValue(builder, pair.Value, depth + 1);
            }

            builder.Append('}');
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');

            foreach (var character in value)
            {
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
        }
    }
}
