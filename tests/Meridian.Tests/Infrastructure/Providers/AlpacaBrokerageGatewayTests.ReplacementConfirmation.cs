using System.Net;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Infrastructure.Adapters.Alpaca;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class AlpacaBrokerageGatewayTests
{
    [Theory]
    [InlineData("pending_new")]
    [InlineData("new")]
    [InlineData("accepted")]
    public async Task OmsAmendment_ProvisionalHttpSuccess_PredecessorFillAndCandidateRefusalRetainFilledOriginal(string candidateStatus)
    {
        var originalFilled = false;
        var patches = 0;
        using var handler = new Meridian.Tests.TestHelpers.StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v2/account")
                return new(HttpStatusCode.OK) { Content = BuildAccountResponse() };
            if (request.Method == HttpMethod.Patch)
            {
                patches++;
                return new(HttpStatusCode.OK)
                {
                    Content = BuildOrderResponse("candidate", candidateStatus,
                    clientOrderId: "candidate-client", qty: "20", replaces: "original", limitPrice: "149")
                };
            }
            if (request.RequestUri.AbsolutePath == "/v2/orders/candidate")
                return new(HttpStatusCode.OK)
                {
                    Content = BuildOrderResponse("candidate", candidateStatus,
                    clientOrderId: "candidate-client", qty: "20", replaces: "original", limitPrice: "149")
                };
            return new(HttpStatusCode.OK)
            {
                Content = BuildOrderResponse("original", originalFilled ? "filled" : "accepted",
                clientOrderId: "local", qty: "10", filledQty: originalFilled ? "10" : "0",
                filledAvgPrice: originalFilled ? "100" : null, limitPrice: "150")
            };
        });
        await using var gateway = CreateSut(handler);
        await gateway.ConnectAsync();
        await using var oms = new OrderManagementSystem(new SimulatedAlpaca(gateway), NullLogger<OrderManagementSystem>.Instance,
            portfolioState: new PaperTradingPortfolio(100_000m));
        await oms.StartAsync();
        (await oms.PlaceOrderAsync(new OrderRequest
        {
            ClientOrderId = "local",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        })).Success.Should().BeTrue();

        var amendment = await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 20m, NewLimitPrice = 149m });

        amendment.Success.Should().BeFalse();
        oms.GetOrder("local")!.Quantity.Should().Be(10m);
        oms.GetOrder("local")!.LimitPrice.Should().Be(150m);
        oms.GetOrder("local")!.Status.Should().Be(OrderStatus.Accepted);
        (await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 30m })).Success.Should().BeFalse();
        patches.Should().Be(1);
        originalFilled = true;
        var channel = GetAlpacaReportChannel(gateway);
        await channel.Writer.WriteAsync(new ExecutionReport
        {
            OrderId = "original",
            GatewayOrderId = "original",
            ClientOrderId = "local",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Filled,
            ReportType = ExecutionReportType.Fill,
            OrderQuantity = 10m,
            FilledQuantity = 10m,
            FillPrice = 100m,
            Timestamp = DateTimeOffset.Parse("2026-10-15T12:00:00Z")
        });
        await channel.Writer.WriteAsync(new ExecutionReport
        {
            OrderId = "candidate",
            GatewayOrderId = "candidate",
            ClientOrderId = "candidate-client",
            ReplacesOrderId = "original",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Rejected,
            ReportType = ExecutionReportType.Rejected,
            OrderQuantity = 20m,
            RejectReason = "Predecessor filled before replacement reached venue.",
            Timestamp = DateTimeOffset.Parse("2026-10-15T12:00:01Z")
        });
        // A subsequent genuine fill proves the report pump consumed the refusal first.
        await channel.Writer.WriteAsync(new ExecutionReport
        {
            OrderId = "sentinel",
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Filled,
            ReportType = ExecutionReportType.Fill,
            OrderQuantity = 1m,
            FilledQuantity = 1m,
            LastFillQuantity = 1m,
            FillPrice = 10m,
            AssetClass = "us_equity"
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        (await oms.ExecutionReports.ReadAsync(timeout.Token)).OrderId.Should().Be("original");
        (await oms.ExecutionReports.ReadAsync(timeout.Token)).OrderId.Should().Be("sentinel");
        oms.GetOrder("local")!.Status.Should().Be(OrderStatus.Filled);
        oms.GetOrder("local")!.Quantity.Should().Be(10m);
        oms.GetOrder("local")!.FilledQuantity.Should().Be(10m);
    }

    [Fact]
    public async Task OmsAmendment_AuthoritativeReplacementEvent_CommitsSuccessorIdentityAndTermsBeforeCancellation()
    {
        var confirmed = false;
        var cancelled = false;
        var deletions = new List<string>();
        using var handler = new Meridian.Tests.TestHelpers.StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v2/account")
                return new(HttpStatusCode.OK) { Content = BuildAccountResponse() };
            if (request.Method == HttpMethod.Patch)
                return new(HttpStatusCode.OK)
                {
                    Content = BuildOrderResponse("candidate", "pending_new",
                    clientOrderId: "candidate-client", qty: "20", replaces: "original", limitPrice: "149")
                };
            if (request.Method == HttpMethod.Delete)
            {
                deletions.Add(path);
                cancelled = true;
                return new(HttpStatusCode.NoContent);
            }
            if (path == "/v2/orders/candidate")
                return new(HttpStatusCode.OK)
                {
                    Content = BuildOrderResponse("candidate", cancelled ? "canceled" : confirmed ? "accepted" : "pending_new",
                    clientOrderId: "candidate-client", qty: "20", replaces: "original", limitPrice: "149")
                };
            return new(HttpStatusCode.OK)
            {
                Content = BuildOrderResponse("original", confirmed ? "replaced" : "accepted",
                clientOrderId: "local", qty: "10", replacedBy: confirmed ? "candidate" : null, limitPrice: "150")
            };
        });
        await using var gateway = CreateSut(handler);
        await gateway.ConnectAsync();
        await using var oms = new OrderManagementSystem(new SimulatedAlpaca(gateway), NullLogger<OrderManagementSystem>.Instance);
        await oms.StartAsync();
        (await oms.PlaceOrderAsync(new OrderRequest
        {
            ClientOrderId = "local",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        })).Success.Should().BeTrue();
        (await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 20m, NewLimitPrice = 149m })).Success.Should().BeFalse();
        confirmed = true;
        await GetAlpacaReportChannel(gateway).Writer.WriteAsync(new ExecutionReport
        {
            OrderId = "original",
            GatewayOrderId = "original",
            ClientOrderId = "local",
            CandidateGatewayOrderId = "candidate",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Accepted,
            ReportType = ExecutionReportType.New,
            OrderQuantity = 10m,
            Diagnostics = new ExecutionDiagnostics { BrokerStatus = "replaced" }
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (oms.GetOrder("local")!.Quantity != 20m)
            await Task.Delay(10, timeout.Token);
        oms.GetOrder("local")!.LimitPrice.Should().Be(149m);

        var cancellation = await oms.CancelOrderAsync("local");

        cancellation.Success.Should().BeTrue();
        deletions.Should().Equal("/v2/orders/candidate");
    }

    [Fact]
    public async Task Cancellation_FilledUnconfirmedCandidate_PublishesCandidateFillsBeforePredecessorCancel()
    {
        var executed = false;
        using var handler = new Meridian.Tests.TestHelpers.StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v2/account")
                return new(HttpStatusCode.OK) { Content = BuildAccountResponse() };
            if (request.Method == HttpMethod.Patch)
                return new(HttpStatusCode.OK)
                {
                    Content = BuildOrderResponse("candidate", "pending_new",
                    clientOrderId: "candidate-client", qty: "20", replaces: "original", limitPrice: "149")
                };
            if (request.Method == HttpMethod.Delete)
                return new((HttpStatusCode)422);
            if (path == "/v2/orders/candidate")
                return new(HttpStatusCode.OK)
                {
                    Content = BuildOrderResponse("candidate", executed ? "filled" : "pending_new",
                    clientOrderId: "candidate-client", qty: "20", filledQty: executed ? "20" : "0",
                    filledAvgPrice: executed ? "149" : null, replaces: "original", limitPrice: "149")
                };
            return new(HttpStatusCode.OK)
            {
                Content = BuildOrderResponse("original", executed ? "replaced" : "accepted",
                clientOrderId: "local", qty: "10", replacedBy: executed ? "candidate" : null, limitPrice: "150")
            };
        });
        await using var gateway = CreateSut(handler);
        await gateway.ConnectAsync();
        await using var oms = new OrderManagementSystem(new SimulatedAlpaca(gateway), NullLogger<OrderManagementSystem>.Instance);
        await oms.StartAsync();
        (await oms.PlaceOrderAsync(new OrderRequest
        {
            ClientOrderId = "local",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        })).Success.Should().BeTrue();
        (await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 20m, NewLimitPrice = 149m })).Success.Should().BeFalse();
        // The candidate executes while its trade-update stream is delayed.
        executed = true;

        await oms.CancelOrderAsync("local");

        // The fills arrive through the asynchronous report pump; allow for a loaded test host.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (oms.GetOrder("local")!.FilledQuantity != 20m)
            await Task.Delay(10, timeout.Token);
        oms.GetOrder("local")!.Quantity.Should().Be(20m);
        oms.GetOrder("local")!.AverageFillPrice.Should().Be(149m);
    }

    [Theory]
    [InlineData("accepted", "accepted", ExecutionReportType.ModificationPending, "original")]
    [InlineData("replaced", "pending_new", ExecutionReportType.ModificationPending, "original")]
    [InlineData("replaced", "accepted", ExecutionReportType.Modified, "candidate")]
    [InlineData("filled", "rejected", ExecutionReportType.ModificationRejected, "original")]
    public async Task GetAmendmentForRecoveryAsync_BeforeStream_RequiresDefinitiveReplacementEvidence(
        string predecessorStatus, string candidateStatus, ExecutionReportType expectedType, string expectedBroker)
    {
        using var handler = new Meridian.Tests.TestHelpers.StubHttpMessageHandler(request => new(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath == "/v2/orders/original"
                ? BuildOrderResponse("original", predecessorStatus, clientOrderId: "local", qty: "10",
                    filledQty: predecessorStatus == "filled" ? "10" : "0", filledAvgPrice: predecessorStatus == "filled" ? "100" : null,
                    replacedBy: predecessorStatus == "replaced" ? "candidate" : null, limitPrice: "150")
                : BuildOrderResponse("candidate", candidateStatus, clientOrderId: "candidate-client", qty: "20",
                    replaces: "original", limitPrice: "149")
        });
        await using var gateway = CreateSut(handler);
        MarkConnected(gateway);
        var original = new OrderState
        {
            OrderId = "local",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m,
            Status = OrderStatus.Accepted
        };
        var pending = new PendingOrderAmendment(original, original with { Quantity = 20m, LimitPrice = 149m }, "original")
        { CandidateBrokerOrderId = "candidate" };

        var result = await gateway.GetAmendmentForRecoveryAsync("local", original, pending);

        result!.ReportType.Should().Be(expectedType);
        result.GatewayOrderId.Should().Be(expectedBroker);
        result.HasOrderTerms.Should().BeTrue();
        result.OrderLimitPrice.Should().Be(expectedType == ExecutionReportType.Modified ? 149m : 150m);
        if (predecessorStatus == "filled")
        {
            result.OrderStatus.Should().Be(OrderStatus.Filled);
            result.FilledQuantity.Should().Be(10m);
        }
    }

    [Fact]
    public async Task OmsAmendment_KnownCandidateFillBeforePredecessorLink_BooksOnceWithoutConfirmingTermsOrClipping()
    {
        var patches = 0;
        using var handler = new Meridian.Tests.TestHelpers.StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v2/account")
                return new(HttpStatusCode.OK) { Content = BuildAccountResponse() };
            if (request.Method == HttpMethod.Patch)
                patches++;
            return new(HttpStatusCode.OK)
            {
                Content = request.Method == HttpMethod.Patch || path == "/v2/orders/candidate"
                    ? BuildOrderResponse("candidate", "pending_new", clientOrderId: "candidate-client", qty: "20",
                        replaces: "original", limitPrice: "149")
                    : BuildOrderResponse("original", "accepted", clientOrderId: "local", qty: "10", limitPrice: "150")
            };
        });
        await using var gateway = CreateSut(handler);
        await gateway.ConnectAsync();
        var portfolio = new PaperTradingPortfolio(100_000m);
        var publisher = new AmendmentPublisher();
        await using var oms = new OrderManagementSystem(new SimulatedAlpaca(gateway), NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio, tradeEventPublisher: publisher);
        await oms.StartAsync();
        (await oms.PlaceOrderAsync(new OrderRequest
        {
            ClientOrderId = "local",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        })).Success.Should().BeTrue();
        (await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 20m, NewLimitPrice = 149m })).Success.Should().BeFalse();
        var fill = new ExecutionReport
        {
            OrderId = "candidate",
            GatewayOrderId = "candidate",
            ClientOrderId = "candidate-client",
            ReplacesOrderId = "original",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.PartiallyFilled,
            ReportType = ExecutionReportType.PartialFill,
            OrderQuantity = 20m,
            FilledQuantity = 15m,
            LastFillQuantity = 15m,
            FillPrice = 100m,
            Timestamp = DateTimeOffset.Parse("2026-10-15T12:00:00Z")
        };
        var channel = GetAlpacaReportChannel(gateway);
        await channel.Writer.WriteAsync(fill);
        await channel.Writer.WriteAsync(fill);
        await channel.Writer.WriteAsync(new ExecutionReport
        {
            OrderId = "sentinel",
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Filled,
            ReportType = ExecutionReportType.Fill,
            OrderQuantity = 1m,
            FilledQuantity = 1m,
            LastFillQuantity = 1m,
            FillPrice = 10m,
            AssetClass = "us_equity"
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        (await oms.ExecutionReports.ReadAsync(timeout.Token)).ClientOrderId.Should().Be("local");
        (await oms.ExecutionReports.ReadAsync(timeout.Token)).OrderId.Should().Be("sentinel");

        publisher.Events.Where(trade => trade.Symbol == "AAPL").Should().ContainSingle().Which.FilledQuantity.Should().Be(15m);
        portfolio.Positions["AAPL"].Quantity.Should().Be(15L);
        oms.GetOrder("local")!.FilledQuantity.Should().Be(15m);
        oms.GetOrder("local")!.Quantity.Should().Be(15m, "only observed executions can expand confirmed quantity before replacement proof");
        oms.GetOrder("local")!.LimitPrice.Should().Be(150m);
        oms.GetExposureReservingOrders().Single(order => order.OrderId == "local").Quantity.Should().Be(20m);
        (await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 30m })).Success.Should().BeFalse();
        patches.Should().Be(1);
    }

    private static Channel<ExecutionReport> GetAlpacaReportChannel(AlpacaBrokerageGateway gateway) =>
        (Channel<ExecutionReport>)typeof(AlpacaBrokerageGateway)
            .GetField("_reportChannel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(gateway)!;
}
