using FluentAssertions;
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
        await using var orchestrator = new BacktestStudioRunOrchestrator(
            store,
            [engine],
            NullLogger<BacktestStudioRunOrchestrator>.Instance);

        var request = BuildValidStudioRequest("strategy-1", "Mean Reversion") with
        {
            DatasetReference = "dataset:us-equities",
            Parameters = new Dictionary<string, string> { ["lookback"] = "20" },
            SweepId = "sweep-001",
            SweepObjective = "FinalEquity"
        };

        var handle = await orchestrator.StartAsync(request);

        var started = await store.GetLatestRunAsync("strategy-1");
        started.Should().NotBeNull();
        started!.RunId.Should().Be(handle.RunId);
        started.Engine.Should().Be("MeridianNative");
        started.DatasetReference.Should().Be("dataset:us-equities");
        started.ParameterSet.Should().ContainKey("lookback").WhoseValue.Should().Be("20");
        started.SweepId.Should().Be("sweep-001");
        started.SweepObjective.Should().Be("FinalEquity");
        started.SweepDefinitionHash.Should().NotBeNullOrWhiteSpace();
        started.EndedAt.Should().BeNull();

        engine.Complete(handle.EngineRunHandle, BuildResult(request.NativeRequest));

        var completed = await WaitForRunAsync(store, "strategy-1", run => run.EndedAt.HasValue);
        completed.Engine.Should().Be("MeridianNative");
        completed.Metrics.Should().NotBeNull();
        completed.TerminalStatus.Should().BeNull();
    }


    [Fact]
    public async Task StartAsync_IgnoresCallerSuppliedSweepDefinitionHash()
    {
        var store = new StrategyRunStore();
        var engine = new StubBacktestStudioEngine();
        await using var orchestrator = new BacktestStudioRunOrchestrator(
            store,
            [engine],
            NullLogger<BacktestStudioRunOrchestrator>.Instance);

        var request = BuildValidStudioRequest("strategy-sweep-hash", "Momentum") with
        {
            Parameters = new Dictionary<string, string> { ["lookback"] = "10" },
            SweepId = "sweep-abc",
            SweepDefinitionHash = "FORGED-HASH",
            SweepObjective = "FinalEquity"
        };

        await orchestrator.StartAsync(request);

        var started = await store.GetLatestRunAsync("strategy-sweep-hash");
        started.Should().NotBeNull();
        started!.SweepDefinitionHash.Should().NotBe("FORGED-HASH");
    }

    [Fact]
    public async Task StartAsync_RecordsBacktestEvidenceLoopOnRunLineage()
    {
        var store = new StrategyRunStore();
        var engine = new StubBacktestStudioEngine();
        await using var orchestrator = new BacktestStudioRunOrchestrator(
            store,
            [engine],
            NullLogger<BacktestStudioRunOrchestrator>.Instance);

        var request = BuildValidStudioRequest("strategy-evidence-loop", "Evidence Loop") with
        {
            OperatorAcceptanceCriteria =
            [
                "Backtest result links to retained strategy thesis.",
                "Operator reviewed paper-validation promotion boundary."
            ],
            RetainedEvidenceReferences =
            [
                "evidence://research/backtests/strategy-evidence-loop/run-001",
                " evidence://research/backtests/strategy-evidence-loop/run-001 "
            ],
            AccountingRecordReferences =
            [
                "ledger://books/11111111-1111-1111-1111-111111111111/accounts/strategy-evidence-loop"
            ],
            ApprovalReferences =
            [
                "approval://strategy/backtest-evidence-loop"
            ],
            PaperValidationReferences =
            [
                "operations://fund-workflows/22222222-2222-2222-2222-222222222222/events/paper-validation-run-001"
            ],
            GovernedReportReferences =
            [
                "reporting-run://strategy-evidence-loop-run-001/manifest"
            ]
        };

        var handle = await orchestrator.StartAsync(request);

        var started = await store.GetLatestRunAsync("strategy-evidence-loop");
        started.Should().NotBeNull();
        started!.OperatorAcceptanceCriteria.Should().BeEquivalentTo(request.OperatorAcceptanceCriteria);
        started.RetainedEvidenceReferences.Should().ContainSingle("evidence://research/backtests/strategy-evidence-loop/run-001");
        started.AccountingRecordReferences.Should().ContainSingle("ledger://books/11111111-1111-1111-1111-111111111111/accounts/strategy-evidence-loop");
        started.ApprovalReferences.Should().ContainSingle("approval://strategy/backtest-evidence-loop");
        started.PaperValidationReferences.Should().ContainSingle("operations://fund-workflows/22222222-2222-2222-2222-222222222222/events/paper-validation-run-001");
        started.GovernedReportReferences.Should().ContainSingle("reporting-run://strategy-evidence-loop-run-001/manifest");

        engine.Complete(handle.EngineRunHandle, BuildResult(request.NativeRequest));

        var completed = await WaitForRunAsync(store, "strategy-evidence-loop", run => run.EndedAt.HasValue);
        completed.OperatorAcceptanceCriteria.Should().BeEquivalentTo(request.OperatorAcceptanceCriteria);
        completed.RetainedEvidenceReferences.Should().ContainSingle("evidence://research/backtests/strategy-evidence-loop/run-001");
        completed.AccountingRecordReferences.Should().ContainSingle("ledger://books/11111111-1111-1111-1111-111111111111/accounts/strategy-evidence-loop");
        completed.ApprovalReferences.Should().ContainSingle("approval://strategy/backtest-evidence-loop");
        completed.PaperValidationReferences.Should().ContainSingle("operations://fund-workflows/22222222-2222-2222-2222-222222222222/events/paper-validation-run-001");
        completed.GovernedReportReferences.Should().ContainSingle("reporting-run://strategy-evidence-loop-run-001/manifest");
    }

    [Theory]
    [InlineData("identity", "nonblank strategy identity")]
    [InlineData("criterion", "nonblank operator acceptance criterion")]
    [InlineData("reference", "retained evidence, accounting, approval, paper-validation, or governed-report reference")]
    public async Task StartAsync_InvalidEvidenceRequest_RejectsBeforeEngineOrPersistence(
        string invalidField,
        string expectedMessage)
    {
        var store = new StrategyRunStore();
        var engine = new StubBacktestStudioEngine();
        await using var orchestrator = new BacktestStudioRunOrchestrator(
            store,
            [engine],
            NullLogger<BacktestStudioRunOrchestrator>.Instance);
        var request = invalidField switch
        {
            "identity" => BuildValidStudioRequest("strategy-invalid", "Invalid") with { StrategyId = " " },
            "criterion" => BuildValidStudioRequest("strategy-invalid", "Invalid") with
            {
                OperatorAcceptanceCriteria = [" ", "\t"]
            },
            "reference" => BuildValidStudioRequest("strategy-invalid", "Invalid") with
            {
                RetainedEvidenceReferences = [" ", "\t"],
                AccountingRecordReferences = [],
                ApprovalReferences = [],
                PaperValidationReferences = [],
                GovernedReportReferences = []
            },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField), invalidField, null)
        };

        var action = () => orchestrator.StartAsync(request);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{expectedMessage}*");
        engine.StartCallCount.Should().Be(0);
        (await store.QueryRunsAsync(new StrategyRunRepositoryQuery(Limit: 10))).Should().BeEmpty();
    }

    [Theory]
    [InlineData("malformed", "retained evidence")]
    [InlineData("retained-mismatch", "retained evidence")]
    [InlineData("accounting-mismatch", "accounting record")]
    [InlineData("approval-mismatch", "approval")]
    [InlineData("paper-validation-mismatch", "paper-validation")]
    [InlineData("governed-report-mismatch", "governed-report")]
    public async Task StartAsync_MalformedOrMismatchedReference_RejectsBeforeEngineOrPersistence(
        string invalidField,
        string expectedCategory)
    {
        var store = new StrategyRunStore();
        var engine = new StubBacktestStudioEngine();
        await using var orchestrator = new BacktestStudioRunOrchestrator(
            store,
            [engine],
            NullLogger<BacktestStudioRunOrchestrator>.Instance);
        var validRequest = BuildValidStudioRequest("strategy-invalid-reference", "Invalid Reference");
        var request = invalidField switch
        {
            "malformed" => validRequest with
            {
                RetainedEvidenceReferences = ["not-a-stable-reference"]
            },
            "retained-mismatch" => validRequest with
            {
                RetainedEvidenceReferences = ["approval://strategy-runs/strategy-invalid-reference"]
            },
            "accounting-mismatch" => validRequest with
            {
                AccountingRecordReferences = ["evidence://strategy-runs/strategy-invalid-reference"]
            },
            "approval-mismatch" => validRequest with
            {
                ApprovalReferences = ["ledger://books/strategy-runs/strategy-invalid-reference"]
            },
            "paper-validation-mismatch" => validRequest with
            {
                PaperValidationReferences = ["reporting-run://strategy-invalid-reference/manifest"]
            },
            "governed-report-mismatch" => validRequest with
            {
                GovernedReportReferences = ["workflow://fund/strategy-invalid-reference"]
            },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField), invalidField, null)
        };

        var action = () => orchestrator.StartAsync(request);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*Every {expectedCategory} reference must be a stable absolute URI*");
        engine.StartCallCount.Should().Be(0);
        (await store.QueryRunsAsync(new StrategyRunRepositoryQuery(Limit: 10))).Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_WhenEngineFails_RecordsFailedRun()
    {
        var store = new StrategyRunStore();
        var engine = new StubBacktestStudioEngine();
        await using var orchestrator = new BacktestStudioRunOrchestrator(
            store,
            [engine],
            NullLogger<BacktestStudioRunOrchestrator>.Instance);

        var request = BuildValidStudioRequest("strategy-2", "Breakout");

        var handle = await orchestrator.StartAsync(request);
        engine.Fail(handle.EngineRunHandle, new InvalidOperationException("boom"));

        var failed = await WaitForRunAsync(
            store,
            "strategy-2",
            run => run.TerminalStatus == StrategyRunStatus.Failed);

        failed.EndedAt.Should().NotBeNull();
        failed.TerminalStatus.Should().Be(StrategyRunStatus.Failed);
    }

    [Fact]
    public async Task StartAsync_WhenCallerCancelsAfterScheduling_RunCanStillComplete()
    {
        var store = new StrategyRunStore();
        var engine = new StubBacktestStudioEngine();
        await using var orchestrator = new BacktestStudioRunOrchestrator(
            store,
            [engine],
            NullLogger<BacktestStudioRunOrchestrator>.Instance);

        using var cts = new CancellationTokenSource();
        var request = BuildValidStudioRequest("strategy-3", "Momentum");

        var handle = await orchestrator.StartAsync(request, cts.Token);
        await engine.WaitForMonitorAsync(handle.EngineRunHandle);

        cts.Cancel();
        engine.Complete(handle.EngineRunHandle, BuildResult(request.NativeRequest));

        var completed = await WaitForRunAsync(
            store,
            "strategy-3",
            run => run.EndedAt.HasValue);

        completed.RunId.Should().Be(handle.RunId);
        completed.TerminalStatus.Should().BeNull();
    }

    [Fact]
    public async Task CancelAsync_WhenCalled_RecordsCancelledRun()
    {
        var store = new StrategyRunStore();
        var engine = new StubBacktestStudioEngine();
        await using var orchestrator = new BacktestStudioRunOrchestrator(
            store,
            [engine],
            NullLogger<BacktestStudioRunOrchestrator>.Instance);

        var request = BuildValidStudioRequest("strategy-cancel", "Momentum");

        var handle = await orchestrator.StartAsync(request, CancellationToken.None);
        await engine.WaitForMonitorAsync(handle.EngineRunHandle);

        await orchestrator.CancelAsync(handle.RunId, CancellationToken.None);

        var cancelled = await WaitForRunAsync(
            store,
            "strategy-cancel",
            run => run.TerminalStatus == StrategyRunStatus.Cancelled);

        cancelled.RunId.Should().Be(handle.RunId);
        cancelled.EndedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DisposeAsync_CancelsInFlightMonitor()
    {
        var store = new StrategyRunStore();
        var engine = new StubBacktestStudioEngine();
        var orchestrator = new BacktestStudioRunOrchestrator(
            store,
            [engine],
            NullLogger<BacktestStudioRunOrchestrator>.Instance);

        var request = BuildValidStudioRequest("strategy-4", "Carry");

        var handle = await orchestrator.StartAsync(request);
        await engine.WaitForMonitorAsync(handle.EngineRunHandle);

        await orchestrator.DisposeAsync();

        (await engine.WaitForMonitorCancellationAsync(handle.EngineRunHandle)).Should().BeTrue();
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

    private static BacktestStudioRunRequest BuildValidStudioRequest(string strategyId, string strategyName) =>
        new(
            StrategyId: strategyId,
            StrategyName: strategyName,
            Engine: StrategyRunEngine.MeridianNative,
            NativeRequest: BuildRequest(),
            OperatorAcceptanceCriteria: ["Operator reviewed the retained backtest evidence."],
            RetainedEvidenceReferences: [$"evidence://strategy-runs/{strategyId}"]);

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
        private readonly Dictionary<string, TaskCompletionSource<bool>> _monitorStarted = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskCompletionSource<bool>> _monitorCancelled = new(StringComparer.Ordinal);

        public StrategyRunEngine Engine => StrategyRunEngine.MeridianNative;

        public int StartCallCount { get; private set; }

        public Task<BacktestStudioRunHandle> StartAsync(BacktestStudioRunRequest request, CancellationToken ct)
        {
            StartCallCount++;
            var runId = Guid.NewGuid().ToString("N");
            var engineRunHandle = Guid.NewGuid().ToString("N");

            _results[engineRunHandle] = new TaskCompletionSource<BacktestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _monitorStarted[engineRunHandle] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _monitorCancelled[engineRunHandle] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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

        public async Task<BacktestResult> GetCanonicalResultAsync(string runHandle, CancellationToken ct)
        {
            _monitorStarted[runHandle].TrySetResult(true);

            try
            {
                return await _results[runHandle].Task.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                var status = _statuses[runHandle];
                _statuses[runHandle] = status with { Status = StrategyRunStatus.Cancelled, Message = "Cancelled" };
                _monitorCancelled[runHandle].TrySetResult(true);
                throw;
            }
        }

        public Task CancelAsync(string runHandle, CancellationToken ct)
        {
            var status = _statuses[runHandle];
            _statuses[runHandle] = status with { Status = StrategyRunStatus.Cancelled, Message = "Cancelled" };
            _results[runHandle].TrySetCanceled(ct.IsCancellationRequested ? ct : new CancellationToken(canceled: true));
            return Task.CompletedTask;
        }

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

        public Task WaitForMonitorAsync(string runHandle) => _monitorStarted[runHandle].Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task<bool> WaitForMonitorCancellationAsync(string runHandle) => _monitorCancelled[runHandle].Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
