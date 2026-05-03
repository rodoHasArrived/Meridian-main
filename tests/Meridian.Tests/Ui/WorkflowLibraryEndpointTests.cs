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
        app.MapWorkstationEndpoints(ServerJsonOptions);
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/workflows");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var library = await response.Content.ReadFromJsonAsync<WorkflowLibraryDto>(ServerJsonOptions);
        library.Should().NotBeNull();
        library!.Workflows.Should().Contain(workflow => workflow.WorkflowId == "strategy-to-paper-review");
        library.Actions.Should().Contain(action =>
            action.ActionId == WorkflowActionIds.DataOpenProviderHealth &&
            action.TargetPageTag == "ProviderHealth");
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
    public async Task FileWorkflowPresetStore_LoadAsync_ShouldHonorCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workflow-presets", Guid.NewGuid().ToString("N"));
        var store = new FileWorkflowPresetStore(root, NullLogger<FileWorkflowPresetStore>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadAsync(cts.Token));
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
        app.MapWorkstationEndpoints(ServerJsonOptions);
        await app.StartAsync();
        return app;
    }
}
