using FluentAssertions;
using Meridian.Contracts.Tenancy;

namespace Meridian.Tests.Storage;

/// <summary>
/// SEC-005 slice 4c (fund-account child partitioning): the child-write tenant guard decides whether a
/// balance / statement / reconciliation / sync / margin write may proceed and which tenant to stamp on
/// the child rows. The Postgres fund-account suite is skipped without a database, so this pure decision
/// is the load-bearing CI safeguard: the highest-risk regression is refusing a legitimate single-company
/// (tenantless) write, so those fail-open branches are proven here.
/// </summary>
public sealed class FundAccountChildWriteTenantGuardTests
{
    // ── Tenantless caller: always fail-open (single-company / legacy admin) ──────────

    [Fact]
    public void TenantlessCaller_MissingAccount_Allowed_WithNullStamp()
    {
        var decision = FundAccountChildWriteTenantGuard.Decide(
            accountExists: false, accountTenant: null, callerTenant: null);

        decision.Allowed.Should().BeTrue("a tenantless caller preserves the pre-existing orphan-insert allowance");
        decision.TenantStamp.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TenantlessCaller_InheritsAccountStamp(string? callerTenant)
    {
        var decision = FundAccountChildWriteTenantGuard.Decide(
            accountExists: true, accountTenant: "  Fund-B  ", callerTenant: callerTenant);

        decision.Allowed.Should().BeTrue();
        decision.TenantStamp.Should().Be("Fund-B", "the child inherits the owning account's stamp, trimmed");
    }

    [Fact]
    public void TenantlessCaller_UnstampedAccount_Allowed_WithNullStamp()
    {
        var decision = FundAccountChildWriteTenantGuard.Decide(
            accountExists: true, accountTenant: null, callerTenant: null);

        decision.Allowed.Should().BeTrue();
        decision.TenantStamp.Should().BeNull();
    }

    // ── Tenant-scoped caller: fail-closed on missing/foreign, else stamp caller ──────

    [Fact]
    public void ScopedCaller_MissingAccount_Refused()
    {
        var decision = FundAccountChildWriteTenantGuard.Decide(
            accountExists: false, accountTenant: null, callerTenant: "fund-a");

        decision.Allowed.Should().BeFalse("a scoped caller cannot write child rows to an account it cannot see");
    }

    [Fact]
    public void ScopedCaller_ForeignAccount_Refused()
    {
        var decision = FundAccountChildWriteTenantGuard.Decide(
            accountExists: true, accountTenant: "fund-b", callerTenant: "fund-a");

        decision.Allowed.Should().BeFalse();
    }

    [Fact]
    public void ScopedCaller_OwnAccount_Allowed_StampsCaller()
    {
        var decision = FundAccountChildWriteTenantGuard.Decide(
            accountExists: true, accountTenant: "fund-a", callerTenant: "fund-a");

        decision.Allowed.Should().BeTrue();
        decision.TenantStamp.Should().Be("fund-a");
    }

    [Theory]
    [InlineData("Fund-A", "  fund-a  ")]
    [InlineData("  FUND-A", "fund-a")]
    public void ScopedCaller_OwnAccount_MatchIsTrimmedAndCaseInsensitive(string accountTenant, string callerTenant)
    {
        var decision = FundAccountChildWriteTenantGuard.Decide(
            accountExists: true, accountTenant: accountTenant, callerTenant: callerTenant);

        decision.Allowed.Should().BeTrue("the tenant match mirrors TenantReadPredicate — trimmed, case-insensitive");
        decision.TenantStamp.Should().Be(callerTenant.Trim());
    }

    [Fact]
    public void ScopedCaller_UnstampedLegacyAccount_Allowed_StampsCallerForward()
    {
        var decision = FundAccountChildWriteTenantGuard.Decide(
            accountExists: true, accountTenant: null, callerTenant: "  fund-a  ");

        decision.Allowed.Should().BeTrue("an unstamped legacy account is fail-open and visible to the scoped caller");
        decision.TenantStamp.Should().Be("fund-a", "the child is stamped with the caller so the aggregate partitions forward");
    }
}
