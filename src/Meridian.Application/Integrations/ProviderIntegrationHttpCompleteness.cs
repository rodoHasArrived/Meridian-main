using System.Globalization;

namespace Meridian.Application.Integrations;

internal static class ProviderIntegrationHttpCompleteness
{
    internal const string MetadataKey = "incompleteResponse";

    internal static bool IsIncomplete(int statusCode, IReadOnlyDictionary<string, string> headers)
        => statusCode == 206 || headers.Keys.Any(key =>
            string.Equals(key, "Content-Range", StringComparison.OrdinalIgnoreCase));

    internal static bool IsRetainedIncomplete(IReadOnlyDictionary<string, string> metadata)
        => metadata.Any(entry =>
            (string.Equals(entry.Key, "statusCode", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var status) && status == 206)
            || (string.Equals(entry.Key, MetadataKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.Value, "true", StringComparison.OrdinalIgnoreCase))
            || string.Equals(entry.Key, "Content-Range", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Key, "contentRange", StringComparison.OrdinalIgnoreCase));
}
