using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Subscriptions.Services;
using Meridian.Core.Subscriptions.Models;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class BondDiscoveryEndpointsTests
{
    [Theory]
    [InlineData(UserPermission.ViewSecurityMaster, false)]
    [InlineData(UserPermission.ModifySecurityMaster, true)]
    public async Task Lookup_UsesFixedIncomeProviderAndReturnsStagingPermission(UserPermission permission, bool canStage)
    {
        var provider = Substitute.For<IFilterableSymbolSearchProvider>();
        provider.Name.Returns("alpaca"); provider.DisplayName.Returns("Alpaca");
        provider.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        provider.SearchAsync("Treasury", 50, "fixed_income", null, Arg.Any<CancellationToken>())
            .Returns(new SymbolSearchResult[]
            {
                new("912TEST", "Synthetic Treasury", AssetType: "FixedIncome", Currency: "USD", Source: "alpaca"),
                new("WRONG", "Wrong asset", AssetType: "Stock", Source: "alpaca")
            });
        using var search = new SymbolSearchService(new[] { provider }, null, new MetadataEnrichmentService());
        await using var app = await CreateAppAsync(permission, search);
        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("canStage").GetBoolean().Should().Be(canStage);
        body.GetProperty("candidates").GetArrayLength().Should().Be(1);
        body.GetProperty("candidates")[0].GetProperty("symbol").GetString().Should().Be("912TEST");
        await provider.Received(1).SearchAsync("Treasury", 50, "fixed_income", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lookup_WithoutReadPermissionRefusesBeforeProviderAccess()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewTrades);
        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Lookup_WithoutTenantScopeFailsClosed()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster, tenantScoped: false);
        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Lookup_WithoutSearchServiceReturnsExplicitUnavailable()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster);
        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Lookup_WithUnavailableProviderHasNoCandidates()
    {
        var provider = Substitute.For<ISymbolSearchProvider>();
        provider.Name.Returns("alpaca"); provider.DisplayName.Returns("Alpaca");
        provider.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(false);
        using var search = new SymbolSearchService(new[] { provider }, null, new MetadataEnrichmentService());
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster, search);
        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("available").GetBoolean().Should().BeFalse();
        body.GetProperty("unavailableReason").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("candidates").GetArrayLength().Should().Be(0);
        await provider.DidNotReceiveWithAnyArgs().SearchAsync(default!, default, default);
    }

    [Fact]
    public async Task Lookup_WithProviderFailureReturnsUnavailableInsteadOfEmptyMatches()
    {
        var provider = Substitute.For<IFilterableSymbolSearchProvider>();
        provider.Name.Returns("alpaca"); provider.DisplayName.Returns("Alpaca");
        provider.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        provider.SearchAsync("Treasury", 50, "fixed_income", null, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<SymbolSearchResult>>>(_ => throw new HttpRequestException("Synthetic provider outage"));
        using var search = new SymbolSearchService(new[] { provider }, null, new MetadataEnrichmentService());
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster, search);
        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain("could not be completed");
    }

    private static async Task<WebApplication> CreateAppAsync(UserPermission permissions, SymbolSearchService? search = null, bool tenantScoped = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        if (search is not null) builder.Services.AddSingleton(search);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            if (tenantScoped) context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "bond-discovery-test-tenant";
            await next();
        });
        app.MapBondDiscoveryEndpoints(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await app.StartAsync();
        return app;
    }
}
