using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Meridian.Core.Pipeline;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.Logging;
using OrderSide = Meridian.Execution.Sdk.OrderSide;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;
using OrderType = Meridian.Execution.Sdk.OrderType;
using TimeInForce = Meridian.Execution.Sdk.TimeInForce;

namespace Meridian.Infrastructure.Adapters.Templates;

/// <summary>
/// Template brokerage gateway scaffold. Copy this file and fill in the TODOs
/// to implement a new brokerage provider.
///
/// Steps to implement a new brokerage:
/// 1. Copy this file to a new folder under Adapters/{BrokerName}/
/// 2. Rename the class to {BrokerName}BrokerageGateway
/// 3. Replace the template [DataSource] and [ImplementsAdr] metadata with broker-specific values
/// 4. Replace scaffold methods with broker-specific API calls
/// 5. Add JSON DTOs and a JsonSerializerContext for ADR-014 compliance
/// 6. Register in DI: services.AddBrokerageGateway&lt;{BrokerName}BrokerageGateway&gt;()
/// 7. Add configuration options record if needed
/// 8. Add tests under tests/Meridian.Tests/Brokerage/
/// </summary>
[DataSource("template-brokerage", "Template Brokerage", DataSourceType.Realtime, DataSourceCategory.Broker,
    Priority = 99, Description = "Deterministic brokerage gateway scaffold for provider implementations")]
[ImplementsAdr("ADR-001", "Template brokerage provider scaffold")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[Obsolete("TemplateBrokerageGateway is a scaffold/copy-target and must not be registered or used in production. " +
          "Copy it to Adapters/{BrokerName}/ and implement all TODO items before use.")]
public sealed class TemplateBrokerageGateway : IBrokerageGateway
{
    private readonly ILogger<TemplateBrokerageGateway> _logger;
    private readonly TemplateBrokerageGatewayOptions _options;
    private readonly Channel<ExecutionReport> _reportChannel;
    private readonly ConcurrentDictionary<string, BrokerOrder> _openOrders = new(StringComparer.Ordinal);
    private volatile bool _connected;
    private bool _disposed;

    // Tracks submitted orders so cancel/modify reports carry the correct symbol and side.
    private readonly ConcurrentDictionary<string, (string Symbol, OrderSide Side, string? ClientOrderId)> _submittedOrders = new();

    public TemplateBrokerageGateway(ILogger<TemplateBrokerageGateway> logger)
        : this(logger, new TemplateBrokerageGatewayOptions())
    {
    }

    public TemplateBrokerageGateway(
        ILogger<TemplateBrokerageGateway> logger,
        TemplateBrokerageGatewayOptions options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _reportChannel = EventPipelinePolicy.Default.CreateChannel<ExecutionReport>(
            singleReader: false, singleWriter: false);
    }

    public string GatewayId => _options.GatewayId;

    public bool IsConnected => _connected;

    public string BrokerDisplayName => _options.BrokerDisplayName;

    public BrokerageCapabilities BrokerageCapabilities => _options.Capabilities;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        if (_connected)
            return;

        ValidateTemplateConnection();

        _connected = true;
        _logger.LogInformation("{BrokerDisplayName} connected", BrokerDisplayName);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        _connected = false;
        _logger.LogInformation("Template broker disconnected");
        return Task.CompletedTask;
    }

    public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        EnsureConnected();

        var orderId = request.ClientOrderId ?? Guid.NewGuid().ToString("N");

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
        _openOrders[orderId] = CreateOpenOrder(orderId, request, report.Timestamp);

        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    public async Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        EnsureConnected();

        _submittedOrders.TryRemove(orderId, out var tracked);
        _openOrders.TryRemove(orderId, out var openOrder);
        var symbol = openOrder?.Symbol ?? tracked.Symbol ?? string.Empty;

        var report = new ExecutionReport
        {
            OrderId = orderId,
            ClientOrderId = openOrder?.ClientOrderId ?? tracked.ClientOrderId,
            ReportType = ExecutionReportType.Cancelled,
            Symbol = symbol,
            Side = openOrder?.Side ?? (tracked.Symbol is not null ? tracked.Side : OrderSide.Buy),
            OrderStatus = OrderStatus.Cancelled,
            GatewayOrderId = orderId,
            Timestamp = DateTimeOffset.UtcNow,
        };
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    public async Task<ExecutionReport> ModifyOrderAsync(
        string orderId, OrderModification modification, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        EnsureConnected();

        _submittedOrders.TryGetValue(orderId, out var tracked);
        _openOrders.AddOrUpdate(
            orderId,
            static (_, state) => CreateModifiedFallbackOrder(state.OrderId, state.Tracked, state.Modification),
            static (_, existing, state) => ApplyModification(existing, state.Modification),
            (OrderId: orderId, Tracked: tracked, Modification: modification));

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

    public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var report in _reportChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return report;
        }
    }

    public Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new AccountInfo
        {
            AccountId = _options.AccountId,
            Equity = _options.Equity,
            Cash = _options.Cash,
            BuyingPower = _options.BuyingPower,
            Currency = _options.Currency,
            Status = _options.AccountStatus,
        });
    }

    public Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_options.Positions);
    }

    public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<BrokerOrder>>(
            _openOrders.Values
                .OrderBy(order => order.CreatedAt)
                .ThenBy(order => order.OrderId, StringComparer.Ordinal)
                .ToArray());
    }

    public async Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            if (!_connected)
                return BrokerHealthStatus.Unhealthy("Not connected");
            var account = await GetAccountInfoAsync(ct).ConfigureAwait(false);
            return BrokerHealthStatus.Healthy($"Account {account.AccountId}");
        }
        catch (Exception ex)
        {
            return BrokerHealthStatus.Unhealthy(ex.Message);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        _connected = false;
        _reportChannel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private void EnsureConnected()
    {
        if (!_connected)
            throw new InvalidOperationException("Template broker is not connected. Call ConnectAsync first.");
    }

    private void ValidateTemplateConnection()
    {
        if (!_options.ConnectionReady)
            throw new InvalidOperationException(_options.ConnectionFailureMessage);
    }

    private static BrokerOrder CreateOpenOrder(string orderId, OrderRequest request, DateTimeOffset createdAt) => new()
    {
        OrderId = orderId,
        ClientOrderId = request.ClientOrderId,
        Symbol = request.Symbol,
        Side = request.Side,
        Type = request.Type,
        Quantity = request.Quantity,
        FilledQuantity = 0m,
        LimitPrice = request.LimitPrice,
        StopPrice = request.StopPrice,
        Status = OrderStatus.Accepted,
        CreatedAt = createdAt,
    };

    private static BrokerOrder CreateModifiedFallbackOrder(
        string orderId,
        (string Symbol, OrderSide Side, string? ClientOrderId) tracked,
        OrderModification modification) => new()
        {
            OrderId = orderId,
            ClientOrderId = tracked.ClientOrderId,
            Symbol = tracked.Symbol ?? string.Empty,
            Side = tracked.Symbol is not null ? tracked.Side : OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = modification.NewQuantity ?? 0m,
            LimitPrice = modification.NewLimitPrice,
            StopPrice = modification.NewStopPrice,
            Status = OrderStatus.Accepted,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static BrokerOrder ApplyModification(BrokerOrder existing, OrderModification modification) => existing with
    {
        Quantity = modification.NewQuantity ?? existing.Quantity,
        LimitPrice = modification.NewLimitPrice ?? existing.LimitPrice,
        StopPrice = modification.NewStopPrice ?? existing.StopPrice,
        Status = OrderStatus.Accepted,
    };
}

public sealed record TemplateBrokerageGatewayOptions
{
    public string GatewayId { get; init; } = "template";
    public string BrokerDisplayName { get; init; } = "Template Broker";
    public BrokerageCapabilities Capabilities { get; init; } =
        BrokerageCapabilities.UsEquity(
            modification: true,
            partialFills: true,
            shortSelling: false,
            fractional: false,
            extendedHours: false);
    public bool ConnectionReady { get; init; } = true;
    public string ConnectionFailureMessage { get; init; } = "Template broker connection is not ready.";
    public string AccountId { get; init; } = "template-account";
    public decimal Equity { get; init; }
    public decimal Cash { get; init; }
    public decimal BuyingPower { get; init; }
    public string Currency { get; init; } = "USD";
    public string AccountStatus { get; init; } = "paper-template";
    public IReadOnlyList<BrokerPosition> Positions { get; init; } = Array.Empty<BrokerPosition>();
}
