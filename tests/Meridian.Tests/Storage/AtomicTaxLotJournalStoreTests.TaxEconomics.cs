using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalHistory_UnquotedDeferralMustReconcileToRetainedJournalEconomics()
    {
        foreach (var retainedDeferred in new[] { 20m, 30m })
        {
            await using var database = await LedgerPostgresTestDatabase.CreateAsync();
            var (command, lots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false, washSale: true);
            var replacementAccount = lots[0].Account with { FinancialAccountId = "broker-2" };
            var timestamp = command.Journal.Entry.Timestamp;
            var replacement = await database.JournalStore.SaveTaxLotAsync(new LedgerTaxLotRecord(
                Guid.NewGuid(), command.LedgerBookId, replacementAccount, "economic-replacement", new(2026, 5, 15),
                1m, 1m, 80m, "USD", timestamp, timestamp, SecurityId: TestSecurityId, BookPositionId: TestBookPositionId));
            var journal = command.Journal.Entry;
            command = (command with
            {
                Journal = command.Journal with
                {
                    Entry = new JournalEntry(journal.JournalEntryId, timestamp, journal.Description,
                        journal.Lines.Select(line => line.Account.Name == LedgerAccounts.RealizedLoss.Name
                            ? new LedgerEntry(line.EntryId, journal.JournalEntryId, timestamp, replacementAccount,
                                20m, 0m, journal.Description, line.Dimensions, line.Currency)
                            : line).ToArray(), journal.Metadata)
                }
            }).WithComputedFingerprint();
            command.DisposalSalePrice.Should().BeNull();
            var posted = await database.JournalStore.AppendAssetPostingAsync(command);
            // The public persistence port accepts the separate positive deferral. Certification
            // must refuse 30: the immutable journal retained cash80 and replacement basis20.
            await database.JournalStore.SaveWashSaleDeferralsAsync([new(Guid.NewGuid(), command.LedgerBookId,
                command.MutationBatchId, TestSecurityId, new(2026, 5, 12), lots[0].Account,
                replacement.TaxLotRecordId, replacement.LotId, retainedDeferred, 1m, lots[0].AcquiredDate,
                "tax-policy-v1", 30, WashSaleReplacementScope.LedgerBook, timestamp)]);

            var restarted = new PostgresLedgerJournalStore(database.Options);
            var history = (await restarted.GetTaxLotDisposalHistoryAsync(command.LedgerBookId,
                [journal.JournalEntryId])).Single();
            var result = LedgerDisposalTaxReadService.Project(history, posted.Journal.Entry,
                command.LedgerBookId, "USD", new(2026, 5, 20));
            if (retainedDeferred == 20m)
            {
                result.State.Should().Be("Settled");
                result.EconomicGainOrLoss.Should().Be(-20m);
                result.RecognizedGainOrLoss.Should().Be(0m);
            }
            else
            {
                result.State.Should().Be("MissingEvidence");
                result.EconomicGainOrLoss.Should().BeNull();
                var project = () => CanonicalDisposalHistoryProjector.Project(history, posted.Journal.Entry,
                    command.LedgerBookId, "USD");
                project.Should().Throw<LedgerValidationException>().WithMessage("*journal cash proceeds and replacement basis*");
            }
        }
    }
}
