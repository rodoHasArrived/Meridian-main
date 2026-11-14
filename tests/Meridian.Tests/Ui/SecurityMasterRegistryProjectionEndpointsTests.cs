using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;
using ReadServiceTests = Meridian.Tests.SecurityMaster.RegistryProjectionReadServiceTests;

namespace Meridian.Tests.Ui;

/// <summary>
/// <c>GET /api/security-master/projections/{assetClass}/{securityId}</c> serves the generic
/// registry read for every projection-registry descriptor, answers 404 for a class without a
/// registry projection and for a security of another class, and sits behind the Security Master
/// view permission like its neighbouring read routes.
/// </summary>
public sealed class SecurityMasterRegistryProjectionEndpointsTests
{
    private static readonly Guid SecurityId = Guid.Parse("4c0f2b1e-9d8a-4e7f-b6c5-a4b3c2d1e0f9");
    private static readonly SecurityMasterOptions Options = new() { Schema = "sm_test", ConnectionString = "Host=unused" };

    public static TheoryData<string> EveryDescriptor() => ReadServiceTests.EveryDescriptor();

    [Theory]
    [MemberData(nameof(EveryDescriptor))]
    public async Task Get_ServesEveryRegistryDescriptor(string assetClass)
    {
        var descriptor = SecurityTermsProjectionRegistry.Require(assetClass);
        await using var app = await CreateAppAsync(ReadServiceTests.StoreReturning(assetClass));

        using var response = await app.GetTestClient().GetAsync($"/api/security-master/projections/{assetClass}/{SecurityId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        root.GetProperty("securityId").GetGuid().Should().Be(SecurityId);
        root.GetProperty("assetClass").GetString().Should().Be(assetClass);
        root.GetProperty("tableName").GetString().Should().Be(descriptor.TableName);
        // Column names are SQL names and are not re-cased by the camelCase policy.
        root.GetProperty("columns").EnumerateObject().Select(property => property.Name)
            .Should().Equal(SecurityTermsProjectionRegistry.ParentColumnNames(descriptor));
        root.GetProperty("childTables").EnumerateArray().Select(child => child.GetProperty("tableName").GetString())
            .Should().Equal(descriptor.ChildTables.Select(child => child.TableName));
        root.GetProperty("satelliteTables").EnumerateArray().Select(satellite => satellite.GetProperty("tableName").GetString())
            .Should().Equal(descriptor.Satellites.Select(satellite => satellite.TableName));
    }

    [Theory]
    [InlineData("Option")]
    [InlineData("CustomAsset")]
    [InlineData("InfrastructureHolding")]
    public async Task Get_AClassWithoutARegistryProjection_Is404(string assetClass)
    {
        await using var app = await CreateAppAsync(ReadServiceTests.StoreReturning(assetClass));

        using var response = await app.GetTestClient().GetAsync($"/api/security-master/projections/{assetClass}/{SecurityId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_ASecurityOfAnotherClass_OrAnUnknownSecurity_Is404()
    {
        await using (var mismatch = await CreateAppAsync(ReadServiceTests.StoreReturning("Repo")))
        {
            using var response = await mismatch.GetTestClient().GetAsync($"/api/security-master/projections/CommercialPaper/{SecurityId}");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        await using var missing = await CreateAppAsync(Substitute.For<ISecurityMasterStore>());
        using var unknown = await missing.GetTestClient().GetAsync($"/api/security-master/projections/CommercialPaper/{Guid.NewGuid()}");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_WithoutSecurityMasterViewPermission_IsForbidden()
    {
        await using var app = await CreateAppAsync(ReadServiceTests.StoreReturning("CommercialPaper"), UserPermission.ViewMarketData);

        using var response = await app.GetTestClient().GetAsync($"/api/security-master/projections/CommercialPaper/{SecurityId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<WebApplication> CreateAppAsync(
        ISecurityMasterStore store,
        UserPermission permissions = UserPermission.ViewSecurityMaster)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterQueryService>());
        builder.Services.AddSingleton<IRegistryProjectionReadService>(
            new RegistryProjectionReadService(Options, store, new ReadServiceTests.FakeRowSource()));

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapSecurityMasterEndpoints(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await app.StartAsync();
        return app;
    }
}
