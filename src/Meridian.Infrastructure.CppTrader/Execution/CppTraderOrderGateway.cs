using System.Threading.Channels;
using Meridian.Infrastructure.CppTrader.Diagnostics;
using Meridian.Infrastructure.CppTrader.Host;
using Meridian.Infrastructure.CppTrader.Options;
using Meridian.Infrastructure.CppTrader.Protocol;
using Meridian.Infrastructure.CppTrader.Symbols;
using Meridian.Infrastructure.CppTrader.Translation;
using GatewayExecutionMode = global::Meridian.Execution.Models.ExecutionMode;
using GatewayOrderStatus = global::Meridian.Execution.Models.OrderStatus;
using ExecutionOrderRequest = global::Meridian.Execution.Sdk.OrderRequest;
using ExecutionOrderSide = global::Meridian.Execution.Sdk.OrderSide;
using ExecutionOrderType = global::Meridian.Execution.Sdk.OrderType;
using ExecutionTimeInForce = global::Meridian.Execution.Sdk.TimeInForce;

namespace Meridian.Infrastructure.CppTrader.Execution;

/// <summary>
/// Process-backed simulated order gateway that delegates matching to an external CppTrader host.
/// </summary>
public sealed class CppTraderOrderGateway : IOrderGateway
{
    private readonly ICppTraderHostManager _hostManager;
    private readonly ICppTraderSymbolMapper _symbolMapper;
    private readonly ICppTraderExecutionTranslator _executionTranslator;
    private readonly ICppTraderSnapshotTranslator _snapshotTranslator;
    private readonly CppTraderLiveFeedAdapter _feedAdapter;
    private readonly ICppTraderSessionDiagnosticsService _sessionDiagnostics;
    private readonly IOptionsMonitor<CppTraderOptions> _optionsMonitor;
    private readonly ILogger<CppTraderOrderGateway> _logger;
    private readonly Channel<OrderStatusUpdate> _updates =
        Channel.CreateUnbounded<OrderStatusUpdate>();
    private readonly Lock _gate = new();
    private readonly HashSet<string> _registeredSymbols = new(StringComparer.OrdinalIgnoreCase);
    private ICppTraderSessionClient? _session;
    private Task? _eventPump;
    private CancellationTokenSource? _sessionCts;
    private bool _disposed;

    public CppTraderOrderGateway(
        ICppTraderHostManager hostManager,
        ICppTraderSymbolMapper symbolMapper,
        ICppTraderExecutionTranslator executionTranslator,
        ICppTraderSnapshotTranslator snapshotTranslator,
        CppTraderLiveFeedAdapter feedAdapter,
        ICppTraderSessionDiagnosticsService sessionDiagnostics,
        IOptionsMonitor<CppTraderOptions> optionsMonitor,
        ILogger<CppTraderOrderGateway> logger)
    {
        _hostManager = hostManager;
        _symbolMapper = symbolMapper;
        _executionTranslator = executionTranslator;
        _snapshotTranslator = snapshotTranslator;
        _feedAdapter = feedAdapter;
        _sessionDiagnostics = sessionDiagnostics;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public string BrokerName => "CppTrader";

    public GatewayExecutionMode Mode => GatewayExecutionMode.Simulation;

    public OrderGatewayCapabilities Capabilities { get; } = new(
        SupportedOrderTypes: new HashSet<ExecutionOrderType>
        {
            ExecutionOrderType.Market,
            ExecutionOrderType.Limit,
            ExecutionOrderType.StopMarket,
            ExecutionOrderType.StopLimit
        },
        SupportedTimeInForce: new HashSet<ExecutionTimeInForce>
        {
            ExecutionTimeInForce.Day,
            ExecutionTimeInForce.GoodTilCancelled,
            ExecutionTimeInForce.ImmediateOrCancel,
            ExecutionTimeInForce.FillOrKill
        },
        SupportedExecutionModes: new HashSet<GatewayExecutionMode>
        {
            GatewayExecutionMode.Simulation,
            GatewayExecutionMode.Paper
        },
        SupportsOrderModification: false,
        SupportsPartialFills: true,
        ProviderExtensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["engine"] = "cpptrader",
            ["transport"] = "length-prefixed-json"
        });

    public Task<OrderValidationResult> ValidateOrderAsync(ExecutionOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled || !options.Features.ExecutionEnabled)
            return Task.FromResult(new OrderValidationResult(false, "CppTrader execution is disabled."));

        if (request.Metadata is { Count: > 0 })
            return Task.FromResult(new OrderValidationResult(false, "CppTrader execution does not yet support provider-specific metadata."));

        try
        {
            _ = _symbolMapper.GetSymbol(request.Symbol);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OrderValidationResult(false, ex.Message));
        }

        if (request.Quantity <= 0)
            return Task.FromResult(new OrderValidationResult(false, "Order quantity must be greater than zero."));

        if (request.Type is ExecutionOrderType.Limit or ExecutionOrderType.StopLimit && request.LimitPrice is not > 0)
            return Task.FromResult(new OrderValidationResult(false, "Limit and stop-limit orders require a positive limit price."));

        if (request.Type is ExecutionOrderType.StopMarket or ExecutionOrderType.StopLimit && request.StopPrice is not > 0)
            return Task.FromResult(new OrderValidationResult(false, "Stop and stop-limit orders require a positive stop price."));

        try
        {
            _symbolMapper.ConvertQuantityToNanos(request.Symbol, request.Quantity);
            if (request.LimitPrice.HasValue)
                _symbolMapper.ConvertPriceToNanos(request.Symbol, request.LimitPrice.Value);
            if (request.StopPrice.HasValue)
                _symbolMapper.ConvertPriceToNanos(request.Symbol, request.StopPrice.Value);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OrderValidationResult(false, ex.Message));
        }

        return Task.FromResult(new OrderValidationResult(true));
    }

    public async Task<OrderAcknowledgement> SubmitAsync(ExecutionOrderRequest request, CancellationToken ct = default)
    {
        var validation = await ValidateOrderAsync(request, ct).ConfigureAwait(false);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Reason ?? "CppTrader order validation failed.");

        var session = await EnsureSessionAsync(ct).ConfigureAwait(false);
        await EnsureSymbolRegisteredAsync(session, request.Symbol, ct).ConfigureAwait(false);

        var orderId = request.ClientOrderId ?? $"cpptrader-{Guid.NewGuid():N}";
        var response = await session.SubmitOrderAsync(
            new SubmitOrderRequest(
                OrderId: orderId,
                ClientOrderId: orderId,
                Symbol: request.Symbol,
                Side: request.Side == ExecutionOrderSide.Buy ? "buy" : "sell",
                OrderType: request.Type switch
                {
                    ExecutionOrderType.Market => "market",
                    ExecutionOrderType.Limit => "limit",
                    ExecutionOrderType.StopMarket => "stop",
                    ExecutionOrderType.StopLimit => "stop-limit",
                    _ => throw new ArgumentOutOfRangeException(nameof(request.Type), request.Type, null)
                },
                TimeInForce: request.TimeInForce switch
                {
                    ExecutionTimeInForce.Day => "day",
                    ExecutionTimeInForce.GoodTilCancelled => "gtc",
                    ExecutionTimeInForce.ImmediateOrCancel => "ioc",
                    ExecutionTimeInForce.FillOrKill => "fok",
                    _ => throw new ArgumentOutOfRangeException(nameof(request.TimeInForce), request.TimeInForce, null)
                },
                QuantityNanos: _symbolMapper.ConvertQuantityToNanos(request.Symbol, request.Quantity),
                LimitPriceNanos: request.LimitPrice.HasValue ? _symbolMapper.ConvertPriceToNanos(request.Symbol, request.LimitPrice.Value) : null,
                StopPriceNanos: request.StopPrice.HasValue ? _symbolMapper.ConvertPriceToNanos(request.Symbol, request.StopPrice.Value) : null,
                IsDayOrder: request.TimeInForce == ExecutionTimeInForce.Day,
                request.Metadata),
            ct).ConfigureAwait(false);

        if (!response.Accepted)
            _hostManager.RecordExecutionUpdate(rejected: true);

        return _executionTranslator.ToAcknowledgement(response);
    }

    public async Task<bool> CancelAsync(string orderId, CancellationToken ct = default)
    {
        var session = await EnsureSessionAsync(ct).ConfigureAwait(false);
        var response = await session.CancelOrderAsync(new CancelOrderRequest(orderId), ct).ConfigureAwait(false);
        return response.Accepted;
    }

    public IAsyncEnumerable<OrderStatusUpdate> StreamOrderUpdatesAsync(CancellationToken ct = default) =>
        _updates.Reader.ReadAllAsync(ct);

    private async Task<ICppTraderSessionClient> EnsureSessionAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_session is not null)
                return _session;
        }

        var created = await _hostManager.CreateSessionAsync(CppTraderSessionKind.Execution, "meridian-execution", ct).ConfigureAwait(false);
        var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var eventPump = Task.Run(() => PumpEventsAsync(created, sessionCts.Token), sessionCts.Token);

        lock (_gate)
        {
            if (_session is null)
            {
                _session = created;
                _sessionCts = sessionCts;
                _eventPump = eventPump;
                _sessionDiagnostics.TrackSession(
                    created.SessionId,
                    new CppTraderSessionDiagnostic(
                        created.SessionId,
                        created.SessionKind,
                        DateTimeOffset.UtcNow,
                        Symbols: Array.Empty<string>(),
                        LastHeartbeat: null,
                        LastFault: null));
            }
            else
            {
                sessionCts.Cancel();
            }

            return _session;
        }
    }

    private async Task EnsureSymbolRegisteredAsync(ICppTraderSessionClient session, string symbol, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_registeredSymbols.Contains(symbol))
                return;
        }

        var response = await session.RegisterSymbolAsync(_symbolMapper.ToRegisterRequest(symbol), ct).ConfigureAwait(false);
        if (!response.Registered)
            throw new InvalidOperationException(response.FailureReason ?? $"CppTrader could not register '{symbol}'.");

        lock (_gate)
        {
            _registeredSymbols.Add(symbol);
            _sessionDiagnostics.TrackSession(
                session.SessionId,
                new CppTraderSessionDiagnostic(
                    session.SessionId,
                    session.SessionKind,
                    DateTimeOffset.UtcNow,
                    Symbols: _registeredSymbols.ToArray(),
                    LastHeartbeat: DateTimeOffset.UtcNow,
                    LastFault: null));
        }
    }

    private async Task PumpEventsAsync(ICppTraderSessionClient session, CancellationToken ct)
    {
        try
        {
            await foreach (var envelope in session.ReadEventsAsync(ct).ConfigureAwait(false))
            {
                switch (envelope.MessageType)
                {
                    case CppTraderProtocolNames.Accepted:
                    {
                        var payload = envelope.Payload.Deserialize(CppTraderJsonContext.Default.AcceptedEvent);
                        if (payload is not null)
                            await _updates.Writer.WriteAsync(_executionTranslator.ToAcceptedStatus(payload), ct).ConfigureAwait(false);
                        break;
                    }
                    case CppTraderProtocolNames.Rejected:
                    {
                        var payload = envelope.Payload.Deserialize(CppTraderJsonContext.Default.RejectedEvent);
                        if (payload is not null)
                        {
                            _hostManager.RecordExecutionUpdate(rejected: true);
                            await _updates.Writer.WriteAsync(_executionTranslator.ToRejectedStatus(payload), ct).ConfigureAwait(false);
                        }
                        break;
                    }
                    case CppTraderProtocolNames.Execution:
                    {
                        var payload = envelope.Payload.Deserialize(CppTraderJsonContext.Default.ExecutionEvent);
                        if (payload is not null)
                        {
                            _hostManager.RecordExecutionUpdate(rejected: false);
                            await _updates.Writer.WriteAsync(_executionTranslator.ToExecutionStatus(payload), ct).ConfigureAwait(false);
                        }
                        break;
                    }
                    case CppTraderProtocolNames.Cancelled:
                    {
                        var payload = envelope.Payload.Deserialize(CppTraderJsonContext.Default.CancelledEvent);
                        if (payload is not null)
                            await _updates.Writer.WriteAsync(_executionTranslator.ToCancelledStatus(payload), ct).ConfigureAwait(false);
                        break;
                    }
                    case CppTraderProtocolNames.BookSnapshot:
                    {
                        var payload = envelope.Payload.Deserialize(CppTraderJsonContext.Default.BookSnapshotEvent);
                        if (payload is not null)
                        {
                            _hostManager.RecordSnapshot();
                            _feedAdapter.ApplySnapshot(_snapshotTranslator.Translate(payload));
                        }
                        break;
                    }
                    case CppTraderProtocolNames.TradePrint:
                    {
                        var payload = envelope.Payload.Deserialize(CppTraderJsonContext.Default.TradePrintEvent);
                        if (payload is not null)
                        {
                            _feedAdapter.ApplyTrade(new Trade(
                                payload.Timestamp,
                                payload.Symbol,
                                payload.Price,
                                payload.Size,
                                payload.Aggressor,
                                payload.SequenceNumber,
                                StreamId: "CPPTRADER",
                                payload.Venue));
                        }
                        break;
                    }
                    case CppTraderProtocolNames.HeartbeatResponse:
                        _hostManager.RecordHeartbeat();
                        break;
                    case CppTraderProtocolNames.Fault:
                    {
                        var payload = envelope.Payload.Deserialize(CppTraderJsonContext.Default.FaultEvent);
                        if (payload is not null)
                            _hostManager.RecordFault(payload.Message);
                        break;
                    }
                    case CppTraderProtocolNames.SessionClosed:
                    {
                        var payload = envelope.Payload.Deserialize(CppTraderJsonContext.Default.SessionClosedEvent);
                        _hostManager.RecordFault(payload?.Reason ?? "CppTrader session closed unexpectedly.");
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _hostManager.RecordFault(ex.Message);
            _logger.LogError(ex, "CppTrader execution event pump failed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _updates.Writer.TryComplete();

        if (_sessionCts is not null)
            await _sessionCts.CancelAsync().ConfigureAwait(false);

        if (_eventPump is not null)
        {
            try
            {
                await _eventPump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_session is not null)
        {
            _sessionDiagnostics.RemoveSession(_session.SessionId);
            await _session.DisposeAsync().ConfigureAwait(false);
        }

        _sessionCts?.Dispose();
    }
}
