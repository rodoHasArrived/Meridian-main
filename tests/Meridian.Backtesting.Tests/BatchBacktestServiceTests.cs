using Microsoft.Extensions.Logging.Abstractions;
using Meridian.Backtesting;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;

namespace Meridian.Backtesting.Tests;

public sealed class BatchBacktestServiceTests
{
    [Fact]
    public async Task RunBatchAsync_UsesStrategyFactoryPerParameterSet_AndProducesDifferentiatedResults()
    {
        var service = new BatchBacktestService(
            NullLogger<BatchBacktestService>.Instance,
            _ => null!,
            (_, runRequest, strategy, _) => Task.FromResult(CreateResult(runRequest, ((ParameterizedTestStrategy)strategy).Weight)));

        var request = new BatchBacktestRequest
        {
            BaseRequest = CreateBaseRequest(),
            ParameterGrid =
            [
                new Dictionary<string, object> { ["Weight"] = 1m, [nameof(BacktestRequest.InitialCash)] = 1_000m },
                new Dictionary<string, object> { ["Weight"] = 3m, [nameof(BacktestRequest.InitialCash)] = 2_000m }
            ],
            MaxConcurrency = 1,
            StrategyDescriptor = "ParameterizedTestStrategy",
            StrategyFactory = static parameters => new ParameterizedTestStrategy(Convert.ToDecimal(parameters["Weight"]))
        };

        var summary = await service.RunBatchAsync(request, progress: null!, CancellationToken.None);

        summary.Runs.Should().HaveCount(2);
        summary.Runs.Select(run => run.Result!.Request.InitialCash).Should().BeEquivalentTo([1_000m, 2_000m]);
        summary.Runs.Select(run => run.Result!.Metrics.NetPnl).Should().BeEquivalentTo([1_000m, 6_000m]);
    }

    private static BacktestRequest CreateBaseRequest()
        => new(DateOnly.Parse("2025-01-01"), DateOnly.Parse("2025-01-10"), ["SPY"], 500m, 0.08, DataRoot: "/tmp");

    private static BacktestResult CreateResult(BacktestRequest request, decimal weight)
    {
        var netPnl = request.InitialCash * weight;
        var metrics = new BacktestMetrics(
            request.InitialCash,
            request.InitialCash + netPnl,
            netPnl,
            netPnl,
            request.InitialCash == 0 ? 0 : netPnl / request.InitialCash,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            new Dictionary<string, SymbolAttribution>());

        return new BacktestResult(request, new HashSet<string>(), [], [], [], metrics, new BacktestLedger(), TimeSpan.Zero, 0);
    }

    private sealed class ParameterizedTestStrategy(decimal weight) : IBacktestStrategy
    {
        public decimal Weight { get; } = weight;
        public string Name => $"Weighted-{Weight}";
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
