using System.Text.Json;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.QuantScript.Api;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.QuantScript.Tests;

public sealed class BacktestProxyTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"quant-proxy-{Guid.NewGuid():N}");

    [Fact]
    public void BuildRequest_MapsExecutionCostTimingAndLiquidityControls()
    {
        var proxy = new BacktestProxy(null, new QuantScriptOptions { DefaultDataRoot = "fallback" })
            .WithSymbols("SPY")
            .From(new DateOnly(2024, 1, 2))
            .To(new DateOnly(2024, 1, 31))
            .WithInitialCash(250_000m)
            .WithFillModel("market-impact")
            .WithSlippage(7.5m)
            .WithCommission(BacktestCommissionKind.Percentage, 2m, 0.25m, 25m)
            .WithMarketImpactCoefficient(0.35m)
            .WithMaxParticipationRate(0.08m)
            .WithOrderBookQueueAheadFraction(0.4m)
            .WithFillTiming(FillTiming.SameBar)
            .WithFillConservatism(FillConservatism.Optimistic);

        var request = proxy.BuildRequest();

        request.Symbols.Should().Equal("SPY");
        request.InitialCash.Should().Be(250_000m);
        request.DefaultExecutionModel.Should().Be(ExecutionModel.MarketImpact);
        request.SlippageBasisPoints.Should().Be(7.5m);
        request.CommissionKind.Should().Be(BacktestCommissionKind.Percentage);
        request.CommissionRate.Should().Be(2m);
        request.CommissionMinimum.Should().Be(0.25m);
        request.CommissionMaximum.Should().Be(25m);
        request.MarketImpactCoefficient.Should().Be(0.35m);
        request.MaxParticipationRate.Should().Be(0.08m);
        request.OrderBookQueueAheadFraction.Should().Be(0.4m);
        request.FillTiming.Should().Be(FillTiming.SameBar);
        request.FillConservatism.Should().Be(FillConservatism.Optimistic);
    }

    [Fact]
    public void WithFillModel_UnknownValue_FailsClosed()
    {
        var proxy = new BacktestProxy(null, new QuantScriptOptions());

        var act = () => proxy.WithFillModel("instant-perfect-fill");

        act.Should().Throw<ArgumentException>().WithMessage("*Unknown fill model*");
    }

    [Fact]
    public void WithSlippage_NegativeValue_FailsClosed()
    {
        var proxy = new BacktestProxy(null, new QuantScriptOptions());

        var act = () => proxy.WithSlippage(-0.01m);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*cannot be negative*");
    }

    [Fact]
    public async Task RunAsync_OnFinishedReceivesTheCompletedMatchingResult()
    {
        Directory.CreateDirectory(_dataRoot);
        WriteBar("SPY", new DateOnly(2024, 1, 2));
        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        var engine = new BacktestEngine(NullLogger<BacktestEngine>.Instance, catalog);
        BacktestResult? callbackResult = null;

        var proxy = new BacktestProxy(engine, new QuantScriptOptions())
            .From(new DateOnly(2024, 1, 2))
            .To(new DateOnly(2024, 1, 3))
            .WithSymbols("SPY")
            .WithDataRoot(_dataRoot)
            .OnFinished((_, result) => callbackResult = result);

        var result = await proxy.RunAsync();

        callbackResult.Should().BeSameAs(result);
    }

    [Fact]
    public async Task RunAsync_WhenOnFinishedThrows_DoesNotCaptureFailedRun()
    {
        Directory.CreateDirectory(_dataRoot);
        WriteBar("SPY", new DateOnly(2024, 1, 2));
        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        var engine = new BacktestEngine(NullLogger<BacktestEngine>.Instance, catalog);
        var proxy = new BacktestProxy(engine, new QuantScriptOptions())
            .From(new DateOnly(2024, 1, 2))
            .To(new DateOnly(2024, 1, 3))
            .WithSymbols("SPY")
            .WithDataRoot(_dataRoot)
            .OnFinished((_, _) => throw new InvalidOperationException("finish failed"));

        var act = async () => await proxy.RunAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("finish failed");
        proxy.DrainCapturedResults().Should().BeEmpty();
        proxy.DrainCapturedFills().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenAnotherRunIsActive_FailsClosed()
    {
        Directory.CreateDirectory(_dataRoot);
        WriteBar("SPY", new DateOnly(2024, 1, 2));
        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        var engine = new BacktestEngine(NullLogger<BacktestEngine>.Instance, catalog);
        var enteredBar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proxy = new BacktestProxy(engine, new QuantScriptOptions())
            .From(new DateOnly(2024, 1, 2))
            .To(new DateOnly(2024, 1, 3))
            .WithSymbols("SPY")
            .WithDataRoot(_dataRoot)
            .OnBar((_, _) =>
            {
                enteredBar.TrySetResult();
                releaseBar.Task.GetAwaiter().GetResult();
            });

        var firstRun = proxy.RunAsync();
        await enteredBar.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondRun = async () => await proxy.RunAsync();
        await secondRun.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already has a run in progress*");

        releaseBar.TrySetResult();
        await firstRun;
    }

    [Fact]
    public async Task RunAsync_WhenUniverseIsEmpty_ReturnsEmptyResult()
    {
        Directory.CreateDirectory(_dataRoot);
        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        var engine = new BacktestEngine(NullLogger<BacktestEngine>.Instance, catalog);
        var proxy = new BacktestProxy(engine, new QuantScriptOptions())
            .From(new DateOnly(2024, 1, 2))
            .To(new DateOnly(2024, 1, 3))
            .WithDataRoot(_dataRoot);

        var result = await proxy.RunAsync();

        result.Universe.Should().BeEmpty();
    }

    private void WriteBar(string symbol, DateOnly date)
    {
        var symbolDirectory = Path.Combine(_dataRoot, symbol);
        Directory.CreateDirectory(symbolDirectory);
        var path = Path.Combine(symbolDirectory, $"{symbol}_bars_{date:yyyy-MM-dd}.jsonl");
        var bar = new HistoricalBar(
            symbol,
            date,
            Open: 100m,
            High: 101m,
            Low: 99m,
            Close: 100m,
            Volume: 1_000,
            Source: "test",
            SequenceNumber: 1);
        var marketEvent = MarketEvent.HistoricalBar(
            bar.ToTimestampUtc(),
            symbol,
            bar,
            source: "test",
            seq: 1);

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(marketEvent, MarketDataJsonContext.HighPerformanceOptions) + Environment.NewLine);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }
}
