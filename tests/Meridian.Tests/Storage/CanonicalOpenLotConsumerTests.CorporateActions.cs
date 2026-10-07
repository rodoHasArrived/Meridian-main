using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class CanonicalOpenLotConsumerTests
{
    [Theory]
    [InlineData("action")]
    [InlineData("source-action")]
    [InlineData("source-action-missing")]
    [InlineData("type")]
    [InlineData("date")]
    [InlineData("predecessor")]
    [InlineData("version")]
    [InlineData("allocation")]
    [InlineData("role")]
    [InlineData("tags")]
    [InlineData("missing")]
    public void Reporting_AverageCostRejectsChangedSuccessorOriginWithUnchangedEconomics(string fault)
    {
        var lot = DurableLot(1);
        var canonical = CanonicalSuccessorWithOrigin(lot);
        var origin = canonical.Acquisition.CorporateActionLineage!;
        var changed = fault switch
        {
            "action" => origin with { CorporateActionId = Guid.NewGuid() },
            "source-action" => origin with { SourceCorporateActionId = Guid.NewGuid() },
            "source-action-missing" => origin with { SourceCorporateActionId = null },
            "type" => origin with { ActionType = CorporateActionAccountingTypeDto.RegS144AExchange },
            "date" => origin with { EffectiveDate = origin.EffectiveDate.AddDays(1) },
            "predecessor" => origin with { PredecessorTaxLotRecordId = Guid.NewGuid() },
            "version" => origin with { PredecessorVersion = origin.PredecessorVersion + 1 },
            "allocation" => origin with { BasisAllocationPercent = 99m },
            "role" => origin with { Role = CorporateActionSuccessorRoleDto.Acquirer },
            "tags" => origin with { ReportingTags = ["ScheduleD"] },
            _ => null
        };
        var altered = canonical with { Acquisition = canonical.Acquisition with { CorporateActionLineage = changed } };
        var journal = DisposalJournal(lot);
        var history = History(lot, journal, altered) with
        {
            ReliefMethod = LedgerTaxLotReliefMethod.AverageCost,
            PoolLots = [canonical]
        };

        var act = () => CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD");

        act.Should().Throw<LedgerValidationException>().WithMessage("*acquisition*pool snapshot*");
    }

    [Fact]
    public void Reporting_AverageCostCertifiesIndependentlyAllocatedSuccessorOrigin()
    {
        var lot = DurableLot(1);
        var canonical = CanonicalSuccessorWithOrigin(lot);
        var clone = canonical with
        {
            Acquisition = canonical.Acquisition with
            {
                CorporateActionLineage = canonical.Acquisition.CorporateActionLineage! with { ReportingTags = [] },
                Evidence = canonical.Acquisition.Evidence.Select(static evidence => evidence with { }).ToArray()
            }
        };
        var journal = DisposalJournal(lot);
        var history = History(lot, journal, clone) with
        {
            ReliefMethod = LedgerTaxLotReliefMethod.AverageCost,
            PoolLots = [canonical]
        };

        CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD").CostBasis.Should().Be(300m);
    }

    private static OpenLotDto CanonicalSuccessorWithOrigin(LedgerTaxLotRecord lot)
        => lot.ToOpenLot() with
        {
            Acquisition = lot.Acquisition! with
            {
                CorporateActionLineage = new(Guid.NewGuid(), CorporateActionAccountingTypeDto.MergerStock,
                    lot.AcquiredDate.AddDays(1), Guid.NewGuid(), 1, 100m, CorporateActionSuccessorRoleDto.Successor, [])
                { SourceCorporateActionId = Guid.NewGuid() }
            }
        };
}
