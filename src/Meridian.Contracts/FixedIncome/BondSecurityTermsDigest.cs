using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.FixedIncome;

/// <summary>
/// The content hash a bond calculation records for its <c>SecurityTerms</c> input
/// (<see cref="BondCalculationInput.ContentSha256"/>): SHA-256 over the common terms' raw text, a line
/// feed, and the asset-specific terms' raw text.
/// <para>
/// The asset-specific terms are hashed without a top-level <see cref="SecurityTermsEnvelope.FormatKey"/>
/// that only restates the family the frozen shape rule infers
/// (<see cref="SecurityTermsEnvelope.RemoveRedundantDeclaredFormat"/>). That declaration is the only
/// key a write, a replay or migration 068 adds to the same economic terms (ADR-024 amendment
/// 2026-11-13), so declaring it never moves the hash; every other byte, including <c>schemaVersion</c>
/// and <see cref="SecurityTermsEnvelope.AssetClassTermsVersionKey"/>, stays hashed.
/// </para>
/// <para>
/// Receipts calculated before this rule hashed the asset-specific terms' raw text as read, which for
/// terms that already declared their family includes the declaration. <see cref="Resolve"/> lets a replay
/// report that retained hash when it is exactly the raw-text hash of the terms resolved now, so an
/// unchanged security keeps verifying; any other retained value is ignored and the canonical hash is
/// reported, so changed terms never verify.
/// </para>
/// </summary>
public static class BondSecurityTermsDigest
{
    /// <summary>The canonical, declared-format-invariant hash of the terms.</summary>
    public static string Compute(JsonElement commonTerms, JsonElement assetSpecificTerms)
        => Sha256Digest.ComputeUtf8(commonTerms.GetRawText() + "\n" + SecurityTermsEnvelope.RemoveRedundantDeclaredFormat(assetSpecificTerms));

    /// <summary>The raw-text hash receipts recorded before the declared-format-invariant rule.</summary>
    public static string ComputeRawText(JsonElement commonTerms, JsonElement assetSpecificTerms)
        => Sha256Digest.ComputeUtf8(commonTerms.GetRawText() + "\n" + assetSpecificTerms.GetRawText());

    /// <summary>
    /// The hash a calculation over these terms reports. Without a retained hash, or when the retained
    /// hash is not the raw-text hash of exactly these terms, this is <see cref="Compute"/>. When a replay
    /// supplies the hash its receipt retained and it equals <see cref="ComputeRawText"/> of these terms
    /// (the bytes the receipt hashed are the bytes resolved now), the retained hash is reported so the
    /// replayed input manifest equals the retained one.
    /// </summary>
    public static string Resolve(JsonElement commonTerms, JsonElement assetSpecificTerms, string? retainedSha256)
    {
        var canonical = Compute(commonTerms, assetSpecificTerms);
        if (retainedSha256 is null || string.Equals(retainedSha256, canonical, StringComparison.Ordinal))
        {
            return canonical;
        }

        var rawText = ComputeRawText(commonTerms, assetSpecificTerms);
        return string.Equals(retainedSha256, rawText, StringComparison.Ordinal) ? rawText : canonical;
    }
}
