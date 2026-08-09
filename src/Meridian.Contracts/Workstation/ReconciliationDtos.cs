using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Banking;
using Meridian.Contracts.Operations;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Source type participating in a reconciliation comparison.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationSourceKind>))]
public enum ReconciliationSourceKind : byte
{
    Unknown = 0,
    Portfolio = 1,
    Ledger = 2,
    Bank = 3,
    ExternalStatement = Bank,
    Cash = 4
}

/// <summary>
/// Current workflow state for a reconciliation break.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationBreakStatus>))]
public enum ReconciliationBreakStatus : byte
{
    Open = 0,
    Matched = 1,
    Investigating = 2,
    Resolved = 3,
    PartialMatch = 4
}

/// <summary>
/// Materiality level for a reconciliation break.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationBreakSeverity>))]
public enum ReconciliationBreakSeverity : byte
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>
/// Canonical classification for reconciliation outcomes.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationBreakCategory>))]
public enum ReconciliationBreakCategory : byte
{
    AmountMismatch = 0,
    MissingLedgerCoverage = 1,
    MissingPortfolioCoverage = 2,
    ClassificationGap = 3,
    TimingMismatch = 4,
    MissingBankCoverage = 5,
    CashMismatch = 6,
    MissingCashCoverage = 7,
    MissingExternalStatementCoverage = 8,
    ExternalStatementMismatch = 9,
    PartialMatch = 10
}

/// <summary>
/// Request to create a reconciliation run for a recorded strategy run.
/// </summary>
public sealed record ReconciliationRunRequest(
    string RunId,
    decimal AmountTolerance = 0.01m,
    int MaxAsOfDriftMinutes = 5,
    /// <summary>
    /// Optional banking entity identifier.  When provided, bank transactions for this
    /// entity are fetched and included as additional reconciliation checks alongside
    /// the portfolio/ledger comparison.
    /// </summary>
    Guid? BankEntityId = null);

/// <summary>
/// Summary of a completed reconciliation run.
/// </summary>
public sealed record ReconciliationRunSummary(
    string ReconciliationRunId,
    string RunId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PortfolioAsOf,
    DateTimeOffset? LedgerAsOf,
    int MatchCount,
    int BreakCount,
    int OpenBreakCount,
    bool HasTimingDrift,
    decimal AmountTolerance,
    int MaxAsOfDriftMinutes,
    int SecurityIssueCount = 0,
    bool HasSecurityCoverageIssues = false,
    int BankTransactionCount = 0,
    int BankBreakCount = 0,
    int ExpectedAccountingEventCount = 0,
    int ExpectedJournalPreviewCount = 0,
    int SecurityMasterAccountingIssueCount = 0,
    bool HasSecurityMasterAccountingIssues = false)
{
    /// <summary>
    /// Optional banking entity scope used when this reconciliation run included bank inputs.
    /// Kept outside the positional constructor so persisted legacy payloads remain readable.
    /// </summary>
    public Guid? BankEntityId { get; init; }
}

/// <summary>
/// Successful comparison row emitted by the reconciliation engine.
/// </summary>
public sealed record ReconciliationMatchDto(
    string CheckId,
    string Label,
    string ExpectedSource,
    string ActualSource,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal Variance,
    DateTimeOffset? ExpectedAsOf,
    DateTimeOffset? ActualAsOf)
{
    /// <summary>Canonical identity of the logical check resolved by this match.</summary>
    public string? LogicalBreakIdentity { get; init; }

    public Guid? BankEntityId { get; init; }

    public string? SourceScope { get; init; }

    public OperationsContinuityCorrelationKeysDto? CorrelationKeys { get; init; }
}

/// <summary>
/// Break row emitted by the reconciliation engine.
/// </summary>
public sealed record ReconciliationBreakDto(
    string CheckId,
    string Label,
    ReconciliationBreakCategory Category,
    ReconciliationBreakStatus Status,
    string MissingSource,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal Variance,
    ReconciliationBreakSeverity Severity,
    string Reason,
    DateTimeOffset? ExpectedAsOf,
    DateTimeOffset? ActualAsOf,
    OperationsContinuityCorrelationKeysDto? CorrelationKeys = null,
    IReadOnlyList<ReconciliationBreakMeasureDto>? Measures = null)
{
    /// <summary>
    /// When this logical break was first observed. Reconciliation retries retain this value even
    /// when the current variance, category, or reason changes. Legacy records may omit it.
    /// </summary>
    public DateTimeOffset? FirstObservedAt { get; init; }

    /// <summary>
    /// Canonical identity used to carry first-observation continuity across retries. The identity
    /// excludes variance, category, reason, and status so those attributes may evolve without
    /// creating a new logical break.
    /// </summary>
    public string? LogicalBreakIdentity { get; init; }

    public Guid? BankEntityId { get; init; }

    public string? SourceScope { get; init; }
}

/// <summary>
/// Builds a stable, case-normalized identity for one logical reconciliation check and its source
/// scope. The hash keeps caller-provided identifiers from becoming ambiguous delimiter-separated
/// keys while retaining a versioned contract prefix.
/// </summary>
public static class ReconciliationLogicalBreakIdentity
{
    public static string Create(
        string checkId,
        Guid? bankEntityId = null,
        string? sourceScope = null,
        OperationsContinuityCorrelationKeysDto? correlationKeys = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkId);

        var canonical = new StringBuilder();
        Append(canonical, "version", "1");
        Append(canonical, "check", Normalize(checkId));
        Append(canonical, "bankEntity", bankEntityId?.ToString("N") ?? string.Empty);
        Append(canonical, "sourceScope", Normalize(sourceScope));
        Append(canonical, "run", Normalize(correlationKeys?.RunId));
        Append(canonical, "fundAccount", correlationKeys?.FundAccountId?.ToString("N") ?? string.Empty);
        Append(canonical, "portfolioSnapshot", Normalize(correlationKeys?.PortfolioSnapshotId));
        Append(canonical, "ledgerBatch", Normalize(correlationKeys?.LedgerBatchId));
        Append(canonical, "ledgerPostingGroup", Normalize(correlationKeys?.LedgerPostingGroupId));
        Append(canonical, "reconciliationCase", Normalize(correlationKeys?.ReconciliationCaseId));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return $"reconciliation-break:v1:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    internal static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static void Append(StringBuilder builder, string name, string value) =>
        builder.Append(name).Append(':').Append(value.Length).Append(':').Append(value).Append('\n');
}

/// <summary>
/// Security Master coverage issue attached to a reconciliation run.
/// </summary>
public sealed record ReconciliationSecurityCoverageIssueDto(
    string Source,
    string Symbol,
    string? AccountName,
    string Reason,
    string? Code = null,
    ReconciliationBreakSeverity Severity = ReconciliationBreakSeverity.Medium,
    string? EvidenceLink = null);

/// <summary>
/// Security Master accounting event type generated from instrument terms and schedules.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExpectedAccountingEventKindDto>))]
public enum ExpectedAccountingEventKindDto : byte
{
    AccrueInterestIncome = 0,
    ReversePriorAccrual = 1,
    RecognizeCouponIncome = 2,
    ReceiveCashInterest = 3,
    AmortizePremium = 4,
    AccreteDiscount = 5,
    RecognizePrincipalPaydown = 6,
    ReduceCostBasisForFactorPaydown = 7,
    RecognizeRealizedGainLoss = 8,
    RecordMaturityProceeds = 9,
    RecordCallProceeds = 10,
    RecordDividendReceivable = 11,
    RecordDividendIncome = 12,
    RecordFxRemeasurement = 13
}

/// <summary>
/// Deterministic input snapshot used to generate an expected accounting event.
/// </summary>
public sealed record AccrualInputSnapshotDto(
    Guid SecurityId,
    string Symbol,
    string AccountId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal ParAmount,
    decimal? CouponRate,
    string? CouponType,
    string? DayCountConvention,
    int? PaymentFrequencyPerYear,
    decimal? PriorFactor,
    decimal? CurrentFactor,
    string SourceHash);

/// <summary>
/// Result of a Security Master accrual calculation.
/// </summary>
public sealed record AccrualCalculationResultDto(
    string EventId,
    Guid SecurityId,
    string Symbol,
    string AccountId,
    DateOnly AccrualStartDate,
    DateOnly AccrualEndDate,
    int AccrualDays,
    decimal DayCountFraction,
    decimal AccruedAmount,
    string Currency,
    AccrualInputSnapshotDto InputSnapshot);

/// <summary>
/// Expected accounting event generated from Security Master accounting rules.
/// </summary>
public sealed record ExpectedAccountingEventDto(
    string EventId,
    ExpectedAccountingEventKindDto EventKind,
    Guid SecurityId,
    string Symbol,
    string AccountId,
    DateOnly EventDate,
    decimal ExpectedAmount,
    decimal PrincipalAmount,
    decimal IncomeAmount,
    string Currency,
    string IdempotencyKey,
    string Provenance,
    AccrualInputSnapshotDto InputSnapshot)
{
    public EconomicEventReferenceDto? EconomicEvent { get; init; }

    public ProjectionLineageDto? ProjectionLineage { get; init; }

    public IReadOnlyList<string> EvidenceLinks { get; init; } = [];
}

/// <summary>
/// Preview line for a balanced journal candidate generated from an expected event.
/// </summary>
public sealed record ExpectedJournalPreviewLineDto(
    string AccountName,
    string AccountType,
    string? Symbol,
    decimal Debit,
    decimal Credit);

/// <summary>
/// Balanced journal preview candidate. Posting remains a separate approval-gated workflow.
/// </summary>
public sealed record ExpectedJournalPreviewDto(
    string JournalPreviewId,
    string ExpectedEventId,
    string Description,
    DateOnly EventDate,
    bool IsBalanced,
    bool RequiresOperatorApproval,
    string IdempotencyKey,
    IReadOnlyList<ExpectedJournalPreviewLineDto> Lines);

/// <summary>
/// Structured posture or reconciliation issue from Security Master-driven accounting.
/// </summary>
public sealed record SecurityMasterAccountingIssueDto(
    string Code,
    string Source,
    string Symbol,
    string? AccountId,
    string Reason,
    ReconciliationBreakSeverity Severity,
    string? EvidenceLink = null,
    decimal? ExpectedAmount = null,
    decimal? ActualAmount = null);

/// <summary>
/// Full detail payload for a single reconciliation run.
/// </summary>
public sealed record ReconciliationRunDetail(
    ReconciliationRunSummary Summary,
    IReadOnlyList<ReconciliationMatchDto> Matches,
    IReadOnlyList<ReconciliationBreakDto> Breaks,
    IReadOnlyList<ReconciliationSecurityCoverageIssueDto>? SecurityCoverageIssues = null,
    /// <summary>
    /// Security Master classification keyed by ticker symbol, populated for every
    /// symbol resolved at reconciliation time from the shared workstation instrument layer.
    /// Suitable for governance and audit reporting.
    /// </summary>
    IReadOnlyDictionary<string, SecurityClassificationSummaryDto>? SecurityClassifications = null,
    IReadOnlyList<ExpectedAccountingEventDto>? ExpectedAccountingEvents = null,
    IReadOnlyList<AccrualCalculationResultDto>? AccrualCalculations = null,
    IReadOnlyList<ExpectedJournalPreviewDto>? ExpectedJournalPreviews = null,
    IReadOnlyList<SecurityMasterAccountingIssueDto>? SecurityMasterAccountingIssues = null);

/// <summary>
/// Operator queue state for a reconciliation break.
/// </summary>
public enum ReconciliationBreakQueueStatus : byte
{
    Open = 0,
    InReview = 1,
    Resolved = 2,
    Dismissed = 3,
    SignedOff = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationCaseLifecycleState>))]
public enum ReconciliationCaseLifecycleState : byte
{
    Open = 0,
    InReview = 1,
    AwaitingApproval = 2,
    Approved = 3,
    Posted = 4,
    Reopened = 5,
    Superseded = 6,
    Investigating = 7,
    AwaitingEvidence = 8,
    Resolved = 9,
    SignedOff = 10
}

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationCasePriority>))]
public enum ReconciliationCasePriority : byte
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationCaseSlaState>))]
public enum ReconciliationCaseSlaState : byte
{
    NotStarted = 0,
    OnTrack = 1,
    Warning = 2,
    Breached = 3,
    Paused = 4,
    Stopped = 5
}

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationCaseCommentVisibility>))]
public enum ReconciliationCaseCommentVisibility : byte
{
    Internal = 0,
    CloseEvidence = 1,
    ExternalSummary = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationCaseworkAction>))]
public enum ReconciliationCaseworkAction : byte
{
    Assign = 0,
    ChangePriority = 1,
    TransitionStatus = 2,
    AddComment = 3,
    EditComment = 4,
    DeleteComment = 5,
    SetRootCause = 6,
    SetResolution = 7,
    LinkEvidence = 8,
    SignOff = 9,
    Reopen = 10,
    Resolve = 11,
    Waive = 12,
    Supersede = 13
}

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationBreakMeasureKindDto>))]
public enum ReconciliationBreakMeasureKindDto : byte
{
    Value = 0,
    Quantity = 1,
    CostBasis = 2
}

/// <summary>
/// One typed, item-level reconciliation comparison. A measure that cannot be calculated must say
/// why instead of substituting zeroes that could be mistaken for an actual tie-out.
/// </summary>
public sealed record ReconciliationBreakMeasureDto(
    ReconciliationBreakMeasureKindDto Kind,
    decimal? Expected,
    decimal? Actual,
    decimal? Variance,
    decimal? Tolerance,
    string Unit,
    string? UnavailableReason = null);

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationBreakDispositionDto>))]
public enum ReconciliationBreakDispositionDto : byte
{
    Resolved = 0,
    Waived = 1,
    Superseded = 2
}

/// <summary>
/// Aggregate readiness state for reconciliation tolerance calibration and governance sign-off.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationCalibrationStatusDto>))]
public enum ReconciliationCalibrationStatusDto : byte
{
    Ready = 0,
    ReviewRequired = 1,
    Blocked = 2
}

/// <summary>
/// Work item shown in the reconciliation break queue.
/// </summary>
public sealed record ReconciliationBreakQueueItem(
    string BreakId,
    string RunId,
    string StrategyName,
    ReconciliationBreakCategory Category,
    ReconciliationBreakQueueStatus Status,
    decimal Variance,
    string Reason,
    string? AssignedTo,
    DateTimeOffset DetectedAt,
    DateTimeOffset LastUpdatedAt,
    string? ReviewedBy = null,
    DateTimeOffset? ReviewedAt = null,
    string? ResolvedBy = null,
    DateTimeOffset? ResolvedAt = null,
    string? ResolutionNote = null,
    ReconciliationBreakSeverity Severity = ReconciliationBreakSeverity.Medium,
    string? ExceptionRoute = null,
    string? ToleranceProfileId = null,
    decimal? ToleranceBand = null,
    string? RequiredSignoffRole = null,
    string? SignoffStatus = null,
    string? FundAccountId = null,
    string? ExplainabilitySummary = null,
    string? RoutingTarget = null,
    string? RoutingDetail = null,
    string? RecommendedAction = null,
    ReconciliationCaseLifecycleState LifecycleState = ReconciliationCaseLifecycleState.Open,
    string? LifecycleRationale = null,
    string? ExternalAccountId = null,
    string? CustodianId = null,
    string? UpstreamSyncCursor = null,
    DateTimeOffset? LastUpstreamSyncAt = null,
    IReadOnlyList<ReconciliationCaseSignoffRecord>? SignoffHistory = null,
    IReadOnlyList<ReconciliationCaseStateTransition>? StateTransitions = null,
    string? Team = null,
    string? Counterparty = null,
    ReconciliationBreakScore? Score = null,
    DateTimeOffset? SlaDueAt = null,
    bool SlaBreached = false,
    string? AssigneeId = null,
    string? AssigneeDisplayName = null,
    ReconciliationCasePriority Priority = ReconciliationCasePriority.Normal,
    string? SlaPolicyId = null,
    DateTimeOffset? SlaWarningAt = null,
    DateTimeOffset? SlaBreachedAt = null,
    ReconciliationCaseSlaState SlaState = ReconciliationCaseSlaState.OnTrack,
    string? AgeBand = null,
    double BusinessAgeHours = 0,
    string? RootCauseCode = null,
    string? ResolutionCode = null,
    string? SignedOffBy = null,
    DateTimeOffset? SignedOffAt = null,
    string? SignOffNote = null,
    string? ReopenedBy = null,
    DateTimeOffset? ReopenedAt = null,
    string? ReopenReason = null,
    long Version = 0,
    IReadOnlyList<ReconciliationCaseComment>? Comments = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    int CommentCount = 0,
    int EvidenceCount = 0,
    DateTimeOffset? LastActivityAt = null,
    string? SourceType = null,
    string? SourceSystem = null,
    string? SourceReference = null,
    string? SourceImportId = null,
    string? SourceBreakId = null,
    string? SourceFingerprint = null,
    ReconciliationBreakExplanationDto? BreakExplanation = null,
    Guid? LedgerBookId = null,
    IReadOnlyList<ReconciliationBreakMeasureDto>? Measures = null,
    ReconciliationBreakDispositionDto? Disposition = null,
    string? DispositionReason = null,
    string? SupersedingBreakId = null,
    string? DispositionApprovedBy = null,
    string? DispositionApprovalReference = null,
    string? DispositionEvidenceHash = null,
    DateTimeOffset? DisposedAt = null,
    IReadOnlyList<string>? BlockedOutputs = null,
    string? AccountingPeriodId = null,
    DateOnly? AsOfDate = null,
    string? DataProvenanceToken = null)
{
    /// <summary>
    /// Immutable tenant identity retained by the queue persistence boundary. Legacy records may
    /// be unscoped; tenant-bound production reads must not surface those records.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Immutable company identity retained alongside <see cref="TenantId"/>. Tenant and company
    /// must either both be present or both be absent on a legacy record.
    /// </summary>
    public string? CompanyId { get; init; }

    /// <summary>
    /// Reporting/close fund-profile identity. Kept separate from the source fund-account identity
    /// and init-only to preserve the established positional constructor and deconstruction ABI.
    /// </summary>
    public string? FundProfileId { get; init; }
}

/// <summary>
/// Server-resolved access scope for reconciliation queue reads and mutations. This value is never
/// accepted from a workstation request body; endpoint adapters build it from the authenticated
/// session.
/// </summary>
public sealed record ReconciliationBreakQueueScope
{
    public ReconciliationBreakQueueScope(string tenantId, string companyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        TenantId = tenantId.Trim();
        CompanyId = companyId.Trim();
    }

    public string TenantId { get; }

    public string CompanyId { get; }

    public bool Owns(ReconciliationBreakQueueItem? item)
        => item is not null
           && !string.IsNullOrWhiteSpace(item.TenantId)
           && !string.IsNullOrWhiteSpace(item.CompanyId)
           && string.Equals(item.TenantId.Trim(), TenantId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(item.CompanyId.Trim(), CompanyId, StringComparison.OrdinalIgnoreCase);
}

public sealed record ReconciliationBreakExplanationDto(
    string Summary,
    IReadOnlyList<string> SourceSystems,
    string ProbableCause,
    string LedgerImpact,
    string SuggestedNextAction,
    IReadOnlyList<string> EvidenceLinks);


public sealed record ReconciliationCaseComment(
    string CommentId,
    string? ParentCommentId,
    string AuthorId,
    string AuthorDisplayName,
    ReconciliationCaseCommentVisibility Visibility,
    string Body,
    IReadOnlyList<string> EvidenceLinks,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt = null,
    DateTimeOffset? DeletedAt = null,
    string? DeletedBy = null,
    IReadOnlyList<string>? Mentions = null,
    IReadOnlyList<string>? LinkedEvidenceIds = null,
    string? PreviousTextHash = null,
    string? EditReason = null,
    string? DeleteReason = null,
    ReconciliationCaseLifecycleState? StatusTransition = null);

public sealed record ReconciliationSlaPolicy(
    string PolicyId,
    string? FundId,
    string? AccountId,
    string? BreakType,
    ReconciliationBreakSeverity Severity,
    ReconciliationCasePriority Priority,
    string TimeZoneId,
    TimeOnly BusinessDayStart,
    TimeOnly BusinessDayEnd,
    int DueBusinessHours,
    int WarningBusinessHours,
    bool StopOnResolved = true,
    bool StopOnSignedOff = false,
    bool PauseAwaitingEvidence = true,
    string? BusinessCalendarId = null,
    IReadOnlyList<string>? HolidayCalendarIds = null,
    IReadOnlyList<string>? PauseRules = null,
    IReadOnlyList<string>? StopRules = null,
    IReadOnlyList<string>? EscalationRules = null);

public sealed record ReconciliationSlaComputationResult(
    string PolicyId,
    DateTimeOffset DueAt,
    DateTimeOffset WarningAt,
    DateTimeOffset? BreachedAt,
    ReconciliationCaseSlaState State,
    string AgeBand,
    double BusinessAgeHours);


public sealed record ReconciliationTaxonomyValue(
    string Code,
    string DisplayName,
    int Version,
    bool IsActive,
    IReadOnlyList<string>? RequiredEvidencePrefixes = null);

public sealed record ReconciliationTaxonomySnapshot(
    int Version,
    IReadOnlyList<ReconciliationTaxonomyValue> RootCauses,
    IReadOnlyList<ReconciliationTaxonomyValue> ResolutionCodes);

public sealed record ReconciliationCaseworkCommand(
    string BreakId,
    ReconciliationCaseworkAction Action,
    string Actor,
    string CommandId,
    string CorrelationId,
    string Source,
    long ExpectedVersion,
    string? Reason = null,
    string? Assignee = null,
    ReconciliationCasePriority? Priority = null,
    ReconciliationCaseLifecycleState? Status = null,
    string? Note = null,
    string? RootCauseCode = null,
    string? ResolutionCode = null,
    string? CausationId = null,
    string? CommentId = null,
    string? ParentCommentId = null,
    ReconciliationCaseCommentVisibility Visibility = ReconciliationCaseCommentVisibility.Internal,
    IReadOnlyList<string>? EvidenceLinks = null,
    bool Privileged = false,
    ReconciliationCaseLifecycleState? StatusTransition = null,
    IReadOnlyList<string>? Mentions = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    string? ApprovalActor = null,
    string? ApprovalReference = null,
    string? SupersedingBreakId = null)
{
    /// <summary>
    /// Server-resolved close scope bound by the statement/Operations evidence handoff. Init-only
    /// preserves the established command constructor and deconstruction ABI.
    /// </summary>
    public ReconciliationCaseworkCloseScopeDto? CloseScope { get; init; }
}

/// <summary>
/// Exact accounting-close scope retained when statement casework has been handed to the matching
/// Operations Continuity workflow. This scope is queue-owned evidence; callers cannot use it to
/// post, approve, or close a ledger period.
/// </summary>
public sealed record ReconciliationCaseworkCloseScopeDto(
    string FundProfileId,
    Guid LedgerBookId,
    Guid AccountingPeriodId,
    DateOnly AsOfDate);

public sealed record ReconciliationBulkCaseworkRequest(
    IReadOnlyList<string> BreakIds,
    ReconciliationCaseworkAction Action,
    string Actor,
    string CommandId,
    string CorrelationId,
    string Source,
    string IdempotencyKey,
    bool DryRun,
    bool AllowPartialSuccess,
    string? Reason = null,
    string? Assignee = null,
    ReconciliationCasePriority? Priority = null,
    ReconciliationCaseLifecycleState? Status = null,
    string? Note = null,
    string? RootCauseCode = null,
    string? ResolutionCode = null,
    int MaxCaseCount = 100,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? ApprovalActor = null,
    string? ApprovalReference = null,
    string? SupersedingBreakId = null);

public sealed record ReconciliationBulkCaseworkCaseResult(
    string BreakId,
    bool Succeeded,
    bool WouldSucceed,
    string? Error,
    ReconciliationBreakQueueItem? Item);

public sealed record ReconciliationBulkCaseworkResult(
    string BulkActionId,
    string IdempotencyKey,
    bool DryRun,
    int RequestedCount,
    int SucceededCount,
    int FailedCount,
    IReadOnlyList<ReconciliationBulkCaseworkCaseResult> Results)
{
    /// <summary>Canonical hash of the complete admitted bulk request.</summary>
    public string InputHashSha256 { get; init; } = ComputeCompatibilityInputHash(
        BulkActionId,
        IdempotencyKey,
        DryRun,
        RequestedCount,
        SucceededCount,
        FailedCount);

    /// <summary>
    /// Verified terminal receipt. Queue implementations replace this compatibility receipt with
    /// one bound to the full request, persisted receipt, and retained audit evidence.
    /// </summary>
    public VerifiedOperationOutcome Outcome { get; init; } = CreateCompatibilityOutcome(
        BulkActionId,
        IdempotencyKey,
        DryRun,
        RequestedCount,
        SucceededCount,
        FailedCount);

    private static VerifiedOperationOutcome CreateCompatibilityOutcome(
        string bulkActionId,
        string idempotencyKey,
        bool dryRun,
        int requestedCount,
        int succeededCount,
        int failedCount)
    {
        var now = DateTimeOffset.UtcNow;
        var inputHash = ComputeCompatibilityInputHash(
            bulkActionId,
            idempotencyKey,
            dryRun,
            requestedCount,
            succeededCount,
            failedCount);
        var evidenceId = "reconciliation-bulk-result";
        var allSucceeded = requestedCount > 0 && failedCount == 0 && succeededCount == requestedCount;
        var partiallySucceeded = succeededCount > 0;
        var state = allSucceeded
            ? OperationTerminalState.Succeeded
            : partiallySucceeded
                ? OperationTerminalState.CompletedWithWarnings
                : OperationTerminalState.Blocked;
        var issues = allSucceeded
            ? Array.Empty<OperationIssue>()
            : partiallySucceeded
                ?
                [
                    new OperationIssue(
                        "partial-casework",
                        $"{failedCount} of {requestedCount} reconciliation cases did not satisfy the requested action.",
                        OperationIssueSeverity.Warning,
                        EvidenceId: evidenceId)
                ]
                :
                [
                    new OperationIssue(
                        "casework-blocked",
                        "No reconciliation cases satisfied the requested bulk action.",
                        OperationIssueSeverity.Error,
                        EvidenceId: evidenceId)
                    {
                        IsBlocking = true
                    }
                ];
        var recovery = allSucceeded
            ? Array.Empty<OperationRecoveryAction>()
            :
            [
                new OperationRecoveryAction(
                    "review-case-failures",
                    "Review case failures",
                    "Review each failed case, satisfy its prerequisites, and retry using a new command id and idempotency key.",
                    Retryable: true,
                    RequiresHumanAction: true)
                {
                    EvidenceIds = [evidenceId]
                }
            ];

        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            OperationId: string.IsNullOrWhiteSpace(bulkActionId)
                ? $"reconciliation-bulk:{Guid.NewGuid():N}"
                : $"reconciliation-bulk:{bulkActionId}",
            OperationKind: dryRun ? "reconciliation.casework.bulk-dry-run" : "reconciliation.casework.bulk-execute",
            State: state,
            StartedAtUtc: now,
            CompletedAtUtc: now,
            AttemptNumber: 1,
            CorrelationId: string.IsNullOrWhiteSpace(bulkActionId) ? "reconciliation-bulk" : bulkActionId,
            InputHashSha256: inputHash,
            Postconditions:
            [
                new OperationPostcondition(
                    "bulk-casework-terminalized",
                    "Every admitted case has an explicit terminal result.",
                    allSucceeded || partiallySucceeded
                        ? OperationPostconditionState.Satisfied
                        : OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: [evidenceId])
            ],
            Evidence:
            [
                new OperationEvidenceReference(
                    evidenceId,
                    "bulk-result",
                    $"Bulk casework returned {succeededCount} successful and {failedCount} failed case results.",
                    Uri: $"urn:sha256:{inputHash}",
                    ContentHashSha256: inputHash,
                    CapturedAtUtc: now)
            ],
            Artifacts: [],
            Issues: issues,
            Recovery: recovery));
    }

    private static string ComputeCompatibilityInputHash(
        string bulkActionId,
        string idempotencyKey,
        bool dryRun,
        int requestedCount,
        int succeededCount,
        int failedCount)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{bulkActionId}|{idempotencyKey}|{dryRun}|{requestedCount}|{succeededCount}|{failedCount}")))
            .ToLowerInvariant();
}

/// <summary>
/// API-safe terminal receipt for a single reconciliation casework attempt. HTTP success only
/// means the command was evaluated; callers must use <see cref="Outcome"/> to determine whether
/// its postconditions were satisfied, blocked, or failed.
/// </summary>
public sealed record ReconciliationCaseworkOperationResult(
    string TransitionStatus,
    ReconciliationBreakQueueItem? Item,
    VerifiedOperationOutcome Outcome,
    string? Error = null,
    string? ErrorCode = null,
    IReadOnlyList<string>? ValidationIssues = null);

public sealed record ReconciliationCaseSignoffRecord(
    string Actor,
    string Role,
    string Decision,
    string? Note,
    DateTimeOffset SignedAt,
    string? InvalidatedBySyncCursor = null,
    DateTimeOffset? InvalidatedAt = null);



public sealed record ReconciliationBreakScore(
    int SeverityScore,
    int PriorityScore,
    decimal MaterialityComponent,
    double AgeHours,
    int CounterpartyCriticalityComponent,
    int RecurringPatternComponent,
    bool IsHighPriority,
    DateTimeOffset? SlaDueAt = null,
    DateTimeOffset? SlaBreachAt = null);

public sealed record ReconciliationCaseStateTransition(
    string TransitionId,
    ReconciliationCaseLifecycleState From,
    ReconciliationCaseLifecycleState To,
    string Actor,
    string? Rationale,
    DateTimeOffset OccurredAt,
    IReadOnlyList<string>? EvidenceReferences = null,
    string? PreviousHash = null,
    string? EntryHash = null);

public enum ReconciliationCaseTransitionAction : byte
{
    StartReview = 0,
    RequestApproval = 1,
    Approve = 2,
    Post = 3,
    Reopen = 4,
    Supersede = 5
}

public sealed record ReconciliationCaseTransitionCommand(
    string BreakId,
    ReconciliationCaseTransitionAction Action,
    string Actor,
    string Reason,
    IReadOnlyList<string> EvidenceReferences,
    string? Role = null,
    string? SupersedingBreakId = null);

/// <summary>
/// Per-profile rollup for reconciliation tolerance calibration and exception routing.
/// </summary>
public sealed record ReconciliationCalibrationProfileSummaryDto(
    string ToleranceProfileId,
    string ExceptionRoute,
    ReconciliationBreakSeverity HighestSeverity,
    decimal? MaxToleranceBand,
    int TotalBreakCount,
    int OpenBreakCount,
    int InReviewBreakCount,
    int ResolvedBreakCount,
    int DismissedBreakCount,
    int PendingSignoffCount,
    int SignedOffCount,
    DateTimeOffset LastUpdatedAt);

/// <summary>
/// Operator-facing calibration summary for the reconciliation break queue.
/// </summary>
public sealed record ReconciliationCalibrationSummaryDto(
    DateTimeOffset AsOf,
    ReconciliationCalibrationStatusDto Status,
    string Summary,
    int TotalBreakCount,
    int ActiveBreakCount,
    int OpenBreakCount,
    int InReviewBreakCount,
    int ResolvedBreakCount,
    int DismissedBreakCount,
    int CriticalOpenBreakCount,
    int PendingSignoffCount,
    int SignedOffCount,
    int MissingCalibrationMetadataCount,
    IReadOnlyList<ReconciliationCalibrationProfileSummaryDto> Profiles)
{
    public int BreakCountTrend { get; init; }
    public decimal AutoMatchRate { get; init; }
    public decimal T0ClosureRate { get; init; }
    public int BreakCountAlertThreshold { get; init; } = 25;
    public decimal AutoMatchRateAlertThreshold { get; init; } = 0.85m;
    public decimal T0ClosureRateAlertThreshold { get; init; } = 0.90m;
    public bool BreakCountAlertTriggered => ActiveBreakCount >= BreakCountAlertThreshold;
    public bool AutoMatchRateAlertTriggered => AutoMatchRate < AutoMatchRateAlertThreshold;
    public bool T0ClosureRateAlertTriggered => T0ClosureRate < T0ClosureRateAlertThreshold;
}

/// <summary>
/// Request to move a break into active review and assign an operator.
/// </summary>
public sealed record ReviewReconciliationBreakRequest(
    string BreakId,
    string AssignedTo,
    string ReviewedBy,
    string? ReviewNote = null,
    string? Team = null);

/// <summary>
/// Request to resolve or dismiss a break with audit metadata.
/// </summary>
public sealed record ResolveReconciliationBreakRequest(
    string BreakId,
    ReconciliationBreakQueueStatus Status,
    string ResolvedBy,
    string ResolutionNote,
    string OperatorRationale,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

/// <summary>
/// Canonical schema version metadata for reconciliation ingress/egress payloads.
/// </summary>
public sealed record ReconciliationSchemaVersion(
    string ContractName,
    int Major,
    int Minor,
    int Patch,
    string ContentType = "application/vnd.meridian.reconciliation+json");

/// <summary>
/// Cross-workflow correlation metadata stamped on every reconciliation payload.
/// </summary>
public sealed record ReconciliationCorrelationContext(
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string WorkflowId,
    string? JobId = null);

/// <summary>
/// Canonical envelope for inbound/outbound reconciliation payloads.
/// </summary>
public sealed record ReconciliationPayloadEnvelope<TPayload>(
    string PayloadId,
    ReconciliationSchemaVersion Schema,
    ReconciliationCorrelationContext Correlation,
    DateTimeOffset CreatedAt,
    string Producer,
    string Direction,
    string? IdempotencyKey,
    TPayload Payload);

/// <summary>
/// Queue orchestration controls and replay metadata for resilient reconciliation jobs.
/// </summary>
public sealed record ReconciliationJobControl(
    string JobId,
    string IdempotencyKey,
    int Attempt,
    int MaxAttempts,
    bool DeadLettered,
    string? DeadLetterReason,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? NextAttemptAt,
    string BackpressureBucket);

/// <summary>
/// Processing telemetry summary for SLA and throughput observability.
/// </summary>
public sealed record ReconciliationProcessingTelemetry(
    double MatchRate,
    double BreakRate,
    int SlaMissCount,
    double P50LatencyMs,
    double P95LatencyMs,
    double P99LatencyMs);

/// <summary>
/// Scoped rollout flags for phased reconciliation releases.
/// </summary>
public sealed record ReconciliationRolloutFlags(
    bool Enabled,
    IReadOnlyList<string> ClientIds,
    IReadOnlyList<string> TeamIds,
    IReadOnlyList<string> CounterpartyIds,
    bool AllowReplay,
    bool AllowBackfill);
