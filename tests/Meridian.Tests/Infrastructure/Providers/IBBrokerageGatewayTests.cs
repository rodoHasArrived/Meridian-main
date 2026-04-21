using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class IBBrokerageGatewayTests
{
    private static IBOptions DefaultOptions(bool paper = true)
        => new(Host: "127.0.0.1", Port: paper ? 7497 : 7496, ClientId: 1, UsePaperTrading: paper);

    private static IBBrokerageGateway CreateSut(FakeIbBrokerageClient client, bool paper = true)
        => new(DefaultOptions(paper), NullLogger<IBBrokerageGateway>.Instance, client);

    [Fact]
    public void Gateway_Metadata_DeclaresIbAndFixedIncomeSupport()
    {
        var sut = CreateSut(new FakeIbBrokerageClient());

        sut.GatewayId.Should().Be("ib");
        sut.BrokerDisplayName.Should().Contain("Interactive Brokers");
        sut.BrokerageCapabilities.SupportedAssetClasses.Should().Contain(["equity", "bond"]);
        sut.BrokerageCapabilities.Extensions["supportsFixedIncome"].Should().Be("true");
    }

    [Fact]
    public async Task ConnectAsync_WithNativeClient_PrimesNextValidId()
    {
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(1100)
        };

        await using var sut = CreateSut(client);

        await sut.ConnectAsync();

        sut.IsConnected.Should().BeTrue();
        client.IsConnected.Should().BeTrue();
    }

#if !IBAPI
    [Fact]
    public async Task ConnectAsync_WithoutVendorRuntime_ThrowsGuidanceException()
    {
        await using var sut = new IBBrokerageGateway(DefaultOptions(), NullLogger<IBBrokerageGateway>.Instance);

        var act = () => sut.ConnectAsync();

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*EnableIbApiVendor=true*")
            .WithMessage("*interactive-brokers-setup.md*");
    }
#endif

    [Fact]
    public async Task SubmitOrderAsync_MapsOpenOrderCallbackIntoAcceptedReport()
    {
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(2001),
            OnPlaceOrder = (c, orderId, request) =>
            {
                c.RaiseOpenOrder(new IBOpenOrderUpdate(
                    orderId,
                    request.Symbol,
                    request.Metadata?.GetValueOrDefault("sec_type") ?? "STK",
                    request.Side == OrderSide.Buy ? "BUY" : "SELL",
                    request.Type == OrderType.Limit ? "LMT" : "MKT",
                    request.Quantity,
                    0m,
                    request.LimitPrice is decimal limit ? (double)limit : null,
                    request.StopPrice is decimal stop ? (double)stop : null,
                    "Submitted",
                    request.ClientOrderId,
                    "DU123456",
                    null,
                    null,
                    request.Metadata,
                    DateTimeOffset.UtcNow));
            }
        };

        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        var report = await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "912828YY0",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10_000m,
            ClientOrderId = "treasury-1",
            Metadata = new Dictionary<string, string> { ["sec_type"] = "GOVT" }
        });

        report.OrderId.Should().Be("treasury-1");
        report.GatewayOrderId.Should().Be("2001");
        report.ReportType.Should().Be(ExecutionReportType.New);
        report.OrderStatus.Should().Be(OrderStatus.Accepted);
        report.Symbol.Should().Be("912828YY0");
    }

    [Fact]
    public async Task ModifyOrderAsync_ReusesGatewayOrderIdAndReturnsModifiedReport()
    {
        var placeCalls = 0;
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(3001),
            OnPlaceOrder = (c, orderId, request) =>
            {
                placeCalls++;
                c.RaiseOpenOrder(new IBOpenOrderUpdate(
                    orderId,
                    request.Symbol,
                    "STK",
                    request.Side == OrderSide.Buy ? "BUY" : "SELL",
                    "LMT",
                    request.Quantity,
                    0m,
                    request.LimitPrice is decimal limit ? (double)limit : null,
                    null,
                    "Submitted",
                    request.ClientOrderId,
                    "DU123456",
                    null,
                    null,
                    request.Metadata,
                    DateTimeOffset.UtcNow));
            }
        };

        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 25m,
            LimitPrice = 185.25m,
            ClientOrderId = "eq-1",
        });

        var report = await sut.ModifyOrderAsync("eq-1", new OrderModification
        {
            NewQuantity = 30m,
            NewLimitPrice = 184.75m
        });

        placeCalls.Should().Be(2);
        report.ReportType.Should().Be(ExecutionReportType.Modified);
        report.GatewayOrderId.Should().Be("3001");
        report.OrderQuantity.Should().Be(30m);
    }

    [Fact]
    public async Task CancelOrderAsync_MapsOrderStatusCancelledCallback()
    {
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(4001),
            OnPlaceOrder = (c, orderId, request) =>
            {
                c.RaiseOpenOrder(new IBOpenOrderUpdate(
                    orderId, request.Symbol, "STK", "BUY", "MKT", request.Quantity, 0m,
                    null, null, "Submitted", request.ClientOrderId, "DU123456", null, null, request.Metadata, DateTimeOffset.UtcNow));
            },
            OnCancelOrder = (c, orderId) =>
            {
                c.RaiseOrderStatus(new IBOrderStatusUpdate(
                    orderId, "Cancelled", 0m, 1m, 0d, 0d, 0L, 1, null, DateTimeOffset.UtcNow));
            }
        };

        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            ClientOrderId = "cancel-me"
        });

        var report = await sut.CancelOrderAsync("cancel-me");

        report.ReportType.Should().Be(ExecutionReportType.Cancelled);
        report.OrderStatus.Should().Be(OrderStatus.Cancelled);
        report.OrderId.Should().Be("cancel-me");
    }

    [Fact]
    public async Task StreamExecutionReportsAsync_EmitsFillFromExecDetails()
    {
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(5001),
            OnPlaceOrder = (c, orderId, request) =>
            {
                c.RaiseOpenOrder(new IBOpenOrderUpdate(
                    orderId, request.Symbol, "STK", "BUY", "MKT", request.Quantity, 0m,
                    null, null, "Submitted", request.ClientOrderId, "DU123456", null, null, request.Metadata, DateTimeOffset.UtcNow));
            }
        };

        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            ClientOrderId = "fill-me"
        }, cts.Token);

        client.RaiseExecution(new IBExecutionUpdate(
            5001, "SPY", "BOT", 10m, 511.25d, 10m, 511.25d, "0001", "DU123456", "SMART", 42L, DateTimeOffset.UtcNow));

        var report = await ReadUntilAsync(sut.StreamExecutionReportsAsync(cts.Token), r => r.ReportType == ExecutionReportType.Fill, cts.Token);

        report.OrderId.Should().Be("fill-me");
        report.OrderStatus.Should().Be(OrderStatus.Filled);
        report.FillPrice.Should().Be(511.25m);
        report.FilledQuantity.Should().Be(10m);
    }

    [Fact]
    public async Task GetPositionsAsync_MapsPositionCallbacks()
    {
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(6001),
            OnRequestPositions = c =>
            {
                c.RaisePosition(new IBPositionUpdate(
                    "DU123456", "IEF", "BOND", 5m, 99.875d, "USD", "SMART",
                    new Dictionary<string, string> { ["accrued_interest"] = "12.50" },
                    DateTimeOffset.UtcNow));
                c.RaisePositionsCompleted();
            }
        };

        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        var positions = await sut.GetPositionsAsync();

        positions.Should().ContainSingle();
        positions[0].Symbol.Should().Be("IEF");
        positions[0].AssetClass.Should().Be("bond");
        positions[0].AccruedInterest.Should().Be(12.50m);
    }

    [Fact]
    public async Task GetOpenOrdersAsync_MapsOpenOrderSnapshotCallbacks()
    {
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(7001),
            OnRequestOpenOrders = c =>
            {
                c.RaiseOpenOrder(new IBOpenOrderUpdate(
                    7001, "AAPL", "STK", "SELL", "LMT", 12m, 2m,
                    205.50d, null, "Submitted", "open-1", "DU123456", null, null, null, DateTimeOffset.UtcNow));
                c.RaiseOpenOrdersCompleted();
            }
        };

        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        var openOrders = await sut.GetOpenOrdersAsync();

        openOrders.Should().ContainSingle();
        openOrders[0].OrderId.Should().Be("7001");
        openOrders[0].ClientOrderId.Should().Be("open-1");
        openOrders[0].Side.Should().Be(OrderSide.Sell);
        openOrders[0].LimitPrice.Should().Be(205.50m);
    }

    [Fact]
    public async Task GetAccountInfoAsync_MapsAccountSummaryCallbacks()
    {
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(8001),
            OnRequestAccountSummary = (c, requestId) =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(25);
                    c.RaiseAccountSummary(new IBAccountSummaryUpdate(requestId, "DU123456", "NetLiquidation", "125000.50", "USD", DateTimeOffset.UtcNow));
                    c.RaiseAccountSummary(new IBAccountSummaryUpdate(requestId, "DU123456", "TotalCashValue", "25000.25", "USD", DateTimeOffset.UtcNow));
                    c.RaiseAccountSummary(new IBAccountSummaryUpdate(requestId, "DU123456", "BuyingPower", "50000.75", "USD", DateTimeOffset.UtcNow));
                    c.RaiseAccountSummaryCompleted(requestId);
                });
            }
        };

        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        var account = await sut.GetAccountInfoAsync();

        account.AccountId.Should().Be("DU123456");
        account.Equity.Should().Be(125000.50m);
        account.Cash.Should().Be(25000.25m);
        account.BuyingPower.Should().Be(50000.75m);
        account.Status.Should().Be("paper");
    }

    private static async Task<ExecutionReport> ReadUntilAsync(
        IAsyncEnumerable<ExecutionReport> stream,
        Func<ExecutionReport, bool> predicate,
        CancellationToken ct)
    {
        await foreach (var report in stream.WithCancellation(ct))
        {
            if (predicate(report))
                return report;
        }

        throw new InvalidOperationException("Expected execution report was not observed.");
    }

    private sealed class FakeIbBrokerageClient : IIBBrokerageClient
    {
        private int _nextRequestId = 100;

        public string Host { get; init; } = "127.0.0.1";
        public int Port { get; init; } = 7497;
        public int ClientId { get; init; } = 1;
        public bool IsConnected { get; private set; }

        public Action<FakeIbBrokerageClient>? OnRequestNextValidId { get; set; }
        public Action<FakeIbBrokerageClient, int, OrderRequest>? OnPlaceOrder { get; set; }
        public Action<FakeIbBrokerageClient, int>? OnCancelOrder { get; set; }
        public Action<FakeIbBrokerageClient, int>? OnRequestAccountSummary { get; set; }
        public Action<FakeIbBrokerageClient>? OnRequestPositions { get; set; }
        public Action<FakeIbBrokerageClient>? OnRequestOpenOrders { get; set; }

        public event EventHandler<int>? NextValidIdReceived;
        public event EventHandler<IBOrderStatusUpdate>? OrderStatusReceived;
        public event EventHandler<IBOpenOrderUpdate>? OpenOrderReceived;
        public event EventHandler? OpenOrdersCompleted;
        public event EventHandler<IBExecutionUpdate>? ExecutionDetailsReceived;
        public event EventHandler<IBPositionUpdate>? PositionReceived;
        public event EventHandler? PositionsCompleted;
        public event EventHandler<IBAccountSummaryUpdate>? AccountSummaryReceived;
        public event EventHandler<int>? AccountSummaryCompleted;
        public event EventHandler<IBApiError>? ErrorOccurred;

        public Task ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken ct = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public void RequestNextValidId() => OnRequestNextValidId?.Invoke(this);

        public Task PlaceOrderAsync(int orderId, OrderRequest request, CancellationToken ct = default)
        {
            OnPlaceOrder?.Invoke(this, orderId, request);
            return Task.CompletedTask;
        }

        public Task CancelOrderAsync(int orderId, CancellationToken ct = default)
        {
            OnCancelOrder?.Invoke(this, orderId);
            return Task.CompletedTask;
        }

        public int RequestAccountSummary()
        {
            var requestId = Interlocked.Increment(ref _nextRequestId);
            OnRequestAccountSummary?.Invoke(this, requestId);
            return requestId;
        }

        public void CancelAccountSummary(int requestId)
        {
        }

        public void RequestPositions() => OnRequestPositions?.Invoke(this);

        public void CancelPositions()
        {
        }

        public void RequestOpenOrders() => OnRequestOpenOrders?.Invoke(this);

        public void RaiseNextValidId(int orderId) => NextValidIdReceived?.Invoke(this, orderId);
        public void RaiseOrderStatus(IBOrderStatusUpdate update) => OrderStatusReceived?.Invoke(this, update);
        public void RaiseOpenOrder(IBOpenOrderUpdate update) => OpenOrderReceived?.Invoke(this, update);
        public void RaiseExecution(IBExecutionUpdate update) => ExecutionDetailsReceived?.Invoke(this, update);
        public void RaisePosition(IBPositionUpdate update) => PositionReceived?.Invoke(this, update);
        public void RaisePositionsCompleted() => PositionsCompleted?.Invoke(this, EventArgs.Empty);
        public void RaiseOpenOrdersCompleted() => OpenOrdersCompleted?.Invoke(this, EventArgs.Empty);
        public void RaiseAccountSummary(IBAccountSummaryUpdate update) => AccountSummaryReceived?.Invoke(this, update);
        public void RaiseAccountSummaryCompleted(int requestId) => AccountSummaryCompleted?.Invoke(this, requestId);
        public void RaiseError(IBApiError error) => ErrorOccurred?.Invoke(this, error);

        public void Dispose()
        {
        }
    }
}
