namespace Meridian.Contracts.SecurityMaster;

public static partial class SecurityAssetPackRegistry
{
    private static readonly IReadOnlyList<MultiAssetCoverageSpecification> SpecializedReadinessSpecifications =
    [
        Listed(
            "Equity",
            "Equities",
            "Listed equity positions require exchange identifiers, quote evidence, tax-lot support, corporate-action coverage, and mapped security-ledger accounts.",
            ["Ticker", "ISIN/CUSIP/FIGI or provider symbol"],
            ["issuer", "share class", "dividend/convertible terms when applicable"],
            ["Quote", "Corporate action", "Position", "Trade", "Cash"],
            "Security position / realized and unrealized P&L / dividend income",
            ["quantity", "market value", "cash", "corporate action", "tax lot"],
            hardBlocker: false),
        Listed(
            "Option",
            "Options",
            "Option readiness depends on OCC identity, underlying linkage, contract economics, exercise/expiry metadata, and derivative ledger classification.",
            ["OCC option symbol", "Underlying SecurityId", "Provider symbol"],
            ["put/call", "strike", "expiry", "multiplier"],
            ["Quote", "Option contract", "Underlying security", "Trade", "Cash"],
            "Derivative asset/liability / premium / realized and unrealized P&L",
            ["quantity", "market value", "cash", "option contract metadata"],
            hardBlocker: true),
        Listed(
            "Future",
            "Futures",
            "Future readiness requires exchange contract metadata, expiry controls, variation margin treatment, and futures-specific reconciliation evidence.",
            ["Root symbol", "Contract month", "Provider symbol"],
            ["expiry", "multiplier", "first notice", "last trade"],
            ["Quote", "Future contract", "Margin", "Trade", "Cash"],
            "Derivative asset/liability / variation margin / realized and unrealized P&L",
            ["quantity", "market value", "cash", "future contract metadata"],
            hardBlocker: true),
        Listed(
            "FxSpot",
            "FX",
            "FX readiness requires currency-pair validation, rate provenance, cash-leg reconciliation, and multi-currency ledger coverage.",
            ["Base currency", "Quote currency", "Provider symbol"],
            ["settlement currency", "trade date", "settlement date"],
            ["FX rate", "Cash", "Trade", "Settlement"],
            "Currency remeasurement / cash / realized and unrealized FX P&L",
            ["FX", "cash", "settlement", "market value"],
            hardBlocker: true),
        Listed(
            "Bond",
            "Fixed income",
            "Fixed income readiness requires debt identifiers, maturity/coupon terms, accruals, amortization policy, and factor/corporate-action evidence when applicable.",
            ["CUSIP/ISIN/FIGI", "Provider symbol"],
            ["maturity", "coupon/spread", "issue date", "call date when applicable"],
            ["Quote", "Accrual", "Cash flow schedule", "Corporate action", "Factor schedule"],
            "Debt security / amortization / interest income / realized and unrealized P&L",
            ["quantity", "market value", "cash", "accrual", "factor schedule"],
            hardBlocker: false),
        Listed(
            "DirectLoan",
            "Private credit / loans",
            "Private-credit readiness requires borrower identity, commitment and obligation schedules, accrual policy, principal paydown events, covenant notices, and direct-lending ledger classification.",
            ["Internal code", "Borrower", "LEI or provider symbol when available"],
            ["borrower", "commitment", "unfunded commitment", "principal", "rate", "maturity", "loan schedule", "covenant", "obligation"],
            ["Loan schedule", "Borrower notice", "Commitment schedule", "Unfunded commitment", "Paydown", "Covenant", "Accrual", "Cash", "Collateral", "Valuation"],
            "Loan receivable / unfunded commitment obligation / interest income / fees / realized and unrealized P&L",
            ["quantity", "market value", "cash", "accrual", "loan schedule", "commitment", "paydown", "obligation"],
            hardBlocker: true),
        Listed(
            "StructuredCredit",
            "Structured credit (MBS / ABS / CLO / CMBS)",
            "The canonical home for securitized products (ADR-022). Structured-credit readiness requires tranche and pool identity, trustee or servicer reporting, factor schedules, collateral tape support, valuation evidence, and cash remittance reconciliation.",
            ["Internal code", "CUSIP/ISIN/FIGI when available", "Provider symbol"],
            ["tranche", "pool/collateral type", "original/current factor", "coupon/index", "factor schedule"],
            ["Trustee report", "Servicer report", "Factor schedule", "Collateral tape", "Dealer pricing", "Valuation source", "Cash remittance"],
            "Structured credit security / factor amortization / interest income / realized and unrealized P&L",
            ["quantity", "market value", "cash remittance", "factor schedule", "collateral tape", "trustee report"],
            hardBlocker: true),
        Listed(
            "PrivateFundInterest",
            "Private fund interests",
            "Private-fund readiness requires GP or sponsor identity, commitment and funded/unfunded balances, retained administrator or GP statements, NAV support, and capital-account evidence.",
            ["Internal code", "LEI or provider symbol when available"],
            ["GP/sponsor", "strategy", "vintage", "commitment", "funded/unfunded", "NAV date", "lockup"],
            ["Administrator statement", "GP statement", "Capital call", "Distribution notice", "NAV statement", "Capital account schedule"],
            "Private fund interest / capital call receivable-payable / distribution income / NAV adjustment",
            ["commitment", "funded", "unfunded", "NAV", "capital call", "distribution", "capital account"],
            hardBlocker: true),
        Listed(
            "PrivateCompanyEquity",
            "Private company equity",
            "Private-company equity readiness requires issuer and share-class terms, cap-table or transfer-agent evidence, financing-round documentation, valuation support, and transaction evidence.",
            ["Internal code", "LEI/CIK or provider symbol when available"],
            ["issuer", "share class", "round", "ownership %", "cost basis", "latest valuation", "restrictions"],
            ["Cap table", "Transfer-agent evidence", "Financing documents", "Share-class documents", "Valuation memo", "409A", "Transaction evidence", "Exit evidence", "Dividend evidence"],
            "Private company equity / cost basis / valuation adjustment / dividend and realized gain/loss",
            ["ownership", "market value", "cost basis", "cap table", "valuation", "transaction", "restriction"],
            hardBlocker: true),
        Listed(
            "RealEstateHolding",
            "Real estate holdings",
            "Real-estate readiness requires property identity, ownership and appraisal terms, property-manager evidence, rent-roll and lease schedules, debt-service evidence, and SPV ownership support.",
            ["Internal code", "LEI or provider symbol when available"],
            ["property type", "market/address", "ownership %", "appraisal value/date", "debt stack", "sponsor"],
            ["Property manager statement", "Rent roll", "Lease schedule", "Appraisal", "Debt-service statement", "Ownership evidence", "SPV evidence"],
            "Real estate holding / rental income / appraisal adjustment / debt-service and ownership accounting",
            ["ownership", "market value", "cash", "rent roll", "lease", "appraisal", "debt service", "SPV"],
            hardBlocker: true),
        Listed(
            "CommitmentGuarantee",
            "Commitments and guarantees",
            "Commitment and guarantee readiness requires counterparty or beneficiary terms, committed and exposure amounts, effective/expiry dates, fee schedules, covenant evidence, and release controls.",
            ["Internal code", "LEI or provider symbol when available"],
            ["counterparty/beneficiary", "committed/guaranteed amount", "unfunded/exposure amount", "effective/expiry dates", "fee/rate", "collateral/covenants"],
            ["Commitment agreement", "Guarantee agreement", "Draw notice", "Usage notice", "Fee schedule", "Accrual schedule", "Collateral evidence", "Covenant evidence", "Release evidence", "Expiry evidence"],
            "Commitment or guarantee exposure / fee accrual / contingent obligation / release accounting",
            ["commitment", "guarantee", "exposure", "fee accrual", "draw", "usage", "collateral", "covenant", "release"],
            hardBlocker: true),
        Listed(
            "CustomAsset",
            "Profile-backed private / other assets",
            "The home for private and other assets with no first-class kind; securitized products (MBS/ABS/CLO/CMBS) belong in StructuredCredit — via a reclassifying profile where profile-backed (ADR-022). Requires governed custom profiles, servicer/trustee reports, factor or NAV evidence, obligation events, valuation approval, and profile-aware ledger classification.",
            ["Internal code", "CUSIP/ISIN/FIGI when available", "Provider symbol"],
            ["approved profile", "profile version", "required profile fields", "valuation date", "servicer/trustee cut-off date", "obligation event"],
            ["Custom profile", "Servicer report", "Trustee report", "Warehouse tape", "Factor schedule", "Dealer pricing", "NAV", "Capital call", "Distribution notice", "Cash", "Collateral", "Obligation schedule"],
            "Profile-derived classification / valuation adjustment / income accrual / commitment and obligation accounting",
            ["quantity", "market value", "cash", "factor schedule", "NAV", "servicer report", "trustee report", "capital call", "distribution", "obligation", "custom-profile evidence"],
            hardBlocker: true),
        Listed(
            "OtherSecurity",
            "Governed other securities",
            "OtherSecurity records must carry category and optional governed profile evidence before accounting or close workflows can rely on them.",
            ["Internal code", "Provider symbol", "Ticker/FIGI when available"],
            ["category", "approved profile when used", "ledger classification"],
            ["Custom profile", "Quote", "Cash", "Valuation"],
            "Governed profile-derived classification / valuation / realized and unrealized P&L",
            ["quantity", "market value", "cash", "custom-profile evidence"],
            hardBlocker: true)
    ];

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<AssetClassDepthTargetSpec>> ReadinessDepthTargets =
        new Dictionary<string, IReadOnlyList<AssetClassDepthTargetSpec>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Bond"] =
            [
                ProviderTarget("factor-corporate-action-evidence", "FactorCorporateActionEvidence", "Factor and corporate-action evidence")
            ],
            ["DirectLoan"] =
            [
                ProviderTarget("loan-schedule-evidence", "LoanScheduleEvidence", "Loan schedule and borrower notices"),
                ProviderTarget("commitment-covenant-evidence", "CommitmentCovenantEvidence", "Commitment, unfunded commitment, and covenant evidence"),
                LedgerTarget("paydown-obligation-ledger", "PaydownObligationLedger", "Paydown and obligation ledger support", "LoanAccountingProjector"),
                LedgerTarget("direct-lending-rule-kernel", "DirectLendingRuleKernel", "Direct-lending F# rule kernel evidence", "Meridian.FSharp.DirectLending.Aggregates")
            ],
            ["StructuredCredit"] =
            [
                ProviderTarget("trustee-servicer-remittance", "StructuredCreditTrusteeEvidence", "Trustee, servicer, and cash remittance evidence"),
                ProviderTarget("factor-schedule", "FactorScheduleEvidence", "Factor schedule evidence"),
                ProviderTarget("collateral-tape", "StructuredCollateralTape", "Collateral tape evidence"),
                ProviderTarget("valuation-source", "StructuredValuationEvidence", "Dealer or valuation-source evidence")
            ],
            ["PrivateFundInterest"] =
            [
                ProviderTarget("administrator-gp-statement", "FundAdministratorStatement", "Administrator or GP statement"),
                ProviderTarget("capital-call-distribution", "CapitalCallDistributionEvidence", "Capital call and distribution notice evidence"),
                ProviderTarget("nav-statement", "PrivateFundNavEvidence", "NAV statement evidence"),
                ProviderTarget("capital-account-schedule", "CapitalAccountScheduleEvidence", "Capital account schedule evidence")
            ],
            ["PrivateCompanyEquity"] =
            [
                ProviderTarget("cap-table", "CapTableEvidence", "Cap table or transfer-agent evidence"),
                ProviderTarget("financing-share-class", "FinancingShareClassEvidence", "Financing and share-class documents"),
                ProviderTarget("valuation", "PrivateCompanyValuationEvidence", "Valuation memo or 409A evidence"),
                ProviderTarget("transaction-exit-dividend", "TransactionExitDividendEvidence", "Transaction, exit, and dividend evidence")
            ],
            ["RealEstateHolding"] =
            [
                ProviderTarget("property-manager", "PropertyManagerEvidence", "Property manager statement evidence"),
                ProviderTarget("rent-roll-lease", "RentRollLeaseEvidence", "Rent roll and lease schedule evidence"),
                ProviderTarget("appraisal", "RealEstateAppraisalEvidence", "Appraisal evidence"),
                ProviderTarget("debt-service-ownership", "DebtServiceOwnershipEvidence", "Debt-service and ownership/SPV evidence")
            ],
            ["CommitmentGuarantee"] =
            [
                ProviderTarget("agreement", "CommitmentAgreementEvidence", "Commitment or guarantee agreement"),
                ProviderTarget("draw-usage", "DrawUsageNoticeEvidence", "Draw or usage notice evidence"),
                ProviderTarget("fee-accrual", "FeeAccrualScheduleEvidence", "Fee and accrual schedule evidence"),
                ProviderTarget("collateral-covenant", "CollateralCovenantEvidence", "Collateral and covenant evidence"),
                ProviderTarget("release-expiry", "ReleaseExpiryEvidence", "Release or expiry evidence")
            ],
            ["CustomAsset"] =
            [
                new("profile-lineage", "AssetProfileLineage", "Approved profile lineage", AssetClassDepthRouteKind.AssetProfiles, "SecurityMaster", "Governance", "SecurityAssetProfileGovernanceService"),
                ProviderTarget("servicer-trustee-evidence", "ServicerTrusteeEvidence", "Servicer, trustee, warehouse, and factor evidence"),
                ProviderTarget("valuation-nav-evidence", "StructuredValuationEvidence", "NAV, dealer pricing, capital call, and distribution evidence"),
                new("obligation-close-evidence", "ObligationCloseEvidence", "Obligation schedule and close-readiness evidence", AssetClassDepthRouteKind.Close, "CloseReadiness", "ProviderEvidence", "FundAccountCloseReadinessService", FallbackEvidenceToProvider: true, UseCloseEvidenceStatus: true)
            ]
        };

    /// <summary>
    /// Every modeled class appears in readiness. Classes without a specialized operational profile
    /// use a conservative, review-gated specification derived from their pack and terms schema.
    /// </summary>
    public static IReadOnlyList<MultiAssetCoverageSpecification> ReadinessSpecifications => Readiness.Value;

    private static readonly Lazy<IReadOnlyList<MultiAssetCoverageSpecification>> Readiness = new(BuildReadinessSpecifications);

    private static IReadOnlyList<MultiAssetCoverageSpecification> BuildReadinessSpecifications()
    {
        var specialized = SpecializedReadinessSpecifications.ToDictionary(static spec => spec.AssetClass, StringComparer.OrdinalIgnoreCase);
        foreach (var assetClass in specialized.Keys.Concat(ReadinessDepthTargets.Keys))
        {
            if (!SecurityAssetClassCatalog.AssetClasses.Contains(assetClass, StringComparer.Ordinal))
                throw new InvalidOperationException($"Readiness metadata names non-canonical asset class '{assetClass}'.");
        }

        return SecurityAssetClassCatalog.All.Select(descriptor =>
            specialized.TryGetValue(descriptor.AssetClass, out var spec)
                ? spec
                : DefaultReadinessSpecification(descriptor)).ToArray();
    }

    /// <summary>Canonical classification for requests and evidence; aliases share catalog ownership.</summary>
    public static string? ResolveAssetClass(string? label)
    {
        var resolution = SecurityAssetClassCatalog.ResolveLabel(label, out var descriptor);
        if (resolution == SecurityAssetClassLabelResolution.Resolved)
            return descriptor!.AssetClass;
        if (resolution == SecurityAssetClassLabelResolution.Ambiguous || string.IsNullOrWhiteSpace(label))
            return null;
        var token = new string(label.Where(char.IsLetterOrDigit).ToArray());
        return ReadinessAliases.TryGetValue(token, out var assetClass) ? assetClass : null;
    }

    // Legacy operational vocabulary stays scoped to readiness; broad labels such as "Debt" or
    // "SPV" must never classify securities in accounting, import, or canonical terms mapping.
    private static readonly IReadOnlyDictionary<string, string> ReadinessAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FixedIncome"] = "Bond",
            ["FixedIncomeSecurity"] = "Bond",
            ["Debt"] = "Bond",
            ["PrivateCredit"] = "DirectLoan",
            ["StructuredProduct"] = "StructuredCredit",
            ["PrivateAsset"] = "CustomAsset",
            ["PrivateEquity"] = "PrivateCompanyEquity",
            ["PrivateCompany"] = "PrivateCompanyEquity",
            ["VentureCapital"] = "PrivateCompanyEquity",
            ["RealEstate"] = "RealEstateHolding",
            ["RealEstateInterest"] = "RealEstateHolding",
            ["Property"] = "RealEstateHolding",
            ["SPV"] = "RealEstateHolding",
            ["UnfundedCommitment"] = "CommitmentGuarantee",
            ["Guarantee"] = "CommitmentGuarantee",
            ["CreditFacility"] = "CommitmentGuarantee",
            ["Commitment"] = "CommitmentGuarantee",
            ["Other"] = "OtherSecurity",
            ["FX"] = "FxSpot",
            ["ForeignExchange"] = "FxSpot",
            ["Currency"] = "FxSpot"
        };

    private static MultiAssetCoverageSpecification DefaultReadinessSpecification(SecurityAssetClassDescriptor descriptor)
    {
        var packs = FindByAssetClass(descriptor.AssetClass);
        return new(
            descriptor.AssetClass,
            SecurityAssetClassRegistry.Humanize(descriptor.AssetClass),
            $"{descriptor.AssetClass} requires retained terms, provider evidence, governed ledger mapping, and reconciliation before operational readiness can be established.",
            descriptor.PreferredIdentifierKinds.Select(static kind => kind.ToString()).ToArray(),
            SecurityAssetTermsSchema.WritableFields(descriptor.AssetClass).Select(static field => field.Key).ToArray(),
            ["Position", "Trade", "Cash", "Valuation", .. (descriptor.SupportsCashflowScheduleByDefault ? new[] { "Cash flow schedule" } : Array.Empty<string>())],
            $"Governed {descriptor.AssetClass} ledger mapping ({string.Join(", ", packs.Select(static pack => pack.DisplayName))})",
            ["quantity", "market value", "cash"],
            HardBlocker: true,
            RequiresOperationalProfileReview: true);
    }

    public static string ReadinessProjectorsFor(string assetClass)
        => assetClass switch
        {
            "Bond" => "DailyPortfolioPricingProjector, FixedIncomeAmortizationProjector, SecurityMasterAccountingEventService",
            "DirectLoan" => "Meridian.FSharp.DirectLending.Aggregates, LoanAccountingProjector, FixedIncomeAmortizationProjector, SecurityMasterAccountingEventService",
            "StructuredCredit" => "DailyPortfolioPricingProjector, FixedIncomeAmortizationProjector, SecurityMasterAccountingEventService",
            "PrivateFundInterest" => "DailyPortfolioPricingProjector, Security Master accounting-event services, FundAccountCloseReadinessService",
            "PrivateCompanyEquity" => "DailyPortfolioPricingProjector, Security Master accounting-event services",
            "RealEstateHolding" => "DailyPortfolioPricingProjector, Security Master accounting-event services, FundAccountCloseReadinessService",
            "CommitmentGuarantee" => "Security Master accounting-event services, FundAccountCloseReadinessService",
            "FxSpot" => "MultiCurrency remeasurement projectors",
            "Option" or "Future" => "DailyPortfolioPricingProjector, LedgerTaxLotReliefProjector",
            "CustomAsset" or "OtherSecurity" => "DailyPortfolioPricingProjector, Security Master accounting-event services",
            "Equity" => "DailyPortfolioPricingProjector, LedgerTaxLotReliefProjector",
            _ => "Asset-specific ledger mapping requires operational profile review"
        };

    private static MultiAssetCoverageSpecification Listed(
        string assetClass,
        string displayName,
        string summary,
        IReadOnlyList<string> requiredIdentifiers,
        IReadOnlyList<string> economicTerms,
        IReadOnlyList<string> providerFeeds,
        string ledgerClassification,
        IReadOnlyList<string> reconciliationSignals,
        bool hardBlocker)
        => new(
            assetClass,
            displayName,
            summary,
            requiredIdentifiers,
            economicTerms,
            providerFeeds,
            ledgerClassification,
            reconciliationSignals,
            hardBlocker);

    private static AssetClassDepthTargetSpec ProviderTarget(
        string keySuffix,
        string targetType,
        string label)
        => new(
            keySuffix,
            targetType,
            label,
            AssetClassDepthRouteKind.Provider,
            "ProviderEvidence",
            "ProviderEvidence",
            "ProviderLedgerReconciliation");

    private static AssetClassDepthTargetSpec LedgerTarget(
        string keySuffix,
        string targetType,
        string label,
        string source)
        => new(
            keySuffix,
            targetType,
            label,
            AssetClassDepthRouteKind.Ledger,
            "Ledger",
            "Ledger",
            source,
            FallbackEvidenceToProvider: true);
}

public sealed record MultiAssetCoverageSpecification(
    string AssetClass,
    string DisplayName,
    string Summary,
    IReadOnlyList<string> RequiredIdentifiers,
    IReadOnlyList<string> EconomicTerms,
    IReadOnlyList<string> ProviderFeeds,
    string LedgerClassification,
    IReadOnlyList<string> ReconciliationSignals,
    bool HardBlocker,
    bool RequiresOperationalProfileReview = false);

public enum AssetClassDepthRouteKind
{
    Provider,
    Ledger,
    Close,
    AssetProfiles
}

public sealed record AssetClassDepthTargetSpec(
    string KeySuffix,
    string TargetType,
    string Label,
    AssetClassDepthRouteKind RouteKind,
    string EvidenceCategory,
    string RequirementCategory,
    string Source,
    bool FallbackEvidenceToProvider = false,
    bool UseCloseEvidenceStatus = false);
