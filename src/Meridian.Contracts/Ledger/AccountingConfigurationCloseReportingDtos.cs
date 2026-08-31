using System.Text;
using System.Text.Json.Serialization;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.Ledger;

public sealed record DimensionMappingProfileDto(
    string ProfileId,
    string DisplayName,
    string ProviderId,
    LedgerDimensionSetDto MeridianDimensions,
    LedgerDimensionSetDto ExternalDimensions,
    AccountingCertificationStateDto CertificationState = AccountingCertificationStateDto.Draft,
    IReadOnlyList<AccountingConfigurationValidationIssueDto>? ValidationIssues = null)
{
    public IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues { get; init; } =
        ValidationIssues ?? [];
}

public sealed record ExternalGlMappingProfileDto(
    string ProfileId,
    string ProviderId,
    string DisplayName,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<DimensionMappingProfileDto> DimensionMappings,
    IReadOnlyDictionary<string, string>? AccountMappings = null,
    AccountingCertificationStateDto CertificationState = AccountingCertificationStateDto.Draft)
{
    public IReadOnlyDictionary<string, string> AccountMappings { get; init; } =
        AccountMappings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record ExternalGlExportCertificationDto(
    string CertificationId,
    AccountingCertificationStateDto State,
    string Actor,
    DateTimeOffset RecordedAtUtc,
    string Summary,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record ExternalGlExportLineDto(
    string ExportLineId,
    string ReconciliationRowId,
    AccountingSystemReconciliationStatusDto SourceStatus,
    string MeridianAccountCode,
    string ExternalAccountId,
    string AccountName,
    string Currency,
    decimal Debit,
    decimal Credit,
    decimal NetAmount,
    LedgerDimensionSetDto? MeridianDimensions = null,
    LedgerDimensionSetDto? ExternalDimensions = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

[JsonConverter(typeof(JsonStringEnumConverter<ExternalGlExportReconciliationSafeguardStateDto>))]
public enum ExternalGlExportReconciliationSafeguardStateDto
{
    MissingEvidence = 0,
    Blocked = 1,
    Ready = 2,
    Certified = 3
}

public sealed record ExternalGlExportPackageDto(
    string ExportPackageId,
    string ProviderId,
    string FundProfileId,
    Guid? LedgerBookId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    bool PostingEnabled,
    string PostingDisabledReason,
    IReadOnlyList<Guid> JournalEntryIds,
    IReadOnlyList<string> EvidenceLinks,
    ExternalGlExportCertificationDto? Certification = null,
    IReadOnlyList<AccountingConfigurationValidationIssueDto>? ValidationIssues = null,
    IReadOnlyList<ExternalGlExportLineDto>? GeneratedLines = null,
    string? MappingProfileId = null,
    string? ReconciliationId = null,
    bool RequireBalancedReconciliation = false,
    ExternalGlExportReconciliationSafeguardStateDto ReconciliationSafeguardState = ExternalGlExportReconciliationSafeguardStateDto.MissingEvidence,
    IReadOnlyList<string>? ReconciliationSafeguardIssueCodes = null,
    string? TenantId = null,
    string? CompanyId = null,
    string? ReconciliationSnapshotHash = null)
{
    public IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues { get; init; } =
        ValidationIssues ?? [];

    public IReadOnlyList<ExternalGlExportLineDto> GeneratedLines { get; init; } =
        GeneratedLines ?? [];

    public IReadOnlyList<string> ReconciliationSafeguardIssueCodes { get; init; } =
        ReconciliationSafeguardIssueCodes ?? [];
}

public sealed record ExternalGlExportPackageManifestDto(
    string ExportPackageId,
    string ProviderId,
    string FundProfileId,
    Guid? LedgerBookId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    AccountingCertificationStateDto CertificationState,
    DateTimeOffset GeneratedAtUtc,
    string ContentHash,
    string ContentType,
    string FileName,
    bool ExternalPostingAllowed,
    string PostingDisabledReason,
    string Payload,
    IReadOnlyList<ExternalGlExportLineDto>? GeneratedLines = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    IReadOnlyList<AccountingConfigurationValidationIssueDto>? ValidationIssues = null,
    string? MappingProfileId = null,
    string? ReconciliationId = null,
    bool RequireBalancedReconciliation = false,
    ExternalGlExportReconciliationSafeguardStateDto ReconciliationSafeguardState = ExternalGlExportReconciliationSafeguardStateDto.MissingEvidence,
    IReadOnlyList<string>? ReconciliationSafeguardIssueCodes = null,
    string? TenantId = null,
    string? CompanyId = null,
    string? ReconciliationSnapshotHash = null)
{
    public IReadOnlyList<ExternalGlExportLineDto> GeneratedLines { get; init; } =
        GeneratedLines ?? [];

    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];

    public IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues { get; init; } =
        ValidationIssues ?? [];

    public IReadOnlyList<string> ReconciliationSafeguardIssueCodes { get; init; } =
        ReconciliationSafeguardIssueCodes ?? [];
}

public sealed record CloseDependencyDto(
    string DependencyId,
    string DependsOnTaskId,
    string Reason);

public sealed record CloseSignOffDto(
    string SignOffId,
    string Role,
    string? Actor,
    ManualJournalEntryStatusDto ApprovalState,
    DateTimeOffset? SignedAtUtc = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? Notes = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record CloseSignOffRequirementDto(
    string RequirementId,
    string Role,
    int RequiredApprovalCount,
    int ApprovedCount,
    bool IsSatisfied,
    string EvidenceRequirement);

public sealed record CloseTaskDependencyConfigurationDto(
    string DependsOnTaskId,
    string? Reason = null);

public sealed record CloseTaskSignOffRequirementConfigurationDto(
    string Role,
    int RequiredApprovalCount,
    string? EvidenceRequirement = null);

public sealed record MaterialityPolicyDto(
    string PolicyId,
    decimal AmountThreshold,
    decimal PercentThreshold,
    string Currency,
    string ReviewRole,
    bool RequiresLateAdjustmentApproval);

public sealed record CloseTaskConfigurationDto(
    string TaskId,
    string? DisplayName = null,
    string? Owner = null,
    DateOnly? DueDate = null,
    int? RequiredApprovalCount = null,
    string? RequiredApprovalRole = null,
    string? RequiredEvidence = null,
    IReadOnlyList<string>? DependsOnTaskIds = null,
    IReadOnlyList<CloseTaskDependencyConfigurationDto>? DependencyConfigurations = null,
    IReadOnlyList<CloseTaskSignOffRequirementConfigurationDto>? SignOffRequirementConfigurations = null)
{
    public IReadOnlyList<string> DependsOnTaskIds { get; init; } =
        DependsOnTaskIds ?? [];

    public IReadOnlyList<CloseTaskDependencyConfigurationDto> DependencyConfigurations { get; init; } =
        DependencyConfigurations ?? [];

    public IReadOnlyList<CloseTaskSignOffRequirementConfigurationDto> SignOffRequirementConfigurations { get; init; } =
        SignOffRequirementConfigurations ?? [];
}

public sealed record ClosePeriodPlanConfigurationDto(
    Guid WorkflowId,
    MaterialityPolicyDto MaterialityPolicy,
    IReadOnlyList<CloseTaskConfigurationDto>? TaskConfigurations = null,
    string? ConfiguredBy = null,
    DateTimeOffset? ConfiguredAtUtc = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<CloseTaskConfigurationDto> TaskConfigurations { get; init; } =
        TaskConfigurations ?? [];

    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record UpsertClosePeriodPlanConfigurationRequestDto(
    Guid WorkflowId,
    MaterialityPolicyDto? MaterialityPolicy = null,
    IReadOnlyList<CloseTaskConfigurationDto>? TaskConfigurations = null,
    string? Actor = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    DateTimeOffset? ExpectedConfiguredAtUtc = null)
{
    public IReadOnlyList<CloseTaskConfigurationDto> TaskConfigurations { get; init; } =
        TaskConfigurations ?? [];

    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record CloseTaskDto(
    string TaskId,
    string DisplayName,
    CloseTaskStatusDto Status,
    string Owner,
    DateOnly DueDate,
    IReadOnlyList<CloseDependencyDto> Dependencies,
    IReadOnlyList<CloseSignOffDto> SignOffs,
    IReadOnlyList<string> EvidenceLinks,
    string? BlockerReason = null,
    IReadOnlyList<CloseSignOffRequirementDto>? SignOffRequirements = null)
{
    public IReadOnlyList<CloseSignOffRequirementDto> SignOffRequirements { get; init; } =
        SignOffRequirements ?? [];
}

public sealed record CloseCalendarMilestoneDto(
    string MilestoneId,
    string TaskId,
    string DisplayName,
    string Owner,
    DateOnly DueDate,
    CloseTaskStatusDto Status,
    bool IsBlocked,
    bool IsSatisfied,
    bool IsPeriodLocked,
    int DependencyCount,
    int RequiredSignOffCount,
    int ApprovedSignOffCount,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? BlockerReason = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record LateAdjustmentRequestDto(
    string RequestId,
    Guid JournalEntryId,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    decimal Amount,
    string Currency,
    string Reason,
    ManualJournalEntryStatusDto ApprovalState,
    MaterialityPolicyDto MaterialityPolicy,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? DecidedBy = null,
    DateTimeOffset? DecidedAtUtc = null,
    string? DecisionNotes = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record CreateLateAdjustmentRequestDto(
    Guid WorkflowId,
    Guid JournalEntryId,
    decimal Amount,
    string Currency,
    string Reason,
    string RequestedBy,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record ReviewLateAdjustmentRequestDto(
    Guid WorkflowId,
    string RequestId,
    ManualJournalEntryStatusDto Decision,
    string Actor,
    string Notes,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record SignOffCloseTaskRequestDto(
    Guid WorkflowId,
    string TaskId,
    string Role,
    ManualJournalEntryStatusDto Decision,
    string Actor,
    string Notes,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record CloseEvidenceReviewDto(
    string ReviewId,
    string IssueCode,
    string? TargetId,
    string ReviewedBy,
    DateTimeOffset ReviewedAtUtc,
    string Notes,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record CloseOperatingCoverageItemDto(
    string ControlId,
    string Label,
    AccountingReadinessStateDto State,
    int EvidenceCount,
    int BlockingIssueCount,
    string RequiredAction,
    IReadOnlyList<string>? EvidenceLinks = null,
    IReadOnlyList<AccountingConfigurationValidationIssueDto>? BlockingIssues = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];

    public IReadOnlyList<AccountingConfigurationValidationIssueDto> BlockingIssues { get; init; } =
        BlockingIssues ?? [];
}

public sealed record ReviewCloseEvidenceRequestDto(
    Guid WorkflowId,
    string IssueCode,
    string? TargetId,
    string Actor,
    string Notes,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record LockClosePeriodRequestDto(
    Guid WorkflowId,
    long ExpectedWorkflowVersion,
    string Actor,
    string Rationale,
    string ReportPackId,
    IReadOnlyList<string>? EvidenceLinks = null,
    IReadOnlyList<OperationsChecklistControlApprovalDto>? ChecklistControlApprovals = null,
    string? CorrelationId = null,
    string? ClosePackageId = null,
    string? ClosePackageManifestId = null,
    string? ClosePackageRetainedManifestRoute = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    bool PrepareClosingEntriesOnly = false,
    [property: JsonIgnore]
    string? ControllerRole = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];

    public IReadOnlyList<OperationsChecklistControlApprovalDto> ChecklistControlApprovals { get; init; } =
        ChecklistControlApprovals ?? [];
}

public sealed record ClosePeriodLockResultDto(
    bool IsLocked,
    ClosePeriodPlanDto? Plan,
    OperationsTransitionResultDto? Transition,
    IReadOnlyList<AccountingConfigurationValidationIssueDto>? Issues = null)
{
    public IReadOnlyList<AccountingConfigurationValidationIssueDto> Issues { get; init; } =
        Issues ?? [];

    /// <summary>
    /// Honest terminal receipt for the combined close operation. A hard-closed ledger with a
    /// pending evidence handoff is Failed, never a successful boolean with a warning hidden beside
    /// it; an already-locked idempotent replay is CompletedWithWarnings.
    /// </summary>
    public VerifiedOperationOutcome Outcome { get; init; } = CreateOutcome(IsLocked, Plan, Transition, Issues);

    private static VerifiedOperationOutcome CreateOutcome(
        bool isLocked,
        ClosePeriodPlanDto? plan,
        OperationsTransitionResultDto? transition,
        IReadOnlyList<AccountingConfigurationValidationIssueDto>? issues)
    {
        var retainedIssues = issues ?? [];
        var hasCriticalIssue = retainedIssues.Any(static issue =>
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        var state = isLocked
            ? retainedIssues.Count == 0
                ? OperationTerminalState.Succeeded
                : hasCriticalIssue
                    ? OperationTerminalState.Failed
                    : OperationTerminalState.CompletedWithWarnings
            : hasCriticalIssue || transition?.Outcome.State == OperationTerminalState.Failed
                ? OperationTerminalState.Failed
                : OperationTerminalState.Blocked;
        var now = DateTimeOffset.UtcNow;
        var correlationId = transition?.Outcome.CorrelationId
            ?? plan?.ClosePlanId
            ?? "accounting-close-period-lock";
        var canonicalInput = string.Join('\n',
            plan?.ClosePlanId ?? string.Empty,
            plan?.FundProfileId ?? string.Empty,
            plan?.LedgerBookId?.ToString("D") ?? string.Empty,
            plan?.PeriodId ?? string.Empty,
            plan?.WorkflowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            isLocked.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join('|', retainedIssues
                .OrderBy(static issue => issue.Code, StringComparer.Ordinal)
                .Select(static issue => $"{issue.Code}:{issue.Severity}:{issue.TargetId}")));
        var inputHash = Sha256Digest.ComputeUtf8(canonicalInput);
        const string evidenceId = "accounting-close-period-lock-result";
        var postconditionSatisfied = state is
            OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings;
        var outcomeIssues = retainedIssues
            .GroupBy(static issue => issue.Code, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(group => new OperationIssue(
                group.Key,
                string.Join(" | ", group
                    .Select(static issue => issue.Message)
                    .Where(static message => !string.IsNullOrWhiteSpace(message))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)),
                postconditionSatisfied
                    ? OperationIssueSeverity.Warning
                    : OperationIssueSeverity.Error,
                EvidenceId: evidenceId)
            {
                IsBlocking = state == OperationTerminalState.Blocked
            })
            .ToList();
        if (!postconditionSatisfied && outcomeIssues.Count == 0)
        {
            outcomeIssues.Add(new OperationIssue(
                state == OperationTerminalState.Failed ? "close-operation-failed" : "close-operation-blocked",
                state == OperationTerminalState.Failed
                    ? "The close operation failed before all required postconditions were verified."
                    : "The close operation is blocked by unsatisfied prerequisites.",
                OperationIssueSeverity.Error,
                EvidenceId: evidenceId)
            {
                IsBlocking = state == OperationTerminalState.Blocked
            });
        }

        var recovery = state == OperationTerminalState.Succeeded
            ? Array.Empty<OperationRecoveryAction>()
            : state == OperationTerminalState.CompletedWithWarnings
                ?
                [
                    new OperationRecoveryAction(
                        "review-close-warning",
                        "Review close warning",
                        "Review the retained close warning and its evidence before relying on downstream close outputs.",
                        Retryable: false,
                        RequiresHumanAction: true,
                        Route: "/accounting/close")
                    {
                        EvidenceIds = [evidenceId]
                    }
                ]
                :
            [
                new OperationRecoveryAction(
                    "repair-and-retry-close",
                    "Repair and retry close",
                    "Inspect the retained close, reconciliation, and reporting evidence; satisfy the reported prerequisite or repair the failed store, then retry the same governed close command.",
                    Retryable: true,
                    RequiresHumanAction: true,
                    Route: "/accounting/close")
                {
                    EvidenceIds = [evidenceId]
                }
            ];

        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            OperationId: $"accounting-close:{inputHash[..16]}",
            OperationKind: "accounting.close.period-lock",
            State: state,
            StartedAtUtc: now,
            CompletedAtUtc: now,
            AttemptNumber: 1,
            CorrelationId: correlationId,
            InputHashSha256: inputHash,
            Postconditions:
            [
                new OperationPostcondition(
                    "close-postconditions-verified",
                    "The ledger period, close workflow, reconciliation continuity, and reporting evidence handoff are verified for the requested close.",
                    postconditionSatisfied
                        ? OperationPostconditionState.Satisfied
                        : OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: [evidenceId])
            ],
            Evidence:
            [
                new OperationEvidenceReference(
                    evidenceId,
                    "close-operation-result",
                    "Accounting close result, validation issues, and workflow transition receipt.",
                    ContentHashSha256: inputHash,
                    CapturedAtUtc: now)
            ],
            Artifacts: [],
            Issues: outcomeIssues,
            Recovery: recovery));
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<ClosePostingGateStateDto>))]
public enum ClosePostingGateStateDto
{
    Unavailable = 0,
    NotRequired = 1,
    Required = 2,
    DraftQueued = 3,
    Submitted = 4,
    Approved = 5,
    Posted = 6,
    ReversalQueued = 7,
    Blocked = 8
}

/// <summary>
/// One non-zero temporary-account balance considered by the period-close posting gate.
/// Dimensions remain attached so fund, entity, and sleeve close scopes never collapse.
/// </summary>
public sealed record ClosePostingBalanceDto(
    string AccountName,
    string AccountType,
    decimal Balance,
    string? Symbol = null,
    string? FinancialAccountId = null,
    LedgerDimensionSetDto? Dimensions = null);

/// <summary>
/// Shared close-plan projection for the final "Post closing entries" gate.
/// The gate is ready only when every revenue and expense balance in the scoped period is zero.
/// </summary>
public sealed record ClosePostingGateDto(
    string GateId,
    string Label,
    ClosePostingGateStateDto State,
    bool IsReadyForLock,
    decimal NetIncomeRoll,
    int TemporaryAccountBalanceCount,
    string Detail,
    Guid? DraftJournalEntryId = null,
    ManualJournalEntryStatusDto? DraftStatus = null,
    string? IdempotencyKey = null,
    IReadOnlyList<ClosePostingBalanceDto>? Balances = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    IReadOnlyList<Guid>? ClosingBatchJournalEntryIds = null,
    IReadOnlyList<Guid>? ReversalDraftJournalEntryIds = null)
{
    public IReadOnlyList<ClosePostingBalanceDto> Balances { get; init; } = Balances ?? [];
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];
    public IReadOnlyList<Guid> ClosingBatchJournalEntryIds { get; init; } = ClosingBatchJournalEntryIds ?? [];
    public IReadOnlyList<Guid> ReversalDraftJournalEntryIds { get; init; } = ReversalDraftJournalEntryIds ?? [];
}

public sealed record ReopenClosePeriodRequestDto(
    Guid WorkflowId,
    long ExpectedWorkflowVersion,
    string Actor,
    string Role,
    string Rationale,
    string IncidentId,
    string Justification,
    string ApprovalReference,
    string ImpactSummary,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];
}

public sealed record ClosePeriodReopenResultDto(
    bool IsReopened,
    ClosePeriodPlanDto? Plan,
    OperationsTransitionResultDto? Transition,
    ClosePostingGateDto? ClosingEntriesGate,
    IReadOnlyList<AccountingConfigurationValidationIssueDto>? Issues = null)
{
    public IReadOnlyList<AccountingConfigurationValidationIssueDto> Issues { get; init; } = Issues ?? [];
}

public sealed record ClosePeriodPlanDto(
    string ClosePlanId,
    string FundProfileId,
    Guid? LedgerBookId,
    string PeriodId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly CloseDueDate,
    bool IsPeriodLocked,
    IReadOnlyList<CloseTaskDto> Tasks,
    IReadOnlyList<LateAdjustmentRequestDto> LateAdjustments,
    MaterialityPolicyDto MaterialityPolicy,
    IReadOnlyList<AccountingConfigurationValidationIssueDto>? ValidationIssues = null,
    IReadOnlyList<CloseCalendarMilestoneDto>? CloseCalendar = null,
    ClosePeriodPlanConfigurationDto? Configuration = null,
    IReadOnlyList<CloseEvidenceReviewDto>? EvidenceReviews = null,
    IReadOnlyList<CloseOperatingCoverageItemDto>? OperatingCoverage = null,
    ClosePostingGateDto? ClosingEntriesGate = null,
    long WorkflowVersion = 0)
{
    public IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues { get; init; } =
        ValidationIssues ?? [];

    public IReadOnlyList<CloseCalendarMilestoneDto> CloseCalendar { get; init; } =
        CloseCalendar ?? [];

    public IReadOnlyList<CloseEvidenceReviewDto> EvidenceReviews { get; init; } =
        EvidenceReviews ?? [];

    public IReadOnlyList<CloseOperatingCoverageItemDto> OperatingCoverage { get; init; } =
        OperatingCoverage ?? [];
}

public sealed record ReportCertificationDto(
    string CertificationId,
    AccountingCertificationStateDto State,
    string Actor,
    DateTimeOffset RecordedAtUtc,
    string Summary,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record RestatementWorkflowDto(
    string RestatementId,
    string PriorPackageId,
    string ReasonCode,
    ManualJournalEntryStatusDto ApprovalState,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record ReportLineProvenanceDto(
    string StatementId,
    string LineId,
    string LineLabel,
    string SourceKind,
    decimal Amount,
    string Currency,
    LedgerDimensionSetDto Dimensions,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record ReportExportArtifactDto(
    string ArtifactId,
    string ArtifactKind,
    string DisplayName,
    string Format,
    string Route,
    AccountingCertificationStateDto CertificationState,
    DateTimeOffset GeneratedAtUtc,
    string ContentHash,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? SourceStatementId = null,
    Guid? LedgerBookId = null,
    LedgerDimensionSetDto? Dimensions = null,
    ReportDimensionScopeDto? DimensionScope = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];

    public LedgerDimensionSetDto Dimensions { get; init; } =
        Dimensions ?? new LedgerDimensionSetDto(BookId: LedgerBookId?.ToString("D"));
}

public sealed record ReportExportArtifactManifestDto(
    string PackageId,
    string ArtifactId,
    string ArtifactKind,
    string DisplayName,
    string Format,
    string Route,
    AccountingCertificationStateDto CertificationState,
    DateTimeOffset GeneratedAtUtc,
    string ContentHash,
    string ContentType,
    string FileName,
    bool ExternalPostingAllowed,
    string Payload,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? SourceStatementId = null,
    Guid? LedgerBookId = null,
    LedgerDimensionSetDto? Dimensions = null,
    ReportDimensionScopeDto? DimensionScope = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];

    public LedgerDimensionSetDto Dimensions { get; init; } =
        Dimensions ?? new LedgerDimensionSetDto(BookId: LedgerBookId?.ToString("D"));
}

public sealed record ReportDimensionScopeDto(
    Guid? LedgerBookId,
    LedgerDimensionSetDto Dimensions,
    bool HasExplicitScope,
    string ScopeHash,
    string CertificationEvidenceToken,
    IReadOnlyList<string>? ScopedDimensionKeys = null)
{
    public IReadOnlyList<string> ScopedDimensionKeys { get; init; } =
        ScopedDimensionKeys ?? [];
}

public sealed record FinancialStatementPackageDto(
    string PackageId,
    string FundProfileId,
    Guid? LedgerBookId,
    string PeriodId,
    AccountingCertificationStateDto CertificationState,
    IReadOnlyList<string> StatementIds,
    IReadOnlyList<string> EvidenceLinks,
    ReportCertificationDto? Certification = null,
    RestatementWorkflowDto? Restatement = null,
    IReadOnlyList<ReportLineProvenanceDto>? LineProvenance = null,
    LedgerDimensionSetDto? Dimensions = null)
{
    public IReadOnlyList<ReportLineProvenanceDto> LineProvenance { get; init; } =
        LineProvenance ?? [];

    public LedgerDimensionSetDto Dimensions { get; init; } =
        Dimensions ?? new LedgerDimensionSetDto(FundId: FundProfileId, BookId: LedgerBookId?.ToString("D"));
}

public sealed record InvestorCapitalStatementDto(
    string StatementId,
    string FundProfileId,
    Guid? LedgerBookId,
    string CapitalAccountId,
    string? InvestorId,
    string PeriodId,
    LedgerDimensionSetDto Dimensions,
    decimal BeginningCapital,
    decimal Contributions,
    decimal Distributions,
    decimal RealizedGainLoss,
    decimal EndingCapital,
    string Currency,
    AccountingCertificationStateDto CertificationState,
    IReadOnlyList<string> EvidenceLinks);

public sealed record RealizedGainLossReportDto(
    string ReportId,
    string FundProfileId,
    Guid? LedgerBookId,
    string PeriodId,
    LedgerDimensionSetDto Dimensions,
    decimal RealizedGainLoss,
    string Currency,
    AccountingCertificationStateDto CertificationState,
    IReadOnlyList<string> EvidenceLinks);

public sealed record NavPackageDto(
    string PackageId,
    string FundProfileId,
    Guid? LedgerBookId,
    string PeriodId,
    LedgerDimensionSetDto Dimensions,
    decimal Nav,
    string Currency,
    AccountingCertificationStateDto CertificationState,
    IReadOnlyList<string> EvidenceLinks,
    ReportCertificationDto? Certification = null,
    RestatementWorkflowDto? Restatement = null);

public sealed record AccountingReportPackageRequestDto(
    string FundProfileId,
    string PeriodId,
    string Actor,
    Guid? LedgerBookId = null,
    Guid? CloseWorkflowId = null,
    string? CapitalAccountId = null,
    string? InvestorId = null,
    decimal BeginningCapital = 0m,
    decimal Contributions = 0m,
    decimal Distributions = 0m,
    decimal RealizedGainLoss = 0m,
    decimal Nav = 0m,
    string Currency = "USD",
    string? RestatementReasonCode = null,
    string? PriorPackageId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    LedgerDimensionSetDto? Dimensions = null,
    string? TenantId = null,
    string? CompanyId = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record CertifyAccountingReportPackageRequestDto(
    string PackageId,
    string Actor,
    string Notes,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    string? TenantId = null,
    string? CompanyId = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record AccountingCloseReadinessItemDto(
    string ItemId,
    string Category,
    string Label,
    AccountingReadinessStateDto State,
    string Summary,
    string RequiredAction,
    int BlockingIssueCount,
    IReadOnlyList<string>? EvidenceLinks = null,
    IReadOnlyList<AccountingConfigurationValidationIssueDto>? BlockingIssues = null,
    Guid? LedgerBookId = null,
    LedgerDimensionSetDto? Dimensions = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];

    public IReadOnlyList<AccountingConfigurationValidationIssueDto> BlockingIssues { get; init; } =
        BlockingIssues ?? [];

    public LedgerDimensionSetDto Dimensions { get; init; } =
        Dimensions ?? new LedgerDimensionSetDto(BookId: LedgerBookId?.ToString("D"));
}

public sealed record AccountingReportPackageBundleDto(
    FinancialStatementPackageDto FinancialStatements,
    IReadOnlyList<InvestorCapitalStatementDto> InvestorCapitalStatements,
    RealizedGainLossReportDto RealizedGainLoss,
    NavPackageDto NavPackage,
    ReportCertificationDto Certification,
    IReadOnlyList<AccountingConfigurationValidationIssueDto>? ValidationIssues = null,
    IReadOnlyList<ReportExportArtifactDto>? ExportArtifacts = null,
    Guid? CloseWorkflowId = null,
    string? TenantId = null,
    string? CompanyId = null,
    IReadOnlyList<AccountingCloseReadinessItemDto>? CloseReadinessItems = null,
    ReportDimensionScopeDto? DimensionScope = null)
{
    public IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues { get; init; } =
        ValidationIssues ?? [];

    public IReadOnlyList<ReportExportArtifactDto> ExportArtifacts { get; init; } =
        ExportArtifacts ?? [];

    public IReadOnlyList<AccountingCloseReadinessItemDto> CloseReadinessItems { get; init; } =
        CloseReadinessItems ?? [];
}

public sealed record ManualJournalEntryWorkbenchDto(
    string FundProfileId,
    Guid? LedgerBookId,
    DateTimeOffset LoadedAtUtc,
    IReadOnlyList<LedgerBookDto> LedgerBooks,
    IReadOnlyList<ChartOfAccountsNodeDto> ChartOfAccounts,
    IReadOnlyList<ManualJournalEntryDraftDto> Drafts,
    IReadOnlyList<AccountingActionAuditEventDto> AuditTrail,
    PrivateCapitalActivityProjectionDto? PrivateCapitalActivity = null);

public sealed record SaveManualJournalEntryDraftRequest(
    ManualJournalEntryDraftDto Draft,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    bool PeriodIsLocked = false,
    Guid? LedgerBookId = null,
    string? TenantId = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null);

public sealed record ValidateManualJournalEntryDraftRequest(
    ManualJournalEntryDraftDto Draft,
    string Actor,
    string? CorrelationId = null,
    bool PeriodIsLocked = false,
    Guid? LedgerBookId = null,
    string? TenantId = null,
    string? CompanyId = null);

public sealed record SubmitManualJournalEntryApprovalRequest(
    Guid JournalEntryId,
    string FundProfileId,
    string Actor,
    int Version,
    string? Notes = null,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    bool PeriodIsLocked = false,
    Guid? LedgerBookId = null,
    string? TenantId = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null);

public sealed record AttachManualJournalEntryEvidenceRequest(
    Guid JournalEntryId,
    string FundProfileId,
    string Actor,
    int Version,
    ManualJournalEntryEvidenceAttachmentDto Attachment,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    bool PeriodIsLocked = false,
    Guid? LedgerBookId = null,
    string? TenantId = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null);

public sealed record ActivateAccountingConfigurationRequest(
    string FundProfileId,
    string Actor,
    Guid? LedgerBookId = null,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    string? TenantId = null);

public interface IAccountingConfigurationService
{
    Task<AccountingConfigurationWorkspaceDto> GetWorkspaceAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null);

    Task<AccountingConfigurationWorkspaceDto> UpsertChartNodeAsync(
        UpsertChartOfAccountsNodeRequest request,
        CancellationToken ct = default);

    Task<AccountingConfigurationWorkspaceDto> UpsertTemplateAsync(
        UpsertJournalEntryTemplateRequest request,
        CancellationToken ct = default);

    Task<AccountingConfigurationWorkspaceDto> UpsertPostingRuleAsync(
        UpsertPostingRuleRequest request,
        CancellationToken ct = default);

    Task<AccountingConfigurationWorkspaceDto> ApprovePostingRulePromotionAsync(
        ApprovePostingRulePromotionRequest request,
        CancellationToken ct = default);

    Task<AccountingConfigurationWorkspaceDto> UpsertRuleTestCaseAsync(
        UpsertAccountingRuleTestCaseRequest request,
        CancellationToken ct = default);

    Task<AccountingJournalTemplatePreviewDto> PreviewTemplateAsync(
        PreviewJournalTemplateRequest request,
        CancellationToken ct = default);

    Task<RuleDryRunResultDto> DryRunPostingRuleAsync(
        RuleDryRunRequestDto request,
        CancellationToken ct = default);

    Task<AccountingRuleTestSuiteResultDto> ExecuteRuleTestCasesAsync(
        ExecuteAccountingRuleTestCasesRequestDto request,
        CancellationToken ct = default);

    Task<AccountingConfigurationWorkspaceDto> ActivateAsync(
        ActivateAccountingConfigurationRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAuditAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null);
}

public interface IManualJournalEntryLifecycleService
{
    Task<JournalEntryLifecycleActionResultDto> ApplyLifecycleActionAsync(
        JournalEntryLifecycleActionRequestDto request,
        CancellationToken ct = default);
}

public interface IManualJournalEntryWorkbenchService
{
    Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default);

    Task<ManualJournalEntryWorkbenchDto> GetWorkbenchAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null);

    Task<PrivateCapitalActivityProjectionDto> GetPrivateCapitalActivityAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null);

    Task<ManualJournalEntryDraftDto> SaveDraftAsync(
        SaveManualJournalEntryDraftRequest request,
        CancellationToken ct = default);

    Task<ManualJournalEntryDraftDto> ValidateDraftAsync(
        ValidateManualJournalEntryDraftRequest request,
        CancellationToken ct = default);

    Task<ManualJournalEntryDraftDto> SubmitApprovalAsync(
        SubmitManualJournalEntryApprovalRequest request,
        CancellationToken ct = default);

    Task<ManualJournalEntryDraftDto> AttachEvidenceAsync(
        AttachManualJournalEntryEvidenceRequest request,
        CancellationToken ct = default)
    {
        return Task.FromException<ManualJournalEntryDraftDto>(
            new NotSupportedException("Manual journal evidence attachment is not available for this workbench."));
    }
}

public interface ICapitalAccountWorkbenchService
{
    Task<CapitalAccountWorkbenchDto> GetWorkbenchAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        string? fundEventId = null,
        string? capitalAccountId = null,
        string? investorId = null,
        string? currency = null,
        CancellationToken ct = default);
}

public interface IPrivateCapitalFundEventCommandCenterService
{
    Task<PrivateCapitalFundEventCommandCenterDto?> GetCommandCenterAsync(
        string? fundProfileId,
        Guid? ledgerBookId,
        string fundEventId,
        CancellationToken ct = default);
}

public interface IManualJournalEntryDraftStore
{
    Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ManualJournalEntryDraftDto>> ListAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null);

    Task<ManualJournalEntryDraftDto?> GetAsync(
        string fundProfileId,
        Guid journalEntryId,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null);

    Task SaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default);

    /// <summary>
    /// Persists a related set of journal drafts as one workbench mutation. Implementations must
    /// make the complete set visible together or leave the retained set unchanged.
    /// </summary>
    Task SaveBatchAsync(
        IReadOnlyList<ManualJournalEntryDraftDto> drafts,
        CancellationToken ct = default);
}

public interface IAccountingConfigurationStore
{
    Task<AccountingConfigurationWorkspaceDto?> GetAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null);

    Task SaveAsync(AccountingConfigurationWorkspaceDto workspace, CancellationToken ct = default);
}

public interface IAccountingActionAuditStore
{
    Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default);

    Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null);
}
