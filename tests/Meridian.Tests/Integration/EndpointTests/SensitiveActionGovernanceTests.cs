using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Integration.EndpointTests;

public sealed class SensitiveActionGovernanceTests
{
    [Fact]
    public void Evaluate_PaymentRelease_RequiresDualApprovalAndMfa()
    {
        var engine = new SensitiveActionPolicyEngine();
        var context = new AccessContext(
            "alice",
            UserRole.Accounting,
            "fund-ops",
            "fund-a",
            "10.0.0.1",
            "device-1",
            "corr-1",
            false,
            ["bob"]);

        var denied = engine.Evaluate(SensitiveActionType.PaymentRelease, context);
        denied.Allowed.Should().BeFalse();

        var allowed = engine.Evaluate(SensitiveActionType.PaymentRelease, context with { MfaSatisfied = true });
        allowed.Allowed.Should().BeTrue();
    }

    [Fact]
    public void ImmutableAuditLog_UsesHashChainIntegrity()
    {
        var audit = new ImmutableAuditLogService();
        var context = new AccessContext("alice", UserRole.Admin, "ops", "entity", "10.0.0.1", "device-1", "corr-1", true, ["bob"]);

        audit.Append(SensitiveActionType.RuleEdit, context, "rule:1", new { limit = 1 }, new { limit = 2 });
        audit.Append(SensitiveActionType.OverrideApproval, context, "override:2", new { state = "pending" }, new { state = "approved" });

        audit.VerifyIntegrity().Should().BeTrue();
        audit.GetAll().Should().HaveCount(2);
    }
}
