using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Adapters;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Strategies.Live;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Moq;

namespace Meridian.Tests.Strategies;

public sealed class PaperStrategyAccountingSnapshotTests
{
    private static readonly string Account = Guid.NewGuid().ToString("D");
    private const string RunId = "paper-native-retained";
    private static readonly DateTimeOffset Cutoff = DateTimeOffset.Parse("2026-11-21T20:15:00Z");

    [Theory]
    [InlineData("Shares", 1, 10, 1000, false)]
    [InlineData("Contracts", 100, .5, 625, false)]
    [InlineData("Contracts", 100, -2, 900, true)]
    [InlineData("FaceValue", 1, 100000, 101250, false)]
    [InlineData("Contracts", 50, 3, 0, false)]
    public void CapturedPaperFactsSurviveMetricsAndPersistenceWithoutReconstructingQuotedCost(
        string unit, int multiplier, double quantity, int basis, bool isShort)
    {
        var references = new List<string> { "retained:synthetic-fill" };
        var expected = new StrategyPositionAccountingEvidence("TEST", Account, Guid.NewGuid(), isShort,
            (decimal)quantity, basis, unit, multiplier, "USD", EvidenceReferences: references)
        { StrategyRunId = RunId };
        var source = CreateSource([expected, expected with { FinancialAccountId = "foreign-account", Symbol = "PRIVATE" }]);
        var context = CreateContext(source);

        var captured = context.CapturePositionAccountingEvidence();
        captured.Should().NotBeNull();
        captured!.Positions.Should().ContainSingle().Which.Should().BeEquivalentTo(expected);
        var tracker = new LiveRunMetricsTracker(1000m, Cutoff.AddDays(-1));
        tracker.RecordDayEnd(DateOnly.FromDateTime(Cutoff.UtcDateTime), 1000m, 1000m,
            new Dictionary<string, Position>(), new Dictionary<string, FinancialAccountSnapshot>(), captured);
        references.Add("not-part-of-the-retained-observation");
        var result = tracker.Build("BrokerPaper", new HashSet<string> { "TEST" }, 1000m,
            new Meridian.Ledger.Ledger(), Cutoff);
        var snapshot = JsonSerializer.Deserialize<PortfolioSnapshot>(JsonSerializer.Serialize(result.Snapshots.Single()))!;
        var entry = StrategyRunEntry.Start("synthetic", "Synthetic", RunType.Paper) with
        { RunId = RunId, Metrics = result with { Snapshots = [snapshot] } };

        var summary = new PortfolioReadService().BuildSummary(entry)!;

        summary.AsOf.Should().Be(Cutoff, "the economic population cut travels with the one atomic source capture");
        var retained = summary.AccountingPositions.Should().ContainSingle().Which;
        retained.Quantity.Should().Be((decimal)quantity);
        retained.MonetaryBasis.Should().Be(basis);
        retained.QuantityUnit.Should().Be(unit);
        retained.Multiplier.Should().Be(multiplier);
        retained.SecurityId.Should().Be(expected.SecurityId);
        retained.Currency.Should().Be("USD");
        retained.EvidenceReferences.Should().Equal("retained:synthetic-fill");
        source.As<IPositionAccountingEvidenceSource>().Verify(item => item.CapturePositionAccountingEvidence(), Times.Once);
    }

    [Fact]
    public void MissingAccountPartitionDoesNotBecomeAProvenEmptyPositionPopulation()
    {
        var source = CreateSource([], coveredAccounts: []);
        var context = CreateContext(source);
        context.CapturePositionAccountingEvidence().Should().BeNull();
        var observe = () => context.CapturePortfolioObservation();
        observe.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AForeignCapturedAccountCannotSatisfyTheRunFinancialAccountBinding()
    {
        var foreignAccount = Guid.NewGuid().ToString("D");
        var row = new StrategyPositionAccountingEvidence("TEST", foreignAccount, Guid.NewGuid(), false,
            10, 1000, "Shares", 1, "USD")
        { StrategyRunId = RunId };
        var source = CreateSource([row], [foreignAccount]);
        var context = CreateContext(source);
        context.CapturePositionAccountingEvidence().Should().BeNull();
        Action cash = () => _ = context.Cash;
        Action equity = () => _ = context.PortfolioValue;
        Action positions = () => _ = context.Positions;
        Action accounts = () => _ = context.Accounts;
        Action portfolio = () => _ = context.Portfolio.Cash;
        foreach (var read in new[] { cash, equity, positions, accounts, portfolio })
            read.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OneAtomicObservationScopesCashEquityPositionsAndAccountMetadataTogether()
    {
        var source = new Mock<IPortfolioState>();
        source.SetupGet(item => item.Cash).Returns(999999m);
        source.SetupGet(item => item.PortfolioValue).Returns(999999m);
        var selected = AccountSnapshot(Account);
        var foreign = AccountSnapshot("foreign-account") with { Cash = 999999m, AccountingEquity = 999999m };
        var evidence = new PositionAccountingEvidenceSnapshot(Cutoff, [])
        { CoveredFinancialAccountIds = [Account, "foreign-account"], Accounts = [selected, foreign] };
        source.As<IPositionAccountingEvidenceSource>().SetupSequence(item => item.CapturePositionAccountingEvidence())
            .Returns(evidence).Throws(new InvalidOperationException("An observation must not re-query its native source."));

        var observation = CreateContext(source).CapturePortfolioObservation();

        observation.Cash.Should().Be(150m);
        observation.Equity.Should().Be(810m);
        observation.Positions.Keys.Should().BeEquivalentTo("LONG", "SHORT");
        var account = observation.Accounts.Values.Should().ContainSingle().Which;
        account.AccountId.Should().Be(Account);
        account.Cash.Should().Be(150m);
        account.MarginBalance.Should().Be(-20m);
        account.LongMarketValue.Should().Be(1000m);
        account.ShortMarketValue.Should().Be(-400m);
        account.Equity.Should().Be(810m);
        observation.AccountingEvidence!.Accounts.Should().ContainSingle();
        source.As<IPositionAccountingEvidenceSource>().Verify(item => item.CapturePositionAccountingEvidence(), Times.Once);
        var tracker = new LiveRunMetricsTracker(810m, Cutoff.AddDays(-1));
        tracker.RecordDayEnd(DateOnly.FromDateTime(Cutoff.UtcDateTime), observation.Cash, observation.Equity,
            observation.Positions, observation.Accounts, observation.AccountingEvidence);
        var retained = tracker.Build("BrokerPaper", new HashSet<string> { "LONG", "SHORT" }, observation.Equity,
            new Meridian.Ledger.Ledger(), Cutoff).Snapshots.Should().ContainSingle().Which;
        retained.Cash.Should().Be(150m);
        retained.TotalEquity.Should().Be(810m);
        retained.LongMarketValue.Should().Be(1000m);
        retained.ShortMarketValue.Should().Be(-400m);
        retained.MarginBalance.Should().Be(-20m);
        retained.FunctionalCurrency.Should().Be("USD");
    }

    [Fact]
    public void StrategyCallbacksReceiveTheSelectedAccountAndNeverTheAggregatePortfolio()
    {
        var source = CreateSource([]);
        source.As<IPositionAccountingEvidenceSource>().Setup(item => item.CapturePositionAccountingEvidence())
            .Returns(new PositionAccountingEvidenceSnapshot(Cutoff, [])
            { CoveredFinancialAccountIds = [Account], Accounts = [AccountSnapshot(Account)] });
        source.SetupGet(item => item.Cash).Returns(999999m);
        source.SetupGet(item => item.PortfolioValue).Returns(999999m);
        var context = CreateContext(source);

        context.Cash.Should().Be(150m);
        context.PortfolioValue.Should().Be(810m);
        context.Positions.Keys.Should().BeEquivalentTo("LONG", "SHORT");
        context.Accounts.Keys.Should().Equal(Account);
        context.Portfolio.Cash.Should().Be(150m);
        context.Portfolio.PortfolioValue.Should().Be(810m);
        context.Portfolio.Positions.Keys.Should().BeEquivalentTo("LONG", "SHORT");
    }

    [Fact]
    public void MissingOrMixedRunHistoryDoesNotReuseWholeAccountMoneyAsStrategyBasis()
    {
        var row = new StrategyPositionAccountingEvidence("TEST", Account, Guid.NewGuid(), false, 10, 1000, "Shares", 1, "USD");
        var captured = CreateContext(CreateSource([row])).CapturePositionAccountingEvidence()!;
        var retained = captured.Positions.Should().ContainSingle().Which;
        retained.Quantity.Should().BeNull();
        retained.MonetaryBasis.Should().BeNull();
        retained.CoverageFailure.Should().Contain("strategy-run acquisition history");
    }

    [Fact]
    public void SoleSiblingRunHoldingsAreNotRelabeledAsTheCurrentRun()
    {
        var row = new StrategyPositionAccountingEvidence("TEST", Account, Guid.NewGuid(), false, 10, 1000, "Shares", 1, "USD")
        { StrategyRunId = "sibling-run" };
        CreateContext(CreateSource([row])).CapturePositionAccountingEvidence()!.Positions.Should().BeEmpty();
    }

    [Fact]
    public void LegacySnapshotJsonDoesNotInventNativePaperReceipts()
    {
        var snapshot = JsonSerializer.Deserialize<PortfolioSnapshot>("{\"Positions\":{},\"Accounts\":{},\"DayCashFlows\":[]}")!;
        snapshot.AccountingPositions.Should().BeNull();
        JsonSerializer.Serialize(snapshot).Should().NotContain("AccountingPositions");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisplayLookupCannotRelabelRetainedPaperIdentityWithTheCurrentTickerOwner(bool hasRetainedIdentity)
    {
        Guid? retainedId = hasRetainedIdentity ? Guid.NewGuid() : null;
        var tracker = new LiveRunMetricsTracker(1000m, Cutoff.AddDays(-1));
        tracker.RecordDayEnd(DateOnly.FromDateTime(Cutoff.UtcDateTime), 1000m, 1000m,
            new Dictionary<string, Position>(), new Dictionary<string, FinancialAccountSnapshot>(),
            new PositionAccountingEvidenceSnapshot(Cutoff,
            [new("TEST", Account, retainedId, false, 10, 1000, "Shares", 1, "USD") { StrategyRunId = RunId }]));
        var result = tracker.Build("BrokerPaper", new HashSet<string> { "TEST" }, 1000m, new Meridian.Ledger.Ledger(), Cutoff);
        var entry = StrategyRunEntry.Start("synthetic", "Synthetic", RunType.Paper) with { RunId = RunId, Metrics = result };
        var lookup = new Mock<ISecurityReferenceLookup>();
        lookup.Setup(item => item.GetByCanonicalAsync(It.Is<SecurityReferenceLookupRequest>(request => request.SecurityId == retainedId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkstationSecurityReference(Guid.NewGuid(), "Current ticker owner", "Equity", "USD",
                Meridian.Contracts.SecurityMaster.SecurityStatusDto.Active, "TEST"));

        var summary = await new PortfolioReadService(lookup.Object).BuildSummaryAsync(entry);

        summary!.AccountingPositions.Should().ContainSingle().Which.SecurityId.Should().Be(retainedId);
        summary.SecurityResolvedCount.Should().Be(0);
        summary.SecurityMissingCount.Should().Be(1);
        lookup.Verify(item => item.GetBySymbolAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        lookup.Verify(item => item.GetByCanonicalAsync(It.IsAny<SecurityReferenceLookupRequest>(), It.IsAny<CancellationToken>()),
            hasRetainedIdentity ? Times.Once() : Times.Never());
    }

    private static Mock<IPortfolioState> CreateSource(IReadOnlyList<StrategyPositionAccountingEvidence> positions,
        IReadOnlyList<string>? coveredAccounts = null)
    {
        var source = new Mock<IPortfolioState>();
        source.As<IPositionAccountingEvidenceSource>().Setup(item => item.CapturePositionAccountingEvidence())
            .Returns(new PositionAccountingEvidenceSnapshot(Cutoff, positions)
            { CoveredFinancialAccountIds = coveredAccounts ?? [Account] });
        return source;
    }

    private static LiveStrategyExecutionContext CreateContext(Mock<IPortfolioState> portfolio) => new(
        new Mock<IOrderGateway>().Object, new LiveMarketDataCache(), portfolio.Object,
        new HashSet<string> { "TEST" }, accountingFinancialAccountId: Account, accountingStrategyRunId: RunId);

    private static ExecutionAccountDetailSnapshot AccountSnapshot(string accountId) => new(accountId, "Synthetic account", AccountKind.Brokerage,
        150m, 20m, 1000m, 400m, 1400m, 600m, 0m, 0m,
        [new ExecutionPosition("LONG", 10, 100m, 0m, 0m), new ExecutionPosition("SHORT", -4, 100m, 0m, 0m)],
        Cutoff, BuyingPower: 300m, ShortMarginCollateral: 80m)
    {
        FunctionalCurrency = "USD",
        AccountingEquity = 810m,
        AccountingLongMarketValue = 1000m,
        AccountingShortMarketValue = -400m
    };
}
