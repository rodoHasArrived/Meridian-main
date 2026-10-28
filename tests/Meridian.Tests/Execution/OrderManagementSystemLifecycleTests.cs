using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class OrderManagementSystemLifecycleTests
{
    [Fact]
    public async Task Construction_DoesNotConsumeReportsOrReplayHandoffs_AndRefusesMutations()
    {
        var gateway = new LifecycleGateway();
        var publisher = new RecordingPublisher();
        var store = new RecordingFailureStore();
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            tradeEventPublisher: publisher, tradeFillHandoffFailureStore: store);

        gateway.StreamCount.Should().Be(0);
        store.LoadCount.Should().Be(0);
        await ((Func<Task>)(() => oms.PlaceOrderAsync(Request()))).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*StartAsync*");
        await ((Func<Task>)(() => oms.CancelOrderAsync("unknown"))).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*StartAsync*");
        await ((Func<Task>)(() => oms.ModifyOrderAsync("unknown", new OrderModification()))).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*StartAsync*");
        await ((Func<Task>)(() => oms.CancelAllAsync())).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*StartAsync*");
        gateway.SubmitCount.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentStart_StartsOneReportPumpAndOneHandoffReplay()
    {
        var gateway = new LifecycleGateway();
        var store = new RecordingFailureStore();
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            tradeEventPublisher: new RecordingPublisher(), tradeFillHandoffFailureStore: store);

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => oms.StartAsync())));
        await Task.WhenAll(gateway.StreamStarted.Task, store.LoadStarted.Task).WaitAsync(TimeSpan.FromSeconds(5));

        gateway.StreamCount.Should().Be(1);
        store.LoadCount.Should().Be(1);
        await Task.WhenAll(oms.StopAsync(), oms.StopAsync());
        gateway.StreamStopped.Task.IsCompletedSuccessfully.Should().BeTrue();
        await ((Func<Task>)(() => oms.StartAsync())).Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task CancelledStart_LeavesInstanceAvailableForExplicitStart()
    {
        var gateway = new LifecycleGateway();
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await ((Func<Task>)(() => oms.StartAsync(cancellation.Token))).Should()
            .ThrowAsync<OperationCanceledException>();
        gateway.StreamCount.Should().Be(0);
        await oms.StartAsync();
        await gateway.StreamStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        gateway.StreamCount.Should().Be(1);
    }

    [Fact]
    public async Task StopBeforeStart_IsSafeAndPreventsLaterStart()
    {
        var gateway = new LifecycleGateway();
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);

        await Task.WhenAll(oms.StopAsync(), oms.StopAsync());

        gateway.StreamCount.Should().Be(0);
        oms.ExecutionReports.Completion.IsCompletedSuccessfully.Should().BeTrue();
        await ((Func<Task>)(() => oms.StartAsync())).Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task StartRacingStop_CannotLeaveAReportConsumerRunningAfterShutdown()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var gateway = new LifecycleGateway();
            await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);
            var start = Task.Run(async () =>
            {
                try
                {
                    await oms.StartAsync();
                }
                catch (ObjectDisposedException)
                {
                    // Stop won admission; no startup is permitted after it.
                }
            });

            await Task.WhenAll(start, Task.Run(() => oms.StopAsync())).WaitAsync(TimeSpan.FromSeconds(5));

            gateway.StreamCount.Should().BeInRange(0, 1);
            if (gateway.StreamCount == 1)
                gateway.StreamStopped.Task.IsCompletedSuccessfully.Should().BeTrue();
            oms.ExecutionReports.Completion.IsCompletedSuccessfully.Should().BeTrue();
        }
    }

    [Fact]
    public async Task CancelledStopWait_DoesNotAbandonAnAdmittedFillOrItsAccountingHandoff()
    {
        var gateway = new LifecycleGateway { BlockSubmit = true };
        var publisher = new RecordingPublisher();
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            tradeEventPublisher: publisher);
        await oms.StartAsync();
        await gateway.StreamStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var submission = oms.PlaceOrderAsync(Request());
        await gateway.SubmitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var cancellation = new CancellationTokenSource();
        var cancelledWait = oms.StopAsync(cancellation.Token);
        var drain = oms.StopAsync();
        try
        {
            drain.IsCompleted.Should().BeFalse();
            gateway.StreamStopped.Task.IsCompleted.Should().BeFalse(
                "report intake remains alive until already-admitted operations have settled");
            await ((Func<Task>)(() => oms.PlaceOrderAsync(Request()))).Should()
                .ThrowAsync<ObjectDisposedException>();
            cancellation.Cancel();
            await ((Func<Task>)(() => cancelledWait)).Should().ThrowAsync<OperationCanceledException>();
            drain.IsCompleted.Should().BeFalse();
        }
        finally
        {
            gateway.ReleaseSubmit.TrySetResult();
        }

        (await submission.WaitAsync(TimeSpan.FromSeconds(5))).Success.Should().BeTrue();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        publisher.Events.Should().ContainSingle(fill => fill.OrderId == "lifecycle-order" && fill.FilledQuantity == 1m);
        gateway.StreamStopped.Task.IsCompletedSuccessfully.Should().BeTrue();
        await oms.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedRegistration_StartsAndStopsTheResolvedSingleton(bool customRegistration)
    {
        var gateway = new LifecycleGateway();
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<IExecutionGateway>(gateway);
                if (customRegistration)
                {
                    services.AddSingleton<IOrderManager>(_ => new OrderManagementSystem(
                        gateway, NullLogger<OrderManagementSystem>.Instance));
                }
                services.AddBrokerageExecution();
                services.AddBrokerageExecution();
            })
            .Build();
        var oms = host.Services.GetRequiredService<IOrderManager>();

        gateway.StreamCount.Should().Be(0);
        await host.StartAsync();
        await gateway.StreamStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Services.GetRequiredService<IOrderManager>().Should().BeSameAs(oms);
        gateway.StreamCount.Should().Be(1);

        await host.StopAsync();
        gateway.StreamStopped.Task.IsCompletedSuccessfully.Should().BeTrue();
        await ((Func<Task>)(() => oms.PlaceOrderAsync(Request()))).Should().ThrowAsync<ObjectDisposedException>();
    }

    private static OrderRequest Request() => new()
    {
        ClientOrderId = "lifecycle-order",
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = 1m
    };

    private sealed class LifecycleGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();
        private int _streamCount;
        public int StreamCount => Volatile.Read(ref _streamCount);
        public int SubmitCount { get; private set; }
        public bool BlockSubmit { get; init; }
        public TaskCompletionSource StreamStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StreamStopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SubmitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSubmit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string GatewayId => "lifecycle-test";
        public bool IsConnected => true;
        public ExecutionMode ExecutionMode => ExecutionMode.Paper;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            SubmitCount++;
            SubmitStarted.TrySetResult();
            if (BlockSubmit)
                await ReleaseSubmit.Task.WaitAsync(ct);
            return new ExecutionReport
            {
                OrderId = request.ClientOrderId!,
                ClientOrderId = request.ClientOrderId,
                Symbol = request.Symbol,
                Side = request.Side,
                ReportType = ExecutionReportType.Fill,
                OrderStatus = OrderStatus.Filled,
                FilledQuantity = request.Quantity,
                FillPrice = 150m,
                Timestamp = DateTimeOffset.UtcNow
            };
        }

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            Interlocked.Increment(ref _streamCount);
            StreamStarted.TrySetResult();
            try
            {
                await foreach (var report in _reports.Reader.ReadAllAsync(ct))
                    yield return report;
            }
            finally
            {
                StreamStopped.TrySetResult();
            }
        }
    }

    private sealed class RecordingPublisher : IScopedTradeEventPublisher
    {
        public string PostingScope => "lifecycle-book/period";
        public List<TradeExecutedEvent> Events { get; } = [];
        public void Publish(TradeExecutedEvent tradeEvent) => Events.Add(tradeEvent);
    }

    private sealed class RecordingFailureStore : ITradeFillHandoffFailureStore
    {
        private int _loadCount;
        public string PostingScope => "lifecycle-book/period";
        public int LoadCount => Volatile.Read(ref _loadCount);
        public TaskCompletionSource LoadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<RetainedTradeFillHandoffFailure>> LoadPendingAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _loadCount);
            LoadStarted.TrySetResult();
            return Task.FromResult<IReadOnlyList<RetainedTradeFillHandoffFailure>>([]);
        }

        public Task RetainAsync(TradeExecutedEvent tradeEvent, string failure, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task MarkReplayedAsync(Guid fillId, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
