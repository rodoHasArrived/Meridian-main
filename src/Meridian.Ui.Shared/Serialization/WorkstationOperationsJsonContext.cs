using System.Text.Json.Serialization;
using Meridian.Infrastructure.Contracts;
using Meridian.Ui.Shared.Contracts;

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
[JsonSerializable(typeof(OperationsWorkflowStatus))]
[JsonSerializable(typeof(OperationsGateStatus))]
[JsonSerializable(typeof(BrokerIntakeState))]
[JsonSerializable(typeof(SecurityMasterValidationState))]
[JsonSerializable(typeof(LedgerPostingState))]
[JsonSerializable(typeof(ReconciliationState))]
[JsonSerializable(typeof(ApprovalState))]
[JsonSerializable(typeof(OperationsContinuityWorkflowDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsContinuityWorkflowDto>))]
[JsonSerializable(typeof(OperationsGateDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsGateDto>))]
[JsonSerializable(typeof(OperationsTransitionRequestDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsTransitionRequestDto>))]
[JsonSerializable(typeof(OperationsTimelineEntryDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsTimelineEntryDto>))]
[JsonSerializable(typeof(OperationsBreakCaseDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsBreakCaseDto>))]
[JsonSerializable(typeof(OperationsApprovalDto))]
[JsonSerializable(typeof(IReadOnlyList<OperationsApprovalDto>))]
internal sealed partial class WorkstationOperationsJsonContext : JsonSerializerContext;
