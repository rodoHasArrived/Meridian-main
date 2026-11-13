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
    /// <summary>Legacy request-named counters contain this unit (requests or weighted credits).</summary>
    public string QuotaUnit { get; init; } = "requests";
    public int? DailyUnitsUsed { get; init; }
    public int? DailyMaximum { get; init; }
    public DateTimeOffset? DailyResetsAt { get; init; }
    public string? DailyResetPolicy { get; init; }

    public int RemainingRequests => DailyMaximum is { } daily && DailyUnitsUsed is { } used
        ? Math.Min(Math.Max(0, MaxRequestsPerWindow - RequestsInWindow), Math.Max(0, daily - used))
        : Math.Max(0, MaxRequestsPerWindow - RequestsInWindow);
}

/// <summary>
/// Implemented by provider runtimes that can expose a coherent rate-limit snapshot.
/// </summary>
public interface IProviderRateLimitDiagnosticsSource
{
    ProviderRateLimitDiagnosticSnapshot GetRateLimitDiagnosticsSnapshot();
}
