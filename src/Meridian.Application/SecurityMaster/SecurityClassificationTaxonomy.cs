using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Reads the structural classification the current F# <c>AssetClassRegistry</c> derives for an
/// asset class, and compares retained labels with it.
/// <para>
/// Only the asset class and family are compared. Sub-types are refined from instrument data for
/// some classes (demand vs. time deposits, free-text OtherSecurity sub-types), so a sub-type
/// difference is not evidence of a taxonomy change.
/// </para>
/// </summary>
public static class SecurityClassificationTaxonomy
{
    /// <summary>The asset-class and family labels the current taxonomy derives for a class.</summary>
    public sealed record Labels(string AssetClass, string? AssetFamily);

    /// <summary>
    /// The labels the current taxonomy derives for <paramref name="typeName"/> (a canonical name or
    /// catalog alias), or <c>null</c> when the name is not a modelled asset class.
    /// </summary>
    public static Labels? Current(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var descriptor = AssetClassRegistry.tryDescriptorByName(SecurityAssetClassCatalog.Canonicalize(typeName.Trim()));
        if (descriptor is null)
            return null;

        var value = descriptor.Value;
        return new Labels(
            SecurityClassificationModule.assetClassName(value.AssetClass),
            value.Family is null ? null : SecurityClassificationModule.familyName(value.Family.Value));
    }

    /// <summary>
    /// Whether retained labels equal the labels the current taxonomy derives for the class. Labels
    /// are decoded first, so a retired F# rendering of the same label still matches.
    /// </summary>
    public static bool MatchesCurrent(string? typeName, string? assetClass, string? assetFamily)
        => Current(typeName) is { } current
           && LabelEquals(current.AssetClass, assetClass)
           && LabelEquals(current.AssetFamily, assetFamily);

    /// <summary>
    /// Whether the classification change between two retained versions of the same security is a
    /// taxonomy revision: the asset class name is unchanged, the earlier version's labels no longer
    /// match the taxonomy, and the later version's labels do. Such a change comes from a revision of
    /// the asset-class table, not from an operator edit.
    /// </summary>
    public static bool IsTaxonomyRevision(SecurityEconomicDefinitionRecord previous, SecurityEconomicDefinitionRecord current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        return string.Equals(
                   SecurityAssetClassCatalog.Canonicalize(previous.TypeName ?? string.Empty),
                   SecurityAssetClassCatalog.Canonicalize(current.TypeName ?? string.Empty),
                   StringComparison.OrdinalIgnoreCase)
               && !MatchesCurrent(previous.TypeName, previous.AssetClass, previous.AssetFamily)
               && MatchesCurrent(current.TypeName, current.AssetClass, current.AssetFamily);
    }

    private static bool LabelEquals(string? expected, string? stored)
        => string.Equals(
            ClassificationLabelReader.Normalize(expected),
            ClassificationLabelReader.Normalize(stored),
            StringComparison.Ordinal);
}
