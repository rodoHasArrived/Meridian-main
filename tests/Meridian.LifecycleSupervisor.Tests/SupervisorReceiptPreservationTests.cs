using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Operations;
using Xunit;

namespace Meridian.LifecycleSupervisor.Tests;

public sealed class SupervisorReceiptPreservationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-supervisor-receipt-preservation-tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("terminal", "corrupted prior terminal evidence")]
    [InlineData("terminal", "")]
    [InlineData("readiness", "{")]
    [InlineData("readiness", "")]
    public void CreateRequest_MalformedPriorReceiptReservesItsAttemptAndPreservesEvidence(
        string receiptKind,
        string priorContents)
    {
        var configuration = CreateConfiguration();
        var requestId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(configuration.StartupOutcomeReceiptRoot);
        var priorPath = Path.Combine(
            configuration.StartupOutcomeReceiptRoot,
            $"startup-{receiptKind}-{requestId}-attempt-0001.verified-outcome.json");
        File.WriteAllText(priorPath, priorContents);

        var request = LifecycleStartupOutcome.CreateRequest(configuration, requestId, browserRequested: false);
        var receipt = PersistReady(configuration, request, "Retry completed.");

        request.AttemptNumber.Should().Be(2);
        receipt.Outcome.AttemptNumber.Should().Be(2);
        Path.GetFileName(receipt.ReceiptPath)
            .Should().Be($"startup-terminal-{requestId}-attempt-0002.verified-outcome.json");
        File.ReadAllText(priorPath).Should().Be(priorContents);
        VerifiedOperationOutcomeValidator.Validate(receipt.Outcome).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Persist_RequestsSelectingTheSameAttemptCannotOverwriteEachOther(bool readinessGateReceipt)
    {
        var configuration = CreateConfiguration();
        var requestId = Guid.NewGuid().ToString("N");
        var firstRequest = LifecycleStartupOutcome.CreateRequest(configuration, requestId, browserRequested: false);
        var concurrentRequest = LifecycleStartupOutcome.CreateRequest(configuration, requestId, browserRequested: false);
        firstRequest.AttemptNumber.Should().Be(concurrentRequest.AttemptNumber);
        var firstReceipt = PersistReady(configuration, firstRequest, "First retained evidence.", readinessGateReceipt);
        var retainedContents = File.ReadAllText(firstReceipt.ReceiptPath);

        Action persistCollision = () => PersistReady(
            configuration, concurrentRequest, "Concurrent evidence must not replace the first receipt.", readinessGateReceipt);

        persistCollision.Should().Throw<IOException>();
        File.ReadAllText(firstReceipt.ReceiptPath).Should().Be(retainedContents);
        Directory.EnumerateFiles(configuration.StartupOutcomeReceiptRoot).Should().ContainSingle();
    }

    [Fact]
    public void PersistConfigurationBlocked_CorruptedPriorReceiptUsesNewAttemptAndPreservesEvidence()
    {
        var requestId = Guid.NewGuid().ToString("N");
        var first = LifecycleStartupOutcome.PersistConfigurationBlocked(
            _root, new JsonException("First malformed manifest."), requestId);
        LifecycleStartupOutcomeReceipt? second = null;
        try
        {
            const string priorContents = "incomplete retained configuration evidence";
            File.WriteAllText(first.ReceiptPath, priorContents);

            second = LifecycleStartupOutcome.PersistConfigurationBlocked(
                _root, new JsonException("Manifest is still malformed."), requestId);

            second.Outcome.AttemptNumber.Should().Be(2);
            second.Outcome.State.Should().Be(OperationTerminalState.Blocked);
            second.ReceiptPath.Should().NotBe(first.ReceiptPath);
            File.ReadAllText(first.ReceiptPath).Should().Be(priorContents);
            VerifiedOperationOutcomeValidator.Validate(second.Outcome).Should().BeEmpty();
        }
        finally
        {
            File.Delete(first.ReceiptPath);
            if (second is not null)
                File.Delete(second.ReceiptPath);
        }
    }

    private static LifecycleStartupOutcomeReceipt PersistReady(
        LifecycleSupervisorConfiguration configuration,
        LifecycleStartupOperationRequest request,
        string message,
        bool readinessGateReceipt = false)
        => LifecycleStartupOutcome.Persist(
            configuration,
            request,
            sessionId: Guid.NewGuid().ToString("N"),
            startedAtUtc: request.StartedAtUtc,
            state: OperationTerminalState.Succeeded,
            prerequisitesSatisfied: true,
            readinessSatisfied: true,
            terminalMessage: message,
            httpPort: 8080,
            readinessGateReceipt: readinessGateReceipt);

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
