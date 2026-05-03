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

public sealed record WorkstationBrokerageSyncRunRequestDto(
    string? ProviderId = null,
    string? ExternalAccountId = null,
    string? RequestedBy = null,
    DateTimeOffset? Since = null);

public sealed record WorkstationBrokerageAccountDto(
    string ProviderId,
    string AccountId,
    string DisplayName,
    string Status,
    string Currency,
    DateTimeOffset RetrievedAt);

public sealed record WorkstationBrokerageAccountLinkDto(
    Guid FundAccountId,
    string ProviderId,
    string ExternalAccountId,
    string DisplayName,
    DateTimeOffset LinkedAt,
    string? LinkedBy = null);

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
