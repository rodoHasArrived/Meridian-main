using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData("other-tenant", "fund-alpha", HttpStatusCode.Forbidden, false)]
    [InlineData("tenant-test", "other-fund", HttpStatusCode.Conflict, false)]
    [InlineData("tenant-test", "fund-alpha", HttpStatusCode.Conflict, false)]
    [InlineData("tenant-test", "fund-alpha", HttpStatusCode.BadRequest, true)]
    public async Task DailyMarkPreview_RejectsInvalidConfidenceForeignScopeAndMissingSnapshotEvidence_WithoutWrites(
        string scheduleTenant, string requestedFund, HttpStatusCode expectedStatus, bool invalidConfidence)
    {
        var asOf = new DateTimeOffset(2026, 7, 3, 21, 0, 0, TimeSpan.Zero);
        var source = new InMemoryDailyValuationPortfolioSource();
        var schedule = await source.SaveAsync(new DailyValuationScheduleWorkItem(
            "preview-schedule", "fund-alpha", "USD", "preparer", Guid.NewGuid(), Guid.NewGuid(),
            asOf, [], "policy-1", "Daily close", "official-close", "controller", asOf.AddDays(-20),
            "Preview current scope", EntityId: "entity-alpha", TenantId: scheduleTenant, CompanyId: "tenant-test",
            PositionSnapshotScopes: [new DailyValuationPositionSnapshotScope("run-1", "account-1")]));
        var configuration = new AccountingConfigurationService(new InMemoryAccountingConfigurationStore(),
            new InMemoryAccountingActionAuditStore());
        var drafts = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configuration, new InMemoryAccountingActionAuditStore());
        var runner = new AutomatedJournalIntakeRunner(new AutomatedJournalDraftIntakeService(workbench, drafts, configuration),
            new FeeScheduleAccrualEventProducer());
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IDailyValuationPortfolioSource>(source);
            services.AddSingleton(new DailyValuationPositionService(null, null, null));
            services.AddSingleton(runner);
            services.AddSingleton(FundScopedWriteTenantOptions.Disabled);
        }, mapLedgerApi: true, currentUserPermissions: UserPermission.AdminMaintenance);

        var request = new RunDailyMarkToMarketDraftIntakeRequest(requestedFund, schedule.Currency, "preparer",
            schedule.LedgerBookId, schedule.PeriodId, asOf, [], schedule.PolicyId, schedule.PolicyName,
            schedule.ValuationMethod, schedule.PolicyApprovedBy, schedule.PolicyApprovedAtUtc, schedule.Reason,
            EntityId: schedule.EntityId, ScheduleId: schedule.ScheduleId);
        var payload = JsonSerializer.SerializeToNode(request, ServerJsonOptions)!;
        payload["minimumConfidence"]!.GetValue<string>().Should().Be("Medium",
            "preview must accept the same string confidence contract emitted for browser schedules");
        if (invalidConfidence)
            payload["minimumConfidence"] = "UnknownConfidence";
        var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.LedgerJournalAutomationDailyMarkToMarketPreview, payload, ServerJsonOptions);

        var responseBody = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expectedStatus, "response body was {0}", responseBody);
        if (invalidConfidence)
            responseBody.Should().Contain("invalid JSON or confidence");
        else if (scheduleTenant == "tenant-test")
            responseBody.Should().Contain(requestedFund == "fund-alpha"
                ? "position snapshot store is unavailable" : "Preview scope does not match");
        (await drafts.ListAsync("fund-alpha", schedule.LedgerBookId)).Should().BeEmpty();
        (await source.GetAsync(schedule.ScheduleId)).Should().BeEquivalentTo(schedule);
    }
}
