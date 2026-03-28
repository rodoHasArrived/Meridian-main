using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.QuantScript.Tests;

public sealed class ScriptRunnerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ScriptRunner CreateRunner(IQuantDataContext? dataContext = null)
    {
        var compiler = new RoslynScriptCompiler(NullLogger<RoslynScriptCompiler>.Instance);
        var ctx = dataContext ?? new FakeQuantDataContext();
        return new ScriptRunner(compiler, ctx, NullLogger<ScriptRunner>.Instance);
    }

    private static IReadOnlyDictionary<string, object?> NoParams =>
        new Dictionary<string, object?>();

    // ── Argument validation ───────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NullOrEmptySource_ThrowsArgumentException()
    {
        var runner = CreateRunner();

        await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync(string.Empty, NoParams));
    }

    // ── Successful execution ──────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_PrintCall_AppearsInConsoleOutput()
    {
        var runner = CreateRunner();
        const string source = "Print(\"hello world\");";

        var result = await runner.RunAsync(source, NoParams);

        result.Success.Should().BeTrue();
        result.ConsoleOutput.Should().Contain("hello world");
    }

    [Fact]
    public async Task RunAsync_PrintMetricCall_AppearsInMetrics()
    {
        var runner = CreateRunner();
        const string source = "PrintMetric(\"Sharpe\", 1.23);";

        var result = await runner.RunAsync(source, NoParams);

        result.Success.Should().BeTrue();
        result.Metrics.Should().Contain(kv => kv.Key == "Sharpe");
    }

    [Fact]
    public async Task RunAsync_ValidScript_ReturnsTiming()
    {
        var runner = CreateRunner();

        var result = await runner.RunAsync("var x = 1 + 1;", NoParams);

        result.Success.Should().BeTrue();
        result.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        result.CompileTime.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    // ── Compilation failure ───────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_SyntaxError_ReturnsFailed_WithDiagnostics()
    {
        var runner = CreateRunner();
        const string source = "int x = \"this is not an int\";";

        var result = await runner.RunAsync(source, NoParams);

        result.Success.Should().BeFalse();
        result.CompilationErrors.Should().NotBeEmpty();
        result.RuntimeError.Should().BeNull();
    }

    // ── Runtime error ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ThrowingScript_ReturnsFailed_WithRuntimeError()
    {
        var runner = CreateRunner();
        const string source = "throw new System.InvalidOperationException(\"test error\");";

        var result = await runner.RunAsync(source, NoParams);

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().NotBeNullOrEmpty();
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_CancelledBeforeRun_ReturnsFailedOrThrows()
    {
        var runner = CreateRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // May return a cancelled result or throw — either is acceptable
        try
        {
            var result = await runner.RunAsync("Print(\"hi\");", NoParams, cts.Token);
            // If it returns, it should indicate failure/cancellation
            (result.Success == false || result.RuntimeError?.Contains("cancel", StringComparison.OrdinalIgnoreCase) == true)
                .Should().BeTrue();
        }
        catch (OperationCanceledException)
        {
            // Also acceptable
        }
    }

    // ── Data access ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_DataGetPrices_ReturnsNonEmptySeries()
    {
        var runner = CreateRunner();
        const string source = """
            var prices = Data.Prices("SPY", new DateTime(2024,1,1), new DateTime(2024,2,1));
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
        var runner = CreateRunner();
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
        var runner = CreateRunner();

        var result = await runner.RunAsync("Print(\"ok\");", null!);

        result.Success.Should().BeTrue();
    }
}
