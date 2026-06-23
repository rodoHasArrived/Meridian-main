namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Registry metadata for introducing asset packs without changing core ledger contracts.
/// </summary>
public static class SecurityAssetPackRegistry
{
    private static readonly IReadOnlyList<string> StandardLifecycleEvents =
    [
        "Purchase",
        "Sale",
        "Coupon",
        "Dividend",
        "Draw",
        "Repayment",
        "CapitalCall",
        "Distribution",
        "Appraisal",
        "Impairment",
        "Maturity",
        "Default",
        "Amendment",
        "CorporateAction"
    ];

    private static readonly IReadOnlyList<string> StandardValuationMethods =
    [
        "MarketPrice",
        "ManagerReportedNav",
        "Appraisal",
        "DiscountedCashFlow",
        "AmortizedCost",
        "UserEstimate",
        "ExternalModel"
    ];

    private static readonly AssetPackContractSchema ContractSchema = new(
        Terms:
        [
            "instrument type",
            "legal terms",
            "economic terms",
            "settlement terms",
            "optional governed attributes"
        ],
        Counterparties:
        [
            "issuer",
            "borrower",
            "manager",
            "administrator",
            "bank",
            "custodian",
            "guarantor",
            "related entity"
        ],
        Dates:
        [
            "trade date",
            "settlement date",
            "issue date",
            "effective date",
            "maturity date",
            "valuation date",
            "amendment date"
        ],
        Currencies:
        [
            "base currency",
            "settlement currency",
            "reporting currency",
            "cash-leg currency"
        ],
        Ownership:
        [
            "owning organization",
            "legal entity",
            "portfolio",
            "account",
            "book",
            "beneficial owner"
        ],
        Seniority:
        [
            "seniority",
            "lien priority",
            "subordination",
            "waterfall position"
        ],
        Collateral:
        [
            "collateral type",
            "collateral value",
            "loan-to-value",
            "guarantee support",
            "covenant package"
        ],
        Rates:
        [
            "coupon rate",
            "spread",
            "reference rate",
            "floor",
            "cap",
            "payment frequency",
            "day-count convention"
        ],
        OptionalAttributes:
        [
            "custom client classification",
            "tax lot policy",
            "valuation source",
            "evidence requirement profile",
            "automation depth"
        ]);

    private static readonly AssetPackValidationRules StandardValidationRules = new(
        RequiredFields:
        [
            "asset pack",
            "owning entity",
            "portfolio or account",
            "book",
            "currency",
            "effective date",
            "valuation method",
            "source evidence or operator rationale"
        ],
        ExpectedSchedules:
        [
            "cash-flow schedule when contractual payments exist",
            "rate reset schedule when floating rates exist",
            "commitment schedule when unfunded exposure exists",
            "valuation cadence when marks are not market-priced"
        ],
        ToleranceChecks:
        [
            "cash variance",
            "position quantity variance",
            "accrual variance",
            "valuation stale-date tolerance",
            "FX translation tolerance"
        ],
        IncompatibleCombinations:
        [
            "market price without market identifier or retained price evidence",
            "amortized cost without principal basis and rate terms",
            "capital call without commitment or ownership context",
            "collateralized seniority without collateral description",
            "deep accounting automation without journal template coverage"
        ]);

    private static readonly AssetPackReportingTaxonomy StandardReportingTaxonomy = new(
        AssetClass: ["asset class", "asset pack", "instrument type"],
        Liquidity: ["daily", "monthly", "quarterly", "illiquid", "locked"],
        Geography: ["country", "region", "jurisdiction"],
        Industry: ["sector", "industry", "issuer industry", "borrower industry"],
        Risk: ["credit risk", "market risk", "liquidity risk", "counterparty risk", "concentration risk"],
        Tax: ["tax character", "withholding posture", "jurisdiction", "tax lot method"],
        CustomClientClassifications: ["client segment", "strategy bucket", "policy bucket", "custom tag"]);

    private static readonly IReadOnlyList<SecurityAssetPackDescriptor> Packs =
    [
        DeepAutomation(
            "cash-bank",
            "Cash and bank accounts",
            ["Cash", "BankAccount", "Deposit", "CashSweep", "MoneyMarketFund"],
            ["Purchase", "Sale", "Draw", "Repayment", "Maturity", "Default", "Amendment"],
            ["MarketPrice", "AmortizedCost", "UserEstimate"],
            ["cash movement", "bank fee", "interest income", "FX remeasurement"]),
        DeepAutomation(
            "public-equity-etf",
            "Public equities and exchange-traded funds",
            ["Equity", "ExchangeTradedFund"],
            ["Purchase", "Sale", "Dividend", "CorporateAction", "Impairment", "Amendment"],
            ["MarketPrice", "UserEstimate", "ExternalModel"],
            ["trade", "dividend", "corporate action", "realized gain/loss", "unrealized gain/loss"]),
        DeepAutomation(
            "fixed-income",
            "Fixed income",
            ["Bond", "TreasuryBill", "CommercialPaper", "CertificateOfDeposit", "Repo"],
            ["Purchase", "Sale", "Coupon", "Repayment", "Maturity", "Default", "Amendment", "CorporateAction", "Impairment"],
            ["MarketPrice", "DiscountedCashFlow", "AmortizedCost", "ExternalModel", "UserEstimate"],
            ["coupon accrual", "principal repayment", "amortization", "realized gain/loss", "impairment"]),
        DeepAutomation(
            "private-fund-partnership",
            "Private funds and partnerships",
            ["PrivateFund", "PartnershipInterest", "CustomAsset"],
            ["Purchase", "Sale", "CapitalCall", "Distribution", "Appraisal", "Impairment", "Amendment", "Maturity"],
            ["ManagerReportedNav", "Appraisal", "DiscountedCashFlow", "UserEstimate", "ExternalModel"],
            ["capital call", "distribution", "NAV adjustment", "management fee", "performance allocation"]),
        DeepAutomation(
            "private-loan-credit",
            "Private loans and credit",
            ["DirectLoan", "PrivateCredit", "CreditFacility"],
            ["Purchase", "Sale", "Coupon", "Draw", "Repayment", "Default", "Amendment", "Impairment", "Maturity"],
            ["DiscountedCashFlow", "AmortizedCost", "Appraisal", "UserEstimate", "ExternalModel"],
            ["interest accrual", "principal draw", "principal repayment", "fee income", "impairment", "default"]),
        DeepAutomation(
            "real-estate",
            "Real estate",
            ["RealEstate", "RealEstateInterest"],
            ["Purchase", "Sale", "Distribution", "Appraisal", "Impairment", "Amendment", "Maturity"],
            ["Appraisal", "DiscountedCashFlow", "ManagerReportedNav", "UserEstimate", "ExternalModel"],
            ["property acquisition", "rental income", "expense allocation", "appraisal adjustment", "impairment"]),
        DeepAutomation(
            "derivatives-fx",
            "Basic derivatives and FX",
            ["Option", "Future", "Swap", "FxSpot", "Forward"],
            ["Purchase", "Sale", "Draw", "Repayment", "Maturity", "Default", "Amendment", "CorporateAction"],
            ["MarketPrice", "DiscountedCashFlow", "ExternalModel", "UserEstimate"],
            ["premium", "variation margin", "settlement", "FX remeasurement", "realized gain/loss"]),
        DeepAutomation(
            "mortgage-facility-intercompany",
            "Mortgages, credit facilities and intercompany loans",
            ["Mortgage", "CreditFacility", "IntercompanyLoan", "DirectLoan"],
            ["Purchase", "Sale", "Coupon", "Draw", "Repayment", "Default", "Amendment", "Maturity", "Impairment"],
            ["DiscountedCashFlow", "AmortizedCost", "Appraisal", "UserEstimate", "ExternalModel"],
            ["interest accrual", "principal draw", "principal repayment", "intercompany elimination", "impairment"]),
        DeepAutomation(
            "commitment-guarantee",
            "Unfunded commitments and guarantees",
            ["UnfundedCommitment", "Guarantee", "CreditFacility"],
            ["Purchase", "Sale", "Draw", "Repayment", "CapitalCall", "Distribution", "Default", "Amendment", "Maturity"],
            ["UserEstimate", "ExternalModel", "DiscountedCashFlow"],
            ["commitment recognition", "guarantee exposure", "drawdown", "fee accrual", "release"]),
        WideCapture(
            "controlled-other-asset",
            "Controlled other asset",
            ["Art", "InsurancePolicy", "Vehicle", "SpecializedHolding", "OtherSecurity", "CustomAsset"],
            ["Purchase", "Sale", "Appraisal", "Impairment", "Amendment", "Maturity"],
            ["Appraisal", "UserEstimate", "ExternalModel", "ManagerReportedNav"],
            ["acquisition", "appraisal adjustment", "impairment", "disposal"])
    ];

    private static readonly IReadOnlyDictionary<string, SecurityAssetPackDescriptor> ByPackId =
        Packs.ToDictionary(pack => pack.PackId, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SecurityAssetPackDescriptor> All => Packs;

    public static SecurityAssetPackDescriptor? Find(string packId)
        => ByPackId.TryGetValue(packId, out var pack) ? pack : null;

    public static IReadOnlyList<SecurityAssetPackDescriptor> FindByAssetClass(string assetClass)
        => Packs
            .Where(pack => pack.AssetClasses.Contains(assetClass, StringComparer.OrdinalIgnoreCase))
            .ToArray();

    private static SecurityAssetPackDescriptor DeepAutomation(
        string packId,
        string displayName,
        IReadOnlyList<string> assetClasses,
        IReadOnlyList<string> lifecycleEvents,
        IReadOnlyList<string> valuationMethods,
        IReadOnlyList<string> journalTemplateEvents)
        => Pack(
            packId,
            displayName,
            assetClasses,
            lifecycleEvents,
            valuationMethods,
            journalTemplateEvents,
            AssetPackAutomationDepth.DeepAccountingAutomation);

    private static SecurityAssetPackDescriptor WideCapture(
        string packId,
        string displayName,
        IReadOnlyList<string> assetClasses,
        IReadOnlyList<string> lifecycleEvents,
        IReadOnlyList<string> valuationMethods,
        IReadOnlyList<string> journalTemplateEvents)
        => Pack(
            packId,
            displayName,
            assetClasses,
            lifecycleEvents,
            valuationMethods,
            journalTemplateEvents,
            AssetPackAutomationDepth.WideCapture);

    private static SecurityAssetPackDescriptor Pack(
        string packId,
        string displayName,
        IReadOnlyList<string> assetClasses,
        IReadOnlyList<string> lifecycleEvents,
        IReadOnlyList<string> valuationMethods,
        IReadOnlyList<string> journalTemplateEvents,
        AssetPackAutomationDepth automationDepth)
        => new(
            PackId: packId,
            DisplayName: displayName,
            AssetClasses: assetClasses,
            ContractSchema: ContractSchema,
            LifecycleEvents: lifecycleEvents.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            SupportedLifecycleEvents: StandardLifecycleEvents,
            ValuationMethods: valuationMethods.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            SupportedValuationMethods: StandardValuationMethods,
            AccountingRules: new AssetPackAccountingRules(
                JournalTemplateEvents: journalTemplateEvents,
                AccountingBases: ["GAAP", "IFRS", "Tax", "Management"],
                Currencies: ["base currency", "transaction currency", "reporting currency"],
                EntityScopes: ["organization", "entity", "portfolio", "account", "book", "fund where applicable"]),
            ValidationRules: StandardValidationRules,
            ReportingTaxonomy: StandardReportingTaxonomy,
            AutomationDepth: automationDepth,
            LedgerExtensionPolicy: "Asset packs map lifecycle events to journal templates; core ledger entries remain balanced, immutable, and asset-pack agnostic.");
}

public enum AssetPackAutomationDepth
{
    WideCapture = 0,
    DeepAccountingAutomation = 1
}

public sealed record SecurityAssetPackDescriptor(
    string PackId,
    string DisplayName,
    IReadOnlyList<string> AssetClasses,
    AssetPackContractSchema ContractSchema,
    IReadOnlyList<string> LifecycleEvents,
    IReadOnlyList<string> SupportedLifecycleEvents,
    IReadOnlyList<string> ValuationMethods,
    IReadOnlyList<string> SupportedValuationMethods,
    AssetPackAccountingRules AccountingRules,
    AssetPackValidationRules ValidationRules,
    AssetPackReportingTaxonomy ReportingTaxonomy,
    AssetPackAutomationDepth AutomationDepth,
    string LedgerExtensionPolicy);

public sealed record AssetPackContractSchema(
    IReadOnlyList<string> Terms,
    IReadOnlyList<string> Counterparties,
    IReadOnlyList<string> Dates,
    IReadOnlyList<string> Currencies,
    IReadOnlyList<string> Ownership,
    IReadOnlyList<string> Seniority,
    IReadOnlyList<string> Collateral,
    IReadOnlyList<string> Rates,
    IReadOnlyList<string> OptionalAttributes);

public sealed record AssetPackAccountingRules(
    IReadOnlyList<string> JournalTemplateEvents,
    IReadOnlyList<string> AccountingBases,
    IReadOnlyList<string> Currencies,
    IReadOnlyList<string> EntityScopes);

public sealed record AssetPackValidationRules(
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<string> ExpectedSchedules,
    IReadOnlyList<string> ToleranceChecks,
    IReadOnlyList<string> IncompatibleCombinations);

public sealed record AssetPackReportingTaxonomy(
    IReadOnlyList<string> AssetClass,
    IReadOnlyList<string> Liquidity,
    IReadOnlyList<string> Geography,
    IReadOnlyList<string> Industry,
    IReadOnlyList<string> Risk,
    IReadOnlyList<string> Tax,
    IReadOnlyList<string> CustomClientClassifications);
