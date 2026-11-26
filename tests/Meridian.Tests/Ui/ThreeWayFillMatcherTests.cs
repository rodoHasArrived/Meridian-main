using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// Fill-level three-way reconciliation: broker fills pair with posted Meridian fills by activity id,
/// client order id or economics, and every difference gets an explicit classification.
/// </summary>
public sealed class ThreeWayFillMatcherTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 11, 13, 23, 59, 59, TimeSpan.Zero);
    private static readonly DateTimeOffset Executed = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    [Fact]
    public void ExactActivityId_MatchesAndCarriesBrokerAndJournalEvidence()
    {
        var checks = Match([Ledger("f1", activityId: "act-1")], [Broker("act-1")]);

        var check = checks.Should().ContainSingle().Subject;
        check.Status.Should().Be("Matched");
        check.Classification.Should().Be(ThreeWayFillClassification.FillMatched);
        check.CheckId.Should().Be("fill:act-1");
        check.EvidenceIds.Should().BeEquivalentTo(["broker-activity:act-1", "journal-entry:journal-f1"]);
    }

    [Fact]
    public void StreamedFillWithoutActivityId_MatchesByClientOrderId()
    {
        var checks = Match([Ledger("f1", clientOrderId: "client-9")],
            [Broker("act-1", clientOrderId: "client-9", executed: Executed.AddSeconds(2))]);

        checks.Should().ContainSingle().Which.Classification.Should().Be(ThreeWayFillClassification.FillMatched);
    }

    [Fact]
    public void StreamedFillWithNoIds_MatchesOnSymbolSideQuantityPriceAndTime()
    {
        var checks = Match([Ledger("f1")], [Broker("act-1", executed: Executed.AddSeconds(30))]);

        checks.Should().ContainSingle().Which.Status.Should().Be("Matched");
    }

    [Fact]
    public void PartialFillsOfOneOrder_PairOneToOneByQuantity()
    {
        var checks = Match(
            [Ledger("f1", quantity: 4m, clientOrderId: "client-9"), Ledger("f2", quantity: 6m, clientOrderId: "client-9")],
            [Broker("act-6", quantity: 6m, clientOrderId: "client-9"), Broker("act-4", quantity: 4m, clientOrderId: "client-9")]);

        checks.Should().HaveCount(2).And.OnlyContain(check => check.Status == "Matched");
    }

    [Fact]
    public void BrokerFillWithoutPosting_IsMissingFillInLedgerBreak()
    {
        var checks = Match([], [Broker("act-1")]);

        var check = checks.Should().ContainSingle().Subject;
        check.Status.Should().Be("Break");
        check.Classification.Should().Be(ThreeWayFillClassification.MissingFillInLedger);
        check.EvidenceIds.Should().Equal("broker-activity:act-1");
    }

    [Fact]
    public void PostingWithoutBrokerFill_IsMissingFillAtBrokerBreak()
    {
        var checks = Match([Ledger("f1")], []);

        var check = checks.Should().ContainSingle().Subject;
        check.Status.Should().Be("Break");
        check.Classification.Should().Be(ThreeWayFillClassification.MissingFillAtBroker);
        check.CheckId.Should().Be("fill:ledger:f1");
    }

    [Theory]
    [InlineData(ThreeWayFillClassification.QuantityMismatch)]
    [InlineData(ThreeWayFillClassification.PriceMismatch)]
    [InlineData(ThreeWayFillClassification.FeeMismatch)]
    public void MatchedFillWithDifferentEconomics_IsClassifiedBreak(string expected)
    {
        var broker = expected switch
        {
            ThreeWayFillClassification.QuantityMismatch => Broker("act-1", quantity: 9m),
            ThreeWayFillClassification.PriceMismatch => Broker("act-1", price: 100.50m),
            _ => Broker("act-1", commission: 1.25m)
        };

        var checks = Match([Ledger("f1", activityId: "act-1", commission: 1.00m)], [broker]);

        var check = checks.Should().ContainSingle().Subject;
        check.Status.Should().Be("Break");
        check.Classification.Should().Be(expected);
    }

    [Fact]
    public void BrokerWithoutReportedCommission_DoesNotRaiseFeeBreak()
    {
        var checks = Match([Ledger("f1", activityId: "act-1", commission: 1.00m)], [Broker("act-1", commission: null)]);

        checks.Should().ContainSingle().Which.Status.Should().Be("Matched");
    }

    [Fact]
    public void PostingAfterBrokerObservation_IsTimingOnlyNotABreak()
    {
        var brokerAsOf = Executed.AddMinutes(-10);
        var checks = ThreeWayFillMatcher.Match(Source("ledger", Cutoff, [Ledger("f1")]),
            Source("broker", brokerAsOf, []), 0.0001m, 0.01m, Window);

        var check = checks.Should().ContainSingle().Subject;
        check.Status.Should().Be(ThreeWayFillMatcher.TimingStatus);
        check.Classification.Should().Be(ThreeWayFillClassification.TimingOnly);
    }

    [Fact]
    public void NeitherSourceCapturedFills_AddsNoFillChecks()
    {
        var checks = ThreeWayFillMatcher.Match(
            Source("ledger", Cutoff, []) with { FillCoverageComplete = false },
            Source("broker", Cutoff, []) with { FillCoverageComplete = false }, 0.0001m, 0.01m, Window);

        checks.Should().BeEmpty();
    }

    [Fact]
    public void OneSourceWithoutFillCoverage_IsNotComparedNeverClean()
    {
        var checks = ThreeWayFillMatcher.Match(Source("ledger", Cutoff, [Ledger("f1")]),
            Source("broker", Cutoff, []) with { FillCoverageComplete = false }, 0.0001m, 0.01m, Window);

        var check = checks.Should().ContainSingle().Subject;
        check.Status.Should().Be(ThreeWayFillMatcher.NotComparedStatus);
        check.Classification.Should().Be(ThreeWayFillClassification.FillCoverageUnavailable);
    }

    [Fact]
    public void BrokerSourceFailingCoverage_DoesNotCompareItsFills()
    {
        var checks = ThreeWayFillMatcher.Match(Source("ledger", Cutoff, [Ledger("f1")]),
            Source("broker", Cutoff, [Broker("act-2")]) with { CoverageFailures = ["stale"] }, 0.0001m, 0.01m, Window);

        checks.Should().ContainSingle().Which.Status.Should().Be(ThreeWayFillMatcher.NotComparedStatus);
    }

    [Fact]
    public void DifferentSymbolOrSide_IsNeverPaired()
    {
        var checks = Match([Ledger("f1")], [Broker("act-1", symbol: "MSFT"), Broker("act-2", side: "Sell")]);

        checks.Should().HaveCount(3);
        checks.Should().NotContain(check => check.Status == "Matched");
    }

    [Fact]
    public void ComparerIncludesFillChecksAlongsideBalanceChecks()
    {
        var position = new ThreeWayPositionDto(Guid.Parse("4b558109-5326-437e-9fd1-c025875ca0e6"), "AAPL", 10m, 1_000m);
        ThreeWaySourceSnapshotDto Snapshot(string name, IReadOnlyList<ThreeWayFillDto> fills) =>
            new(name, Cutoff, "evidence:" + name, 1_000m, [position], ThreeWayReconciliationOptions.SupportedBasisConvention, [])
            {
                Fills = fills,
                FillCoverageComplete = name != "oms"
            };

        var checks = ThreeWayReconciliationComparer.Compare(
            [Snapshot("ledger", []), Snapshot("oms", []), Snapshot("broker", [Broker("act-1")])],
            Cutoff, TimeSpan.FromMinutes(30), Window);

        checks.Should().Contain(check => check.Classification == ThreeWayFillClassification.MissingFillInLedger);
        checks.Where(check => check.Measure != "fill").Should().OnlyContain(check => check.Status == "Matched");
    }

    private static IReadOnlyList<ThreeWayReconciliationCheckDto> Match(
        IReadOnlyList<ThreeWayFillDto> ledgerFills, IReadOnlyList<ThreeWayFillDto> brokerFills)
        => ThreeWayFillMatcher.Match(Source("ledger", Cutoff, ledgerFills), Source("broker", Cutoff, brokerFills),
            0.0001m, 0.01m, Window);

    private static ThreeWaySourceSnapshotDto Source(string name, DateTimeOffset asOf, IReadOnlyList<ThreeWayFillDto> fills)
        => new(name, asOf, "evidence:" + name, null, [], ThreeWayReconciliationOptions.SupportedBasisConvention, [])
        {
            Fills = fills,
            FillCoverageComplete = true
        };

    private static ThreeWayFillDto Ledger(string fillId, decimal quantity = 10m, decimal price = 100m,
        decimal? commission = null, string? activityId = null, string? clientOrderId = null)
        => new("ledger:" + fillId, "AAPL", "Buy", quantity, price, commission, Executed, "evidence:ledger")
        {
            BrokerActivityId = activityId,
            ClientOrderId = clientOrderId,
            JournalEntryIds = ["journal-" + fillId]
        };

    private static ThreeWayFillDto Broker(string activityId, decimal quantity = 10m, decimal price = 100m,
        decimal? commission = null, string? clientOrderId = null, DateTimeOffset? executed = null,
        string symbol = "AAPL", string side = "Buy")
        => new("broker:" + activityId, symbol, side, quantity, price, commission, executed ?? Executed, "evidence:broker")
        {
            BrokerActivityId = activityId,
            BrokerOrderId = "order-" + activityId,
            ClientOrderId = clientOrderId
        };
}
