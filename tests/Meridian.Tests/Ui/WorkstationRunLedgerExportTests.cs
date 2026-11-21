using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Strategies.Interfaces;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task RunLedgerAsOf_UsesHistoricalPostingCutThroughHttp()
    {
        await using var app = await CreateAppAsync(RegisterRunReadServices, currentUserPermissions: UserPermission.ViewStrategies);
        var entry = BuildActivePaperRun("run-as-of-http", withBreaks: false);
        await app.Services.GetRequiredService<IStrategyRepository>().RecordRunAsync(entry);
        var first = entry.Metrics!.Ledger.Journal.Min(posting => posting.Timestamp);
        var expected = new Meridian.Strategies.Services.LedgerReadService().BuildSummary(entry, first)!;

        var response = await app.GetTestClient().GetFromJsonAsync<LedgerSummary>(
            $"/api/workstation/runs/{entry.RunId}/ledger?asOf={Uri.EscapeDataString(first.ToString("O"))}", ServerJsonOptions);

        response.Should().NotBeNull();
        response!.AsOf.Should().Be(first);
        response.AssetBalance.Should().Be(expected.AssetBalance);
        response.JournalEntryCount.Should().Be(expected.JournalEntryCount);
        response.Journal.Should().OnlyContain(posting => posting.Timestamp <= first);
    }

    [Fact]
    public async Task RunLedgerExport_LegacyRunDownloadsAvailableReportsAndDeclaresMissingLots()
    {
        await using var app = await CreateAppAsync(RegisterRunReadServices, currentUserPermissions: UserPermission.ViewStrategies);
        var entry = BuildActivePaperRun("run-export-http", withBreaks: false);
        await app.Services.GetRequiredService<IStrategyRepository>().RecordRunAsync(entry);

        using var response = await app.GetTestClient().GetAsync($"/api/workstation/runs/{entry.RunId}/ledger/export?format=Csv");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        using var archive = new ZipArchive(await response.Content.ReadAsStreamAsync());
        archive.Entries.Should().Contain(file => file.FullName == "journal.csv");
        archive.Entries.Should().Contain(file => file.FullName == "trial-balance.csv");
        var availability = archive.GetEntry("availability.csv");
        availability.Should().NotBeNull();
        using var reader = new StreamReader(availability!.Open());
        (await reader.ReadToEndAsync()).Should().Contain("Unavailable");
    }

    [Fact]
    public async Task RunLedgerExport_UnknownRunDoesNotExposePackage()
    {
        await using var app = await CreateAppAsync(RegisterRunReadServices, currentUserPermissions: UserPermission.ViewStrategies);
        using var response = await app.GetTestClient().GetAsync("/api/workstation/runs/no-such-run/ledger/export");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
