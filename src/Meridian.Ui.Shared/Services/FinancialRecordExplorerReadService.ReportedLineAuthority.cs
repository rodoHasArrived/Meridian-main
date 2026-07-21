using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
    private static bool IsAuthoritativelyReported(ReportPackWorkflowRecordDto record)
    {
        if (record.State is not (ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated) ||
            record.Publication is not { } publication ||
            record.LineProvenance is not { Count: > 0 })
        {
            return false;
        }

        var publicationIsComplete =
            HasText(publication.ManifestId) &&
            HasText(publication.RetainedManifestPath) &&
            HasText(publication.EvidenceHash) &&
            HasText(publication.SignedOffBy) &&
            HasText(publication.SignedOffRole) &&
            HasText(publication.SignOffContext) &&
            publication.SignedOffAt != default &&
            publication.ActionOrigin == OperationsActionOriginDto.HumanOperator &&
            HasCompleteReportEvidence(publication.EvidenceLinks) &&
            record.AuditTrail.Any(static audit =>
                audit.ToState == ReportPackWorkflowStateDto.Approved &&
                HasText(audit.Actor) &&
                audit.At != default) &&
            record.AuditTrail.Any(static audit =>
                audit.ToState == ReportPackWorkflowStateDto.Published &&
                HasText(audit.Actor) &&
                audit.At != default) &&
            record.LineProvenance.All(line => EnumerateReportLineEvidencePointers(line)
                .All(pointer => publication.EvidenceLinks.Any(link =>
                    string.Equals(link.EvidenceId, pointer, StringComparison.OrdinalIgnoreCase))));
        if (!publicationIsComplete)
        {
            return false;
        }

        if (record.State == ReportPackWorkflowStateDto.Published)
        {
            return true;
        }

        return record.Restatement is { } restatement &&
               HasText(restatement.ReasonCode) &&
               HasText(restatement.Approver) &&
               restatement.PriorVersionReportId != Guid.Empty &&
               restatement.ChangedLines.Count > 0 &&
               restatement.ChangedLines.All(static line =>
                   HasText(line.LineKey) &&
                   HasText(line.PreviousValue) &&
                   HasText(line.CurrentValue) &&
                   HasCompleteReportEvidence(line.EvidenceLinks)) &&
               HasCompleteReportEvidence(restatement.EvidenceLinks) &&
               record.AuditTrail.Any(static audit =>
                   audit.ToState == ReportPackWorkflowStateDto.Restated &&
                   HasText(audit.Actor) &&
                   audit.At != default);
    }

    private static bool HasCompleteReportEvidence(IReadOnlyList<ReportPackEvidenceLinkDto>? evidenceLinks)
        => evidenceLinks is { Count: > 0 } &&
           evidenceLinks.All(static evidence =>
               HasText(evidence.EvidenceId) &&
               HasText(evidence.Label) &&
               HasText(evidence.Source));

    private static IEnumerable<string> EnumerateReportLineEvidencePointers(ReportPackLineProvenanceDto line)
    {
        yield return line.EvidenceId;
        yield return line.SourceId;

        foreach (var pointer in new[]
                 {
                     line.RunId,
                     line.SourceSessionId,
                     line.LedgerEntryId,
                     line.ReconciliationCaseId,
                     line.ReconciliationRunId,
                     line.ProviderEventId,
                     line.SecurityMasterId,
                     line.SecurityDefinitionId,
                     line.ApprovalId
                 })
        {
            if (HasText(pointer))
            {
                yield return pointer!;
            }
        }
    }
}
