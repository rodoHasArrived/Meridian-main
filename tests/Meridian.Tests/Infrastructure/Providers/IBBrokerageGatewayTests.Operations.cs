using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class IBBrokerageGatewayTests
{
    [Fact]
    public async Task CancelOrderAsync_PendingAndWorkingCallbacksWaitForTerminalStateAndPreserveRacingFills()
    {
        var client = CreateAcknowledgingClient();
        client.OnCancelOrder = (c, id) => c.RaiseOrderStatus(Status(id, "PendingCancel"));
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var cancellation = sut.CancelOrderAsync("working-order", timeout.Token);
        cancellation.IsCompleted.Should().BeFalse();
        client.RaiseOrderStatus(Status(11001, "Submitted"));
        cancellation.IsCompleted.Should().BeFalse();
        client.RaiseOrderStatus(Status(11001, "PendingCancel"));
        client.RaiseExecution(new IBExecutionUpdate(11001, "AAPL", "BOT", 3m, 100d,
            3m, 100d, "partial-during-cancel", "DU1", "SMART", 1L, null, DateTimeOffset.UtcNow));
        cancellation.IsCompleted.Should().BeFalse();
        // A stale cancellation callback must not erase the execution callback's fill.
        client.RaiseOrderStatus(Status(11001, "Cancelled"));

        var report = await cancellation;
        report.ReportType.Should().Be(ExecutionReportType.Cancelled);
        report.OrderStatus.Should().Be(OrderStatus.Cancelled);
        report.FilledQuantity.Should().Be(3m);
        report.FillPrice.Should().Be(100m);
        var fill = await ReadUntilAsync(sut.StreamExecutionReportsAsync(timeout.Token),
            r => r.ReportType == ExecutionReportType.PartialFill, timeout.Token);
        fill.OrderStatus.Should().Be(OrderStatus.PendingCancel);
        fill.FilledQuantity.Should().Be(3m);
    }

    [Theory]
    [InlineData(10147)]
    [InlineData(10148)]
    [InlineData(161)]
    public async Task CancelOrderAsync_RefusalPreservesWorkingOrderAndCumulativeFill(int errorCode)
    {
        var client = CreateAcknowledgingClient();
        client.OnCancelOrder = (c, id) =>
        {
            c.RaiseOrderStatus(Status(id, "PendingCancel"));
            c.RaiseExecution(new IBExecutionUpdate(id, "AAPL", "BOT", 3m, 100d, 3m,
                100d, "cancel-refusal-fill", "DU1", "SMART", 1L, null, DateTimeOffset.UtcNow));
            c.RaiseError(new IBApiError(id, errorCode, "Cancellation refused", null));
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());

        var report = await sut.CancelOrderAsync("working-order");

        report.ReportType.Should().Be(ExecutionReportType.CancelRejected);
        report.OrderStatus.Should().Be(OrderStatus.PartiallyFilled);
        report.FilledQuantity.Should().Be(3m);
        report.OrderQuantity.Should().Be(10m);
        report.RejectReason.Should().Be("Cancellation refused");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var streamed = await ReadUntilAsync(sut.StreamExecutionReportsAsync(timeout.Token),
            r => r.ReportType == ExecutionReportType.CancelRejected, timeout.Token);
        streamed.OrderStatus.Should().NotBe(OrderStatus.Rejected);

        // A failed cancel leaves an order that can still be amended.
        var amended = await sut.ModifyOrderAsync("working-order", new OrderModification { NewLimitPrice = 99m });
        amended.ReportType.Should().Be(ExecutionReportType.Modified);
        amended.OrderQuantity.Should().Be(10m);
        amended.FilledQuantity.Should().Be(3m);
    }

    [Fact]
    public async Task CancelOrderAsync_FullExecutionCompletesAsFilledWithoutCancellationConfirmation()
    {
        var client = CreateAcknowledgingClient();
        client.OnCancelOrder = (c, id) =>
        {
            c.RaiseOrderStatus(Status(id, "PendingCancel"));
            c.RaiseExecution(new IBExecutionUpdate(id, "AAPL", "BOT", 10m, 100d, 10m,
                100d, "filled-during-cancel", "DU1", "SMART", 1L, null, DateTimeOffset.UtcNow));
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());

        var report = await sut.CancelOrderAsync("working-order");

        report.ReportType.Should().Be(ExecutionReportType.Fill);
        report.OrderStatus.Should().Be(OrderStatus.Filled);
        report.FilledQuantity.Should().Be(10m);
    }

    [Fact]
    public async Task OrderStatusCancelled_AfterCompleteExecutionCannotReplaceFilledState()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        client.RaiseExecution(new IBExecutionUpdate(11001, "AAPL", "BOT", 10m, 100d, 10m,
            100d, "filled", "DU1", "SMART", 1L, null, DateTimeOffset.UtcNow));
        client.OnCancelOrder = (c, id) => c.RaiseOrderStatus(Status(id, "Cancelled"));

        var report = await sut.CancelOrderAsync("working-order");

        report.ReportType.Should().Be(ExecutionReportType.Fill);
        report.OrderStatus.Should().Be(OrderStatus.Filled);
        report.FilledQuantity.Should().Be(10m);
    }

    [Fact]
    public async Task CancelOrderAsync_Code202ConfirmsCancellation()
    {
        var client = CreateAcknowledgingClient();
        client.OnCancelOrder = (c, id) => c.RaiseError(new IBApiError(id, 202, "Order cancelled", null));
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());

        var report = await sut.CancelOrderAsync("working-order");

        report.OrderStatus.Should().Be(OrderStatus.Cancelled);
        report.ReportType.Should().Be(ExecutionReportType.Cancelled);
    }

    [Theory]
    [InlineData(399)]
    [InlineData(2104)]
    [InlineData(2109)]
    [InlineData(2168)]
    [InlineData(2169)]
    public async Task CancelOrderAsync_OrderWarningsDoNotCompleteOrRejectPendingCancellation(int warningCode)
    {
        var client = CreateAcknowledgingClient();
        client.OnCancelOrder = (c, id) => c.RaiseError(new IBApiError(id, warningCode, "Order warning", null));
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var cancellation = sut.CancelOrderAsync("working-order", timeout.Token);
        cancellation.IsCompleted.Should().BeFalse();
        client.RaiseOrderStatus(Status(11001, "Cancelled"));

        (await cancellation).ReportType.Should().Be(ExecutionReportType.Cancelled);
    }

    [Theory]
    [InlineData(104)]
    [InlineData(105)]
    [InlineData(110)]
    [InlineData(201)]
    [InlineData(2102)]
    public async Task ModifyOrderAsync_RefusalKeepsPreviouslyConfirmedQuantityAndPrices(int errorCode)
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        client.OnPlaceOrder = (c, id, _) => c.RaiseError(new IBApiError(id, errorCode, "Amendment refused", null));

        var refused = await sut.ModifyOrderAsync("working-order", new OrderModification
        {
            NewQuantity = 20m,
            NewLimitPrice = 101m
        });

        refused.ReportType.Should().Be(ExecutionReportType.ModificationRejected);
        refused.OrderStatus.Should().Be(OrderStatus.Accepted);
        refused.OrderQuantity.Should().Be(10m);
        OrderRequest? retried = null;
        client.OnPlaceOrder = (c, id, request) =>
        {
            retried = request;
            c.RaiseOpenOrder(OpenOrder(id, request));
        };
        var accepted = await sut.ModifyOrderAsync("working-order", new OrderModification { NewQuantity = 11m });
        accepted.ReportType.Should().Be(ExecutionReportType.Modified);
        retried!.LimitPrice.Should().Be(100m);
        retried.Quantity.Should().Be(11m);
    }

    [Fact]
    public async Task ModifyOrderAsync_StatusAndStaleTermsDoNotAcknowledgeAmendment()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        OrderRequest? requested = null;
        client.OnPlaceOrder = (c, id, request) =>
        {
            requested = request;
            c.RaiseOrderStatus(Status(id, "Submitted"));
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var amendment = sut.ModifyOrderAsync("working-order", new OrderModification
        {
            NewQuantity = 12m,
            NewLimitPrice = 101m
        }, timeout.Token);
        amendment.IsCompleted.Should().BeFalse();
        client.RaiseOpenOrder(OpenOrder(11001, WorkingOrder()));
        amendment.IsCompleted.Should().BeFalse();
        client.RaiseOpenOrder(OpenOrder(11001, requested!));

        var report = await amendment;
        report.ReportType.Should().Be(ExecutionReportType.Modified);
        report.OrderQuantity.Should().Be(12m);
    }

    [Fact]
    public async Task ModifyOrderAsync_InactiveCallbackRefusesOperationWithoutRejectingWorkingOrder()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        client.OnPlaceOrder = (c, id, request) => c.RaiseOpenOrder(OpenOrder(id, request) with
        {
            Status = "Inactive",
            RejectReason = "Modification refused"
        });

        var report = await sut.ModifyOrderAsync("working-order", new OrderModification { NewQuantity = 20m });

        report.ReportType.Should().Be(ExecutionReportType.ModificationRejected);
        report.OrderStatus.Should().Be(OrderStatus.Accepted);
        report.OrderQuantity.Should().Be(10m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrderStatusInactive_RefusesPendingOperationWithoutRejectingWorkingOrder(bool modification)
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        client.OnPlaceOrder = (c, id, _) => c.RaiseOrderStatus(Status(id, "Inactive"));
        client.OnCancelOrder = (c, id) => c.RaiseOrderStatus(Status(id, "Inactive"));

        var report = modification
            ? await sut.ModifyOrderAsync("working-order", new OrderModification { NewQuantity = 20m })
            : await sut.CancelOrderAsync("working-order");

        report.ReportType.Should().Be(modification ? ExecutionReportType.ModificationRejected : ExecutionReportType.CancelRejected);
        report.OrderStatus.Should().Be(OrderStatus.Accepted);
        report.OrderQuantity.Should().Be(10m);
    }

    [Fact]
    public async Task ModifyOrderAsync_PreSubmittedStopOrderEchoConfirmsChangedTerms()
    {
        var client = CreateAcknowledgingClient();
        client.OnPlaceOrder = (c, id, request) => c.RaiseOpenOrder(OpenOrder(id, request) with
        {
            OrderType = "STP",
            Status = "PreSubmitted"
        });
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder() with { Type = OrderType.StopMarket, LimitPrice = null, StopPrice = 90m });

        var report = await sut.ModifyOrderAsync("working-order", new OrderModification { NewStopPrice = 91m });

        report.ReportType.Should().Be(ExecutionReportType.Modified);
        report.OrderStatus.Should().Be(OrderStatus.PendingNew);
    }

    [Theory]
    [InlineData("Filled")]
    [InlineData("Cancelled")]
    public async Task GetOpenOrdersAsync_DelayedWorkingCallbackCannotReviveTerminalOrder(string terminalStatus)
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        client.RaiseOrderStatus(Status(11001, terminalStatus, terminalStatus == "Filled" ? 10m : 0m));
        client.OnRequestOpenOrders = c =>
        {
            c.RaiseOpenOrder(OpenOrder(11001, WorkingOrder()));
            c.RaiseOpenOrdersCompleted();
        };

        (await sut.GetOpenOrdersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecutionCallbacks_MarkCumulativeAveragesAndIgnoreStaleQuantityAndPriceOnRefusal()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        client.RaiseExecution(new IBExecutionUpdate(11001, "AAPL", "BOT", 2m, 100d,
            2m, 100d, "first", "DU1", "SMART", 1L, null, DateTimeOffset.UtcNow));
        client.RaiseExecution(new IBExecutionUpdate(11001, "AAPL", "BOT", 2m, 120d,
            4m, 110d, "second", "DU1", "SMART", 1L, null, DateTimeOffset.UtcNow));
        client.RaiseExecution(new IBExecutionUpdate(11001, "AAPL", "BOT", 2m, 100d,
            2m, 100d, "first", "DU1", "SMART", 1L, null, DateTimeOffset.UtcNow));
        client.OnCancelOrder = (c, id) => c.RaiseError(new IBApiError(id, 10148, "Cancel refused", null));

        var refused = await sut.CancelOrderAsync("working-order");

        refused.ReportType.Should().Be(ExecutionReportType.CancelRejected);
        refused.FilledQuantity.Should().Be(4m);
        refused.FillPrice.Should().Be(110m);
        refused.FillPriceIsCumulativeAverage.Should().BeTrue();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var second = await ReadUntilAsync(sut.StreamExecutionReportsAsync(timeout.Token),
            r => r.ReportType == ExecutionReportType.PartialFill && r.FilledQuantity == 4m, timeout.Token);
        second.FillPrice.Should().Be(110m);
        second.FillPriceIsCumulativeAverage.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitOrderAsync_ActualOrderRejectionRemainsTerminal()
    {
        var client = CreateAcknowledgingClient();
        client.OnPlaceOrder = (c, id, _) => c.RaiseError(new IBApiError(id, 201, "Order rejected", null));
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        var report = await sut.SubmitOrderAsync(WorkingOrder());

        report.ReportType.Should().Be(ExecutionReportType.Rejected);
        report.OrderStatus.Should().Be(OrderStatus.Rejected);
    }

    [Fact]
    public async Task CancelOrderAsync_CallerCancellationRemovesOnlyItsPendingOperation()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        using var cancellation = new CancellationTokenSource();
        var abandoned = sut.CancelOrderAsync("working-order", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        client.OnCancelOrder = (c, id) => c.RaiseOrderStatus(Status(id, "Cancelled"));

        (await sut.CancelOrderAsync("working-order")).OrderStatus.Should().Be(OrderStatus.Cancelled);
    }

    private static FakeIbBrokerageClient CreateAcknowledgingClient()
        => new()
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(11001),
            OnPlaceOrder = (c, id, request) => c.RaiseOpenOrder(OpenOrder(id, request))
        };

    private static OrderRequest WorkingOrder() => new()
    {
        ClientOrderId = "working-order",
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 100m
    };

    private static IBOpenOrderUpdate OpenOrder(int id, OrderRequest request)
        => new(id, request.Symbol, "STK", "BUY", "LMT", request.Quantity, 0m,
            request.LimitPrice is decimal limit ? (double)limit : null,
            request.StopPrice is decimal stop ? (double)stop : null,
            "Submitted", request.ClientOrderId, "DU1", null, null, request.Metadata, DateTimeOffset.UtcNow);

    private static IBOrderStatusUpdate Status(int id, string status, decimal filled = 0m)
        => new(id, status, filled, 10m - filled, filled > 0 ? 100d : 0d, 0d, 1L, 1,
            null, null, DateTimeOffset.UtcNow);
}
