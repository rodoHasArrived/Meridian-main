using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Meridian.Application.Pipeline;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.Logging;
using IBOptions = Meridian.Application.Config.IBOptions;
using OrderSide = Meridian.Execution.Sdk.OrderSide;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;
using OrderType = Meridian.Execution.Sdk.OrderType;
using TimeInForce = Meridian.Execution.Sdk.TimeInForce;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Interactive Brokers brokerage gateway for live or paper order execution via the TWS/Gateway API.
/// Uses the native IB socket path when the official vendor SDK is available.
/// </summary>
[DataSource("ib-brokerage", "Interactive Brokers Brokerage", DataSourceType.Realtime, DataSourceCategory.Broker,
    Priority = 5, Description = "Interactive Brokers TWS/Gateway order execution")]
[ImplementsAdr("ADR-001", "IB brokerage provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class IBBrokerageGateway : IBrokerageGateway
{
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromSeconds(10);

    private readonly IBOptions _options;
    private readonly ILogger<IBBrokerageGateway> _logger;
    private readonly IIBBrokerageClient _client;
    private readonly Channel<ExecutionReport> _reportChannel;
    private readonly ConcurrentDictionary<int, SubmittedOrderContext> _submittedOrders = new();
    private readonly ConcurrentDictionary<string, int> _gatewayOrderIdsByExternalId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, BrokerOrder> _openOrders = new();
    private readonly ConcurrentDictionary<int, PendingOrderOperation> _pendingOrderOperations = new();
    private readonly ConcurrentDictionary<int, AccountSummaryCollector> _accountSummaryRequests = new();
    private readonly SemaphoreSlim _openOrdersGate = new(1, 1);
    private readonly SemaphoreSlim _positionsGate = new(1, 1);

    private volatile bool _connected;
    private bool _disposed;
    private int _nextOrderId;
    private TaskCompletionSource<int>? _nextValidIdWaiter;
    private TaskCompletionSource<IReadOnlyList<BrokerOrder>>? _openOrdersSnapshotWaiter;
    private ConcurrentDictionary<int, BrokerOrder>? _openOrdersSnapshotBuffer;
    private TaskCompletionSource<IReadOnlyList<BrokerPosition>>? _positionsSnapshotWaiter;
    private ConcurrentDictionary<string, BrokerPosition>? _positionsSnapshotBuffer;

    public IBBrokerageGateway(
        IBOptions options,
        ILogger<IBBrokerageGateway> logger,
        IIBBrokerageClient? client = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = client ?? CreateBrokerageClient(options);
        _reportChannel = EventPipelinePolicy.CompletionQueue.CreateChannel<ExecutionReport>();

        _client.NextValidIdReceived += OnNextValidIdReceived;
        _client.OrderStatusReceived += OnOrderStatusReceived;
        _client.OpenOrderReceived += OnOpenOrderReceived;
        _client.OpenOrdersCompleted += OnOpenOrdersCompleted;
        _client.ExecutionDetailsReceived += OnExecutionDetailsReceived;
        _client.PositionReceived += OnPositionReceived;
        _client.PositionsCompleted += OnPositionsCompleted;
        _client.AccountSummaryReceived += OnAccountSummaryReceived;
        _client.AccountSummaryCompleted += OnAccountSummaryCompleted;
        _client.ErrorOccurred += OnErrorOccurred;
    }

    /// <inheritdoc />
    public string GatewayId => "ib";

    /// <inheritdoc />
    public bool IsConnected => _connected && _client.IsConnected;

    /// <inheritdoc />
    public string BrokerDisplayName => "Interactive Brokers";

    /// <inheritdoc />
    public BrokerageCapabilities BrokerageCapabilities { get; } = new()
    {
        SupportedOrderTypes = new HashSet<OrderType>
        {
            OrderType.Market,
            OrderType.Limit,
            OrderType.StopMarket,
            OrderType.StopLimit,
            OrderType.MarketOnOpen,
            OrderType.MarketOnClose,
            OrderType.LimitOnOpen,
            OrderType.LimitOnClose
        },
        SupportedTimeInForce = new HashSet<TimeInForce>
        {
            TimeInForce.Day,
            TimeInForce.GoodTilCancelled,
            TimeInForce.ImmediateOrCancel,
            TimeInForce.FillOrKill
        },
        SupportsOrderModification = true,
        SupportsPartialFills = true,
        SupportsShortSelling = true,
        SupportsFractionalShares = false,
        SupportsExtendedHours = true,
        SupportedAssetClasses = ["equity", "option", "futures", "forex", "bond"],
        SupportedMarkets = ["US", "EU", "APAC"],
        Extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["supportsFixedIncome"] = "true",
        },
    };

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsConnected)
            return;

        _logger.LogInformation(
            "IB brokerage connecting to {Host}:{Port} (clientId={ClientId}, paper={Paper})",
            _options.Host, _options.Port, _options.ClientId, _options.UsePaperTrading);

        await _client.ConnectAsync(ct).ConfigureAwait(false);
        await PrimeNextOrderIdAsync(ct).ConfigureAwait(false);

        _connected = true;

        _logger.LogInformation(
            "IB brokerage connected to {Host}:{Port} (nextValidId={NextOrderId})",
            _client.Host, _client.Port, _nextOrderId);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (!_connected && !_client.IsConnected)
            return;

        await _client.DisconnectAsync(ct).ConfigureAwait(false);
        _connected = false;

        _logger.LogInformation("IB brokerage disconnected");
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        var gatewayOrderId = ReserveGatewayOrderId();
        var meridianOrderId = request.ClientOrderId ?? $"IB-{gatewayOrderId}";
        var context = SubmittedOrderContext.FromRequest(gatewayOrderId, meridianOrderId, request);
        var pendingOperation = RegisterPendingOrderOperation(gatewayOrderId, PendingOrderOperationKind.Submit);

        _submittedOrders[gatewayOrderId] = context;
        _gatewayOrderIdsByExternalId[meridianOrderId] = gatewayOrderId;
        _gatewayOrderIdsByExternalId[gatewayOrderId.ToString()] = gatewayOrderId;

        try
        {
            await _client.PlaceOrderAsync(gatewayOrderId, request, ct).ConfigureAwait(false);
            return await AwaitPendingOrderOperationAsync(
                gatewayOrderId,
                pendingOperation,
                $"submit order {meridianOrderId}",
                ct).ConfigureAwait(false);
        }
        catch
        {
            _pendingOrderOperations.TryRemove(gatewayOrderId, out _);
            _submittedOrders.TryRemove(gatewayOrderId, out _);
            _gatewayOrderIdsByExternalId.TryRemove(meridianOrderId, out _);
            _gatewayOrderIdsByExternalId.TryRemove(gatewayOrderId.ToString(), out _);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        var (gatewayOrderId, context) = ResolveOrder(orderId);
        var pendingOperation = RegisterPendingOrderOperation(gatewayOrderId, PendingOrderOperationKind.Cancel);

        await _client.CancelOrderAsync(gatewayOrderId, ct).ConfigureAwait(false);
        return await AwaitPendingOrderOperationAsync(
            gatewayOrderId,
            pendingOperation,
            $"cancel order {context.MeridianOrderId}",
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> ModifyOrderAsync(
        string orderId,
        OrderModification modification,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(modification);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        var (gatewayOrderId, context) = ResolveOrder(orderId);
        var updatedRequest = context.ToModifiedRequest(modification);
        var updatedContext = SubmittedOrderContext.FromRequest(gatewayOrderId, context.MeridianOrderId, updatedRequest);
        var pendingOperation = RegisterPendingOrderOperation(gatewayOrderId, PendingOrderOperationKind.Modify);

        _submittedOrders[gatewayOrderId] = updatedContext;

        await _client.PlaceOrderAsync(gatewayOrderId, updatedRequest, ct).ConfigureAwait(false);
        return await AwaitPendingOrderOperationAsync(
            gatewayOrderId,
            pendingOperation,
            $"modify order {context.MeridianOrderId}",
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var report in _reportChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return report;
        }
    }

    /// <inheritdoc />
    public async Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        var requestId = _client.RequestAccountSummary();
        var collector = new AccountSummaryCollector(_options.UsePaperTrading);
        _accountSummaryRequests[requestId] = collector;

        try
        {
            return await collector.Completion.Task.WaitAsync(CallbackTimeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _accountSummaryRequests.TryRemove(requestId, out _);
            _client.CancelAccountSummary(requestId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        await _positionsGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _positionsSnapshotBuffer = new ConcurrentDictionary<string, BrokerPosition>(StringComparer.OrdinalIgnoreCase);
            _positionsSnapshotWaiter = new TaskCompletionSource<IReadOnlyList<BrokerPosition>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _client.RequestPositions();
            return await _positionsSnapshotWaiter.Task.WaitAsync(CallbackTimeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _client.CancelPositions();
            _positionsSnapshotWaiter = null;
            _positionsSnapshotBuffer = null;
            _positionsGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        await _openOrdersGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _openOrdersSnapshotBuffer = new ConcurrentDictionary<int, BrokerOrder>();
            _openOrdersSnapshotWaiter = new TaskCompletionSource<IReadOnlyList<BrokerOrder>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _client.RequestOpenOrders();
            return await _openOrdersSnapshotWaiter.Task.WaitAsync(CallbackTimeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _openOrdersSnapshotWaiter = null;
            _openOrdersSnapshotBuffer = null;
            _openOrdersGate.Release();
        }
    }

    /// <inheritdoc />
    public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        if (_client is UnsupportedIBBrokerageClient unsupportedClient)
        {
            return Task.FromResult(BrokerHealthStatus.Unhealthy(unsupportedClient.GuidanceMessage));
        }

        if (!IsConnected)
        {
            return Task.FromResult(BrokerHealthStatus.Unhealthy(
                $"IB brokerage is not connected to {_options.Host}:{_options.Port}."));
        }

        return Task.FromResult(BrokerHealthStatus.Healthy(
            $"Connected to {_client.Host}:{_client.Port} (client {_client.ClientId}, {(_options.UsePaperTrading ? "paper" : "live")})"));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        _client.NextValidIdReceived -= OnNextValidIdReceived;
        _client.OrderStatusReceived -= OnOrderStatusReceived;
        _client.OpenOrderReceived -= OnOpenOrderReceived;
        _client.OpenOrdersCompleted -= OnOpenOrdersCompleted;
        _client.ExecutionDetailsReceived -= OnExecutionDetailsReceived;
        _client.PositionReceived -= OnPositionReceived;
        _client.PositionsCompleted -= OnPositionsCompleted;
        _client.AccountSummaryReceived -= OnAccountSummaryReceived;
        _client.AccountSummaryCompleted -= OnAccountSummaryCompleted;
        _client.ErrorOccurred -= OnErrorOccurred;

        try
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "IB brokerage disposal disconnect cleanup skipped");
        }

        _openOrdersGate.Dispose();
        _positionsGate.Dispose();
        _reportChannel.Writer.TryComplete();
        _client.Dispose();
    }

    private static IIBBrokerageClient CreateBrokerageClient(IBOptions options)
    {
#if IBAPI
        var publisher = new NullMarketEventPublisher();
        var router = new IBCallbackRouter(
            new MarketDepthCollector(publisher, requireExplicitSubscription: false),
            new TradeDataCollector(publisher, null));

        return new EnhancedIBConnectionManager(
            router,
            options.Host,
            options.Port,
            options.ClientId);
#else
        return new UnsupportedIBBrokerageClient(options);
#endif
    }

    private async Task PrimeNextOrderIdAsync(CancellationToken ct)
    {
        var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousWaiter = Interlocked.Exchange(ref _nextValidIdWaiter, waiter);
        previousWaiter?.TrySetCanceled();

        _client.RequestNextValidId();

        try
        {
            _nextOrderId = await waiter.Task.WaitAsync(CallbackTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"IB did not send nextValidId within {CallbackTimeout.TotalSeconds:0} seconds after connect.",
                ex);
        }
        finally
        {
            Interlocked.CompareExchange(ref _nextValidIdWaiter, null, waiter);
        }
    }

    private int ReserveGatewayOrderId()
    {
        var reserved = Interlocked.Increment(ref _nextOrderId);
        return reserved - 1;
    }

    private PendingOrderOperation RegisterPendingOrderOperation(int gatewayOrderId, PendingOrderOperationKind kind)
    {
        var operation = new PendingOrderOperation(
            kind,
            new TaskCompletionSource<ExecutionReport>(TaskCreationOptions.RunContinuationsAsynchronously));

        _pendingOrderOperations[gatewayOrderId] = operation;
        return operation;
    }

    private async Task<ExecutionReport> AwaitPendingOrderOperationAsync(
        int gatewayOrderId,
        PendingOrderOperation operation,
        string description,
        CancellationToken ct)
    {
        try
        {
            return await operation.Completion.Task.WaitAsync(CallbackTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            _pendingOrderOperations.TryRemove(gatewayOrderId, out _);
            throw new TimeoutException(
                $"IB did not acknowledge the request to {description} within {CallbackTimeout.TotalSeconds:0} seconds.",
                ex);
        }
    }

    private (int GatewayOrderId, SubmittedOrderContext Context) ResolveOrder(string orderId)
    {
        if (_gatewayOrderIdsByExternalId.TryGetValue(orderId, out var gatewayOrderId) &&
            _submittedOrders.TryGetValue(gatewayOrderId, out var context))
        {
            return (gatewayOrderId, context);
        }

        if (int.TryParse(orderId, out gatewayOrderId) &&
            _submittedOrders.TryGetValue(gatewayOrderId, out context))
        {
            return (gatewayOrderId, context);
        }

        throw new InvalidOperationException(
            $"Order '{orderId}' is unknown to the IB brokerage gateway. Submit the order before modifying or cancelling it.");
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException(
                "IB brokerage gateway is not connected. Call ConnectAsync first.");
        }
    }

    private void OnNextValidIdReceived(object? sender, int nextValidId)
    {
        _nextOrderId = nextValidId;
        Interlocked.Exchange(ref _nextValidIdWaiter, null)?.TrySetResult(nextValidId);
    }

    private void OnAccountSummaryReceived(object? sender, IBAccountSummaryUpdate update)
    {
        if (_accountSummaryRequests.TryGetValue(update.RequestId, out var collector))
        {
            collector.Apply(update);
        }
    }

    private void OnAccountSummaryCompleted(object? sender, int requestId)
    {
        if (_accountSummaryRequests.TryGetValue(requestId, out var collector))
        {
            collector.TryComplete();
        }
    }

    private void OnPositionReceived(object? sender, IBPositionUpdate update)
    {
        var buffer = _positionsSnapshotBuffer;
        if (buffer is null || string.IsNullOrWhiteSpace(update.Symbol))
            return;

        var symbol = update.Symbol.Trim();
        var averageCost = (decimal)update.AverageCost;
        var marketValue = Math.Abs(update.Quantity) * averageCost;
        var accruedInterest = TryGetDecimal(update.Metadata, "accrued_interest");

        buffer[symbol] = new BrokerPosition
        {
            PositionId = $"{update.Account}:{symbol}",
            Symbol = symbol,
            Quantity = update.Quantity,
            AverageEntryPrice = averageCost,
            MarketPrice = averageCost,
            MarketValue = marketValue,
            UnrealizedPnl = 0m,
            AssetClass = MapAssetClass(update.SecurityType),
            Metadata = update.Metadata,
            AccruedInterest = accruedInterest
        };
    }

    private void OnPositionsCompleted(object? sender, EventArgs e)
    {
        _positionsSnapshotWaiter?.TrySetResult(
            _positionsSnapshotBuffer?.Values
                .OrderBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            ?? Array.Empty<BrokerPosition>());
    }

    private void OnOpenOrderReceived(object? sender, IBOpenOrderUpdate update)
    {
        var gatewayOrderId = update.OrderId;
        var context = _submittedOrders.TryGetValue(gatewayOrderId, out var tracked) ? tracked : null;
        var brokerOrder = new BrokerOrder
        {
            OrderId = gatewayOrderId.ToString(),
            ClientOrderId = context?.ClientOrderId ?? update.ClientOrderId,
            Symbol = context?.Symbol ?? update.Symbol,
            Side = context?.Side ?? ParseSide(update.Action),
            Type = context?.Type ?? ParseOrderType(update.OrderType),
            Quantity = context?.Quantity ?? update.Quantity,
            FilledQuantity = update.FilledQuantity,
            LimitPrice = update.LimitPrice is > 0 ? (decimal)update.LimitPrice.Value : context?.LimitPrice,
            StopPrice = update.StopPrice is > 0 ? (decimal)update.StopPrice.Value : context?.StopPrice,
            Status = MapOrderStatus(update.Status, update.FilledQuantity, Math.Max(update.Quantity - update.FilledQuantity, 0m), update.RejectReason),
            CreatedAt = context?.CreatedAt ?? update.ReceivedAt
        };

        _openOrders[gatewayOrderId] = brokerOrder;
        var openOrdersSnapshotBuffer = _openOrdersSnapshotBuffer;
        if (openOrdersSnapshotBuffer is not null)
            openOrdersSnapshotBuffer[gatewayOrderId] = brokerOrder;

        if (_pendingOrderOperations.TryGetValue(gatewayOrderId, out var pending) &&
            pending.Kind is PendingOrderOperationKind.Submit or PendingOrderOperationKind.Modify)
        {
            var reportType = brokerOrder.Status == OrderStatus.Rejected
                ? ExecutionReportType.Rejected
                : pending.Kind == PendingOrderOperationKind.Modify
                    ? ExecutionReportType.Modified
                    : ExecutionReportType.New;

            var report = BuildExecutionReport(
                gatewayOrderId,
                reportType,
                brokerOrder.Status,
                context,
                brokerOrder.Symbol,
                brokerOrder.Side,
                brokerOrder.Quantity,
                brokerOrder.FilledQuantity,
                update.LimitPrice is > 0 ? (decimal)update.LimitPrice.Value : null,
                update.RejectReason,
                update.Commission is > 0 ? (decimal)update.Commission.Value : null,
                update.ReceivedAt);

            PublishReport(report);
            CompletePendingOrderOperation(gatewayOrderId, report);
        }
    }

    private void OnOpenOrdersCompleted(object? sender, EventArgs e)
    {
        _openOrdersSnapshotWaiter?.TrySetResult(
            _openOrdersSnapshotBuffer?.Values
                .OrderBy(o => o.Symbol, StringComparer.OrdinalIgnoreCase)
                .ThenBy(o => o.OrderId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            ?? Array.Empty<BrokerOrder>());
    }

    private void OnOrderStatusReceived(object? sender, IBOrderStatusUpdate update)
    {
        var gatewayOrderId = update.OrderId;
        var context = _submittedOrders.TryGetValue(gatewayOrderId, out var tracked) ? tracked : null;
        var orderStatus = MapOrderStatus(update.Status, update.Filled, update.Remaining, null);

        if (_openOrders.TryGetValue(gatewayOrderId, out var openOrder))
        {
            var refreshed = openOrder with
            {
                FilledQuantity = update.Filled,
                Status = orderStatus,
            };

            if (orderStatus is OrderStatus.Cancelled or OrderStatus.Filled)
                _openOrders.TryRemove(gatewayOrderId, out _);
            else
                _openOrders[gatewayOrderId] = refreshed;

            var openOrdersSnapshotBuffer = _openOrdersSnapshotBuffer;
            if (openOrdersSnapshotBuffer is not null)
                openOrdersSnapshotBuffer[gatewayOrderId] = refreshed;
        }

        if (!_pendingOrderOperations.TryGetValue(gatewayOrderId, out var pending) &&
            orderStatus is not (OrderStatus.Cancelled or OrderStatus.Rejected))
        {
            return;
        }

        var reportType = pending?.Kind switch
        {
            PendingOrderOperationKind.Cancel => orderStatus == OrderStatus.Rejected
                ? ExecutionReportType.Rejected
                : ExecutionReportType.Cancelled,
            PendingOrderOperationKind.Modify => orderStatus == OrderStatus.Rejected
                ? ExecutionReportType.Rejected
                : ExecutionReportType.Modified,
            _ when orderStatus == OrderStatus.Rejected => ExecutionReportType.Rejected,
            _ when orderStatus == OrderStatus.Cancelled => ExecutionReportType.Cancelled,
            _ => ExecutionReportType.New,
        };

        var report = BuildExecutionReport(
            gatewayOrderId,
            reportType,
            orderStatus,
            context,
            context?.Symbol ?? openOrder?.Symbol ?? gatewayOrderId.ToString(),
            context?.Side ?? openOrder?.Side ?? OrderSide.Buy,
            context?.Quantity ?? openOrder?.Quantity ?? update.Filled + update.Remaining,
            update.Filled,
            update.LastFillPrice > 0 ? (decimal)update.LastFillPrice : update.AverageFillPrice > 0 ? (decimal)update.AverageFillPrice : null,
            null,
            null,
            update.ReceivedAt);

        PublishReport(report);

        if (orderStatus is OrderStatus.Cancelled or OrderStatus.Rejected ||
            pending is not null)
        {
            CompletePendingOrderOperation(gatewayOrderId, report);
        }
    }

    private void OnExecutionDetailsReceived(object? sender, IBExecutionUpdate update)
    {
        var gatewayOrderId = update.OrderId;
        var context = _submittedOrders.TryGetValue(gatewayOrderId, out var tracked) ? tracked : null;
        var orderQuantity = context?.Quantity ?? update.CumulativeQuantity;
        var reportType = update.CumulativeQuantity >= orderQuantity
            ? ExecutionReportType.Fill
            : ExecutionReportType.PartialFill;
        var orderStatus = reportType == ExecutionReportType.Fill
            ? OrderStatus.Filled
            : OrderStatus.PartiallyFilled;

        var report = BuildExecutionReport(
            gatewayOrderId,
            reportType,
            orderStatus,
            context,
            context?.Symbol ?? update.Symbol,
            context?.Side ?? ParseExecutionSide(update.Side),
            orderQuantity,
            update.CumulativeQuantity,
            (decimal)update.Price,
            null,
            null,
            update.ExecutedAt);

        if (_openOrders.TryGetValue(gatewayOrderId, out var openOrder))
        {
            if (orderStatus == OrderStatus.Filled)
            {
                _openOrders.TryRemove(gatewayOrderId, out _);
            }
            else
            {
                _openOrders[gatewayOrderId] = openOrder with
                {
                    FilledQuantity = update.CumulativeQuantity,
                    Status = orderStatus
                };
            }
        }

        PublishReport(report);
    }

    private void OnErrorOccurred(object? sender, IBApiError error)
    {
        if (_accountSummaryRequests.TryRemove(error.RequestId, out var collector))
        {
            collector.Completion.TrySetException(new IBApiException(error.ErrorCode, error.ErrorMessage));
            return;
        }

        if (error.RequestId <= 0 || !_submittedOrders.TryGetValue(error.RequestId, out var context))
            return;

        var report = BuildExecutionReport(
            error.RequestId,
            ExecutionReportType.Rejected,
            OrderStatus.Rejected,
            context,
            context.Symbol,
            context.Side,
            context.Quantity,
            0m,
            null,
            error.ErrorMessage,
            null,
            DateTimeOffset.UtcNow);

        PublishReport(report);
        CompletePendingOrderOperation(error.RequestId, report);
    }

    private void PublishReport(ExecutionReport report)
    {
        if (!_reportChannel.Writer.TryWrite(report))
        {
            _ = _reportChannel.Writer.WriteAsync(report);
        }
    }

    private void CompletePendingOrderOperation(int gatewayOrderId, ExecutionReport report)
    {
        if (_pendingOrderOperations.TryRemove(gatewayOrderId, out var pending))
        {
            pending.Completion.TrySetResult(report);
        }
    }

    private static ExecutionReport BuildExecutionReport(
        int gatewayOrderId,
        ExecutionReportType reportType,
        OrderStatus orderStatus,
        SubmittedOrderContext? context,
        string symbol,
        OrderSide side,
        decimal quantity,
        decimal filledQuantity,
        decimal? fillPrice,
        string? rejectReason,
        decimal? commission,
        DateTimeOffset timestamp)
    {
        return new ExecutionReport
        {
            OrderId = context?.MeridianOrderId ?? gatewayOrderId.ToString(),
            ClientOrderId = context?.ClientOrderId,
            ReportType = reportType,
            Symbol = symbol,
            Side = side,
            OrderStatus = orderStatus,
            OrderQuantity = quantity,
            FilledQuantity = filledQuantity,
            FillPrice = fillPrice,
            Commission = commission,
            RejectReason = rejectReason,
            GatewayOrderId = gatewayOrderId.ToString(),
            Timestamp = timestamp,
        };
    }

    private static OrderStatus MapOrderStatus(string? status, decimal filled, decimal remaining, string? rejectReason)
    {
        var normalized = status?.Trim();

        if (!string.IsNullOrWhiteSpace(rejectReason))
            return OrderStatus.Rejected;

        if (string.Equals(normalized, "Filled", StringComparison.OrdinalIgnoreCase))
            return OrderStatus.Filled;

        if (string.Equals(normalized, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "ApiCancelled", StringComparison.OrdinalIgnoreCase))
            return OrderStatus.Cancelled;

        if (string.Equals(normalized, "PendingCancel", StringComparison.OrdinalIgnoreCase))
            return OrderStatus.PendingCancel;

        if (string.Equals(normalized, "Inactive", StringComparison.OrdinalIgnoreCase))
            return OrderStatus.Rejected;

        if (filled > 0 && remaining > 0)
            return OrderStatus.PartiallyFilled;

        if (string.Equals(normalized, "Submitted", StringComparison.OrdinalIgnoreCase))
            return OrderStatus.Accepted;

        if (string.Equals(normalized, "PreSubmitted", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "PendingSubmit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "ApiPending", StringComparison.OrdinalIgnoreCase))
            return OrderStatus.PendingNew;

        return filled > 0 ? OrderStatus.PartiallyFilled : OrderStatus.Accepted;
    }

    private static string MapAssetClass(string? securityType)
        => securityType?.ToUpperInvariant() switch
        {
            "OPT" => "option",
            "FUT" => "futures",
            "CASH" => "forex",
            "BOND" => "bond",
            "GOVT" => "bond",
            _ => "equity"
        };

    private static OrderSide ParseSide(string? action)
        => string.Equals(action, "SELL", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(action, "SLD", StringComparison.OrdinalIgnoreCase)
            ? OrderSide.Sell
            : OrderSide.Buy;

    private static OrderSide ParseExecutionSide(string? side)
        => string.Equals(side, "SLD", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(side, "SELL", StringComparison.OrdinalIgnoreCase)
            ? OrderSide.Sell
            : OrderSide.Buy;

    private static OrderType ParseOrderType(string? orderType)
        => orderType?.ToUpperInvariant() switch
        {
            "LMT" => OrderType.Limit,
            "STP" => OrderType.StopMarket,
            "STP LMT" => OrderType.StopLimit,
            _ => OrderType.Market
        };

    private static decimal? TryGetDecimal(IReadOnlyDictionary<string, string>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var raw))
            return null;

        return decimal.TryParse(raw, out var parsed)
            ? parsed
            : null;
    }

    private sealed record SubmittedOrderContext(
        int GatewayOrderId,
        string MeridianOrderId,
        string? ClientOrderId,
        string Symbol,
        OrderSide Side,
        OrderType Type,
        decimal Quantity,
        decimal? LimitPrice,
        decimal? StopPrice,
        TimeInForce TimeInForce,
        string? StrategyId,
        IReadOnlyDictionary<string, string>? Metadata,
        DateTimeOffset CreatedAt)
    {
        public static SubmittedOrderContext FromRequest(int gatewayOrderId, string meridianOrderId, OrderRequest request)
            => new(
                gatewayOrderId,
                meridianOrderId,
                request.ClientOrderId,
                request.Symbol,
                request.Side,
                request.Type,
                request.Quantity,
                request.LimitPrice,
                request.StopPrice,
                request.TimeInForce,
                request.StrategyId,
                request.Metadata,
                DateTimeOffset.UtcNow);

        public OrderRequest ToModifiedRequest(OrderModification modification)
            => new()
            {
                Symbol = Symbol,
                Side = Side,
                Type = Type,
                Quantity = modification.NewQuantity ?? Quantity,
                LimitPrice = modification.NewLimitPrice ?? LimitPrice,
                StopPrice = modification.NewStopPrice ?? StopPrice,
                TimeInForce = TimeInForce,
                ClientOrderId = ClientOrderId,
                StrategyId = StrategyId,
                Metadata = Metadata
            };
    }

    private sealed record PendingOrderOperation(
        PendingOrderOperationKind Kind,
        TaskCompletionSource<ExecutionReport> Completion);

    private enum PendingOrderOperationKind
    {
        Submit,
        Modify,
        Cancel
    }

    private sealed class AccountSummaryCollector
    {
        private string? _accountId;
        private decimal? _equity;
        private decimal? _cash;
        private decimal? _buyingPower;
        private string? _currency;
        private readonly bool _paper;

        public AccountSummaryCollector(bool paper)
        {
            _paper = paper;
            Completion = new TaskCompletionSource<AccountInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public TaskCompletionSource<AccountInfo> Completion { get; }

        public void Apply(IBAccountSummaryUpdate update)
        {
            _accountId ??= update.Account;
            _currency ??= string.Equals(update.Tag, "Currency", StringComparison.OrdinalIgnoreCase)
                ? update.Value
                : update.Currency;

            if (!decimal.TryParse(update.Value, out var numericValue))
                return;

            switch (update.Tag)
            {
                case "NetLiquidation":
                    _equity = numericValue;
                    break;
                case "TotalCashValue":
                    _cash = numericValue;
                    break;
                case "BuyingPower":
                    _buyingPower = numericValue;
                    break;
            }
        }

        public void TryComplete()
        {
            Completion.TrySetResult(new AccountInfo
            {
                AccountId = _accountId ?? "IB",
                Equity = _equity ?? 0m,
                Cash = _cash ?? 0m,
                BuyingPower = _buyingPower ?? 0m,
                Currency = _currency ?? "USD",
                Status = _paper ? "paper" : "active",
            });
        }
    }

    private sealed class UnsupportedIBBrokerageClient : IIBBrokerageClient
    {
        public UnsupportedIBBrokerageClient(IBOptions options)
        {
            Host = options.Host;
            Port = options.Port;
            ClientId = options.ClientId;
            GuidanceMessage = IBBuildGuidance.BuildRealProviderMessage("IBBrokerageGateway")
                + " Brokerage order routing is guidance-only until the official vendor runtime is enabled.";
        }

        public string Host { get; }
        public int Port { get; }
        public int ClientId { get; }
        public bool IsConnected => false;
        public string GuidanceMessage { get; }

        // Required by IIBBrokerageClient contract so unsupported-runtime guidance mode remains substitutable.
#pragma warning disable CS0067 // Event is never used
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
#pragma warning restore CS0067

        public Task ConnectAsync(CancellationToken ct = default)
            => Task.FromException(CreateException());

        public Task DisconnectAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public void RequestNextValidId() => throw CreateException();

        public Task PlaceOrderAsync(int orderId, OrderRequest request, CancellationToken ct = default)
            => Task.FromException(CreateException());

        public Task CancelOrderAsync(int orderId, CancellationToken ct = default)
            => Task.FromException(CreateException());

        public int RequestAccountSummary() => throw CreateException();

        public void CancelAccountSummary(int requestId) { }

        public void RequestPositions() => throw CreateException();

        public void CancelPositions() { }

        public void RequestOpenOrders() => throw CreateException();

        public void Dispose()
        {
        }

        private NotSupportedException CreateException() => new(GuidanceMessage);
    }

    private sealed class NullMarketEventPublisher : IMarketEventPublisher
    {
        public bool TryPublish(in MarketEvent evt) => true;
    }
}
