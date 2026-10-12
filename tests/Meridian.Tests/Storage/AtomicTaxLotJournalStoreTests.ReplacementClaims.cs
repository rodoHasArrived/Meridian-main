using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalHistory_CertifiesCumulativeClaimsForSharedReplacementRecipients()
    {
        foreach (var scenario in new[] { "overclaimed", "sufficient-capacity", "ambiguous-shared-allocation" })
        {
            await using var database = await LedgerPostgresTestDatabase.CreateAsync();
            var (first, lots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false, washSale: true,
                financialAccountId: "claim-source");
            var original = lots[0];
            var replacementAccount = original.Account with { FinancialAccountId = "claim-recipient" };
            var quantity = scenario switch
            {
                "overclaimed" => 1m,
                "sufficient-capacity" => 2m,
                _ => 4m
            };
            var timestamp = first.Journal.Entry.Timestamp;
            var shared = await database.JournalStore.SaveTaxLotAsync(new(Guid.NewGuid(), first.LedgerBookId,
                replacementAccount, "shared-claim-recipient", new(2026, 5, 15), quantity, quantity, 80m, "USD",
                timestamp, timestamp, SecurityId: TestSecurityId, BookPositionId: TestBookPositionId));
            var journal = first.Journal.Entry;
            first = (first with
            {
                Journal = first.Journal with
                {
                    Entry = new JournalEntry(journal.JournalEntryId, timestamp, journal.Description,
                        journal.Lines.Select(line => line.Account.Name == LedgerAccounts.RealizedLoss.Name
                            ? new LedgerEntry(line.EntryId, journal.JournalEntryId, timestamp, replacementAccount,
                                20m, 0m, journal.Description, line.Dimensions, line.Currency)
                            : line).ToArray(), journal.Metadata)
                }
            }).WithComputedFingerprint();
            await database.JournalStore.AppendAssetPostingAsync(first);

            WashSaleDeferralRecord Claim(AtomicTaxLotJournalCommand source, LedgerTaxLotRecord recipient,
                decimal amount) => new(Guid.NewGuid(), source.LedgerBookId, source.MutationBatchId,
                TestSecurityId, new(2026, 5, 12), original.Account, recipient.TaxLotRecordId, recipient.LotId,
                amount, 1m, original.AcquiredDate, "tax-policy-v1", 30, WashSaleReplacementScope.LedgerBook,
                source.Journal.Entry.Timestamp);

            var firstClaims = new List<WashSaleDeferralRecord>();
            if (scenario == "ambiguous-shared-allocation")
            {
                var exclusive = await database.JournalStore.SaveTaxLotAsync(new(Guid.NewGuid(), first.LedgerBookId,
                    replacementAccount, "exclusive-claim-recipient", new(2026, 5, 15), 4m, 4m, 80m, "USD",
                    timestamp, timestamp, SecurityId: TestSecurityId, BookPositionId: TestBookPositionId));
                // Both rows repeat this disposal's aggregate matched quantity. Their loss amounts
                // do not establish how much replacement quantity belongs to either recipient.
                firstClaims.Add(Claim(first, shared, 10m));
                firstClaims.Add(Claim(first, exclusive, 10m));
            }
            else
            {
                firstClaims.Add(Claim(first, shared, 20m));
            }
            await database.JournalStore.SaveWashSaleDeferralsAsync(firstClaims);
            var initial = await new LedgerDisposalTaxReadService(new PostgresLedgerJournalStore(database.Options))
                .ReadAsync(first.LedgerBookId, first.Journal.PeriodId, first.Journal.Entry, "USD");
            initial.Disposals.Should().ContainSingle().Which.State.Should().Be("Settled", scenario);

            var secondLotId = Guid.NewGuid();
            var secondLot = await database.JournalStore.SaveTaxLotAsync(original with
            {
                TaxLotRecordId = secondLotId,
                LotId = "second-claim-source",
                Acquisition = original.Acquisition! with
                {
                    Evidence = original.Acquisition.Evidence.Select(proof => proof with
                    {
                        SubjectId = secondLotId.ToString("D")
                    }).ToArray()
                }
            });
            var sourceEvent = Guid.NewGuid();
            var secondWrite = BuildJournalWrite(first.LedgerBookId, first.Journal.PeriodId, sourceEvent,
                "second-replacement-claim");
            var secondJournal = secondWrite.Entry;
            secondWrite = secondWrite with
            {
                Entry = new JournalEntry(secondJournal.JournalEntryId, secondJournal.Timestamp,
                    secondJournal.Description, first.Journal.Entry.Lines.Select(line => new LedgerEntry(
                        Guid.NewGuid(), secondJournal.JournalEntryId, secondJournal.Timestamp, line.Account,
                        line.Debit, line.Credit, secondJournal.Description, line.Dimensions, line.Currency)).ToArray(),
                    secondJournal.Metadata)
            };
            var evidence = BuildEvidence("second-replacement-claim", 'd');
            var second = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), first.LedgerBookId, secondWrite,
                sourceEvent, "second-replacement-claim", first.ExpectedPeriodVersion,
                AtomicTaxLotMutationKind.Disposal, [evidence],
                disposalSelections: [new(secondLot.TaxLotRecordId, secondLot.LotId, secondLot.Version,
                    1m, 1m, 0, evidence.EvidenceId, 100m, 100m)],
                reliefMethod: "Fifo", policyRevision: "tax-policy-v1");
            await database.JournalStore.AppendAssetPostingAsync(second);
            await database.JournalStore.SaveWashSaleDeferralsAsync([Claim(second, shared, 20m)]);

            var restarted = new PostgresLedgerJournalStore(database.Options);
            foreach (var source in new[] { first, second })
            {
                // Request each journal separately: cumulative certification must also consider
                // claims from disposal batches outside the requested history set.
                var read = () => restarted.GetTaxLotDisposalHistoryAsync(source.LedgerBookId,
                    [source.Journal.Entry.JournalEntryId]);
                var result = await new LedgerDisposalTaxReadService(restarted).ReadAsync(source.LedgerBookId,
                    source.Journal.PeriodId, source.Journal.Entry, "USD");
                if (scenario == "sufficient-capacity")
                {
                    (await read()).Single().MatchedReplacementQuantity.Should().Be(1m);
                    result.EvidenceState.Should().Be("Available");
                    var disposal = result.Disposals.Should().ContainSingle().Which;
                    disposal.State.Should().Be("Settled");
                    disposal.EconomicGainOrLoss.Should().Be("-20");
                    disposal.RecognizedGainOrLoss.Should().Be("0");
                    disposal.DeferredLoss.Should().Be("20");
                }
                else
                {
                    await read.Should().ThrowAsync<LedgerValidationException>();
                    result.EvidenceState.Should().Be("MissingEvidence", scenario);
                    result.Disposals.Should().BeEmpty();
                }
            }
        }
    }
}
