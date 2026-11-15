using System.Text.Json;
using System.Data.Common;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>Supplemental audit lookup for the exact version retained in a report receipt.</summary>
public sealed class SecurityReportInputAttributionResolver(ISecurityMasterEventStore? events,
    ISecurityMasterRevisionStore? revisions, IOperationsContinuityWorkflowService? workflows,
    ILedgerJournalStore? ledger, IFundProfileTenancyRegistry? tenancy)
{
    public async Task<SecurityReportChangeAttributionDto> ResolveAsync(SecurityDetailDto retained,
        string tenantId, string companyId, CancellationToken ct = default)
    {
        try
        { return await ResolveCoreAsync(retained, tenantId, companyId, ct).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or DbException or NotSupportedException or InvalidOperationException)
        {
            return new(retained.Version, null, null, null, null, null, null, null, null, null,
                ["Supplemental exact-version attribution could not be read from its audit authorities."]);
        }
    }

    private async Task<SecurityReportChangeAttributionDto> ResolveCoreAsync(SecurityDetailDto retained,
        string tenantId, string companyId, CancellationToken ct)
    {
        var gaps = new List<string>();
        var result = new SecurityReportChangeAttributionDto(retained.Version, null, null, null,
            null, null, null, null, null, null, gaps);
        if (events is null)
            return Gap("Exact-version Security Master event authority is unavailable.");
        var matching = (await events.LoadAsync(retained.SecurityId, ct).ConfigureAwait(false))
            .Where(row => row.SecurityId == retained.SecurityId && row.StreamVersion == retained.Version).ToArray();
        if (matching.Length != 1)
            return Gap("The retained security version has no unique immutable event; no latest-version fallback was used.");
        var envelope = matching[0];
        SecurityEconomicDefinitionRecord economic;
        try
        {
            economic = SecurityMasterMapping.FromEventEnvelope(envelope);
            var exact = SecurityMasterMapping.ToDetail(SecurityEconomicDefinitionAdapter.ToProjection(economic));
            var retainedTerms = RetainedSecurityTermsReadBoundary.ReadTerms(retained.AssetClass, retained.AssetSpecificTerms);
            if (exact.SecurityId != retained.SecurityId || exact.Version != retained.Version
                || exact.AssetClass != retained.AssetClass || exact.Currency != retained.Currency
                || !JsonElement.DeepEquals(exact.CommonTerms, retained.CommonTerms)
                || !JsonElement.DeepEquals(exact.AssetSpecificTerms, retainedTerms))
                return Gap("The exact-version event does not match the calculation receipt's retained terms.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or InvalidDataException)
        { return Gap("The exact-version event cannot be decoded by this node."); }
        result = result with { EventActor = envelope.Actor, EventRecordedAt = envelope.EventTimestamp };
        var payloadSource = SourceRecord(economic.Provenance);
        var metadataSource = SourceRecord(envelope.Metadata);
        if (payloadSource is not null && metadataSource is not null && payloadSource != metadataSource)
            return Gap("The immutable event has contradictory revision provenance.");
        if (!Guid.TryParse(payloadSource ?? metadataSource, out var revisionId))
            return Gap("The exact-version event has no governed revision reference; its event actor is not evidence of who drafted or approved it.");
        if (ReadString(economic.Provenance, "sourceSystem") != SecurityMasterWriteAuthority.WorkbenchSourceSystem)
            return Gap("The exact-version event does not declare governed workbench revision provenance.");
        if (revisions is null || workflows is null || ledger is null || tenancy is null)
            return Gap("Governed revision/workflow attribution authority is unavailable.");
        var revision = await revisions.GetAsync(revisionId, ct).ConfigureAwait(false);
        if (revision is null || revision.RevisionId != revisionId || revision.SecurityId != retained.SecurityId
            || revision.State != SecurityMasterRevisionStateDto.Published)
            return Gap("The event's referenced published revision cannot be verified for this security/version.");
        if (revision.WorkflowId is not { } workflowId)
            return Gap("The referenced revision has no retained approval workflow binding.");
        var workflow = await workflows.GetAsync(workflowId, ct).ConfigureAwait(false);
        if (workflow is null || workflow.WorkflowId != workflowId || workflow.LedgerBookId is not { } bookId)
            return Gap("The retained workflow's ownership cannot be verified in the current scope.");
        var book = await ledger.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false);
        var owner = book is null ? null : await tenancy.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
        if (owner is null || !owner.IsHeldBy(tenantId)
            || !string.Equals(owner.CompanyId, companyId, StringComparison.Ordinal)
            || revision.FundProfileId is { } fund && fund != book!.FundProfileId)
            return Gap("The retained workflow's ownership cannot be verified in the current scope.");
        if (workflow.Purpose == OperationsWorkflowPurposeDto.SecurityMasterRevision
            && (workflow.SubjectRevisionId != revisionId || string.IsNullOrWhiteSpace(workflow.SubjectMaker)
                || string.IsNullOrWhiteSpace(workflow.SubjectEvidenceId) || workflow.SubjectEvidenceSha256?.Length != 64
                || workflow.SubjectMaker != revision.DraftedBy
                || workflow.SubjectEvidenceId != revision.RepairEvidenceId
                || workflow.SubjectEvidenceSha256 != revision.RepairEvidenceSha256))
            return Gap("The retained workflow's exact revision and source-evidence identity cannot be verified.");
        if (workflow.ApprovalState != OperationsApprovalStateDto.Approved)
            return Gap("The referenced workflow has no retained approved state.");
        var marker = $"[security-master-revision:{revisionId:D}]";
        var pairs = workflow.Approvals.Select((approval, index) => (approval, index))
            .Where(row => row.approval.Status == OperationsApprovalStateDto.Approved)
            .Select(row => (Decision: row.approval, Submission: workflow.Approvals.Take(row.index).LastOrDefault()))
            .Where(pair => pair.Submission is { Status: OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned }
                && pair.Submission.Rationale?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true
                && !string.IsNullOrWhiteSpace(pair.Submission.Operator)
                && pair.Submission.SubmittedAtUtc == pair.Decision.SubmittedAtUtc
                && pair.Submission.Operator == pair.Decision.Operator
                && pair.Decision.DecidedAtUtc.HasValue && !string.IsNullOrWhiteSpace(pair.Decision.Reviewer)
                && !string.IsNullOrWhiteSpace(pair.Decision.ApprovalId)
                && pair.Submission.SubmittedAtUtc <= pair.Decision.DecidedAtUtc
                && pair.Decision.DecidedAtUtc <= envelope.EventTimestamp
                && !string.Equals(pair.Decision.Reviewer, pair.Decision.Operator, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pair.Decision.Reviewer, revision.DraftedBy, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (pairs.Length != 1)
            return Gap("A unique approval decision bound to the exact revision submission is unavailable.");
        var approval = pairs[0].Decision;
        if (revision.SubmittedBy is { } submitter && submitter != approval.Operator)
            return Gap("The revision's retained submitter contradicts its approval submission.");
        if (string.IsNullOrWhiteSpace(revision.DraftedBy))
            gaps.Add("The legacy revision did not retain its drafter; no event-actor or publisher fallback was used.");
        if (string.IsNullOrWhiteSpace(revision.SubmittedBy))
            gaps.Add("The revision did not retain its submitter separately; the workflow's exact submission operator is shown.");
        return result with
        {
            RevisionId = revisionId,
            DraftedBy = string.IsNullOrWhiteSpace(revision.DraftedBy) ? null : revision.DraftedBy,
            SubmittedBy = string.IsNullOrWhiteSpace(revision.SubmittedBy) ? approval.Operator : revision.SubmittedBy,
            ApprovedBy = approval.Reviewer,
            ApprovedAt = approval.DecidedAtUtc,
            ApprovalId = approval.ApprovalId,
            EvidenceRoute = $"/api/workstation/operations/continuity/{workflowId:D}"
        };

        SecurityReportChangeAttributionDto Gap(string message)
        { gaps.Add(message); return result; }
    }

    private static string? SourceRecord(JsonElement value) => ReadString(value, "sourceRecordId");
    private static string? ReadString(JsonElement value, string key)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var source)
            && source.ValueKind == JsonValueKind.String ? source.GetString() : null;
}
