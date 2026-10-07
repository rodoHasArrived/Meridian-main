using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalHistory_RetainsOriginalPolicyRevisionAndRecordedTimeAfterPolicyChanges()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, _, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false);
        var posted = await database.JournalStore.AppendAssetPostingAsync(command);
        var originalPolicy = (await database.JournalStore.ListTaxLotPoliciesAsync(command.LedgerBookId)).Single();

        // The configured row is mutable, even when its identity and effective date stay the same.
        // A retained result must not substitute this policy's new revision or activation settings.
        await database.JournalStore.SaveTaxLotPolicyAsync(originalPolicy with
        {
            PolicyId = "tax-policy-v2",
            ReliefMethod = LedgerTaxLotReliefMethod.Lifo,
            WashSalePolicy = new(true, 45, WashSaleReplacementScope.DisposingAccount, new(2026, 5, 1)),
            UpdatedAt = originalPolicy.UpdatedAt.AddDays(15)
        });

        var restarted = new PostgresLedgerJournalStore(database.Options);
        var retained = (await restarted.GetTaxLotDisposalHistoryAsync(command.LedgerBookId,
            [command.Journal.Entry.JournalEntryId])).Single();

        retained.PolicyRevision.Should().Be("tax-policy-v1");
        retained.ReliefMethod.Should().Be(LedgerTaxLotReliefMethod.Fifo);
        retained.RecordedAt.Should().Be(posted.Mutations[0].RecordedAt);
        retained.WashSaleBasisIncreases.Should().BeEmpty("no retained deferral proves any wash-sale policy settings");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalHistory_CertifiesAggregateMatchedQuantityAcrossEveryDeferralRow()
    {
        // A fully deferred loss settles only with a certified matched aggregate. Put the larger
        // value first and last in retained read order so neither arbitrary choice can pass.
        foreach (var (firstMatched, secondMatched) in new[] { (1m, 0.5m), (0.5m, 1m), (1m, 1m) })
        {
            await using var database = await LedgerPostgresTestDatabase.CreateAsync();
            var (command, lots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false, washSale: true);
            var replacementAccount = lots[0].Account with { FinancialAccountId = "broker-2" };
            var timestamp = command.Journal.Entry.Timestamp;
            var replacements = new List<LedgerTaxLotRecord>();
            foreach (var lotId in new[] { "replacement-a", "replacement-z" })
                replacements.Add(await database.JournalStore.SaveTaxLotAsync(new LedgerTaxLotRecord(
                    Guid.NewGuid(), command.LedgerBookId, replacementAccount, lotId, new(2026, 5, 15),
                    0.5m, 0.5m, 80m, "USD", timestamp, timestamp,
                    SecurityId: TestSecurityId, BookPositionId: TestBookPositionId)));

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
            var posted = await database.JournalStore.AppendAssetPostingAsync(command);
            await database.JournalStore.SaveWashSaleDeferralsAsync(replacements.Select((replacement, index) =>
                new WashSaleDeferralRecord(Guid.NewGuid(), command.LedgerBookId, command.MutationBatchId,
                    TestSecurityId, new(2026, 5, 12), lots[0].Account, replacement.TaxLotRecordId,
                    replacement.LotId, 10m, index == 0 ? firstMatched : secondMatched, lots[0].AcquiredDate,
                    "tax-policy-v1", 30, WashSaleReplacementScope.LedgerBook, timestamp)).Reverse().ToArray());

            var restarted = new PostgresLedgerJournalStore(database.Options);
            var read = () => restarted.GetTaxLotDisposalHistoryAsync(command.LedgerBookId,
                [command.Journal.Entry.JournalEntryId]);
            var result = await new LedgerDisposalTaxReadService(restarted).ReadAsync(command.LedgerBookId,
                command.Journal.PeriodId, posted.Journal.Entry, "USD");

            if (firstMatched != secondMatched)
            {
                await read.Should().ThrowAsync<LedgerValidationException>()
                    .WithMessage("*aggregate matched replacement quantity*");
                result.EvidenceState.Should().Be("MissingEvidence");
                result.Disposals.Should().BeEmpty("conflicting aggregate evidence cannot establish a tax result's finality");
            }
            else
            {
                var retained = (await read()).Single();
                retained.MatchedReplacementQuantity.Should().Be(1m, "the common aggregate is repeated, not additive");
                result.EvidenceState.Should().Be("Available");
                result.Disposals.Should().ContainSingle().Which.State.Should().Be("Settled");
            }
        }
    }
}
