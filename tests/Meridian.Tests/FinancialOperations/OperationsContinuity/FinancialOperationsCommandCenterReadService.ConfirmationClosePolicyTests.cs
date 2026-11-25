using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;

namespace Meridian.Tests.FinancialOperations.OperationsContinuity;

/// <summary>Opt-in confirmation close blocking (ADR-032) as one close-readiness contributor.</summary>
public sealed partial class FinancialOperationsCommandCenterReadServiceTests
{
    [Fact]
    public async Task ConfirmationClosePolicy_WithoutAnActivePolicy_LeavesTheCloseDecisionUnchanged()
    {
        var workflow = CreateWorkflow();
        var gate = new StubConfirmationCloseGate(_ => null);

        var result = await ReadShadowScoped(CreateConfirmationService(workflow, gate), workflow);

        result.CloseReadiness!.Contributors.Should().NotContain(c => c.ContributorId == "intercompany-confirmation");
        result.IsReadyToComplete.Should().BeTrue();
        gate.Calls.Should().ContainSingle().Which.Should().Be((BookId, ShadowTenant, "company-alpha"));
    }

    [Fact]
    public async Task ConfirmationClosePolicy_ABlockingFinding_BlocksCloseAsAKnownState()
    {
        var workflow = CreateWorkflow();
        var policy = Guid.NewGuid();
        var gate = new StubConfirmationCloseGate(_ => new("Blocked", ["confirmation-1"],
            "Close policy blocks close at 2026-06-30: intercompany.confirmation.disputed for accounting profile p-1.", policy, 2));

        var result = await ReadShadowScoped(CreateConfirmationService(workflow, gate), workflow);

        var blocker = result.CloseReadiness!.Blockers.Should().ContainSingle(b => b.ContributorId == "intercompany-confirmation").Subject;
        blocker.Code.Should().Be("close.intercompany-confirmation.blocked");
        blocker.Owner.Should().Be("Intercompany");
        blocker.Message.Should().Contain("intercompany.confirmation.disputed");
        blocker.RecordIds.Should().Equal("confirmation-1");
        result.CloseReadiness.IsComplete.Should().BeTrue("a disputed pair under an approved policy is a known blocking state");
        result.IsReadyToComplete.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmationClosePolicy_NoBlockingFinding_ContributesReady()
    {
        var workflow = CreateWorkflow();
        var gate = new StubConfirmationCloseGate(_ => new("Ready", [], "No blocking confirmation finding.", Guid.NewGuid(), 2));

        var result = await ReadShadowScoped(CreateConfirmationService(workflow, gate), workflow);

        result.CloseReadiness!.Contributors.Should().Contain(c => c.ContributorId == "intercompany-confirmation" && c.Status == "Ready");
        result.IsReadyToComplete.Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmationClosePolicy_UnreadableStatusOrAFailingGate_MakesTheDecisionIncomplete()
    {
        var workflow = CreateWorkflow();
        var unavailable = await ReadShadowScoped(CreateConfirmationService(workflow,
            new StubConfirmationCloseGate(_ => new("Unavailable", [], "Balances cannot be read.", Guid.NewGuid(), 2))), workflow);
        var failing = await ReadShadowScoped(CreateConfirmationService(workflow,
            new StubConfirmationCloseGate(_ => throw new IOException("private connection details"))), workflow);

        foreach (var result in new[] { unavailable, failing })
        {
            var blocker = result.CloseReadiness!.Blockers.Should().ContainSingle(b => b.ContributorId == "intercompany-confirmation").Subject;
            blocker.Type.Should().Be("Unavailable");
            blocker.Message.Should().NotContain("private connection details");
            result.CloseReadiness.IsComplete.Should().BeFalse();
            result.IsReadyToComplete.Should().BeFalse();
        }
    }

    private static FinancialOperationsCommandCenterReadService CreateConfirmationService(
        OperationsContinuityWorkflowDto workflow, IIntercompanyConfirmationCloseGate gate)
        => new(new StubOperationsContinuityWorkflowService(workflow), new StubCloseCalendarService(ReadyCalendar(workflow)),
            new StubPrivateCapitalCloseCockpitService(FreshCockpit(workflow)), CreateBookService(),
            CreateClosePlanService(workflow), CreateSubjectSource(), confirmationCloseGate: gate);

    private sealed class StubConfirmationCloseGate(Func<CloseReadinessScopeDto, IntercompanyConfirmationCloseGateResultDto?> result)
        : IIntercompanyConfirmationCloseGate
    {
        public List<(Guid? LedgerBookId, string? TenantId, string? CompanyId)> Calls { get; } = [];

        public Task<IntercompanyConfirmationCloseGateResultDto?> EvaluateAsync(CloseReadinessScopeDto scope,
            string? tenantId, string? companyId, CancellationToken ct = default)
        {
            Calls.Add((scope.LedgerBookId, tenantId, companyId));
            return Task.FromResult(result(scope));
        }
    }
}
