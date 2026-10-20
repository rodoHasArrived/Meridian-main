using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Infrastructure.Adapters.Alpaca;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class AlpacaBrokerageGatewayTests
{
    [Fact]
    public async Task OmsAmendments_RouteBrokerIdentities_AndBookReplacementChainFillsExactlyOnce()
    {
        const string localId = "local-client-id";
        var patchPaths = new List<string>();
        var deletes = new List<string>();
        var patches = 0;
        var cancelled = false;
        using var handler = new Meridian.Tests.TestHelpers.StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v2/account")
                return new(HttpStatusCode.OK) { Content = BuildAccountResponse() };
            if (request.Method == HttpMethod.Post)
                return new(HttpStatusCode.OK)
                {
                    Content = BuildOrderResponse("broker-1", "partially_filled", clientOrderId: localId,
                    qty: "10", filledQty: "2", filledAvgPrice: "100")
                };
            if (request.Method == HttpMethod.Patch)
            {
                patchPaths.Add(path);
                patches++;
                return patches switch
                {
                    1 => new(HttpStatusCode.OK)
                    {
                        Content = BuildOrderResponse("broker-2", "partially_filled", clientOrderId: "replacement-client-2",
                        qty: "12", filledQty: "5", filledAvgPrice: "106")
                    },
                    2 => new(HttpStatusCode.OK)
                    {
                        Content = BuildOrderResponse("broker-3", "partially_filled", clientOrderId: "replacement-client-3",
                        qty: "12", filledQty: "8", filledAvgPrice: "111.25")
                    },
                    _ => new(HttpStatusCode.UnprocessableEntity) { Content = BuildJson(new { message = "amendment refused" }) }
                };
            }
            if (request.Method == HttpMethod.Delete)
            {
                deletes.Add(path);
                cancelled = true;
                return new(HttpStatusCode.NoContent);
            }
            if (path == "/v2/orders/broker-1")
                return new(HttpStatusCode.OK)
                {
                    Content = BuildOrderResponse("broker-1", patches >= 1 ? "replaced" : "partially_filled", clientOrderId: localId,
                        qty: "10", filledQty: "2", filledAvgPrice: "100", replacedBy: patches >= 1 ? "broker-2" : null, limitPrice: "150")
                };
            if (path == "/v2/orders/broker-2")
                return new(HttpStatusCode.OK)
                {
                    Content = BuildOrderResponse("broker-2", patches >= 2 ? "replaced" : "partially_filled", clientOrderId: "replacement-client-2",
                        qty: "12", filledQty: "5", filledAvgPrice: "106", replaces: "broker-1", replacedBy: patches >= 2 ? "broker-3" : null, limitPrice: "150")
                };
            path.Should().Be("/v2/orders/broker-3");
            return new(HttpStatusCode.OK)
            {
                Content = BuildOrderResponse("broker-3", cancelled ? "canceled" : "partially_filled",
                clientOrderId: "replacement-client-3", qty: "12", filledQty: patches >= 3 ? "10" : "8",
                filledAvgPrice: patches >= 3 ? "113" : "111.25", replaces: "broker-2", limitPrice: "149")
            };
        });
        await using var gateway = CreateSut(handler);
        await gateway.ConnectAsync();
        var portfolio = new PaperTradingPortfolio(100_000m);
        var publisher = new AmendmentPublisher();
        using var oms = new OrderManagementSystem(new SimulatedAlpaca(gateway), NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio, tradeEventPublisher: publisher);
        await oms.StartAsync();

        (await oms.PlaceOrderAsync(new OrderRequest
        {
            ClientOrderId = localId,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        })).Success.Should().BeTrue();
        var reports = (System.Threading.Channels.Channel<ExecutionReport>)typeof(AlpacaBrokerageGateway)
            .GetField("_reportChannel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(gateway)!;
        var rejectedTime = DateTimeOffset.Parse("2026-10-15T11:00:00Z");
        // This is the mapped Alpaca trade-update shape: the proposed replacement was
        // rejected while the original broker-1 remains partially filled and working.
        await reports.Writer.WriteAsync(new ExecutionReport
        {
            OrderId = "rejected-replacement",
            GatewayOrderId = "rejected-replacement",
            ClientOrderId = "rejected-replacement-client",
            ReplacesOrderId = "broker-1",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            ReportType = ExecutionReportType.Rejected,
            OrderStatus = OrderStatus.Rejected,
            OrderQuantity = 20m,
            FilledQuantity = 0m,
            RejectReason = "Replacement refused",
            Timestamp = rejectedTime
        });
        using (var observed = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            while (oms.GetOrder(localId)!.LastUpdatedAt != rejectedTime)
                await Task.Delay(10, observed.Token);
        oms.GetOrder(localId)!.Status.Should().Be(OrderStatus.PartiallyFilled);
        oms.GetOrder(localId)!.FilledQuantity.Should().Be(2m);
        oms.GetOpenOrders().Should().ContainSingle();
        (await oms.ModifyOrderAsync(localId, new OrderModification { NewQuantity = 12m })).Success.Should().BeTrue();
        (await oms.ModifyOrderAsync(localId, new OrderModification { NewLimitPrice = 149m })).Success.Should().BeTrue();
        // An old middle-chain replacement can be replayed after its own successor was
        // acknowledged. It must not restore broker-2 as the next amendment/cancel target.
        var lateTime = DateTimeOffset.Parse("2026-10-15T12:00:00Z");
        await reports.Writer.WriteAsync(new ExecutionReport
        {
            OrderId = "broker-2",
            GatewayOrderId = "broker-2",
            ClientOrderId = "replacement-client-2",
            ReplacesOrderId = "broker-1",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            ReportType = ExecutionReportType.Cancelled,
            OrderStatus = OrderStatus.Cancelled,
            OrderQuantity = 12m,
            FilledQuantity = 5m,
            FillPrice = 106m,
            FillPriceIsCumulativeAverage = true,
            Timestamp = lateTime
        });
        using (var observed = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            while (oms.GetOrder(localId)!.LastUpdatedAt != lateTime)
                await Task.Delay(10, observed.Token);
        oms.GetOrder(localId)!.Status.Should().Be(OrderStatus.PartiallyFilled,
            "a cancellation from a superseded broker identity cannot terminate its working replacement");
        var refused = await oms.ModifyOrderAsync(localId, new OrderModification { NewLimitPrice = 148m });

        refused.Success.Should().BeFalse();
        refused.OrderState!.Status.Should().Be(OrderStatus.PartiallyFilled);
        refused.OrderState.Quantity.Should().Be(12m);
        refused.OrderState.FilledQuantity.Should().Be(10m);
        refused.OrderState.AverageFillPrice.Should().Be(113m);
        var cancellation = await oms.CancelOrderAsync(localId);
        cancellation.Success.Should().BeTrue();
        cancellation.OrderState!.Status.Should().Be(OrderStatus.Cancelled);
        patchPaths.Should().Equal("/v2/orders/broker-1", "/v2/orders/broker-2", "/v2/orders/broker-3");
        deletes.Should().Equal("/v2/orders/broker-3");
        publisher.Events.Select(fill => fill.FilledQuantity).Should().Equal(2m, 3m, 3m, 2m);
        publisher.Events.Select(fill => fill.FillPrice).Should().Equal(100m, 110m, 120m, 120m);
        publisher.Events.Select(fill => fill.OrderId).Distinct().Should().Equal(localId);
        portfolio.Positions["AAPL"].Quantity.Should().Be(10L);
        oms.GetAllRetainedOrderStates().Should().ContainSingle();
    }

    [Fact]
    public async Task ModifyOrderAsync_UuidShapedClientId_ResolvesOnlyClientNamespace()
    {
        const string clientId = "9d6197ed-2372-4cdb-8faa-c5c99e1c1c5b";
        var paths = new List<string>();
        using var handler = new Meridian.Tests.TestHelpers.StubHttpMessageHandler(request =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            return new(HttpStatusCode.OK)
            {
                Content = BuildOrderResponse(request.Method == HttpMethod.Patch ? "replacement" : "actual-broker",
                    clientOrderId: clientId, qty: "2")
            };
        });
        await using var gateway = CreateSut(handler);
        MarkConnected(gateway);
        var result = await gateway.ModifyOrderAsync(
            new OrderCancellationIdentifier(clientId, OrderCancellationIdentifierKind.ClientOrderId),
            new OrderModification { NewQuantity = 2m });

        paths.Should().Equal($"/v2/orders:by_client_order_id?client_order_id={clientId}", "/v2/orders/actual-broker",
            "/v2/orders/actual-broker", "/v2/orders/replacement");
        result.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        result.GatewayOrderId.Should().Be("actual-broker");
        result.CandidateGatewayOrderId.Should().Be("replacement");
        result.ClientOrderId.Should().Be(clientId);
    }

    [Fact]
    public async Task ModifyOrderAsync_RejectedReplacementAcknowledgement_VerifiesOriginalWorkingState()
    {
        using var handler = new Meridian.Tests.TestHelpers.StubHttpMessageHandler(request => new(HttpStatusCode.OK)
        {
            Content = request.Method == HttpMethod.Patch
                ? BuildOrderResponse("replacement", "rejected", clientOrderId: "replacement-client")
                : BuildOrderResponse("original", "partially_filled", qty: "5", filledQty: "2", filledAvgPrice: "100")
        });
        await using var gateway = CreateSut(handler);
        MarkConnected(gateway);

        var result = await gateway.ModifyOrderAsync("original", new OrderModification { NewQuantity = 5m });

        result.ReportType.Should().Be(ExecutionReportType.ModificationRejected);
        result.OrderStatus.Should().Be(OrderStatus.PartiallyFilled);
        result.GatewayOrderId.Should().Be("original");
        result.FilledQuantity.Should().Be(2m);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(-1)]
    [InlineData(-2)]
    public async Task ModifyOrderAsync_UncertainPatchOutcome_RetainsPendingIdentityAndExposure(int failure)
    {
        var requests = 0;
        using var handler = new Meridian.Tests.TestHelpers.StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = BuildOrderResponse("original-broker") };
            requests++;
            request.Method.Should().Be(HttpMethod.Patch);
            if (failure == -1)
                throw new HttpRequestException("Transport closed after dispatch.");
            if (failure == -2)
                throw new TaskCanceledException("HTTP client timeout after dispatch.");
            return new((HttpStatusCode)failure);
        });
        await using var gateway = CreateSut(handler);
        MarkConnected(gateway);

        var report = await gateway.ModifyOrderAsync("original-broker", new OrderModification { NewQuantity = 20m });

        report.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        report.OrderStatus.Should().Be(OrderStatus.Accepted);
        report.GatewayOrderId.Should().Be("original-broker");
        report.RejectReason.Should().Contain("reconcile");
        requests.Should().Be(1, "an uncertain PATCH must not be retried or treated as a definite refusal");
    }

    private sealed class AmendmentPublisher : ITradeEventPublisher
    {
        public ConcurrentQueue<TradeExecutedEvent> Events { get; } = new();
        public void Publish(TradeExecutedEvent tradeEvent) => Events.Enqueue(tradeEvent);
    }

    [Fact]
    public async Task OmsReplacementAliases_SurviveCompactionAndRestart_WithoutAdoptingDuplicateFills()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"alpaca-alias-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "recovery.json");
            var store = new FileBrokerageOrderRecoveryStore(path, "alpaca");
            store.BindScope("paper:account");
            store.Save(new RetainedBrokerageOrder(new OrderState
            {
                OrderId = "original-client",
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = OrderType.Limit,
                Quantity = 12m,
                FilledQuantity = 12m,
                AverageFillPrice = 100m,
                Status = OrderStatus.Filled
            }, "broker-3", false)
            { BrokerOrderAliases = ["broker-1", "broker-2", "broker-3"] });
            store.TryCompact("original-client").Should().BeTrue();
            await using var gateway = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, BuildAccountResponse()));
            MarkConnected(gateway);
            var publisher = new AmendmentPublisher();
            var portfolio = new PaperTradingPortfolio(100_000m);
            await using var oms = new OrderManagementSystem(new SimulatedAlpaca(gateway), NullLogger<OrderManagementSystem>.Instance,
                portfolioState: portfolio, tradeEventPublisher: publisher,
                recoveryStore: new FileBrokerageOrderRecoveryStore(path, "alpaca"));
            await oms.StartAsync();
            var reports = (System.Threading.Channels.Channel<ExecutionReport>)typeof(AlpacaBrokerageGateway)
                .GetField("_reportChannel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(gateway)!;
            await reports.Writer.WriteAsync(new ExecutionReport
            {
                OrderId = "broker-2",
                GatewayOrderId = "broker-2",
                ClientOrderId = "replacement-client-2",
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Filled,
                ReportType = ExecutionReportType.Fill,
                OrderQuantity = 12m,
                FilledQuantity = 12m,
                LastFillQuantity = 7m,
                FillPrice = 100m,
                AssetClass = "us_equity"
            });
            // The pump is sequential: observing this later genuine fill proves the replay was handled.
            await reports.Writer.WriteAsync(new ExecutionReport
            {
                OrderId = "sentinel",
                Symbol = "MSFT",
                Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Filled,
                ReportType = ExecutionReportType.Fill,
                OrderQuantity = 1m,
                FilledQuantity = 1m,
                LastFillQuantity = 1m,
                FillPrice = 100m,
                AssetClass = "us_equity"
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            (await oms.ExecutionReports.ReadAsync(timeout.Token)).OrderId.Should().Be("sentinel");
            publisher.Events.Should().ContainSingle().Which.Symbol.Should().Be("MSFT");
            portfolio.Positions.Should().NotContainKey("AAPL");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetOrderForRecoveryAsync_FollowsValidatedReplacementChain_WithOriginalClientIdentity()
    {
        using var handler = new Meridian.Tests.TestHelpers.StubHttpMessageHandler(request => new(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath == "/v2/orders:by_client_order_id"
                ? BuildJson(new
                {
                    id = "original",
                    client_order_id = "local",
                    symbol = "AAPL",
                    side = "buy",
                    status = "replaced",
                    qty = "10",
                    filled_qty = "2",
                    filled_avg_price = "100",
                    updated_at = "2026-10-15T12:00:00Z",
                    replaced_by = "replacement"
                })
                : BuildJson(new
                {
                    id = "replacement",
                    client_order_id = "replacement-client",
                    symbol = "AAPL",
                    side = "buy",
                    status = "partially_filled",
                    qty = "12",
                    filled_qty = "5",
                    filled_avg_price = "106",
                    updated_at = "2026-10-15T12:00:01Z",
                    replaces = "original"
                })
        });
        await using var gateway = CreateSut(handler);
        MarkConnected(gateway);

        var result = await gateway.GetOrderForRecoveryAsync("local");

        result!.ClientOrderId.Should().Be("local");
        result.GatewayOrderId.Should().Be("replacement");
        result.FilledQuantity.Should().Be(5m);
        result.FillPrice.Should().Be(106m);
    }

    // The transport is a real Alpaca gateway over stub HTTP; only production readiness gates
    // are bypassed by advertising this deterministic test harness as a simulated execution mode.
    private sealed class SimulatedAlpaca(AlpacaBrokerageGateway gateway) : IExecutionGateway,
        IExecutionGatewayModeProvider, IExplicitOrderModificationGateway, IExplicitOrderCancellationGateway
    {
        public string GatewayId => gateway.GatewayId;
        public bool IsConnected => gateway.IsConnected;
        public ExecutionMode ExecutionMode => ExecutionMode.Paper;
        public Task ConnectAsync(CancellationToken ct = default) => gateway.ConnectAsync(ct);
        public Task DisconnectAsync(CancellationToken ct = default) => gateway.DisconnectAsync(ct);
        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default) => gateway.SubmitOrderAsync(request, ct);
        public Task<ExecutionReport> CancelOrderAsync(string id, CancellationToken ct = default) => gateway.CancelOrderAsync(id, ct);
        public Task<ExecutionReport> CancelOrderAsync(OrderCancellationIdentifier id, CancellationToken ct = default) => gateway.CancelOrderAsync(id, ct);
        public Task<ExecutionReport> ModifyOrderAsync(string id, OrderModification change, CancellationToken ct = default) => gateway.ModifyOrderAsync(id, change, ct);
        public Task<ExecutionReport> ModifyOrderAsync(OrderCancellationIdentifier id, OrderModification change, CancellationToken ct = default) => gateway.ModifyOrderAsync(id, change, ct);
        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default) => gateway.StreamExecutionReportsAsync(ct);
    }
}
