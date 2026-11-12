using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Backfill;
using Meridian.Tests.Integration;
using Xunit;
using BackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Tests.Application.Backfill;

public sealed class HistoricalBackfillSessionCoverageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"backfill-session-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("2024-01-02T23:59:00Z", "2024-01-03T00:00:00Z")]
    [InlineData("2024-01-03T00:30:00Z", "2024-01-03T00:31:00Z")]
    [InlineData("2024-07-02T23:59:00Z", "2024-07-03T00:00:00Z")]
    public async Task RunAsync_AfterHoursBar_DoesNotCheckpointTheFollowingSession(string start, string end)
    {
        var barStart = DateTimeOffset.Parse(start);
        var session = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
            barStart, "America/New_York").DateTime);
        var provider = new IntradayProvider(new AggregateBar("SPY", barStart, DateTimeOffset.Parse(end),
            100m, 101m, 99m, 100m, 10, Source: "test"));
        var checkpoint = new BackfillStatusStore(_root);
        var service = new HistoricalBackfillService([provider], checkpointStore: checkpoint);
        await using var pipeline = new EventPipeline(new InMemoryStorageSink(),
            capacity: 100, enablePeriodicFlush: false);
        var request = new BackfillRequest("test", ["SPY"], From: session, To: session,
            Granularity: DataGranularity.Minute1);

        var firstResult = await service.RunAsync(request, pipeline);

        firstResult.Success.Should().BeTrue();
        checkpoint.TryReadSymbolCheckpoints(DataGranularity.Minute1)!["SPY"].Should().Be(session);
        var nextSession = session.AddDays(1);
        await service.RunAsync(request with
        {
            From = nextSession, To = nextSession, ResumeFromCheckpoint = true
        }, pipeline);

        provider.RequestedFrom.Should().Contain(nextSession,
            "the following session must still be fetched when resuming");
    }

    private sealed class IntradayProvider(AggregateBar bar) : IHistoricalDataProvider, IHistoricalAggregateBarProvider
    {
        public string Name => "test";
        public string DisplayName => "Test";
        public string Description => string.Empty;
        public IReadOnlyList<DataGranularity> SupportedGranularities => [DataGranularity.Minute1];
        public List<DateOnly?> RequestedFrom { get; } = [];
        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HistoricalBar>>([]);
        public Task<IReadOnlyList<AggregateBar>> GetAggregateBarsAsync(
            string symbol, DataGranularity granularity, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        {
            RequestedFrom.Add(from);
            return Task.FromResult<IReadOnlyList<AggregateBar>>([bar]);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
