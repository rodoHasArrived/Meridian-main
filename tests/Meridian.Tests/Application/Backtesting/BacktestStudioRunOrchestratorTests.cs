using FluentAssertions;
using Meridian.Application.Backtesting;
using Meridian.Backtesting;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Application.Backtesting;

public sealed class BacktestStudioRunOrchestratorTests
{
    [Fact]
    public async Task StartAsync_RecordsInitialAndCompletedRunEntries()
    {
        var store = new StrategyRunStore();
        var engine = new StubBacktestStudioEngine();
        var orchestrator = new BacktestStudioRunOrchestrator(
            store,
            [engine],
            NullLogger<BacktestStudioRunOrchestrator>.Instance);

        var request = new BacktestStudioRunRequest(
            StrategyId: "strategy-1",
            StrategyName: "Mean Reversion",
            Engine: StrategyRunEngine.MeridianNative,
            NativeRequest: BuildRequest(),
            DatasetReference: "dataset:us-equities",
            Parameters: new Dictionary<string, string> { ["lookback"] = "20" });

        var handle = await orchestrator.StartAsync(request);

        var started = await store.GetLatestRunAsync("strategy-1");
        started.Should().NotBeNull();
        started!.RunId.Should().Be(handle.RunId);
        started.Engine.Should().Be("MeridianNative");
        started.DatasetReference.Should().Be("dataset:us-equities");
        started.ParameterSet.Should().ContainKey("lookback").WhoseValue.Should().Be("20");
        started.EndedAt.Should().BeNull();

        engine.Complete(handle.EngineRunHandle, BuildResult(request.NativeRequest));

        var completed = await WaitForRunAsync(store, "strategy-1", run => run.EndedAt.HasValue);
        completed.Engine.Should().Be("MeridianNative");
        completed.Metrics.Should().NotBeNull();
        completed.TerminalStatus.Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_WhenEngineFails_RecordsFailedRun()
    {
        var store = new StrategyRunStore();
        var engine = new StubBacktestStudioEngine();
        var orchestrator = new BacktestStudioRunOrchestrator(
            store,
            [engine],
            NullLogger<BacktestStudioRunOrchestrator>.Instance);

        var request = new BacktestStudioRunRequest(
            StrategyId: "strategy-2",
            StrategyName: "Breakout",
            Engine: StrategyRunEngine.MeridianNative,
            NativeRequest: BuildRequest());

        var handle = await orchestrator.StartAsync(request);
        engine.Fail(handle.EngineRunHandle, new InvalidOperationException("boom"));

        var failed = await WaitForRunAsync(
            store,
            "strategy-2",
            run => run.TerminalStatus == StrategyRunStatus.Failed);

        failed.EndedAt.Should().NotBeNull();
        failed.TerminalStatus.Should().Be(StrategyRunStatus.Failed);
    }

    private static async Task<StrategyRunEntry> WaitForRunAsync(
        StrategyRunStore store,
        string strategyId,
        Func<StrategyRunEntry, bool> predicate)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var run = await store.GetLatestRunAsync(strategyId);
            if (run is not null && predicate(run))
                return run;

            await Task.Delay(20);
        }

        throw new Xunit.Sdk.XunitException($"Timed out waiting for run '{strategyId}' to satisfy the predicate.");
    }

    private static BacktestRequest BuildRequest() =>
        new(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: "data");

    private static BacktestResult BuildResult(BacktestRequest request) =>
        new(
            Request: request,
            Universe: new HashSet<string>(),
            Snapshots: [],
            CashFlows: [],
            Fills: [],
            Metrics: new BacktestMetrics(
                InitialCapital: 100_000m,
                FinalEquity: 101_000m,
                GrossPnl: 1_100m,
                NetPnl: 1_000m,
                TotalReturn: 0.01m,
                AnnualizedReturn: 0.01m,
                SharpeRatio: 1.1,
                SortinoRatio: 1.0,
                CalmarRatio: 0.9,
                MaxDrawdown: 500m,
                MaxDrawdownPercent: 0.005m,
                MaxDrawdownRecoveryDays: 1,
                ProfitFactor: 1.3,
                WinRate: 0.55,
                TotalTrades: 2,
                WinningTrades: 1,
                LosingTrades: 1,
                TotalCommissions: 5m,
                TotalMarginInterest: 0m,
                TotalShortRebates: 0m,
                Xirr: 0.01,
                SymbolAttribution: new Dictionary<string, SymbolAttribution>()),
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(1),
            TotalEventsProcessed: 10L);

    private sealed class StubBacktestStudioEngine : IBacktestStudioEngine
    {
        private readonly Dictionary<string, TaskCompletionSource<BacktestResult>> _results = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BacktestStudioRunStatus> _statuses = new(StringComparer.Ordinal);

        public StrategyRunEngine Engine => StrategyRunEngine.MeridianNative;

        public Task<BacktestStudioRunHandle> StartAsync(BacktestStudioRunRequest request, CancellationToken ct)
        {
            var runId = Guid.NewGuid().ToString("N");
            var engineRunHandle = Guid.NewGuid().ToString("N");

            _results[engineRunHandle] = new TaskCompletionSource<BacktestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _statuses[engineRunHandle] = new BacktestStudioRunStatus(
                runId,
                StrategyRunStatus.Running,
                0d,
                DateTimeOffset.UtcNow,
                EstimatedCompletionAt: null,
                Message: "Running");

            return Task.FromResult(new BacktestStudioRunHandle(runId, engineRunHandle, Engine));
        }

        public Task<BacktestStudioRunStatus> GetStatusAsync(string runHandle, CancellationToken ct) =>
            Task.FromResult(_statuses[runHandle]);

        public Task<BacktestResult> GetCanonicalResultAsync(string runHandle, CancellationToken ct) =>
            _results[runHandle].Task.WaitAsync(ct);

        public void Complete(string runHandle, BacktestResult result)
        {
            var status = _statuses[runHandle];
            _statuses[runHandle] = status with { Status = StrategyRunStatus.Completed, Progress = 1d, Message = "Completed" };
            _results[runHandle].TrySetResult(result);
        }

        public void Fail(string runHandle, Exception exception)
        {
            var status = _statuses[runHandle];
            _statuses[runHandle] = status with { Status = StrategyRunStatus.Failed, Message = exception.Message };
            _results[runHandle].TrySetException(exception);
        }
    }
}
