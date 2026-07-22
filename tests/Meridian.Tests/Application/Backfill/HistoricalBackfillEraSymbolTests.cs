using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Tests.Integration;
using Xunit;
using AppBackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Tests.Application.Backfill;

/// <summary>
/// Verifies era-correct per-chunk symbol resolution: a daily backfill spanning a ticker
/// rename queries the provider with the ticker of that era while landing every bar under
/// the canonical requested symbol, so renamed securities keep one continuous series.
/// </summary>
public sealed class HistoricalBackfillEraSymbolTests : IAsyncLifetime
{
    private static readonly DateOnly RenameDate = new(2022, 6, 9);

    private InMemoryStorageSink _sink = null!;
    private EventPipeline _pipeline = null!;

    public Task InitializeAsync()
    {
        _sink = new InMemoryStorageSink();
        _pipeline = new EventPipeline(_sink, capacity: 10_000, enablePeriodicFlush: false);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _pipeline.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_PreRenameRange_QueriesEraTickerAndLandsUnderCanonicalSymbol()
    {
        var requestedSymbols = new ConcurrentBag<string>();
        var provider = MakeRecordingProvider(requestedSymbols);
        var service = new HistoricalBackfillService(
            [provider],
            symbolTimelineResolver: new StubTimelineResolver());

        var result = await service.RunAsync(
            new AppBackfillRequest("test", ["META"], From: new DateOnly(2022, 1, 3), To: new DateOnly(2022, 1, 4)),
            _pipeline);

        result.Success.Should().BeTrue();
        requestedSymbols.Should().NotBeEmpty();
        requestedSymbols.Should().OnlyContain(symbol => symbol == "FB",
            "chunks starting before the rename must query the provider with the era ticker");
        _sink.StoredEvents.Should().NotBeEmpty();
        _sink.StoredEvents.Should().OnlyContain(evt => evt.Symbol == "META",
            "fetched bars must land under the canonical requested symbol");
    }

    [Fact]
    public async Task RunAsync_PostRenameRange_QueriesRequestedSymbolUnchanged()
    {
        var requestedSymbols = new ConcurrentBag<string>();
        var provider = MakeRecordingProvider(requestedSymbols);
        var service = new HistoricalBackfillService(
            [provider],
            symbolTimelineResolver: new StubTimelineResolver());

        await service.RunAsync(
            new AppBackfillRequest("test", ["META"], From: new DateOnly(2023, 1, 3), To: new DateOnly(2023, 1, 4)),
            _pipeline);

        requestedSymbols.Should().NotBeEmpty();
        requestedSymbols.Should().OnlyContain(symbol => symbol == "META");
    }

    [Fact]
    public async Task RunAsync_WithoutTimelineResolver_BehaviourIsUnchanged()
    {
        var requestedSymbols = new ConcurrentBag<string>();
        var provider = MakeRecordingProvider(requestedSymbols);
        var service = new HistoricalBackfillService([provider]);

        await service.RunAsync(
            new AppBackfillRequest("test", ["META"], From: new DateOnly(2022, 1, 3), To: new DateOnly(2022, 1, 4)),
            _pipeline);

        requestedSymbols.Should().NotBeEmpty();
        requestedSymbols.Should().OnlyContain(symbol => symbol == "META");
    }

    [Fact]
    public async Task RunAsync_TimelineResolverThrows_FallsBackToRequestedSymbol()
    {
        var requestedSymbols = new ConcurrentBag<string>();
        var provider = MakeRecordingProvider(requestedSymbols);
        var service = new HistoricalBackfillService(
            [provider],
            symbolTimelineResolver: new ThrowingTimelineResolver());

        var result = await service.RunAsync(
            new AppBackfillRequest("test", ["META"], From: new DateOnly(2022, 1, 3), To: new DateOnly(2022, 1, 4)),
            _pipeline);

        result.Success.Should().BeTrue("symbology failures must never break a backfill run");
        requestedSymbols.Should().NotBeEmpty();
        requestedSymbols.Should().OnlyContain(symbol => symbol == "META");
    }

    private static RecordingProvider MakeRecordingProvider(ConcurrentBag<string> requestedSymbols)
        => new("test", (symbol, from, _, _) =>
        {
            requestedSymbols.Add(symbol);
            var sessionDate = from ?? new DateOnly(2022, 1, 3);
            return Task.FromResult<IReadOnlyList<HistoricalBar>>(
                [new HistoricalBar(symbol, sessionDate, 100m, 101m, 99m, 100.5m, 1_000, "test")]);
        });

    /// <summary>META traded as FB strictly before the rename date.</summary>
    private sealed class StubTimelineResolver : IHistoricalSymbolTimelineResolver
    {
        public Task<string> ResolveTickerForDateAsync(string symbol, DateOnly date, CancellationToken ct = default)
            => Task.FromResult(
                string.Equals(symbol, "META", StringComparison.OrdinalIgnoreCase) && date < RenameDate
                    ? "FB"
                    : symbol);
    }

    private sealed class ThrowingTimelineResolver : IHistoricalSymbolTimelineResolver
    {
        public Task<string> ResolveTickerForDateAsync(string symbol, DateOnly date, CancellationToken ct = default)
            => throw new InvalidOperationException("symbology store unavailable");
    }

    private sealed class RecordingProvider : IHistoricalDataProvider
    {
        private readonly Func<string, DateOnly?, DateOnly?, CancellationToken, Task<IReadOnlyList<HistoricalBar>>> _fetch;

        public RecordingProvider(
            string name,
            Func<string, DateOnly?, DateOnly?, CancellationToken, Task<IReadOnlyList<HistoricalBar>>> fetch)
        {
            Name = DisplayName = name;
            _fetch = fetch;
        }

        public string Name { get; }
        public string DisplayName { get; }
        public string Description => string.Empty;

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
            => _fetch(symbol, from, to, ct);
    }
}
