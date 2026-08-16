using FluentAssertions;
using Meridian.Audit.Compliance;
using Meridian.Identity.Auth;
using Meridian.Tests.Infrastructure;

namespace Meridian.Tests.Compliance;

/// <summary>
/// Guards the governed-payment failure mode where caller-authored identity claims are mistaken for
/// authenticated, object-bound approval evidence.
/// </summary>
public sealed class CompliancePolicyEngineTests : TempDirectoryTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_PaymentReleaseWithAuthoritativeObjectBoundApprovals_Allows()
    {
        var (policy, store) = CreatePolicy();
        var approval = CreateApprovedRequest(store);

        var result = policy.Evaluate(
            Actor("release-operator", UserRole.Controller, mfa: true),
            CreateActionRequest(approvalRequestId: approval.ApprovalRequestId));

        result.Allowed.Should().BeTrue();
        result.Reason.Should().Be("Allowed");
    }

    [Fact]
    public void Evaluate_CallerSuppliedRequesterAndApproverClaimsWithoutAuthority_Rejects()
    {
        var (policy, _) = CreatePolicy();
        var spoofed = CreateActionRequest(
            requestedBy: "requester-1",
            approvers: ["approver-2", "approver-3"]);

        var result = policy.Evaluate(
            Actor("release-operator", UserRole.Controller, mfa: true),
            spoofed);

        result.Allowed.Should().BeFalse(
            "caller-authored actor IDs are identity claims, not approval evidence");
        result.Reason.Should().Be(
            "Step-up requirement failed: authoritative approval request required.");
    }

    [Fact]
    public void Evaluate_UnknownApprovalRequest_RejectsFailClosed()
    {
        var (policy, _) = CreatePolicy();

        var result = policy.Evaluate(
            Actor("release-operator", UserRole.Controller, mfa: true),
            CreateActionRequest(approvalRequestId: "missing-approval-request"));

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("Step-up requirement failed: approval request not found.");
    }

    [Theory]
    [InlineData("Payment", "payment-2", "fund-1")]
    [InlineData("Override", "payment-1", "fund-1")]
    [InlineData("Payment", "payment-1", "fund-2")]
    public void Evaluate_ApprovalBoundToDifferentObject_Rejects(
        string objectType,
        string objectId,
        string entityId)
    {
        var (policy, store) = CreatePolicy();
        var approval = CreateApprovedRequest(store);

        var result = policy.Evaluate(
            Actor("release-operator", UserRole.Controller, mfa: true),
            CreateActionRequest(
                objectType: objectType,
                objectId: objectId,
                entityId: entityId,
                approvalRequestId: approval.ApprovalRequestId));

        result.Allowed.Should().BeFalse(
            "approval evidence must be bound to the exact governed object and entity");
        result.Reason.Should().Be(
            "Step-up requirement failed: approval evidence does not match the requested object.");
    }

    [Fact]
    public void Evaluate_AuthoritativeRequesterAttemptsExecution_RejectsSegregationOfDuties()
    {
        var (policy, store) = CreatePolicy();
        var approval = CreateApprovedRequest(store);

        var result = policy.Evaluate(
            Actor("requester-1", UserRole.Controller, mfa: true),
            CreateActionRequest(approvalRequestId: approval.ApprovalRequestId));

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Contain("Segregation of duties violation");
    }

    [Fact]
    public void Evaluate_OnlyOneAuthoritativeApproval_RejectsDualApproval()
    {
        var (policy, store) = CreatePolicy();
        var approval = CreateApprovalRequest(store);
        store.RecordDecision(
            approval.ApprovalRequestId,
            Actor("approver-2", UserRole.Admin, mfa: true),
            approved: true);

        var result = policy.Evaluate(
            Actor("release-operator", UserRole.Controller, mfa: true),
            CreateActionRequest(approvalRequestId: approval.ApprovalRequestId));

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("Step-up requirement failed: dual approval required.");
    }

    [Fact]
    public void Evaluate_AuthoritativeRejection_RejectsAction()
    {
        var (policy, store) = CreatePolicy();
        var approval = CreateApprovalRequest(store);
        store.RecordDecision(
            approval.ApprovalRequestId,
            Actor("approver-2", UserRole.Admin, mfa: true),
            approved: false);

        var result = policy.Evaluate(
            Actor("release-operator", UserRole.Controller, mfa: true),
            CreateActionRequest(approvalRequestId: approval.ApprovalRequestId));

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("Step-up requirement failed: approval request was rejected.");
    }

    [Fact]
    public void Evaluate_ExpiredAuthoritativeApproval_RejectsAction()
    {
        var clock = new MutableTimeProvider(Now);
        var store = new FileComplianceApprovalStore(
            Path.Combine(TestDataRoot, "expired-approvals.json"),
            clock,
            approvalLifetime: TimeSpan.FromMinutes(5));
        var approval = CreateApprovedRequest(store);
        var policy = new CompliancePolicyEngine(store, clock);
        clock.Advance(TimeSpan.FromMinutes(6));

        var result = policy.Evaluate(
            Actor("release-operator", UserRole.Controller, mfa: true),
            CreateActionRequest(approvalRequestId: approval.ApprovalRequestId));

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("Step-up requirement failed: approval request expired.");
    }

    [Fact]
    public void RecordDecision_RequesterIdentityFromAuthenticatedContext_RejectsSelfApproval()
    {
        var (_, store) = CreatePolicy();
        var approval = CreateApprovalRequest(store);

        var act = () => store.RecordDecision(
            approval.ApprovalRequestId,
            Actor("REQUESTER-1", UserRole.Admin, mfa: true),
            approved: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot approve their own*");
    }

    [Fact]
    public void ApprovalStore_Restart_RetainsAuthenticatedActorsAndObjectBinding()
    {
        var clock = new MutableTimeProvider(Now);
        var path = Path.Combine(TestDataRoot, "restart-approvals.json");
        var store = new FileComplianceApprovalStore(path, clock);
        var approval = CreateApprovedRequest(store);

        var restarted = new FileComplianceApprovalStore(path, clock);
        var retained = restarted.Resolve(approval.ApprovalRequestId);

        retained.Should().NotBeNull();
        retained!.RequestedByActorId.Should().Be("requester-1");
        retained.ObjectType.Should().Be("Payment");
        retained.ObjectId.Should().Be("payment-1");
        retained.EntityId.Should().Be("fund-1");
        retained.Decisions.Select(decision => decision.ApprovedByActorId)
            .Should().Equal("approver-2", "approver-3");
    }

    [Theory]
    [InlineData(SensitiveAction.RuleEdit)]
    [InlineData(SensitiveAction.BreakClosure)]
    [InlineData(SensitiveAction.PaymentRelease)]
    [InlineData(SensitiveAction.OverrideApproval)]
    public void Evaluate_ActorLacksRequiredRole_Rejects(SensitiveAction action)
    {
        var (policy, _) = CreatePolicy();
        var request = CreateActionRequest(action: action);

        var result = policy.Evaluate(
            Actor("analyst-1", UserRole.ReadOnly, mfa: true),
            request);

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("Missing required privileged role.");
    }

    [Theory]
    [InlineData(SensitiveAction.RuleEdit, UserRole.Developer)]
    [InlineData(SensitiveAction.BreakClosure, UserRole.FundAccountant)]
    public void Evaluate_NonStepUpActionWithoutMfaOrApproval_Allows(
        SensitiveAction action,
        UserRole role)
    {
        var (policy, _) = CreatePolicy();

        var result = policy.Evaluate(
            Actor("ops-1", role, mfa: false),
            CreateActionRequest(action: action));

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_StepUpActionWithoutMfa_RejectsBeforeApprovalResolution()
    {
        var (policy, _) = CreatePolicy();

        var result = policy.Evaluate(
            Actor("release-operator", UserRole.Controller, mfa: false),
            CreateActionRequest(approvalRequestId: "any"));

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("Step-up requirement failed: MFA required.");
    }

    [Fact]
    public void Evaluate_UnknownAction_Rejects()
    {
        var (policy, _) = CreatePolicy();

        var result = policy.Evaluate(
            Actor("ops-1", UserRole.Admin, mfa: true),
            CreateActionRequest(action: (SensitiveAction)999));

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("Unknown action.");
    }

    [Fact]
    public void AuditLog_HashChain_VerifiesIntegrity()
    {
        var audit = new ImmutableAuditLogService();
        var actor = Actor("ops-1", UserRole.Admin, mfa: true);

        audit.Append(actor, new ComplianceActionRequest(
            SensitiveAction.RuleEdit,
            "Rule",
            "rule-1",
            "{\"limit\":10}",
            "{\"limit\":15}",
            "corr-a",
            "entity-a"));
        audit.Append(actor, new ComplianceActionRequest(
            SensitiveAction.RuleEdit,
            "Rule",
            "rule-2",
            "{\"limit\":20}",
            "{\"limit\":25}",
            "corr-b",
            "entity-a"));

        audit.VerifyIntegrity().Should().BeTrue();
        audit.GetAll().Should().HaveCount(2);
    }

    private (CompliancePolicyEngine Policy, FileComplianceApprovalStore Store) CreatePolicy()
    {
        var clock = new MutableTimeProvider(Now);
        var store = new FileComplianceApprovalStore(
            Path.Combine(TestDataRoot, $"approvals-{Guid.NewGuid():N}.json"),
            clock);
        return (new CompliancePolicyEngine(store, clock), store);
    }

    private static ComplianceApprovalRequestRecord CreateApprovalRequest(
        IComplianceApprovalStore store)
        => store.CreateRequest(
            Actor("requester-1", UserRole.Admin, mfa: true),
            new ComplianceApprovalRequestCommand(
                SensitiveAction.PaymentRelease,
                ObjectType: "Payment",
                ObjectId: "payment-1",
                CorrelationId: "corr-1",
                EntityId: "fund-1"));

    private static ComplianceApprovalRequestRecord CreateApprovedRequest(
        IComplianceApprovalStore store)
    {
        var approval = CreateApprovalRequest(store);
        store.RecordDecision(
            approval.ApprovalRequestId,
            Actor("approver-2", UserRole.Admin, mfa: true),
            approved: true);
        return store.RecordDecision(
            approval.ApprovalRequestId,
            Actor("approver-3", UserRole.Admin, mfa: true),
            approved: true);
    }

    private static ActorContext Actor(string actorId, UserRole role, bool mfa)
        => new(actorId, [role.ToString()], "Compliance", "127.0.0.1", "test-device", mfa);

    private static ComplianceActionRequest CreateActionRequest(
        SensitiveAction action = SensitiveAction.PaymentRelease,
        string objectType = "Payment",
        string objectId = "payment-1",
        string? entityId = "fund-1",
        string? approvalRequestId = null,
        string? requestedBy = null,
        string[]? approvers = null)
        => new(
            action,
            ObjectType: objectType,
            ObjectId: objectId,
            BeforeStateJson: "{\"status\":\"pending\"}",
            AfterStateJson: "{\"status\":\"released\"}",
            CorrelationId: "corr-1",
            EntityId: entityId,
            ApprovalRequestId: approvalRequestId,
            RequestedByActorId: requestedBy,
            AdditionalApproverIds: approvers);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
