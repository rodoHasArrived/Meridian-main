using FluentAssertions;
using Meridian.Audit.Compliance;
using Meridian.Identity.Auth;

namespace Meridian.Tests.Compliance;

public sealed class CompliancePolicyEngineTests
{
    [Fact]
    public void PaymentRelease_RequiresRoleDualApprovalAndMfa()
    {
        var policy = new CompliancePolicyEngine();
        var actor = new ActorContext("approver-1", [nameof(UserRole.Controller)], "Treasury", "127.0.0.1", "dev1", MfaSatisfied: true);
        var request = new ComplianceActionRequest(
            SensitiveAction.PaymentRelease,
            "Payment",
            "payment-1",
            "{}",
            "{\"status\":\"released\"}",
            "corr-1",
            "fund-1",
            RequestedByActorId: "requester-1",
            AdditionalApproverIds: ["approver-2", "approver-3"]);

        var result = policy.Evaluate(actor, request);
        Assert.True(result.Allowed);
    }

    [Fact]
    public void SegregationOfDuties_BlocksSelfApproval()
    {
        var policy = new CompliancePolicyEngine();
        var actor = new ActorContext("approver-1", [nameof(UserRole.Admin)], "Risk", "127.0.0.1", "dev1", MfaSatisfied: true);
        var request = new ComplianceActionRequest(
            SensitiveAction.OverrideApproval,
            "Override",
            "ovr-1",
            null,
            "{}",
            "corr-2",
            "fund-1",
            RequestedByActorId: "approver-1",
            AdditionalApproverIds: ["approver-2", "approver-3"]);

        var result = policy.Evaluate(actor, request);
        Assert.False(result.Allowed);
    }

    [Fact]
    public void AuditLog_HashChain_VerifiesIntegrity()
    {
        var audit = new ImmutableAuditLogService();
        var actor = new ActorContext("ops-1", [nameof(UserRole.Admin)], "Ops", "10.0.0.1", "laptop", true);

        audit.Append(actor, new ComplianceActionRequest(SensitiveAction.RuleEdit, "Rule", "rule-1", "{\"limit\":10}", "{\"limit\":15}", "corr-a", "entity-a"));
        audit.Append(actor, new ComplianceActionRequest(SensitiveAction.RuleEdit, "Rule", "rule-2", "{\"limit\":20}", "{\"limit\":25}", "corr-b", "entity-a"));

        Assert.True(audit.VerifyIntegrity());
        Assert.Equal(2, audit.GetAll().Count);
    }

    [Theory]
    [InlineData(SensitiveAction.RuleEdit)]
    [InlineData(SensitiveAction.BreakClosure)]
    [InlineData(SensitiveAction.PaymentRelease)]
    [InlineData(SensitiveAction.OverrideApproval)]
    public void Evaluate_ActorLacksRequiredRole_Rejects(SensitiveAction action)
    {
        var policy = new CompliancePolicyEngine();
        var actor = new ActorContext(
            "analyst-1", [nameof(UserRole.ReadOnly)], "Research", "10.0.0.5", "desk-3", MfaSatisfied: true);
        var request = CreateRequest(
            action,
            requestedBy: "requester-1",
            approvers: ["approver-2", "approver-3"]);

        var result = policy.Evaluate(actor, request);

        result.Allowed.Should().BeFalse(
            "every sensitive action requires a role granting its gating permission");
        result.Reason.Should().Be("Missing required privileged role.");
    }

    [Fact]
    public void Evaluate_UnknownAction_Rejects()
    {
        var policy = new CompliancePolicyEngine();
        var actor = new ActorContext(
            "ops-1", [nameof(UserRole.Admin), nameof(UserRole.Controller)], "Ops", "10.0.0.1", "laptop", MfaSatisfied: true);
        var request = CreateRequest((SensitiveAction)999, requestedBy: "requester-1");

        var result = policy.Evaluate(actor, request);

        result.Allowed.Should().BeFalse("actions outside the policy catalog must default to deny");
        result.Reason.Should().Be("Unknown action.");
    }

    [Theory]
    [InlineData(SensitiveAction.RuleEdit, nameof(UserRole.Developer))]
    [InlineData(SensitiveAction.BreakClosure, nameof(UserRole.FundAccountant))]
    public void Evaluate_NonStepUpActionWithoutMfa_Allows(SensitiveAction action, string role)
    {
        // Rule edits and break closures require the privileged role but no MFA step-up
        // or dual approval; this pins the boundary so step-up rules do not silently
        // expand or contract.
        var policy = new CompliancePolicyEngine();
        var actor = new ActorContext(
            "ops-1", [role], "Ops", "10.0.0.1", "laptop", MfaSatisfied: false);
        var request = CreateRequest(action, requestedBy: "requester-1", approvers: null);

        var result = policy.Evaluate(actor, request);

        result.Allowed.Should().BeTrue();
        result.Reason.Should().Be("Allowed");
    }

    [Theory]
    [InlineData(SensitiveAction.PaymentRelease, nameof(UserRole.Controller))]
    [InlineData(SensitiveAction.OverrideApproval, nameof(UserRole.Admin))]
    public void Evaluate_StepUpActionWithoutMfa_Rejects(SensitiveAction action, string role)
    {
        var policy = new CompliancePolicyEngine();
        var actor = new ActorContext(
            "approver-1", [role], "Treasury", "10.0.0.1", "laptop", MfaSatisfied: false);
        var request = CreateRequest(
            action,
            requestedBy: "requester-1",
            approvers: ["approver-2", "approver-3"]);

        var result = policy.Evaluate(actor, request);

        result.Allowed.Should().BeFalse("payment release and override approval require MFA step-up");
        result.Reason.Should().Be("Step-up requirement failed: MFA required.");
    }

    public static TheoryData<string[]?> InsufficientApprovers => new()
    {
        null,
        Array.Empty<string>(),
        new[] { "approver-2" }
    };

    [Theory]
    [MemberData(nameof(InsufficientApprovers))]
    public void Evaluate_StepUpActionWithoutTwoApprovers_Rejects(string[]? approvers)
    {
        var policy = new CompliancePolicyEngine();
        var actor = new ActorContext(
            "approver-1", [nameof(UserRole.Controller)], "Treasury", "10.0.0.1", "laptop", MfaSatisfied: true);
        var request = CreateRequest(
            SensitiveAction.PaymentRelease,
            requestedBy: "requester-1",
            approvers: approvers);

        var result = policy.Evaluate(actor, request);

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("Step-up requirement failed: dual approval required.");
    }

    [Fact]
    public void Evaluate_DuplicateApproversDifferingOnlyByCase_RejectsDualApproval()
    {
        // One person approving twice under case variants of the same id must not
        // satisfy dual approval.
        var policy = new CompliancePolicyEngine();
        var actor = new ActorContext(
            "approver-1", [nameof(UserRole.Controller)], "Treasury", "10.0.0.1", "laptop", MfaSatisfied: true);
        var request = CreateRequest(
            SensitiveAction.PaymentRelease,
            requestedBy: "requester-1",
            approvers: ["approver-2", "APPROVER-2"]);

        var result = policy.Evaluate(actor, request);

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("Step-up requirement failed: dual approval required.");
    }

    [Fact]
    public void Evaluate_SelfApprovalWithCaseInsensitiveActorIdMatch_Rejects()
    {
        var policy = new CompliancePolicyEngine();
        var actor = new ActorContext(
            "Approver-1", [nameof(UserRole.Admin)], "Risk", "10.0.0.1", "laptop", MfaSatisfied: true);
        var request = CreateRequest(
            SensitiveAction.OverrideApproval,
            requestedBy: "approver-1",
            approvers: ["approver-2", "approver-3"]);

        var result = policy.Evaluate(actor, request);

        result.Allowed.Should().BeFalse(
            "segregation of duties must not be bypassable via actor-id casing");
        result.Reason.Should().Contain("Segregation of duties violation");
    }

    private static ComplianceActionRequest CreateRequest(
        SensitiveAction action,
        string? requestedBy = null,
        string[]? approvers = null) => new(
        action,
        ObjectType: "Payment",
        ObjectId: "payment-1",
        BeforeStateJson: "{\"status\":\"pending\"}",
        AfterStateJson: "{\"status\":\"released\"}",
        CorrelationId: "corr-1",
        EntityId: "fund-1",
        RequestedByActorId: requestedBy,
        AdditionalApproverIds: approvers);
}
