using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Evidence;

internal static class EvidenceProofChainBuilder
{
    private static readonly ProofChainLayerDefinition[] LayerDefinitions =
    [
        new(
            EvidenceProofChainLayerKindDto.Source,
            "Source",
            ["source", "provider", "statement", "custodian", "broker", "bank", "cash", "payment-requester", "expected-cash-movement", "retained-evidence", "evidence-link", "security-master", "identity"]),
        new(
            EvidenceProofChainLayerKindDto.Normalization,
            "Normalization",
            ["normaliz", "mapping", "parser", "taxonomy", "canonical", "import", "backfill", "dictionary", "classification"]),
        new(
            EvidenceProofChainLayerKindDto.Reconciliation,
            "Reconciliation",
            ["reconciliation", "break", "casework", "matching", "variance", "bank-cash-evidence", "payment-intent", "settlement"]),
        new(
            EvidenceProofChainLayerKindDto.CapitalAccounts,
            "Capital accounts",
            ["capital-account", "capital account", "subledger", "private-capital", "fund-event", "investor"]),
        new(
            EvidenceProofChainLayerKindDto.Ledger,
            "Ledger",
            ["ledger", "journal", "trial-balance", "gl-impact", "posting"]),
        new(
            EvidenceProofChainLayerKindDto.Close,
            "Close",
            ["close", "approval", "checklist", "operations", "readiness", "gate", "promotion"]),
        new(
            EvidenceProofChainLayerKindDto.Reporting,
            "Reporting",
            ["report", "analysis-export", "portfolio-context", "lineage", "published-output"]),
        new(
            EvidenceProofChainLayerKindDto.Delivery,
            "Delivery",
            ["delivery", "distribution", "recipient", "portal", "email", "transport", "secure-link", "publication", "export"]),
        new(
            EvidenceProofChainLayerKindDto.Audit,
            "Audit",
            ["audit", "vault", "manifest", "artifact", "retention", "hash", "approval-audit", "execution-deferred"])
    ];

    public static EvidenceProofChainDto Build(
        IReadOnlyList<EvidenceNodeDto> nodes,
        EvidenceCompletenessDto completeness)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(completeness);

        var nodesById = nodes.ToDictionary(static node => node.EvidenceId, StringComparer.OrdinalIgnoreCase);
        var readyIds = completeness.ReadyIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingIds = completeness.MissingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var layers = LayerDefinitions
            .Select(definition => BuildLayer(definition, nodes, nodesById, completeness.RequiredIds, readyIds, missingIds))
            .ToArray();
        var coveredLayerCount = layers.Count(static layer => layer.EvidenceIds.Count > 0);
        var coveragePercent = layers.Length == 0
            ? 0
            : (int)Math.Round(coveredLayerCount * 100d / layers.Length, MidpointRounding.AwayFromZero);
        var status = ResolveProofChainStatus(layers, coveredLayerCount);
        var blockedLayerCount = layers.Count(static layer => layer.Status is EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing);
        var staleLayerCount = layers.Count(static layer => layer.Status == EvidenceStatusDto.Stale);
        var reviewLayerCount = layers.Count(static layer => layer.Status == EvidenceStatusDto.ReviewRequired);
        var summary = $"{coveredLayerCount} of {layers.Length} v0.18 proof-chain layers have evidence; " +
            $"{blockedLayerCount} blocked or missing, {staleLayerCount} stale, {reviewLayerCount} review-required.";

        return new EvidenceProofChainDto(
            CoveragePercent: coveragePercent,
            Status: status,
            CoveredLayerCount: coveredLayerCount,
            TotalLayerCount: layers.Length,
            Layers: layers,
            Summary: summary);
    }

    private static EvidenceProofChainLayerDto BuildLayer(
        ProofChainLayerDefinition definition,
        IReadOnlyList<EvidenceNodeDto> nodes,
        IReadOnlyDictionary<string, EvidenceNodeDto> nodesById,
        IReadOnlyList<string> requiredIds,
        IReadOnlySet<string> readyIds,
        IReadOnlySet<string> missingIds)
    {
        var layerNodes = nodes
            .Where(node => ResolveLayer(node) == definition.Layer)
            .OrderBy(static node => node.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var evidenceIds = layerNodes
            .Select(static node => node.EvidenceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var requiredEvidenceIds = requiredIds
            .Where(id => ResolveLayer(id, nodesById) == definition.Layer)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingEvidenceIds = requiredEvidenceIds
            .Where(id => missingIds.Contains(id) || !nodesById.ContainsKey(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var readyEvidenceIds = evidenceIds
            .Where(readyIds.Contains)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reviewEvidenceIds = layerNodes
            .Where(static node => node.Status != EvidenceStatusDto.Ready)
            .Select(static node => node.EvidenceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var evidenceKinds = layerNodes
            .Select(static node => node.Kind)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static kind => kind, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var denominator = requiredEvidenceIds.Length > 0 ? requiredEvidenceIds.Length : evidenceIds.Length;
        var coveragePercent = denominator == 0
            ? 0
            : (int)Math.Round(readyEvidenceIds.Length * 100d / denominator, MidpointRounding.AwayFromZero);
        var status = ResolveLayerStatus(layerNodes, missingEvidenceIds);
        var summary = evidenceIds.Length == 0
            ? $"No {definition.Label} evidence is present in this packet."
            : $"{definition.Label} has {evidenceIds.Length} evidence node(s), {readyEvidenceIds.Length} ready, " +
              $"{reviewEvidenceIds.Length} review-required or blocked, and {missingEvidenceIds.Length} missing.";

        return new EvidenceProofChainLayerDto(
            Layer: definition.Layer,
            Label: definition.Label,
            Status: status,
            CoveragePercent: coveragePercent,
            RequiredEvidenceIds: requiredEvidenceIds,
            EvidenceIds: evidenceIds,
            ReadyEvidenceIds: readyEvidenceIds,
            ReviewEvidenceIds: reviewEvidenceIds,
            MissingEvidenceIds: missingEvidenceIds,
            EvidenceKinds: evidenceKinds,
            Summary: summary);
    }

    private static EvidenceStatusDto ResolveProofChainStatus(
        IReadOnlyList<EvidenceProofChainLayerDto> layers,
        int coveredLayerCount)
    {
        if (coveredLayerCount == 0)
        {
            return EvidenceStatusDto.Missing;
        }

        if (layers.Any(static layer => layer.Status == EvidenceStatusDto.Blocked))
        {
            return EvidenceStatusDto.Blocked;
        }

        if (layers.Any(static layer => layer.Status == EvidenceStatusDto.Stale))
        {
            return EvidenceStatusDto.Stale;
        }

        return coveredLayerCount == layers.Count &&
               layers.All(static layer => layer.Status == EvidenceStatusDto.Ready)
            ? EvidenceStatusDto.Ready
            : EvidenceStatusDto.ReviewRequired;
    }

    private static EvidenceStatusDto ResolveLayerStatus(
        IReadOnlyList<EvidenceNodeDto> nodes,
        IReadOnlyList<string> missingEvidenceIds)
    {
        if (missingEvidenceIds.Count > 0)
        {
            return EvidenceStatusDto.Blocked;
        }

        if (nodes.Count == 0)
        {
            return EvidenceStatusDto.Missing;
        }

        if (nodes.Any(static node => node.Status is EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing))
        {
            return EvidenceStatusDto.Blocked;
        }

        if (nodes.Any(static node => node.Status == EvidenceStatusDto.Stale || node.Freshness.IsStale))
        {
            return EvidenceStatusDto.Stale;
        }

        if (nodes.Any(static node => node.Status == EvidenceStatusDto.ReviewRequired))
        {
            return EvidenceStatusDto.ReviewRequired;
        }

        return EvidenceStatusDto.Ready;
    }

    private static EvidenceProofChainLayerKindDto? ResolveLayer(
        string evidenceId,
        IReadOnlyDictionary<string, EvidenceNodeDto> nodesById)
        => nodesById.TryGetValue(evidenceId, out var node)
            ? ResolveLayer(node)
            : ResolveLayerFromText(evidenceId.Split(':', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? evidenceId);

    private static EvidenceProofChainLayerKindDto? ResolveLayer(EvidenceNodeDto node)
    {
        var evidenceIdSuffix = node.EvidenceId.Split(':', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? node.EvidenceId;
        var exactLayer = ResolveLayerByExactKeyword(node.Kind) ?? ResolveLayerByExactKeyword(evidenceIdSuffix);
        if (exactLayer is not null)
        {
            return exactLayer;
        }

        var primaryLayer = ResolveLayerFromText($"{node.Kind} {evidenceIdSuffix}");
        if (primaryLayer is not null)
        {
            return primaryLayer;
        }

        var artifactText = string.Join(' ', node.ArtifactRefs.Select(static artifact =>
            $"{artifact.Kind} {artifact.ArtifactId} {artifact.Path} {artifact.Route} {artifact.CanonicalSubjectKind}"));
        return ResolveLayerFromText($"{node.SourceSystem} {node.Summary} {artifactText}");
    }

    private static EvidenceProofChainLayerKindDto? ResolveLayerByExactKeyword(string value)
    {
        foreach (var definition in LayerDefinitions)
        {
            if (definition.Keywords.Any(keyword => string.Equals(value, keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return definition.Layer;
            }
        }

        return null;
    }

    private static EvidenceProofChainLayerKindDto? ResolveLayerFromText(string value)
    {
        foreach (var definition in LayerDefinitions)
        {
            if (definition.Keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return definition.Layer;
            }
        }

        return null;
    }

    private sealed record ProofChainLayerDefinition(
        EvidenceProofChainLayerKindDto Layer,
        string Label,
        IReadOnlyList<string> Keywords);
}
