namespace Meridian.Contracts.Backfill;

/// <summary>Estimate based on the historical adapter's quota and local observations.</summary>
public sealed record BackfillQuotaEstimate(
    long? ExpectedRequests,
    int? RequestsPerWindow,
    double? WindowSeconds,
    int? ObservedRemainingRequests,
    DateTimeOffset? ObservedAt,
    DateTimeOffset? ResetAt,
    int? BackfillRequestsPerWindow,
    int? BackfillRemainingRequests,
    int ReservedHeadroomPercent,
    int? MaxConcurrentRequests,
    double? EstimatedDurationSeconds,
    bool ExceedsBudget,
    string Uncertainty)
{
    public string QuotaUnit { get; init; } = "requests";
    public int? DailyLimit { get; init; }
    public int? DailyBackfillAllowance { get; init; }
    public int? ObservedDailyRemaining { get; init; }
    public DateTimeOffset? DailyResetAt { get; init; }
}

public sealed record BackfillThrottleEvent(string Provider, string Reason, DateTimeOffset ObservedAt, double? WaitSeconds);

/// <summary>Observed adapter activity and variance against the estimate retained at submission.</summary>
public sealed record BackfillQuotaReceipt(
    BackfillQuotaEstimate Estimate,
    IReadOnlyList<string> ObservedProviders,
    long? ActualRequests,
    double ActualDurationSeconds,
    long? RequestVariance,
    double? DurationVarianceSeconds,
    int ThrottlingEventCount,
    IReadOnlyList<BackfillThrottleEvent> ThrottlingEvents,
    IReadOnlyList<string>? ProvidersThatReturnedBars = null);

public sealed record BackfillSubmissionReceipt(
    string ExecutionId,
    string Status,
    string Provider,
    DateTimeOffset ScheduledAt,
    BackfillQuotaEstimate Quota);
