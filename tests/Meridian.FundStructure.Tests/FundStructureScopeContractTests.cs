using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Entities.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;
using Xunit;

namespace Meridian.FundStructure.Tests;

/// <summary>
/// One contract, run against both <see cref="IFundStructureService"/> implementations.
/// </summary>
/// <remarks>
/// <para>
/// Both implementations ship. `StorageFeatureRegistration` selects Postgres or in-memory on whether
/// a connection string is configured, and the WPF desktop workstation registers the in-memory one
/// unconditionally — so these are two shipping products, not a production path and a test double.
/// They also carry 25 same-named private helpers, of which six differ structurally.
/// </para>
/// <para>
/// The specific question these tests exist to answer (#2612): the Postgres service's visible scope
/// cascade begins at fund level, while the in-memory service additionally narrows businesses by
/// organization and clients and funds by business. If Postgres does not apply that upper-level
/// narrowing, an organization-scoped caller can see records belonging to another organization.
/// These assertions state what both implementations must do, so the answer is a test result rather
/// than an argument about which file to read.
/// </para>
/// </remarks>
public abstract class FundStructureScopeContractTests
{
    private static readonly DateTimeOffset EffectiveFrom = new(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);

    protected abstract IFundStructureService CreateService();

    /// <summary>Two organizations, each owning one business, one client, and one fund.</summary>
    private sealed record Fixture(
        Guid OrganizationA, Guid BusinessA, Guid ClientA, Guid FundA,
        Guid OrganizationB, Guid BusinessB, Guid ClientB, Guid FundB);

    private static async Task<Fixture> SeedAsync(IFundStructureService service)
    {
        var f = new Fixture(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        foreach (var (org, business, client, fund, tag) in new[]
                 {
                     (f.OrganizationA, f.BusinessA, f.ClientA, f.FundA, "A"),
                     (f.OrganizationB, f.BusinessB, f.ClientB, f.FundB, "B"),
                 })
        {
            await service.CreateOrganizationAsync(new CreateOrganizationRequest(
                org, $"ORG-{tag}", $"Organization {tag}", "USD", EffectiveFrom, "contract-test"));

            await service.CreateBusinessAsync(new CreateBusinessRequest(
                business, org, BusinessKindDto.FundManager,
                $"BUS-{tag}", $"Business {tag}", "USD", EffectiveFrom, "contract-test"));

            await service.CreateClientAsync(new CreateClientRequest(
                client, business, $"CLI-{tag}", $"Client {tag}", "USD", EffectiveFrom, "contract-test"));

            await service.CreateFundAsync(new CreateFundRequest(
                fund, $"FND-{tag}", $"Fund {tag}", "USD", EffectiveFrom, "contract-test",
                Description: null, BusinessId: business));
        }

        return f;
    }

    [Fact]
    public async Task OrganizationScopedQuery_ExcludesBusinessesOwnedByAnotherOrganization()
    {
        var service = CreateService();
        var fixture = await SeedAsync(service);

        var graph = await service.GetOrganizationStructureAsync(
            new OrganizationStructureQuery(OrganizationId: fixture.OrganizationA));

        var businessIds = graph.Businesses.Select(b => b.BusinessId).ToList();
        Assert.Contains(fixture.BusinessA, businessIds);
        // A caller scoped to one organization must not see a business owned by another.
        Assert.DoesNotContain(fixture.BusinessB, businessIds);
    }

    [Fact]
    public async Task OrganizationScopedQuery_ExcludesClientsOwnedByAnotherOrganization()
    {
        var service = CreateService();
        var fixture = await SeedAsync(service);

        var graph = await service.GetOrganizationStructureAsync(
            new OrganizationStructureQuery(OrganizationId: fixture.OrganizationA));

        // Clients belong to a business, which belongs to an organization, so the scope must cascade.
        Assert.DoesNotContain(fixture.ClientB, graph.Clients.Select(c => c.ClientId).ToList());
    }

    [Fact]
    public async Task OrganizationScopedQuery_ExcludesFundsOwnedByAnotherOrganization()
    {
        var service = CreateService();
        var fixture = await SeedAsync(service);

        var graph = await service.GetOrganizationStructureAsync(
            new OrganizationStructureQuery(OrganizationId: fixture.OrganizationA));

        // A fund attached to another organization's business is outside the requested scope.
        Assert.DoesNotContain(fixture.FundB, graph.Funds.Select(x => x.FundId).ToList());
    }

    [Fact]
    public async Task BusinessScopedQuery_ExcludesClientsOwnedByAnotherBusiness()
    {
        var service = CreateService();
        var fixture = await SeedAsync(service);

        var graph = await service.GetOrganizationStructureAsync(
            new OrganizationStructureQuery(BusinessId: fixture.BusinessA));

        var clientIds = graph.Clients.Select(c => c.ClientId).ToList();
        Assert.Contains(fixture.ClientA, clientIds);
        // A caller scoped to one business must not see another business's clients.
        Assert.DoesNotContain(fixture.ClientB, clientIds);
    }

    [Fact]
    public async Task UnscopedQuery_ReturnsBothOrganizations()
    {
        // Guards the assertions above: if the scoped queries returned nothing at all they would pass
        // vacuously, so pin that the fixture really did produce two organizations' worth of records.
        var service = CreateService();
        var fixture = await SeedAsync(service);

        var graph = await service.GetOrganizationStructureAsync(new OrganizationStructureQuery());

        var organizationIds = graph.Organizations.Select(o => o.OrganizationId).ToList();
        Assert.Contains(fixture.OrganizationA, organizationIds);
        Assert.Contains(fixture.OrganizationB, organizationIds);
        var allBusinessIds = graph.Businesses.Select(b => b.BusinessId).ToList();
        Assert.Contains(fixture.BusinessA, allBusinessIds);
        Assert.Contains(fixture.BusinessB, allBusinessIds);
    }
}

/// <summary>The contract as enforced by the implementation the WPF desktop workstation ships.</summary>
public sealed class InMemoryFundStructureScopeContractTests : FundStructureScopeContractTests
{
    protected override IFundStructureService CreateService()
        => new InMemoryFundStructureService(new InMemoryFundAccountService());
}

/// <summary>
/// The same contract as enforced by the implementation a Postgres-configured deployment ships.
/// </summary>
/// <remarks>
/// Backed by <see cref="FakeFundStructureStore"/> rather than PostgreSQL. The rules under test are
/// service-layer — <c>PostgresFundStructureStore</c> does no cross-entity filtering — so this
/// exercises the real scoping code, and it runs everywhere instead of skipping wherever no database
/// is available.
/// </remarks>
public sealed class PostgresFundStructureScopeContractTests : FundStructureScopeContractTests
{
    protected override IFundStructureService CreateService()
        => new PostgresFundStructureService(
            new FakeFundStructureStore(),
            new InMemoryFundAccountService(),
            new FundStructurePolicyService());
}
