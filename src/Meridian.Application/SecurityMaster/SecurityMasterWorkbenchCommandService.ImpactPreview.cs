using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterWorkbenchCommandService
{
    private readonly ISecurityMasterImpactPreviewService? _impactPreview;
    private readonly ISecurityMasterImpactScopeAccessor? _impactScope;
    private readonly Func<bool> _requireImpactPreview = static () => false;

    public async Task<SecurityMasterEditResultDto> ApproveRevisionAsync(ApproveSecurityMasterRevisionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var revision = await EnsureRevisionInStateAsync(request.RevisionId, request.SecurityId, SecurityMasterRevisionStateDto.Submitted, ct).ConfigureAwait(false);
        if (revision.WorkflowId != request.WorkflowId)
            throw new SecurityMasterRevisionStateException(request.RevisionId, "The approval workflow does not match the revision's bound workflow.");
        EnsureApproverIsIndependentOfSubmitter(request, revision);
        var receiptId = request.ImpactReceiptId ?? revision.ReviewedImpactReceiptId;
        if (!RequiresImpact(revision, receiptId))
            return await ApproveRevisionCoreAsync(request, ct).ConfigureAwait(false);
        var (service, scope) = RequiredImpactContext(receiptId);
        await using var dependencies = await service.AcquireFinalizationLeaseAsync(revision, scope, ct).ConfigureAwait(false);
        return await ExecuteMutationAsync(request.SecurityId, async () =>
        {
            await LockImpactSourceAsync(request.SecurityId, lockCanonical: true, ct).ConfigureAwait(false);
            var receipt = await service.ValidateAsync(revision, scope, receiptId!, ct: ct).ConfigureAwait(false);
            if (receipt.Books.Completeness == SecurityImpactCompleteness.Unavailable)
                throw new SecurityMasterImpactPreviewException("ImpactBookCoverageUnavailable", "Book coverage must be established before approval; unavailable prior cash flows remain explicit and do not imply zero impact.");
            var evidenceMarker = $"[security-master-impact:{receipt.ContentSha256}]";
            var existingWorkflow = await _approvalWorkflow.GetAsync(request.WorkflowId, ct).ConfigureAwait(false);
            if (existingWorkflow is { ApprovalState: OperationsApprovalStateDto.Approved }
                && existingWorkflow.Approvals.LastOrDefault(approval => approval.Status == OperationsApprovalStateDto.Approved)
                    ?.Rationale?.TrimEnd().EndsWith(evidenceMarker, StringComparison.Ordinal) != true)
                throw new SecurityMasterImpactPreviewException("ImpactApprovalMismatch", "The existing workflow approval did not review this exact impact receipt. Create a new governed draft and workflow.");
            await _revisions.BindImpactPreviewAsync(revision.RevisionId, revision.SecurityId,
                SecurityMasterRevisionStateDto.Submitted, receipt.ContentSha256, receipt.CanonicalVersion, ct).ConfigureAwait(false);
            return await ApproveRevisionCoreAsync(request with { Rationale = request.Rationale + " " + evidenceMarker }, ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<SecurityMasterPublishResultDto> PublishRevisionAsync(PublishSecurityMasterRevisionRequest request, CancellationToken ct = default)
    {
        var revision = await EnsureRevisionInStateAsync(request.RevisionId, request.SecurityId, SecurityMasterRevisionStateDto.Approved, ct).ConfigureAwait(false);
        var receiptId = request.ImpactReceiptId ?? revision.ReviewedImpactReceiptId;
        if (!RequiresImpact(revision, receiptId))
            return await PublishRevisionCoreAsync(request, ct).ConfigureAwait(false);
        var (service, scope) = RequiredImpactContext(receiptId);
        if (revision.ReviewedImpactReceiptId != receiptId)
            throw new SecurityMasterImpactPreviewException("ImpactNotApproved", "Publication must use the exact impact receipt retained with approval.");
        await using var dependencies = await service.AcquireFinalizationLeaseAsync(revision, scope, ct).ConfigureAwait(false);
        return await ExecuteMutationAsync(request.SecurityId, async () =>
        {
            await LockImpactSourceAsync(request.SecurityId, lockCanonical: true, ct).ConfigureAwait(false);
            try
            { await service.ValidateAsync(revision, scope, receiptId!, ct: ct).ConfigureAwait(false); }
            catch (SecurityMasterImpactPreviewException ex) when (ex.Code == "ImpactSecurityChanged")
            {
                // A previous publish attempt may already have merged this exact approved document.
                // Other canonical changes fail the complete proposed-document comparison.
                await service.ValidateAsync(revision, scope, receiptId!, canonicalMergeApplied: true, ct).ConfigureAwait(false);
            }
            return await PublishRevisionCoreAsync(request, ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private bool RequiresImpact(SecurityMasterRevisionRecord revision, string? receiptId)
        => _requireImpactPreview() || revision.RepairBatchId is not null || !string.IsNullOrWhiteSpace(receiptId);

    private (ISecurityMasterImpactPreviewService Service, SecurityMasterImpactScope Scope) RequiredImpactContext(string? receiptId)
    {
        if (string.IsNullOrWhiteSpace(receiptId))
            throw new SecurityMasterImpactPreviewException("ImpactReceiptRequired", "Create and inspect current impact evidence before approval or publication.");
        if (_impactPreview is null || _impactScope is null)
            throw new SecurityMasterImpactPreviewException("ImpactPreviewUnavailable", "Configured impact evidence and authenticated scope are required for this governed change.");
        return (_impactPreview, _impactScope.GetRequired());
    }

    private async Task ValidatePublicationImpactAsync(PublishSecurityMasterRevisionRequest request, CancellationToken ct)
    {
        var revision = await _revisions.GetAsync(request.RevisionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The revision disappeared before publication.");
        var receiptId = request.ImpactReceiptId ?? revision.ReviewedImpactReceiptId;
        if (!RequiresImpact(revision, receiptId))
            return;
        var (service, scope) = RequiredImpactContext(receiptId);
        await LockImpactSourceAsync(request.SecurityId, lockCanonical: true, ct).ConfigureAwait(false);
        await service.ValidateAsync(revision, scope, receiptId!, canonicalMergeApplied: true, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Guid>> ResolveReviewedBooksAsync(SecurityMasterRevisionRecord revision,
        SecurityMasterDownstreamImpactDto impact, CancellationToken ct)
    {
        if (revision.ReviewedImpactReceiptId is null)
            return await _affectedLedgerBookResolver.ResolveAsync(impact, ct).ConfigureAwait(false);
        var (service, scope) = RequiredImpactContext(revision.ReviewedImpactReceiptId);
        var receipt = await service.GetAsync(scope, revision.ReviewedImpactReceiptId, ct).ConfigureAwait(false)
            ?? throw new SecurityMasterImpactPreviewException("ImpactReceiptMissing", "The approved impact receipt is unavailable.");
        if (receipt.Books.Completeness == SecurityImpactCompleteness.Unavailable)
            throw new SecurityMasterImpactPreviewException("ImpactBookCoverageUnavailable", "Book coverage is unavailable; publication cannot treat this as zero affected books. Restore the ledger lookup and create a fresh governed review.");
        return receipt.Books.BookIds;
    }

    private async Task LockImpactSourceAsync(Guid securityId, bool lockCanonical, CancellationToken ct)
    {
        if (_revisions is not ISecurityMasterMutationParticipant participant)
            throw new SecurityMasterImpactPreviewException("ImpactMutationGuardUnavailable", "Impact finalization requires durable revision and canonical version guards.");
        await using var lease = await PostgresSecurityMasterMutation.OpenConnectionAsync(participant.MutationOptions, ct).ConfigureAwait(false);
        var schema = '"' + participant.MutationOptions.Schema.ToLowerInvariant().Replace("\"", "\"\"") + '"';
        await using var command = lease.Connection.CreateCommand();
        command.CommandText = $"set local lock_timeout = '5s'; lock table {schema}.security_cashflow_source_assignments in share mode;";
        if (lockCanonical)
        {
            command.CommandText += $" select pg_advisory_xact_lock(hashtext(@security::text)); select version from {schema}.securities where security_id = @security for share;";
            command.Parameters.AddWithValue("security", securityId);
        }
        try
        { await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        { throw new SecurityMasterImpactPreviewException("ImpactGuardUnavailable", "Source or canonical dependency locks could not be acquired; retry finalization."); }
        if (lockCanonical)
            PostgresSecurityMasterMutation.MarkImpactCanonicalFence(participant.MutationOptions, securityId);
    }
}
