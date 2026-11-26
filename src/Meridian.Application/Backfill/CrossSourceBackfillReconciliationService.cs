using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Platform.Scheduling;
using Meridian.Storage.Backfill;

namespace Meridian.Application.Backfill;

/// <summary>
/// Compares bounded daily backfill output across historical providers so data-confidence
/// workflows can retain deterministic discrepancy evidence instead of relying on log-only sampling.
/// </summary>
public sealed class CrossSourceBackfillReconciliationService
{
    private readonly IReadOnlyDictionary<string, IHistoricalDataProvider> _providers;

    /// <summary>
    /// Resolves the complete expected session population for the requested symbol and bounded
    /// range. A missing calendar, unresolved listing or partial coverage cannot close a comparison.
    /// Without a host resolver, only an explicit supported listing identity can resolve.
    /// </summary>
    public Func<string, DateOnly, DateOnly, CancellationToken, Task<CrossSourceBackfillSessionCoverage?>>?
        ResolveSessionCoverageAsync
    { get; init; }

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
        ct.ThrowIfCancellationRequested();

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

        var discrepancies = new List<CrossSourceBackfillDiscrepancy>();
        var sessionCoverage = await ResolveCoverageAsync(request, ct).ConfigureAwait(false);
        if (sessionCoverage is null)
        {
            discrepancies.Add(new CrossSourceBackfillDiscrepancy(
                request.From, baselineProviderKey, "session-calendar", "SessionCalendarMissingEvidence",
                null, null, null, null, null, null,
                "The symbol has no verified exchange-session calendar covering the entire requested range; agreeing provider rows cannot prove complete coverage."));
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
                discrepancies,
                providerErrors,
                sessionCoverage);
        }

        var requestedSymbol = NormalizeSymbol(request.Symbol);
        discrepancies.AddRange(CreateSymbolMismatchDiscrepancies(
            requestedSymbol,
            baselineProviderKey,
            baselineProviderKey,
            baselineFetch.Bars,
            isBaseline: true));
        var baselineSymbolBars = FilterBarsForRequestedSymbol(baselineFetch.Bars, requestedSymbol);
        discrepancies.AddRange(CreateUnexpectedSessionDiscrepancies(
            baselineProviderKey, baselineProviderKey, request, baselineSymbolBars, sessionCoverage, isBaseline: true));
        baselineSymbolBars = FilterBarsForRequestedRange(baselineSymbolBars, request);
        discrepancies.AddRange(CreateMissingEvidenceDiscrepancies(
            requestedSymbol,
            baselineProviderKey,
            baselineProviderKey,
            request.From,
            request.To,
            baselineSymbolBars,
            sessionCoverage,
            isBaseline: true));
        var baselineBars = IndexBySessionDate(baselineSymbolBars);

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
            var comparisonSymbolBars = FilterBarsForRequestedSymbol(comparisonFetch.Bars, requestedSymbol);
            discrepancies.AddRange(CreateUnexpectedSessionDiscrepancies(
                baselineProviderKey, comparisonProviderKey, request, comparisonSymbolBars, sessionCoverage, isBaseline: false));
            comparisonSymbolBars = FilterBarsForRequestedRange(comparisonSymbolBars, request);
            discrepancies.AddRange(CreateMissingEvidenceDiscrepancies(
                requestedSymbol,
                baselineProviderKey,
                comparisonProviderKey,
                request.From,
                request.To,
                comparisonSymbolBars,
                sessionCoverage,
                isBaseline: false));
            var comparisonBars = IndexBySessionDate(comparisonSymbolBars);
            discrepancies.AddRange(CompareProviderBars(
                baselineProviderKey,
                comparisonProviderKey,
                baselineBars,
                comparisonBars,
                sessionCoverage?.ExpectedSessions,
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
            providerErrors,
            sessionCoverage);
    }

    /// <summary>Adapts the shared exchange-calendar resolver without inferring sessions from provider rows.</summary>
    public static async Task<CrossSourceBackfillSessionCoverage?> ResolveExchangeSessionCoverageAsync(
        HistoricalSessionScheduleResolver resolver, string symbol, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ct.ThrowIfCancellationRequested();
        var schedule = await resolver.ResolveAsync(symbol, from, to, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return schedule is null ? null : new(schedule.Calendar, from, to, schedule.Sessions);
    }

    private async Task<CrossSourceBackfillSessionCoverage?> ResolveCoverageAsync(
        CrossSourceBackfillReconciliationRequest request, CancellationToken ct)
    {
        try
        {
            var coverage = ResolveSessionCoverageAsync is { } resolve
                ? await resolve(NormalizeSymbol(request.Symbol), request.From, request.To, ct).ConfigureAwait(false)
                : await ResolveExchangeSessionCoverageAsync(new HistoricalSessionScheduleResolver(null),
                    NormalizeSymbol(request.Symbol), request.From, request.To, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (coverage is null || !coverage.Covers(request.From, request.To))
                return null;

            // Freeze exactly the requested range; neither mutable resolver output nor a wider
            // calendar population may change the retained expectation after provider retrieval.
            return new CrossSourceBackfillSessionCoverage(coverage.Calendar, request.From, request.To,
                Array.AsReadOnly(coverage.ExpectedSessions.Where(date => date >= request.From && date <= request.To)
                    .Order().ToArray()));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Lookup outages, provider timeouts and malformed calendars all leave coverage
            // unavailable. Provider comparisons still run and retain any independent drift.
            return null;
        }
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
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

    private static IReadOnlyList<HistoricalBar> FilterBarsForRequestedRange(
        IReadOnlyList<HistoricalBar> bars, CrossSourceBackfillReconciliationRequest request)
        => bars.Where(bar => bar.SessionDate >= request.From && bar.SessionDate <= request.To).ToArray();

    private static IEnumerable<CrossSourceBackfillDiscrepancy> CreateUnexpectedSessionDiscrepancies(
        string baselineProvider, string provider, CrossSourceBackfillReconciliationRequest request,
        IReadOnlyList<HistoricalBar> bars, CrossSourceBackfillSessionCoverage? coverage, bool isBaseline)
    {
        var expected = coverage?.ExpectedSessions.ToHashSet();
        foreach (var bar in bars.Where(bar => bar.SessionDate < request.From || bar.SessionDate > request.To ||
                                             expected is not null && !expected.Contains(bar.SessionDate)))
        {
            yield return new CrossSourceBackfillDiscrepancy(
                bar.SessionDate, baselineProvider, provider,
                isBaseline ? "BaselineUnexpectedSession" : "ComparisonUnexpectedSession",
                isBaseline ? bar.Close : null, isBaseline ? null : bar.Close,
                isBaseline ? bar.Volume : null, isBaseline ? null : bar.Volume, null, null,
                $"{provider} returned a bar outside the requested range or on a confirmed non-trading date ({bar.SessionDate}); it cannot establish expected-session coverage.");
        }
    }

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

    private static IEnumerable<CrossSourceBackfillDiscrepancy> CreateMissingEvidenceDiscrepancies(
        string requestedSymbol,
        string baselineProvider,
        string provider,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<HistoricalBar> symbolScopedBars,
        CrossSourceBackfillSessionCoverage? sessionCoverage,
        bool isBaseline)
    {
        if (symbolScopedBars.Count > 0 || sessionCoverage?.ExpectedSessions.Count == 0)
        {
            yield break;
        }

        yield return new CrossSourceBackfillDiscrepancy(
            from,
            baselineProvider,
            provider,
            isBaseline ? "BaselineMissingEvidence" : "ComparisonMissingEvidence",
            null,
            null,
            null,
            null,
            null,
            null,
            $"{provider} returned no symbol-scoped bars for {requestedSymbol} between {from} and {to}; cross-source closure requires retained provider evidence.");
    }

    private static IEnumerable<CrossSourceBackfillDiscrepancy> CompareProviderBars(
        string baselineProvider,
        string comparisonProvider,
        IReadOnlyDictionary<DateOnly, HistoricalBar> baselineBars,
        IReadOnlyDictionary<DateOnly, HistoricalBar> comparisonBars,
        IReadOnlyList<DateOnly>? expectedSessions,
        decimal closeToleranceRatio,
        decimal volumeToleranceRatio)
    {
        IEnumerable<DateOnly> sessionDates = expectedSessions is null
            ? baselineBars.Keys.Union(comparisonBars.Keys)
            : expectedSessions;
        foreach (var sessionDate in sessionDates.OrderBy(static date => date))
        {
            var hasBaseline = baselineBars.TryGetValue(sessionDate, out var baselineBar);
            var hasComparison = comparisonBars.TryGetValue(sessionDate, out var comparisonBar);

            if (!hasBaseline && !hasComparison)
            {
                yield return new CrossSourceBackfillDiscrepancy(
                    sessionDate, baselineProvider, comparisonProvider, "SharedMissingSession",
                    null, null, null, null, null, null,
                    $"Neither {baselineProvider} nor {comparisonProvider} returned a bar for expected exchange session {sessionDate}; provider agreement does not establish complete coverage.");
                continue;
            }

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
        IReadOnlyList<CrossSourceBackfillProviderError> providerErrors,
        CrossSourceBackfillSessionCoverage? sessionCoverage)
        => new(
            NormalizeSymbol(request.Symbol),
            request.From,
            request.To,
            baselineProvider,
            comparisonProviders,
            discrepancies,
            providerErrors,
            DateTimeOffset.UtcNow)
        {
            SessionCoverage = sessionCoverage
        };

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
    /// <summary>Expected sessions from an identified calendar, independent of provider observations.</summary>
    public CrossSourceBackfillSessionCoverage? SessionCoverage { get; init; }

    public bool HasVerifiedSessionCoverage => SessionCoverage?.Covers(From, To) == true;

    public bool IsClean => HasVerifiedSessionCoverage && Discrepancies.Count == 0 && ProviderErrors.Count == 0;

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
        var missingEvidenceCount = symbolResults.Sum(static result =>
            result.Discrepancies.Count(static discrepancy => IsMissingEvidenceDiscrepancy(discrepancy.Kind)) +
            (!result.HasVerifiedSessionCoverage && !result.Discrepancies.Any(static discrepancy =>
                discrepancy.Kind == "SessionCalendarMissingEvidence") ? 1 : 0));
        var discrepancyCount = symbolResults.Sum(static result => result.Discrepancies.Count);
        var symbolsRequiringReview = symbolResults
            .Where(static result => !result.HasVerifiedSessionCoverage || result.ProviderErrors.Count > 0 || result.Discrepancies.Count > 0)
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

        if (missingEvidenceCount > 0)
        {
            return new CrossSourceBackfillClosureDecision(
                CrossSourceBackfillClosureStatus.BlockedByMissingEvidence,
                "MissingEvidence",
                $"{FormatSymbolCount(symbolsRequiringReview.Length)} require operator review because {missingEvidenceCount} calendar or provider evidence gap(s) were retained.",
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
            "Cross-source backfill remediation is closed because every expected exchange session has provider evidence and all symbols matched tolerance.",
            Array.Empty<string>());
    }

    private static string FormatSymbolCount(int count)
        => count == 1 ? "1 symbol" : $"{count} symbols";

    private static bool IsMissingEvidenceDiscrepancy(string kind)
        => string.Equals(kind, "BaselineMissingEvidence", StringComparison.Ordinal) ||
           string.Equals(kind, "ComparisonMissingEvidence", StringComparison.Ordinal) ||
           string.Equals(kind, "SessionCalendarMissingEvidence", StringComparison.Ordinal) ||
           string.Equals(kind, "SharedMissingSession", StringComparison.Ordinal);
}

/// <summary>Complete session population attested by a trusted exchange-calendar resolver over explicit bounds.</summary>
public sealed record CrossSourceBackfillSessionCoverage(
    string Calendar, DateOnly From, DateOnly To, IReadOnlyList<DateOnly> ExpectedSessions)
{
    public bool Covers(DateOnly from, DateOnly to)
        => !string.IsNullOrWhiteSpace(Calendar) && From <= To && from <= to &&
           ExpectedSessions is not null && ExpectedSessions.All(date => date >= From && date <= To) &&
           ExpectedSessions.Distinct().Count() == ExpectedSessions.Count &&
           BackfillCoverageMath.Missing(from, to, [new BackfillCoverageRange(From, To, 0)]).Count == 0;
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
