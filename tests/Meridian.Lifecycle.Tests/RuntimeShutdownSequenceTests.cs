using FluentAssertions;
using Meridian.Application.Composition.Startup;
using Meridian.Contracts.Lifecycle;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Xunit;

namespace Meridian.Tests.Application.Composition.Startup;

public sealed class RuntimeShutdownSequenceTests
{
    private readonly ILogger _log = new LoggerConfiguration().CreateLogger();

    [Fact]
    public async Task ExecuteAsync_OrdersParticipantsPersistsReceiptAndDoesNotReleaseTerminationEarly()
    {
        using var lifecycle = ApplicationLifecycleCoordinator.Create(_log);
        await lifecycle.RequestShutdownAsync(new LifecycleShutdownRequestDto
        {
            Reason = LifecycleShutdownReason.Supervisor
        });
        var executionOrder = new List<string>();
        var store = new RecordingReceiptStore();
        var sequence = CreateSequence(
            lifecycle,
            store,
            [
                new RecordingParticipant("flush-b", LifecycleShutdownStage.Flushing, 20, executionOrder),
                new RecordingParticipant("drain", LifecycleShutdownStage.Draining, 100, executionOrder),
                new RecordingParticipant("flush-a", LifecycleShutdownStage.Flushing, 10, executionOrder)
            ]);

        var receipt = await sequence.ExecuteAsync();

        executionOrder.Should().Equal("drain", "flush-a", "flush-b");
        receipt.Outcome.Should().Be(LifecycleShutdownOutcome.Succeeded);
        store.HostReceipt.Should().Be(receipt);
        lifecycle.TerminationToken.IsCancellationRequested.Should().BeFalse();
        lifecycle.ActiveShutdownOperation!.CurrentStage.Should().Be(LifecycleShutdownStage.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_CriticalParticipantFails_RecordsFailureAndContinuesEvidenceCollection()
    {
        using var lifecycle = ApplicationLifecycleCoordinator.Create(_log);
        await lifecycle.RequestShutdownAsync(new LifecycleShutdownRequestDto
        {
            Reason = LifecycleShutdownReason.Operator
        });
        var executionOrder = new List<string>();
        var store = new RecordingReceiptStore();
        var sequence = CreateSequence(
            lifecycle,
            store,
            [
                new RecordingParticipant(
                    "critical-drain",
                    LifecycleShutdownStage.Draining,
                    10,
                    executionOrder,
                    critical: true,
                    exception: new InvalidOperationException("failure detail")),
                new RecordingParticipant("flush", LifecycleShutdownStage.Flushing, 10, executionOrder)
            ]);

        var receipt = await sequence.ExecuteAsync();

        executionOrder.Should().Equal("critical-drain", "flush");
        receipt.Outcome.Should().Be(LifecycleShutdownOutcome.Failed);
        receipt.Participants[0].Message.Should().Be(nameof(InvalidOperationException));
        receipt.Participants[0].Message.Should().NotContain("failure detail");
        store.HostReceipt.Should().Be(receipt);
    }

    private static RuntimeShutdownSequence CreateSequence(
        IRuntimeLifecycleControlPlane lifecycle,
        ILifecycleReceiptStore store,
        IEnumerable<IRuntimeShutdownParticipant> participants)
        => new(
            lifecycle,
            store,
            participants,
            new RuntimeShutdownOptions { ParticipantTimeout = TimeSpan.FromSeconds(1) },
            NullLogger<RuntimeShutdownSequence>.Instance);

    private sealed class RecordingParticipant : IRuntimeShutdownParticipant
    {
        private readonly List<string> _executionOrder;
        private readonly Exception? _exception;

        public RecordingParticipant(
            string id,
            LifecycleShutdownStage stage,
            int order,
            List<string> executionOrder,
            bool critical = false,
            Exception? exception = null)
        {
            Id = id;
            Stage = stage;
            Order = order;
            IsCritical = critical;
            _executionOrder = executionOrder;
            _exception = exception;
        }

        public string Id { get; }
        public LifecycleShutdownStage Stage { get; }
        public int Order { get; }
        public bool IsCritical { get; }

        public ValueTask ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _executionOrder.Add(Id);
            return _exception is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(_exception);
        }
    }

    private sealed class RecordingReceiptStore : ILifecycleReceiptStore
    {
        public LifecycleShutdownReceiptDto? HostReceipt { get; private set; }

        public ValueTask WriteHostReceiptAsync(LifecycleShutdownReceiptDto receipt, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            HostReceipt = receipt;
            return ValueTask.CompletedTask;
        }

        public ValueTask<LifecycleShutdownReceiptDto?> ReadLatestHostReceiptAsync(CancellationToken ct = default)
            => ValueTask.FromResult(HostReceipt);

        public ValueTask WriteSessionReceiptAsync(LifecycleSessionReceiptDto receipt, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}
