using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

// GET /api/workstation/scope-options (P4): open to any authenticated operator, but carrying only the
// entities, funds and accounts the caller's own scoped grants already let it read. Assignment
// persistence is the only double: the authorization decisions and the account -> fund ancestry are
// the production ScopedAccessService and FundStructureAccessScopeLineageProvider.
public sealed partial class WorkstationEndpointsTests
{
    private const string ScopeOptionsPath = "/api/workstation/scope-options";

    [Fact]
    public async Task MapWorkstationEndpoints_ScopeOptions_AdminSeesEveryActiveNodeWithRelationships()
    {
        var fixture = new ScopeOptionsFixture([]);
        await using var app = await CreateAppAsync(fixture.Register, currentUserPermissions: UserPermission.AdminMaintenance);
        var first = await fixture.SeedBranchAsync("FIRST");
        var second = await fixture.SeedBranchAsync("SECOND");

        using var options = await ReadJsonAsync(app.GetTestClient(), ScopeOptionsPath);

        ScopeOptionIds(options, "entities", "entityId").Should().BeEquivalentTo([first.EntityId, second.EntityId]);
        ScopeOptionIds(options, "funds", "fundId").Should().BeEquivalentTo([first.FundId, second.FundId]);
        ScopeOptionIds(options, "accounts", "accountId").Should().BeEquivalentTo([first.AccountId, second.AccountId]);

        var account = ScopeOption(options, "accounts", "accountId", first.AccountId);
        account.GetProperty("displayName").GetString().Should().Be("Account FIRST");
        account.GetProperty("fundId").GetGuid().Should().Be(first.FundId);
        account.GetProperty("entityId").GetGuid().Should().Be(first.EntityId);
        account.GetProperty("fundWithheld").GetBoolean().Should().BeFalse("an administrator sees every fund");
        account.GetProperty("entityWithheld").GetBoolean().Should().BeFalse("an administrator sees every entity");
        var fund = ScopeOption(options, "funds", "fundId", first.FundId);
        fund.GetProperty("name").GetString().Should().Be("Fund FIRST");
        fund.GetProperty("entityIds").EnumerateArray().Select(static id => id.GetGuid()).Should().Equal(first.EntityId);
        ScopeOption(options, "entities", "entityId", first.EntityId).GetProperty("name").GetString().Should().Be("Entity FIRST");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ScopeOptions_FundGrantShowsOnlyThatFundsEntityAndAccounts()
    {
        var allowedFundId = Guid.NewGuid();
        var fixture = new ScopeOptionsFixture([(AccessScopeKindDto.Fund, allowedFundId, UserPermission.ManageFundStructure)]);
        await using var app = await CreateAppAsync(fixture.Register, currentUserPermissions: UserPermission.ManageFundStructure);
        var allowed = await fixture.SeedBranchAsync("ALLOWED", fundId: allowedFundId);
        await fixture.SeedBranchAsync("OTHER");

        using var options = await ReadJsonAsync(app.GetTestClient(), ScopeOptionsPath);

        ScopeOptionIds(options, "funds", "fundId").Should().Equal(allowed.FundId);
        ScopeOptionIds(options, "entities", "entityId").Should().Equal(allowed.EntityId);
        ScopeOptionIds(options, "accounts", "accountId").Should().Equal(allowed.AccountId);
        var account = ScopeOption(options, "accounts", "accountId", allowed.AccountId);
        account.GetProperty("fundId").GetGuid().Should().Be(allowed.FundId);
        account.GetProperty("entityId").GetGuid().Should().Be(allowed.EntityId);
        account.GetProperty("fundWithheld").GetBoolean().Should().BeFalse();
        account.GetProperty("entityWithheld").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ScopeOptions_TradingOperatorSeesItsAccountButNotTheFundOrEntityBehindIt()
    {
        // A ViewTrades grant on a fund reaches that fund's accounts (as the brokerage household read
        // does), but no existing ViewTrades read discloses the fund or entity record itself.
        var fundId = Guid.NewGuid();
        var fixture = new ScopeOptionsFixture([(AccessScopeKindDto.Fund, fundId, UserPermission.ViewTrades)]);
        await using var app = await CreateAppAsync(fixture.Register, currentUserPermissions: UserPermission.ViewTrades);
        var branch = await fixture.SeedBranchAsync("TRADING", fundId: fundId);
        await fixture.SeedBranchAsync("OTHER");

        using var options = await ReadJsonAsync(app.GetTestClient(), ScopeOptionsPath);

        options.RootElement.GetProperty("entities").GetArrayLength().Should().Be(0);
        options.RootElement.GetProperty("funds").GetArrayLength().Should().Be(0);
        ScopeOptionIds(options, "accounts", "accountId").Should().Equal(branch.AccountId);
        var account = ScopeOption(options, "accounts", "accountId", branch.AccountId);
        account.GetProperty("displayName").GetString().Should().Be("Account TRADING");
        account.GetProperty("fundId").ValueKind.Should().Be(JsonValueKind.Null, "the caller may not see the fund");
        account.GetProperty("entityId").ValueKind.Should().Be(JsonValueKind.Null, "the caller may not see the entity");
        // The account does have a fund and an entity: say so, without naming either.
        account.GetProperty("fundWithheld").GetBoolean().Should().BeTrue("the account has a fund the caller may not see");
        account.GetProperty("entityWithheld").GetBoolean().Should().BeTrue("the account has an entity the caller may not see");
        options.RootElement.GetRawText().Should().NotContain(branch.FundId.ToString("D"), "a withheld fund's id is never disclosed")
            .And.NotContain(branch.EntityId.ToString("D"), "a withheld entity's id is never disclosed")
            .And.NotContain("Fund TRADING", "a withheld fund's name is never disclosed")
            .And.NotContain("Entity TRADING", "a withheld entity's name is never disclosed");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ScopeOptions_DirectLendingFundGrantShowsAccountsButNotTheFundRecord()
    {
        // The fund-scoped direct-lending reads return a fund's accounts, never the fund record.
        var fundId = Guid.NewGuid();
        var fixture = new ScopeOptionsFixture([(AccessScopeKindDto.Fund, fundId, UserPermission.ManageDirectLending)]);
        await using var app = await CreateAppAsync(fixture.Register, currentUserPermissions: UserPermission.ManageDirectLending);
        var branch = await fixture.SeedBranchAsync("LENDING", fundId: fundId);
        await fixture.SeedBranchAsync("OTHER");

        using var options = await ReadJsonAsync(app.GetTestClient(), ScopeOptionsPath);

        options.RootElement.GetProperty("funds").GetArrayLength().Should().Be(0);
        options.RootElement.GetProperty("entities").GetArrayLength().Should().Be(0);
        ScopeOptionIds(options, "accounts", "accountId").Should().Equal(branch.AccountId);
        var account = ScopeOption(options, "accounts", "accountId", branch.AccountId);
        account.GetProperty("fundId").ValueKind.Should().Be(JsonValueKind.Null);
        account.GetProperty("fundWithheld").GetBoolean().Should().BeTrue("the account's fund exists but is not disclosed");
        account.GetProperty("entityWithheld").GetBoolean().Should().BeTrue("the account's entity exists but is not disclosed");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ScopeOptions_AccountingOperatorSeesGrantedEntityOnly()
    {
        var entityId = Guid.NewGuid();
        var fixture = new ScopeOptionsFixture([(AccessScopeKindDto.LegalEntity, entityId, UserPermission.ViewLedgerReports)]);
        await using var app = await CreateAppAsync(fixture.Register, currentUserPermissions: UserPermission.ViewLedgerReports);
        await fixture.SeedBranchAsync("LEDGER", entityId: entityId);
        await fixture.SeedBranchAsync("OTHER");

        using var options = await ReadJsonAsync(app.GetTestClient(), ScopeOptionsPath);

        ScopeOptionIds(options, "entities", "entityId").Should().Equal(entityId);
        options.RootElement.GetProperty("funds").GetArrayLength().Should().Be(0);
        options.RootElement.GetProperty("accounts").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ScopeOptions_AssignmentDoesNotWidenAPermissionTheCallerDoesNotHold()
    {
        // The assignment carries ManageFundStructure, but the session's role does not: the grant
        // cannot be used, so the fund stays hidden even though the account (ViewTrades) is shown.
        var fundId = Guid.NewGuid();
        var fixture = new ScopeOptionsFixture([
            (AccessScopeKindDto.Fund, fundId, UserPermission.ManageFundStructure | UserPermission.ViewTrades)]);
        await using var app = await CreateAppAsync(fixture.Register, currentUserPermissions: UserPermission.ViewTrades);
        var branch = await fixture.SeedBranchAsync("NARROW", fundId: fundId);

        using var options = await ReadJsonAsync(app.GetTestClient(), ScopeOptionsPath);

        options.RootElement.GetProperty("funds").GetArrayLength().Should().Be(0);
        options.RootElement.GetProperty("entities").GetArrayLength().Should().Be(0);
        ScopeOptionIds(options, "accounts", "accountId").Should().Equal(branch.AccountId);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ScopeOptions_PermissionWithoutAnyGrantReturnsTheEmptyShape()
    {
        var fixture = new ScopeOptionsFixture([]);
        await using var app = await CreateAppAsync(fixture.Register,
            currentUserPermissions: UserPermission.ViewTrades | UserPermission.ManageFundStructure);
        await fixture.SeedBranchAsync("UNGRANTED");

        using var options = await ReadJsonAsync(app.GetTestClient(), ScopeOptionsPath);

        AssertEmptyScopeOptions(options);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ScopeOptions_CallerWithNoScopePermissionGetsEmptyListsWithoutReadingTheStructure()
    {
        var service = Substitute.For<IFundStructureService>();
        await using var app = await CreateAppAsync(services => services.AddSingleton(service),
            currentUserPermissions: UserPermission.ViewHistoricalData);

        using var options = await ReadJsonAsync(app.GetTestClient(), ScopeOptionsPath);

        AssertEmptyScopeOptions(options);
        await service.DidNotReceiveWithAnyArgs().GetOrganizationStructureAsync(default!, default);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ScopeOptions_RefusesACallerWithoutTenantAndCompanyScope()
    {
        var service = Substitute.For<IFundStructureService>();
        await using var app = await CreateAppAsync(services => services.AddSingleton(service),
            currentUserPermissions: UserPermission.AdminMaintenance, currentUserCompanyId: null);

        var response = await app.GetTestClient().GetAsync(ScopeOptionsPath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await service.DidNotReceiveWithAnyArgs().GetOrganizationStructureAsync(default!, default);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ScopeOptions_ReportsAFundStructureTenantRefusalAsForbidden()
    {
        // The fund-structure service is the tenant boundary: it serves only the caller's tenant
        // snapshot and refuses an unresolvable one. That refusal must reach the caller as 403, not
        // as an empty list that reads like "nothing to choose".
        var service = Substitute.For<IFundStructureService>();
        service.GetOrganizationStructureAsync(Arg.Any<OrganizationStructureQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OrganizationStructureGraphDto>(
                new FundStructureTenantScopeException("A tenant-scoped session is required to read the fund structure.")));
        await using var app = await CreateAppAsync(services => services.AddSingleton(service),
            currentUserPermissions: UserPermission.AdminMaintenance, useApiExceptionHandler: true);

        var response = await app.GetTestClient().GetAsync(ScopeOptionsPath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ScopeOptions_ReportsAnUnregisteredStructureServiceAsUnavailable()
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.AdminMaintenance);

        var response = await app.GetTestClient().GetAsync(ScopeOptionsPath);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ScopeOptions_DeclaresSessionAuthenticationAndTenantScope()
    {
        await using var app = await CreateAppAsync();
        var endpoint = app.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Single(candidate => string.Equals(
                candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                "GetWorkstationScopeOptions",
                StringComparison.Ordinal));

        endpoint.Metadata.GetMetadata<WorkstationTenantScopeMetadata>().Should().NotBeNull();
        var authorization = endpoint.Metadata.GetMetadata<EndpointAuthorizationMetadata>();
        authorization.Should().NotBeNull("the route requires an authenticated workstation session");
        authorization!.Permissions.Should().BeEmpty("visibility is decided per node by the caller's scoped grants");
    }

    [Fact]
    public void WorkstationScopeOptions_SerializeThroughTheEndpointJsonOptionsWithExplicitNullRelationships()
    {
        var accountId = Guid.NewGuid();
        var payload = new WorkstationScopeOptionsDto(
            [],
            [],
            [new WorkstationScopeAccountOptionDto(accountId, "Account", FundId: null, EntityId: null)]);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, UiEndpoints.CreateEndpointJsonOptions()));

        var account = document.RootElement.GetProperty("accounts")[0];
        account.GetProperty("accountId").GetGuid().Should().Be(accountId);
        account.GetProperty("displayName").GetString().Should().Be("Account");
        account.GetProperty("fundId").ValueKind.Should().Be(JsonValueKind.Null);
        account.GetProperty("entityId").ValueKind.Should().Be(JsonValueKind.Null);
        account.GetProperty("fundWithheld").GetBoolean().Should().BeFalse("the flags default to false and are always written");
        account.GetProperty("entityWithheld").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("entities").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void WorkstationScopeOptions_RoundTripTheWithheldFlagsAndReadAPayloadWithoutThem()
    {
        var accountId = Guid.NewGuid();
        var jsonOptions = UiEndpoints.CreateEndpointJsonOptions();
        var payload = new WorkstationScopeOptionsDto(
            [],
            [],
            [new WorkstationScopeAccountOptionDto(accountId, "Account", FundId: null, EntityId: null, FundWithheld: true, EntityWithheld: true)]);

        var roundTripped = JsonSerializer.Deserialize<WorkstationScopeOptionsDto>(
            JsonSerializer.Serialize(payload, jsonOptions), jsonOptions);

        roundTripped!.Accounts.Should().ContainSingle()
            .Which.Should().Be(new WorkstationScopeAccountOptionDto(accountId, "Account", null, null, true, true));

        // A payload from a host older than the flags still reads, with both false.
        var legacy = JsonSerializer.Deserialize<WorkstationScopeOptionsDto>(
            $$"""{"entities":[],"funds":[],"accounts":[{"accountId":"{{accountId}}","displayName":"Account","fundId":null,"entityId":null}]}""",
            jsonOptions);
        legacy!.Accounts.Should().ContainSingle()
            .Which.Should().Be(new WorkstationScopeAccountOptionDto(accountId, "Account", null, null));
    }

    private static void AssertEmptyScopeOptions(JsonDocument options)
    {
        foreach (var list in new[] { "entities", "funds", "accounts" })
        {
            var element = options.RootElement.GetProperty(list);
            element.ValueKind.Should().Be(JsonValueKind.Array, $"{list} is always present");
            element.GetArrayLength().Should().Be(0);
        }
    }

    private static Guid[] ScopeOptionIds(JsonDocument options, string list, string idProperty)
        => options.RootElement.GetProperty(list).EnumerateArray()
            .Select(item => item.GetProperty(idProperty).GetGuid())
            .ToArray();

    private static JsonElement ScopeOption(JsonDocument options, string list, string idProperty, Guid id)
        => options.RootElement.GetProperty(list).EnumerateArray()
            .Single(item => item.GetProperty(idProperty).GetGuid() == id);

    private sealed record ScopeOptionsBranch(Guid FundId, Guid EntityId, Guid AccountId);

    /// <summary>In-memory fund structure plus production scoped authorization over fixed assignments.</summary>
    private sealed class ScopeOptionsFixture
    {
        private static readonly DateTimeOffset EffectiveFrom = new(2026, 6, 16, 0, 0, 0, TimeSpan.Zero);
        private const string Actor = "scope-options-test";

        private readonly IFundAccountService _accounts = new InMemoryFundAccountService();
        private readonly IFundStructureService _structure;
        private readonly IScopedAuthorizationService _authorization;

        public ScopeOptionsFixture(IReadOnlyCollection<(AccessScopeKindDto Kind, Guid Id, UserPermission Permissions)> grants)
        {
            _structure = new InMemoryFundStructureService(_accounts);
            _authorization = new ScopedAccessService(
                new ScopeOptionsAssignmentStore(grants),
                new FundStructureAccessScopeLineageProvider(_structure));
        }

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<IFundAccountService>(_accounts);
            services.AddSingleton<IFundStructureService>(_structure);
            services.AddSingleton<IScopedAuthorizationService>(_authorization);
        }

        public async Task<ScopeOptionsBranch> SeedBranchAsync(string tag, Guid? fundId = null, Guid? entityId = null)
        {
            var organizationId = Guid.NewGuid();
            var businessId = Guid.NewGuid();
            var fund = fundId ?? Guid.NewGuid();

            await _structure.CreateOrganizationAsync(new CreateOrganizationRequest(
                organizationId, Code("ORG"), $"Organization {tag}", "USD", EffectiveFrom, Actor));
            await _structure.CreateBusinessAsync(new CreateBusinessRequest(
                businessId, organizationId, BusinessKindDto.FundManager, Code("BUS"), $"Business {tag}", "USD", EffectiveFrom, Actor));
            await _structure.CreateFundAsync(new CreateFundRequest(
                fund, Code("FND"), $"Fund {tag}", "USD", EffectiveFrom, Actor, BusinessId: businessId));
            var entity = await _structure.CreateLegalEntityAsync(new(entityId ?? Guid.NewGuid(),
                LegalEntityTypeDto.Vehicle, Code("ENT"), $"Entity {tag}", "US", "USD", EffectiveFrom, Actor));
            var account = await _accounts.CreateAccountAsync(new(Guid.NewGuid(), AccountTypeDto.Custody,
                Code("ACC"), $"Account {tag}", "USD", EffectiveFrom, Actor, FundId: fund, EntityId: entity.EntityId));
            await _structure.LinkNodesAsync(new(Guid.NewGuid(), fund, entity.EntityId,
                OwnershipRelationshipTypeDto.Owns, EffectiveFrom, Actor, OwnershipPercent: 100m));
            await _structure.LinkNodesAsync(new(Guid.NewGuid(), fund, account.AccountId,
                OwnershipRelationshipTypeDto.Operates, EffectiveFrom, Actor));
            return new ScopeOptionsBranch(fund, entity.EntityId, account.AccountId);
        }

        private static string Code(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
    }

    /// <summary>Fixed, effective User assignments for the default test operator ("ops-user").</summary>
    private sealed class ScopeOptionsAssignmentStore(
        IReadOnlyCollection<(AccessScopeKindDto Kind, Guid Id, UserPermission Permissions)> grants) : IScopedAccessAssignmentStore
    {
        private readonly IReadOnlyList<UserAccessAssignmentDto> _assignments = grants.Select(grant => new UserAccessAssignmentDto(
            Guid.NewGuid(), "ops-user", AccessPrincipalKindDto.User, grant.Kind, grant.Id,
            "Operator", null, RolePermissions.GetPermissionNames(grant.Permissions), (long)grant.Permissions,
            DateTimeOffset.UtcNow.AddDays(-1), null, "test-admin", "Scope options regression", "scope-options-test",
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
}
