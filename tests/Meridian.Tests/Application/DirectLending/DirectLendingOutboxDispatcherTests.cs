using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Reflection;

namespace Meridian.Tests.Application.DirectLending;

public sealed class DirectLendingOutboxDispatcherTests
{
    [Fact]
    public async Task ExecuteAsync_WhenPollingStoreThrows_KeepsFailureInsideWorkerLoop()
    {
        var operationsStore = Substitute.For<IDirectLendingOperationsStore>();
        var commandService = Substitute.For<IDirectLendingCommandService>();
        var queryService = Substitute.For<IDirectLendingQueryService>();
        using var cts = new CancellationTokenSource();
        var pollAttempts = 0;

        operationsStore
            .GetPendingOutboxMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                pollAttempts++;
                cts.Cancel();
                return Task.FromException<IReadOnlyList<DirectLendingOutboxMessage>>(new InvalidOperationException("Database unavailable."));
            });

        var dispatcher = new DirectLendingOutboxDispatcher(
            operationsStore,
            commandService,
            queryService,
            new DirectLendingOptions
            {
                OutboxBatchSize = 25,
                OutboxPollIntervalSeconds = 1
            },
            NullLogger<DirectLendingOutboxDispatcher>.Instance);

        var act = async () =>
        {
            var executeAsync = typeof(DirectLendingOutboxDispatcher).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            executeAsync.Should().NotBeNull();

            var task = (Task)executeAsync!.Invoke(dispatcher, new object[] { cts.Token })!;
            await task.ConfigureAwait(false);
        };

        await act.Should().NotThrowAsync();
        pollAttempts.Should().Be(1);
        await operationsStore.DidNotReceiveWithAnyArgs().MarkOutboxProcessedAsync(default, default);
        await operationsStore.DidNotReceiveWithAnyArgs().MarkOutboxFailedAsync(default, default!, default);
    }
}
