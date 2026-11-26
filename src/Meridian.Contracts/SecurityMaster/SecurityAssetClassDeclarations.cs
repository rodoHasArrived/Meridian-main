namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// One Security Master asset class, declared once. The mechanical per-class plumbing is generated
/// from this declaration (<c>W11-PACK-002</c>, <c>DEC-PACK-CODEGEN-001</c>). A row carries only
/// what <see cref="SecurityAssetClassCatalog"/> and <see cref="SecurityAssetTermsCodecBindings"/>
/// do not already say: the structural classification, the case documentation, and whether its writer
/// arm is hand-written.
/// </summary>
/// <param name="Name">
/// The canonical asset-class name. It is also the F# <c>SecurityKind</c> union case, the
/// <see cref="SecurityAssetClassNames"/> constant and the generated codec's method suffix.
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
/// <param name="CaseDoc">The XML doc lines written above the union case, without the <c>///</c>.</param>
/// <param name="HandWrittenWriterArm">
/// Whether the F# writer dispatch arm is hand-written, outside the generated region, because the
/// class has no codec binding.
/// </param>
internal sealed record SecurityAssetClassDeclaration(
    string Name,
    string AssetClass,
    string? Family,
    string SubType,
    string? IssuerType = null,
    string? RiskCountry = null,
    bool IsDerivative = false,
    string? TermsType = null,
    IReadOnlyList<string>? CaseDoc = null,
    bool HandWrittenWriterArm = false)
{
    public string TermsTypeName => TermsType ?? Name + "Terms";
}

/// <summary>
/// The single declaration of every compiled Security Master asset class, in F# union order. It is
/// the source of the generated regions marked <c>&lt;generated:…&gt;</c> in
/// <c>Domain/SecurityMaster.fs</c> (the <c>SecurityKind</c> cases, the <c>AssetClassRegistry</c>
/// key arms and classification rows), <c>Interop.SecurityMaster.fs</c> (the flat-writer dispatch)
/// and <see cref="SecurityAssetClassNames"/>. Decoding is dispatched through the per-class asset
/// modules' codec registry, not a generated switch. Edit a row here, then regenerate: set
/// <c>MERIDIAN_ACCEPT_GENERATED_CODEC=1</c> and run <c>SecurityClassPlumbingGenerationTests</c>.
/// Without that variable the tests fail when a generated region differs from this table.
/// <para>
/// Adding a class takes a row here and, for a flat class, a row in
/// <see cref="SecurityAssetTermsCodecBindings"/>. The per-class semantic sites listed in
/// <c>docs/engineering/plans/security-master-class-plumbing-codegen.md</c> stay hand-written.
/// </para>
/// </summary>
internal static class SecurityAssetClassDeclarations
{
    private const string Derivative = "Derivative";
    private const string Listed = "ListedDerivative";
    private const string Otc = "OtcDerivative";

    public static IReadOnlyList<SecurityAssetClassDeclaration> All { get; } =
    [
        new("Equity", "Equity", "CommonEquity", "CommonShare"),
        new("Option", Derivative, Listed, "OptionContract", IsDerivative: true),
        new("Future", Derivative, Listed, "FutureContract", IsDerivative: true),
        new("Bond", "FixedIncome", "CorporateDebt", "CorporateBond", IssuerType: "Corporate"),
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
        new("FxForward", Derivative, Otc, "FxForward", IsDerivative: true,
            CaseDoc: ["Outright deliverable FX forward."]),
        new("NonDeliverableForward", Derivative, Otc, "NonDeliverableForward", IsDerivative: true,
            CaseDoc: ["Cash-settled non-deliverable FX forward."]),
        new("CreditDefaultSwap", Derivative, Otc, "CreditDefaultSwap", IsDerivative: true,
            CaseDoc: ["Single-name or index credit default swap."]),
        new("Swaption", Derivative, Otc, "Swaption", IsDerivative: true,
            CaseDoc: ["Option to enter an interest-rate swap."]),
        new("CapFloor", Derivative, Otc, "CapFloor", IsDerivative: true,
            CaseDoc: ["Interest-rate cap, floor or collar."]),
        new("StructuredNote", "FixedIncome", "CorporateDebt", "StructuredNote", IssuerType: "Corporate",
            CaseDoc: ["Issuer note with a payoff linked to a reference asset or basket."]),
        new("InvestmentFund", "Fund", "PooledFund", "InvestmentFund",
            CaseDoc:
            [
                "Mutual fund, ETF, hedge fund, REIT, or closed-end fund.",
                "Market value = units × NAV (or market price); no amortization in general.",
            ]),
        new("CustomAsset", "Other", "CustomAsset", "CustomAsset",
            CaseDoc:
            [
                "Governed profile-backed custom asset (private and other assets modeled via",
                "approved custom profiles). Terms are carried as an opaque, verbatim document.",
            ],
            HandWrittenWriterArm: true),
    ];
}
