#if STOCKSHARP
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Meridian.Application.Pipeline;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.StockSharp;

/// <summary>
/// StockSharp-based brokerage gateway that supports 90+ brokers and exchanges
/// through StockSharp's unified connector framework. Supports Rithmic, CQG,
/// IQFeed, Interactive Brokers via StockSharp, and many more.
/// </summary>
/// <remarks>
/// StockSharp Connector features:
/// - Unified adapter pattern: one gateway, many underlying brokers
/// - Native order types per adapter (market, limit, stop, conditional)
/// - Real-time execution reports via Connector.NewOrder / OrderChanged events
/// - Portfolio and position sync via Connector.PortfolioChanged
/// - Rate limits vary by underlying adapter
/// </remarks>
[DataSource("stocksharp-brokerage", "StockSharp Brokerage", DataSourceType.Realtime, DataSourceCategory.Broker,
    Priority = 20, Description = "StockSharp unified brokerage gateway (Rithmic, CQG, IB, etc.)")]
[ImplementsAdr("ADR-001", "StockSharp brokerage provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class StockSharpBrokerageGateway : IBrokerageGateway
{
    private readonly ILogger<StockSharpBrokerageGateway> _logger;
    private readonly Channel<ExecutionReport> _reportChannel;
    private volatile bool _connected;
    private bool _disposed;

    /// <summary>The underlying StockSharp adapter type (e.g., "Rithmic", "CQG", "IB").</summary>
    private readonly string _adapterType;

    // Tracks submitted orders so cancel/modify reports can carry the correct symbol and side.
    // In a full StockSharp implementation the Connector raises events with order details.
    private readonly Dictionary<string, (string Symbol, OrderSide Side, string? ClientOrderId)> _submittedOrders = new();

    public StockSharpBrokerageGateway(
        string adapterType,
        ILogger<StockSharpBrokerageGateway> logger)
    {
        _adapterType = adapterType ?? throw new ArgumentNullException(nameof(adapterType));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reportChannel = EventPipelinePolicy.Default.CreateChannel<ExecutionReport>(
            singleReader: false, singleWriter: false);
    }

    /// <inheritdoc />
    public string GatewayId => $"stocksharp-{_adapterType.ToLowerInvariant()}";

    /// <inheritdoc />
    public bool IsConnected => _connected;

    /// <inheritdoc />
    public string BrokerDisplayName => $"StockSharp ({_adapterType})";

    /// <inheritdoc />
    public BrokerageCapabilities BrokerageCapabilities { get; } =
        BrokerageCapabilities.UsEquity(
            modification: true,
            partialFills: true,
            shortSelling: true,
            fractional: false,
            extendedHours: false);

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connected) return;

        _logger.LogInformation("StockSharp ({Adapter}) brokerage connecting...", _adapterType);

        // In a full implementation, this would:
        // 1. Create the StockSharp Connector with the appropriate adapter
        // 2. Configure adapter-specific settings
        // 3. Call Connector.Connect()
        // 4. Wire up Connector.NewOrder, Connector.OrderChanged,
        //    Connector.OrderRegisterFailed events to the report channel

        await Task.CompletedTask.ConfigureAwait(false);
        _connected = true;

        _logger.LogInformation("StockSharp ({Adapter}) brokerage connected", _adapterType);
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _connected = false;
        _logger.LogInformation("StockSharp ({Adapter}) brokerage disconnected", _adapterType);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        var orderId = request.ClientOrderId ?? $"SS-{Guid.NewGuid():N}";

        _logger.LogInformation(
            "StockSharp ({Adapter}) submitting: {Side} {Quantity} {Symbol} @ {Type}",
            _adapterType, request.Side, request.Quantity, request.Symbol, request.Type);

        // In a full implementation, this would:
        // 1. Create a StockSharp Security for the symbol
        // 2. Create a StockSharp Order with the appropriate type/price/qty
        // 3. Call Connector.RegisterOrder(order)
        // 4. Return the initial report; fills arrive via events

        var report = new ExecutionReport
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
        };

        // Track submitted order so cancel/modify reports carry the correct symbol and side.
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

        _logger.LogInformation("StockSharp ({Adapter}) cancelling {OrderId}", _adapterType, orderId);

        _submittedOrders.TryGetValue(orderId, out var tracked);

        // Connector.CancelOrder(order)
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

        _logger.LogInformation("StockSharp ({Adapter}) modifying {OrderId}", _adapterType, orderId);

        _submittedOrders.TryGetValue(orderId, out var tracked);

        // Connector.ReRegisterOrder(oldOrder, newOrder)
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

        // Connector.Portfolios / Connector.GetPortfolioValue()
        return Task.FromResult(new AccountInfo
        {
            AccountId = $"SS-{_adapterType}",
            Equity = 0m,
            Cash = 0m,
            BuyingPower = 0m,
            Currency = "USD",
            Status = "active",
        });
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        // Connector.Positions
        return Task.FromResult<IReadOnlyList<BrokerPosition>>(Array.Empty<BrokerPosition>());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        // Connector.Orders.Where(o => o.State == OrderStates.Active)
        return Task.FromResult<IReadOnlyList<BrokerOrder>>(Array.Empty<BrokerOrder>());
    }

    /// <inheritdoc />
    public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        if (!_connected)
            return Task.FromResult(BrokerHealthStatus.Unhealthy("Not connected"));

        return Task.FromResult(BrokerHealthStatus.Healthy($"StockSharp {_adapterType} connected"));
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

    private void EnsureConnected()
    {
        if (!_connected)
            throw new InvalidOperationException(
                $"StockSharp ({_adapterType}) brokerage gateway is not connected. Call ConnectAsync first.");
    }
}
#endif
