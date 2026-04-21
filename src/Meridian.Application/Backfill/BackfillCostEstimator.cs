using Meridian.Application.Logging;
using Meridian.Infrastructure.Adapters.Core;
using Serilog;

namespace Meridian.Application.Backfill;

/// <summary>
/// Estimates the cost (API calls, wall-clock time, quota impact) of a backfill
/// request before execution. Surfaces via the <c>/api/backfill/cost-estimate</c>
/// endpoint and the <c>--backfill --dry-run</c> CLI flag.
/// </summary>
public sealed class BackfillCostEstimator
{
    private readonly IReadOnlyDictionary<string, IHistoricalDataProvider> _providers;
    private readonly ILogger _log;

    public BackfillCostEstimator(
        IEnumerable<IHistoricalDataProvider> providers,
        ILogger? logger = null)
    {
        _providers = providers.ToDictionary(p => p.Name.ToLowerInvariant());
        _log = logger ?? LoggingSetup.ForContext<BackfillCostEstimator>();
    }

    /// <summary>
    /// Estimates the cost of a backfill request.
    /// </summary>
    public BackfillCostEstimate Estimate(BackfillCostRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var symbols = request.Symbols?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
                      ?? Array.Empty<string>();
        var granularity = request.Granularity;

        if (symbols.Length == 0)
            return BackfillCostEstimate.Empty("No symbols specified.");

        var from = request.From ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));
        var to = request.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var tradingDays = EstimateTradingDays(from, to);

        var providerEstimates = new List<ProviderCostEstimate>();

        // If a specific provider is requested, estimate for that one
        if (!string.IsNullOrWhiteSpace(request.Provider))
        {
            if (_providers.TryGetValue(request.Provider.ToLowerInvariant(), out var provider))
            {
                if (!SupportsGranularity(provider, granularity))
                {
                    return CreateEstimateWithoutProviders(
                        symbols,
                        from,
                        to,
                        tradingDays,
                        granularity,
                        $"Provider '{provider.DisplayName}' does not support {granularity.ToDisplayName()} backfill.");
                }

                providerEstimates.Add(EstimateForProvider(provider, symbols, granularity, tradingDays, from, to));
            }
            else
            {
                return CreateEstimateWithoutProviders(
                    symbols,
                    from,
                    to,
                    tradingDays,
                    granularity,
                    $"Unknown provider: {request.Provider}");
            }
        }
        else
        {
            // Estimate for all available providers, sorted by priority
            foreach (var provider in _providers.Values.OrderBy(p => p.Priority))
            {
                if (SupportsGranularity(provider, granularity))
                    providerEstimates.Add(EstimateForProvider(provider, symbols, granularity, tradingDays, from, to));
            }
        }

        if (providerEstimates.Count == 0)
        {
            return CreateEstimateWithoutProviders(
                symbols,
                from,
                to,
                tradingDays,
                granularity,
                $"No historical providers support {granularity.ToDisplayName()} backfill.");
        }

        var recommended = providerEstimates
            .Where(e => !e.WouldExceedFreeQuota)
            .OrderBy(e => e.EstimatedWallClockTime)
            .ThenBy(e => e.EstimatedApiCalls)
            .FirstOrDefault() ?? providerEstimates.FirstOrDefault();

        return new BackfillCostEstimate(
            Symbols: symbols,
            From: from,
            To: to,
            TradingDays: tradingDays,
            ProviderEstimates: providerEstimates,
            RecommendedProvider: recommended?.ProviderName,
            Warnings: GenerateWarnings(providerEstimates, symbols, granularity, tradingDays),
            EstimatedAt: DateTimeOffset.UtcNow);
    }

    private ProviderCostEstimate EstimateForProvider(
        IHistoricalDataProvider provider,
        string[] symbols,
        DataGranularity granularity,
        int tradingDays,
        DateOnly from,
        DateOnly to)
    {
        var callsPerSymbol = EstimateRequestsPerSymbol(provider, granularity, tradingDays);
        var totalCalls = symbols.Length * callsPerSymbol;

        var rateLimitDelay = provider.RateLimitDelay;
        var maxRequestsPerWindow = provider.MaxRequestsPerWindow;
        var rateLimitWindow = provider.RateLimitWindow;

        // Estimate wall-clock time
        TimeSpan estimatedTime;
        if (rateLimitDelay > TimeSpan.Zero)
        {
            estimatedTime = TimeSpan.FromTicks(rateLimitDelay.Ticks * totalCalls);
        }
        else if (maxRequestsPerWindow < int.MaxValue && maxRequestsPerWindow > 0)
        {
            var windowsNeeded = (double)totalCalls / maxRequestsPerWindow;
            estimatedTime = TimeSpan.FromTicks((long)(windowsNeeded * rateLimitWindow.Ticks));
        }
        else
        {
            // No rate limit info — estimate 200ms per call (network overhead)
            estimatedTime = TimeSpan.FromMilliseconds(200 * totalCalls);
        }

        // Check if this would exceed a reasonable free tier quota
        // (heuristic: >1000 calls in a day is likely paid territory)
        var wouldExceedFree = maxRequestsPerWindow < int.MaxValue &&
                              totalCalls > maxRequestsPerWindow * 2;

        return new ProviderCostEstimate(
            ProviderName: provider.Name,
            DisplayName: provider.DisplayName,
            Priority: provider.Priority,
            EstimatedApiCalls: totalCalls,
            EstimatedWallClockTime: estimatedTime,
            MaxRequestsPerWindow: maxRequestsPerWindow == int.MaxValue ? null : maxRequestsPerWindow,
            RateLimitWindow: rateLimitWindow,
            RateLimitDelay: rateLimitDelay,
            WouldExceedFreeQuota: wouldExceedFree,
            SupportsDateRange: callsPerSymbol <= 1);
    }

    private static List<string> GenerateWarnings(
        List<ProviderCostEstimate> estimates,
        string[] symbols,
        DataGranularity granularity,
        int tradingDays)
    {
        var warnings = new List<string>();

        if (tradingDays > 365)
            warnings.Add($"Large date range ({tradingDays} trading days). Consider breaking into smaller chunks.");

        if (granularity.IsIntraday() && tradingDays > 60)
        {
            warnings.Add(
                $"{granularity.ToDisplayName()} intraday requests will be chunked into multiple Yahoo-compatible windows.");
        }

        if (symbols.Length > 50)
            warnings.Add($"Large symbol list ({symbols.Length} symbols). This will require many API calls.");

        foreach (var est in estimates.Where(e => e.WouldExceedFreeQuota))
        {
            warnings.Add($"{est.DisplayName}: estimated {est.EstimatedApiCalls} calls may exceed free-tier quota ({est.MaxRequestsPerWindow} per {est.RateLimitWindow.TotalHours:F0}h).");
        }

        foreach (var est in estimates.Where(e => e.EstimatedWallClockTime > TimeSpan.FromHours(1)))
        {
            warnings.Add($"{est.DisplayName}: estimated wall-clock time is {est.EstimatedWallClockTime.TotalHours:F1} hours.");
        }

        return warnings;
    }

    private static BackfillCostEstimate CreateEstimateWithoutProviders(
        string[] symbols,
        DateOnly from,
        DateOnly to,
        int tradingDays,
        DataGranularity granularity,
        string warning)
    {
        var warnings = GenerateWarnings(new List<ProviderCostEstimate>(), symbols, granularity, tradingDays);
        warnings.Add(warning);

        return new BackfillCostEstimate(
            Symbols: symbols,
            From: from,
            To: to,
            TradingDays: tradingDays,
            ProviderEstimates: Array.Empty<ProviderCostEstimate>(),
            RecommendedProvider: null,
            Warnings: warnings,
            EstimatedAt: DateTimeOffset.UtcNow);
    }

    private static bool SupportsGranularity(IHistoricalDataProvider provider, DataGranularity granularity)
    {
        if (!granularity.IsIntraday())
            return true;

        return provider is IHistoricalAggregateBarProvider aggregateProvider &&
               aggregateProvider.SupportedGranularities.Contains(granularity);
    }

    private static int EstimateRequestsPerSymbol(IHistoricalDataProvider provider, DataGranularity granularity, int tradingDays)
    {
        if (!granularity.IsIntraday())
            return 1;

        if (provider is not IHistoricalAggregateBarProvider aggregateProvider ||
            !aggregateProvider.SupportedGranularities.Contains(granularity))
        {
            throw new InvalidOperationException(
                $"Provider '{provider.DisplayName}' does not support {granularity.ToDisplayName()} intraday backfill.");
        }

        var windowDays = granularity switch
        {
            DataGranularity.Minute1 => 8,
            DataGranularity.Minute5 or DataGranularity.Minute15 or DataGranularity.Minute30 => 60,
            DataGranularity.Hour1 or DataGranularity.Hour4 => 730,
            _ => 1
        };

        var normalizedTradingDays = Math.Max(tradingDays, 1);
        return (int)Math.Ceiling(normalizedTradingDays / (double)windowDays);
    }

    private static int EstimateTradingDays(DateOnly from, DateOnly to)
    {
        if (to <= from)
            return 0;

        // Count actual weekdays (Mon–Fri) in [from, to).
        // Does not subtract US holidays — that would require a TradingCalendar dependency —
        // but weekday counting is a significant improvement over a 5/7 approximation,
        // which is inaccurate for short ranges (e.g., a Mon–Sat [from, to) range with 5 trading days
        // would be approximated as 3 when using integer 5/7 scaling).
        var totalDays = to.DayNumber - from.DayNumber;
        var fullWeeks = totalDays / 7;
        var remainingDays = totalDays % 7;
        var startDow = (int)from.DayOfWeek; // 0=Sun, 1=Mon, …, 6=Sat

        var partialWeekdays = 0;
        for (var i = 0; i < remainingDays; i++)
        {
            var dow = (startDow + i) % 7;
            if (dow is not 0 and not 6) // exclude Sunday and Saturday
                partialWeekdays++;
        }

        return fullWeeks * 5 + partialWeekdays;
    }
}

/// <summary>
/// Request for cost estimation.
/// </summary>
public sealed record BackfillCostRequest(
    string[]? Symbols,
    string? Provider = null,
    DateOnly? From = null,
    DateOnly? To = null,
    DataGranularity Granularity = DataGranularity.Daily);

/// <summary>
/// Cost estimate for a backfill request.
/// </summary>
public sealed record BackfillCostEstimate(
    string[] Symbols,
    DateOnly From,
    DateOnly To,
    int TradingDays,
    IReadOnlyList<ProviderCostEstimate> ProviderEstimates,
    string? RecommendedProvider,
    IReadOnlyList<string> Warnings,
    DateTimeOffset EstimatedAt)
{
    public static BackfillCostEstimate Empty(string reason) => new(
        Symbols: Array.Empty<string>(),
        From: DateOnly.MinValue,
        To: DateOnly.MinValue,
        TradingDays: 0,
        ProviderEstimates: Array.Empty<ProviderCostEstimate>(),
        RecommendedProvider: null,
        Warnings: new[] { reason },
        EstimatedAt: DateTimeOffset.UtcNow);
}

/// <summary>
/// Cost estimate for a specific provider.
/// </summary>
public sealed record ProviderCostEstimate(
    string ProviderName,
    string DisplayName,
    int Priority,
    long EstimatedApiCalls,
    TimeSpan EstimatedWallClockTime,
    int? MaxRequestsPerWindow,
    TimeSpan RateLimitWindow,
    TimeSpan RateLimitDelay,
    bool WouldExceedFreeQuota,
    bool SupportsDateRange);
