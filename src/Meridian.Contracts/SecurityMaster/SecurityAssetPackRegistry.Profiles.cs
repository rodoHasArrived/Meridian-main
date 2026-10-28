namespace Meridian.Contracts.SecurityMaster;

public static partial class SecurityAssetPackRegistry
{
    /// <summary>
    /// Only a registered profile identity may reclassify a profile-backed security. Caller-supplied
    /// category metadata may corroborate this mapping, but never decides the class.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ProfileAssetClasses { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["structured-credit-io-po"] = "StructuredCredit",
                ["private-fund-interest"] = "PrivateFundInterest",
                ["private-company-equity"] = "PrivateCompanyEquity",
                ["real-estate-holding"] = "RealEstateHolding",
                ["commitment-guarantee"] = "CommitmentGuarantee"
            });

    /// <summary>Compares profile class labels independently of punctuation and casing.</summary>
    public static bool MatchesProfileClassificationKeyword(string? value, string keyword)
    {
        static string Token(string label) => new(label.Where(char.IsLetterOrDigit).ToArray());
        return value is not null && string.Equals(Token(value), Token(keyword), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Class labels that must agree with a registered profile's identity. All canonical names and
    /// aliases are included, so a new class's metadata cannot bypass the conflict check.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ProfileClassificationKeywords { get; } =
        SecurityAssetClassCatalog.All.ToDictionary(
            static descriptor => descriptor.AssetClass,
            static descriptor => (IReadOnlyList<string>)new[] { descriptor.AssetClass }
                .Concat(descriptor.Aliases ?? [])
                .Concat(descriptor.AssetClass switch
                {
                    "PrivateFundInterest" => new[] { "PrivateFunds" },
                    "PrivateCompanyEquity" => ["PrivateEquity"],
                    "RealEstateHolding" => ["RealEstate", "RealEstateInterest"],
                    "CommitmentGuarantee" => ["UnfundedCommitment", "Guarantee"],
                    _ => []
                })
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
}
