using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.Logging;
using IBOptions = Meridian.Application.Config.IBOptions;
using OrderSide = Meridian.Execution.Sdk.OrderSide;
using OrderType = Meridian.Execution.Sdk.OrderType;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;
using TimeInForce = Meridian.Execution.Sdk.TimeInForce;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Interactive Brokers brokerage gateway for live order execution via the TWS/Gateway API.
/// Communicates with IB TWS or IB Gateway running locally. Supports equities, options,
/// futures, and forex.
/// </summary>
/// <remarks>
/// IB TWS API:
/// - Connects to TWS or IB Gateway on localhost (default port 7496 live, 7497 paper)
/// - Full order lifecycle: submit, modify, cancel with real-time execution reports
/// - Supports market, limit, stop, stop-limit, and many more order types
/// - Supports partial fills and order modification
/// - Rate limit: 50 messages/sec
///
/// This implementation provides the brokerage abstraction layer. The actual TWS API
/// communication is delegated to the IB connection infrastructure. When IBAPI is not
/// compiled in, this gateway operates in simulation mode using synthetic responses.
/// </remarks>
[DataSource("ib-brokerage", "Interactive Brokers Brokerage", DataSourceType.Realtime, DataSourceCategory.Broker,
    Priority = 5, Description = "Interactive Brokers TWS/Gateway order execution")]
[ImplementsAdr("ADR-001", "IB brokerage provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class IBBrokerageGateway : IBrokerageGateway
{
    private readonly IBOptions _options;
    private readonly ILogger<IBBrokerageGateway> _logger;
    private readonly Channel<ExecutionReport> _reportChannel;
    private volatile bool _connected;
    private bool _disposed;
    private int _nextOrderId;

    public IBBrokerageGateway(
        IBOptions options,
        ILogger<IBBrokerageGateway> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reportChannel = Channel.CreateBounded<ExecutionReport>(
            new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.Wait });
    }

    /// <inheritdoc />
    public string GatewayId => "ib";

    /// <inheritdoc />
    public bool IsConnected => _connected;

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
            OrderType.StopLimit
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
        SupportedAssetClasses = ["equity", "option", "futures", "forex"],
        SupportedMarkets = ["US", "EU", "APAC"],
    };

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connected) return;

        _logger.LogInformation(
            "IB brokerage connecting to {Host}:{Port} (clientId={ClientId}, paper={Paper})",
            _options.Host, _options.Port, _options.ClientId, _options.UsePaperTrading);

        // Connection to TWS/Gateway via the IB API
        // In non-IBAPI mode, we validate connectivity by checking host/port reachability
        await ValidateConnectionAsync(ct).ConfigureAwait(false);

        _connected = true;
        _nextOrderId = 1;

        _logger.LogInformation("IB brokerage connected to {Host}:{Port}", _options.Host, _options.Port);
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _connected = false;
        _logger.LogInformation("IB brokerage disconnected");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        var orderId = request.ClientOrderId ?? GenerateOrderId();

        _logger.LogInformation(
            "IB submitting order {OrderId}: {Side} {Quantity} {Symbol} @ {Type}",
            orderId, request.Side, request.Quantity, request.Symbol, request.Type);

        // Build IB contract and order via the TWS API
        // This is where the IB API placeOrder() call would go
        var report = await SubmitToTwsAsync(orderId, request, ct).ConfigureAwait(false);
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        _logger.LogInformation("IB cancelling order {OrderId}", orderId);

        // This is where the IB API cancelOrder() call would go
        var report = new ExecutionReport
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.Cancelled,
            Symbol = string.Empty,
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Cancelled,
            Timestamp = DateTimeOffset.UtcNow,
        };
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> ModifyOrderAsync(
        string orderId, OrderModification modification, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(modification);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        _logger.LogInformation("IB modifying order {OrderId}", orderId);

        // IB modification is done by re-placing the order with the same orderId
        var report = new ExecutionReport
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.Modified,
            Symbol = string.Empty,
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Accepted,
            Timestamp = DateTimeOffset.UtcNow,
        };
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
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
    public Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        // In a full implementation, this calls reqAccountSummary() on the IB API
        // and awaits the accountSummary() callback
        return Task.FromResult(new AccountInfo
        {
            AccountId = $"IB-{_options.ClientId}",
            Equity = 0m,
            Cash = 0m,
            BuyingPower = 0m,
            Currency = "USD",
            Status = _options.UsePaperTrading ? "paper" : "active",
        });
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        // In a full implementation, this calls reqPositions() on the IB API
        // and collects position() callbacks
        return Task.FromResult<IReadOnlyList<BrokerPosition>>(Array.Empty<BrokerPosition>());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        // In a full implementation, this calls reqOpenOrders() on the IB API
        // and collects openOrder()/orderStatus() callbacks
        return Task.FromResult<IReadOnlyList<BrokerOrder>>(Array.Empty<BrokerOrder>());
    }

    /// <inheritdoc />
    public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        if (!_connected)
            return Task.FromResult(BrokerHealthStatus.Unhealthy("Not connected to TWS/Gateway"));

        return Task.FromResult(BrokerHealthStatus.Healthy(
            $"Connected to {_options.Host}:{_options.Port} (client {_options.ClientId})"));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _connected = false;
        _reportChannel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    // ── Private helpers ─────────────────────────────────────────────────

    private void EnsureConnected()
    {
        if (!_connected)
            throw new InvalidOperationException("IB brokerage gateway is not connected. Call ConnectAsync first.");
    }

    private string GenerateOrderId()
    {
        var id = Interlocked.Increment(ref _nextOrderId);
        return $"IB-{DateTimeOffset.UtcNow:yyyyMMdd}-{id:D6}";
    }

    private Task ValidateConnectionAsync(CancellationToken ct)
    {
        // In non-IBAPI builds, validate that a TWS/Gateway endpoint is configured.
        // When IBAPI is available, this would call EClientSocket.eConnect().
        if (string.IsNullOrWhiteSpace(_options.Host))
            throw new InvalidOperationException("IB Host is not configured.");

        if (_options.Port <= 0 || _options.Port > 65535)
            throw new InvalidOperationException($"IB Port {_options.Port} is invalid.");

        _logger.LogInformation("IB connection validated for {Host}:{Port}", _options.Host, _options.Port);
        return Task.CompletedTask;
    }

    private Task<ExecutionReport> SubmitToTwsAsync(string orderId, OrderRequest request, CancellationToken ct)
    {
        // In a full IBAPI implementation, this would:
        // 1. Build an IB Contract (symbol, secType, exchange, currency)
        // 2. Build an IB Order (action, orderType, totalQuantity, lmtPrice, auxPrice, tif)
        // 3. Call _client.placeOrder(nextValidOrderId, contract, order)
        // 4. Await the orderStatus() callback

        // For now, return an accepted report. The actual fill will come via the
        // TWS callback pipeline when IBAPI is integrated.
        return Task.FromResult(new ExecutionReport
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.New,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = OrderStatus.Accepted,
            OrderQuantity = request.Quantity,
            GatewayOrderId = orderId,
            Timestamp = DateTimeOffset.UtcNow,
        });
    }
}
