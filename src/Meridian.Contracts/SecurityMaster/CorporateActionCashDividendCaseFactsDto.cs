namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Complete retained processing options and case evidence in one exact scoped case-version read.
/// The digest binds immutable content, excluding workflow-only case-version increments.
/// </summary>
public sealed record CorporateActionCashDividendCaseFactsDto(
    Guid CaseId,
    long CaseVersion,
    IReadOnlyList<CorporateActionProcessingOptionDto> Options,
    IReadOnlyList<CorporateActionEvidenceDto> Evidence,
    string SnapshotJson,
    string SnapshotHash);
