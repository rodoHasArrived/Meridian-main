using System.ComponentModel;
using System.Diagnostics;
using Meridian.ProcessIsolation;

namespace Meridian.QuantScript.Tests;

/// <summary>
/// Launch-contract validation and lifecycle containment for <see cref="ContainedProcess"/>,
/// the reusable boundary the isolated QuantScript worker runs inside. Lifecycle cases use
/// <c>/bin/sh</c> and only run on Linux/macOS; Windows Job Object behaviour is covered through
/// <see cref="ScriptRunnerTests"/>.
/// </summary>
[Collection(ContainedProcessCollection.CollectionName)]
public sealed class ContainedProcessTests
{
    private const string Shell = "/bin/sh";

    // ── Launch-contract validation ───────────────────────────────────────────

    [Fact]
    public void Start_NullStartInfo_Throws()
    {
        var act = () => ContainedProcess.Start(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Start_ShellExecute_IsRejectedBeforeLaunch()
    {
        var startInfo = RedirectedStartInfo(ExistingAbsoluteExecutable());
        startInfo.UseShellExecute = true;

        var act = () => ContainedProcess.Start(startInfo);

        act.Should().Throw<ArgumentException>().WithMessage("*shell execution*");
    }

    [Theory]
    [InlineData("sh")]
    [InlineData("dotnet")]
    [InlineData("./worker")]
    public void Start_RelativeExecutable_IsRejected(string fileName)
    {
        var act = () => ContainedProcess.Start(RedirectedStartInfo(fileName));

        act.Should().Throw<FileNotFoundException>().WithMessage("*existing absolute path*");
    }

    [Fact]
    public void Start_MissingAbsoluteExecutable_IsRejected()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "worker");

        var act = () => ContainedProcess.Start(RedirectedStartInfo(missing));

        act.Should().Throw<FileNotFoundException>().Which.FileName.Should().Be(missing);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Start_UnredirectedOutputStreams_AreRejected(bool redirectStdout, bool redirectStderr)
    {
        var startInfo = RedirectedStartInfo(ExistingAbsoluteExecutable());
        startInfo.RedirectStandardOutput = redirectStdout;
        startInfo.RedirectStandardError = redirectStderr;

        var act = () => ContainedProcess.Start(startInfo);

        act.Should().Throw<ArgumentException>().WithMessage("*redirect stdout and stderr*");
    }

    [Fact]
    public void Start_NegativeLimits_AreRejectedBeforeLaunch()
    {
        var startInfo = RedirectedStartInfo(ExistingAbsoluteExecutable());

        var memory = () => ContainedProcess.Start(startInfo, new ContainedProcessLimits(-1, TimeSpan.Zero, 0));
        var cpu = () => ContainedProcess.Start(startInfo, new ContainedProcessLimits(0, TimeSpan.FromTicks(-1), 0));
        var count = () => ContainedProcess.Start(startInfo, new ContainedProcessLimits(0, TimeSpan.Zero, -1));

        memory.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("MaxAggregateMemoryBytes");
        cpu.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("MaxAggregateCpuTime");
        count.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("MaxActiveProcesses");
    }

    [Fact]
    public void Start_RequireHardLimitsOffWindows_FailsClosedInsteadOfSilentlyDegrading()
    {
        if (OperatingSystem.IsWindows())
            return;

        var marker = Path.Combine(Path.GetTempPath(), $"contained-hard-{Guid.NewGuid():N}.txt");
        var startInfo = ShellStartInfo($"echo started > '{marker}'");

        var act = () => ContainedProcess.Start(
            startInfo,
            new ContainedProcessLimits(64L * 1024 * 1024, TimeSpan.FromSeconds(5), 1, RequireHardLimits: true));

        act.Should().Throw<PlatformNotSupportedException>().WithMessage("*Windows Job Objects*");
        File.Exists(marker).Should().BeFalse("a rejected launch must never start the child");
    }

    [Fact]
    public void None_HasNoResourceCeilings()
    {
        ContainedProcessLimits.None.MaxAggregateMemoryBytes.Should().Be(0);
        ContainedProcessLimits.None.MaxAggregateCpuTime.Should().Be(TimeSpan.Zero);
        ContainedProcessLimits.None.MaxActiveProcesses.Should().Be(0);
        ContainedProcessLimits.None.RequireHardLimits.Should().BeFalse();
    }

    // ── Lifecycle (POSIX shells) ─────────────────────────────────────────────

    [Fact]
    public async Task Start_ShortLivedChild_RunsToCompletionAndCapturesOutput()
    {
        if (!HasPosixShell())
            return;

        await using var contained = ContainedProcess.Start(ShellStartInfo("echo contained-ok; exit 3"));

        var stdout = await contained.Process.StandardOutput.ReadToEndAsync();
        await contained.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

        stdout.Trim().Should().Be("contained-ok");
        contained.Process.ExitCode.Should().Be(3);
    }

    [Fact]
    public async Task TerminateAsync_KillsRootAndBackgroundDescendant()
    {
        if (!HasPosixShell())
            return;

        // The shell prints the PID of a backgrounded sleeper, then waits on it.
        var contained = ContainedProcess.Start(ShellStartInfo("sleep 120 & echo $!; wait"));
        int descendantPid;
        await using (contained)
        {
            var line = await contained.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            descendantPid = int.Parse(line!.Trim(), System.Globalization.CultureInfo.InvariantCulture);
            IsAlive(descendantPid).Should().BeTrue();

            await contained.TerminateAsync(TimeSpan.FromSeconds(5));

            contained.Process.HasExited.Should().BeTrue();
        }

        (await WaitUntilAsync(() => !IsAlive(descendantPid), TimeSpan.FromSeconds(10)))
            .Should().BeTrue("termination must cover the whole contained process tree");
    }

    [Fact]
    public async Task TerminateAsync_CalledTwiceAndAfterExit_IsIdempotent()
    {
        if (!HasPosixShell())
            return;

        await using var contained = ContainedProcess.Start(ShellStartInfo("sleep 120"));

        await contained.TerminateAsync(TimeSpan.FromSeconds(5));
        var second = () => contained.TerminateAsync(TimeSpan.FromSeconds(1));

        await second.Should().NotThrowAsync();
        contained.Process.HasExited.Should().BeTrue();
    }

    [Fact]
    public async Task TerminateAsync_ChildThatAlreadyExited_DoesNotThrow()
    {
        if (!HasPosixShell())
            return;

        await using var contained = ContainedProcess.Start(ShellStartInfo("exit 0"));
        await contained.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var act = () => contained.TerminateAsync(TimeSpan.FromSeconds(1));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_RunningChild_TerminatesItAndIsSafeToRepeat()
    {
        if (!HasPosixShell())
            return;

        var contained = ContainedProcess.Start(ShellStartInfo("sleep 120"));
        var pid = contained.Process.Id;

        await contained.DisposeAsync();
        var again = async () => await contained.DisposeAsync();
        var syncAgain = () => contained.Dispose();

        await again.Should().NotThrowAsync();
        syncAgain.Should().NotThrow();
        (await WaitUntilAsync(() => !IsAlive(pid), TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_RunningChild_TerminatesItSynchronously()
    {
        if (!HasPosixShell())
            return;

        var contained = ContainedProcess.Start(ShellStartInfo("sleep 120"));
        var pid = contained.Process.Id;

        contained.Dispose();

        (await WaitUntilAsync(() => !IsAlive(pid), TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task GetResourceSnapshot_Linux_AggregatesRootAndDescendantsWithoutClaimingHardLimits()
    {
        if (!OperatingSystem.IsLinux() || !HasPosixShell())
            return;

        await using var contained = ContainedProcess.Start(
            ShellStartInfo("sleep 120 & sleep 120 & echo ready; wait"),
            new ContainedProcessLimits(256L * 1024 * 1024, TimeSpan.FromSeconds(30), 4));
        (await contained.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))
            .Should().Be("ready");

        var observed = await WaitUntilAsync(
            () => contained.GetResourceSnapshot().ActiveProcessCount >= 3,
            TimeSpan.FromSeconds(10));
        var snapshot = contained.GetResourceSnapshot();

        observed.Should().BeTrue("the shell and both background sleepers are in the contained tree");
        snapshot.CurrentMemoryBytes.Should().BePositive();
        snapshot.PeakMemoryBytes.Should().BeGreaterThanOrEqualTo(snapshot.CurrentMemoryBytes);
        snapshot.CpuTime.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        snapshot.HardLimitsApplied.Should().BeFalse("only Windows Job Objects enforce hard ceilings");
    }

    [Fact]
    public async Task GetResourceSnapshot_Linux_ExitedChildReportsNoActiveProcesses()
    {
        if (!OperatingSystem.IsLinux() || !HasPosixShell())
            return;

        await using var contained = ContainedProcess.Start(
            ShellStartInfo("exit 0"),
            new ContainedProcessLimits(256L * 1024 * 1024, TimeSpan.FromSeconds(30), 4));
        await contained.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

        contained.GetResourceSnapshot().ActiveProcessCount.Should().Be(0,
            "a reaped child is no longer running even though the tree walk visited its pid");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool HasPosixShell() => !OperatingSystem.IsWindows() && File.Exists(Shell);

    private static string ExistingAbsoluteExecutable()
        => OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")
            : Shell;

    private static ProcessStartInfo RedirectedStartInfo(string fileName)
        => new(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

    private static ProcessStartInfo ShellStartInfo(string script)
    {
        var startInfo = RedirectedStartInfo(Shell);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(script);
        return startInfo;
    }

    private static bool IsAlive(int pid)
    {
        if (OperatingSystem.IsLinux())
        {
            // A killed-but-unreaped child is a zombie; treat it as dead.
            var statPath = $"/proc/{pid}/stat";
            try
            {
                if (!File.Exists(statPath))
                    return false;
                var stat = File.ReadAllText(statPath);
                var state = stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[0];
                return state != "Z" && state != "X";
            }
            catch (IOException)
            {
                return false;
            }
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(50);
        }

        return condition();
    }
}

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class ContainedProcessCollection
{
    public const string CollectionName = "Contained process lifecycle";
}
