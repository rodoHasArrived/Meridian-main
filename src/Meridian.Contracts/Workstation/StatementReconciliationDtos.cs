using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Lifecycle state for a statement reconciliation run shared by workstation clients.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StatementRunStatus>))]
public enum StatementRunStatus : byte
{
    Unknown = 0,
    PendingValidation = 1,
    Validating = 2,
    ValidationFailed = 3,
    Importing = 4,
    Reconciling = 5,
    ReviewRequired = 6,
    Completed = 7,
    Failed = 8,
    Canceled = 9
}

/// <summary>
/// Severity for source validation issues found before or during statement normalization.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StatementValidationSeverity>))]
public enum StatementValidationSeverity : byte
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

/// <summary>
/// Confidence tier assigned to a statement item matched against Meridian books and activity.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StatementMatchTier>))]
public enum StatementMatchTier : byte
{
    Unknown = 0,
    Exact = 1,
    WithinTolerance = 2,
    Probable = 3,
    Manual = 4,
    Unmatched = 5
}

/// <summary>
/// Canonical category for a statement reconciliation break.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StatementBreakType>))]
public enum StatementBreakType : byte
{
    Unknown = 0,
    MissingStatementPosition = 1,
    MissingBookPosition = 2,
    PositionQuantityMismatch = 3,
    PositionMarketValueMismatch = 4,
    MissingStatementCash = 5,
    MissingBookCash = 6,
    CashBalanceMismatch = 7,
    MissingStatementTransaction = 8,
    MissingBookTransaction = 9,
    TransactionAmountMismatch = 10,
    SecurityIdentifierMismatch = 11,
    TimingMismatch = 12,
    ClassificationMismatch = 13,
    DuplicateStatementItem = 14,
    ValidationFailure = 15
}

/// <summary>
/// Source metadata for a broker, custodian, or administrator statement payload.
/// </summary>
public sealed record StatementSourceDto(
    string? SourceId,
    string? SourceKind,
    string? SourceSystem,
    string? AccountId,
    string? AccountName,
    string? Currency,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    DateTimeOffset? StatementAsOfUtc = null,
    string? CustodianId = null,
    string? ExternalAccountId = null);

/// <summary>
/// Validation issue raised while reading, mapping, or normalizing a statement source.
/// </summary>
public sealed record StatementValidationIssueDto(
    string? IssueId,
    StatementValidationSeverity? Severity,
    string? Code,
    string? Message,
    int? SourceRowNumber = null,
    string? SourceColumn = null,
    string? RawValue = null,
    string? RecommendedAction = null,
    string? EvidenceLink = null);

/// <summary>
/// Canonical position row normalized from a statement source.
/// </summary>
public sealed record StatementNormalizedPositionDto(
    string? RowId,
    string? AccountId,
    string? Symbol,
    decimal? Quantity,
    decimal? MarketPrice,
    decimal? MarketValue,
    string? Currency,
    DateTimeOffset? AsOfUtc,
    string? Fingerprint,
    string? SecurityId = null,
    string? Cusip = null,
    string? Isin = null,
    string? Sedol = null,
    string? SourceReference = null);

/// <summary>
/// Canonical cash-balance row normalized from a statement source.
/// </summary>
public sealed record StatementNormalizedCashDto(
    string? RowId,
    string? AccountId,
    string? Currency,
    decimal? Amount,
    DateTimeOffset? AsOfUtc,
    string? Fingerprint,
    string? CashType = null,
    string? SourceReference = null);

/// <summary>
/// Canonical transaction row normalized from a statement source.
/// </summary>
public sealed record StatementNormalizedTransactionDto(
    string? RowId,
    string? AccountId,
    string? ActivityType,
    DateOnly? TradeDate,
    decimal? Amount,
    string? Currency,
    string? Fingerprint,
    DateOnly? SettleDate = null,
    string? ExternalTransactionId = null,
    string? Symbol = null,
    decimal? Quantity = null,
    decimal? Price = null,
    string? Description = null,
    string? SourceReference = null);

/// <summary>
/// Aggregate matching posture for a statement reconciliation run.
/// </summary>
public sealed record StatementMatchSummaryDto(
    int? StatementItemCount,
    int? MatchedItemCount,
    int? AutoMatchedItemCount,
    int? ManualMatchedItemCount,
    int? ReviewRequiredItemCount,
    int? BreakCount,
    int? PositionBreakCount,
    int? CashBreakCount,
    int? TransactionBreakCount,
    StatementMatchTier? HighestUnresolvedTier = null,
    decimal? MatchedNotional = null,
    decimal? UnmatchedNotional = null,
    string? BaseCurrency = null);

/// <summary>
/// Open or resolved variance between normalized statement data and Meridian books/activity.
/// </summary>
public sealed record StatementBreakDto(
    string? BreakId,
    StatementBreakType? BreakType,
    StatementValidationSeverity? Severity,
    StatementMatchTier? MatchTier,
    string? StatementReference,
    string? Description,
    decimal? StatementAmount,
    decimal? BookAmount,
    decimal? Delta,
    decimal? Tolerance,
    string? Currency,
    DateTimeOffset? CreatedAtUtc,
    string? Status,
    string? InternalReference = null,
    string? Owner = null,
    DateTimeOffset? LastObservedAtUtc = null,
    string? RecommendedAction = null,
    string? EvidenceLink = null);

/// <summary>
/// Operator case opened to investigate and close one or more statement reconciliation breaks.
/// </summary>
public sealed record StatementReconciliationCaseDto(
    string? CaseId,
    string? RunId,
    string? Status,
    string? Priority,
    string? Title,
    string? Summary,
    IReadOnlyList<string>? BreakIds,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? LastUpdatedAtUtc,
    string? LastUpdatedBy,
    string? Owner = null,
    DateTimeOffset? DueAtUtc = null,
    DateTimeOffset? ResolvedAtUtc = null,
    string? ResolutionCode = null,
    string? ResolutionSummary = null,
    string? EvidenceLink = null);

/// <summary>
/// Shared workstation payload for a statement reconciliation run and its normalized evidence.
/// </summary>
public sealed record StatementRunDto(
    string? RunId,
    StatementRunStatus? Status,
    StatementSourceDto? Source,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? SourceFileName,
    string? SourceFileHash,
    string? MappingProfileId,
    int? MappingProfileVersion,
    string? ToleranceProfileId,
    int? ToleranceProfileVersion,
    string? ImportedBy,
    DateTimeOffset? ImportedAtUtc,
    IReadOnlyList<StatementValidationIssueDto>? ValidationIssues,
    IReadOnlyList<StatementNormalizedPositionDto>? Positions,
    IReadOnlyList<StatementNormalizedCashDto>? CashBalances,
    IReadOnlyList<StatementNormalizedTransactionDto>? Transactions,
    StatementMatchSummaryDto? MatchSummary,
    IReadOnlyList<StatementBreakDto>? Breaks,
    IReadOnlyList<StatementReconciliationCaseDto>? Cases,
    string? ImportId = null,
    string? FundProfileId = null,
    Guid? FundAccountId = null,
    string? Notes = null);
/// <summary>
/// Request to create a statement reconciliation run from an already accessible source file.
/// Endpoint implementations keep import and reconciliation behavior in shared services.
/// </summary>
public sealed record StatementRunCreateDto(
    string Broker,
    string SourceInstitution,
    string FundAccountId,
    string ExternalAccountId,
    DateOnly StatementPeriodStart,
    DateOnly StatementPeriodEnd,
    string SourcePath,
    string OriginalFileName,
    string MappingProfileId,
    string ToleranceProfileId,
    string ImportedBy,
    string? SourceFileHash = null,
    string? Notes = null);

/// <summary>
/// Request to trigger reconciliation matching for an existing statement run.
/// </summary>
public sealed record StatementRunReconcileRequestDto(
    string? Actor = null,
    string? Reason = null,
    bool Force = false);

/// <summary>
/// Lightweight statement-run index row returned by the workstation reconciliation API.
/// </summary>
public sealed record StatementRunSummaryDto(
    string RunId,
    string ImportId,
    StatementRunStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int PositionMatches,
    int CashMatches,
    int TransactionMatches,
    int OpenExceptionCount);

/// <summary>
/// Validation envelope for a statement reconciliation run.
/// </summary>
public sealed record StatementRunValidationDto(
    string RunId,
    IReadOnlyList<StatementValidationIssueDto> Issues,
    bool IsBlocked);

/// <summary>
/// Open or retained break row scoped to a statement reconciliation run.
/// </summary>
public sealed record StatementRunBreakDto(
    string BreakId,
    string RunId,
    string ImportId,
    string SourceReference,
    StatementBreakType BreakType,
    string Category,
    decimal Delta,
    decimal Tolerance,
    bool ToleranceBreached,
    DateTimeOffset CreatedAtUtc,
    string Status);
/// <summary>
/// Backward-compatible open exception projection for statement reconciliation queues.
/// </summary>
public sealed record StatementRunExceptionDto(
    string BreakId,
    string RunId,
    string ImportId,
    string SourceReference,
    string BreakCode,
    string Category,
    decimal Delta,
    decimal Tolerance,
    bool ToleranceBreached,
    string CreatedAtUtc,
    string Status);
