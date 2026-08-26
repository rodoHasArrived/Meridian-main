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
            ["Deposit", "CashSweep", "MoneyMarketFund"],
            ["Purchase", "Sale", "Draw", "Repayment", "Maturity", "Default", "Amendment"],
            ["MarketPrice", "AmortizedCost", "UserEstimate"],
            ["cash movement", "bank fee", "interest income", "FX remeasurement"],
            plannedAssetClasses: ["Cash", "BankAccount"]),
        DeepAutomation(
            "public-equity-etf",
            "Public equities and exchange-traded funds",
            ["Equity", "InvestmentFund"],
            ["Purchase", "Sale", "Dividend", "CorporateAction", "Impairment", "Amendment"],
            ["MarketPrice", "UserEstimate", "ExternalModel"],
            ["trade", "dividend", "corporate action", "realized gain/loss", "unrealized gain/loss"],
            plannedAssetClasses: ["ExchangeTradedFund"]),
        DeepAutomation(
            "fixed-income",
            "Fixed income",
            ["Bond", "StructuredCredit", "TreasuryBill", "CommercialPaper", "CertificateOfDeposit", "Repo"],
            ["Purchase", "Sale", "Coupon", "Repayment", "Maturity", "Default", "Amendment", "CorporateAction", "Impairment"],
            ["MarketPrice", "DiscountedCashFlow", "AmortizedCost", "ExternalModel", "UserEstimate"],
            ["coupon accrual", "principal repayment", "amortization", "realized gain/loss", "impairment"]),
        DeepAutomation(
            "private-fund-partnership",
            "Private funds and partnerships",
            ["PrivateFundInterest", "PrivateCompanyEquity"],
            ["Purchase", "Sale", "CapitalCall", "Distribution", "Appraisal", "Impairment", "Amendment", "Maturity"],
            ["ManagerReportedNav", "Appraisal", "DiscountedCashFlow", "UserEstimate", "ExternalModel"],
            ["capital call", "distribution", "NAV adjustment", "management fee", "performance allocation"],
            plannedAssetClasses: ["PrivateFund", "PartnershipInterest"]),
        DeepAutomation(
            "private-loan-credit",
            "Private loans and credit",
            ["DirectLoan"],
            ["Purchase", "Sale", "Coupon", "Draw", "Repayment", "Default", "Amendment", "Impairment", "Maturity"],
            ["DiscountedCashFlow", "AmortizedCost", "Appraisal", "UserEstimate", "ExternalModel"],
            ["interest accrual", "principal draw", "principal repayment", "fee income", "impairment", "default"],
            plannedAssetClasses: ["PrivateCredit", "CreditFacility"]),
        DeepAutomation(
            "real-estate",
            "Real estate",
            ["RealEstateHolding"],
            ["Purchase", "Sale", "Distribution", "Appraisal", "Impairment", "Amendment", "Maturity"],
            ["Appraisal", "DiscountedCashFlow", "ManagerReportedNav", "UserEstimate", "ExternalModel"],
            ["property acquisition", "rental income", "expense allocation", "appraisal adjustment", "impairment"],
            plannedAssetClasses: ["RealEstate", "RealEstateInterest"]),
        DeepAutomation(
            "derivatives-fx",
            "Basic derivatives and FX",
            ["Option", "Future", "Swap", "FxSpot", "Cfd", "Warrant"],
            ["Purchase", "Sale", "Draw", "Repayment", "Maturity", "Default", "Amendment", "CorporateAction"],
            ["MarketPrice", "DiscountedCashFlow", "ExternalModel", "UserEstimate"],
            ["premium", "variation margin", "settlement", "FX remeasurement", "realized gain/loss"],
            plannedAssetClasses: ["Forward"]),
        DeepAutomation(
            "mortgage-facility-intercompany",
            "Mortgages, credit facilities and intercompany loans",
            ["DirectLoan"],
            ["Purchase", "Sale", "Coupon", "Draw", "Repayment", "Default", "Amendment", "Maturity", "Impairment"],
            ["DiscountedCashFlow", "AmortizedCost", "Appraisal", "UserEstimate", "ExternalModel"],
            ["interest accrual", "principal draw", "principal repayment", "intercompany elimination", "impairment"],
            plannedAssetClasses: ["Mortgage", "CreditFacility", "IntercompanyLoan"]),
        DeepAutomation(
            "commitment-guarantee",
            "Unfunded commitments and guarantees",
            ["CommitmentGuarantee"],
            ["Purchase", "Sale", "Draw", "Repayment", "CapitalCall", "Distribution", "Default", "Amendment", "Maturity"],
            ["UserEstimate", "ExternalModel", "DiscountedCashFlow"],
            ["commitment recognition", "guarantee exposure", "drawdown", "fee accrual", "release"],
            plannedAssetClasses: ["UnfundedCommitment", "Guarantee", "CreditFacility"]),
        WideCapture(
            "controlled-other-asset",
            "Controlled other asset",
            ["OtherSecurity", "CustomAsset", "Commodity", "CryptoCurrency"],
            ["Purchase", "Sale", "Appraisal", "Impairment", "Amendment", "Maturity"],
            ["Appraisal", "UserEstimate", "ExternalModel", "ManagerReportedNav"],
            ["acquisition", "appraisal adjustment", "impairment", "disposal"],
            plannedAssetClasses: ["Art", "InsurancePolicy", "Vehicle", "SpecializedHolding"])
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

    public static AssetPackRegistryValidationResult ValidateAll()
    {
        return ValidateCandidateSet([]);
    }

    public static AssetPackRegistryValidationResult ValidateCandidateSet(
        IReadOnlyList<SecurityAssetPackDescriptor> candidatePacks)
    {
        ArgumentNullException.ThrowIfNull(candidatePacks);

        var candidateIds = new HashSet<string>(
            candidatePacks.Select(static pack => pack.PackId),
            StringComparer.OrdinalIgnoreCase);
        var packs = Packs.Concat(candidatePacks).ToArray();
        var issues = packs
            .SelectMany(pack => ValidateDescriptor(pack).Issues)
            .ToList();

        issues.AddRange(packs
            .GroupBy(static pack => pack.PackId, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(group => Issue(
                "asset-pack.duplicate-pack-id",
                "Critical",
                "PackId",
                $"Asset pack id '{group.Key}' is duplicated.")));

        issues.AddRange(packs
            .SelectMany(static pack => pack.AssetClasses.Select(assetClass => new { pack.PackId, AssetClass = assetClass }))
            .GroupBy(static row => row.AssetClass, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Select(static row => row.PackId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Where(group => group.Any(row => candidateIds.Contains(row.PackId)))
            .Select(group => Issue(
                "asset-pack.asset-class-overlap",
                "Critical",
                "AssetClasses",
                $"Asset class '{group.Key}' is claimed by multiple asset packs.")));

        return new AssetPackRegistryValidationResult(issues.Count == 0, issues);
    }

    public static SecurityAssetPackDescriptor CreateCandidateDescriptor(
        string packId,
        string displayName,
        IReadOnlyList<string> assetClasses,
        IReadOnlyList<string> lifecycleEvents,
        IReadOnlyList<string> valuationMethods,
        IReadOnlyList<string> journalTemplateEvents,
        AssetPackAutomationDepth automationDepth,
        IReadOnlyList<string>? plannedAssetClasses = null)
    {
        return Pack(
            packId,
            displayName,
            assetClasses,
            lifecycleEvents,
            valuationMethods,
            journalTemplateEvents,
            automationDepth,
            plannedAssetClasses);
    }

    public static AssetPackRegistryValidationResult ValidateDescriptor(SecurityAssetPackDescriptor pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        var issues = new List<AssetPackRegistryValidationIssue>();
        RequireText(pack.PackId, "PackId", "asset-pack.pack-id-required", issues);
        RequireText(pack.DisplayName, "DisplayName", "asset-pack.display-name-required", issues);
        RequireClaimedOrPlannedAssetClasses(pack, issues);
        RequireCatalogAssetClasses(pack, issues);
        RequireSchema(pack.ContractSchema, issues);
        RequireSupportedValues(pack.LifecycleEvents, pack.SupportedLifecycleEvents, "LifecycleEvents", "asset-pack.unsupported-lifecycle-event", issues);
        RequireLifecycleCoverage(pack, issues);
        RequireSupportedValues(pack.ValuationMethods, pack.SupportedValuationMethods, "ValuationMethods", "asset-pack.unsupported-valuation-method", issues);
        RequireAccountingRules(pack, issues);
        RequireValidationRules(pack.ValidationRules, issues);
        RequireReportingTaxonomy(pack.ReportingTaxonomy, issues);
        RequireAdmissionPolicy(pack.AdmissionPolicy, issues);

        if (string.IsNullOrWhiteSpace(pack.LedgerExtensionPolicy) ||
            !pack.LedgerExtensionPolicy.Contains("core ledger", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Issue(
                "asset-pack.core-ledger-policy-required",
                "Critical",
                "LedgerExtensionPolicy",
                "Asset packs must state that lifecycle-to-template extension does not alter the core ledger."));
        }

        return new AssetPackRegistryValidationResult(issues.Count == 0, issues);
    }

    private static SecurityAssetPackDescriptor DeepAutomation(
        string packId,
        string displayName,
        IReadOnlyList<string> assetClasses,
        IReadOnlyList<string> lifecycleEvents,
        IReadOnlyList<string> valuationMethods,
        IReadOnlyList<string> journalTemplateEvents,
        IReadOnlyList<string>? plannedAssetClasses = null)
        => Pack(
            packId,
            displayName,
            assetClasses,
            lifecycleEvents,
            valuationMethods,
            journalTemplateEvents,
            AssetPackAutomationDepth.DeepAccountingAutomation,
            plannedAssetClasses);

    private static SecurityAssetPackDescriptor WideCapture(
        string packId,
        string displayName,
        IReadOnlyList<string> assetClasses,
        IReadOnlyList<string> lifecycleEvents,
        IReadOnlyList<string> valuationMethods,
        IReadOnlyList<string> journalTemplateEvents,
        IReadOnlyList<string>? plannedAssetClasses = null)
        => Pack(
            packId,
            displayName,
            assetClasses,
            lifecycleEvents,
            valuationMethods,
            journalTemplateEvents,
            AssetPackAutomationDepth.WideCapture,
            plannedAssetClasses);

    private static SecurityAssetPackDescriptor Pack(
        string packId,
        string displayName,
        IReadOnlyList<string> assetClasses,
        IReadOnlyList<string> lifecycleEvents,
        IReadOnlyList<string> valuationMethods,
        IReadOnlyList<string> journalTemplateEvents,
        AssetPackAutomationDepth automationDepth,
        IReadOnlyList<string>? plannedAssetClasses = null)
        => new(
            PackId: packId,
            DisplayName: displayName,
            AssetClasses: assetClasses,
            PlannedAssetClasses: plannedAssetClasses ?? [],
            ContractSchema: ContractSchema,
            LifecycleEvents: lifecycleEvents.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            SupportedLifecycleEvents: StandardLifecycleEvents,
            LifecycleCoverage: BuildLifecycleCoverage(lifecycleEvents, journalTemplateEvents, automationDepth),
            ValuationMethods: valuationMethods.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            SupportedValuationMethods: StandardValuationMethods,
            AccountingRules: new AssetPackAccountingRules(
                JournalTemplateEvents: journalTemplateEvents,
                JournalTemplates: journalTemplateEvents.Select(ToJournalTemplateRule).ToArray(),
                AccountingBases: ["GAAP", "IFRS", "Tax", "Management"],
                Currencies: ["base currency", "transaction currency", "reporting currency"],
                EntityScopes: ["organization", "entity", "portfolio", "account", "book", "fund where applicable"]),
            ValidationRules: StandardValidationRules,
            ReportingTaxonomy: StandardReportingTaxonomy,
            AutomationDepth: automationDepth,
            AdmissionPolicy: BuildAdmissionPolicy(automationDepth),
            LedgerExtensionPolicy: "Asset packs map lifecycle events to journal templates; core ledger entries remain balanced, immutable, and asset-pack agnostic.");

    private static AssetPackAdmissionPolicy BuildAdmissionPolicy(AssetPackAutomationDepth automationDepth)
    {
        var automationPolicy = automationDepth == AssetPackAutomationDepth.DeepAccountingAutomation
            ? "Automated posting candidates require a mapped lifecycle event, retained evidence or operator rationale, approval state, accounting basis, currency scope, entity scope, idempotency key, and period posture before journal creation."
            : "Records may be captured and classified, but accounting automation remains disabled until governed journal-template, evidence, approval, and validation coverage is added.";

        return new AssetPackAdmissionPolicy(
            AllowsWideCapture: true,
            RequiresJournalTemplateBeforeAutomatedPosting: true,
            AccountingPostingPolicy: automationPolicy,
            EvidencePolicy: "Every captured asset-pack record must retain source evidence or explicit operator rationale before close, reporting, or accounting use.",
            CoreLedgerMutationPolicy: "Asset packs may introduce lifecycle-to-template mappings and validation rules, but must not add asset-specific core ledger mutation paths.",
            AutomationEscalationTriggers:
            [
                "recurring lifecycle volume",
                "material close or reporting dependency",
                "approved journal-template coverage",
                "source evidence and reconciliation coverage",
                "operator-reviewed validation history"
            ]);
    }

    private static AssetPackJournalTemplateRule ToJournalTemplateRule(string journalTemplateEvent)
        => new(
            LifecycleEvent: InferLifecycleEvent(journalTemplateEvent),
            TemplateId: $"asset-pack.{NormalizeTemplateToken(journalTemplateEvent)}",
            AccountingBases: ["GAAP", "IFRS", "Tax", "Management"],
            CurrencyScopes: ["base currency", "transaction currency", "reporting currency"],
            EntityScopes: ["organization", "entity", "portfolio", "account", "book", "fund where applicable"]);

    private static IReadOnlyList<AssetPackLifecycleCoverage> BuildLifecycleCoverage(
        IReadOnlyList<string> lifecycleEvents,
        IReadOnlyList<string> journalTemplateEvents,
        AssetPackAutomationDepth automationDepth)
    {
        var templatesByLifecycleEvent = journalTemplateEvents
            .Select(ToJournalTemplateRule)
            .GroupBy(static template => template.LifecycleEvent, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<string>)group.Select(static template => template.TemplateId).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return lifecycleEvents
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(lifecycleEvent =>
            {
                var templateIds = templatesByLifecycleEvent.TryGetValue(lifecycleEvent, out var ids) ? ids : [];
                var automationStatus = templateIds.Count > 0
                    ? "AutomatedTemplateAvailable"
                    : automationDepth == AssetPackAutomationDepth.DeepAccountingAutomation
                        ? "CapturedPendingTemplate"
                        : "ManualReviewRequired";

                return new AssetPackLifecycleCoverage(
                    LifecycleEvent: lifecycleEvent,
                    CapturePolicy: "Retain source evidence or operator rationale before accounting, reporting, or close usage.",
                    AccountingAutomationStatus: automationStatus,
                    JournalTemplateIds: templateIds);
            })
            .ToArray();
    }

    private static string InferLifecycleEvent(string journalTemplateEvent)
    {
        var token = NormalizeTemplateToken(journalTemplateEvent);
        return token switch
        {
            var value when value.Contains("purchase", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("acquisition", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("trade", StringComparison.OrdinalIgnoreCase) => "Purchase",
            var value when value.Contains("sale", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("disposal", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("realized-gain-loss", StringComparison.OrdinalIgnoreCase) => "Sale",
            var value when value.Contains("coupon", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("interest", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("fee", StringComparison.OrdinalIgnoreCase) => "Coupon",
            var value when value.Contains("dividend", StringComparison.OrdinalIgnoreCase) => "Dividend",
            var value when value.Contains("draw", StringComparison.OrdinalIgnoreCase) => "Draw",
            var value when value.Contains("repayment", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("paydown", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("release", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("settlement", StringComparison.OrdinalIgnoreCase) => "Repayment",
            var value when value.Contains("capital-call", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("commitment", StringComparison.OrdinalIgnoreCase) => "CapitalCall",
            var value when value.Contains("distribution", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("rental-income", StringComparison.OrdinalIgnoreCase) => "Distribution",
            var value when value.Contains("appraisal", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("nav", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("valuation", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("unrealized-gain-loss", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("fx-remeasurement", StringComparison.OrdinalIgnoreCase) => "Appraisal",
            var value when value.Contains("default", StringComparison.OrdinalIgnoreCase) => "Default",
            var value when value.Contains("impairment", StringComparison.OrdinalIgnoreCase) => "Impairment",
            var value when value.Contains("maturity", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("amortization", StringComparison.OrdinalIgnoreCase) => "Maturity",
            var value when value.Contains("corporate-action", StringComparison.OrdinalIgnoreCase) => "CorporateAction",
            _ => "Amendment"
        };
    }

    private static string NormalizeTemplateToken(string value)
        => string.Join(
            "-",
            value.Split([' ', '/', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToLowerInvariant();

    /// <summary>
    /// A pack must name at least one asset class SOMEWHERE. Present coverage and planned coverage are
    /// both admissible: a pack drafted ahead of the domain work — the extension path this registry
    /// exists to support — legitimately covers nothing yet and declares only planned classes.
    /// </summary>
    private static void RequireClaimedOrPlannedAssetClasses(
        SecurityAssetPackDescriptor pack,
        List<AssetPackRegistryValidationIssue> issues)
    {
        if (pack.AssetClasses.Count == 0 && pack.PlannedAssetClasses.Count == 0)
        {
            issues.Add(Issue(
                "asset-pack.asset-class-required",
                "Critical",
                "AssetClasses",
                "A pack must name at least one asset class under AssetClasses (covered today) or "
                + "PlannedAssetClasses (anticipated)."));
        }
    }

    /// <summary>
    /// Holds the pack registry to the asset-class catalog in BOTH directions. The claimed set must
    /// name only classes the domain can represent, and the planned set must name only classes it
    /// cannot — otherwise a pack can advertise coverage for an instrument that has no
    /// <c>SecurityKind</c> arm, terms schema, or validator, and the operational readiness report
    /// republishes that claim.
    /// </summary>
    private static void RequireCatalogAssetClasses(
        SecurityAssetPackDescriptor pack,
        List<AssetPackRegistryValidationIssue> issues)
    {
        foreach (var assetClass in pack.AssetClasses)
        {
            if (!IsCatalogAssetClass(assetClass))
            {
                issues.Add(Issue(
                    "asset-pack.asset-class-not-in-catalog",
                    "Critical",
                    "AssetClasses",
                    $"Asset class '{assetClass}' is not a Security Master catalog asset class. " +
                    "Name a catalog class, or declare it under PlannedAssetClasses."));
            }
        }

        foreach (var plannedAssetClass in pack.PlannedAssetClasses)
        {
            if (IsCatalogAssetClass(plannedAssetClass))
            {
                issues.Add(Issue(
                    "asset-pack.planned-asset-class-already-modeled",
                    "Critical",
                    "PlannedAssetClasses",
                    $"Asset class '{plannedAssetClass}' is already a Security Master catalog asset " +
                    "class and must move into AssetClasses."));
            }
        }
    }

    private static bool IsCatalogAssetClass(string assetClass)
        => SecurityAssetClassCatalog.AssetClasses.Contains(assetClass, StringComparer.OrdinalIgnoreCase);

    private static void RequireSchema(
        AssetPackContractSchema schema,
        List<AssetPackRegistryValidationIssue> issues)
    {
        RequireNonEmpty(schema.Terms, "ContractSchema.Terms", "asset-pack.schema-terms-required", issues);
        RequireNonEmpty(schema.Counterparties, "ContractSchema.Counterparties", "asset-pack.schema-counterparties-required", issues);
        RequireNonEmpty(schema.Dates, "ContractSchema.Dates", "asset-pack.schema-dates-required", issues);
        RequireNonEmpty(schema.Currencies, "ContractSchema.Currencies", "asset-pack.schema-currencies-required", issues);
        RequireNonEmpty(schema.Ownership, "ContractSchema.Ownership", "asset-pack.schema-ownership-required", issues);
        RequireNonEmpty(schema.Seniority, "ContractSchema.Seniority", "asset-pack.schema-seniority-required", issues);
        RequireNonEmpty(schema.Collateral, "ContractSchema.Collateral", "asset-pack.schema-collateral-required", issues);
        RequireNonEmpty(schema.Rates, "ContractSchema.Rates", "asset-pack.schema-rates-required", issues);
        RequireNonEmpty(schema.OptionalAttributes, "ContractSchema.OptionalAttributes", "asset-pack.schema-optional-attributes-required", issues);
    }

    private static void RequireAccountingRules(
        SecurityAssetPackDescriptor pack,
        List<AssetPackRegistryValidationIssue> issues)
    {
        RequireNonEmpty(pack.AccountingRules.AccountingBases, "AccountingRules.AccountingBases", "asset-pack.accounting-basis-required", issues);
        RequireNonEmpty(pack.AccountingRules.Currencies, "AccountingRules.Currencies", "asset-pack.accounting-currency-required", issues);
        RequireNonEmpty(pack.AccountingRules.EntityScopes, "AccountingRules.EntityScopes", "asset-pack.accounting-entity-scope-required", issues);

        if (pack.AutomationDepth == AssetPackAutomationDepth.DeepAccountingAutomation)
        {
            RequireNonEmpty(
                pack.AccountingRules.JournalTemplates,
                "AccountingRules.JournalTemplates",
                "asset-pack.deep-automation-template-required",
                issues);
        }

        foreach (var template in pack.AccountingRules.JournalTemplates)
        {
            RequireText(template.LifecycleEvent, "AccountingRules.JournalTemplates.LifecycleEvent", "asset-pack.template-lifecycle-required", issues);
            RequireText(template.TemplateId, "AccountingRules.JournalTemplates.TemplateId", "asset-pack.template-id-required", issues);
            RequireNonEmpty(template.AccountingBases, "AccountingRules.JournalTemplates.AccountingBases", "asset-pack.template-basis-required", issues);
            RequireNonEmpty(template.CurrencyScopes, "AccountingRules.JournalTemplates.CurrencyScopes", "asset-pack.template-currency-scope-required", issues);
            RequireNonEmpty(template.EntityScopes, "AccountingRules.JournalTemplates.EntityScopes", "asset-pack.template-entity-scope-required", issues);

            if (!pack.SupportedLifecycleEvents.Contains(template.LifecycleEvent, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(Issue(
                    "asset-pack.template-unsupported-lifecycle-event",
                    "Critical",
                    "AccountingRules.JournalTemplates.LifecycleEvent",
                    $"Journal template '{template.TemplateId}' uses unsupported lifecycle event '{template.LifecycleEvent}'."));
            }
        }
    }

    private static void RequireLifecycleCoverage(
        SecurityAssetPackDescriptor pack,
        List<AssetPackRegistryValidationIssue> issues)
    {
        RequireNonEmpty(pack.LifecycleCoverage, "LifecycleCoverage", "asset-pack.lifecycle-coverage-required", issues);
        foreach (var lifecycleEvent in pack.LifecycleEvents)
        {
            if (!pack.LifecycleCoverage.Any(coverage =>
                    string.Equals(coverage.LifecycleEvent, lifecycleEvent, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(Issue(
                    "asset-pack.lifecycle-coverage-missing",
                    "Critical",
                    "LifecycleCoverage",
                    $"Lifecycle event '{lifecycleEvent}' must declare capture and automation coverage."));
            }
        }

        foreach (var coverage in pack.LifecycleCoverage)
        {
            RequireText(coverage.LifecycleEvent, "LifecycleCoverage.LifecycleEvent", "asset-pack.lifecycle-coverage-event-required", issues);
            RequireText(coverage.CapturePolicy, "LifecycleCoverage.CapturePolicy", "asset-pack.lifecycle-capture-policy-required", issues);
            RequireText(coverage.AccountingAutomationStatus, "LifecycleCoverage.AccountingAutomationStatus", "asset-pack.lifecycle-automation-status-required", issues);

            if (!pack.SupportedLifecycleEvents.Contains(coverage.LifecycleEvent, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(Issue(
                    "asset-pack.lifecycle-coverage-unsupported-event",
                    "Critical",
                    "LifecycleCoverage.LifecycleEvent",
                    $"Lifecycle coverage uses unsupported event '{coverage.LifecycleEvent}'."));
            }
        }
    }

    private static void RequireValidationRules(
        AssetPackValidationRules rules,
        List<AssetPackRegistryValidationIssue> issues)
    {
        RequireNonEmpty(rules.RequiredFields, "ValidationRules.RequiredFields", "asset-pack.required-fields-required", issues);
        RequireNonEmpty(rules.ExpectedSchedules, "ValidationRules.ExpectedSchedules", "asset-pack.expected-schedules-required", issues);
        RequireNonEmpty(rules.ToleranceChecks, "ValidationRules.ToleranceChecks", "asset-pack.tolerance-checks-required", issues);
        RequireNonEmpty(rules.IncompatibleCombinations, "ValidationRules.IncompatibleCombinations", "asset-pack.incompatible-combinations-required", issues);
    }

    private static void RequireReportingTaxonomy(
        AssetPackReportingTaxonomy taxonomy,
        List<AssetPackRegistryValidationIssue> issues)
    {
        RequireNonEmpty(taxonomy.AssetClass, "ReportingTaxonomy.AssetClass", "asset-pack.taxonomy-asset-class-required", issues);
        RequireNonEmpty(taxonomy.Liquidity, "ReportingTaxonomy.Liquidity", "asset-pack.taxonomy-liquidity-required", issues);
        RequireNonEmpty(taxonomy.Geography, "ReportingTaxonomy.Geography", "asset-pack.taxonomy-geography-required", issues);
        RequireNonEmpty(taxonomy.Industry, "ReportingTaxonomy.Industry", "asset-pack.taxonomy-industry-required", issues);
        RequireNonEmpty(taxonomy.Risk, "ReportingTaxonomy.Risk", "asset-pack.taxonomy-risk-required", issues);
        RequireNonEmpty(taxonomy.Tax, "ReportingTaxonomy.Tax", "asset-pack.taxonomy-tax-required", issues);
        RequireNonEmpty(taxonomy.CustomClientClassifications, "ReportingTaxonomy.CustomClientClassifications", "asset-pack.taxonomy-custom-required", issues);
    }

    private static void RequireAdmissionPolicy(
        AssetPackAdmissionPolicy policy,
        List<AssetPackRegistryValidationIssue> issues)
    {
        if (!policy.AllowsWideCapture)
        {
            issues.Add(Issue(
                "asset-pack.wide-capture-policy-required",
                "Critical",
                "AdmissionPolicy.AllowsWideCapture",
                "Asset packs must support wide capture before deeper automation is enabled."));
        }

        if (!policy.RequiresJournalTemplateBeforeAutomatedPosting)
        {
            issues.Add(Issue(
                "asset-pack.template-before-posting-policy-required",
                "Critical",
                "AdmissionPolicy.RequiresJournalTemplateBeforeAutomatedPosting",
                "Automated accounting must require a governed journal template before posting candidates are created."));
        }

        RequireText(policy.AccountingPostingPolicy, "AdmissionPolicy.AccountingPostingPolicy", "asset-pack.accounting-posting-policy-required", issues);
        RequireText(policy.EvidencePolicy, "AdmissionPolicy.EvidencePolicy", "asset-pack.evidence-policy-required", issues);
        RequireText(policy.CoreLedgerMutationPolicy, "AdmissionPolicy.CoreLedgerMutationPolicy", "asset-pack.core-ledger-mutation-policy-required", issues);
        RequireNonEmpty(policy.AutomationEscalationTriggers, "AdmissionPolicy.AutomationEscalationTriggers", "asset-pack.automation-escalation-trigger-required", issues);

        if (!policy.AccountingPostingPolicy.Contains("evidence", StringComparison.OrdinalIgnoreCase) &&
            !policy.AccountingPostingPolicy.Contains("rationale", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Issue(
                "asset-pack.posting-policy-evidence-required",
                "Critical",
                "AdmissionPolicy.AccountingPostingPolicy",
                "Accounting posting policy must require retained evidence or explicit operator rationale."));
        }

        if (!policy.CoreLedgerMutationPolicy.Contains("core ledger", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Issue(
                "asset-pack.core-ledger-mutation-policy-reference-required",
                "Critical",
                "AdmissionPolicy.CoreLedgerMutationPolicy",
                "Core ledger mutation policy must explicitly preserve the core ledger boundary."));
        }
    }

    private static void RequireSupportedValues(
        IReadOnlyList<string> values,
        IReadOnlyList<string> supportedValues,
        string target,
        string code,
        List<AssetPackRegistryValidationIssue> issues)
    {
        RequireNonEmpty(values, target, $"{code}.none", issues);
        foreach (var value in values)
        {
            if (!supportedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(Issue(
                    code,
                    "Critical",
                    target,
                    $"'{value}' is not supported by the asset-pack registry contract."));
            }
        }
    }

    private static void RequireText(
        string? value,
        string target,
        string code,
        List<AssetPackRegistryValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(Issue(code, "Critical", target, $"{target} is required."));
        }
    }

    private static void RequireNonEmpty<T>(
        IReadOnlyList<T> values,
        string target,
        string code,
        List<AssetPackRegistryValidationIssue> issues)
    {
        if (values.Count == 0)
        {
            issues.Add(Issue(code, "Critical", target, $"{target} must contain at least one value."));
        }
    }

    private static AssetPackRegistryValidationIssue Issue(
        string code,
        string severity,
        string target,
        string message)
        => new(code, severity, target, message);
}

public enum AssetPackAutomationDepth
{
    WideCapture = 0,
    DeepAccountingAutomation = 1
}

public sealed record SecurityAssetPackDescriptor(
    string PackId,
    string DisplayName,
    // The Security Master asset classes this pack covers TODAY. Every entry must name a class in
    // SecurityAssetClassCatalog: a pack that claims a class the domain cannot represent reports
    // coverage the system does not have, and the operational readiness surface republishes that
    // claim to operators.
    IReadOnlyList<string> AssetClasses,
    // Coverage this pack anticipates but the domain does not model yet — no SecurityKind arm, no
    // terms schema, no validator. Kept separate from AssetClasses so the roadmap information
    // survives without being read as present capability. Entries must NOT name a catalog class;
    // once one lands, it moves into AssetClasses.
    IReadOnlyList<string> PlannedAssetClasses,
    AssetPackContractSchema ContractSchema,
    IReadOnlyList<string> LifecycleEvents,
    IReadOnlyList<string> SupportedLifecycleEvents,
    IReadOnlyList<AssetPackLifecycleCoverage> LifecycleCoverage,
    IReadOnlyList<string> ValuationMethods,
    IReadOnlyList<string> SupportedValuationMethods,
    AssetPackAccountingRules AccountingRules,
    AssetPackValidationRules ValidationRules,
    AssetPackReportingTaxonomy ReportingTaxonomy,
    AssetPackAutomationDepth AutomationDepth,
    AssetPackAdmissionPolicy AdmissionPolicy,
    string LedgerExtensionPolicy);

public sealed record AssetPackAdmissionPolicy(
    bool AllowsWideCapture,
    bool RequiresJournalTemplateBeforeAutomatedPosting,
    string AccountingPostingPolicy,
    string EvidencePolicy,
    string CoreLedgerMutationPolicy,
    IReadOnlyList<string> AutomationEscalationTriggers);

public sealed record AssetPackLifecycleCoverage(
    string LifecycleEvent,
    string CapturePolicy,
    string AccountingAutomationStatus,
    IReadOnlyList<string> JournalTemplateIds);

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
    IReadOnlyList<AssetPackJournalTemplateRule> JournalTemplates,
    IReadOnlyList<string> AccountingBases,
    IReadOnlyList<string> Currencies,
    IReadOnlyList<string> EntityScopes);

public sealed record AssetPackJournalTemplateRule(
    string LifecycleEvent,
    string TemplateId,
    IReadOnlyList<string> AccountingBases,
    IReadOnlyList<string> CurrencyScopes,
    IReadOnlyList<string> EntityScopes);

public sealed record AssetPackRegistryValidationResult(
    bool IsValid,
    IReadOnlyList<AssetPackRegistryValidationIssue> Issues);

public sealed record AssetPackRegistryValidationIssue(
    string Code,
    string Severity,
    string Target,
    string Message);

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
