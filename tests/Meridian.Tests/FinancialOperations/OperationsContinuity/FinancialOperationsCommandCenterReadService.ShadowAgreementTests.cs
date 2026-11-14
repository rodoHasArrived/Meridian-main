using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;

namespace Meridian.Tests.FinancialOperations.OperationsContinuity;

public sealed partial class FinancialOperationsCommandCenterReadServiceTests
{
    private const string ShadowTenant = "tenant-alpha";

    [Fact]
    public async Task ShadowBook_BlocksCloseReadinessWithItsAgreementFigures()
    {
        var workflow = CreateWorkflow();
        var guard = new StubBookOfRecordGuard(_ => new(BookId, false, OnboardingShadowStates.Shadow, "workspace-1",
            "The ledger book is a shadow book in parallel run: 1 of 2 required period(s) reconcile cleanly with the incumbent and 3 difference(s) remain unexplained.",
            1, 2, 3));

        var result = await ReadShadowScoped(CreateShadowService(workflow, () => guard), workflow);

        result.CloseReadiness!.Contributors.Should().Contain(c => c.ContributorId == "shadow-agreement" && c.Status == "Blocked");
        var blocker = result.CloseReadiness.Blockers.Should().ContainSingle(b => b.ContributorId == "shadow-agreement").Subject;
        blocker.Code.Should().Be("close.shadow-agreement.blocked");
        blocker.Owner.Should().Be("Onboarding");
        blocker.Message.Should().Contain("1 of 2 required period(s)").And.Contain("3 difference(s) remain unexplained");
        blocker.RecordIds.Should().Equal("workspace-1");
        result.CloseReadiness.IsComplete.Should().BeTrue("a shadow book is a known blocking state, not missing evidence");
        result.CloseReadiness.IsReadyToClose.Should().BeFalse();
        result.IsReadyToComplete.Should().BeFalse();
        guard.Calls.Should().ContainSingle().Which.Should().Be((ShadowTenant, "company-alpha", BookId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(OnboardingShadowStates.Promoted)]
    public async Task BookOfRecord_ContributesReady(string? shadowState)
    {
        var workflow = CreateWorkflow();
        var guard = new StubBookOfRecordGuard(_ => new(BookId, true, shadowState, shadowState is null ? null : "workspace-1",
            "The ledger book is the book of record."));

        var result = await ReadShadowScoped(CreateShadowService(workflow, () => guard), workflow);

        result.CloseReadiness!.Contributors.Should().HaveCount(7)
            .And.Contain(c => c.ContributorId == "shadow-agreement" && c.Status == "Ready");
        result.CloseReadiness.Blockers.Should().BeEmpty();
        result.IsReadyToComplete.Should().BeTrue();
    }

    [Fact]
    public async Task WithdrawnShadowTrial_StillBlocksClose()
    {
        var workflow = CreateWorkflow();
        var guard = new StubBookOfRecordGuard(_ => new(BookId, false, OnboardingShadowStates.Withdrawn, "workspace-1",
            "The shadow trial for this ledger book was withdrawn without promotion."));

        var result = await ReadShadowScoped(CreateShadowService(workflow, () => guard), workflow);

        result.CloseReadiness!.Blockers.Should().ContainSingle(b => b.ContributorId == "shadow-agreement" && b.Type == "Blocked");
        result.IsReadyToComplete.Should().BeFalse();
    }

    [Fact]
    public async Task NoGuard_ReportsNoShadowContributor()
    {
        var workflow = CreateWorkflow();

        var result = await ReadShadowScoped(CreateShadowService(workflow, () => null), workflow);

        result.CloseReadiness!.Contributors.Should().NotContain(c => c.ContributorId == "shadow-agreement");
        result.IsReadyToComplete.Should().BeTrue();
    }

    [Fact]
    public async Task FailingGuard_IsUnavailableAndDoesNotLeakExceptionText()
    {
        var workflow = CreateWorkflow();
        var guard = new StubBookOfRecordGuard(_ => throw new IOException("private connection details"));

        var result = await ReadShadowScoped(CreateShadowService(workflow, () => guard), workflow);

        var blocker = result.CloseReadiness!.Blockers.Should().ContainSingle(b => b.ContributorId == "shadow-agreement").Subject;
        blocker.Type.Should().Be("Unavailable");
        blocker.Message.Should().NotContain("private connection details");
        result.CloseReadiness.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GuardThatCannotBeComposed_FailsClosed()
    {
        var workflow = CreateWorkflow();

        var result = await ReadShadowScoped(CreateShadowService(workflow,
            () => throw new InvalidOperationException("storage not configured")), workflow);

        result.CloseReadiness!.Blockers.Should().ContainSingle(b => b.ContributorId == "shadow-agreement" && b.Type == "Unavailable");
        result.IsReadyToComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GuardWithoutTenant_RequiresScopeInsteadOfReadingAnUnscopedStatus()
    {
        var workflow = CreateWorkflow();
        var guard = new StubBookOfRecordGuard(_ => new(BookId, true, null, null, "Book of record."));

        var result = await ReadScoped(CreateShadowService(workflow, () => guard), workflow);

        result.CloseReadiness!.Blockers.Should().ContainSingle(b => b.ContributorId == "shadow-agreement" && b.Type == "ScopeRequired");
        guard.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task IncompleteScope_ListsTheShadowContributorAsPending()
    {
        var workflow = CreateWorkflow();
        var guard = new StubBookOfRecordGuard(_ => new(BookId, true, null, null, "Book of record."));

        var result = await CreateShadowService(workflow, () => guard).GetCommandCenterAsync(
            fundProfileId: "fund-alpha", tenantId: ShadowTenant, companyId: "company-alpha");

        result.CloseReadiness!.Contributors.Should().Contain(c => c.ContributorId == "shadow-agreement" && c.Status == "ScopeRequired");
        guard.Calls.Should().BeEmpty();
    }

    private static FinancialOperationsCommandCenterReadService CreateShadowService(
        OperationsContinuityWorkflowDto workflow, Func<IBookOfRecordGuard?> guardFactory)
        => new(new StubOperationsContinuityWorkflowService(workflow), new StubCloseCalendarService(ReadyCalendar(workflow)),
            new StubPrivateCapitalCloseCockpitService(FreshCockpit(workflow)), CreateBookService(),
            CreateClosePlanService(workflow), CreateSubjectSource(), bookOfRecordGuardFactory: guardFactory);

    private static Task<FinancialOperationsCommandCenterDto> ReadShadowScoped(
        FinancialOperationsCommandCenterReadService service, OperationsContinuityWorkflowDto workflow)
        => service.GetCommandCenterAsync("fund-alpha", BookId, workflow.FundAccountId, workflow.PeriodId, "entity-alpha",
            tenantId: ShadowTenant, companyId: "company-alpha");

    private sealed class StubBookOfRecordGuard(Func<Guid, BookOfRecordStatusDto> status) : IBookOfRecordGuard
    {
        public List<(string TenantId, string? CompanyId, Guid LedgerBookId)> Calls { get; } = [];

        public Task<BookOfRecordStatusDto> GetStatusAsync(string tenantId, string? companyId, Guid ledgerBookId,
            CancellationToken ct = default)
        {
            Calls.Add((tenantId, companyId, ledgerBookId));
            return Task.FromResult(status(ledgerBookId));
        }
    }
}
