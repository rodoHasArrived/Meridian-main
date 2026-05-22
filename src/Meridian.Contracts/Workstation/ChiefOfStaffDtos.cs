using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

[JsonConverter(typeof(JsonStringEnumConverter<ChiefOfStaffIntentKindDto>))]
public enum ChiefOfStaffIntentKindDto
{
    Unknown = 0,
    AccountingReconciliationReview = 1,
    TradingReadinessReview = 2,
    ReportPackApproval = 3,
    GovernanceAuditTrailReview = 4,
    GeneralOperatorAssistance = 5
}

[JsonConverter(typeof(JsonStringEnumConverter<ChiefOfStaffSessionStatusDto>))]
public enum ChiefOfStaffSessionStatusDto
{
    Created = 0,
    ReviewRequired = 1,
    Blocked = 2,
    AwaitingOperatorDecision = 3,
    Approved = 4,
    Rejected = 5,
    Deferred = 6
}

[JsonConverter(typeof(JsonStringEnumConverter<ChiefOfStaffDecisionKindDto>))]
public enum ChiefOfStaffDecisionKindDto
{
    Approve = 0,
    Reject = 1,
    Defer = 2
}

public sealed record ChiefOfStaffStartRequestDto(
    string OperatorRequest,
    string? OperatorId = null,
    string? Workspace = null,
    string? FundProfileId = null,
    Guid? FundAccountId = null,
    string? CorrelationId = null,
    IReadOnlyDictionary<string, string>? ContextTags = null);

public sealed record ChiefOfStaffSessionQueryDto(
    string? Workspace = null,
    string? FundProfileId = null,
    Guid? FundAccountId = null,
    ChiefOfStaffSessionStatusDto? Status = null,
    int Limit = 25);

public sealed record ChiefOfStaffActionCandidateDto(
    string ActionId,
    string Label,
    string TargetWorkflow,
    string? TargetRoute,
    string? TargetPageTag,
    string? RequiredSignoffRole,
    bool ApprovalRequired,
    string ImpactSummary,
    IReadOnlyList<string> EvidencePrerequisites);

public sealed record ChiefOfStaffRecommendationDto(
    string RecommendationId,
    string Title,
    string Detail,
    OperatorWorkItemToneDto Tone,
    IReadOnlyList<ChiefOfStaffActionCandidateDto> Actions);

public sealed record ChiefOfStaffTraceSummaryDto(
    string TraceId,
    string RuntimeName,
    string? RuntimeVersion,
    DateTimeOffset CapturedAt,
    IReadOnlyList<string> WarningCodes);

public sealed record ChiefOfStaffEvidenceBundleDto(
    IReadOnlyList<EvidenceSubjectDto> Subjects,
    IReadOnlyList<EvidencePacketDto> Packets,
    IReadOnlyList<OperatorWorkItemDto> WorkItems,
    IReadOnlyList<string> RelatedReconciliationRunIds,
    IReadOnlyList<string> RelatedWorkflowIds,
    IReadOnlyList<EvidenceArtifactRefDto> TraceArtifacts,
    EvidenceStatusDto CompletenessStatus,
    IReadOnlyList<string> Warnings);

public sealed record ChiefOfStaffSessionDto(
    Guid SessionId,
    ChiefOfStaffIntentKindDto IntentKind,
    string OperatorRequest,
    string MarkdownSummary,
    JsonElement StructuredPayload,
    ChiefOfStaffEvidenceBundleDto EvidenceBundle,
    IReadOnlyList<ChiefOfStaffRecommendationDto> Recommendations,
    IReadOnlyList<ChiefOfStaffActionCandidateDto> Actions,
    ChiefOfStaffTraceSummaryDto TraceSummary,
    DateTimeOffset FreshnessAsOf,
    ChiefOfStaffSessionStatusDto Status,
    bool PendingApproval,
    IReadOnlyList<string> RoutedWorkflowReferences,
    IReadOnlyList<string> Warnings);

public sealed record ChiefOfStaffSessionSummaryDto(
    Guid SessionId,
    ChiefOfStaffIntentKindDto IntentKind,
    string OperatorRequest,
    ChiefOfStaffSessionStatusDto Status,
    DateTimeOffset FreshnessAsOf,
    bool PendingApproval,
    IReadOnlyList<string> Warnings);

public sealed record ChiefOfStaffDecisionRequestDto(
    ChiefOfStaffDecisionKindDto Decision,
    string Actor,
    string? SelectedActionId = null,
    string? Rationale = null,
    string? CorrelationId = null);

public sealed record ChiefOfStaffRuntimeRequestDto(
    Guid SessionId,
    ChiefOfStaffIntentKindDto IntentKind,
    string OperatorRequest,
    string? OperatorId,
    string? Workspace,
    string? FundProfileId,
    Guid? FundAccountId,
    string? CorrelationId,
    IReadOnlyDictionary<string, string> ContextTags);

public sealed record ChiefOfStaffRuntimeResponseDto(
    ChiefOfStaffIntentKindDto IntentKind,
    string MarkdownSummary,
    JsonElement StructuredPayload,
    IReadOnlyList<ChiefOfStaffRecommendationDto> Recommendations,
    IReadOnlyList<ChiefOfStaffActionCandidateDto> Actions,
    ChiefOfStaffEvidenceBundleDto? EvidenceBundle,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<string> Warnings);

[JsonConverter(typeof(JsonStringEnumConverter<ChiefOfStaffRuntimeHealthStatusDto>))]
public enum ChiefOfStaffRuntimeHealthStatusDto
{
    Healthy = 0,
    Degraded = 1,
    Unavailable = 2
}

public sealed record ChiefOfStaffRuntimeHealthDto(
    ChiefOfStaffRuntimeHealthStatusDto Status,
    string Detail,
    DateTimeOffset CheckedAt);

public sealed record ChiefOfStaffTraceEnvelopeDto(
    Guid SessionId,
    string TraceId,
    string RuntimeName,
    string? RuntimeVersion,
    DateTimeOffset CapturedAt,
    ChiefOfStaffRuntimeRequestDto RuntimeRequest,
    ChiefOfStaffRuntimeResponseDto RuntimeResponse,
    IReadOnlyList<string> WarningCodes);

public sealed record ChiefOfStaffTraceExportRequestDto(
    string RequestedBy,
    string? Reason = null,
    bool IncludeWarnings = true);

public sealed record ChiefOfStaffApprovalCommandDto(
    Guid SessionId,
    string ActionId,
    string Actor,
    string? Rationale,
    string? CorrelationId,
    string? TargetWorkflow,
    string? TargetRoute,
    string? RequiredSignoffRole,
    Guid? OperationsContinuityWorkflowId = null,
    long? OperationsWorkflowVersion = null,
    string? ReportPackId = null);

public sealed record ChiefOfStaffApprovalResultDto(
    bool Success,
    bool Routed,
    string Status,
    string Detail,
    string? AuditReference = null,
    string? WorkflowReference = null);

public sealed record ChiefOfStaffEvidenceExportDto(
    Guid SessionId,
    EvidencePacketExportResponse Manifest,
    ChiefOfStaffTraceSummaryDto TraceSummary);
