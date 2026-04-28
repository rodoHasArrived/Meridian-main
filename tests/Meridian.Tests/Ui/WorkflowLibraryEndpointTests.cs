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
}
