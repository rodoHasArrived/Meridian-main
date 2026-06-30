using FluentAssertions;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

/// <summary>
/// SEC-005 slice 4c-ii: the highest-risk regression in the tenant read predicates is over-filtering —
/// a predicate wrongly applied to a tenantless caller, or a normalization mismatch, would make
/// legitimate single-company data vanish. The behavioral SQL is proven in the DB-gated Postgres suite
/// (skipped in CI without a database), so these pure-logic tests guard the apply/normalize decision in
/// CI, which is exactly the branch that determines whether a row is fail-open or scoped.
/// </summary>
public sealed class TenantReadPredicateTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ShouldFilter_IsFalse_ForTenantlessCaller(string? callerTenantId)
        => TenantReadPredicate.ShouldFilter(callerTenantId).Should()
            .BeFalse("a tenantless caller must not be tenant-scoped — every row stays fail-open");

    [Theory]
    [InlineData("tenant-a")]
    [InlineData("  tenant-a  ")]
    [InlineData("TENANT-A")]
    public void ShouldFilter_IsTrue_ForResolvedTenant(string callerTenantId)
        => TenantReadPredicate.ShouldFilter(callerTenantId).Should()
            .BeTrue("a caller with a resolved tenant must scope its reads");

    [Theory]
    [InlineData("tenant-a", "tenant-a")]
    [InlineData("  tenant-a  ", "tenant-a")]
    [InlineData("\tTenant-A\n", "Tenant-A")]
    public void NormalizeParameter_TrimsToMatchWriteSideStamp(string callerTenantId, string expected)
        => TenantReadPredicate.NormalizeParameter(callerTenantId).Should().Be(expected);

    [Fact]
    public void FilterClause_IsFailOpen_AndCaseInsensitiveOnBothSides()
    {
        var clause = TenantReadPredicate.FilterClause("tenant_id");

        // Fail-open: a NULL (unbound/legacy) row must still pass.
        clause.Should().Contain("tenant_id is null");
        // Case-insensitive, trimmed comparison on BOTH sides, matching FundProfileOwnership.IsHeldBy and
        // the write-side stamp — a one-sided lower(trim()) would silently drop legitimately-owned rows.
        clause.Should().Contain("lower(trim(tenant_id)) = lower(trim(@caller_tenant))");
        clause.TrimStart().Should().StartWith("and (", "the clause concatenates onto a where chain");
    }

    [Fact]
    public void FilterClause_QualifiesTheGivenColumnExpression()
        => TenantReadPredicate.FilterClause("p.tenant_id").Should()
            .Contain("lower(trim(p.tenant_id)) = lower(trim(@caller_tenant))");

    [Fact]
    public void PeriodExistsClause_ScopesByPeriodTenant_FailOpen()
    {
        var clause = TenantReadPredicate.PeriodExistsClause("acct.accounting_periods", "je.period_id");

        clause.TrimStart().Should().StartWith("and exists (select 1 from acct.accounting_periods");
        clause.Should().Contain("tenant_period.period_id = je.period_id");
        clause.Should().Contain("tenant_period.tenant_id is null");
        clause.Should().Contain("lower(trim(tenant_period.tenant_id)) = lower(trim(@caller_tenant))");
    }

    [Fact]
    public void ParameterName_IsStable()
        => TenantReadPredicate.ParameterName.Should().Be("caller_tenant");
}
