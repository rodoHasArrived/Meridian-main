using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Backfill;
using Meridian.Platform.Scheduling;
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
        var service = CreateService([baseline, comparison]);

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
        var service = CreateService([baseline, comparison]);

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
        var service = CreateService(
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
        var service = CreateService([baseline, comparison]);

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
        var service = CreateService([baseline, comparison]);

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
        var service = CreateService([baseline, comparison]);

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
            .Equal(["BaselineMissingEvidence", "ComparisonMissingEvidence", "SharedMissingSession"]);
        result.Discrepancies.Should().OnlyContain(discrepancy => discrepancy.SessionDate == sessionDate);
        result.Discrepancies.Where(discrepancy => discrepancy.Kind.EndsWith("MissingEvidence", StringComparison.Ordinal))
            .Should().OnlyContain(discrepancy => discrepancy.Message.Contains("returned no symbol-scoped bars", StringComparison.Ordinal));
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
        var service = CreateService([baseline, comparison]);

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
        var service = CreateService([baseline, comparison]);
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SharedMissingExpectedSession_BlocksLeadingInteriorAndTrailingGaps(int missingOffset)
    {
        var from = new DateOnly(2026, 6, 29);
        var to = from.AddDays(2);
        var missing = from.AddDays(missingOffset);
        var observed = Enumerable.Range(0, 3).Select(from.AddDays).Where(date => date != missing).ToArray();
        var baseline = new FakeHistoricalDataProvider("alpaca",
            observed.Select(date => Bar("SPY", date, 100m, 1_000, "alpaca")).ToArray());
        var comparison = new FakeHistoricalDataProvider("polygon",
            observed.Select(date => Bar("SPY", date, 100m, 1_000, "polygon")).ToArray());

        var result = await CreateService([baseline, comparison]).ReconcileDailyAsync(
            new("SPY", from, to, "alpaca", ["polygon"]));

        result.IsClean.Should().BeFalse();
        result.ClosureDecision.Status.Should().Be(CrossSourceBackfillClosureStatus.BlockedByMissingEvidence);
        result.SessionCoverage!.Calendar.Should().Be(HistoricalSessionEvidence.UsEquities);
        result.SessionCoverage.From.Should().Be(from);
        result.SessionCoverage.To.Should().Be(to);
        result.SessionCoverage.ExpectedSessions.Should().Equal(from, from.AddDays(1), to);
        var gap = result.Discrepancies.Should().ContainSingle().Subject;
        gap.Kind.Should().Be("SharedMissingSession");
        gap.SessionDate.Should().Be(missing);
        gap.BaselineClose.Should().BeNull();
        gap.ComparisonClose.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmedHolidayBetweenObservedSessions_DoesNotCreateAGap()
    {
        var from = new DateOnly(2024, 7, 3);
        var holiday = from.AddDays(1);
        var to = from.AddDays(2);
        var baseline = new FakeHistoricalDataProvider("alpaca", [Bar("SPY", from, 100m, 100, "alpaca"), Bar("SPY", to, 100m, 100, "alpaca")]);
        var comparison = new FakeHistoricalDataProvider("polygon", [Bar("SPY", from, 100m, 100, "polygon"), Bar("SPY", to, 100m, 100, "polygon")]);

        var result = await CreateService([baseline, comparison]).ReconcileDailyAsync(
            new("SPY", from, to, "alpaca", ["polygon"]));

        result.IsSlaClosed.Should().BeTrue();
        result.SessionCoverage!.ExpectedSessions.Should().Equal(from, to).And.NotContain(holiday);
        result.Discrepancies.Should().BeEmpty();
    }

    [Fact]
    public async Task ProviderBarOnConfirmedHoliday_IsUnexpectedWithoutCreatingAMissingSession()
    {
        var from = new DateOnly(2024, 7, 3);
        var holiday = from.AddDays(1);
        var to = from.AddDays(2);
        var baseline = new FakeHistoricalDataProvider("alpaca",
        [
            Bar("SPY", from, 100m, 100, "alpaca"),
            Bar("SPY", holiday, 100m, 100, "alpaca"),
            Bar("SPY", to, 100m, 100, "alpaca")
        ]);
        var comparison = new FakeHistoricalDataProvider("polygon",
            [Bar("SPY", from, 100m, 100, "polygon"), Bar("SPY", to, 100m, 100, "polygon")]);

        var result = await CreateService([baseline, comparison]).ReconcileDailyAsync(
            new("SPY", from, to, "alpaca", ["polygon"]));

        result.IsSlaClosed.Should().BeFalse();
        var discrepancy = result.Discrepancies.Should().ContainSingle().Subject;
        discrepancy.Kind.Should().Be("BaselineUnexpectedSession");
        discrepancy.SessionDate.Should().Be(holiday);
    }

    [Fact]
    public async Task ConfirmedHolidayOnlyRange_CanCloseWithEmptyProviderResponses()
    {
        var holiday = new DateOnly(2024, 7, 4);
        var baseline = new FakeHistoricalDataProvider("alpaca");
        var comparison = new FakeHistoricalDataProvider("polygon");

        var result = await CreateService([baseline, comparison]).ReconcileDailyAsync(
            new("SPY", holiday, holiday, "alpaca", ["polygon"]));

        result.IsSlaClosed.Should().BeTrue();
        result.SessionCoverage!.ExpectedSessions.Should().BeEmpty();
        baseline.Calls.Should().Be(1);
        comparison.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("SPY")]
    [InlineData("BTC/USD")]
    [InlineData("VOD:XLON")]
    public async Task UnresolvedOrUnsupportedListing_DoesNotInferAWeekdayCalendar(string symbol)
    {
        var day = new DateOnly(2026, 6, 29);
        var baseline = new FakeHistoricalDataProvider("alpaca", [Bar(symbol, day, 100m, 100, "alpaca")]);
        var comparison = new FakeHistoricalDataProvider("polygon", [Bar(symbol, day, 100m, 100, "polygon")]);
        var service = new CrossSourceBackfillReconciliationService([baseline, comparison]);

        var result = await service.ReconcileDailyAsync(new(symbol, day, day, "alpaca", ["polygon"]));

        result.IsClean.Should().BeFalse();
        result.SessionCoverage.Should().BeNull();
        result.ClosureDecision.Status.Should().Be(CrossSourceBackfillClosureStatus.BlockedByMissingEvidence);
        result.Discrepancies.Should().ContainSingle().Which.Kind.Should().Be("SessionCalendarMissingEvidence");
    }

    [Fact]
    public async Task ExplicitSupportedListing_UsesExchangeCalendarWithoutAnInferredSymbolDefault()
    {
        var day = new DateOnly(2026, 6, 29);
        const string symbol = "SPY:ARCX";
        var service = new CrossSourceBackfillReconciliationService([
            new FakeHistoricalDataProvider("alpaca", [Bar(symbol, day, 100m, 100, "alpaca")]),
            new FakeHistoricalDataProvider("polygon", [Bar(symbol, day, 100m, 100, "polygon")])]);

        var result = await service.ReconcileDailyAsync(new(symbol, day, day, "alpaca", ["polygon"]));

        result.IsSlaClosed.Should().BeTrue();
        result.SessionCoverage!.Calendar.Should().Be(HistoricalSessionEvidence.UsEquities);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("blank-identity")]
    [InlineData("leading-coverage")]
    [InlineData("trailing-coverage")]
    [InlineData("out-of-bounds-session")]
    [InlineData("duplicate-session")]
    [InlineData("timeout")]
    public async Task UnavailableOrMalformedCalendar_CannotCertifyMatchingProviderRows(string failure)
    {
        var from = new DateOnly(2026, 6, 29);
        var to = from.AddDays(2);
        var days = Enumerable.Range(0, 3).Select(from.AddDays).ToArray();
        var baseline = new FakeHistoricalDataProvider("alpaca", days.Select(day => Bar("SPY", day, 100m, 100, "alpaca")).ToArray());
        var comparison = new FakeHistoricalDataProvider("polygon", days.Select(day => Bar("SPY", day, 100m, 100, "polygon")).ToArray());
        var service = new CrossSourceBackfillReconciliationService([baseline, comparison])
        {
            ResolveSessionCoverageAsync = (_, _, _, _) => failure == "timeout"
                ? throw new TimeoutException("calendar unavailable")
                : Task.FromResult<CrossSourceBackfillSessionCoverage?>(failure switch
                {
                    "missing" => null,
                    "blank-identity" => new(" ", from, to, days),
                    "leading-coverage" => new("fixture-calendar-v1", from.AddDays(1), to, days[1..]),
                    "trailing-coverage" => new("fixture-calendar-v1", from, to.AddDays(-1), days[..2]),
                    "out-of-bounds-session" => new("fixture-calendar-v1", from, to, [.. days, to.AddDays(1)]),
                    "duplicate-session" => new("fixture-calendar-v1", from, to, [.. days, from]),
                    _ => throw new InvalidOperationException()
                })
        };

        var result = await service.ReconcileDailyAsync(new("SPY", from, to, "alpaca", ["polygon"]));

        result.IsSlaClosed.Should().BeFalse();
        result.SessionCoverage.Should().BeNull();
        result.Discrepancies.Should().ContainSingle().Which.Kind.Should().Be("SessionCalendarMissingEvidence");
        baseline.Calls.Should().Be(1, "calendar failure does not discard useful independent price comparisons");
        comparison.Calls.Should().Be(1);
    }

    [Fact]
    public async Task CalendarCancellation_PropagatesBeforeAnyProviderFetch()
    {
        var day = new DateOnly(2026, 6, 29);
        var baseline = new FakeHistoricalDataProvider("alpaca");
        var comparison = new FakeHistoricalDataProvider("polygon");
        using var cancellation = new CancellationTokenSource();
        var service = new CrossSourceBackfillReconciliationService([baseline, comparison])
        {
            ResolveSessionCoverageAsync = (_, _, _, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult<CrossSourceBackfillSessionCoverage?>(null);
            }
        };

        var reconcile = () => service.ReconcileDailyAsync(new("SPY", day, day, "alpaca", ["polygon"]), cancellation.Token);

        await reconcile.Should().ThrowAsync<OperationCanceledException>();
        baseline.Calls.Should().Be(0);
        comparison.Calls.Should().Be(0);
    }

    [Fact]
    public async Task MatchingOutOfRangeBars_DoNotCountAsBoundedProviderEvidence()
    {
        var day = new DateOnly(2026, 6, 29);
        var outside = Bar("SPY", day.AddDays(-1), 100m, 100, "fixture");
        var baseline = new FakeHistoricalDataProvider("alpaca", handler: (_, _, _, _) => Task.FromResult<IReadOnlyList<HistoricalBar>>([outside]));
        var comparison = new FakeHistoricalDataProvider("polygon", handler: (_, _, _, _) => Task.FromResult<IReadOnlyList<HistoricalBar>>([outside]));

        var result = await CreateService([baseline, comparison]).ReconcileDailyAsync(new("SPY", day, day, "alpaca", ["polygon"]));

        result.IsSlaClosed.Should().BeFalse();
        result.Discrepancies.Should().Contain(discrepancy => discrepancy.Kind == "SharedMissingSession" && discrepancy.SessionDate == day);
        result.Discrepancies.Should().Contain(discrepancy => discrepancy.Kind == "BaselineUnexpectedSession");
        result.Discrepancies.Should().Contain(discrepancy => discrepancy.Kind == "ComparisonUnexpectedSession");
    }

    [Fact]
    public void LegacyResultWithoutCalendarEvidence_CannotRetainAClosedStatus()
    {
        var day = new DateOnly(2026, 6, 29);
        var legacy = new CrossSourceBackfillReconciliationResult("SPY", day, day, "alpaca", ["polygon"], [], [], DateTimeOffset.UtcNow);

        legacy.IsClean.Should().BeFalse();
        legacy.IsSlaClosed.Should().BeFalse();
        legacy.ClosureDecision.Status.Should().Be(CrossSourceBackfillClosureStatus.BlockedByMissingEvidence);
        legacy.SymbolsRequiringReview.Should().Equal("SPY");
    }

    private static CrossSourceBackfillReconciliationService CreateService(IEnumerable<IHistoricalDataProvider> providers)
        => new(providers)
        {
            // These fixtures explicitly declare US-equity sessions; production resolves listing identity.
            ResolveSessionCoverageAsync = (_, from, to, _) =>
            {
                var schedule = new HistoricalSessionScheduleResolver(null).UsSessions(from, to);
                return Task.FromResult<CrossSourceBackfillSessionCoverage?>(new(schedule.Calendar, from, to, schedule.Sessions));
            }
        };

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
