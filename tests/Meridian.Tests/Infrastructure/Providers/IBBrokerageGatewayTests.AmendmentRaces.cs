using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class IBBrokerageGatewayTests
{
    [Fact]
    public async Task ModifyOrderAsync_PreCancelledRequestDoesNotRetainUndispatchedProposal()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ModifyOrderAsync("working-order",
            new OrderModification { NewQuantity = 12m }, cancellation.Token));

        var acknowledged = await sut.ModifyOrderAsync("working-order", new OrderModification { NewQuantity = 11m });
        acknowledged.ReportType.Should().Be(ExecutionReportType.Modified);
        acknowledged.OrderQuantity.Should().Be(11m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModifyOrderAsync_StoppedWaitRetainsTermsForLateAcknowledgment(bool timeout)
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        OrderRequest? amendmentRequest = null;
        client.OnPlaceOrder = (_, _, request) => amendmentRequest = request;
        using var cancellation = new CancellationTokenSource();

        var amendment = sut.ModifyOrderAsync("working-order",
            new OrderModification { NewQuantity = 12m, NewLimitPrice = 101m }, cancellation.Token);
        if (timeout)
            await Assert.ThrowsAsync<TimeoutException>(() => amendment);
        else
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => amendment);
        }
        await Assert.ThrowsAsync<BrokerageRequestNotSentException>(() => sut.ModifyOrderAsync("working-order",
            new OrderModification { NewQuantity = 13m }));

        client.RaiseExecution(AmendmentExecution(10m, 10m, 100d, 100d, "late-execution"));
        client.RaiseOpenOrder(OpenOrder(11001, amendmentRequest!));
        using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var acknowledged = await ReadUntilAsync(sut.StreamExecutionReportsAsync(reportTimeout.Token),
            report => report.ReportType == ExecutionReportType.Modified, reportTimeout.Token);
        acknowledged.OrderQuantity.Should().Be(12m);
        acknowledged.FilledQuantity.Should().Be(10m);
        acknowledged.OrderStatus.Should().Be(OrderStatus.PartiallyFilled);
        acknowledged.HasOrderTerms.Should().BeTrue();
        acknowledged.OrderLimitPrice.Should().Be(101m);

        OrderRequest? nextRequest = null;
        client.OnPlaceOrder = (c, id, request) =>
        {
            nextRequest = request;
            c.RaiseOpenOrder(OpenOrder(id, request));
        };
        await sut.ModifyOrderAsync("working-order", new OrderModification { NewLimitPrice = 99m });
        nextRequest!.Quantity.Should().Be(12m);
    }

    [Fact]
    public async Task ModifyOrderAsync_LateRefusalAfterCallerCancellationRetainsOriginalTerms()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        client.OnPlaceOrder = (_, _, _) => { };
        using var cancellation = new CancellationTokenSource();
        var amendment = sut.ModifyOrderAsync("working-order",
            new OrderModification { NewQuantity = 12m, NewLimitPrice = 101m }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => amendment);

        client.RaiseError(new IBApiError(11001, 105, "Amendment refused", null));
        using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var refused = await ReadUntilAsync(sut.StreamExecutionReportsAsync(reportTimeout.Token),
            report => report.ReportType == ExecutionReportType.ModificationRejected, reportTimeout.Token);
        refused.OrderStatus.Should().Be(OrderStatus.Accepted);
        refused.OrderQuantity.Should().Be(10m);

        OrderRequest? nextRequest = null;
        client.OnPlaceOrder = (c, id, request) =>
        {
            nextRequest = request;
            c.RaiseOpenOrder(OpenOrder(id, request));
        };
        await sut.ModifyOrderAsync("working-order", new OrderModification { NewQuantity = 11m });
        nextRequest!.LimitPrice.Should().Be(100m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModifyOrderAsync_LateAcknowledgmentDoesNotCompleteOrReopenConcurrentCancellation(bool cancelBeforeEcho)
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        OrderRequest? amendmentRequest = null;
        client.OnPlaceOrder = (_, _, request) => amendmentRequest = request;
        using var cancellation = new CancellationTokenSource();
        var amendment = sut.ModifyOrderAsync("working-order",
            new OrderModification { NewQuantity = 12m, NewLimitPrice = 101m }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => amendment);

        var cancelIds = new List<int>();
        client.OnCancelOrder = (c, id) =>
        {
            cancelIds.Add(id);
            c.RaiseOrderStatus(Status(id, "PendingCancel"));
        };
        using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var cancel = sut.CancelOrderAsync("working-order", reportTimeout.Token);
        cancel.IsCompleted.Should().BeFalse();
        if (cancelBeforeEcho)
            client.RaiseOrderStatus(Status(11001, "Cancelled"));

        client.RaiseOpenOrder(OpenOrder(11001, amendmentRequest!));
        var acknowledged = await ReadUntilAsync(sut.StreamExecutionReportsAsync(reportTimeout.Token),
            report => report.ReportType == ExecutionReportType.Modified, reportTimeout.Token);
        acknowledged.OrderQuantity.Should().Be(12m);
        acknowledged.OrderStatus.Should().Be(cancelBeforeEcho ? OrderStatus.Cancelled : OrderStatus.PendingCancel);
        if (!cancelBeforeEcho)
        {
            cancel.IsCompleted.Should().BeFalse();
            client.RaiseOrderStatus(Status(11001, "Cancelled"));
        }
        (await cancel).OrderStatus.Should().Be(OrderStatus.Cancelled);
        cancelIds.Should().Equal(11001);
    }

    [Fact]
    public async Task ModifyOrderAsync_LateRefusalDoesNotConsumePendingCancellation()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        client.OnPlaceOrder = (_, _, _) => { };
        using var cancellation = new CancellationTokenSource();
        var amendment = sut.ModifyOrderAsync("working-order", new OrderModification { NewQuantity = 12m }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => amendment);
        client.OnCancelOrder = (c, id) => c.RaiseOrderStatus(Status(id, "PendingCancel"));
        using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var cancel = sut.CancelOrderAsync("working-order", reportTimeout.Token);

        client.RaiseError(new IBApiError(11001, 105, "Amendment refused", null));
        cancel.IsCompleted.Should().BeFalse();
        var refused = await ReadUntilAsync(sut.StreamExecutionReportsAsync(reportTimeout.Token),
            report => report.ReportType == ExecutionReportType.ModificationRejected, reportTimeout.Token);
        refused.OrderStatus.Should().Be(OrderStatus.PendingCancel);
        client.RaiseOrderStatus(Status(11001, "Cancelled"));
        (await cancel).ReportType.Should().Be(ExecutionReportType.Cancelled);
    }

    [Fact]
    public async Task ModifyOrderAsync_ExecutionBeforeEchoUsesProposedQuantityWithoutAcknowledgingTerms()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        OrderRequest? amendmentRequest = null;
        client.OnPlaceOrder = (_, _, request) => amendmentRequest = request;
        using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var amendment = sut.ModifyOrderAsync("working-order",
            new OrderModification { NewQuantity = 12m, NewLimitPrice = 107m }, reportTimeout.Token);

        client.RaiseExecution(AmendmentExecution(10m, 10m, 100d, 100d, "old-cap"));
        amendment.IsCompleted.Should().BeFalse();
        var first = await ReadUntilAsync(sut.StreamExecutionReportsAsync(reportTimeout.Token),
            report => report.FilledQuantity == 10m, reportTimeout.Token);
        first.ReportType.Should().Be(ExecutionReportType.PartialFill);
        first.OrderQuantity.Should().Be(12m);
        first.HasOrderTerms.Should().BeFalse();

        client.RaiseExecution(AmendmentExecution(2m, 12m, 106d, 101d, "new-cap"));
        amendment.IsCompleted.Should().BeFalse();
        client.RaiseOpenOrder(OpenOrder(11001, amendmentRequest!));
        var confirmed = await amendment;
        confirmed.ReportType.Should().Be(ExecutionReportType.Modified);
        confirmed.OrderStatus.Should().Be(OrderStatus.Filled);
        confirmed.FilledQuantity.Should().Be(12m);
        confirmed.OrderQuantity.Should().Be(12m);
        confirmed.OrderLimitPrice.Should().Be(107m);
        confirmed.HasOrderTerms.Should().BeTrue();
    }

    [Fact]
    public async Task ModifyOrderAsync_DelayedExecutionCannotReopenBrokerConfirmedFilledState()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        OrderRequest? amendmentRequest = null;
        client.OnPlaceOrder = (_, _, request) => amendmentRequest = request;
        using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var amendment = sut.ModifyOrderAsync("working-order", new OrderModification { NewQuantity = 12m }, reportTimeout.Token);
        client.RaiseOrderStatus(Status(11001, "Filled", 10m));
        client.RaiseExecution(AmendmentExecution(10m, 10m, 100d, 100d, "delayed-after-filled"));
        var reports = sut.StreamExecutionReportsAsync(reportTimeout.Token);
        await ReadUntilAsync(reports, report => report.OrderStatus == OrderStatus.Filled, reportTimeout.Token);

        var delayed = await ReadUntilAsync(reports, report => report.FilledQuantity == 10m, reportTimeout.Token);

        delayed.OrderStatus.Should().Be(OrderStatus.Filled);
        delayed.ReportType.Should().Be(ExecutionReportType.Fill);
        amendment.IsCompleted.Should().BeFalse();
        client.RaiseOpenOrder(OpenOrder(11001, amendmentRequest!));
        (await amendment).OrderStatus.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public async Task ModifyOrderAsync_RefusedIncreaseAfterOriginalQuantityFillsBecomesTerminalFilled()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        client.OnPlaceOrder = (_, _, _) => { };
        using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var amendment = sut.ModifyOrderAsync("working-order", new OrderModification { NewQuantity = 12m }, reportTimeout.Token);
        client.RaiseExecution(AmendmentExecution(10m, 10m, 100d, 100d, "original-cap"));
        amendment.IsCompleted.Should().BeFalse();

        client.RaiseError(new IBApiError(11001, 105, "Amendment refused", null));
        var refused = await amendment;

        refused.ReportType.Should().Be(ExecutionReportType.ModificationRejected);
        refused.OrderQuantity.Should().Be(10m);
        refused.FilledQuantity.Should().Be(10m);
        refused.OrderStatus.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public async Task CancelOrderAsync_RefusalRetainsUnconfirmedIncreaseUntilAmendmentIsAlsoRefused()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        client.OnPlaceOrder = (_, _, _) => { };
        using var cancellation = new CancellationTokenSource();
        var amendment = sut.ModifyOrderAsync("working-order", new OrderModification { NewQuantity = 12m }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => amendment);
        client.RaiseExecution(AmendmentExecution(10m, 10m, 100d, 100d, "original-cap"));
        client.OnCancelOrder = (c, id) => c.RaiseError(new IBApiError(id, 10148, "Cancellation refused", null));

        var refusedCancel = await sut.CancelOrderAsync("working-order");

        refusedCancel.ReportType.Should().Be(ExecutionReportType.CancelRejected);
        refusedCancel.OrderStatus.Should().Be(OrderStatus.PartiallyFilled);
        refusedCancel.OrderQuantity.Should().Be(12m);
        client.RaiseError(new IBApiError(11001, 105, "Amendment refused", null));
        using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var refusedAmendment = await ReadUntilAsync(sut.StreamExecutionReportsAsync(reportTimeout.Token),
            report => report.ReportType == ExecutionReportType.ModificationRejected, reportTimeout.Token);
        refusedAmendment.OrderStatus.Should().Be(OrderStatus.Filled);
        refusedAmendment.OrderQuantity.Should().Be(10m);
    }

    [Fact]
    public async Task OmsAmendment_ExecutionsBeforeBrokerEchoBookEntireAuthorizedIncreaseAndPreserveTerminalState()
    {
        var client = CreateAcknowledgingClient();
        await using var gateway = CreateSut(client);
        await gateway.ConnectAsync();
        var publisher = new AmendmentRacePublisher();
        var portfolio = new PaperTradingPortfolio(100_000m);
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio, tradeEventPublisher: publisher);
        (await oms.PlaceOrderAsync(WorkingOrder())).Success.Should().BeTrue();
        OrderRequest? amendmentRequest = null;
        client.OnPlaceOrder = (c, _, request) =>
        {
            amendmentRequest = request;
            c.RaiseExecution(AmendmentExecution(10m, 10m, 100d, 100d, "old-cap"));
            c.RaiseExecution(AmendmentExecution(2m, 12m, 106d, 101d, "new-cap"));
        };
        using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var amendment = oms.ModifyOrderAsync("working-order",
            new OrderModification { NewQuantity = 12m, NewLimitPrice = 107m }, reportTimeout.Token);
        await publisher.AllFills.Task.WaitAsync(reportTimeout.Token);

        amendment.IsCompleted.Should().BeFalse();
        oms.GetOrder("working-order")!.FilledQuantity.Should().Be(12m);
        portfolio.Positions["AAPL"].Quantity.Should().Be(12L);
        client.RaiseOpenOrder(OpenOrder(11001, amendmentRequest!));
        var result = await amendment;

        result.Success.Should().BeTrue();
        result.OrderState!.Quantity.Should().Be(12m);
        result.OrderState.LimitPrice.Should().Be(107m);
        result.OrderState.FilledQuantity.Should().Be(12m);
        result.OrderState.AverageFillPrice.Should().Be(101m);
        result.OrderState.Status.Should().Be(OrderStatus.Filled);
        publisher.Events.Sum(fill => fill.FilledQuantity).Should().Be(12m);
        publisher.Events.Sum(fill => fill.GrossValue).Should().Be(1212m);
    }

    private static IBExecutionUpdate AmendmentExecution(decimal shares, decimal cumulative, double price, double average, string id)
        => new(11001, "AAPL", "BOT", shares, price, cumulative, average, id, "DU1", "SMART", 1L, null, DateTimeOffset.UtcNow);

    private sealed class AmendmentRacePublisher : ITradeEventPublisher
    {
        public ConcurrentQueue<TradeExecutedEvent> Events { get; } = new();
        public TaskCompletionSource AllFills { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Publish(TradeExecutedEvent tradeEvent)
        {
            Events.Enqueue(tradeEvent);
            if (Events.Sum(fill => fill.FilledQuantity) >= 12m)
                AllFills.TrySetResult();
        }
    }
}
