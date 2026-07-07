using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.FundAccounts;
using Xunit;

namespace Meridian.Tests.Identity;

/// <summary>
/// Operator scenario: scoped authorization walks a fund's ownership lineage so a grant at a parent
/// scope authorizes a child. Each ancestor must be attributed its own least-privilege scope kind;
/// an ancestor must never be surfaced as the highest-privilege <see cref="AccessScopeKindDto.Global"/>
/// scope, which would widen authority on the lineage path.
/// </summary>
public sealed class FundStructureAccessScopeLineageProviderTests
{
    [Fact]
    public async Task ResolveLineageAsync_MapsEachAncestorToItsOwnScopeKindNeverGlobal()
    {
        var fundStructure = new InMemoryFundStructureService(new InMemoryFundAccountService());
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        var orgId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var fundId = Guid.NewGuid();

        await fundStructure.CreateOrganizationAsync(new CreateOrganizationRequest(orgId, "ORG", "Organization", "USD", now, "test"));
        await fundStructure.CreateBusinessAsync(new CreateBusinessRequest(businessId, orgId, BusinessKindDto.FundManager, "BUS", "Business", "USD", now, "test"));
        await fundStructure.CreateFundAsync(new CreateFundRequest(fundId, "FUND-A", "Fund A", "USD", now, "test", BusinessId: businessId));

        var provider = new FundStructureAccessScopeLineageProvider(fundStructure);

        var lineage = await provider.ResolveLineageAsync(AccessScopeKindDto.Fund, fundId);

        lineage.Should().Contain(new AccessScopeRef(AccessScopeKindDto.Business, businessId));
        lineage.Should().Contain(new AccessScopeRef(AccessScopeKindDto.Organization, orgId));
        lineage.Should().NotContain(scope => scope.ScopeKind == AccessScopeKindDto.Global,
            "an ancestor node must never be surfaced as the highest-privilege Global scope");
    }

    [Fact]
    public async Task ResolveLineageAsync_ForGlobalScope_ReturnsEmpty()
    {
        var fundStructure = new InMemoryFundStructureService(new InMemoryFundAccountService());
        var provider = new FundStructureAccessScopeLineageProvider(fundStructure);

        var lineage = await provider.ResolveLineageAsync(AccessScopeKindDto.Global, Guid.NewGuid());

        lineage.Should().BeEmpty();
    }
}
