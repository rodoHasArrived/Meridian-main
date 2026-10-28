using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class OrderManagementSystemSubscriptionFailureTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsumerFailureAfterDequeue_IsPreservedWhenShutdownDrainsAdmittedOperations(
        bool failDuringShutdown)
    {
        var gateway = new PendingSubmitGateway();
        var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);
        await using var subscriber = oms.SubscribeLosslessExecutionReports(capacity: 1);
        var failure = new IOException("Consumer could not retain an accepted execution.");
        try
        {
            await oms.StartAsync();
            var first = await oms.PlaceOrderAsync(Request("filled"));
            first.Success.Should().BeTrue();
            var dequeued = await subscriber.Reports.ReadAsync().AsTask().WaitAsync(TestTimeout);
            dequeued.OrderId.Should().Be(first.OrderId);

            Task<OrderResult>? pendingOrder = null;
            Task? shutdown = null;
            if (failDuringShutdown)
            {
                pendingOrder = oms.PlaceOrderAsync(Request("pending"));
                await gateway.PendingSubmitStarted.Task.WaitAsync(TestTimeout);
                shutdown = oms.StopAsync();
                shutdown.IsCompleted.Should().BeFalse();
                Assert.Throws<ObjectDisposedException>(() => oms.SubscribeLosslessExecutionReports());
            }

            await subscriber.FailAsync(failure);
            await subscriber.Reports.Completion.WaitAsync(TestTimeout);

            if (!failDuringShutdown)
            {
                var admissionFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => oms.PlaceOrderAsync(Request("after-failure")));
                admissionFailure.InnerException.Should().BeSameAs(failure);
                shutdown = oms.StopAsync();
            }
            else
            {
                shutdown!.IsCompleted.Should().BeFalse(
                    "the failure latch must not bypass the admitted broker operation's drain");
                gateway.ReleasePendingSubmit.TrySetResult();
                (await pendingOrder!.WaitAsync(TestTimeout)).Success.Should().BeTrue();
            }

            var shutdownFailure = await Record.ExceptionAsync(() => shutdown!.WaitAsync(TestTimeout));
            shutdownFailure.Should().BeSameAs(failure,
                "a consumer can fail outside the report-pump stack while shutdown is already in progress");
            var repeatedShutdownFailure = await Record.ExceptionAsync(() => oms.StopAsync().WaitAsync(TestTimeout));
            repeatedShutdownFailure.Should().BeSameAs(failure);
        }
        finally
        {
            gateway.ReleasePendingSubmit.TrySetResult();
            // The terminal exception is asserted above; cleanup must still drain on an earlier
            // assertion failure without replacing that assertion with the same terminal error.
            _ = await Record.ExceptionAsync(() => oms.DisposeAsync().AsTask().WaitAsync(TestTimeout));
        }
    }

    private static OrderRequest Request(string orderId) => new()
    {
        ClientOrderId = orderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = 1m
    };

    private sealed class PendingSubmitGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();
        public TaskCompletionSource PendingSubmitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePendingSubmit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string GatewayId => "subscription-failure-test";
        public bool IsConnected => true;
        public ExecutionMode ExecutionMode => ExecutionMode.Paper;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            var pending = request.ClientOrderId == "pending";
            if (pending)
            {
                PendingSubmitStarted.TrySetResult();
                await ReleasePendingSubmit.Task.WaitAsync(ct);
            }
            return new ExecutionReport
            {
                OrderId = request.ClientOrderId!,
                ClientOrderId = request.ClientOrderId,
                Symbol = request.Symbol,
                Side = request.Side,
                ReportType = pending ? ExecutionReportType.New : ExecutionReportType.Fill,
                OrderStatus = pending ? OrderStatus.Accepted : OrderStatus.Filled,
                OrderQuantity = request.Quantity,
                FilledQuantity = pending ? 0m : request.Quantity,
                FillPrice = pending ? null : 100m,
                Timestamp = DateTimeOffset.UtcNow
            };
        }

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default)
            => _reports.Reader.ReadAllAsync(ct);
    }
}
