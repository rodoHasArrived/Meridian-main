using FluentAssertions;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingConfigurationServiceTests
{
    [Fact]
    public async Task ManualJournalRebook_ReplacesThePostedJournal_InsteadOfAddingToIt()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var journalStore = WritableManualJournalLedgerJournalStore.Default();
        var service = CreateManualJournalEntryWorkbenchService(configuration, journalStore: journalStore);
        var posted = await PostBalancedManualJournalAsync(service);
        ManualJournalEntryLineDto[] replacement =
        [
            new("reclass-debit-cash", AccountingTemplateLineSideDto.Debit, 100m, "USD", "Assets:Cash"),
            new("reclass-credit-accrued", AccountingTemplateLineSideDto.Credit, 100m, "USD", "Liabilities:Accrued Expenses")
        ];

        var rebook = await service.ApplyLifecycleActionAsync(ManualRebookRequest(posted, replacement));

        var draft = rebook.GeneratedJournalEntries.Should().ContainSingle().Subject;
        draft.Lines.Where(line => line.LineId.StartsWith("rebook-inverse-", StringComparison.Ordinal))
            .Should().HaveCount(posted.Lines.Count);
        draft.Lines.Where(line => line.LineId.StartsWith("rebook-replacement-", StringComparison.Ordinal))
            .Should().HaveCount(replacement.Length);
        // The original stays posted (Rebooked cannot be reversed later), so the rebook draft alone
        // must move the books from the original economics to exactly the replacement economics.
        NetByAccount(posted.Lines.Concat(draft.Lines)).Should().BeEquivalentTo(NetByAccount(replacement));

        var postedRebook = await PostCorrectionDraftAsync(service, draft);
        postedRebook.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        postedRebook.PostedJournal.Should().NotBeNull();
    }

    [Fact]
    public async Task ManualJournalRebook_WithoutReplacementLines_NetsToZeroInsteadOfDuplicatingTheJournal()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var journalStore = WritableManualJournalLedgerJournalStore.Default();
        var service = CreateManualJournalEntryWorkbenchService(configuration, journalStore: journalStore);
        var posted = await PostBalancedManualJournalAsync(service);

        var rebook = await service.ApplyLifecycleActionAsync(ManualRebookRequest(posted, []));

        var draft = rebook.GeneratedJournalEntries.Should().ContainSingle().Subject;
        draft.Lines.Should().HaveCount(posted.Lines.Count * 2);
        NetByAccount(draft.Lines).Should().BeEmpty("an unchanged rebook restates the posted journal; it must not post it again");
        NetByAccount(posted.Lines.Concat(draft.Lines)).Should().BeEquivalentTo(NetByAccount(posted.Lines));
    }

    private async Task<ManualJournalEntryDraftDto> PostBalancedManualJournalAsync(ManualJournalEntryWorkbenchService service)
    {
        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user"));
        var submitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId, saved.FundProfileId, "controller", saved.Version, LedgerBookId: saved.LedgerBookId));
        var approved = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId, submitted.FundProfileId, JournalEntryLifecycleActionDto.Approve, "controller",
            submitted.Version, Notes: "Controller approved the source entry.",
            EvidenceLinks: [ManualJournalApprovalEvidence(submitted)], LedgerBookId: submitted.LedgerBookId));
        var posted = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId, approved.JournalEntry.FundProfileId, JournalEntryLifecycleActionDto.Post,
            "controller", approved.JournalEntry.Version, Notes: "Posted before rebook.",
            EvidenceLinks: [ManualJournalPostingEvidence(approved.JournalEntry)], LedgerBookId: approved.JournalEntry.LedgerBookId));
        posted.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        return posted.JournalEntry;
    }

    private static JournalEntryLifecycleActionRequestDto ManualRebookRequest(
        ManualJournalEntryDraftDto posted, IReadOnlyList<ManualJournalEntryLineDto> replacement)
        => new(posted.JournalEntryId, posted.FundProfileId, JournalEntryLifecycleActionDto.Rebook, "controller",
            posted.Version, Notes: "Reclassify the posted income",
            EvidenceLinks: [$"/api/workstation/evidence/subjects/accounting-record/rebook/ledger-book/{posted.LedgerBookId:D}/{posted.PeriodId}"],
            RebookLines: replacement, LedgerBookId: posted.LedgerBookId);

    private async Task<JournalEntryLifecycleActionResultDto> PostCorrectionDraftAsync(
        ManualJournalEntryWorkbenchService service, ManualJournalEntryDraftDto draft)
    {
        var submitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            draft.JournalEntryId, draft.FundProfileId, "controller", draft.Version, LedgerBookId: draft.LedgerBookId));
        var approved = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId, submitted.FundProfileId, JournalEntryLifecycleActionDto.Approve, "reviewer",
            submitted.Version, Notes: "Reviewer approved the rebook.",
            EvidenceLinks: [ManualJournalApprovalEvidence(submitted)], LedgerBookId: submitted.LedgerBookId));
        return await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId, approved.JournalEntry.FundProfileId, JournalEntryLifecycleActionDto.Post,
            "reviewer", approved.JournalEntry.Version, Notes: "Posted the reviewed rebook.",
            EvidenceLinks: [ManualJournalPostingEvidence(approved.JournalEntry)], LedgerBookId: approved.JournalEntry.LedgerBookId));
    }

    // Signed net (debit positive) per posting account identity, omitting accounts that net to zero.
    private static Dictionary<string, decimal> NetByAccount(IEnumerable<ManualJournalEntryLineDto> lines)
        => lines
            .GroupBy(line => $"{line.AccountPath}|{line.LedgerAccountSymbol}|{line.LedgerAccountFinancialAccountId}|{line.Currency}")
            .Select(group => (group.Key, Net: group.Sum(line => line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : -line.Amount)))
            .Where(item => item.Net != 0m)
            .ToDictionary(item => item.Key, item => item.Net);
}
