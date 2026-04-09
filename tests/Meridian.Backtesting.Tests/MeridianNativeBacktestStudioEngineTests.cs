using FluentAssertions;
using Meridian.Application.Backtesting;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
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
        result.EngineMetadata.EngineId.Should().Be("MeridianNative");
        result.TotalEventsProcessed.Should().BeGreaterThan(0);
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
}
