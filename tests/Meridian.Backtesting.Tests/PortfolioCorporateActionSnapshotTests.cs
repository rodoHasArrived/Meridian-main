using FluentAssertions;
using Meridian.Backtesting.Portfolio;
using System.Text.Json;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Regression coverage for portfolio snapshot direction and corporate-action lot conservation.
/// </summary>
public sealed class PortfolioCorporateActionSnapshotTests
{
    private static readonly DateTimeOffset OpenedAt =
        new(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ShortPosition_ExposesDirectedLotsWithShortUnrealizedPnl()
    {
        var portfolio = new SimulatedPortfolio(
            10_000m,
            new FixedCommissionModel(0m),
            annualMarginRate: 0.05,
            annualShortRebateRate: 0.02);
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SPY",
            -10L,
            100m,
            0m,
            OpenedAt));
        portfolio.UpdateLastPrice("SPY", 80m);

        var position = portfolio.GetCurrentPositions()["SPY"];
        var account = portfolio.GetAccountSnapshots()[BacktestDefaults.DefaultBrokerageAccountId];
        var lot = portfolio.GetOpenLots("SPY").Should().ContainSingle().Subject;

        lot.IsShort.Should().BeTrue();
        lot.UnrealizedPnl(80m).Should().Be(200m);
        position.OpenLots.Should().ContainSingle().Which.IsShort.Should().BeTrue();
        account.OpenLots.Should().ContainSingle().Which.IsShort.Should().BeTrue();
        position.UnrealizedPnl.Should().Be(200m);
    }

    [Fact]
    public void OpenLot_EquivalentProvenanceCollections_PreserveRecordValueSemantics()
    {
        var lotId = Guid.NewGuid();
        var openFillId = Guid.NewGuid();
        var sourceLotId = Guid.NewGuid();
        var sourceFillId = Guid.NewGuid();
        var components = new[]
        {
            new OpenLotBasisComponent(
                sourceLotId,
                sourceFillId,
                OpenedAt,
                SuccessorQuantity: 1m,
                AllocatedBasis: 100m)
        };
        var first = new OpenLot(lotId, "XYZ", 1L, 100m, OpenedAt, openFillId)
        {
            BasisComponents = components
        };
        var second = new OpenLot(lotId, "XYZ", 1L, 100m, OpenedAt, openFillId)
        {
            BasisComponents = components.ToList()
        };

        first.Should().Be(second,
            "separately materialized component collections contain the same lot value");
        first.GetHashCode().Should().Be(second.GetHashCode());
        new HashSet<OpenLot> { first }.Should().Contain(second);
    }

    [Fact]
    public void ReverseSplit_FractionalLotsCombineIntoAggregateWholeShareAndConserveBasis()
    {
        var portfolio = new SimulatedPortfolio(
            10_000m,
            new FixedCommissionModel(0m),
            annualMarginRate: 0.05,
            annualShortRebateRate: 0.02);
        var firstFillId = Guid.NewGuid();
        var secondFillId = Guid.NewGuid();
        var firstOpenedAt = OpenedAt.AddYears(-2);
        var secondOpenedAt = OpenedAt.AddMonths(-1);
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), firstFillId, "XYZ", 1L, 100m, 0m, firstOpenedAt));
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), secondFillId, "XYZ", 1L, 200m, 0m, secondOpenedAt));
        var sourceLots = portfolio.GetOpenLots("XYZ").ToArray();
        var originalBasis = sourceLots
            .Sum(static lot => lot.Quantity * lot.EntryPrice);

        portfolio.ApplyAssetEvent(new AssetEvent(
            OpenedAt.AddMonths(1),
            "XYZ",
            AssetEventType.Split,
            PositionFactor: 0.5m,
            ReferencePrice: 300m));

        var position = portfolio.GetCurrentPositions()["XYZ"];
        var successorLot = portfolio.GetOpenLots("XYZ").Should().ContainSingle().Subject;
        position.Quantity.Should().Be(1L);
        successorLot.Quantity.Should().Be(1L);
        successorLot.EntryPrice.Should().Be(300m);
        (successorLot.Quantity * successorLot.EntryPrice).Should().Be(originalBasis);
        position.AverageCostBasis.Should().Be(300m);
        successorLot.LotId.Should().NotBe(sourceLots[0].LotId).And.NotBe(sourceLots[1].LotId);
        successorLot.OpenFillId.Should().NotBe(firstFillId).And.NotBe(secondFillId);
        successorLot.OpenedAt.Should().Be(secondOpenedAt,
            "the summary date must conservatively use the newest component");
        successorLot.BasisComponents.Should().BeEquivalentTo([
            new OpenLotBasisComponent(sourceLots[0].LotId, firstFillId, firstOpenedAt, 0.5m, 100m),
            new OpenLotBasisComponent(sourceLots[1].LotId, secondFillId, secondOpenedAt, 0.5m, 200m)
        ], options => options.WithStrictOrdering());
        successorLot.IsLongTerm(OpenedAt.AddMonths(2)).Should().BeFalse();
        successorLot.IsLongTerm(OpenedAt.AddYears(2)).Should().BeTrue();

        var snapshot = portfolio.TakeSnapshot(OpenedAt.AddMonths(1), new DateOnly(2024, 2, 2));
        var snapshotLot = snapshot.Positions["XYZ"].OpenLots.Should().ContainSingle().Subject;
        snapshotLot.BasisComponents.Should().BeEquivalentTo(successorLot.BasisComponents);
        snapshot.Accounts[BacktestDefaults.DefaultBrokerageAccountId].OpenLots.Should()
            .ContainSingle().Which.BasisComponents.Should().BeEquivalentTo(successorLot.BasisComponents);

        var serialized = JsonSerializer.Serialize(snapshotLot);
        var roundTripped = JsonSerializer.Deserialize<OpenLot>(serialized);
        roundTripped.Should().NotBeNull();
        roundTripped!.BasisComponents.Should().BeEquivalentTo(
            successorLot.BasisComponents,
            options => options.WithStrictOrdering());
        roundTripped.Should().Be(snapshotLot,
            "record value equality must survive independent JSON materialization");

        var callerOwnedComponents = successorLot.BasisComponents.ToList();
        var copiedLot = successorLot with { BasisComponents = callerOwnedComponents };
        callerOwnedComponents.Clear();
        copiedLot.BasisComponents.Should().HaveCount(2,
            "the init accessor must copy caller-owned component collections");
        Action mutateSnapshotComponents = () =>
            ((IList<OpenLotBasisComponent>)snapshotLot.BasisComponents).Clear();
        mutateSnapshotComponents.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ChainedSplits_PropagateOriginalCompositeComponentsWithoutIdentityCollapse()
    {
        var portfolio = new SimulatedPortfolio(
            10_000m,
            new FixedCommissionModel(0m),
            annualMarginRate: 0.05,
            annualShortRebateRate: 0.02);
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "XYZ", 1L, 100m, 0m, OpenedAt.AddYears(-2)));
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "XYZ", 1L, 200m, 0m, OpenedAt.AddMonths(-1)));
        var originalLots = portfolio.GetOpenLots("XYZ").ToArray();

        portfolio.ApplyAssetEvent(new AssetEvent(
            OpenedAt,
            "XYZ",
            AssetEventType.Split,
            PositionFactor: 0.5m,
            ReferencePrice: 300m));
        var firstCompositeId = portfolio.GetOpenLots("XYZ").Should().ContainSingle().Subject.LotId;
        portfolio.ApplyAssetEvent(new AssetEvent(
            OpenedAt.AddDays(1),
            "XYZ",
            AssetEventType.Split,
            PositionFactor: 2m,
            ReferencePrice: 150m));
        portfolio.ApplyAssetEvent(new AssetEvent(
            OpenedAt.AddDays(2),
            "XYZ",
            AssetEventType.Split,
            PositionFactor: 0.5m,
            ReferencePrice: 300m));

        var finalLot = portfolio.GetOpenLots("XYZ").Should().ContainSingle().Subject;
        finalLot.LotId.Should().NotBe(firstCompositeId,
            "a later transformation receives an identity derived from its immediate parents");
        finalLot.EntryPrice.Should().Be(300m);
        finalLot.BasisComponents.Select(static component => component.SourceLotId).Should()
            .Equal(originalLots.Select(static lot => lot.LotId));
        finalLot.BasisComponents.Select(static component => component.SuccessorQuantity).Should()
            .Equal(0.5m, 0.5m);
        finalLot.BasisComponents.Select(static component => component.AllocatedBasis).Should()
            .Equal(100m, 200m);
    }

    [Fact]
    public void ChainedDirectSplits_PreserveSingleSourceLotAndFillIdentity()
    {
        var portfolio = new SimulatedPortfolio(
            10_000m,
            new FixedCommissionModel(0m),
            annualMarginRate: 0.05,
            annualShortRebateRate: 0.02);
        var fillId = Guid.NewGuid();
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), fillId, "XYZ", 1L, 100m, 0m, OpenedAt));
        var originalLotId = portfolio.GetOpenLots("XYZ").Should().ContainSingle().Subject.LotId;

        portfolio.ApplyAssetEvent(new AssetEvent(
            OpenedAt.AddDays(1),
            "XYZ",
            AssetEventType.Split,
            PositionFactor: 2m,
            ReferencePrice: 50m));
        portfolio.ApplyAssetEvent(new AssetEvent(
            OpenedAt.AddDays(2),
            "XYZ",
            AssetEventType.Split,
            PositionFactor: 2m,
            ReferencePrice: 25m));

        var successor = portfolio.GetOpenLots("XYZ").Should().ContainSingle().Subject;
        successor.Quantity.Should().Be(4L);
        successor.CostBasis().Should().Be(100m);
        successor.LotId.Should().Be(originalLotId);
        successor.OpenFillId.Should().Be(fillId);
        successor.BasisComponents.Should().ContainSingle().Which.Should().Be(
            new OpenLotBasisComponent(originalLotId, fillId, OpenedAt, 4m, 100m));
    }

    [Fact]
    public void ReverseSplit_AllocatesSuccessorBasisByFifoEntitlementAcrossLotBoundaries()
    {
        var portfolio = new SimulatedPortfolio(
            10_000m,
            new FixedCommissionModel(0m),
            annualMarginRate: 0.05,
            annualShortRebateRate: 0.02);
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "XYZ", 3L, 100m, 0m, OpenedAt));
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "XYZ", 1L, 200m, 0m, OpenedAt.AddMinutes(1)));
        var sourceLots = portfolio.GetOpenLots("XYZ").ToArray();

        portfolio.ApplyAssetEvent(new AssetEvent(
            OpenedAt.AddMonths(1),
            "XYZ",
            AssetEventType.Split,
            PositionFactor: 0.5m,
            ReferencePrice: 300m));

        var successorLots = portfolio.GetOpenLots("XYZ").ToArray();
        successorLots.Should().HaveCount(2);
        successorLots[0].Quantity.Should().Be(1L);
        successorLots[0].EntryPrice.Should().Be(200m);
        successorLots[0].LotId.Should().Be(sourceLots[0].LotId,
            "the first whole entitlement retains its source-lot identity");
        successorLots[1].Quantity.Should().Be(1L);
        successorLots[1].EntryPrice.Should().Be(300m,
            "the successor combines the remaining half-entitlements at their own bases");
        successorLots[1].LotId.Should().NotBe(sourceLots[0].LotId)
            .And.NotBe(sourceLots[1].LotId,
                "a composite successor must not masquerade as either contributor");
        successorLots[1].Notes.Should().Contain(sourceLots[0].LotId.ToString("N"))
            .And.Contain(sourceLots[1].LotId.ToString("N"));
        successorLots.Sum(static lot => lot.Quantity * lot.EntryPrice).Should().Be(500m);

        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "XYZ", -1L, 250m, 0m, OpenedAt.AddMonths(2)));

        portfolio.GetCurrentPositions()["XYZ"].RealizedPnl.Should().Be(50m,
            "FIFO disposal must relieve the $200 first successor, not the $300 composite");
        portfolio.GetOpenLots("XYZ").Should().ContainSingle()
            .Which.EntryPrice.Should().Be(300m);
    }

    [Fact]
    public void AssetEvent_AppliesToEveryBrokerageHolderAndPreservesAccountEvidence()
    {
        var accounts = new[]
        {
            new FinancialAccount("broker-a", "Broker A", FinancialAccountKind.Brokerage, InitialCash: 10_000m),
            new FinancialAccount("broker-b", "Broker B", FinancialAccountKind.Brokerage, InitialCash: 20_000m),
            new FinancialAccount("bank", "Treasury", FinancialAccountKind.Bank, InitialCash: 5_000m),
        };
        var portfolio = new SimulatedPortfolio(accounts, "broker-a", new FixedCommissionModel(0m));
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "SPY", 10L, 100m, 0m, OpenedAt, "broker-a"));
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "SPY", 20L, 100m, 0m, OpenedAt, "broker-b"));
        portfolio.UpdateLastPrice("SPY", 100m);
        var effectiveAt = OpenedAt.AddMonths(1);

        portfolio.ApplyAssetEvent(new AssetEvent(
            effectiveAt,
            "SPY",
            AssetEventType.Split,
            PositionFactor: 2m));
        portfolio.ApplyAssetEvent(new AssetEvent(
            effectiveAt.AddMinutes(1),
            "SPY",
            AssetEventType.Dividend,
            CashPerShare: 1m));

        var snapshot = portfolio.TakeSnapshot(effectiveAt.AddMinutes(2), new DateOnly(2024, 2, 2));
        snapshot.Accounts["broker-a"].Positions["SPY"].Quantity.Should().Be(20L);
        snapshot.Accounts["broker-b"].Positions["SPY"].Quantity.Should().Be(40L);
        snapshot.Accounts["broker-a"].Positions["SPY"].AverageCostBasis.Should().Be(50m);
        snapshot.Accounts["broker-b"].Positions["SPY"].AverageCostBasis.Should().Be(50m);
        snapshot.Accounts["bank"].Positions.Should().BeEmpty();
        portfolio.LastPrices["SPY"].Should().Be(50m);
        snapshot.DayCashFlows.OfType<AssetEventCashFlow>()
            .Where(static flow => flow.EventType == AssetEventType.Dividend)
            .Select(static flow => (flow.AccountId, flow.Amount))
            .Should().BeEquivalentTo([("broker-a", 20m), ("broker-b", 40m)]);
    }

    [Fact]
    public void ReverseSplit_AccountCostFallbacksAreFrozenWithoutPublishingAFalseMarketMark()
    {
        var accounts = new[]
        {
            new FinancialAccount("broker-a", "Broker A", FinancialAccountKind.Brokerage, InitialCash: 10_000m),
            new FinancialAccount("broker-b", "Broker B", FinancialAccountKind.Brokerage, InitialCash: 20_000m)
        };
        var portfolio = new SimulatedPortfolio(accounts, "broker-a", new FixedCommissionModel(0m));
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "XYZ", 3L, 90m, 0m, OpenedAt, "broker-a"));
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "XYZ", 3L, 120m, 0m, OpenedAt, "broker-b"));
        var effectiveAt = OpenedAt.AddMonths(1);

        portfolio.ApplyAssetEvent(new AssetEvent(
            effectiveAt,
            "XYZ",
            AssetEventType.Split,
            PositionFactor: 0.5m));

        var snapshot = portfolio.TakeSnapshot(effectiveAt, new DateOnly(2024, 2, 2));
        snapshot.Accounts["broker-a"].Positions["XYZ"].AverageCostBasis.Should().Be(180m);
        snapshot.Accounts["broker-b"].Positions["XYZ"].AverageCostBasis.Should().Be(240m);
        snapshot.Accounts["broker-a"].Positions["XYZ"].UnrealizedPnl.Should().Be(0m);
        snapshot.Accounts["broker-b"].Positions["XYZ"].UnrealizedPnl.Should().Be(0m);
        snapshot.DayCashFlows.OfType<AssetEventCashFlow>()
            .Select(static flow => (flow.AccountId, flow.Amount))
            .Should().BeEquivalentTo([("broker-a", 90m), ("broker-b", 120m)]);
        portfolio.LastPrices.Should().NotContainKey("XYZ",
            "an account-cost fallback is not an authoritative global market mark");
    }

    [Fact]
    public void ReverseSplit_AllFractionalPositionClearsTheObsoleteSourceMark()
    {
        var portfolio = new SimulatedPortfolio(
            10_000m,
            new FixedCommissionModel(0m),
            annualMarginRate: 0.05,
            annualShortRebateRate: 0.02);
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "XYZ", 1L, 100m, 0m, OpenedAt));
        portfolio.UpdateLastPrice("XYZ", 100m);

        portfolio.ApplyAssetEvent(new AssetEvent(
            OpenedAt.AddMonths(1),
            "XYZ",
            AssetEventType.Split,
            PositionFactor: 0.5m,
            ReferencePrice: 300m));

        portfolio.GetCurrentPositions().Should().NotContainKey("XYZ");
        portfolio.LastPrices.Should().NotContainKey("XYZ",
            "no successor holding remains to carry the post-split mark");
        var snapshot = portfolio.TakeSnapshot(
            OpenedAt.AddMonths(1),
            new DateOnly(2024, 2, 2));
        var disposal = snapshot.DayCashFlows.OfType<AssetEventCashFlow>()
            .Should().ContainSingle().Subject;
        disposal.FractionalUnits.Should().Be(0.5m);
        disposal.BasisDisposed.Should().Be(100m);
        disposal.RealizedPnl.Should().Be(50m,
            "all-fraction disposal evidence must survive even though no position remains");
    }

    [Fact]
    public void ReverseSplit_CashInLieuRelievesFractionalBasisAndRecognizesGain()
    {
        var ledger = new BacktestLedger();
        var portfolio = new SimulatedPortfolio(
            10_000m,
            new FixedCommissionModel(0m),
            annualMarginRate: 0.05,
            annualShortRebateRate: 0.02,
            ledger);
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "XYZ", 3L, 90m, 0m, OpenedAt));

        portfolio.ApplyAssetEvent(new AssetEvent(
            OpenedAt.AddMonths(1),
            "XYZ",
            AssetEventType.Split,
            PositionFactor: 0.5m,
            ReferencePrice: 400m));

        var position = portfolio.GetCurrentPositions()["XYZ"];
        position.Quantity.Should().Be(1L);
        position.AverageCostBasis.Should().Be(180m);
        position.RealizedPnl.Should().Be(110m);
        ledger.GetBalance(LedgerAccounts.Securities(
                "XYZ",
                BacktestDefaults.DefaultBrokerageAccountId))
            .Should().Be(180m);
        ledger.GetBalance(LedgerAccounts.RealizedGainFor(
                BacktestDefaults.DefaultBrokerageAccountId))
            .Should().Be(110m);
    }

    [Fact]
    public void ReverseSplit_ShortCashInLieuRelievesPayableAndRecognizesLoss()
    {
        var ledger = new BacktestLedger();
        var portfolio = new SimulatedPortfolio(
            10_000m,
            new FixedCommissionModel(0m),
            annualMarginRate: 0.05,
            annualShortRebateRate: 0.02,
            ledger);
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "XYZ", -3L, 90m, 0m, OpenedAt));

        portfolio.ApplyAssetEvent(new AssetEvent(
            OpenedAt.AddMonths(1),
            "XYZ",
            AssetEventType.Split,
            PositionFactor: 0.5m,
            ReferencePrice: 400m));

        var position = portfolio.GetCurrentPositions()["XYZ"];
        position.Quantity.Should().Be(-1L);
        position.AverageCostBasis.Should().Be(180m);
        position.RealizedPnl.Should().Be(-110m);
        position.OpenLots.Should().ContainSingle().Which.IsShort.Should().BeTrue();
        ledger.GetBalance(LedgerAccounts.ShortSecuritiesPayable(
                "XYZ",
                BacktestDefaults.DefaultBrokerageAccountId))
            .Should().Be(180m);
        ledger.GetBalance(LedgerAccounts.RealizedLossFor(
                BacktestDefaults.DefaultBrokerageAccountId))
            .Should().Be(110m);
    }

    [Fact]
    public void SymbolChangingAssetEvent_UpdatesSuccessorLotSymbol()
    {
        var portfolio = new SimulatedPortfolio(
            10_000m,
            new FixedCommissionModel(0m),
            annualMarginRate: 0.05,
            annualShortRebateRate: 0.02);
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "OLD", 2L, 100m, 0m, OpenedAt));

        portfolio.ApplyAssetEvent(new AssetEvent(
            OpenedAt.AddMonths(1),
            "OLD",
            AssetEventType.Acquisition,
            PositionFactor: 1m,
            TargetSymbol: "NEW",
            ReferencePrice: 100m));

        portfolio.GetOpenLots("OLD").Should().BeEmpty();
        portfolio.GetOpenLots("NEW").Should().ContainSingle()
            .Which.Symbol.Should().Be("NEW");
    }

    [Fact]
    public void SymbolChangingAssetEvent_OppositeDestinationDirectionFailsBeforeMutation()
    {
        var portfolio = new SimulatedPortfolio(
            10_000m,
            new FixedCommissionModel(0m),
            annualMarginRate: 0.05,
            annualShortRebateRate: 0.02);
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "OLD", 10L, 100m, 0m, OpenedAt));
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "NEW", -5L, 90m, 0m, OpenedAt.AddMinutes(1)));
        var cashBefore = portfolio.Cash;

        Action act = () => portfolio.ApplyAssetEvent(new AssetEvent(
            OpenedAt.AddMonths(1),
            "OLD",
            AssetEventType.Acquisition,
            PositionFactor: 1m,
            TargetSymbol: "NEW",
            ReferencePrice: 100m));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*would combine opposite position directions*");
        portfolio.Cash.Should().Be(cashBefore);
        portfolio.GetCurrentPositions()["OLD"].Quantity.Should().Be(10L);
        portfolio.GetCurrentPositions()["NEW"].Quantity.Should().Be(-5L);
        portfolio.GetOpenLots("OLD").Should().ContainSingle()
            .Which.IsShort.Should().BeFalse();
        portfolio.GetOpenLots("NEW").Should().ContainSingle()
            .Which.IsShort.Should().BeTrue();
    }
}
