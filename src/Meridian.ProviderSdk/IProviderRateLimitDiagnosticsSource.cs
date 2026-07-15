namespace Meridian.ProviderSdk;

/// <summary>
/// Canonical surface labels used by provider rate-limit diagnostics.
/// </summary>
public static class ProviderRateLimitSurfaces
{
    public const string Historical = "historical";
    public const string DataSource = "data-source";
    public const string Streaming = "streaming";
}

/// <summary>
/// Immutable, single-sample rate-limit state exposed by a provider runtime.
/// </summary>
public sealed record ProviderRateLimitDiagnosticSnapshot(
    string ProviderId,
    string Surface,
    DateTimeOffset ObservedAt,
    int RequestsInWindow,
    int MaxRequestsPerWindow,
    TimeSpan Window,
    bool IsRateLimited,
    DateTimeOffset? ResetAt,
    double UsageRatio,
    string? Reason = null,
    bool StateAvailable = true)
{
    public int RemainingRequests => Math.Max(0, MaxRequestsPerWindow - RequestsInWindow);
}

/// <summary>
/// Implemented by provider runtimes that can expose a coherent rate-limit snapshot.
/// </summary>
public interface IProviderRateLimitDiagnosticsSource
{
    ProviderRateLimitDiagnosticSnapshot GetRateLimitDiagnosticsSnapshot();
}
