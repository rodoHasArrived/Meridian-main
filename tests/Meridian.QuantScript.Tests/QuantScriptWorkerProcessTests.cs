using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Meridian.QuantScript.Runtime;

namespace Meridian.QuantScript.Tests;

/// <summary>Launch the actual worker entrypoint, including its argument and request boundary.</summary>
public sealed class QuantScriptWorkerProcessTests
{
    private const int ProtocolLimit = 65_536;

    public static IEnumerable<object[]> InvalidLaunchArguments =>
    [
        [Array.Empty<string>()],
        [new[] { "--wrong-mode", "unused", "unused", "65536" }],
        [new[] { "--isolated-worker", "unused", "unused", "1023" }],
        [new[] { "--isolated-worker", "unused", "unused", "67108865" }],
        [new[] { "--isolated-worker", "unused", "unused", "-1" }]
    ];

    [Theory]
    [MemberData(nameof(InvalidLaunchArguments))]
    public async Task InvalidLaunchContract_ExitsWithUsageCodeWithoutOpeningPipes(string[] arguments)
    {
        using var process = new Process { StartInfo = CreateStartInfo(arguments) };
        process.Start().Should().BeTrue();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            process.ExitCode.Should().Be(64);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [Theory]
    [InlineData("", false, false, "source was empty")]
    [InlineData("Print(1);", true, false, "absolute path")]
    [InlineData("Print(1);", false, true, "protocol limit did not match")]
    public async Task InvalidRequest_ReturnsCorrelatedFatalFrameAndSoftwareExitCode(
        string source, bool relativeRoot, bool mismatchedLimit, string expectedError)
    {
        var request = CreateRequest(source, relativeRoot ? "relative-data" : Path.GetTempPath(),
            mismatchedLimit ? ProtocolLimit + 1 : ProtocolLimit);

        var (frame, exitCode) = await ExecuteWorkerAsync(QuantScriptWorkerProtocol.Execute, request);

        exitCode.Should().Be(70);
        frame.Kind.Should().Be(QuantScriptWorkerProtocol.FatalError);
        QuantScriptWorkerProtocol.ReadPayload<WorkerFatalError>(frame).Message.Should().Contain(expectedError);
    }

    [Fact]
    public async Task UnexpectedFrameKind_FailsClosedRatherThanExecutingPayload()
    {
        var request = CreateRequest("Print(1);", Path.GetTempPath(), ProtocolLimit);

        var (frame, exitCode) = await ExecuteWorkerAsync(QuantScriptWorkerProtocol.Result, request);

        exitCode.Should().Be(70);
        frame.Kind.Should().Be(QuantScriptWorkerProtocol.FatalError);
        QuantScriptWorkerProtocol.ReadPayload<WorkerFatalError>(frame).Message.Should().Contain("Expected an execute frame");
    }

    [Fact]
    public async Task ValidRequest_ExecutesInWorkerAndReturnsTypedResult()
    {
        var request = CreateRequest("Print(\"worker-entrypoint\");", Path.GetTempPath(), ProtocolLimit);

        var (frame, exitCode) = await ExecuteWorkerAsync(QuantScriptWorkerProtocol.Execute, request);

        exitCode.Should().Be(0);
        frame.Kind.Should().Be(QuantScriptWorkerProtocol.Result);
        var result = QuantScriptWorkerProtocol.ReadPayload<WorkerExecutionResponse>(frame).Result;
        result.Success.Should().BeTrue();
        result.ConsoleOutput.Should().Contain("worker-entrypoint");
    }

    private static WorkerExecutionRequest CreateRequest(string source, string dataRoot, int maxProtocolBytes)
        => new([], new WorkerScriptCell(source, new Dictionary<string, WorkerParameterValue>()),
            new WorkerRunOptions(20, false, 4, 8, dataRoot, 20_000, 100, maxProtocolBytes));

    private static async Task<(WorkerEnvelope Frame, int ExitCode)> ExecuteWorkerAsync(
        string kind, WorkerExecutionRequest request)
    {
        using var outbound = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        using var inbound = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var process = new Process
        {
            StartInfo = CreateStartInfo(["--isolated-worker", outbound.GetClientHandleAsString(),
                inbound.GetClientHandleAsString(), ProtocolLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)])
        };
        process.Start().Should().BeTrue();
        outbound.DisposeLocalCopyOfClientHandle();
        inbound.DisposeLocalCopyOfClientHandle();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await QuantScriptWorkerProtocol.WriteAsync(outbound, kind, "worker-test-request", request,
                ProtocolLimit, deadline.Token);
            var frame = await QuantScriptWorkerProtocol.ReadAsync(inbound, ProtocolLimit, deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            frame.CorrelationId.Should().Be("worker-test-request");
            (await stderr).Should().BeEmpty();
            (await stdout).Should().BeEmpty("script output travels in the typed response");
            return (frame, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments)
    {
        var workerAssembly = Path.Combine(AppContext.BaseDirectory, "workers", "quant-script",
            "Meridian.QuantScript.Worker.dll");
        File.Exists(workerAssembly).Should().BeTrue("the build copies the real worker sidecar");
        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var info = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(workerAssembly)!
        };
        info.ArgumentList.Add(workerAssembly);
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        return info;
    }
}
