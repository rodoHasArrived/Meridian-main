using System.Diagnostics;
using System.IO.Pipes;
using Meridian.ProcessIsolation;
using Meridian.QuantScript.Runtime;

namespace Meridian.QuantScript.Tests;

/// <summary>
/// Drives the real <c>Meridian.QuantScript.Worker</c> sidecar (copied next to the test assembly
/// by <c>QuantScriptWorker.Sidecar.targets</c>) over its anonymous-pipe protocol and asserts its
/// fail-closed launch contract: bad command lines exit 64, protocol or request violations reply
/// with a correlated <c>fatal-error</c> frame and exit 70, and a valid request returns a result.
/// </summary>
[Collection(ContainedProcessCollection.CollectionName)]
public sealed class QuantScriptWorkerProgramTests
{
    private const int UsageExitCode = 64;
    private const int FatalExitCode = 70;
    private const int ProtocolBytes = 1024 * 1024;
    private static readonly TimeSpan WorkerTimeout = TimeSpan.FromSeconds(60);

    // ── Command-line contract (exit 64) ──────────────────────────────────────

    public static TheoryData<string[]> InvalidCommandLines => new()
    {
        Array.Empty<string>(),
        new[] { "--isolated-worker" },
        new[] { "--isolated-worker", "1", "2" },
        new[] { "--isolated-worker", "1", "2", "4096", "extra" },
        new[] { "--worker", "1", "2", "4096" },
        new[] { "--ISOLATED-WORKER", "1", "2", "4096" },
        new[] { "--isolated-worker", "1", "2", "1023" },
        new[] { "--isolated-worker", "1", "2", "67108865" },
        new[] { "--isolated-worker", "1", "2", "-4096" },
        new[] { "--isolated-worker", "1", "2", "+4096" },
        new[] { "--isolated-worker", "1", "2", "4,096" },
        new[] { "--isolated-worker", "1", "2", "not-a-number" }
    };

    [Theory]
    [MemberData(nameof(InvalidCommandLines))]
    public async Task Worker_InvalidCommandLine_ExitsWithUsageCodeBeforeOpeningPipes(string[] arguments)
    {
        var startInfo = WorkerStartInfo("unused-request", "unused-response");
        ReplaceWorkerArguments(startInfo, arguments);

        await using var contained = ContainedProcess.Start(startInfo);
        contained.Process.StandardInput.Close();
        await contained.Process.WaitForExitAsync().WaitAsync(WorkerTimeout);

        contained.Process.ExitCode.Should().Be(UsageExitCode);
    }

    // ── Protocol / request contract (exit 70 + fatal-error frame) ────────────

    [Fact]
    public async Task Worker_FirstFrameIsNotExecute_RepliesFatalErrorWithSameCorrelationId()
    {
        var outcome = await RunWorkerAsync(
            QuantScriptWorkerProtocol.DataResponse,
            "corr-not-execute",
            new WorkerFatalError("not a request"));

        outcome.ExitCode.Should().Be(FatalExitCode);
        outcome.Response.Kind.Should().Be(QuantScriptWorkerProtocol.FatalError);
        outcome.Response.CorrelationId.Should().Be("corr-not-execute");
        QuantScriptWorkerProtocol.ReadPayload<WorkerFatalError>(outcome.Response).Message
            .Should().Contain("Expected an execute frame").And.Contain(QuantScriptWorkerProtocol.DataResponse);
    }

    [Fact]
    public async Task Worker_RequestProtocolLimitDiffersFromLaunchLimit_FailsClosed()
    {
        var request = Request("var x = 1;", maxProtocolBytes: ProtocolBytes / 2);

        var outcome = await RunWorkerAsync(QuantScriptWorkerProtocol.Execute, "corr-limit", request);

        outcome.ExitCode.Should().Be(FatalExitCode);
        outcome.Response.Kind.Should().Be(QuantScriptWorkerProtocol.FatalError);
        outcome.Response.CorrelationId.Should().Be("corr-limit");
        QuantScriptWorkerProtocol.ReadPayload<WorkerFatalError>(outcome.Response).Message
            .Should().Contain("protocol limit did not match");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t")]
    public async Task Worker_EmptyCurrentCell_FailsClosed(string source)
    {
        var outcome = await RunWorkerAsync(QuantScriptWorkerProtocol.Execute, "corr-empty", Request(source));

        outcome.ExitCode.Should().Be(FatalExitCode);
        outcome.Response.Kind.Should().Be(QuantScriptWorkerProtocol.FatalError);
        QuantScriptWorkerProtocol.ReadPayload<WorkerFatalError>(outcome.Response).Message
            .Should().Contain("cell source was empty");
    }

    [Theory]
    [InlineData("./data")]
    [InlineData("data")]
    [InlineData("../data")]
    public async Task Worker_RelativeDataRoot_FailsClosed(string dataRoot)
    {
        var outcome = await RunWorkerAsync(
            QuantScriptWorkerProtocol.Execute,
            "corr-root",
            Request("var x = 1;", dataRoot: dataRoot));

        outcome.ExitCode.Should().Be(FatalExitCode);
        outcome.Response.Kind.Should().Be(QuantScriptWorkerProtocol.FatalError);
        QuantScriptWorkerProtocol.ReadPayload<WorkerFatalError>(outcome.Response).Message
            .Should().Contain("absolute path");
    }

    [Fact]
    public async Task Worker_ValidRequest_ReturnsCorrelatedResultAndExitsZero()
    {
        var outcome = await RunWorkerAsync(
            QuantScriptWorkerProtocol.Execute,
            "corr-ok",
            Request("var total = 40 + 2; Print(total);"));

        outcome.ExitCode.Should().Be(0, outcome.StandardError);
        outcome.Response.Kind.Should().Be(QuantScriptWorkerProtocol.Result);
        outcome.Response.CorrelationId.Should().Be("corr-ok");
        var result = QuantScriptWorkerProtocol.ReadPayload<WorkerExecutionResponse>(outcome.Response).Result;
        result.Validate();
        result.Success.Should().BeTrue(
            "compilation={0}; runtime={1}",
            string.Join("; ", result.CompilationErrors.Select(e => e.Message)),
            result.RuntimeError);
        result.ConsoleOutput.Should().Contain("42");
    }

    [Fact]
    public async Task Worker_ScriptCompilationError_ReturnsUnsuccessfulResultNotFatalError()
    {
        var outcome = await RunWorkerAsync(
            QuantScriptWorkerProtocol.Execute,
            "corr-compile",
            Request("this is not C#"));

        outcome.ExitCode.Should().Be(0, outcome.StandardError);
        outcome.Response.Kind.Should().Be(QuantScriptWorkerProtocol.Result);
        var result = QuantScriptWorkerProtocol.ReadPayload<WorkerExecutionResponse>(outcome.Response).Result;
        result.Success.Should().BeFalse();
        result.CompilationErrors.Should().NotBeEmpty();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed record WorkerOutcome(int ExitCode, WorkerEnvelope Response, string StandardError);

    private static async Task<WorkerOutcome> RunWorkerAsync<T>(string kind, string correlationId, T payload)
    {
        await using var parentToWorker = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        await using var workerToParent = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var startInfo = WorkerStartInfo(
            parentToWorker.GetClientHandleAsString(),
            workerToParent.GetClientHandleAsString());

        await using var contained = ContainedProcess.Start(startInfo);
        contained.Process.StandardInput.Close();
        parentToWorker.DisposeLocalCopyOfClientHandle();
        workerToParent.DisposeLocalCopyOfClientHandle();
        var stderrTask = contained.Process.StandardError.ReadToEndAsync();
        var stdoutTask = contained.Process.StandardOutput.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(WorkerTimeout);
        await QuantScriptWorkerProtocol.WriteAsync(parentToWorker, kind, correlationId, payload, ProtocolBytes, timeout.Token);
        var response = await QuantScriptWorkerProtocol.ReadAsync(workerToParent, ProtocolBytes, timeout.Token);
        await contained.Process.WaitForExitAsync(timeout.Token);
        await stdoutTask;

        return new WorkerOutcome(contained.Process.ExitCode, response, await stderrTask);
    }

    private static ProcessStartInfo WorkerStartInfo(string requestHandle, string responseHandle)
        => WorkerLaunchResolver.CreateStartInfo(
            new QuantScriptOptions { MaxWorkerProtocolBytes = ProtocolBytes },
            requestHandle,
            responseHandle);

    private static void ReplaceWorkerArguments(ProcessStartInfo startInfo, IReadOnlyList<string> arguments)
    {
        // The resolver appends "--isolated-worker <request> <response> <limit>" after any host
        // arguments (for example "exec ... worker.dll"); swap only those four.
        for (var i = 0; i < 4; i++)
            startInfo.ArgumentList.RemoveAt(startInfo.ArgumentList.Count - 1);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
    }

    private static WorkerExecutionRequest Request(
        string source,
        int maxProtocolBytes = ProtocolBytes,
        string? dataRoot = null)
        => new(
            Array.Empty<WorkerScriptCell>(),
            new WorkerScriptCell(source, new Dictionary<string, WorkerParameterValue>()),
            new WorkerRunOptions(
                CompilationTimeoutSeconds: 30,
                EnableUnsafeScripts: false,
                MaxCachedCompilations: 4,
                MaxPlotsPerRun: 10,
                DefaultDataRoot: dataRoot ?? Path.Combine(Path.GetTempPath(), "meridian-quantscript-worker-tests"),
                MaxRunElapsedMilliseconds: 0,
                MaxOutputItemsPerRun: 0,
                MaxProtocolBytes: maxProtocolBytes));
}
