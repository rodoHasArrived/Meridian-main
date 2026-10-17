using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 3)]
    public async Task SaveTaxLotAsync_GovernedStoreRejectsStandaloneWritesBeforeOpeningConnection(
        bool canonicalFacts, long version)
    {
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions
        {
            RequireGovernedPostingCommand = true,
            RequireExpectedVersion = true
        });
        var lot = BuildDirectSaveLot(Guid.NewGuid(), canonicalFacts) with { Version = version };

        var save = () => store.SaveTaxLotAsync(lot);

        await save.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*Production tax-lot writes require IAtomicTaxLotJournalStore*");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task SaveTaxLotAsync_GovernedStorePreservesLegacyRowsAndRejectsInsertsAfterRestart()
        => AssertDirectSavesRequireCompatibilityStoreAsync(canonicalFacts: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task SaveTaxLotAsync_GovernedStorePreservesCanonicalRowsAndRejectsInsertsAfterRestart()
        => AssertDirectSavesRequireCompatibilityStoreAsync(canonicalFacts: true);

    private static async Task AssertDirectSavesRequireCompatibilityStoreAsync(bool canonicalFacts)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var bookId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-05-12T14:00:00Z");
        await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(
            bookId, "fund-direct-lot-admission", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Direct lot admission", "USD", now, now), ct);

        // Retained historical rows still load in a compatibility store. Canonical acquisition
        // facts alone do not turn their later standalone update into a governed atomic posting.
        var retained = await database.JournalStore.SaveTaxLotAsync(BuildDirectSaveLot(bookId, canonicalFacts), ct);
        var updated = retained with { OpenQuantity = 90m, UpdatedAt = now.AddMinutes(1) };
        var inserted = BuildDirectSaveLot(bookId, canonicalFacts) with { LotId = "new-direct-lot" };
        var productionOptions = new LedgerJournalStoreOptions
        {
            ConnectionString = database.Options.ConnectionString,
            SchemaName = database.Options.SchemaName,
            RequireGovernedPostingCommand = true,
            RequireExpectedVersion = true
        };

        foreach (var attempt in new[] { updated, inserted })
        {
            var restarted = new PostgresLedgerJournalStore(productionOptions);
            var save = () => restarted.SaveTaxLotAsync(attempt, ct);
            await save.Should().ThrowAsync<LedgerValidationException>()
                .WithMessage("*Production tax-lot writes require IAtomicTaxLotJournalStore*");
        }

        var reader = new PostgresLedgerJournalStore(productionOptions);
        (await reader.GetTaxLotsByIdsAsync(bookId, [retained.TaxLotRecordId, inserted.TaxLotRecordId], ct))
            .Should().ContainSingle().Which.Should().BeEquivalentTo(retained);
        (await reader.GetByAggregateAsync(bookId, ct)).Should().BeEmpty();
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var counts = connection.CreateCommand();
        counts.CommandText = $"""
            select (select count(*) from "{database.Options.SchemaName}".atomic_tax_lot_posting_batches)
                 + (select count(*) from "{database.Options.SchemaName}".tax_lot_mutations);
            """;
        (await counts.ExecuteScalarAsync(ct)).Should().Be(0L);

        var compatibility = new PostgresLedgerJournalStore(database.Options);
        var compatibleUpdate = await compatibility.SaveTaxLotAsync(updated, ct);
        compatibleUpdate.Version.Should().Be(retained.Version + 1);
        compatibleUpdate.OpenQuantity.Should().Be(90m);
        (await compatibility.SaveTaxLotAsync(inserted, ct)).Version.Should().Be(1);
    }

    private static LedgerTaxLotRecord BuildDirectSaveLot(Guid bookId, bool canonicalFacts)
    {
        var lot = BuildAcquisitionCommand(ledgerBookId: bookId, canonicalFacts: canonicalFacts).AcquisitionLot!;
        return lot with
        {
            Version = 0,
            SourceJournalEntryId = null,
            OriginatingMutationBatchId = null,
            LastMutationBatchId = null,
            SecurityId = canonicalFacts ? lot.SecurityId : Guid.Empty,
            BookPositionId = canonicalFacts ? lot.BookPositionId : Guid.Empty
        };
    }
}
