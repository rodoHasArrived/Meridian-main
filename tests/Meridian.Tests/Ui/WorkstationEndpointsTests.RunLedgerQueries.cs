using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task RunLedgerQueries_RunningBalanceRetainsOpeningAndRefusesAmbiguousAccountIdentity()
    {
        await using var app = await CreateAppAsync(RegisterRunReadServices, currentUserPermissions: UserPermission.ViewStrategies);
        var run = BuildReconciliationReadyRun("ledger-running-balance");
        var at = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
        var ledger = new Meridian.Ledger.Ledger();
        var firstCash = new Meridian.Ledger.LedgerAccount("Cash", Meridian.Ledger.LedgerAccountType.Asset, FinancialAccountId: "first");
        var secondCash = new Meridian.Ledger.LedgerAccount("Cash", Meridian.Ledger.LedgerAccountType.Asset, FinancialAccountId: "second");
        ledger.PostLines(at, "Opening", [(firstCash, 100m, 0m), (Meridian.Ledger.LedgerAccounts.CapitalAccount, 0m, 100m)]);
        ledger.PostLines(at.AddDays(1), "Receipt", [(firstCash, 20m, 0m), (Meridian.Ledger.LedgerAccounts.CapitalAccount, 0m, 20m)]);
        ledger.PostLines(at.AddDays(1), "Other account", [(secondCash, 999m, 0m), (Meridian.Ledger.LedgerAccounts.CapitalAccount, 0m, 999m)]);
        await app.Services.GetRequiredService<IStrategyRepository>().RecordRunAsync(run with { Metrics = run.Metrics! with { Ledger = ledger } });
        var client = app.GetTestClient();
        var path = $"/api/workstation/runs/{run.RunId}/ledger/running-balance?accountName=Cash";
        (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var response = await client.GetAsync(path + "&financialAccountId=first&from=" + Uri.EscapeDataString(at.AddDays(1).ToString("O")));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var points = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        points.RootElement.GetArrayLength().Should().Be(1);
        points.RootElement[0].GetProperty("balance").GetDecimal().Should().Be(120m);
        points.RootElement[0].GetProperty("description").GetString().Should().Be("Receipt");
    }

    [Fact]
    public async Task RunLedgerQueries_ReturnFullJournalAndAsOfAccounts_AndProtectExport()
    {
        await using var app = await CreateAppAsync(RegisterRunReadServices,
            currentUserPermissions: UserPermission.ViewStrategies | UserPermission.ExportData);
        var run = BuildReconciliationReadyRun("ledger-query-run");
        await app.Services.GetRequiredService<IStrategyRepository>().RecordRunAsync(run);
        var client = app.GetTestClient();
        var prefix = $"/api/workstation/runs/{run.RunId}/ledger";

        using var response = await client.GetAsync(prefix + "/entries?limit=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var journal = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        journal.RootElement.GetArrayLength().Should().Be(1);
        journal.RootElement[0].GetProperty("lines").GetArrayLength().Should().BeGreaterThan(1);
        journal.RootElement[0].TryGetProperty("metadata", out _).Should().BeTrue();

        var beforeFirstPosting = run.Metrics!.Ledger.Journal.Min(entry => entry.Timestamp).AddTicks(-1);
        using var accounts = await client.GetAsync(prefix + "/trial-balance/as-of?asOf=" + Uri.EscapeDataString(beforeFirstPosting.ToString("O")));
        accounts.StatusCode.Should().Be(HttpStatusCode.OK);
        (await accounts.Content.ReadAsStringAsync()).Should().Be("[]");

        using var csv = await client.PostAsJsonAsync(prefix + "/export", new { format = "csv" });
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync()).Should().Contain("metadata_json");
        (await client.GetAsync(prefix + "/entries?limit=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RunLedgerQueries_RefuseForeignRunsAndExportWithoutExportPermission()
    {
        await using var app = await CreateAppAsync(RegisterRunReadServices,
            currentUserPermissions: UserPermission.ViewStrategies, currentUserTenantId: "tenant-a", currentUserCompanyId: "company-a");
        var repository = app.Services.GetRequiredService<IStrategyRepository>();
        var foreign = BuildReconciliationReadyRun("foreign-ledger-query") with
        {
            ParameterSet = new Dictionary<string, string>
            { ["workstationTenantId"] = "tenant-b", ["workstationCompanyId"] = "company-b" }
        };
        await repository.RecordRunAsync(foreign);
        var local = BuildReconciliationReadyRun("local-ledger-query");
        await repository.RecordRunAsync(local);
        var client = app.GetTestClient();
        foreach (var view in new[] { "entries", "accounts", "symbols/AAPL", "running-balance?accountName=Cash" })
            (await client.GetAsync($"/api/workstation/runs/{foreign.RunId}/ledger/{view}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var export = await client.PostAsJsonAsync($"/api/workstation/runs/{local.RunId}/ledger/export", new { format = "csv" });
        export.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
