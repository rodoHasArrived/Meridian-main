using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Entities.FundStructure;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class FundStructureEndpointAuthorizationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AccountingView_ShouldRequireScopedFundAccess()
    {
        var allowedFundId = Guid.NewGuid();
        var deniedFundId = Guid.NewGuid();
        await using var app = await CreateAppAsync([(AccessScopeKindDto.Fund, allowedFundId)]);
        var structureService = app.Services.GetRequiredService<IFundStructureService>();
        await SeedFundAsync(structureService, allowedFundId);
        await SeedFundAsync(structureService, deniedFundId);

        var client = app.GetTestClient();

        var allowed = await client.GetAsync($"/api/fund-structure/accounting-view?fundId={allowedFundId:D}");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);

        var denied = await client.GetAsync($"/api/fund-structure/accounting-view?fundId={deniedFundId:D}");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OrganizationGraph_ShouldDenyUnscopedReadForNonAdmin()
    {
        await using var app = await CreateAppAsync([(AccessScopeKindDto.Organization, Guid.NewGuid())]);

        var response = await app.GetTestClient().GetAsync("/api/fund-structure/graph");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OrganizationGraph_ShouldAllowUnscopedReadForAdminMaintenance()
    {
        await using var app = await CreateAppAsync(
            [],
            permissions: UserPermission.AdminMaintenance);

        var response = await app.GetTestClient().GetAsync("/api/fund-structure/graph");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(AccessScopeKindDto.Account, FundStructureNodeKindDto.Account)]
    [InlineData(AccessScopeKindDto.LegalEntity, FundStructureNodeKindDto.Entity)]
    public async Task OrganizationGraph_NodeGrantCannotAuthorizeUnrelatedSummaryCollections(
        AccessScopeKindDto scopeKind, FundStructureNodeKindDto nodeKind)
    {
        var allowedId = Guid.NewGuid();
        await using var app = await CreateAppAsync([(scopeKind, allowedId)], useRetainedAssignments: true);
        var allowed = await SeedGraphBranchAsync(app, "ALLOWED",
            entityId: nodeKind == FundStructureNodeKindDto.Entity ? allowedId : null,
            accountId: nodeKind == FundStructureNodeKindDto.Account ? allowedId : null);
        var unrelated = await SeedGraphBranchAsync(app, "UNRELATED");
        var authorization = app.Services.GetRequiredService<IScopedAuthorizationService>();
        (await authorization.AuthorizeAsync("fund-structure-user", UserPermission.ManageFundStructure,
            scopeKind, allowedId, UserPermission.ManageFundStructure)).IsAllowed.Should().BeTrue();
        (await authorization.AuthorizeAsync("fund-structure-user", UserPermission.ManageFundStructure,
            AccessScopeKindDto.Account, unrelated.AccountId, UserPermission.ManageFundStructure))
            .IsAllowed.Should().BeFalse();

        // NodeId is a presentation filter: the service intentionally retains summary collections.
        // Prove those collections are populated before asking the HTTP boundary to protect them.
        var raw = await app.Services.GetRequiredService<IFundStructureService>()
            .GetOrganizationStructureAsync(new(NodeId: allowedId, NodeKind: nodeKind));
        raw.Nodes.Should().ContainSingle().Which.NodeId.Should().Be(allowedId);
        raw.Accounts.Select(account => account.AccountId).Should().Contain([allowed.AccountId, unrelated.AccountId]);
        raw.Entities.Select(entity => entity.EntityId).Should().Contain([allowed.EntityId, unrelated.EntityId]);

        var focused = await app.GetTestClient().GetAsync(
            $"/api/fund-structure/legacy-graph?nodeKind={nodeKind}&nodeId={allowedId:D}");
        focused.StatusCode.Should().Be(HttpStatusCode.OK,
            "the focused graph contains only the authorized node, without wider summary collections");
        var focusedGraph = await focused.Content.ReadFromJsonAsync<FundStructureGraphDto>(JsonOptions);
        focusedGraph.Should().NotBeNull();
        focusedGraph!.Nodes.Should().ContainSingle().Which.NodeId.Should().Be(allowedId);

        var response = await app.GetTestClient().GetAsync(
            $"/api/fund-structure/graph?nodeKind={nodeKind}&nodeId={allowedId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "one node grant cannot authorize the wider organization graph summary payload");
    }

    [Fact]
    public async Task OrganizationGraph_AllowedBusinessCannotAuthorizeAnUnrelatedOrganizationSelector()
    {
        var allowedBusinessId = Guid.NewGuid();
        await using var app = await CreateAppAsync(
            [(AccessScopeKindDto.Business, allowedBusinessId)], useRetainedAssignments: true);
        await SeedGraphBranchAsync(app, "ALLOWED", businessId: allowedBusinessId);
        var unrelated = await SeedGraphBranchAsync(app, "UNRELATED");

        var response = await app.GetTestClient().GetAsync(
            $"/api/fund-structure/graph?businessId={allowedBusinessId:D}&organizationId={unrelated.OrganizationId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the empty business intersection still returns the requested organization root");
    }

    [Fact]
    public async Task OrganizationGraph_OrganizationGrantRetainsMatchingBusinessAndNodePresentationQueries()
    {
        var allowedOrganizationId = Guid.NewGuid();
        await using var app = await CreateAppAsync(
            [(AccessScopeKindDto.Organization, allowedOrganizationId)], useRetainedAssignments: true);
        var externalPortfolioReference = Guid.NewGuid().ToString("D");
        var allowed = await SeedGraphBranchAsync(app, "ALLOWED", organizationId: allowedOrganizationId,
            portfolioReference: externalPortfolioReference);
        var unrelated = await SeedGraphBranchAsync(app, "UNRELATED");
        var response = await app.GetTestClient().GetAsync(
            $"/api/fund-structure/graph?organizationId={allowed.OrganizationId:D}&businessId={allowed.BusinessId:D}"
            + $"&nodeKind=Account&nodeId={allowed.AccountId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var graph = await response.Content.ReadFromJsonAsync<OrganizationStructureGraphDto>(JsonOptions);
        graph.Should().NotBeNull();
        graph!.Nodes.Should().ContainSingle().Which.NodeId.Should().Be(allowed.AccountId);
        graph.Organizations.Should().ContainSingle().Which.OrganizationId.Should().Be(allowed.OrganizationId);
        graph.Businesses.Should().ContainSingle().Which.BusinessId.Should().Be(allowed.BusinessId);
        graph.Entities.Should().ContainSingle().Which.EntityId.Should().Be(allowed.EntityId);
        graph.Accounts.Should().ContainSingle().Which.AccountId.Should().Be(allowed.AccountId);
        graph.Accounts[0].PortfolioId.Should().Be(externalPortfolioReference,
            "a GUID-shaped external brokerage reference is account metadata, not a structural portfolio selector");
        graph.Accounts.Should().NotContain(account => account.AccountId == unrelated.AccountId);
    }

    [Fact]
    public async Task OrganizationGraph_AdminUnscopedReadRetainsPopulatedAuthorizedInput()
    {
        await using var app = await CreateAppAsync([], permissions: UserPermission.AdminMaintenance,
            useRetainedAssignments: true);
        var first = await SeedGraphBranchAsync(app, "FIRST");
        var second = await SeedGraphBranchAsync(app, "SECOND");

        var response = await app.GetTestClient().GetAsync("/api/fund-structure/graph");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var graph = await response.Content.ReadFromJsonAsync<OrganizationStructureGraphDto>(JsonOptions);
        graph.Should().NotBeNull();
        graph!.Organizations.Select(organization => organization.OrganizationId)
            .Should().BeEquivalentTo([first.OrganizationId, second.OrganizationId]);
        graph.Accounts.Select(account => account.AccountId)
            .Should().BeEquivalentTo([first.AccountId, second.AccountId]);
    }

    [Fact]
    public async Task CashFlowView_UnassignedLedgerGroup_ShouldDenyUnscopedReadForNonAdmin()
    {
        await using var app = await CreateAppAsync([(AccessScopeKindDto.Organization, Guid.NewGuid())]);

        var response = await app.GetTestClient().GetAsync(
            "/api/fund-structure/cash-flow-view?scopeKind=LedgerGroup&ledgerGroupId=unassigned");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CashFlowView_UnassignedLedgerGroup_ShouldAllowScopedRead()
    {
        var organizationId = Guid.NewGuid();
        await using var app = await CreateAppAsync([(AccessScopeKindDto.Organization, organizationId)]);

        var response = await app.GetTestClient().GetAsync(
            $"/api/fund-structure/cash-flow-view?scopeKind=LedgerGroup&ledgerGroupId=unassigned&organizationId={organizationId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<GovernanceCashFlowViewDto>(JsonOptions);

        payload.Should().NotBeNull();
        payload!.Scope.ScopeKind.Should().Be(GovernanceCashFlowScopeKindDto.LedgerGroup);
        payload.Scope.LedgerGroupId.Should().Be(LedgerGroupId.Unassigned);
        payload.AccountCount.Should().Be(0);
        payload.Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task OperationsWorkspace_WithoutTenantAndCompanyScope_ShouldFailClosed()
    {
        await using var app = await CreateAppAsync([]);

        var response = await app.GetTestClient().GetAsync(
            "/api/fund-structure/workspace-view?fundProfileId=fund-operations-test");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CashFlowView_ConflictingStatementAuthority_ReturnsEvidenceUnavailableConflict()
    {
        var fundId = Guid.NewGuid();
        await using var app = await CreateAppAsync([(AccessScopeKindDto.Fund, fundId)]);
        await SeedFundAsync(app.Services.GetRequiredService<IFundStructureService>(), fundId);
        var accounts = app.Services.GetRequiredService<IFundAccountService>();
        var date = new DateOnly(2026, 6, 20);
        var account = await accounts.CreateAccountAsync(new(Guid.NewGuid(), AccountTypeDto.Bank,
            "BANK-CONFLICT", "Conflicting statement", "USD", new(2026, 6, 16, 0, 0, 0, TimeSpan.Zero), "test", FundId: fundId));
        foreach (var balance in new[] { 100m, 120m })
        {
            var batch = Guid.NewGuid();
            await accounts.IngestBankStatementAsync(new(batch, account.AccountId, date, "Bank", null,
                [new(Guid.NewGuid(), batch, account.AccountId, date, date, 10m, "USD", "Interest", "Interest", null, balance)], "test"));
        }

        var response = await app.GetTestClient().GetAsync(
            $"/api/fund-structure/cash-flow-view?scopeKind=Fund&fundId={fundId:D}&asOf=2026-06-20T12:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        problem!.Title.Should().Be("Bank statement evidence unavailable");
        problem.Detail.Should().Contain("same header date and revision");
    }

    private static async Task<WebApplication> CreateAppAsync(
        IReadOnlyCollection<(AccessScopeKindDto Kind, Guid Id)> allowedScopes,
        UserPermission permissions = UserPermission.ManageFundStructure,
        bool useRetainedAssignments = false)
    {
        var fundAccountService = new InMemoryFundAccountService();
        var fundStructureService = new InMemoryFundStructureService(fundAccountService);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IFundAccountService>(fundAccountService);
        builder.Services.AddSingleton<IFundStructureService>(fundStructureService);
        builder.Services.AddSingleton<IScopedAuthorizationService>(useRetainedAssignments
            ? new ScopedAccessService(new TestScopedAccessAssignmentStore(allowedScopes),
                new FundStructureAccessScopeLineageProvider(fundStructureService))
            : new TestScopedAuthorizationService(allowedScopes));

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "fund-structure-user";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next(context);
        });
        app.MapFundStructureEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }

    private static async Task<(Guid OrganizationId, Guid BusinessId)> SeedFundAsync(
        IFundStructureService structureService, Guid fundId,
        Guid? organizationId = null, Guid? businessId = null)
    {
        var organization = organizationId ?? Guid.NewGuid();
        var business = businessId ?? Guid.NewGuid();
        var effectiveFrom = new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero);

        await structureService.CreateOrganizationAsync(new CreateOrganizationRequest(
            organization,
            $"ORG-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            "Endpoint Auth Organization",
            "USD",
            effectiveFrom,
            "endpoint-auth-test"));

        await structureService.CreateBusinessAsync(new CreateBusinessRequest(
            business,
            organization,
            BusinessKindDto.FundManager,
            $"BUS-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            "Endpoint Auth Business",
            "USD",
            effectiveFrom,
            "endpoint-auth-test"));

        await structureService.CreateFundAsync(new CreateFundRequest(
            fundId,
            $"FND-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            "Endpoint Auth Fund",
            "USD",
            effectiveFrom,
            "endpoint-auth-test",
            BusinessId: business));
        return (organization, business);
    }

    private static async Task<GraphBranch> SeedGraphBranchAsync(
        WebApplication app, string tag, Guid? organizationId = null, Guid? businessId = null,
        Guid? entityId = null, Guid? accountId = null, string? portfolioReference = null)
    {
        var service = app.Services.GetRequiredService<IFundStructureService>();
        var fundId = Guid.NewGuid();
        var hierarchy = await SeedFundAsync(service, fundId, organizationId, businessId);
        var effectiveFrom = new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero);
        var entity = await service.CreateLegalEntityAsync(new(entityId ?? Guid.NewGuid(),
            LegalEntityTypeDto.Vehicle, $"ENTITY-{tag}", $"Entity {tag}", "US", "USD", effectiveFrom, "endpoint-auth-test"));
        var account = await app.Services.GetRequiredService<IFundAccountService>().CreateAccountAsync(new(
            accountId ?? Guid.NewGuid(), AccountTypeDto.Custody, $"ACCOUNT-{tag}", $"Account {tag}",
            "USD", effectiveFrom, "endpoint-auth-test", FundId: fundId, EntityId: entity.EntityId,
            PortfolioId: portfolioReference));
        await service.LinkNodesAsync(new(Guid.NewGuid(), fundId, entity.EntityId,
            OwnershipRelationshipTypeDto.Owns, effectiveFrom, "endpoint-auth-test", OwnershipPercent: 100m));
        await service.LinkNodesAsync(new(Guid.NewGuid(), fundId, account.AccountId,
            OwnershipRelationshipTypeDto.Operates, effectiveFrom, "endpoint-auth-test"));
        return new(hierarchy.OrganizationId, hierarchy.BusinessId, fundId, entity.EntityId, account.AccountId);
    }

    private sealed record GraphBranch(Guid OrganizationId, Guid BusinessId, Guid FundId, Guid EntityId, Guid AccountId);

    // Only assignment persistence is doubled: authorization decisions and ancestry are production
    // implementations, so the fixtures verify that an organization grant reaches its real children.
    private sealed class TestScopedAccessAssignmentStore(
        IReadOnlyCollection<(AccessScopeKindDto Kind, Guid Id)> scopes) : IScopedAccessAssignmentStore
    {
        private readonly IReadOnlyList<UserAccessAssignmentDto> _assignments = scopes.Select(scope => new UserAccessAssignmentDto(
            Guid.NewGuid(), "fund-structure-user", AccessPrincipalKindDto.User, scope.Kind, scope.Id,
            "FundOperator", null, [nameof(UserPermission.ManageFundStructure)], (long)UserPermission.ManageFundStructure,
            DateTimeOffset.UtcNow.AddDays(-1), null, "test-admin", "Endpoint scope regression", "endpoint-scope-test",
            1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)).ToArray();

        public Task<IReadOnlyList<UserAccessAssignmentDto>> ListAsync(CancellationToken ct = default)
            => Task.FromResult(_assignments);

        public Task<UserAccessAssignmentDto?> GetAsync(Guid assignmentId, CancellationToken ct = default)
            => Task.FromResult(_assignments.FirstOrDefault(assignment => assignment.AssignmentId == assignmentId));

        public Task<UserAccessAssignmentDto> CreateAsync(UserAccessAssignmentDto assignment, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<UserAccessAssignmentDto> ReplaceAsync(
            UserAccessAssignmentDto assignment, long expectedVersion, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class TestScopedAuthorizationService : IScopedAuthorizationService
    {
        private readonly HashSet<(AccessScopeKindDto Kind, Guid Id)> _allowedScopes;

        public TestScopedAuthorizationService(IReadOnlyCollection<(AccessScopeKindDto Kind, Guid Id)> allowedScopes)
        {
            _allowedScopes = allowedScopes.ToHashSet();
        }

        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(
            string actor,
            UserPermission requiredPermission,
            AccessScopeKindDto scopeKind,
            Guid? scopeId,
            UserPermission globalPermissions,
            CancellationToken ct = default)
        {
            var allowed = requiredPermission == UserPermission.ManageFundStructure
                && scopeId.HasValue
                && _allowedScopes.Contains((scopeKind, scopeId.Value));
            return Task.FromResult(new ScopedAuthorizationDecisionDto(
                allowed,
                actor,
                requiredPermission,
                scopeKind,
                scopeId,
                allowed ? "Test scope grants access." : "Test scope denies access."));
        }
    }
}
