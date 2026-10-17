using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Catalog;

/// <summary>
/// An operator-selected legacy alias. Evidence must establish this particular alias's origin;
/// its spelling, an inferred source, and a matching typed identifier do not establish ownership.
/// Only providerless aliases with the exact value and source are eligible.
/// Typed fields are preserved unless separate evidence establishes that field's legacy origin;
/// even then, conflicting explicit alias or provider authority prevents clearing the field.
/// </summary>
public sealed record SecurityMasterLegacyIdentifierCleanupItem(
    Guid SecurityId,
    SecurityIdentifierKind IdentifierKind,
    string IdentifierValue,
    string? ExpectedAliasSource,
    string EvidenceReference,
    string? TypedIdentifierEvidenceReference = null);

/// <summary>
/// Explicit, opt-in cleanup instructions bound to a complete retained registry snapshot.
/// Evidence references are retained for operator review; recording them does not certify approval.
/// </summary>
public sealed record SecurityMasterLegacyIdentifierCleanupManifest(
    string CleanupId,
    string RegistryFingerprint,
    IReadOnlyList<SecurityMasterLegacyIdentifierCleanupItem> Items);

/// <summary>
/// Dry-run result. Applying it checks the manifest, every selected alias, and the complete
/// registry fingerprint again before any mutation. A changed registry requires a new dry run.
/// </summary>
public sealed record SecurityMasterLegacyIdentifierCleanupValidation(
    SecurityMasterLegacyIdentifierCleanupManifest Manifest,
    string ManifestFingerprint,
    int RemovedAliases,
    int ClearedIdentifiers);

/// <summary>
/// Durable evidence of one atomic cleanup. The exact manifest and its receipt are retained
/// together in symbols.json, allowing exact retries after restart and rejecting ID reuse.
/// </summary>
public sealed record SecurityMasterLegacyIdentifierCleanupReceipt(
    SecurityMasterLegacyIdentifierCleanupManifest Manifest,
    string ManifestFingerprint,
    DateTimeOffset AppliedAtUtc,
    int RemovedAliases,
    int ClearedIdentifiers);

/// <summary>
/// Optional maintenance capability, separate from normal Security Master reconciliation.
/// Callers must supply independently reviewed evidence for every legacy alias selected.
/// </summary>
public interface ISecurityMasterLegacyIdentifierCleanupWriter
{
    Task<string> GetLegacyIdentifierCleanupFingerprintAsync(CancellationToken ct = default);

    Task<SecurityMasterLegacyIdentifierCleanupValidation> ValidateLegacyIdentifierCleanupAsync(
        SecurityMasterLegacyIdentifierCleanupManifest manifest,
        CancellationToken ct = default);

    Task<SecurityMasterLegacyIdentifierCleanupReceipt> ApplyLegacyIdentifierCleanupAsync(
        SecurityMasterLegacyIdentifierCleanupValidation validation,
        CancellationToken ct = default);
}
