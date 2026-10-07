using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalHistory_CarriedHoldingPeriodRequiresImmediateSourceAndRecipientEvidence()
    {
        foreach (var scenario in new[] { "valid", "security", "sale-date", "account", "recipient", "window", "scope", "policy" })
        {
            await using var database = await LedgerPostgresTestDatabase.CreateAsync();
            var (source, lots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false, washSale: true);
            var original = lots[0];
            var recipientId = Guid.NewGuid();
            var acquired = new DateOnly(2026, 5, 15);
            var recipientAccount = original.Account with { FinancialAccountId = "carry-recipient" };
            var recipient = await database.JournalStore.SaveTaxLotAsync(original with
            {
                TaxLotRecordId = recipientId,
                LotId = "carry-recipient-lot",
                Account = recipientAccount,
                AcquiredDate = acquired,
                Acquisition = original.Acquisition! with
                {
                    HoldingPeriodStartDate = acquired,
                    Evidence = original.Acquisition.Evidence.Select(proof => proof with
                    {
                        SubjectId = recipientId.ToString("D"),
                        EffectiveDate = acquired
                    }).ToArray()
                }
            });
            var sourceJournal = source.Journal.Entry;
            source = (source with
            {
                Journal = source.Journal with
                {
                    Entry = new JournalEntry(sourceJournal.JournalEntryId, sourceJournal.Timestamp, sourceJournal.Description,
                        sourceJournal.Lines.Select(line => line.Account.Name == LedgerAccounts.RealizedLoss.Name
                            ? new LedgerEntry(line.EntryId, sourceJournal.JournalEntryId, sourceJournal.Timestamp,
                                recipientAccount, 20m, 0m, sourceJournal.Description, line.Dimensions, line.Currency)
                            : line).ToArray(), sourceJournal.Metadata)
                }
            }).WithComputedFingerprint();
            await database.JournalStore.AppendAssetPostingAsync(source);
            var carried = new WashSaleDeferralRecord(Guid.NewGuid(), source.LedgerBookId, source.MutationBatchId,
                TestSecurityId, new(2026, 5, 12), original.Account, recipientId, recipient.LotId,
                20m, 1m, original.AcquiredDate, "tax-policy-v1", 30, WashSaleReplacementScope.LedgerBook,
                sourceJournal.Timestamp);
            carried = scenario switch
            {
                "security" => carried with { SecurityId = Guid.NewGuid() },
                "sale-date" => carried with { SaleDate = carried.SaleDate.AddDays(1) },
                "account" => carried with { DisposalAccount = original.Account with { FinancialAccountId = "foreign-source" } },
                "recipient" => carried with { ReplacementLotId = "unrelated-recipient" },
                "window" => carried with { WindowDays = 1 },
                "scope" => carried with { Scope = WashSaleReplacementScope.DisposingAccount },
                "policy" => carried with { PolicyId = "other-policy-revision" },
                _ => carried
            };
            await database.JournalStore.SaveWashSaleDeferralsAsync([carried]);
            await database.JournalStore.SaveTaxLotPolicyAsync(new(Guid.NewGuid(), source.LedgerBookId, recipientAccount,
                LedgerTaxLotReliefMethod.Fifo, "tax-policy-v1", new(2026, 5, 1), sourceJournal.Timestamp, sourceJournal.Timestamp));
            var targetId = Guid.NewGuid();
            var sourceEvent = Guid.NewGuid();
            var dimensions = sourceJournal.Lines[0].Dimensions;
            var at = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
            LedgerEntry Leg(LedgerAccount account, decimal debit, decimal credit) => new(Guid.NewGuid(), targetId,
                at, account, debit, credit, "Dispose replacement", dimensions,
                new LedgerEntryCurrency("USD", "USD", debit, credit, 1m));
            var journal = new JournalEntry(targetId, at, "Dispose replacement",
            [
                Leg(LedgerAccounts.CashAccount("carry-recipient"), 110m, 0m),
                Leg(recipientAccount, 0m, 100m),
                Leg(LedgerAccounts.RealizedGainFor("carry-recipient"), 0m, 10m)
            ], sourceJournal.Metadata with { EffectiveDate = new(2026, 5, 20), IdempotencyKey = "carry-target" });
            var write = source.Journal with { Entry = journal, SourceEventId = sourceEvent };
            var proof = BuildEvidence("carry-target-disposal", 'd');
            var target = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), source.LedgerBookId, write, sourceEvent,
                "carry-target", source.ExpectedPeriodVersion, AtomicTaxLotMutationKind.Disposal, [proof],
                disposalSelections: [new(recipientId, recipient.LotId, recipient.Version, 1m, 1m, 0,
                    proof.EvidenceId, 100m, 100m)], reliefMethod: "Fifo", policyRevision: "tax-policy-v1");
            var posted = await database.JournalStore.AppendAssetPostingAsync(target);
            var restarted = new PostgresLedgerJournalStore(database.Options);
            var read = () => restarted.GetTaxLotDisposalHistoryAsync(source.LedgerBookId, [targetId]);
            var result = await new LedgerDisposalTaxReadService(restarted).ReadAsync(source.LedgerBookId,
                write.PeriodId, posted.Journal.Entry, "USD");
            if (scenario == "valid")
            {
                (await read()).Single().Lots.Single().HoldingPeriodStart.Should().Be(original.AcquiredDate);
                var parcel = result.Disposals.Should().ContainSingle().Which.Parcels.Should().ContainSingle().Which;
                parcel.HoldingPeriodCarried.Should().BeTrue();
                parcel.Character.Should().Be("LongTerm");
            }
            else
            {
                await read.Should().ThrowAsync<LedgerValidationException>().WithMessage("*holding-period carry*");
                result.EvidenceState.Should().Be("MissingEvidence", scenario);
                result.Disposals.Should().BeEmpty();
            }
        }
    }
}
