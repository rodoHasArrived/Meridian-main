using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    private static AccrualScheduleInput AccrualInput() => new("fee-accrual", "fund", Guid.NewGuid(), "entity", "USD",
        RecurringJournalCadence.Monthly, new(2026, 10, 1), new(2026, 11, 1), "123.123456789123456789",
        "Expense:Fees", LedgerAccountType.Expense, "Liability:Accruals", LedgerAccountType.Liability,
        "Reviewed fee accrual", [new("evidence", "retained://fees", "Accrual", "Terms", new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), "custodian", EvidenceVersion: 1)]);

    [Fact]
    public async Task AccrualSchedules_PreviewRetainAndUnchangedReplay_KeepExactAmountAndServerActor()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-accrual-api-" + Guid.NewGuid().ToString("N"));
        var store = new FileRecurringJournalStore(directory);
        await store.InitializeAsync();
        try
        {
            await using var app = await CreateAppAsync(services =>
            {
                services.AddSingleton<IRecurringJournalStore>(store);
                services.AddSingleton<IRecurringJournalPeriodAuthority>(new EndpointRecurringPeriodAuthority());
            }, mapLedgerApi: true, currentUserPermissions: UserPermission.ManageLedgerReports);
            var client = app.GetTestClient();
            var input = AccrualInput();
            using var previewResponse = await client.PostAsJsonAsync(UiApiRoutes.LedgerAccrualSchedulePreview, input, ServerJsonOptions);
            previewResponse.StatusCode.Should().Be(HttpStatusCode.OK, await previewResponse.Content.ReadAsStringAsync());
            using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
            var rows = preview.RootElement.GetProperty("occurrences").EnumerateArray().ToArray();
            rows.Should().HaveCount(2);
            rows[0].GetProperty("amount").GetString().Should().Be(input.Amount);
            var command = new ConfigureAccrualScheduleInput(input, preview.RootElement.GetProperty("fingerprint").GetString()!);
            var first = await client.PostAsJsonAsync(UiApiRoutes.LedgerAccrualScheduleConfigure, command, ServerJsonOptions);
            first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
            var replay = await client.PostAsJsonAsync(UiApiRoutes.LedgerAccrualScheduleConfigure, command, ServerJsonOptions);
            replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
            (await replay.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
            var changed = await client.PostAsJsonAsync(UiApiRoutes.LedgerAccrualScheduleConfigure,
                command with { Input = input with { Amount = "9" } }, ServerJsonOptions);
            changed.StatusCode.Should().Be(HttpStatusCode.Conflict);
            await using var session = await store.OpenSessionAsync();
            session.Schedules.Should().ContainSingle();
            session.Templates.Should().ContainSingle();
            var saved = session.CurrentSchedules.Single();
            saved.RegisteredBy.Should().Be("ops-user");
            saved.Scope.TenantId.Should().Be("tenant-test");
            saved.Schedule.Parameters["amount"].Should().Be(123.123456789123456789m);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1e3")]
    [InlineData("1.12345678901234567890123456789")]
    [InlineData("9999999999999999999999999999999")]
    public void AccrualSchedules_RejectInexactUnsupportedOrZeroAmounts(string amount)
    {
        Action action = () => { _ = LedgerEndpoints.BuildAccrualSchedule(AccrualInput() with { Amount = amount }, "tenant", "company"); };
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task AccrualSchedules_ReadersCannotConfigureOrPreview_AndUnavailableRegistryIsExplicit()
    {
        await using var app = await CreateAppAsync(mapLedgerApi: true, currentUserPermissions: UserPermission.ViewLedgerReports);
        var client = app.GetTestClient();
        var input = AccrualInput();
        var response = await client.PostAsJsonAsync(UiApiRoutes.LedgerAccrualScheduleConfigure,
            new ConfigureAccrualScheduleInput(input, new string('a', 64)), ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var preview = await client.PostAsJsonAsync(UiApiRoutes.LedgerAccrualSchedulePreview, input, ServerJsonOptions);
        preview.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var registry = await client.GetAsync($"{UiApiRoutes.LedgerAccrualSchedules}?fundProfileId=fund&ledgerBookId={input.LedgerBookId:D}&entityId=entity");
        registry.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    }
}
