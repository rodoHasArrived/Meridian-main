namespace Meridian.Contracts.Workstation;

/// <summary>
/// Classification of preparer evidence tasks attached to exact visible source documents. These
/// counts are not financial assertion counts, independent review or Verified Coverage.
/// </summary>
public sealed record PreparerAttestationEvidenceProjectionDto(
    SinglePrincipalAttestationScopeDto Scope,
    int SchemaVersion,
    string Availability,
    DateTimeOffset AssessedAt,
    string Disclosure,
    string Boundary,
    int? RecordedReceiptCount,
    int? UnrevokedReceiptCount,
    int? RevokedReceiptCount,
    int? SyntheticReceiptCount,
    int? UnclassifiedReceiptCount,
    IReadOnlyList<PreparerEvidenceAcknowledgmentDto> Acknowledgments,
    IReadOnlyList<string> Warnings);

/// <summary>An immutable assertion and its original declaration alongside a dated applicability assessment.</summary>
public sealed record PreparerEvidenceAcknowledgmentDto(
    PreparerAttestationReceiptDto Receipt,
    SinglePrincipalDeclarationDto OriginalDeclaration,
    PreparerAttestationRevocationDto? Revocation,
    string Applicability);
