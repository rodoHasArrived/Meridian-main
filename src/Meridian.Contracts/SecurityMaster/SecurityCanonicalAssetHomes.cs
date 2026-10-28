namespace Meridian.Contracts.SecurityMaster;

/// <summary>Legacy labels whose economics have a first-class canonical asset home.</summary>
public static class SecurityCanonicalAssetHomes
{
    // Retained labels remain readable in historical documents, but cannot introduce a second
    // representation through BondSubclass.Other or a generic custom-asset profile.
    public static IReadOnlyList<string> SecuritizedLabels { get; } = Array.AsReadOnly<string>(
    [
        "StructuredCredit", "AssetBacked", "AssetBackedSecurity", "ABS", "MortgageBacked",
        "MortgageBackedSecurity", "MBS", "AgencyMbs", "NonAgencyMbs", "CommercialMbs", "CMBS",
        "Cmo", "Clo", "Cdo", "PrincipalOnly", "InterestOnly", "InverseInterestOnly",
        "CollateralizedLoanObligation", "CommercialMortgageBacked", "CommercialMortgageBackedSecurity"
    ]);

    private static readonly HashSet<string> SecuritizedKeys = SecuritizedLabels
        .Select(NormalizeLabel).ToHashSet(StringComparer.Ordinal);

    /// <summary>Uses the same separator-insensitive spelling as catalog label resolution.</summary>
    public static string NormalizeLabel(string? label)
        => new string((label ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public static bool IsSecuritizedLabel(string? label)
        => SecuritizedKeys.Contains(NormalizeLabel(label));

    public static bool IsBankLoanLabel(string? label)
        => NormalizeLabel(label) == "BANKLOAN";
}
