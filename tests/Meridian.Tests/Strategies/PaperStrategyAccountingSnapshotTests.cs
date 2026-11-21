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
            (decimal)quantity, basis, unit, multiplier, "USD", EvidenceReferences: references) { StrategyRunId = RunId };
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
        CreateContext(source).CapturePositionAccountingEvidence().Should().BeNull();
    }

    [Fact]
    public void AForeignCapturedAccountCannotSatisfyTheRunFinancialAccountBinding()
    {
        var foreignAccount = Guid.NewGuid().ToString("D");
        var row = new StrategyPositionAccountingEvidence("TEST", foreignAccount, Guid.NewGuid(), false,
            10, 1000, "Shares", 1, "USD") { StrategyRunId = RunId };
        var source = CreateSource([row], [foreignAccount]);
        CreateContext(source).CapturePositionAccountingEvidence().Should().BeNull();
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
}
