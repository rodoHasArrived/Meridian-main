using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task NewUnitAcquisition_RequiresCanonicalFactsBeforeAnyDurableWrite()
        => AssertNewAcquisitionAdmissionAsync(face: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task NewFaceAcquisition_RequiresCanonicalFactsBeforeAnyDurableWrite()
        => AssertNewAcquisitionAdmissionAsync(face: true);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task LegacyUnitAcquisition_ExactReplaySurvivesAdmissionCutoverAndRestart()
        => AssertLegacyAcquisitionReplayAsync(face: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task LegacyFaceAcquisition_ExactReplaySurvivesAdmissionCutoverAndRestart()
        => AssertLegacyAcquisitionReplayAsync(face: true);

    private static async Task AssertNewAcquisitionAdmissionAsync(bool face)
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var command = await PrepareFactFreeAcquisitionAsync(database, face);
        var auditBefore = (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents;

        var post = () => database.JournalStore.AppendAssetPostingAsync(command);

        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*canonical acquisition facts*");
        (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await database.JournalStore.GetByPeriodAsync(command.Journal.PeriodId)).Should().BeEmpty();
        (await database.JournalStore.ListOpenTaxLotsByAssetScopeAsync(command.LedgerBookId,
            TestSecurityId, TestBookPositionId, command.AcquisitionLot!.AcquiredDate)).Should().BeEmpty();
        (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBefore);

        var lot = command.AcquisitionLot!;
        var evidence = command.RetainedEvidence[0] with
        {
            SubjectType = "OpenLotAcquisition",
            SubjectId = lot.TaxLotRecordId.ToString("D"),
            EffectiveDate = lot.AcquiredDate
        };
        var complete = (command with
        {
            AcquisitionLot = lot with
            {
                Acquisition = new(face ? LotQuantityBasis.Face : LotQuantityBasis.Units,
                    "USD", "USD", 1m, 10_000m, 10_000m, lot.AcquiredDate,
                    face ? new(100m, 1m, BondAmortizationMethod.NoAmortization, null) : null, [evidence])
            }
        }).WithComputedFingerprint();
        var authority = await database.SeedLotReferenceAuthorityAsync(lot.LedgerBookId, lot.SecurityId, lot.BookPositionId);
        complete = (complete with { ReferenceVersions = authority.Versions }).WithComputedFingerprint();
        var posted = await database.JournalStore.AppendAssetPostingAsync(complete);
        posted.IsExactReplay.Should().BeFalse("refusal must not reserve the posting identity");
        posted.MutatedLots.Should().ContainSingle().Which.ToOpenLot().OpenFunctionalCostBasis.Should().Be(10_000m);
    }

    private static async Task AssertLegacyAcquisitionReplayAsync(bool face)
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var command = await PrepareFactFreeAcquisitionAsync(database, face);
        await SeedHistoricalAcquisitionReceiptAsync(database, command);
        var retained = (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId))!;
        var period = (await database.JournalStore.GetPeriodAsync(command.Journal.PeriodId))!;
        await database.JournalStore.SavePeriodAsync(period with { Version = period.Version + 1 }, period.Version);
        var auditBefore = (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents;

        var restarted = new PostgresLedgerJournalStore(database.Options);
        var replay = await restarted.AppendAssetPostingAsync(command);

        replay.IsExactReplay.Should().BeTrue();
        replay.Journal.Entry.JournalEntryId.Should().Be(retained.Journal.Entry.JournalEntryId);
        replay.Mutations.Should().ContainSingle().Which.MutationRecordId.Should().Be(retained.Mutations[0].MutationRecordId);
        replay.MutatedLots.Should().ContainSingle().Which.Acquisition.Should().BeNull();
        replay.MutatedLots[0].Version.Should().Be(1);

        var changed = (command with { AcquisitionLot = command.AcquisitionLot! with { UnitCost = 101m } })
            .WithComputedFingerprint();
        var collide = () => restarted.AppendAssetPostingAsync(changed);
        await collide.Should().ThrowAsync<LedgerValidationException>().WithMessage("*identity collision*");
        (await restarted.GetByPeriodAsync(command.Journal.PeriodId)).Should().ContainSingle();
        (await restarted.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBefore);
    }

    private static async Task<AtomicTaxLotJournalCommand> PrepareFactFreeAcquisitionAsync(
        LedgerPostgresTestDatabase database, bool face)
    {
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var openedAt = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        await database.JournalStore.SaveLedgerBookAsync(new(bookId, "acquisition-admission", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Acquisition admission", "USD", openedAt, openedAt));
        var period = await database.JournalStore.SavePeriodAsync(new(periodId, bookId, 2026, 5, "2026-05",
            new(2026, 5, 1), new(2026, 5, 31), "Open", openedAt, null, 0), 0);
        var command = BuildAcquisitionCommand(bookId, periodId, period.Version, canonicalFacts: false);
        if (face)
            command = command with
            {
                AcquisitionLot = command.AcquisitionLot! with
                { OriginalFace = 10_000m, BookedFactor = 1m, ParBasis = 100m }
            };
        return command.WithComputedFingerprint();
    }

    private static async Task SeedHistoricalAcquisitionReceiptAsync(
        LedgerPostgresTestDatabase database, AtomicTaxLotJournalCommand command)
    {
        // Represent a committed pre-cutover receipt directly. Do not add a production escape hatch
        // or disable immutable-row triggers merely to create a historical replay fixture.
        await database.JournalStore.AppendAsync(command.Journal);
        var lot = command.AcquisitionLot!;
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        var schema = database.Options.SchemaName;
        insert.CommandText = $"""
            insert into "{schema}".atomic_tax_lot_posting_batches
                (mutation_batch_id, ledger_book_id, period_id, journal_entry_id, source_event_id,
                 idempotency_key, canonical_fingerprint, expected_period_version, mutation_kind,
                 retained_evidence, created_at, security_id, book_position_id)
            values (@batch, @book, @period, @journal, @event, @key, @fingerprint, @period_version,
                    'Acquisition', @evidence, @recorded, @security, @position);
            insert into "{schema}".tax_lots
                (tax_lot_record_id, ledger_book_id, account_name, account_type, lot_id, acquired_date,
                 original_quantity, open_quantity, unit_cost, currency, source_journal_entry_id,
                 evidence_ref, version, originating_mutation_batch_id, last_mutation_batch_id,
                 created_at, updated_at, security_id, book_position_id, original_face, booked_factor, par_basis)
            values (@lot, @book, @account, @account_type, @lot_name, @acquired, 100, 100, 100, 'USD',
                    @journal, @evidence_id, 1, @batch, @batch, @recorded, @recorded, @security, @position,
                    @face, @factor, @par);
            insert into "{schema}".tax_lot_mutations
                (mutation_record_id, mutation_batch_id, mutation_kind, tax_lot_record_id, lot_id,
                 selection_ordinal, quantity_before, quantity_delta, quantity_after, unit_cost, cost_basis,
                 expected_version, result_version, selection_evidence_id, retained_evidence,
                 journal_entry_id, source_event_id, lot_snapshot_after, recorded_at, security_id, book_position_id)
            values (@mutation, @batch, 'Acquisition', @lot, @lot_name, 0, 0, 100, 100, 100, 10000,
                    0, 1, @evidence_id, @evidence, @journal, @event, @snapshot, @recorded, @security, @position);
            """;
        insert.Parameters.AddWithValue("batch", command.MutationBatchId);
        insert.Parameters.AddWithValue("book", command.LedgerBookId);
        insert.Parameters.AddWithValue("period", command.Journal.PeriodId);
        insert.Parameters.AddWithValue("journal", command.Journal.Entry.JournalEntryId);
        insert.Parameters.AddWithValue("event", command.SourceEventId);
        insert.Parameters.AddWithValue("key", command.IdempotencyKey);
        insert.Parameters.AddWithValue("fingerprint", command.CanonicalFingerprint);
        insert.Parameters.AddWithValue("period_version", command.ExpectedPeriodVersion);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter());
        insert.Parameters.AddWithValue("evidence", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(command.RetainedEvidence, json));
        insert.Parameters.AddWithValue("recorded", lot.CreatedAt.UtcDateTime);
        insert.Parameters.AddWithValue("security", lot.SecurityId);
        insert.Parameters.AddWithValue("position", lot.BookPositionId);
        insert.Parameters.AddWithValue("lot", lot.TaxLotRecordId);
        insert.Parameters.AddWithValue("account", lot.Account.Name);
        insert.Parameters.AddWithValue("account_type", lot.Account.AccountType.ToString());
        insert.Parameters.AddWithValue("lot_name", lot.LotId);
        insert.Parameters.AddWithValue("acquired", lot.AcquiredDate);
        insert.Parameters.AddWithValue("evidence_id", lot.EvidenceRef!);
        insert.Parameters.Add("face", NpgsqlDbType.Numeric).Value = (object?)lot.OriginalFace ?? DBNull.Value;
        insert.Parameters.Add("factor", NpgsqlDbType.Numeric).Value = (object?)lot.BookedFactor ?? DBNull.Value;
        insert.Parameters.Add("par", NpgsqlDbType.Numeric).Value = (object?)lot.ParBasis ?? DBNull.Value;
        insert.Parameters.AddWithValue("mutation", Guid.NewGuid());
        insert.Parameters.AddWithValue("snapshot", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(lot, json));
        await insert.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}
