namespace Meridian.Execution.Sdk;

/// <summary>
/// Internal metadata carrying gateway-resolved order-sizing semantics into pre-trade risk. The OMS
/// removes caller-supplied values and stamps the key only after consulting the active gateway.
/// </summary>
public static class OrderSizingMetadata
{
    /// <summary>Internal marker for face-value quantity priced as a percentage of par.</summary>
    public const string FaceValuePercentageOfParKey =
        "meridian:internal:face-value-percentage-of-par";

    /// <summary>Returns true only when the server-owned sizing marker is present and true.</summary>
    public static bool UsesFaceValuePercentageOfPar(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return false;
        }

        foreach (var (key, value) in metadata)
        {
            if (string.Equals(key, FaceValuePercentageOfParKey, StringComparison.OrdinalIgnoreCase)
                && bool.TryParse(value, out var enabled))
            {
                return enabled;
            }
        }

        return false;
    }

    /// <summary>Copies metadata and stamps the server-owned face-value sizing marker.</summary>
    public static IReadOnlyDictionary<string, string> WithFaceValuePercentageOfPar(
        IReadOnlyDictionary<string, string>? metadata)
    {
        // Preserve the request's existing key comparer/duplicates. Broker metadata readers have
        // their own ordered alias rules, and stamping one internal key must not rewrite them.
        var stamped = metadata is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(metadata);
        stamped[FaceValuePercentageOfParKey] = bool.TrueString;
        return stamped;
    }
}
