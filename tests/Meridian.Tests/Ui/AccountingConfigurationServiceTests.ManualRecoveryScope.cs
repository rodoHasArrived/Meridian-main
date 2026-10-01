using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingConfigurationServiceTests
{
    [Fact]
    public async Task ManualAuditRecovery_DesktopRetry_RepairsScopedBrowserSubmission()
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var (saved, request) = await ScopedPendingSubmissionAsync(fixture);
        var pendingBefore = File.ReadAllBytes(fixture.PendingFiles().Single());
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(
            audit: new RecoveryFailingAudit(fixture.Audit(), "manual-je.submit-approval"))
            .SubmitApprovalAsync(request with { TenantId = null, CompanyId = null }));
        File.ReadAllBytes(fixture.PendingFiles().Single()).Should().Equal(pendingBefore);

        var repaired = await fixture.Service().SubmitApprovalAsync(request with { TenantId = null, CompanyId = null });
        repaired.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
        repaired.TenantId.Should().Be(saved.TenantId);
        repaired.CompanyId.Should().Be(saved.CompanyId);
        var audit = (await fixture.Audit().ListAsync()).Should().ContainSingle(x => x.Action == "manual-je.submit-approval").Subject;
        audit.Actor.Should().Be(request.Actor);
        audit.RecordedAtUtc.Should().Be(repaired.UpdatedAtUtc);
        await fixture.Service().SubmitApprovalAsync(request);
        (await fixture.Audit().ListAsync()).Should().ContainSingle(x => x.Action == "manual-je.submit-approval");
        fixture.PendingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task ManualAuditRecovery_DesktopNextCommand_RepairsBeforeAdvancingAndBlocksDuringOutage()
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var (saved, _) = await ScopedPendingSubmissionAsync(fixture);
        var submitted = (await fixture.Drafts().GetAsync(saved.FundProfileId, saved.JournalEntryId))!;
        var approve = new JournalEntryLifecycleActionRequestDto(saved.JournalEntryId, saved.FundProfileId,
            JournalEntryLifecycleActionDto.Approve, "controller", submitted.Version,
            Notes: "Independent review", CorrelationId: "desktop-approve",
            EvidenceLinks: [$"/api/workstation/evidence/subjects/accounting-record/approval/tenant/{submitted.TenantId}/company/{submitted.CompanyId}/ledger-book/{submitted.LedgerBookId:D}/{submitted.PeriodId}"], LedgerBookId: saved.LedgerBookId);
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(
            audit: new RecoveryFailingAudit(fixture.Audit(), "manual-je.submit-approval")).ApplyLifecycleActionAsync(approve));
        (await fixture.Drafts().GetAsync(saved.FundProfileId, saved.JournalEntryId)).Should().BeEquivalentTo(submitted);
        (await fixture.Audit().ListAsync()).Should().NotContain(x => x.Action == "manual-je.approve");

        var result = await fixture.Service().ApplyLifecycleActionAsync(approve);
        result.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Approved);
        var audits = await fixture.Audit().ListAsync();
        audits.Should().ContainSingle(x => x.Action == "manual-je.submit-approval");
        audits.Should().ContainSingle(x => x.Action == "manual-je.approve");
        fixture.PendingFiles().Should().BeEmpty();
    }

    [Theory]
    [InlineData("foreign-tenant", "company-alpha")]
    [InlineData("tenant-alpha", "foreign-company")]
    public async Task ManualAuditRecovery_ExplicitForeignScope_DoesNotRepairPendingAudit(string tenant, string company)
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var (_, request) = await ScopedPendingSubmissionAsync(fixture);
        var pendingBefore = File.ReadAllBytes(fixture.PendingFiles().Single());
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.Service().SubmitApprovalAsync(
            request with { TenantId = tenant, CompanyId = company }));
        File.ReadAllBytes(fixture.PendingFiles().Single()).Should().Equal(pendingBefore);
        (await fixture.Audit().ListAsync()).Should().NotContain(x => x.Action == "manual-je.submit-approval");
    }

    [Fact]
    public async Task ManualAuditRecovery_AmbiguousDesktopIdentity_FailsBeforeRepair()
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var (saved, request) = await ScopedPendingSubmissionAsync(fixture);
        await fixture.Drafts().SaveAsync(saved with { TenantId = "another-tenant", CompanyId = "another-company" });
        var action = () => fixture.Service().SubmitApprovalAsync(request with { TenantId = null, CompanyId = null });
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*scope is ambiguous*");
        (await fixture.Audit().ListAsync()).Should().NotContain(x => x.Action == "manual-je.submit-approval");
        fixture.PendingFiles().Should().ContainSingle();
        // Explicit identity still recovers only the intended company.
        await fixture.Service().SubmitApprovalAsync(request);
        (await fixture.Drafts().GetAsync(saved.FundProfileId, saved.JournalEntryId,
            tenantId: "another-tenant", companyId: "another-company"))!.Version.Should().Be(saved.Version);
    }

    [Theory]
    [InlineData("foreign-tenant", "company-alpha")]
    [InlineData("tenant-alpha", "foreign-company")]
    public async Task ManualAuditRecovery_ContradictorySaveScope_FailsBeforeRepair(string draftTenant, string draftCompany)
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var (saved, _) = await ScopedPendingSubmissionAsync(fixture);
        var original = File.ReadAllBytes(fixture.PendingFiles().Single());
        var request = new SaveManualJournalEntryDraftRequest(
            saved with { TenantId = draftTenant, CompanyId = draftCompany }, "operator", "conflicting-save",
            LedgerBookId: saved.LedgerBookId, TenantId: saved.TenantId, CompanyId: saved.CompanyId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service().SaveDraftAsync(request));
        File.ReadAllBytes(fixture.PendingFiles().Single()).Should().Equal(original);
        (await fixture.Audit().ListAsync()).Should().NotContain(x => x.Action == "manual-je.submit-approval");
    }

    [Theory]
    [InlineData("tenant-alpha", null)]
    [InlineData(null, "company-alpha")]
    public async Task ManualAuditRecovery_PartialScope_ResolvesUniqueIdentity(string? tenant, string? company)
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var (_, request) = await ScopedPendingSubmissionAsync(fixture);
        var result = await fixture.Service().SubmitApprovalAsync(request with { TenantId = tenant, CompanyId = company });
        result.TenantId.Should().Be("tenant-alpha");
        result.CompanyId.Should().Be("company-alpha");
        (await fixture.Audit().ListAsync()).Should().ContainSingle(x => x.Action == "manual-je.submit-approval");
    }

    private static async Task<(ManualJournalEntryDraftDto Draft, SubmitManualJournalEntryApprovalRequest Request)>
        ScopedPendingSubmissionAsync(ManualRecoveryFixture fixture)
    {
        var saved = await fixture.Service().SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            BalancedManualJournalEntry() with { TenantId = "tenant-alpha", CompanyId = "company-alpha" },
            "ops-user", "browser-save", TenantId: "tenant-alpha", CompanyId: "company-alpha"));
        var request = new SubmitManualJournalEntryApprovalRequest(saved.JournalEntryId, saved.FundProfileId,
            "controller", saved.Version, Notes: "Submit scoped journal", CorrelationId: "browser-submit",
            LedgerBookId: saved.LedgerBookId, TenantId: saved.TenantId, CompanyId: saved.CompanyId);
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(
            audit: new RecoveryFailingAudit(fixture.Audit(), "manual-je.submit-approval")).SubmitApprovalAsync(request));
        return (saved, request);
    }
}
