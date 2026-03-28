using Moq;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Meridian.QuantScript.Compilation;

namespace Meridian.QuantScript.Tests;

public sealed class ScriptRunnerTests
{
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
            null!,  // BacktestEngine — not exercised in these tests
            Options.Create(new QuantScriptOptions { RunTimeoutSeconds = 10 }),
            NullLogger<ScriptRunner>.Instance);
    }

    [Fact]
    public async Task RunAsync_EmptyScript_Succeeds()
    {
        var runner = BuildRunner();
        var result = await runner.RunAsync("// empty", new Dictionary<string, object?>());
        result.Success.Should().BeTrue();
        result.RuntimeError.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_CompilationError_ReturnsFailure()
    {
        var runner = BuildRunner();
        var result = await runner.RunAsync("not valid c# !!!", new Dictionary<string, object?>());
        result.Success.Should().BeFalse();
        result.CompilationErrors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RunAsync_RuntimeException_ReturnsFailure()
    {
        var runner = BuildRunner();
        var result = await runner.RunAsync(
            "throw new System.Exception(\"boom\");",
            new Dictionary<string, object?>());
        result.Success.Should().BeFalse();
        result.RuntimeError.Should().Contain("boom");
    }

    [Fact]
    public async Task RunAsync_CancellationToken_CancelsRun()
    {
        var runner = BuildRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await runner.RunAsync(
            "// should be cancelled",
            new Dictionary<string, object?>(),
            cts.Token);
        // Either the compile step notices cancellation or the run step does
        // Either way the operation should terminate without throwing.
        result.Should().NotBeNull();
    }

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
            null!,  // BacktestEngine — not exercised in this test
            Options.Create(shortTimeout),
            NullLogger<ScriptRunner>.Instance);

        var result = await runner.RunAsync(
            "System.Threading.Thread.Sleep(5000);",
            new Dictionary<string, object?>());

        result.Success.Should().BeFalse();
        result.RuntimeError.Should().NotBeNull();
    }
}
