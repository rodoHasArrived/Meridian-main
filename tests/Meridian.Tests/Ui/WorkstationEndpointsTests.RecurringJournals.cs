using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
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
    [Fact]
    public async Task RecurringQueue_UsesAuthenticatedScope_AndFailsUnavailableInsteadOfEmpty()
    {
        var source = new CapturingRecurringQueueSource();
        await using var app = await CreateAppAsync(services => services.AddSingleton<IRecurringJournalQueueSource>(source),
            mapLedgerApi: true, currentUserPermissions: UserPermission.ViewLedgerReports);
        var client = app.GetTestClient();
        var book = Guid.NewGuid();
        var url = $"{UiApiRoutes.LedgerJournalAutomationRecurringOccurrences}?fundProfileId=fund&ledgerBookId={book:D}&entityId=entity&tenantId=forged&companyId=forged";
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        source.Scope.Should().Be(("tenant-test", "tenant-test"));
        source.Unavailable = true;
        (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await client.GetAsync(UiApiRoutes.LedgerJournalAutomationRecurringOccurrences)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RecurringConfiguration_IsAtomicAndRetainsServerActorAndOwnership()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-recurring-api-" + Guid.NewGuid().ToString("N"));
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
            var now = DateTimeOffset.UtcNow;
            var template = new JournalTemplate("api-template", "Test template", "Retained source", [
                new(LedgerAccounts.Cash, JournalTemplateSide.Debit, FixedAmount: 10m),
                new(LedgerAccounts.CashInterestIncome, JournalTemplateSide.Credit, FixedAmount: 10m)]);
            var schedule = new RecurringJournalSchedule("api-schedule", template.TemplateId, new("fund", "book"),
                RecurringJournalCadence.Monthly, new(2026, 10, 1), "forged", now);
            var request = new ConfigureRecurringJournalRequest(RecurringJournalScheduleSnapshot.Capture(schedule), template,
                new("fund", Guid.NewGuid(), "entity", "USD", "forged", "forged"), [], 0, 0);
            var first = await client.PostAsJsonAsync(UiApiRoutes.LedgerJournalAutomationRecurringSchedules, request, ServerJsonOptions);
            first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
            var stale = await client.PostAsJsonAsync(UiApiRoutes.LedgerJournalAutomationRecurringSchedules,
                request with { ExpectedTemplateVersion = 1 }, ServerJsonOptions);
            stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
            await using var session = await store.OpenSessionAsync();
            session.Templates.Should().ContainSingle();
            session.Schedules.Should().ContainSingle();
            session.CurrentSchedules.Single().RegisteredBy.Should().Be("ops-user");
            session.CurrentSchedules.Single().Scope.TenantId.Should().Be("tenant-test");
            session.Templates.Single().Scope!.CompanyId.Should().Be("tenant-test");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class CapturingRecurringQueueSource : IRecurringJournalQueueSource
    {
        public (string?, string?) Scope { get; private set; }
        public bool Unavailable { get; set; }
        public Task<RecurringJournalQueueDto> GetQueueAsync(string fundProfileId, Guid ledgerBookId, string entityId,
            CancellationToken ct = default, string? tenantId = null, string? companyId = null)
        {
            Scope = (tenantId, companyId);
            if (Unavailable)
                throw new IOException("Injected durable state outage");
            return Task.FromResult(new RecurringJournalQueueDto(fundProfileId, ledgerBookId, entityId, []));
        }
    }

    private sealed class EndpointRecurringPeriodAuthority : IRecurringJournalPeriodAuthority
    {
        public Task<RecurringJournalPeriodState> ResolveAsync(RecurringJournalScope scope, DateOnly date, CancellationToken ct)
            => Task.FromResult(new RecurringJournalPeriodState(Guid.NewGuid().ToString("D"), 1, true, null, null, null));
    }
}
