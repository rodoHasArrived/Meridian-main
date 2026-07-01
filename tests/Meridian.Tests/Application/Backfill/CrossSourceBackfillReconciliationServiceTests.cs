using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Application.Backfill;

/// <summary>
/// Guards cross-source daily backfill reconciliation for provider divergence, missing sessions,
/// and backup-provider outage scenarios that can block downstream data-confidence decisions.
/// </summary>
public sealed class CrossSourceBackfillReconciliationServiceTests
{
    [Fact]
    public async Task Scenario_CrossSourceBackfillDrift_ReportsPriceAndVolumeBreak()
    {
        var sessionDate = new DateOnly(2026, 06, 29);
        var baseline = new FakeHistoricalDataProvider(
            "alpaca",
            [Bar("SPY", sessionDate, close: 100m, volume: 1_000, source: "alpaca")]);
        var comparison = new FakeHistoricalDataProvider(
            "polygon",
            [Bar("SPY", sessionDate, close: 103m, volume: 1_200, source: "polygon")]);
        var service = new CrossSourceBackfillReconciliationService([baseline, comparison]);

        var result = await service.ReconcileDailyAsync(new CrossSourceBackfillReconciliationRequest(
            Symbol: "SPY",
            From: sessionDate,
            To: sessionDate,
            BaselineProvider: "alpaca",
            ComparisonProviders: ["polygon"]));

        result.IsClean.Should().BeFalse();
        result.IsSlaClosed.Should().BeFalse();
        result.ClosureDecision.Status.Should().Be(CrossSourceBackfillClosureStatus.BlockedByDiscrepancy);
        result.SymbolsRequiringReview.Should().Equal(["SPY"]);
        result.ProviderErrors.Should().BeEmpty();
        var discrepancy = result.Discrepancies.Should().ContainSingle().Subject;
        discrepancy.Kind.Should().Be("PriceAndVolumeDrift");
        discrepancy.SessionDate.Should().Be(sessionDate);
        discrepancy.BaselineClose.Should().Be(100m);
        discrepancy.ComparisonClose.Should().Be(103m);
        discrepancy.CloseDifferenceRatio.Should().Be(0.03m);
        discrepancy.VolumeDifferenceRatio.Should().Be(0.2m);
    }

    [Fact]
    public async Task Scenario_CrossSourceBackfillGap_ReportsMissingComparisonSession()
    {
        var firstDate = new DateOnly(2026, 06, 29);
        var secondDate = firstDate.AddDays(1);
        var baseline = new FakeHistoricalDataProvider(
            "alpaca",
            [
                Bar("SPY", firstDate, close: 100m, volume: 1_000, source: "alpaca"),
                Bar("SPY", secondDate, close: 101m, volume: 1_100, source: "alpaca")
            ]);
        var comparison = new FakeHistoricalDataProvider(
            "stooq",
            [Bar("SPY", firstDate, close: 100m, volume: 1_000, source: "stooq")]);
        var service = new CrossSourceBackfillReconciliationService([baseline, comparison]);

        var result = await service.ReconcileDailyAsync(new CrossSourceBackfillReconciliationRequest(
            Symbol: "SPY",
            From: firstDate,
            To: secondDate,
            BaselineProvider: "alpaca",
            ComparisonProviders: ["stooq"]));

        var discrepancy = result.Discrepancies.Should().ContainSingle().Subject;
        discrepancy.Kind.Should().Be("ComparisonMissingSession");
        discrepancy.SessionDate.Should().Be(secondDate);
        discrepancy.BaselineClose.Should().Be(101m);
        discrepancy.ComparisonClose.Should().BeNull();
        discrepancy.Message.Should().Contain("stooq did not return a bar");
    }

    [Fact]
    public async Task Scenario_BackupProviderOutage_RetainsProviderErrorAndContinuesComparisons()
    {
        var sessionDate = new DateOnly(2026, 06, 29);
        var baseline = new FakeHistoricalDataProvider(
            "alpaca",
            [Bar("SPY", sessionDate, close: 100m, volume: 1_000, source: "alpaca")]);
        var failingComparison = new FakeHistoricalDataProvider(
            "tiingo",
            handler: (_, _, _, _) => throw new InvalidOperationException("HTTP 503 from fixture"));
        var cleanComparison = new FakeHistoricalDataProvider(
            "polygon",
            [Bar("SPY", sessionDate, close: 100m, volume: 1_000, source: "polygon")]);
        var service = new CrossSourceBackfillReconciliationService(
            [baseline, failingComparison, cleanComparison]);

        var result = await service.ReconcileDailyAsync(new CrossSourceBackfillReconciliationRequest(
            Symbol: "SPY",
            From: sessionDate,
            To: sessionDate,
            BaselineProvider: "alpaca",
            ComparisonProviders: ["tiingo", "polygon"]));

        result.IsClean.Should().BeFalse("provider errors are retained as reconciliation blockers");
        result.IsSlaClosed.Should().BeFalse();
        result.ClosureDecision.Status.Should().Be(CrossSourceBackfillClosureStatus.BlockedByProviderError);
        result.SymbolsRequiringReview.Should().Equal(["SPY"]);
        result.Discrepancies.Should().BeEmpty("the healthy comparison agrees with the baseline");
        var error = result.ProviderErrors.Should().ContainSingle().Subject;
        error.Provider.Should().Be("tiingo");
        error.ErrorType.Should().Be(nameof(InvalidOperationException));
        cleanComparison.Calls.Should().Be(1, "provider failure should not prevent later comparisons");
    }

    [Fact]
    public async Task Scenario_MultiSymbolBackfillReconciliation_PreservesRequestOrderAndRetainsPerSymbolEvidence()
    {
        var sessionDate = new DateOnly(2026, 06, 29);
        var baseline = new FakeHistoricalDataProvider(
            "alpaca",
            [
                Bar("MSFT", sessionDate, close: 200m, volume: 2_000, source: "alpaca"),
                Bar("AAPL", sessionDate, close: 100m, volume: 1_000, source: "alpaca"),
                Bar("SPY", sessionDate, close: 500m, volume: 5_000, source: "alpaca")
            ]);
        var comparison = new FakeHistoricalDataProvider(
            "polygon",
            [
                Bar("MSFT", sessionDate, close: 200m, volume: 2_000, source: "polygon"),
                Bar("AAPL", sessionDate, close: 104m, volume: 1_000, source: "polygon"),
                Bar("SPY", sessionDate, close: 500m, volume: 5_000, source: "polygon")
            ]);
        var service = new CrossSourceBackfillReconciliationService([baseline, comparison]);

        var result = await service.ReconcileDailyBatchAsync(new CrossSourceBackfillBatchReconciliationRequest(
            Symbols: [" msft ", "AAPL", "MSFT", "spy"],
            From: sessionDate,
            To: sessionDate,
            BaselineProvider: "alpaca",
            ComparisonProviders: ["polygon"],
            CloseToleranceRatio: 0.01m,
            VolumeToleranceRatio: 0.05m));

        result.IsClean.Should().BeFalse("AAPL drift remains visible inside the batch result");
        result.IsSlaClosed.Should().BeFalse();
        result.ClosureDecision.Status.Should().Be(CrossSourceBackfillClosureStatus.BlockedByDiscrepancy);
        result.SymbolsRequiringReview.Should().Equal(["AAPL"]);
        result.SymbolResults.Select(symbolResult => symbolResult.Symbol)
            .Should()
            .Equal(["MSFT", "AAPL", "SPY"]);
        baseline.SymbolCalls.Should().Equal(["MSFT", "AAPL", "SPY"]);
        comparison.SymbolCalls.Should().Equal(["MSFT", "AAPL", "SPY"]);

        var aapl = result.SymbolResults.Single(symbolResult => symbolResult.Symbol == "AAPL");
        aapl.Discrepancies.Should().ContainSingle(discrepancy =>
            discrepancy.Kind == "ClosePriceDrift" &&
            discrepancy.BaselineClose == 100m &&
            discrepancy.ComparisonClose == 104m);
        result.SymbolResults.Single(symbolResult => symbolResult.Symbol == "MSFT").IsClean.Should().BeTrue();
        result.SymbolResults.Single(symbolResult => symbolResult.Symbol == "SPY").IsClean.Should().BeTrue();
    }

    [Fact]
    public async Task Scenario_CrossProviderRemediationClosure_CleanBatchMarksSlaClosed()
    {
        var sessionDate = new DateOnly(2026, 06, 29);
        var baseline = new FakeHistoricalDataProvider(
            "alpaca",
            [
                Bar("MSFT", sessionDate, close: 200m, volume: 2_000, source: "alpaca"),
                Bar("AAPL", sessionDate, close: 100m, volume: 1_000, source: "alpaca")
            ]);
        var comparison = new FakeHistoricalDataProvider(
            "polygon",
            [
                Bar("MSFT", sessionDate, close: 200m, volume: 2_000, source: "polygon"),
                Bar("AAPL", sessionDate, close: 100m, volume: 1_000, source: "polygon")
            ]);
        var service = new CrossSourceBackfillReconciliationService([baseline, comparison]);

        var result = await service.ReconcileDailyBatchAsync(new CrossSourceBackfillBatchReconciliationRequest(
            Symbols: ["MSFT", "AAPL"],
            From: sessionDate,
            To: sessionDate,
            BaselineProvider: "alpaca",
            ComparisonProviders: ["polygon"]));

        result.IsClean.Should().BeTrue();
        result.IsSlaClosed.Should().BeTrue();
        result.ClosureDecision.Status.Should().Be(CrossSourceBackfillClosureStatus.Closed);
        result.SymbolsRequiringReview.Should().BeEmpty();
        result.SymbolResults.Should().OnlyContain(static symbolResult => symbolResult.IsSlaClosed);
    }

    [Fact]
    public async Task Scenario_CrossSourceMissingEvidence_BlocksClosureWhenProvidersReturnNoSymbolScopedBars()
    {
        var sessionDate = new DateOnly(2026, 06, 29);
        var baseline = new FakeHistoricalDataProvider("alpaca");
        var comparison = new FakeHistoricalDataProvider("polygon");
        var service = new CrossSourceBackfillReconciliationService([baseline, comparison]);

        var result = await service.ReconcileDailyAsync(new CrossSourceBackfillReconciliationRequest(
            Symbol: "SPY",
            From: sessionDate,
            To: sessionDate,
            BaselineProvider: "alpaca",
            ComparisonProviders: ["polygon"]));

        result.IsClean.Should().BeFalse("cross-source remediation cannot close without retained provider evidence");
        result.IsSlaClosed.Should().BeFalse();
        result.ClosureDecision.Status.Should().Be(CrossSourceBackfillClosureStatus.BlockedByMissingEvidence);
        result.SymbolsRequiringReview.Should().Equal(["SPY"]);
        result.ProviderErrors.Should().BeEmpty();
        result.Discrepancies.Select(static discrepancy => discrepancy.Kind)
            .Should()
            .Equal(["BaselineMissingEvidence", "ComparisonMissingEvidence"]);
        result.Discrepancies.Should().OnlyContain(discrepancy =>
            discrepancy.SessionDate == sessionDate &&
            discrepancy.Message.Contains("returned no symbol-scoped bars", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Scenario_CrossProviderSymbolContamination_BlocksClosureWhenFallbackReturnsWrongSymbol()
    {
        var sessionDate = new DateOnly(2026, 06, 29);
        var baseline = new FakeHistoricalDataProvider(
            "alpaca",
            [Bar("SPY", sessionDate, close: 500m, volume: 5_000, source: "alpaca")]);
        var comparison = new FakeHistoricalDataProvider(
            "tiingo",
            handler: (_, _, _, _) => Task.FromResult<IReadOnlyList<HistoricalBar>>(
                [Bar("IVV", sessionDate, close: 500m, volume: 5_000, source: "tiingo")]));
        var service = new CrossSourceBackfillReconciliationService([baseline, comparison]);

        var result = await service.ReconcileDailyAsync(new CrossSourceBackfillReconciliationRequest(
            Symbol: "SPY",
            From: sessionDate,
            To: sessionDate,
            BaselineProvider: "alpaca",
            ComparisonProviders: ["tiingo"]));

        result.IsClean.Should().BeFalse("same-date fallback evidence for a different symbol cannot close SPY remediation");
        result.IsSlaClosed.Should().BeFalse();
        result.ClosureDecision.Status.Should().Be(CrossSourceBackfillClosureStatus.BlockedByMissingEvidence);
        result.SymbolsRequiringReview.Should().Equal(["SPY"]);
        result.Discrepancies.Should().Contain(discrepancy =>
            discrepancy.Kind == "ComparisonSymbolMismatch" &&
            discrepancy.ComparisonProvider == "tiingo" &&
            discrepancy.ComparisonClose == 500m &&
            discrepancy.ComparisonVolume == 5_000 &&
            discrepancy.Message.Contains("tiingo returned IVV while reconciling SPY", StringComparison.Ordinal));
        result.Discrepancies.Should().Contain(discrepancy =>
            discrepancy.Kind == "ComparisonMissingEvidence" &&
            discrepancy.ComparisonProvider == "tiingo");
        result.Discrepancies.Should().Contain(discrepancy =>
            discrepancy.Kind == "ComparisonMissingSession" &&
            discrepancy.ComparisonProvider == "tiingo");
    }

    [Fact]
    public async Task ReconcileDailyAsync_Cancellation_PropagatesWithoutProviderCalls()
    {
        var sessionDate = new DateOnly(2026, 06, 29);
        var baseline = new FakeHistoricalDataProvider(
            "alpaca",
            [Bar("SPY", sessionDate, close: 100m, volume: 1_000, source: "alpaca")]);
        var comparison = new FakeHistoricalDataProvider(
            "polygon",
            [Bar("SPY", sessionDate, close: 100m, volume: 1_000, source: "polygon")]);
        var service = new CrossSourceBackfillReconciliationService([baseline, comparison]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.ReconcileDailyAsync(new CrossSourceBackfillReconciliationRequest(
            Symbol: "SPY",
            From: sessionDate,
            To: sessionDate,
            BaselineProvider: "alpaca",
            ComparisonProviders: ["polygon"]), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        baseline.Calls.Should().Be(0);
        comparison.Calls.Should().Be(0);
    }

    private static HistoricalBar Bar(
        string symbol,
        DateOnly sessionDate,
        decimal close,
        long volume,
        string source)
        => new(symbol, sessionDate, close, close, close, close, volume, source);

    private sealed class FakeHistoricalDataProvider : IHistoricalDataProvider
    {
        private readonly IReadOnlyList<HistoricalBar> _bars;
        private readonly Func<string, DateOnly?, DateOnly?, CancellationToken, Task<IReadOnlyList<HistoricalBar>>>? _handler;

        public FakeHistoricalDataProvider(
            string name,
            IReadOnlyList<HistoricalBar>? bars = null,
            Func<string, DateOnly?, DateOnly?, CancellationToken, Task<IReadOnlyList<HistoricalBar>>>? handler = null)
        {
            Name = name;
            _bars = bars ?? Array.Empty<HistoricalBar>();
            _handler = handler;
        }

        public string Name { get; }

        public string DisplayName => Name;

        public string Description => $"Fake historical provider {Name}";

        public int Priority => 100;

        public TimeSpan RateLimitDelay => TimeSpan.Zero;

        public int MaxRequestsPerWindow => int.MaxValue;

        public TimeSpan RateLimitWindow => TimeSpan.FromHours(1);

        public HistoricalDataCapabilities Capabilities => HistoricalDataCapabilities.None;

        public int Calls { get; private set; }

        public IReadOnlyList<string> SymbolCalls => _symbolCalls;

        private readonly List<string> _symbolCalls = [];

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol,
            DateOnly? from,
            DateOnly? to,
            CancellationToken ct = default)
        {
            Calls++;
            _symbolCalls.Add(symbol);

            if (_handler is not null)
            {
                return _handler(symbol, from, to, ct);
            }

            var filtered = _bars
                .Where(bar => string.Equals(bar.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .Where(bar => !from.HasValue || bar.SessionDate >= from.Value)
                .Where(bar => !to.HasValue || bar.SessionDate <= to.Value)
                .ToArray();

            return Task.FromResult<IReadOnlyList<HistoricalBar>>(filtered);
        }

        public void Dispose()
        {
        }
    }
}
