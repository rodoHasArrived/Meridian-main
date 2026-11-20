namespace Meridian.Contracts.Backfill;

/// <summary>Historical provider quota and the shared policy observed when a plan was estimated.</summary>
public sealed record BackfillQuotaEstimate(
    long? ExpectedRequests,
    int? ProviderLimit,
    double? WindowSeconds,
    int? ObservedRemainingRequests,
    int? BackfillRequestsPerWindow,
    int? AvailableBackfillRequests,
    int ReserveHeadroomPercent,
    int? MaxConcurrentRequests,
    double? EstimatedDurationSeconds,
    bool ExceedsBudget,
    DateTimeOffset? ObservedAt,
    DateTimeOffset? RetryAt,
    string? LimitSource,
    string Uncertainty,
    string QuotaUnit = "requests",
    int? DailyProviderLimit = null,
    int? DailyRemaining = null,
    int? DailyBackfillLimit = null,
    DateTimeOffset? DailyResetAt = null);

public sealed record BackfillEquivalentProvider(string Provider, string DisplayName);

public sealed record BackfillProviderUsage(
    string Provider,
    long FetchAttempts,
    long? ObservedRequests);

public sealed record BackfillThrottleEvent(string Provider, string Reason, int Count, double WaitSeconds);

/// <summary>Evidence from one run. Null counts indicate incomplete request instrumentation.</summary>
public sealed record BackfillQuotaReceipt(
    BackfillQuotaEstimate? Estimate,
    IReadOnlyList<string> ObservedProviders,
    IReadOnlyList<BackfillProviderUsage> ProviderUsage,
    IReadOnlyList<BackfillThrottleEvent> ThrottlingEvents,
    long? ActualRequests,
    double ActualDurationSeconds,
    long? RequestVariance,
    double? DurationVarianceSeconds);

public sealed record QueueBackfillRequest(Guid PreviewId, bool AcceptThrottle = false, DateTimeOffset? NotBeforeUtc = null);

public sealed record BackfillJobReceipt(
    string ExecutionId,
    string Status,
    string Provider,
    string[] Symbols,
    DateOnly? From,
    DateOnly? To,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? NotBeforeUtc,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    long? BarsWritten,
    string? Error,
    BackfillQuotaEstimate Estimate,
    BackfillQuotaReceipt? Actual);
