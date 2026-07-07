namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Canonical Security Master corporate-action event vocabulary. The constants are the
/// stored wire values; per-type metadata, provider aliases, and ISO 15022 alignment live
/// in <see cref="CorporateActionTypeDescriptorCatalog"/>, which this class delegates to.
/// </summary>
public static class CorporateActionEventTypes
{
    public const string Dividend = "Dividend";
    public const string StockSplit = "StockSplit";
    public const string ReverseStockSplit = "ReverseStockSplit";
    public const string SpinOff = "SpinOff";
    public const string MergerAbsorption = "MergerAbsorption";
    public const string RightsIssue = "RightsIssue";
    public const string PrincipalPaydown = "PrincipalPaydown";
    public const string BondCall = "BondCall";
    public const string FuturesExpiry = "FuturesExpiry";
    public const string OptionContractAdjustment = "OptionContractAdjustment";
    public const string CryptoFork = "CryptoFork";
    public const string TenderOffer = "TenderOffer";
    public const string SpecialDividend = "SpecialDividend";
    public const string SymbolChange = "SymbolChange";
    public const string NameChange = "NameChange";
    public const string Delisting = "Delisting";
    public const string BondMaturityRedemption = "BondMaturityRedemption";
    public const string ReturnOfCapital = "ReturnOfCapital";

    /// <summary>
    /// Resolves canonical names, provider aliases, and CAEV codes to the canonical name.
    /// Unknown values are returned trimmed so callers can surface them verbatim in
    /// rejection reasons.
    /// </summary>
    public static string Normalize(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            return string.Empty;

        return CorporateActionTypeDescriptorCatalog.TryNormalize(eventType, out var descriptor)
            ? descriptor.CanonicalName
            : eventType.Trim();
    }

    public static bool IsKnown(string eventType)
        => CorporateActionTypeDescriptorCatalog.TryNormalize(eventType, out _);
}
