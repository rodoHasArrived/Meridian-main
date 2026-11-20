using Meridian.Contracts.Backfill;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;
using Meridian.Storage.Backfill;

namespace Meridian.Application.Backfill;

internal static class BackfillQuotaPlanner
{
    private static readonly DateOnly EarliestDate = new(1970, 1, 1);

    public static BackfillRequest ResolveDates(BackfillRequest request, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var to = request.To ?? today;
        if (to < EarliestDate || to == DateOnly.MaxValue)
            throw new InvalidOperationException("The effective end date is outside the supported backfill range.");

        var defaultStart = request.Granularity.IsIntraday() ? to.AddDays(-30) : to.AddYears(-1);
        var from = request.From ?? (defaultStart < EarliestDate ? EarliestDate : defaultStart);
        if (from > to)
            throw new InvalidOperationException("The effective start date must be on or before the end date.");
        if (request.Granularity.IsIntraday() && to.DayNumber - from.DayNumber >= 31)
            throw new InvalidOperationException("Intraday backfill date range cannot exceed 31 days.");

        return request with { From = from, To = to, Symbols = BackfillSymbolNormalizer.Normalize(request.Symbols) };
    }

    public static BackfillOperationalBudgetConfig GetPolicy(BackfillConfig? config, string provider)
    {
        var policy = config?.OperationalBudgets?
            .FirstOrDefault(entry => string.Equals(entry.Key, provider, StringComparison.OrdinalIgnoreCase)).Value
            ?? new BackfillOperationalBudgetConfig();
        if (policy.ReserveHeadroomPercent is < 0 or > 99 || policy.MaxConcurrentRequests is <= 0)
            throw new InvalidOperationException($"The provider administrator must correct the operational budget for '{provider}'.");

        return policy;
    }

    public static IHistoricalDataProvider[] GetApprovedAlternatives(
        BackfillConfig? config, string provider, DataGranularity granularity, IReadOnlyList<IHistoricalDataProvider> providers)
    {
        var approved = GetPolicy(config, provider).ApprovedEquivalentProviders ?? [];
        var original = providers.FirstOrDefault(candidate => candidate.Name.Equals(provider, StringComparison.OrdinalIgnoreCase));
        return providers.Where(candidate => candidate is not CompositeHistoricalDataProvider &&
            candidate.IsSimulated == (original?.IsSimulated ?? false) &&
            !candidate.Name.Equals(provider, StringComparison.OrdinalIgnoreCase) &&
            approved.Contains(candidate.Name, StringComparer.OrdinalIgnoreCase) &&
            (!granularity.IsIntraday() || candidate is IHistoricalAggregateBarProvider aggregate &&
                aggregate.SupportedGranularities.Contains(granularity))).ToArray();
    }

    public static BackfillQuotaEstimate Estimate(
        BackfillRequest request, IHistoricalDataProvider provider, BackfillConfig? config,
        BackfillStatusStore statusStore, DateTimeOffset now)
    {
        var policy = GetPolicy(config, provider.Name);
        var limit = ReadLimit(provider);
        if (limit is null)
        {
            return new BackfillQuotaEstimate(null, null, null, null, null, null, null, null,
                policy.ReserveHeadroomPercent, policy.MaxConcurrentRequests, null, false,
                "The historical adapter has no usable provider quota for this selection.");
        }

        var calls = CountRequests(ResolveDates(request, now), provider, statusStore);
        var (maximum, window, sample) = limit.Value;
        var budget = ApplyHeadroom(maximum, policy.ReserveHeadroomPercent);
        var dailyLimit = sample.DailyMaximum is > 0 and < int.MaxValue ? sample.DailyMaximum : null;
        var dailyBudget = dailyLimit is { } daily ? ApplyHeadroom(daily, policy.ReserveHeadroomPercent) : (int?)null;
        // The snapshot includes capabilities sharing the adapter's credential budget. Clients
        // outside the host can still lower the real balance.
        var observed = sample is { StateAvailable: true, RequestsInWindow: >= 0 } &&
            sample.ObservedAt <= now.AddSeconds(1) && now - sample.ObservedAt < window ? sample : null;
        var blocked = observed is { IsRateLimited: true };
        var remaining = observed is null ? (int?)null : blocked ? 0 : observed.RemainingRequests;
        var usable = observed is null ? (int?)null : blocked ? 0 : Math.Max(0, budget - observed.RequestsInWindow);
        var dailyUsed = observed?.DailyUnitsUsed;
        var dailyRemaining = dailyUsed.HasValue && dailyLimit.HasValue ? Math.Max(0, dailyLimit.Value - dailyUsed.Value) : (int?)null;
        var dailyUsable = dailyUsed.HasValue && dailyBudget.HasValue ? Math.Max(0, dailyBudget.Value - dailyUsed.Value) : (int?)null;
        var available = Math.Min(usable ?? budget, dailyUsable ?? dailyBudget ?? int.MaxValue);
        var windowWait = EstimateWait(calls, budget, usable, window, observed?.ResetAt, now);
        var dailyWait = dailyBudget is { } dailyAllowance
            ? EstimateWait(calls, dailyAllowance, dailyUsable, TimeSpan.FromDays(1), observed?.DailyResetsAt, now) : 0;
        var duration = Math.Ceiling(Math.Max(Math.Max(windowWait, dailyWait), Math.Max(0, calls - 1) * provider.RateLimitDelay.TotalSeconds) + calls);

        return new BackfillQuotaEstimate(calls, maximum, window.TotalSeconds, remaining, observed?.ObservedAt,
            observed?.ResetAt, budget, usable.HasValue ? available : null, policy.ReserveHeadroomPercent, policy.MaxConcurrentRequests,
            duration, calls > available,
            "One request per symbol and date window before pagination or retries. Timing assumes about one second per request and excludes queue time. " +
            (request.ResumeFromCheckpoint ? "Retained coverage is considered and may change before execution. " : "") +
            (sample.QuotaUnit == "credits" ? "The historical adapter uses one credit per request. " : "") +
            (observed is null ? "Current usage is unavailable." : "The remaining budget reflects the host's observations. Other clients may use the same quota."))
        {
            QuotaUnit = sample.QuotaUnit,
            DailyLimit = dailyLimit,
            DailyBackfillAllowance = dailyBudget,
            ObservedDailyRemaining = dailyRemaining,
            DailyResetAt = observed?.DailyResetsAt
        };
    }

    private static long CountRequests(BackfillRequest request, IHistoricalDataProvider provider, BackfillStatusStore statusStore)
    {
        var from = request.From!.Value;
        var to = request.To!.Value;
        var coverage = request.ResumeFromCheckpoint
            ? statusStore.TryReadSymbolCoverage(request.Granularity, provider.Name, provider.CoverageFeed)
            : null;
        if (coverage is not { Count: > 0 })
        {
            return BackfillPartitionPlanner.Build(provider, request.Granularity, from, to.AddDays(1), request.Symbols.Count)
                .Sum(partition => partition.EstimatedApiCalls);
        }

        long calls = 0;
        foreach (var symbol in request.Symbols)
        {
            var missing = BackfillCoverageMath.Missing(from, to, coverage.GetValueOrDefault(symbol) ?? []);
            foreach (var (missingFrom, missingTo) in missing)
            {
                calls += BackfillPartitionPlanner.Build(provider, request.Granularity, missingFrom!.Value,
                    missingTo!.Value.AddDays(1), 1).Count;
            }
        }
        return calls;
    }

    private static double EstimateWait(long calls, int budget, int? usable, TimeSpan window, DateTimeOffset? reset, DateTimeOffset now)
    {
        var overflow = Math.Max(0, calls - (usable ?? budget));
        var waitWindows = Math.Ceiling((double)overflow / budget);
        var firstWait = reset is { } resetAt && resetAt > now ? (resetAt - now).TotalSeconds : window.TotalSeconds;
        return waitWindows > 0 ? firstWait + (waitWindows - 1) * window.TotalSeconds : 0;
    }

    internal static int ApplyHeadroom(int limit, int percentage) => Math.Max(1, (int)((long)limit * (100 - percentage) / 100));

    internal static (int Maximum, TimeSpan Window, ProviderRateLimitDiagnosticSnapshot Sample)? ReadLimit(IHistoricalDataProvider provider)
    {
        var sample = (provider as IProviderRateLimitDiagnosticsSource)?.GetRateLimitDiagnosticsSnapshot();
        return sample is { Surface: ProviderRateLimitSurfaces.Historical, QuotaKnown: true, MaxRequestsPerWindow: > 0 and < int.MaxValue } &&
            sample.Window > TimeSpan.Zero && sample.ProviderId.Equals(provider.Name, StringComparison.OrdinalIgnoreCase) &&
            (sample.QuotaUnit == "requests" || (sample.QuotaUnit == "credits" && provider is BaseHistoricalDataProvider))
            ? (sample.MaxRequestsPerWindow, sample.Window, sample) : null;
    }
}
