using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Ui.Shared.Serialization;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for workstation operations continuity contracts.
/// Per ADR-014, endpoint serialization must use source-generated metadata for these payloads.
/// </summary>
[ImplementsAdr("ADR-014", "Source-generated JSON context for workstation operations continuity contracts")]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OperationsWorkflowStatusDto))]
[JsonSerializable(typeof(OperationsGateStatusDto))]
[JsonSerializable(typeof(OperationsGateKeyDto))]
[JsonSerializable(typeof(OperationsBrokerIntakeStateDto))]
[JsonSerializable(typeof(OperationsSecurityMasterStateDto))]
[JsonSerializable(typeof(OperationsLedgerPostingStateDto))]
[JsonSerializable(typeof(OperationsReconciliationStateDto))]
[JsonSerializable(typeof(OperationsApprovalStateDto))]
[JsonSerializable(typeof(OperationsStartWorkflowRequestDto))]
[JsonSerializable(typeof(OperationsTransitionRequestDto))]
[JsonSerializable(typeof(OperationsSecurityMasterOverrideApprovalRequestDto))]
[JsonSerializable(typeof(OperationsGatePostureRequestDto))]
[JsonSerializable(typeof(OperationsSecurityMasterResolveRequestDto))]
[JsonSerializable(typeof(OperationsLedgerDraftRequestDto))]
[JsonSerializable(typeof(OperationsLedgerValidationRequestDto))]
[JsonSerializable(typeof(OperationsLedgerPostRequestDto))]
[JsonSerializable(typeof(OperationsLedgerJournalCandidateDto))]
[JsonSerializable(typeof(OperationsLedgerJournalLineDto))]
[JsonSerializable(typeof(OperationsJournalEntryMetadataDto))]
[JsonSerializable(typeof(OperationsReconciliationRunRequestDto))]
[JsonSerializable(typeof(OperationsResolveBreakCaseRequestDto))]
[JsonSerializable(typeof(OperationsSubmitApprovalRequestDto))]
[JsonSerializable(typeof(OperationsApprovalDecisionRequestDto))]
[JsonSerializable(typeof(OperationsRejectWorkflowRequestDto))]
[JsonSerializable(typeof(OperationsCloseWorkflowRequestDto))]
[JsonSerializable(typeof(OperationsReopenWorkflowRequestDto))]
[JsonSerializable(typeof(OperationsTransitionResultDto))]
[JsonSerializable(typeof(OperationsContinuityWorkflowDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsContinuityWorkflowDto>))]
[JsonSerializable(typeof(OperationsContinuityWorkflowSummaryDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsContinuityWorkflowSummaryDto>))]
[JsonSerializable(typeof(OperationsGateDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsGateDto>))]
[JsonSerializable(typeof(OperationsTimelineEntryDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsTimelineEntryDto>))]
[JsonSerializable(typeof(OperationsWorkflowAuditDto))]
[JsonSerializable(typeof(OperationsBreakCaseDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsBreakCaseDto>))]
[JsonSerializable(typeof(OperationsApprovalDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsApprovalDto>))]
[JsonSerializable(typeof(OperationsLedgerPreviewDto))]
[JsonSerializable(typeof(OperationsReportPackReadinessDto))]
[JsonSerializable(typeof(OperationsWorkflowBlockerDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsWorkflowBlockerDto>))]
[JsonSerializable(typeof(OperationsNextActionDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsNextActionDto>))]
[JsonSerializable(typeof(OperationsEvidenceLinkDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsEvidenceLinkDto>))]
internal sealed partial class WorkstationOperationsJsonContext : JsonSerializerContext;
