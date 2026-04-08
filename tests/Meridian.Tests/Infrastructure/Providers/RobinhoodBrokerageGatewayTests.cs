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
    private const string BaseUrl = "https://api.robinhood.com";

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

    // ── GetPositionsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPositionsAsync_NoOptionPositions_ReturnsOnlyEquityPositions()
    {
        // Equity positions page, then options positions page (empty).
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildPositionsResponse() },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildJson(new { results = Array.Empty<object>() }) },
            // instrument lookup for the equity position
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildJson(new { url = "https://api.robinhood.com/instruments/AAPL/", symbol = "AAPL" }) },
        });

        // SequentialStubHandler dequeues in order; instrument resolution is async inside the loop.
        // Use a router that can handle any order of calls.
        var handler = new UrlRoutingStubHandler(new Dictionary<string, HttpResponseMessage>
        {
            [$"{BaseUrl}/positions/?nonzero=true"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildPositionsResponse() },
            [$"{BaseUrl}/options/positions/?nonzero=true"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildJson(new { results = Array.Empty<object>() }) },
            [$"https://api.robinhood.com/instruments/AAPL/"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildJson(new { url = "https://api.robinhood.com/instruments/AAPL/", symbol = "AAPL" }) },
        });

        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var positions = await sut.GetPositionsAsync(cts.Token);

        positions.Should().HaveCount(1);
        positions[0].Symbol.Should().Be("AAPL");
        positions[0].AssetClass.Should().Be("equity");

        await sut.DisposeAsync();
    }

    [Fact]
    public async Task GetPositionsAsync_WithOptionPositions_ReturnsBothEquityAndOptionPositions()
    {
        var optionPositionsContent = BuildJson(new
        {
            results = new[]
            {
                new
                {
                    option = "https://api.robinhood.com/options/instruments/some-uuid/",
                    chain_symbol = "AAPL",
                    quantity = "2",
                    average_price = "3.50",
                    type = "call"
                }
            }
        });

        var handler = new UrlRoutingStubHandler(new Dictionary<string, HttpResponseMessage>
        {
            [$"{BaseUrl}/positions/?nonzero=true"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildPositionsResponse() },
            [$"{BaseUrl}/options/positions/?nonzero=true"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = optionPositionsContent },
            [$"https://api.robinhood.com/instruments/AAPL/"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildJson(new { url = "https://api.robinhood.com/instruments/AAPL/", symbol = "AAPL" }) },
        });

        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var positions = await sut.GetPositionsAsync(cts.Token);

        positions.Should().HaveCount(2);

        var equity = positions.First(p => p.AssetClass == "equity");
        equity.Symbol.Should().Be("AAPL");
        equity.Quantity.Should().Be(10m);

        var option = positions.First(p => p.AssetClass == "option");
        option.Symbol.Should().Be("AAPL");
        option.Quantity.Should().Be(2m);
        option.AverageEntryPrice.Should().Be(3.50m);

        await sut.DisposeAsync();
    }

    // ── GetOpenOrdersAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetOpenOrdersAsync_WithOpenOptionOrders_ReturnsBothEquityAndOptionOrders()
    {
        var equityOrdersContent = BuildJson(new
        {
            results = new[]
            {
                new
                {
                    id = "eq-order-1",
                    ref_id = "ref-eq-1",
                    symbol = "AAPL",
                    side = "buy",
                    type = "limit",
                    time_in_force = "gfd",
                    quantity = "1",
                    price = "150.00",
                    state = "queued",
                    created_at = "2024-01-02T10:00:00Z"
                }
            }
        });

        var optionOrdersContent = BuildJson(new
        {
            results = new[]
            {
                new
                {
                    id = "opt-order-1",
                    ref_id = "ref-opt-1",
                    chain_symbol = "AAPL",
                    direction = "debit",
                    type = "limit",
                    time_in_force = "gfd",
                    quantity = "2",
                    price = "3.50",
                    state = "queued",
                    created_at = "2024-01-02T10:05:00Z"
                }
            }
        });

        var handler = new UrlRoutingStubHandler(new Dictionary<string, HttpResponseMessage>
        {
            [$"{BaseUrl}/orders/?state=queued,unconfirmed,confirmed,partially_filled"] =
                new HttpResponseMessage(HttpStatusCode.OK) { Content = equityOrdersContent },
            [$"{BaseUrl}/options/orders/?state=queued,unconfirmed,confirmed,partially_filled"] =
                new HttpResponseMessage(HttpStatusCode.OK) { Content = optionOrdersContent },
        });

        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var orders = await sut.GetOpenOrdersAsync(cts.Token);

        orders.Should().HaveCount(2);
        orders.Should().Contain(o => o.OrderId == "eq-order-1" && o.Symbol == "AAPL");
        orders.Should().Contain(o => o.OrderId == "opt-order-1" && o.Symbol == "AAPL" && o.Side == OrderSide.Buy);

        await sut.DisposeAsync();
    }

    // ── SubmitOrderAsync (options) ────────────────────────────────────────

    [Fact]
    public async Task SubmitOrderAsync_OptionOrder_RoutesToOptionsOrdersEndpoint()
    {
        var optionOrderResponse = BuildJson(new
        {
            id = "opt-order-submit-1",
            chain_symbol = "AAPL",
            direction = "debit",
            type = "limit",
            quantity = "1",
            state = "confirmed",
            created_at = "2024-01-02T10:00:00Z"
        });

        var postedUrls = new List<string>();
        var handler = new RecordingStubHandler(postedUrls,
            connectResponse: BuildAccountResponse(),
            accountUrlResponse: BuildAccountResponse(),
            submitResponse: optionOrderResponse);

        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            LimitPrice = 3.50m,
            TimeInForce = TimeInForce.Day,
            Metadata = new Dictionary<string, string>
            {
                ["asset_class"] = "option",
                ["option_instrument_url"] = "https://api.robinhood.com/options/instruments/test-uuid/",
            }
        };

        var report = await sut.SubmitOrderAsync(request, cts.Token);

        report.ReportType.Should().Be(ExecutionReportType.New);
        report.OrderStatus.Should().Be(OrderStatus.Accepted);
        postedUrls.Should().Contain(u => u.Contains("/options/orders/"));

        await sut.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrderAsync_OptionOrder_MissingInstrumentUrl_ReturnsRejected()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, BuildAccountResponse()));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Connect first (StubHttpHandler always returns account response).
        await sut.ConnectAsync(cts.Token);

        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            LimitPrice = 3.50m,
            TimeInForce = TimeInForce.Day,
            Metadata = new Dictionary<string, string>
            {
                ["asset_class"] = "option",
                // option_instrument_url intentionally omitted
            }
        };

        var report = await sut.SubmitOrderAsync(request, cts.Token);

        report.ReportType.Should().Be(ExecutionReportType.Rejected);
        report.RejectReason.Should().Contain("option_instrument_url");

        await sut.DisposeAsync();
    }

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

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new HttpClient(_handler, disposeHandler: false) { BaseAddress = new Uri("https://api.robinhood.com/") };
    }

    /// <summary>Routes each request to a pre-canned response keyed by the full request URL.</summary>
    private sealed class UrlRoutingStubHandler : HttpMessageHandler
    {
        // Pre-read all body strings at construction time to avoid blocking async calls at dispatch time.
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _routes;

        public UrlRoutingStubHandler(Dictionary<string, HttpResponseMessage> routes)
        {
            _routes = new Dictionary<string, (HttpStatusCode, string)>(routes.Count);
            foreach (var (url, msg) in routes)
            {
                var body = msg.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                _routes[url] = (msg.StatusCode, body);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var url = request.RequestUri?.ToString() ?? string.Empty;
            if (_routes.TryGetValue(url, out var cached))
            {
                return Task.FromResult(new HttpResponseMessage(cached.Status)
                {
                    Content = new StringContent(cached.Body, System.Text.Encoding.UTF8, "application/json")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    /// <summary>
    /// Records all POST request URLs and routes:
    /// <list type="bullet">
    ///   <item>GET /accounts/ → <paramref name="connectResponse"/> (ConnectAsync + GetAccountUrlAsync)</item>
    ///   <item>POST /options/orders/ → <paramref name="submitResponse"/></item>
    /// </list>
    /// </summary>
    private sealed class RecordingStubHandler : HttpMessageHandler
    {
        private readonly List<string> _postedUrls;
        private readonly string _connectBody;
        private readonly string _submitBody;

        public RecordingStubHandler(
            List<string> postedUrls,
            StringContent connectResponse,
            StringContent accountUrlResponse,
            StringContent submitResponse)
        {
            _postedUrls = postedUrls;
            // Pre-read bodies synchronously at construction — no blocking at dispatch time.
            _connectBody = connectResponse.ReadAsStringAsync().GetAwaiter().GetResult();
            _submitBody  = submitResponse.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var url = request.RequestUri?.ToString() ?? string.Empty;

            if (request.Method == HttpMethod.Post)
            {
                _postedUrls.Add(url);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_submitBody, System.Text.Encoding.UTF8, "application/json")
                });
            }

            // All GETs return the account response (handles ConnectAsync + GetAccountUrlAsync).
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_connectBody, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
