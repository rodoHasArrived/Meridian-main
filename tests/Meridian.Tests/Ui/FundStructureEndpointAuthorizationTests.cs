using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
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

    private static async Task<WebApplication> CreateAppAsync(
        IReadOnlyCollection<(AccessScopeKindDto Kind, Guid Id)> allowedScopes,
        UserPermission permissions = UserPermission.ManageFundStructure)
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
        builder.Services.AddSingleton<IScopedAuthorizationService>(
            new TestScopedAuthorizationService(allowedScopes));

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

    private static async Task SeedFundAsync(IFundStructureService structureService, Guid fundId)
    {
        var organizationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var effectiveFrom = new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero);

        await structureService.CreateOrganizationAsync(new CreateOrganizationRequest(
            organizationId,
            $"ORG-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            "Endpoint Auth Organization",
            "USD",
            effectiveFrom,
            "endpoint-auth-test"));

        await structureService.CreateBusinessAsync(new CreateBusinessRequest(
            businessId,
            organizationId,
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
            BusinessId: businessId));
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
