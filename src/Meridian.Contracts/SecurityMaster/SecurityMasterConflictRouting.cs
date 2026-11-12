using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Routes a <see cref="SecurityMasterConflict"/> to the review surface that owns it, by declared
/// facts rather than by substrings of its field path. A path is split on dots and each segment is
/// compared whole (case-insensitive, underscores ignored), so <c>ProfileFields.providerName</c> is
/// not an identifier conflict because it contains "provider", and <c>EconomicTerms.issueDate</c>
/// is a terms conflict although no fixed word list names it.
/// </summary>
/// <remarks>
/// <para>
/// The keys that make a conflict a terms conflict are owned by the record's asset class: every term
/// key and alias the class declares in <see cref="SecurityAssetTermsSchema"/>
/// (<see cref="TermRoutingKeys"/>). A class gains or loses routing by changing its schema, not by a
/// change here. Beside them stand the declared conflict kinds
/// (<see cref="SecurityMasterConflictKinds"/>), the containers conflict detection writes its paths
/// under (<see cref="TermContainers"/>) and the classification and currency names of
/// <see cref="SecurityMasterConflictFieldCategories"/>.
/// </para>
/// <para>
/// A conflict can be both an identity and a terms conflict. One that is neither takes the default
/// review route and is still listed with the record's open conflicts.
/// </para>
/// </remarks>
public static class SecurityMasterConflictRouting
{
    private static readonly ConcurrentDictionary<string, FrozenSet<string>> TermKeysByClass = new(StringComparer.Ordinal);

    /// <summary>
    /// The path containers that hold a record's terms: the three conflict detection writes
    /// (<c>EconomicTerms</c>, <c>CommonTerms</c>, <c>ProfileFields</c>), the stored terms document and
    /// the economic definition read model.
    /// </summary>
    public static IReadOnlySet<string> TermContainers { get; } = new[]
        {
            "EconomicTerms", "CommonTerms", "ProfileFields", "AssetSpecificTerms", "EconomicDefinition", "Terms"
        }
        .Select(Normalize)
        .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The path segments that name provider evidence rather than a declared identifier kind: provider
    /// mappings and aliases, which the identity panel reviews beside identifiers.
    /// </summary>
    public static IReadOnlySet<string> ProviderEvidenceSegments { get; } = new[]
        {
            "Provider", "Providers", "ProviderMapping", "ProviderMappings", "ProviderSymbol", "ProviderSymbols",
            "Mapping", "Mappings", "Alias", "Aliases"
        }
        .Select(Normalize)
        .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The term keys <paramref name="assetClass"/> owns for routing: each field key and alias its terms
    /// schema declares, normalized. Empty for a class the schema does not declare.
    /// </summary>
    public static IReadOnlySet<string> TermRoutingKeys(string? assetClass)
    {
        if (string.IsNullOrWhiteSpace(assetClass))
        {
            return FrozenSet<string>.Empty;
        }

        var canonical = SecurityAssetClassCatalog.Canonicalize(assetClass);
        return TermKeysByClass.GetOrAdd(canonical, static name =>
            SecurityAssetTermsSchema.TryGetFields(name, out var fields)
                ? fields.SelectMany(static field => field.Aliases.Prepend(field.Key)).Select(Normalize).ToFrozenSet(StringComparer.Ordinal)
                : FrozenSet<string>.Empty);
    }

    /// <summary>
    /// Whether the conflict belongs to identity and provider evidence: an identifier ambiguity, a
    /// declared identifier name or kind in its path, or a provider-mapping or alias segment.
    /// </summary>
    public static bool IsIdentityConflict(SecurityMasterConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        return SecurityMasterConflictFieldCategories.For(conflict).HasFlag(SecurityMasterConflictFieldCategory.Identifier)
            || Segments(conflict.FieldPath).Any(ProviderEvidenceSegments.Contains);
    }

    /// <summary>
    /// Whether the conflict is about the record's terms: a declared term-mismatch kind, a path under a
    /// terms container, a classification or currency name, or a term key the record's asset class owns.
    /// </summary>
    public static bool IsTermsConflict(SecurityMasterConflict conflict, string? assetClass)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        if (string.Equals(conflict.ConflictKind, SecurityMasterConflictKinds.EconomicTermMismatch, StringComparison.OrdinalIgnoreCase)
            || string.Equals(conflict.ConflictKind, SecurityMasterConflictKinds.CommonTermMismatch, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        const SecurityMasterConflictFieldCategory termCategories =
            SecurityMasterConflictFieldCategory.Classification | SecurityMasterConflictFieldCategory.Currency;
        if ((SecurityMasterConflictFieldCategories.For(conflict) & termCategories) != SecurityMasterConflictFieldCategory.None)
        {
            return true;
        }

        var classKeys = TermRoutingKeys(assetClass);
        return Segments(conflict.FieldPath).Any(segment => TermContainers.Contains(segment) || classKeys.Contains(segment));
    }

    /// <summary>Whether any whole segment of <paramref name="fieldPath"/> equals <paramref name="name"/>.</summary>
    public static bool HasSegment(string? fieldPath, string? name)
        => !string.IsNullOrWhiteSpace(name) && Segments(fieldPath).Contains(Normalize(name), StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="fieldPath"/> is, or ends with, the whole value <paramref name="value"/>
    /// (which may itself contain dots, as a provider symbol such as <c>AAPL.O</c> does).
    /// </summary>
    public static bool EndsWithValue(string? fieldPath, string? value)
    {
        if (string.IsNullOrWhiteSpace(fieldPath) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var path = fieldPath.Trim();
        var tail = value.Trim();
        return path.Equals(tail, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("." + tail, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Segments(string? fieldPath)
        => string.IsNullOrWhiteSpace(fieldPath)
            ? []
            : fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Normalize);

    private static string Normalize(string name)
        => name.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}
