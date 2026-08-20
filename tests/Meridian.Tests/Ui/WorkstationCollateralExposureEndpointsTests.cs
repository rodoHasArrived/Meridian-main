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
        var client = app.GetTestClient();

        // Ingested through the route rather than seeded in process, so ingest and read agree on the
        // tenant scope by construction. Seeding directly would pass only while the two happened to
        // resolve the same key -- which is the thing worth proving, not assuming.
        var ingest = await client.PostAsJsonAsync(
            "/api/workstation/collateral/ingest",
            new[]
            {
                new CollateralInputRow(
                    AsOf: DateTimeOffset.UtcNow,
                    Counterparty: "northwind-bank",
                    ProductType: "swap",
                    PositionNotional: 5_000m,
                    MarkToMarket: 1_000m,
                    CollateralBalance: 400m,
                    CollateralType: "cash",
                    InitialMargin: 100m,
                    VariationMargin: 50m)
            },
            ServerJsonOptions);
        ingest.StatusCode.Should().Be(HttpStatusCode.Accepted);

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
        buffer.BufferedCount(CollateralTenantScope.For("tenant-test", "tenant-test"))
            .Should().Be(1, "reading exposure is not a consumption of collateral input");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_CollateralExposure_ShouldNotServeAnotherTenantsRows()
    {
        // The buffer is a process-wide singleton, so without a server-resolved scope key one tenant's
        // ingest lands in every tenant's exposure -- and a same-named counterparty restatement from
        // either overwrites the other's current reading.
        await using var app = await CreateAppAsync(
            services =>
            {
                services.AddSingleton<CollateralExposureService>();
                services.AddSingleton<CollateralIngestionBuffer>();
            },
            currentUserCompanyId: "tenant-alpha");

        var alphaIngest = await app.GetTestClient().PostAsJsonAsync(
            "/api/workstation/collateral/ingest",
            new[]
            {
                new CollateralInputRow(
                    AsOf: DateTimeOffset.UtcNow,
                    Counterparty: "shared-counterparty",
                    ProductType: "repo",
                    PositionNotional: 1m,
                    MarkToMarket: 100m,
                    CollateralBalance: 1m,
                    CollateralType: "cash",
                    InitialMargin: 1m,
                    VariationMargin: 0m)
            },
            ServerJsonOptions);
        alphaIngest.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var buffer = app.Services.GetRequiredService<CollateralIngestionBuffer>();
        buffer.BufferedCount(CollateralTenantScope.For("tenant-alpha", "tenant-alpha")).Should().Be(1);
        buffer.BufferedCount(CollateralTenantScope.For("tenant-beta", "tenant-beta")).Should().Be(0);
        buffer.SnapshotCurrent(CollateralTenantScope.For("tenant-beta", "tenant-beta")).Should().BeEmpty(
            "another tenant reads nothing rather than falling back to a shared buffer");
    }
}
