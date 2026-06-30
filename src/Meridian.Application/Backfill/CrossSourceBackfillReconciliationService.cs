using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Application.Backfill;

/// <summary>
/// Compares bounded daily backfill output across historical providers so data-confidence
/// workflows can retain deterministic discrepancy evidence instead of relying on log-only sampling.
/// </summary>
public sealed class CrossSourceBackfillReconciliationService
{
    private readonly IReadOnlyDictionary<string, IHistoricalDataProvider> _providers;

    public CrossSourceBackfillReconciliationService(IEnumerable<IHistoricalDataProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers.ToDictionary(
            provider => provider.Name.Trim().ToLowerInvariant(),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<CrossSourceBackfillReconciliationResult> ReconcileDailyAsync(
        CrossSourceBackfillReconciliationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var baselineProviderKey = NormalizeProviderName(request.BaselineProvider);
        var comparisonProviderKeys = request.ComparisonProviders
            .Select(NormalizeProviderName)
            .Where(provider => provider.Length > 0)
            .Where(provider => !string.Equals(provider, baselineProviderKey, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (comparisonProviderKeys.Length == 0)
        {
            throw new InvalidOperationException("At least one non-baseline comparison provider is required.");
        }

        var providerErrors = new List<CrossSourceBackfillProviderError>();
        var baselineFetch = await FetchProviderBarsAsync(
            baselineProviderKey,
            request.Symbol,
            request.From,
            request.To,
            ct).ConfigureAwait(false);

        if (baselineFetch.Error is not null)
        {
            providerErrors.Add(baselineFetch.Error);
            return CreateResult(
                request,
                baselineProviderKey,
                comparisonProviderKeys,
                Array.Empty<CrossSourceBackfillDiscrepancy>(),
                providerErrors);
        }

        var requestedSymbol = NormalizeSymbol(request.Symbol);
        var discrepancies = new List<CrossSourceBackfillDiscrepancy>();
        discrepancies.AddRange(CreateSymbolMismatchDiscrepancies(
            requestedSymbol,
            baselineProviderKey,
            baselineProviderKey,
            baselineFetch.Bars,
            isBaseline: true));
        var baselineBars = IndexBySessionDate(FilterBarsForRequestedSymbol(baselineFetch.Bars, requestedSymbol));

        foreach (var comparisonProviderKey in comparisonProviderKeys)
        {
            ct.ThrowIfCancellationRequested();

            var comparisonFetch = await FetchProviderBarsAsync(
                comparisonProviderKey,
                request.Symbol,
                request.From,
                request.To,
                ct).ConfigureAwait(false);

            if (comparisonFetch.Error is not null)
            {
                providerErrors.Add(comparisonFetch.Error);
                continue;
            }

            discrepancies.AddRange(CreateSymbolMismatchDiscrepancies(
                requestedSymbol,
                baselineProviderKey,
                comparisonProviderKey,
                comparisonFetch.Bars,
                isBaseline: false));
            var comparisonBars = IndexBySessionDate(FilterBarsForRequestedSymbol(comparisonFetch.Bars, requestedSymbol));
            discrepancies.AddRange(CompareProviderBars(
                baselineProviderKey,
                comparisonProviderKey,
                baselineBars,
                comparisonBars,
                request.CloseToleranceRatio,
                request.VolumeToleranceRatio));
        }

        return CreateResult(
            request,
            baselineProviderKey,
            comparisonProviderKeys,
            discrepancies
                .OrderBy(discrepancy => discrepancy.SessionDate)
                .ThenBy(discrepancy => discrepancy.ComparisonProvider, StringComparer.OrdinalIgnoreCase)
                .ThenBy(discrepancy => discrepancy.Kind, StringComparer.Ordinal)
                .ToArray(),
            providerErrors);
    }

    public async Task<CrossSourceBackfillBatchReconciliationResult> ReconcileDailyBatchAsync(
        CrossSourceBackfillBatchReconciliationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var symbols = NormalizeSymbols(request.Symbols);
        if (symbols.Length == 0)
        {
            throw new ArgumentException("At least one symbol is required.", nameof(request));
        }

        var results = new List<CrossSourceBackfillReconciliationResult>(symbols.Length);
        foreach (var symbol in symbols)
        {
            ct.ThrowIfCancellationRequested();

            results.Add(await ReconcileDailyAsync(
                new CrossSourceBackfillReconciliationRequest(
                    symbol,
                    request.From,
                    request.To,
                    request.BaselineProvider,
                    request.ComparisonProviders,
                    request.CloseToleranceRatio,
                    request.VolumeToleranceRatio),
                ct).ConfigureAwait(false));
        }

        return new CrossSourceBackfillBatchReconciliationResult(results, DateTimeOffset.UtcNow);
    }

    private static void ValidateRequest(CrossSourceBackfillReconciliationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
        {
            throw new ArgumentException("Symbol is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.BaselineProvider))
        {
            throw new ArgumentException("Baseline provider is required.", nameof(request));
        }

        if (request.ComparisonProviders is null || request.ComparisonProviders.Count == 0)
        {
            throw new InvalidOperationException("At least one comparison provider is required.");
        }

        if (request.To < request.From)
        {
            throw new InvalidOperationException("Reconciliation end date cannot be earlier than the start date.");
        }

        if (request.CloseToleranceRatio < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Close tolerance ratio cannot be negative.");
        }

        if (request.VolumeToleranceRatio < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Volume tolerance ratio cannot be negative.");
        }
    }

    private static string[] NormalizeSymbols(IReadOnlyList<string>? symbols)
    {
        if (symbols is null || symbols.Count == 0)
        {
            return [];
        }

        var normalized = new List<string>(symbols.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }

            var normalizedSymbol = symbol.Trim().ToUpperInvariant();
            if (seen.Add(normalizedSymbol))
            {
                normalized.Add(normalizedSymbol);
            }
        }

        return normalized.ToArray();
    }

    private async Task<ProviderFetchResult> FetchProviderBarsAsync(
        string providerKey,
        string symbol,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!_providers.TryGetValue(providerKey, out var provider))
        {
            return ProviderFetchResult.Failed(new CrossSourceBackfillProviderError(
                providerKey,
                "Provider is not registered for historical backfill reconciliation.",
                "UnknownProvider"));
        }

        try
        {
            var bars = await provider.GetDailyBarsAsync(symbol, from, to, ct).ConfigureAwait(false);
            return ProviderFetchResult.Succeeded(bars);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProviderFetchResult.Failed(new CrossSourceBackfillProviderError(
                provider.Name,
                ex.Message,
                ex.GetType().Name));
        }
    }

    private static IReadOnlyDictionary<DateOnly, HistoricalBar> IndexBySessionDate(IReadOnlyList<HistoricalBar> bars)
    {
        return bars
            .GroupBy(static bar => bar.SessionDate)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static bar => bar.SequenceNumber)
                    .Last());
    }

    private static IReadOnlyList<HistoricalBar> FilterBarsForRequestedSymbol(
        IReadOnlyList<HistoricalBar> bars,
        string requestedSymbol)
        => bars
            .Where(bar => SymbolMatches(bar.Symbol, requestedSymbol))
            .ToArray();

    private static IEnumerable<CrossSourceBackfillDiscrepancy> CreateSymbolMismatchDiscrepancies(
        string requestedSymbol,
        string baselineProvider,
        string provider,
        IReadOnlyList<HistoricalBar> bars,
        bool isBaseline)
    {
        foreach (var bar in bars
            .Where(bar => !SymbolMatches(bar.Symbol, requestedSymbol))
            .OrderBy(static bar => bar.SessionDate)
            .ThenBy(static bar => bar.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            var actualSymbol = NormalizeSymbol(bar.Symbol);
            yield return new CrossSourceBackfillDiscrepancy(
                bar.SessionDate,
                baselineProvider,
                provider,
                isBaseline ? "BaselineSymbolMismatch" : "ComparisonSymbolMismatch",
                isBaseline ? bar.Close : null,
                isBaseline ? null : bar.Close,
                isBaseline ? bar.Volume : null,
                isBaseline ? null : bar.Volume,
                null,
                null,
                $"{provider} returned {actualSymbol} while reconciling {requestedSymbol} for {bar.SessionDate}; cross-source closure requires symbol-scoped evidence.");
        }
    }

    private static IEnumerable<CrossSourceBackfillDiscrepancy> CompareProviderBars(
        string baselineProvider,
        string comparisonProvider,
        IReadOnlyDictionary<DateOnly, HistoricalBar> baselineBars,
        IReadOnlyDictionary<DateOnly, HistoricalBar> comparisonBars,
        decimal closeToleranceRatio,
        decimal volumeToleranceRatio)
    {
        foreach (var sessionDate in baselineBars.Keys.Union(comparisonBars.Keys).OrderBy(static date => date))
        {
            var hasBaseline = baselineBars.TryGetValue(sessionDate, out var baselineBar);
            var hasComparison = comparisonBars.TryGetValue(sessionDate, out var comparisonBar);

            if (!hasBaseline && hasComparison)
            {
                yield return new CrossSourceBackfillDiscrepancy(
                    sessionDate,
                    baselineProvider,
                    comparisonProvider,
                    "BaselineMissingSession",
                    null,
                    comparisonBar!.Close,
                    null,
                    comparisonBar.Volume,
                    null,
                    null,
                    $"{baselineProvider} did not return a bar for {sessionDate}, but {comparisonProvider} did.");
                continue;
            }

            if (hasBaseline && !hasComparison)
            {
                yield return new CrossSourceBackfillDiscrepancy(
                    sessionDate,
                    baselineProvider,
                    comparisonProvider,
                    "ComparisonMissingSession",
                    baselineBar!.Close,
                    null,
                    baselineBar.Volume,
                    null,
                    null,
                    null,
                    $"{comparisonProvider} did not return a bar for {sessionDate}, but {baselineProvider} did.");
                continue;
            }

            if (baselineBar is null || comparisonBar is null)
            {
                continue;
            }

            var closeDifferenceRatio = RelativeDifference(baselineBar.Close, comparisonBar.Close);
            var volumeDifferenceRatio = RelativeDifference(baselineBar.Volume, comparisonBar.Volume);
            var hasCloseDrift = closeDifferenceRatio > closeToleranceRatio;
            var hasVolumeDrift = volumeDifferenceRatio > volumeToleranceRatio;

            if (!hasCloseDrift && !hasVolumeDrift)
            {
                continue;
            }

            var kind = hasCloseDrift && hasVolumeDrift
                ? "PriceAndVolumeDrift"
                : hasCloseDrift
                    ? "ClosePriceDrift"
                    : "VolumeDrift";

            yield return new CrossSourceBackfillDiscrepancy(
                sessionDate,
                baselineProvider,
                comparisonProvider,
                kind,
                baselineBar.Close,
                comparisonBar.Close,
                baselineBar.Volume,
                comparisonBar.Volume,
                closeDifferenceRatio,
                volumeDifferenceRatio,
                $"{comparisonProvider} {kind} exceeded tolerance against {baselineProvider} for {sessionDate}.");
        }
    }

    private static decimal RelativeDifference(decimal baseline, decimal comparison)
    {
        if (baseline == 0m)
        {
            return comparison == 0m ? 0m : 1m;
        }

        return Math.Abs(baseline - comparison) / Math.Abs(baseline);
    }

    private static decimal RelativeDifference(long baseline, long comparison)
    {
        if (baseline == 0)
        {
            return comparison == 0 ? 0m : 1m;
        }

        return Math.Abs((decimal)baseline - comparison) / Math.Abs((decimal)baseline);
    }

    private static string NormalizeProviderName(string provider)
        => provider.Trim().ToLowerInvariant();

    private static string NormalizeSymbol(string symbol)
        => symbol.Trim().ToUpperInvariant();

    private static bool SymbolMatches(string actualSymbol, string requestedSymbol)
        => string.Equals(actualSymbol.Trim(), requestedSymbol, StringComparison.OrdinalIgnoreCase);

    private static CrossSourceBackfillReconciliationResult CreateResult(
        CrossSourceBackfillReconciliationRequest request,
        string baselineProvider,
        IReadOnlyList<string> comparisonProviders,
        IReadOnlyList<CrossSourceBackfillDiscrepancy> discrepancies,
        IReadOnlyList<CrossSourceBackfillProviderError> providerErrors)
        => new(
            NormalizeSymbol(request.Symbol),
            request.From,
            request.To,
            baselineProvider,
            comparisonProviders,
            discrepancies,
            providerErrors,
            DateTimeOffset.UtcNow);

    private sealed record ProviderFetchResult(
        IReadOnlyList<HistoricalBar> Bars,
        CrossSourceBackfillProviderError? Error)
    {
        public static ProviderFetchResult Succeeded(IReadOnlyList<HistoricalBar> bars) => new(bars, null);

        public static ProviderFetchResult Failed(CrossSourceBackfillProviderError error)
            => new(Array.Empty<HistoricalBar>(), error);
    }
}

public sealed record CrossSourceBackfillReconciliationRequest(
    string Symbol,
    DateOnly From,
    DateOnly To,
    string BaselineProvider,
    IReadOnlyList<string> ComparisonProviders,
    decimal CloseToleranceRatio = 0.01m,
    decimal VolumeToleranceRatio = 0.05m);

public sealed record CrossSourceBackfillBatchReconciliationRequest(
    IReadOnlyList<string> Symbols,
    DateOnly From,
    DateOnly To,
    string BaselineProvider,
    IReadOnlyList<string> ComparisonProviders,
    decimal CloseToleranceRatio = 0.01m,
    decimal VolumeToleranceRatio = 0.05m);

public sealed record CrossSourceBackfillReconciliationResult(
    string Symbol,
    DateOnly From,
    DateOnly To,
    string BaselineProvider,
    IReadOnlyList<string> ComparisonProviders,
    IReadOnlyList<CrossSourceBackfillDiscrepancy> Discrepancies,
    IReadOnlyList<CrossSourceBackfillProviderError> ProviderErrors,
    DateTimeOffset ReconciledAt)
{
    public bool IsClean => Discrepancies.Count == 0 && ProviderErrors.Count == 0;

    public bool IsSlaClosed => ClosureDecision.Status == CrossSourceBackfillClosureStatus.Closed;

    public IReadOnlyList<string> SymbolsRequiringReview => ClosureDecision.SymbolsRequiringReview;

    public CrossSourceBackfillClosureDecision ClosureDecision
        => CrossSourceBackfillClosureDecision.FromResults([this]);
}

public sealed record CrossSourceBackfillBatchReconciliationResult(
    IReadOnlyList<CrossSourceBackfillReconciliationResult> SymbolResults,
    DateTimeOffset ReconciledAt)
{
    public bool IsClean => ClosureDecision.Status == CrossSourceBackfillClosureStatus.Closed;

    public bool IsSlaClosed => ClosureDecision.Status == CrossSourceBackfillClosureStatus.Closed;

    public IReadOnlyList<string> SymbolsRequiringReview => ClosureDecision.SymbolsRequiringReview;

    public CrossSourceBackfillClosureDecision ClosureDecision
        => CrossSourceBackfillClosureDecision.FromResults(SymbolResults);
}

public enum CrossSourceBackfillClosureStatus
{
    Closed = 0,
    BlockedByProviderError = 1,
    BlockedByDiscrepancy = 2,
    BlockedByMissingEvidence = 3
}

public sealed record CrossSourceBackfillClosureDecision(
    CrossSourceBackfillClosureStatus Status,
    string ReasonCode,
    string Message,
    IReadOnlyList<string> SymbolsRequiringReview)
{
    public static CrossSourceBackfillClosureDecision FromResults(
        IReadOnlyList<CrossSourceBackfillReconciliationResult> symbolResults)
    {
        ArgumentNullException.ThrowIfNull(symbolResults);

        if (symbolResults.Count == 0)
        {
            return new CrossSourceBackfillClosureDecision(
                CrossSourceBackfillClosureStatus.BlockedByMissingEvidence,
                "NoEvidence",
                "Cross-source backfill remediation cannot close without retained reconciliation evidence.",
                Array.Empty<string>());
        }

        var providerErrorCount = symbolResults.Sum(static result => result.ProviderErrors.Count);
        var discrepancyCount = symbolResults.Sum(static result => result.Discrepancies.Count);
        var symbolsRequiringReview = symbolResults
            .Where(static result => result.ProviderErrors.Count > 0 || result.Discrepancies.Count > 0)
            .Select(static result => result.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (providerErrorCount > 0)
        {
            return new CrossSourceBackfillClosureDecision(
                CrossSourceBackfillClosureStatus.BlockedByProviderError,
                "ProviderError",
                $"{FormatSymbolCount(symbolsRequiringReview.Length)} require operator review because {providerErrorCount} provider error(s) were retained.",
                symbolsRequiringReview);
        }

        if (discrepancyCount > 0)
        {
            return new CrossSourceBackfillClosureDecision(
                CrossSourceBackfillClosureStatus.BlockedByDiscrepancy,
                "Discrepancy",
                $"{FormatSymbolCount(symbolsRequiringReview.Length)} require operator review because {discrepancyCount} cross-source discrepancy signal(s) exceeded tolerance.",
                symbolsRequiringReview);
        }

        return new CrossSourceBackfillClosureDecision(
            CrossSourceBackfillClosureStatus.Closed,
            "Closed",
            "Cross-source backfill remediation is closed because all symbols matched tolerance and providers returned evidence.",
            Array.Empty<string>());
    }

    private static string FormatSymbolCount(int count)
        => count == 1 ? "1 symbol" : $"{count} symbols";
}

public sealed record CrossSourceBackfillDiscrepancy(
    DateOnly SessionDate,
    string BaselineProvider,
    string ComparisonProvider,
    string Kind,
    decimal? BaselineClose,
    decimal? ComparisonClose,
    long? BaselineVolume,
    long? ComparisonVolume,
    decimal? CloseDifferenceRatio,
    decimal? VolumeDifferenceRatio,
    string Message);

public sealed record CrossSourceBackfillProviderError(
    string Provider,
    string Message,
    string ErrorType);
