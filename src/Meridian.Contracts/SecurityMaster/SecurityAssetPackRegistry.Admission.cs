namespace Meridian.Contracts.SecurityMaster;

public static partial class SecurityAssetPackRegistry
{
    /// <summary>The canonical-write gate that consumes <see cref="AssetPackContractSchema.AssetClassTerms"/>.</summary>
    public const string AdmissionEnforcement = "CanonicalCreateAndAmend";

    private static IReadOnlyList<AssetPackClassTermsSchema> BuildAssetClassTermsSchemas(IReadOnlyList<string> assetClasses)
        => assetClasses.Where(IsCatalogAssetClass).Select(assetClass =>
        {
            var canonical = SecurityAssetClassCatalog.GetOrDefault(assetClass).AssetClass;
            var writes = SecurityAssetClassCatalog.GetTermsVersion(canonical);
            var fields = SecurityAssetTermsSchema.WritableFields(canonical);
            var innerFields = SecurityAssetTermsSchema.ElementFieldPaths
                .Where(path => path.StartsWith(canonical + ".", StringComparison.Ordinal))
                .Select(path => path[(canonical.Length + 1)..])
                .Where(path => IsWritablePath(canonical, path, writes))
                .ToDictionary(path => path, path => (IReadOnlyList<SecurityAssetTermField>)SecurityAssetTermsSchema
                    .ElementFields(canonical, path).Where(field => field.SinceTermsVersion <= writes).ToArray(), StringComparer.Ordinal);
            return new AssetPackClassTermsSchema(canonical, writes, fields, innerFields);
        }).ToArray();

    private static bool IsWritablePath(string assetClass, string path, int writes)
    {
        var fields = SecurityAssetTermsSchema.WritableFields(assetClass);
        var segments = path.Split('.');
        for (var index = 0; index < segments.Length; index++)
        {
            if (!fields.Any(field => field.Key == segments[index] && field.SinceTermsVersion <= writes))
                return false;
            fields = SecurityAssetTermsSchema.ElementFields(assetClass, string.Join('.', segments.Take(index + 1)));
        }
        return true;
    }

    private static void RequireAssetClassTermsSchemas(SecurityAssetPackDescriptor pack, List<AssetPackRegistryValidationIssue> issues)
    {
        var expected = BuildAssetClassTermsSchemas(pack.AssetClasses);
        var actual = pack.ContractSchema.AssetClassTerms;
        if (actual.Count != expected.Count || expected.Any(schema =>
        {
            var matching = actual.Where(candidate => candidate.AssetClass == schema.AssetClass).ToArray();
            return matching.Length != 1 || !MatchesWritableSchema(matching[0], schema);
        }))
        {
            issues.Add(Issue("asset-pack.writable-contract-schema-mismatch", "Critical", "ContractSchema.AssetClassTerms",
                "Each modeled asset class must carry its current writable field and nested-item schemas from SecurityAssetTermsSchema."));
        }
    }

    private static bool MatchesWritableSchema(AssetPackClassTermsSchema actual, AssetPackClassTermsSchema expected)
        => actual.TermsVersion == expected.TermsVersion
           && actual.Fields.SequenceEqual(expected.Fields)
           && actual.ElementFieldsByPath.Count == expected.ElementFieldsByPath.Count
           && expected.ElementFieldsByPath.All(pair => actual.ElementFieldsByPath.TryGetValue(pair.Key, out var fields)
               && fields.SequenceEqual(pair.Value));
}

/// <summary>
/// Machine-readable admission shape for one asset class at this node's write version. Fields and
/// nested item shapes come from the canonical terms schema, independently of descriptive pack policies.
/// </summary>
public sealed record AssetPackClassTermsSchema(
    string AssetClass,
    int TermsVersion,
    IReadOnlyList<SecurityAssetTermField> Fields,
    IReadOnlyDictionary<string, IReadOnlyList<SecurityAssetTermField>> ElementFieldsByPath);
