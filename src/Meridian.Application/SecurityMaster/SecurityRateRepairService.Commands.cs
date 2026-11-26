using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityRateRepairService
{
    public async Task<SecurityRateRepairBatchDto> ExecuteAsync(Guid batchId, string command,
        SecurityRateRepairBatchCommandRequest request, SecurityRateRepairActor actor, CancellationToken ct = default)
    {
        RequireActor(actor, true);
        if (request.ExpectedVersion < 1 || request.Reviewer?.Length > 128 || request.Rationale?.Length > 4000)
            throw new ArgumentException("A current batch version, bounded reviewer and rationale are required.");
        if (command is not ("Stage" or "Submit" or "Approve" or "Publish"))
            throw new ArgumentException("Unknown repair command.");
        await using var commandLease = await store.AcquireCommandLeaseAsync(batchId, actor.TenantId, request.ExpectedVersion, ct).ConfigureAwait(false);
        var batch = await store.GetAsync(batchId, actor.TenantId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Repair batch was not found.");
        if (batch.Manifest.CompanyId != actor.CompanyId)
            throw new UnauthorizedAccessException("Repair scope is unavailable.");
        if ((command == "Stage" || command == "Approve")
            && string.Equals(batch.Manifest.Maker.Trim(), actor.Actor.Trim(), StringComparison.OrdinalIgnoreCase) != (command == "Stage"))
            throw new UnauthorizedAccessException(command == "Stage" ? "Only the original maker can stage this frozen repair." : "The original maker cannot approve its repair.");
        if (command == "Submit" && (string.IsNullOrWhiteSpace(request.Reviewer)
            || string.Equals(request.Reviewer.Trim(), batch.Manifest.Maker.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.Reviewer.Trim(), actor.Actor.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Assign a reviewer independent from both the maker and submitter.");
        if (command is "Submit" or "Approve" && string.IsNullOrWhiteSpace(request.Rationale))
            throw new ArgumentException("A retained submission or approval rationale is required.");
        await ValidateAccountingScopeAsync(batch.Manifest.Request.Scope, ct).ConfigureAwait(false);
        var retainedEvidence = await evidence.ResolveAsync(batch.Manifest.Request.EvidenceId, actor.TenantId, ct).ConfigureAwait(false);
        if (retainedEvidence.ContentSha256 != batch.Manifest.EvidenceSha256)
            throw new SecurityRateRepairConflictException("The retained evidence no longer matches the frozen batch.");
        foreach (var item in batch.Manifest.Items)
        {
            try
            {
                var latest = await store.GetAsync(batchId, actor.TenantId, ct).ConfigureAwait(false)
                    ?? throw new KeyNotFoundException("Repair batch was not found.");
                var currentItem = latest.Items.Single(row => row.SecurityId == item.SecurityId);
                var currentRevision = currentItem.RevisionId is { } currentId
                    ? await revisions.GetAsync(currentId, ct).ConfigureAwait(false) : null;
                // Acquire ledger authority before entering the item's Security Master mutation,
                // and retain it until that outer transaction (including progress) has committed.
                await using var finalization = command is "Approve" or "Publish" && currentRevision is not null
                    ? await impacts.AcquireFinalizationLeaseAsync(currentRevision,
                        new(actor.TenantId, actor.CompanyId), ct).ConfigureAwait(false) : null;
                await store.ExecuteItemAsync(batchId, actor.TenantId, item.SecurityId,
                    progress => ExecuteItemAsync(batch.Manifest, item, progress, command, request, actor, ct), ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or UnauthorizedAccessException))
            {
                var code = exception switch
                {
                    SecurityMasterConcurrencyException => "StaleRecord",
                    SecurityRateRepairConflictException => "RepairConflict",
                    SecurityMasterImpactPreviewException impact => impact.Code,
                    ArgumentException => "RepairValidation",
                    SecurityMasterRevisionStateException => "RevisionStateConflict",
                    _ => "RepairItemFailed"
                };
                var reason = code == "RepairItemFailed" ? "This item could not complete; its durable draft and workflow remain available for review and retry." : exception.Message;
                await store.ExecuteItemAsync(batchId, actor.TenantId, item.SecurityId, progress => Task.FromResult(progress with
                {
                    State = progress.RevisionId is null ? (code is "StaleRecord" or "RepairConflict" ? "Conflicted" : "Failed") : progress.State,
                    ErrorCode = code,
                    Error = reason
                }), ct).ConfigureAwait(false);
            }
        }
        return await ToDtoAsync(await store.GetAsync(batchId, actor.TenantId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained repair batch disappeared."), actor, ct).ConfigureAwait(false);
    }

    private async Task<SecurityRateRepairProgress> ExecuteItemAsync(SecurityRateRepairManifest batch,
        SecurityRateRepairFrozenItem frozen, SecurityRateRepairProgress progress, string command,
        SecurityRateRepairBatchCommandRequest request, SecurityRateRepairActor actor, CancellationToken ct)
    {
        var revision = progress.RevisionId is { } id ? await revisions.GetAsync(id, ct).ConfigureAwait(false) : null;
        if (progress.RevisionId is not null && (revision is null || revision.RepairBatchId != batch.Request.RequestId))
            throw new SecurityRateRepairConflictException("The retained item revision cannot be verified.");
        if (revision?.State == SecurityMasterRevisionStateDto.Published)
            return progress with { State = "Published", Error = null, ErrorCode = null };
        var scope = new SecurityMasterImpactScope(actor.TenantId, actor.CompanyId);
        if (command == "Stage")
        {
            if (revision is null)
            {
                var current = await securities.GetProjectionAsync(frozen.SecurityId, ct).ConfigureAwait(false)
                    ?? throw new ArgumentException("The selected security is unavailable.");
                var candidate = await CandidateAsync(current, ct).ConfigureAwait(false);
                if (current.Version != frozen.BaseVersion || candidate.RawValue != frozen.RawValue
                    || !candidate.Eligible || candidate.CurrentUnit is not null
                    || candidate.SourceSystem != batch.Request.Cohort.SourceSystem || candidate.SourceField != batch.Request.Cohort.SourceField)
                    throw new SecurityRateRepairConflictException("The selected source, numeric value or canonical record changed after cohort selection.");
                if (workbench is not ISecurityMasterRateRepairDraftWriter writer)
                    throw new InvalidOperationException("The workbench cannot retain batch draft context.");
                var staged = await writer.StageRateRepairAsync(new(frozen.SecurityId, frozen.BaseVersion,
                    "assetSpecificTerms.rateUnit", batch.Request.Unit, batch.Request.EffectiveFrom, actor.Actor,
                    batch.Request.Justification, SourceRecordId: batch.Request.EvidenceId,
                    FundProfileId: batch.Request.Scope.FundProfileId),
                    new(batch.Request.RequestId, batch.Request.EvidenceId, batch.EvidenceSha256), ct).ConfigureAwait(false);
                revision = await revisions.GetAsync(staged.RevisionId, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The staged revision was not retained.");
                progress = progress with { RevisionId = revision.RevisionId, State = "Draft" };
            }
            if (revision.State is not (SecurityMasterRevisionStateDto.Draft or SecurityMasterRevisionStateDto.Submitted))
                return progress with { State = revision.State.ToString() };
            var receipt = await impacts.CreateAsync(frozen.SecurityId, revision.RevisionId, scope, batch.Request.EffectiveFrom, ct).ConfigureAwait(false);
            return progress with { State = revision.State.ToString(), ImpactReceiptId = receipt.ContentSha256, Error = null, ErrorCode = null };
        }
        if (revision is null)
            return progress;
        var subjectReference = $"security-master-revision:{revision.RevisionId:D}";
        if (command == "Submit" && revision.State == SecurityMasterRevisionStateDto.Draft)
        {
            if (progress.ImpactReceiptId is null)
                throw new ArgumentException("Retained impact evidence must be prepared first.");
            await impacts.ValidateAsync(revision, scope, progress.ImpactReceiptId, ct: ct).ConfigureAwait(false);
            var started = await workflows.StartSecurityMasterRevisionWorkflowAsync(progress.WorkflowId, revision.RevisionId, actor.Actor, ct).ConfigureAwait(false);
            if (!started.Success || started.Workflow is null)
                throw new InvalidOperationException(started.ErrorMessage ?? "Revision approval workflow could not start.");
            await workbench.SubmitForApprovalAsync(new(frozen.SecurityId, revision.RevisionId, actor.Actor, request.Rationale,
                batch.Request.Scope.FundProfileId, progress.WorkflowId, started.Workflow.Version, request.Reviewer, subjectReference), ct).ConfigureAwait(false);
            progress = progress with { SubmittedBy = actor.Actor, AssignedReviewer = request.Reviewer };
        }
        else if (command == "Approve" && revision.State == SecurityMasterRevisionStateDto.Submitted)
        {
            var workflow = await workflows.GetAsync(progress.WorkflowId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The assigned approval workflow is unavailable.");
            await workbench.ApproveRevisionAsync(new(frozen.SecurityId, revision.RevisionId, progress.WorkflowId,
                workflow.Version, actor.Actor, actor.Actor, request.Rationale!, subjectReference)
            { ImpactReceiptId = progress.ImpactReceiptId }, ct).ConfigureAwait(false);
        }
        else if (command == "Publish" && revision.State == SecurityMasterRevisionStateDto.Approved)
        {
            await workbench.PublishRevisionAsync(new(frozen.SecurityId, revision.RevisionId, actor.Actor, revision.Actor)
            { ImpactReceiptId = progress.ImpactReceiptId }, ct).ConfigureAwait(false);
        }
        else
        {
            // Another item may be ready for this batch command while this one still needs review.
            // A no-op must not erase the refusal explaining why this item could not advance.
            return progress with { State = revision.State.ToString() };
        }
        revision = await revisions.GetAsync(revision.RevisionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The item revision disappeared.");
        var decided = revision.State is SecurityMasterRevisionStateDto.Approved or SecurityMasterRevisionStateDto.Published
            ? (await workflows.GetAsync(progress.WorkflowId, ct).ConfigureAwait(false))?.Approvals
                .LastOrDefault(static approval => approval.Status == OperationsApprovalStateDto.Approved)?.ApprovalId : progress.ApprovalReceiptId;
        return progress with { State = revision.State.ToString(), ApprovalReceiptId = decided, Error = null, ErrorCode = null };
    }
}
