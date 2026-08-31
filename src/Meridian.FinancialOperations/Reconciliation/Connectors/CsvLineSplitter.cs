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
    public static IReadOnlyList<string> SplitLines(string content)
        => SplitLines(content, maxLines: int.MaxValue);

    /// <summary>
    /// Splits statement text into logical lines, stopping once <paramref name="maxLines"/> lines have
    /// been produced. Semantics are identical to the unbounded overload for any content inside the
    /// bound, including the trailing empty segment a terminating newline produces.
    /// </summary>
    /// <remarks>
    /// The unbounded form allocated three full copies of the content before a caller could reject
    /// anything - two from the newline-normalizing <c>Replace</c> calls and one more as the split
    /// array - so a caller-supplied document sized the allocation even when its row count was already
    /// over the ingress bound. This walks the content once and stops at the bound instead. It returns
    /// at most <paramref name="maxLines"/> + 1 lines, so a caller can detect the overflow by count and
    /// refuse without ever holding the whole file as lines.
    /// </remarks>
    public static IReadOnlyList<string> SplitLines(string content, int maxLines)
        => SplitLines(content, maxLines, maxLineLength: int.MaxValue, out _);

    /// <summary>
    /// As <see cref="SplitLines(string, int)"/>, and additionally reports through
    /// <paramref name="lineTooLong"/> whether any discovered line exceeded <paramref name="maxLineLength"/>.
    /// Discovery stops at the first such line, so a caller never pays to split its fields.
    /// </summary>
    /// <remarks>
    /// A line count alone does not bound a CSV. One line can carry the entire document, and splitting its
    /// fields then materializes a list entry and a string per delimiter - a single-line exhaustion path
    /// that a row bound never sees.
    ///
    /// <paramref name="maxLineLength"/> is a bound on the line's UTF-8 <em>byte</em> length, not its
    /// UTF-16 length. The two differ by up to 3x: a BMP character above U+07FF encodes to three bytes in
    /// one UTF-16 unit, so a CJK line measured by character count slips 3x past a byte cap. The character
    /// count is still used as a fast pre-filter, because UTF-8 bytes are never fewer than UTF-16 units -
    /// a line over the bound in characters is certainly over it in bytes. Only a line long enough to
    /// possibly breach the bound pays for an exact byte count.
    /// </remarks>
    public static IReadOnlyList<string> SplitLines(
        string content,
        int maxLines,
        int maxLineLength,
        out bool lineTooLong)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLines);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLineLength);
        lineTooLong = false;

        var start = content.Length > 0 && content[0] == ByteOrderMark ? 1 : 0;
        var lines = new List<string>();
        var index = start;
        var lineStart = start;

        while (index < content.Length)
        {
            var current = content[index];
            if (current is not ('\r' or '\n'))
            {
                index++;
                continue;
            }

            if (ExceedsByteBound(content.AsSpan(lineStart, index - lineStart), maxLineLength))
            {
                lineTooLong = true;
                return lines;
            }

            lines.Add(content[lineStart..index]);
            if (lines.Count > maxLines)
            {
                return lines;
            }

            // \r\n is one break, not two; a lone \r is a break in its own right.
            index += current == '\r' && index + 1 < content.Length && content[index + 1] == '\n' ? 2 : 1;
            lineStart = index;
        }

        if (ExceedsByteBound(content.AsSpan(lineStart), maxLineLength))
        {
            lineTooLong = true;
            return lines;
        }

        // Split always yields a final segment, including the empty one a terminating newline leaves.
        lines.Add(content[lineStart..]);
        return lines;
    }

    /// <summary>
    /// Whether a line exceeds a UTF-8 byte bound, counting bytes only when the character count leaves it
    /// possible. A UTF-16 unit encodes to at most three UTF-8 bytes, so anything at or under a third of
    /// the bound is certainly inside it, and anything over the bound in characters is certainly outside.
    /// </summary>
    private static bool ExceedsByteBound(ReadOnlySpan<char> line, int maxLineBytes)
    {
        if (line.Length > maxLineBytes)
        {
            return true;
        }

        return line.Length > maxLineBytes / 3 && Encoding.UTF8.GetByteCount(line) > maxLineBytes;
    }
}
