using FluentAssertions;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Covers the fund-scope filtering on the cross-strategy aggregate portfolio read: per-run
/// contributions for fund accounts outside the caller's scoped authority are removed and
/// the position aggregates recomputed, so one fund's operator cannot read another fund's
/// holdings through the aggregation surface.
/// </summary>
public sealed class WorkstationPortfolioAggregationScopeTests
{
    private static readonly Guid AllowedAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DeniedAccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static HttpContext CreateContext(UserPermission permissions)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IScopedAuthorizationService>(new ViewTradesScopedAuthorizationService(AllowedAccountId));
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        context.Items[LoginSessionMiddleware.CurrentUserKey] = "fund-viewer";
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
        return context;
    }

    private static AggregatedPosition PositionWith(params RunPositionContribution[] contributions)
    {
        var totalQuantity = contributions.Sum(static c => c.Quantity);
        return new AggregatedPosition(
            Symbol: "AAPL",
            TotalQuantity: totalQuantity,
            LongQuantity: contributions.Where(static c => c.Quantity > 0).Sum(static c => c.Quantity),
            ShortQuantity: contributions.Where(static c => c.Quantity < 0).Sum(static c => Math.Abs(c.Quantity)),
            WeightedAverageCost: totalQuantity != 0m
                ? contributions.Sum(static c => c.Quantity * c.CostBasis) / totalQuantity
                : 0m,
            TotalUnrealisedPnl: contributions.Sum(static c => c.UnrealisedPnl),
            Contributions: contributions);
    }

    private static RunPositionContribution Contribution(string accountId, decimal quantity, decimal costBasis = 100m) =>
        new(RunId: $"run-{accountId}", AccountId: accountId, Quantity: quantity, CostBasis: costBasis, UnrealisedPnl: quantity);

    [Fact]
    public async Task Filter_RemovesUnauthorizedFundContributions_AndRecomputesAggregates()
    {
        var positions = new[]
        {
            PositionWith(
                Contribution(AllowedAccountId.ToString("D"), quantity: 100m),
                Contribution(DeniedAccountId.ToString("D"), quantity: 500m),
                Contribution("default", quantity: 10m))
        };

        var filtered = await WorkstationEndpoints.FilterToAuthorizedAccountsAsync(
            CreateContext(UserPermission.ViewTrades), positions);

        var position = filtered.Should().ContainSingle().Subject;
        position.Contributions.Should().ContainSingle("the denied fund is removed, and the shared execution book "
            + "it could have routed through is not readable by a caller scoped to one fund");
        position.Contributions.Should().OnlyContain(c => c.AccountId == AllowedAccountId.ToString("D"));
        position.TotalQuantity.Should().Be(100m, "aggregates are recomputed so totals never leak the filtered fund's size");
        position.LongQuantity.Should().Be(100m);
        position.TotalUnrealisedPnl.Should().Be(100m);
    }

    [Fact]
    public async Task Filter_HidesTheSharedExecutionBook_EvenWithNoFundKeyedContribution()
    {
        // The production fill path records every fund-scoped order under the non-Guid
        // "default" execution account, so an aggregation of ordinary fills has no Guid
        // contributions at all. An "authorized for every fund present" test is vacuously
        // true exactly then — precisely when the shared book is most likely to hold another
        // fund's flow — so a scoped caller must not see it.
        var positions = new[]
        {
            PositionWith(
                Contribution(AllowedAccountId.ToString("D"), quantity: 100m),
                Contribution("default", quantity: 10m))
        };

        var filtered = await WorkstationEndpoints.FilterToAuthorizedAccountsAsync(
            CreateContext(UserPermission.ViewTrades), positions);

        var position = filtered.Should().ContainSingle().Subject;
        position.Contributions.Should().ContainSingle()
            .Which.AccountId.Should().Be(AllowedAccountId.ToString("D"));
        position.TotalQuantity.Should().Be(100m);
    }

    [Fact]
    public async Task Filter_HidesRunLocalPositionsFromScopedCallers()
    {
        var positions = new[]
        {
            PositionWith(
                Contribution("paper-run", quantity: 100m),
                Contribution("other-run", quantity: 10m))
        };

        var filtered = await WorkstationEndpoints.FilterToAuthorizedAccountsAsync(
            CreateContext(UserPermission.ViewTrades), positions);

        filtered.Should().BeEmpty("nothing in an unattributable book is provably the caller's");
    }

    [Fact]
    public async Task Filter_AdminMaintenance_StillSeesTheSharedExecutionBook()
    {
        var positions = new[]
        {
            PositionWith(
                Contribution("default", quantity: 100m),
                Contribution("paper-run", quantity: 10m))
        };

        var filtered = await WorkstationEndpoints.FilterToAuthorizedAccountsAsync(
            CreateContext(UserPermission.ViewTrades | UserPermission.AdminMaintenance), positions);

        filtered.Should().ContainSingle().Which.Contributions.Should().HaveCount(
            2,
            "admin authority spans every fund, so the shared book reveals nothing new");
    }

    [Fact]
    public async Task Filter_DropsPositionsWithNoSurvivingContributions()
    {
        var positions = new[] { PositionWith(Contribution(DeniedAccountId.ToString("D"), quantity: 500m)) };

        var filtered = await WorkstationEndpoints.FilterToAuthorizedAccountsAsync(
            CreateContext(UserPermission.ViewTrades), positions);

        filtered.Should().BeEmpty("a position visible only through an unauthorized fund must vanish entirely");
    }

    [Fact]
    public async Task Filter_AdminMaintenance_SeesTheFullAggregation()
    {
        var positions = new[]
        {
            PositionWith(
                Contribution(AllowedAccountId.ToString("D"), quantity: 100m),
                Contribution(DeniedAccountId.ToString("D"), quantity: 500m))
        };

        var filtered = await WorkstationEndpoints.FilterToAuthorizedAccountsAsync(
            CreateContext(UserPermission.ViewTrades | UserPermission.AdminMaintenance), positions);

        filtered.Should().ContainSingle().Which.Contributions.Should().HaveCount(2);
    }

    [Fact]
    public void ExposureReport_WithOffsettingLotsAcrossRuns_ReportsPositiveGrossExposure()
    {
        // Long 100 @ $100 and short 90 @ $200 in one symbol. Netting first (as the
        // aggregation service's signed weighted-average cost does) yields a negative
        // per-share cost and nonsensical exposure; per-contribution valuation gives
        // 10k + 18k = 28k gross and 10k - 18k = -8k net.
        var positions = new[]
        {
            PositionWith(
                Contribution("paper-run", quantity: 100m, costBasis: 100m),
                Contribution("other-run", quantity: -90m, costBasis: 200m))
        };

        var report = WorkstationEndpoints.BuildExposureReport(positions);

        report.GrossExposure.Should().Be(28_000m);
        report.NetExposure.Should().Be(-8_000m);
        report.Top5Concentrations.Should().ContainSingle().Which.Should().Be("AAPL");
    }

    [Fact]
    public async Task ExposureReport_BuiltFromScopedPositions_ExcludesUnauthorizedFunds()
    {
        var positions = new[]
        {
            PositionWith(
                Contribution(AllowedAccountId.ToString("D"), quantity: 100m, costBasis: 100m),
                Contribution(DeniedAccountId.ToString("D"), quantity: 500m, costBasis: 100m))
        };

        var scoped = await WorkstationEndpoints.FilterToAuthorizedAccountsAsync(
            CreateContext(UserPermission.ViewTrades), positions);
        var report = WorkstationEndpoints.BuildExposureReport(scoped);

        report.GrossExposure.Should().Be(10_000m, "the denied fund's $50k must not appear in the summary");
        report.NetExposure.Should().Be(10_000m);
    }

    private sealed class ViewTradesScopedAuthorizationService(Guid allowedAccountId) : IScopedAuthorizationService
    {
        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(
            string actor,
            UserPermission requiredPermission,
            AccessScopeKindDto scopeKind,
            Guid? scopeId,
            UserPermission globalPermissions,
            CancellationToken ct = default)
        {
            var allowed = requiredPermission == UserPermission.ViewTrades &&
                scopeKind == AccessScopeKindDto.Account &&
                scopeId == allowedAccountId;

            return Task.FromResult(new ScopedAuthorizationDecisionDto(
                allowed,
                actor,
                requiredPermission,
                scopeKind,
                scopeId,
                allowed ? "Scoped test grant." : "Scoped test denial."));
        }
    }
}
