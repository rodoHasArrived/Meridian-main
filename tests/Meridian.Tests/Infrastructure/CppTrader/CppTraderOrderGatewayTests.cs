using System.Threading.Channels;
using System.Text.Json;
using FluentAssertions;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.CppTrader.Diagnostics;
using Meridian.Infrastructure.CppTrader.Execution;
using Meridian.Infrastructure.CppTrader.Host;
using Meridian.Infrastructure.CppTrader.Options;
using Meridian.Infrastructure.CppTrader.Protocol;
using Meridian.Infrastructure.CppTrader.Symbols;
using Meridian.Infrastructure.CppTrader.Translation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Tests.Infrastructure.CppTrader;

public sealed class CppTraderOrderGatewayTests
{
    [Fact]
    public void SymbolMapper_converts_prices_and_quantities_losslessly()
    {
        var options = CreateOptions(new CppTraderOptions
        {
            Enabled = true,
            Symbols = new Dictionary<string, CppTraderSymbolSpecification>(StringComparer.OrdinalIgnoreCase)
            {
                ["MSFT"] = new()
                {
                    Symbol = "MSFT",
                    SymbolId = 1,
                    TickSize = 0.01m,
                    QuantityIncrement = 1m,
                    LotSize = 1m,
                    PriceScale = 2
                }
            }
        });

        var mapper = new CppTraderSymbolMapper(options);

        mapper.ConvertPriceToNanos("MSFT", 125.34m).Should().Be(12_534);
        mapper.ConvertQuantityToNanos("MSFT", 50m).Should().Be(50);
        mapper.ConvertQuantityFromNanos("MSFT", 3).Should().Be(3m);
    }

    [Fact]
    public async Task Gateway_streams_execution_updates_and_populates_live_feed()
    {
        var options = CreateOptions(new CppTraderOptions
        {
            Enabled = true,
            Symbols = new Dictionary<string, CppTraderSymbolSpecification>(StringComparer.OrdinalIgnoreCase)
            {
                ["MSFT"] = new()
                {
                    Symbol = "MSFT",
                    SymbolId = 1,
                    TickSize = 0.01m,
                    QuantityIncrement = 1m,
                    LotSize = 1m,
                    PriceScale = 2
                }
            }
        });

        var session = new FakeSessionClient("session-1");
        var host = new FakeHostManager(session, options.CurrentValue.Enabled);
        var feed = new CppTraderLiveFeedAdapter();
        var gateway = new CppTraderOrderGateway(
            host,
            new CppTraderSymbolMapper(options),
            new CppTraderExecutionTranslator(new CppTraderSymbolMapper(options)),
            new CppTraderSnapshotTranslator(),
            feed,
            new CppTraderSessionDiagnosticsService(),
            options,
            NullLogger<CppTraderOrderGateway>.Instance);

        var ack = await gateway.SubmitAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = Meridian.Execution.Sdk.OrderSide.Buy,
            Type = Meridian.Execution.Sdk.OrderType.Limit,
            Quantity = 10,
            LimitPrice = 125.50m,
            TimeInForce = Meridian.Execution.Sdk.TimeInForce.GoodTilCancelled
        });

        ack.Status.Should().Be(Meridian.Execution.Models.OrderStatus.Accepted);

        session.Publish(new CppTraderEnvelope(
            CppTraderProtocolNames.Accepted,
            Payload: JsonSerializer.SerializeToElement(
                new AcceptedEvent(ack.OrderId, ack.ClientOrderId, "MSFT", DateTimeOffset.UtcNow))));

        session.Publish(new CppTraderEnvelope(
            CppTraderProtocolNames.BookSnapshot,
            Payload: JsonSerializer.SerializeToElement(
                new BookSnapshotEvent(new CppTraderBookSnapshot(
                    "MSFT",
                    [new CppTraderBookLevel(125.49m, 100)],
                    [new CppTraderBookLevel(125.50m, 200)],
                    125.495m,
                    125.496m,
                    0.6m,
                    42,
                    "XNAS",
                    DateTimeOffset.UtcNow)))));

        session.Publish(new CppTraderEnvelope(
            CppTraderProtocolNames.Execution,
            Payload: JsonSerializer.SerializeToElement(
                new ExecutionEvent(ack.OrderId, ack.ClientOrderId, "MSFT", 10, 10, 125.50m, true, DateTimeOffset.UtcNow))));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var updates = new List<OrderStatusUpdate>();
        await foreach (var update in gateway.StreamOrderUpdatesAsync(cts.Token))
        {
            updates.Add(update);
            if (updates.Count == 2)
                break;
        }

        updates.Should().ContainSingle(update => update.Status == Meridian.Execution.Models.OrderStatus.Accepted);
        updates.Should().ContainSingle(update => update.Status == Meridian.Execution.Models.OrderStatus.Filled);
        feed.GetLastOrderBook("MSFT").Should().NotBeNull();
        feed.GetLastQuote("MSFT")!.AskPrice.Should().Be(125.50m);
    }

    private static IOptionsMonitor<CppTraderOptions> CreateOptions(CppTraderOptions options) =>
        new StaticOptionsMonitor(options);

    private sealed class StaticOptionsMonitor(CppTraderOptions options) : IOptionsMonitor<CppTraderOptions>
    {
        public CppTraderOptions CurrentValue { get; } = options;

        public CppTraderOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<CppTraderOptions, string> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static NullDisposable Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeHostManager(FakeSessionClient session, bool enabled) : ICppTraderHostManager
    {
        public Task<ICppTraderSessionClient> CreateSessionAsync(CppTraderSessionKind sessionKind, string? sessionName = null, CancellationToken ct = default) =>
            Task.FromResult<ICppTraderSessionClient>(session);

        public HostHealthSnapshot GetHealthSnapshot() => new(enabled, true, 1, 0, 0, 0, 0, null, null, null);

        public void RecordFault(string message)
        {
        }

        public void RecordHeartbeat()
        {
        }

        public void RecordExecutionUpdate(bool rejected)
        {
        }

        public void RecordSnapshot()
        {
        }
    }

    private sealed class FakeSessionClient(string sessionId) : ICppTraderSessionClient
    {
        private readonly Channel<CppTraderEnvelope> _events = Channel.CreateUnbounded<CppTraderEnvelope>();

        public string SessionId { get; } = sessionId;

        public CppTraderSessionKind SessionKind => CppTraderSessionKind.Execution;

        public Task<RegisterSymbolResponse> RegisterSymbolAsync(RegisterSymbolRequest request, CancellationToken ct = default) =>
            Task.FromResult(new RegisterSymbolResponse(request.Symbol, Registered: true));

        public Task<SubmitOrderResponse> SubmitOrderAsync(SubmitOrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(new SubmitOrderResponse(request.OrderId, request.ClientOrderId, request.Symbol, Accepted: true, FailureReason: null, DateTimeOffset.UtcNow));

        public Task<CancelOrderResponse> CancelOrderAsync(CancelOrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CancelOrderResponse(request.OrderId, Accepted: true, FailureReason: null, DateTimeOffset.UtcNow));

        public Task<GetSnapshotResponse> GetSnapshotAsync(GetSnapshotRequest request, CancellationToken ct = default) =>
            Task.FromResult(new GetSnapshotResponse(null));

        public Task<HeartbeatResponse> HeartbeatAsync(CancellationToken ct = default) =>
            Task.FromResult(new HeartbeatResponse("fake", DateTimeOffset.UtcNow));

        public IAsyncEnumerable<CppTraderEnvelope> ReadEventsAsync(CancellationToken ct = default) =>
            _events.Reader.ReadAllAsync(ct);

        public void Publish(CppTraderEnvelope envelope) => _events.Writer.TryWrite(envelope);

        public ValueTask DisposeAsync()
        {
            _events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
