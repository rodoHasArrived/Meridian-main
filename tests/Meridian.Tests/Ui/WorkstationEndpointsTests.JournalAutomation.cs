using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MonthlyAutomationEndpoints_IsolateTenant_RearmWithAudit_AndRejectStaleOrRunningWrites()
    {
        var store = new InMemoryAutomatedJournalScheduleStore();
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var owned = await store.SaveAsync(MonthlySchedule("owned") with
        {
            TenantId = "tenant-test",
            CompanyId = "tenant-test"
        });
        await store.SaveAsync(MonthlySchedule("other") with
        {
            TenantId = "tenant-other",
            CompanyId = "tenant-other"
        });
        await using var app = await CreateAppAsync(
            services =>
            {
                services.AddSingleton<IAutomatedJournalScheduleStore>(store);
                services.AddSingleton<IManualJournalEntryDraftStore>(draftStore);
                services.AddSingleton<TimeProvider>(TimeProvider.System);
            },
            mapLedgerApi: true,
            currentUserPermissions: Meridian.Identity.Auth.UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        var list = await client.GetFromJsonAsync<AutomatedJournalScheduleWorkItem[]>(
            UiApiRoutes.LedgerJournalAutomationMonthlySchedules,
            ServerJsonOptions);
        var rearmResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerJournalAutomationMonthlySchedules,
            owned,
            ServerJsonOptions);
        var rearmed = await rearmResponse.Content.ReadFromJsonAsync<AutomatedJournalScheduleWorkItem>(ServerJsonOptions);
        var staleResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerJournalAutomationMonthlySchedules,
            owned,
            ServerJsonOptions);
        var running = await store.SaveAsync(rearmed! with { State = AutomatedJournalScheduleStateDto.Running });
        var runningResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerJournalAutomationMonthlySchedules,
            running,
            ServerJsonOptions);

        list.Should().ContainSingle().Which.ScheduleId.Should().Be("owned");
        rearmResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        rearmed!.RunHistory.Should().ContainSingle(history =>
            history.HistoryKind == AutomatedJournalScheduleHistoryKind.Rearm &&
            history.Actor == "ops-user" &&
            history.PreviousVersion == owned.Version &&
            history.ResultVersion == rearmed.Version);
        staleResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        runningResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task MonthlyRunDueEndpoint_ResolvesWorkerAndExecutesOneShot()
    {
        var store = new InMemoryAutomatedJournalScheduleStore();
        var configurationStore = new InMemoryAccountingConfigurationStore();
        var auditStore = new InMemoryAccountingActionAuditStore();
        var configuration = new AccountingConfigurationService(configurationStore, auditStore);
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(draftStore, configuration, auditStore);
        var intake = new AutomatedJournalDraftIntakeService(workbench, draftStore, configuration);
        var runner = new AutomatedJournalIntakeRunner(intake, new FeeScheduleAccrualEventProducer());
        var worker = new AutomatedJournalScheduledWorker(
            store,
            runner,
            NullLogger<AutomatedJournalScheduledWorker>.Instance);
        await using var app = await CreateAppAsync(
            services =>
            {
                services.AddSingleton<IAutomatedJournalScheduleStore>(store);
                services.AddSingleton(worker);
                services.AddSingleton<TimeProvider>(TimeProvider.System);
            },
            mapLedgerApi: true,
            currentUserPermissions: Meridian.Identity.Auth.UserPermission.AdminMaintenance);

        var response = await app.GetTestClient().PostAsync(
            UiApiRoutes.LedgerJournalAutomationMonthlyRunDue,
            content: null);
        var result = await response.Content.ReadFromJsonAsync<AutomatedJournalScheduledBatchResult>(ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result!.Runs.Should().BeEmpty();
    }

    private static AutomatedJournalScheduleWorkItem MonthlySchedule(string scheduleId)
        => new(
            ScheduleId: scheduleId,
            Kind: AutomatedJournalScheduleKind.DividendCapture,
            FundProfileId: "fund-alpha",
            LedgerBookId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            PeriodId: "2026-07",
            EntityId: "entity-alpha",
            Currency: "USD",
            PeriodStart: new DateOnly(2026, 7, 1),
            PeriodEnd: new DateOnly(2026, 7, 31),
            DueDate: new DateOnly(2026, 8, 1),
            DueTimeLocal: new TimeOnly(9, 0),
            TimeZoneId: "UTC",
            Actor: "client-supplied-actor",
            Positions: [new DividendAccrualPosition("AAPL", 100m)]);
}
