using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ReportingSnapshot_ConcurrentJournalAndHistoryCommitPreservesOnePopulation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var (command, lots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false, washSale: true);
        await database.JournalStore.AppendAssetPostingAsync(command, ct);
        var openedPeriod = (await database.JournalStore.GetPeriodAsync(command.Journal.PeriodId, ct))!;
        var capturedPeriod = await database.JournalStore.SavePeriodAsync(openedPeriod with
        {
            Status = "SoftClosed",
            ClosedAt = command.Journal.Entry.Timestamp
        }, openedPeriod.Version, ct: ct);
        var replacement = await database.JournalStore.SaveTaxLotAsync(new LedgerTaxLotRecord(
            Guid.NewGuid(), command.LedgerBookId, lots[0].Account, "later-wash-replacement", new(2026, 5, 15),
            1m, 1m, 80m, "USD", command.Journal.Entry.Timestamp, command.Journal.Entry.Timestamp,
            SecurityId: TestSecurityId, BookPositionId: TestBookPositionId), ct);

        var applicationName = $"reporting-snapshot-{Guid.NewGuid():N}";
        var snapshotStore = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions
        {
            ConnectionString = new NpgsqlConnectionStringBuilder(database.Options.ConnectionString)
            {
                ApplicationName = applicationName
            }.ConnectionString,
            SchemaName = database.Options.SchemaName
        });
        var query = new LedgerJournalEntryQuery(LedgerBookId: command.LedgerBookId,
            EffectiveTo: new DateOnly(2026, 5, 31));

        await using var blocker = new NpgsqlConnection(database.Options.ConnectionString);
        await blocker.OpenAsync(ct);
        await using var blockingTransaction = await blocker.BeginTransactionAsync(ct);
        await using (var hold = blocker.CreateCommand())
        {
            hold.Transaction = blockingTransaction;
            // Pause the actual storage capture at its first history query, after its journals were
            // read. Regular journal posting and wash-sale evidence writes do not touch this table.
            hold.CommandText = $"lock table \"{database.Options.SchemaName}\".tax_lot_mutations in access exclusive mode;";
            await hold.ExecuteNonQueryAsync(ct);
        }

        var captureTask = snapshotStore.CaptureReportingSnapshotAsync(query, capturedPeriod.PeriodId, ct);
        var concurrentJournal = BuildJournalWrite(command.LedgerBookId, command.Journal.PeriodId,
            Guid.NewGuid(), "concurrent-reporting-post", amount: 25m);
        try
        {
            await WaitForReportingHistoryReadAsync(database.Options.ConnectionString, applicationName, ct);
            captureTask.IsCompleted.Should().BeFalse("the first history read must be held after journals were captured");
            await database.JournalStore.SavePeriodAsync(capturedPeriod with { Status = "Open", ClosedAt = null },
                capturedPeriod.Version, ct: ct);
            await database.JournalStore.AppendAsync(concurrentJournal, ct);
            await database.JournalStore.SaveWashSaleDeferralsAsync([new(Guid.NewGuid(), command.LedgerBookId,
                command.MutationBatchId, TestSecurityId, new(2026, 5, 12), lots[0].Account,
                replacement.TaxLotRecordId, replacement.LotId, 20m, 1m, lots[0].AcquiredDate,
                "snapshot-wash-policy", 30, WashSaleReplacementScope.LedgerBook,
                command.Journal.Entry.Timestamp)], ct);
        }
        finally
        {
            await blockingTransaction.RollbackAsync(CancellationToken.None);
        }

        var snapshot = await captureTask;
        snapshot.Period.Should().BeEquivalentTo(capturedPeriod);
        snapshot.Journals.Should().ContainSingle().Which.Entry.JournalEntryId
            .Should().Be(command.Journal.Entry.JournalEntryId);
        var retainedHistory = snapshot.TaxLotDisposalHistory.Should().ContainSingle().Which;
        retainedHistory.JournalEntryId.Should().Be(command.Journal.Entry.JournalEntryId);
        retainedHistory.WashSaleBasisIncreases.Should().BeEmpty("history must use the journal query's earlier snapshot");

        var fresh = await snapshotStore.CaptureReportingSnapshotAsync(query, capturedPeriod.PeriodId, ct);
        fresh.Period!.Status.Should().Be("Open");
        fresh.Period.Version.Should().Be(capturedPeriod.Version + 1);
        fresh.Journals.Select(static record => record.Entry.JournalEntryId).Should().BeEquivalentTo(new[]
        {
            command.Journal.Entry.JournalEntryId, concurrentJournal.Entry.JournalEntryId
        });
        fresh.TaxLotDisposalHistory.Should().ContainSingle().Which.WashSaleBasisIncreases
            .Should().ContainSingle().Which.Amount.Should().Be(20m);
        snapshot.TaxLotDisposalHistory.Single().WashSaleBasisIncreases.Should().BeEmpty();
        snapshot.Period!.Status.Should().Be("SoftClosed");
        snapshot.Period.Version.Should().Be(capturedPeriod.Version);
    }

    private static async Task WaitForReportingHistoryReadAsync(
        string connectionString,
        string applicationName,
        CancellationToken ct)
    {
        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync(ct);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await using var command = observer.CreateCommand();
            command.CommandText = """
                select exists (
                    select 1 from pg_stat_activity
                    where application_name = @application_name
                      and wait_event_type = 'Lock'
                      and wait_event = 'relation'
                      and query like '%tax_lot_mutations%');
                """;
            command.Parameters.AddWithValue("application_name", applicationName);
            if (await command.ExecuteScalarAsync(ct) is true)
            {
                return;
            }

            await Task.Delay(10, ct);
        }
    }
}
