using FluentAssertions;
using Meridian.Application.Accounting;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ledger;
using Moq;
using Xunit;

namespace Meridian.Tests.Application.Accounting;

/// <summary>
/// Tests the mark-to-market wiring: provider-chain prices → DailyPortfolioPricingProjector →
/// governed AutomatedJournalApproval draft → posted fair-value adjustments on the ledger.
/// </summary>
public sealed class DailyMarkToMarketServiceTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 07, 03, 21, 0, 0, TimeSpan.Zero);

    private static DailyPortfolioPricingPolicy Policy => new(
        fundId: "fund-alpha",
        policyId: "vp-1",
        policyName: "Daily fair value",
        valuationMethod: "market-close",
        approvedBy: "cfo",
        approvedAtUtc: AsOf.AddDays(-30));

    [Fact]
    public async Task PrepareApprovePost_MarksBooksToMarket()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(AsOf.AddDays(-10), "Buy 100 AAPL @ 150",
        [
            (LedgerAccounts.Securities("AAPL"), 15_000m, 0m),
            (LedgerAccounts.Cash, 0m, 15_000m)
        ]);
        ledger.PostLines(AsOf.AddDays(-10), "Buy 50 MSFT @ 200",
        [
            (LedgerAccounts.Securities("MSFT"), 10_000m, 0m),
            (LedgerAccounts.Cash, 0m, 10_000m)
        ]);

        var prices = new MapPriceSource()
            .Add("AAPL", 160m)
            .Add("MSFT", 190m);
        var service = new DailyMarkToMarketService(prices);

        var run = await service.PrepareAsync(new DailyMarkToMarketRequest(
            Policy,
            PeriodId: "2026-07",
            AsOf,
            BaseCurrency: "USD",
            Positions:
            [
                new MarkToMarketPosition("AAPL", Quantity: 100m, CostPrice: 150m),
                new MarkToMarketPosition("MSFT", Quantity: 50m, CostPrice: 200m)
            ],
            Actor: "ops",
            Reason: "daily close marks"));

        run.HasDraft.Should().BeTrue();
        run.UnpricedSymbols.Should().BeEmpty();
        run.Projection!.TotalMarketValue.Should().Be(16_000m + 9_500m);
        run.Projection.NetUnrealizedGainOrLoss.Should().Be(1_000m - 500m);
        run.Approval!.Status.Should().Be(AutomatedJournalApprovalStatus.Submitted);
        run.Approval.Draft.IsBalanced.Should().BeTrue();
        run.Approval.Draft.Event.Kind.Should().Be(AutomatedJournalEventKind.FairValueMarkAdjustment);

        var posted = run.Approval
            .Approve("controller", AsOf, "reviewed against custodian prices", ["evidence:AAPL", "evidence:MSFT"])
            .PostTo(ledger, "controller", AsOf, "posted after approval", ["evidence:AAPL", "evidence:MSFT"]);

        posted.Status.Should().Be(AutomatedJournalApprovalStatus.Posted);

        // The books now carry market values — the substance of a true NAV.
        ledger.GetBalance(LedgerAccounts.Securities("AAPL")).Should().Be(16_000m);
        ledger.GetBalance(LedgerAccounts.Securities("MSFT")).Should().Be(9_500m);
        (ledger.GetBalance(LedgerAccounts.Securities("AAPL")) + ledger.GetBalance(LedgerAccounts.Securities("MSFT")))
            .Should().Be(run.Projection.TotalMarketValue);
    }

    [Fact]
    public async Task PrepareAsync_MissingPrice_SurfacesUnpricedSymbolInsteadOfMarkingAtCost()
    {
        var prices = new MapPriceSource().Add("AAPL", 160m);
        var service = new DailyMarkToMarketService(prices);

        var run = await service.PrepareAsync(new DailyMarkToMarketRequest(
            Policy, "2026-07", AsOf, "USD",
            [
                new MarkToMarketPosition("AAPL", 100m, 150m),
                new MarkToMarketPosition("NOPRICE", 10m, 50m)
            ],
            Actor: "ops", Reason: "daily close marks"));

        run.UnpricedSymbols.Should().Equal("NOPRICE");
        run.HasDraft.Should().BeTrue("priced positions must still produce a draft");
        run.Projection!.Lines.Should().ContainSingle(line => line.Symbol == "AAPL");
    }

    [Fact]
    public async Task PrepareAsync_FlatMarks_ProducesProjectionButNoDraft()
    {
        var prices = new MapPriceSource().Add("AAPL", 150m);
        var service = new DailyMarkToMarketService(prices);

        var run = await service.PrepareAsync(new DailyMarkToMarketRequest(
            Policy, "2026-07", AsOf, "USD",
            [new MarkToMarketPosition("AAPL", 100m, 150m)],
            Actor: "ops", Reason: "daily close marks"));

        run.HasDraft.Should().BeFalse("marks equal to cost create no fair-value movement");
        run.Projection.Should().NotBeNull();
        run.UnpricedSymbols.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_NoPricedPositions_ReturnsRunWithoutProjection()
    {
        var service = new DailyMarkToMarketService(new MapPriceSource());

        var run = await service.PrepareAsync(new DailyMarkToMarketRequest(
            Policy, "2026-07", AsOf, "USD",
            [new MarkToMarketPosition("NOPRICE", 10m, 50m)],
            Actor: "ops", Reason: "daily close marks"));

        run.HasDraft.Should().BeFalse();
        run.Projection.Should().BeNull();
        run.UnpricedSymbols.Should().Equal("NOPRICE");
    }

    [Fact]
    public async Task PrepareAsync_StaleClosingMark_BlocksStrictValuationBatch()
    {
        var prices = new MapPriceSource().Add(
            "AAPL",
            160m,
            DateOnly.FromDateTime(AsOf.UtcDateTime).AddDays(-4),
            DailyPortfolioPriceConfidence.High);
        var service = new DailyMarkToMarketService(prices);

        var run = await service.PrepareAsync(new DailyMarkToMarketRequest(
            Policy,
            "2026-07",
            AsOf,
            "USD",
            [new MarkToMarketPosition("AAPL", 100m, 150m)],
            Actor: "ops",
            Reason: "daily close marks",
            QualityPolicy: new MarkPriceQualityPolicy(
                TimeSpan.FromDays(3),
                DailyPortfolioPriceConfidence.Medium,
                RequireCompleteCoverage: true)));

        run.IsBlocked.Should().BeTrue();
        run.HasDraft.Should().BeFalse();
        var rejection = run.RejectedMarks.Should().ContainSingle().Subject;
        rejection.Reason.Should().Contain("4 days old");
        rejection.EvidenceReference.Should().Be("evidence:AAPL");
    }

    [Fact]
    public async Task PrepareAsync_LowConfidenceClosingMark_BlocksStrictValuationBatch()
    {
        var prices = new MapPriceSource().Add(
            "AAPL",
            160m,
            DateOnly.FromDateTime(AsOf.UtcDateTime),
            DailyPortfolioPriceConfidence.Low);
        var service = new DailyMarkToMarketService(prices);

        var run = await service.PrepareAsync(new DailyMarkToMarketRequest(
            Policy,
            "2026-07",
            AsOf,
            "USD",
            [new MarkToMarketPosition("AAPL", 100m, 150m)],
            Actor: "ops",
            Reason: "daily close marks",
            QualityPolicy: new MarkPriceQualityPolicy(
                TimeSpan.FromDays(3),
                DailyPortfolioPriceConfidence.Medium,
                RequireCompleteCoverage: true)));

        run.IsBlocked.Should().BeTrue();
        run.HasDraft.Should().BeFalse();
        var rejection = run.RejectedMarks.Should().ContainSingle().Subject;
        rejection.Reason.Should().Contain("below required Medium");
        rejection.EvidenceReference.Should().Be("evidence:AAPL");
    }

    [Fact]
    public async Task HistoricalCloseMarkPriceSource_UsesLatestSessionOnOrBeforeAsOf()
    {
        var friday = new DateOnly(2026, 07, 03);
        var sunday = new DateOnly(2026, 07, 05);
        IReadOnlyList<HistoricalBar> bars =
        [
            new HistoricalBar("AAPL", friday.AddDays(-1), 149m, 151m, 148m, 150.5m, 1_000L, "stooq", 0),
            new HistoricalBar("AAPL", friday, 150m, 152m, 149m, 151.25m, 1_000L, "stooq", 1)
        ];
        var provider = new Mock<IHistoricalDataProvider>();
        provider.Setup(p => p.Name).Returns("composite");
        provider.Setup(p => p.GetDailyBarsAsync("AAPL", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bars);

        var source = new HistoricalCloseMarkPriceSource(provider.Object);

        var quote = await source.GetMarkPriceAsync("AAPL", sunday);

        quote.Should().NotBeNull();
        quote!.Price.Should().Be(151.25m, "the latest session on or before the valuation date wins");
        quote.Source.Should().Be("stooq");
        quote.EvidenceReference.Should().Contain("2026-07-03");
        quote.ObservedOn.Should().Be(friday);
        quote.Confidence.Should().Be(DailyPortfolioPriceConfidence.Medium);
    }

    [Fact]
    public async Task HistoricalCloseMarkPriceSource_ProviderFailure_ReturnsNullInsteadOfThrowing()
    {
        var provider = new Mock<IHistoricalDataProvider>();
        provider.Setup(p => p.Name).Returns("composite");
        provider.Setup(p => p.GetDailyBarsAsync("AAPL", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AggregateException("All providers failed for AAPL"));

        var source = new HistoricalCloseMarkPriceSource(provider.Object);

        var quote = await source.GetMarkPriceAsync("AAPL", new DateOnly(2026, 07, 03));

        quote.Should().BeNull("an unpriced position must surface as a gap, not an exception");
    }

    private sealed class MapPriceSource : IMarkPriceSource
    {
        private readonly Dictionary<string, MarkPriceQuote> _quotes = new(StringComparer.OrdinalIgnoreCase);

        public MapPriceSource Add(
            string symbol,
            decimal price,
            DateOnly? observedOn = null,
            DailyPortfolioPriceConfidence confidence = DailyPortfolioPriceConfidence.High)
        {
            _quotes[symbol] = new MarkPriceQuote(
                price,
                "test-source",
                $"evidence:{symbol}",
                observedOn ?? DateOnly.FromDateTime(AsOf.UtcDateTime),
                confidence);
            return this;
        }

        public Task<MarkPriceQuote?> GetMarkPriceAsync(string symbol, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult(_quotes.TryGetValue(symbol, out var quote) ? quote : null);
    }
}
