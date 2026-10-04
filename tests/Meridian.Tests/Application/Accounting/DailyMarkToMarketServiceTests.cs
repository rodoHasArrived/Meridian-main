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
    private static readonly Guid AaplSecurityId = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid MsftSecurityId = Guid.Parse("b2000000-0000-0000-0000-000000000002");

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
        var carryingValues = new LedgerCarryingValueSource(ledger);
        var service = new DailyMarkToMarketService(prices, carryingValues);

        var run = await service.PrepareAsync(new DailyMarkToMarketRequest(
            Policy,
            PeriodId: "2026-07",
            AsOf,
            BaseCurrency: "USD",
            Positions:
            [
                new MarkToMarketPosition("AAPL", Quantity: 100m, CostPrice: 150m, SecurityId: AaplSecurityId),
                new MarkToMarketPosition("MSFT", Quantity: 50m, CostPrice: 200m, SecurityId: MsftSecurityId)
            ],
            Actor: "ops",
            Reason: "daily close marks"));

        run.HasDraft.Should().BeTrue();
        run.DraftCount.Should().Be(2, "each Security Master/account scope has an unambiguous posting draft");
        run.UnpricedSymbols.Should().BeEmpty();
        run.Projection!.TotalMarketValue.Should().Be(16_000m + 9_500m);
        run.Projection.NetUnrealizedGainOrLoss.Should().Be(1_000m - 500m);
        run.Projection.NetMarkAdjustment.Should().Be(1_000m - 500m);
        run.Approval!.Status.Should().Be(AutomatedJournalApprovalStatus.Submitted);
        run.Approvals.Should().OnlyContain(approval => approval.Draft.IsBalanced);
        run.Approvals.Should().OnlyContain(approval =>
            approval.Draft.Event.Kind == AutomatedJournalEventKind.FairValueMarkAdjustment);
        carryingValues.CallCount.Should().Be(1, "the durable ledger scope is hydrated in one batch");

        var posted = PostApprovals(run.Approvals, ledger, AsOf);
        posted.Should().OnlyContain(approval => approval.Status == AutomatedJournalApprovalStatus.Posted);

        // The books now carry market values — the substance of a true NAV.
        ledger.GetBalance(LedgerAccounts.Securities("AAPL")).Should().Be(16_000m);
        ledger.GetBalance(LedgerAccounts.Securities("MSFT")).Should().Be(9_500m);
        (ledger.GetBalance(LedgerAccounts.Securities("AAPL")) + ledger.GetBalance(LedgerAccounts.Securities("MSFT")))
            .Should().Be(run.Projection.TotalMarketValue);
    }

    [Fact]
    public async Task Scenario_ConsecutiveDailyCloses_PostOnlyIncrementalMarkMovement()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(AsOf.AddDays(-10), "Buy 100 AAPL @ 150",
        [
            (LedgerAccounts.Securities("AAPL", "broker-1"), 15_000m, 0m),
            (LedgerAccounts.CashAccount("broker-1"), 0m, 15_000m)
        ]);
        var prices = new MapPriceSource().Add("AAPL", 160m, new DateOnly(2026, 07, 03));
        var carryingValues = new LedgerCarryingValueSource(ledger);
        var service = new DailyMarkToMarketService(prices, carryingValues);
        var position = new MarkToMarketPosition(
            "AAPL", 100m, 150m, FinancialAccountId: "broker-1", SecurityId: AaplSecurityId);

        var dayOne = await service.PrepareAsync(Request(AsOf, position));
        dayOne.Projection!.Lines.Should().ContainSingle()
            .Which.MarkAdjustment.Should().Be(1_000m);
        PostApprovals(dayOne.Approvals, ledger, AsOf);
        ledger.GetBalance(LedgerAccounts.Securities("AAPL", "broker-1")).Should().Be(16_000m);

        prices.Add("AAPL", 160m, new DateOnly(2026, 07, 04));
        var dayTwo = await service.PrepareAsync(Request(AsOf.AddDays(1), position));
        var unchanged = dayTwo.Projection!.Lines.Should().ContainSingle().Subject;
        unchanged.PriorCarryingValue.Should().Be(16_000m);
        unchanged.HasPriorCarryingValue.Should().BeTrue();
        unchanged.UnrealizedGainOrLoss.Should().Be(1_000m, "cumulative reporting remains versus cost");
        unchanged.MarkAdjustment.Should().Be(0m, "the unchanged close is already carried on the ledger");
        dayTwo.HasDraft.Should().BeFalse();

        prices.Add("AAPL", 165m, new DateOnly(2026, 07, 05));
        var dayThree = await service.PrepareAsync(Request(AsOf.AddDays(2), position));
        var changed = dayThree.Projection!.Lines.Should().ContainSingle().Subject;
        changed.PriorCarryingValue.Should().Be(16_000m);
        changed.UnrealizedGainOrLoss.Should().Be(1_500m);
        changed.MarkAdjustment.Should().Be(500m, "only the movement from the prior carrying value is posted");
        PostApprovals(dayThree.Approvals, ledger, AsOf.AddDays(2));

        ledger.GetBalance(LedgerAccounts.Securities("AAPL", "broker-1")).Should().Be(16_500m);
        carryingValues.CallCount.Should().Be(3, "each run performs exactly one batch hydration");
        ledger.GetJournalEntries(new LedgerQuery(ActivityType: "fair-value-mark"))
            .Should().HaveCount(2, "the unchanged middle day creates no journal");
    }

    [Fact]
    public async Task PrepareAsync_SameDayRetryAndCorrectedMark_UsesStableCorrectionAwareIdentity()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(AsOf.AddDays(-10), "Buy 100 AAPL @ 150",
        [
            (LedgerAccounts.Securities("AAPL", "broker-1"), 15_000m, 0m),
            (LedgerAccounts.CashAccount("broker-1"), 0m, 15_000m)
        ]);
        var prices = new MapPriceSource().Add(
            "AAPL", 160m, new DateOnly(2026, 07, 03), evidenceReference: "price://aapl/revision-1");
        var service = new DailyMarkToMarketService(prices, new LedgerCarryingValueSource(ledger));
        var position = new MarkToMarketPosition(
            "AAPL", 100m, 150m, FinancialAccountId: "broker-1", SecurityId: AaplSecurityId);
        var request = Request(AsOf, position);

        var first = await service.PrepareAsync(request);
        var retry = await service.PrepareAsync(request);

        retry.Approval!.Draft.Metadata.IdempotencyKey
            .Should().Be(first.Approval!.Draft.Metadata.IdempotencyKey,
                "an identical retry must resolve to the same durable idempotency identity");

        PostApprovals(first.Approvals, ledger, AsOf);
        ledger.GetBalance(LedgerAccounts.Securities("AAPL", "broker-1")).Should().Be(16_000m);

        prices.Add(
            "AAPL", 161m, new DateOnly(2026, 07, 03), evidenceReference: "price://aapl/revision-2");
        var corrected = await service.PrepareAsync(request);

        corrected.Approval!.Draft.Metadata.IdempotencyKey
            .Should().NotBe(first.Approval.Draft.Metadata.IdempotencyKey,
                "a corrected same-day mark must not be collapsed into the earlier draft");
        var correctedLine = corrected.Projection!.Lines.Should().ContainSingle().Subject;
        correctedLine.PriorCarryingValue.Should().Be(16_000m,
            "the posted first revision is the durable carrying-value baseline");
        correctedLine.UnrealizedGainOrLoss.Should().Be(1_100m,
            "cumulative unrealized performance remains measured against cost");
        correctedLine.MarkAdjustment.Should().Be(100m,
            "the corrected revision posts only the movement beyond the first posted mark");

        PostApprovals(corrected.Approvals, ledger, AsOf);
        ledger.GetBalance(LedgerAccounts.Securities("AAPL", "broker-1")).Should().Be(16_100m);
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
        run.HasDraft.Should().BeFalse("incomplete valuation coverage cannot support approved numbers");
        run.IsBlocked.Should().BeTrue();
        run.Projection.Should().BeNull();
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
            DailyPortfolioPriceConfidence confidence = DailyPortfolioPriceConfidence.High,
            string? evidenceReference = null)
        {
            _quotes[symbol] = new MarkPriceQuote(
                price,
                "test-source",
                evidenceReference ?? $"evidence:{symbol}",
                observedOn ?? DateOnly.FromDateTime(AsOf.UtcDateTime),
                confidence);
            return this;
        }

        public Task<MarkPriceQuote?> GetMarkPriceAsync(string symbol, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult(_quotes.TryGetValue(symbol, out var quote) ? quote : null);
    }

    private static DailyMarkToMarketRequest Request(
        DateTimeOffset asOf,
        params MarkToMarketPosition[] positions)
        => new(
            Policy,
            PeriodId: "2026-07",
            AsOf: asOf,
            BaseCurrency: "USD",
            Positions: positions,
            Actor: "ops",
            Reason: "daily close marks",
            LedgerBookId: Guid.Parse("c3000000-0000-0000-0000-000000000003"));

    private static IReadOnlyList<AutomatedJournalApproval> PostApprovals(
        IReadOnlyList<AutomatedJournalApproval> approvals,
        Meridian.Ledger.Ledger ledger,
        DateTimeOffset occurredAt)
        => approvals
            .Select(approval =>
            {
                var evidence = approval.Draft.Metadata.EvidenceReferences
                    .Select(static reference => reference.Uri)
                    .ToArray();
                return approval
                    .Approve("controller", occurredAt, "reviewed against custodian prices", evidence)
                    .PostTo(ledger, "controller", occurredAt, "posted after approval", evidence);
            })
            .ToArray();

    private sealed class LedgerCarryingValueSource : IMarkToMarketCarryingValueSource
    {
        private readonly Meridian.Ledger.Ledger _ledger;

        public LedgerCarryingValueSource(Meridian.Ledger.Ledger ledger)
        {
            _ledger = ledger;
        }

        public int CallCount { get; private set; }

        public Task<IReadOnlyDictionary<MarkToMarketCarryingValueKey, MarkToMarketCarryingValue>> GetCarryingValuesAsync(
            MarkToMarketCarryingValueRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            CallCount++;
            IReadOnlyDictionary<MarkToMarketCarryingValueKey, MarkToMarketCarryingValue> result = request.Positions
                .ToDictionary(
                    MarkToMarketCarryingValueKey.FromPosition,
                    position =>
                    {
                        var account = LedgerAccounts.Securities(position.Symbol, position.FinancialAccountId);
                        var existsAsOf = _ledger.GetEntries(account).Any(entry => entry.Timestamp <= request.AsOf);
                        return new MarkToMarketCarryingValue(
                            existsAsOf ? _ledger.GetBalanceAsOf(account, request.AsOf) : null,
                            source: $"ledger:{request.LedgerBookId:D}",
                            capturedAtUtc: request.AsOf,
                            evidenceReference: $"ledger://{request.LedgerBookId:D}/{position.Symbol}");
                    });
            return Task.FromResult(result);
        }
    }
}
