using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class OrderManagementLifecycleCoordinatorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StartupTokenCancelledAfterStart_DoesNotCancelReportIntakeOrOperationAdmission()
    {
        var incoming = Channel.CreateUnbounded<ExecutionReport>();
        var started = Signal();
        var handled = new TaskCompletionSource<ExecutionReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = Signal();
        async Task RunReportsAsync(CancellationToken ct)
        {
            started.TrySetResult();
            try
            {
                await foreach (var report in incoming.Reader.ReadAllAsync(ct))
                    handled.TrySetResult(report);
            }
            finally
            {
                stopped.TrySetResult();
            }
        }
        await using var lifecycle = CreateLifecycle(RunReportsAsync, out _);
        using var startup = new CancellationTokenSource();
        await lifecycle.StartAsync(startup.Token);
        await started.Task.WaitAsync(TestTimeout);

        startup.Cancel();

        using (lifecycle.EnterOperation())
        {
            var report = Report("after-startup-cancellation");
            incoming.Writer.TryWrite(report).Should().BeTrue();
            (await handled.Task.WaitAsync(TestTimeout)).Should().BeSameAs(report);
        }
        stopped.Task.IsCompleted.Should().BeFalse();
        await lifecycle.StopAsync().WaitAsync(TestTimeout);
        stopped.Task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task Stop_WaitsForEveryAdmittedOperation_AndDuplicateReleaseCannotFinishTheDrain()
    {
        var started = Signal();
        var cancellationObserved = Signal();
        async Task RunReportsAsync(CancellationToken ct)
        {
            using var registration = ct.Register(() => cancellationObserved.TrySetResult());
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        await using var lifecycle = CreateLifecycle(RunReportsAsync, out var hub);
        await lifecycle.StartAsync();
        await started.Task.WaitAsync(TestTimeout);
        using var first = lifecycle.EnterOperation();
        using var second = lifecycle.EnterOperation();

        var shutdown = lifecycle.StopAsync();
        Assert.Throws<ObjectDisposedException>(() => lifecycle.EnterOperation());
        first.Dispose();
        first.Dispose();

        shutdown.IsCompleted.Should().BeFalse("the second admitted operation still owns its work");
        cancellationObserved.Task.IsCompleted.Should().BeFalse(
            "report intake remains active until every admitted operation has settled");
        second.Dispose();
        await shutdown.WaitAsync(TestTimeout);
        cancellationObserved.Task.IsCompletedSuccessfully.Should().BeTrue();
        hub.Observer.Completion.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Stop_JoinsReportIntakeAndSelectedRetainedHandoffBeforeCompletingSubscribers(
        bool finishReportsFirst)
    {
        var reportStarted = Signal();
        var cancellationObserved = Signal();
        var releaseReport = Signal();
        var reportFinished = Signal();
        var publisher = new BlockingPublisher();
        var fill = new TradeExecutedEvent(Guid.NewGuid(), "retained", "AAPL", OrderSide.Buy,
            1m, 100m, 0m, 0m, 0m, DateTimeOffset.UtcNow);
        var store = new RetainedFailureStore(fill);
        var accounting = new AccountingHandoffCoordinator(publisher, store, NullLogger.Instance);
        ExecutionReportHub? reports = null;
        var report = Report("admitted-report");
        async Task RunReportsAsync(CancellationToken ct)
        {
            using var registration = ct.Register(() => cancellationObserved.TrySetResult());
            reportStarted.TrySetResult();
            await releaseReport.Task;
            await reports!.PublishAsync(report, new ExecutionReportHub.DeliveryCheckpoint());
            reportFinished.TrySetResult();
        }
        await using var lifecycle = CreateLifecycle(RunReportsAsync, out var hub, accounting);
        reports = hub;
        await using var subscriber = lifecycle.SubscribeExecutionReports(1, "test-consumer", null);
        try
        {
            await lifecycle.StartAsync();
            await Task.WhenAll(reportStarted.Task, publisher.Started.Task).WaitAsync(TestTimeout);
            var shutdown = lifecycle.StopAsync();
            await cancellationObserved.Task.WaitAsync(TestTimeout);

            if (finishReportsFirst)
            {
                releaseReport.TrySetResult();
                await reportFinished.Task.WaitAsync(TestTimeout);
                (await subscriber.Reports.ReadAsync().AsTask().WaitAsync(TestTimeout)).Should().BeSameAs(report);
                shutdown.IsCompleted.Should().BeFalse("the selected retained fill has not reached accounting yet");
                subscriber.Reports.Completion.IsCompleted.Should().BeFalse();
                publisher.Release.TrySetResult();
            }
            else
            {
                publisher.Release.TrySetResult();
                await store.Acknowledged.Task.WaitAsync(TestTimeout);
                shutdown.IsCompleted.Should().BeFalse("the report worker still owes its admitted fill");
                subscriber.Reports.Completion.IsCompleted.Should().BeFalse();
                releaseReport.TrySetResult();
                (await subscriber.Reports.ReadAsync().AsTask().WaitAsync(TestTimeout)).Should().BeSameAs(report);
            }

            await shutdown.WaitAsync(TestTimeout);
            store.AcknowledgedFillId.Should().Be(fill.FillId);
            store.AcknowledgmentToken.CanBeCanceled.Should().BeFalse();
            subscriber.Reports.Completion.IsCompletedSuccessfully.Should().BeTrue();
            hub.Observer.TryRead(out var observed).Should().BeTrue();
            observed.Should().BeSameAs(report);
            await hub.Observer.Completion.WaitAsync(TestTimeout);
        }
        finally
        {
            releaseReport.TrySetResult();
            publisher.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task CancellationAndWorkerFailure_StillFinalizeReportsAndPreserveTheSharedShutdownFailure()
    {
        var started = Signal();
        var cancellationObserved = Signal();
        var releaseWorker = Signal();
        var cancellationFailure = new IOException("Cancellation callback failed.");
        var workerFailure = new InvalidOperationException("Report worker failed while unwinding.");
        async Task RunReportsAsync(CancellationToken ct)
        {
            using var registration = ct.Register(() =>
            {
                cancellationObserved.TrySetResult();
                throw cancellationFailure;
            });
            started.TrySetResult();
            await releaseWorker.Task;
            throw workerFailure;
        }
        var lifecycle = CreateLifecycle(RunReportsAsync, out var hub,
            options: new OrderManagementSystemOptions
            {
                ExecutionSubscriberDrainTimeout = TimeSpan.FromMilliseconds(1)
            });
        var recovered = new List<ExecutionReport>();
        await using var subscriber = lifecycle.SubscribeExecutionReports(1, "abandoned-consumer", (report, _, _) =>
        {
            recovered.Add(report);
            return ValueTask.CompletedTask;
        });
        try
        {
            await lifecycle.StartAsync();
            await started.Task.WaitAsync(TestTimeout);
            var buffered = Report("buffered-before-shutdown");
            await hub.PublishAsync(buffered, new ExecutionReportHub.DeliveryCheckpoint());

            var shutdown = lifecycle.StopAsync();
            await cancellationObserved.Task.WaitAsync(TestTimeout);
            shutdown.IsCompleted.Should().BeFalse("a failing cancellation callback must not bypass the worker join");
            subscriber.Reports.Completion.IsCompleted.Should().BeFalse();
            releaseWorker.TrySetResult();

            var failure = await Assert.ThrowsAsync<AggregateException>(() => shutdown.WaitAsync(TestTimeout));
            var causes = failure.Flatten().InnerExceptions;
            causes.Should().HaveCount(2);
            causes.Should().ContainSingle(exception => ReferenceEquals(exception, cancellationFailure));
            causes.Should().ContainSingle(exception => ReferenceEquals(exception, workerFailure));
            recovered.Should().ContainSingle().Which.Should().BeSameAs(buffered);
            subscriber.Reports.Completion.IsCompletedSuccessfully.Should().BeTrue();
            hub.Observer.TryRead(out var observed).Should().BeTrue();
            observed.Should().BeSameAs(buffered);
            await hub.Observer.Completion.WaitAsync(TestTimeout);

            var repeatedStop = await Record.ExceptionAsync(() => lifecycle.StopAsync().WaitAsync(TestTimeout));
            repeatedStop.Should().BeSameAs(failure);
            var repeatedDispose = await Record.ExceptionAsync(
                () => lifecycle.DisposeAsync().AsTask().WaitAsync(TestTimeout));
            repeatedDispose.Should().BeSameAs(failure);
        }
        finally
        {
            releaseWorker.TrySetResult();
            _ = await Record.ExceptionAsync(() => lifecycle.DisposeAsync().AsTask().WaitAsync(TestTimeout));
        }
    }

    private static OrderManagementLifecycleCoordinator CreateLifecycle(
        Func<CancellationToken, Task> reportPump,
        out ExecutionReportHub hub,
        AccountingHandoffCoordinator? accounting = null,
        OrderManagementSystemOptions? options = null)
    {
        var failures = new ExecutionFailureState(NullLogger.Instance);
        hub = new ExecutionReportHub(options ?? new OrderManagementSystemOptions(),
            () => "lifecycle-test", null, NullLogger.Instance, failures);
        return new OrderManagementLifecycleCoordinator(reportPump,
            accounting ?? new AccountingHandoffCoordinator(null, null, NullLogger.Instance),
            hub, failures, null, (_, _) => 0m);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static ExecutionReport Report(string orderId) => new()
    {
        OrderId = orderId,
        ClientOrderId = orderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        ReportType = ExecutionReportType.Fill,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = 1m,
        FilledQuantity = 1m,
        FillPrice = 100m,
        Timestamp = DateTimeOffset.UtcNow
    };

    private sealed class BlockingPublisher : IScopedTradeEventPublisher
    {
        public string PostingScope => "lifecycle-test/open-period";
        public TaskCompletionSource Started { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();

        public void Publish(TradeExecutedEvent tradeEvent)
            => throw new InvalidOperationException("Retained replay must use asynchronous publication.");

        public async Task PublishAsync(TradeExecutedEvent tradeEvent)
        {
            Started.TrySetResult();
            await Release.Task;
        }
    }

    private sealed class RetainedFailureStore(TradeExecutedEvent fill) : ITradeFillHandoffFailureStore
    {
        public string PostingScope => "lifecycle-test/open-period";
        public TaskCompletionSource Acknowledged { get; } = Signal();
        public Guid? AcknowledgedFillId { get; private set; }
        public CancellationToken AcknowledgmentToken { get; private set; }

        public Task<IReadOnlyList<RetainedTradeFillHandoffFailure>> LoadPendingAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<RetainedTradeFillHandoffFailure>>(
                [new(fill, DateTimeOffset.UtcNow, 1, "Publisher previously unavailable.", DateTimeOffset.UtcNow)]);
        }

        public Task RetainAsync(TradeExecutedEvent tradeEvent, string failure, CancellationToken ct = default)
            => throw new InvalidOperationException("Successful selected-fill replay must not retain another failure.");

        public Task MarkReplayedAsync(Guid fillId, CancellationToken ct = default)
        {
            AcknowledgedFillId = fillId;
            AcknowledgmentToken = ct;
            Acknowledged.TrySetResult();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
