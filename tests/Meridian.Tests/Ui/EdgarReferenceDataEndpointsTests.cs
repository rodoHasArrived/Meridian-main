using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class EdgarReferenceDataEndpointsTests
{
    [Fact]
    public async Task MapEdgarReferenceDataEndpoints_PostIngest_ReturnsResult()
    {
        var orchestrator = new FakeEdgarIngestOrchestrator();
        await using var app = await CreateAppAsync(orchestrator, new FakeEdgarReferenceDataStore(), UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.SecurityMasterEdgarIngest,
            new EdgarIngestRequest("all-filers", IncludeXbrl: true, Cik: "789019", MaxFilers: 1, DryRun: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        orchestrator.LastRequest.Should().NotBeNull();
        orchestrator.LastRequest!.IncludeXbrl.Should().BeTrue();
        orchestrator.LastRequest.Cik.Should().Be("789019");
        orchestrator.LastRequest.MaxFilers.Should().Be(1);

        var result = await response.Content.ReadFromJsonAsync<EdgarIngestResult>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        result!.DryRun.Should().BeTrue();
    }


    [Fact]
    public async Task MapEdgarReferenceDataEndpoints_PostIngest_RequiresModifySecurityMasterPermission()
    {
        var orchestrator = new FakeEdgarIngestOrchestrator();
        await using var app = await CreateAppAsync(orchestrator, new FakeEdgarReferenceDataStore(), UserPermission.ViewSecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.SecurityMasterEdgarIngest,
            new EdgarIngestRequest(MaxFilers: 1));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        orchestrator.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task MapEdgarReferenceDataEndpoints_PostIngest_AppliesServerSideMaxFilersCap()
    {
        var orchestrator = new FakeEdgarIngestOrchestrator();
        await using var app = await CreateAppAsync(orchestrator, new FakeEdgarReferenceDataStore(), UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.SecurityMasterEdgarIngest,
            new EdgarIngestRequest(MaxFilers: null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        orchestrator.LastRequest.Should().NotBeNull();
        orchestrator.LastRequest!.MaxFilers.Should().Be(250);
    }
    [Fact]
    public async Task MapEdgarReferenceDataEndpoints_GetFiler_ReturnsLocalPartition()
    {
        var store = new FakeEdgarReferenceDataStore
        {
            Filer = new EdgarFilerRecord(
                "0000789019",
                "MICROSOFT CORP",
                "operating",
                "7372",
                "Prepackaged Software",
                null,
                "WA",
                null,
                "0630",
                null,
                null,
                "https://www.microsoft.com",
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                ["MSFT"],
                ["Nasdaq"],
                [],
                [],
                DateTimeOffset.UtcNow)
        };
        await using var app = await CreateAppAsync(new FakeEdgarIngestOrchestrator(), store);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/reference-data/edgar/filers/789019");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var filer = await response.Content.ReadFromJsonAsync<EdgarFilerRecord>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        filer!.Cik.Should().Be("0000789019");
        filer.Tickers.Should().Contain("MSFT");
    }

    [Fact]
    public async Task MapEdgarReferenceDataEndpoints_GetSecurityData_ReturnsLocalPartition()
    {
        var store = new FakeEdgarReferenceDataStore
        {
            SecurityData = new EdgarSecurityDataRecord(
                "0000789019",
                [
                    new EdgarDebtOfferingTerms(
                        "0000789019",
                        "0001564590-23-000002",
                        "424B2",
                        "msft-424b2.htm",
                        "Prospectus supplement",
                        "MICROSOFT CORP",
                        "4.200% Senior Notes due 2033",
                        "594918BN3",
                        null,
                        "USD",
                        1_000_000_000,
                        1_000_000_000,
                        4.2m,
                        "Fixed",
                        "30/360",
                        "SemiAnnual",
                        new DateOnly(2023, 7, 31),
                        new DateOnly(2033, 8, 1),
                        null,
                        "Senior Unsecured",
                        null,
                        true,
                        null,
                        99.5m,
                        2_000,
                        1_000,
                        null,
                        [],
                        [],
                        0.9m,
                        [])
                ],
                [],
                DateTimeOffset.UtcNow)
        };
        await using var app = await CreateAppAsync(new FakeEdgarIngestOrchestrator(), store);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/reference-data/edgar/security-data/789019");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var securityData = await response.Content.ReadFromJsonAsync<EdgarSecurityDataRecord>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        securityData!.DebtOfferings.Should().ContainSingle(o => o.Cusip == "594918BN3");
    }

    private static async Task<WebApplication> CreateAppAsync(
        IEdgarIngestOrchestrator orchestrator,
        IEdgarReferenceDataStore store,
        UserPermission permissions = UserPermission.ModifySecurityMaster)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(orchestrator);
        builder.Services.AddSingleton(store);

        var app = builder.Build();
        app.Use((context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            return next();
        });
        app.MapEdgarReferenceDataEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private sealed class FakeEdgarIngestOrchestrator : IEdgarIngestOrchestrator
    {
        public EdgarIngestRequest? LastRequest { get; private set; }

        public Task<EdgarIngestResult> IngestAsync(EdgarIngestRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new EdgarIngestResult(
                FilersProcessed: 1,
                TickerAssociationsStored: request.DryRun ? 0 : 1,
                FactsStored: 0,
                SecurityDataStored: 0,
                SecuritiesCreated: 0,
                SecuritiesAmended: 0,
                SecuritiesSkipped: 0,
                ConflictsDetected: 0,
                DryRun: request.DryRun,
                Errors: []));
        }
    }

    private sealed class FakeEdgarReferenceDataStore : IEdgarReferenceDataStore
    {
        public EdgarFilerRecord? Filer { get; init; }
        public EdgarSecurityDataRecord? SecurityData { get; init; }

        public Task SaveTickerAssociationsAsync(IReadOnlyList<EdgarTickerAssociation> associations, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<EdgarTickerAssociation>> LoadTickerAssociationsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EdgarTickerAssociation>>([]);

        public Task SaveFilerAsync(EdgarFilerRecord record, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<EdgarFilerRecord?> LoadFilerAsync(string cik, CancellationToken ct = default)
            => Task.FromResult(Filer);

        public Task SaveFactsAsync(EdgarFactsRecord record, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<EdgarFactsRecord?> LoadFactsAsync(string cik, CancellationToken ct = default)
            => Task.FromResult<EdgarFactsRecord?>(null);

        public Task SaveSecurityDataAsync(EdgarSecurityDataRecord record, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<EdgarSecurityDataRecord?> LoadSecurityDataAsync(string cik, CancellationToken ct = default)
            => Task.FromResult(SecurityData);
    }
}
