using FluentAssertions;
using Meridian.Contracts.Tenancy;

namespace Meridian.Tests.Storage;

/// <summary>
/// SEC-005 slice 4c-ii and W9-GOV-008 criterion 2. The behavioural SQL is proven in the DB-gated
/// Postgres suites, which CI skips for want of a database, so these pure-logic tests are the only
/// place the apply/normalize/refuse decision is exercised in CI — and that decision is exactly what
/// determines whether a row is served, hidden, or refused.
/// </summary>
/// <remarks>
/// Both failure directions are severe and they pull opposite ways. Over-filtering makes legitimate
/// single-company data vanish; under-filtering is the cross-tenant read the criterion exists to
/// close. The tests below therefore pin both postures explicitly rather than only the tightened one.
/// </remarks>
public sealed class TenantReadPredicateTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ShouldFilter_IsFalse_ForTenantlessCaller(string? callerTenantId)
        => TenantReadPredicate.ShouldFilter(callerTenantId).Should()
            .BeFalse("a tenantless caller has no tenant to scope by");

    [Theory]
    [InlineData("tenant-a")]
    [InlineData("  tenant-a  ")]
    [InlineData("TENANT-A")]
    public void ShouldFilter_IsTrue_ForResolvedTenant(string callerTenantId)
        => TenantReadPredicate.ShouldFilter(callerTenantId).Should()
            .BeTrue("a caller with a resolved tenant must scope its reads");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldRejectRead_IsTrue_ForATenantlessCallerUnderFailClosed(string? callerTenantId)
        => TenantReadPredicate.ShouldRejectRead(callerTenantId, TenantScopeEnforcementMode.FailClosed)
            .Should().BeTrue(
                "an unresolvable scope is rejected rather than defaulted — an empty result set is a "
                + "default the caller cannot tell apart from genuinely having no rows");

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void ShouldRejectRead_IsFalse_ForATenantlessCallerUnderTheDeploymentBoundary(string? callerTenantId)
        => TenantReadPredicate.ShouldRejectRead(callerTenantId, TenantScopeEnforcementMode.DeploymentBoundary)
            .Should().BeFalse("the single-company deployment and the legacy tenantless admin still read");

    [Theory]
    [InlineData(TenantScopeEnforcementMode.DeploymentBoundary)]
    [InlineData(TenantScopeEnforcementMode.FailClosed)]
    public void ShouldRejectRead_IsFalse_ForAResolvedTenantUnderEitherPosture(TenantScopeEnforcementMode mode)
        => TenantReadPredicate.ShouldRejectRead("tenant-a", mode).Should().BeFalse();

    [Theory]
    [InlineData("tenant-a", "tenant-a")]
    [InlineData("  tenant-a  ", "tenant-a")]
    [InlineData("\tTenant-A\n", "Tenant-A")]
    public void NormalizeParameter_TrimsToMatchWriteSideStamp(string callerTenantId, string expected)
        => TenantReadPredicate.NormalizeParameter(callerTenantId).Should().Be(expected);

    [Fact]
    public void FilterClause_ServesUnattributedRows_UnderTheDeploymentBoundary()
    {
        var clause = TenantReadPredicate.FilterClause("tenant_id", TenantScopeEnforcementMode.DeploymentBoundary);

        // A NULL (unbound or legacy) row must still pass: this is the posture that keeps an
        // un-attributed database readable while the backfill is being run and reviewed.
        clause.Should().Contain("tenant_id is null");
        clause.Should().Contain("lower(trim(tenant_id)) = lower(trim(@caller_tenant))");
        clause.TrimStart().Should().StartWith("and (", "the clause concatenates onto a where chain");
    }

    [Fact]
    public void FilterClause_DropsTheNullDisjunct_UnderFailClosed()
    {
        var clause = TenantReadPredicate.FilterClause("tenant_id", TenantScopeEnforcementMode.FailClosed);

        // The whole point of the tightening. Leaving "is null or" behind here would satisfy every
        // other test in this file and still serve every unattributed row to every scoped caller.
        clause.Should().NotContain("is null");
        clause.Should().Contain("lower(trim(tenant_id)) = lower(trim(@caller_tenant))");
        clause.TrimStart().Should().StartWith("and (");
    }

    [Theory]
    [InlineData(TenantScopeEnforcementMode.DeploymentBoundary)]
    [InlineData(TenantScopeEnforcementMode.FailClosed)]
    public void FilterClause_ComparesCaseInsensitivelyOnBothSides(TenantScopeEnforcementMode mode)
        // A one-sided lower(trim()) would silently drop legitimately-owned rows, and it would do so
        // only for tenants whose stored casing happened to differ — the worst kind of intermittent.
        => TenantReadPredicate.FilterClause("p.tenant_id", mode).Should()
            .Contain("lower(trim(p.tenant_id)) = lower(trim(@caller_tenant))");

    [Fact]
    public void PeriodExistsClause_ServesUnattributedPeriods_UnderTheDeploymentBoundary()
    {
        var clause = TenantReadPredicate.PeriodExistsClause(
            "acct.accounting_periods", "je.period_id", TenantScopeEnforcementMode.DeploymentBoundary);

        clause.TrimStart().Should().StartWith("and exists (select 1 from acct.accounting_periods");
        clause.Should().Contain("tenant_period.period_id = je.period_id");
        clause.Should().Contain("tenant_period.tenant_id is null");
        clause.Should().Contain("lower(trim(tenant_period.tenant_id)) = lower(trim(@caller_tenant))");
    }

    [Fact]
    public void PeriodExistsClause_DropsTheNullDisjunct_UnderFailClosed()
    {
        var clause = TenantReadPredicate.PeriodExistsClause(
            "acct.accounting_periods", "je.period_id", TenantScopeEnforcementMode.FailClosed);

        clause.Should().NotContain("is null");
        clause.Should().Contain("tenant_period.period_id = je.period_id");
        clause.Should().Contain("lower(trim(tenant_period.tenant_id)) = lower(trim(@caller_tenant))");
    }

    [Fact]
    public void ParameterName_IsStable()
        => TenantReadPredicate.ParameterName.Should().Be("caller_tenant");
}

/// <summary>
/// The deployment switch that selects the tenant-scope posture (W9-GOV-008 criterion 2).
/// </summary>
/// <remarks>
/// Absent and misspelled are deliberately different answers. Saying nothing is a deployment that has
/// not chosen, so it inherits the default. Saying something unrecognised is a deployment that HAS
/// chosen and been misheard — and since the default is the open posture, quietly falling back would
/// start a shared deployment with its data exposed, by an operator who believed they had closed it.
/// </remarks>
public sealed class TenantScopeEnforcementOptionsTests
{
    [Theory]
    [InlineData("fail-closed")]
    [InlineData("failclosed")]
    [InlineData("closed")]
    [InlineData("strict")]
    [InlineData("  Fail-Closed  ")]
    public void ARecognisedFailClosedValue_SelectsFailClosed(string value)
        => TenantScopeEnforcementOptions
            .FromEnvironmentValue(value, TenantScopeEnforcementOptions.DeploymentBoundary)
            .IsFailClosed.Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnAbsentValue_KeepsTheFallback(string? value)
        => TenantScopeEnforcementOptions
            .FromEnvironmentValue(value, TenantScopeEnforcementOptions.FailClosed)
            .IsFailClosed.Should().BeTrue("an unset switch inherits the deployment's default");

    [Theory]
    [InlineData("fail_closed")]
    [InlineData("failclosd")]
    [InlineData("true")]
    [InlineData("enabled")]
    public void APresentButUnrecognisedValue_IsRefusedRatherThanDowngraded(string value)
    {
        // The dangerous direction: silently falling back here hands the operator the OPEN posture
        // while they believe they closed it.
        var parse = () => TenantScopeEnforcementOptions
            .FromEnvironmentValue(value, TenantScopeEnforcementOptions.DeploymentBoundary);

        parse.Should().Throw<ArgumentException>().WithMessage($"*{value}*");
    }
}

