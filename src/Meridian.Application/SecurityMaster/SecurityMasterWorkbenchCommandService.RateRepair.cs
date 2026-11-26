using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

public interface ISecurityMasterRateRepairDraftWriter
{
    Task<SecurityMasterEditResultDto> StageRateRepairAsync(
        UpdateSecurityFieldRequest request, SecurityMasterRevisionDraftContext context, CancellationToken ct = default);
}

public sealed partial class SecurityMasterWorkbenchCommandService : ISecurityMasterRateRepairDraftWriter
{
    public Task<SecurityMasterEditResultDto> StageRateRepairAsync(
        UpdateSecurityFieldRequest request, SecurityMasterRevisionDraftContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.RepairBatchId == Guid.Empty || string.IsNullOrWhiteSpace(context.EvidenceId)
            || !Meridian.Contracts.Integrity.Sha256Digest.IsCanonical(context.EvidenceSha256)
            || request.FieldPath != "assetSpecificTerms.rateUnit")
            throw new ArgumentException("Retained batch, evidence and exact scalar rate-unit field are required.");
        return UpdateSecurityFieldCoreAsync(request, context, ct);
    }

    private async Task EnsureRevisionWorkflowPurposeAsync(SecurityMasterRevisionRecord revision, Guid workflowId, CancellationToken ct)
    {
        var workflow = await _approvalWorkflow.GetAsync(workflowId, ct).ConfigureAwait(false);
        if (revision.RepairBatchId is null && workflow?.Purpose != OperationsWorkflowPurposeDto.SecurityMasterRevision)
            return;
        if (revision.RepairBatchId is null || workflow is null
            || workflow.Purpose != OperationsWorkflowPurposeDto.SecurityMasterRevision
            || workflow.SubjectRevisionId != revision.RevisionId || workflow.SubjectMaker != revision.DraftedBy
            || workflow.SubjectEvidenceId != revision.RepairEvidenceId || workflow.SubjectEvidenceSha256 != revision.RepairEvidenceSha256)
            throw new ArgumentException("A repair revision requires its exact subject-scoped workflow, maker and retained evidence; close workflows cannot approve it.");
    }
}
