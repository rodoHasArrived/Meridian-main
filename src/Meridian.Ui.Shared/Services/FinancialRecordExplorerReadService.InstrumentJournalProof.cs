using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
    private async Task<IReadOnlyList<InstrumentJournalProof>> BuildInstrumentJournalProofsAsync(
        AssetOperationsDetailDto operations,
        CancellationToken ct)
    {
        var proofs = new List<InstrumentJournalProof>();
        foreach (var position in operations.BookPositions)
        {
            var lineage = position.ProjectionLineage ?? operations.ProjectionLineages
                .FirstOrDefault(candidate => candidate.BookPositionId == position.PositionId ||
                    candidate.TriggerEvent.BookPositionId == position.PositionId);
            if (lineage is null ||
                !string.Equals(lineage.ModelKey, "mbs-factor-paydown", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            LedgerJournalEntryRecord? journal = null;
            if (_ledgerJournalStore is not null)
            {
                try
                {
                    var journals = await _ledgerJournalStore.QueryAsync(
                        new LedgerJournalEntryQuery(
                            LedgerBookId: position.BookContext.LedgerBookId,
                            AggregateId: position.BookContext.LedgerBookId,
                            SourceEventId: lineage.TriggerEvent.EventId),
                        ct).ConfigureAwait(false);
                    journal = journals
                        .OrderBy(static candidate => candidate.GlobalSequence)
                        .FirstOrDefault();
                }
                catch (NotSupportedException)
                {
                    journal = null;
                }
            }

            var role = operations.InstrumentRoles.FirstOrDefault(candidate => candidate.RoleId == position.RoleId);
            var state = operations.PositionEconomicStates
                .Where(candidate => candidate.PositionId == position.PositionId)
                .OrderByDescending(static candidate => candidate.AsOfDate)
                .ThenByDescending(static candidate => candidate.Version)
                .FirstOrDefault() ?? position.CurrentEconomicState;
            proofs.Add(new InstrumentJournalProof(position, role, state, lineage, journal));
        }

        return proofs;
    }

    private static void AppendInstrumentJournalProofFields(
        List<FinancialRecordExplorerSummaryItemDto> fields,
        SecurityInstrumentEnrichment enrichment)
    {
        if (enrichment.InstrumentJournalProofs.FirstOrDefault() is not { } proof)
        {
            return;
        }

        fields.Add(new(
            "Factor Evidence",
            proof.Lineage.TriggerEvent.EvidenceLinks.FirstOrDefault() ?? "Missing",
            $"Source event {proof.Lineage.TriggerEvent.EventId:D}; content hash {proof.Lineage.TriggerEvent.SourceContentHash ?? "missing"}.",
            proof.Lineage.TriggerEvent.EvidenceLinks.Count > 0 ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning));
        fields.Add(new(
            "Role / Position",
            $"{proof.Role?.RoleKind ?? "Unresolved"} / {proof.Position.PositionId:D}",
            $"Role {proof.Position.RoleId:D}; ledger book {proof.Position.BookContext.LedgerBookId:D}; position version {proof.Position.Version.ToString(CultureInfo.InvariantCulture)}.",
            proof.Role is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success));
        fields.Add(new(
            "Economic Projection",
            proof.Lineage.ModelKey,
            $"Projection run {proof.Lineage.ProjectionRunId:D}; projection event {proof.Lineage.ProjectionEventId?.ToString("D") ?? "none"}; trigger {proof.Lineage.TriggerEvent.EventId:D}; factors {proof.State?.PriorFactor?.ToString(CultureInfo.InvariantCulture) ?? "?"} to {proof.State?.CurrentFactor?.ToString(CultureInfo.InvariantCulture) ?? "?"}.",
            FinancialRecordExplorerTone.Info));
        fields.Add(new(
            "Posting Candidate",
            proof.Journal?.CommandId?.ToString("D") ?? "Pending",
            proof.Journal is null
                ? "The projection is retained, but no immutable journal exists for the ledger-book/source-event key."
                : $"Posting command {proof.Journal.CommandId?.ToString("D") ?? "not stamped"} resolved through the immutable journal.",
            proof.Journal is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Info));
        fields.Add(new(
            "Approval",
            JournalTag(proof.Journal, "approvalState") ?? "Pending",
            $"Approval id {JournalTag(proof.Journal, "approvalId") ?? "not retained"}.",
            string.Equals(JournalTag(proof.Journal, "approvalState"), "Approved", StringComparison.OrdinalIgnoreCase)
                ? FinancialRecordExplorerTone.Success
                : FinancialRecordExplorerTone.Warning));
        fields.Add(new(
            "Immutable JournalEntry",
            proof.Journal?.Entry.JournalEntryId.ToString("D") ?? "Not posted",
            proof.Journal is null
                ? "Journal truth remains absent; the projection is not an accounting fact."
                : $"Global sequence {proof.Journal.GlobalSequence.ToString(CultureInfo.InvariantCulture)}; aggregate {proof.Journal.AggregateId:D}; period {proof.Journal.PeriodId:D}.",
            proof.Journal is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success));
        fields.Add(new(
            "Ledger / Report Evidence",
            proof.Journal is null ? "Awaiting journal" : $"{proof.Journal.Entry.Lines.Count.ToString(CultureInfo.InvariantCulture)} ledger lines",
            $"Immutable journal {proof.Journal?.Entry.JournalEntryId.ToString("D") ?? "none"}; retained report-line references {enrichment.ReportLineUsages.Count.ToString(CultureInfo.InvariantCulture)}.",
            proof.Journal is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Info));
    }

    private static void AppendInstrumentJournalProofImpacts(
        List<FinancialRecordExplorerRelationshipDto> impacts,
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
    {
        if (enrichment.InstrumentJournalProofs.FirstOrDefault() is not { } proof)
        {
            return;
        }

        var assetHref = BuildAssetOperationsHref(reference.SecurityId);
        var journalHref = BuildSecurityJournalHref(enrichment);
        var journalImpactIndex = impacts.FindIndex(static impact => impact.RelationshipId == "journal");
        var durableJournalImpact = new FinancialRecordExplorerRelationshipDto(
            "journal",
            "Immutable JournalEntry",
            proof.Journal is null
                ? $"No journal exists for ledger book {proof.Position.BookContext.LedgerBookId:D} and source event {proof.Lineage.TriggerEvent.EventId:D}."
                : $"Journal {proof.Journal.Entry.JournalEntryId:D}; global sequence {proof.Journal.GlobalSequence.ToString(CultureInfo.InvariantCulture)}; source event {proof.Lineage.TriggerEvent.EventId:D}.",
            journalHref,
            proof.Journal is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success);
        if (journalImpactIndex >= 0)
        {
            impacts[journalImpactIndex] = durableJournalImpact;
        }
        else
        {
            impacts.Add(durableJournalImpact);
        }

        impacts.Add(new(
            "factor-evidence",
            "Factor evidence",
            $"{proof.Lineage.TriggerEvent.EvidenceLinks.FirstOrDefault() ?? "Missing evidence"}; source hash {proof.Lineage.TriggerEvent.SourceContentHash ?? "missing"}.",
            proof.Lineage.TriggerEvent.EvidenceLinks.FirstOrDefault() ?? BuildSecurityEvidenceHref(reference, enrichment),
            proof.Lineage.TriggerEvent.EvidenceLinks.Count > 0 ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning));
        impacts.Add(new(
            "instrument-role-position",
            "Role / position",
            $"Role {proof.Position.RoleId:D}; position {proof.Position.PositionId:D}; ledger book {proof.Position.BookContext.LedgerBookId:D}.",
            assetHref,
            proof.Role is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success));
        impacts.Add(new(
            "economic-projection",
            "Economic projection",
            $"{proof.Lineage.ModelKey} {proof.Lineage.ModelVersion}; run {proof.Lineage.ProjectionRunId:D}; event {proof.Lineage.TriggerEvent.EventId:D}.",
            assetHref,
            FinancialRecordExplorerTone.Info));
        impacts.Add(new(
            "posting-candidate",
            "Posting candidate",
            $"Command {proof.Journal?.CommandId?.ToString("D") ?? "pending"}.",
            journalHref,
            proof.Journal is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Info));
        impacts.Add(new(
            "approval",
            "Independent approval",
            $"Approval {JournalTag(proof.Journal, "approvalId") ?? "pending"}; state {JournalTag(proof.Journal, "approvalState") ?? "Pending"}.",
            journalHref,
            string.Equals(JournalTag(proof.Journal, "approvalState"), "Approved", StringComparison.OrdinalIgnoreCase)
                ? FinancialRecordExplorerTone.Success
                : FinancialRecordExplorerTone.Warning));
        impacts.Add(new(
            "ledger-report-evidence",
            "Ledger / report evidence",
            $"Journal {proof.Journal?.Entry.JournalEntryId.ToString("D") ?? "not posted"}; report-line references {enrichment.ReportLineUsages.Count.ToString(CultureInfo.InvariantCulture)}.",
            proof.Journal is null ? assetHref : journalHref,
            proof.Journal is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success));
    }

    private static void AppendInstrumentJournalProofUsedIn(
        List<FinancialRecordExplorerRelationshipDto> relationships,
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
    {
        if (enrichment.InstrumentJournalProofs.Count == 0)
        {
            return;
        }

        relationships.Add(new(
            "instrument-journal-proof",
            "Instrument-to-journal proof",
            $"{enrichment.InstrumentJournalProofs.Count.ToString(CultureInfo.InvariantCulture)} typed role/position projection chain{Plural(enrichment.InstrumentJournalProofs.Count)} can be reconstructed by ledger book and source event.",
            BuildAssetOperationsHref(reference.SecurityId),
            enrichment.InstrumentJournalProofs.Any(static proof => proof.Journal is not null)
                ? FinancialRecordExplorerTone.Success
                : FinancialRecordExplorerTone.Warning));
    }

    private static void AppendInstrumentJournalProofGraph(
        List<FinancialRecordExplorerGraphNodeDto> nodes,
        List<FinancialRecordExplorerGraphEdgeDto> edges,
        FinancialRecordExplorerRowDto row,
        FinancialRecordExplorerRelationshipDto? journal)
    {
        var factorEvidence = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "factor-evidence");
        if (factorEvidence is null)
        {
            return;
        }

        var rolePosition = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "instrument-role-position");
        var economicProjection = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "economic-projection");
        var postingCandidate = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "posting-candidate");
        var approval = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "approval");
        var ledgerReportEvidence = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "ledger-report-evidence");
        var factorEvidenceNodeId = AddRelationshipGraphNode(nodes, row.RecordId, factorEvidence, "factor-evidence");
        var rolePositionNodeId = AddRelationshipGraphNode(nodes, row.RecordId, rolePosition, "instrument-role-position");
        var economicProjectionNodeId = AddRelationshipGraphNode(nodes, row.RecordId, economicProjection, "economic-projection");
        var postingCandidateNodeId = AddRelationshipGraphNode(nodes, row.RecordId, postingCandidate, "posting-candidate");
        var approvalNodeId = AddRelationshipGraphNode(nodes, row.RecordId, approval, "approval");
        var immutableJournalNodeId = AddRelationshipGraphNode(nodes, row.RecordId, journal, "journal");
        var ledgerReportEvidenceNodeId = AddRelationshipGraphNode(nodes, row.RecordId, ledgerReportEvidence, "ledger-report-evidence");
        AddGraphEdge(edges, factorEvidenceNodeId, rolePositionNodeId, "supports", factorEvidence.Tone);
        AddGraphEdge(edges, rolePositionNodeId, economicProjectionNodeId, "projects", rolePosition?.Tone ?? FinancialRecordExplorerTone.Info);
        AddGraphEdge(edges, economicProjectionNodeId, postingCandidateNodeId, "proposes", economicProjection?.Tone ?? FinancialRecordExplorerTone.Info);
        AddGraphEdge(edges, postingCandidateNodeId, approvalNodeId, "requires", postingCandidate?.Tone ?? FinancialRecordExplorerTone.Warning);
        AddGraphEdge(edges, approvalNodeId, immutableJournalNodeId, "authorizes", approval?.Tone ?? FinancialRecordExplorerTone.Warning);
        AddGraphEdge(edges, immutableJournalNodeId, ledgerReportEvidenceNodeId, "proves", journal?.Tone ?? FinancialRecordExplorerTone.Warning);
    }

    private static string BuildSecurityJournalHref(SecurityInstrumentEnrichment enrichment)
    {
        var journalEntryId = enrichment.InstrumentJournalProofs
            .Select(static proof => proof.Journal?.Entry.JournalEntryId)
            .FirstOrDefault(static id => id.HasValue);
        if (journalEntryId.HasValue)
        {
            return UiApiRoutes.WithQuery(
                UiApiRoutes.LedgerManualJournalEntryWorkbench,
                $"ledgerEntryId={Uri.EscapeDataString(journalEntryId.Value.ToString("D"))}");
        }

        var reportLineHref = enrichment.ReportLineUsages
            .Select(static usage => BuildReportLineJournalHref(usage.Line))
            .FirstOrDefault(HasText);
        if (HasText(reportLineHref))
        {
            return reportLineHref!;
        }

        var ledgerReference = enrichment.Operations?.LedgerProjections
            .Select(static projection => projection.LedgerReferenceId)
            .FirstOrDefault(HasText);
        return HasText(ledgerReference)
            ? UiApiRoutes.WithQuery(
                UiApiRoutes.LedgerManualJournalEntryWorkbench,
                $"ledgerEntryId={Uri.EscapeDataString(ledgerReference!)}")
            : string.Empty;
    }

    private static string BuildAssetOperationsHref(Guid securityId)
        => UiApiRoutes.WithParam(UiApiRoutes.WorkstationAssetOperations, "securityId", securityId.ToString("D"));

    private sealed record InstrumentJournalProof(
        BookPositionDto Position,
        InstrumentRoleDto? Role,
        PositionEconomicStateDto? State,
        ProjectionLineageDto Lineage,
        LedgerJournalEntryRecord? Journal);

    private static string? JournalTag(LedgerJournalEntryRecord? journal, string key)
        => journal?.Entry.Metadata.Tags is { } tags && tags.TryGetValue(key, out var value)
            ? value
            : null;
}
