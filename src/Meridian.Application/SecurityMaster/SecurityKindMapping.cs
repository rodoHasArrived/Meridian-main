using Meridian.Contracts.Domain.Enums;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Read-only bridge between <see cref="InstrumentType"/> (market-data layer) and the
/// F# <c>SecurityKind</c> asset-class strings (security master layer).
///
/// <para>
/// These two classification systems evolved independently: <see cref="InstrumentType"/> is
/// used by the data-collection pipeline and provider adapters, while the security master uses
/// F# discriminated-union asset-class names. This static lookup provides a non-breaking,
/// read-only mapping so routing code does not need to hardcode string comparisons.
/// </para>
/// </summary>
public static class SecurityKindMapping
{
    // AssetClass strings match SecurityKind.assetClass in SecurityMaster.fs.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<InstrumentType>> AssetClassToInstrumentTypes =
        new Dictionary<string, IReadOnlyList<InstrumentType>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Equity"]               = [InstrumentType.Equity],
            ["Option"]               = [InstrumentType.EquityOption, InstrumentType.IndexOption, InstrumentType.FuturesOption],
            ["Future"]               = [InstrumentType.Future, InstrumentType.SingleStockFuture],
            ["Bond"]                 = [InstrumentType.Bond],
            ["FxSpot"]               = [InstrumentType.Forex],
            ["Deposit"]              = [InstrumentType.Deposit],
            ["MoneyMarketFund"]      = [InstrumentType.Equity],     // MMFs trade as equity on most exchanges
            ["CertificateOfDeposit"] = [InstrumentType.Deposit],
            ["CommercialPaper"]      = [InstrumentType.Bond],
            ["TreasuryBill"]         = [InstrumentType.Bond],
            ["Repo"]                 = [InstrumentType.Repo],
            ["CashSweep"]            = [InstrumentType.Deposit],
            ["OtherSecurity"]        = [],
            ["Swap"]                 = [InstrumentType.Swap],
            ["DirectLoan"]           = [InstrumentType.DirectLoan],
            ["Commodity"]            = [InstrumentType.Commodity],
            ["CryptoCurrency"]       = [InstrumentType.Crypto],
            ["Cfd"]                  = [InstrumentType.CFD],
            ["Warrant"]              = [InstrumentType.Warrant],
        };

    private static readonly IReadOnlyDictionary<InstrumentType, string> InstrumentTypeToAssetClass =
        new Dictionary<InstrumentType, string>
        {
            [InstrumentType.Equity]          = "Equity",
            [InstrumentType.EquityOption]    = "Option",
            [InstrumentType.IndexOption]     = "Option",
            [InstrumentType.FuturesOption]   = "Option",
            [InstrumentType.Future]          = "Future",
            [InstrumentType.SingleStockFuture] = "Future",
            [InstrumentType.Forex]           = "FxSpot",
            [InstrumentType.Commodity]       = "Commodity",
            [InstrumentType.Crypto]          = "CryptoCurrency",
            [InstrumentType.Bond]            = "Bond",
            [InstrumentType.Index]           = "OtherSecurity",
            [InstrumentType.CFD]             = "Cfd",
            [InstrumentType.Warrant]         = "Warrant",
            [InstrumentType.Swap]            = "Swap",
            [InstrumentType.DirectLoan]      = "DirectLoan",
            [InstrumentType.Repo]            = "Repo",
            [InstrumentType.Deposit]         = "Deposit",
        };

    /// <summary>
    /// Returns the canonical security master asset-class string for the given
    /// <paramref name="instrumentType"/>, or <c>null</c> when no mapping exists.
    /// </summary>
    public static string? ToAssetClass(InstrumentType instrumentType) =>
        InstrumentTypeToAssetClass.TryGetValue(instrumentType, out var ac) ? ac : null;

    /// <summary>
    /// Returns all <see cref="InstrumentType"/> values that correspond to the given
    /// security master <paramref name="assetClass"/> string.
    /// Returns an empty list when the asset class is not mapped to a market-data instrument type.
    /// </summary>
    public static IReadOnlyList<InstrumentType> ToInstrumentTypes(string assetClass) =>
        AssetClassToInstrumentTypes.TryGetValue(assetClass, out var types)
            ? types
            : Array.Empty<InstrumentType>();

    /// <summary>
    /// Returns the single most-specific <see cref="InstrumentType"/> hint for a given
    /// <paramref name="assetClass"/>, or <c>null</c> when ambiguous or unmapped.
    /// Useful for provider subscription routing when a precise type is needed.
    /// </summary>
    public static InstrumentType? ToPrimaryInstrumentType(string assetClass)
    {
        var types = ToInstrumentTypes(assetClass);
        return types.Count == 1 ? types[0] : null;
    }
}
