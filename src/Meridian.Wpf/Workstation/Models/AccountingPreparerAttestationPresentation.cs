using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Workstation.Models;

/// <summary>Displays native accounting decision classification without inferring review or coverage.</summary>
public static class AccountingPreparerAttestationPresentation
{
    public static IReadOnlyList<string> Describe(LedgerAmountProofDto proof, out string summary)
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
            if (decision is null || decision.SourceScope != proof.Scope || decision.SchemaVersion != 1
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
}
