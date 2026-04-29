using FluentAssertions;
using Meridian.Application.Backtesting;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Backtesting.Tests;

public sealed class MeridianNativeBacktestStudioEngineTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly MeridianNativeBacktestStudioEngine _engine;

    public MeridianNativeBacktestStudioEngineTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), $"meridian-backtest-studio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataRoot);

        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        var backtestEngine = new BacktestEngine(NullLogger<BacktestEngine>.Instance, catalog);
        _engine = new MeridianNativeBacktestStudioEngine(backtestEngine, NullLogger<MeridianNativeBacktestStudioEngine>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public async Task StartAsync_CompletesAndReturnsCanonicalResult()
    {
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), 185m);

        var request = new BacktestStudioRunRequest(
            StrategyId: "native-1",
            StrategyName: "NoOp",
            Engine: Contracts.Workstation.StrategyRunEngine.MeridianNative,
            NativeRequest: new BacktestRequest(
                From: new DateOnly(2024, 1, 2),
                To: new DateOnly(2024, 1, 2),
                DataRoot: _dataRoot),
            Strategy: new NoOpBacktestStrategy());

        var handle = await _engine.StartAsync(request, CancellationToken.None);
        var status = await _engine.GetStatusAsync(handle.EngineRunHandle, CancellationToken.None);
        var result = await _engine.GetCanonicalResultAsync(handle.EngineRunHandle, CancellationToken.None);

        handle.RunId.Should().NotBeNullOrWhiteSpace();
        status.RunId.Should().Be(handle.RunId);
        result.EngineMetadata.Should().NotBeNull();
        result.EngineMetadata!.EngineId.Should().Be("MeridianNative");
        result.TotalEventsProcessed.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task StartAsync_WhenStartTokenIsCanceledAfterScheduling_RunStillCompletes()
    {
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), 185m);

        using var cts = new CancellationTokenSource();
        var request = new BacktestStudioRunRequest(
            StrategyId: "native-decoupled-cancel",
            StrategyName: "NoOp",
            Engine: StrategyRunEngine.MeridianNative,
            NativeRequest: new BacktestRequest(
                From: new DateOnly(2024, 1, 2),
                To: new DateOnly(2024, 1, 2),
                DataRoot: _dataRoot),
            Strategy: new NoOpBacktestStrategy());

        var handle = await _engine.StartAsync(request, cts.Token);
        cts.Cancel();

        var result = await _engine.GetCanonicalResultAsync(handle.EngineRunHandle, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var status = await _engine.GetStatusAsync(handle.EngineRunHandle, CancellationToken.None);

        result.TotalEventsProcessed.Should().BeGreaterThan(0);
        status.Status.Should().Be(StrategyRunStatus.Completed);
    }

    [Fact]
    public async Task CancelAsync_WhenRunIsInFlight_TransitionsToCancelled()
    {
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), 185m);

        var blockingAdjuster = new BlockingCorporateActionAdjustmentService();
        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        var backtestEngine = new BacktestEngine(
            NullLogger<BacktestEngine>.Instance,
            catalog,
            corporateActionAdjustment: blockingAdjuster);
        var engine = new MeridianNativeBacktestStudioEngine(
            backtestEngine,
            NullLogger<MeridianNativeBacktestStudioEngine>.Instance);

        var request = new BacktestStudioRunRequest(
            StrategyId: "native-explicit-cancel",
            StrategyName: "NoOp",
            Engine: StrategyRunEngine.MeridianNative,
            NativeRequest: new BacktestRequest(
                From: new DateOnly(2024, 1, 2),
                To: new DateOnly(2024, 1, 2),
                DataRoot: _dataRoot,
                AdjustForCorporateActions: true),
            Strategy: new NoOpBacktestStrategy());

        var handle = await engine.StartAsync(request, CancellationToken.None);
        await blockingAdjuster.Started.WaitAsync(TimeSpan.FromSeconds(5));

        await engine.CancelAsync(handle.EngineRunHandle, CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await engine.GetCanonicalResultAsync(handle.EngineRunHandle, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));

        var status = await engine.GetStatusAsync(handle.EngineRunHandle, CancellationToken.None);
        status.Status.Should().Be(StrategyRunStatus.Cancelled);
    }

    [Fact]
    public async Task StartAsync_WhenRunCompletes_PreservesResultWithinRetentionAfterActiveRemoval()
    {
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), 185m);

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        var backtestEngine = new BacktestEngine(NullLogger<BacktestEngine>.Instance, catalog);
        var engine = new MeridianNativeBacktestStudioEngine(
            backtestEngine,
            NullLogger<MeridianNativeBacktestStudioEngine>.Instance,
            clock,
            terminalRetention: TimeSpan.FromMinutes(5),
            maxTerminalHistory: 10);

        var request = new BacktestStudioRunRequest(
            StrategyId: "native-retained",
            StrategyName: "NoOp",
            Engine: StrategyRunEngine.MeridianNative,
            NativeRequest: new BacktestRequest(
                From: new DateOnly(2024, 1, 2),
                To: new DateOnly(2024, 1, 2),
                DataRoot: _dataRoot),
            Strategy: new NoOpBacktestStrategy());

        var handle = await engine.StartAsync(request, CancellationToken.None);
        var result = await engine.GetCanonicalResultAsync(handle.EngineRunHandle, CancellationToken.None);
        var status = await engine.GetStatusAsync(handle.EngineRunHandle, CancellationToken.None);

        status.Status.Should().Be(StrategyRunStatus.Completed);
        result.EngineMetadata.Should().NotBeNull();
        result.EngineMetadata!.EngineId.Should().Be("MeridianNative");
    }

    [Fact]
    public async Task StartAsync_PrunesTerminalRunsByTtl()
    {
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), 185m);

        var startTime = DateTimeOffset.UtcNow;
        var clock = new MutableTimeProvider(startTime);
        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        var backtestEngine = new BacktestEngine(NullLogger<BacktestEngine>.Instance, catalog);
        var engine = new MeridianNativeBacktestStudioEngine(
            backtestEngine,
            NullLogger<MeridianNativeBacktestStudioEngine>.Instance,
            clock,
            terminalRetention: TimeSpan.FromSeconds(1),
            maxTerminalHistory: 10);

        var request = new BacktestStudioRunRequest(
            StrategyId: "native-ttl",
            StrategyName: "NoOp",
            Engine: StrategyRunEngine.MeridianNative,
            NativeRequest: new BacktestRequest(
                From: new DateOnly(2024, 1, 2),
                To: new DateOnly(2024, 1, 2),
                DataRoot: _dataRoot),
            Strategy: new NoOpBacktestStrategy());

        var handle = await engine.StartAsync(request, CancellationToken.None);
        await engine.GetCanonicalResultAsync(handle.EngineRunHandle, CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(2));

        await WaitForRunPrunedAsync(engine, handle.EngineRunHandle, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StartAsync_PrunesTerminalRunsByMaximumHistory()
    {
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), 185m);

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        var backtestEngine = new BacktestEngine(NullLogger<BacktestEngine>.Instance, catalog);
        var engine = new MeridianNativeBacktestStudioEngine(
            backtestEngine,
            NullLogger<MeridianNativeBacktestStudioEngine>.Instance,
            clock,
            terminalRetention: TimeSpan.FromMinutes(10),
            maxTerminalHistory: 2);

        var handles = new List<BacktestStudioRunHandle>();
        for (var i = 0; i < 3; i++)
        {
            var request = new BacktestStudioRunRequest(
                StrategyId: $"native-cap-{i}",
                StrategyName: "NoOp",
                Engine: StrategyRunEngine.MeridianNative,
                NativeRequest: new BacktestRequest(
                    From: new DateOnly(2024, 1, 2),
                    To: new DateOnly(2024, 1, 2),
                    DataRoot: _dataRoot),
                Strategy: new NoOpBacktestStrategy());

            var handle = await engine.StartAsync(request, CancellationToken.None);
            handles.Add(handle);
            await engine.GetCanonicalResultAsync(handle.EngineRunHandle, CancellationToken.None);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await engine.GetStatusAsync(handles[0].EngineRunHandle, CancellationToken.None));

        var latestStatus = await engine.GetStatusAsync(handles[2].EngineRunHandle, CancellationToken.None);
        latestStatus.Status.Should().Be(StrategyRunStatus.Completed);
    }

    private void WriteBarJsonl(string symbol, DateOnly date, decimal basePrice)
    {
        var symbolDir = Path.Combine(_dataRoot, symbol);
        Directory.CreateDirectory(symbolDir);
        var path = Path.Combine(symbolDir, "bars.jsonl");

        var bar = new HistoricalBar(
            symbol,
            date,
            Open: basePrice,
            High: basePrice + 2m,
            Low: basePrice - 2m,
            Close: basePrice + 1m,
            Volume: 1_000);

        var evt = MarketEvent.HistoricalBar(
            new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            symbol,
            bar);

        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(evt) + Environment.NewLine);
    }

    private sealed class NoOpBacktestStrategy : IBacktestStrategy
    {
        public string Name => "NoOp";

        public void Initialize(IBacktestContext ctx) { }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }

    private static async Task WaitForRunPrunedAsync(
        MeridianNativeBacktestStudioEngine engine,
        string runHandle,
        TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        InvalidOperationException? lastException = null;

        while (sw.Elapsed < timeout)
        {
            try
            {
                await engine.GetStatusAsync(runHandle, CancellationToken.None);
            }
            catch (InvalidOperationException ex)
            {
                lastException = ex;
                break;
            }

            await Task.Delay(1);
        }

        lastException.Should().NotBeNull($"run '{runHandle}' should be pruned after terminal TTL expires");
    }

    private sealed class BlockingCorporateActionAdjustmentService : ICorporateActionAdjustmentService
    {
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public async Task<IReadOnlyList<HistoricalBar>> AdjustAsync(
            IReadOnlyList<HistoricalBar> bars,
            string ticker,
            CancellationToken ct = default)
        {
            _started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return bars;
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
