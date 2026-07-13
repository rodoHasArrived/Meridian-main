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
    public async Task MapWorkstationEndpoints_CollateralExposure_WithoutIngestedRows_ShouldReturnHonestlyEmptySnapshot()
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

        // No collateral rows have been ingested, so the snapshot must not invent counterparties,
        // breaches, or collateral calls. The ingestion-mode label states that the buffer is empty.
        payload!.Counterparties.Should().BeEmpty();
        payload.Breaches.Should().BeEmpty();
        payload.CollateralCalls.Should().BeEmpty();
        payload.Trend.Should().HaveCount(12);
        payload.IngestionMode.Should().Be("micro-batch buffer (empty)");
    }
}
