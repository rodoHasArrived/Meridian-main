using System.Text;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Workflows;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Workstation.Models;

public sealed record EvidenceWorkbenchSubjectRowModel(
    string SubjectKind,
    string SubjectId,
    string Label,
    string Workspace,
    string SubjectKey,
    Guid? LedgerBookId = null)
{
    public static EvidenceWorkbenchSubjectRowModel FromDto(EvidenceSubjectDto subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return new EvidenceWorkbenchSubjectRowModel(
            subject.SubjectKind,
            subject.SubjectId,
            string.IsNullOrWhiteSpace(subject.Label) ? $"{subject.SubjectKind}/{subject.SubjectId}" : subject.Label,
            string.IsNullOrWhiteSpace(subject.Workspace) ? "Workstation" : subject.Workspace,
            $"{subject.SubjectKind}/{subject.SubjectId}",
            subject.LedgerBookId);
    }
}

public sealed record EvidenceWorkbenchSummaryFactModel(
    string Label,
    string Value,
    string Detail);

public sealed record EvidenceProofChainLayerRowModel(
    string LayerId,
    string Label,
    string StatusText,
    WorkstationReadinessTone ReadinessTone,
    string Tone,
    string CoverageText,
    string CountsText,
    string KindsText,
    string Summary);

public sealed record EvidenceNodeRowModel(
    string EvidenceId,
    string KindText,
    string StatusText,
    WorkstationReadinessTone ReadinessTone,
    string Tone,
    string SourceSystem,
    string FreshnessText,
    WorkstationReadinessTone FreshnessTone,
    string Summary,
    string ArtifactCountText,
    string WorkItemCountText);

public sealed record EvidenceLineageRowModel(
    string EdgeId,
    string FromId,
    string RelationshipText,
    string ToId,
    string Reason);

public sealed record EvidenceRequestListRowModel(
    string RequestListId,
    string KindText,
    string TargetText,
    string SeverityText,
    WorkstationReadinessTone SeverityTone,
    string Tone,
    string StatusText,
    string OpenRequestCountText,
    string EvidenceKindsText,
    string BlockedOutputsText,
    string Summary,
    string VaultText,
    string RetainedText);

public sealed record EvidencePacketActionRowModel(
    string ActionId,
    string Label,
    string Detail,
    string TargetPageTag,
    string TargetText);

public sealed record EvidenceWorkbenchPacketPresentationModel(
    string ScoreText,
    string StatusText,
    WorkstationReadinessTone ReadinessTone,
    string Tone,
    string GeneratedText,
    string ProofChainSummaryText,
    string ProofChainCoverageText,
    IReadOnlyList<EvidenceProofChainLayerRowModel> ProofChainLayers,
    IReadOnlyList<EvidenceNodeRowModel> Nodes,
    IReadOnlyList<EvidenceLineageRowModel> LineageRows,
    IReadOnlyList<string> MissingEvidenceIds,
    IReadOnlyList<string> StaleEvidenceIds,
    IReadOnlyList<string> OrphanEvidenceIds,
    IReadOnlyList<string> SlaBreachMessages,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<EvidencePacketActionRowModel> Actions)
{
    public bool HasProofChainLayers => ProofChainLayers.Count > 0;
}

public static class EvidenceWorkbenchPresentationMapper
{
    public static IReadOnlyList<EvidenceWorkbenchSubjectRowModel> BuildSubjectRows(
        IReadOnlyList<EvidenceSubjectDto> subjects)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        return subjects.Select(EvidenceWorkbenchSubjectRowModel.FromDto).ToArray();
    }

    /// <summary>
    /// Projects a shared evidence packet into desktop rows. When <paramref name="validation"/> is
    /// provided (a fresh server-side validate result) it replaces the packet's retained
    /// completeness snapshot, matching the browser workbench behaviour.
    /// </summary>
    public static EvidenceWorkbenchPacketPresentationModel BuildPacket(
        EvidencePacketDto packet,
        EvidenceCompletenessDto? validation = null)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var completeness = validation ?? packet.Completeness;
        var readinessTone = ToReadinessTone(completeness.Status);
        var proofChain = packet.ProofChain;

        return new EvidenceWorkbenchPacketPresentationModel(
            $"{completeness.Score}% complete",
            FormatKindText(completeness.Status.ToString()),
            readinessTone,
            ToWorkspaceTone(readinessTone),
            FormatTimestamp(packet.GeneratedAt),
            string.IsNullOrWhiteSpace(proofChain.Summary)
                ? "Proof-chain coverage was not returned by this packet."
                : proofChain.Summary,
            proofChain.TotalLayerCount == 0
                ? "No proof-chain coverage"
                : $"{proofChain.CoveredLayerCount}/{proofChain.TotalLayerCount} layers, {proofChain.CoveragePercent}% coverage",
            proofChain.Layers.Select(ToProofChainLayerRow).ToArray(),
            packet.Nodes.Select(ToNodeRow).ToArray(),
            packet.Edges.Select(ToLineageRow).ToArray(),
            completeness.MissingIds,
            completeness.StaleIds,
            completeness.OrphanEvidenceIds,
            completeness.SlaAssessments
                .Where(static assessment => assessment.IsBreached)
                .Select(static assessment => assessment.Message)
                .ToArray(),
            packet.Warnings,
            packet.Actions.Select(ToPacketActionRow).ToArray());
    }

    public static IReadOnlyList<EvidenceRequestListRowModel> BuildRequestListRows(
        IReadOnlyList<EvidenceVaultRequestListEntryDto> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries.Select(ToRequestListRow).ToArray();
    }

    public static WorkstationReadinessTone ToReadinessTone(EvidenceStatusDto status)
        => status switch
        {
            EvidenceStatusDto.Ready => WorkstationReadinessTone.EvidenceLinked,
            EvidenceStatusDto.ReviewRequired => WorkstationReadinessTone.SignoffRequired,
            EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing => WorkstationReadinessTone.Blocked,
            EvidenceStatusDto.Stale => WorkstationReadinessTone.Stale,
            _ => WorkstationReadinessTone.Neutral
        };

    public static string FormatKindText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        var normalized = value.Trim().Replace('-', ' ').Replace('_', ' ').Replace('.', ' ');
        var builder = new StringBuilder(normalized.Length + 8);
        for (var index = 0; index < normalized.Length; index++)
        {
            var current = normalized[index];
            if (index > 0 && char.IsUpper(current) && !char.IsUpper(normalized[index - 1]) && normalized[index - 1] != ' ')
            {
                builder.Append(' ');
            }

            builder.Append(index == 0 ? char.ToUpperInvariant(current) : current);
        }

        return builder.ToString();
    }

    private static EvidenceProofChainLayerRowModel ToProofChainLayerRow(EvidenceProofChainLayerDto layer)
    {
        var readinessTone = ToReadinessTone(layer.Status);
        return new EvidenceProofChainLayerRowModel(
            layer.Layer.ToString(),
            layer.Label,
            FormatKindText(layer.Status.ToString()),
            readinessTone,
            ToWorkspaceTone(readinessTone),
            $"{layer.CoveragePercent}% coverage",
            $"{Pluralize(layer.ReadyEvidenceIds.Count, "ready node")}; {Pluralize(layer.ReviewEvidenceIds.Count, "review node")}; {Pluralize(layer.MissingEvidenceIds.Count, "missing node")}",
            layer.EvidenceKinds.Count > 0
                ? string.Join(", ", layer.EvidenceKinds.Select(FormatKindText))
                : "No evidence kinds",
            layer.Summary);
    }

    private static EvidenceNodeRowModel ToNodeRow(EvidenceNodeDto node)
    {
        var readinessTone = ToReadinessTone(node.Status);
        var (freshnessText, freshnessTone) = FormatFreshness(node.Freshness);
        return new EvidenceNodeRowModel(
            node.EvidenceId,
            FormatKindText(node.Kind),
            FormatKindText(node.Status.ToString()),
            readinessTone,
            ToWorkspaceTone(readinessTone),
            string.IsNullOrWhiteSpace(node.SourceSystem) ? "Unknown source" : node.SourceSystem,
            freshnessText,
            freshnessTone,
            node.Summary,
            Pluralize(node.ArtifactRefs.Count, "artifact"),
            Pluralize(node.RelatedWorkItemIds.Count, "work item"));
    }

    private static EvidenceLineageRowModel ToLineageRow(EvidenceEdgeDto edge)
        => new(
            $"{edge.FromId}->{edge.ToId}:{edge.Relationship}",
            edge.FromId,
            FormatKindText(edge.Relationship),
            edge.ToId,
            edge.Reason);

    private static EvidenceRequestListRowModel ToRequestListRow(EvidenceVaultRequestListEntryDto entry)
    {
        var severityTone = entry.HighestSeverity switch
        {
            EvidenceValidationSeverityDto.Critical => WorkstationReadinessTone.Blocked,
            EvidenceValidationSeverityDto.Warning => WorkstationReadinessTone.SignoffRequired,
            _ => WorkstationReadinessTone.Neutral
        };

        return new EvidenceRequestListRowModel(
            entry.RequestListId,
            FormatKindText(entry.RequestListKind),
            $"{FormatKindText(entry.TargetKind)} {entry.TargetId}",
            FormatKindText(entry.HighestSeverity.ToString()),
            severityTone,
            ToWorkspaceTone(severityTone),
            FormatKindText(entry.Status),
            entry.OpenRequestCount == 0 ? "No open requests" : Pluralize(entry.OpenRequestCount, "open request"),
            entry.EvidenceKinds.Count > 0
                ? string.Join(", ", entry.EvidenceKinds.Select(FormatKindText))
                : "No evidence kinds",
            entry.BlockedOutputs.Count > 0
                ? string.Join(", ", entry.BlockedOutputs)
                : "No blocked outputs",
            entry.Summary,
            entry.VaultId,
            $"Retained {FormatTimestamp(entry.RetainedAt)}");
    }

    private static EvidencePacketActionRowModel ToPacketActionRow(WorkflowActionDto action)
        => new(
            action.ActionId,
            action.Label,
            action.Detail,
            action.TargetPageTag,
            BuildPacketActionTargetText(action));

    private static string BuildPacketActionTargetText(WorkflowActionDto action)
    {
        if (string.Equals(action.ActionId, WorkflowActionIds.EvidenceValidate, StringComparison.OrdinalIgnoreCase))
        {
            return "Run validation";
        }

        if (string.Equals(action.ActionId, WorkflowActionIds.EvidenceExportManifest, StringComparison.OrdinalIgnoreCase))
        {
            return "Export manifest";
        }

        return $"Open {NormalizeActionTargetText(action.TargetPageTag)}";
    }

    private static string NormalizeActionTargetText(string targetPageTag)
    {
        var trimmed = targetPageTag.Trim();
        var separatorIndex = trimmed.IndexOf(':', StringComparison.Ordinal);
        return separatorIndex > 0 ? trimmed[..separatorIndex] : trimmed;
    }

    private static (string Text, WorkstationReadinessTone Tone) FormatFreshness(EvidenceFreshnessDto freshness)
    {
        if (freshness.IsStale)
        {
            var reason = string.IsNullOrWhiteSpace(freshness.Reason) ? "stale evidence" : freshness.Reason;
            return ($"Stale - {reason}", WorkstationReadinessTone.Stale);
        }

        return freshness.AsOf is null
            ? ("No freshness data", WorkstationReadinessTone.Neutral)
            : ($"As of {FormatTimestamp(freshness.AsOf.Value)}", WorkstationReadinessTone.EvidenceLinked);
    }

    private static string FormatTimestamp(DateTimeOffset value)
        => $"{value.UtcDateTime:yyyy-MM-dd HH:mm} UTC";

    private static string Pluralize(int count, string singular)
        => count == 1 ? $"1 {singular}" : $"{count} {singular}s";

    private static string ToWorkspaceTone(WorkstationReadinessTone readinessTone)
        => readinessTone switch
        {
            WorkstationReadinessTone.Blocked => WorkspaceTone.Danger,
            WorkstationReadinessTone.SignoffRequired => WorkspaceTone.Warning,
            WorkstationReadinessTone.EvidenceLinked or WorkstationReadinessTone.Ready => WorkspaceTone.Success,
            WorkstationReadinessTone.Recovery or WorkstationReadinessTone.Stale => WorkspaceTone.Warning,
            _ => WorkspaceTone.Neutral
        };
}
