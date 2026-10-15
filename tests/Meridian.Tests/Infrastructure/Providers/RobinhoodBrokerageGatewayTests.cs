using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Robinhood;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Unit tests for <see cref="RobinhoodBrokerageGateway"/>.
/// All tests use a stub HTTP handler — no real network calls are made.
/// </summary>
public sealed class RobinhoodBrokerageGatewayTests : IDisposable
{
    private readonly string? _originalAccessToken;

    public RobinhoodBrokerageGatewayTests()
    {
        _originalAccessToken = Environment.GetEnvironmentVariable("ROBINHOOD_ACCESS_TOKEN");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ROBINHOOD_ACCESS_TOKEN", _originalAccessToken);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static RobinhoodBrokerageGateway CreateSut(
        HttpMessageHandler handler,
        string? accessToken = "test-token")
    {
        return new RobinhoodBrokerageGateway(
            new StubHttpClientFactory(handler),
            NullLogger<RobinhoodBrokerageGateway>.Instance,
            accessToken: accessToken);
    }

    private static StringContent BuildAccountResponse() =>
        BuildJson(new
        {
            results = new[]
            {
                new
                {
                    url = "https://api.robinhood.com/accounts/TEST123/",
                    account_number = "TEST123",
                    equity = "10000.00",
                    cash = "5000.00",
                    buying_power = "9000.00",
                    deactivated = false
                }
            }
        });

    private static StringContent BuildOrderResponse(
        string id = "order-1", string state = "confirmed", string quantity = "1",
        string? filledQuantity = "0", string? averagePrice = null, string refId = "client-1") =>
        BuildJson(new
        {
            id,
            ref_id = refId,
            symbol = "AAPL",
            side = "buy",
            type = "market",
            time_in_force = "gfd",
            quantity,
            cumulative_quantity = filledQuantity,
            average_price = averagePrice,
            state,
            created_at = "2024-01-02T10:00:00Z"
        });

    private static StringContent BuildOptionOrderResponse(
        string id = "option-order-1", string state = "confirmed", string quantity = "1",
        string? filledQuantity = "0", string? averagePrice = null, bool useLegExecutionPrices = false) =>
        BuildJson(new
        {
            id,
            ref_id = "client-1",
            chain_symbol = "AAPL",
            direction = "credit",
            quantity,
            processed_quantity = filledQuantity,
            average_price = useLegExecutionPrices ? null : averagePrice,
            type = "market",
            time_in_force = "gfd",
            price = "1.25",
            legs = new[]
            {
                new
                {
                    option = "https://api.robinhood.com/options/instruments/opt-close/",
                    side = "sell",
                    ratio_quantity = 1,
                    position_effect = "close",
                    executions = useLegExecutionPrices ? new[] { new { quantity = filledQuantity, price = averagePrice } } : null
                }
            },
            state,
            created_at = "2024-01-02T10:00:00Z"
        });

    private static StringContent BuildOptionInstrumentResponse(string chainSymbol = "AAPL") =>
        BuildJson(new
        {
            url = "https://api.robinhood.com/options/instruments/opt-close/",
            chain_symbol = chainSymbol,
            expiration_date = "2024-01-19",
            strike_price = "100.00",
            type = "call"
        });

    private static StringContent BuildInstrumentListResponse() =>
        BuildJson(new
        {
            results = new[]
            {
                new { url = "https://api.robinhood.com/instruments/AAPL/", symbol = "AAPL" }
            }
        });

    private static StringContent BuildPositionsResponse() =>
        BuildJson(new
        {
            results = new[]
            {
                new
                {
                    instrument = "https://api.robinhood.com/instruments/AAPL/",
                    quantity = "10",
                    average_buy_price = "150.00"
                }
            }
        });

    private static StringContent BuildJson(object obj) =>
        new StringContent(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

    // ── Metadata ──────────────────────────────────────────────────────────

    [Fact]
    public void GatewayId_ReturnsRobinhood()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")));
        sut.GatewayId.Should().Be("robinhood");
    }

    [Fact]
    public void BrokerDisplayName_ContainsRobinhood()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")));
        sut.BrokerDisplayName.Should().Contain("Robinhood");
    }

    [Fact]
    public void BrokerageCapabilities_SupportsFractional()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")));
        sut.BrokerageCapabilities.SupportsFractionalShares.Should().BeTrue();
    }

    [Fact]
    public void BrokerageCapabilities_DoesNotAdvertiseSessionScopedOrderTypes()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")));

        sut.BrokerageCapabilities.SupportedOrderTypes.Should().NotContain(OrderType.MarketOnOpen);
        sut.BrokerageCapabilities.SupportedOrderTypes.Should().NotContain(OrderType.MarketOnClose);
        sut.BrokerageCapabilities.SupportedOrderTypes.Should().NotContain(OrderType.LimitOnOpen);
        sut.BrokerageCapabilities.SupportedOrderTypes.Should().NotContain(OrderType.LimitOnClose);
    }

    [Fact]
    public void IsConnected_BeforeConnect_ReturnsFalse()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")));
        sut.IsConnected.Should().BeFalse();
    }

    // ── ConnectAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_NoToken_ThrowsInvalidOperationException()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, BuildAccountResponse()), accessToken: null);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var act = () => sut.ConnectAsync(cts.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ROBINHOOD_ACCESS_TOKEN*");
    }

    [Fact]
    public async Task ConnectAsync_ValidToken_SetsIsConnectedTrue()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, BuildAccountResponse()));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.ConnectAsync(cts.Token);

        sut.IsConnected.Should().BeTrue();
        await sut.DisposeAsync();
    }

    // ── SubmitOrderAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task SubmitOrderAsync_RejectsLimitOnOpenBeforePostingOrder()
    {
        var handler = new RecordingHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildAccountResponse() }));
        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        var act = () => sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.LimitOnOpen,
            Quantity = 1m,
            LimitPrice = 175m
        }, cts.Token);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*LimitOnOpen*session timing qualifier*");
        handler.Requests.Should().ContainSingle();
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_MarketBuy_ReturnsNewReport()
    {
        // Handler returns instrument list, account URL, and order response in sequence
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildAccountResponse() },     // ConnectAsync
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildInstrumentListResponse() }, // instrument lookup
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildAccountResponse() },     // account URL
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildOrderResponse() },       // order submit
        });
        var sut = CreateSut(new SequentialStubHandler(responses));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            TimeInForce = TimeInForce.Day,
        };

        var report = await sut.SubmitOrderAsync(request, cts.Token);

        report.Should().NotBeNull();
        report.Symbol.Should().Be("AAPL");
        report.Side.Should().Be(OrderSide.Buy);
        report.ReportType.Should().Be(ExecutionReportType.New);

        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_ServerError_ReturnsRejectedReport()
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildAccountResponse() },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildInstrumentListResponse() },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildAccountResponse() },
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("error") },
        });
        var sut = CreateSut(new SequentialStubHandler(responses));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            TimeInForce = TimeInForce.Day,
        };

        var report = await sut.SubmitOrderAsync(request, cts.Token);

        report.ReportType.Should().Be(ExecutionReportType.Rejected);
        report.OrderStatus.Should().Be(OrderStatus.Rejected);

        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_WithOptionMetadata_RoutesThroughOptionsOrdersEndpoint()
    {
        var handler = new RecordingHttpHandler((request, _) =>
        {
            var response = request.RequestUri?.AbsolutePath switch
            {
                "/accounts/" => new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildAccountResponse() },
                "/options/instruments/opt-close/" => new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildOptionInstrumentResponse("AAPL") },
                "/options/orders/" => new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildOptionOrderResponse() },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("unexpected") }
            };

            return Task.FromResult(response);
        });

        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Sell,
            Type = OrderType.Market,
            Quantity = 1m,
            TimeInForce = TimeInForce.Day,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["asset_class"] = "option",
                ["option_instrument_url"] = "https://api.robinhood.com/options/instruments/opt-close/",
                ["position_effect"] = "close"
            }
        };

        var report = await sut.SubmitOrderAsync(request, cts.Token);

        report.ReportType.Should().Be(ExecutionReportType.New);
        report.OrderStatus.Should().Be(OrderStatus.Accepted);

        var submit = handler.Requests.Should().ContainSingle(
            r => r.Method == HttpMethod.Post && r.Url.EndsWith("/options/orders/", StringComparison.Ordinal))
            .Subject;
        submit.Body.Should().Contain("\"option\":\"https://api.robinhood.com/options/instruments/opt-close/\"");
        handler.Requests.Should().NotContain(r =>
            r.Url.StartsWith("https://api.robinhood.com/instruments/", StringComparison.Ordinal));
        handler.Requests.Should().NotContain(
            r => r.Method == HttpMethod.Post && string.Equals(r.Url, "https://api.robinhood.com/orders/", StringComparison.Ordinal));

        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_WithOptionMetadataSymbolMismatch_ReturnsRejectedReport()
    {
        var handler = new RecordingHttpHandler((request, _) =>
        {
            var response = request.RequestUri?.AbsolutePath switch
            {
                "/accounts/" => new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildAccountResponse() },
                "/options/instruments/opt-close/" => new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildOptionInstrumentResponse("TSLA") },
                "/options/orders/" => new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildOptionOrderResponse() },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("unexpected") }
            };

            return Task.FromResult(response);
        });

        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Sell,
            Type = OrderType.Market,
            Quantity = 1m,
            TimeInForce = TimeInForce.Day,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["asset_class"] = "option",
                ["option_instrument_url"] = "https://api.robinhood.com/options/instruments/opt-close/",
                ["position_effect"] = "close"
            }
        };

        var report = await sut.SubmitOrderAsync(request, cts.Token);

        report.ReportType.Should().Be(ExecutionReportType.Rejected);
        report.OrderStatus.Should().Be(OrderStatus.Rejected);
        report.RejectReason.Should().Contain("does not match order symbol");
        handler.Requests.Should().NotContain(
            r => r.Method == HttpMethod.Post && r.Url.EndsWith("/options/orders/", StringComparison.Ordinal));

        await sut.DisposeAsync();
    }

    [Fact]
    public async Task ModifyOrderAsync_WhenCancelFails_DoesNotResubmitReplacementOrder()
    {
        var handler = new RecordingHttpHandler((request, _) =>
        {
            var response = request.RequestUri?.AbsolutePath switch
            {
                "/accounts/" => new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildAccountResponse() },
                "/orders/order-1/" => new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildOrderResponse(id: "order-1") },
                "/orders/order-1/cancel/" => new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("too late") },
                "/orders/" => new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildOrderResponse(id: "replacement-order") },
                "/options/orders/" => new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildOptionOrderResponse(id: "replacement-option") },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("unexpected") }
            };

            return Task.FromResult(response);
        });

        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        var report = await sut.ModifyOrderAsync(
            "order-1",
            new OrderModification { NewQuantity = 2m },
            cts.Token);

        report.OrderStatus.Should().Be(OrderStatus.Accepted);
        report.ReportType.Should().Be(ExecutionReportType.ModificationRejected);
        report.RejectReason.Should().Contain("Cancel request failed");
        handler.Requests.Should().ContainSingle(
            r => r.Method == HttpMethod.Post && r.Url.EndsWith("/orders/order-1/cancel/", StringComparison.Ordinal));
        handler.Requests.Should().NotContain(
            r => r.Method == HttpMethod.Post && r.Url.EndsWith("/orders/", StringComparison.Ordinal));
        handler.Requests.Should().NotContain(
            r => r.Method == HttpMethod.Post && r.Url.EndsWith("/options/orders/", StringComparison.Ordinal));

        await sut.DisposeAsync();
    }

    [Fact]
    public async Task ModifyOrderAsync_CancelAcceptedButStillWorking_DoesNotSubmitReplacement()
    {
        var handler = CreateModificationHandler((_, _) => BuildOrderResponse(quantity: "10", filledQuantity: "2", averagePrice: "150"));
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var report = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });

        report.ReportType.Should().Be(ExecutionReportType.ModificationRejected);
        report.OrderStatus.Should().Be(OrderStatus.PendingCancel);
        report.FilledQuantity.Should().Be(2m);
        report.RejectReason.Should().Contain("not been confirmed");
        handler.Requests.Should().NotContain(r => IsReplacementPost(r));
    }

    [Fact]
    public async Task CancelOrderAsync_HttpAcceptance_DoesNotClaimTerminalCancellation()
    {
        var handler = CreateModificationHandler((_, _) => BuildOrderResponse());
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var report = await sut.CancelOrderAsync("order-1");

        report.OrderStatus.Should().Be(OrderStatus.PendingCancel);
        report.ReportType.Should().Be(ExecutionReportType.CancelRejected);
    }

    [Fact]
    public async Task ModifyOrderAsync_OrderFillsDuringCancellation_DoesNotSubmitReplacement()
    {
        var handler = CreateModificationHandler((_, read) => read < 3
            ? BuildOrderResponse(quantity: "10", filledQuantity: "2", averagePrice: "150")
            : BuildOrderResponse(state: "filled", quantity: "10", filledQuantity: "10", averagePrice: "151"));
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var report = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });

        report.ReportType.Should().Be(ExecutionReportType.ModificationRejected);
        report.OrderStatus.Should().Be(OrderStatus.Filled);
        report.FilledQuantity.Should().Be(10m);
        report.FillPrice.Should().Be(151m);
        handler.Requests.Should().NotContain(r => IsReplacementPost(r));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbled")]
    public async Task ModifyOrderAsync_CancelledWithoutConfirmedFillQuantity_DoesNotSubmitReplacement(string? filled)
    {
        var handler = CreateModificationHandler((_, read) => BuildOrderResponse(
            state: read < 3 ? "confirmed" : "cancelled", quantity: "10", filledQuantity: filled));
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var report = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });

        report.ReportType.Should().Be(ExecutionReportType.ModificationRejected);
        report.OrderStatus.Should().Be(OrderStatus.Cancelled);
        report.RejectReason.Should().Contain("confirmed cumulative fill quantity");
        handler.Requests.Should().NotContain(r => IsReplacementPost(r));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task ModifyOrderAsync_ConfirmedFillsMeetRequestedTotal_DoesNotSubmitReplacement(int filled)
    {
        var handler = CreateModificationHandler((_, read) => BuildOrderResponse(
            state: read < 3 ? "confirmed" : "cancelled", quantity: "10", filledQuantity: filled.ToString()));
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var report = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 4m });

        report.ReportType.Should().Be(ExecutionReportType.ModificationRejected);
        report.FilledQuantity.Should().Be(filled);
        handler.Requests.Should().NotContain(r => IsReplacementPost(r));
    }

    [Fact]
    public async Task ModifyOrderAsync_ConfirmedCancellation_SubmitsOnlyRemainingAndRetainsChainAcrossAmendments()
    {
        var handler = CreateModificationHandler((id, read) => id == "order-1"
            ? BuildOrderResponse(state: read < 3 ? "confirmed" : "cancelled", quantity: "10",
                filledQuantity: read < 3 ? "2" : "3", averagePrice: "150")
            : BuildOrderResponse(id, state: read < 2 ? "confirmed" : "cancelled", quantity: "9",
                filledQuantity: "2", averagePrice: "151", refId: "replacement-ref"));
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var first = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m, NewLimitPrice = 152m });
        var second = await sut.ModifyOrderAsync("client-1", new OrderModification { NewLimitPrice = 153m });

        first.ReportType.Should().Be(ExecutionReportType.Modified);
        first.ClientOrderId.Should().Be("client-1");
        first.GatewayOrderId.Should().Be("replacement-1");
        first.OrderQuantity.Should().Be(12m);
        first.FilledQuantity.Should().Be(3m);
        second.ClientOrderId.Should().Be("client-1");
        second.GatewayOrderId.Should().Be("replacement-2");
        second.OrderQuantity.Should().Be(12m);
        second.FilledQuantity.Should().Be(5m);
        first.FillPrice.Should().Be(150m);
        second.FillPrice.Should().Be(150.4m);
        first.FillPriceIsCumulativeAverage.Should().BeTrue();
        second.FillPriceIsCumulativeAverage.Should().BeTrue();
        second.ReplacesOrderId.Should().Be("replacement-1");
        var posts = handler.Requests.Where(IsReplacementPost).ToArray();
        posts.Should().HaveCount(2);
        using var firstPayload = JsonDocument.Parse(posts[0].Body!);
        using var secondPayload = JsonDocument.Parse(posts[1].Body!);
        firstPayload.RootElement.GetProperty("quantity").GetString().Should().Be("9");
        secondPayload.RootElement.GetProperty("quantity").GetString().Should().Be("7");
        firstPayload.RootElement.GetProperty("ref_id").GetString().Should().NotBe("client-1");
        secondPayload.RootElement.GetProperty("ref_id").GetString().Should()
            .NotBe(firstPayload.RootElement.GetProperty("ref_id").GetString());
        handler.Requests.Should().Contain(r => r.Url.EndsWith("/orders/replacement-1/cancel/"));

        await sut.DisposeAsync();
        var reports = new List<ExecutionReport>();
        await foreach (var report in sut.StreamExecutionReportsAsync())
            reports.Add(report);
        reports.Should().HaveCount(2);
        reports.Should().OnlyContain(report => report.ReportType == ExecutionReportType.Modified);
    }

    [Fact]
    public async Task ModifyOrderAsync_ReplacementRejected_KeepsConfirmedOriginalCancellationAndFills()
    {
        var handler = CreateModificationHandler((_, read) => BuildOrderResponse(
            state: read < 3 ? "confirmed" : "cancelled", quantity: "10", filledQuantity: "3", averagePrice: "150"),
            rejectReplacement: true);
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var report = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });

        report.ReportType.Should().Be(ExecutionReportType.ModificationRejected);
        report.OrderStatus.Should().Be(OrderStatus.Cancelled);
        report.ClientOrderId.Should().Be("client-1");
        report.GatewayOrderId.Should().Be("order-1");
        report.FilledQuantity.Should().Be(3m);
        report.RejectReason.Should().Contain("replacement submission failed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModifyOrderAsync_OptionCancellation_UsesProcessedContractsAndPreservesLeg(bool useLegExecutionPrices)
    {
        var reads = 0;
        var handler = new RecordingHttpHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var content = path switch
            {
                "/accounts/" => BuildAccountResponse(),
                "/options/orders/option-order-1/" => BuildOptionOrderResponse(
                    state: ++reads < 3 ? "confirmed" : "cancelled", quantity: "10", filledQuantity: "3", averagePrice: "1.25",
                    useLegExecutionPrices: useLegExecutionPrices),
                "/options/orders/option-order-1/cancel/" => BuildJson(new { }),
                "/options/instruments/opt-close/" => BuildOptionInstrumentResponse(),
                "/options/orders/" => BuildOptionOrderResponse("replacement-option"),
                _ => null
            };
            return Task.FromResult(new HttpResponseMessage(content is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            { Content = content ?? new StringContent("missing") });
        });
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var report = await sut.ModifyOrderAsync("option-order-1", new OrderModification { NewQuantity = 12m });

        report.ReportType.Should().Be(ExecutionReportType.Modified);
        report.ClientOrderId.Should().Be("client-1");
        report.FilledQuantity.Should().Be(3m);
        report.FillPrice.Should().Be(1.25m);
        var post = handler.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post && r.Url.EndsWith("/options/orders/")).Subject;
        using var payload = JsonDocument.Parse(post.Body!);
        payload.RootElement.GetProperty("quantity").GetString().Should().Be("9");
        payload.RootElement.GetProperty("legs")[0].GetProperty("position_effect").GetString().Should().Be("close");
    }

    [Fact]
    public async Task ModifyOrderAsync_ReplacementPostTimesOut_BlocksRetryUntilReconciliation()
    {
        var handler = CreateModificationHandler((_, read) => BuildOrderResponse(
            state: read < 3 ? "confirmed" : "cancelled", quantity: "10", filledQuantity: "2", averagePrice: "150"),
            timeoutReplacement: true);
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var first = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });
        var retry = await sut.ModifyOrderAsync("client-1", new OrderModification { NewQuantity = 12m });
        var cancel = await sut.CancelOrderAsync("order-1");

        first.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        first.OrderStatus.Should().Be(OrderStatus.PendingNew);
        first.RejectReason.Should().Contain("unconfirmed outcome");
        retry.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        retry.RejectReason.Should().Be(first.RejectReason);
        cancel.ReportType.Should().Be(ExecutionReportType.CancelRejected);
        cancel.OrderStatus.Should().Be(OrderStatus.PendingNew);
        handler.Requests.Should().ContainSingle(r => IsReplacementPost(r));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ModifyOrderAsync_ReplacementResponseLacksBrokerIdentity_RemainsPending(string? brokerId)
    {
        var handler = CreateModificationHandler((_, read) => BuildOrderResponse(
            state: read < 3 ? "confirmed" : "cancelled", quantity: "10", filledQuantity: "2", averagePrice: "150"),
            replacementResponse: () => BuildOrderResponse(brokerId!));
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var pending = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });
        var retry = await sut.ModifyOrderAsync("client-1", new OrderModification { NewQuantity = 12m });

        pending.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        pending.GatewayOrderId.Should().Be("order-1");
        retry.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        retry.RejectReason.Should().Be(pending.RejectReason);
        handler.Requests.Should().ContainSingle(r => IsReplacementPost(r));
    }

    [Fact]
    public async Task ModifyOrderAsync_CallerCancelsReplacementPost_ReturnsPendingAndBlocksRetry()
    {
        using var cts = new CancellationTokenSource();
        var handler = CreateModificationHandler((_, read) => BuildOrderResponse(
            state: read < 3 ? "confirmed" : "cancelled", quantity: "10", filledQuantity: "2", averagePrice: "150"),
            timeoutReplacement: true, replacementStarted: () => cts.Cancel());
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var pending = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m }, cts.Token);
        var retry = await sut.ModifyOrderAsync("client-1", new OrderModification { NewQuantity = 12m });

        pending.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        retry.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        retry.RejectReason.Should().Be(pending.RejectReason);
        handler.Requests.Should().ContainSingle(r => IsReplacementPost(r));
    }

    [Fact]
    public async Task ModifyOrderAsync_StaleReadAfterPendingCancel_PreservesConfirmedFillWatermark()
    {
        var handler = CreateModificationHandler((_, read) => BuildOrderResponse(
            state: read <= 7 ? "confirmed" : "cancelled", quantity: "10",
            filledQuantity: read <= 7 ? "2" : "1", averagePrice: "150"));
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var pending = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 5m });
        var confirmed = await sut.ModifyOrderAsync("client-1", new OrderModification { NewQuantity = 5m });

        pending.OrderStatus.Should().Be(OrderStatus.PendingCancel);
        confirmed.ReportType.Should().Be(ExecutionReportType.Modified);
        confirmed.FilledQuantity.Should().Be(2m);
        var post = handler.Requests.Should().ContainSingle(r => IsReplacementPost(r)).Subject;
        using var payload = JsonDocument.Parse(post.Body!);
        payload.RootElement.GetProperty("quantity").GetString().Should().Be("3");
    }

    [Fact]
    public async Task ModifyOrderAsync_ShutdownDuringConfirmation_DoesNotSubmitReplacement()
    {
        using var cts = new CancellationTokenSource();
        var handler = CreateModificationHandler((_, read) =>
        {
            if (read == 3)
                cts.Cancel();
            return BuildOrderResponse(quantity: "10");
        });
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var act = () => sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().NotContain(r => IsReplacementPost(r));
    }

    private static bool IsReplacementPost(RecordedRequest request)
        => request.Method == HttpMethod.Post && request.Url.EndsWith("/orders/", StringComparison.Ordinal);

    private static RecordingHttpHandler CreateModificationHandler(
        Func<string, int, HttpContent> orderResponse, bool rejectReplacement = false,
        bool timeoutReplacement = false, Action? replacementStarted = null,
        Func<HttpContent>? replacementResponse = null)
    {
        var reads = new Dictionary<string, int>();
        var replacements = 0;
        return new RecordingHttpHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response;
            if (path == "/accounts/")
                response = new(HttpStatusCode.OK) { Content = BuildAccountResponse() };
            else if (path == "/instruments/")
                response = new(HttpStatusCode.OK) { Content = BuildInstrumentListResponse() };
            else if (path.EndsWith("/cancel/", StringComparison.Ordinal))
                response = new(HttpStatusCode.NoContent);
            else if (path == "/orders/" && request.Method == HttpMethod.Post)
            {
                replacementStarted?.Invoke();
                if (timeoutReplacement)
                    throw new TaskCanceledException("HTTP request timed out after submission");
                response = rejectReplacement
                    ? new(HttpStatusCode.Conflict) { Content = new StringContent("replacement refused") }
                    : new(HttpStatusCode.OK) { Content = replacementResponse?.Invoke() ?? BuildOrderResponse($"replacement-{++replacements}") };
            }
            else if (path.StartsWith("/orders/", StringComparison.Ordinal))
            {
                var id = path.Split('/')[2];
                reads.TryGetValue(id, out var count);
                reads[id] = ++count;
                response = new(HttpStatusCode.OK) { Content = orderResponse(id, count) };
            }
            else
                response = new(HttpStatusCode.NotFound) { Content = new StringContent("unexpected") };
            return Task.FromResult(response);
        });
    }

    // ── GetAccountInfoAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetAccountInfoAsync_ValidResponse_ReturnsAccountInfo()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, BuildAccountResponse()));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var info = await sut.GetAccountInfoAsync(cts.Token);

        info.AccountId.Should().Be("TEST123");
        info.Equity.Should().Be(10000m);
        info.Cash.Should().Be(5000m);
    }

    [Fact]
    public async Task GetPositionsAsync_UntrustedInstrumentUrl_DoesNotFollowExternalHost()
    {
        var handler = new RecordingHttpHandler((request, _) =>
        {
            var response = request.RequestUri?.AbsolutePath switch
            {
                "/positions/" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = BuildJson(new
                    {
                        results = new[]
                        {
                            new
                            {
                                instrument = "https://attacker.example/instruments/AAPL/",
                                quantity = "10",
                                average_buy_price = "150.00"
                            }
                        }
                    })
                },
                "/options/positions/" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = BuildJson(new { results = Array.Empty<object>() })
                },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("unexpected") }
            };

            return Task.FromResult(response);
        });

        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var positions = await sut.GetPositionsAsync(cts.Token);

        positions.Should().ContainSingle();
        positions[0].Symbol.Should().Be("https://attacker.example/instruments/AAPL/");
        handler.Requests.Should().NotContain(r => r.Url.Contains("attacker.example", StringComparison.OrdinalIgnoreCase));
    }

    // ── CheckHealthAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CheckHealthAsync_NotConnected_ReturnsUnhealthy()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, BuildAccountResponse()));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var health = await sut.CheckHealthAsync(cts.Token);

        health.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public async Task CheckHealthAsync_Connected_ReturnsHealthy()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, BuildAccountResponse()));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        var health = await sut.CheckHealthAsync(cts.Token);

        health.IsHealthy.Should().BeTrue();
        await sut.DisposeAsync();
    }

    // ── DisconnectAsync / DisposeAsync ────────────────────────────────────

    [Fact]
    public async Task DisconnectAsync_AfterConnect_SetsIsConnectedFalse()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, BuildAccountResponse()));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        await sut.DisconnectAsync(cts.Token);

        sut.IsConnected.Should().BeFalse();
        await sut.DisposeAsync();
    }

    // ── Stub infrastructure ──────────────────────────────────────────────

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly Func<HttpContent> _contentFactory;

        public StubHttpHandler(HttpStatusCode statusCode, HttpContent singleContent)
            : this(statusCode, () =>
            {
                // Re-serialize so each request gets a fresh, readable stream.
                var raw = singleContent.ReadAsStringAsync().GetAwaiter().GetResult();
                return new StringContent(raw, System.Text.Encoding.UTF8, "application/json");
            })
        {
        }

        public StubHttpHandler(HttpStatusCode statusCode, Func<HttpContent> contentFactory)
        {
            _statusCode = statusCode;
            _contentFactory = contentFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(_statusCode) { Content = _contentFactory() });
        }
    }

    private sealed class SequentialStubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public SequentialStubHandler(Queue<HttpResponseMessage> responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return _responses.Count > 0
                ? Task.FromResult(_responses.Dequeue())
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Url, string? Body);

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public RecordingHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.AbsoluteUri ?? string.Empty,
                body));

            return await _responder(request, ct).ConfigureAwait(false);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new HttpClient(_handler, disposeHandler: false) { BaseAddress = new Uri("https://api.robinhood.com/") };
    }
}
