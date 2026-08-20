using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Strategies.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData(UiApiRoutes.WorkstationData)]
    [InlineData(UiApiRoutes.WorkstationDataOperations)]
    [InlineData(UiApiRoutes.WorkstationDataReplacementCost)]
    public async Task MapWorkstationEndpoints_DataWorkspaceReads_WithoutDataReadPermission_ShouldReturnForbidden(string route)
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ModifySecurityMaster);
        var response = await app.GetTestClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(UiApiRoutes.WorkstationData)]
    [InlineData(UiApiRoutes.WorkstationDataOperations)]
    [InlineData(UiApiRoutes.WorkstationDataReplacementCost)]
    public async Task MapWorkstationEndpoints_DataWorkspaceReads_WithHistoricalDataReadPermission_ShouldReachHandler(string route)
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ViewHistoricalData);
        var response = await app.GetTestClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData(UserPermission.ViewHistoricalData)]
    [InlineData(UserPermission.ViewDiagnostics)]
    [InlineData(UserPermission.ManageStorage)]
    public async Task MapWorkstationEndpoints_DataWorkspaceRead_WithAnyDataReadPermission_ShouldReachHandler(UserPermission permission)
    {
        await using var app = await CreateAppAsync(currentUserPermissions: permission);
        var response = await app.GetTestClient().GetAsync(UiApiRoutes.WorkstationData);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData(UserPermission.AdminMaintenance)]
    [InlineData(UserPermission.ManageDirectLending)]
    [InlineData(UserPermission.ModifySecurityMaster)]
    public async Task MapWorkstationEndpoints_BreakQueueReads_ShouldAdmitEveryMutationProfile(
        UserPermission mutationPermission)
    {
        await using var app = await CreateAppAsync(currentUserPermissions: mutationPermission);
        var client = app.GetTestClient();

        using var list = await client.GetAsync(UiApiRoutes.ReconciliationBreakQueue);
        using var detail = await client.GetAsync(
            UiApiRoutes.ReconciliationBreakQueueById.Replace("{breakId}", "missing-break", StringComparison.Ordinal));

        list.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "a profile that can act on reconciliation casework must be able to load its queue");
        detail.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "the detail declaration must admit the same mutation profile before repository lookup");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BreakQueueReads_WithTradingReadOnly_ShouldReturnForbidden()
    {
        // Break records carry strategy and run identifiers, variances, reasons, assignees, sign-off
        // history, counterparties and resolution notes -- reconciliation casework, which ViewTrades
        // cannot act on: every break-queue mutation requires AdminMaintenance, ManageDirectLending or
        // ModifySecurityMaster.
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ViewTrades);
        var client = app.GetTestClient();

        using var list = await client.GetAsync(UiApiRoutes.ReconciliationBreakQueue);
        using var detail = await client.GetAsync(
            UiApiRoutes.ReconciliationBreakQueueById.Replace("{breakId}", "missing-break", StringComparison.Ordinal));

        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        detail.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "the detail route must refuse whoever the list route refuses, or it becomes the way around it");
    }

    [Theory]
    [InlineData(UiApiRoutes.WorkstationAccounting)]
    [InlineData(UiApiRoutes.WorkstationGovernance)]
    public async Task MapWorkstationEndpoints_AccountingWorkspace_ProjectsEachFamilyByTheCallersOwnAuthority(string route)
    {
        // The workspace deliberately admits every desk that works the period, but its payload
        // aggregates families a narrower route owns: the break queue, the manual-journal workbench,
        // and the authoritative reporting projection. ViewTrades holds none of those three.
        await using var app = await CreateAppAsync(
            services => RegisterRunReadServices(services),
            currentUserPermissions: UserPermission.ViewTrades);

        var repository = app.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
        await repository.CreateIfMissingAsync(
            TestReconciliationQueueScope,
            BuildBreakQueueItem("trading-visible-case", Guid.NewGuid()));

        using var response = await app.GetTestClient().GetAsync(route);
        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "the workspace shell stays reachable -- the fix is what it serves, not who it admits");

        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = payload.RootElement;

        root.GetProperty("breakQueue").GetArrayLength().Should().Be(
            0,
            "break records carry the casework detail the break-queue routes refuse this caller");
        root.TryGetProperty("manualJournalWorkbench", out var workbench).Should().BeTrue();
        workbench.ValueKind.Should().Be(
            JsonValueKind.Null,
            "the workbench route admits only AdminMaintenance and ManageDirectLending");
        root.GetProperty("reporting").GetProperty("profiles").GetArrayLength().Should().Be(
            0,
            "the reporting reads admit only ViewReporting and AdminMaintenance");
        root.GetProperty("controlCenter").GetProperty("ownerWorkload").GetArrayLength().Should().Be(
            0,
            "assignees are break-queue casework, not a workspace headline");
        root.GetProperty("controlCenter").GetProperty("accountFilterOptions").GetArrayLength().Should().Be(
            0,
            "the accounts under casework are break-queue casework too");

        // The counters stay truthful. Withholding them as well would blank the screen for the desks
        // the workspace was widened for, without withholding anything the records do not disclose in
        // far more detail.
        root.GetProperty("metrics").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "open-breaks")
            .GetProperty("value")
            .GetString()
            .Should()
            .Be("1");
        root.GetProperty("workspace").GetProperty("openBreaks").GetInt32().Should().Be(1);
    }
}
