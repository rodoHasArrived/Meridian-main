using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingConfigurationServiceTests
{
    [Fact]
    public async Task ManualAuditRecovery_ArchivedAutosaves_ReplayOriginalResultAndRepairMissingAudit()
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var retention = new ManualJournalMutationRecoveryOptions { MaxCompletedCount = 2 };
        var firstRequest = new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user", "manual-je-autosave");
        var first = await fixture.Service(retention: retention).SaveDraftAsync(firstRequest);
        var current = first;
        for (var index = 0; index < 6; index++)
            current = await fixture.Service(retention: retention).SaveDraftAsync(firstRequest with
            {
                Draft = current with { Memo = "Autosave " + index },
                LedgerBookId = current.LedgerBookId
            });
        Directory.GetFiles(Path.Combine(fixture.RecoveryDirectory, "completed"), "*.json").Should().HaveCount(2);
        Directory.GetFiles(Path.Combine(fixture.RecoveryDirectory, "archive"), "*.gz", SearchOption.AllDirectories).Should().HaveCount(5);
        var replayed = await fixture.Service(retention: retention).SaveDraftAsync(firstRequest);
        replayed.Should().BeEquivalentTo(first);
        (await fixture.Drafts().GetAsync(current.FundProfileId, current.JournalEntryId)).Should().BeEquivalentTo(current);
        (await fixture.Audit().ListAsync()).Should().HaveCount(7);

        var missingAudit = new FileAccountingConfigurationStore(Path.Combine(fixture.RootDirectory, "restored-audit.json"));
        await fixture.Service(audit: missingAudit, retention: retention).SaveDraftAsync(firstRequest);
        await fixture.Service(audit: missingAudit, retention: retention).SaveDraftAsync(firstRequest);
        var audit = (await missingAudit.ListAsync()).Should().ContainSingle().Subject;
        audit.Should().BeEquivalentTo((await fixture.Audit().ListAsync()).Single(x => x.AfterHash == audit.AfterHash));
    }

    [Fact]
    public async Task ManualAuditRecovery_RestoredDataRoot_RecoversPendingAndReplaysArchive()
    {
        using var source = await ManualRecoveryFixture.CreateAsync();
        var retention = new ManualJournalMutationRecoveryOptions { MaxCompletedCount = 0 };
        var original = new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user", "backup-save");
        var saved = await source.Service(retention: retention).SaveDraftAsync(original);
        var submit = new SubmitManualJournalEntryApprovalRequest(saved.JournalEntryId, saved.FundProfileId,
            "controller", saved.Version, Notes: "Submit before backup", CorrelationId: "backup-submit", LedgerBookId: saved.LedgerBookId);
        await Assert.ThrowsAsync<IOException>(() => source.Service(retention: retention,
            audit: new RecoveryFailingAudit(source.Audit(), "manual-je.submit-approval")).SubmitApprovalAsync(submit));

        using var restored = await ManualRecoveryFixture.CreateAsync();
        foreach (var file in Directory.EnumerateFiles(source.RootDirectory, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(restored.RootDirectory, Path.GetRelativePath(source.RootDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        var repaired = await restored.Service(retention: retention).SubmitApprovalAsync(submit);
        repaired.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
        (await restored.Service(retention: retention).SaveDraftAsync(original)).Should().BeEquivalentTo(saved);
        restored.PendingFiles().Should().BeEmpty();
        (await restored.Audit().ListAsync()).Should().HaveCount(2);
        source.PendingFiles().Should().ContainSingle();
    }

    [Theory]
    [InlineData(JournalEntryLifecycleActionDto.Reverse, "manual-je.reverse-draft")]
    [InlineData(JournalEntryLifecycleActionDto.Rebook, "manual-je.rebook-draft")]
    public async Task ManualAuditRecovery_GeneratedDraftCommand_RepairsItsSourceReceipt(
        JournalEntryLifecycleActionDto action, string failingAudit)
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var approved = await fixture.ApprovedAsync();
        var posted = (await fixture.Service().ApplyLifecycleActionAsync(RecoveryPostRequest(approved))).JournalEntry;
        var correction = new JournalEntryLifecycleActionRequestDto(posted.JournalEntryId, posted.FundProfileId,
            action, "controller", posted.Version, Notes: "Correct retained adjustment", CorrelationId: "source-correction",
            EvidenceLinks: [$"/api/workstation/evidence/subjects/accounting-record/{(action == JournalEntryLifecycleActionDto.Reverse ? "reversal" : "rebook")}/ledger-book/{posted.LedgerBookId:D}/{posted.PeriodId}"],
            LedgerBookId: posted.LedgerBookId);
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(
            audit: new RecoveryFailingAudit(fixture.Audit(), failingAudit)).ApplyLifecycleActionAsync(correction));
        var generated = (await fixture.Drafts().ListAsync(posted.FundProfileId)).Single(x => x.JournalEntryId != posted.JournalEntryId);
        await fixture.Service().SaveDraftAsync(new SaveManualJournalEntryDraftRequest(generated with { Memo = "Reviewed correction" },
            "ops-user", "edit-generated", LedgerBookId: generated.LedgerBookId));
        fixture.PendingFiles().Should().BeEmpty();
        (await fixture.Audit().ListAsync()).Should().ContainSingle(x => x.Action == failingAudit);
        fixture.Ledger.Appended.Should().ContainSingle();
    }
}
