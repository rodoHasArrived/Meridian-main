namespace Meridian.DataIntegration.Monitoring;

internal static class ProviderMonitoringIdentity
{
    public static bool TryNormalize(string? providerName, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = providerName.Trim().ToLowerInvariant();
        return true;
    }

    public static bool Equals(string? left, string? right)
        => TryNormalize(left, out var normalizedLeft) &&
            TryNormalize(right, out var normalizedRight) &&
            string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
}
