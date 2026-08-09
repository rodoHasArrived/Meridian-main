using System.Globalization;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Neutralizes values that a spreadsheet application would interpret as formulas so exported CSV
/// cells can never execute when a recipient opens a delivered report. Plain negative numbers are
/// deliberately left untouched because signed amounts are pervasive in financial exports.
/// </summary>
internal static class SpreadsheetFormulaGuard
{
    internal static string Neutralize(string value)
    {
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
}
