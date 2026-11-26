using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Storage.Policies;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed class TimeSeriesAlignmentEndpointsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"alignment-{Guid.NewGuid():N}");

    [Fact]
    public async Task PreviewAndExecution_UseRealArchiveRowsAndRepeatDeterministically()
    {
        var wednesday = new DateOnly(2024, 7, 3);
        var friday = wednesday.AddDays(2);
        await WriteAsync(Bar("STOOQ", wednesday, 100, false));
        await WriteAsync(Bar("STOOQ", wednesday, 100, false));
        await WriteAsync(Bar("STOOQ", friday, 102, false));
        await WriteAsync(Bar("ALPACA", wednesday, 101, false));

        // Publish one equivalent gzip partition beside its surviving plain source.
        var plain = ArchivePath(Bar("STOOQ", wednesday, 100, false));
        await using (var input = File.OpenRead(plain))
        await using (var output = File.Create(plain + ".gz"))
        await using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            await input.CopyToAsync(gzip);

        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        var request = new TimeSeriesAlignmentRequest(["AAPL"], wednesday, friday,
            Providers: ["stooq", "alpaca"], GapStrategy: "Null", Timezone: "UTC", Fields: ["Close"]);
        var preview = await PostAsync<TimeSeriesAlignmentPreview>(client, UiApiRoutes.AlignmentPreview, request);
        var run = await PostAsync<TimeSeriesAlignmentExecution>(client, UiApiRoutes.AlignmentCreate,
            request with { ExpectedContentHash = preview.Summary.ContentHash });
        var rerun = await PostAsync<TimeSeriesAlignmentExecution>(client, UiApiRoutes.AlignmentCreate,
            request with { Providers = ["ALPACA", "STOOQ"] });

        run.Should().BeEquivalentTo(rerun, options => options.WithStrictOrdering());
        run.Summary.Should().BeEquivalentTo(preview.Summary);
        run.Summary.TotalSourceRecords.Should().Be(4);
        run.Summary.IntervalCount.Should().Be(2);
        run.Summary.GapsDetected.Should().Be(1);
        run.Summary.Differences.Should().ContainSingle(diff =>
            diff.ObservedOverlap == 1 && diff.MaxAbsoluteCloseDifference == 1m && diff.ComparisonNote == null);
        run.Summary.Series.Should().ContainSingle(series => series.Provider == "STOOQ" && series.DuplicateRecords == 1);
        run.Rows.Select(row => row.SessionDate).Should().Equal(wednesday, wednesday, friday, friday);
        run.Rows.Select(row => row.Close).Should().Equal(101m, 100m, null, 102m);
        run.Rows.Should().OnlyContain(row => row.Open == null);
        run.Rows[1].SourceIds.Should().HaveCount(2).And.OnlyContain(id => id.StartsWith("jsonl:AAPL%2FHistoricalBar%2F2024-07-03.jsonl:"));
        run.Rows[2].SourceIds.Should().BeEmpty();
        preview.SampleRows.Should().BeEquivalentTo(run.Rows, options => options.WithStrictOrdering());
        File.Exists(plain).Should().BeTrue();
        File.Exists(plain + ".gz").Should().BeTrue();
    }

    [Fact]
    public async Task IntradayTradeAlignment_UsesHalfDayWindowAndDoesNotCarryBetweenSessions()
    {
        var day = new DateOnly(2024, 7, 3);
        var start = DateTimeOffset.Parse("2024-07-03T13:30:00Z", System.Globalization.CultureInfo.InvariantCulture);
        await WriteAsync(Print("ALPACA", start, 100m, 1));
        await WriteAsync(Print("POLYGON", start, 101m, 2));
        await WriteAsync(Print("ALPACA", start.AddMinutes(10), 103m, 3));
        await WriteAsync(Print("POLYGON", start.AddHours(3.5), 104m, 4)); // At the half-day close, excluded.
        await WriteAsync(Print("ALPACA", start.AddDays(2).AddMinutes(5), 105m, 5));

        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        var request = new TimeSeriesAlignmentRequest(["AAPL"], day, day.AddDays(2),
            Interval: "Minute5", GapStrategy: "ForwardFill", MaxGapIntervals: 1, Timezone: "America/New_York", InputKind: "Trades");
        var result = await PostAsync<TimeSeriesAlignmentExecution>(client, UiApiRoutes.AlignmentCreate, request);
        result.Summary.IntervalCount.Should().Be(42 + 78);
        result.Summary.RecordsOutsideCalendar.Should().Be(1);
        var carried = result.Rows.Single(row => row.Provider == "ALPACA" && row.SessionDate == day && row.Timestamp.Minute == 35 && row.Timestamp.Hour == 9);
        carried.Should().Match<TimeSeriesAlignmentRow>(row => row.Close == 100 && row.Volume == 0 && row.ValueStatus == "ForwardFilled");
        carried.SourceIds.Should().Equal(result.Rows[0].SourceIds);
        var nextOpening = result.Rows.Single(row => row.Provider == "ALPACA" && row.SessionDate == day.AddDays(2) &&
            row.Timestamp.Hour == 9 && row.Timestamp.Minute == 30);
        nextOpening.Close.Should().BeNull();
    }

    [Fact]
    public async Task CorporateActionBasis_LabelsUnverifiedDifferencesAndRejectsMixedSeries()
    {
        var day = new DateOnly(2024, 7, 1);
        await WriteAsync(Bar("ALPACA", day, 100, true));
        await WriteAsync(Bar("POLYGON", day, 103, true));
        await WriteAsync(Bar("ALPACA", day.AddDays(2), 101, true));
        await WriteAsync(Bar("POLYGON", day.AddDays(2), 102, true));

        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        var request = new TimeSeriesAlignmentRequest(["AAPL"], day, day.AddDays(2),
            GapStrategy: "LinearInterpolate", Timezone: "UTC");
        var response = await PostAsync<TimeSeriesAlignmentExecution>(client, UiApiRoutes.AlignmentCreate, request);
        response.Summary.Differences.Single().ComparisonNote.Should().Contain("treatment is unverified");
        response.Summary.Differences.Single().MaxAbsoluteCloseDifference.Should().Be(3);
        response.Rows.Where(row => row.SessionDate == day.AddDays(1)).Should().OnlyContain(row => row.ValueStatus == "Missing");

        await WriteAsync(Bar("ALPACA", day.AddDays(1), 120, false));
        using var ambiguous = await client.PostAsJsonAsync(UiApiRoutes.AlignmentPreview, request);
        ambiguous.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var adjusted = await PostAsync<TimeSeriesAlignmentPreview>(client, UiApiRoutes.AlignmentPreview,
            request with { AdjustmentPolicy = "Adjusted" });
        adjusted.Summary.Series.Should().OnlyContain(series => series.AdjustmentBasis == "Adjusted");
    }

    [Fact]
    public async Task RejectsStalePreviewAndContradictoryDuplicate()
    {
        var day = new DateOnly(2024, 7, 1);
        await WriteAsync(Bar("ALPACA", day, 100, false));
        await WriteAsync(Bar("POLYGON", day, 101, false));
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        var request = new TimeSeriesAlignmentRequest(["AAPL"], day, day);
        var preview = await PostAsync<TimeSeriesAlignmentPreview>(client, UiApiRoutes.AlignmentPreview, request);

        await WriteAsync(Bar("POLYGON", day, 101, false));
        using var stale = await client.PostAsJsonAsync(UiApiRoutes.AlignmentCreate,
            request with { ExpectedContentHash = preview.Summary.ContentHash });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await WriteAsync(Bar("POLYGON", day, 102, false));
        using var conflict = await client.PostAsJsonAsync(UiApiRoutes.AlignmentCreate, request);
        conflict.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ContinuousTimeZoneKeepsRepeatedWallClockHoursDistinct()
    {
        var day = new DateOnly(2024, 10, 27);
        var first = new DateTimeOffset(2024, 10, 27, 0, 20, 0, TimeSpan.Zero);
        await WriteAsync(Print("ALPACA", first, 100m, 1));
        await WriteAsync(Print("POLYGON", first, 101m, 1));
        await WriteAsync(Print("ALPACA", first.AddHours(1), 102m, 2));
        await WriteAsync(Print("POLYGON", first.AddHours(1), 103m, 2));

        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        var request = new TimeSeriesAlignmentRequest(["AAPL"], day, day, Interval: "Hour1",
            Timezone: "Europe/London", MarketHoursOnly: false, InputKind: "Trades");
        var result = await PostAsync<TimeSeriesAlignmentExecution>(client, UiApiRoutes.AlignmentCreate, request);

        result.Summary.IntervalCount.Should().Be(25);
        var alpaca = result.Rows.Where(row => row.Provider == "ALPACA").ToArray();
        alpaca.Select(row => row.Timestamp.Hour).Should().Equal(1, 1);
        alpaca.Select(row => row.Timestamp.Offset).Should().Equal(TimeSpan.FromHours(1), TimeSpan.Zero);
        alpaca.Select(row => row.Close).Should().Equal(100m, 102m);
    }

    [Fact]
    public async Task UnreadableSelectedArchiveFailsWithoutPublishingHostOutput()
    {
        var day = new DateOnly(2024, 7, 1);
        var bar = Bar("ALPACA", day, 100, false);
        await WriteAsync(bar);
        await WriteAsync(Bar("POLYGON", day, 101, false));
        var hostPath = Path.Combine(_root, "host-output.csv");

        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        var body = new { symbols = new[] { "AAPL" }, fromDate = day, toDate = day, outputPath = hostPath };
        using var valid = await client.PostAsJsonAsync(UiApiRoutes.AlignmentCreate, body);
        valid.StatusCode.Should().Be(HttpStatusCode.OK, await valid.Content.ReadAsStringAsync());
        File.Exists(hostPath).Should().BeFalse();

        await File.AppendAllTextAsync(ArchivePath(bar), "{bad archive line}");
        using var invalid = await client.PostAsJsonAsync(UiApiRoutes.AlignmentCreate, body);
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        File.Exists(hostPath).Should().BeFalse();
    }

    [Fact]
    public async Task RejectsUnboundedGridAndRequiresHistoricalDataPermission()
    {
        var day = new DateOnly(2024, 7, 1);
        var request = new TimeSeriesAlignmentRequest(["AAPL"], day, day.AddDays(1),
            Interval: "Second1", MarketHoursOnly: false);
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var tooLarge = await client.PostAsJsonAsync(UiApiRoutes.AlignmentPreview, request);
        tooLarge.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var deniedApp = await StartAsync((UserPermission)0);
        using var deniedClient = deniedApp.GetTestClient();
        using var denied = await deniedClient.PostAsJsonAsync(UiApiRoutes.AlignmentCreate, request);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static MarketEvent Bar(string source, DateOnly date, decimal close, bool? adjusted)
    {
        var bar = new HistoricalBar("AAPL", date, close, close, close, close, 1, source, IsAdjusted: adjusted);
        return MarketEvent.HistoricalBar(bar.ToTimestampUtc(), "AAPL", bar, source);
    }

    private static MarketEvent Print(string source, DateTimeOffset timestamp, decimal price, long sequence)
        => MarketEvent.Trade(timestamp, "AAPL",
            new Trade(timestamp, "AAPL", price, 10, (AggressorSide)0, sequence), source);

    private string ArchivePath(MarketEvent evt) => new JsonlStoragePolicy(new StorageOptions { RootPath = _root }).GetPath(evt);

    private async Task WriteAsync(MarketEvent evt)
    {
        var path = ArchivePath(evt);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.AppendAllTextAsync(path, JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions) + "\n");
    }

    private async Task<WebApplication> StartAsync(UserPermission permission = UserPermission.ViewHistoricalData)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new StorageOptions { RootPath = _root });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permission;
            await next();
        });
        app.MapAlignmentEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string route, TimeSeriesAlignmentRequest request)
    {
        using var response = await client.PostAsJsonAsync(route, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
