using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Durable authority for provider observations and scoped corporate-action processing cases.
/// Implementations must make each mutation, its version increment, audit transition, and command
/// receipt atomic.
/// </summary>
public interface ICorporateActionOperationsStore
{
    Task<CorporateActionSourceProposalDto> RecordSourceProposalAsync(
        CorporateActionSourceProposalDto proposal,
        CancellationToken ct = default);

    Task<CorporateActionSourceProposalDto?> GetSourceProposalAsync(Guid proposalId, CancellationToken ct = default);

    Task<IReadOnlyList<CorporateActionSourceProposalDto>> ListSourceProposalsAsync(
        Guid? securityId,
        string? state,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// Lists only proposals that can still receive an accept/reject decision. Implementations
    /// must apply the state predicate before ordering and limiting the durable result set.
    /// </summary>
    Task<IReadOnlyList<CorporateActionSourceProposalDto>> ListActionableSourceProposalsAsync(
        Guid? securityId,
        int take,
        CancellationToken ct = default);

    Task<CorporateActionSourceProposalAcceptanceResultDto?> GetAcceptanceReceiptAsync(
        Guid proposalId,
        string idempotencyKey,
        string requestFingerprint,
        CancellationToken ct = default);

    Task<CorporateActionSourceProposalAcceptanceResultDto> AcceptSourceProposalAsync(
        AcceptCorporateActionSourceProposalRequestDto request,
        Guid corporateActionId,
        Guid caseId,
        Guid transitionId,
        SecurityMasterCorporateActionRestatementDto? restatement,
        string requestFingerprint,
        CancellationToken ct = default);

    Task<CorporateActionSourceProposalDecisionResultDto> RejectSourceProposalAsync(
        RejectCorporateActionSourceProposalRequestDto request,
        string requestFingerprint,
        CancellationToken ct = default);

    Task<CorporateActionProcessingCaseDto?> GetCaseAsync(
        Guid caseId,
        string tenantId,
        string companyId,
        CancellationToken ct = default);

    Task<IReadOnlyList<CorporateActionProcessingCaseDto>> ListCasesAsync(
        string tenantId,
        string companyId,
        Guid? securityId,
        string? state,
        int take,
        CancellationToken ct = default);

    Task<CorporateActionConflictDto?> GetConflictAsync(
        Guid caseId,
        Guid conflictId,
        string tenantId,
        string companyId,
        CancellationToken ct = default);

    Task<IReadOnlyList<CorporateActionConflictDto>> ListConflictsAsync(
        Guid caseId,
        string tenantId,
        string companyId,
        string? state,
        int take,
        CancellationToken ct = default);

    Task<CorporateActionEvidenceMutationResultDto> AddEvidenceAsync(
        AddCorporateActionEvidenceRequestDto request,
        Guid evidenceId,
        string requestFingerprint,
        CancellationToken ct = default);

    Task<CorporateActionConflictMutationResultDto> RecordConflictAsync(
        RecordCorporateActionConflictRequestDto request,
        Guid conflictId,
        string requestFingerprint,
        CancellationToken ct = default);

    Task<CorporateActionConflictResolutionResultDto> ResolveConflictAsync(
        ResolveCorporateActionConflictRequestDto request,
        string requestFingerprint,
        CancellationToken ct = default);

    Task<CorporateActionProcessingOptionMutationResultDto> UpsertOptionAsync(
        UpsertCorporateActionProcessingOptionRequestDto request,
        Guid optionId,
        string requestFingerprint,
        CancellationToken ct = default);

    Task<CorporateActionCaseTransitionResultDto> TransitionCaseAsync(
        TransitionCorporateActionCaseRequestDto request,
        Guid transitionId,
        string requestFingerprint,
        CancellationToken ct = default);
}
