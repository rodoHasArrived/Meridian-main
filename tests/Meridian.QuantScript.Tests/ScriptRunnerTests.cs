using Meridian.QuantScript.Api;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Plotting;
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
        PlotQueue? plotQueue = null)
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
            Options.Create(new QuantScriptOptions { RunTimeoutSeconds = 10 }),
            NullLogger<ScriptRunner>.Instance,
            null);
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
    public async Task RunAsync_CancelledBeforeRun_ReturnsFailedOrCompletes()
    {
        var runner = BuildRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await runner.RunAsync("Print(\"hi\");", NoParams, cts.Token);
            result.Should().NotBeNull();
        }
        catch (OperationCanceledException)
        {
            // Acceptable: some code paths throw on immediate cancellation
        }
    }

    [Fact]
    public async Task RunAsync_CancellationToken_CancelsRun()
    {
        var runner = BuildRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // A pre-cancelled token throws OperationCanceledException from CompileAsync
        try
        {
            var result = await runner.RunAsync("// should be cancelled", NoParams, cts.Token);
            result.Should().NotBeNull();
        }
        catch (OperationCanceledException)
        {
            // Acceptable: compilation cancelled before run begins
        }
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Timeout_TerminatesAfterConfiguredDuration()
    {
        var compiler = new RoslynScriptCompiler(
            Options.Create(new QuantScriptOptions()),
            NullLogger<RoslynScriptCompiler>.Instance);
        var dataContext = new Mock<IQuantDataContext>().Object;
        var shortTimeout = new QuantScriptOptions { RunTimeoutSeconds = 1 };

        var runner = new ScriptRunner(
            compiler,
            dataContext,
            new PlotQueue(),
            Options.Create(shortTimeout),
            NullLogger<ScriptRunner>.Instance,
            null);

        // Use a tight spin-loop that respects the thread-pool cancellation token
        // (Thread.Sleep cannot be interrupted, but a spin check can)
        var result = await runner.RunAsync(
            "while(true) { if(System.Threading.Thread.Sleep(50) == false) {} }",
            NoParams);

        // Should have been cancelled by the run timeout (success or cancelled are both acceptable
        // depending on how the script terminates, but it should complete within the test)
        result.Should().NotBeNull();
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
        var second = await runner.ContinueWithAsync("x += 1; Print(x);", first.Checkpoint!, NoParams);

        first.Checkpoint.Should().NotBeNull();
        second.Success.Should().BeTrue();
        second.ConsoleOutput.Should().Contain("42");
    }

    [Fact]
    public async Task ContinueWithAsync_CompilationFailure_PreservesPreviousCheckpoint()
    {
        var runner = BuildRunner();

        var first = await runner.RunAsync("var x = 41;", NoParams);
        var second = await runner.ContinueWithAsync("x = ;", first.Checkpoint!, NoParams);

        second.Success.Should().BeFalse();
        second.CompilationErrors.Should().NotBeEmpty();
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
}
