using System.Globalization;
using System.Text.Json;

namespace Meridian.DataIntegration.AccountingSystem;

internal static class AccountingProviderJson
{
    public static string Text(JsonElement value, string name)
        => value.TryGetProperty(name, out var field) && field.ValueKind != JsonValueKind.Null
            ? field.ToString() : string.Empty;

    public static string RequiredText(JsonElement value, string name)
        => !string.IsNullOrWhiteSpace(Text(value, name)) ? Text(value, name)
            : throw new InvalidOperationException("Provider response is missing a required field.");

    public static JsonElement[] Rows(JsonElement value, string name)
        => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Array
            ? field.EnumerateArray().ToArray()
            : throw new InvalidOperationException("Provider response is missing a required collection.");

    public static decimal Amount(string value, bool blankIsZero = false)
    {
        if (blankIsZero && string.IsNullOrWhiteSpace(value))
            return 0m;
        return decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var amount)
            ? amount : throw new InvalidOperationException("Provider response contains an invalid amount.");
    }

    public static DateOnly Date(string value)
    {
        if (value.StartsWith("/Date(", StringComparison.Ordinal) && value.EndsWith(")/", StringComparison.Ordinal))
        {
            var milliseconds = value[6..^2];
            var zone = milliseconds.IndexOfAny(['+', '-'], 1);
            if (zone >= 0)
                milliseconds = milliseconds[..zone];
            if (long.TryParse(milliseconds, CultureInfo.InvariantCulture, out var epoch))
                return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime);
        }
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        throw new InvalidOperationException("Provider response contains an invalid accounting date.");
    }
}
