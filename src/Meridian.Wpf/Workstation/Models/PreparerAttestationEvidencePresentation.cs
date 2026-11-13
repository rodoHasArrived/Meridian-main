using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Workstation.Models;

/// <summary>Displays shared evidence-task counts and disclosures without deriving financial assurance.</summary>
public static class PreparerAttestationEvidencePresentation
{
    public static bool MatchesScope(PreparerAttestationEvidenceProjectionDto projection, string tenant, string company, Guid? ledgerBookId) =>
        projection.Scope is { } scope && !string.IsNullOrWhiteSpace(tenant) && !string.IsNullOrWhiteSpace(company)
        && string.Equals(scope.TenantId?.Trim(), tenant.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(scope.CompanyId?.Trim(), company.Trim(), StringComparison.OrdinalIgnoreCase)
        && (scope.LedgerBookId is null || (scope.LedgerBookId != Guid.Empty && scope.LedgerBookId == ledgerBookId));

    public static IReadOnlyList<string> Describe(PreparerAttestationEvidenceProjectionDto? projection, out string summary)
    {
        const string boundary = "Evidence-task receipts are not financial approvals or Verified Coverage.";
        if (projection is null)
        {
            summary = "Preparer evidence classification was not returned. " + boundary;
            return [];
        }
        if (projection.SchemaVersion != 1 || projection.Disclosure != SinglePrincipalAttestationPolicy.Disclosure
            || projection.Acknowledgments is null)
        {
            summary = "Preparer evidence classification is unavailable for this version. " + boundary;
            return [];
        }
        if (projection.Availability != "Available")
        {
            summary = "Preparer evidence assessment is unavailable. " + boundary;
            return projection.Warnings ?? [];
        }
        summary = $"Retained evidence receipts: {Count(projection.RecordedReceiptCount)}; unrevoked: {Count(projection.UnrevokedReceiptCount)}; "
            + $"revoked: {Count(projection.RevokedReceiptCount)}; synthetic: {Count(projection.SyntheticReceiptCount)}. " + boundary;
        return projection.Acknowledgments.Select(entry =>
        {
            if (entry?.Receipt is null || entry.OriginalDeclaration is null)
                return "Review classification unavailable; original scoped evidence was not returned.";
            var receipt = entry.Receipt;
            var classification = receipt.SchemaVersion == SinglePrincipalAttestationPolicy.SchemaVersion
                && receipt.DecisionKind == SinglePrincipalAttestationPolicy.DecisionKind
                && receipt.Disclosure == SinglePrincipalAttestationPolicy.Disclosure
                ? SinglePrincipalAttestationPolicy.Disclosure
                : "Review classification unavailable; no independent review established";
            return $"{classification}\n{receipt.AttestedBy} · receipt {receipt.ReceiptId:D} · source {receipt.SourceArtifactId}\n"
                + $"Source provenance: {receipt.SourceProvenance}; assertion revision: {receipt.SubjectRevision}. "
                + $"Current applicability: {Applicability(entry.Applicability)}. "
                + (entry.Revocation is null ? "Original declaration revision " + entry.OriginalDeclaration.Revision + "."
                    : $"Revoked by {entry.Revocation.Actor}: {entry.Revocation.Reason}. Original assertion retained.");
        }).ToArray();
    }

    private static string Count(int? count) => count?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable";
    private static string Applicability(string value) => value switch
    {
        "ExactSource" => "exact retained source revision; no independent review",
        "StaleSource" => "source changed; historical assertion only",
        "Revoked" => "revoked",
        "Unavailable" => "unavailable",
        _ => "unavailable (unsupported assessment)"
    };
}
