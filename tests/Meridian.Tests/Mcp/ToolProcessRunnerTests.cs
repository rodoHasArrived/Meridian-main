using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using FluentAssertions;
using Meridian.Mcp.Tools;
using Meridian.ProcessTestHelper;
using Xunit;

namespace Meridian.Tests.Mcp;

[CollectionDefinition("Tool process containment", DisableParallelization = true)]
public sealed class ToolProcessRunnerCollection
{
    public const string CollectionName = "Tool process containment";
}

[Collection(ToolProcessRunnerCollection.CollectionName)]
public sealed class ToolProcessRunnerTests : IDisposable
{
    private static readonly TimeSpan HelperReadyTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DescendantExitTimeout = TimeSpan.FromSeconds(10);
    private readonly List<Process> _trackedDescendants = new();
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "meridian-tests",
        "mcp-tool-process",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunAsync_PreCancelled_DoesNotStartMutationProcess()
    {
        Directory.CreateDirectory(_testRoot);
        var mutationPath = Path.Combine(_testRoot, "pre-cancelled-mutation.txt");
        var startInfo = CreateHelperStartInfo("write-immediately", mutationPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            _ = await ToolProcessRunner.RunAsync(
                startInfo,
                TimeSpan.FromSeconds(10),
                cancellation.Token);
        });

        exception.CancellationToken.Should().Be(cancellation.Token);
        File.Exists(mutationPath).Should().BeFalse("a pre-cancelled tool must never be launched");
    }

    [Fact]
    public async Task RunAsync_CallerCancellation_KillsDescendantBeforeItCanMutate()
    {
        Directory.CreateDirectory(_testRoot);
        var readyPath = Path.Combine(_testRoot, "caller-cancel-ready.txt");
        var mutationPath = Path.Combine(_testRoot, "caller-cancel-mutation.txt");
        var gatePath = Path.Combine(_testRoot, "caller-cancel-gate.txt");
        var startInfo = CreateHelperStartInfo(
            "spawn-gated-mutation",
            readyPath,
            mutationPath,
            gatePath);
        using var cancellation = new CancellationTokenSource();

        var run = ToolProcessRunner.RunAsync(startInfo, TimeSpan.FromSeconds(20), cancellation.Token);
        var descendant = await OpenReadyDescendantAsync(readyPath, HelperReadyTimeout)
            ?? throw new InvalidOperationException("The ready descendant exited before cancellation was requested.");
        Track(descendant);
        await cancellation.CancelAsync();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            _ = await run;
        });

        exception.CancellationToken.Should().Be(cancellation.Token);
        await WaitForExitAsync(descendant, DescendantExitTimeout);
        File.WriteAllText(gatePath, "release");
        File.Exists(mutationPath).Should().BeFalse("the cancelled tool's process tree must not continue editing");
    }

    [Fact]
    public async Task RunAsync_CallerCancellationBeforeDescendantReadiness_PreventsLaterMutation()
    {
        Directory.CreateDirectory(_testRoot);
        var readyPath = Path.Combine(_testRoot, "startup-cancel-ready.txt");
        var mutationPath = Path.Combine(_testRoot, "startup-cancel-mutation.txt");
        var gatePath = Path.Combine(_testRoot, "startup-cancel-gate.txt");
        var startInfo = CreateHelperStartInfo(
            "delayed-spawn-gated-mutation",
            "500",
            readyPath,
            mutationPath,
            gatePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        var run = ToolProcessRunner.RunAsync(startInfo, TimeSpan.FromSeconds(20), cancellation.Token);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            _ = await run;
        });

        exception.CancellationToken.Should().Be(cancellation.Token);
        File.Exists(readyPath).Should().BeFalse(
            "cancellation was requested before the helper's delayed descendant launch");
        File.WriteAllText(gatePath, "release");
        await Task.Delay(TimeSpan.FromSeconds(1));
        File.Exists(mutationPath).Should().BeFalse(
            "a process that loses the Linux setsid startup race must not launch later");
    }

    [Fact]
    public async Task RunAsync_DeadlineExpiry_KillsDescendantAndReportsTimeout()
    {
        Directory.CreateDirectory(_testRoot);
        var readyPath = Path.Combine(_testRoot, "deadline-ready.txt");
        var mutationPath = Path.Combine(_testRoot, "deadline-mutation.txt");
        var gatePath = Path.Combine(_testRoot, "deadline-gate.txt");
        var startInfo = CreateHelperStartInfo(
            "spawn-gated-mutation",
            readyPath,
            mutationPath,
            gatePath);

        var run = ToolProcessRunner.RunAsync(
            startInfo,
            TimeSpan.FromSeconds(12),
            CancellationToken.None);
        var descendant = await OpenReadyDescendantAsync(readyPath, HelperReadyTimeout)
            ?? throw new InvalidOperationException("The ready descendant exited before the deadline was reached.");
        Track(descendant);
        var act = async () =>
        {
            _ = await run;
        };

        await act.Should().ThrowAsync<TimeoutException>();
        await WaitForExitAsync(descendant, DescendantExitTimeout);
        File.WriteAllText(gatePath, "release");
        File.Exists(mutationPath).Should().BeFalse("the timed-out tool's process tree must not continue editing");
    }

    [Fact]
    public async Task RunAsync_ParentExit_KillsDetachedDescendantBeforeReturning()
    {
        Directory.CreateDirectory(_testRoot);
        var readyPath = Path.Combine(_testRoot, "detached-ready.txt");
        var mutationPath = Path.Combine(_testRoot, "detached-mutation.txt");
        var gatePath = Path.Combine(_testRoot, "detached-gate.txt");
        var startInfo = CreateHelperStartInfo(
            "spawn-detached-gated-mutation",
            readyPath,
            mutationPath,
            gatePath);

        var run = ToolProcessRunner.RunAsync(
            startInfo,
            TimeSpan.FromSeconds(20),
            CancellationToken.None);
        var descendant = await OpenReadyDescendantAsync(
            readyPath,
            HelperReadyTimeout,
            allowAlreadyExited: true);
        if (descendant is not null)
            Track(descendant);

        var result = await run;

        result.ExitCode.Should().Be(0);
        if (descendant is not null)
            await WaitForExitAsync(descendant, DescendantExitTimeout);
        File.WriteAllText(gatePath, "release");
        File.Exists(mutationPath).Should().BeFalse(
            "a detached descendant must be terminated when the contained parent exits");
    }

    [Fact]
    public async Task RunAsync_ConcurrentStandardOutputAndError_AreFullyDrained()
    {
        const int lineCount = 512;
        var startInfo = CreateHelperStartInfo(
            "emit-output",
            lineCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "1024");

        var result = await ToolProcessRunner.RunAsync(
            startInfo,
            TimeSpan.FromSeconds(20),
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("stdout-0000:").And.Contain($"stdout-{lineCount - 1:D4}:");
        result.StandardError.Should().Contain("stderr-0000:").And.Contain($"stderr-{lineCount - 1:D4}:");
        result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(lineCount);
        result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(lineCount);
    }

    [Fact]
    public async Task RunAsync_ConcurrentWindowsLaunches_DoNotInheritUnrelatedHandles()
    {
        if (!OperatingSystem.IsWindows())
            return;

        const int processCount = 4;
        Directory.CreateDirectory(_testRoot);
        using var unrelatedPipe = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.Inheritable);
        var runs = new List<Task<ToolProcessResult>>(processCount);
        var gatePaths = new List<string>(processCount);

        for (var index = 0; index < processCount; index++)
        {
            var readyPath = Path.Combine(_testRoot, $"handle-ready-{index}.txt");
            var mutationPath = Path.Combine(_testRoot, $"handle-mutation-{index}.txt");
            var gatePath = Path.Combine(_testRoot, $"handle-gate-{index}.txt");
            gatePaths.Add(gatePath);
            runs.Add(ToolProcessRunner.RunAsync(
                CreateHelperStartInfo(
                    "spawn-gated-mutation",
                    readyPath,
                    mutationPath,
                    gatePath),
                TimeSpan.FromSeconds(20),
                CancellationToken.None));
            Track(await OpenReadyDescendantAsync(readyPath, HelperReadyTimeout));
        }

        unrelatedPipe.DisposeLocalCopyOfClientHandle();
        var sentinel = new byte[1];
        var endOfPipe = unrelatedPipe.ReadAsync(sentinel.AsMemory(), CancellationToken.None).AsTask();
        var completed = await Task.WhenAny(endOfPipe, Task.Delay(TimeSpan.FromSeconds(2)));

        foreach (var gatePath in gatePaths)
            File.WriteAllText(gatePath, "release");
        var results = await Task.WhenAll(runs);
        var bytesRead = await endOfPipe;

        completed.Should().BeSameAs(
            endOfPipe,
            "concurrent tool launches must inherit only their three explicitly allowed standard handles");
        bytesRead.Should().Be(0);
        results.Should().OnlyContain(static result => result.ExitCode == 0);
    }

    public void Dispose()
    {
        foreach (var descendant in _trackedDescendants)
        {
            try
            {
                if (!descendant.HasExited)
                    descendant.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // A racing exit is already the desired state.
            }
            finally
            {
                descendant.Dispose();
            }
        }

        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    private static ProcessStartInfo CreateHelperStartInfo(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(typeof(ProcessTestHelperMarker).Assembly.Location);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    private void Track(Process? descendant)
    {
        if (descendant is not null)
            _trackedDescendants.Add(descendant);
    }

    private static async Task<Process?> OpenReadyDescendantAsync(
        string path,
        TimeSpan timeout,
        bool allowAlreadyExited = false)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            if (File.Exists(path)
                && TryReadProcessId(path, out var processId))
            {
                try
                {
                    return Process.GetProcessById(processId);
                }
                catch (ArgumentException) when (allowAlreadyExited)
                {
                    return null;
                }
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Timed out waiting for a descendant process ID in {path}.");
    }

    private static bool TryReadProcessId(string path, out int processId)
    {
        try
        {
            return int.TryParse(
                File.ReadAllText(path),
                System.Globalization.CultureInfo.InvariantCulture,
                out processId);
        }
        catch (IOException)
        {
            processId = 0;
            return false;
        }
    }

    private static async Task WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Descendant process {process.Id} did not exit within {timeout.TotalSeconds:0.###} seconds.");
        }
    }
}
