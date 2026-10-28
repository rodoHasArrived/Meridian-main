using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed class HistoricalTaxLotQuantityTests
{
    [Theory]
    [InlineData(40)]
    [InlineData(100)]
    public void Project_RetainsEntitlementBeforePartialOrFullLaterDisposal(int sold)
    {
        var (lot, history) = Scenario(sold);
        var projected = HistoricalTaxLotQuantity.Project(lot, history, new(2026, 5, 15));
        projected.OpenQuantity.Should().Be(100m);
        projected.Version.Should().Be(lot.Version);
        projected.OriginalFace.Should().Be(10_000m);
        HistoricalTaxLotQuantity.Project(lot, history, new(2026, 5, 20)).OpenQuantity.Should().Be(100m - sold);
        HistoricalTaxLotQuantity.Project(lot, history, new(2026, 5, 11)).OpenQuantity.Should().Be(0m);
    }

    [Fact]
    public void Project_UsesEconomicDateWhenBackdatedDisposalWasRecordedAfterLaterDisposal()
    {
        var (lot, history) = Scenario(40);
        var third = history[1] with
        {
            MutationBatchId = Guid.NewGuid(),
            Before = 60m,
            Delta = -10m,
            After = 50m,
            ExpectedVersion = 2,
            ResultVersion = 3,
            EffectiveDate = new(2026, 5, 14)
        };
        lot = lot with { OpenQuantity = 50m, Version = 3, LastMutationBatchId = third.MutationBatchId };
        HistoricalTaxLotQuantity.Project(lot, [.. history, third], new(2026, 5, 15)).OpenQuantity.Should().Be(90m);
    }

    [Theory]
    [InlineData("missing-acquisition")]
    [InlineData("missing-disposal")]
    [InlineData("wrong-scope")]
    [InlineData("broken-quantity-chain")]
    [InlineData("duplicate-version")]
    [InlineData("wrong-last-batch")]
    [InlineData("unknown-mutation")]
    [InlineData("disposal-before-acquisition")]
    public void Project_RefusesIncompleteOrInconsistentRetainedHistory(string defect)
    {
        var (lot, history) = Scenario(40);
        switch (defect)
        {
            case "missing-acquisition":
                history = [history[1]];
                break;
            case "missing-disposal":
                history = [history[0]];
                break;
            case "wrong-scope":
                history[1] = history[1] with { BookPositionId = Guid.NewGuid() };
                break;
            case "broken-quantity-chain":
                history[1] = history[1] with { Before = 99m, After = 59m };
                break;
            case "duplicate-version":
                history = [.. history, history[1]];
                break;
            case "wrong-last-batch":
                lot = lot with { LastMutationBatchId = Guid.NewGuid() };
                break;
            case "unknown-mutation":
                history[1] = history[1] with { Kind = (AtomicTaxLotMutationKind)99 };
                break;
            case "disposal-before-acquisition":
                history[1] = history[1] with { EffectiveDate = new(2026, 5, 10) };
                break;
        }
        var act = () => HistoricalTaxLotQuantity.Project(lot, history, new(2026, 5, 15));
        act.Should().Throw<LedgerValidationException>().WithMessage("*complete retained mutation evidence*");
    }

    [Fact]
    public void Project_RefusesLegacyReliefWithoutHistoryButAcceptsPristineAcquisition()
    {
        var (lot, _) = Scenario(40);
        lot = lot with { OriginatingMutationBatchId = null, LastMutationBatchId = null, Version = 1 };
        var act = () => HistoricalTaxLotQuantity.Project(lot, [], new(2026, 5, 15));
        act.Should().Throw<LedgerValidationException>();
        HistoricalTaxLotQuantity.Project(lot with { OpenQuantity = 100m }, [], new(2026, 5, 15))
            .OpenQuantity.Should().Be(100m);
    }

    [Fact]
    public void Project_DatesTheFactor_BeforeAndAfterAPartialPaydown()
    {
        // Paydowns post with a lag, so the factor a lot held as of a date is the latest paydown on
        // or before it, not the factor it holds now. Before the paydown date the lot reads at its
        // booked factor (no current factor stated); on and after it, at the paid-down factor.
        var (lot, history) = Scenario(40);
        var paydown = Guid.NewGuid();
        history =
        [
            .. history,
            new(lot.TaxLotRecordId, paydown, AtomicTaxLotMutationKind.FactorPaydown, 60m, 0m, 60m,
                2, 3, new(2026, 5, 25), lot.SecurityId, lot.BookPositionId, CurrentFactorAfter: 0.7m)
        ];
        lot = lot with { Version = 3, LastMutationBatchId = paydown, CurrentFactor = 0.7m };

        var before = HistoricalTaxLotQuantity.Project(lot, history, new(2026, 5, 24));
        before.OpenQuantity.Should().Be(60m);
        before.CurrentFactor.Should().BeNull("the lot was still at its booked factor on that date");
        before.Version.Should().Be(3, "only quantity and factor are restated");

        var after = HistoricalTaxLotQuantity.Project(lot, history, new(2026, 5, 25));
        after.OpenQuantity.Should().Be(60m);
        after.CurrentFactor.Should().Be(0.7m);
        HistoricalTaxLotQuantity.Project(lot, history, new(2026, 5, 15)).CurrentFactor.Should().BeNull();
    }

    [Fact]
    public void Project_FullPaydownClosesTheLotOnItsEffectiveDate()
    {
        var (lot, history) = Scenario(40);
        var partial = Guid.NewGuid();
        var full = Guid.NewGuid();
        history =
        [
            .. history,
            new(lot.TaxLotRecordId, partial, AtomicTaxLotMutationKind.FactorPaydown, 60m, 0m, 60m,
                2, 3, new(2026, 5, 25), lot.SecurityId, lot.BookPositionId, CurrentFactorAfter: 0.7m),
            new(lot.TaxLotRecordId, full, AtomicTaxLotMutationKind.FactorPaydown, 60m, -60m, 0m,
                3, 4, new(2026, 6, 25), lot.SecurityId, lot.BookPositionId, CurrentFactorAfter: 0m)
        ];
        lot = lot with { OpenQuantity = 0m, Version = 4, LastMutationBatchId = full, CurrentFactor = 0m };

        var open = HistoricalTaxLotQuantity.Project(lot, history, new(2026, 6, 24));
        open.OpenQuantity.Should().Be(60m);
        open.CurrentFactor.Should().Be(0.7m);
        var closed = HistoricalTaxLotQuantity.Project(lot, history, new(2026, 6, 25));
        closed.OpenQuantity.Should().Be(0m, "a full paydown closes the lot on its own date");
        closed.CurrentFactor.Should().Be(0m);
    }

    [Theory]
    [InlineData("factor-rises")]
    [InlineData("factor-unexplained")]
    [InlineData("history-explains-factor-lot-lacks")]
    [InlineData("paydown-dated-before-earlier-paydown")]
    [InlineData("full-paydown-keeps-quantity")]
    [InlineData("partial-paydown-moves-quantity")]
    [InlineData("later-kind-forgets-factor")]
    [InlineData("acquisition-opens-with-factor")]
    public void Project_RefusesAFactorThatRisesOrALotFactorNotExplainedByHistory(string defect)
    {
        var (lot, history) = Scenario(40);
        var paydown = Guid.NewGuid();
        DatedTaxLotQuantityMutation Paydown(decimal factor, decimal before, decimal delta, long version, DateOnly date, Guid batch)
            => new(lot.TaxLotRecordId, batch, AtomicTaxLotMutationKind.FactorPaydown, before, delta, before + delta,
                version - 1, version, date, lot.SecurityId, lot.BookPositionId, CurrentFactorAfter: factor);
        history = [.. history, Paydown(0.7m, 60m, 0m, 3, new(2026, 5, 25), paydown)];
        lot = lot with { Version = 3, LastMutationBatchId = paydown, CurrentFactor = 0.7m };
        switch (defect)
        {
            case "factor-rises":
                var second = Guid.NewGuid();
                history = [.. history, Paydown(0.75m, 60m, 0m, 4, new(2026, 5, 26), second)];
                lot = lot with { Version = 4, LastMutationBatchId = second, CurrentFactor = 0.75m };
                break;
            case "factor-unexplained":
                lot = lot with { CurrentFactor = 0.6m };
                break;
            case "history-explains-factor-lot-lacks":
                lot = lot with { CurrentFactor = null };
                break;
            case "paydown-dated-before-earlier-paydown":
                var backdated = Guid.NewGuid();
                history = [.. history, Paydown(0.6m, 60m, 0m, 4, new(2026, 5, 24), backdated)];
                lot = lot with { Version = 4, LastMutationBatchId = backdated, CurrentFactor = 0.6m };
                break;
            case "full-paydown-keeps-quantity":
                var keeps = Guid.NewGuid();
                history = [.. history, Paydown(0m, 60m, 0m, 4, new(2026, 6, 1), keeps)];
                lot = lot with { Version = 4, LastMutationBatchId = keeps, CurrentFactor = 0m };
                break;
            case "partial-paydown-moves-quantity":
                history[2] = history[2] with { Delta = -10m, After = 50m };
                lot = lot with { OpenQuantity = 50m };
                break;
            case "later-kind-forgets-factor":
                var sale = Guid.NewGuid();
                history = [.. history, new(lot.TaxLotRecordId, sale, AtomicTaxLotMutationKind.Disposal, 60m, -10m, 50m,
                    3, 4, new(2026, 6, 1), lot.SecurityId, lot.BookPositionId, CurrentFactorAfter: null)];
                lot = lot with { OpenQuantity = 50m, Version = 4, LastMutationBatchId = sale };
                break;
            case "acquisition-opens-with-factor":
                history[0] = history[0] with { CurrentFactorAfter = 0.8m };
                break;
        }
        var act = () => HistoricalTaxLotQuantity.Project(lot, history, new(2026, 7, 1));
        act.Should().Throw<LedgerValidationException>().WithMessage("*complete retained mutation evidence*");
    }

    [Fact]
    public void Project_SeedsASuccessorsFactorFromItsOpeningSnapshot()
    {
        // A corporate-action successor born from a paid-down predecessor opens already holding the
        // inherited factor; its own history has no paydown row, so the factor comes from the
        // opening snapshot and is dated on the successor's birth.
        var (lot, _) = Scenario(0);
        var birth = Guid.NewGuid();
        lot = lot with
        {
            Version = 1,
            OriginatingMutationBatchId = birth,
            LastMutationBatchId = birth,
            CurrentFactor = 0.5m,
            Acquisition = new OpenLotAcquisitionDto(LotQuantityBasis.Face, "USD", "USD", 1m, 10_000m, 10_000m, lot.AcquiredDate,
                new(100m, 0.8m, BondAmortizationMethod.NoAmortization, null), [])
            {
                CorporateActionLineage = new OpenLotCorporateActionLineageDto(Guid.NewGuid(), CorporateActionAccountingTypeDto.RegS144AExchange,
                    lot.AcquiredDate, Guid.NewGuid(), 3, 100m, CorporateActionSuccessorRoleDto.Successor, [])
                { SourceCorporateActionId = Guid.NewGuid() }
            }
        };
        DatedTaxLotQuantityMutation[] history =
        [
            new(lot.TaxLotRecordId, birth, AtomicTaxLotMutationKind.CorporateAction, 0m, 100m, 100m,
                0, 1, lot.AcquiredDate, lot.SecurityId, lot.BookPositionId, CurrentFactorAfter: 0.5m)
        ];

        HistoricalTaxLotQuantity.Project(lot, history, lot.AcquiredDate).CurrentFactor.Should().Be(0.5m);
        HistoricalTaxLotQuantity.Project(lot, history, lot.AcquiredDate.AddDays(-1)).OpenQuantity.Should().Be(0m);
        var unexplained = () => HistoricalTaxLotQuantity.Project(lot with { CurrentFactor = 0.4m }, history, lot.AcquiredDate);
        unexplained.Should().Throw<LedgerValidationException>();
    }

    [Fact]
    public void Project_RefusesALegacyLotThatHoldsAFactorWithoutHistory()
    {
        var (lot, _) = Scenario(40);
        lot = lot with { OriginatingMutationBatchId = null, LastMutationBatchId = null, Version = 1, OpenQuantity = 100m };
        HistoricalTaxLotQuantity.Project(lot, [], new(2026, 5, 15)).OpenQuantity.Should().Be(100m);
        var act = () => HistoricalTaxLotQuantity.Project(lot with { CurrentFactor = 0.7m }, [], new(2026, 5, 15));
        act.Should().Throw<LedgerValidationException>().WithMessage("*complete retained mutation evidence*");
    }

    private static (LedgerTaxLotRecord Lot, DatedTaxLotQuantityMutation[] History) Scenario(decimal sold)
    {
        var acquired = Guid.NewGuid();
        var disposed = Guid.NewGuid();
        var lot = new LedgerTaxLotRecord(Guid.NewGuid(), Guid.NewGuid(), LedgerAccounts.Securities("POOL"),
            "pool-lot", new(2026, 5, 12), 100m, 100m - sold, 100m, "USD", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, Version: 2, OriginatingMutationBatchId: acquired, LastMutationBatchId: disposed,
            SecurityId: Guid.NewGuid(), BookPositionId: Guid.NewGuid(), OriginalFace: 10_000m, BookedFactor: 0.8m, ParBasis: 100m);
        return (lot,
        [
            new(lot.TaxLotRecordId, acquired, AtomicTaxLotMutationKind.Acquisition, 0m, 100m, 100m,
                0, 1, lot.AcquiredDate, lot.SecurityId, lot.BookPositionId),
            new(lot.TaxLotRecordId, disposed, AtomicTaxLotMutationKind.Disposal, 100m, -sold, 100m - sold,
                1, 2, new(2026, 5, 20), lot.SecurityId, lot.BookPositionId)
        ]);
    }
}
