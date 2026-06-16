using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Api;
using Meridian.Contracts.Integrations;
using Meridian.Identity.Auth;
using Meridian.Storage.Integrations;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using IntegrationProviderConnectionDto = Meridian.Contracts.Integrations.ProviderConnectionDto;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationTemplates_ReturnsStarterTemplatePack()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewConfig);
            var client = app.GetTestClient();

            var templates = await client.GetFromJsonAsync<IReadOnlyList<ProviderIntegrationTemplateCatalogEntryDto>>(
                UiApiRoutes.WorkstationProviderIntegrationTemplates,
                ServerJsonOptions);

            templates.Should().NotBeNull();
            templates!.Select(template => template.ManifestId).Should().BeEquivalentTo(
                "template-manual-csv-upload-v1",
                "template-custodian-positions-v1",
                "template-brokerage-transactions-v1",
                "template-fixed-income-security-master-v1");
            templates.Should().Contain(template =>
                template.ManifestId == "template-manual-csv-upload-v1" &&
                template.IntegrationType == IntegrationTypeDto.ManualUpload &&
                template.Capabilities.Contains(ProviderCapabilityKindDto.Positions) &&
                template.Capabilities.Contains(ProviderCapabilityKindDto.Transactions));
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationTemplate_ReturnsManifestDetail()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewConfig);
            var client = app.GetTestClient();

            var manifest = await client.GetFromJsonAsync<ProviderIntegrationManifestDto>(
                ProviderIntegrationTemplateRoute("template-custodian-positions-v1"),
                ServerJsonOptions);

            manifest.Should().NotBeNull();
            manifest!.ManifestId.Should().Be("template-custodian-positions-v1");
            manifest.IntegrationType.Should().Be(IntegrationTypeDto.Rest);
            manifest.Endpoints.Should().Contain(endpoint => endpoint.EndpointKey == "accounts");
            manifest.Endpoints.Should().Contain(endpoint => endpoint.EndpointKey == "positions");
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationMonitor_ReturnsLatestDurableEvidence()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = await CreateSeededProviderIntegrationStoreAsync(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewConfig);
            var client = app.GetTestClient();

            var monitor = await client.GetFromJsonAsync<ProviderIntegrationConnectionMonitorDto>(
                $"{ProviderIntegrationMonitorRoute("connection-alpha")}?recentRunLimit=1",
                ServerJsonOptions);

            monitor.Should().NotBeNull();
            monitor!.ConnectionId.Should().Be("connection-alpha");
            monitor.DisplayName.Should().Be("Custodian ABC");
            monitor.RecentSyncRuns.Should().ContainSingle();
            monitor.LastSyncRun.Should().NotBeNull();
            monitor.LastSyncRun!.SyncRunId.Should().Be("sync-run-new");
            monitor.LastSyncRun.DurableStagingRecordCount.Should().Be(1);
            monitor.LastSyncRun.DurableQuarantinedRecordCount.Should().Be(2);
            monitor.LastSyncRun.CriticalIssueCount.Should().Be(1);
            monitor.RecentRecordsReceived.Should().Be(3);
            monitor.RecentRecordsAccepted.Should().Be(1);
            monitor.RecentRecordsQuarantined.Should().Be(2);
            monitor.HasCriticalIssues.Should().BeTrue();
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationReadiness_ReturnsActivationBlockers()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = await CreateSeededProviderIntegrationStoreAsync(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewConfig);
            var client = app.GetTestClient();

            var readiness = await client.GetFromJsonAsync<ProviderIntegrationActivationReadinessDto>(
                $"{ProviderIntegrationReadinessRoute("manifest-custodian-abc-v1")}?connectionId=connection-alpha",
                ServerJsonOptions);

            readiness.Should().NotBeNull();
            readiness!.IsReady.Should().BeFalse();
            readiness.RequiredEvidence.Should().Contain("activation-approval");
            readiness.Issues.Should().Contain(issue =>
                issue.Code == "provider-manifest.required-mapping-missing" &&
                issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationReadiness_ReturnsNotFoundForMissingManifest()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewConfig);
            var client = app.GetTestClient();

            var response = await client.GetAsync(ProviderIntegrationReadinessRoute("missing-manifest"));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationTemplates_RequireProviderReadPermission()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewMarketData);
            var client = app.GetTestClient();

            var response = await client.GetAsync(UiApiRoutes.WorkstationProviderIntegrationTemplates);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationMonitor_ReturnsNotFoundForMissingConnection()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewConfig);
            var client = app.GetTestClient();

            var response = await client.GetAsync(ProviderIntegrationMonitorRoute("missing-connection"));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationMonitor_RequiresProviderReadPermission()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = await CreateSeededProviderIntegrationStoreAsync(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewMarketData);
            var client = app.GetTestClient();

            var response = await client.GetAsync(ProviderIntegrationMonitorRoute("connection-alpha"));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    private static void RegisterProviderIntegrationEndpointServices(
        IServiceCollection services,
        IProviderIntegrationManifestStore store)
    {
        services.AddSingleton<IProviderIntegrationManifestStore>(store);
        services.AddSingleton<ProviderIntegrationTemplateCatalog>();
        services.AddSingleton<ProviderIntegrationActivationReadinessService>();
        services.AddSingleton<ProviderIntegrationMonitoringService>();
    }

    private static async Task<FileProviderIntegrationManifestStore> CreateSeededProviderIntegrationStoreAsync(string testRoot)
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateProviderIntegrationEndpointManifest();
        var connection = CreateProviderIntegrationEndpointConnection(manifest);
        await store.SaveManifestAsync(manifest).ConfigureAwait(false);
        await store.SaveConnectionAsync(connection).ConfigureAwait(false);
        await SaveProviderIntegrationEndpointRunAsync(
            store,
            CreateProviderIntegrationEndpointSyncRun(
                "sync-run-old",
                manifest,
                connection,
                DateTimeOffset.Parse("2026-06-16T10:00:00Z"),
                ProviderIntegrationProcessingStatusDto.Validated,
                received: 2,
                accepted: 2,
                quarantined: 0,
                issues: []),
            stagingCount: 2,
            quarantineCount: 0).ConfigureAwait(false);
        await SaveProviderIntegrationEndpointRunAsync(
            store,
            CreateProviderIntegrationEndpointSyncRun(
                "sync-run-new",
                manifest,
                connection,
                DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
                ProviderIntegrationProcessingStatusDto.Quarantined,
                received: 3,
                accepted: 1,
                quarantined: 2,
                issues:
                [
                    new ValidationIssueDto(
                        "required.missing",
                        ProviderIntegrationIssueSeverityDto.Critical,
                        "Security identifier is required.",
                        "security.cusip",
                        "Map CUSIP before activation.")
                ]),
            stagingCount: 1,
            quarantineCount: 2).ConfigureAwait(false);
        return store;
    }

    private static async Task SaveProviderIntegrationEndpointRunAsync(
        FileProviderIntegrationManifestStore store,
        ProviderIntegrationSyncRunDto syncRun,
        int stagingCount,
        int quarantineCount)
    {
        await store.SaveSyncRunAsync(syncRun).ConfigureAwait(false);
        for (var index = 0; index < stagingCount; index++)
        {
            await store.SaveStagingRecordAsync(
                new IntegrationStagingRecordDto(
                    $"staging-{syncRun.SyncRunId}-{index}",
                    syncRun.SyncRunId,
                    syncRun.ConnectionId,
                    syncRun.Capability,
                    syncRun.RawPayloadId ?? "payload-missing",
                    $"source-{index}",
                    $"{syncRun.ConnectionId}:{syncRun.Capability}:source-{index}",
                    ProviderIntegrationEndpointJson("""{"providerAccountId":"A-100","quantity":100,"asOf":"2026-06-16"}"""),
                    [],
                    ProviderIntegrationProcessingStatusDto.Validated,
                    syncRun.StartedAt)).ConfigureAwait(false);
        }

        for (var index = 0; index < quarantineCount; index++)
        {
            await store.SaveQuarantinedRecordAsync(
                new QuarantinedRecordDto(
                    $"quarantine-{syncRun.SyncRunId}-{index}",
                    syncRun.SyncRunId,
                    syncRun.ConnectionId,
                    syncRun.Capability,
                    ProviderIntegrationEndpointJson("""{"account_id":"A-100"}"""),
                    ProviderIntegrationEndpointJson("""{"providerAccountId":"A-100"}"""),
                    syncRun.Issues,
                    ProviderIntegrationProcessingStatusDto.Quarantined,
                    syncRun.StartedAt)).ConfigureAwait(false);
        }
    }

    private static ProviderIntegrationSyncRunDto CreateProviderIntegrationEndpointSyncRun(
        string syncRunId,
        ProviderIntegrationManifestDto manifest,
        IntegrationProviderConnectionDto connection,
        DateTimeOffset startedAt,
        ProviderIntegrationProcessingStatusDto status,
        int received,
        int accepted,
        int quarantined,
        IReadOnlyList<ValidationIssueDto> issues)
        => new(
            syncRunId,
            manifest.ManifestId,
            connection.ConnectionId,
            manifest.ProviderId,
            ProviderCapabilityKindDto.Positions,
            "positions",
            startedAt,
            startedAt.AddMinutes(2),
            status,
            received,
            accepted,
            quarantined,
            $"payload-{syncRunId}",
            issues);

    private static ProviderIntegrationManifestDto CreateProviderIntegrationEndpointManifest()
        => new(
            "manifest-custodian-abc-v1",
            1,
            "custodian-abc",
            "Custodian ABC",
            IntegrationTypeDto.Rest,
            "production",
            new ProviderIntegrationAuthConfigDto(
                ProviderIntegrationAuthTypeDto.OAuth2,
                "https://api.example.com/oauth/token",
                ["positions.read"],
                new Dictionary<string, string>()),
            [
                new ProviderCapabilityDto(
                    ProviderCapabilityKindDto.Positions,
                    Enabled: true,
                    RequiresCertifiedAdapter: false,
                    RequiredCanonicalFields: ["providerAccountId", "quantity", "asOf"])
            ],
            [],
            [],
            new SyncScheduleDto(
                "incremental",
                "daily",
                "06:00",
                "America/New_York",
                ProviderIntegrationCursorTypeDto.Timestamp,
                "updated_at",
                "monthly"),
            [],
            new ProviderIntegrationActivationPolicyDto(
                RequiresAuthenticationTest: true,
                RequiresEndpointTest: true,
                RequiresDryRun: true,
                RequiresApproval: true,
                ProductionWriteCapabilitiesAllowed: false,
                RequiredIssueCodes: []),
            ProviderIntegrationActivationStateDto.DryRunPassed,
            "ops@example.com",
            DateTimeOffset.Parse("2026-06-16T09:00:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Workstation monitor endpoint test manifest");

    private static IntegrationProviderConnectionDto CreateProviderIntegrationEndpointConnection(ProviderIntegrationManifestDto manifest)
        => new(
            "connection-alpha",
            manifest.ProviderId,
            manifest.ManifestId,
            "General Account",
            manifest.Environment,
            ProviderIntegrationActivationStateDto.DryRunPassed,
            "vault://provider-credentials/custodian-abc/general-account",
            [ProviderCapabilityKindDto.Positions],
            "ops@example.com",
            DateTimeOffset.Parse("2026-06-16T09:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T09:05:00Z"),
            ApprovalEvidenceId: null);

    private static string ProviderIntegrationMonitorRoute(string connectionId)
        => UiApiRoutes.WorkstationProviderIntegrationConnectionMonitor.Replace(
            "{connectionId}",
            Uri.EscapeDataString(connectionId),
            StringComparison.Ordinal);

    private static string ProviderIntegrationTemplateRoute(string manifestId)
        => UiApiRoutes.WorkstationProviderIntegrationTemplateById.Replace(
            "{manifestId}",
            Uri.EscapeDataString(manifestId),
            StringComparison.Ordinal);

    private static string ProviderIntegrationReadinessRoute(string manifestId)
        => UiApiRoutes.WorkstationProviderIntegrationManifestReadiness.Replace(
            "{manifestId}",
            Uri.EscapeDataString(manifestId),
            StringComparison.Ordinal);

    private static string CreateProviderIntegrationTestRoot()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"mdc_provider_integration_endpoint_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
        return testRoot;
    }

    private static void DeleteProviderIntegrationTestRoot(string testRoot)
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static JsonElement ProviderIntegrationEndpointJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
