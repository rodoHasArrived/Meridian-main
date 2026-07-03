using System.Text;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// RFC-4180 style CSV line splitting with a configurable delimiter and quote character.
/// Shared by the statement connectors; the legacy positional statement paths keep their
/// existing parsing untouched.
/// </summary>
public static class CsvLineSplitter
{
    private const char ByteOrderMark = '\uFEFF';

    public static IReadOnlyList<string> Split(string line, char delimiter = ',', char quote = '"')
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == quote)
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == quote)
                {
                    current.Append(quote);
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (character == delimiter && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        values.Add(current.ToString());
        return values;
    }

    /// <summary>
    /// Splits statement text into logical lines, tolerating \r\n and \r endings and a UTF-8 BOM.
    /// Blank lines are preserved so callers keep stable row numbers.
    /// </summary>
    public static IReadOnlyList<string> SplitLines(string content, char quote = '"')
    {
        var normalized = content.Length > 0 && content[0] == ByteOrderMark ? content[1..] : content;
        var lines = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (character == quote)
            {
                current.Append(character);
                if (inQuotes && index + 1 < normalized.Length && normalized[index + 1] == quote)
                {
                    current.Append(normalized[index + 1]);
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && (character == '\r' || character == '\n'))
            {
                if (character == '\r' && index + 1 < normalized.Length && normalized[index + 1] == '\n')
                {
                    index++;
                }

                lines.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        lines.Add(current.ToString());
        return lines;
    }
}
