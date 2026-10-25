using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// The round-trip guard for rows stamped above their class's write version (ADR-024, readable
/// before writable): a deactivation or patch-less amend re-serializes such a row only when every
/// versioned term survives.
/// </summary>
public sealed partial class SecurityMasterService
{
    /// <summary>
    /// A row stamped above its class's write version (ADR-024, readable before writable) was written
    /// by a node that writes the newer version. Deactivating it, or amending it without a terms patch,
    /// re-serializes the record this node read, and the tolerant readers type only what this node
    /// knows: an inner member it does not know, a mistyped value or a case-variant key would be
    /// dropped. Refuse the write unless re-serializing keeps every versioned term exactly. A terms
    /// patch replaces the terms wholesale and is mapped in Write mode instead, so it is not checked
    /// here. An all-empty block normalizes equal and is re-emitted at the write version.
    /// </summary>
    private static void EnsureAheadOfWriteTermsRoundTripSafely(
        SecurityProjectionRecord projection,
        JsonElement? assetSpecificTermsPatch)
    {
        var terms = projection.AssetSpecificTerms;
        var writes = SecurityAssetClassCatalog.GetTermsVersion(projection.AssetClass);
        if (assetSpecificTermsPatch is not null
            || terms.ValueKind != JsonValueKind.Object
            || SecurityTermsEnvelope.IsProfileGovernedPayload(projection.AssetClass, terms)
            || SecurityTermsEnvelope.ResolveAssetClassTermsVersion(terms) <= writes)
        {
            return;
        }

        var stored = SecurityAssetTermsSchema.NormalizedTermsAbove(projection.AssetClass, terms, writes, out var conflicts);
        using var rewrittenTerms = JsonDocument.Parse(
            new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(projection)).AssetSpecificTermsJson);
        var rewritten = SecurityAssetTermsSchema.NormalizedTermsAbove(projection.AssetClass, rewrittenTerms.RootElement, writes, out _);
        var changedPath = conflicts.FirstOrDefault()
            ?? stored.Select(static entry => entry.Key)
                .Concat(rewritten.Select(static entry => entry.Key))
                .Distinct(StringComparer.Ordinal)
                .FirstOrDefault(path => !JsonNode.DeepEquals(stored[path], rewritten[path]));
        if (changedPath is null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Security '{projection.SecurityId:D}' is stamped {SecurityTermsEnvelope.AssetClassTermsVersionKey} " +
            $"{SecurityTermsEnvelope.ResolveAssetClassTermsVersion(terms)}, which this node reads but does not write, and " +
            $"re-serializing it here would change '{changedPath}'. Apply the change from a node that writes that version, " +
            $"or amend it with terms at version {writes} that settle the block.");
    }
}
