using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;

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

public sealed record FundAccountBrokerageBalanceSnapshotDto(
    decimal Cash,
    decimal Equity,
    decimal BuyingPower,
    string Currency,
    decimal MarginBalance);

public sealed record FundAccountBrokeragePositionDto(
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

public sealed record FundAccountBrokerageOrderDto(
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

public sealed record FundAccountBrokerageFillDto(
    string FillId,
    string? OrderId,
    string Symbol,
    string Side,
    decimal Quantity,
    decimal Price,
    DateTimeOffset FilledAt,
    string? Venue = null,
    decimal? Commission = null,
    decimal? RealizedPnl = null);

public sealed record FundAccountBrokerageCashTransactionDto(
    string TransactionId,
    string TransactionType,
    decimal Amount,
    string Currency,
    DateTimeOffset PostedAt,
    string? Symbol = null,
    string? Description = null);

public sealed record FundAccountBrokerageCorporateActionDto(
    string EventId,
    string EventType,
    string? Symbol,
    DateOnly? EffectiveDate,
    DateOnly? ExDate,
    decimal? Amount = null,
    decimal? Quantity = null,
    decimal? Factor = null,
    string? Currency = null,
    string? Description = null);

public sealed record FundAccountBrokerageSyncActivityDto(
    Guid FundAccountId,
    WorkstationBrokerageAccountLinkDto Link,
    WorkstationBrokerageSyncStatusDto Status,
    FundAccountBrokerageBalanceSnapshotDto? Balance,
    IReadOnlyList<FundAccountBrokeragePositionDto> Positions,
    IReadOnlyList<FundAccountBrokerageOrderDto> Orders,
    IReadOnlyList<FundAccountBrokerageFillDto> Fills,
    IReadOnlyList<FundAccountBrokerageCashTransactionDto> CashTransactions,
    DateTimeOffset SyncedAt,
    string RawSnapshotPath,
    string ProjectionPath,
    IReadOnlyList<FundAccountBrokerageCorporateActionDto>? CorporateActions = null);

[JsonConverter(typeof(JsonStringEnumConverter<ProviderLedgerReconciliationStatusDto>))]
public enum ProviderLedgerReconciliationStatusDto
{
    Matched = 0,
    Breaks = 1,
    Blocked = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderLedgerReconciliationCheckStatusDto>))]
public enum ProviderLedgerReconciliationCheckStatusDto
{
    Matched = 0,
    Break = 1,
    Blocked = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderLedgerReconciliationBreakSignOffStateDto>))]
public enum ProviderLedgerReconciliationBreakSignOffStateDto
{
    Open = 0,
    Assigned = 1,
    SignedOff = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderSecurityMasterPassportStatusDto>))]
public enum ProviderSecurityMasterPassportStatusDto
{
    Resolved = 0,
    Inferred = 1,
    Unresolved = 2,
    Blocked = 3
}

public sealed record ProviderLedgerReconciliationRequestDto(
    decimal AmountTolerance = 0.01m,
    int ProviderStaleAfterMinutes = 30,
    string? RequestedBy = null,
    string? DefaultBreakOwner = null,
    IReadOnlyList<string>? SignedOffBreakKeys = null,
    string? SignedOffBy = null);

public sealed record ProviderLedgerReconciliationCheckDto(
    string CheckId,
    string Label,
    ProviderLedgerReconciliationCheckStatusDto Status,
    ReconciliationBreakCategory Category,
    string ExpectedSource,
    string ActualSource,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal? Variance,
    ReconciliationBreakSeverity Severity,
    string Reason);

public sealed record ProviderLedgerReconciliationBreakDto(
    string BreakId,
    string CheckId,
    string Code,
    ReconciliationBreakCategory Category,
    ReconciliationBreakSeverity Severity,
    string ExpectedSource,
    string ActualSource,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal? Variance,
    string Reason,
    string? Symbol = null,
    string? EvidenceLink = null,
    string? BreakKey = null,
    string? Owner = null,
    decimal? Tolerance = null,
    DateTimeOffset? FirstObservedAt = null,
    DateTimeOffset? LastObservedAt = null,
    int AgeMinutes = 0,
    ProviderLedgerReconciliationBreakSignOffStateDto SignOffState = ProviderLedgerReconciliationBreakSignOffStateDto.Open,
    string? SignedOffBy = null,
    DateTimeOffset? SignedOffAt = null);

public sealed record ProviderLedgerReconciliationSummaryDto(
    Guid ReconciliationRunId,
    Guid AccountId,
    DateTimeOffset CreatedAt,
    ProviderLedgerReconciliationStatusDto Status,
    int TotalChecks,
    int MatchedChecks,
    int BreakCount,
    int SecurityIssueCount,
    decimal AmountTolerance,
    int ProviderStaleAfterMinutes,
    string? ProviderId,
    string? ExternalAccountId,
    DateTimeOffset? ProviderSyncedAt,
    DateOnly? InternalAsOfDate,
    string? DetailPath = null,
    int OpenBreakCount = 0,
    int SignedOffBreakCount = 0,
    int OldestBreakAgeMinutes = 0);

public sealed record ProviderSecurityMasterPassportDto(
    string Symbol,
    string ProviderId,
    string ExternalAccountId,
    DateTimeOffset ProviderSyncedAt,
    bool ProviderIsStale,
    string AssetClass,
    string? Currency,
    string? PositionId,
    Guid? SecurityId,
    string? SecurityDisplayName,
    SecurityStatusDto? SecurityStatus,
    ProviderSecurityMasterPassportStatusDto Status,
    decimal ConfidenceScore,
    string ResolutionSource,
    IReadOnlyList<string> IdentifierConflicts,
    IReadOnlyList<string> ValidationIssueCodes,
    IReadOnlyList<string> OverrideHistory,
    DateTimeOffset ObservedAt,
    int FreshnessMinutes,
    string Reason);

public sealed record ProviderShadowBookComparisonLineDto(
    string Dimension,
    string Label,
    string InternalSource,
    string ProviderSource,
    decimal? InternalAmount,
    decimal? ProviderAmount,
    decimal? Variance,
    ProviderLedgerReconciliationCheckStatusDto Status,
    string Reason);

public sealed record ProviderShadowBookComparisonDto(
    Guid AccountId,
    DateTimeOffset CreatedAt,
    string Currency,
    int ComparableLineCount,
    int MatchedLineCount,
    int BreakLineCount,
    int UnavailableLineCount,
    IReadOnlyList<ProviderShadowBookComparisonLineDto> Lines);

public sealed record ProviderCorporateActionReadinessLineDto(
    string Dimension,
    string Label,
    ProviderLedgerReconciliationCheckStatusDto Status,
    string RequiredFeed,
    string EvidenceSource,
    int Count,
    string Reason);

public sealed record ProviderCorporateActionEvidenceCandidateDto(
    string CandidateId,
    string CandidateType,
    string? Symbol,
    Guid? SecurityId,
    string? SecurityDisplayName,
    ProviderLedgerReconciliationCheckStatusDto Status,
    string RequiredFeed,
    string EvidenceSource,
    string ProviderId,
    string ExternalAccountId,
    string? ProviderEventId,
    DateTimeOffset ObservedAt,
    decimal? Amount,
    decimal? Quantity,
    string? Currency,
    string Reason);

public sealed record ProviderCorporateActionLedgerEffectDto(
    string CandidateId,
    string CandidateType,
    string? Symbol,
    Guid? SecurityId,
    string? ProviderEventId,
    string LedgerEffectKind,
    DateOnly? EffectiveDate,
    decimal? Factor,
    decimal? CashAmount,
    decimal? PrincipalAmount,
    decimal? IncomeAmount,
    string? Currency,
    ProviderLedgerReconciliationCheckStatusDto Status,
    string Reason,
    IReadOnlyList<ExpectedJournalPreviewLineDto> JournalLines);

public sealed record ProviderSecurityMasterScheduleFeedDto(
    string FeedId,
    string CandidateId,
    string CandidateType,
    string FeedKind,
    string RequiredFeed,
    string EvidenceSource,
    string ProviderId,
    string ExternalAccountId,
    string? ProviderEventId,
    string? Symbol,
    Guid? SecurityId,
    DateOnly? EffectiveDate,
    decimal? Factor,
    decimal? CashAmount,
    decimal? PrincipalAmount,
    decimal? IncomeAmount,
    string? Currency,
    string LedgerEffectKind,
    ProviderLedgerReconciliationCheckStatusDto Status,
    bool CanUpdateSecurityMaster,
    bool CanSupportLedgerValuation,
    string Reason);

public sealed record ProviderCorporateActionReadinessDto(
    bool ProviderCorporateActionsRoutable,
    ProviderLedgerReconciliationCheckStatusDto Status,
    int PositionCount,
    int SecurityResolvedCount,
    int EquityPositionCount,
    int FixedIncomeOrStructuredPositionCount,
    int FactorScheduleCandidateCount,
    int IncomeCashTransactionCount,
    int DividendCashTransactionCount,
    int InterestCashTransactionCount,
    IReadOnlyList<string> RequiredFeeds,
    IReadOnlyList<string> MissingFeeds,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<ProviderCorporateActionReadinessLineDto> Lines,
    int ProviderCorporateActionEventCount = 0,
    int FactorScheduleEventCount = 0,
    bool FactorScheduleRoutable = false,
    int LoanScheduleEventCount = 0)
{
    public IReadOnlyList<ProviderCorporateActionEvidenceCandidateDto> EvidenceCandidates { get; init; } = [];

    public IReadOnlyList<ProviderCorporateActionLedgerEffectDto> LedgerEffects { get; init; } = [];

    public IReadOnlyList<ProviderSecurityMasterScheduleFeedDto> SecurityMasterScheduleFeeds { get; init; } = [];

    public int PrincipalCashTransactionCount { get; init; }
}

public sealed record ProviderLedgerReconciliationDetailDto(
    ProviderLedgerReconciliationSummaryDto Summary,
    IReadOnlyList<ProviderLedgerReconciliationCheckDto> Checks,
    IReadOnlyList<ProviderLedgerReconciliationBreakDto> Breaks,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<ProviderSecurityMasterPassportDto>? SecurityMasterPassports = null,
    ProviderShadowBookComparisonDto? ShadowBookComparison = null,
    ProviderCorporateActionReadinessDto? CorporateActionReadiness = null);

[JsonConverter(typeof(JsonStringEnumConverter<FundAccountCloseReadinessStatusDto>))]
public enum FundAccountCloseReadinessStatusDto
{
    Ready = 0,
    ReviewRequired = 1,
    Blocked = 2
}

public sealed record FundAccountCloseReadinessComponentDto(
    string Key,
    string Label,
    FundAccountCloseReadinessStatusDto Status,
    int Score,
    int Weight,
    string Severity,
    string Reason,
    string? RouteHint = null,
    string? EvidenceLink = null)
{
    public string BlockingReason => Reason;
}

public sealed record FundAccountCloseReadinessBlockerDto(
    string Code,
    string Category,
    string Severity,
    string Message,
    string? RouteHint = null,
    string? EvidenceLink = null);

public sealed record FundAccountCloseReadinessActionDto(
    string Code,
    string Label,
    string Severity,
    string? RouteHint = null);

public sealed record FundAccountCloseReadinessDto(
    Guid AccountId,
    DateTimeOffset EvaluatedAtUtc,
    FundAccountCloseReadinessStatusDto Status,
    bool IsReadyToClose,
    int Score,
    DateTimeOffset? LatestProviderSyncedAt,
    DateOnly? LatestLedgerAsOfDate,
    Guid? LatestReconciliationRunId,
    IReadOnlyList<FundAccountCloseReadinessComponentDto> Components,
    IReadOnlyList<FundAccountCloseReadinessBlockerDto> Blockers,
    IReadOnlyList<FundAccountCloseReadinessActionDto> NextActions);
