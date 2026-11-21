namespace Meridian.Core.Config;

/// <summary>Provider-specific, non-secret validation tolerances. Zero latency disables that check.</summary>
public sealed record ProviderValidationProfile(
    int StaleToleranceDays = 10,
    int FutureSkewSeconds = 300,
    int? StaleThresholdSeconds = null,
    double MaxLatencyMs = 0)
{
    public static ProviderValidationProfile Default { get; } = new();

    public void Validate()
    {
        if (StaleToleranceDays is < 0 or > 365 || FutureSkewSeconds is < 0 or > 86400 ||
            StaleThresholdSeconds is < 0 or > 31536000 || !double.IsFinite(MaxLatencyMs) ||
            MaxLatencyMs is < 0 or > 86400000)
            throw new ArgumentException("Validation tolerances are outside their supported bounds.");
    }

    public bool Loosens(ProviderValidationProfile previous) =>
        StaleToleranceDays > previous.StaleToleranceDays || FutureSkewSeconds > previous.FutureSkewSeconds ||
        (previous.StaleThresholdSeconds.HasValue &&
            (!StaleThresholdSeconds.HasValue || StaleThresholdSeconds > previous.StaleThresholdSeconds)) ||
        (previous.MaxLatencyMs > 0 && (MaxLatencyMs == 0 || MaxLatencyMs > previous.MaxLatencyMs));

    public static ProviderValidationProfile Resolve(BackfillConfig? config, string providerName)
        => config?.ValidationProfiles?.FirstOrDefault(pair =>
            string.Equals(pair.Key, providerName, StringComparison.OrdinalIgnoreCase)).Value ?? Default;
}

/// <summary>Before/after evidence saved atomically with each profile change.</summary>
public sealed record ProviderValidationProfileAudit(
    Guid Id,
    string Provider,
    DateTimeOffset ChangedAt,
    string Actor,
    string? Reason,
    bool Loosened,
    ProviderValidationProfile Before,
    ProviderValidationProfile After);
