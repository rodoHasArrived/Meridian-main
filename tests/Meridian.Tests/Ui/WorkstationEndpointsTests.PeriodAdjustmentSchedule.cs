using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData(UserPermission.ManageDirectLending, true)]
    [InlineData(UserPermission.AdminMaintenance, true)]
    [InlineData(UserPermission.ManageDirectLending, false)]
    public async Task GenericMonthlyScheduleRoute_CannotCreateOrConfigureSourceLinkedReversal(
        UserPermission permission, bool dedicatedKind)
    {
        var store = new InMemoryAutomatedJournalScheduleStore();
        await using var app = await CreateAppAsync(services =>
            services.AddSingleton<IAutomatedJournalScheduleStore>(store),
            mapLedgerApi: true, currentUserPermissions: permission);
        var item = MonthlySchedule("untrusted-mark-reversal") with
        {
            Kind = dedicatedKind ? AutomatedJournalScheduleKind.PeriodEndMarkReversal : AutomatedJournalScheduleKind.DividendCapture,
            PeriodAdjustment = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 8, 1),
                "untrusted-intent", "Generic route must not grant source authority", true)
        };
        var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.LedgerJournalAutomationMonthlySchedules, item, ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await store.ListAsync()).Should().BeEmpty();
    }
}
