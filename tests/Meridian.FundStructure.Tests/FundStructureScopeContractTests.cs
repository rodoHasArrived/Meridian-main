using System.Text.Json;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Entities.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;
using Xunit;

namespace Meridian.FundStructure.Tests;

/// <summary>
/// One domain-filtering contract exercised through both service implementations. The PostgreSQL
/// runner uses a store double: this proves shared service behavior, not database or tenant authority.
/// Tenant admission and production/local composition have separate contracts.
/// </summary>
public abstract partial class FundStructureScopeContractTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 01, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EffectiveFrom = AsOf.AddDays(-1);

    protected abstract IFundStructureService CreateService(
        IFundAccountService accounts, IGovernanceSharedDataAccessService? metadata = null);

    // Organizations have no update command. Seed persisted DTOs through each adapter to exercise
    // independent IsActive and EffectiveTo fields without reflection or a new production API.
    protected abstract Task<IFundStructureService> CreateFromSnapshotAsync(
        OrganizationStructureGraphDto snapshot, IFundAccountService accounts,
        IGovernanceSharedDataAccessService? metadata = null);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrganizationScopedQuery_ReturnsOnlyMatchingDescendantsAndAccounts(bool withMetadata)
    {
        var fixture = await SeedAsync(withMetadata);
        var views = await QueryAsync(fixture.Service, fixture.A.OrganizationId);

        AssertScope(views, [fixture.A.OrganizationId], fixture.A);
        Assert.Equal(fixture.Metadata?.Value, views.Graph.SharedDataAccess);
        Assert.Equal(fixture.Metadata?.Value, views.Accounting.SharedDataAccess);
        Assert.All(views.Graph.Accounts,
            account => Assert.Equal(fixture.Metadata?.Value, account.SharedDataAccess));
        Assert.All(views.Graph.InvestmentPortfolios,
            portfolio => Assert.Equal(fixture.Metadata?.Value, portfolio.SharedDataAccess));
    }

    [Fact]
    public async Task BusinessScopedQuery_ReturnsOnlyMatchingDescendants()
    {
        var fixture = await SeedAsync();
        var views = await QueryAsync(fixture.Service, businessId: fixture.A.BusinessId);

        AssertScope(views, [fixture.A.OrganizationId, fixture.B.OrganizationId], fixture.A);
    }

    [Fact]
    public async Task UnscopedQuery_ReturnsBothOrganizationsAndTheirDescendants()
    {
        var fixture = await SeedAsync();
        var views = await QueryAsync(fixture.Service);

        AssertScope(views, [fixture.A.OrganizationId, fixture.B.OrganizationId], fixture.A, fixture.B);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownOrganization_DoesNotBecomeAnUnscopedQuery(bool withMetadata)
    {
        var fixture = await SeedAsync(withMetadata);
        AssertScope(await QueryAsync(fixture.Service),
            [fixture.A.OrganizationId, fixture.B.OrganizationId], fixture.A, fixture.B);

        foreach (var unknownOrganization in new[] { Guid.NewGuid(), Guid.Empty })
        {
            AssertScope(await QueryAsync(fixture.Service, unknownOrganization), []);
            AssertScope(await QueryAsync(fixture.Service, unknownOrganization, fixture.A.BusinessId), []);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConflictingOrganizationAndBusinessFilters_RetainOnlyTheOrganizationRoot(bool unknownBusiness)
    {
        var fixture = await SeedAsync();
        AssertScope(await QueryAsync(fixture.Service),
            [fixture.A.OrganizationId, fixture.B.OrganizationId], fixture.A, fixture.B);

        var businessId = unknownBusiness ? Guid.NewGuid() : fixture.B.BusinessId;
        var views = await QueryAsync(fixture.Service, fixture.A.OrganizationId, businessId);

        AssertScope(views, [fixture.A.OrganizationId]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InactiveOrganization_RespectsActiveOnlyWithoutWideningTheScope(bool activeOnly)
    {
        var fixture = await SeedAsync();
        var snapshot = await SnapshotAsync(fixture);
        snapshot = snapshot with
        {
            Organizations = snapshot.Organizations.Select(organization =>
                organization.OrganizationId == fixture.A.OrganizationId
                    ? organization with { IsActive = false }
                    : organization).ToList()
        };
        var service = await CreateFromSnapshotAsync(snapshot, fixture.Accounts);
        var views = await QueryAsync(service, fixture.A.OrganizationId, activeOnly: activeOnly);

        if (activeOnly)
            AssertScope(views, []);
        else
        {
            AssertScope(views, [fixture.A.OrganizationId], fixture.A);
            Assert.False(Assert.Single(views.Graph.Organizations).IsActive);
        }

        await AssertBusinessViewsAsync(service, fixture.A, visible: !activeOnly,
            activeOnly: activeOnly, asOf: AsOf, organizationIsActive: false);
    }

    [Fact]
    public async Task InactiveOrganizations_DoNotActivateUnscopedMissingRootCompatibility()
    {
        var fixture = await SeedAsync();
        var snapshot = await SnapshotAsync(fixture);
        var orphan = snapshot.Businesses[0] with
        {
            BusinessId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            Code = "ORPHAN",
            Name = "Business without a retained organization",
            ClientIds = [],
            FundIds = [],
            InvestmentPortfolioIds = []
        };
        snapshot = snapshot with
        {
            Organizations = snapshot.Organizations.Select(organization => organization with { IsActive = false }).ToList(),
            Businesses = [.. snapshot.Businesses, orphan]
        };
        var service = await CreateFromSnapshotAsync(snapshot, fixture.Accounts);

        // The roots exist within the effective window. Disabling them must not turn a previously
        // excluded orphan business into an unscoped match through the missing-root compatibility path.
        AssertScope(await QueryAsync(service, activeOnly: false),
            [fixture.A.OrganizationId, fixture.B.OrganizationId], fixture.A, fixture.B);
        AssertScope(await QueryAsync(service, fixture.A.OrganizationId), []);
        var views = await QueryAsync(service);

        Assert.Empty(views.Graph.Organizations);
        Assert.Empty(views.Graph.Businesses);
        Assert.Empty(views.Graph.Clients);
        Assert.Empty(views.Graph.Funds);
        Assert.Empty(views.Graph.Sleeves);
        Assert.Empty(views.Graph.Vehicles);
        Assert.Empty(views.Graph.InvestmentPortfolios);
        Assert.All(views.Graph.Nodes,
            node => Assert.True(node.Kind is FundStructureNodeKindDto.Entity or FundStructureNodeKindDto.Account));
        Assert.Empty(views.Graph.OwnershipLinks);
        Assert.Empty(views.Graph.Assignments);
        // Unscoped account-derived entity visibility remains independent of this structural filter.
        Assert.NotEmpty(views.Graph.Entities);
        Assert.NotEmpty(views.Graph.Accounts);
        Assert.Null(views.Accounting.Organization);
        Assert.Null(views.Accounting.Business);
        Assert.Empty(views.Accounting.InvestmentPortfolios);
        Assert.Empty(views.Accounting.Accounts);
        Assert.Empty(views.Accounting.LedgerGroups);
        Assert.NotNull(views.Accounting.Assignments);
        Assert.Empty(views.Accounting.Assignments);
    }

    [Theory]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    public async Task OrganizationEffectiveWindow_IsInclusiveAndAppliesEvenWhenActiveOnlyIsFalse(
        bool activeOnly, int boundary)
    {
        var fixture = await SeedAsync();
        var snapshot = await SnapshotAsync(fixture);
        var end = AsOf.AddDays(1);
        snapshot = snapshot with
        {
            Organizations = snapshot.Organizations.Select(organization =>
                organization.OrganizationId == fixture.A.OrganizationId
                    ? organization with { EffectiveFrom = AsOf, EffectiveTo = end }
                    : organization).ToList()
        };
        var service = await CreateFromSnapshotAsync(snapshot, fixture.Accounts);
        var queryTime = boundary switch
        {
            -1 => AsOf.AddTicks(-1),
            0 => AsOf,
            1 => end,
            _ => end.AddTicks(1)
        };
        // Children and the other organization remain effective at all four instants. A failure
        // cannot be masked by independently filtering every descendant out of the fixture.
        var positive = await QueryAsync(service, fixture.B.OrganizationId, asOf: queryTime);
        Assert.Equal(fixture.B.BusinessId, Assert.Single(positive.Graph.Businesses).BusinessId);
        Assert.Contains(positive.Graph.Accounts, account => account.AccountId == fixture.B.DirectAccountId);
        var views = await QueryAsync(service, fixture.A.OrganizationId,
            activeOnly: activeOnly, asOf: queryTime);

        if (boundary is 0 or 1)
            AssertScope(views, [fixture.A.OrganizationId], fixture.A);
        else
            AssertScope(views, []);

        await AssertBusinessViewsAsync(service, fixture.A, visible: boundary is 0 or 1,
            activeOnly: activeOnly, asOf: queryTime);
    }

    [Fact]
    public async Task EmptyStructureSnapshot_DoesNotRecoverScopeFromAvailableAccounts()
    {
        var fixture = await SeedAsync();
        AssertScope(await QueryAsync(fixture.Service),
            [fixture.A.OrganizationId, fixture.B.OrganizationId], fixture.A, fixture.B);
        var empty = new OrganizationStructureGraphDto([], [], [], [], [], [], [], [], [], [], [], []);
        var service = await CreateFromSnapshotAsync(empty, fixture.Accounts);

        // The account dependency remains populated. These are empty, already-authorized structure
        // inputs, not a test of whether this service authenticates a tenant.
        Assert.NotEmpty(await fixture.Accounts.QueryAccountsAsync(new AccountStructureQuery()));
        AssertScope(await QueryAsync(service), []);
        AssertScope(await QueryAsync(service, fixture.A.OrganizationId), []);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnscopedQuery_PreservesPartialInputWithoutVisibleOrganizationRoots(bool missingOrganizations)
    {
        var fixture = await SeedAsync();
        var snapshot = await SnapshotAsync(fixture);
        snapshot = snapshot with
        {
            Organizations = missingOrganizations ? [] : snapshot.Organizations
                .Select(organization => organization with { EffectiveFrom = AsOf.AddDays(1) }).ToList()
        };
        var service = await CreateFromSnapshotAsync(snapshot, fixture.Accounts);

        // Keep the existing optional-filter behavior for already-authorized partial inputs.
        // A missing organization selector is different from a selector that matches no root.
        AssertScope(await QueryAsync(service), [], fixture.A, fixture.B);
        AssertScope(await QueryAsync(service, businessId: fixture.A.BusinessId), [], fixture.A);
        AssertScope(await QueryAsync(service, fixture.A.OrganizationId), []);
    }

    [Fact]
    public async Task UnscopedQuery_PreservesStandaloneEntityAccountWithoutAssigningItToAnOrganization()
    {
        var accounts = new InMemoryFundAccountService();
        var service = CreateService(accounts);
        var entityId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await service.CreateLegalEntityAsync(new CreateLegalEntityRequest(
            entityId, LegalEntityTypeDto.Vehicle, "STANDALONE", "Standalone entity", "US", "USD",
            EffectiveFrom, "contract-test"));
        await accounts.CreateAccountAsync(new CreateAccountRequest(
            accountId, AccountTypeDto.Custody, "STANDALONE", "Standalone account", "USD",
            EffectiveFrom, "contract-test", EntityId: entityId));

        var views = await QueryAsync(service);
        Assert.Empty(views.Graph.Organizations);
        Assert.Empty(views.Graph.Businesses);
        Assert.Equal(entityId, Assert.Single(views.Graph.Entities).EntityId);
        Assert.Equal(accountId, Assert.Single(views.Graph.Accounts).AccountId);
        AssertIds([entityId, accountId], views.Graph.Nodes.Select(node => node.NodeId));
        // Accounting separately requires a business/fund/portfolio relationship, even unscoped.
        Assert.Empty(views.Accounting.Accounts);
        Assert.Empty(views.Accounting.LedgerGroups);
        AssertScope(await QueryAsync(service, Guid.NewGuid()), []);
        AssertScope(await QueryAsync(service, businessId: Guid.NewGuid()), []);
    }

    private async Task<Fixture> SeedAsync(bool withMetadata = false)
    {
        var accounts = new InMemoryFundAccountService();
        var metadata = withMetadata ? new SharedDataAccess() : null;
        var fixture = new Fixture(CreateService(accounts, metadata), accounts, new Branch("A"), new Branch("B"), metadata);
        foreach (var branch in new[] { fixture.A, fixture.B })
        {
            var service = fixture.Service;
            await service.CreateOrganizationAsync(new CreateOrganizationRequest(
                branch.OrganizationId, $"ORG-{branch.Tag}", $"Organization {branch.Tag}", "USD", EffectiveFrom, "contract-test"));
            await service.CreateBusinessAsync(new CreateBusinessRequest(
                branch.BusinessId, branch.OrganizationId, BusinessKindDto.FundManager,
                $"BUS-{branch.Tag}", $"Business {branch.Tag}", "USD", EffectiveFrom, "contract-test"));
            await service.CreateClientAsync(new CreateClientRequest(
                branch.ClientId, branch.BusinessId, $"CLI-{branch.Tag}", $"Client {branch.Tag}", "USD", EffectiveFrom, "contract-test"));
            await service.CreateFundAsync(new CreateFundRequest(
                branch.FundId, $"FND-{branch.Tag}", $"Fund {branch.Tag}", "USD", EffectiveFrom, "contract-test", BusinessId: branch.BusinessId));
            await service.CreateSleeveAsync(new CreateSleeveRequest(
                branch.SleeveId, branch.FundId, $"SLV-{branch.Tag}", "Sleeve", EffectiveFrom, "contract-test"));
            foreach (var entityId in new[] { branch.EntityId, branch.AccountEntityId })
                await service.CreateLegalEntityAsync(new CreateLegalEntityRequest(
                    entityId, LegalEntityTypeDto.Vehicle, $"ENT-{entityId:N}", "Entity", "US", "USD", EffectiveFrom, "contract-test"));
            await service.CreateVehicleAsync(new CreateVehicleRequest(
                branch.VehicleId, branch.FundId, branch.EntityId, $"VEH-{branch.Tag}", "Vehicle", "USD", EffectiveFrom, "contract-test"));
            await service.CreateInvestmentPortfolioAsync(new CreateInvestmentPortfolioRequest(
                branch.PortfolioId, branch.BusinessId, $"PF-{branch.Tag}", "Portfolio", "USD", EffectiveFrom, "contract-test", FundId: branch.FundId));
            // This entity is reachable only through an otherwise in-scope account. Narrowing entity
            // seeds must preserve it; removing every account-derived entity would break this case.
            await accounts.CreateAccountAsync(new CreateAccountRequest(
                branch.DirectAccountId, AccountTypeDto.Custody, $"DIRECT-{branch.Tag}", "Direct account", "USD",
                EffectiveFrom, "contract-test", EntityId: branch.AccountEntityId, FundId: branch.FundId,
                LedgerReference: $"BOOK-{branch.Tag}"));
            await accounts.CreateAccountAsync(new CreateAccountRequest(
                branch.EntityAccountId, AccountTypeDto.Bank, $"ENTITY-{branch.Tag}", "Entity account", "USD",
                EffectiveFrom, "contract-test", EntityId: branch.EntityId, LedgerReference: $"BOOK-{branch.Tag}"));
            await accounts.CreateAccountAsync(new CreateAccountRequest(
                branch.LinkedAccountId, AccountTypeDto.Custody, $"LINKED-{branch.Tag}", "Linked account", "USD",
                EffectiveFrom, "contract-test", LedgerReference: "FALLBACK"));
            await service.LinkNodesAsync(new LinkFundStructureNodesRequest(
                branch.AccountLinkId, branch.PortfolioId, branch.LinkedAccountId,
                OwnershipRelationshipTypeDto.Operates, EffectiveFrom, "contract-test"));
            await service.AssignNodeAsync(new AssignFundStructureNodeRequest(
                branch.AssignmentId, branch.LinkedAccountId, "LedgerGroup", $"ASSIGNED-{branch.Tag}",
                EffectiveFrom, "contract-test", IsPrimary: true));
        }
        return fixture;
    }

    private static Task<OrganizationStructureGraphDto> SnapshotAsync(Fixture fixture) =>
        fixture.Service.GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: false, AsOf: AsOf));

    private static async Task<Views> QueryAsync(IFundStructureService service, Guid? organizationId = null,
        Guid? businessId = null, bool activeOnly = true, DateTimeOffset? asOf = null) => new(
            await service.GetOrganizationStructureAsync(new OrganizationStructureQuery(
                OrganizationId: organizationId, BusinessId: businessId, ActiveOnly: activeOnly, AsOf: asOf ?? AsOf)),
            await service.GetAccountingViewAsync(new AccountingStructureQuery(
                OrganizationId: organizationId, BusinessId: businessId, ActiveOnly: activeOnly, AsOf: asOf ?? AsOf)));

    private static async Task AssertBusinessViewsAsync(IFundStructureService service, Branch branch,
        bool visible, bool activeOnly, DateTimeOffset asOf, bool organizationIsActive = true)
    {
        var advisory = await service.GetAdvisoryViewAsync(new AdvisoryStructureQuery(
            branch.BusinessId, OrganizationId: branch.OrganizationId, ActiveOnly: activeOnly, AsOf: asOf));
        var operating = await service.GetFundOperatingViewAsync(new FundOperatingStructureQuery(
            branch.BusinessId, OrganizationId: branch.OrganizationId, ActiveOnly: activeOnly, AsOf: asOf));

        // Aggregate the two view assertions so a regression reports both mismatched read paths.
        Assert.All(new[]
        {
            (Organization: advisory?.Organization, Business: advisory?.Business),
            (Organization: operating?.Organization, Business: operating?.Business)
        }, view =>
        {
            if (!visible)
            {
                Assert.Null(view.Organization);
                Assert.Null(view.Business);
                return;
            }

            Assert.NotNull(view.Organization);
            Assert.NotNull(view.Business);
            Assert.Equal(branch.OrganizationId, view.Organization.OrganizationId);
            Assert.Equal(organizationIsActive, view.Organization.IsActive);
            Assert.Equal(branch.BusinessId, view.Business.BusinessId);
        });
    }

    private static void AssertScope(Views views, IReadOnlyList<Guid> organizationIds, params Branch[] branches)
    {
        var graph = views.Graph;
        var accounting = views.Accounting;
        AssertIds(organizationIds, graph.Organizations.Select(item => item.OrganizationId));
        AssertIds(branches.Select(item => item.BusinessId), graph.Businesses.Select(item => item.BusinessId));
        AssertIds(branches.Select(item => item.ClientId), graph.Clients.Select(item => item.ClientId));
        AssertIds(branches.Select(item => item.FundId), graph.Funds.Select(item => item.FundId));
        AssertIds(branches.Select(item => item.SleeveId), graph.Sleeves.Select(item => item.SleeveId));
        AssertIds(branches.Select(item => item.VehicleId), graph.Vehicles.Select(item => item.VehicleId));
        AssertIds(branches.SelectMany(item => new[] { item.EntityId, item.AccountEntityId }), graph.Entities.Select(item => item.EntityId));
        AssertIds(branches.Select(item => item.PortfolioId), graph.InvestmentPortfolios.Select(item => item.InvestmentPortfolioId));
        var accountIds = branches.SelectMany(item => item.AccountIds).ToList();
        AssertIds(accountIds, graph.Accounts.Select(item => item.AccountId));
        var nodeIds = organizationIds.Concat(branches.SelectMany(item => item.DescendantNodeIds)).ToHashSet();
        AssertIds(nodeIds, graph.Nodes.Select(item => item.NodeId));
        Assert.All(graph.OwnershipLinks, link =>
        {
            Assert.Contains(link.ParentNodeId, nodeIds);
            Assert.Contains(link.ChildNodeId, nodeIds);
        });
        AssertIds(branches.Select(item => item.AssignmentId), graph.Assignments.Select(item => item.AssignmentId));
        if (branches.Length == 0)
            Assert.Empty(graph.OwnershipLinks);
        else
            foreach (var branch in branches)
                Assert.Contains(graph.OwnershipLinks, link => link.OwnershipLinkId == branch.AccountLinkId);

        if (organizationIds.Count == 0)
            Assert.Null(accounting.Organization);
        else
        {
            Assert.NotNull(accounting.Organization);
            Assert.Contains(accounting.Organization.OrganizationId, organizationIds);
        }
        if (branches.Length == 0)
            Assert.Null(accounting.Business);
        else
        {
            Assert.NotNull(accounting.Business);
            Assert.Contains(accounting.Business.BusinessId, branches.Select(item => item.BusinessId));
        }
        AssertIds(branches.Select(item => item.PortfolioId), accounting.InvestmentPortfolios.Select(item => item.InvestmentPortfolioId));
        AssertIds(accountIds, accounting.Accounts.Select(item => item.AccountId));
        AssertIds(accountIds, accounting.LedgerGroups.SelectMany(item => item.AccountIds));
        Assert.Equal(branches.SelectMany(item => new[] { $"BOOK-{item.Tag}", $"ASSIGNED-{item.Tag}" }).Order(),
            accounting.LedgerGroups.Select(item => item.DisplayName).Order());
        Assert.NotNull(accounting.Assignments);
        AssertIds(branches.Select(item => item.AssignmentId), accounting.Assignments.Select(item => item.AssignmentId));
    }

    private static void AssertIds(IEnumerable<Guid> expected, IEnumerable<Guid> actual) =>
        Assert.Equal(expected.Order(), actual.Order());

    private sealed record Views(OrganizationStructureGraphDto Graph, AccountingStructureViewDto Accounting);
    private sealed record Fixture(IFundStructureService Service, InMemoryFundAccountService Accounts,
        Branch A, Branch B, SharedDataAccess? Metadata);

    private sealed class Branch(string tag)
    {
        public string Tag { get; } = tag;
        public Guid OrganizationId { get; } = Guid.NewGuid();
        public Guid BusinessId { get; } = Guid.NewGuid();
        public Guid ClientId { get; } = Guid.NewGuid();
        public Guid FundId { get; } = Guid.NewGuid();
        public Guid SleeveId { get; } = Guid.NewGuid();
        public Guid VehicleId { get; } = Guid.NewGuid();
        public Guid EntityId { get; } = Guid.NewGuid();
        public Guid AccountEntityId { get; } = Guid.NewGuid();
        public Guid PortfolioId { get; } = Guid.NewGuid();
        public Guid DirectAccountId { get; } = Guid.NewGuid();
        public Guid EntityAccountId { get; } = Guid.NewGuid();
        public Guid LinkedAccountId { get; } = Guid.NewGuid();
        public Guid AccountLinkId { get; } = Guid.NewGuid();
        public Guid AssignmentId { get; } = Guid.NewGuid();
        public IEnumerable<Guid> AccountIds => [DirectAccountId, EntityAccountId, LinkedAccountId];
        public IEnumerable<Guid> DescendantNodeIds =>
            [BusinessId, ClientId, FundId, SleeveId, VehicleId, EntityId, AccountEntityId, PortfolioId, .. AccountIds];
    }

    private sealed class SharedDataAccess : IGovernanceSharedDataAccessService
    {
        public FundStructureSharedDataAccessDto Value { get; } = new(
            new SecurityMasterAccessSummaryDto(true, "Available", true, true, true),
            new HistoricalPriceAccessSummaryDto(true, false, 0, [], "No stored prices"),
            new BackfillAccessSummaryDto(true, false, 1, null, null, null, null, null, 0, 0, "Available"));

        public Task<FundStructureSharedDataAccessDto> GetSharedDataAccessAsync(CancellationToken ct = default) =>
            Task.FromResult(Value);
    }
}

/// <summary>Exercises the local service directly, outside production or tenant-guard composition.</summary>
public sealed class InMemoryFundStructureScopeContractTests : FundStructureScopeContractTests
{
    protected override IFundStructureService CreateService(
        IFundAccountService accounts, IGovernanceSharedDataAccessService? metadata = null) =>
        new InMemoryFundStructureService(accounts, metadata, persistencePath: null);

    protected override async Task<IFundStructureService> CreateFromSnapshotAsync(
        OrganizationStructureGraphDto snapshot, IFundAccountService accounts,
        IGovernanceSharedDataAccessService? metadata = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"meridian-scope-contract-{Guid.NewGuid():N}.json");
        try
        {
            var state = new
            {
                Version = 1,
                snapshot.Organizations,
                snapshot.Businesses,
                snapshot.Clients,
                snapshot.Funds,
                snapshot.Sleeves,
                snapshot.Vehicles,
                snapshot.Entities,
                snapshot.InvestmentPortfolios,
                snapshot.OwnershipLinks,
                snapshot.Assignments,
                LinkedAccountIds = snapshot.Accounts.Select(account => account.AccountId).ToList()
            };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return new InMemoryFundStructureService(accounts, metadata, path);
        }
        finally
        {
            // The read-only test service has captured the JSON; no persisted fixture is retained.
            File.Delete(path);
        }
    }
}

/// <summary>Exercises the real PostgreSQL service with an unpartitioned store double, without skips.</summary>
public sealed class PostgresFundStructureScopeContractTests : FundStructureScopeContractTests
{
    protected override IFundStructureService CreateService(
        IFundAccountService accounts, IGovernanceSharedDataAccessService? metadata = null) =>
        new PostgresFundStructureService(new FakeFundStructureStore(), accounts, new FundStructurePolicyService(), metadata);

    protected override async Task<IFundStructureService> CreateFromSnapshotAsync(
        OrganizationStructureGraphDto snapshot, IFundAccountService accounts,
        IGovernanceSharedDataAccessService? metadata = null)
    {
        var store = new FakeFundStructureStore();
        foreach (var item in snapshot.Organizations)
            await store.UpsertOrganizationAsync(item);
        foreach (var item in snapshot.Businesses)
            await store.UpsertBusinessAsync(item);
        foreach (var item in snapshot.Clients)
            await store.UpsertClientAsync(item);
        foreach (var item in snapshot.Funds)
            await store.UpsertFundAsync(item);
        foreach (var item in snapshot.Sleeves)
            await store.UpsertSleeveAsync(item);
        foreach (var item in snapshot.Vehicles)
            await store.UpsertVehicleAsync(item);
        foreach (var item in snapshot.Entities)
            await store.UpsertLegalEntityAsync(item);
        foreach (var item in snapshot.InvestmentPortfolios)
            await store.UpsertInvestmentPortfolioAsync(item);
        foreach (var item in snapshot.OwnershipLinks)
            await store.UpsertOwnershipLinkAsync(item);
        foreach (var item in snapshot.Assignments)
            await store.UpsertAssignmentAsync(item);
        foreach (var item in snapshot.Accounts)
            await store.UpsertLinkedAccountIdAsync(item.AccountId);
        return new PostgresFundStructureService(store, accounts, new FundStructurePolicyService(), metadata);
    }
}
