using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task ReportingSnapshot_ConcurrentJournalAndHistoryCommitPreservesOnePopulation()
        => AssertReportingSnapshotPreservesOnePopulationAsync(retainExistingDeferral: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task ReportingSnapshot_ConcurrentRecipientCapacityChangePreservesRetainedSupportAndBlocksFreshCapture()
        => AssertReportingSnapshotPreservesOnePopulationAsync(retainExistingDeferral: true);

    private static async Task AssertReportingSnapshotPreservesOnePopulationAsync(bool retainExistingDeferral)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var (command, lots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false, washSale: true);
        var replacement = await database.JournalStore.SaveTaxLotAsync(new LedgerTaxLotRecord(
            Guid.NewGuid(), command.LedgerBookId, lots[0].Account, "later-wash-replacement", new(2026, 5, 15),
            1m, 1m, 80m, "USD", command.Journal.Entry.Timestamp, command.Journal.Entry.Timestamp,
            SecurityId: TestSecurityId, BookPositionId: TestBookPositionId), ct);
        LedgerTaxLotRecord? retainedReplacement = null;
        if (retainExistingDeferral)
        {
            retainedReplacement = await database.JournalStore.SaveTaxLotAsync(replacement with
            {
                TaxLotRecordId = Guid.NewGuid(),
                LotId = "retained-wash-replacement",
                Account = replacement.Account with { FinancialAccountId = "snapshot-deferral-recipient" }
            }, ct);
            var journal = command.Journal.Entry;
            command = (command with
            {
                Journal = command.Journal with
                {
                    Entry = new JournalEntry(journal.JournalEntryId, journal.Timestamp, journal.Description,
                        journal.Lines.Select(line => line.Account.Name == LedgerAccounts.RealizedLoss.Name
                            ? new LedgerEntry(line.EntryId, journal.JournalEntryId, journal.Timestamp,
                                retainedReplacement.Account, 20m, 0m, journal.Description, line.Dimensions, line.Currency)
                            : line).ToArray(), journal.Metadata)
                }
            }).WithComputedFingerprint();
        }
        await database.JournalStore.AppendAssetPostingAsync(command, ct);
        var openedPeriod = (await database.JournalStore.GetPeriodAsync(command.Journal.PeriodId, ct))!;
        var capturedPeriod = await database.JournalStore.SavePeriodAsync(openedPeriod with
        {
            Status = "SoftClosed",
            ClosedAt = command.Journal.Entry.Timestamp
        }, openedPeriod.Version, ct: ct);
        if (retainedReplacement is not null)
        {
            await database.JournalStore.SaveWashSaleDeferralsAsync([new(Guid.NewGuid(), command.LedgerBookId,
                command.MutationBatchId, TestSecurityId, new(2026, 5, 12), lots[0].Account,
                retainedReplacement.TaxLotRecordId, retainedReplacement.LotId, 20m, 1m, lots[0].AcquiredDate,
                command.PolicyRevision!, 30, WashSaleReplacementScope.LedgerBook,
                command.Journal.Entry.Timestamp)], ct);
        }

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
            if (retainedReplacement is null)
            {
                await database.JournalStore.SaveWashSaleDeferralsAsync([new(Guid.NewGuid(), command.LedgerBookId,
                    command.MutationBatchId, TestSecurityId, new(2026, 5, 12), lots[0].Account,
                    replacement.TaxLotRecordId, replacement.LotId, 20m, 1m, lots[0].AcquiredDate,
                    command.PolicyRevision!, 30, WashSaleReplacementScope.LedgerBook,
                    command.Journal.Entry.Timestamp)], ct);
            }
            else
            {
                // Corrupt capacity after the journals were retained. Every recipient and claim
                // certification read must still use the capture's earlier transaction snapshot.
                await using var writer = new NpgsqlConnection(database.Options.ConnectionString);
                await writer.OpenAsync(ct);
                await using var alter = writer.CreateCommand();
                alter.CommandText = $"update \"{database.Options.SchemaName}\".tax_lots set original_quantity = 0.5, open_quantity = 0.5 where tax_lot_record_id = @id;";
                alter.Parameters.AddWithValue("id", retainedReplacement.TaxLotRecordId);
                (await alter.ExecuteNonQueryAsync(ct)).Should().Be(1);
            }
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
        AssertRetainedDeferrals(retainedHistory, retainedReplacement);
        snapshot.Period!.Status.Should().Be("SoftClosed");
        snapshot.Period.Version.Should().Be(capturedPeriod.Version);

        if (retainedReplacement is not null)
        {
            var projection = CanonicalDisposalHistoryProjector.Project(retainedHistory,
                snapshot.Journals.Single().Entry, command.LedgerBookId, "USD");
            projection.Proceeds.Should().Be(80m);
            projection.RecognizedGainOrLoss.Should().Be(0m);
            var freshCapture = () => snapshotStore.CaptureReportingSnapshotAsync(query, capturedPeriod.PeriodId, ct);
            await freshCapture.Should().ThrowAsync<LedgerValidationException>()
                .WithMessage("*matched replacement quantity exceeds*");
            AssertRetainedDeferrals(snapshot.TaxLotDisposalHistory.Single(), retainedReplacement);
            return;
        }

        var fresh = await snapshotStore.CaptureReportingSnapshotAsync(query, capturedPeriod.PeriodId, ct);
        fresh.Period!.Status.Should().Be("Open");
        fresh.Period.Version.Should().Be(capturedPeriod.Version + 1);
        fresh.Journals.Select(static record => record.Entry.JournalEntryId).Should().BeEquivalentTo(new[]
        {
            command.Journal.Entry.JournalEntryId, concurrentJournal.Entry.JournalEntryId
        });
        var freshHistory = fresh.TaxLotDisposalHistory.Should().ContainSingle().Which;
        freshHistory.WashSaleBasisIncreases.Should().ContainSingle().Which.Amount.Should().Be(20m);
        freshHistory.DeferralRecipients.Should().ContainSingle(recipient =>
            recipient.ReplacementTaxLotRecordId == replacement.TaxLotRecordId);
        AssertRetainedDeferrals(snapshot.TaxLotDisposalHistory.Single(), retainedReplacement);
    }

    private static void AssertRetainedDeferrals(LedgerTaxLotDisposalHistoryRecord history,
        LedgerTaxLotRecord? retainedReplacement)
    {
        if (retainedReplacement is null)
        {
            history.WashSaleBasisIncreases.Should().BeEmpty("history must use the journal query's earlier snapshot");
            history.DeferralRecipients.Should().BeNull();
            return;
        }

        history.WashSaleBasisIncreases.Should().ContainSingle().Which.Amount.Should().Be(20m);
        var recipient = history.DeferralRecipients.Should().ContainSingle().Which;
        recipient.ReplacementTaxLotRecordId.Should().Be(retainedReplacement.TaxLotRecordId);
        recipient.ReplacementLotId.Should().Be(retainedReplacement.LotId);
        recipient.DeferredLoss.Should().Be(20m);
        history.MatchedReplacementQuantity.Should().Be(1m);
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
                    select 1 from pg_stat_activity activity
                    join pg_locks waiting on waiting.pid = activity.pid
                    join pg_class relation on relation.oid = waiting.relation
                    where activity.application_name = @application_name
                      and activity.wait_event_type = 'Lock'
                      and waiting.locktype = 'relation'
                      and not waiting.granted
                      and relation.relname = 'tax_lot_mutations');
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
