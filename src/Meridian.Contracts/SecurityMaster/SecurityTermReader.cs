using System.Globalization;
using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Shared low-level reader for probing economic-term JSON by case-insensitive key aliases.
/// Centralizes the value-coercion rules (string/number/bool → string, number/string → decimal,
/// string → date) so cash-flow and obligation projections resolve terms identically instead of each
/// carrying its own copy of the probing helpers. A whitespace or unparseable value never short-circuits
/// the search: the reader keeps trying the remaining aliases and sources until one resolves.
/// </summary>
public static class SecurityTermReader
{
    /// <summary>Case-insensitive property lookup within a single JSON object element.</summary>
    public static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    /// <summary>Reads the first alias resolvable to a non-empty string (or number/bool) in <paramref name="source"/>.</summary>
    public static string? ReadString(JsonElement source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(source, propertyName, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            else if (property.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return property.ToString();
            }
        }

        return null;
    }

    /// <summary>Reads the first alias resolvable to a decimal in <paramref name="source"/>.</summary>
    public static decimal? ReadDecimal(JsonElement source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(source, propertyName, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String &&
                decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    /// <summary>Reads the first alias resolvable to a date (date-only or timestamp) in <paramref name="source"/>.</summary>
    public static DateOnly? ReadDate(JsonElement source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(source, propertyName, out var property) ||
                property.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var raw = property.GetString();
            if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }

            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            {
                return DateOnly.FromDateTime(timestamp.UtcDateTime.Date);
            }
        }

        return null;
    }

    /// <summary>Reads the first resolvable string across <paramref name="sources"/> in priority order.</summary>
    public static string? ReadString(IEnumerable<JsonElement> sources, params string[] propertyNames)
    {
        foreach (var source in sources)
        {
            if (ReadString(source, propertyNames) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Reads the first resolvable decimal across <paramref name="sources"/> in priority order.</summary>
    public static decimal? ReadDecimal(IEnumerable<JsonElement> sources, params string[] propertyNames)
    {
        foreach (var source in sources)
        {
            if (ReadDecimal(source, propertyNames) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Reads the first resolvable date across <paramref name="sources"/> in priority order.</summary>
    public static DateOnly? ReadDate(IEnumerable<JsonElement> sources, params string[] propertyNames)
    {
        foreach (var source in sources)
        {
            if (ReadDate(source, propertyNames) is { } value)
            {
                return value;
            }
        }

        return null;
    }
}
