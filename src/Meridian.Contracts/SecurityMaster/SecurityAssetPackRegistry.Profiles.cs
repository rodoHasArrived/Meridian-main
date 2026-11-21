namespace Meridian.Contracts.SecurityMaster;

public static partial class SecurityAssetPackRegistry
{
    /// <summary>
    /// Only a registered profile identity may reclassify a profile-backed security. Caller-supplied
    /// category metadata may corroborate this mapping, but never decides the class. Each class owns
    /// its ids (<see cref="SecurityAssetClassDescriptor.ReclassifyingProfileIds"/>); this is the
    /// lookup over them, and an id two classes claim fails at type initialization.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ProfileAssetClasses { get; } = BuildProfileAssetClasses();

    /// <summary>
    /// Resolves a caller-supplied reclassifying profile id (matched case-insensitively) to the
    /// catalog's own spelling, so logs and reports carry the catalog value rather than request text.
    /// </summary>
    public static bool TryGetCanonicalProfileId(string? profileId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? canonicalProfileId)
    {
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            foreach (var key in ProfileAssetClasses.Keys)
            {
                if (string.Equals(key, profileId, StringComparison.OrdinalIgnoreCase))
                {
                    canonicalProfileId = key;
                    return true;
                }
            }
        }

        canonicalProfileId = null;
        return false;
    }

    private static IReadOnlyDictionary<string, string> BuildProfileAssetClasses()
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in SecurityAssetClassCatalog.All)
        {
            foreach (var profileId in descriptor.ReclassifyingProfileIds)
            {
                if (!owners.TryAdd(profileId, descriptor.AssetClass))
                {
                    throw new InvalidOperationException(
                        $"Reclassifying profile id '{profileId}' is declared by both '{owners[profileId]}' and '{descriptor.AssetClass}'; a profile resolves to one class.");
                }
            }
        }

        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(owners);
    }
}
