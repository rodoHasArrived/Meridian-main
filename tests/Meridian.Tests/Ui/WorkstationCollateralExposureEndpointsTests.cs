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

    [Fact]
    public async Task MapWorkstationEndpoints_CollateralExposure_ShouldNotConsumeTheBufferItReports()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<CollateralExposureService>();
            services.AddSingleton<CollateralIngestionBuffer>();
        });

        var buffer = app.Services.GetRequiredService<CollateralIngestionBuffer>();
        buffer.TryIngest(new CollateralInputRow(
            AsOf: DateTimeOffset.UtcNow,
            Counterparty: "northwind-bank",
            ProductType: "swap",
            PositionNotional: 5_000m,
            MarkToMarket: 1_000m,
            CollateralBalance: 400m,
            CollateralType: "cash",
            InitialMargin: 100m,
            VariationMargin: 50m)).Should().BeTrue();

        var client = app.GetTestClient();

        // Exposure is an aggregate of what is buffered, so reading it must leave the buffer intact.
        // Draining on read made the snapshot cover only the rows arriving since the previous reader:
        // two operators looking at the same moment saw different exposure, and the second saw none.
        var first = await client.GetAsync("/api/workstation/collateral/exposure");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstPayload = await first.Content.ReadFromJsonAsync<ExposureSnapshotDto>(ServerJsonOptions);
        firstPayload!.Counterparties.Should().ContainSingle();

        var second = await client.GetAsync("/api/workstation/collateral/exposure");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondPayload = await second.Content.ReadFromJsonAsync<ExposureSnapshotDto>(ServerJsonOptions);

        secondPayload!.Counterparties.Should().ContainSingle(
            "a second reader must see the same exposure as the first, not an emptied buffer");
        buffer.BufferedCount.Should().Be(1, "reading exposure is not a consumption of collateral input");
    }
}
