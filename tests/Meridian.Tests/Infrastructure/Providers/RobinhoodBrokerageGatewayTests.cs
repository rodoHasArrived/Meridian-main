using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
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
        string? filledQuantity = "0", string? averagePrice = null, bool useLegExecutionPrices = false, string refId = "client-1") =>
        BuildJson(new
        {
            id,
            ref_id = refId,
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrderAsync_UncertainReplacementFoundByReference_CancelsActualBrokerOrder(bool callerCancelledPost)
    {
        using var cts = new CancellationTokenSource();
        var handler = CreateRecoveryHandler((reference, _, _) => OrderHistory(
            [BuildOrderResponse("recovered", quantity: "10", filledQuantity: "1", averagePrice: "160", refId: reference)]),
            replacementStarted: callerCancelledPost ? () => cts.Cancel() : null);
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();
        var pending = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m }, cts.Token);

        var cancelled = await sut.CancelOrderAsync("client-1");

        pending.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        cancelled.OrderStatus.Should().Be(OrderStatus.Cancelled);
        cancelled.GatewayOrderId.Should().Be("recovered");
        cancelled.ClientOrderId.Should().Be("client-1");
        cancelled.OrderQuantity.Should().Be(12m);
        cancelled.FilledQuantity.Should().Be(3m);
        cancelled.FillPrice.Should().Be((2m * 150m + 160m) / 3m);
        handler.Requests.Should().ContainSingle(r => IsReplacementPost(r));
        handler.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post && r.Url.EndsWith("/orders/recovered/cancel/"));
    }

    [Fact]
    public async Task ModifyOrderAsync_UncertainReplacementFound_UsesItsFillsAndIntendedTotalForNextReplacement()
    {
        var handler = CreateRecoveryHandler((reference, _, _) => OrderHistory(
            [BuildOrderResponse("recovered", quantity: "10", filledQuantity: "1", averagePrice: "160", refId: reference)]));
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();
        await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });

        var amended = await sut.ModifyOrderAsync("client-1", new OrderModification { NewQuantity = 13m });

        amended.ReportType.Should().Be(ExecutionReportType.Modified);
        amended.GatewayOrderId.Should().Be("second-replacement");
        amended.ReplacesOrderId.Should().Be("recovered");
        amended.ClientOrderId.Should().Be("client-1");
        amended.OrderQuantity.Should().Be(13m);
        amended.FilledQuantity.Should().Be(3m);
        var posts = handler.Requests.Where(IsReplacementPost).ToArray();
        posts.Should().HaveCount(2);
        using var payload = JsonDocument.Parse(posts[1].Body!);
        payload.RootElement.GetProperty("quantity").GetString().Should().Be("10");
    }

    [Fact]
    public async Task CancelOrderAsync_UncertainReplacementAppearsLater_RechecksReferenceAndBecomesOperable()
    {
        var handler = CreateRecoveryHandler((reference, _, lookup) => lookup == 1
            ? OrderHistory([])
            : OrderHistory([BuildOrderResponse("recovered", quantity: "10", filledQuantity: "1", averagePrice: "160", refId: reference)]));
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();
        await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });

        var stillUnknown = await sut.CancelOrderAsync("client-1");
        var confirmed = await sut.CancelOrderAsync("order-1");

        stillUnknown.ReportType.Should().Be(ExecutionReportType.CancelRejected);
        stillUnknown.OrderStatus.Should().Be(OrderStatus.PendingNew);
        confirmed.OrderStatus.Should().Be(OrderStatus.Cancelled);
        confirmed.GatewayOrderId.Should().Be("recovered");
        handler.Requests.Should().ContainSingle(r => IsReplacementPost(r));
    }

    [Theory]
    [InlineData("rejected", OrderStatus.Rejected)]
    [InlineData("filled", OrderStatus.Filled)]
    [InlineData("expired", OrderStatus.Expired)]
    public async Task CancelOrderAsync_UncertainReplacementHasDefinitiveTerminalState_ClearsUncertainty(string state, OrderStatus expected)
    {
        var fill = state == "filled" ? "10" : "1";
        var handler = CreateRecoveryHandler((reference, _, _) => OrderHistory(
            [BuildOrderResponse("recovered", state, "10", fill, "160", reference)]), recoveredState: state, recoveredFilled: fill);
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();
        await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });

        var resolved = await sut.CancelOrderAsync("client-1");
        var repeated = await sut.CancelOrderAsync("order-1");

        resolved.OrderStatus.Should().Be(expected);
        repeated.OrderStatus.Should().Be(expected);
        resolved.GatewayOrderId.Should().Be("recovered");
        resolved.FilledQuantity.Should().Be(2m + decimal.Parse(fill));
        handler.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Get && r.Url.Contains("?ref_id="));
        handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Post && r.Url.EndsWith("/orders/recovered/cancel/"));
        handler.Requests.Should().ContainSingle(r => IsReplacementPost(r));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrderAsync_ReferenceMatchOnLaterPage_RequiresCompleteUniqueHistory(bool duplicateBrokerId)
    {
        var handler = CreateRecoveryHandler((reference, uri, _) => uri.Query.Contains("cursor=second")
            ? OrderHistory([BuildOrderResponse("recovered", quantity: "10", filledQuantity: "1", averagePrice: "160", refId: reference)])
            : OrderHistory([BuildOrderResponse(duplicateBrokerId ? "recovered" : "unrelated", quantity: "10", filledQuantity: "1",
                averagePrice: "160", refId: duplicateBrokerId ? reference : "another-ref")], "https://api.robinhood.com/orders/?cursor=second"));
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();
        await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });

        var report = await sut.CancelOrderAsync("client-1");

        report.OrderStatus.Should().Be(OrderStatus.Cancelled);
        report.GatewayOrderId.Should().Be("recovered");
        handler.Requests.Should().Contain(r => r.Url.Contains("cursor=second"));
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("untrusted-next")]
    [InlineData("looping-next")]
    [InlineData("not-found")]
    [InlineData("empty")]
    [InlineData("wrong-instrument")]
    public async Task ModifyOrderAsync_ReferenceHistoryDoesNotProveOneMatchingOrder_KeepsPendingWithoutDuplicateSubmission(string scenario)
    {
        var handler = CreateRecoveryHandler((reference, uri, _) => scenario switch
        {
            "not-found" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "empty" => OrderHistory([]),
            "wrong-instrument" => OrderHistory([BuildOrderResponse("recovered", quantity: "99", filledQuantity: "1", averagePrice: "160", refId: reference)]),
            "untrusted-next" => OrderHistory([BuildOrderResponse("recovered", quantity: "10", filledQuantity: "1", averagePrice: "160", refId: reference)],
                "https://attacker.example/orders/?cursor=second"),
            "looping-next" => OrderHistory([BuildOrderResponse("recovered", quantity: "10", filledQuantity: "1", averagePrice: "160", refId: reference)], uri.AbsoluteUri),
            _ => uri.Query.Contains("cursor=second")
                ? OrderHistory([BuildOrderResponse("other-broker", quantity: "10", filledQuantity: "1", averagePrice: "160", refId: reference)])
                : OrderHistory([BuildOrderResponse("recovered", quantity: "10", filledQuantity: "1", averagePrice: "160", refId: reference)],
                    "https://api.robinhood.com/orders/?cursor=second")
        });
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();
        await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });

        var report = await sut.ModifyOrderAsync("client-1", new OrderModification { NewQuantity = 12m });

        report.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        handler.Requests.Should().ContainSingle(r => IsReplacementPost(r));
        handler.Requests.Should().NotContain(r => r.Url.Contains("attacker.example"));
        handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Post && r.Url.EndsWith("/orders/recovered/cancel/"));
    }

    [Fact]
    public async Task CancelOrderAsync_UncertainOptionReplacement_RecoversQualifiedContractAndCancelsBrokerId()
    {
        var handler = CreateRecoveryHandler((reference, _, _) => OrderHistory(
            [BuildOptionOrderResponse("recovered", quantity: "10", filledQuantity: "1", averagePrice: "1.50", refId: reference)]), options: true);
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();
        await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });

        var report = await sut.CancelOrderAsync("client-1");

        report.OrderStatus.Should().Be(OrderStatus.Cancelled);
        report.GatewayOrderId.Should().Be("recovered");
        report.FilledQuantity.Should().Be(3m);
        report.FillPrice.Should().Be((2m * 1.25m + 1.50m) / 3m);
        handler.Requests.Should().Contain(r => r.Method == HttpMethod.Get && r.Url.Contains("/options/orders/?ref_id="));
        handler.Requests.Should().Contain(r => r.Method == HttpMethod.Post && r.Url.EndsWith("/options/orders/recovered/cancel/"));
    }

    [Fact]
    public async Task ModifyOrderAsync_DiscoveredReplacementBrokerId_ReusesCanonicalChainFillBasis()
    {
        var handler = CreateRecoveryHandler((reference, _, _) => OrderHistory(
            [BuildOrderResponse("recovered", quantity: "10", filledQuantity: "1", averagePrice: "160", refId: reference)]));
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();
        await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });

        var report = await sut.ModifyOrderAsync("recovered", new OrderModification { NewQuantity = 12m });

        report.ClientOrderId.Should().Be("client-1");
        report.FilledQuantity.Should().Be(3m);
        report.OrderQuantity.Should().Be(12m);
        var posts = handler.Requests.Where(IsReplacementPost).ToArray();
        posts.Should().HaveCount(2);
        using var payload = JsonDocument.Parse(posts[1].Body!);
        payload.RootElement.GetProperty("quantity").GetString().Should().Be("9");
    }

    [Fact]
    public async Task ModifyOrderAsync_FailedReadAfterSuccessfulPriceAmendment_PreservesConfirmedReplacementTerms()
    {
        var handler = CreateModificationHandler((id, read) => id == "order-1"
            ? BuildOrderResponse(state: read < 3 ? "confirmed" : "cancelled", quantity: "10", filledQuantity: "2", averagePrice: "150")
            : BuildJson(new { }));
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();
        var amended = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m, NewLimitPrice = 152m, NewStopPrice = 148m });

        var refused = await sut.CancelOrderAsync("client-1");

        amended.ReportType.Should().Be(ExecutionReportType.Modified);
        refused.ReportType.Should().Be(ExecutionReportType.CancelRejected);
        refused.HasOrderTerms.Should().BeTrue();
        refused.OrderLimitPrice.Should().Be(152m);
        refused.OrderStopPrice.Should().Be(148m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AmendmentRecovery_AfterRestart_DoesNotMistakeCancelledPredecessorForReplacementOutcome(bool predecessorInitiallyUnavailable)
    {
        var available = false;
        var predecessorVisible = true;
        var handler = CreateRecoveryHandler((reference, _, _) => available
            ? OrderHistory([BuildOrderResponse("recovered", quantity: "10", filledQuantity: "1", averagePrice: "160", refId: reference)])
            : OrderHistory([]), predecessorVisible: () => predecessorVisible);
        ExecutionReport pending;
        await using (var first = CreateSut(handler))
        {
            await first.ConnectAsync();
            pending = await first.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });
        }
        pending.CandidateClientOrderId.Should().NotBeNullOrWhiteSpace();
        pending.CandidateClientOrderId.Should().NotBe("order-1");
        var original = new OrderState
        {
            OrderId = "client-1",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            FilledQuantity = 2m,
            AverageFillPrice = 150m,
            Status = OrderStatus.Accepted
        };
        var retained = original with { Status = OrderStatus.PendingNew };
        var amendment = new PendingOrderAmendment(original, original with { Quantity = 12m }, "order-1")
        { CandidateClientOrderId = pending.CandidateClientOrderId };
        await using var restarted = CreateSut(handler);
        await restarted.ConnectAsync();
        var recovery = (IBrokerageOrderAmendmentRecoveryGateway)restarted;
        predecessorVisible = !predecessorInitiallyUnavailable;

        var unresolved = await recovery.GetAmendmentForRecoveryAsync("client-1", retained, amendment);
        predecessorVisible = true;
        var blocked = await restarted.ModifyOrderAsync("client-1", new OrderModification { NewQuantity = 12m });
        unresolved.Should().BeNull();
        blocked.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        handler.Requests.Should().ContainSingle(r => IsReplacementPost(r));

        available = true;
        var recovered = await recovery.GetAmendmentForRecoveryAsync("client-1", retained, amendment);
        var cancelled = await restarted.CancelOrderAsync("client-1");

        recovered.Should().NotBeNull();
        recovered!.GatewayOrderId.Should().Be("recovered");
        recovered.ReplacesOrderId.Should().Be("order-1");
        recovered.HasOrderTerms.Should().BeTrue();
        recovered.OrderQuantity.Should().Be(12m);
        recovered.FilledQuantity.Should().Be(3m);
        cancelled.OrderStatus.Should().Be(OrderStatus.Cancelled);
        cancelled.GatewayOrderId.Should().Be("recovered");
        cancelled.ClientOrderId.Should().Be("client-1");
        handler.Requests.Should().ContainSingle(r => IsReplacementPost(r));
    }

    [Fact]
    public async Task OmsCancel_AfterRestartWithOnlyTheReplacementReference_CancelsTheRecoveredSuccessor()
    {
        var handler = CreateRecoveryHandler((reference, _, _) =>
            OrderHistory([BuildOrderResponse("recovered", quantity: "10", filledQuantity: "1", averagePrice: "160", refId: reference)]));
        ExecutionReport pending;
        await using (var first = CreateSut(handler))
        {
            await first.ConnectAsync();
            pending = await first.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });
        }
        var original = new OrderState
        {
            OrderId = "client-1",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            FilledQuantity = 2m,
            AverageFillPrice = 150m,
            Status = OrderStatus.Accepted
        };
        var directory = Path.Combine(Path.GetTempPath(), $"robinhood-ref-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "orders.json");
            // The pending report persisted only the replacement's client reference.
            new FileBrokerageOrderRecoveryStore(path, "robinhood").Save(new RetainedBrokerageOrder(original, "order-1", true)
            {
                PendingAmendment = new PendingOrderAmendment(original, original with { Quantity = 12m }, "order-1")
                { CandidateClientOrderId = pending.CandidateClientOrderId }
            });
            await using var restarted = CreateSut(handler);
            await restarted.ConnectAsync();
            await using var oms = new OrderManagementSystem(restarted, NullLogger<OrderManagementSystem>.Instance,
                recoveryStore: new FileBrokerageOrderRecoveryStore(path, "robinhood"),
                portfolioState: new PaperTradingPortfolio(100_000m));
            await oms.StartAsync();

            var cancelled = await oms.CancelOrderAsync("client-1");

            cancelled.Success.Should().BeTrue(cancelled.ErrorMessage);
            handler.Requests.Should().Contain(r => r.Url.Contains("/orders/recovered/cancel/", StringComparison.Ordinal),
                "the kill switch must reach the live successor, not only the cancelled predecessor");
            oms.GetOrder("client-1")!.FilledQuantity.Should().Be(3m, "the successor's fill is booked before cancelling");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AmendmentRecovery_ReferenceLostBeforePersistence_BlocksCanonicalAndPredecessorAliases()
    {
        var handler = CreateRecoveryHandler((reference, _, _) => OrderHistory(
            [BuildOrderResponse("recovered", quantity: "10", filledQuantity: "1", averagePrice: "160", refId: reference)]));
        await using (var first = CreateSut(handler))
        {
            await first.ConnectAsync();
            await first.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });
        }
        var original = new OrderState
        {
            OrderId = "client-1",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            FilledQuantity = 2m,
            AverageFillPrice = 150m,
            Status = OrderStatus.Accepted
        };
        var amendment = new PendingOrderAmendment(original, original with { Quantity = 12m }, "order-1");
        await using var restarted = CreateSut(handler);
        await restarted.ConnectAsync();

        var unresolved = await ((IBrokerageOrderAmendmentRecoveryGateway)restarted).GetAmendmentForRecoveryAsync(
            "client-1", original with { Status = OrderStatus.PendingNew }, amendment);
        var canonical = await restarted.ModifyOrderAsync("client-1", new OrderModification { NewQuantity = 12m });
        var predecessor = await restarted.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });
        var cancellation = await restarted.CancelOrderAsync("order-1");

        unresolved.Should().BeNull();
        canonical.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        predecessor.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        predecessor.RejectReason.Should().Contain("reference is unavailable");
        cancellation.ReportType.Should().Be(ExecutionReportType.CancelRejected);
        handler.Requests.Should().ContainSingle(r => IsReplacementPost(r));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task ModifyOrderAsync_AmbiguousServerResponse_ReconcilesBeforeFurtherSubmission(HttpStatusCode status)
    {
        var handler = CreateRecoveryHandler((reference, _, _) => OrderHistory(
            [BuildOrderResponse("recovered", quantity: "10", filledQuantity: "1", averagePrice: "160", refId: reference)]),
            firstReplacementStatus: status);
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var pending = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });
        var cancelled = await sut.CancelOrderAsync("client-1");

        pending.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        cancelled.OrderStatus.Should().Be(OrderStatus.Cancelled);
        cancelled.GatewayOrderId.Should().Be("recovered");
        handler.Requests.Should().ContainSingle(r => IsReplacementPost(r));
    }

    [Fact]
    public async Task ModifyOrderAsync_PreassignedReference_IsTheSubmittedRefAndRecoversAfterRestart()
    {
        var handler = CreateRecoveryHandler((reference, _, _) => OrderHistory(
            [BuildOrderResponse("recovered", quantity: "10", filledQuantity: "1", averagePrice: "160", refId: reference)]));
        ExecutionReport pending;
        await using (var first = CreateSut(handler))
        {
            await first.ConnectAsync();
            pending = await first.ModifyOrderAsync("order-1",
                new OrderModification { NewQuantity = 12m, ReplacementClientOrderId = "persisted-ref" });
        }
        var original = RetainedOriginal(filled: 2m, averagePrice: 150m);
        var amendment = new PendingOrderAmendment(original, original with { Quantity = 12m }, "order-1")
        { CandidateClientOrderId = "persisted-ref", PriorChainBrokerOrderIds = [] };
        await using var restarted = CreateSut(handler);
        await restarted.ConnectAsync();

        var recovered = await ((IBrokerageOrderAmendmentRecoveryGateway)restarted).GetAmendmentForRecoveryAsync(
            "client-1", original with { Status = OrderStatus.PendingNew }, amendment);

        pending.ReportType.Should().Be(ExecutionReportType.ModificationPending);
        pending.CandidateClientOrderId.Should().Be("persisted-ref");
        recovered.Should().NotBeNull();
        recovered!.GatewayOrderId.Should().Be("recovered");
        recovered.CandidateClientOrderId.Should().Be("persisted-ref");
        handler.Requests.Should().ContainSingle(r => IsReplacementPost(r));
    }

    [Theory]
    // Predecessor order-1 is cancelled with 2 filled @ 150 and the candidate has 1 filled @ 160.
    [InlineData(false, 1, 150, "10", 3, 460)]   // single member: the store missed one predecessor fill
    [InlineData(true, 2, 145, "9", 4, 600)]     // two members: earlier member order-0 filled 1 @ 140
    public async Task AmendmentRecovery_LatePredecessorFill_AttributesEveryChainMemberExactly(
        bool earlierMember, int retainedFilled, int retainedAverage, string candidateQuantity,
        int expectedFilled, int expectedNotional)
    {
        var handler = CreateRecoveryHandler((reference, _, _) => OrderHistory(
                [BuildOrderResponse("recovered", quantity: candidateQuantity, filledQuantity: "1", averagePrice: "160", refId: reference)]),
            priorMember: id => id switch
            {
                "order-0" => BuildOrderResponse("order-0", "cancelled", "10", "1", "140", "client-1"),
                "recovered" => BuildOrderResponse("recovered", "confirmed", candidateQuantity, "1", "160", "persisted-ref"),
                _ => null
            });
        await using (var first = CreateSut(handler))
        {
            await first.ConnectAsync();
            await first.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m, ReplacementClientOrderId = "persisted-ref" });
        }
        var original = RetainedOriginal(filled: retainedFilled, averagePrice: retainedAverage);
        var amendment = new PendingOrderAmendment(original, original with { Quantity = 12m }, "order-1")
        {
            CandidateClientOrderId = "persisted-ref",
            PriorChainBrokerOrderIds = earlierMember ? ["order-0"] : []
        };
        await using var restarted = CreateSut(handler);
        await restarted.ConnectAsync();

        var recovered = await ((IBrokerageOrderAmendmentRecoveryGateway)restarted).GetAmendmentForRecoveryAsync(
            "client-1", original with { Status = OrderStatus.PendingNew }, amendment);

        recovered.Should().NotBeNull("the broker's higher predecessor fill is evidence, not a conflict");
        recovered!.FilledQuantity.Should().Be(expectedFilled);
        recovered.FillPrice.Should().Be((decimal)expectedNotional / expectedFilled);
        recovered.FillPriceIsCumulativeAverage.Should().BeTrue();
        recovered.OrderQuantity.Should().Be(12m);
    }

    private static OrderState RetainedOriginal(decimal filled, decimal averagePrice) => new()
    {
        OrderId = "client-1",
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = 10m,
        FilledQuantity = filled,
        AverageFillPrice = averagePrice,
        Status = OrderStatus.Accepted
    };

    private static HttpResponseMessage OrderHistory(HttpContent[] orders, string? next = null)
        => new(HttpStatusCode.OK)
        {
            Content = BuildJson(new
            {
                results = orders.Select(order => JsonSerializer.Deserialize<JsonElement>(order.ReadAsStringAsync().GetAwaiter().GetResult())).ToArray(),
                next
            })
        };

    private static RecordingHttpHandler CreateRecoveryHandler(
        Func<string, Uri, int, HttpResponseMessage> history,
        string recoveredState = "confirmed", string recoveredFilled = "1", bool options = false,
        Action? replacementStarted = null, HttpStatusCode? firstReplacementStatus = null,
        Func<bool>? predecessorVisible = null, Func<string, HttpContent?>? priorMember = null)
    {
        var cancelled = new HashSet<string>();
        string reference = string.Empty;
        var replacements = 0;
        var lookups = 0;
        var segment = options ? "/options/orders/" : "/orders/";
        return new RecordingHttpHandler(async (request, ct) =>
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            if (path == "/accounts/")
                return new(HttpStatusCode.OK) { Content = BuildAccountResponse() };
            if (path == "/instruments/")
                return new(HttpStatusCode.OK) { Content = BuildInstrumentListResponse() };
            if (path == "/options/instruments/opt-close/")
                return new(HttpStatusCode.OK) { Content = BuildOptionInstrumentResponse() };
            if (path == segment && request.Method == HttpMethod.Get)
                return history(reference, uri, ++lookups);
            if (path == segment && request.Method == HttpMethod.Post)
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                if (++replacements == 1)
                {
                    reference = payload.RootElement.GetProperty("ref_id").GetString()!;
                    replacementStarted?.Invoke();
                    if (firstReplacementStatus is { } status)
                        return new(status) { Content = new StringContent("Replacement acknowledgement unavailable") };
                    throw new TaskCanceledException("Submitted replacement response was lost");
                }
                return new(HttpStatusCode.OK) { Content = BuildOrderResponse("second-replacement") };
            }
            if (!path.StartsWith(segment, StringComparison.Ordinal))
                return new(HttpStatusCode.NotFound);
            var id = path[segment.Length..].Split('/')[0];
            if (id == "order-1" && predecessorVisible?.Invoke() == false)
                return new(HttpStatusCode.NotFound);
            if (path.EndsWith("/cancel/", StringComparison.Ordinal))
            {
                cancelled.Add(id);
                return new(HttpStatusCode.NoContent);
            }
            if (priorMember?.Invoke(id) is { } member)
                return new(HttpStatusCode.OK) { Content = member };
            var state = cancelled.Contains(id) ? "cancelled" : id == "order-1" ? "confirmed" : recoveredState;
            var filled = id == "order-1" ? "2" : recoveredFilled;
            var average = options ? id == "order-1" ? "1.25" : "1.50" : id == "order-1" ? "150" : "160";
            return new(HttpStatusCode.OK)
            {
                Content = options
                    ? BuildOptionOrderResponse(id, state, "10", filled, average, refId: id == "order-1" ? "client-1" : reference)
                    : BuildOrderResponse(id, state, "10", filled, average, id == "order-1" ? "client-1" : reference)
            };
        });
    }

    private static bool IsReplacementPost(RecordedRequest request)
        => request.Method == HttpMethod.Post && request.Url.EndsWith("/orders/", StringComparison.Ordinal);

    [Fact]
    public async Task ModifyOrderAsync_CancelledBeforeThePredecessorCancel_IsNotSentAndLeavesTheOrderUnblocked()
    {
        using var stop = new CancellationTokenSource();
        var cancelOnRead = false;
        var handler = CreateSendBoundaryHandler(stop, () => cancelOnRead, cancelOnInstrument: () => false);
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();
        cancelOnRead = true;

        var cancelled = () => sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m }, stop.Token);

        await cancelled.Should().ThrowAsync<BrokerageRequestNotSentException>();
        handler.Requests.Should().NotContain(r => r.Url.EndsWith("/cancel/", StringComparison.Ordinal),
            "nothing reached Robinhood before the predecessor's cancel request");
        cancelOnRead = false;
        var retried = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m });
        retried.ReportType.Should().Be(ExecutionReportType.Modified, "the unsent attempt left nothing to reconcile");
    }

    [Fact]
    public async Task ModifyOrderAsync_CancelledAfterThePredecessorCancelBeforeTheReplacementPost_ReportsCancelledWithoutSuccessor()
    {
        using var stop = new CancellationTokenSource();
        var handler = CreateSendBoundaryHandler(stop, () => false, cancelOnInstrument: () => true);
        await using var sut = CreateSut(handler);
        await sut.ConnectAsync();

        var report = await sut.ModifyOrderAsync("order-1", new OrderModification { NewQuantity = 12m }, stop.Token);

        report.ReportType.Should().Be(ExecutionReportType.ModificationRejected,
            "the predecessor is confirmed cancelled and no replacement request was sent");
        report.OrderStatus.Should().Be(OrderStatus.Cancelled);
        report.CandidateClientOrderId.Should().BeNull("there is no replacement reference left to reconcile");
        handler.Requests.Should().NotContain(r => IsReplacementPost(r));
        var next = await sut.CancelOrderAsync("client-1");
        (next.RejectReason ?? string.Empty).Should().NotContain("must be reconciled", "no unresolved attempt remains");
    }

    /// <summary>Order-1 is working until its cancel request, then cancelled. Optionally cancels
    /// the caller's token on the first order read or on the replacement's instrument lookup.</summary>
    private static RecordingHttpHandler CreateSendBoundaryHandler(
        CancellationTokenSource stop, Func<bool> cancelOnRead, Func<bool> cancelOnInstrument)
    {
        var cancelled = false;
        var replacements = 0;
        return new RecordingHttpHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response;
            if (path == "/accounts/")
                response = new(HttpStatusCode.OK) { Content = BuildAccountResponse() };
            else if (path == "/instruments/")
            {
                if (cancelOnInstrument())
                    stop.Cancel();
                response = new(HttpStatusCode.OK) { Content = BuildInstrumentListResponse() };
            }
            else if (path.EndsWith("/cancel/", StringComparison.Ordinal))
            {
                cancelled = true;
                response = new(HttpStatusCode.NoContent);
            }
            else if (path == "/orders/" && request.Method == HttpMethod.Post)
                response = new(HttpStatusCode.OK) { Content = BuildOrderResponse($"replacement-{++replacements}") };
            else if (path.StartsWith("/orders/", StringComparison.Ordinal))
            {
                if (cancelOnRead())
                    stop.Cancel();
                var id = path.Split('/')[2];
                response = new(HttpStatusCode.OK)
                {
                    Content = id == "order-1"
                        ? BuildOrderResponse(state: cancelled ? "cancelled" : "confirmed", quantity: "10", filledQuantity: "2", averagePrice: "150")
                        : BuildOrderResponse(id)
                };
            }
            else
                response = new(HttpStatusCode.NotFound) { Content = new StringContent("unexpected") };
            return Task.FromResult(response);
        });
    }

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
