using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Identity.Auth;

namespace Meridian.Tests.Integration.EndpointTests;

[Trait("Category", "Integration")]
public sealed class EndpointTestFixtureDataIsolationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentFixtures_ReplayFilesSessionsAndSavedSamples_BelongOnlyToTheirHost(
        bool disposeFirstFixtureFirst)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var first = new EndpointTestFixture();
        var second = new EndpointTestFixture();

        try
        {
            await Task.WhenAll(
                Task.Run(first.InitializeAsync, timeout.Token),
                Task.Run(second.InitializeAsync, timeout.Token)).WaitAsync(timeout.Token);
            using var firstClient = first.CreateSessionClient(UserPermission.ViewHistoricalData);
            using var secondClient = second.CreateSessionClient(UserPermission.ViewHistoricalData);
            var firstData = await WriteDataAsync(first, "FIRSTDATA", timeout.Token);
            var secondData = await WriteDataAsync(second, "SECONDDATA", timeout.Token);

            var states = await Task.WhenAll(
                CreateReplayAndSampleAsync(firstClient, firstData.Path, timeout.Token),
                CreateReplayAndSampleAsync(secondClient, secondData.Path, timeout.Token));
            await Task.WhenAll(
                AssertVisibleDataAsync(firstClient, firstData, states[0], states[1], timeout.Token),
                AssertVisibleDataAsync(secondClient, secondData, states[1], states[0], timeout.Token),
                AssertForeignFileRejectedAsync(firstClient, secondData.Path, timeout.Token),
                AssertForeignFileRejectedAsync(secondClient, firstData.Path, timeout.Token));

            var disposed = disposeFirstFixtureFirst ? first : second;
            var survivorClient = disposeFirstFixtureFirst ? secondClient : firstClient;
            var survivorData = disposeFirstFixtureFirst ? secondData : firstData;
            var survivorState = states[disposeFirstFixtureFirst ? 1 : 0];
            var otherState = states[disposeFirstFixtureFirst ? 0 : 1];
            await disposed.DisposeAsync().WaitAsync(timeout.Token);

            File.Exists(disposeFirstFixtureFirst ? firstData.Path : secondData.Path).Should().BeFalse();
            File.Exists(survivorData.Path).Should().BeTrue();
            await AssertVisibleDataAsync(survivorClient, survivorData, survivorState, otherState, timeout.Token);
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await Task.WhenAll(first.DisposeAsync(), second.DisposeAsync()).WaitAsync(cleanupTimeout.Token);
        }
    }

    private static async Task<(string Path, string Line)> WriteDataAsync(
        EndpointTestFixture fixture, string symbol, CancellationToken ct)
    {
        Directory.CreateDirectory(fixture.DataRoot);
        // Both hosts contain the same relative filename, but the persisted trade is different.
        var path = Path.Combine(fixture.DataRoot, "ISOLATION_trades.jsonl");
        var timestamp = new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var trade = new Trade(timestamp, symbol, 100m, 10, AggressorSide.Buy, 1, "TEST", "XNYS");
        var line = JsonSerializer.Serialize(
            MarketEvent.Trade(timestamp, symbol, trade, "TEST", 1),
            MarketDataJsonContext.HighPerformanceOptions);
        await File.WriteAllTextAsync(path, line + Environment.NewLine, ct);
        return (path, line);
    }

    private static async Task<(string SessionId, string SampleId)> CreateReplayAndSampleAsync(
        HttpClient client, string filePath, CancellationToken ct)
    {
        using var replayResponse = await client.PostAsJsonAsync(
            "/api/replay/start", new { FilePath = filePath }, ct);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var replay = JsonDocument.Parse(await replayResponse.Content.ReadAsStringAsync(ct));
        var sessionId = replay.RootElement.GetProperty("sessionId").GetString()!;

        using var sampleResponse = await client.PostAsJsonAsync(
            "/api/sampling/create", new { Symbol = "ISOLATION", Strategy = "systematic", SampleSize = 10 }, ct);
        sampleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var sample = JsonDocument.Parse(await sampleResponse.Content.ReadAsStringAsync(ct));
        sample.RootElement.GetProperty("status").GetString().Should().Be("created");
        sample.RootElement.GetProperty("sampleSize").GetInt32().Should().Be(1);
        return (sessionId, sample.RootElement.GetProperty("sampleId").GetString()!);
    }

    private static async Task AssertVisibleDataAsync(
        HttpClient client,
        (string Path, string Line) data,
        (string SessionId, string SampleId) own,
        (string SessionId, string SampleId) other,
        CancellationToken ct)
    {
        using var filesResponse = await client.GetAsync("/api/replay/files?symbol=ISOLATION", ct);
        filesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var files = JsonDocument.Parse(await filesResponse.Content.ReadAsStringAsync(ct));
        files.RootElement.GetProperty("files").EnumerateArray()
            .Select(file => file.GetProperty("path").GetString()).Should().Equal(data.Path);

        using var ownReplay = await client.GetAsync($"/api/replay/{own.SessionId}/status", ct);
        ownReplay.StatusCode.Should().Be(HttpStatusCode.OK);
        using var replay = JsonDocument.Parse(await ownReplay.Content.ReadAsStringAsync(ct));
        replay.RootElement.GetProperty("filePath").GetString().Should().Be(data.Path);
        using var otherReplay = await client.GetAsync($"/api/replay/{other.SessionId}/status", ct);
        otherReplay.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var savedResponse = await client.GetAsync("/api/sampling/saved", ct);
        savedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var saved = JsonDocument.Parse(await savedResponse.Content.ReadAsStringAsync(ct));
        saved.RootElement.GetProperty("samples").EnumerateArray()
            .Select(sample => sample.GetProperty("sampleId").GetString()).Should().Equal(own.SampleId);
        using var ownSample = await client.GetAsync($"/api/sampling/{own.SampleId}", ct);
        ownSample.StatusCode.Should().Be(HttpStatusCode.OK);
        using var sample = JsonDocument.Parse(await ownSample.Content.ReadAsStringAsync(ct));
        sample.RootElement.GetProperty("events").EnumerateArray()
            .Select(line => line.GetString()).Should().Equal(data.Line);
        using var otherSample = await client.GetAsync($"/api/sampling/{other.SampleId}", ct);
        otherSample.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task AssertForeignFileRejectedAsync(HttpClient client, string foreignPath, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/replay/start", new { FilePath = foreignPath }, ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
