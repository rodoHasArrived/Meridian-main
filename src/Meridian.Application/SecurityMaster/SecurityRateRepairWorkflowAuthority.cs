using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.Ledger;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityRateRepairWorkflowAuthority(
    ISecurityMasterRevisionStore revisions, ISecurityRateRepairStore batches,
    ISecurityMasterImpactScopeAccessor scopeAccessor, ILedgerJournalStore ledger) : ISecurityMasterRevisionWorkflowAuthority
{
    public async Task<SecurityMasterRevisionWorkflowAuthority?> ResolveAsync(Guid revisionId, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor))
            return null;
        var scope = scopeAccessor.GetRequired();
        var revision = await revisions.GetAsync(revisionId, ct).ConfigureAwait(false);
        if (revision?.RepairBatchId is not { } batchId || string.IsNullOrWhiteSpace(revision.DraftedBy)
            || !revision.FieldBaseRecorded || revision.FieldPath != "assetSpecificTerms.rateUnit"
            || revision.State is not (SecurityMasterRevisionStateDto.Draft or SecurityMasterRevisionStateDto.Submitted))
            return null;
        var batch = await batches.GetAsync(batchId, scope.TenantId, ct).ConfigureAwait(false);
        if (batch is null || batch.Manifest.CompanyId != scope.CompanyId
            || !batch.Items.Any(item => item.RevisionId == revisionId && item.SecurityId == revision.SecurityId)
            || batch.Manifest.Maker != revision.DraftedBy || batch.Manifest.Request.EvidenceId != revision.RepairEvidenceId
            || batch.Manifest.EvidenceSha256 != revision.RepairEvidenceSha256)
            return null;
        var accounting = batch.Manifest.Request.Scope;
        var book = await ledger.GetLedgerBookAsync(accounting.LedgerBookId, ct).ConfigureAwait(false);
        if (book?.FundProfileId != accounting.FundProfileId || !Guid.TryParse(accounting.PeriodId, out var periodId))
            return null;
        var period = await ledger.GetPeriodAsync(periodId, ct).ConfigureAwait(false);
        if (period?.LedgerBookId != accounting.LedgerBookId)
            return null;
        var prepared = batch.Items.Single(item => item.RevisionId == revisionId && item.SecurityId == revision.SecurityId);
        return new(prepared.WorkflowId, revisionId, accounting.FundAccountId, accounting.LedgerBookId, accounting.PeriodId,
            revision.DraftedBy, revision.RepairEvidenceId!, revision.RepairEvidenceSha256!);
    }
}
