using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class AlpacaTradeUpdatesClientTests
{
    [Fact]
    public async Task ProcessMessageAsync_TradeUpdateWithDocumentedExecutionIdentity_EmitsReportAndPersistsWatermark()
    {
        var cursor = new InMemoryCursorStore();
        await using var sut = CreateClient(cursor);

        await sut.ProcessMessageAsync("""
            {"stream":"trade_updates","data":{"event":"fill","execution_id":"execution-123","timestamp":"2026-07-23T12:34:56Z","price":"123.45","order":{"id":"order-123","client_order_id":"client-123","symbol":"AAPL","side":"buy","qty":"10","filled_qty":"10","status":"filled"}}}
            """);

        await using var reports = sut.Reports.GetAsyncEnumerator();
        (await reports.MoveNextAsync()).Should().BeTrue();
        reports.Current.OrderId.Should().Be("order-123");
        reports.Current.ReportType.Should().Be(ExecutionReportType.Fill);
        cursor.Watermark.Should().Be(DateTimeOffset.Parse("2026-07-23T12:34:56Z"));
        cursor.EventIds.Should().ContainSingle().Which.Should().Be("execution:execution-123");
    }

    [Fact]
    public async Task ReconcileExecutionSnapshotsAsync_QueriesBoundedAllOrderHistoryAfterWatermark()
    {
        var cursor = new InMemoryCursorStore();
        await using var stream = CreateClient(cursor);
        await stream.ProcessMessageAsync("""
            {"stream":"trade_updates","data":{"event":"fill","execution_id":"execution-123","timestamp":"2026-07-23T12:34:56Z","order":{"id":"order-123","symbol":"AAPL","side":"buy","qty":"10","filled_qty":"10","status":"filled"}}}
            """);

        Uri? requestUri = null;
        var handler = new DelegateHandler(request =>
        {
            requestUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"id":"order-123","symbol":"AAPL","side":"buy","qty":"10","filled_qty":"10","filled_avg_price":"123.45","status":"filled","updated_at":"2026-07-23T12:34:56Z"}]""", Encoding.UTF8, "application/json")
            };
        });
        var options = new AlpacaOptions(KeyId: "key", SecretKey: "secret");
        var credentials = new AlpacaCredentialSnapshot("key", "secret", "paper", UseSandbox: true);
        var gateway = new AlpacaBrokerageGateway(new StubHttpClientFactory(handler), options,
            NullLogger<AlpacaBrokerageGateway>.Instance, stream, credentials);

        var reports = await gateway.ReconcileExecutionSnapshotsAsync(CancellationToken.None);

        requestUri.Should().NotBeNull();
        requestUri!.Query.Should().Contain("status=all").And.Contain("limit=500").And.Contain("after=");
        reports.Should().ContainSingle();
        reports[0].ReportType.Should().Be(ExecutionReportType.Fill);
        reports[0].OrderStatus.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public async Task ProcessMessageAsync_LifecycleUpdateWithoutExecutionId_UsesEventAndOrderIdentity()
    {
        var cursor = new InMemoryCursorStore();
        await using var sut = CreateClient(cursor);

        await sut.ProcessMessageAsync("""
            {"stream":"trade_updates","data":{"event":"canceled","timestamp":"2026-07-23T12:34:56Z","order":{"id":"order-123","symbol":"AAPL","side":"buy","qty":"10","filled_qty":"0","status":"canceled"}}}
            """);

        var timestamp = DateTimeOffset.Parse("2026-07-23T12:34:56Z");
        cursor.EventIds.Should().ContainSingle().Which.Should().Be(
            $"event:canceled:order-123:{timestamp.UtcDateTime.Ticks}");
    }

    [Fact]
    public async Task Constructor_UsesProvidedCredentialSnapshotForStreamEndpoint()
    {
        var options = new AlpacaOptions(KeyId: "key", SecretKey: "secret", UseSandbox: false);
        var credentials = new AlpacaCredentialSnapshot("key", "secret", "paper", UseSandbox: true);
        await using var sut = new AlpacaTradeUpdatesClient(options, NullLogger<AlpacaTradeUpdatesClient>.Instance, credentials: credentials);

        sut.StreamEndpoint.Should().Be(new Uri("wss://paper-api.alpaca.markets/stream"));
    }

    private static AlpacaTradeUpdatesClient CreateClient(InMemoryCursorStore cursor) => new(
        new AlpacaOptions(KeyId: "key", SecretKey: "secret"),
        NullLogger<AlpacaTradeUpdatesClient>.Instance,
        cursorStore: cursor);

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class InMemoryCursorStore : IAlpacaTradeUpdateCursorStore
    {
        public DateTimeOffset? Watermark { get; private set; }
        public IReadOnlyList<string> EventIds { get; private set; } = [];
        public DateTimeOffset? Load() => Watermark;
        public IReadOnlyList<string> LoadRecentEventIds() => EventIds;
        public void Save(DateTimeOffset watermark, IReadOnlyCollection<string> recentEventIds)
        {
            Watermark = watermark;
            EventIds = recentEventIds.ToArray();
        }
    }
}
