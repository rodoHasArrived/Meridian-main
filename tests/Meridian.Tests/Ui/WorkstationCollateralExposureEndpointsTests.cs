using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_CollateralExposure_ShouldReturnSnapshotWithThresholdsAndCalls()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<CollateralExposureService>();
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/collateral/exposure");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ExposureSnapshotDto>(ServerJsonOptions);
        payload.Should().NotBeNull();
        payload!.Counterparties.Should().NotBeEmpty();
        payload.Breaches.Should().NotBeEmpty();
        payload.Trend.Should().HaveCount(12);
        payload.IngestionMode.Should().StartWith("micro-batch");
    }
}
