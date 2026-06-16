using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Identity.Auth;
using Meridian.Instruments.FixedIncome;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class BondReferenceEndpointsTests
{
    [Fact]
    public async Task BondReferenceEndpoints_ShouldReturnLifecycleAndAccrualData()
    {
        var securityId = Guid.NewGuid();
        var service = Substitute.For<IBondReferenceService>();
        service.GetReferenceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new BondReferenceDto(
                securityId,
                "Meridian 2032 Senior Notes",
                "USD",
                "Meridian Treasury LLC",
                "SeniorUnsecured",
                "M2032",
                new BondLifecycleDto(
                    securityId,
                    BondLifecycleStat.Callable,
                    new DateOnly(2024, 1, 15),
                    new DateOnly(2029, 1, 15),
                    new DateOnly(2032, 1, 15),
                    true,
                    9),
                new BondAccrualConventionDto(
                    securityId,
                    "30/360",
                    2,
                    "NYSE",
                    "Fixed",
                    5.125m,
                    null,
                    null,
                    9),
                9));

        await using var app = await CreateAppAsync(service);
        var client = app.GetTestClient();

        using var response = await client.GetAsync($"/api/reference-data/bonds/{securityId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<BondReferenceDto>();
        dto.Should().NotBeNull();
        dto!.Lifecycle!.LifecycleStat.Should().Be(BondLifecycleStat.Callable);
        dto.AccrualConvention!.DayCountConvention.Should().Be("30/360");
        dto.AccrualConvention.FixedCouponRate.Should().Be(5.125m);
    }

    [Fact]
    public async Task BondReferenceEndpoints_WithModifySecurityMasterPermission_ShouldReturnReferenceData()
    {
        var securityId = Guid.NewGuid();
        var service = Substitute.For<IBondReferenceService>();
        service.GetLifecycleAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new BondLifecycleDto(
                securityId,
                BondLifecycleStat.Active,
                new DateOnly(2024, 1, 15),
                null,
                new DateOnly(2032, 1, 15),
                false,
                3));

        await using var app = await CreateAppAsync(service, UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.GetAsync($"/api/reference-data/bonds/{securityId}/lifecycle");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BondReferenceEndpoints_WhenUserLacksSecurityMasterReadPermission_ShouldReturnForbidden()
    {
        var securityId = Guid.NewGuid();
        var service = Substitute.For<IBondReferenceService>();

        await using var app = await CreateAppAsync(service, UserPermission.ViewTrades);
        var client = app.GetTestClient();

        using var response = await client.GetAsync($"/api/reference-data/bonds/{securityId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await service.DidNotReceiveWithAnyArgs().GetReferenceAsync(default, default);
    }

    [Fact]
    public async Task BondIssuerLadder_WhenIssuerNameIsBlank_ShouldReturnBadRequest()
    {
        var service = Substitute.For<IBondReferenceService>();

        await using var app = await CreateAppAsync(service);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/reference-data/bonds/issuer-ladder?issuerName=%20%20");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await service.DidNotReceiveWithAnyArgs().GetIssuerLadderAsync(default!, default);
    }

    [Fact]
    public async Task BondIssuerLadder_WhenIssuerNameIsTooLong_ShouldReturnBadRequest()
    {
        var service = Substitute.For<IBondReferenceService>();
        var issuerName = new string('A', 201);

        await using var app = await CreateAppAsync(service);
        var client = app.GetTestClient();

        using var response = await client.GetAsync($"/api/reference-data/bonds/issuer-ladder?issuerName={issuerName}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await service.DidNotReceiveWithAnyArgs().GetIssuerLadderAsync(default!, default);
    }

    [Theory]
    [InlineData("2030-01-01", "2029-01-01")]
    [InlineData("2026-01-01", "2077-01-01")]
    public async Task BondMaturityLadder_WhenDateRangeIsInvalid_ShouldReturnBadRequest(string from, string to)
    {
        var service = Substitute.For<IBondReferenceService>();

        await using var app = await CreateAppAsync(service);
        var client = app.GetTestClient();

        using var response = await client.GetAsync($"/api/reference-data/bonds/maturity-ladder?from={from}&to={to}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await service.DidNotReceiveWithAnyArgs().GetMaturityLadderAsync(default, default, default);
    }

    private static async Task<WebApplication> CreateAppAsync(
        IBondReferenceService service,
        UserPermission permissions = UserPermission.ViewSecurityMaster)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(service);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapBondReferenceEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }
}
