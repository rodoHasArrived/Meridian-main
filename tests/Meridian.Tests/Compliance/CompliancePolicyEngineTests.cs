using Meridian.Audit.Compliance;

namespace Meridian.Tests.Compliance;

public sealed class CompliancePolicyEngineTests
{
    [Fact]
    public void PaymentRelease_RequiresRoleDualApprovalAndMfa()
    {
        var policy = new CompliancePolicyEngine();
        var actor = new ActorContext("approver-1", ["TreasuryOperator"], "Treasury", "127.0.0.1", "dev1", MfaSatisfied: true);
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
        var actor = new ActorContext("approver-1", ["OverrideApprover"], "Risk", "127.0.0.1", "dev1", MfaSatisfied: true);
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
        var actor = new ActorContext("ops-1", ["RulesAdmin"], "Ops", "10.0.0.1", "laptop", true);

        audit.Append(actor, new ComplianceActionRequest(SensitiveAction.RuleEdit, "Rule", "rule-1", "{\"limit\":10}", "{\"limit\":15}", "corr-a", "entity-a"));
        audit.Append(actor, new ComplianceActionRequest(SensitiveAction.RuleEdit, "Rule", "rule-2", "{\"limit\":20}", "{\"limit\":25}", "corr-b", "entity-a"));

        Assert.True(audit.VerifyIntegrity());
        Assert.Equal(2, audit.GetAll().Count);
    }
}
