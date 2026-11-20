using Meridian.Contracts.Backfill;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;

namespace Meridian.Application.Backfill;

internal static class BackfillQuotaPlanner
{
    public static BackfillQuotaEstimate Estimate(
        BackfillRequest request,
        IHistoricalDataProvider? provider,
        BackfillQuotaPolicy policy,
        bool observeUsage = true)
    {
        var snapshot = (provider as IProviderRateLimitDiagnosticsSource)?.GetRateLimitDiagnosticsSnapshot();
        if (snapshot is not null && snapshot.Surface != ProviderRateLimitSurfaces.Historical)
            snapshot = null;

        var limit = snapshot?.MaxRequestsPerWindow;
        var window = snapshot?.Window;
        var observed = observeUsage && snapshot is { StateAvailable: true } ? snapshot : null;
        if (provider is null || limit is not (> 0 and < int.MaxValue) || window is not { Ticks: > 0 })
        {
            return new BackfillQuotaEstimate(null, null, null, null, null, null,
                policy.ReserveHeadroomPercent, policy.MaxConcurrentRequests, null, false,
                observed?.ObservedAt, observed is { IsRateLimited: true } ? observed.ResetAt : null, null,
                "The historical provider exposes no usable request limit for this plan.");
        }

        var symbolCount = BackfillSymbolNormalizer.Normalize(request.Symbols).Length;
        // An open-ended fetch is a single adapter window per symbol, as in execution.
        var expectedRequests = request.From is { } from && request.To is { } to && to >= from
            ? BackfillPartitionPlanner.Build(provider, request.Granularity, from, to.AddDays(1), symbolCount)
                .Sum(partition => partition.EstimatedApiCalls)
            : symbolCount;
        var backfillLimit = policy.GetBackfillLimit(limit.Value);
        var dailyLimit = snapshot?.DailyMaximum;
        var dailyBackfillLimit = dailyLimit is { } daily ? policy.GetBackfillLimit(daily) : (int?)null;
        var dailyRemaining = observed?.DailyUnitsUsed is { } used && dailyLimit is { } dailyMaximum
            ? Math.Max(0, dailyMaximum - used) : (int?)null;
        var dailyAvailable = observed?.DailyUnitsUsed is { } usedToday && dailyBackfillLimit is { } dailyCap
            ? Math.Max(0, dailyCap - usedToday) : (int?)null;
        var remaining = observed is null ? (int?)null : observed.IsRateLimited ? 0 : observed.RemainingRequests;
        var available = observed is null ? (int?)null
            : observed.IsRateLimited ? 0 : Math.Max(0, backfillLimit - observed.RequestsInWindow);
        if (available is { } windowAvailable && dailyAvailable is { } currentDayAvailable)
            available = Math.Min(windowAvailable, currentDayAvailable);
        var retryAt = observed is { IsRateLimited: true } ? observed.ResetAt : null;

        var firstWindow = observed is null ? backfillLimit
            : observed.IsRateLimited ? 0 : Math.Max(0, backfillLimit - observed.RequestsInWindow);
        var extraWindows = Math.Ceiling(Math.Max(0, expectedRequests - firstWindow) / (double)backfillLimit);
        var windowWait = extraWindows * window.Value.TotalSeconds;
        if (extraWindows > 0 && retryAt is { } retry && observed is { Reason: not "daily-quota" })
            windowWait += (retry - observed.ObservedAt).TotalSeconds - window.Value.TotalSeconds;

        var firstDay = dailyAvailable ?? dailyBackfillLimit;
        var dailyWait = 0d;
        if (firstDay is { } dayAvailable && dailyBackfillLimit is { } dayCapacity && expectedRequests > dayAvailable)
        {
            var extraDays = Math.Ceiling((expectedRequests - dayAvailable) / (double)dayCapacity);
            var daySeconds = TimeSpan.FromDays(1).TotalSeconds;
            var firstReset = observed?.DailyResetsAt is { } reset
                ? Math.Max(0, (reset - observed.ObservedAt).TotalSeconds)
                : daySeconds;
            dailyWait = firstReset + (extraDays - 1) * daySeconds;
        }

        var quotaUnit = snapshot?.QuotaUnit ?? "requests";
        var perRequestSeconds = Math.Max(provider.RateLimitDelay.TotalSeconds, 0.2);
        var estimatedDuration = Math.Max(dailyWait, Math.Max(0, windowWait)) + expectedRequests * perRequestSeconds;
        var uncertainty = observed is null
            ? "Current usage is unavailable. Duration assumes an unused first window."
            : "Remaining requests reflect this host's historical adapter. Other clients can consume the provider account's quota.";
        uncertainty += " Pagination, retries and queue time can change the result. Duration assumes at least 0.2 seconds per planned request plus quota waits.";
        if (request.ResumeFromCheckpoint)
            uncertainty += " Resuming from retained coverage can reduce the number of requests.";
        if (quotaUnit != "requests")
            uncertainty += $" Each planned single-symbol bars request is budgeted as one {quotaUnit.TrimEnd('s')}.";

        return new BackfillQuotaEstimate(expectedRequests, limit, window.Value.TotalSeconds, remaining,
            backfillLimit, available, policy.ReserveHeadroomPercent, policy.MaxConcurrentRequests,
            Math.Ceiling(estimatedDuration), expectedRequests > firstWindow || expectedRequests > firstDay,
            observed?.ObservedAt, retryAt, "historical rate-limit diagnostics", uncertainty,
            quotaUnit, dailyLimit, dailyRemaining, dailyBackfillLimit, observed?.DailyResetsAt);
    }
}
