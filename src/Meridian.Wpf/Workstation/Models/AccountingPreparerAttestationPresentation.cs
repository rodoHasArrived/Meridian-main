using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Workstation.Models;

/// <summary>Displays native accounting decision classification without inferring review or coverage.</summary>
public static class AccountingPreparerAttestationPresentation
{
    // Report journal identities are supplied only after the drawer validates the retained
    // selection's complete journal/line binding against the response.
    public static IReadOnlyList<string> Describe(LedgerAmountProofDto proof, out string summary,
        IReadOnlyList<Guid>? reportJournalEntryIds = null)
    {
        if (proof.AccountingPreparerAttestations is null)
        {
            summary = "Accounting review classification was not returned. No ordinary or independent approval is inferred.";
            return [];
        }
        if (proof.AccountingPreparerAttestations.Count == 0)
        {
            summary = proof.OrdinaryRecordedApprovalSatisfied is false
                ? "Ordinary recorded-approval requirements are not satisfied. No Verified Coverage credit is established."
                : "No accounting preparer-attestation decisions were returned. This does not establish independent review or Verified Coverage.";
            return [];
        }
        summary = proof.OrdinaryRecordedApprovalSatisfied is false
            ? "Contributing accounting decisions include preparer attestations. Ordinary recorded approval is not satisfied; these decisions do not supply Verified Coverage credit."
            : "Contributing accounting decisions have preparer or unavailable classification. No ordinary approval or Verified Coverage credit is inferred.";
        return proof.AccountingPreparerAttestations.Select(decision =>
        {
            if (decision is null || !MatchesSource(decision, proof, reportJournalEntryIds) || decision.SchemaVersion != 1
                || decision.JournalEntryIds is null || decision.ValidationIssues is null
                || decision.Classification != LedgerAmountApprovalClassification.PreparerAttestation
                || decision.Disclosure != SinglePrincipalAttestationPolicy.Disclosure)
                return "Accounting review classification unavailable for this exact amount scope; no ordinary or independent approval is established.";
            return $"{SinglePrincipalAttestationPolicy.Disclosure}\nAccounting attestation {decision.AttestationId?.ToString("D") ?? "unavailable"}; case {decision.CaseId?.ToString("D") ?? "unavailable"}; "
                + $"actor {decision.Actor ?? "unrecorded"}; recorded {decision.RecordedAt?.ToString("O") ?? "unavailable"}. "
                + $"Original declaration {decision.DeclarationId?.ToString("D") ?? "unavailable"}, revision {decision.DeclarationRevision?.ToString() ?? "unavailable"}; "
                + $"policy {decision.PolicyVersion ?? "unavailable"}. Journals: {string.Join(", ", decision.JournalEntryIds)}. "
                + (decision.ValidationIssues.Count == 0 ? "" : "Classification issues: " + string.Join("; ", decision.ValidationIssues));
        }).ToArray();
    }

    private static bool MatchesSource(AccountingPreparerAttestationDisclosureDto decision, LedgerAmountProofDto proof,
        IReadOnlyList<Guid>? reportJournalEntryIds)
    {
        if (!proof.SubjectId.StartsWith("report:", StringComparison.Ordinal))
            return reportJournalEntryIds is null && decision.SourceScope == proof.Scope;
        var source = decision.SourceScope;
        if (reportJournalEntryIds is not { Count: > 0 } || source is null || source.PeriodId == Guid.Empty
            || (source with { PeriodId = proof.Scope.PeriodId }) != proof.Scope
            || decision.JournalEntryIds is not { Count: > 0 })
            return false;
        return decision.JournalEntryIds.All(journal => journal != Guid.Empty && reportJournalEntryIds.Contains(journal)
            && proof.Evidence.Any(item =>
            {
                var parts = item.SourceSubjectId?.Split(':');
                return item.Kind != "ledger-record" && item.SourceScope == source && parts is { Length: 3 }
                    && Guid.TryParseExact(parts[0], "D", out var sourceJournal) && sourceJournal == journal
                    && Guid.TryParseExact(parts[1], "D", out var line) && line != Guid.Empty
                    && parts[2] is "debit" or "credit";
            }));
    }
}
