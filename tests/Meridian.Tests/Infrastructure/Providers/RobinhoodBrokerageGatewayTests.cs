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

    private static StringContent BuildOrderResponse(string id = "order-1", string state = "confirmed") =>
        BuildJson(new
        {
            id,
            ref_id = "client-1",
            symbol = "AAPL",
            side = "buy",
            type = "market",
            time_in_force = "gfd",
            quantity = "1",
            state,
            created_at = "2024-01-02T10:00:00Z"
        });

    private static StringContent BuildOptionOrderResponse(string id = "option-order-1", string state = "confirmed") =>
        BuildJson(new
        {
            id,
            ref_id = "client-1",
            chain_symbol = "AAPL",
            direction = "credit",
            quantity = "1",
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
                    position_effect = "close"
                }
            },
            state,
            created_at = "2024-01-02T10:00:00Z"
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
        handler.Requests.Should().NotContain(r => r.Url.Contains("/instruments/", StringComparison.Ordinal));
        handler.Requests.Should().NotContain(
            r => r.Method == HttpMethod.Post && string.Equals(r.Url, "https://api.robinhood.com/orders/", StringComparison.Ordinal));

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

        report.OrderStatus.Should().Be(OrderStatus.Rejected);
        report.RejectReason.Should().Contain("Cancel request failed");
        handler.Requests.Should().ContainSingle(
            r => r.Method == HttpMethod.Post && r.Url.EndsWith("/orders/order-1/cancel/", StringComparison.Ordinal));
        handler.Requests.Should().NotContain(
            r => r.Method == HttpMethod.Post && r.Url.EndsWith("/orders/", StringComparison.Ordinal));
        handler.Requests.Should().NotContain(
            r => r.Method == HttpMethod.Post && r.Url.EndsWith("/options/orders/", StringComparison.Ordinal));

        await sut.DisposeAsync();
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
