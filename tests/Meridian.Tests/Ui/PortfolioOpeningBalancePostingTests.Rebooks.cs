using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed partial class PortfolioOpeningBalancePostingTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task CashOpening_UnchangedRebookAndDescendantsDoNotDoublePrimaryBalances()
        => PrepareReviewAndPostOpeningAsync(AccountingBasisKindDto.Primary, openRebook: OpeningRebookScenario.CashUnchanged);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task CashOpening_UnchangedRebookAndDescendantsDoNotDoubleTaxBalances()
        => PrepareReviewAndPostOpeningAsync(AccountingBasisKindDto.Tax, openRebook: OpeningRebookScenario.CashUnchanged);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task MixedOpening_ChangedRebookAndDescendantsReplacePrimaryBalances()
        => PrepareReviewAndPostOpeningAsync(AccountingBasisKindDto.Primary, openRebook: OpeningRebookScenario.MixedChanged);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task MixedOpening_ChangedRebookAndDescendantsReplaceTaxBalances()
        => PrepareReviewAndPostOpeningAsync(AccountingBasisKindDto.Tax, openRebook: OpeningRebookScenario.MixedChanged);

    private enum OpeningRebookScenario { CashUnchanged, MixedChanged }

    private sealed record OpeningRebookFixture(LedgerPostgresTestDatabase Database,
        ManualJournalEntryWorkbenchService Workbench, InMemoryManualJournalEntryDraftStore Drafts,
        AccountingConfigurationService Configuration, CapturingPostingTarget Target,
        ManualJournalEntryDraftDto OriginalDraft, JournalEntry OriginalJournal,
        string Fund, string Tenant, string Company, Guid Book, Guid Period);

    private static async Task ExerciseOpenOpeningRebooksAsync(OpeningRebookFixture fixture, OpeningRebookScenario scenario)
    {
        var original = fixture.OriginalDraft;
        JournalEntryLifecycleActionRequestDto? lastPostRequest = null;
        var replacement = scenario == OpeningRebookScenario.CashUnchanged ? null
            : original.Lines.Select(line => line with
            {
                Amount = line.AccountPath == "Equity:Capital" ? 440m
                    : line.LedgerAccountSymbol == "AAPL" ? 240m
                    : line.LedgerAccountSymbol == "MSFT" ? 165m : 35m
            }).ToArray();
        var first = await CorrectAsync(original, JournalEntryLifecycleActionDto.Rebook, replacement);
        first.RebookedFromJournalEntryId.Should().Be(original.JournalEntryId);
        first.Lines.Where(line => line.LineId.StartsWith("rebook-inverse-", StringComparison.Ordinal))
            .Should().HaveCount(original.Lines.Count);
        first.TreasuryContext!.IdempotencyKey.Should().Be($"portfolio-opening-correction:{original.JournalEntryId:D}:{first.JournalEntryId:D}");

        // Save round-trips retain the server's source link and unique key even if a client strips them.
        var request = new SaveManualJournalEntryDraftRequest(first with
        {
            RebookedFromJournalEntryId = Guid.NewGuid(),
            ReversalOfJournalEntryId = Guid.NewGuid(),
            Rebook = null,
            Reversal = null,
            TreasuryContext = first.TreasuryContext with { IdempotencyKey = "ordinary-manual-key" }
        }, first.PreparedBy!, LedgerBookId: fixture.Book, TenantId: fixture.Tenant, CompanyId: fixture.Company);
        first = await fixture.Workbench.SaveDraftAsync(JsonSerializer.Deserialize<SaveManualJournalEntryDraftRequest>(JsonSerializer.Serialize(request))!);
        first.RebookedFromJournalEntryId.Should().Be(original.JournalEntryId);
        first.ReversalOfJournalEntryId.Should().BeNull();
        first.TreasuryContext!.IdempotencyKey.Should().Be($"portfolio-opening-correction:{original.JournalEntryId:D}:{first.JournalEntryId:D}");
        first.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);

        var changedInverse = first.Lines.Select(line => line.LineId.StartsWith("rebook-inverse-", StringComparison.Ordinal)
            ? line with { Amount = line.Amount * 2m } : line).ToArray();
        var changed = await fixture.Workbench.ValidateDraftAsync(new(first with { Lines = changedInverse }, "untrusted-client",
            LedgerBookId: fixture.Book, TenantId: fixture.Tenant, CompanyId: fixture.Company));
        changed.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.opening-correction-authority-required");
        var forged = await fixture.Workbench.ValidateDraftAsync(new(first with
        {
            JournalEntryId = Guid.NewGuid(),
            TreasuryContext = first.TreasuryContext with { IdempotencyKey = "ordinary-manual-key" }
        }, "untrusted-client", LedgerBookId: fixture.Book, TenantId: fixture.Tenant, CompanyId: fixture.Company));
        forged.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.opening-correction-authority-required");
        var forgedId = Guid.NewGuid();
        var forgedCanonical = await fixture.Workbench.ValidateDraftAsync(new(first with
        {
            JournalEntryId = forgedId,
            TreasuryContext = first.TreasuryContext with { IdempotencyKey = $"portfolio-opening-correction:{original.JournalEntryId:D}:{forgedId:D}" }
        }, "untrusted-client", LedgerBookId: fixture.Book, TenantId: fixture.Tenant, CompanyId: fixture.Company));
        forgedCanonical.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.opening-correction-authority-required");

        await fixture.Configuration.UpsertChartNodeAsync(new(fixture.Fund,
            new("cash", "Assets:Cash", "Redirected Cash", "Asset"), "chart-controller",
            LedgerBookId: fixture.Book, CompanyId: fixture.Company, TenantId: fixture.Tenant));
        var remapped = await fixture.Workbench.ValidateDraftAsync(new(first, "reviewer", LedgerBookId: fixture.Book,
            TenantId: fixture.Tenant, CompanyId: fixture.Company));
        remapped.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.opening-correction-authority-required");
        await fixture.Configuration.UpsertChartNodeAsync(new(fixture.Fund,
            new("cash", "Assets:Cash", "Cash", "Asset"), "chart-controller",
            LedgerBookId: fixture.Book, CompanyId: fixture.Company, TenantId: fixture.Tenant));

        var postedFirst = await ReviewAndPostAsync(first);
        var firstWrite = fixture.Target.LastWrite!;
        await AssertBalancesAsync();
        AssertSecurityLineage(firstWrite.Entry);
        var ordinaryKey = "manual-je:forged-opening-rebook";
        var omittedAuthority = firstWrite with
        {
            Entry = new JournalEntry(firstWrite.Entry.JournalEntryId, firstWrite.Entry.Timestamp, firstWrite.Entry.Description,
                firstWrite.Entry.Lines, firstWrite.Entry.Metadata with { IdempotencyKey = ordinaryKey }),
            PostingCommand = firstWrite.PostingCommand! with
            {
                IdempotencyKey = ordinaryKey,
                TreasuryContext = firstWrite.PostingCommand.TreasuryContext! with { IdempotencyKey = ordinaryKey }
            }
        };
        var appendWithoutAuthority = () => fixture.Database.JournalStore.AppendAsync(omittedAuthority);
        await appendWithoutAuthority.Should().ThrowAsync<LedgerValidationException>().WithMessage("*claimed imported opening source*");
        var changedEconomics = firstWrite with
        {
            Entry = new JournalEntry(firstWrite.Entry.JournalEntryId, firstWrite.Entry.Timestamp, firstWrite.Entry.Description,
                firstWrite.Entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
                    line.Account, line.Debit * 2m, line.Credit * 2m, line.Description, line.Dimensions,
                    new LedgerEntryCurrency("USD", "USD", line.Debit * 2m, line.Credit * 2m, 1m))).ToArray(), firstWrite.Entry.Metadata)
        };
        var appendChangedEconomics = () => fixture.Database.JournalStore.AppendAsync(changedEconomics);
        await appendChangedEconomics.Should().ThrowAsync<LedgerValidationException>().WithMessage("*every immutable predecessor inverse leg*");

        // The first changed mixed-security correction is +65. Rebooking that adjustment with
        // unchanged terms must be zero, rather than adding another 65 to the imported account.
        var second = await CorrectAsync(postedFirst, JournalEntryLifecycleActionDto.Rebook);
        second.TreasuryContext!.IdempotencyKey.Should().Be($"portfolio-opening-correction:{postedFirst.JournalEntryId:D}:{second.JournalEntryId:D}");
        var postedSecond = await ReviewAndPostAsync(second);
        fixture.Target.LastWrite!.Entry.Lines.GroupBy(line => line.Account)
            .Should().OnlyContain(group => group.Sum(line => line.Debit - line.Credit) == 0m);
        await AssertBalancesAsync();
        AssertSecurityLineage(fixture.Target.LastWrite.Entry);
        var third = await CorrectAsync(postedSecond, JournalEntryLifecycleActionDto.Reverse);
        var postedThird = await ReviewAndPostAsync(third);
        var thirdWrite = fixture.Target.LastWrite!;
        await AssertBalancesAsync();
        AssertSecurityLineage(thirdWrite.Entry);
        (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period)).Should().HaveCount(4);

        var period = (await fixture.Database.JournalStore.GetPeriodAsync(fixture.Period))!;
        await fixture.Database.JournalStore.SaveHardClosedPeriodAsync(period with { Status = "HardClosed", ClosedAt = DateTimeOffset.UtcNow },
            period.Version, new(Guid.NewGuid(), fixture.Period, "Open", "HardClosed", "close-controller",
                "Close the reviewed imported balances and corrections.", DateTimeOffset.UtcNow));
        var locked = await fixture.Workbench.ApplyLifecycleActionAsync(Action(postedThird, JournalEntryLifecycleActionDto.LockAfterClose));
        locked.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.CloseLocked);
        // Both acknowledgement seams replay retained facts after close, before fresh admission rules.
        var replay = await fixture.Workbench.ApplyLifecycleActionAsync(lastPostRequest!);
        replay.JournalEntry.JournalEntryId.Should().Be(postedThird.JournalEntryId);
        (await fixture.Target.PostAsync(thirdWrite)).WasAppended.Should().BeFalse();
        (await fixture.Drafts.GetAsync(fixture.Fund, postedThird.JournalEntryId,
            tenantId: fixture.Tenant, companyId: fixture.Company))!.Status.Should().Be(ManualJournalEntryStatusDto.CloseLocked);
        (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period)).Should().HaveCount(4);

        async Task<ManualJournalEntryDraftDto> CorrectAsync(ManualJournalEntryDraftDto source, JournalEntryLifecycleActionDto action,
            IReadOnlyList<ManualJournalEntryLineDto>? lines = null)
            => (await fixture.Workbench.ApplyLifecycleActionAsync(Action(source, action) with { RebookLines = lines ?? [] }))
                .GeneratedJournalEntries.Single();

        async Task<ManualJournalEntryDraftDto> ReviewAndPostAsync(ManualJournalEntryDraftDto draft)
        {
            var validation = await fixture.Workbench.ValidateDraftAsync(new(draft, draft.PreparedBy!, LedgerBookId: fixture.Book,
                TenantId: fixture.Tenant, CompanyId: fixture.Company));
            validation.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
            var submitted = await fixture.Workbench.SubmitApprovalAsync(new(draft.JournalEntryId, fixture.Fund, draft.PreparedBy!,
                draft.Version, Notes: "Review the original source, complete inverse and replacement.", EvidenceLinks: [Evidence(draft)],
                LedgerBookId: fixture.Book, TenantId: fixture.Tenant, CompanyId: fixture.Company));
            var approved = (await fixture.Workbench.ApplyLifecycleActionAsync(Action(submitted, JournalEntryLifecycleActionDto.Approve))).JournalEntry;
            lastPostRequest = Action(approved, JournalEntryLifecycleActionDto.Post);
            return (await fixture.Workbench.ApplyLifecycleActionAsync(lastPostRequest)).JournalEntry;
        }

        JournalEntryLifecycleActionRequestDto Action(ManualJournalEntryDraftDto draft, JournalEntryLifecycleActionDto action)
            => new(draft.JournalEntryId, fixture.Fund, action,
                action is JournalEntryLifecycleActionDto.Rebook or JournalEntryLifecycleActionDto.Reverse
                    ? $"opening-corrector-{draft.JournalEntryId:N}" : "opening-controller",
                draft.Version, "Review and correct the retained opening source without changing history.",
                CorrelationId: $"opening-{action}:{draft.JournalEntryId:D}", EvidenceLinks: [Evidence(draft)],
                LedgerBookId: fixture.Book, TenantId: fixture.Tenant, CompanyId: fixture.Company);

        string Evidence(ManualJournalEntryDraftDto draft)
            => $"evidence://review-correction/tenant/{fixture.Tenant}/company/{fixture.Company}/book/{fixture.Book:D}/period/{fixture.Period:D}/journal/{draft.JournalEntryId:D}/close-approval-posting";

        async Task AssertBalancesAsync()
        {
            var journals = await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period);
            journals.Should().OnlyContain(journal => journal.AccountingBasis == original.AccountingBasis
                && journal.AccountingPolicyId == "broker-opening-policy" && journal.AccountingPolicyVersion == "v2");
            journals.Single(journal => journal.Entry.JournalEntryId == fixture.OriginalJournal.JournalEntryId).Entry
                .Should().BeEquivalentTo(fixture.OriginalJournal, "the original imported journal is immutable");
            var balances = journals.SelectMany(journal => journal.Entry.Lines).GroupBy(line => line.Account)
                .ToDictionary(group => group.Key, group => group.Sum(line => line.Debit - line.Credit));
            foreach (var line in fixture.OriginalJournal.Lines)
            {
                var expected = scenario == OpeningRebookScenario.CashUnchanged ? line.Debit - line.Credit
                    : line.Account.AccountType == LedgerAccountType.Equity ? -440m
                    : line.Account.Symbol == "AAPL" ? 240m : line.Account.Symbol == "MSFT" ? 165m : 35m;
                balances[line.Account].Should().Be(expected);
            }
        }

        void AssertSecurityLineage(JournalEntry journal)
        {
            journal.Metadata.SecurityId.Should().BeNull();
            journal.Metadata.Symbol.Should().BeNull();
            foreach (var line in journal.Lines.Where(line => line.Account.Symbol is not null))
            {
                journal.Metadata.Tags!["securityMaster.lineageMode"].Should().Be("per-line");
                var prefix = $"securityMaster.line.{line.EntryId:D}.";
                journal.Metadata.Tags[prefix + "securityId"].Should().Be(line.Dimensions!.InstrumentId!.Value.ToString("D"));
                journal.Metadata.Tags[prefix + "provenance"].Should().Contain(line.Account.Symbol == "AAPL" ? "version:3;" : "version:7;");
                line.Currency!.IsFunctionalCurrency.Should().BeTrue();
            }
        }
    }
}
