using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class ExecutionReportHubTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PartialDelivery_RetryKeepsOriginalTargetsAndSkipsAlreadyDeliveredSubscribers()
    {
        var hub = CreateHub(out _);
        var recoveryFailure = new InvalidOperationException("Recovery temporarily unavailable.");
        await using var first = hub.Subscribe(1, "first", null);
        await using var second = hub.Subscribe(1, "second", (_, _, _) => throw recoveryFailure);
        var occupyingReport = Report("occupying");
        await hub.PublishAsync(occupyingReport, new ExecutionReportHub.DeliveryCheckpoint());
        first.Reports.TryRead(out _).Should().BeTrue();

        var target = Report("target");
        var checkpoint = new ExecutionReportHub.DeliveryCheckpoint();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await hub.PublishAsync(target, checkpoint));
        failure.Should().BeSameAs(recoveryFailure);
        checkpoint.IsComplete.Should().BeFalse();

        await using var lateSubscriber = hub.Subscribe(1, "late", null);
        second.Reports.TryRead(out var occupying).Should().BeTrue();
        occupying.Should().BeSameAs(occupyingReport);

        await hub.PublishAsync(target, checkpoint);

        checkpoint.IsComplete.Should().BeTrue();
        first.Reports.TryRead(out var firstDelivery).Should().BeTrue();
        firstDelivery.Should().BeSameAs(target);
        first.Reports.TryRead(out _).Should().BeFalse("retry must skip the already delivered first subscriber");
        second.Reports.TryRead(out var secondDelivery).Should().BeTrue();
        secondDelivery.Should().BeSameAs(target);
        second.Reports.TryRead(out _).Should().BeFalse();
        lateSubscriber.Reports.TryRead(out _).Should().BeFalse(
            "a subscriber registered after the first attempt is outside that fill's captured targets");
    }

    [Fact]
    public async Task UnsubscribedCapturedTarget_RetryAccountsForItsReportAfterRegistryRemoval()
    {
        var hub = CreateHub(out _);
        var target = Report("target");
        var rejectFirstTarget = true;
        var accounted = new List<ExecutionReport>();
        await using var first = hub.Subscribe(1, "first", null);
        await using var second = hub.Subscribe(1, "second", (report, _, _) =>
        {
            if (ReferenceEquals(report, target) && rejectFirstTarget)
            {
                rejectFirstTarget = false;
                throw new InvalidOperationException("Recovery temporarily unavailable.");
            }

            accounted.Add(report);
            return ValueTask.CompletedTask;
        });
        var occupyingReport = Report("occupying");
        await hub.PublishAsync(occupyingReport, new ExecutionReportHub.DeliveryCheckpoint());
        first.Reports.TryRead(out _).Should().BeTrue();
        var checkpoint = new ExecutionReportHub.DeliveryCheckpoint();
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await hub.PublishAsync(target, checkpoint));

        await second.DisposeAsync();
        accounted.Should().Equal(occupyingReport);
        await hub.PublishAsync(target, checkpoint);

        checkpoint.IsComplete.Should().BeTrue();
        accounted.Should().Equal(occupyingReport, target);
        first.Reports.TryRead(out var delivered).Should().BeTrue();
        delivered.Should().BeSameAs(target);
        first.Reports.TryRead(out _).Should().BeFalse();
        second.Reports.TryRead(out _).Should().BeFalse(
            "the captured closed target must use its accounting handler instead of its completed inbox");
        await second.Reports.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task BeginClose_RejectsSubscriptionsButAllowsAdmittedFillToPublishBeforeCompletion()
    {
        var hub = CreateHub(out _, drainTimeout: TestTimeout);
        await using var subscriber = hub.Subscribe(1, "existing", null);

        hub.BeginClose();

        Assert.Throws<ObjectDisposedException>(() => hub.Subscribe(1, "too-late", null));
        var target = Report("admitted-before-close");
        var checkpoint = new ExecutionReportHub.DeliveryCheckpoint();
        await hub.PublishAsync(target, checkpoint);
        var completion = CompleteSubscribersAsync(hub);
        completion.IsCompleted.Should().BeFalse("an accepted report remains buffered for the consumer");

        subscriber.Reports.TryRead(out var delivered).Should().BeTrue();
        delivered.Should().BeSameAs(target);
        await completion.WaitAsync(TestTimeout);
        await subscriber.Reports.Completion.WaitAsync(TestTimeout);
        checkpoint.IsComplete.Should().BeTrue();
        hub.CompleteObserver();
    }

    [Fact]
    public async Task DrainDeadline_AccountsForEveryBufferedReportAndAggregatesRecoveryFailures()
    {
        var hub = CreateHub(out _, drainTimeout: TimeSpan.FromMilliseconds(1));
        var attempted = new List<ExecutionReport>();
        var failures = new List<Exception>();
        await using var subscriber = hub.Subscribe(3, "abandoned", (report, _, _) =>
        {
            attempted.Add(report);
            var failure = new IOException($"Cannot retain {report.OrderId}.");
            failures.Add(failure);
            throw failure;
        });
        var reports = new[] { Report("one"), Report("two"), Report("three") };
        foreach (var report in reports)
        {
            await hub.PublishAsync(report, new ExecutionReportHub.DeliveryCheckpoint());
        }
        hub.BeginClose();

        var aggregate = await Assert.ThrowsAsync<AggregateException>(
            () => CompleteSubscribersAsync(hub).WaitAsync(TestTimeout));

        attempted.Should().Equal(reports);
        aggregate.InnerExceptions.Should().Equal(failures);
        subscriber.Reports.TryRead(out _).Should().BeFalse();
        await subscriber.Reports.Completion.WaitAsync(TestTimeout);
        hub.CompleteObserver();
    }

    [Fact]
    public async Task FailureAfterDequeue_LatchesFailureCompletesSubscriptionAndRejectsLaterPublication()
    {
        var hub = CreateHub(out var failures);
        await using var subscriber = hub.Subscribe(1, "consumer", null);
        var acceptedReport = Report("accepted");
        await hub.PublishAsync(acceptedReport, new ExecutionReportHub.DeliveryCheckpoint());
        subscriber.Reports.TryRead(out var dequeued).Should().BeTrue();
        dequeued.Should().BeSameAs(acceptedReport);
        var consumerFailure = new InvalidOperationException("Consumer could not account for the dequeued fill.");

        await subscriber.FailAsync(consumerFailure);

        failures.Failure.Should().BeSameAs(consumerFailure);
        await subscriber.Reports.Completion.WaitAsync(TestTimeout);
        var laterReport = Report("later");
        var checkpoint = new ExecutionReportHub.DeliveryCheckpoint();
        var deliveryFailure = await Assert.ThrowsAsync<OrderManagementSystem.ExecutionReportDeliveryException>(
            async () => await hub.PublishAsync(laterReport, checkpoint));
        deliveryFailure.Report.Should().BeSameAs(laterReport);
        checkpoint.IsComplete.Should().BeFalse();
        hub.BeginClose();
        await CompleteSubscribersAsync(hub).WaitAsync(TestTimeout);
        hub.CompleteObserver();
    }

    private static ExecutionReportHub CreateHub(
        out ExecutionFailureState failures,
        TimeSpan? drainTimeout = null)
    {
        failures = new ExecutionFailureState(NullLogger.Instance);
        return new ExecutionReportHub(
            new OrderManagementSystemOptions
            {
                ExecutionSubscriberDrainTimeout = drainTimeout ?? TestTimeout
            },
            () => "test-gateway",
            auditTrail: null,
            NullLogger.Instance,
            failures);
    }

    private static async Task CompleteSubscribersAsync(ExecutionReportHub hub)
        => await hub.CompleteSubscribersAsync();

    private static ExecutionReport Report(string orderId) => new()
    {
        OrderId = orderId,
        ClientOrderId = orderId,
        ReportType = ExecutionReportType.Fill,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = 1m,
        FilledQuantity = 1m,
        FillPrice = 100m,
        Commission = 0m,
        Timestamp = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero)
    };
}
