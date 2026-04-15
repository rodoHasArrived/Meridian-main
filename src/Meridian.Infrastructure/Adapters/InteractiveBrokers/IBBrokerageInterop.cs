using Meridian.Execution.Sdk;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Compile-neutral brokerage-facing contract that abstracts the native IB socket client.
/// The real implementation is provided by <see cref="EnhancedIBConnectionManager"/> in IBAPI builds,
/// while tests can inject a fake implementation without referencing the vendor SDK.
/// </summary>
public interface IIBBrokerageClient : IDisposable
{
    string Host { get; }
    int Port { get; }
    int ClientId { get; }
    bool IsConnected { get; }

    event EventHandler<int>? NextValidIdReceived;
    event EventHandler<IBOrderStatusUpdate>? OrderStatusReceived;
    event EventHandler<IBOpenOrderUpdate>? OpenOrderReceived;
    event EventHandler? OpenOrdersCompleted;
    event EventHandler<IBExecutionUpdate>? ExecutionDetailsReceived;
    event EventHandler<IBPositionUpdate>? PositionReceived;
    event EventHandler? PositionsCompleted;
    event EventHandler<IBAccountSummaryUpdate>? AccountSummaryReceived;
    event EventHandler<int>? AccountSummaryCompleted;
    event EventHandler<IBApiError>? ErrorOccurred;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    void RequestNextValidId();
    Task PlaceOrderAsync(int orderId, OrderRequest request, CancellationToken ct = default);
    Task CancelOrderAsync(int orderId, CancellationToken ct = default);
    int RequestAccountSummary();
    void CancelAccountSummary(int requestId);
    void RequestPositions();
    void CancelPositions();
    void RequestOpenOrders();
}

public sealed record IBOrderStatusUpdate(
    int OrderId,
    string Status,
    decimal Filled,
    decimal Remaining,
    double AverageFillPrice,
    double LastFillPrice,
    long PermId,
    int ClientId,
    string? WhyHeld,
    DateTimeOffset ReceivedAt);

public sealed record IBOpenOrderUpdate(
    int OrderId,
    string Symbol,
    string? SecurityType,
    string Action,
    string OrderType,
    decimal Quantity,
    decimal FilledQuantity,
    double? LimitPrice,
    double? StopPrice,
    string Status,
    string? ClientOrderId,
    string? Account,
    double? Commission,
    string? RejectReason,
    IReadOnlyDictionary<string, string>? Metadata,
    DateTimeOffset ReceivedAt);

public sealed record IBExecutionUpdate(
    int OrderId,
    string Symbol,
    string Side,
    decimal Shares,
    double Price,
    decimal CumulativeQuantity,
    double AveragePrice,
    string ExecutionId,
    string? Account,
    string? Exchange,
    long PermId,
    DateTimeOffset ExecutedAt);

public sealed record IBPositionUpdate(
    string Account,
    string Symbol,
    string? SecurityType,
    decimal Quantity,
    double AverageCost,
    string? Currency,
    string? Exchange,
    IReadOnlyDictionary<string, string>? Metadata,
    DateTimeOffset ReceivedAt);

public sealed record IBAccountSummaryUpdate(
    int RequestId,
    string Account,
    string Tag,
    string Value,
    string Currency,
    DateTimeOffset ReceivedAt);
