namespace Meridian.Contracts.FundStructure;

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
    decimal? ClosingBalance);

/// <summary>Request to record an account balance snapshot.</summary>
public sealed record RecordAccountBalanceSnapshotRequest(
    Guid AccountId,
    DateOnly AsOfDate,
    string Currency,
    decimal CashBalance,
    string Source,
    string? RecordedBy = null,
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
    string LoadedBy);

/// <summary>Request to ingest a bank statement.</summary>
public sealed record IngestBankStatementRequest(
    Guid BatchId,
    Guid AccountId,
    DateOnly StatementDate,
    string BankName,
    string? Notes,
    IReadOnlyList<BankStatementLineDto> Lines,
    string LoadedBy);


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
