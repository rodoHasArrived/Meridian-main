using System.Globalization;

namespace Meridian.Ledger;

/// <summary>
/// Formats one text cell for the ledger CSV artifacts. Text that a spreadsheet would evaluate as a
/// formula (leading <c>=</c>, <c>+</c>, <c>@</c>, tab, or a non-numeric leading <c>-</c>) is prefixed
/// with an apostrophe so it opens as text; plain negative numbers stay numeric. Values that do not
/// look like formulas are emitted exactly as before, so existing artifact checksums are unchanged.
/// </summary>
internal static class LedgerCsvCell
{
    internal static string Escape(string value)
    {
        value = Neutralize(value);
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string Neutralize(string value)
    {
        var firstNonSpace = 0;
        while (firstNonSpace < value.Length && value[firstNonSpace] == ' ')
            firstNonSpace++;

        if (firstNonSpace >= value.Length)
            return value;

        var leading = value[firstNonSpace];
        var isFormulaLead = leading is '=' or '+' or '@' or '\t' or '\r' or '\n'
            || leading == '-' && !decimal.TryParse(
                value.AsSpan(firstNonSpace),
                NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out _);

        return isFormulaLead ? $"'{value}" : value;
    }
}
