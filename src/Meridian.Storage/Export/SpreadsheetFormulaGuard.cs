using System.Globalization;

namespace Meridian.Storage.Export;

/// <summary>
/// Formats untrusted text for spreadsheet-delimited exports without allowing a value to be
/// interpreted as a formula. Plain negative numbers remain numeric because signed amounts are
/// pervasive in financial data.
/// </summary>
public static class SpreadsheetFormulaGuard
{
    /// <summary>Prefixes formula-looking text with an apostrophe so spreadsheet tools treat it as text.</summary>
    public static string Neutralize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            return value;
        }

        var firstNonSpace = 0;
        while (firstNonSpace < value.Length && value[firstNonSpace] == ' ')
        {
            firstNonSpace++;
        }
        if (firstNonSpace >= value.Length)
        {
            return value;
        }

        var leading = value[firstNonSpace];
        var isSafeNegativeNumber = leading == '-'
            && decimal.TryParse(
                value.AsSpan(firstNonSpace),
                NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out _);
        return leading is '=' or '+' or '@' or '\t' or '\r' or '\n'
            || leading == '-' && !isSafeNegativeNumber
                ? $"'{value}"
                : value;
    }

    /// <summary>
    /// Escapes one CSV cell and also protects semicolon-delimited spreadsheet locales. Each
    /// semicolon-separated segment is neutralized independently, then semicolons and tab
    /// characters force CSV quoting so they cannot be promoted to a new cell by import heuristics.
    /// </summary>
    public static string EscapeCsvCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var neutralized = string.Join(
            ";",
            value.Split(';').Select(Neutralize));
        return neutralized.IndexOfAny([',', ';', '"', '\t', '\r', '\n']) >= 0
            ? $"\"{neutralized.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : neutralized;
    }
}
