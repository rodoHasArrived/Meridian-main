namespace Meridian.Ledger;

/// <summary>
/// Shared guard sequence for governed ledger lifecycle transitions (automated journal approvals,
/// report-pack lifecycles). Centralizes the actor/reason requirements, evidence normalization,
/// evidence-required policy, and transition-allowlist check that were previously copy-pasted per
/// aggregate, so the approval state-machine shape is maintained in one place.
/// </summary>
internal static class LedgerGovernedLifecycle
{
    /// <summary>
    /// Validates a lifecycle transition's inputs in the canonical order (actor, reason, evidence,
    /// allowed transition) and returns the trimmed actor/reason plus normalized evidence links for
    /// the transition event.
    /// </summary>
    internal static (string Actor, string Reason, IReadOnlyList<string> EvidenceLinks) PrepareTransition(
        string actor,
        string reason,
        IReadOnlyList<string>? evidenceLinks,
        bool requireEvidence,
        string actorRequiredMessage,
        string reasonRequiredMessage,
        string evidenceRequiredMessage,
        bool transitionAllowed,
        string transitionNotAllowedMessage)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException(actorRequiredMessage, nameof(actor));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException(reasonRequiredMessage, nameof(reason));

        var normalizedEvidence = LedgerEvidenceLinks.Normalize(evidenceLinks);
        if (requireEvidence && normalizedEvidence.Count == 0)
            throw new ArgumentException(evidenceRequiredMessage, nameof(evidenceLinks));
        if (!transitionAllowed)
            throw new InvalidOperationException(transitionNotAllowedMessage);

        return (actor.Trim(), reason.Trim(), normalizedEvidence);
    }
}

/// <summary>
/// Canonical evidence-link normalization for ledger aggregates: trims each link, drops empties,
/// and de-duplicates case-insensitively. Consolidates the <c>NormalizeEvidence</c> copies that
/// were previously private to each aggregate.
/// </summary>
internal static class LedgerEvidenceLinks
{
    internal static IReadOnlyList<string> Normalize(IReadOnlyList<string>? evidenceLinks)
        => evidenceLinks?
            .Select(static link => link.Trim())
            .Where(static link => link.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
}
