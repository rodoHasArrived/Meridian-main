using System.Globalization;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
    private static FinancialRecordExplorerRecordGraphDto BuildGraph(IReadOnlyList<FinancialRecordExplorerRowDto> rows)
    {
        if (rows.Count == 0)
        {
            return new FinancialRecordExplorerRecordGraphDto([], []);
        }

        var nodes = rows
            .Take(25)
            .Select(static row => new FinancialRecordExplorerGraphNodeDto(row.RecordId, row.Label, row.RecordType, row.Tone, row.Detail.FullRecordHref))
            .ToList();
        var edges = new List<FinancialRecordExplorerGraphEdgeDto>();
        foreach (var row in rows.Take(25))
        {
            foreach (var relationship in row.Detail.UsedIn.Take(2))
            {
                var nodeId = $"rel:{relationship.RelationshipId}";
                if (nodes.All(node => !string.Equals(node.NodeId, nodeId, StringComparison.Ordinal)))
                {
                    nodes.Add(new FinancialRecordExplorerGraphNodeDto(nodeId, relationship.Label, "relationship", relationship.Tone, relationship.Href));
                }

                edges.Add(new FinancialRecordExplorerGraphEdgeDto(row.RecordId, nodeId, "used in", relationship.Tone));
            }
        }

        return new FinancialRecordExplorerRecordGraphDto(nodes, edges);
    }

    private static FinancialRecordExplorerRecordGraphDto BuildReportLineProvenanceGraph(
        IReadOnlyList<FinancialRecordExplorerRowDto> rows)
    {
        if (rows.Count == 0)
        {
            return new FinancialRecordExplorerRecordGraphDto([], []);
        }

        var nodes = new List<FinancialRecordExplorerGraphNodeDto>();
        var edges = new List<FinancialRecordExplorerGraphEdgeDto>();

        foreach (var row in rows.Take(12))
        {
            AddGraphNode(nodes, new(row.RecordId, row.Label, row.RecordType, row.Tone, row.Detail.FullRecordHref));

            var instrument = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "instrument");
            var activity = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "position-transaction");
            var reconciliation = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "reconciliation");
            var report = row.Detail.UsedIn.FirstOrDefault(static relationship => relationship.RelationshipId.StartsWith("report-pack:", StringComparison.OrdinalIgnoreCase));
            var evidence = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "evidence-audit-links");
            var evidencePacket = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "evidence") ?? evidence;
            var auditEvent = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "audit-event");

            var previousNodeId = AddRelationshipGraphNode(nodes, row.RecordId, instrument, "instrument");
            var activityNodeId = AddRelationshipGraphNode(nodes, row.RecordId, activity, "position-transaction");
            AddGraphEdge(edges, previousNodeId, activityNodeId, "feeds", activity?.Tone ?? FinancialRecordExplorerTone.Info);

            var reconciliationNodeId = AddRelationshipGraphNode(nodes, row.RecordId, reconciliation, "reconciliation");
            AddGraphEdge(edges, activityNodeId, reconciliationNodeId, "reconciles", reconciliation?.Tone ?? FinancialRecordExplorerTone.Info);

            AddGraphEdge(edges, reconciliationNodeId, row.RecordId, "reported", row.Tone);

            var reportNodeId = AddRelationshipGraphNode(nodes, row.RecordId, report, "report-pack");
            AddGraphEdge(edges, row.RecordId, reportNodeId, "included in", report?.Tone ?? row.Tone);

            var evidenceNodeId = AddRelationshipGraphNode(nodes, row.RecordId, evidence, "evidence-audit-links");
            AddGraphEdge(edges, reportNodeId, evidenceNodeId, "retains audit", evidence?.Tone ?? FinancialRecordExplorerTone.Info);

            var evidencePacketNodeId = AddRelationshipGraphNode(nodes, row.RecordId, evidencePacket, "evidence");
            AddGraphEdge(edges, row.RecordId, evidencePacketNodeId, "retains evidence", evidencePacket?.Tone ?? FinancialRecordExplorerTone.Info);

            var auditNodeId = AddRelationshipGraphNode(nodes, row.RecordId, auditEvent, "audit-event");
            AddGraphEdge(edges, evidencePacketNodeId, auditNodeId, "audits", auditEvent?.Tone ?? FinancialRecordExplorerTone.Info);
        }

        return new FinancialRecordExplorerRecordGraphDto(nodes, edges);
    }

    private static FinancialRecordExplorerRecordGraphDto BuildSecurityInstrumentGraph(
        IReadOnlyList<FinancialRecordExplorerRowDto> rows)
    {
        if (rows.Count == 0)
        {
            return new FinancialRecordExplorerRecordGraphDto([], []);
        }

        var nodes = new List<FinancialRecordExplorerGraphNodeDto>();
        var edges = new List<FinancialRecordExplorerGraphEdgeDto>();

        foreach (var row in rows.Take(12))
        {
            AddGraphNode(nodes, new(row.RecordId, row.Label, row.RecordType, row.Tone, row.Detail.FullRecordHref));

            var positionTransaction = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "position-transaction");
            var passport = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "instrument-passport");
            var operations = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "asset-operations-readiness");
            var terms = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "terms-obligations");
            var reconciliation = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "reconciliation");
            var journal = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "journal");
            var reportLine = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "report-line");
            var evidence = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "evidence");
            var audit = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "audit-event");
            AppendInstrumentJournalProofGraph(nodes, edges, row, journal);

            var positionTransactionNodeId = AddRelationshipGraphNode(nodes, row.RecordId, positionTransaction, "position-transaction");
            AddGraphEdge(edges, row.RecordId, positionTransactionNodeId, "referenced by", positionTransaction?.Tone ?? FinancialRecordExplorerTone.Info);

            var passportNodeId = AddRelationshipGraphNode(nodes, row.RecordId, passport, "instrument-passport");
            AddGraphEdge(edges, row.RecordId, passportNodeId, "trust", passport?.Tone ?? row.Tone);

            var operationsNodeId = AddRelationshipGraphNode(nodes, row.RecordId, operations, "asset-operations-readiness");
            AddGraphEdge(edges, row.RecordId, operationsNodeId, "operations", operations?.Tone ?? FinancialRecordExplorerTone.Info);

            var termsNodeId = AddRelationshipGraphNode(nodes, row.RecordId, terms, "terms-obligations");
            AddGraphEdge(edges, operationsNodeId, termsNodeId, "terms", terms?.Tone ?? FinancialRecordExplorerTone.Info);

            var reconciliationNodeId = AddRelationshipGraphNode(nodes, row.RecordId, reconciliation, "reconciliation");
            AddGraphEdge(edges, operationsNodeId, reconciliationNodeId, "reconciles", reconciliation?.Tone ?? FinancialRecordExplorerTone.Info);

            string? journalNodeId = null;
            if (journal is not null)
            {
                journalNodeId = AddRelationshipGraphNode(nodes, row.RecordId, journal, "journal");
                AddGraphEdge(edges, reconciliationNodeId, journalNodeId, "posts", journal.Tone);
            }

            string? reportLineNodeId = null;
            if (reportLine is not null)
            {
                reportLineNodeId = AddRelationshipGraphNode(nodes, row.RecordId, reportLine, "report-line");
                AddGraphEdge(
                    edges,
                    journalNodeId ?? reconciliationNodeId,
                    reportLineNodeId,
                    "reported",
                    reportLine.Tone);
            }

            var evidenceNodeId = AddRelationshipGraphNode(nodes, row.RecordId, evidence, "evidence");
            AddGraphEdge(edges, reportLineNodeId ?? reconciliationNodeId, evidenceNodeId, "retains evidence", evidence?.Tone ?? FinancialRecordExplorerTone.Info);

            var auditNodeId = AddRelationshipGraphNode(nodes, row.RecordId, audit, "audit-event");
            AddGraphEdge(edges, evidenceNodeId, auditNodeId, "audits", audit?.Tone ?? FinancialRecordExplorerTone.Info);
        }

        return new FinancialRecordExplorerRecordGraphDto(nodes, edges);
    }

    private static string AddRelationshipGraphNode(
        List<FinancialRecordExplorerGraphNodeDto> nodes,
        string rowRecordId,
        FinancialRecordExplorerRelationshipDto? relationship,
        string fallbackNodeType)
    {
        var nodeId = relationship is null
            ? $"rel:{rowRecordId}:{fallbackNodeType}"
            : $"rel:{rowRecordId}:{relationship.RelationshipId}";
        AddGraphNode(nodes, new(
            nodeId,
            relationship?.Label ?? fallbackNodeType,
            relationship?.RelationshipId ?? fallbackNodeType,
            relationship?.Tone ?? FinancialRecordExplorerTone.Warning,
            relationship?.Href ?? string.Empty));
        return nodeId;
    }

    private static void AddGraphNode(
        List<FinancialRecordExplorerGraphNodeDto> nodes,
        FinancialRecordExplorerGraphNodeDto node)
    {
        if (nodes.Any(existing => string.Equals(existing.NodeId, node.NodeId, StringComparison.Ordinal)))
        {
            return;
        }

        nodes.Add(node);
    }

    private static void AddGraphEdge(
        List<FinancialRecordExplorerGraphEdgeDto> edges,
        string sourceNodeId,
        string targetNodeId,
        string label,
        FinancialRecordExplorerTone tone)
    {
        if (string.IsNullOrWhiteSpace(sourceNodeId) ||
            string.IsNullOrWhiteSpace(targetNodeId) ||
            string.Equals(sourceNodeId, targetNodeId, StringComparison.Ordinal))
        {
            return;
        }

        edges.Add(new(sourceNodeId, targetNodeId, label, tone));
    }

}
