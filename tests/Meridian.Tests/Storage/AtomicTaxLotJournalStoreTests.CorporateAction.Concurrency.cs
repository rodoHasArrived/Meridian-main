using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_ConcurrentBackdatedAcquisition_InvalidatesReviewedInventoryAtomically()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var command = fixture.Command();
        var predecessor = (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId,
            [fixture.Instruction.ExpectedLot.TaxLotRecordId])).Single();
        var lotId = Guid.NewGuid();
        var evidence = BuildEvidence("concurrent-acquisition-" + lotId.ToString("N"), 'f') with
        { EffectiveDate = predecessor.AcquiredDate, SubjectType = "OpenLotAcquisition", SubjectId = lotId.ToString("D") };
        var acquired = predecessor with
        {
            TaxLotRecordId = lotId,
            LotId = "concurrent-backdated-lot",
            Version = 1,
            OriginalQuantity = 60m,
            OpenQuantity = 60m,
            OriginalFace = 6_000m,
            UnitCost = 115.5m,
            SourceJournalEntryId = null,
            EvidenceRef = evidence.EvidenceId,
            OriginatingMutationBatchId = null,
            LastMutationBatchId = null,
            BasisAdjustment = null,
            Acquisition = predecessor.Acquisition! with
            { TransactionCostBasis = 6_300m, FunctionalCostBasis = 6_930m, Evidence = [evidence] }
        };

        // Hold the action immediately after its inventory read. The acquisition can then commit
        // against a snapshot with no retained action, creating the reciprocal SSI dependency.
        var barrierKey = Random.Shared.Next(1, int.MaxValue);
        await using var control = new NpgsqlConnection(fixture.Base.Options.ConnectionString);
        await control.OpenAsync();
        await using (var barrier = control.CreateCommand())
        {
            barrier.CommandText = "select pg_advisory_lock(@key::bigint);";
            barrier.Parameters.AddWithValue("key", barrierKey);
            await barrier.ExecuteNonQueryAsync();
        }
        Task<AtomicTaxLotJournalResult>? posting = null;
        try
        {
            await using (var install = control.CreateCommand())
            {
                var schema = fixture.Base.Options.SchemaName;
                install.CommandText = $"""
                    create function "{schema}".pause_corporate_inventory() returns trigger language plpgsql as $$
                    begin
                        if new.mutation_batch_id = '{command.MutationBatchId:D}'::uuid then
                            perform pg_advisory_xact_lock({barrierKey}::bigint);
                        end if;
                        return new;
                    end $$;
                    create trigger pause_corporate_inventory before insert on "{schema}".atomic_tax_lot_posting_batches
                        for each row execute function "{schema}".pause_corporate_inventory();
                    """;
                await install.ExecuteNonQueryAsync();
            }
            posting = fixture.Store.AppendAssetPostingAsync(command);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var waiting = control.CreateCommand();
            waiting.CommandText = """
                select exists(select 1 from pg_locks where locktype = 'advisory'
                    and classid = 0 and objid = @key::oid and objsubid = 1 and not granted);
                """;
            waiting.Parameters.AddWithValue("key", barrierKey);
            while (!(bool)(await waiting.ExecuteScalarAsync(timeout.Token))!)
            {
                if (posting.IsCompleted)
                    await posting; // Surface an unexpected pre-barrier failure immediately.
                await Task.Delay(20, timeout.Token);
            }

            await fixture.Restart().SaveTaxLotAsync(acquired).WaitAsync(TimeSpan.FromSeconds(30));
            await ReleaseBarrierAsync();
            var attempt = async () => await posting.WaitAsync(TimeSpan.FromSeconds(30));
            (await attempt.Should().ThrowAsync<PostgresException>()).Which.SqlState
                .Should().Be(PostgresErrorCodes.SerializationFailure);
            await fixture.AssertUnchangedAsync(command);
            (await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId, [lotId])).Should().ContainSingle();

            var staleRetry = async () => await fixture.Restart().AppendAssetPostingAsync(command);
            await staleRetry.Should().ThrowAsync<LedgerValidationException>();
            await fixture.AssertUnchangedAsync(command);
        }
        finally
        {
            await ReleaseBarrierAsync();
            if (posting is not null)
            {
                try
                { await posting.WaitAsync(TimeSpan.FromSeconds(30)); }
                catch (Exception) { /* Preserve the assertion failure; the fixture removes its schema. */ }
            }
        }

        async Task ReleaseBarrierAsync()
        {
            await using var release = control.CreateCommand();
            release.CommandText = "select pg_advisory_unlock(@key::bigint);";
            release.Parameters.AddWithValue("key", barrierKey);
            await release.ExecuteNonQueryAsync();
        }
    }
}
