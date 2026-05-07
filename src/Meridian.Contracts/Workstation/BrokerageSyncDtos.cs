using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

[JsonConverter(typeof(JsonStringEnumConverter<WorkstationBrokerageSyncHealth>))]
public enum WorkstationBrokerageSyncHealth
{
    Unlinked = 0,
    Healthy = 1,
    Stale = 2,
    Degraded = 3,
    Failed = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<BrokerageAccountKindDto>))]
public enum BrokerageAccountKindDto
{
    Unknown = 0,
    TaxableBrokerage = 1,
    RothIra = 2,
    TraditionalIra = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<BrokerageConnectionStateDto>))]
public enum BrokerageConnectionStateDto
{
    NotConfigured = 0,
    Disconnected = 1,
    AuthorizationPending = 2,
    Connected = 3,
    ReauthorizationRequired = 4,
    Degraded = 5
}

public sealed record WorkstationBrokerageSyncRunRequestDto(
    string? ProviderId = null,
    string? ExternalAccountId = null,
    string? RequestedBy = null,
    DateTimeOffset? Since = null,
    BrokerageAccountKindDto AccountKind = BrokerageAccountKindDto.Unknown);

public sealed record WorkstationBrokerageAccountDto(
    string ProviderId,
    string AccountId,
    string DisplayName,
    string Status,
    string Currency,
    DateTimeOffset RetrievedAt,
    BrokerageAccountKindDto AccountKind = BrokerageAccountKindDto.Unknown);

public sealed record WorkstationBrokerageAccountLinkDto(
    Guid FundAccountId,
    string ProviderId,
    string ExternalAccountId,
    string DisplayName,
    DateTimeOffset LinkedAt,
    string? LinkedBy = null,
    BrokerageAccountKindDto AccountKind = BrokerageAccountKindDto.Unknown);

public sealed record BrokerageAccountLinkRequestDto(
    string ProviderId,
    string ExternalAccountId,
    string? DisplayName = null,
    string? LinkedBy = null,
    BrokerageAccountKindDto AccountKind = BrokerageAccountKindDto.Unknown);

public sealed record WorkstationBrokerageSyncStatusDto(
    Guid FundAccountId,
    string? ProviderId,
    string? ExternalAccountId,
    WorkstationBrokerageSyncHealth Health,
    bool IsLinked,
    bool IsStale,
    DateTimeOffset? LastAttemptedSyncAt,
    DateTimeOffset? LastSuccessfulSyncAt,
    string? LastError,
    int PositionCount,
    int OpenOrderCount,
    int FillCount,
    int CashTransactionCount,
    int SecurityMissingCount,
    IReadOnlyList<string> Warnings,
    BrokerageAccountKindDto AccountKind = BrokerageAccountKindDto.Unknown);

public sealed record BrokerageConnectionStatusDto(
    string ProviderId,
    string DisplayName,
    BrokerageConnectionStateDto State,
    bool IsConfigured,
    bool IsConnected,
    string? AuthorizationUrl,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? ExpiresAt,
    string? LastError,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Scopes,
    string? Environment = null,
    string? ExternalAccountId = null,
    DateTimeOffset? VerifiedAt = null,
    string? MaskedKeyId = null);

public sealed record AlpacaBrokerageConnectionRequestDto(
    string? KeyId,
    string? SecretKey,
    string? Environment = "paper");

public sealed record BrokeragePortfolioPerformancePointDto(
    DateOnly Date,
    decimal Equity,
    decimal Cash,
    decimal NetCashFlow);

public sealed record BrokeragePortfolioPerformanceDto(
    Guid FundAccountId,
    string? ProviderId,
    string? ExternalAccountId,
    BrokerageAccountKindDto AccountKind,
    DateOnly? From,
    DateOnly? To,
    bool HasSufficientHistory,
    decimal? BeginningEquity,
    decimal? EndingEquity,
    decimal NetCashFlow,
    decimal? CashAdjustedReturn,
    decimal? CashAdjustedReturnPercent,
    IReadOnlyList<BrokeragePortfolioPerformancePointDto> Points,
    IReadOnlyList<string> Warnings);

public sealed record BrokerageCashFlowEntryDto(
    string TransactionId,
    string TransactionType,
    string Category,
    decimal Amount,
    string Currency,
    DateTimeOffset PostedAt,
    string? Symbol = null,
    string? Description = null);

public sealed record BrokerageCashFlowSummaryDto(
    Guid FundAccountId,
    string? ProviderId,
    string? ExternalAccountId,
    BrokerageAccountKindDto AccountKind,
    DateOnly? From,
    DateOnly? To,
    decimal TotalInflows,
    decimal TotalOutflows,
    decimal NetCashFlow,
    string Currency,
    int TransactionCount,
    IReadOnlyList<BrokerageCashFlowEntryDto> Entries,
    IReadOnlyList<string> Warnings);

public sealed record BrokerageHouseholdAccountDto(
    Guid FundAccountId,
    string ProviderId,
    string ExternalAccountId,
    string DisplayName,
    BrokerageAccountKindDto AccountKind,
    WorkstationBrokerageSyncHealth Health,
    decimal Cash,
    decimal Equity,
    decimal BuyingPower,
    string Currency,
    DateTimeOffset SyncedAt,
    int PositionCount,
    int CashTransactionCount,
    IReadOnlyList<string> Warnings);

public sealed record BrokerageHouseholdPositionDto(
    Guid FundAccountId,
    string ProviderId,
    string ExternalAccountId,
    BrokerageAccountKindDto AccountKind,
    string Symbol,
    decimal Quantity,
    decimal AverageEntryPrice,
    decimal MarketPrice,
    decimal MarketValue,
    decimal UnrealizedPnl,
    string AssetClass,
    WorkstationSecurityReference? Security,
    string? Description = null,
    string? PositionId = null,
    string? Currency = null);

public sealed record BrokerageHouseholdPortfolioDto(
    string ProviderId,
    DateTimeOffset AsOf,
    decimal TotalCash,
    decimal TotalEquity,
    decimal TotalBuyingPower,
    string Currency,
    IReadOnlyList<BrokerageHouseholdAccountDto> Accounts,
    IReadOnlyList<BrokerageHouseholdPositionDto> Positions,
    IReadOnlyList<string> Warnings);

[Obsolete("Use fund-account scoped workstation payloads and continuity/governance DTOs for sync posture. This standalone sync projection DTO is deprecated.")]
public sealed record WorkstationBrokerageBalanceSnapshotDto(
    decimal Cash,
    decimal Equity,
    decimal BuyingPower,
    string Currency,
    decimal MarginBalance);

[Obsolete("Use fund-account scoped workstation payloads and continuity/governance DTOs for sync posture. This standalone sync projection DTO is deprecated.")]
public sealed record WorkstationBrokeragePositionDto(
    string Symbol,
    decimal Quantity,
    decimal AverageEntryPrice,
    decimal MarketPrice,
    decimal MarketValue,
    decimal UnrealizedPnl,
    string AssetClass,
    WorkstationSecurityReference? Security,
    string? Description = null,
    string? PositionId = null,
    string? Currency = null);

[Obsolete("Use fund-account scoped workstation payloads and continuity/governance DTOs for sync posture. This standalone sync projection DTO is deprecated.")]
public sealed record WorkstationBrokerageOrderDto(
    string OrderId,
    string? ClientOrderId,
    string Symbol,
    string Side,
    string Type,
    string Status,
    decimal Quantity,
    decimal FilledQuantity,
    decimal? LimitPrice,
    decimal? StopPrice,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt = null);

[Obsolete("Use fund-account scoped workstation payloads and continuity/governance DTOs for sync posture. This standalone sync projection DTO is deprecated.")]
public sealed record WorkstationBrokerageFillDto(
    string FillId,
    string? OrderId,
    string Symbol,
    string Side,
    decimal Quantity,
    decimal Price,
    DateTimeOffset FilledAt,
    string? Venue = null,
    decimal? Commission = null);

[Obsolete("Use fund-account scoped workstation payloads and continuity/governance DTOs for sync posture. This standalone sync projection DTO is deprecated.")]
public sealed record WorkstationBrokerageCashTransactionDto(
    string TransactionId,
    string TransactionType,
    decimal Amount,
    string Currency,
    DateTimeOffset PostedAt,
    string? Symbol = null,
    string? Description = null);

[Obsolete("Use WorkstationBrokerageSyncStatusDto via fund-account scoped readiness/work-item/governance flows. This standalone sync projection DTO is deprecated.")]
public sealed record WorkstationBrokerageSyncViewDto(
    Guid FundAccountId,
    WorkstationBrokerageAccountLinkDto Link,
    WorkstationBrokerageSyncStatusDto Status,
    WorkstationBrokerageBalanceSnapshotDto? Balance,
    IReadOnlyList<WorkstationBrokeragePositionDto> Positions,
    IReadOnlyList<WorkstationBrokerageOrderDto> Orders,
    IReadOnlyList<WorkstationBrokerageFillDto> Fills,
    IReadOnlyList<WorkstationBrokerageCashTransactionDto> CashTransactions,
    DateTimeOffset SyncedAt,
    string RawSnapshotPath,
    string ProjectionPath);
