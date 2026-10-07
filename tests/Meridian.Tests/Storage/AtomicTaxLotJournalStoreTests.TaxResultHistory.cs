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
        foreach (var (firstMatched, secondMatched) in new[] { (1m, 0.5m), (0.5m, 1m), (1.5m, 1.5m), (1m, 1m) })
        {
            await using var database = await LedgerPostgresTestDatabase.CreateAsync();
            var (command, lots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false, washSale: true);
            var replacementAccount = lots[0].Account with { FinancialAccountId = "broker-2" };
            var timestamp = command.Journal.Entry.Timestamp;
            // Keep recipient capacity sufficient when testing the stricter disposed-loss bound.
            var replacementQuantity = firstMatched == secondMatched && firstMatched > 1m
                ? firstMatched / 2m : 0.5m;
            var replacements = new List<LedgerTaxLotRecord>();
            foreach (var lotId in new[] { "replacement-a", "replacement-z" })
                replacements.Add(await database.JournalStore.SaveTaxLotAsync(new LedgerTaxLotRecord(
                    Guid.NewGuid(), command.LedgerBookId, replacementAccount, lotId, new(2026, 5, 15),
                    replacementQuantity, replacementQuantity, 80m, "USD", timestamp, timestamp,
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
                retained.MatchedReplacementQuantity.Should().Be(firstMatched, "the common aggregate is repeated, not additive");
                result.EvidenceState.Should().Be("Available");
                var disposal = result.Disposals.Should().ContainSingle().Which;
                if (firstMatched > 1m)
                {
                    disposal.State.Should().Be("MissingEvidence", "agreement between rows cannot certify more replacements than the disposed loss quantity");
                    disposal.EconomicGainOrLoss.Should().BeNull();
                    disposal.RecognizedGainOrLoss.Should().BeNull();
                    disposal.DeferredLoss.Should().BeNull();
                    disposal.Character.Should().BeNull();
                    disposal.Parcels.Should().BeEmpty();
                }
                else
                {
                    disposal.State.Should().Be("Settled");
                }
            }
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalHistory_CertifiesEveryDeferralSecuritySaleDateAndDisposingAccount()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        foreach (var scenario in new[]
                 {
                     "security", "sale-date", "account-name", "account-type", "symbol", "financial-account",
                     "nullable-account", "case-alias"
                 })
        {
            var (command, lots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false, washSale: true,
                financialAccountId: scenario == "nullable-account" ? null : "broker-1");
            var replacementAccount = lots[0].Account with { FinancialAccountId = "broker-2" };
            var journal = command.Journal.Entry;
            // The economic date deliberately differs from posting time. A same-book deferral
            // carrying the posting date must not be accepted as evidence for this disposal.
            var saleDate = new DateOnly(2026, 5, 11);
            command = (command with
            {
                Journal = command.Journal with
                {
                    Entry = new JournalEntry(journal.JournalEntryId, journal.Timestamp, journal.Description,
                        journal.Lines.Select(line => line.Account.Name == LedgerAccounts.RealizedLoss.Name
                            ? new LedgerEntry(line.EntryId, journal.JournalEntryId, journal.Timestamp, replacementAccount,
                                20m, 0m, journal.Description, line.Dimensions, line.Currency)
                            : line).ToArray(), journal.Metadata with { EffectiveDate = saleDate })
                }
            }).WithComputedFingerprint();
            var posted = await database.JournalStore.AppendAssetPostingAsync(command);
            var deferrals = new List<WashSaleDeferralRecord>();
            foreach (var suffix in new[] { "a", "z" })
            {
                var replacement = await database.JournalStore.SaveTaxLotAsync(new LedgerTaxLotRecord(
                    Guid.NewGuid(), command.LedgerBookId, replacementAccount, $"identity-replacement-{suffix}", new(2026, 5, 15),
                    0.5m, 0.5m, 80m, "USD", journal.Timestamp, journal.Timestamp,
                    SecurityId: TestSecurityId, BookPositionId: TestBookPositionId));
                deferrals.Add(new(Guid.NewGuid(), command.LedgerBookId, command.MutationBatchId,
                    TestSecurityId, saleDate, lots[0].Account, replacement.TaxLotRecordId, replacement.LotId,
                    10m, 1m, lots[0].AcquiredDate, "tax-policy-v1", 30, WashSaleReplacementScope.LedgerBook, journal.Timestamp));
            }
            var last = deferrals[1];
            deferrals[1] = scenario switch
            {
                "security" => last with { SecurityId = Guid.NewGuid() },
                "sale-date" => last with { SaleDate = new(2026, 5, 12) },
                "account-name" => last with { DisposalAccount = last.DisposalAccount with { Name = "Other investments" } },
                "account-type" => last with { DisposalAccount = last.DisposalAccount with { AccountType = LedgerAccountType.Revenue } },
                "symbol" => last with { DisposalAccount = last.DisposalAccount with { Symbol = "OTHER" } },
                "financial-account" => last with { DisposalAccount = last.DisposalAccount with { FinancialAccountId = null } },
                "case-alias" => last with { DisposalAccount = last.DisposalAccount with { FinancialAccountId = "BROKER-1" } },
                _ => last
            };
            // These are ordinary accepted writes. Read certification must not trust a same-book
            // foreign identity merely because the write API accepts the batch/replacement links.
            await database.JournalStore.SaveWashSaleDeferralsAsync(deferrals);
            var reopened = new PostgresLedgerJournalStore(database.Options);
            var read = () => reopened.GetTaxLotDisposalHistoryAsync(command.LedgerBookId,
                [journal.JournalEntryId]);
            var result = await new LedgerDisposalTaxReadService(reopened).ReadAsync(command.LedgerBookId,
                command.Journal.PeriodId, posted.Journal.Entry, "USD");

            if (scenario is "nullable-account" or "case-alias")
            {
                var retained = (await read()).Should().ContainSingle().Which;
                retained.WashSaleBasisIncreases.Should().HaveCount(2);
                result.EvidenceState.Should().Be("Available");
                var disposal = result.Disposals.Should().ContainSingle().Which;
                disposal.State.Should().Be("Settled");
                disposal.SaleDate.Should().Be(saleDate);
                disposal.DeferredLoss.Should().Be(20m);
                disposal.RecognizedGainOrLoss.Should().Be(0m);
            }
            else
            {
                await read.Should().ThrowAsync<LedgerValidationException>()
                    .WithMessage("*deferral security, sale date, or disposing account*", scenario);
                result.EvidenceState.Should().Be("MissingEvidence", scenario);
                result.Disposals.Should().BeEmpty("mismatched retained identity cannot certify disposal economics or finality");
            }
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalHistory_LegacyDeferralIdentityUsesUtcSaleDateWhenEffectiveDateIsAbsent()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, lots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false, washSale: true);
        var replacementAccount = lots[0].Account with { FinancialAccountId = "broker-2" };
        var timestamp = new DateTimeOffset(2026, 5, 11, 20, 0, 0, TimeSpan.FromHours(-7));
        var journal = command.Journal.Entry;
        command = (command with
        {
            Journal = command.Journal with
            {
                Entry = new JournalEntry(journal.JournalEntryId, timestamp, journal.Description,
                    journal.Lines.Select(line => new LedgerEntry(line.EntryId, journal.JournalEntryId, timestamp,
                        line.Account.Name == LedgerAccounts.RealizedLoss.Name ? replacementAccount : line.Account,
                        line.Debit, line.Credit, journal.Description, line.Dimensions, line.Currency)).ToArray(),
                    journal.Metadata with { EffectiveDate = null, IdempotencyKey = null })
            }
        }).WithComputedFingerprint();
        await RetainLegacyProceedsDisposalAsync(database, command, lots);
        var replacement = await database.JournalStore.SaveTaxLotAsync(new LedgerTaxLotRecord(
            Guid.NewGuid(), command.LedgerBookId, replacementAccount, "utc-replacement", new(2026, 5, 15),
            1m, 1m, 80m, "USD", timestamp, timestamp, SecurityId: TestSecurityId, BookPositionId: TestBookPositionId));
        await database.JournalStore.SaveWashSaleDeferralsAsync([new(Guid.NewGuid(), command.LedgerBookId,
            command.MutationBatchId, TestSecurityId, new(2026, 5, 12), lots[0].Account,
            replacement.TaxLotRecordId, replacement.LotId, 20m, 1m, lots[0].AcquiredDate,
            "tax-policy-v1", 30, WashSaleReplacementScope.LedgerBook, timestamp)]);

        var result = await new LedgerDisposalTaxReadService(new PostgresLedgerJournalStore(database.Options))
            .ReadAsync(command.LedgerBookId, command.Journal.PeriodId, command.Journal.Entry, "USD");

        result.EvidenceState.Should().Be("Available");
        var disposal = result.Disposals.Should().ContainSingle().Which;
        disposal.State.Should().Be("Settled");
        disposal.SaleDate.Should().Be(new DateOnly(2026, 5, 12));
        disposal.DeferredLoss.Should().Be(20m);
        disposal.RecognizedGainOrLoss.Should().Be(0m);
    }
}
