using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData("ledger")]
    [InlineData("portfolio")]
    [InlineData("security-instrument")]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorers_ShouldReturnStableSharedShape(string explorerId)
    {
        await using var app = await CreateAppAsync(RegisterFinancialRecordExplorerTestServices);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("financial-record-explorer-run", withBreaks: false));

        using var payload = await ReadJsonAsync(app.GetTestClient(), $"/api/workstation/financial-record-explorers/{explorerId}");
        var root = payload.RootElement;

        root.GetProperty("explorerId").GetString().Should().Be(explorerId);
        root.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("sourceState").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("isBlocked").GetBoolean().Should().BeFalse();
        root.GetProperty("savedViews").EnumerateArray().Should().Contain(view =>
            view.GetProperty("isSystem").GetBoolean() &&
            view.GetProperty("isActive").GetBoolean());
        root.GetProperty("summaryItems").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("columns").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("rows").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("recordGraph").GetProperty("nodes").ValueKind.Should().Be(JsonValueKind.Array);

        if (explorerId is "ledger" or "portfolio")
        {
            root.GetProperty("rows").GetArrayLength().Should().BeGreaterThan(0);
            var selected = root.GetProperty("selectedRecord");
            selected.ValueKind.Should().Be(JsonValueKind.Object);
            selected.GetProperty("usedIn").ValueKind.Should().Be(JsonValueKind.Array);
            selected.GetProperty("impacts").ValueKind.Should().Be(JsonValueKind.Array);
            selected.GetProperty("proofActions").ValueKind.Should().Be(JsonValueKind.Array);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerUnknownId_ShouldReturnNotFound()
    {
        await using var app = await CreateAppAsync(RegisterFinancialRecordExplorerTestServices);
        var response = await app.GetTestClient().GetAsync("/api/workstation/financial-record-explorers/not-real");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerSavedViews_ShouldPersistAndReloadForExplorer()
    {
        await using var app = await CreateAppAsync(RegisterFinancialRecordExplorerTestServices);
        var client = app.GetTestClient();

        var saveResponse = await client.PostAsJsonAsync(
            "/api/workstation/financial-record-explorers/ledger/saved-views",
            new FinancialRecordExplorerSavedViewSaveRequestDto(
                "Material trial-balance view",
                "Operator-created saved view for ledger review.",
                "Cash",
                [new("account-type", "Account Type", "Asset")]),
            ServerJsonOptions);

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<FinancialRecordExplorerSavedViewDto>(ServerJsonOptions);
        saved.Should().NotBeNull();
        saved!.IsSystem.Should().BeFalse();

        using var payload = await ReadJsonAsync(client, "/api/workstation/financial-record-explorers/ledger");
        payload.RootElement.GetProperty("savedViews").EnumerateArray().Should().Contain(view =>
            view.GetProperty("viewId").GetString() == saved.ViewId &&
            view.GetProperty("label").GetString() == "Material trial-balance view" &&
            !view.GetProperty("isSystem").GetBoolean());
    }

    private static void RegisterFinancialRecordExplorerTestServices(IServiceCollection services)
    {
        RegisterRunReadServices(services);
        services.AddSingleton<IFinancialRecordExplorerSavedViewStore>(_ =>
            new FileFinancialRecordExplorerSavedViewStore(
                Path.Combine(Path.GetTempPath(), "meridian-tests", "financial-record-explorers", Guid.NewGuid().ToString("N")),
                NullLogger<FileFinancialRecordExplorerSavedViewStore>.Instance));
        services.AddSingleton<FinancialRecordExplorerReadService>();
    }
}
