using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace Meridian.LifecycleSupervisor.Tests;

public sealed class LifecycleDatabaseToolTests
{
    [Fact]
    public async Task RunToolAsync_CompletesWhenAGrandchildKeepsInheritedPipesOpen()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // start /b spawns a detached child that inherits the redirected pipes and outlives
        // the tool by ~30s — the exact shape of pg_ctl start leaving postgres.exe attached
        // to the supervisor's stdout/stderr. The run must complete on the tool's exit plus
        // the drain grace, not on the grandchild's lifetime.
        var stopwatch = Stopwatch.StartNew();
        var run = LifecycleDatabaseController.RunToolAsync(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/c", "start /b cmd /c \"timeout /t 30 /nobreak > NUL\" & exit /b 0"],
            TimeSpan.FromSeconds(15),
            CancellationToken.None);

        await run.WaitAsync(TimeSpan.FromSeconds(12));
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task RunToolAsync_ReportsFailureDiagnosticsFromAFailingTool()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // A failing tool leaves no surviving child, so its pipes close immediately and the
        // grace-bounded drain must still deliver the tool's own diagnostic output.
        var act = () => LifecycleDatabaseController.RunToolAsync(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/c", "echo tool-diagnostic-output & exit /b 7"],
            TimeSpan.FromSeconds(15),
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*exit code 7*tool-diagnostic-output*");
    }
}
