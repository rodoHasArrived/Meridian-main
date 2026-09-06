using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Application.DirectLending;

public sealed class DirectLendingOutboxFailureTests
{
    [Theory]
    [InlineData("direct-lending.projection.requested")]
    [InlineData("direct-lending.reconciliation.requested")]
    public async Task RejectedCommand_RemainsRetryable_AndIsAcknowledgedOnlyAfterSuccess(string topic)
    {
        var loanId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 5, 1);
        var now = DateTimeOffset.UtcNow;
        var projection = new ProjectionRunDto(Guid.NewGuid(), loanId, 1, 1, asOf, null,
            sourceEventId, "Retry", "terms-hash", "test", ProjectionRunStatus.Completed, null, now);
        var reconciliation = new ReconciliationRunDto(Guid.NewGuid(), loanId,
            projection.ProjectionRunId, now, now, "Completed");
        var message = new DirectLendingOutboxMessage(Guid.NewGuid(), topic, loanId.ToString("N"),
            JsonSerializer.Serialize(new
            {
                loanId,
                sourceEventId,
                commandId,
                effectiveDate = asOf,
                eventType = "LoanActivated",
                servicingRevision = 1,
                sourceSystem = "test"
            }),
            null, now, now, null, 0, null);
        var store = Substitute.For<IDirectLendingOperationsStore>();
        var commands = Substitute.For<IDirectLendingCommandService>();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var attempts = 0;
        store.GetPendingOutboxMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DirectLendingOutboxMessage>>([message]));
        commands.RequestProjectionAsync(loanId, asOf, Arg.Any<DirectLendingCommandMetadataDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(++attempts == 1
                ? DirectLendingCommandResult<ProjectionRunDto>.Failure(DirectLendingErrorCode.ConcurrencyConflict, "retry projection")
                : DirectLendingCommandResult<ProjectionRunDto>.Success(projection)));
        commands.ReconcileAsync(loanId, Arg.Any<DirectLendingCommandMetadataDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(++attempts == 1
                ? DirectLendingCommandResult<ReconciliationRunDto>.Failure(DirectLendingErrorCode.ConcurrencyConflict, "retry reconciliation")
                : DirectLendingCommandResult<ReconciliationRunDto>.Success(reconciliation)));
        store.MarkOutboxProcessedAsync(message.OutboxMessageId, Arg.Any<CancellationToken>())
            .Returns(_ => { lifetime.Cancel(); return Task.CompletedTask; });
        using var worker = new DirectLendingOutboxDispatcher(store, commands,
            Substitute.For<IDirectLendingQueryService>(), new DirectLendingOptions(),
            NullLogger<DirectLendingOutboxDispatcher>.Instance);
        var execute = typeof(DirectLendingOutboxDispatcher).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await ((Task)execute.Invoke(worker, [lifetime.Token])!).WaitAsync(TimeSpan.FromSeconds(10));

        attempts.Should().Be(2, "a failure result must not acknowledge the durable message");
        await store.Received(1).MarkOutboxFailedAsync(message.OutboxMessageId,
            Arg.Is<string>(error => error.StartsWith("retry ")), Arg.Any<CancellationToken>());
        await store.Received(1).MarkOutboxProcessedAsync(message.OutboxMessageId, Arg.Any<CancellationToken>());
        await store.Received(2).GetPendingOutboxMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
