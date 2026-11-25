namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// One Security Master asset class, stated once for the class-plumbing generator prototype
/// (<c>W11-PACK-002</c>). The declaration carries only what neither
/// <see cref="Meridian.Contracts.SecurityMaster.SecurityAssetClassCatalog"/> nor
/// <see cref="Meridian.Contracts.SecurityMaster.SecurityAssetTermsCodecBindings"/> already says:
/// the class's structural classification and the few arms that are not mechanical. Class order
/// comes from the catalog, and the codec arms' parameters come from the binding table.
/// </summary>
/// <param name="Name">
/// The canonical asset-class name. It is also the <c>SecurityKind</c> union case, the
/// <c>SecurityAssetClassNames</c> constant and the generated codec's method suffix.
/// </param>
/// <param name="AssetClass">The F# <c>AssetClass</c> case name.</param>
/// <param name="Family">
/// The family label, or null for none. A label that names a nullary F# <c>AssetFamily</c> case
/// renders as that case; any other label renders as <c>AssetFamily.OtherFamily "label"</c>.
/// </param>
/// <param name="SubType">
/// The sub-type label. A label that names a nullary F# <c>SecuritySubType</c> case renders as that
/// case; any other label renders as <c>SecuritySubType.OtherSubType "label"</c>.
/// </param>
/// <param name="IssuerType">The default issuer archetype, or null for none.</param>
/// <param name="RiskCountry">The default risk country, or null for none.</param>
/// <param name="IsDerivative">Whether the class is an exchange-traded or OTC derivative.</param>
/// <param name="TermsType">The F# terms record name, when it is not <c>{Name}Terms</c>.</param>
/// <param name="DecodeMethod">
/// The decode method, when the class's binding is generated but the switch calls a hand-written
/// wrapper around it. The arguments are still derived from the binding.
/// </param>
/// <param name="DecodeExpression">
/// The whole decode arm expression, for a class with no codec binding (the profile-backed
/// <c>CustomAsset</c>).
/// </param>
/// <param name="HandWrittenWriterArm">
/// Whether the F# writer dispatch arm is hand-written because the class has no codec binding.
/// </param>
internal sealed record SecurityClassDeclaration(
    string Name,
    string AssetClass,
    string? Family,
    string SubType,
    string? IssuerType = null,
    string? RiskCountry = null,
    bool IsDerivative = false,
    string? TermsType = null,
    string? DecodeMethod = null,
    string? DecodeExpression = null,
    bool HandWrittenWriterArm = false)
{
    public string TermsTypeName => TermsType ?? Name + "Terms";
}

/// <summary>
/// The class-plumbing declaration for every existing Security Master class, in F# union order.
/// <see cref="SecurityClassPlumbingGenerationTests"/> holds it to the catalog, the F# union and
/// the hand-written source; it does not replace any of them (prototype only).
/// </summary>
internal static class SecurityClassPlumbingDeclarations
{
    private const string Derivative = "Derivative";
    private const string Listed = "ListedDerivative";
    private const string Otc = "OtcDerivative";

    public static IReadOnlyList<SecurityClassDeclaration> All { get; } =
    [
        new("Equity", "Equity", "CommonEquity", "CommonShare"),
        new("Option", Derivative, Listed, "OptionContract", IsDerivative: true),
        new("Future", Derivative, Listed, "FutureContract", IsDerivative: true),
        new("Bond", "FixedIncome", "CorporateDebt", "CorporateBond", IssuerType: "Corporate", DecodeMethod: "DecodeBond"),
        new("FxSpot", "CashEquivalent", "ForeignExchange", "FxSpot"),
        new("Deposit", "CashEquivalent", "BankProduct", "TimeDeposit", IssuerType: "Bank"),
        new("MoneyMarketFund", "Fund", "MoneyMarket", "MoneyMarketFund", IssuerType: "FundVehicle"),
        new("CertificateOfDeposit", "CashEquivalent", "BankProduct", "CertificateOfDeposit", IssuerType: "Bank"),
        new("CommercialPaper", "CashEquivalent", "CorporateDebt", "CommercialPaper", IssuerType: "Corporate"),
        new("TreasuryBill", "FixedIncome", "Sovereign", "TreasuryBill", IssuerType: "Sovereign", RiskCountry: "US"),
        new("Repo", "Financing", "RepurchaseAgreement", "Repo"),
        new("CashSweep", "CashEquivalent", "StructuredCash", "CashSweep"),
        new("OtherSecurity", "Other", null, "OtherSecurity"),
        new("Swap", Derivative, Otc, "SwapContract", IsDerivative: true),
        new("DirectLoan", "PrivateCredit", "PrivateLoan", "DirectLoan"),
        new("StructuredCredit", "FixedIncome", "SecuritizedCredit", "StructuredCredit", IssuerType: "StructuredVehicle"),
        new("PrivateFundInterest", "Fund", "PartnershipEquity", "LimitedPartnershipInterest", IssuerType: "FundVehicle"),
        new("PrivateCompanyEquity", "Equity", "PrivateCompanyEquity", "PrivateCompanyEquity", IssuerType: "PrivateCompany"),
        new("RealEstateHolding", "Other", "RealEstate", "RealEstateHolding", IssuerType: "PropertyOrSpv"),
        new("CommitmentGuarantee", "Financing", "CommitmentGuarantee", "CommitmentGuarantee", IssuerType: "Counterparty"),
        new("Commodity", "Other", "Commodity", "Commodity"),
        new("CryptoCurrency", "Other", "Crypto", "CryptoCurrency", TermsType: "CryptoTerms"),
        new("Cfd", Derivative, Otc, "Cfd", IsDerivative: true),
        new("Warrant", Derivative, Listed, "Warrant", IsDerivative: true),
        new("FxForward", Derivative, Otc, "FxForward", IsDerivative: true),
        new("NonDeliverableForward", Derivative, Otc, "NonDeliverableForward", IsDerivative: true),
        new("CreditDefaultSwap", Derivative, Otc, "CreditDefaultSwap", IsDerivative: true),
        new("Swaption", Derivative, Otc, "Swaption", IsDerivative: true),
        new("CapFloor", Derivative, Otc, "CapFloor", IsDerivative: true),
        new("StructuredNote", "FixedIncome", "CorporateDebt", "StructuredNote", IssuerType: "Corporate"),
        new("InvestmentFund", "Fund", "PooledFund", "InvestmentFund"),
        new("CustomAsset", "Other", "CustomAsset", "CustomAsset",
            DecodeExpression: "ToCustomAssetKind(bound, mode)", HandWrittenWriterArm: true),
    ];
}
