using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityRateRepairService
{
    private async Task<SecurityRateRepairCandidateDto> CandidateAsync(SecurityProjectionRecord record, CancellationToken ct)
    {
        var field = SecurityRateFieldUnits.CouponFieldKey(record.AssetClass);
        var path = $"assetSpecificTerms.{field}";
        var unitSchema = SecurityAssetTermsSchema.Field(record.AssetClass, SecurityRecordRateUnit.FieldKey);
        var fieldMeaning = SecurityRateFieldUnits.Find(record.AssetClass, field);
        var declaredUnit = SecurityRecordRateUnit.Read(record.AssetClass, record.AssetSpecificTerms);
        var terms = record.AssetSpecificTerms;
        var raw = terms.ValueKind == JsonValueKind.Object && terms.TryGetProperty(field, out var value) ? value.GetRawText() : null;
        var numeric = terms.ValueKind == JsonValueKind.Object && terms.TryGetProperty(field, out value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _);
        var provenance = SecurityMasterProvenanceReader.Read(record.Provenance);
        var source = provenance.SourceSystem;
        if (fieldProvenance is not null)
        {
            var attribution = (await fieldProvenance.GetAsync(record.SecurityId, ct).ConfigureAwait(false))
                .Where(item => item.FieldPath == path && item.Origin == SecurityFieldProvenanceOrigins.CanonicalWrite)
                .OrderByDescending(static item => item.SourceVersion).FirstOrDefault();
            if (attribution is not null)
                source = attribution.SourceSystem;
        }
        var issuer = Text(record.CommonTerms, "issuerName") ?? Text(terms, "issuerName") ?? Text(terms, "institutionName") ?? Text(terms, "borrower");
        var schemaVersion = Text(record.Provenance, "sourceSchemaVersion");
        string? blocker = null, reason = null;
        if (SecurityTermsEnvelope.IsProfileGovernedPayload(record.AssetClass, terms))
        { blocker = "ProfileSpecificRepairRequired"; reason = "This profile owns its fields; use the governed profile-specific repair workflow."; }
        else if (unitSchema is null)
        { blocker = "FullScheduleMigrationRequired"; reason = "This class has no scalar rateUnit field. Its unit requires a complete validated schedule migration and impact review."; }
        else if (declaredUnit is not null)
        { blocker = "UnitAlreadyDeclared"; reason = "The record already declares a unit; changing it requires an individual governed correction."; }
        else if (fieldMeaning is null || !fieldMeaning.HonorsRecordUnit || !numeric)
        { blocker = "RateFieldUnavailable"; reason = "A declared numeric annual-rate field is required before selecting its unit."; }
        else if (fieldMeaning.DefaultUnit is not null)
        { blocker = "FieldUnitAlreadyDefined"; reason = "This field has an explicit class convention; no ambiguous unit repair is needed."; }
        else if (source == SecurityMasterProvenanceReader.UnknownSource || string.IsNullOrWhiteSpace(source))
        { blocker = "SourceEvidenceRequired"; reason = "The rate's source is unknown; establish individual source provenance before grouping it."; }
        var fixPath = $"/accounting/security-master?securityId={record.SecurityId:D}";
        if (blocker == "FullScheduleMigrationRequired" && SecurityAssetClassCatalog.GetOrDefault(record.AssetClass).CarriesBondCouponStructure)
            fixPath += "&tab=evidence&bondAction=migration";
        return new(record.SecurityId, record.DisplayName, record.AssetClass, record.Version, source, path,
            schemaVersion, issuer, raw, declaredUnit?.ToString(), unitSchema?.AllowedValues ?? [], blocker is null,
            blocker, reason, fixPath);
    }

    private static string? Text(JsonElement document, string key)
        => document.ValueKind == JsonValueKind.Object && document.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private async Task<SecurityRateRepairBatchDto> ToDtoAsync(SecurityRateRepairRetainedBatch batch, SecurityRateRepairActor actor, CancellationToken ct)
    {
        var currentProgress = new List<SecurityRateRepairProgress>();
        foreach (var progress in batch.Items)
        {
            var revision = progress.RevisionId is { } id ? await revisions.GetAsync(id, ct).ConfigureAwait(false) : null;
            if (revision is null)
            { currentProgress.Add(progress); continue; }
            var workflow = await workflows.GetAsync(progress.WorkflowId, ct).ConfigureAwait(false);
            var approval = workflow?.Approvals.LastOrDefault();
            currentProgress.Add(progress with
            {
                State = revision.State.ToString(),
                SubmittedBy = revision.SubmittedBy ?? progress.SubmittedBy,
                AssignedReviewer = approval?.Reviewer ?? progress.AssignedReviewer,
                ApprovalReceiptId = approval?.Status == OperationsApprovalStateDto.Approved ? approval.ApprovalId : progress.ApprovalReceiptId
            });
        }
        batch = batch with { Items = currentProgress };
        return ToDto(batch, actor, currentAuthority: true);
    }

    private static SecurityRateRepairBatchDto ToDto(SecurityRateRepairRetainedBatch batch, SecurityRateRepairActor actor, bool currentAuthority)
    {
        var manifest = batch.Manifest;
        var request = manifest.Request;
        var items = manifest.Items.Select(item =>
        {
            var progress = batch.Items.Single(row => row.SecurityId == item.SecurityId);
            return new SecurityRateRepairItemDto(item.SecurityId, item.BaseVersion, item.DisplayName, item.AssetClass,
                item.RawValue, progress.State, progress.RevisionId, progress.WorkflowId, progress.ImpactReceiptId,
                progress.ApprovalReceiptId, progress.ErrorCode, progress.Error);
        }).ToArray();
        bool canModify = currentAuthority && actor.CanModify && !string.IsNullOrWhiteSpace(actor.CompanyId);
        var checker = !string.Equals(manifest.Maker.Trim(), actor.Actor.Trim(), StringComparison.OrdinalIgnoreCase);
        var actions = new SecurityRateRepairActionDto[]
        {
            Action("Stage", canModify && !checker && items.Any(static item => item.RevisionId is null || item.ImpactReceiptId is null
                    || (item.ErrorCode is not null && item.State is "Draft" or "Submitted")),
                "Only the original maker can stage remaining items or refresh their impact evidence."),
            Action("Submit", canModify && items.Any(static item => item.State == "Draft" && item.ImpactReceiptId is not null),
                "Staged drafts need retained impact evidence and an independent assigned reviewer."),
            Action("Approve", canModify && checker && batch.Items.Any(item => item.State == "Submitted"
                    && string.Equals(item.AssignedReviewer?.Trim(), actor.Actor.Trim(), StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(item.SubmittedBy?.Trim(), actor.Actor.Trim(), StringComparison.OrdinalIgnoreCase)),
                checker ? "Only the assigned independent reviewer can approve submitted items." : "The original maker cannot approve this batch."),
            Action("Publish", canModify && items.Any(static item => item.State == "Approved"),
                "Only approved items with current impact evidence can publish.")
        };
        if (!currentAuthority)
            actions = actions.Select(static action => action with
            { Enabled = false, DisabledReason = "Open the retained batch to inspect its current revision and approval authority." }).ToArray();
        return new(request.RequestId, batch.Version, manifest.CreatedAt, manifest.Maker, request.Scope, request.Cohort,
            request.Unit, request.EffectiveFrom, request.EvidenceId, manifest.EvidenceSha256, request.Justification, items, actions);
    }

    private static SecurityRateRepairActionDto Action(string command, bool enabled, string reason)
        => new(command, enabled, enabled ? null : reason);
}
