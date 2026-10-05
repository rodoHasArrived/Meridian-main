using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class BrokerageOrderRecoveryTests
{
    private static readonly Guid FundAccountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DisconnectAfterBrokerAcceptance_RetainsWorkingExposureAndRefusesRetry()
    {
        using var directory = new RecoveryDirectory();
        var gateway = new RecoveryGateway
        {
            SubmitResponse = (_, _) => Task.FromException<ExecutionReport>(
                new IOException("Broker accepted the order; the response connection was lost."))
        };
        await using var oms = CreateOms(gateway, recoveryPath: directory.Path);
        var request = Request("acceptance-response-lost");

        var result = await oms.PlaceOrderAsync(request);
        var retry = await oms.PlaceOrderAsync(request);

        result.Success.Should().BeFalse();
        result.OrderState.Status.Should().Be(OrderStatus.PendingNew,
            "a lost response cannot establish that the broker rejected the order");
        oms.GetOpenOrders().Should().ContainSingle(order =>
            order.OrderId == request.ClientOrderId && order.Quantity - order.FilledQuantity == 10m);
        retry.Success.Should().BeFalse();
        gateway.SubmitCount.Should().Be(1, "retrying an ambiguous client ID must never route twice");
        oms.GetRecoveryOrders(FundAccountId).Should().ContainSingle(order =>
            order.OrderId == request.ClientOrderId);
    }

    [Fact]
    public async Task PartialFillBeforeSubmitDisconnect_RetainsExposureAndBooksLateFillOnlyOnce()
    {
        using var directory = new RecoveryDirectory();
        var response = new TaskCompletionSource<ExecutionReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new RecoveryGateway { SubmitResponse = (_, _) => response.Task };
        var portfolio = new PaperTradingPortfolio(100_000m);
        await using var oms = CreateOms(gateway, portfolio, directory.Path);
        var request = Request("partial-before-disconnect");

        var submission = oms.PlaceOrderAsync(request);
        await gateway.SubmissionStarted.Task.WaitAsync(Timeout);
        await gateway.PublishAsync(Report(request.ClientOrderId!, OrderStatus.PartiallyFilled, 4m));
        (await ReadFillAsync(oms)).FilledQuantity.Should().Be(4m);
        response.SetException(new IOException("Lost response after the first partial execution."));
        var result = await submission.WaitAsync(Timeout);

        result.OrderState.Status.Should().Be(OrderStatus.PartiallyFilled);
        result.OrderState.FilledQuantity.Should().Be(4m);
        portfolio.Positions["AAPL"].Quantity.Should().Be(4);
        oms.GetOpenOrders().Sum(order => order.Quantity - order.FilledQuantity).Should().Be(6m,
            "the working remainder and the filled position must preserve all ten shares of exposure");
        oms.GetRecoveryOrders(FundAccountId).Should().ContainSingle(order =>
            order.OrderId == request.ClientOrderId && order.FilledQuantity == 4m);
        (await oms.PlaceOrderAsync(request)).Success.Should().BeFalse();
        gateway.SubmitCount.Should().Be(1);

        var finalFill = Report(request.ClientOrderId!, OrderStatus.Filled, 10m);
        await gateway.PublishAsync(finalFill);
        (await ReadFillAsync(oms)).FilledQuantity.Should().Be(6m);
        await gateway.PublishAsync(finalFill);
        (await DrainThroughMarkerAsync(oms, gateway)).Should().BeEmpty(
            "a replayed terminal fill must not produce another trade");

        oms.GetOrder(request.ClientOrderId!)!.Status.Should().Be(OrderStatus.Filled);
        oms.GetOpenOrders().Should().BeEmpty();
        portfolio.Positions["AAPL"].Quantity.Should().Be(10);
        portfolio.Cash.Should().Be(98_500m);
    }

    [Fact]
    public async Task FillArrivingAfterCancellation_PreservesCancelledRemainderAndActualExposure()
    {
        using var directory = new RecoveryDirectory();
        var gateway = new RecoveryGateway();
        var portfolio = new PaperTradingPortfolio(100_000m);
        await using var oms = CreateOms(gateway, portfolio, directory.Path);
        var request = Request("cancel-before-late-fill");
        (await oms.PlaceOrderAsync(request)).Success.Should().BeTrue();
        (await oms.CancelOrderAsync(request.ClientOrderId!)).Success.Should().BeTrue();

        // A fill executed before cancellation can be delivered after its acknowledgement.
        await gateway.PublishAsync(Report(request.ClientOrderId!, OrderStatus.PartiallyFilled, 4m));
        (await ReadFillAsync(oms)).FilledQuantity.Should().Be(4m);

        oms.GetOrder(request.ClientOrderId!)!.Status.Should().Be(OrderStatus.Cancelled,
            "new fill evidence cannot reopen the cancelled remainder");
        oms.GetOrder(request.ClientOrderId!)!.FilledQuantity.Should().Be(4m);
        oms.GetOpenOrders().Should().BeEmpty();
        portfolio.Positions["AAPL"].Quantity.Should().Be(4);

        var finalFill = Report(request.ClientOrderId!, OrderStatus.Filled, 10m);
        await gateway.PublishAsync(finalFill);
        (await ReadFillAsync(oms)).FilledQuantity.Should().Be(6m);
        await gateway.PublishAsync(finalFill);
        (await DrainThroughMarkerAsync(oms, gateway)).Should().BeEmpty();

        oms.GetOrder(request.ClientOrderId!)!.Status.Should().Be(OrderStatus.Filled);
        portfolio.Positions["AAPL"].Quantity.Should().Be(10);
        portfolio.Cash.Should().Be(98_500m);
        (await oms.PlaceOrderAsync(request)).Success.Should().BeFalse();
        gateway.SubmitCount.Should().Be(1);
    }

    [Fact]
    public async Task RestartAfterLostAcceptance_RestoresAccountScopeAndRequiresReconciliation()
    {
        using var directory = new RecoveryDirectory();
        var request = Request("restart-unknown-acceptance");
        var firstGateway = new RecoveryGateway
        {
            SubmitResponse = (_, _) => Task.FromException<ExecutionReport>(new IOException("Disconnected."))
        };
        await using (var first = CreateOms(firstGateway, recoveryPath: directory.Path))
        {
            (await first.PlaceOrderAsync(request)).OrderState.Status.Should().Be(OrderStatus.PendingNew);
        }

        var recoveredGateway = new RecoveryGateway();
        var recoveredPortfolio = new PaperTradingPortfolio(100_000m);
        await using var recovered = CreateOms(recoveredGateway, recoveredPortfolio, directory.Path);
        var retained = recovered.GetOpenOrders().Should().ContainSingle().Subject;
        retained.OrderId.Should().Be(request.ClientOrderId);
        retained.FundAccountId.Should().Be(FundAccountId);
        retained.StrategyId.Should().Be(request.StrategyId);
        retained.RunId.Should().Be(request.Metadata!["runId"],
            "the recovery panel must identify the affected strategy run after restart");
        retained.Quantity.Should().Be(10m);
        recovered.GetRecoveryOrders(FundAccountId).Should().ContainSingle();
        recovered.GetRecoveryOrders(Guid.NewGuid()).Should().BeEmpty();

        (await recovered.PlaceOrderAsync(request)).Success.Should().BeFalse();
        recoveredGateway.SubmitCount.Should().Be(0);

        await recovered.ReconcileRecoveryOrderAsync(Report(request.ClientOrderId!, OrderStatus.Filled, 10m));
        (await ReadFillAsync(recovered)).FilledQuantity.Should().Be(10m);
        recovered.GetRecoveryOrders(FundAccountId).Should().BeEmpty();
        recovered.GetOpenOrders().Should().BeEmpty();
        recoveredPortfolio.Positions["AAPL"].Quantity.Should().Be(10);
        recoveredPortfolio.Cash.Should().Be(98_500m);
        recoveredGateway.SubmitCount.Should().Be(0, "recovery must observe broker state without resubmitting");
    }

    [Fact]
    public async Task RestartAfterPartialFill_DeduplicatesReplayAndRetainsUnfilledExposure()
    {
        using var directory = new RecoveryDirectory();
        var request = Request("restart-partial-fill");
        var partial = Report(request.ClientOrderId!, OrderStatus.PartiallyFilled, 4m);
        var firstGateway = new RecoveryGateway { SubmitResponse = (_, _) => Task.FromResult(partial) };
        await using (var first = CreateOms(firstGateway, new PaperTradingPortfolio(100_000m), directory.Path))
        {
            (await first.PlaceOrderAsync(request)).Success.Should().BeTrue();
            (await ReadFillAsync(first)).FilledQuantity.Should().Be(4m);
        }

        // Rebuild the previously persisted position, as the portfolio owner does on restart.
        // Order recovery must neither reapply this fill nor discard the six working shares.
        var recoveredPortfolio = new PaperTradingPortfolio(100_000m);
        recoveredPortfolio.ApplyFill(partial);
        var recoveredGateway = new RecoveryGateway();
        await using var recovered = CreateOms(recoveredGateway, recoveredPortfolio, directory.Path);
        recovered.GetOpenOrders().Should().ContainSingle(order =>
            order.FilledQuantity == 4m && order.Quantity - order.FilledQuantity == 6m);

        await recoveredGateway.PublishAsync(partial);
        (await DrainThroughMarkerAsync(recovered, recoveredGateway)).Should().BeEmpty();
        recoveredPortfolio.Positions["AAPL"].Quantity.Should().Be(4);
        recoveredPortfolio.Cash.Should().Be(99_400m);

        await recoveredGateway.PublishAsync(Report(request.ClientOrderId!, OrderStatus.Filled, 10m));
        (await ReadFillAsync(recovered)).FilledQuantity.Should().Be(6m);
        recoveredPortfolio.Positions["AAPL"].Quantity.Should().Be(10);
        recoveredPortfolio.Cash.Should().Be(98_500m);
        recovered.GetOpenOrders().Should().BeEmpty();
        (await recovered.PlaceOrderAsync(request)).Success.Should().BeFalse();
        recoveredGateway.SubmitCount.Should().Be(0);
    }

    [Fact]
    public async Task RecoveryAfterPartialFill_UsesMissingFillEconomicsFromBrokerCumulativeAverage()
    {
        using var directory = new RecoveryDirectory();
        var request = Request("recover-cumulative-average");
        var partial = Report(request.ClientOrderId!, OrderStatus.PartiallyFilled, 4m) with { FillPrice = 149m };
        var firstGateway = new RecoveryGateway { SubmitResponse = (_, _) => Task.FromResult(partial) };
        await using (var first = CreateOms(firstGateway, new PaperTradingPortfolio(100_000m), directory.Path))
        {
            (await first.PlaceOrderAsync(request)).Success.Should().BeTrue();
            (await ReadFillAsync(first)).FillPrice.Should().Be(149m);
        }

        var recoveredPortfolio = new PaperTradingPortfolio(100_000m);
        recoveredPortfolio.ApplyFill(partial);
        var recoveredGateway = new RecoveryGateway();
        await using var recovered = CreateOms(recoveredGateway, recoveredPortfolio, directory.Path);
        var retained = recovered.GetRecoveryOrders(FundAccountId).Should().ContainSingle().Subject;
        retained.FilledQuantity.Should().Be(4m);
        retained.AverageFillPrice.Should().Be(149m);
        retained.RunId.Should().Be(request.Metadata!["runId"]);

        // Broker lookup reports cumulative economics: 10 shares at a $150.20 average.
        // After the retained 4 at $149, the missing 6 shares therefore executed at $151.
        var evidence = Report(request.ClientOrderId!, OrderStatus.Filled, 10m) with { FillPrice = 150.2m };
        await recovered.ReconcileRecoveryOrderAsync(evidence);
        var increment = await ReadFillAsync(recovered);

        increment.FilledQuantity.Should().Be(6m);
        increment.FillPrice.Should().Be(151m);
        recovered.GetOrder(request.ClientOrderId!)!.AverageFillPrice.Should().Be(150.2m);
        recoveredPortfolio.Positions["AAPL"].Quantity.Should().Be(10);
        recoveredPortfolio.Cash.Should().Be(98_498m,
            "recovery must book exactly $1,502 of cumulative broker fills");
        recovered.GetRecoveryOrders(FundAccountId).Should().BeEmpty();
        recovered.GetOpenOrders().Should().BeEmpty();

        await recovered.ReconcileRecoveryOrderAsync(evidence);
        (await DrainThroughMarkerAsync(recovered, recoveredGateway)).Should().BeEmpty();
        recoveredPortfolio.Cash.Should().Be(98_498m);
        recoveredGateway.SubmitCount.Should().Be(0);
    }

    [Theory]
    [InlineData("MSFT", OrderSide.Buy, 10)]
    [InlineData("AAPL", OrderSide.Sell, 10)]
    [InlineData("AAPL", OrderSide.Buy, 11)]
    public async Task InconsistentRecoveryEvidence_LeavesOrderBlockedWithoutChangingExposure(
        string symbol,
        OrderSide side,
        int filledQuantity)
    {
        using var directory = new RecoveryDirectory();
        var gateway = new RecoveryGateway
        {
            SubmitResponse = (_, _) => Task.FromException<ExecutionReport>(new IOException("Disconnected."))
        };
        var portfolio = new PaperTradingPortfolio(100_000m);
        await using var oms = CreateOms(gateway, portfolio, directory.Path);
        var request = Request("inconsistent-recovery-evidence");
        await oms.PlaceOrderAsync(request);

        var evidence = Report(request.ClientOrderId!, OrderStatus.Filled, filledQuantity) with
        {
            Symbol = symbol,
            Side = side
        };
        var reconcile = () => oms.ReconcileRecoveryOrderAsync(evidence);

        await reconcile.Should().ThrowAsync<InvalidDataException>();
        oms.GetRecoveryOrders(FundAccountId).Should().ContainSingle();
        oms.GetOpenOrders().Should().ContainSingle(order =>
            order.Status == OrderStatus.PendingNew && order.FilledQuantity == 0m);
        portfolio.Positions.Should().BeEmpty();
        portfolio.Cash.Should().Be(100_000m);
        gateway.SubmitCount.Should().Be(1);
    }

    [Theory]
    [InlineData(OrderStatus.Filled)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Rejected)]
    [InlineData(OrderStatus.Expired)]
    public async Task RestartAfterTerminalBrokerResponse_DoesNotReuseDispatchedClientId(OrderStatus status)
    {
        using var directory = new RecoveryDirectory();
        var request = Request($"restart-terminal-{status}");
        var gateway = new RecoveryGateway
        {
            SubmitResponse = (order, _) => Task.FromResult(Report(
                order.ClientOrderId!, status, status == OrderStatus.Filled ? 10m : 0m))
        };
        await using (var first = CreateOms(gateway, recoveryPath: directory.Path))
        {
            (await first.PlaceOrderAsync(request)).OrderState.Status.Should().Be(status);
        }

        var recoveredGateway = new RecoveryGateway();
        await using var recovered = CreateOms(recoveredGateway, recoveryPath: directory.Path);

        if (status is OrderStatus.Filled or OrderStatus.Rejected)
            recovered.GetRecoveryOrders(FundAccountId).Should().BeEmpty(
                "a definitive rejection may never create a broker order, and a full fill has no execution remainder");
        else
            recovered.GetRecoveryOrders(FundAccountId).Should().ContainSingle(
                "cancelled and expired orders still need late-fill verification after restart");

        (await recovered.PlaceOrderAsync(request)).Success.Should().BeFalse();
        recoveredGateway.SubmitCount.Should().Be(0,
            "a terminal acknowledgement does not authorize another submission with the same client ID");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"version\":1,")]
    [InlineData("null")]
    public void CorruptRecoveryFile_FailsClosedInsteadOfStartingWithAnEmptyBook(string contents)
    {
        using var directory = new RecoveryDirectory();
        File.WriteAllText(directory.Path, contents);

        var reopen = () => new FileBrokerageOrderRecoveryStore(directory.Path, "alpaca");

        var exception = reopen.Should().Throw<Exception>().Which;
        (exception is JsonException or InvalidDataException).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, -1)]
    [InlineData(10, 11)]
    public void InvalidRetainedQuantities_FailClosed(int quantity, int filledQuantity)
    {
        using var directory = new RecoveryDirectory();
        File.WriteAllText(directory.Path, $$"""
            {
              "version": 1,
              "gatewayId": "alpaca",
              "orders": [{
                "state": {
                  "orderId": "invalid-persisted-quantity",
                  "symbol": "AAPL",
                  "side": "Buy",
                  "type": "Limit",
                  "quantity": {{quantity}},
                  "filledQuantity": {{filledQuantity}},
                  "status": "Accepted"
                },
                "requiresRecovery": true
              }]
            }
            """);

        var reopen = () => new FileBrokerageOrderRecoveryStore(directory.Path, "alpaca");

        reopen.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task RecoveryFileFromAnotherGateway_FailsClosed()
    {
        using var directory = new RecoveryDirectory();
        await using (var first = CreateOms(new RecoveryGateway(), recoveryPath: directory.Path))
        {
            (await first.PlaceOrderAsync(Request("foreign-gateway-order"))).Success.Should().BeTrue();
        }

        var reopen = () => new FileBrokerageOrderRecoveryStore(directory.Path, "interactive-brokers");

        reopen.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task RecoveryWriteFailure_PreventsBrokerDispatch()
    {
        using var directory = new RecoveryDirectory();
        // An existing directory at the snapshot filename makes atomic replacement fail
        // without relying on platform-specific file permissions or elevated test users.
        Directory.CreateDirectory(directory.Path);
        var gateway = new RecoveryGateway();
        await using var oms = CreateOms(gateway, recoveryPath: directory.Path);

        var result = await oms.PlaceOrderAsync(Request("write-before-dispatch-failure"));

        result.Success.Should().BeFalse();
        result.OrderState.Status.Should().Be(OrderStatus.Rejected);
        gateway.SubmitCount.Should().Be(0,
            "a broker call without durable identity could execute twice after restart");
        oms.GetOpenOrders().Should().BeEmpty();
    }

    private static OrderManagementSystem CreateOms(
        RecoveryGateway gateway,
        PaperTradingPortfolio? portfolio = null,
        string? recoveryPath = null) => new(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio,
            recoveryStore: recoveryPath is null
                ? null
                : new FileBrokerageOrderRecoveryStore(recoveryPath, gateway.GatewayId));

    private static OrderRequest Request(string clientOrderId) => new()
    {
        ClientOrderId = clientOrderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 150m,
        FundAccountId = FundAccountId,
        StrategyId = "account-recovery-strategy",
        Metadata = new Dictionary<string, string> { ["runId"] = "account-recovery-run" }
    };

    private static ExecutionReport Report(string clientOrderId, OrderStatus status, decimal filledQuantity) => new()
    {
        OrderId = clientOrderId,
        ClientOrderId = clientOrderId,
        GatewayOrderId = $"broker-{clientOrderId}",
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        OrderStatus = status,
        ReportType = status switch
        {
            OrderStatus.Filled => ExecutionReportType.Fill,
            OrderStatus.PartiallyFilled => ExecutionReportType.PartialFill,
            OrderStatus.Cancelled => ExecutionReportType.Cancelled,
            OrderStatus.Rejected => ExecutionReportType.Rejected,
            OrderStatus.Expired => ExecutionReportType.Expired,
            _ => ExecutionReportType.New
        },
        OrderQuantity = 10m,
        FilledQuantity = filledQuantity,
        FillPrice = filledQuantity > 0m ? 150m : null,
        Commission = 0m,
        Timestamp = DateTimeOffset.UtcNow
    };

    private static async Task<ExecutionReport> ReadFillAsync(OrderManagementSystem oms)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        return await oms.ExecutionReports.ReadAsync(timeout.Token);
    }

    private static async Task<IReadOnlyList<ExecutionReport>> DrainThroughMarkerAsync(
        OrderManagementSystem oms,
        RecoveryGateway gateway)
    {
        // The single report pump observes this external marker only after every preceding
        // replay. This avoids delays or racing assertions against asynchronous processing.
        var markerId = $"external-marker-{Guid.NewGuid():N}";
        await gateway.PublishAsync(Report(markerId, OrderStatus.Filled, 1m) with { Symbol = "MARKER" });
        var reports = new List<ExecutionReport>();
        using var timeout = new CancellationTokenSource(Timeout);
        while (true)
        {
            var report = await oms.ExecutionReports.ReadAsync(timeout.Token);
            if (report.ClientOrderId == markerId)
                return reports;
            reports.Add(report);
        }
    }

    private sealed class RecoveryGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();

        public Func<OrderRequest, CancellationToken, Task<ExecutionReport>>? SubmitResponse { get; init; }

        public TaskCompletionSource SubmissionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SubmitCount { get; private set; }

        public string GatewayId => "alpaca";

        public bool IsConnected => true;

        public ExecutionMode ExecutionMode => ExecutionMode.Paper;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            SubmitCount++;
            SubmissionStarted.TrySetResult();
            return SubmitResponse?.Invoke(request, ct)
                ?? Task.FromResult(Report(request.ClientOrderId!, OrderStatus.Accepted, 0m));
        }

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            Task.FromResult(Report(orderId, OrderStatus.Cancelled, 0m));

        public Task<ExecutionReport> ModifyOrderAsync(
            string orderId,
            OrderModification modification,
            CancellationToken ct = default) => throw new NotSupportedException();

        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default) =>
            _reports.Reader.ReadAllAsync(ct);

        public ValueTask PublishAsync(ExecutionReport report) => _reports.Writer.WriteAsync(report);
    }

    private sealed class RecoveryDirectory : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "meridian-tests", "brokerage-order-recovery", Guid.NewGuid().ToString("N"));

        public RecoveryDirectory() => Directory.CreateDirectory(_directory);

        public string Path => System.IO.Path.Combine(_directory, "orders.json");

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
