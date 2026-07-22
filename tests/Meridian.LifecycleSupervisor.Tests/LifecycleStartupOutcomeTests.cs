using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Launcher;
using Xunit;

namespace Meridian.LifecycleSupervisor.Tests;

public sealed class LifecycleStartupOutcomeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-lifecycle-startup-outcome-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Persist_BlockedPreflightIsRequestBoundAndUsesUriOnlyEvidence()
    {
        var configuration = CreateConfiguration();
        var request = LifecycleStartupOutcome.CreateRequest(
            configuration,
            Guid.NewGuid().ToString("N"),
            browserRequested: true);

        var receipt = LifecycleStartupOutcome.Persist(
            configuration,
            request,
            sessionId: Guid.NewGuid().ToString("N"),
            startedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
            state: OperationTerminalState.Blocked,
            prerequisitesSatisfied: false,
            readinessSatisfied: false,
            terminalMessage: "Lifecycle supervisor preflight failed: host executable is missing.",
            browserRequested: true,
            exceptionType: nameof(LifecycleStartupBlockedException));

        receipt.Outcome.OperationId.Should().Be($"startup:{request.RequestId}");
        receipt.Outcome.CorrelationId.Should().Be(request.RequestId);
        receipt.Outcome.AttemptNumber.Should().Be(1);
        receipt.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        receipt.Outcome.Evidence.Should().OnlyContain(item => item.ContentHashSha256 == null);
        receipt.Outcome.Evidence.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.Uri));
        receipt.Outcome.Recovery.Should().Contain(item => item.ActionId == "repair-and-retry");
        VerifiedOperationOutcomeValidator.Validate(receipt.Outcome).Should().BeEmpty();
    }

    [Fact]
    public void Persist_BrowserFailureIsWarningAndDoesNotRetainBootstrapToken()
    {
        var configuration = CreateConfiguration();
        var request = LifecycleStartupOutcome.CreateRequest(
            configuration,
            Guid.NewGuid().ToString("N"),
            browserRequested: true);
        const string safeUrl = "http://127.0.0.1:8080/setup/account";

        var receipt = LifecycleStartupOutcome.Persist(
            configuration,
            request,
            sessionId: Guid.NewGuid().ToString("N"),
            startedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
            state: OperationTerminalState.CompletedWithWarnings,
            prerequisitesSatisfied: true,
            readinessSatisfied: true,
            terminalMessage: "Browser launch failed. Retry the open command or use the safe URL.",
            httpPort: 8080,
            browserRequested: true,
            browserOpened: false,
            browserUri: safeUrl,
            exceptionType: nameof(InvalidOperationException));

        receipt.Outcome.State.Should().Be(OperationTerminalState.CompletedWithWarnings);
        receipt.Outcome.Recovery.Should().Contain(item => item.ActionId == "open-manually");
        var json = File.ReadAllText(receipt.ReceiptPath);
        json.Should().Contain(safeUrl);
        json.Should().NotContain("#token=");
        VerifiedOperationOutcomeValidator.Validate(receipt.Outcome).Should().BeEmpty();
    }

    [Fact]
    public void Persist_RetryUsesNewAttemptFileAndPreservesPriorReceipt()
    {
        var configuration = CreateConfiguration();
        var requestId = Guid.NewGuid().ToString("N");
        var firstRequest = LifecycleStartupOutcome.CreateRequest(
            configuration,
            requestId,
            browserRequested: true);
        var first = PersistReady(configuration, firstRequest, "First attempt completed.");

        var secondRequest = LifecycleStartupOutcome.CreateRequest(
            configuration,
            requestId,
            browserRequested: true);
        var second = PersistReady(configuration, secondRequest, "Second attempt completed.");

        firstRequest.AttemptNumber.Should().Be(1);
        secondRequest.AttemptNumber.Should().Be(2);
        first.ReceiptPath.Should().NotBe(second.ReceiptPath);
        File.Exists(first.ReceiptPath).Should().BeTrue();
        File.Exists(second.ReceiptPath).Should().BeTrue();
        File.ReadAllText(first.ReceiptPath).Should().Contain("First attempt completed.");
        File.ReadAllText(second.ReceiptPath).Should().Contain("Second attempt completed.");
    }

    [Fact]
    public void Persist_ReadinessGateAndTerminalReceiptAreSeparateImmutableFiles()
    {
        var configuration = CreateConfiguration();
        var request = LifecycleStartupOutcome.CreateRequest(
            configuration,
            Guid.NewGuid().ToString("N"),
            browserRequested: true);
        var startedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);

        var readiness = LifecycleStartupOutcome.Persist(
            configuration,
            request,
            sessionId: Guid.NewGuid().ToString("N"),
            startedAtUtc: startedAtUtc,
            state: OperationTerminalState.Succeeded,
            prerequisitesSatisfied: true,
            readinessSatisfied: true,
            terminalMessage: "Exact Ready status was retained.",
            httpPort: 8080,
            browserRequested: true,
            browserOpened: false,
            browserUri: "http://127.0.0.1:8080/workstation/",
            readinessGateReceipt: true);
        var terminal = PersistReady(configuration, request, "Browser launch was accepted.", startedAtUtc);

        readiness.ReceiptPath.Should().NotBe(terminal.ReceiptPath);
        Path.GetFileName(readiness.ReceiptPath).Should().StartWith("startup-readiness-");
        Path.GetFileName(terminal.ReceiptPath).Should().StartWith("startup-terminal-");
        File.Exists(readiness.ReceiptPath).Should().BeTrue();
        File.Exists(terminal.ReceiptPath).Should().BeTrue();
    }

    [Fact]
    public async Task OpenOutcomeGate_CompletesOnlyMatchingRequest()
    {
        var configuration = CreateConfiguration();
        var firstRequest = LifecycleStartupOutcome.CreateRequest(
            configuration,
            Guid.NewGuid().ToString("N"),
            browserRequested: true);
        var secondRequest = LifecycleStartupOutcome.CreateRequest(
            configuration,
            Guid.NewGuid().ToString("N"),
            browserRequested: true);
        var gate = new LifecycleOpenOutcomeGate();
        var firstWait = gate.WaitAsync(firstRequest.RequestId, TimeSpan.FromSeconds(5));
        var secondWait = gate.WaitAsync(secondRequest.RequestId, TimeSpan.FromSeconds(5));
        var firstReceipt = PersistReady(configuration, firstRequest, "First request completed.");

        gate.Complete(firstRequest.RequestId, firstReceipt);

        (await firstWait).Should().BeSameAs(firstReceipt);
        secondWait.IsCompleted.Should().BeFalse();
        var secondReceipt = PersistReady(configuration, secondRequest, "Second request completed.");
        gate.Complete(secondRequest.RequestId, secondReceipt);
        (await secondWait).Should().BeSameAs(secondReceipt);
    }

    [Fact]
    public void LauncherMonitor_AcceptsOnlyTheExpectedRequest()
    {
        var configuration = CreateConfiguration();
        var expectedRequestId = Guid.NewGuid().ToString("N");
        var unrelatedRequest = LifecycleStartupOutcome.CreateRequest(
            configuration,
            Guid.NewGuid().ToString("N"),
            browserRequested: true);
        var baseline = StartupOutcomeReceiptMonitor.Capture(configuration.StartupOutcomeReceiptRoot);
        var launchedAtUtc = DateTimeOffset.UtcNow;
        PersistReady(configuration, unrelatedRequest, "Unrelated request completed.", launchedAtUtc);

        StartupOutcomeReceiptMonitor.TryReadChanged(
                configuration.StartupOutcomeReceiptRoot,
                baseline,
                LifecycleStartupOutcome.OperationKind,
                expectedRequestId,
                launchedAtUtc,
                out _,
                out _)
            .Should().BeFalse();

        var expectedRequest = LifecycleStartupOutcome.CreateRequest(
            configuration,
            expectedRequestId,
            browserRequested: true);
        var expected = PersistReady(configuration, expectedRequest, "Expected request completed.", launchedAtUtc);

        StartupOutcomeReceiptMonitor.TryReadChanged(
                configuration.StartupOutcomeReceiptRoot,
                baseline,
                LifecycleStartupOutcome.OperationKind,
                expectedRequestId,
                launchedAtUtc,
                out var outcome,
                out var path)
            .Should().BeTrue();
        path.Should().Be(expected.ReceiptPath);
        outcome!.CorrelationId.Should().Be(expectedRequestId);
    }

    [Fact]
    public void LauncherMonitor_RejectsTamperedOperationIdentity()
    {
        var configuration = CreateConfiguration();
        var requestId = Guid.NewGuid().ToString("N");
        var request = LifecycleStartupOutcome.CreateRequest(configuration, requestId, browserRequested: true);
        var baseline = StartupOutcomeReceiptMonitor.Capture(configuration.StartupOutcomeReceiptRoot);
        var launchedAtUtc = DateTimeOffset.UtcNow;
        var receipt = PersistReady(configuration, request, "Startup completed.", launchedAtUtc);
        var tampered = receipt.Outcome with { OperationId = "startup:different-request" };
        File.WriteAllText(
            receipt.ReceiptPath,
            JsonSerializer.Serialize(
                tampered,
                OperationsContractsJsonContext.Default.VerifiedOperationOutcome));

        StartupOutcomeReceiptMonitor.TryReadChanged(
                configuration.StartupOutcomeReceiptRoot,
                baseline,
                LifecycleStartupOutcome.OperationKind,
                requestId,
                launchedAtUtc,
                out _,
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void LauncherMonitor_RejectsReceiptWhoseOperationStartedBeforeLaunch()
    {
        var configuration = CreateConfiguration();
        var requestId = Guid.NewGuid().ToString("N");
        var request = LifecycleStartupOutcome.CreateRequest(configuration, requestId, browserRequested: true);
        var baseline = StartupOutcomeReceiptMonitor.Capture(configuration.StartupOutcomeReceiptRoot);
        var launchedAtUtc = DateTimeOffset.UtcNow;
        PersistReady(
            configuration,
            request,
            "A stale request completed after this launcher started.",
            launchedAtUtc.AddMinutes(-1));

        StartupOutcomeReceiptMonitor.TryReadChanged(
                configuration.StartupOutcomeReceiptRoot,
                baseline,
                LifecycleStartupOutcome.OperationKind,
                requestId,
                launchedAtUtc,
                out _,
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void LauncherFailureReceipt_IsValidatedAndNonOverwriting()
    {
        var receiptRoot = Path.Combine(_root, "launcher-receipts");
        var requestId = Guid.NewGuid().ToString("N");
        var startedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        var supervisorPath = Path.Combine(_root, "Meridian.LifecycleSupervisor.exe");
        var supervisorLogPath = Path.Combine(_root, "logs", "lifecycle-supervisor.log");

        var firstPath = StartupOutcomeReceiptMonitor.PersistLauncherFailure(
            receiptRoot,
            requestId,
            startedAtUtc,
            OperationTerminalState.Failed,
            "The supervisor process exited before producing a receipt.",
            supervisorPath,
            supervisorLogPath,
            processStarted: true,
            exceptionType: "SupervisorProcessExit");
        var secondPath = StartupOutcomeReceiptMonitor.PersistLauncherFailure(
            receiptRoot,
            requestId,
            startedAtUtc,
            OperationTerminalState.Failed,
            "The retry also failed.",
            supervisorPath,
            supervisorLogPath,
            processStarted: true,
            exceptionType: "SupervisorProcessExit");

        firstPath.Should().NotBe(secondPath);
        var first = ReadOutcome(firstPath);
        var second = ReadOutcome(secondPath);
        first.AttemptNumber.Should().Be(1);
        second.AttemptNumber.Should().Be(2);
        VerifiedOperationOutcomeValidator.Validate(first).Should().BeEmpty();
        VerifiedOperationOutcomeValidator.Validate(second).Should().BeEmpty();
    }

    [Fact]
    public void PersistConfigurationBlocked_BindsLauncherRequestAndWritesDiagnosticLog()
    {
        var requestId = Guid.NewGuid().ToString("N");

        var receipt = LifecycleStartupOutcome.PersistConfigurationBlocked(
            _root,
            new JsonException("Manifest JSON is malformed."),
            requestId);

        try
        {
            receipt.Outcome.State.Should().Be(OperationTerminalState.Blocked);
            receipt.Outcome.CorrelationId.Should().Be(requestId);
            receipt.Outcome.OperationId.Should().Be($"startup:{requestId}");
            var logUri = receipt.Outcome.Evidence.Single(item => item.EvidenceId == "supervisor-log").Uri;
            File.Exists(new Uri(logUri!).LocalPath).Should().BeTrue();
            VerifiedOperationOutcomeValidator.Validate(receipt.Outcome).Should().BeEmpty();
        }
        finally
        {
            File.Delete(receipt.ReceiptPath);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void LauncherExitWithoutVerifiedReceipt_FailsClosed(int supervisorExitCode)
    {
        Meridian.Launcher.Program
            .ClassifySupervisorExitWithoutVerifiedOutcome(supervisorExitCode)
            .Should().Be(1);
    }

    private static LifecycleStartupOutcomeReceipt PersistReady(
        LifecycleSupervisorConfiguration configuration,
        LifecycleStartupOperationRequest request,
        string message,
        DateTimeOffset? startedAtUtc = null)
        => LifecycleStartupOutcome.Persist(
            configuration,
            request,
            sessionId: Guid.NewGuid().ToString("N"),
            startedAtUtc: startedAtUtc ?? DateTimeOffset.UtcNow.AddSeconds(-1),
            state: OperationTerminalState.Succeeded,
            prerequisitesSatisfied: true,
            readinessSatisfied: true,
            terminalMessage: message,
            httpPort: 8080,
            browserRequested: true,
            browserOpened: true,
            browserUri: "http://127.0.0.1:8080/workstation/");

    private static VerifiedOperationOutcome ReadOutcome(string path)
        => JsonSerializer.Deserialize(
               File.ReadAllBytes(path),
               OperationsContractsJsonContext.Default.VerifiedOperationOutcome)
           ?? throw new InvalidDataException("Expected a launcher outcome receipt.");

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
