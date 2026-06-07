using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Unit tests for <see cref="AlpacaBrokerageGateway"/>, including fixed income (bond/treasury) support.
/// All tests use a stub HTTP handler — no real network calls are made.
/// </summary>
public sealed class AlpacaBrokerageGatewayTests
{
    private const string PaperBaseUrl = "https://paper-api.alpaca.markets";

    // ── Helpers ──────────────────────────────────────────────────────────

    private static AlpacaBrokerageGateway CreateSut(HttpMessageHandler handler, bool useSandbox = true)
    {
        var options = new AlpacaOptions(KeyId: "test-key", SecretKey: "test-secret", UseSandbox: useSandbox);
        return new AlpacaBrokerageGateway(
            new StubHttpClientFactory(handler),
            options,
            NullLogger<AlpacaBrokerageGateway>.Instance);
    }

    private static StringContent BuildAccountResponse(string status = "active") =>
        BuildJson(new
        {
            account_number = "TEST123",
            equity = "100000.00",
            cash = "50000.00",
            buying_power = "90000.00",
            currency = "USD",
            status
        });

    private static StringContent BuildOrderResponse(
        string id = "order-001",
        string status = "accepted",
        string symbol = "AAPL") =>
        BuildJson(new
        {
            id,
            client_order_id = "client-1",
            symbol,
            side = "buy",
            type = "market",
            qty = "1",
            filled_qty = "0",
            status,
            created_at = "2024-01-15T10:00:00Z"
        });

    private static StringContent BuildPositionsResponse(object[] positions) =>
        BuildJson(positions);

    private static StringContent BuildJson(object obj) =>
        new StringContent(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

    // ── Capabilities ─────────────────────────────────────────────────────

    [Fact]
    public void GatewayId_ReturnsAlpaca()
    {
        var sut = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, new StringContent("{}")));
        sut.GatewayId.Should().Be("alpaca");
    }

    [Fact]
    public void BrokerDisplayName_ContainsAlpaca()
    {
        var sut = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, new StringContent("{}")));
        sut.BrokerDisplayName.Should().Contain("Alpaca");
    }

    [Fact]
    public void BrokerageCapabilities_DeclaresEquityAndFixedIncome()
    {
        var sut = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, new StringContent("{}")));

        sut.BrokerageCapabilities.SupportedAssetClasses.Should().Contain("equity");
        sut.BrokerageCapabilities.SupportedAssetClasses.Should().Contain("us_treasury");
        sut.BrokerageCapabilities.SupportedAssetClasses.Should().Contain("bond");
    }

    [Fact]
    public void BrokerageCapabilities_SupportsNotionalOrders()
    {
        var sut = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, new StringContent("{}")));

        sut.BrokerageCapabilities.Extensions.Should().ContainKey("supportsNotionalOrders");
        sut.BrokerageCapabilities.Extensions["supportsNotionalOrders"].Should().Be("true");
    }

    [Fact]
    public void BrokerageCapabilities_SupportsFractionalSharesAndModification()
    {
        var sut = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, new StringContent("{}")));

        sut.BrokerageCapabilities.SupportsFractionalShares.Should().BeTrue();
        sut.BrokerageCapabilities.SupportsOrderModification.Should().BeTrue();
    }

    [Fact]
    public void BrokerageCapabilities_AdvertisesDocumentedAdvancedOrderTypes()
    {
        var sut = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, new StringContent("{}")));

        sut.BrokerageCapabilities.SupportedOrderTypes.Should().Contain(OrderType.MarketOnOpen);
        sut.BrokerageCapabilities.SupportedOrderTypes.Should().Contain(OrderType.MarketOnClose);
        sut.BrokerageCapabilities.SupportedOrderTypes.Should().Contain(OrderType.LimitOnOpen);
        sut.BrokerageCapabilities.SupportedOrderTypes.Should().Contain(OrderType.LimitOnClose);
        sut.BrokerageCapabilities.SupportedOrderTypes.Should().Contain(OrderType.TrailingStop);
        sut.BrokerageCapabilities.Extensions.Should().ContainKey("supportsAdvancedOrderClasses");
    }

    // ── ConnectAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_ValidCredentials_SetsIsConnectedTrue()
    {
        var sut = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, BuildAccountResponse()));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.ConnectAsync(cts.Token);

        sut.IsConnected.Should().BeTrue();
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_MissingCredentials_ThrowsInvalidOperationException()
    {
        using var env = AlpacaEnvScope.Clear();
        var options = new AlpacaOptions(KeyId: "", SecretKey: "");
        var sut = new AlpacaBrokerageGateway(
            new StubHttpClientFactory(new ConstantStubHandler(HttpStatusCode.OK, BuildAccountResponse())),
            options,
            NullLogger<AlpacaBrokerageGateway>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var act = () => sut.ConnectAsync(cts.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*KeyId*SecretKey*");
    }

    [Fact]
    public async Task ConnectAsync_DefaultOptions_UsesPaperAccountEndpointAndAlpacaHeaders()
    {
        using var env = AlpacaEnvScope.Clear();
        HttpRequestMessage? capturedRequest = null;
        var sut = new AlpacaBrokerageGateway(
            new StubHttpClientFactory(new CapturingStubHandler(
                request => capturedRequest = request,
                _ => BuildAccountResponse())),
            new AlpacaOptions(KeyId: "paper-key", SecretKey: "paper-secret"),
            NullLogger<AlpacaBrokerageGateway>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.ConnectAsync(cts.Token);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Be($"{PaperBaseUrl}/v2/account");
        capturedRequest.Headers.GetValues("APCA-API-KEY-ID").Should().ContainSingle().Which.Should().Be("paper-key");
        capturedRequest.Headers.GetValues("APCA-API-SECRET-KEY").Should().ContainSingle().Which.Should().Be("paper-secret");
    }

    [Fact]
    public async Task ConnectAsync_LiveEnvironmentVariable_UsesLiveAccountEndpoint()
    {
        using var env = AlpacaEnvScope.Clear();
        Environment.SetEnvironmentVariable(AlpacaCredentialEnvironment.TradingEnvironmentName, "live");
        HttpRequestMessage? capturedRequest = null;
        var sut = new AlpacaBrokerageGateway(
            new StubHttpClientFactory(new CapturingStubHandler(
                request => capturedRequest = request,
                _ => BuildAccountResponse())),
            new AlpacaOptions(KeyId: "live-key", SecretKey: "live-secret"),
            NullLogger<AlpacaBrokerageGateway>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.ConnectAsync(cts.Token);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Be("https://api.alpaca.markets/v2/account");
    }

    // ── SubmitOrderAsync: equity orders ───────────────────────────────────

    [Fact]
    public async Task SubmitOrderAsync_EquityMarketBuy_ReturnsNewReport()
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildAccountResponse() },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildOrderResponse("ord-1", "accepted", "AAPL") },
        });
        var sut = CreateSut(new SequentialStubHandler(responses));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        var report = await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
        }, cts.Token);

        report.ReportType.Should().Be(ExecutionReportType.New);
        report.Symbol.Should().Be("AAPL");
        report.Side.Should().Be(OrderSide.Buy);
        report.OrderStatus.Should().Be(OrderStatus.Accepted);
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_MarketOnClose_UsesClosingAuctionTimeInForce()
    {
        string? capturedBody = null;
        var sut = CreateSut(new CapturingStubHandler(
            req =>
            {
                if (req.Content != null && req.RequestUri?.AbsolutePath == "/v2/orders")
                    capturedBody = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            },
            req => req.RequestUri?.AbsolutePath == "/v2/account"
                ? BuildAccountResponse()
                : BuildOrderResponse("ord-moc-1", "accepted", "AAPL")));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.MarketOnClose,
            Quantity = 10m,
        }, cts.Token);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("type").GetString().Should().Be("market");
        doc.RootElement.GetProperty("time_in_force").GetString().Should().Be("cls");
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_ServerError_ReturnsRejectedReport()
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildAccountResponse() },
            new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad request") },
        });
        var sut = CreateSut(new SequentialStubHandler(responses));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        var report = await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
        }, cts.Token);

        report.ReportType.Should().Be(ExecutionReportType.Rejected);
        report.OrderStatus.Should().Be(OrderStatus.Rejected);
        await sut.DisposeAsync();
    }

    // ── SubmitOrderAsync: fixed income notional orders ────────────────────

    [Fact]
    public async Task SubmitOrderAsync_FixedIncomeMetadata_UsesFaceValueQtyAndAssetClass()
    {
        string? capturedBody = null;

        var sut = CreateSut(new CapturingStubHandler(
            req =>
            {
                if (req.Content != null)
                    capturedBody = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            },
            req => req.RequestUri?.AbsolutePath == "/v2/account"
                ? BuildAccountResponse()
                : BuildOrderResponse("ord-treasury-1", "accepted", "912828YY0")));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        var report = await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "912828YY0",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 5000m,           // dollar notional
            Metadata = new Dictionary<string, string>
            {
                ["notional"] = "true",
                ["asset_class"] = "treasury",
                ["broker_account_id"] = "broker-account-1",
            },
        }, cts.Token);

        report.ReportType.Should().Be(ExecutionReportType.New);
        capturedBody.Should().Contain("\"qty\"");
        capturedBody.Should().NotContain("\"notional\"");
        capturedBody.Should().NotContain("\"asset_class\"");
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_NotionalOrderMetadata_UsesNotionalInsteadOfQty()
    {
        string? capturedBody = null;

        var sut = CreateSut(new CapturingStubHandler(
            req =>
            {
                if (req.Content != null && req.RequestUri?.AbsolutePath == "/v2/orders")
                    capturedBody = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            },
            req => req.RequestUri?.AbsolutePath == "/v2/account"
                ? BuildAccountResponse()
                : BuildOrderResponse("ord-2", "accepted", "CUSIP123")));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "CUSIP123",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 2500m,
            Metadata = new Dictionary<string, string> { ["notional"] = "true" },
        }, cts.Token);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("qty", out _).Should().BeFalse();
        doc.RootElement.GetProperty("notional").GetString().Should().Be("2500");
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_NonNotionalOrder_UsesQtyField()
    {
        string? capturedBody = null;

        var sut = CreateSut(new CapturingStubHandler(
            req =>
            {
                if (req.Content != null && req.RequestUri?.AbsolutePath == "/v2/orders")
                    capturedBody = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            },
            req => req.RequestUri?.AbsolutePath == "/v2/account"
                ? BuildAccountResponse()
                : BuildOrderResponse("ord-3", "accepted", "AAPL")));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
        }, cts.Token);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("qty", out var qtyProp).Should().BeTrue();
        qtyProp.GetString().Should().Be("10");
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_ExtendedHoursLimitOrder_SetsExtendedHours()
    {
        string? capturedBody = null;

        var sut = CreateSut(new CapturingStubHandler(
            req =>
            {
                if (req.Content != null && req.RequestUri?.AbsolutePath == "/v2/orders")
                    capturedBody = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            },
            req => req.RequestUri?.AbsolutePath == "/v2/account"
                ? BuildAccountResponse()
                : BuildOrderResponse("ord-ext-1", "accepted", "AAPL")));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 180.25m,
            Metadata = new Dictionary<string, string> { ["extended_hours"] = "true" },
        }, cts.Token);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("extended_hours").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("type").GetString().Should().Be("limit");
        doc.RootElement.GetProperty("time_in_force").GetString().Should().Be("day");
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_BracketMetadata_SerializesTakeProfitAndStopLoss()
    {
        string? capturedBody = null;

        var sut = CreateSut(new CapturingStubHandler(
            req =>
            {
                if (req.Content != null && req.RequestUri?.AbsolutePath == "/v2/orders")
                    capturedBody = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            },
            req => req.RequestUri?.AbsolutePath == "/v2/account"
                ? BuildAccountResponse()
                : BuildOrderResponse("ord-bracket-1", "accepted", "SPY")));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 100m,
            TimeInForce = TimeInForce.GoodTilCancelled,
            Metadata = new Dictionary<string, string>
            {
                ["order_class"] = "bracket",
                ["take_profit.limit_price"] = "301",
                ["stop_loss.stop_price"] = "299",
                ["stop_loss.limit_price"] = "298.5",
            },
        }, cts.Token);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("order_class").GetString().Should().Be("bracket");
        doc.RootElement.GetProperty("take_profit").GetProperty("limit_price").GetString().Should().Be("301");
        doc.RootElement.GetProperty("stop_loss").GetProperty("stop_price").GetString().Should().Be("299");
        doc.RootElement.GetProperty("stop_loss").GetProperty("limit_price").GetString().Should().Be("298.5");
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_TrailingStop_SerializesTrailPrice()
    {
        string? capturedBody = null;

        var sut = CreateSut(new CapturingStubHandler(
            req =>
            {
                if (req.Content != null && req.RequestUri?.AbsolutePath == "/v2/orders")
                    capturedBody = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            },
            req => req.RequestUri?.AbsolutePath == "/v2/account"
                ? BuildAccountResponse()
                : BuildOrderResponse("ord-trail-1", "accepted", "SPY")));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Sell,
            Type = OrderType.TrailingStop,
            Quantity = 100m,
            TrailPrice = 6.15m,
        }, cts.Token);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("type").GetString().Should().Be("trailing_stop");
        doc.RootElement.GetProperty("trail_price").GetString().Should().Be("6.15");
        doc.RootElement.TryGetProperty("trail_percent", out _).Should().BeFalse();
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_FixedIncomeWithStopOrder_ThrowsBeforePostingOrder()
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildAccountResponse() },
        });
        var sut = CreateSut(new SequentialStubHandler(responses));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        var act = () => sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "912797MU8",
            Side = OrderSide.Buy,
            Type = OrderType.StopMarket,
            Quantity = 1000m,
            StopPrice = 99m,
            Metadata = new Dictionary<string, string> { ["asset_class"] = "treasury" },
        }, cts.Token);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*fixed-income*market and limit*");
        responses.Should().BeEmpty();
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_OptionOrder_SerializesAssetClassAndPositionIntent()
    {
        string? capturedBody = null;

        var sut = CreateSut(new CapturingStubHandler(
            req =>
            {
                if (req.Content != null && req.RequestUri?.AbsolutePath == "/v2/orders")
                    capturedBody = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            },
            req => req.RequestUri?.AbsolutePath == "/v2/account"
                ? BuildAccountResponse()
                : BuildOrderResponse("ord-opt-1", "accepted", "AAPL260116C00200000")));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "AAPL260116C00200000",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            LimitPrice = 2.15m,
            PositionIntent = PositionIntent.BuyToOpen,
            Metadata = new Dictionary<string, string> { ["asset_class"] = "us_option" },
        }, cts.Token);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("asset_class").GetString().Should().Be("us_option");
        doc.RootElement.GetProperty("position_intent").GetString().Should().Be("buy_to_open");
        doc.RootElement.GetProperty("qty").GetString().Should().Be("1");
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_MultiLegOptionOrder_SerializesMlegPayload()
    {
        string? capturedBody = null;

        var sut = CreateSut(new CapturingStubHandler(
            req =>
            {
                if (req.Content != null && req.RequestUri?.AbsolutePath == "/v2/orders")
                    capturedBody = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            },
            req => req.RequestUri?.AbsolutePath == "/v2/account"
                ? BuildAccountResponse()
                : BuildOrderResponse("ord-mleg-1", "accepted", "AAPL")));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            LimitPrice = 1.00m,
            Legs =
            [
                new OrderLeg
                {
                    Symbol = "AAPL260116C00190000",
                    RatioQuantity = 1m,
                    Side = OrderSide.Buy,
                    PositionIntent = PositionIntent.BuyToOpen,
                },
                new OrderLeg
                {
                    Symbol = "AAPL260116C00210000",
                    RatioQuantity = 1m,
                    Side = OrderSide.Sell,
                    PositionIntent = PositionIntent.SellToOpen,
                },
            ],
        }, cts.Token);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("order_class").GetString().Should().Be("mleg");
        doc.RootElement.TryGetProperty("symbol", out _).Should().BeFalse();
        var legs = doc.RootElement.GetProperty("legs").EnumerateArray().ToArray();
        legs.Should().HaveCount(2);
        legs[0].GetProperty("ratio_qty").GetString().Should().Be("1");
        legs[0].GetProperty("position_intent").GetString().Should().Be("buy_to_open");
        legs[1].GetProperty("side").GetString().Should().Be("sell");
        await sut.DisposeAsync();
    }

    // ── GetPositionsAsync: fixed income ───────────────────────────────────

    [Fact]
    public async Task GetPositionsAsync_WithTreasuryPosition_MapsAccruedInterest()
    {
        var positionJson = BuildPositionsResponse(new object[]
        {
            new
            {
                symbol = "912828YY0",
                qty = "10",
                avg_entry_price = "98.50",
                current_price = "99.25",
                market_value = "992.50",
                unrealized_pl = "7.50",
                asset_class = "us_treasury",
                accrued_interest = "1.25",
            }
        });
        var sut = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, positionJson));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var positions = await sut.GetPositionsAsync(cts.Token);

        positions.Should().HaveCount(1);
        var pos = positions[0];
        pos.Symbol.Should().Be("912828YY0");
        pos.AssetClass.Should().Be("us_treasury");
        pos.AccruedInterest.Should().Be(1.25m);
        pos.MarketValue.Should().Be(992.50m);
    }

    [Fact]
    public async Task GetPositionsAsync_WithEquityPosition_HasNullAccruedInterest()
    {
        var positionJson = BuildPositionsResponse(new object[]
        {
            new
            {
                symbol = "AAPL",
                qty = "100",
                avg_entry_price = "175.00",
                current_price = "180.00",
                market_value = "18000.00",
                unrealized_pl = "500.00",
                asset_class = "equity",
            }
        });
        var sut = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, positionJson));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var positions = await sut.GetPositionsAsync(cts.Token);

        positions.Should().HaveCount(1);
        positions[0].AccruedInterest.Should().BeNull();
        positions[0].AssetClass.Should().Be("equity");
    }

    [Fact]
    public async Task GetPositionsAsync_MixedPortfolio_ReturnsBothAssetClasses()
    {
        var positionJson = BuildPositionsResponse(new object[]
        {
            new
            {
                symbol = "AAPL",
                qty = "50",
                avg_entry_price = "175.00",
                current_price = "180.00",
                market_value = "9000.00",
                unrealized_pl = "250.00",
                asset_class = "equity",
            },
            new
            {
                symbol = "912796TY0",
                qty = "5",
                avg_entry_price = "99.80",
                current_price = "99.90",
                market_value = "499.50",
                unrealized_pl = "0.50",
                asset_class = "us_treasury",
                accrued_interest = "0.75",
            }
        });
        var sut = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, positionJson));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var positions = await sut.GetPositionsAsync(cts.Token);

        positions.Should().HaveCount(2);
        positions.Should().Contain(p => p.AssetClass == "equity" && p.AccruedInterest == null);
        positions.Should().Contain(p => p.AssetClass == "us_treasury" && p.AccruedInterest == 0.75m);
    }

    [Fact]
    public async Task Scenario_MultiAccountAllocation_ReadSideSyncMapsAccountPortfolioOrdersFillsAndCashActivity()
    {
        var capturedPaths = new List<string>();
        var handler = new CapturingStubHandler(
            request => capturedPaths.Add(request.RequestUri?.PathAndQuery ?? string.Empty),
            request =>
            {
                var uri = request.RequestUri!;
                if (uri.AbsolutePath == "/v2/account")
                {
                    return BuildAccountResponse();
                }

                if (uri.AbsolutePath == "/v2/positions")
                {
                    return BuildPositionsResponse(new object[]
                    {
                        new
                        {
                            symbol = "AAPL",
                            qty = "100",
                            avg_entry_price = "175.00",
                            current_price = "187.50",
                            market_value = "18750.00",
                            unrealized_pl = "1250.00",
                            asset_class = "equity",
                        }
                    });
                }

                if (uri.AbsolutePath == "/v2/orders")
                {
                    return BuildJson(new object[]
                    {
                        new
                        {
                            id = "ord-open-1",
                            client_order_id = "client-open-1",
                            symbol = "AAPL",
                            side = "buy",
                            type = "limit",
                            qty = "25",
                            filled_qty = "0",
                            limit_price = "185.00",
                            status = "accepted",
                            created_at = "2026-04-25T14:30:00Z"
                        }
                    });
                }

                if (uri.AbsolutePath == "/v2/account/activities")
                {
                    return BuildJson(new object[]
                    {
                        new
                        {
                            id = "fill-1",
                            activity_type = "FILL",
                            transaction_time = "2026-04-25T14:35:00Z",
                            symbol = "AAPL",
                            qty = "10",
                            price = "184.25",
                            side = "buy",
                            order_id = "ord-fill-1",
                            commission = "0",
                            exchange = "XNAS"
                        },
                        new
                        {
                            id = "cash-1",
                            activity_type = "DIV",
                            transaction_time = "2026-04-24T20:00:00Z",
                            symbol = "AAPL",
                            net_amount = "42.50",
                            currency = "USD",
                            description = "Dividend"
                        }
                    });
                }

                return BuildJson(new { });
            });
        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var accounts = await ((IBrokerageAccountCatalog)sut).GetAccountsAsync(cts.Token);
        var portfolio = await ((IBrokeragePortfolioSync)sut).GetPortfolioSnapshotAsync("TEST123", cts.Token);
        var activity = await ((IBrokerageActivitySync)sut).GetActivitySnapshotAsync(
            "TEST123",
            new DateTimeOffset(2026, 4, 24, 0, 0, 0, TimeSpan.Zero),
            cts.Token);

        accounts.Should().ContainSingle(account =>
            account.ProviderId == "alpaca" &&
            account.AccountId == "TEST123" &&
            account.Currency == "USD");
        portfolio.Balance.Equity.Should().Be(100000m);
        portfolio.Positions.Should().ContainSingle(position =>
            position.Symbol == "AAPL" &&
            position.Quantity == 100m &&
            position.MarketValue == 18750m);
        activity.Orders.Should().ContainSingle(order =>
            order.OrderId == "ord-open-1" &&
            order.Status == OrderStatus.Accepted);
        activity.Fills.Should().ContainSingle(fill =>
            fill.FillId == "fill-1" &&
            fill.OrderId == "ord-fill-1" &&
            fill.Price == 184.25m);
        activity.CashTransactions.Should().ContainSingle(cash =>
            cash.TransactionId == "cash-1" &&
            cash.TransactionType == "DIV" &&
            cash.Amount == 42.50m);
        capturedPaths.Should().Contain(path =>
            path.StartsWith("/v2/account/activities?direction=desc&page_size=100&after=", StringComparison.Ordinal));
    }


    [Fact]
    public async Task GetActivitySnapshotAsync_OptionsLifecycle_MapsPartialFillAndLegRejectStatuses()
    {
        var handler = new CapturingStubHandler(
            _ => { },
            request =>
            {
                var uri = request.RequestUri!;
                if (uri.AbsolutePath == "/v2/account")
                {
                    return BuildAccountResponse();
                }

                if (uri.AbsolutePath == "/v2/orders")
                {
                    return BuildJson(new object[]
                    {
                        new
                        {
                            id = "ord-mleg-partial",
                            client_order_id = "client-mleg-partial",
                            symbol = "AAPL",
                            side = "buy",
                            type = "limit",
                            qty = "2",
                            filled_qty = "1",
                            limit_price = "1.25",
                            status = "partially_filled",
                            created_at = "2026-04-25T14:30:00Z"
                        },
                        new
                        {
                            id = "ord-mleg-reject",
                            client_order_id = "client-mleg-reject",
                            symbol = "AAPL",
                            side = "buy",
                            type = "limit",
                            qty = "1",
                            filled_qty = "0",
                            limit_price = "0.95",
                            status = "rejected",
                            created_at = "2026-04-25T14:31:00Z"
                        }
                    });
                }

                if (uri.AbsolutePath == "/v2/account/activities")
                {
                    return BuildJson(new object[]
                    {
                        new
                        {
                            id = "fill-partial-leg-1",
                            activity_type = "FILL",
                            transaction_time = "2026-04-25T14:35:00Z",
                            symbol = "AAPL260116C00190000",
                            qty = "1",
                            price = "1.20",
                            side = "buy",
                            order_id = "ord-mleg-partial",
                            commission = "0",
                            exchange = "XNAS"
                        }
                    });
                }

                return BuildJson(new { });
            });

        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var activity = await ((IBrokerageActivitySync)sut).GetActivitySnapshotAsync(
            "TEST123",
            new DateTimeOffset(2026, 4, 25, 0, 0, 0, TimeSpan.Zero),
            cts.Token);

        activity.Orders.Should().Contain(order => order.OrderId == "ord-mleg-partial" && order.Status == OrderStatus.PartiallyFilled);
        activity.Orders.Should().Contain(order => order.OrderId == "ord-mleg-reject" && order.Status == OrderStatus.Rejected);
        activity.Fills.Should().ContainSingle(fill =>
            fill.FillId == "fill-partial-leg-1" &&
            fill.OrderId == "ord-mleg-partial" &&
            fill.Symbol == "AAPL260116C00190000" &&
            fill.Quantity == 1m &&
            fill.Price == 1.20m);
    }

    // ── Stub infrastructure ───────────────────────────────────────────────

    private sealed class ConstantStubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly Func<HttpContent> _contentFactory;

        public ConstantStubHandler(HttpStatusCode statusCode, HttpContent singleContent)
            : this(statusCode, () =>
            {
                var raw = singleContent.ReadAsStringAsync().GetAwaiter().GetResult();
                return new StringContent(raw, Encoding.UTF8, "application/json");
            })
        {
        }

        public ConstantStubHandler(HttpStatusCode statusCode, Func<HttpContent> contentFactory)
        {
            _statusCode = statusCode;
            _contentFactory = contentFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(_statusCode) { Content = _contentFactory() });
        }
    }

    private sealed class SequentialStubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public SequentialStubHandler(Queue<HttpResponseMessage> responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return _responses.Count > 0
                ? Task.FromResult(_responses.Dequeue())
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{}") });
        }
    }

    private sealed class CapturingStubHandler : HttpMessageHandler
    {
        private readonly Action<HttpRequestMessage> _capture;
        private readonly Func<HttpRequestMessage, StringContent> _contentSelector;

        public CapturingStubHandler(
            Action<HttpRequestMessage> capture,
            Func<HttpRequestMessage, StringContent> contentSelector)
        {
            _capture = capture;
            _contentSelector = contentSelector;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _capture(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = _contentSelector(request)
            });
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new HttpClient(_handler, disposeHandler: false);
    }

    private sealed class AlpacaEnvScope : IDisposable
    {
        private static readonly string[] Names =
        [
            AlpacaCredentialEnvironment.KeyIdName,
            AlpacaCredentialEnvironment.SecretKeyName,
            AlpacaCredentialEnvironment.TradingEnvironmentName,
            "APCA_API_KEY_ID",
            "APCA_API_SECRET_KEY",
            "ALPACA__KEYID",
            "ALPACA__SECRETKEY"
        ];

        private readonly Dictionary<string, (string? Process, string? User)> _values;

        private AlpacaEnvScope()
        {
            _values = Names.ToDictionary(
                name => name,
                name => (
                    Environment.GetEnvironmentVariable(name),
                    Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)));
        }

        public static AlpacaEnvScope Clear()
        {
            var scope = new AlpacaEnvScope();
            foreach (var name in Names)
            {
                Environment.SetEnvironmentVariable(name, null);
                Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
            }

            return scope;
        }

        public void Dispose()
        {
            foreach (var (name, value) in _values)
            {
                Environment.SetEnvironmentVariable(name, value.Process);
                Environment.SetEnvironmentVariable(name, value.User, EnvironmentVariableTarget.User);
            }
        }
    }
}
