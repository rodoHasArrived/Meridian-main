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
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorer_ShouldNotApplyAnUnknownOrOtherOperatorsView(bool anotherOperator)
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions,
            currentUserName: "alice");
        await app.Services.GetRequiredService<IStrategyRepository>().RecordRunAsync(
            BuildActivePaperRun("financial-record-explorer-personal-view-boundary", withBreaks: false));
        var client = app.GetTestClient();
        var baseline = await client.GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/ledger", ServerJsonOptions);
        baseline!.Rows.Should().NotBeEmpty();

        var viewId = "unknown-personal-view";
        if (anotherOperator)
        {
            var response = await client.PostAsJsonAsync(
                "/api/workstation/financial-record-explorers/ledger/saved-views",
                new FinancialRecordExplorerSavedViewSaveRequestDto(
                    "Alice's cash view", "Only Alice may apply this view.", "Cash", [], ["accountName"]),
                ServerJsonOptions);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            viewId = (await response.Content.ReadFromJsonAsync<FinancialRecordExplorerSavedViewDto>(ServerJsonOptions))!.ViewId;
            client.DefaultRequestHeaders.Add("X-Meridian-Test-User", "bob");
        }

        var explorer = await client.GetFromJsonAsync<FinancialRecordExplorerDto>(
            $"/api/workstation/financial-record-explorers/ledger?viewId={Uri.EscapeDataString(viewId)}", ServerJsonOptions);

        explorer!.Rows.Should().BeEmpty("an unresolved personal view must not fall back to unfiltered records");
        explorer.SelectedRecord.Should().BeNull();
        explorer.SavedViews.Should().NotContain(view => view.IsActive || view.ViewId == viewId);
    }
}
