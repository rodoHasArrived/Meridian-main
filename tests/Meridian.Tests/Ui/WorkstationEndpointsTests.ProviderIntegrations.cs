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
    private const string DefaultProviderIntegrationTenantId = "tenant-test";

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
    public async Task MapWorkstationEndpoints_ProviderIntegrationSetupSave_PersistsDraftInTenantPartition()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ManageProviders);
            var client = app.GetTestClient();
            var request = CreateProviderIntegrationSetupSaveRequest();

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationSetupSave,
                request,
                ServerJsonOptions);
            var result = await response.Content.ReadFromJsonAsync<ProviderIntegrationSetupSaveResultDto>(ServerJsonOptions);
            var tenantStore = DefaultProviderIntegrationTenantStore(store);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            result.Should().NotBeNull();
            result!.Saved.Should().BeTrue();
            result.Readiness.Issues.Should().Contain(issue => issue.Code == "provider-manifest.endpoint-test-required");
            (await tenantStore.GetManifestAsync(request.Manifest.ManifestId))!.ChangeReason.Should()
                .Be("Saved from workstation endpoint.");
            (await tenantStore.GetConnectionAsync(request.Connection.ConnectionId))!.UpdatedAt.Should()
                .Be(DateTimeOffset.Parse("2026-06-16T15:00:00Z"));
            (await store.GetManifestAsync(request.Manifest.ManifestId)).Should().BeNull();
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationSetupSave_ReturnsBadRequestForMismatchedConnection()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ManageProviders);
            var client = app.GetTestClient();
            var request = CreateProviderIntegrationSetupSaveRequest(connectionManifestId: "manifest-other");

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationSetupSave,
                request,
                ServerJsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await DefaultProviderIntegrationTenantStore(store).GetManifestAsync(request.Manifest.ManifestId)).Should().BeNull();
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationSetupSave_RequiresConfigurePermission()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewConfig);
            var client = app.GetTestClient();
            var request = CreateProviderIntegrationSetupSaveRequest();

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationSetupSave,
                request,
                ServerJsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await DefaultProviderIntegrationTenantStore(store).GetManifestAsync(request.Manifest.ManifestId)).Should().BeNull();
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
    public async Task MapWorkstationEndpoints_ProviderIntegrationQuarantineReview_ReturnsIssueGroups()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = await CreateSeededProviderIntegrationStoreAsync(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewConfig);
            var client = app.GetTestClient();

            var review = await client.GetFromJsonAsync<ProviderIntegrationQuarantineReviewDto>(
                $"{ProviderIntegrationQuarantineReviewRoute("connection-alpha")}?recentRunLimit=1",
                ServerJsonOptions);

            review.Should().NotBeNull();
            review!.ConnectionId.Should().Be("connection-alpha");
            review.SyncRunIds.Should().ContainSingle().Which.Should().Be("sync-run-new");
            review.TotalQuarantinedRecords.Should().Be(2);
            review.CriticalIssueCount.Should().Be(2);
            review.IssueGroups.Should().ContainSingle(group =>
                group.IssueCode == "required.missing" &&
                group.RecordCount == 2);
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationQuarantineResolve_PersistsDecision()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = await CreateSeededProviderIntegrationStoreAsync(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ManageProviders);
            var client = app.GetTestClient();
            var request = new ProviderIntegrationQuarantineResolutionRequestDto(
                "connection-alpha",
                "sync-run-new",
                "quarantine-sync-run-new-0",
                ProviderIntegrationQuarantineResolutionActionDto.ReplayAfterMappingChange,
                "operator@example.com",
                DateTimeOffset.Parse("2026-06-16T12:10:00Z"),
                "CUSIP mapping was updated.");

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationQuarantineResolve,
                request,
                ServerJsonOptions);
            var result = await response.Content.ReadFromJsonAsync<ProviderIntegrationQuarantineResolutionResultDto>(ServerJsonOptions);
            var decisions = await DefaultProviderIntegrationTenantStore(store)
                .ListQuarantineDecisionsAsync(request.SyncRunId);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            result.Should().NotBeNull();
            result!.Resolved.Should().BeTrue();
            result.Decision.Action.Should().Be(ProviderIntegrationQuarantineResolutionActionDto.ReplayAfterMappingChange);
            decisions.Should().ContainSingle()
                .Which.QuarantineRecordId.Should().Be(request.QuarantineRecordId);
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationQuarantineResolve_RequiresConfigurePermission()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = await CreateSeededProviderIntegrationStoreAsync(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewConfig);
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationQuarantineResolve,
                new ProviderIntegrationQuarantineResolutionRequestDto(
                    "connection-alpha",
                    "sync-run-new",
                    "quarantine-sync-run-new-0",
                    ProviderIntegrationQuarantineResolutionActionDto.IgnoreProviderRecord,
                    "operator@example.com",
                    DateTimeOffset.Parse("2026-06-16T12:10:00Z"),
                    "Provider duplicate."),
                ServerJsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await DefaultProviderIntegrationTenantStore(store).ListQuarantineDecisionsAsync("sync-run-new"))
                .Should()
                .BeEmpty();
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
    public async Task MapWorkstationEndpoints_ProviderIntegrationReadiness_UsesRequestTenantPartition()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            var alphaStore = ((IProviderIntegrationTenantManifestStoreFactory)store).ForTenant("tenant-alpha");
            var manifest = CreateProviderIntegrationEndpointManifest();
            var connection = CreateProviderIntegrationEndpointConnection(manifest);
            await alphaStore.SaveManifestAsync(manifest);
            await alphaStore.SaveConnectionAsync(connection);
            await using var alphaApp = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewConfig,
                currentUserCompanyId: "tenant-alpha");
            await using var betaApp = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewConfig,
                currentUserCompanyId: "tenant-beta");
            var alphaClient = alphaApp.GetTestClient();
            var betaClient = betaApp.GetTestClient();
            var route = $"{ProviderIntegrationReadinessRoute(manifest.ManifestId)}?connectionId={Uri.EscapeDataString(connection.ConnectionId)}";

            var alphaResponse = await alphaClient.GetAsync(route);
            var betaResponse = await betaClient.GetAsync(route);

            alphaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            betaResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
    public async Task MapWorkstationEndpoints_ProviderIntegrationManualCsvDryRun_RetainsAndStagesAcceptedRows()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            var tenantStore = DefaultProviderIntegrationTenantStore(store);
            var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-manual-csv-upload-v1")!;
            var connection = CreateProviderIntegrationManualCsvConnection(manifest);
            await tenantStore.SaveManifestAsync(manifest);
            await tenantStore.SaveConnectionAsync(connection);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ManageProviders);
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationManualCsvDryRun,
                CreateManualCsvDryRunRequest(manifest, connection),
                ServerJsonOptions);
            var result = await response.Content.ReadFromJsonAsync<ProviderIntegrationDryRunResultDto>(ServerJsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            result.Should().NotBeNull();
            result!.RecordsReceived.Should().Be(1);
            result.RecordsAccepted.Should().Be(1);
            result.RecordsQuarantined.Should().Be(0);
            result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Validated);
            (await tenantStore.GetRawPayloadAsync(result.SyncRunId, result.RawPayloadId)).Should().NotBeNull();
            (await tenantStore.ListStagingRecordsAsync(result.SyncRunId)).Should().ContainSingle();
            (await tenantStore.ListQuarantinedRecordsAsync(result.SyncRunId)).Should().BeEmpty();
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationManualCsvDryRun_ReturnsNotFoundForMissingManifest()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-manual-csv-upload-v1")!;
            var connection = CreateProviderIntegrationManualCsvConnection(manifest);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ManageProviders);
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationManualCsvDryRun,
                CreateManualCsvDryRunRequest(manifest, connection),
                ServerJsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationManualCsvDryRun_RequiresConfigurePermission()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            var tenantStore = DefaultProviderIntegrationTenantStore(store);
            var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-manual-csv-upload-v1")!;
            var connection = CreateProviderIntegrationManualCsvConnection(manifest);
            await tenantStore.SaveManifestAsync(manifest);
            await tenantStore.SaveConnectionAsync(connection);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewConfig);
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationManualCsvDryRun,
                CreateManualCsvDryRunRequest(manifest, connection),
                ServerJsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await tenantStore.GetSyncRunAsync("sync-run-manual-endpoint-1")).Should().BeNull();
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationRestDryRun_RetainsAndStagesAcceptedRows()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            var tenantStore = DefaultProviderIntegrationTenantStore(store);
            var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-custodian-positions-v1")!;
            var connection = CreateProviderIntegrationRestConnection(manifest);
            await tenantStore.SaveManifestAsync(manifest);
            await tenantStore.SaveConnectionAsync(connection);
            var transport = new ProviderIntegrationEndpointTransport(
                new ProviderIntegrationHttpResponse(
                    200,
                    new Dictionary<string, string>(),
                    """
                    {
                      "positions": [
                        {
                          "account_id": "A-100",
                          "cusip": "9128285M8",
                          "quantity": "100",
                          "currency": "usd",
                          "as_of_date": "2026-06-16",
                          "position_id": "POS-1"
                        }
                      ]
                    }
                    """));
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store, transport),
                currentUserPermissions: UserPermission.ManageProviders);
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationRestDryRun,
                CreateRestDryRunRequest(manifest, connection),
                ServerJsonOptions);
            var result = await response.Content.ReadFromJsonAsync<ProviderIntegrationDryRunResultDto>(ServerJsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            result.Should().NotBeNull();
            result!.RecordsReceived.Should().Be(1);
            result.RecordsAccepted.Should().Be(1);
            result.RecordsQuarantined.Should().Be(0);
            transport.Requests.Should().ContainSingle()
                .Which.Path.Should().Be("/v1/accounts/A-100/positions");
            (await tenantStore.GetRawPayloadAsync(result.SyncRunId, result.RawPayloadId)).Should().NotBeNull();
            (await tenantStore.ListStagingRecordsAsync(result.SyncRunId)).Should().ContainSingle();
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationActivate_PersistsActiveState()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            var tenantStore = DefaultProviderIntegrationTenantStore(store);
            var manifest = CreateProviderIntegrationActivationManifest();
            var connection = CreateProviderIntegrationActivationConnection(manifest);
            await tenantStore.SaveManifestAsync(manifest);
            await tenantStore.SaveConnectionAsync(connection);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ManageProviders);
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationActivate,
                CreateProviderIntegrationActivationRequest(manifest, connection),
                ServerJsonOptions);
            var result = await response.Content.ReadFromJsonAsync<ProviderIntegrationActivationResultDto>(ServerJsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            result.Should().NotBeNull();
            result!.Activated.Should().BeTrue();
            result.ManifestState.Should().Be(ProviderIntegrationActivationStateDto.Active);
            result.ConnectionState.Should().Be(ProviderIntegrationActivationStateDto.Active);
            (await tenantStore.GetManifestAsync(manifest.ManifestId))!.State.Should().Be(ProviderIntegrationActivationStateDto.Active);
            (await tenantStore.GetConnectionAsync(connection.ConnectionId))!.ApprovalEvidenceId.Should().Be("approval-evidence-endpoint-1");
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationActivate_ReturnsBlockedResultWhenReadinessFails()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            var tenantStore = DefaultProviderIntegrationTenantStore(store);
            var manifest = CreateProviderIntegrationActivationManifest(missingQuantityMapping: true);
            var connection = CreateProviderIntegrationActivationConnection(manifest);
            await tenantStore.SaveManifestAsync(manifest);
            await tenantStore.SaveConnectionAsync(connection);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ManageProviders);
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationActivate,
                CreateProviderIntegrationActivationRequest(manifest, connection),
                ServerJsonOptions);
            var result = await response.Content.ReadFromJsonAsync<ProviderIntegrationActivationResultDto>(ServerJsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            result.Should().NotBeNull();
            result!.Activated.Should().BeFalse();
            result.Readiness.Issues.Should().Contain(issue =>
                issue.Code == "provider-manifest.required-mapping-missing" &&
                issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
            (await tenantStore.GetManifestAsync(manifest.ManifestId))!.State.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
            (await tenantStore.GetConnectionAsync(connection.ConnectionId))!.State.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationActivate_RequiresConfigurePermission()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            var tenantStore = DefaultProviderIntegrationTenantStore(store);
            var manifest = CreateProviderIntegrationActivationManifest();
            var connection = CreateProviderIntegrationActivationConnection(manifest);
            await tenantStore.SaveManifestAsync(manifest);
            await tenantStore.SaveConnectionAsync(connection);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewConfig);
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationActivate,
                CreateProviderIntegrationActivationRequest(manifest, connection),
                ServerJsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await tenantStore.GetManifestAsync(manifest.ManifestId))!.State.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
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
        IProviderIntegrationManifestStore store,
        IProviderIntegrationHttpTransport? transport = null)
    {
        services.AddSingleton<IProviderIntegrationManifestStore>(store);
        services.AddSingleton<ProviderIntegrationTemplateCatalog>();
        services.AddSingleton<ProviderIntegrationDryRunService>();
        services.AddSingleton<IProviderIntegrationHttpTransport>(transport ?? new ProviderIntegrationEndpointTransport());
        services.AddSingleton<ProviderIntegrationRestDryRunService>();
        services.AddSingleton<ProviderIntegrationSetupService>();
        services.AddSingleton<ProviderIntegrationActivationReadinessService>();
        services.AddSingleton<ProviderIntegrationActivationService>();
        services.AddSingleton<ProviderIntegrationMonitoringService>();
        services.AddSingleton<ProviderIntegrationQuarantineReviewService>();
    }

    private static async Task<FileProviderIntegrationManifestStore> CreateSeededProviderIntegrationStoreAsync(string testRoot)
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var tenantStore = DefaultProviderIntegrationTenantStore(store);
        var manifest = CreateProviderIntegrationEndpointManifest();
        var connection = CreateProviderIntegrationEndpointConnection(manifest);
        await tenantStore.SaveManifestAsync(manifest).ConfigureAwait(false);
        await tenantStore.SaveConnectionAsync(connection).ConfigureAwait(false);
        await SaveProviderIntegrationEndpointRunAsync(
            tenantStore,
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
            tenantStore,
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
        IProviderIntegrationManifestStore store,
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

    private static string ProviderIntegrationQuarantineReviewRoute(string connectionId)
        => UiApiRoutes.WorkstationProviderIntegrationQuarantineReview.Replace(
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

    private static ManualCsvProviderIntegrationDryRunRequestDto CreateManualCsvDryRunRequest(
        ProviderIntegrationManifestDto manifest,
        IntegrationProviderConnectionDto connection)
        => new(
            "sync-run-manual-endpoint-1",
            manifest.ManifestId,
            connection.ConnectionId,
            ProviderCapabilityKindDto.Positions,
            "positions.csv",
            """
            account_id,cusip,quantity,as_of,source_id
            A-100,9128285M8,100,2026-06-16,POS-1
            """,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"));

    private static ProviderIntegrationRestDryRunRequestDto CreateRestDryRunRequest(
        ProviderIntegrationManifestDto manifest,
        IntegrationProviderConnectionDto connection)
        => new(
            "sync-run-rest-endpoint-1",
            manifest.ManifestId,
            connection.ConnectionId,
            ProviderCapabilityKindDto.Positions,
            "positions",
            new Dictionary<string, string> { ["accountId"] = "A-100" },
            new Dictionary<string, string>(),
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            MaxPages: 1);

    private static IntegrationProviderConnectionDto CreateProviderIntegrationManualCsvConnection(
        ProviderIntegrationManifestDto manifest)
        => new(
            "connection-manual-csv",
            manifest.ProviderId,
            manifest.ManifestId,
            "Manual CSV Test",
            "test",
            ProviderIntegrationActivationStateDto.Draft,
            "vault://provider-credentials/manual-csv/test",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovalEvidenceId: null);

    private static IntegrationProviderConnectionDto CreateProviderIntegrationRestConnection(
        ProviderIntegrationManifestDto manifest)
        => new(
            "connection-rest-custodian",
            manifest.ProviderId,
            manifest.ManifestId,
            "Custodian REST Test",
            "test",
            ProviderIntegrationActivationStateDto.Draft,
            "vault://provider-credentials/custodian-rest/test",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovalEvidenceId: null);

    private static ProviderIntegrationSetupSaveRequestDto CreateProviderIntegrationSetupSaveRequest(
        string connectionManifestId = "manifest-setup-endpoint-v1")
    {
        var manifest = new ProviderIntegrationManifestDto(
            "manifest-setup-endpoint-v1",
            1,
            "provider-setup",
            "Provider Setup",
            IntegrationTypeDto.Rest,
            "test",
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
            [
                ProviderIntegrationActivationMapping("providerAccountId"),
                ProviderIntegrationActivationMapping("quantity")
            ],
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
            ProviderIntegrationActivationStateDto.Draft,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T14:30:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Endpoint setup draft");
        var connection = new IntegrationProviderConnectionDto(
            "connection-setup-endpoint",
            manifest.ProviderId,
            connectionManifestId,
            "Provider Setup Test",
            manifest.Environment,
            ProviderIntegrationActivationStateDto.Draft,
            "vault://provider-credentials/provider-setup/test",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T14:30:00Z"),
            DateTimeOffset.Parse("2026-06-16T14:30:00Z"),
            ApprovalEvidenceId: null);
        return new ProviderIntegrationSetupSaveRequestDto(
            manifest,
            connection,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T15:00:00Z"),
            "Saved from workstation endpoint.");
    }

    private static ProviderIntegrationActivationRequestDto CreateProviderIntegrationActivationRequest(
        ProviderIntegrationManifestDto manifest,
        Meridian.Contracts.Integrations.ProviderConnectionDto connection)
        => new(
            manifest.ManifestId,
            connection.ConnectionId,
            "approver@example.com",
            DateTimeOffset.Parse("2026-06-16T14:00:00Z"),
            "approval-evidence-endpoint-1",
            "Approved from workstation endpoint test.");

    private static ProviderIntegrationManifestDto CreateProviderIntegrationActivationManifest(
        bool missingQuantityMapping = false)
        => new(
            "manifest-activation-endpoint-v1",
            1,
            "provider-alpha",
            "Provider Alpha",
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
            missingQuantityMapping
                ?
                [
                    ProviderIntegrationActivationMapping("providerAccountId"),
                    ProviderIntegrationActivationMapping("asOf")
                ]
                :
                [
                    ProviderIntegrationActivationMapping("providerAccountId"),
                    ProviderIntegrationActivationMapping("quantity"),
                    ProviderIntegrationActivationMapping("asOf")
                ],
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
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Endpoint activation test manifest");

    private static FieldMappingDto ProviderIntegrationActivationMapping(string targetField)
        => new(
            ProviderCapabilityKindDto.Positions,
            $"$.{targetField.Replace('.', '_')}",
            targetField,
            null,
            Required: true,
            ProviderMappingConfidenceDto.Approved,
            DefaultValue: null,
            ConstantValue: null);

    private static Meridian.Contracts.Integrations.ProviderConnectionDto CreateProviderIntegrationActivationConnection(
        ProviderIntegrationManifestDto manifest)
        => new(
            "connection-activation-endpoint",
            manifest.ProviderId,
            manifest.ManifestId,
            "Provider Alpha Production",
            manifest.Environment,
            ProviderIntegrationActivationStateDto.DryRunPassed,
            "vault://provider-credentials/provider-alpha/production",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:05:00Z"),
            ApprovalEvidenceId: null);

    private sealed class ProviderIntegrationEndpointTransport : IProviderIntegrationHttpTransport
    {
        private readonly Queue<ProviderIntegrationHttpResponse> responses;

        public ProviderIntegrationEndpointTransport(params ProviderIntegrationHttpResponse[] responses)
        {
            this.responses = new Queue<ProviderIntegrationHttpResponse>(responses);
        }

        public List<ProviderIntegrationHttpRequest> Requests { get; } = [];

        public Task<ProviderIntegrationHttpResponse> SendAsync(
            ProviderIntegrationHttpRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(responses.Count == 0
                ? new ProviderIntegrationHttpResponse(500, new Dictionary<string, string>(), "{}")
                : responses.Dequeue());
        }
    }

    private static IProviderIntegrationManifestStore DefaultProviderIntegrationTenantStore(
        FileProviderIntegrationManifestStore store)
        => ((IProviderIntegrationTenantManifestStoreFactory)store).ForTenant(DefaultProviderIntegrationTenantId);

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
