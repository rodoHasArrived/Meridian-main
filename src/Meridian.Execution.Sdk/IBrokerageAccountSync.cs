namespace Meridian.Execution.Sdk;

/// <summary>
/// Enumerates brokerage or custodian accounts that can be imported into Meridian's
/// read-side portfolio, ledger, and governance workflows.
/// </summary>
public interface IBrokerageAccountCatalog
{
    string ProviderId { get; }

    string ProviderDisplayName { get; }

    Task<IReadOnlyList<BrokerageExternalAccountDto>> GetAccountsAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads point-in-time account, balance, and position state from a brokerage or custodian.
/// </summary>
public interface IBrokeragePortfolioSync
{
    string ProviderId { get; }

    Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(
        string externalAccountId,
        CancellationToken ct = default);
}

/// <summary>
/// Reads recent orders, fills, and cash movements from a brokerage or custodian.
/// </summary>
public interface IBrokerageActivitySync
{
    string ProviderId { get; }

    Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
        string externalAccountId,
        DateTimeOffset? since = null,
        CancellationToken ct = default);
}

public sealed record BrokerageExternalAccountDto(
    string ProviderId,
    string AccountId,
    string DisplayName,
    string Status,
    string Currency,
    DateTimeOffset RetrievedAt,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record BrokeragePortfolioSnapshotDto(
    BrokerageExternalAccountDto Account,
    BrokerageBalanceSnapshotDto Balance,
    IReadOnlyList<BrokeragePositionSnapshotDto> Positions,
    DateTimeOffset RetrievedAt);

public sealed record BrokerageBalanceSnapshotDto(
    decimal Cash,
    decimal Equity,
    decimal BuyingPower,
    string Currency,
    decimal MarginBalance = 0m);

public sealed record BrokeragePositionSnapshotDto(
    string Symbol,
    decimal Quantity,
    decimal AverageEntryPrice,
    decimal MarketPrice,
    decimal MarketValue,
    decimal UnrealizedPnl,
    string AssetClass,
    string? Description = null,
    string? PositionId = null,
    string? Currency = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record BrokerageActivitySnapshotDto(
    string ProviderId,
    string AccountId,
    DateTimeOffset RetrievedAt,
    IReadOnlyList<BrokerageOrderSnapshotDto> Orders,
    IReadOnlyList<BrokerageFillSnapshotDto> Fills,
    IReadOnlyList<BrokerageCashTransactionDto> CashTransactions);

public sealed record BrokerageOrderSnapshotDto(
    string OrderId,
    string? ClientOrderId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    OrderStatus Status,
    decimal Quantity,
    decimal FilledQuantity,
    decimal? LimitPrice,
    decimal? StopPrice,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt = null);

public sealed record BrokerageFillSnapshotDto(
    string FillId,
    string? OrderId,
    string Symbol,
    OrderSide Side,
    decimal Quantity,
    decimal Price,
    DateTimeOffset FilledAt,
    string? Venue = null,
    decimal? Commission = null);

public sealed record BrokerageCashTransactionDto(
    string TransactionId,
    string TransactionType,
    decimal Amount,
    string Currency,
    DateTimeOffset PostedAt,
    string? Symbol = null,
    string? Description = null);
