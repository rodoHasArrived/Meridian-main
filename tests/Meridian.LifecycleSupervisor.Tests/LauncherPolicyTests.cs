using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Lifecycle;
using Meridian.Contracts.Operations;
using Meridian.Launcher;
using Xunit;

namespace Meridian.LifecycleSupervisor.Tests;

public sealed class LauncherPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-launcher-policy-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultInvocation_RequestsConsumerStartup()
    {
        LauncherCommandPolicy.TryResolveStartupCommand([], out var command).Should().BeTrue();
        command.Should().Be("start");
    }

    [Theory]
    [InlineData("start", "start")]
    [InlineData("START", "start")]
    [InlineData("open", "open")]
    [InlineData("Open", "open")]
    public void ConsumerCommands_AreCanonicalized(string input, string expected)
    {
        LauncherCommandPolicy.TryResolveStartupCommand([input], out var command).Should().BeTrue();
        command.Should().Be(expected);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("status")]
    [InlineData("stop")]
    [InlineData("restart")]
    [InlineData("preflight")]
    [InlineData("--help")]
    [InlineData("help")]
    [InlineData("")]
    [InlineData("unknown")]
    public void OperatorCommands_AreRejectedBeforeSupervisorInvocation(string input)
    {
        LauncherCommandPolicy.TryResolveStartupCommand([input], out _).Should().BeFalse();
    }

    [Fact]
    public void ExtraArguments_AreRejectedInsteadOfSilentlyExecutingTheFirstCommand()
    {
        LauncherCommandPolicy.TryResolveStartupCommand(["start", "stop"], out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(LifecycleDatabaseManagementMode.Dedicated, 60, 60, 185)]
    [InlineData(LifecycleDatabaseManagementMode.Dedicated, 20, 120, 265)]
    [InlineData(LifecycleDatabaseManagementMode.Dedicated, 600, 600, 1805)]
    [InlineData(LifecycleDatabaseManagementMode.External, 60, 600, 60)]
    public void StartupBudget_IncludesEveryOwnedStartupStage(
        LifecycleDatabaseManagementMode databaseMode,
        int startupSeconds,
        int databaseSeconds,
        int expectedSeconds)
    {
        var manifest = new LifecycleSupervisorManifestDto
        {
            DatabaseMode = databaseMode,
            StartupTimeoutSeconds = startupSeconds,
            DatabaseTimeoutSeconds = databaseSeconds
        };

        LifecycleStartupTiming.GetStartupBudget(manifest).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public async Task ForwardedOpen_ProducesFreshLauncherReceiptAfterAnOlderSessionStarted()
    {
        var configuration = CreateConfiguration();
        await using var runtime = new LifecycleSupervisorRuntime(configuration);
        var oldSessionStartedAtUtc = DateTimeOffset.UtcNow.AddHours(-1);
        SetRuntimeField(runtime, "_sessionId", Guid.NewGuid().ToString("N"));
        SetRuntimeField(runtime, "_startedAtUtc", oldSessionStartedAtUtc);
        SetRuntimeField(runtime, "_preflightSucceeded", true);
        SetRuntimeField(runtime, "_readinessGatePersisted", true);
        SetRuntimeField(runtime, "_hostLifecycle", new RuntimeLifecycleSnapshotDto
        {
            SessionId = "existing-host-session",
            State = RuntimeLifecycleState.Ready,
            Readiness = RuntimeReadinessStatus.Ready,
            StartedAtUtc = oldSessionStartedAtUtc,
            StateChangedAtUtc = oldSessionStartedAtUtc,
            ActivePhase = "Serving",
            AcceptingWork = true,
            ShutdownRequested = false
        });
        // Leave the HTTP port unavailable to exercise the real open command's warning receipt
        // without launching a browser or requiring Windows processes in this regression test.
        var requestId = Guid.NewGuid().ToString("N");
        var baseline = StartupOutcomeReceiptMonitor.Capture(configuration.StartupOutcomeReceiptRoot, requestId);
        var launchedAtUtc = DateTimeOffset.UtcNow;
        var handler = typeof(LifecycleSupervisorRuntime).GetMethod(
            "HandleCommandAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var response = await (Task<LifecycleSupervisorMessageDto>)handler.Invoke(
            runtime,
            [new LifecycleSupervisorMessageDto { Command = "open", RequestId = requestId }])!;

        response.Success.Should().BeTrue();
        response.Reason.Should().Be(nameof(OperationTerminalState.CompletedWithWarnings));
        var retained = JsonSerializer.Deserialize(
            File.ReadAllBytes(response.Detail!),
            OperationsContractsJsonContext.Default.VerifiedOperationOutcome)!;
        retained.StartedAtUtc.Should().BeOnOrAfter(launchedAtUtc);
        retained.StartedAtUtc.Should().BeAfter(oldSessionStartedAtUtc);
        StartupOutcomeReceiptMonitor.TryReadChanged(
                configuration.StartupOutcomeReceiptRoot,
                baseline,
                LifecycleStartupOutcome.OperationKind,
                requestId,
                launchedAtUtc,
                out var accepted,
                out var receiptPath)
            .Should().BeTrue();
        accepted!.State.Should().Be(OperationTerminalState.CompletedWithWarnings);
        receiptPath.Should().Be(response.Detail);
    }

    private LifecycleSupervisorConfiguration CreateConfiguration()
    {
        var evidenceRoot = Path.Combine(_root, "evidence");
        return LifecycleSupervisorConfiguration.Load(_root) with
        {
            DataRoot = Path.Combine(_root, "data"),
            RuntimeRoot = Path.Combine(_root, "runtime"),
            ReceiptRoot = Path.Combine(_root, "runtime", "receipts"),
            StartupOutcomeReceiptRoot = Path.Combine(evidenceRoot, "receipts"),
            SupervisorLogPath = Path.Combine(evidenceRoot, "supervisor.log"),
            HostLogRoot = Path.Combine(evidenceRoot, "host"),
            DatabaseLogPath = Path.Combine(evidenceRoot, "postgresql.log")
        };
    }

    private static void SetRuntimeField(LifecycleSupervisorRuntime runtime, string name, object value)
        => typeof(LifecycleSupervisorRuntime)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(runtime, value);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
