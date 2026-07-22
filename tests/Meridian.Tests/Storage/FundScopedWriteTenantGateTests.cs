using FluentAssertions;
using Meridian.Contracts.Tenancy;

namespace Meridian.Tests.Storage;

/// <summary>
/// SEC-005 slice 4c-iii: the fund-scoped write gate is a deliberate, deployment-gated contract change,
/// so its three-way decision (allow / warn-and-allow / deny) is isolated and unit-tested. The
/// load-bearing case is that a tenantless caller is NEVER denied while enforcement is off — that keeps
/// the single-company legacy admin working until a deployment explicitly opts in.
/// </summary>
public sealed class FundScopedWriteTenantGateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TenantScopedCaller_AlwaysAllowed_RegardlessOfEnforcement(bool enforce)
        => FundScopedWriteTenantGate.Decide(hasTenantScope: true, enforce)
            .Should().Be(FundScopedWriteTenantDecision.Allow);

    [Fact]
    public void TenantlessCaller_EnforcementOff_WarnsButAllows()
        => FundScopedWriteTenantGate.Decide(hasTenantScope: false, enforce: false)
            .Should().Be(FundScopedWriteTenantDecision.WarnAndAllow,
                "detection-first: the tenantless legacy admin must keep working until a deployment opts in");

    [Fact]
    public void TenantlessCaller_EnforcementOn_Denied()
        => FundScopedWriteTenantGate.Decide(hasTenantScope: false, enforce: true)
            .Should().Be(FundScopedWriteTenantDecision.Deny);
}
