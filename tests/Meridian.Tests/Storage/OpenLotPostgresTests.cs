using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class OpenLotPostgresTests
{
    [LedgerDatabaseFact]
    public async Task AcquisitionFacts_RoundTripRemainImmutableAndSurvivePartialRelief()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var lot = OpenLotConvergenceTests.Lot(1);
        await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(lot.LedgerBookId,
            "fund-alpha", Guid.NewGuid(), FundStructureNodeKindDto.Fund, "Fund book", "USD", lot.CreatedAt, lot.UpdatedAt));
        var saved = await database.JournalStore.SaveTaxLotAsync(lot);
        var rows = await database.JournalStore.ListOpenTaxLotsAsync(lot.LedgerBookId, lot.Account);
        rows.Should().ContainSingle().Which.Acquisition.Should().BeEquivalentTo(lot.Acquisition);
        rows[0].ToOpenLot().OpenFunctionalCostBasis.Should().Be(1100m);

        var reduced = await database.JournalStore.SaveTaxLotAsync(saved with { OpenQuantity = 5m });
        reduced.ToOpenLot().OpenFunctionalCostBasis.Should().Be(550m);
        var changed = reduced with
        {
            Acquisition = reduced.Acquisition! with { AcquisitionFxRateToFunctional = 1.2m, FunctionalCostBasis = 1200m }
        };
        var rewrite = () => database.JournalStore.SaveTaxLotAsync(changed);
        await rewrite.Should().ThrowAsync<PostgresException>().WithMessage("*immutable*");
        var retained = await database.JournalStore.GetTaxLotsByIdsAsync(lot.LedgerBookId, [lot.TaxLotRecordId]);
        retained.Single().Acquisition!.AcquisitionFxRateToFunctional.Should().Be(1.1m);
    }
}
