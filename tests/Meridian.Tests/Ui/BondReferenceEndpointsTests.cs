using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.FixedIncome;
using Meridian.Contracts.FixedIncome;
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

    private static async Task<WebApplication> CreateAppAsync(IBondReferenceService service)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(service);

        var app = builder.Build();
        app.MapBondReferenceEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }
}
