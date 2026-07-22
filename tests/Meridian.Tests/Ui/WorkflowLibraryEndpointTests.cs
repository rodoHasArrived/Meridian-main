using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class WorkflowLibraryEndpointTests
{
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void WorkflowRegistry_DefaultCatalog_ResolvesInboxKindsAndRoutes()
    {
        var registry = WorkflowRegistry.CreateDefault();

        registry.ResolveTargetPageTag(WorkflowActionIds.DataOpenProviderHealth, "Fallback")
            .Should()
            .Be("ProviderHealth");
        registry.ResolveRoute($"{UiApiRoutes.ReconciliationBreakQueue}/break-123/review")!
            .TargetPageTag
            .Should()
            .Be("FundReconciliation");
        registry.ResolveTargetPageTag(WorkflowActionIds.AccountingReviewLedgerContinuity, "Fallback")
            .Should()
            .Be("FundTrialBalance");
        registry.ResolveTargetPageTag(WorkflowActionIds.AccountingReviewOperationsContinuity, "Fallback")
            .Should()
            .Be("OperationsContinuity");
        registry.ResolveRoute($"{UiApiRoutes.OperationsContinuity}/00000000-0000-0000-0000-000000000001/close-readiness")!
            .TargetPageTag
            .Should()
            .Be("OperationsClose");
        registry.GetWorkflowDefinitions()
            .Select(static workflow => workflow.WorkspaceId)
            .Should()
            .Contain(["trading", "portfolio", "accounting", "reporting", "strategy", "data", "settings"]);
        registry.GetWorkflowDefinitions()
            .Should()
            .Contain(workflow =>
                workflow.WorkflowId == "primary-operator-workflow" &&
                workflow.Title == "Primary Operator Workflow");
        registry.GetWorkflowDefinitions()
            .Should()
            .Contain(workflow =>
                workflow.WorkflowId == "accounting-records-evidence-review" &&
                workflow.Title == "Accounting Records Evidence Review");
        registry.ResolveTargetPageTag(WorkflowActionIds.PrimaryOperatorReconcile, "Fallback")
            .Should()
            .Be("FundReconciliation");
        registry.ResolveTargetPageTag(WorkflowActionIds.PrimaryOperatorReport, "Fallback")
            .Should()
            .Be("FundReportPack");
        registry.ResolveTargetPageTag(WorkflowActionIds.AccountingRecordsReviewApprovals, "Fallback")
            .Should()
            .Be("OperationsContinuity");
        registry.ResolveTargetPageTag(WorkflowActionIds.AccountingRecordsReviewReportLineage, "Fallback")
            .Should()
            .Be("FundReportPack");
        registry.ResolveTargetPageTag(WorkflowActionIds.PortfolioReviewAggregate, "Fallback")
            .Should()
            .Be("AggregatePortfolio");
        registry.ResolveRoute(UiApiRoutes.PortfolioExposure)!
            .TargetPageTag
            .Should()
            .Be("AggregatePortfolio");
        registry.ResolveOperatorWorkItem(new OperatorWorkItemDto(
                WorkItemId: "sync-1",
                Kind: OperatorWorkItemKindDto.BrokerageSync,
                Label: "Brokerage sync failed",
                Detail: "Account sync needs review.",
                Tone: OperatorWorkItemToneDto.Warning,
                CreatedAt: DateTimeOffset.UtcNow))!
            .TargetPageTag
            .Should()
            .Be("AccountPortfolio");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_Workflows_ShouldReturnBuiltInWorkflowLibrary()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddWorkflowLibrary();

        await using var app = builder.Build();
        app.Use(AddTestTenantContext);
        app.MapWorkstationEndpoints(ServerJsonOptions);
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/workflows");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var library = await response.Content.ReadFromJsonAsync<WorkflowLibraryDto>(ServerJsonOptions);
        library.Should().NotBeNull();
        library!.Workflows.Should().Contain(workflow => workflow.WorkflowId == "primary-operator-workflow");
        var primaryOperatorWorkflow = library.Workflows.Single(workflow => workflow.WorkflowId == "primary-operator-workflow");
        primaryOperatorWorkflow.Title.Should().Be("Primary Operator Workflow");
        primaryOperatorWorkflow.Summary.Should().Contain("import through certified reporting");
        primaryOperatorWorkflow.Actions.Select(static action => action.Label)
            .Should()
            .Equal("Import", "Validate", "Reconcile", "Investigate", "Approve", "Report");
        primaryOperatorWorkflow.Actions.Select(static action => action.TargetPageTag)
            .Should()
            .Equal("DataShell", "Backfill", "FundReconciliation", "PortfolioShell", "OperationsContinuity", "FundReportPack");
        primaryOperatorWorkflow.MarketPatternTags.Should().Contain("import validate reconcile");
        library.Workflows.Should().Contain(workflow => workflow.WorkflowId == "accounting-records-evidence-review");
        var accountingRecordsWorkflow = library.Workflows.Single(workflow => workflow.WorkflowId == "accounting-records-evidence-review");
        accountingRecordsWorkflow.Summary.Should().Contain("v0.15 operational record");
        accountingRecordsWorkflow.Actions.Select(static action => action.Label)
            .Should()
            .Equal(
                "Review Source Records",
                "Review Normalized Activity",
                "Review Reconciliation Cases",
                "Review Ledger Evidence",
                "Review Approval History",
                "Review Report Lineage");
        accountingRecordsWorkflow.Actions.Select(static action => action.TargetPageTag)
            .Should()
            .Equal("DataShell", "PortfolioShell", "FundReconciliation", "FundTrialBalance", "OperationsContinuity", "FundReportPack");
        accountingRecordsWorkflow.EvidenceTags.Should().Contain(["source records", "normalized activity", "reconciliation cases", "ledger evidence", "approvals", "document attachments", "export manifests", "report lineage"]);
        accountingRecordsWorkflow.MarketPatternTags.Should().Contain(["export manifests", "restatement lineage"]);
        accountingRecordsWorkflow.Actions.Should().Contain(action =>
            action.ActionId == WorkflowActionIds.AccountingRecordsReviewReportLineage &&
            action.Detail.Contains("document attachments", StringComparison.OrdinalIgnoreCase) &&
            action.Detail.Contains("export manifests", StringComparison.OrdinalIgnoreCase));
        library!.Workflows.Should().Contain(workflow => workflow.WorkflowId == "strategy-to-paper-review");
        library.Workflows.Should().Contain(workflow =>
            workflow.WorkflowId == "strategy-to-paper-review" &&
            workflow.Title == "Research to Paper Review" &&
            workflow.Summary.Contains("strategy research evidence", StringComparison.OrdinalIgnoreCase) &&
            workflow.MarketPatternTags.Contains("research to backtest") &&
            !workflow.MarketPatternTags.Contains("strategy to backtest"));
        library.Workflows.Should().Contain(workflow =>
            workflow.WorkflowId == "portfolio-position-review" &&
            workflow.WorkspaceId == "portfolio" &&
            workflow.EntryPageTag == "PortfolioShell");
        library.Actions.Should().Contain(action =>
            action.ActionId == WorkflowActionIds.DataOpenProviderHealth &&
            action.TargetPageTag == "ProviderHealth");
        library.Actions.Should().Contain(action =>
            action.ActionId == WorkflowActionIds.PortfolioReviewAggregate &&
            action.TargetPageTag == "AggregatePortfolio");
        library.Actions.Should().Contain(action =>
            action.ActionId == WorkflowActionIds.PortfolioImportSnapshots &&
            action.TargetPageTag == "PortfolioImport");
        library.Actions.Should().Contain(action =>
            action.ActionId == WorkflowActionIds.AccountingReviewLedgerContinuity &&
            action.TargetPageTag == "FundTrialBalance");
        library.Actions.Should().Contain(action =>
            action.ActionId == WorkflowActionIds.AccountingReviewOperationsContinuity &&
            action.TargetPageTag == "OperationsContinuity");
        library.Actions.Should().Contain(action =>
            action.ActionId == WorkflowActionIds.AccountingReviewCloseReadiness &&
            action.TargetPageTag == "OperationsClose");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowPresets_ShouldPersistPinUseAndDeletePreset()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workflow-presets", Guid.NewGuid().ToString("N"));
        await using var app = await CreateWorkflowPresetAppAsync(root);
        var client = app.GetTestClient();

        var emptyResponse = await client.GetAsync("/api/workstation/workflows/presets");
        emptyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var emptyLibrary = await emptyResponse.Content.ReadFromJsonAsync<WorkflowPresetLibraryDto>(ServerJsonOptions);
        emptyLibrary!.Presets.Should().BeEmpty();

        var request = new WorkflowPresetSaveRequest(
            PresetId: "morning-provider-check",
            Name: "Morning provider check",
            Description: "Provider recovery workflow for the opening review.",
            WorkflowId: "data-provider-recovery",
            ActionId: WorkflowActionIds.DataOpenProviderHealth,
            Tags: ["data", "desk", "data"],
            IsPinned: false);

        var saveResponse = await client.PostAsJsonAsync("/api/workstation/workflows/presets", request, ServerJsonOptions);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<WorkflowPresetDto>(ServerJsonOptions);
        saved.Should().NotBeNull();
        saved!.PresetId.Should().Be("morning-provider-check");
        saved.WorkflowTitle.Should().Be("Data Provider Recovery");
        saved.TargetPageTag.Should().Be("ProviderHealth");
        saved.Tags.Should().Equal("data", "desk");

        var snapshotPath = Path.Combine(root, "workstation", "workflows", "workflow-presets.json");
        File.Exists(snapshotPath).Should().BeTrue();

        var pinResponse = await client.PostAsJsonAsync(
            "/api/workstation/workflows/presets/morning-provider-check/pin",
            new WorkflowPresetPinRequest(true),
            ServerJsonOptions);
        pinResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pinned = await pinResponse.Content.ReadFromJsonAsync<WorkflowPresetDto>(ServerJsonOptions);
        pinned!.IsPinned.Should().BeTrue();

        var usedResponse = await client.PostAsync("/api/workstation/workflows/presets/morning-provider-check/used", content: null);
        usedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var used = await usedResponse.Content.ReadFromJsonAsync<WorkflowPresetDto>(ServerJsonOptions);
        used!.LastUsedAt.Should().NotBeNull();

        var libraryResponse = await client.GetAsync("/api/workstation/workflows/presets");
        var library = await libraryResponse.Content.ReadFromJsonAsync<WorkflowPresetLibraryDto>(ServerJsonOptions);
        library!.Presets.Should().ContainSingle(preset =>
            preset.PresetId == "morning-provider-check" &&
            preset.IsPinned &&
            preset.LastUsedAt.HasValue);

        var deleteResponse = await client.DeleteAsync("/api/workstation/workflows/presets/morning-provider-check");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var finalResponse = await client.GetAsync("/api/workstation/workflows/presets");
        var finalLibrary = await finalResponse.Content.ReadFromJsonAsync<WorkflowPresetLibraryDto>(ServerJsonOptions);
        finalLibrary!.Presets.Should().BeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowPresets_ShouldRoundTripViewStateEnvelope()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workflow-presets", Guid.NewGuid().ToString("N"));
        await using var app = await CreateWorkflowPresetAppAsync(root);
        var client = app.GetTestClient();

        const string envelope = "eyJ2IjoxLCJzY3JlZW4iOiJyZXBvcnRpbmctZXhwb3J0cyIsInN0YXRlIjp7fX0";
        var saveResponse = await client.PostAsJsonAsync(
            "/api/workstation/workflows/presets",
            new WorkflowPresetSaveRequest(
                PresetId: "saved-view",
                Name: "Saved view",
                Description: null,
                WorkflowId: "data-provider-recovery",
                ActionId: null,
                Tags: [],
                IsPinned: false,
                ViewStateEnvelope: envelope),
            ServerJsonOptions);

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<WorkflowPresetDto>(ServerJsonOptions);
        saved!.ViewStateEnvelope.Should().Be(envelope);

        var pinResponse = await client.PostAsJsonAsync(
            "/api/workstation/workflows/presets/saved-view/pin",
            new WorkflowPresetPinRequest(true),
            ServerJsonOptions);
        var pinned = await pinResponse.Content.ReadFromJsonAsync<WorkflowPresetDto>(ServerJsonOptions);
        pinned!.ViewStateEnvelope.Should().Be(envelope);

        var usedResponse = await client.PostAsync("/api/workstation/workflows/presets/saved-view/used", content: null);
        var used = await usedResponse.Content.ReadFromJsonAsync<WorkflowPresetDto>(ServerJsonOptions);
        used!.ViewStateEnvelope.Should().Be(envelope);

        var libraryResponse = await client.GetAsync("/api/workstation/workflows/presets");
        var library = await libraryResponse.Content.ReadFromJsonAsync<WorkflowPresetLibraryDto>(ServerJsonOptions);
        library!.Presets.Should().ContainSingle(preset =>
            preset.PresetId == "saved-view" && preset.ViewStateEnvelope == envelope);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowPresets_ShouldDefaultViewStateEnvelopeToNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workflow-presets", Guid.NewGuid().ToString("N"));
        await using var app = await CreateWorkflowPresetAppAsync(root);
        var client = app.GetTestClient();

        var saveResponse = await client.PostAsJsonAsync(
            "/api/workstation/workflows/presets",
            new WorkflowPresetSaveRequest(
                PresetId: "plain-preset",
                Name: "Plain preset",
                Description: null,
                WorkflowId: "data-provider-recovery",
                ActionId: null,
                Tags: [],
                IsPinned: false),
            ServerJsonOptions);

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<WorkflowPresetDto>(ServerJsonOptions);
        saved!.ViewStateEnvelope.Should().BeNull();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowPresets_ShouldRejectOversizedViewStateEnvelope()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workflow-presets", Guid.NewGuid().ToString("N"));
        await using var app = await CreateWorkflowPresetAppAsync(root);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/workstation/workflows/presets",
            new WorkflowPresetSaveRequest(
                PresetId: "oversized-view",
                Name: "Oversized view",
                Description: null,
                WorkflowId: "data-provider-recovery",
                ActionId: null,
                Tags: [],
                IsPinned: false,
                ViewStateEnvelope: new string('v', 4097)),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("cannot exceed 4096");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowPresets_ShouldLoadLegacySnapshotsWithoutViewState()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workflow-presets", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "workstation", "workflows", "workflow-presets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        await File.WriteAllTextAsync(snapshotPath, """
        {
          "version": 1,
          "presets": [
            {
              "presetId": "legacy-preset",
              "name": "Legacy preset",
              "description": null,
              "workflowId": "data-provider-recovery",
              "workflowTitle": "Data Provider Recovery",
              "actionId": null,
              "actionLabel": "Open workflow",
              "workspaceId": "data",
              "workspaceTitle": "Data",
              "targetPageTag": "ProviderHealth",
              "tags": [],
              "isPinned": false,
              "createdAt": "2026-06-01T00:00:00+00:00",
              "updatedAt": "2026-06-01T00:00:00+00:00",
              "lastUsedAt": null
            }
          ]
        }
        """);

        await using var app = await CreateWorkflowPresetAppAsync(root);
        var client = app.GetTestClient();

        var libraryResponse = await client.GetAsync("/api/workstation/workflows/presets");
        libraryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var library = await libraryResponse.Content.ReadFromJsonAsync<WorkflowPresetLibraryDto>(ServerJsonOptions);
        library!.Presets.Should().ContainSingle(preset =>
            preset.PresetId == "legacy-preset" && preset.ViewStateEnvelope == null);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowPresets_ShouldRejectUnknownWorkflow()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workflow-presets", Guid.NewGuid().ToString("N"));
        await using var app = await CreateWorkflowPresetAppAsync(root);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/workstation/workflows/presets",
            new WorkflowPresetSaveRequest(
                PresetId: null,
                Name: "Unknown workflow",
                Description: null,
                WorkflowId: "does-not-exist",
                ActionId: null,
                Tags: [],
                IsPinned: false),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("does-not-exist");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowPresets_ShouldRejectOversizedPayloadFields()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workflow-presets", Guid.NewGuid().ToString("N"));
        await using var app = await CreateWorkflowPresetAppAsync(root);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/workstation/workflows/presets",
            new WorkflowPresetSaveRequest(
                PresetId: new string('p', 129),
                Name: "Valid name",
                Description: null,
                WorkflowId: "data-provider-recovery",
                ActionId: null,
                Tags: [],
                IsPinned: false),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("cannot exceed 128");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowPresets_ShouldRejectNewPresetWhenLimitReached()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workflow-presets", Guid.NewGuid().ToString("N"));
        await using var app = await CreateWorkflowPresetAppAsync(root);
        var client = app.GetTestClient();

        for (var i = 0; i < 250; i++)
        {
            var saveResponse = await client.PostAsJsonAsync(
                "/api/workstation/workflows/presets",
                new WorkflowPresetSaveRequest(
                    PresetId: $"preset-{i}",
                    Name: $"Preset {i}",
                    Description: null,
                    WorkflowId: "data-provider-recovery",
                    ActionId: null,
                    Tags: [],
                    IsPinned: false),
                ServerJsonOptions);
            saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var overflowResponse = await client.PostAsJsonAsync(
            "/api/workstation/workflows/presets",
            new WorkflowPresetSaveRequest(
                PresetId: "preset-overflow",
                Name: "Preset overflow",
                Description: null,
                WorkflowId: "data-provider-recovery",
                ActionId: null,
                Tags: [],
                IsPinned: false),
            ServerJsonOptions);

        overflowResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await overflowResponse.Content.ReadAsStringAsync();
        body.Should().Contain("limit (250)");
    }

    [Fact]
    public async Task FileWorkflowPresetStore_LoadAsync_ShouldHonorCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workflow-presets", Guid.NewGuid().ToString("N"));
        var store = new FileWorkflowPresetStore(root, NullLogger<FileWorkflowPresetStore>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadAsync(cts.Token));
    }

    [Fact]
    public async Task FileWorkflowPresetStore_LoadAsync_ShouldRejectUnsupportedSnapshotVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workflow-presets", Guid.NewGuid().ToString("N"));
        var snapshotDirectory = Path.Combine(root, "workstation", "workflows");
        Directory.CreateDirectory(snapshotDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(snapshotDirectory, "workflow-presets.json"),
            """{"version":999,"presets":[]}""");

        var store = new FileWorkflowPresetStore(root, NullLogger<FileWorkflowPresetStore>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadAsync());
        ex.Message.Should().Contain("version 999");
    }

    private static async Task<WebApplication> CreateWorkflowPresetAppAsync(string root)
    {
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, """{"DataRoot":"."}""");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new Meridian.Application.UI.ConfigStore(configPath));
        builder.Services.AddWorkflowLibrary();

        var app = builder.Build();
        app.Use(AddTestTenantContext);
        app.MapWorkstationEndpoints(ServerJsonOptions);
        await app.StartAsync();
        return app;
    }

    private static async Task AddTestTenantContext(HttpContext context, Func<Task> next)
    {
        context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-test";
        context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-test";
        context.Items[LoginSessionMiddleware.CurrentUserKey] = "workflow-test-operator";
        await next();
    }
}
