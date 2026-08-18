using System.Diagnostics;
using Meridian.QuantScript.Api;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Plotting;
using Meridian.QuantScript.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Meridian.QuantScript.Tests;

public sealed class ScriptRunnerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ScriptRunner BuildRunner(
        IQuantScriptCompiler? compiler = null,
        IQuantDataContext? dataContext = null,
        PlotQueue? plotQueue = null,
        int runTimeoutSeconds = 10,
        long maxMemoryDeltaBytes = 384L * 1024 * 1024,
        long maxWorkerMemoryBytes = 512L * 1024 * 1024,
        int maxWorkerCpuTimeSeconds = 60,
        int maxWorkerProcessCount = 1,
        int maxConcurrentWorkers = 2,
        int maxQueuedWorkerRequests = 8,
        int workerQueueWaitTimeoutMilliseconds = 30_000,
        int maxHostRpcCallsPerRun = 128,
        int maxHostRpcRecordsPerRun = 100_000,
        int maxHostRpcResponseBytesPerRun = 8 * 1024 * 1024,
        int maxHostRpcSymbolsPerRun = 32,
        int maxHostRpcDateRangeDays = 3_660,
        int maxRunElapsedMilliseconds = 0,
        int maxOutputItemsPerRun = 0,
        bool enableUnsafeScripts = false,
        int maxWorkerStandardOutputBytes = 64 * 1024,
        int maxWorkerStandardErrorBytes = 64 * 1024,
        string? workerExecutablePath = null,
        IQuantScriptWorkerClient? workerClient = null)
    {
        compiler ??= new RoslynScriptCompiler(
            Options.Create(new QuantScriptOptions()),
            NullLogger<RoslynScriptCompiler>.Instance);

        dataContext ??= new Mock<IQuantDataContext>().Object;

        // BacktestEngine is sealed; pass null since the unit tests don't exercise backtest paths.
        return new ScriptRunner(
            compiler,
            dataContext,
            plotQueue ?? new PlotQueue(),
            Options.Create(new QuantScriptOptions
            {
                RunTimeoutSeconds = runTimeoutSeconds,
                MaxMemoryDeltaBytes = maxMemoryDeltaBytes,
                MaxWorkerMemoryBytes = maxWorkerMemoryBytes,
                MaxWorkerCpuTimeSeconds = maxWorkerCpuTimeSeconds,
                MaxWorkerProcessCount = maxWorkerProcessCount,
                MaxConcurrentWorkers = maxConcurrentWorkers,
                MaxQueuedWorkerRequests = maxQueuedWorkerRequests,
                WorkerQueueWaitTimeoutMilliseconds = workerQueueWaitTimeoutMilliseconds,
                MaxHostRpcCallsPerRun = maxHostRpcCallsPerRun,
                MaxHostRpcRecordsPerRun = maxHostRpcRecordsPerRun,
                MaxHostRpcResponseBytesPerRun = maxHostRpcResponseBytesPerRun,
                MaxHostRpcSymbolsPerRun = maxHostRpcSymbolsPerRun,
                MaxHostRpcDateRangeDays = maxHostRpcDateRangeDays,
                MaxRunElapsedMilliseconds = maxRunElapsedMilliseconds,
                MaxOutputItemsPerRun = maxOutputItemsPerRun,
                EnableUnsafeScripts = enableUnsafeScripts,
                MaxWorkerStandardOutputBytes = maxWorkerStandardOutputBytes,
                MaxWorkerStandardErrorBytes = maxWorkerStandardErrorBytes,
                WorkerExecutablePath = workerExecutablePath
            }),
            NullLogger<ScriptRunner>.Instance,
            null,
            workerClient ?? new QuantScriptWorkerClient(NullLogger<ScriptRunner>.Instance));
    }

    private static IReadOnlyDictionary<string, object?> NoParams =>
        new Dictionary<string, object?>();

    // ── Argument validation ───────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NullOrEmptySource_ThrowsArgumentException()
    {
        var runner = BuildRunner();

        await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync(string.Empty, NoParams));
    }

    // ── Successful execution ──────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_PrintCall_AppearsInConsoleOutput()
    {
        var runner = BuildRunner();
        const string source = "Print(\"hello world\");";

        var result = await runner.RunAsync(source, NoParams);

        result.Success.Should().BeTrue();
        result.ConsoleOutput.Should().Contain("hello world");
    }

    [Fact]
    public async Task RunAsync_PrintMetricCall_AppearsInMetrics()
    {
        var runner = BuildRunner();
        const string source = "PrintMetric(\"Sharpe\", 1.23);";

        var result = await runner.RunAsync(source, NoParams);

        result.Success.Should().BeTrue();
        result.Metrics.Should().Contain(kv => kv.Key == "Sharpe");
    }

    [Fact]
    public async Task RunAsync_ValidScript_ReturnsTiming()
    {
        var runner = BuildRunner();

        var result = await runner.RunAsync("var x = 1 + 1;", NoParams);

        result.Success.Should().BeTrue();
        result.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        result.CompileTime.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task RunAsync_EmptyScript_Succeeds()
    {
        var runner = BuildRunner();
        var result = await runner.RunAsync("// empty", NoParams);
        result.Success.Should().BeTrue();
        result.RuntimeError.Should().BeNull();
    }

    // ── Compilation failure ───────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_SyntaxError_ReturnsFailed_WithDiagnostics()
    {
        var runner = BuildRunner();
        const string source = "int x = \"this is not an int\";";

        var result = await runner.RunAsync(source, NoParams);

        result.Success.Should().BeFalse();
        result.CompilationErrors.Should().NotBeEmpty();
        result.RuntimeError.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_CompilationError_ReturnsFailure()
    {
        var runner = BuildRunner();
        var result = await runner.RunAsync("not valid c# !!!", NoParams);
        result.Success.Should().BeFalse();
        result.CompilationErrors.Should().NotBeEmpty();
    }

    // ── Runtime error ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ThrowingScript_ReturnsFailed_WithRuntimeError()
    {
        var runner = BuildRunner();
        const string source = "throw new System.InvalidOperationException(\"test error\");";

        var result = await runner.RunAsync(source, NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RunAsync_RuntimeException_ReturnsFailure()
    {
        var runner = BuildRunner();
        var result = await runner.RunAsync(
            "throw new System.Exception(\"boom\");",
            NoParams);
        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Contain("boom");
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_PreCanceledToken_ThrowsOperationCanceledException()
    {
        var runner = BuildRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => runner.RunAsync("Print(\"hi\");", NoParams, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_UserCancellationDuringRun_ReturnsCancelledRuntimeError_AndNoCheckpoint()
    {
        var runner = BuildRunner(runTimeoutSeconds: 10);
        using var cts = new CancellationTokenSource();
        var run = runner.RunAsync("while (true) { }", NoParams, cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script cancelled by user.");
        result.Checkpoint.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_TimeoutDuringRun_ReturnsTimeoutRuntimeError_AndNoCheckpoint()
    {
        var runner = BuildRunner(runTimeoutSeconds: 1);
        var result = await runner.RunAsync(
            "while (true) { }",
            NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script timed out.");
        result.Checkpoint.Should().BeNull();
    }

    [Fact]
    public async Task ContinueWithAsync_TimeoutDuringRun_PreservesPreviousCheckpoint_AndReturnsTimeoutRuntimeError()
    {
        var worker = new ScriptedWorkerClient(
            new WorkerExecutionOutcome(
                WorkerCompletionKind.Completed,
                new WorkerScriptRunResult(
                    true,
                    0,
                    0,
                    [],
                    [],
                    null,
                    string.Empty,
                    [],
                    [],
                    [],
                    [],
                    []),
                0),
            new WorkerExecutionOutcome(WorkerCompletionKind.TimedOut, null, 0));
        var runner = BuildRunner(workerClient: worker);
        var first = await runner.RunAsync("var x = 41;", NoParams);
        first.Checkpoint.Should().NotBeNull();

        var result = await runner.ContinueWithAsync(
            "while (true) { }",
            first.Checkpoint!,
            NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script timed out.");
        result.Checkpoint.Should().BeSameAs(first.Checkpoint);
        worker.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_ElapsedLimitExceeded_ReturnsRuntimeLimitDiagnostic()
    {
        var runner = BuildRunner(maxRunElapsedMilliseconds: 1);

        var result = await runner.RunAsync("System.Threading.Thread.Sleep(25);", NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script exceeded configured elapsed-time limit.");
        result.RuntimeDiagnostics.Should().ContainSingle(x => x.Severity == "RuntimeLimit");
    }

    [Fact]
    public async Task RunAsync_OutputLimitExceeded_ReturnsRuntimeLimitDiagnostic()
    {
        var runner = BuildRunner(maxOutputItemsPerRun: 1);

        var result = await runner.RunAsync("PrintMetric(\"One\", 1); PrintMetric(\"Two\", 2);", NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script exceeded configured output-item limit.");
        result.RuntimeDiagnostics.Should().Contain(x => x.Message.Contains("Output item limit exceeded"));
    }

    [Fact]
    public async Task RunAsync_MemoryLimitExceeded_KillsWorkerAndReturnsChildPeakDelta()
    {
        const long memoryLimit = 32L * 1024 * 1024;
        var runner = BuildRunner(
            runTimeoutSeconds: 15,
            maxMemoryDeltaBytes: memoryLimit);

        var result = await runner.RunAsync(
            "var bytes = new byte[96 * 1024 * 1024]; System.Array.Fill(bytes, (byte)1); while (true) { }",
            NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script exceeded configured worker-tree memory limit.");
        result.PeakMemoryBytes.Should().BeGreaterThan(memoryLimit);
        result.RuntimeDiagnostics.Should().ContainSingle(x =>
            x.Severity == "RuntimeLimit" && x.Message.Contains("Worker-tree memory limit exceeded"));
    }

    [Fact]
    public async Task RunAsync_CpuTimeLimitExceeded_KillsWorkerTree()
    {
        var runner = BuildRunner(
            runTimeoutSeconds: 15,
            maxWorkerCpuTimeSeconds: 1);

        var result = await runner.RunAsync("while (true) { }", NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script exceeded configured worker-tree CPU limit.");
        result.RuntimeDiagnostics.Should().ContainSingle(item =>
            item.Severity == "RuntimeLimit" && item.Message.Contains("CPU-time limit exceeded"));
    }

    [Fact]
    public async Task RunAsync_StdoutLimitExceeded_KillsWorker()
    {
        var runner = BuildRunner(maxWorkerStandardOutputBytes: 512);

        var result = await runner.RunAsync(
            "System.Console.Write(new string('x', 4096)); while (true) { }",
            NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script worker exceeded configured standard-output limit.");
    }

    [Fact]
    public async Task RunAsync_StderrLimitExceeded_KillsWorker()
    {
        var runner = BuildRunner(maxWorkerStandardErrorBytes: 512);

        var result = await runner.RunAsync(
            "System.Console.Error.Write(new string('x', 4096)); while (true) { }",
            NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script worker exceeded configured standard-error limit.");
    }

    [Fact]
    public async Task RunAsync_UnavailableWorker_FailsClosedWithoutInProcessFallback()
    {
        var missingWorker = Path.Combine(
            Path.GetTempPath(),
            $"missing-quant-worker-{Guid.NewGuid():N}",
            "Meridian.QuantScript.Worker.exe");
        var runner = BuildRunner(workerExecutablePath: missingWorker);

        var result = await runner.RunAsync("Print(42);", NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script worker containment is unavailable.");
        result.RuntimeDiagnostics.Should().ContainSingle(x => x.Severity == "RuntimeIsolation");
    }

    [Fact]
    public async Task RunAsync_CancellationTerminatesWorkerProcess()
    {
        var markerPath = Path.Combine(Path.GetTempPath(), $"quant-worker-{Guid.NewGuid():N}.pid");
        var sourcePath = markerPath.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var runner = BuildRunner(runTimeoutSeconds: 15, enableUnsafeScripts: true);
        using var cts = new CancellationTokenSource();

        try
        {
            var run = runner.RunAsync(
                $"System.IO.File.WriteAllText(\"{sourcePath}\", System.Environment.ProcessId.ToString()); while (true) {{ }}",
                NoParams,
                cts.Token);
            await WaitForFileAsync(markerPath, TimeSpan.FromSeconds(10));
            var workerProcessId = int.Parse(await File.ReadAllTextAsync(markerPath));

            cts.Cancel();
            var result = await run.WaitAsync(TimeSpan.FromSeconds(5));

            result.RuntimeError.Should().Be("Script cancelled by user.");
            await WaitForProcessExitAsync(workerProcessId, TimeSpan.FromSeconds(3));
        }
        finally
        {
            if (File.Exists(markerPath))
                File.Delete(markerPath);
        }
    }

    [Fact]
    public async Task RunAsync_AdmissionQueueIsFull_RejectsWithoutStartingAnotherWorker()
    {
        var worker = new BlockingWorkerClient();
        var runner = BuildRunner(
            maxConcurrentWorkers: 1,
            maxQueuedWorkerRequests: 0,
            workerClient: worker);

        var first = runner.RunAsync("Print(1);", NoParams);
        await worker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var rejected = await runner.RunAsync("Print(2);", NoParams);

        rejected.Success.Should().BeFalse();
        rejected.RuntimeError.Should().Be("Script worker capacity is currently exhausted.");
        rejected.RuntimeDiagnostics.Should().ContainSingle(item =>
            item.Severity == "RuntimeLimit" && item.Message.Contains("admission queue"));
        worker.CallCount.Should().Be(1);

        worker.Release.TrySetResult();
        (await first).Success.Should().BeTrue();
        worker.MaximumActive.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_HostRpcDateRangeQuotaRejectsBeforeProviderMaterialization()
    {
        var dataContext = new Mock<IQuantDataContext>(MockBehavior.Strict);
        var runner = BuildRunner(dataContext: dataContext.Object, maxHostRpcDateRangeDays: 5);

        var result = await runner.RunAsync(
            "Data.Prices(\"SPY\", new System.DateTime(2024,1,1), new System.DateTime(2024,2,1));",
            NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script exceeded configured host-data RPC limits.");
        result.RuntimeDiagnostics.Should().ContainSingle(item => item.Message.Contains("date-range quota"));
        dataContext.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_HostRpcCallQuotaIsAggregateAndStopsFurtherProviderCalls()
    {
        var dataContext = new Mock<IQuantDataContext>(MockBehavior.Strict);
        dataContext
            .Setup(context => context.PricesAsync(
                "SPY",
                new DateOnly(2024, 1, 2),
                new DateOnly(2024, 1, 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PriceSeries(
                "SPY",
                [new PriceBar(new DateOnly(2024, 1, 2), 100, 101, 99, 100, 1_000)]));
        var runner = BuildRunner(
            dataContext: dataContext.Object,
            runTimeoutSeconds: 30,
            maxHostRpcCallsPerRun: 1);
        const string source = """
            Data.Prices("SPY", new System.DateOnly(2024,1,2), new System.DateOnly(2024,1,2));
            Data.Prices("SPY", new System.DateOnly(2024,1,2), new System.DateOnly(2024,1,2));
            """;

        var result = await runner.RunAsync(source, NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script exceeded configured host-data RPC limits.");
        result.RuntimeDiagnostics.Should().ContainSingle(item => item.Message.Contains("call quota"));
        dataContext.Verify(context => context.PricesAsync(
            "SPY",
            new DateOnly(2024, 1, 2),
            new DateOnly(2024, 1, 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_HostRpcDistinctSymbolQuotaStopsUnadmittedSymbolBeforeProviderCall()
    {
        var dataContext = new Mock<IQuantDataContext>(MockBehavior.Strict);
        dataContext
            .Setup(context => context.PricesAsync(
                "SPY",
                new DateOnly(2024, 1, 2),
                new DateOnly(2024, 1, 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PriceSeries(
                "SPY",
                [new PriceBar(new DateOnly(2024, 1, 2), 100, 101, 99, 100, 1_000)]));
        var runner = BuildRunner(
            dataContext: dataContext.Object,
            runTimeoutSeconds: 30,
            maxHostRpcSymbolsPerRun: 1);
        const string source = """
            Data.Prices("SPY", new System.DateOnly(2024,1,2), new System.DateOnly(2024,1,2));
            Data.Prices("QQQ", new System.DateOnly(2024,1,2), new System.DateOnly(2024,1,2));
            """;

        var result = await runner.RunAsync(source, NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script exceeded configured host-data RPC limits.");
        result.RuntimeDiagnostics.Should().ContainSingle(item => item.Message.Contains("distinct-symbol quota"));
        dataContext.Verify(context => context.PricesAsync(
            "SPY",
            new DateOnly(2024, 1, 2),
            new DateOnly(2024, 1, 2),
            It.IsAny<CancellationToken>()), Times.Once);
        dataContext.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_HostRpcRecordQuotaRejectsBeforeEnumeratingOversizedResponse()
    {
        var oversized = new NonEnumeratingTradeList(2);
        var dataContext = new Mock<IQuantDataContext>(MockBehavior.Strict);
        dataContext
            .Setup(context => context.TradesAsync("SPY", new DateOnly(2024, 1, 2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(oversized);
        var runner = BuildRunner(
            dataContext: dataContext.Object,
            runTimeoutSeconds: 30,
            maxHostRpcRecordsPerRun: 1);

        var result = await runner.RunAsync(
            "Data.Trades(\"SPY\", new System.DateOnly(2024,1,2));",
            NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script exceeded configured host-data RPC limits.");
        result.RuntimeDiagnostics.Should().ContainSingle(item => item.Message.Contains("record quota"));
        oversized.EnumerationAttempts.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_HostRpcAggregateByteQuotaStopsBoundedSerialization()
    {
        var bars = Enumerable.Range(0, 128)
            .Select(index => new PriceBar(
                new DateOnly(2024, 1, 1).AddDays(index),
                100 + index,
                101 + index,
                99 + index,
                100.5m + index,
                1_000_000))
            .ToArray();
        var dataContext = new Mock<IQuantDataContext>(MockBehavior.Strict);
        dataContext
            .Setup(context => context.PricesAsync(
                "SPY",
                new DateOnly(2024, 1, 1),
                new DateOnly(2024, 5, 7),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PriceSeries("SPY", bars));
        var runner = BuildRunner(
            dataContext: dataContext.Object,
            runTimeoutSeconds: 30,
            maxHostRpcResponseBytesPerRun: 1_024);

        var result = await runner.RunAsync(
            "Data.Prices(\"SPY\", new System.DateTime(2024,1,1), new System.DateTime(2024,5,7));",
            NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Be("Script exceeded configured host-data RPC limits.");
        result.RuntimeDiagnostics.Should().ContainSingle(item => item.Message.Contains("response-byte quota"));
    }

    [Fact]
    public async Task RunAsync_WindowsJobCloseTerminatesAllowedDescendant()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var markerPath = Path.Combine(Path.GetTempPath(), $"quant-descendant-{Guid.NewGuid():N}.txt");
        var escapedMarker = markerPath.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var runner = BuildRunner(
            runTimeoutSeconds: 30,
            enableUnsafeScripts: true,
            maxWorkerProcessCount: 2);
        var source = $$"""
            var command = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.System),
                "cmd.exe");
            var start = new System.Diagnostics.ProcessStartInfo(command)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("ping -n 3 127.0.0.1 > nul & echo escaped>\"{{escapedMarker}}\"");
            _ = System.Diagnostics.Process.Start(start);
            """;

        try
        {
            var result = await runner.RunAsync(source, NoParams);
            result.Success.Should().BeTrue(
                "the descendant must start before job-close containment is exercised; runtime={0}; compilation={1}",
                result.RuntimeError,
                string.Join(" | ", result.CompilationErrors.Select(item => item.Message)));

            await Task.Delay(TimeSpan.FromSeconds(3));
            File.Exists(markerPath).Should().BeFalse("closing the worker Job Object must terminate descendants");
        }
        finally
        {
            if (File.Exists(markerPath))
                File.Delete(markerPath);
        }
    }

    // ── Data access ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_DataGetPrices_ReturnsNonEmptySeries()
    {
        var mockCtx = new Mock<IQuantDataContext>();
        mockCtx.Setup(c => c.PricesAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PriceSeries("SPY", [new PriceBar(new DateOnly(2024, 1, 2), 480, 482, 479, 481, 1_000_000)]));

        var runner = BuildRunner(dataContext: mockCtx.Object);
        const string source = """
            var prices = Data.Prices("SPY", new System.DateTime(2024,1,1), new System.DateTime(2024,2,1));
            Print($"Bars: {prices.Count}");
            """;

        var result = await runner.RunAsync(source, NoParams);

        result.Success.Should().BeTrue();
        result.ConsoleOutput.Should().Contain("Bars:");
    }

    // ── Parameter passing ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ParamOverride_UsesSuppliedValue()
    {
        var runner = BuildRunner();
        const string source = """
            var lookback = Param<int>("Lookback", 20);
            Print($"Lookback={lookback}");
            """;
        var parameters = new Dictionary<string, object?> { ["Lookback"] = 50 };

        var result = await runner.RunAsync(source, parameters);

        result.Success.Should().BeTrue();
        result.ConsoleOutput.Should().Contain("Lookback=50");
    }

    [Fact]
    public async Task RunAsync_ContextHelpers_ReadToolbarContext()
    {
        var runner = BuildRunner();
        const string source = """
            var from = ContextFrom.HasValue ? ContextFrom.Value.ToString("yyyy-MM-dd") : "none";
            var to = ContextTo.HasValue ? ContextTo.Value.ToString("yyyy-MM-dd") : "none";
            Print($"ctx={ContextSymbol}|{from}|{to}|{ContextInterval}");
            """;
        var parameters = new Dictionary<string, object?>
        {
            ["context.symbol"] = "SPY",
            ["context.from"] = new DateOnly(2024, 1, 2),
            ["context.to"] = new DateOnly(2024, 2, 3),
            ["context.interval"] = "daily"
        };

        var result = await runner.RunAsync(source, parameters);

        result.Success.Should().BeTrue();
        result.ConsoleOutput.Should().Contain("ctx=SPY|2024-01-02|2024-02-03|daily");
    }

    // ── Null-parameters coercion ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NullParameters_TreatedAsEmpty()
    {
        var runner = BuildRunner();

        var result = await runner.RunAsync("Print(\"ok\");", null!);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ContinueWithAsync_ReusesPriorScriptState()
    {
        var runner = BuildRunner();

        var first = await runner.RunAsync("var x = 41;", NoParams);
        var second = await runner.ContinueWithAsync("x += 1; var y = x + 1; Print(y);", first.Checkpoint!, NoParams);

        first.Checkpoint.Should().NotBeNull();
        second.Success.Should().BeTrue();
        second.CompilationErrors.Should().BeEmpty();
        second.CompileTime.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        second.ConsoleOutput.Should().Contain("43");
    }

    [Fact]
    public async Task ContinueWithAsync_CompilerWarning_RemainsSeparateFromErrors()
    {
        var runner = BuildRunner();
        var first = await runner.RunAsync("var x = 41;", NoParams);

        var second = await runner.ContinueWithAsync(
            "#warning continuation-warning\nx += 1; Print(x);",
            first.Checkpoint!,
            NoParams);

        second.Success.Should().BeTrue();
        second.CompilationErrors.Should().BeEmpty();
        second.CompilationWarnings.Should().Contain(diagnostic =>
            diagnostic.Severity == "Warning" &&
            diagnostic.Message.Contains("continuation-warning", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ContinueWithAsync_CompileTime_RemainsZeroWhenRuntimeWorkExists()
    {
        var runner = BuildRunner();

        var first = await runner.RunAsync("var x = 41;", NoParams);
        var second = await runner.ContinueWithAsync(
            "System.Threading.Thread.Sleep(25); x += 1; Print(x);",
            first.Checkpoint!,
            NoParams);

        second.Success.Should().BeTrue();
        second.CompileTime.Should().Be(TimeSpan.Zero);
        second.Elapsed.Should().BeGreaterThan(second.CompileTime);
    }

    [Fact]
    public async Task ContinueWithAsync_CompilationFailure_PreservesPreviousCheckpoint()
    {
        var runner = BuildRunner();

        var first = await runner.RunAsync("var x = 41;", NoParams);
        var second = await runner.ContinueWithAsync("x = \"wrong\";", first.Checkpoint!, NoParams);

        second.Success.Should().BeFalse();
        second.CompilationErrors.Should().NotBeEmpty();
        second.RuntimeError.Should().BeNull();
        second.Checkpoint.Should().BeSameAs(first.Checkpoint);

        var third = await runner.ContinueWithAsync("x += 1; Print(x);", first.Checkpoint!, NoParams);
        third.Success.Should().BeTrue();
        third.ConsoleOutput.Should().Contain("42");
    }

    [Theory]
    [InlineData("#r \"System.Xml\"\nvar x = 1;", "#r")]
    [InlineData("#load \"helpers/common.csx\"\nvar x = 1;", "#load")]
    [InlineData("var text = System.IO.File.ReadAllText(\"/tmp/a.txt\");", "System.IO.")]
    public async Task ContinueWithAsync_SafeModeBlocksUnsafeContinuationSource_AndPreservesCheckpoint(
        string continuationSource,
        string blockedMarker)
    {
        var runner = BuildRunner();

        var first = await runner.RunAsync("var x = 41;", NoParams);

        var second = await runner.ContinueWithAsync(continuationSource, first.Checkpoint!, NoParams);

        second.Success.Should().BeFalse();
        second.CompilationErrors.Should().ContainSingle(d =>
            d.Message.Contains(blockedMarker, StringComparison.Ordinal) &&
            d.Message.Contains("disabled in safe mode", StringComparison.OrdinalIgnoreCase));
        second.RuntimeError.Should().BeNull();
        second.Checkpoint.Should().BeSameAs(first.Checkpoint);
    }

    [Fact]
    public async Task RunAsync_UsesFreshPerInvocationPlotQueue_NotInjectedQueueState()
    {
        var injectedQueue = new PlotQueue();
        injectedQueue.Enqueue(new PlotRequest("leftover", PlotType.Line));
        injectedQueue.Complete();

        var runner = BuildRunner(plotQueue: injectedQueue);
        var result = await runner.RunAsync("Print(\"no plots\")", NoParams);

        result.Success.Should().BeTrue();
        result.Plots.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_PlotsDoNotLeakAcrossRuns()
    {
        var runner = BuildRunner();
        const string emitPlotSource = """
            var r = new ReturnSeries(
                "T",
                ReturnKind.Arithmetic,
                new[] { new ReturnPoint(new DateOnly(2024, 1, 1), 0.01) });
            r.Plot("run1");
            """;

        var first = await runner.RunAsync(emitPlotSource, NoParams);
        var second = await runner.RunAsync("Print(\"second\")", NoParams);

        first.Success.Should().BeTrue();
        first.Plots.Should().ContainSingle(p => p.Title == "run1");
        second.Success.Should().BeTrue();
        second.Plots.Should().BeEmpty();
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(path) && deadline.Elapsed < timeout)
            await Task.Delay(25);

        File.Exists(path).Should().BeTrue($"worker marker should appear within {timeout}");
    }

    private static async Task WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                    return;
            }
            catch (ArgumentException)
            {
                return;
            }

            await Task.Delay(25);
        }

        Action lookup = () => Process.GetProcessById(processId);
        lookup.Should().Throw<ArgumentException>("the isolated worker must not survive cancellation");
    }

    private sealed class BlockingWorkerClient : IQuantScriptWorkerClient
    {
        private int _active;
        private int _callCount;
        private int _maximumActive;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount => Volatile.Read(ref _callCount);
        public int MaximumActive => Volatile.Read(ref _maximumActive);

        public async Task<WorkerExecutionOutcome> ExecuteAsync(
            WorkerExecutionRequest request,
            IQuantDataContext dataContext,
            QuantScriptOptions options,
            CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
            var active = Interlocked.Increment(ref _active);
            var observed = Volatile.Read(ref _maximumActive);
            while (active > observed)
            {
                var previous = Interlocked.CompareExchange(ref _maximumActive, active, observed);
                if (previous == observed)
                    break;
                observed = previous;
            }
            Entered.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(ct);
                return new WorkerExecutionOutcome(
                    WorkerCompletionKind.Completed,
                    new WorkerScriptRunResult(
                        true,
                        0,
                        0,
                        [],
                        [],
                        null,
                        string.Empty,
                        [],
                        [],
                        [],
                        [],
                        []),
                    0);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class ScriptedWorkerClient(params WorkerExecutionOutcome[] outcomes)
        : IQuantScriptWorkerClient
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<WorkerExecutionOutcome> ExecuteAsync(
            WorkerExecutionRequest request,
            IQuantDataContext dataContext,
            QuantScriptOptions options,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var index = Interlocked.Increment(ref _callCount) - 1;
            if ((uint)index >= (uint)outcomes.Length)
                throw new InvalidOperationException("No scripted worker outcome remains for this invocation.");

            return Task.FromResult(outcomes[index]);
        }
    }

    private sealed class NonEnumeratingTradeList(int count) : IReadOnlyList<ScriptTrade>
    {
        private int _enumerationAttempts;
        public int EnumerationAttempts => Volatile.Read(ref _enumerationAttempts);
        public int Count { get; } = count;
        public ScriptTrade this[int index] => throw new InvalidOperationException("The oversized response must not be indexed.");

        public IEnumerator<ScriptTrade> GetEnumerator()
        {
            Interlocked.Increment(ref _enumerationAttempts);
            throw new InvalidOperationException("The oversized response must be rejected before enumeration.");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
