using Meridian.Contracts.SecurityMaster;
using FluentAssertions;
using Meridian.Backtesting.Portfolio;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Execution.Sdk;
using Meridian.Execution.TaxLotAccounting;

namespace Meridian.Backtesting.Tests;

public sealed class CanonicalLotReliefParityTests
{
    private static readonly DateTimeOffset AcquiredAt = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PositionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BookId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData(LotSelectionMethod.Fifo, OpenLotReliefMethod.Fifo)]
    [InlineData(LotSelectionMethod.Lifo, OpenLotReliefMethod.Lifo)]
    [InlineData(LotSelectionMethod.Hifo, OpenLotReliefMethod.Hifo)]
    [InlineData(LotSelectionMethod.SpecificId, OpenLotReliefMethod.SpecificId)]
    public void ActualFillRelief_AgreesWithCanonicalTransactionBasisAndPreservesBothCurrencyFacts(
        LotSelectionMethod simulationMethod, OpenLotReliefMethod canonicalMethod)
    {
        var portfolio = CreatePortfolio(simulationMethod);
        portfolio.ProcessFill(Fill(3, 100m, AcquiredAt));
        portfolio.ProcessFill(Fill(4, 120m, AcquiredAt.AddDays(1)));
        var before = portfolio.GetOpenLots("LOT").ToArray();
        var canonical = before.Select((lot, index) => Retained(lot, index == 0 ? 1.1m : 1.3m)).ToArray();
        var ids = canonicalMethod == OpenLotReliefMethod.SpecificId
            ? new[] { before[1].LotId, before[0].LotId } : null;
        var expected = new OpenLotReliefService().Select(canonical, 5m, canonicalMethod, ids);
        var executionSelector = canonicalMethod switch
        {
            OpenLotReliefMethod.Fifo => TaxLotSelectors.Fifo(),
            OpenLotReliefMethod.Lifo => TaxLotSelectors.Lifo(),
            OpenLotReliefMethod.Hifo => TaxLotSelectors.Hifo(),
            _ => TaxLotSelectors.SpecificId(ids!)
        };
        var execution = executionSelector.Relieve(before.Select(static lot => new TaxLot(
            lot.LotId, lot.Symbol, lot.Quantity, lot.EntryPrice, lot.OpenedAt)
        { UnitKind = lot.UnitKind, Multiplier = lot.Multiplier }).ToArray(), 5, 150m);

        portfolio.ProcessFill(Fill(-5, 150m, AcquiredAt.AddDays(2), ids?[0]));

        var closed = portfolio.GetClosedLots("LOT");
        closed.Select(static lot => lot.LotId).Should().Equal(expected.Selections.Select(static lot => lot.TaxLotRecordId));
        closed.Select(static lot => (decimal)lot.Quantity).Should().Equal(expected.Selections.Select(static lot => lot.Quantity));
        closed.Sum(static lot => lot.Quantity * lot.EntryPrice).Should().Be(expected.TransactionCostBasis);
        closed.Sum(static lot => lot.RealizedPnl).Should().Be(750m - expected.TransactionCostBasis);
        execution.RelievedLots.Select(static lot => lot.Lot.LotId)
            .Should().Equal(expected.Selections.Select(static lot => lot.TaxLotRecordId));
        execution.RelievedLots.Sum(static lot => lot.CostRelieved).Should().Be(expected.TransactionCostBasis);
        execution.TotalRealizedPnl.Should().Be(750m - expected.TransactionCostBasis);
        (portfolio.GetOpenLots("LOT").Sum(static lot => lot.CostBasis()) + expected.TransactionCostBasis)
            .Should().Be(780m);
        expected.FunctionalCostBasis.Should().Be(canonicalMethod == OpenLotReliefMethod.Fifo ? 642m : 734m);
        canonical[0].Acquisition.AcquisitionFxRateToFunctional.Should().Be(1.1m);
        canonical[1].Acquisition.AcquisitionFxRateToFunctional.Should().Be(1.3m);
    }

    [Theory]
    [InlineData(LotSelectionMethod.Fifo)]
    [InlineData(LotSelectionMethod.Lifo)]
    [InlineData(LotSelectionMethod.Hifo)]
    [InlineData(LotSelectionMethod.SpecificId)]
    public void ActualShortCover_PreservesSaleProceedsWithoutClaimingCanonicalShortAuthority(LotSelectionMethod method)
    {
        var portfolio = CreatePortfolio(method);
        portfolio.ProcessFill(Fill(-3, 100m, AcquiredAt));
        portfolio.ProcessFill(Fill(-4, 120m, AcquiredAt.AddDays(1)));
        var before = portfolio.GetOpenLots("LOT");
        var target = method == LotSelectionMethod.SpecificId ? before[1].LotId : (Guid?)null;

        portfolio.ProcessFill(Fill(5, 90m, AcquiredAt.AddDays(2), target));

        var remaining = portfolio.GetOpenLots("LOT");
        var closed = portfolio.GetClosedLots("LOT");
        remaining.Should().OnlyContain(static lot => lot.IsShort && lot.Quantity > 0);
        closed.Should().OnlyContain(static lot => lot.IsShort);
        var relievedBasis = closed.Sum(static lot => lot.Quantity * lot.EntryPrice);
        (remaining.Sum(static lot => lot.CostBasis()) + relievedBasis).Should().Be(780m);
        closed.Sum(static lot => lot.RealizedPnl).Should().Be(relievedBasis - 450m);
        relievedBasis.Should().Be(method == LotSelectionMethod.Fifo ? 540m : 580m);
    }

    private static SimulatedPortfolio CreatePortfolio(LotSelectionMethod method) => new(
        [new FinancialAccount(BacktestDefaults.DefaultBrokerageAccountId, "Lot parity", FinancialAccountKind.Brokerage,
            InitialCash: 10_000m, Rules: new FinancialAccountRules(LotSelection: method))],
        BacktestDefaults.DefaultBrokerageAccountId, new FixedCommissionModel(0m));

    private static FillEvent Fill(long quantity, decimal price, DateTimeOffset at, Guid? target = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "LOT", quantity, price, 0m, at, TargetLotId: target);

    // These explicit test facts model a separately retained acquisition. Production simulation
    // does not manufacture SecurityIds, FX evidence or canonical lots from symbol-based fills.
    private static OpenLotDto Retained(OpenLot lot, decimal fx)
    {
        var date = DateOnly.FromDateTime(lot.OpenedAt.UtcDateTime);
        var evidence = new RetainedEvidenceIdentityDto("acquisition:" + lot.LotId, "evidence://acquisition/" + lot.LotId,
            new string('a', 64), "custodian", "retained-source", "Accepted", "reviewer", lot.OpenedAt, date, 1,
            lot.OpenedAt, "custodian", "OpenLotAcquisition", lot.LotId.ToString("D"));
        return new(lot.LotId, SecurityId, PositionId, BookId, lot.LotId.ToString("D"), date,
            lot.Quantity, lot.Quantity, lot.CostBasis(), lot.CostBasis() * fx, 1,
            new(LotQuantityBasis.Units, "EUR", "USD", fx, lot.CostBasis(), lot.CostBasis() * fx, date, null, [evidence]));
    }
}
