using System.Globalization;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// Culture- and format-aware value parsing shared by the statement connectors. Profiles
/// supply date-format and culture hints; parsing falls back progressively so common
/// real-world statements (US/EU decimals, parenthesized negatives, timestamped dates)
/// import without operator intervention.
/// </summary>
internal static class StatementValueParser
{
    private const NumberStyles DecimalStyles =
        NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowParentheses;

    public static bool TryParseDecimal(string? value, StatementMappingProfileDocument profile, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var culture = ResolveCulture(profile);
        return decimal.TryParse(trimmed, DecimalStyles, culture, out result)
            || decimal.TryParse(trimmed, DecimalStyles, CultureInfo.InvariantCulture, out result);
    }

    public static bool TryParseDate(string? value, StatementMappingProfileDocument profile, out DateOnly result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        foreach (var format in profile.DateFormats ?? [])
        {
            if (DateOnly.TryParseExact(trimmed, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            {
                return true;
            }
        }

        var culture = ResolveCulture(profile);
        if (DateOnly.TryParse(trimmed, culture, DateTimeStyles.None, out result)
            || DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
        {
            return true;
        }

        // Timestamped exports (e.g. "2026-06-01T14:30:00Z" or "20260601;093000") reduce to
        // their date component rather than failing the row.
        var datePortion = trimmed.Split(';', 'T', ' ')[0];
        if (datePortion.Length != trimmed.Length)
        {
            foreach (var format in profile.DateFormats ?? [])
            {
                if (DateOnly.TryParseExact(datePortion, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                {
                    return true;
                }
            }
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            result = DateOnly.FromDateTime(timestamp.UtcDateTime);
            return true;
        }

        return false;
    }

    private static CultureInfo ResolveCulture(StatementMappingProfileDocument profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Culture))
        {
            return CultureInfo.InvariantCulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(profile.Culture);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
