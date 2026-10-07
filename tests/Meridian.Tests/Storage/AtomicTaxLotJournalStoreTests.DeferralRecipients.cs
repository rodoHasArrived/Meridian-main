using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalHistory_CertifiesRetainedReplacementRecipientAndOriginalCapacity()
    {
        foreach (var scenario in new[]
                 {
                     "valid", "lower-boundary", "upper-boundary", "closed-recipient", "same-lot-id",
                     "security", "recipient", "book", "before-window", "after-window", "scope", "self", "quantity"
                 })
        {
            await using var database = await LedgerPostgresTestDatabase.CreateAsync();
            var (command, lots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false, washSale: true,
                financialAccountId: "broker-1");
            var original = lots[0];
            var replacementAccount = original.Account with { FinancialAccountId = "broker-2" };
            var journal = command.Journal.Entry;
            var saleDate = new DateOnly(2026, 5, 12);
            var recipientBookId = command.LedgerBookId;
            if (scenario == "book")
            {
                recipientBookId = Guid.NewGuid();
                await database.JournalStore.SaveLedgerBookAsync(new(recipientBookId, "recipient-evidence",
                    Guid.NewGuid(), FundStructureNodeKindDto.Fund, "Other recipient book", "USD",
                    journal.Timestamp, journal.Timestamp));
            }
            var acquired = scenario switch
            {
                "lower-boundary" => saleDate.AddDays(-30),
                "upper-boundary" => saleDate.AddDays(30),
                "before-window" => saleDate.AddDays(-31),
                "after-window" => saleDate.AddDays(31),
                _ => saleDate.AddDays(3)
            };
            var originalQuantity = scenario == "quantity" ? 0.1m : 1m;
            var recipient = scenario == "self" ? original : await database.JournalStore.SaveTaxLotAsync(new(
                Guid.NewGuid(), recipientBookId, replacementAccount,
                scenario == "same-lot-id" ? original.LotId : "retained-recipient", acquired,
                originalQuantity, scenario == "closed-recipient" ? 0m : originalQuantity, 80m, "USD",
                journal.Timestamp, journal.Timestamp,
                SecurityId: scenario == "security" ? Guid.NewGuid() : TestSecurityId,
                BookPositionId: TestBookPositionId));
            command = (command with
            {
                Journal = command.Journal with
                {
                    Entry = new JournalEntry(journal.JournalEntryId, journal.Timestamp, journal.Description,
                        journal.Lines.Select(line => line.Account.Name == LedgerAccounts.RealizedLoss.Name
                            ? new LedgerEntry(line.EntryId, journal.JournalEntryId, journal.Timestamp, replacementAccount,
                                20m, 0m, journal.Description, line.Dimensions, line.Currency)
                            : line).ToArray(), journal.Metadata)
                }
            }).WithComputedFingerprint();
            var posted = await database.JournalStore.AppendAssetPostingAsync(command);
            // These accepted retained writes carry valid source identities and economics. An
            // unrelated/ineligible recipient or impossible replacement capacity cannot settle them.
            await database.JournalStore.SaveWashSaleDeferralsAsync([new(Guid.NewGuid(), command.LedgerBookId,
                command.MutationBatchId, TestSecurityId, saleDate, original.Account,
                recipient.TaxLotRecordId, scenario == "recipient" ? "unrelated-lot" : recipient.LotId,
                20m, 1m, original.AcquiredDate, "tax-policy-v1", scenario == "self" ? 10_000 : 30,
                scenario == "scope" ? WashSaleReplacementScope.DisposingAccount : WashSaleReplacementScope.LedgerBook,
                scenario == "self" ? posted.Mutations[0].RecordedAt.AddSeconds(1) : journal.Timestamp)]);

            var restarted = new PostgresLedgerJournalStore(database.Options);
            var read = () => restarted.GetTaxLotDisposalHistoryAsync(command.LedgerBookId, [journal.JournalEntryId]);
            var result = await new LedgerDisposalTaxReadService(restarted).ReadAsync(command.LedgerBookId,
                command.Journal.PeriodId, posted.Journal.Entry, "USD");
            if (scenario is "valid" or "lower-boundary" or "upper-boundary" or "closed-recipient" or "same-lot-id")
            {
                (await read()).Single().MatchedReplacementQuantity.Should().Be(1m);
                result.EvidenceState.Should().Be("Available", scenario);
                var disposal = result.Disposals.Should().ContainSingle().Which;
                disposal.State.Should().Be("Settled", scenario);
                disposal.EconomicGainOrLoss.Should().Be(-20m);
                disposal.RecognizedGainOrLoss.Should().Be(0m);
            }
            else
            {
                await read.Should().ThrowAsync<LedgerValidationException>().WithMessage(scenario == "quantity"
                    ? "*recipients' original quantity*" : "*deferral recipient*");
                result.EvidenceState.Should().Be("MissingEvidence", scenario);
                result.Disposals.Should().BeEmpty();
            }
        }
    }
}
