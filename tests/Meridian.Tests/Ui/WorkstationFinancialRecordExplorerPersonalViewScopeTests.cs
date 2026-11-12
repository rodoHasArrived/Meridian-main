using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerPersonalView_RejectsOversizedPresentationBeforeNormalization(
        bool oversizedFilters)
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);
        IReadOnlyList<FinancialRecordExplorerFilterDto> filters = oversizedFilters
            ? Enumerable.Repeat(new FinancialRecordExplorerFilterDto("account-type", "Account Type", "Asset"), 51).ToArray()
            : [];
        IReadOnlyList<string> columns = oversizedFilters
            ? ["balance"]
            : Enumerable.Repeat("balance", 101).ToArray();

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/workstation/financial-record-explorers/ledger/saved-views",
            new FinancialRecordExplorerSavedViewSaveRequestDto("Oversized view", "Must remain invalid.", "Cash", filters, columns),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("tenant-test", "other-operator", false, false)]
    [InlineData("tenant-test", "other-operator", false, true)]
    [InlineData("tenant-other", "view-owner", false, false)]
    [InlineData("tenant-other", "view-owner", false, true)]
    [InlineData("tenant-test", "view-owner", true, false)]
    [InlineData("tenant-test", "view-owner", true, true)]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerPersonalView_RefusesForeignOrUnknownViewIds(
        string readerTenant, string readerActor, bool unknownViewId, bool explicitQueryOperands)
    {
        var savedViewRoot = Path.Combine(Path.GetTempPath(), $"meridian-explorer-view-scope-{Guid.NewGuid():N}");
        try
        {
            await using var ownerApp = await CreateAppAsync(
                services => RegisterFinancialRecordExplorerTestServices(services, savedViewRoot),
                currentUserPermissions: ExplorerOperatorPermissions,
                currentUserName: "view-owner");
            var saveResponse = await ownerApp.GetTestClient().PostAsJsonAsync(
                "/api/workstation/financial-record-explorers/ledger/saved-views",
                new FinancialRecordExplorerSavedViewSaveRequestDto(
                    "Owner cash view", "Scoped personal view.", "Cash",
                    [new("account-type", "Account Type", "Asset")], ["accountName", "balance"]),
                ServerJsonOptions);
            saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var saved = await saveResponse.Content.ReadFromJsonAsync<FinancialRecordExplorerSavedViewDto>(ServerJsonOptions);
            saved.Should().NotBeNull();

            await using var readerApp = await CreateAppAsync(
                services => RegisterFinancialRecordExplorerTestServices(services, savedViewRoot),
                currentUserPermissions: ExplorerOperatorPermissions,
                currentUserCompanyId: readerTenant,
                currentUserName: readerActor);
            await readerApp.Services.GetRequiredService<IStrategyRepository>()
                .RecordRunAsync(BuildActivePaperRun("personal-view-reader-run", withBreaks: false));
            var client = readerApp.GetTestClient();
            var full = await client.GetFromJsonAsync<FinancialRecordExplorerDto>(
                "/api/workstation/financial-record-explorers/ledger", ServerJsonOptions);
            full!.Rows.Should().HaveCountGreaterThan(1, "the reader has an accessible ledger independent of view authorization");
            var requestedViewId = unknownViewId ? "unknown-personal-view" : saved!.ViewId;
            var queryOperands = explicitQueryOperands ? "&searchText=Cash&filter=account-type%3AAsset" : string.Empty;

            var scoped = await client.GetFromJsonAsync<FinancialRecordExplorerDto>(
                $"/api/workstation/financial-record-explorers/ledger?viewId={Uri.EscapeDataString(requestedViewId)}{queryOperands}",
                ServerJsonOptions);

            scoped!.Rows.Should().BeEmpty("unknown or foreign view identities must not fall back to the accessible base rows");
            scoped.SelectedRecord.Should().BeNull();
            scoped.SavedViews.Should().NotContain(view => view.IsActive && view.ViewId == requestedViewId);
        }
        finally
        {
            if (Directory.Exists(savedViewRoot))
                Directory.Delete(savedViewRoot, recursive: true);
        }
    }
}
