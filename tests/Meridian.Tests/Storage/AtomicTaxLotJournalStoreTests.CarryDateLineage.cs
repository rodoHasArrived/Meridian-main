using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalHistory_CertifiesCarryDatesThroughBoundedRetainedLineage()
    {
        foreach (var scenario in new[]
                 {
                     "valid-chain", "ancestor-date", "latest-date", "immutable-anchor", "cycle",
                     "ambiguous-source", "depth-boundary", "depth-limit"
                 })
        {
            await using var database = await LedgerPostgresTestDatabase.CreateAsync();
            var (template, initialLots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false,
                washSale: true, financialAccountId: "date-source");
            var acquired = new DateOnly(2026, 5, 1);
            var anchor = scenario == "immutable-anchor" ? new DateOnly(2020, 1, 1) : acquired;
            var original = initialLots[0];
            var anchorId = Guid.NewGuid();
            var quantity = scenario == "ambiguous-source" ? 0.5m : 1m;
            original = await database.JournalStore.SaveTaxLotAsync(original with
            {
                TaxLotRecordId = anchorId,
                LotId = "date-anchor",
                Account = original.Account with { FinancialAccountId = "date-anchor" },
                AcquiredDate = acquired,
                OriginalQuantity = quantity,
                OpenQuantity = quantity,
                Acquisition = original.Acquisition! with
                {
                    TransactionCostBasis = quantity * 100m,
                    FunctionalCostBasis = quantity * 100m,
                    HoldingPeriodStartDate = anchor,
                    Evidence = original.Acquisition.Evidence.Select(proof => proof with
                    {
                        SubjectId = anchorId.ToString("D"),
                        EffectiveDate = acquired
                    }).ToArray()
                }
            });
            var sourceLots = new List<LedgerTaxLotRecord> { original };
            if (scenario == "ambiguous-source")
                sourceLots.Add(await SaveCarryDateLotAsync(database, original, "other-source", acquired.AddDays(1),
                    original.Account, 0.5m));

            var chainLength = scenario switch { "depth-boundary" => 32, "depth-limit" => 33, _ => 2 };
            for (var index = 0; index < chainLength; index++)
            {
                var recipientAccount = original.Account with { FinancialAccountId = $"date-recipient-{index}" };
                var recipient = await SaveCarryDateLotAsync(database, original, $"date-recipient-{index}",
                    new(2026, 5, 10), recipientAccount, 1m);
                var source = await AppendCarryDateDisposalAsync(database, template, sourceLots,
                    recipientAccount, new(2026, 5, 12), $"date-source-{index}");
                var carried = scenario == "ancestor-date" || scenario == "latest-date" && index == chainLength - 1
                    ? new DateOnly(2010, 1, 1) : anchor;
                var row = new WashSaleDeferralRecord(Guid.NewGuid(), template.LedgerBookId, source.MutationBatchId,
                    TestSecurityId, new(2026, 5, 12), sourceLots[0].Account, recipient.TaxLotRecordId,
                    recipient.LotId, 20m, 1m, carried, "tax-policy-v1", 30,
                    WashSaleReplacementScope.LedgerBook, source.Journal.Entry.Timestamp);
                await database.JournalStore.SaveWashSaleDeferralsAsync([row]);
                sourceLots = [recipient];
            }
            var target = await AppendCarryDateDisposalAsync(database, template, sourceLots,
                null, new(2026, 5, 20), "date-final-disposal");
            if (scenario == "cycle")
            {
                // Accepted retained relationships can form a date-copy cycle even though each
                // individual date and replacement window looks plausible. No history is edited.
                await database.JournalStore.SaveWashSaleDeferralsAsync([new(Guid.NewGuid(), template.LedgerBookId,
                    target.MutationBatchId, TestSecurityId, new(2026, 5, 20), sourceLots[0].Account,
                    original.TaxLotRecordId, original.LotId, 20m, 1m, anchor, "tax-policy-v1", 30,
                    WashSaleReplacementScope.LedgerBook, target.Journal.Entry.Timestamp)]);
            }

            var reopened = new PostgresLedgerJournalStore(database.Options);
            var read = () => reopened.GetTaxLotDisposalHistoryAsync(template.LedgerBookId,
                [target.Journal.Entry.JournalEntryId]);
            var result = await new LedgerDisposalTaxReadService(reopened).ReadAsync(template.LedgerBookId,
                target.Journal.PeriodId, target.Journal.Entry, "USD");
            if (scenario is "valid-chain" or "immutable-anchor" or "depth-boundary")
            {
                (await read()).Single().Lots.Single().HoldingPeriodStart.Should().Be(anchor);
                result.EvidenceState.Should().Be("Available", scenario);
                var disposal = result.Disposals.Should().ContainSingle().Which;
                disposal.State.Should().Be("Settled");
                var parcel = disposal.Parcels.Should().ContainSingle().Which;
                parcel.Character.Should().Be(scenario == "immutable-anchor" ? "LongTerm" : "ShortTerm");
                parcel.HoldingPeriodCarried.Should().BeTrue();
            }
            else
            {
                await read.Should().ThrowAsync<LedgerValidationException>().WithMessage("*holding-period carry*");
                result.EvidenceState.Should().Be("MissingEvidence", scenario);
                result.Disposals.Should().BeEmpty();
            }
        }
    }

    private static Task<LedgerTaxLotRecord> SaveCarryDateLotAsync(LedgerPostgresTestDatabase database,
        LedgerTaxLotRecord template, string lotId, DateOnly acquired, LedgerAccount account, decimal quantity)
    {
        var id = Guid.NewGuid();
        return database.JournalStore.SaveTaxLotAsync(template with
        {
            TaxLotRecordId = id,
            LotId = lotId,
            Account = account,
            AcquiredDate = acquired,
            OriginalQuantity = quantity,
            OpenQuantity = quantity,
            Acquisition = template.Acquisition! with
            {
                TransactionCostBasis = quantity * 100m,
                FunctionalCostBasis = quantity * 100m,
                HoldingPeriodStartDate = acquired,
                Evidence = template.Acquisition.Evidence.Select(proof => proof with
                {
                    SubjectId = id.ToString("D"),
                    EffectiveDate = acquired
                }).ToArray()
            }
        });
    }

    private static async Task<AtomicTaxLotJournalCommand> AppendCarryDateDisposalAsync(LedgerPostgresTestDatabase database,
        AtomicTaxLotJournalCommand template, IReadOnlyList<LedgerTaxLotRecord> lots, LedgerAccount? replacementAccount,
        DateOnly saleDate, string key)
    {
        var account = lots[0].Account;
        var at = new DateTimeOffset(saleDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        if (!(await database.JournalStore.ListTaxLotPoliciesAsync(template.LedgerBookId))
            .Any(policy => policy.Account == account))
            await database.JournalStore.SaveTaxLotPolicyAsync(new(Guid.NewGuid(), template.LedgerBookId, account,
                LedgerTaxLotReliefMethod.Fifo, "tax-policy-v1", new(2026, 5, 1), at, at));
        var journalId = Guid.NewGuid();
        var sourceEvent = Guid.NewGuid();
        var basis = lots.Sum(static lot => lot.OpenQuantity * lot.UnitCost);
        var dimensions = template.Journal.Entry.Lines[0].Dimensions;
        LedgerEntry Leg(LedgerAccount ledgerAccount, decimal debit, decimal credit) => new(Guid.NewGuid(),
            journalId, at, ledgerAccount, debit, credit, key, dimensions,
            new LedgerEntryCurrency("USD", "USD", debit, credit, 1m));
        var lines = new List<LedgerEntry>
        {
            Leg(LedgerAccounts.CashAccount(account.FinancialAccountId!), basis * (replacementAccount is null ? 1.1m : 0.8m), 0m),
            Leg(account, 0m, basis),
            replacementAccount is null
                ? Leg(LedgerAccounts.RealizedGainFor(account.FinancialAccountId!), 0m, basis * 0.1m)
                : Leg(replacementAccount, basis * 0.2m, 0m)
        };
        var journal = new JournalEntry(journalId, at, key, lines,
            template.Journal.Entry.Metadata with { EffectiveDate = saleDate, IdempotencyKey = key });
        var write = template.Journal with { Entry = journal, SourceEventId = sourceEvent };
        var proof = BuildEvidence(key, 'd');
        var command = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), template.LedgerBookId, write,
            sourceEvent, key, template.ExpectedPeriodVersion, AtomicTaxLotMutationKind.Disposal, [proof],
            disposalSelections: lots.Select((lot, index) => new LedgerTaxLotDisposalSelection(lot.TaxLotRecordId,
                lot.LotId, lot.Version, lot.OpenQuantity, lot.OpenQuantity, index, proof.EvidenceId,
                lot.UnitCost, lot.OpenQuantity * lot.UnitCost)).ToArray(),
            reliefMethod: "Fifo", policyRevision: "tax-policy-v1");
        await database.JournalStore.AppendAssetPostingAsync(command);
        return command;
    }
}
