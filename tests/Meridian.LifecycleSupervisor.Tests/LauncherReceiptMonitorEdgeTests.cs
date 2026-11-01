using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Launcher;
using Xunit;

namespace Meridian.LifecycleSupervisor.Tests;

/// <summary>
/// Edge cases of the launcher's startup-outcome observation: request-id validation (the id is
/// spliced into a file search pattern), stale baseline receipts, in-place rewrites, and the
/// strict argument policy.
/// </summary>
public sealed class LauncherReceiptMonitorEdgeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-launcher-receipt-edge-tests",
        Guid.NewGuid().ToString("N"));

    // ── Command policy ───────────────────────────────────────────────────────

    [Fact]
    public void TryResolveStartupCommand_NullArgumentList_Throws()
    {
        var act = () => LauncherCommandPolicy.TryResolveStartupCommand(null!, out _);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" start")]
    [InlineData("start ")]
    [InlineData("open\n")]
    public void TryResolveStartupCommand_NullOrPaddedCommand_IsRejected(string? argument)
    {
        LauncherCommandPolicy.TryResolveStartupCommand([argument!], out _).Should().BeFalse();
    }

    // ── Request-id validation ────────────────────────────────────────────────

    [Theory]
    [InlineData("*")]
    [InlineData("../other")]
    [InlineData("not-a-guid")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("")]
    public void CaptureAndTryReadChanged_NonNormalizedRequestId_AreRejectedBeforeSearching(string requestId)
    {
        var receiptRoot = Path.Combine(_root, "receipts");
        Directory.CreateDirectory(receiptRoot);

        var capture = () => StartupOutcomeReceiptMonitor.Capture(receiptRoot, requestId);
        var read = () => StartupOutcomeReceiptMonitor.TryReadChanged(
            receiptRoot,
            new Dictionary<string, StartupOutcomeReceiptFingerprint>(),
            LifecycleStartupOutcome.OperationKind,
            requestId,
            DateTimeOffset.UtcNow,
            out _,
            out _);

        capture.Should().Throw<ArgumentException>().WithMessage("*normalized launch request ID*");
        read.Should().Throw<ArgumentException>().WithMessage("*normalized launch request ID*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryReadChanged_BlankOperationKind_Throws(string operationKind)
    {
        var act = () => StartupOutcomeReceiptMonitor.TryReadChanged(
            Path.Combine(_root, "receipts"),
            new Dictionary<string, StartupOutcomeReceiptFingerprint>(),
            operationKind,
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            out _,
            out _);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CaptureAndTryReadChanged_MissingReceiptRoot_ObserveNothing()
    {
        var missing = Path.Combine(_root, "never-created");
        var requestId = Guid.NewGuid().ToString("N");

        StartupOutcomeReceiptMonitor.Capture(missing).Should().BeEmpty();
        StartupOutcomeReceiptMonitor.Capture(missing, requestId).Should().BeEmpty();
        StartupOutcomeReceiptMonitor.TryReadChanged(
                missing,
                new Dictionary<string, StartupOutcomeReceiptFingerprint>(),
                LifecycleStartupOutcome.OperationKind,
                requestId,
                DateTimeOffset.UtcNow,
                out var outcome,
                out var path)
            .Should().BeFalse();
        outcome.Should().BeNull();
        path.Should().BeNull();
    }

    // ── Baseline and rewrite handling ────────────────────────────────────────

    [Fact]
    public void TryReadChanged_ReceiptAlreadyInBaselineAndUnchanged_IsNotReportedAsThisLaunch()
    {
        var configuration = CreateConfiguration();
        var requestId = Guid.NewGuid().ToString("N");
        var request = LifecycleStartupOutcome.CreateRequest(configuration, requestId, browserRequested: true);
        var launchedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        PersistReady(configuration, request, launchedAtUtc.AddMinutes(1));
        var baseline = StartupOutcomeReceiptMonitor.Capture(configuration.StartupOutcomeReceiptRoot, requestId);
        baseline.Should().ContainSingle();

        StartupOutcomeReceiptMonitor.TryReadChanged(
                configuration.StartupOutcomeReceiptRoot,
                baseline,
                LifecycleStartupOutcome.OperationKind,
                requestId,
                launchedAtUtc,
                out _,
                out _)
            .Should().BeFalse("a receipt that predates observation and never changed is stale evidence");
    }

    [Fact]
    public void TryReadChanged_BaselineReceiptRewrittenWithDifferentStartTime_IsRejected()
    {
        var configuration = CreateConfiguration();
        var requestId = Guid.NewGuid().ToString("N");
        var request = LifecycleStartupOutcome.CreateRequest(configuration, requestId, browserRequested: true);
        var launchedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        var receipt = PersistReady(configuration, request, launchedAtUtc.AddMinutes(1));
        var baseline = StartupOutcomeReceiptMonitor.Capture(configuration.StartupOutcomeReceiptRoot, requestId);
        var rewritten = receipt.Outcome with { StartedAtUtc = receipt.Outcome.StartedAtUtc.AddSeconds(1) };
        VerifiedOperationOutcomeValidator.Validate(rewritten).Should().BeEmpty("the rewrite must otherwise be a valid receipt");
        WriteOutcome(receipt.ReceiptPath, rewritten);

        StartupOutcomeReceiptMonitor.TryReadChanged(
                configuration.StartupOutcomeReceiptRoot,
                baseline,
                LifecycleStartupOutcome.OperationKind,
                requestId,
                launchedAtUtc,
                out _,
                out _)
            .Should().BeFalse("an in-place rewrite must keep the original operation identity and start time");
    }

    [Fact]
    public void TryReadChanged_BaselineReceiptCompletedInPlaceWithSameIdentity_IsAccepted()
    {
        var configuration = CreateConfiguration();
        var requestId = Guid.NewGuid().ToString("N");
        var request = LifecycleStartupOutcome.CreateRequest(configuration, requestId, browserRequested: true);
        var launchedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        var receipt = PersistReady(configuration, request, launchedAtUtc.AddMinutes(1));
        var baseline = StartupOutcomeReceiptMonitor.Capture(configuration.StartupOutcomeReceiptRoot, requestId);
        var completedLater = receipt.Outcome with { CompletedAtUtc = receipt.Outcome.CompletedAtUtc.AddSeconds(2) };
        VerifiedOperationOutcomeValidator.Validate(completedLater).Should().BeEmpty();
        WriteOutcome(receipt.ReceiptPath, completedLater);

        StartupOutcomeReceiptMonitor.TryReadChanged(
                configuration.StartupOutcomeReceiptRoot,
                baseline,
                LifecycleStartupOutcome.OperationKind,
                requestId,
                launchedAtUtc,
                out var outcome,
                out var path)
            .Should().BeTrue();
        path.Should().Be(receipt.ReceiptPath);
        outcome!.CompletedAtUtc.Should().Be(completedLater.CompletedAtUtc);
    }

    [Fact]
    public void TryReadChanged_DifferentOperationKind_IsRejected()
    {
        var configuration = CreateConfiguration();
        var requestId = Guid.NewGuid().ToString("N");
        var request = LifecycleStartupOutcome.CreateRequest(configuration, requestId, browserRequested: true);
        var baseline = StartupOutcomeReceiptMonitor.Capture(configuration.StartupOutcomeReceiptRoot, requestId);
        var launchedAtUtc = DateTimeOffset.UtcNow;
        PersistReady(configuration, request, launchedAtUtc);

        StartupOutcomeReceiptMonitor.TryReadChanged(
                configuration.StartupOutcomeReceiptRoot,
                baseline,
                "lifecycle.shutdown",
                requestId,
                launchedAtUtc,
                out _,
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TryReadChanged_UnparseableReceiptForTheRequest_IsIgnored()
    {
        var configuration = CreateConfiguration();
        var requestId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(configuration.StartupOutcomeReceiptRoot);
        var garbage = Path.Combine(
            configuration.StartupOutcomeReceiptRoot,
            $"startup-terminal-{requestId}-attempt-0001.verified-outcome.json");
        File.WriteAllText(garbage, "{ truncated");

        StartupOutcomeReceiptMonitor.Capture(configuration.StartupOutcomeReceiptRoot, requestId).Should().BeEmpty();
        StartupOutcomeReceiptMonitor.TryReadChanged(
                configuration.StartupOutcomeReceiptRoot,
                new Dictionary<string, StartupOutcomeReceiptFingerprint>(),
                LifecycleStartupOutcome.OperationKind,
                requestId,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                out _,
                out _)
            .Should().BeFalse();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LifecycleStartupOutcomeReceipt PersistReady(
        LifecycleSupervisorConfiguration configuration,
        LifecycleStartupOperationRequest request,
        DateTimeOffset startedAtUtc)
        => LifecycleStartupOutcome.Persist(
            configuration,
            request,
            sessionId: Guid.NewGuid().ToString("N"),
            startedAtUtc: startedAtUtc,
            state: OperationTerminalState.Succeeded,
            prerequisitesSatisfied: true,
            readinessSatisfied: true,
            terminalMessage: "Startup completed.",
            httpPort: 8080,
            browserRequested: true,
            browserOpened: true,
            browserUri: "http://127.0.0.1:8080/workstation/");

    private static void WriteOutcome(string path, VerifiedOperationOutcome outcome)
        => File.WriteAllText(
            path,
            JsonSerializer.Serialize(outcome, OperationsContractsJsonContext.Default.VerifiedOperationOutcome));

    private LifecycleSupervisorConfiguration CreateConfiguration()
    {
        var configuration = LifecycleSupervisorConfiguration.Load(_root);
        var evidenceRoot = Path.Combine(_root, "evidence");
        return configuration with
        {
            StartupOutcomeReceiptRoot = Path.Combine(evidenceRoot, "receipts"),
            SupervisorLogPath = Path.Combine(evidenceRoot, "supervisor.log"),
            HostLogRoot = Path.Combine(evidenceRoot, "host"),
            DatabaseLogPath = Path.Combine(evidenceRoot, "postgresql.log")
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
