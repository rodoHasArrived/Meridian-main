using System.Text.Json;

namespace Meridian.Infrastructure.Utilities;

/// <summary>
/// Extension methods for <see cref="JsonElement"/> to simplify property extraction.
/// Reduces boilerplate TryGetProperty patterns across provider implementations.
///
/// Example usage:
/// <code>
/// // Before
/// var sym = elem.TryGetProperty("S", out var sProp) ? sProp.GetString() : null;
/// var price = elem.TryGetProperty("p", out var pProp) ? pProp.GetDecimal() : 0m;
///
/// // After
/// var sym = elem.GetStringOrNull("S");
/// var price = elem.GetDecimalOrDefault("p");
/// </code>
/// </summary>
public static class JsonElementExtensions
{
    /// <summary>
    /// Gets a string property value or null if not present.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <param name="propertyName">The property name to look up.</param>
    /// <returns>The string value or null.</returns>
    public static string? GetStringOrNull(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() : null;
    }

    /// <summary>
    /// Gets a string property value or empty string if not present.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <param name="propertyName">The property name to look up.</param>
    /// <returns>The string value or empty string.</returns>
    public static string GetStringOrEmpty(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// Gets a decimal property value or default if not present.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <param name="propertyName">The property name to look up.</param>
    /// <param name="defaultValue">The default value if property not found.</param>
    /// <returns>The decimal value or default.</returns>
    public static decimal GetDecimalOrDefault(this JsonElement element, string propertyName, decimal defaultValue = 0m)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return defaultValue;

        // Handle both number and string representations
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(prop.GetString(), out var d) => d,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Gets a double property value or default if not present.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <param name="propertyName">The property name to look up.</param>
    /// <param name="defaultValue">The default value if property not found.</param>
    /// <returns>The double value or default.</returns>
    public static double GetDoubleOrDefault(this JsonElement element, string propertyName, double defaultValue = 0.0)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return defaultValue;

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetDouble(),
            JsonValueKind.String when double.TryParse(prop.GetString(), out var d) => d,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Gets an int property value or default if not present.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <param name="propertyName">The property name to look up.</param>
    /// <param name="defaultValue">The default value if property not found.</param>
    /// <returns>The int value or default.</returns>
    public static int GetInt32OrDefault(this JsonElement element, string propertyName, int defaultValue = 0)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return defaultValue;

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetInt32(),
            JsonValueKind.String when int.TryParse(prop.GetString(), out var i) => i,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Gets a long property value or default if not present.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <param name="propertyName">The property name to look up.</param>
    /// <param name="defaultValue">The default value if property not found.</param>
    /// <returns>The long value or default.</returns>
    public static long GetInt64OrDefault(this JsonElement element, string propertyName, long defaultValue = 0L)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return defaultValue;

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetInt64(),
            JsonValueKind.String when long.TryParse(prop.GetString(), out var l) => l,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Gets a boolean property value or default if not present.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <param name="propertyName">The property name to look up.</param>
    /// <param name="defaultValue">The default value if property not found.</param>
    /// <returns>The boolean value or default.</returns>
    public static bool GetBoolOrDefault(this JsonElement element, string propertyName, bool defaultValue = false)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return defaultValue;

        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(prop.GetString(), out var b) => b,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Gets a DateTimeOffset from a Unix milliseconds timestamp property.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <param name="propertyName">The property name to look up.</param>
    /// <param name="defaultValue">The default value if property not found or invalid.</param>
    /// <returns>The DateTimeOffset value or default.</returns>
    public static DateTimeOffset GetDateTimeOffsetFromUnixMs(
        this JsonElement element,
        string propertyName,
        DateTimeOffset? defaultValue = null)
    {
        var fallback = defaultValue ?? DateTimeOffset.UtcNow;

        if (!element.TryGetProperty(propertyName, out var prop))
            return fallback;

        var timestamp = prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetInt64(),
            JsonValueKind.String when long.TryParse(prop.GetString(), out var l) => l,
            _ => 0L
        };

        return timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp) : fallback;
    }

    /// <summary>
    /// Gets a DateTimeOffset from a Unix seconds timestamp property.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <param name="propertyName">The property name to look up.</param>
    /// <param name="defaultValue">The default value if property not found or invalid.</param>
    /// <returns>The DateTimeOffset value or default.</returns>
    public static DateTimeOffset GetDateTimeOffsetFromUnixSeconds(
        this JsonElement element,
        string propertyName,
        DateTimeOffset? defaultValue = null)
    {
        var fallback = defaultValue ?? DateTimeOffset.UtcNow;

        if (!element.TryGetProperty(propertyName, out var prop))
            return fallback;

        var timestamp = prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetInt64(),
            JsonValueKind.String when long.TryParse(prop.GetString(), out var l) => l,
            _ => 0L
        };

        return timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(timestamp) : fallback;
    }

    /// <summary>
    /// Checks if a property exists and has a non-null value.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>True if the property exists and is not null.</returns>
    public static bool HasProperty(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null;
    }

    /// <summary>
    /// Gets an array property as an enumerable of JsonElements.
    /// Returns empty enumerable if property not found or not an array.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <param name="propertyName">The property name to look up.</param>
    /// <returns>Enumerable of JsonElements or empty.</returns>
    public static IEnumerable<JsonElement> GetArrayOrEmpty(this JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Array)
        {
            return prop.EnumerateArray();
        }
        return Enumerable.Empty<JsonElement>();
    }

    /// <summary>
    /// Gets an array of integers from a property.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <param name="propertyName">The property name to look up.</param>
    /// <returns>Array of integers or empty array.</returns>
    public static int[] GetInt32ArrayOrEmpty(this JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Array)
        {
            return prop.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Number)
                .Select(e => e.GetInt32())
                .ToArray();
        }
        return Array.Empty<int>();
    }
}
