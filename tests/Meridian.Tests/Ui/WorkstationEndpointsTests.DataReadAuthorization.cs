using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
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

    [Theory]
    [InlineData(UiApiRoutes.WorkstationAccounting)]
    [InlineData(UiApiRoutes.WorkstationGovernance)]
    public async Task MapWorkstationEndpoints_AccountingWorkspace_WithoutRunAuthority_ShouldWithholdRunCardsAndBalances(string route)
    {
        // The workspace admits the Security Master desk on the strength of the period it works, and
        // the strategy runs behind that period are not part of that basis: the cards carry the run id
        // and strategy name, the audit, ledger and portfolio references, the governance evidence and
        // the reconciliation detail, and the cash-flow summary carries the runs' balances. Every one
        // of those is served head-on by a route admitting only ViewStrategies and ManageStrategies.
        await using var app = await CreateAppAsync(
            services => RegisterRunReadServices(services),
            currentUserPermissions: UserPermission.ViewSecurityMaster);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            "run-security-master-view",
            "withheld",
            "Withheld From Security Master",
            RunType.Paper,
            new DateTimeOffset(2026, 6, 20, 13, 0, 0, TimeSpan.Zero),
            fundProfileId: "test-fund-profile"));

        using var response = await app.GetTestClient().GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = payload.RootElement;

        root.GetProperty("reconciliationQueue").GetArrayLength().Should().Be(
            0,
            "run identity and its evidence are what the run routes serve, and this caller holds neither strategy permission");
        root.GetProperty("cashFlow").GetProperty("runsWithCashSignals").GetInt32().Should().Be(0);
        root.GetProperty("cashFlow").GetProperty("totalCash").GetDecimal().Should().Be(0m);

        // The counters stay, for the same reason the break-queue counters do: a count is what the
        // screen exists to show every desk it admits, and the run existed regardless.
        root.GetProperty("workspace").GetProperty("totalRuns").GetInt32().Should().Be(1);
    }

    [Theory]
    [InlineData(UiApiRoutes.WorkstationAccounting)]
    [InlineData(UiApiRoutes.WorkstationGovernance)]
    public async Task MapWorkstationEndpoints_AccountingWorkspace_WithoutDataAuthority_ShouldWithholdKernelObservability(string route)
    {
        // Kernel telemetry rides in this payload but is not accounting posture: domain names,
        // throughput, latency percentiles, drift and determinism mismatches. The Data workspace serves
        // the same object to ViewHistoricalData, ViewDiagnostics or ManageStorage, and admission to
        // the period screen establishes none of those. Its headline card goes with the projection --
        // a zeroed alert count shown to a caller who cannot see the domains would read as an
        // all-clear rather than as a withholding.
        await using var app = await CreateAppAsync(
            services => RegisterRunReadServices(services),
            currentUserPermissions: UserPermission.ViewSecurityMaster);

        using var response = await app.GetTestClient().GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = payload.RootElement;

        root.GetProperty("kernelObservability").GetProperty("domains").GetArrayLength().Should().Be(0);
        root.GetProperty("kernelObservability").GetProperty("updatedAtUtc").ValueKind.Should().Be(
            JsonValueKind.Null,
            "the withheld projection uses the same shape as an unavailable one");
        root.GetProperty("metrics").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .Should().NotContain("kernel-critical-jumps");
    }

    [Theory]
    [InlineData(UserPermission.ViewSecurityMaster | UserPermission.ViewHistoricalData)]
    [InlineData(UserPermission.ViewSecurityMaster | UserPermission.ViewDiagnostics)]
    public async Task MapWorkstationEndpoints_AccountingWorkspace_WithDataAuthority_ShouldServeKernelObservability(
        UserPermission permissions)
    {
        // The other end: a caller the Data workspace admits still sees it through the period screen.
        await using var app = await CreateAppAsync(
            services => RegisterRunReadServices(services),
            currentUserPermissions: permissions);

        using var response = await app.GetTestClient().GetAsync(UiApiRoutes.WorkstationAccounting);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        payload.RootElement.GetProperty("metrics").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .Should().Contain("kernel-critical-jumps");
    }

    [Theory]
    [InlineData(UserPermission.ViewSecurityMaster | UserPermission.ViewStrategies)]
    [InlineData(UserPermission.ViewSecurityMaster | UserPermission.ManageStrategies)]
    public async Task MapWorkstationEndpoints_AccountingWorkspace_WithRunAuthority_ShouldServeRunCards(
        UserPermission permissions)
    {
        // The other end of the same rule: withholding must follow the caller's authority, not the
        // workspace's, so a caller the run routes admit still sees what those routes would serve.
        // The Security Master permission is what opens the workspace -- neither strategy permission
        // is in its declaration -- and the strategy permission is what fills the cards.
        await using var app = await CreateAppAsync(
            services => RegisterRunReadServices(services),
            currentUserPermissions: permissions);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            "run-strategy-view",
            "served",
            "Served To Strategy Desk",
            RunType.Paper,
            new DateTimeOffset(2026, 6, 20, 13, 0, 0, TimeSpan.Zero),
            fundProfileId: "test-fund-profile"));

        using var response = await app.GetTestClient().GetAsync(UiApiRoutes.WorkstationAccounting);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        payload.RootElement.GetProperty("reconciliationQueue").EnumerateArray()
            .Should()
            .ContainSingle(item => item.GetProperty("runId").GetString() == "run-strategy-view");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_WithTradingReadOnly_ShouldNotContributeBreakRecords()
    {
        // The inbox reaches break records without passing the break-queue routes, so aligning only
        // those declarations would leave this path open. The records surface here as work items
        // carrying the break reason, status and assignee -- the same casework, in another shape.
        await using var app = await CreateAppAsync(
            services => RegisterRunReadServices(services),
            currentUserPermissions: UserPermission.ViewTrades);

        var repository = app.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
        await repository.CreateIfMissingAsync(
            TestReconciliationQueueScope,
            BuildBreakQueueItem("inbox-trading-case", Guid.NewGuid()));

        var inbox = await app.GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);

        inbox.Should().NotBeNull();

        // Asserted on the work-item id, not the kind. Trading readiness contributes a
        // ReconciliationBreak-kind item for its own reconciliation-policy gate, which is a gate
        // status rather than a break record and belongs to a ViewTrades caller. The queue-derived
        // records are the ones keyed "reconciliation-break-{breakId}".
        inbox!.Items.Should().NotContain(
            item => item.WorkItemId.StartsWith("reconciliation-break-", StringComparison.Ordinal),
            "a caller the break-queue routes refuse must not receive the same records through the inbox");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_SeparatesPeriodCloseContributorsByPayloadNotKind()
    {
        // Two contributors write OperatorWorkItemKindDto.LedgerPeriodClose. DailyAccrualWorker writes
        // it to say a loan could not accrue because the period is shut -- a loan id, a date, and
        // nothing of the book. PostgresLedgerBookService writes it to request a sign-off, carrying the
        // book name, accounting policy, required role and tolerance profile. Filtering on the kind
        // withheld from the direct-lending desk the one item in the collection that is entirely its
        // own, so the payload is what has to be tested.
        var inboxService = new RecordingOperatorInboxService();
        await inboxService.UpsertItemAsync(new OperatorWorkItemDto(
            WorkItemId: "direct-lending-period-blocked:loan-1:20260620",
            Kind: OperatorWorkItemKindDto.LedgerPeriodClose,
            Label: "Direct lending accrual blocked by accounting period",
            Detail: "Loan loan-1 could not post accrual for 2026-06-20.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: DateTimeOffset.UnixEpoch,
            Workspace: "Accounting",
            TargetRoute: "/accounting/reconciliation",
            TargetPageTag: "FundReconciliation",
            Scope: "DirectLendingAccrual"));
        await inboxService.UpsertItemAsync(new OperatorWorkItemDto(
            WorkItemId: "ledger-period-close-abc",
            Kind: OperatorWorkItemKindDto.LedgerPeriodClose,
            Label: "Accrual HardClosed sign-off required",
            Detail: "Fund book June 2026 is in HardClosed.",
            Tone: OperatorWorkItemToneDto.Critical,
            CreatedAt: DateTimeOffset.UnixEpoch,
            Workspace: "Accounting",
            TargetRoute: "/accounting/reconciliation",
            TargetPageTag: "FundReconciliation",
            Scope: "ledger-book:abc;ledger-period:def",
            RequiredSignoffRole: "Controller",
            ToleranceProfileId: "standard",
            SignoffStatus: "Pending"));

        await using var app = await CreateAppAsync(
            services =>
            {
                RegisterRunReadServices(services);
                services.AddSingleton<IOperatorInboxService>(inboxService);
            },
            currentUserPermissions: UserPermission.ViewDirectLending);

        var inbox = await app.GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(
            item => item.WorkItemId == "direct-lending-period-blocked:loan-1:20260620",
            "a blocked accrual is direct-lending work, and it discloses nothing of the ledger book");
        inbox.Items.Should().NotContain(
            item => item.WorkItemId == "ledger-period-close-abc",
            "the sign-off request carries the book, policy, role and tolerance the ledger period routes serve to ManageDirectLending and AdminMaintenance alone");
    }

    private sealed class RecordingOperatorInboxService : IOperatorInboxService
    {
        private readonly Dictionary<string, OperatorWorkItemDto> _items = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<OperatorWorkItemDto>> GetItemsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OperatorWorkItemDto>>(_items.Values.ToArray());

        public Task UpsertItemAsync(OperatorWorkItemDto item, CancellationToken ct = default)
        {
            _items[item.WorkItemId] = item;
            return Task.CompletedTask;
        }

        public Task RemoveItemAsync(string workItemId, CancellationToken ct = default)
        {
            _items.Remove(workItemId);
            return Task.CompletedTask;
        }
    }
}
