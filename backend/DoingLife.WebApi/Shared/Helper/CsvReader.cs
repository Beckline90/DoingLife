using System.Text;

namespace DoingLife.WebApi.Shared.Helper
{
    /// <summary>
    /// Minimal CSV reader: commas, "quoted, fields", "" for a quote inside a quoted field,
    /// and line breaks inside quotes. The first row is the header.
    /// </summary>
    public static class CsvReader
    {
        public static List<CsvRow> Read(string path)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);   // strips a BOM if there is one
            var records = Parse(text);

            var result = new List<CsvRow>();
            if (records.Count == 0)
            {
                return result;
            }

            var header = records[0].Select(h => h.Trim()).ToList();

            for (var i = 1; i < records.Count; i++)
            {
                var record = records[i];

                // Skip completely blank lines.
                if (record.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var c = 0; c < header.Count; c++)
                {
                    values[header[c]] = c < record.Count ? record[c] : "";
                }

                // Line number as the person sees it in an editor (header is line 1).
                result.Add(new CsvRow(Path.GetFileName(path), i + 1, values));
            }

            return result;
        }

        private static List<List<string>> Parse(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');   // "" is one quote character
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(ch);
                    }
                }
                else if (ch == '"')
                {
                    inQuotes = true;
                }
                else if (ch == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (ch == '\r')
                {
                    // ignored: the \n that follows ends the row
                }
                else if (ch == '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else
                {
                    field.Append(ch);
                }
            }

            // Last row when the file does not end with a line break.
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            return rows;
        }
    }

    /// <summary>One data row, with typed getters that explain what is wrong and where.</summary>
    public class CsvRow
    {
        private readonly Dictionary<string, string> _values;
        public string File { get; }
        public int Line { get; }

        public CsvRow(string file, int line, Dictionary<string, string> values)
        {
            File = file;
            Line = line;
            _values = values;
        }

        public Exception Error(string message) =>
            new InvalidDataException($"{File} line {Line}: {message}");

        private string Raw(string column)
        {
            if (!_values.TryGetValue(column, out var value))
            {
                throw Error($"the column '{column}' is missing");
            }
            return value.Trim();
        }

        public string Required(string column)
        {
            var value = Raw(column);
            if (value.Length == 0)
            {
                throw Error($"'{column}' is required");
            }
            return value;
        }

        public string? Optional(string column)
        {
            var value = Raw(column);
            return value.Length == 0 ? null : value;
        }

        // For columns that only some files have (for example 'notes'): a missing column is fine.
        public string? OptionalIfPresent(string column)
        {
            if (!_values.TryGetValue(column, out var value)) return null;
            value = value.Trim();
            return value.Length == 0 ? null : value;
        }

        public bool Bool(string column)
        {
            var value = Raw(column);
            if (value.Length == 0) return false;
            if (bool.TryParse(value, out var result)) return result;
            throw Error($"'{column}' must be true or false, not '{value}'");
        }

        public bool? OptionalBool(string column)
        {
            var value = Raw(column);
            if (value.Length == 0) return null;   // blank means "not checked", not "no"
            if (bool.TryParse(value, out var result)) return result;
            throw Error($"'{column}' must be true or false, not '{value}'");
        }

        public int RequiredInt(string column)
        {
            var value = Required(column);
            if (int.TryParse(value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var result)) return result;
            throw Error($"'{column}' must be a whole number, not '{value}'");
        }

        public int? OptionalInt(string column)
        {
            var value = Optional(column);
            if (value == null) return null;
            if (int.TryParse(value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var result)) return result;
            throw Error($"'{column}' must be a whole number, not '{value}'");
        }

        public decimal? OptionalDecimal(string column)
        {
            var value = Optional(column);
            if (value == null) return null;
            if (decimal.TryParse(value, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var result)) return result;
            throw Error($"'{column}' must be a number, not '{value}'");
        }

        public DateOnly RequiredDate(string column)
        {
            var value = Required(column);
            if (DateOnly.TryParseExact(value, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var result)) return result;
            throw Error($"'{column}' must be a date like 2026-11-26, not '{value}'");
        }

        public DateOnly? OptionalDate(string column)
        {
            var value = Optional(column);
            if (value == null) return null;
            if (DateOnly.TryParseExact(value, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var result)) return result;
            throw Error($"'{column}' must be a date like 2026-11-26, not '{value}'");
        }
    }
}
