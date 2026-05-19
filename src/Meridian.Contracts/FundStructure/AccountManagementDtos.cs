namespace Meridian.Contracts.FundStructure;

using System.Text.Json.Serialization;

/// <summary>Snapshot of an account's balance at a point in time.</summary>
public sealed record AccountBalanceSnapshotDto(
    Guid SnapshotId,
    Guid AccountId,
    Guid? FundId,
    DateOnly AsOfDate,
    string Currency,
    decimal CashBalance,
    decimal? SecuritiesMarketValue,
    decimal? AccruedInterest,
    decimal? PendingSettlement,
    string Source,
    DateTimeOffset RecordedAt,
    string? ExternalReference);

/// <summary>A single position line from a custodian statement.</summary>
public sealed record CustodianPositionLineDto(
    Guid LineId,
    Guid BatchId,
    Guid AccountId,
    DateOnly AsOfDate,
    string Identifier,
    string IdentifierType,
    decimal Quantity,
    decimal MarketValue,
    string Currency,
    string? SecurityName,
    string? AssetClass,
    bool IsShort);

/// <summary>A single transaction line from a bank statement.</summary>
public sealed record BankStatementLineDto(
    Guid LineId,
    Guid BatchId,
    Guid AccountId,
    DateOnly TransactionDate,
    DateOnly ValueDate,
    decimal Amount,
    string Currency,
    string TransactionType,
    string Description,
    string? Reference,
    decimal? ClosingBalance)
{
    public DateOnly StatementDate => ValueDate;

    public decimal? RunningBalance => ClosingBalance;
}

/// <summary>Request to record an account balance snapshot.</summary>
public sealed record RecordAccountBalanceSnapshotRequest(
    Guid AccountId,
    DateOnly AsOfDate,
    string Currency,
    decimal CashBalance,
    string Source,
    string? RecordedBy = null,
    bool IsBackfill = false,
    decimal? SecuritiesMarketValue = null,
    decimal? AccruedInterest = null,
    decimal? PendingSettlement = null,
    string? ExternalReference = null);

/// <summary>Request to ingest a custodian position statement.</summary>
public sealed record IngestCustodianStatementRequest(
    Guid BatchId,
    Guid AccountId,
    DateOnly AsOfDate,
    string CustodianName,
    string SourceFormat,
    string? Notes,
    IReadOnlyList<CustodianPositionLineDto> Lines,
    string LoadedBy,
    bool IsBackfill = false);

/// <summary>Request to ingest a bank statement.</summary>
public sealed record IngestBankStatementRequest(
    Guid BatchId,
    Guid AccountId,
    DateOnly StatementDate,
    string BankName,
    string? Notes,
    IReadOnlyList<BankStatementLineDto> Lines,
    string LoadedBy,
    bool IsBackfill = false);


public sealed record CustodianStatementBatchDto(
    Guid BatchId,
    Guid AccountId,
    DateOnly AsOfDate,
    string CustodianName,
    string SourceFormat,
    int LineCount,
    DateTimeOffset IngestedAt,
    string LoadedBy);

/// Metadata for a bank statement batch that was ingested.
public sealed record BankStatementBatchDto(
    Guid BatchId,
    Guid AccountId,
    DateOnly StatementDate,
    string BankName,
    int LineCount,
    DateTimeOffset IngestedAt,
    string LoadedBy);

/// Header record for an account-level reconciliation run.
public sealed record AccountReconciliationRunDto(
    Guid ReconciliationRunId,
    Guid AccountId,
    DateOnly AsOfDate,
    string Status,
    int TotalChecks,
    int TotalMatched,
    int TotalBreaks,
    decimal BreakAmountTotal,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    string RequestedBy);

/// Result of a single check within a reconciliation run.
public sealed record AccountReconciliationResultDto(
    Guid ResultId,
    Guid ReconciliationRunId,
    string CheckLabel,
    bool IsMatch,
    string Category,
    string Status,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal? Variance,
    string Reason);

/// <summary>Provider account-link posture for account-scoped sync and readiness.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccountProviderLinkStatusDto>))]
public enum AccountProviderLinkStatusDto
{
    NotLinked = 0,
    Linked = 1,
    Verified = 2,
    Degraded = 3,
    Revoked = 4,
    Expired = 5,
    Unauthorized = 6,
    Unsupported = 7,
    SyncPending = 8,
    SyncFailed = 9
}

/// <summary>Outcome for one account-scoped sync attempt.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccountSyncStatusDto>))]
public enum AccountSyncStatusDto
{
    Pending = 0,
    Succeeded = 1,
    Degraded = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>Structured failure class for account sync retries and readiness.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccountSyncFailureKindDto>))]
public enum AccountSyncFailureKindDto
{
    None = 0,
    CredentialMissing = 1,
    Unauthorized = 2,
    ProviderUnavailable = 3,
    RateLimited = 4,
    Timeout = 5,
    Cancelled = 6,
    ValidationFailed = 7,
    Unsupported = 8,
    Duplicate = 9,
    Unknown = 99
}

/// <summary>Operator severity for account readiness issues.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccountReadinessSeverityDto>))]
public enum AccountReadinessSeverityDto
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

/// <summary>Durable history entry for one provider/account sync attempt.</summary>
public sealed record AccountSyncHistoryEntryDto(
    Guid SyncHistoryId,
    Guid AccountId,
    string Capability,
    AccountSyncStatusDto Status,
    AccountProviderLinkStatusDto ProviderLinkStatus,
    string? ProviderId,
    string? ExternalAccountId,
    DateTimeOffset AttemptedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FreshUntil,
    AccountSyncFailureKindDto FailureKind,
    string? FailureMessage,
    string? CorrelationId,
    string? RequestedBy,
    string? RawEvidencePath,
    string? ProjectionEvidencePath,
    int SecurityMissingCount,
    IReadOnlyList<string> Warnings);

/// <summary>Request to append or upsert an account-scoped sync history entry.</summary>
public sealed record RecordAccountSyncHistoryRequest(
    Guid AccountId,
    string Capability,
    AccountSyncStatusDto Status,
    AccountProviderLinkStatusDto ProviderLinkStatus = AccountProviderLinkStatusDto.Linked,
    string? ProviderId = null,
    string? ExternalAccountId = null,
    DateTimeOffset? AttemptedAt = null,
    DateTimeOffset? CompletedAt = null,
    DateTimeOffset? FreshUntil = null,
    AccountSyncFailureKindDto FailureKind = AccountSyncFailureKindDto.None,
    string? FailureMessage = null,
    string? CorrelationId = null,
    string? RequestedBy = null,
    string? RawEvidencePath = null,
    string? ProjectionEvidencePath = null,
    int SecurityMissingCount = 0,
    IReadOnlyList<string>? Warnings = null);

/// <summary>One account readiness issue derived from account metadata, sync history, and reconciliation state.</summary>
public sealed record AccountReadinessIssueDto(
    string Code,
    AccountReadinessSeverityDto Severity,
    string Title,
    string Message,
    Guid AccountId,
    string? ProviderId = null,
    string? ExternalAccountId = null,
    string? Capability = null,
    string? SuggestedAction = null,
    string? EvidenceLink = null);

/// <summary>Account-scoped production readiness snapshot for provider, ledger, sync, and reconciliation checks.</summary>
public sealed record AccountReadinessSnapshotDto(
    Guid AccountId,
    DateTimeOffset EvaluatedAt,
    AccountProviderLinkStatusDto ProviderLinkStatus,
    AccountSyncStatusDto? LatestSyncStatus,
    DateTimeOffset? LastSuccessfulSyncAt,
    DateTimeOffset? FreshUntil,
    bool IsReady,
    IReadOnlyList<AccountReadinessIssueDto> Issues);

/// <summary>Polymorphic envelope for account-type specific details.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CustodianAccountDetailsEnvelopeDto), "custodian")]
[JsonDerivedType(typeof(BankAccountDetailsEnvelopeDto), "bank")]
public abstract record AccountDetailsDto;

/// <summary>Custodian-specific account details.</summary>
public sealed record CustodianAccountDetailsDto(
    string? SubAccountNumber,
    string? DtcParticipantCode,
    string? CrestMemberCode,
    string? EuroclearAccountNumber,
    string? ClearstreamAccountNumber,
    string? PrimebrokerGiveupCode,
    string? SafekeepingLocation,
    string? ServiceAgreementReference);

/// <summary>Polymorphic envelope for custodian account details.</summary>
public sealed record CustodianAccountDetailsEnvelopeDto(CustodianAccountDetailsDto Details) : AccountDetailsDto;

/// <summary>Bank-specific account details.</summary>
public sealed record BankAccountDetailsDto(
    string? AccountNumber,
    string? BankName,
    string? BranchName,
    string? Iban,
    string? BicSwift,
    string? RoutingNumber,
    string? SortCode,
    string? IntermediaryBankBic,
    string? IntermediaryBankName,
    string? BeneficiaryName,
    string? BeneficiaryAddress);

/// <summary>Polymorphic envelope for bank account details.</summary>
public sealed record BankAccountDetailsEnvelopeDto(BankAccountDetailsDto Details) : AccountDetailsDto;

/// <summary>Request to create a new fund account.</summary>
public sealed record CreateAccountRequest(
    Guid AccountId,
    AccountTypeDto AccountType,
    string AccountCode,
    string DisplayName,
    string BaseCurrency,
    DateTimeOffset EffectiveFrom,
    string CreatedBy,
    Guid? FundId = null,
    Guid? EntityId = null,
    Guid? SleeveId = null,
    Guid? VehicleId = null,
    string? Institution = null,
    string? PortfolioId = null,
    string? LedgerReference = null,
    string? StrategyId = null,
    string? RunId = null,
    AccountOperationalStatusDto OperationalStatus = AccountOperationalStatusDto.Active,
    CustodianAccountDetailsDto? CustodianDetails = null,
    BankAccountDetailsDto? BankDetails = null);

/// <summary>Request to update custodian account details.</summary>
public sealed record UpdateCustodianAccountDetailsRequest(
    CustodianAccountDetailsDto Details,
    string UpdatedBy);

/// <summary>Request to update bank account details.</summary>
public sealed record UpdateBankAccountDetailsRequest(
    BankAccountDetailsDto Details,
    string UpdatedBy);

/// <summary>Request to reconcile an account at a given date.</summary>
public sealed record ReconcileAccountRequest(
    Guid AccountId,
    DateOnly AsOfDate,
    string RequestedBy);

/// <summary>Fund-level grouping of accounts by type.</summary>
public sealed record FundAccountsDto(
    Guid FundId,
    IReadOnlyList<AccountSummaryDto> CustodianAccounts,
    IReadOnlyList<AccountSummaryDto> BankAccounts,
    IReadOnlyList<AccountSummaryDto> BrokerageAccounts,
    IReadOnlyList<AccountSummaryDto> OtherAccounts);
