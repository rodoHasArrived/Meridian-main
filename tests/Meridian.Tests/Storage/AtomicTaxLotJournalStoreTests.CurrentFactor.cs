using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

/// <summary>
/// The current paydown factor on the lot of record (V_ledger_052): absent while a lot is at its
/// booked factor, never stamped at acquisition, movable only by a governed decline, and read on
/// every lot path at its own ordinal.
/// </summary>
public sealed partial class AtomicTaxLotJournalStoreTests
{
    [Fact]
    public void CurrentFactorMigration_AddsANullableColumnGuardedByAConstraintAndATrigger()
    {
        var sql = ReadMigration("V_ledger_052__tax_lot_current_factor.sql");

        sql.Should().Contain("add column if not exists current_factor numeric(38, 12) null");
        sql.Should().Contain("ck_tax_lots_current_factor");
        sql.Should().Contain("current_factor > 0 and", "a lot that pays down to nothing is the paydown mutation's representation, not this column's");
        sql.Should().Contain("current_factor < booked_factor", "a current factor at or above the booked factor is a restatement, not a paydown");
        sql.Should().Contain("acquisition_terms is not null and", "only a canonical lot with retained acquisition facts carries a current factor");
        sql.Should().Contain("create or replace function __SCHEMA__.protect_open_lot_current_factor()");
        sql.Should().Contain("before insert or update on __SCHEMA__.tax_lots");

        // Idempotent replay under DriftPolicy.Reapply: every statement guards itself.
        sql.Should().Contain("pg_constraint");
        sql.Should().Contain("pg_trigger");

        // Nothing is backfilled and nothing destructive runs: a legacy lot states no factor rather
        // than inheriting its booked one, and no earlier migration is touched.
        var ddl = StripSqlComments(sql);
        ddl.Should().NotContain("not null default");
        ddl.Should().NotContain("update __SCHEMA__.tax_lots");
        ddl.Should().NotContain("drop ");
        ddl.Should().NotContain("protect_open_lot_acquisition", "the acquisition guard that freezes booked_factor stays as V_ledger_035 wrote it");
    }

    [Fact]
    public void Fingerprint_IsUnchangedForALotThatHoldsNoCurrentFactor()
    {
        // Like the face terms before it, the current factor is omitted when absent so every
        // acquisition batch already retained keeps its digest; a lot that states one is a different
        // command, which is what makes stamping it at acquisition an identity collision.
        var command = BuildAcquisitionCommand(canonicalFacts: false);
        var lot = command.AcquisitionLot!;

        lot.CurrentFactor.Should().BeNull();
        AtomicTaxLotJournalFingerprint.Compute(command).Should().Be(command.CanonicalFingerprint);

        var serialized = System.Text.Json.JsonSerializer.Serialize(
            lot,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        serialized.Should().NotContain("currentFactor");

        var stated = lot with { OriginalFace = 10_000m, BookedFactor = 1m, ParBasis = 100m, CurrentFactor = 0.9m };
        AtomicTaxLotJournalFingerprint.Compute(command with { AcquisitionLot = stated })
            .Should().NotBe(AtomicTaxLotJournalFingerprint.Compute(command with { AcquisitionLot = stated with { CurrentFactor = null } }));
    }

    [Fact]
    public async Task AppendAssetPostingAsync_RejectsAnAcquisitionThatStatesACurrentFactorBeforeOpeningConnection()
    {
        var command = BuildAcquisitionCommand();
        command = (command with
        {
            AcquisitionLot = command.AcquisitionLot! with { OriginalFace = 10_000m, BookedFactor = 1m, ParBasis = 100m, CurrentFactor = 0.9m }
        }).WithComputedFingerprint();
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());

        var act = () => store.AppendAssetPostingAsync(command);

        await act.Should().ThrowAsync<LedgerValidationException>().WithMessage("*opens a lot at its booked factor*");
    }

    [Fact]
    public async Task SaveTaxLotAsync_RejectsALotThatStatesACurrentFactorBeforeOpeningConnection()
    {
        var source = new FaceValueLot("lot-1", TestSecurityId, new DateOnly(2026, 1, 1), 100_000m, 102m, bookedFactor: 0.8m);
        var lot = BuildBareLotOfRecord(TestSecurityId, source).WithFaceValueTerms(source) with { CurrentFactor = 0.7m };
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());

        var act = () => store.SaveTaxLotAsync(lot);

        await act.Should().ThrowAsync<LedgerValidationException>().WithMessage("*opens at its booked factor*");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CurrentFactor_OpensNull_AndOnlyAGovernedDeclineCanMoveIt()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();

        // A canonical face lot with retained acquisition facts: the only kind of lot that may carry
        // a current factor, and the kind whose booked factor V_ledger_035 already freezes.
        var command = await PrepareFactFreeAcquisitionAsync(database, face: true);
        var lot = command.AcquisitionLot!;
        var evidence = command.RetainedEvidence[0] with
        {
            SubjectType = "OpenLotAcquisition",
            SubjectId = lot.TaxLotRecordId.ToString("D"),
            EffectiveDate = lot.AcquiredDate
        };
        var authority = await database.SeedLotReferenceAuthorityAsync(lot.LedgerBookId, lot.SecurityId, lot.BookPositionId);
        command = (command with
        {
            ReferenceVersions = authority.Versions,
            AcquisitionLot = lot with
            {
                Acquisition = new(LotQuantityBasis.Face, "USD", "USD", 1m, 10_000m, 10_000m, lot.AcquiredDate,
                    new(100m, 1m, BondAmortizationMethod.NoAmortization, null), [evidence])
            }
        }).WithComputedFingerprint();
        var acquired = (await database.JournalStore.AppendAssetPostingAsync(command)).MutatedLots.Should().ContainSingle().Subject;
        acquired.CurrentFactor.Should().BeNull("a lot of record opens at its booked factor");
        acquired.ToFaceValueLot()!.CurrentFactor.Should().Be(1m);

        var schema = database.Options.SchemaName;
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();

        // last_mutation_batch_id references the posting-batch table and the trigger demands a batch
        // other than the lot's own, so the accepted decline borrows the batch of a second, genuine
        // acquisition in the same book. Refusals raise in the trigger or the check before the
        // foreign key is consulted, so any id serves them.
        var donor = BuildAcquisitionCommand(lot.LedgerBookId, command.Journal.PeriodId, command.ExpectedPeriodVersion,
            idempotencyKey: "atomic-acquisition:lot-donor") with
        { ReferenceVersions = authority.Versions };
        donor = (donor with { AcquisitionLot = donor.AcquisitionLot! with { LotId = "lot-aapl-20260512-donor" } }).WithComputedFingerprint();
        await database.JournalStore.AppendAssetPostingAsync(donor);

        async Task<string> RefusedAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var refused = connection.CreateCommand();
            refused.CommandText = sql;
            refused.Parameters.AddWithValue("lot", acquired.TaxLotRecordId);
            foreach (var (name, value) in parameters)
                refused.Parameters.AddWithValue(name, value);
            var act = async () => await refused.ExecuteNonQueryAsync();
            return (await act.Should().ThrowAsync<PostgresException>()).Which.Message;
        }

        // Opening a lot with a factor already applied is refused before the row exists.
        (await RefusedAsync($"""
            insert into "{schema}".tax_lots (
                tax_lot_record_id, ledger_book_id, account_name, account_type, lot_id, acquired_date,
                original_quantity, open_quantity, unit_cost, currency, created_at, updated_at, current_factor)
            values (gen_random_uuid(), gen_random_uuid(), 'Investments', 'Asset', 'lot-opened-paid-down',
                date '2026-05-12', 100, 100, 100, 'USD', now(), now(), 0.5);
            """)).Should().Contain("opens at its booked factor");

        // The factor moves only through a versioned, quantity-preserving, new-batch mutation.
        (await RefusedAsync($"""update "{schema}".tax_lots set current_factor = 0.9 where tax_lot_record_id = @lot;"""))
            .Should().Contain("versioned, quantity-preserving mutation batch");
        (await RefusedAsync($"""
            update "{schema}".tax_lots set current_factor = 0.9, version = version + 1
            where tax_lot_record_id = @lot;
            """)).Should().Contain("versioned, quantity-preserving mutation batch", "the batch must change too");
        (await RefusedAsync($"""
            update "{schema}".tax_lots
            set current_factor = 0.9, version = version + 1, last_mutation_batch_id = @batch, open_quantity = open_quantity - 1
            where tax_lot_record_id = @lot;
            """, ("batch", Guid.NewGuid()))).Should().Contain("quantity-preserving");

        // Within a well-formed mutation, the value itself is bounded: below the booked factor, above zero.
        (await RefusedAsync($"""
            update "{schema}".tax_lots set current_factor = 1, version = version + 1, last_mutation_batch_id = @batch
            where tax_lot_record_id = @lot;
            """, ("batch", Guid.NewGuid()))).Should().Contain("ck_tax_lots_current_factor");
        (await RefusedAsync($"""
            update "{schema}".tax_lots set current_factor = 0, version = version + 1, last_mutation_batch_id = @batch
            where tax_lot_record_id = @lot;
            """, ("batch", Guid.NewGuid()))).Should().Contain("ck_tax_lots_current_factor");

        // A governed decline is accepted.
        await using (var decline = connection.CreateCommand())
        {
            decline.CommandText = $"""
                update "{schema}".tax_lots set current_factor = 0.9, version = version + 1, last_mutation_batch_id = @batch
                where tax_lot_record_id = @lot;
                """;
            decline.Parameters.AddWithValue("lot", acquired.TaxLotRecordId);
            decline.Parameters.AddWithValue("batch", donor.MutationBatchId);
            (await decline.ExecuteNonQueryAsync()).Should().Be(1);
        }

        // Every lot read path shares the ordinal reader, so every one of them now sees the factor,
        // and the aggregate rehydrates at the paid-down face rather than the booked one.
        var byAccount = (await database.JournalStore.ListOpenTaxLotsAsync(lot.LedgerBookId, lot.Account))
            .Should().ContainSingle(l => l.TaxLotRecordId == lot.TaxLotRecordId).Subject;
        byAccount.CurrentFactor.Should().Be(0.9m);
        var restated = byAccount.ToFaceValueLot();
        restated.Should().NotBeNull();
        restated!.BookedFactor.Should().Be(1m);
        restated.CurrentFactor.Should().Be(0.9m);
        restated.FaceOutstanding.Should().Be(9_000m);
        (await database.JournalStore.GetTaxLotsByIdsAsync(lot.LedgerBookId, [lot.TaxLotRecordId]))
            .Should().ContainSingle().Which.CurrentFactor.Should().Be(0.9m);
        (await database.JournalStore.GetTaxLotsByLotIdsAsync(lot.LedgerBookId, lot.Account, [lot.LotId]))
            .Should().ContainSingle().Which.CurrentFactor.Should().Be(0.9m);

        // The as-of read restates quantity only and cannot yet date the factor, so it fails closed
        // rather than letting a later factor leak into an earlier effective date.
        var asOf = () => database.JournalStore.ListOpenTaxLotsByAssetScopeAsync(
            lot.LedgerBookId, lot.SecurityId, lot.BookPositionId, lot.AcquiredDate.AddDays(1));
        await asOf.Should().ThrowAsync<LedgerValidationException>().WithMessage("*cannot be established*");

        // Once applied, the factor can only keep declining and can never be removed; the booked
        // factor stays frozen by the acquisition guard.
        (await RefusedAsync($"""
            update "{schema}".tax_lots set current_factor = 0.95, version = version + 1, last_mutation_batch_id = @batch
            where tax_lot_record_id = @lot;
            """, ("batch", Guid.NewGuid()))).Should().Contain("can only decline");
        (await RefusedAsync($"""
            update "{schema}".tax_lots set current_factor = null, version = version + 1, last_mutation_batch_id = @batch
            where tax_lot_record_id = @lot;
            """, ("batch", Guid.NewGuid()))).Should().Contain("cannot be removed");
        (await RefusedAsync($"""
            update "{schema}".tax_lots set booked_factor = 0.9, version = version + 1, last_mutation_batch_id = @batch
            where tax_lot_record_id = @lot;
            """, ("batch", Guid.NewGuid()))).Should().Contain("append-only correction lineage");

        // The ledger set replays scripts whose checksum changed, so the migration must be a no-op
        // the second time against a schema that already carries its objects.
        await using (var replay = connection.CreateCommand())
        {
            replay.CommandText = ReadMigration("V_ledger_052__tax_lot_current_factor.sql")
                .Replace("__SCHEMA__", $"\"{schema}\"", StringComparison.Ordinal);
            await replay.ExecuteNonQueryAsync();
        }

        (await database.JournalStore.GetTaxLotsByIdsAsync(lot.LedgerBookId, [lot.TaxLotRecordId]))
            .Should().ContainSingle().Which.CurrentFactor.Should().Be(0.9m, "a replayed migration changes no data");
    }
}
