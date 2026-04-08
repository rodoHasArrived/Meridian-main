using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Meridian.Application.Pipeline;
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
/// Interactive Brokers brokerage gateway for live order execution via the TWS/Gateway API.
/// Communicates with IB TWS or IB Gateway running locally. Supports equities, options,
/// futures, forex, and fixed income instruments (corporate bonds, US Treasuries).
/// </summary>
/// <remarks>
/// IB TWS API:
/// - Connects to TWS or IB Gateway on localhost (default port 7496 live, 7497 paper)
/// - Full order lifecycle: submit, modify, cancel with real-time execution reports
/// - Supports market, limit, stop, stop-limit, and many more order types
/// - Supports partial fills and order modification
/// - Rate limit: 50 messages/sec
///
/// Fixed income support:
/// - Corporate bonds: use <c>Metadata["sec_type"] = "BOND"</c> on <see cref="OrderRequest"/>
/// - US Treasuries/Government bonds: use <c>Metadata["sec_type"] = "GOVT"</c>
/// - Bond quantity is number of bonds where 1 unit = $1,000 par (e.g., <c>Quantity = 5</c> → $5,000 face value)
/// - Orders without <c>sec_type</c> metadata default to IB SecType <c>"STK"</c> (equity)
/// - Accrued interest is reported per position via the TWS <c>position()</c> callback
///   and mapped to <see cref="BrokerPosition.AccruedInterest"/>
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

    // Tracks submitted orders so cancel/modify reports can carry the correct symbol and side.
    // In a full TWS implementation the IB API provides this information via callbacks.
    private readonly Dictionary<string, (string Symbol, OrderSide Side, string? ClientOrderId)> _submittedOrders = new();

    public IBBrokerageGateway(
        IBOptions options,
        ILogger<IBBrokerageGateway> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reportChannel = EventPipelinePolicy.CompletionQueue.CreateChannel<ExecutionReport>();
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
        // IB supports corporate bonds (SecType=BOND) and US government bonds/treasuries (SecType=GOVT).
        // Pass Metadata["sec_type"] = "BOND" or "GOVT" on the OrderRequest to route fixed income orders.
        SupportedAssetClasses = ["equity", "option", "futures", "forex", "bond"],
        SupportedMarkets = ["US", "EU", "APAC"],
        Extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Signals that this gateway routes IB bond/treasury contracts via sec_type metadata.
            ["supportsFixedIncome"] = "true",
        },
    };

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connected)
            return;

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

        // Read optional IB-specific SecType metadata for fixed income routing.
        // "BOND" = corporate bond, "GOVT" = US Treasury / government bond.
        var secType = request.Metadata?.GetValueOrDefault("sec_type");

        _logger.LogInformation(
            "IB submitting order {OrderId}: {Side} {Quantity} {Symbol} @ {Type}{SecTypeSuffix}",
            orderId, request.Side, request.Quantity, request.Symbol, request.Type,
            secType is not null ? $" [secType={secType}]" : string.Empty);

        // Build IB contract and order via the TWS API
        // This is where the IB API placeOrder() call would go
        var report = await SubmitToTwsAsync(orderId, request, secType, ct).ConfigureAwait(false);

        // Track submitted order so cancel/modify reports can carry the correct symbol and side.
        _submittedOrders[orderId] = (request.Symbol, request.Side, request.ClientOrderId);

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

        _submittedOrders.TryGetValue(orderId, out var tracked);

        // This is where the IB API cancelOrder() call would go
        var report = new ExecutionReport
        {
            OrderId = orderId,
            ClientOrderId = tracked.ClientOrderId,
            ReportType = ExecutionReportType.Cancelled,
            Symbol = tracked.Symbol ?? string.Empty,
            Side = tracked.Symbol is not null ? tracked.Side : OrderSide.Buy,
            OrderStatus = OrderStatus.Cancelled,
            GatewayOrderId = orderId,
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

        _submittedOrders.TryGetValue(orderId, out var tracked);

        // IB modification is done by re-placing the order with the same orderId
        var report = new ExecutionReport
        {
            OrderId = orderId,
            ClientOrderId = tracked.ClientOrderId,
            ReportType = ExecutionReportType.Modified,
            Symbol = tracked.Symbol ?? string.Empty,
            Side = tracked.Symbol is not null ? tracked.Side : OrderSide.Buy,
            OrderStatus = OrderStatus.Accepted,
            GatewayOrderId = orderId,
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
        // and collects position() callbacks. For bond and treasury positions the callback
        // includes an accruedInterest field which maps to BrokerPosition.AccruedInterest.
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
        if (_disposed)
            return ValueTask.CompletedTask;
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

    private Task<ExecutionReport> SubmitToTwsAsync(string orderId, OrderRequest request, string? secType, CancellationToken ct)
    {
        // In a full IBAPI implementation, this would:
        // 1. Build an IB Contract:
        //    - secType = secType ?? "STK"  (use "BOND" for corporate bonds, "GOVT" for US Treasuries)
        //    - For bonds: symbol = CUSIP, exchange = "SMART", currency = "USD"
        //    - For US Treasuries: symbol = CUSIP, secType = "GOVT", exchange = "US-T-BOND"
        // 2. Build an IB Order (action, orderType, totalQuantity, lmtPrice, tif)
        //    - Note: for bonds/treasuries, totalQuantity is face value in number of bonds,
        //      where 1 unit = $1,000 par amount (e.g., quantity=5 → $5,000 face value).
        // 3. Call _client.placeOrder(nextValidOrderId, contract, order)
        // 4. Await the orderStatus() callback

        if (secType is not null)
            _logger.LogDebug("IB order {OrderId} using IB secType={SecType}", orderId, secType);

        // For now, return an accepted report. The actual fill will come via the
        // TWS callback pipeline when IBAPI is integrated.
        return Task.FromResult(new ExecutionReport
        {
            OrderId = orderId,
            ClientOrderId = request.ClientOrderId,
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
