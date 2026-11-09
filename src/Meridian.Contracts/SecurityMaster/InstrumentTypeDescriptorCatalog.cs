using Meridian.Contracts.Domain.Enums;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Contract-owned metadata for market-data instrument types. The enum remains the compact
/// wire value; this catalog carries the operational semantics consumers need for routing,
/// validation, lifecycle, ledger, and risk workflows.
/// </summary>
public static class InstrumentTypeDescriptorCatalog
{
    private static readonly IReadOnlyList<InstrumentTypeDescriptor> Descriptors =
    [
        new(
            InstrumentType: InstrumentType.Equity,
            DisplayName: "Equity",
            SecurityMasterAssetClass: "Equity",
            SecurityMasterAssetFamily: "CommonEquity",
            SecurityMasterSubType: "CommonShare",
            DefaultProviderSecurityType: "STK",
            IsTradeable: true,
            IsReferenceOnly: false,
            IsDerivative: false,
            RequiresUnderlying: false,
            ProducesCashFlows: true,
            RequiresLotTracking: true,
            SettlementModel: "Cash security settlement with dividend and corporate-action events.",
            CompatibleSecurityMasterAssetClasses: ["Equity", "MoneyMarketFund"],
            PreferredIdentifierKinds: ["Ticker", "ISIN", "CUSIP", "FIGI", "SEDOL", "RIC", "ProviderSymbol"],
            RequiredEconomicTerms: ["issuer", "share class", "currency", "primary exchange", "trading status"],
            ProviderCapabilities: ["Quote", "Trade", "Position", "CorporateAction"],
            LifecycleEvents: ["Purchase", "Sale", "Dividend", "Split", "Merger", "Delisting"],
            ValidationRules: ["primary identifier present", "listing venue known", "active quote source retained", "corporate-action source retained"],
            LedgerBehaviorHints: ["security position", "dividend income", "realized and unrealized P&L", "tax-lot tracking"],
            RiskModelHints: ["equity exposure", "issuer concentration", "country and sector classification"]),
        new(
            InstrumentType: InstrumentType.EquityOption,
            DisplayName: "Equity option",
            SecurityMasterAssetClass: "Option",
            SecurityMasterAssetFamily: "ListedDerivative",
            SecurityMasterSubType: "OptionContract",
            DefaultProviderSecurityType: "OPT",
            IsTradeable: true,
            IsReferenceOnly: false,
            IsDerivative: true,
            RequiresUnderlying: true,
            ProducesCashFlows: false,
            RequiresLotTracking: true,
            SettlementModel: "Listed option premium settlement with exercise, assignment, and expiry handling.",
            CompatibleSecurityMasterAssetClasses: ["Option"],
            PreferredIdentifierKinds: ["OCC", "Ticker", "FIGI", "ProviderSymbol", "InternalCode"],
            RequiredEconomicTerms: ["underlying security", "put or call", "strike", "expiry", "multiplier", "exercise style"],
            ProviderCapabilities: ["Quote", "Trade", "Position", "Greeks", "Exercise"],
            LifecycleEvents: ["Purchase", "Sale", "Exercise", "Assignment", "Expiry"],
            ValidationRules: ["underlying security resolved", "expiry and strike present", "multiplier present", "option style retained"],
            LedgerBehaviorHints: ["option premium", "derivative asset or liability", "exercise assignment posting", "realized and unrealized P&L"],
            RiskModelHints: ["delta", "gamma", "theta", "vega", "expiry bucket"]),
        new(
            InstrumentType: InstrumentType.IndexOption,
            DisplayName: "Index option",
            SecurityMasterAssetClass: "Option",
            SecurityMasterAssetFamily: "ListedDerivative",
            SecurityMasterSubType: "OptionContract",
            DefaultProviderSecurityType: "OPT",
            IsTradeable: true,
            IsReferenceOnly: false,
            IsDerivative: true,
            RequiresUnderlying: true,
            ProducesCashFlows: false,
            RequiresLotTracking: true,
            SettlementModel: "Cash-settled listed option settlement against an index reference.",
            CompatibleSecurityMasterAssetClasses: ["Option"],
            PreferredIdentifierKinds: ["OCC", "Ticker", "FIGI", "ProviderSymbol", "InternalCode"],
            RequiredEconomicTerms: ["underlying index", "put or call", "strike", "expiry", "multiplier", "settlement style"],
            ProviderCapabilities: ["Quote", "Trade", "Position", "Greeks", "CashSettlement"],
            LifecycleEvents: ["Purchase", "Sale", "Expiry", "CashSettlement"],
            ValidationRules: ["underlying index resolved", "cash-settlement style present", "expiry and strike present", "multiplier present"],
            LedgerBehaviorHints: ["option premium", "cash-settled derivative P&L", "expiry settlement"],
            RiskModelHints: ["index exposure", "delta", "gamma", "expiry bucket", "volatility surface"]),
        new(
            InstrumentType: InstrumentType.Future,
            DisplayName: "Future",
            SecurityMasterAssetClass: "Future",
            SecurityMasterAssetFamily: "ListedDerivative",
            SecurityMasterSubType: "FutureContract",
            DefaultProviderSecurityType: "FUT",
            IsTradeable: true,
            IsReferenceOnly: false,
            IsDerivative: true,
            RequiresUnderlying: false,
            ProducesCashFlows: false,
            RequiresLotTracking: true,
            SettlementModel: "Futures margin settlement with daily variation and expiry or delivery.",
            CompatibleSecurityMasterAssetClasses: ["Future", "Commodity"],
            PreferredIdentifierKinds: ["Ticker", "FIGI", "RIC", "ProviderSymbol", "InternalCode"],
            RequiredEconomicTerms: ["contract root", "expiry", "exchange", "multiplier", "tick size", "settlement method"],
            ProviderCapabilities: ["Quote", "Trade", "Position", "Margin", "Expiry"],
            LifecycleEvents: ["Purchase", "Sale", "VariationMargin", "Roll", "Expiry", "Delivery"],
            ValidationRules: ["expiry present", "exchange present", "multiplier present", "margin model retained"],
            LedgerBehaviorHints: ["variation margin", "futures realized and unrealized P&L", "roll tracking"],
            RiskModelHints: ["underlying exposure", "expiry bucket", "margin requirement", "basis risk"]),
        new(
            InstrumentType: InstrumentType.SingleStockFuture,
            DisplayName: "Single stock future",
            SecurityMasterAssetClass: "Future",
            SecurityMasterAssetFamily: "ListedDerivative",
            SecurityMasterSubType: "FutureContract",
            DefaultProviderSecurityType: "SSF",
            IsTradeable: true,
            IsReferenceOnly: false,
            IsDerivative: true,
            RequiresUnderlying: true,
            ProducesCashFlows: false,
            RequiresLotTracking: true,
            SettlementModel: "Futures margin settlement tied to a single equity underlying.",
            CompatibleSecurityMasterAssetClasses: ["Future", "Equity"],
            PreferredIdentifierKinds: ["Ticker", "FIGI", "RIC", "ProviderSymbol", "InternalCode"],
            RequiredEconomicTerms: ["underlying equity", "expiry", "exchange", "multiplier", "tick size"],
            ProviderCapabilities: ["Quote", "Trade", "Position", "Margin", "Expiry"],
            LifecycleEvents: ["Purchase", "Sale", "VariationMargin", "Roll", "Expiry", "Delivery"],
            ValidationRules: ["underlying equity resolved", "expiry present", "exchange present", "multiplier present"],
            LedgerBehaviorHints: ["variation margin", "single-name futures exposure", "roll tracking"],
            RiskModelHints: ["single-name delta", "expiry bucket", "margin requirement"]),
        new(
            InstrumentType: InstrumentType.Forex,
            DisplayName: "FX spot",
            SecurityMasterAssetClass: "FxSpot",
            SecurityMasterAssetFamily: "BankProduct",
            SecurityMasterSubType: "OtherSubType",
            DefaultProviderSecurityType: "CASH",
            IsTradeable: true,
            IsReferenceOnly: false,
            IsDerivative: false,
            RequiresUnderlying: false,
            ProducesCashFlows: false,
            RequiresLotTracking: false,
            SettlementModel: "Currency-pair cash settlement with base and quote currency legs.",
            CompatibleSecurityMasterAssetClasses: ["FxSpot"],
            PreferredIdentifierKinds: ["Ticker", "RIC", "ProviderSymbol", "InternalCode"],
            RequiredEconomicTerms: ["base currency", "quote currency", "settlement convention", "trading venue"],
            ProviderCapabilities: ["Quote", "Trade", "Position", "FXRate"],
            LifecycleEvents: ["Trade", "Settlement", "Rollover"],
            ValidationRules: ["currency pair parsed", "base and quote currency retained", "settlement convention retained"],
            LedgerBehaviorHints: ["cash currency exposure", "realized FX P&L", "cash settlement"],
            RiskModelHints: ["currency exposure", "cross-rate dependency", "settlement risk"]),
        new(
            InstrumentType: InstrumentType.Commodity,
            DisplayName: "Commodity",
            SecurityMasterAssetClass: "Commodity",
            SecurityMasterAssetFamily: "OtherFamily",
            SecurityMasterSubType: "OtherSubType",
            DefaultProviderSecurityType: "CMDTY",
            IsTradeable: true,
            IsReferenceOnly: false,
            IsDerivative: false,
            RequiresUnderlying: false,
            ProducesCashFlows: false,
            RequiresLotTracking: true,
            SettlementModel: "Commodity spot or reference settlement with unit-of-measure controls.",
            CompatibleSecurityMasterAssetClasses: ["Commodity"],
            PreferredIdentifierKinds: ["Ticker", "RIC", "ProviderSymbol", "InternalCode"],
            RequiredEconomicTerms: ["commodity", "unit of measure", "currency", "delivery or reference location"],
            ProviderCapabilities: ["Quote", "Trade", "Position"],
            LifecycleEvents: ["Purchase", "Sale", "Delivery", "Storage"],
            ValidationRules: ["unit of measure retained", "reference location retained", "pricing source retained"],
            LedgerBehaviorHints: ["commodity inventory or exposure", "realized and unrealized P&L"],
            RiskModelHints: ["commodity exposure", "location basis", "storage or delivery risk"]),
        new(
            InstrumentType: InstrumentType.Crypto,
            DisplayName: "Crypto spot",
            SecurityMasterAssetClass: "CryptoCurrency",
            SecurityMasterAssetFamily: "OtherFamily",
            SecurityMasterSubType: "OtherSubType",
            DefaultProviderSecurityType: "CRYPTO",
            IsTradeable: true,
            IsReferenceOnly: false,
            IsDerivative: false,
            RequiresUnderlying: false,
            ProducesCashFlows: false,
            RequiresLotTracking: true,
            SettlementModel: "Digital asset spot settlement with exchange, custodian, and wallet controls.",
            CompatibleSecurityMasterAssetClasses: ["CryptoCurrency"],
            PreferredIdentifierKinds: ["Ticker", "RIC", "ProviderSymbol", "InternalCode"],
            RequiredEconomicTerms: ["asset symbol", "quote currency", "exchange", "custody venue"],
            ProviderCapabilities: ["Quote", "Trade", "Position", "Custody"],
            LifecycleEvents: ["Purchase", "Sale", "Transfer", "Fork", "Airdrop"],
            ValidationRules: ["custody venue retained", "exchange source retained", "quote currency retained"],
            LedgerBehaviorHints: ["digital asset position", "realized and unrealized P&L", "custody transfer"],
            RiskModelHints: ["crypto exposure", "exchange concentration", "custody risk"]),
        new(
            InstrumentType: InstrumentType.Bond,
            DisplayName: "Bond",
            SecurityMasterAssetClass: "Bond",
            SecurityMasterAssetFamily: "CorporateDebt",
            SecurityMasterSubType: "CorporateBond",
            DefaultProviderSecurityType: "BOND",
            IsTradeable: true,
            IsReferenceOnly: false,
            IsDerivative: false,
            RequiresUnderlying: false,
            ProducesCashFlows: true,
            RequiresLotTracking: true,
            SettlementModel: "Fixed-income settlement with face-value lots, coupon, principal, call, and maturity events.",
            CompatibleSecurityMasterAssetClasses: ["Bond", "TreasuryBill", "CommercialPaper", "CertificateOfDeposit", "StructuredCredit"],
            PreferredIdentifierKinds: ["ISIN", "CUSIP", "FIGI", "BBGID", "ProviderSymbol", "InternalCode"],
            RequiredEconomicTerms: ["issuer", "currency", "maturity", "coupon", "day-count", "payment frequency", "face value"],
            ProviderCapabilities: ["Quote", "Position", "CashFlow", "Factor", "Rating"],
            LifecycleEvents: ["Coupon", "PrincipalRepayment", "Maturity", "Call", "Default", "FactorUpdate"],
            ValidationRules: ["maturity present", "coupon terms retained", "face-value lot model retained", "cash-flow source retained"],
            LedgerBehaviorHints: ["amortized cost", "interest accrual", "coupon income", "principal paydown", "fixed-income realized and unrealized P&L"],
            RiskModelHints: ["duration", "spread", "rating", "issuer concentration", "cash-flow profile"]),
        new(
            InstrumentType: InstrumentType.FuturesOption,
            DisplayName: "Futures option",
            SecurityMasterAssetClass: "Option",
            SecurityMasterAssetFamily: "ListedDerivative",
            SecurityMasterSubType: "OptionContract",
            DefaultProviderSecurityType: "FOP",
            IsTradeable: true,
            IsReferenceOnly: false,
            IsDerivative: true,
            RequiresUnderlying: true,
            ProducesCashFlows: false,
            RequiresLotTracking: true,
            SettlementModel: "Option premium settlement against a futures contract underlying.",
            CompatibleSecurityMasterAssetClasses: ["Option", "Future"],
            PreferredIdentifierKinds: ["Ticker", "FIGI", "RIC", "ProviderSymbol", "InternalCode"],
            RequiredEconomicTerms: ["underlying future", "put or call", "strike", "expiry", "multiplier", "exercise style"],
            ProviderCapabilities: ["Quote", "Trade", "Position", "Greeks", "Exercise"],
            LifecycleEvents: ["Purchase", "Sale", "Exercise", "Assignment", "Expiry"],
            ValidationRules: ["underlying future resolved", "expiry and strike present", "multiplier present"],
            LedgerBehaviorHints: ["option premium", "futures option exercise", "derivative P&L"],
            RiskModelHints: ["underlying futures exposure", "delta", "gamma", "expiry bucket"]),
        new(
            InstrumentType: InstrumentType.Index,
            DisplayName: "Index",
            SecurityMasterAssetClass: "OtherSecurity",
            SecurityMasterAssetFamily: "OtherFamily",
            SecurityMasterSubType: "OtherSubType",
            DefaultProviderSecurityType: "IND",
            IsTradeable: false,
            IsReferenceOnly: true,
            IsDerivative: false,
            RequiresUnderlying: false,
            ProducesCashFlows: false,
            RequiresLotTracking: false,
            SettlementModel: "Reference-only index with no trade settlement or owned-position lot model.",
            CompatibleSecurityMasterAssetClasses: ["OtherSecurity"],
            PreferredIdentifierKinds: ["Ticker", "FIGI", "RIC", "ProviderSymbol", "InternalCode"],
            RequiredEconomicTerms: ["index family", "currency", "calculation source", "publication calendar"],
            ProviderCapabilities: ["Quote", "Benchmark", "ReferenceData"],
            LifecycleEvents: ["ConstituentUpdate", "Rebalance", "Publication"],
            ValidationRules: ["reference source retained", "currency retained", "tradeability blocked"],
            LedgerBehaviorHints: ["benchmark context only", "no position posting"],
            RiskModelHints: ["benchmark exposure", "tracking error reference"]),
        new(
            InstrumentType: InstrumentType.CFD,
            DisplayName: "CFD",
            SecurityMasterAssetClass: "Cfd",
            SecurityMasterAssetFamily: "ListedDerivative",
            SecurityMasterSubType: "OtherSubType",
            DefaultProviderSecurityType: "CFD",
            IsTradeable: true,
            IsReferenceOnly: false,
            IsDerivative: true,
            RequiresUnderlying: true,
            ProducesCashFlows: false,
            RequiresLotTracking: true,
            SettlementModel: "Contract-for-difference cash settlement with financing and mark-to-market events.",
            CompatibleSecurityMasterAssetClasses: ["Cfd", "Equity", "Commodity", "FxSpot"],
            PreferredIdentifierKinds: ["Ticker", "RIC", "ProviderSymbol", "InternalCode"],
            RequiredEconomicTerms: ["underlying security", "currency", "financing terms", "margin model"],
            ProviderCapabilities: ["Quote", "Trade", "Position", "Margin", "Financing"],
            LifecycleEvents: ["Purchase", "Sale", "FinancingAccrual", "MarkToMarket"],
            ValidationRules: ["underlying resolved", "margin model retained", "financing terms retained"],
            LedgerBehaviorHints: ["CFD exposure", "financing accrual", "derivative P&L"],
            RiskModelHints: ["underlying exposure", "leverage", "counterparty risk"]),
        new(
            InstrumentType: InstrumentType.Warrant,
            DisplayName: "Warrant",
            SecurityMasterAssetClass: "Warrant",
            SecurityMasterAssetFamily: "ListedDerivative",
            SecurityMasterSubType: "OptionContract",
            DefaultProviderSecurityType: "WAR",
            IsTradeable: true,
            IsReferenceOnly: false,
            IsDerivative: true,
            RequiresUnderlying: true,
            ProducesCashFlows: false,
            RequiresLotTracking: true,
            SettlementModel: "Warrant settlement with issuer, underlying, exercise, and expiry terms.",
            CompatibleSecurityMasterAssetClasses: ["Warrant", "Option", "Equity"],
            PreferredIdentifierKinds: ["Ticker", "ISIN", "CUSIP", "FIGI", "ProviderSymbol", "InternalCode"],
            RequiredEconomicTerms: ["issuer", "underlying security", "strike", "expiry", "conversion ratio"],
            ProviderCapabilities: ["Quote", "Trade", "Position", "Exercise"],
            LifecycleEvents: ["Purchase", "Sale", "Exercise", "Expiry", "IssuerAction"],
            ValidationRules: ["issuer retained", "underlying resolved", "expiry and strike present", "conversion ratio present"],
            LedgerBehaviorHints: ["warrant premium", "exercise conversion", "derivative P&L"],
            RiskModelHints: ["issuer risk", "underlying exposure", "expiry bucket"]),
        new(
            InstrumentType: InstrumentType.Swap,
            DisplayName: "Swap",
            SecurityMasterAssetClass: "Swap",
            SecurityMasterAssetFamily: "ListedDerivative",
            SecurityMasterSubType: "SwapContract",
            DefaultProviderSecurityType: "SWAP",
            IsTradeable: true,
            IsReferenceOnly: false,
            IsDerivative: true,
            RequiresUnderlying: true,
            ProducesCashFlows: true,
            RequiresLotTracking: false,
            SettlementModel: "OTC or listed swap settlement with reset, payment, valuation, and termination events.",
            CompatibleSecurityMasterAssetClasses: ["Swap"],
            PreferredIdentifierKinds: ["InternalCode", "ProviderSymbol", "PermId", "Lei"],
            RequiredEconomicTerms: ["counterparty", "notional", "effective date", "maturity", "leg definitions", "reset schedule"],
            ProviderCapabilities: ["ReferenceData", "Valuation", "CashFlow", "Position"],
            LifecycleEvents: ["EffectiveDate", "Reset", "Payment", "Valuation", "Termination", "Maturity"],
            ValidationRules: ["counterparty retained", "notional retained", "legs defined", "payment schedule retained"],
            LedgerBehaviorHints: ["swap fair value", "reset accrual", "periodic payment", "counterparty exposure"],
            RiskModelHints: ["counterparty risk", "curve exposure", "spread exposure", "notional profile"]),
        new(
            InstrumentType: InstrumentType.DirectLoan,
            DisplayName: "Direct loan",
            SecurityMasterAssetClass: "DirectLoan",
            SecurityMasterAssetFamily: "PrivateLoan",
            SecurityMasterSubType: "DirectLoan",
            DefaultProviderSecurityType: "LOAN",
            IsTradeable: false,
            IsReferenceOnly: false,
            IsDerivative: false,
            RequiresUnderlying: false,
            ProducesCashFlows: true,
            RequiresLotTracking: true,
            SettlementModel: "Private credit settlement with funded commitment, interest, amortization, covenant, and default events.",
            CompatibleSecurityMasterAssetClasses: ["DirectLoan", "StructuredCredit"],
            PreferredIdentifierKinds: ["InternalCode", "ProviderSymbol", "LoanX", "CUSIP", "ISIN"],
            RequiredEconomicTerms: ["borrower", "facility", "commitment", "funded amount", "rate type", "maturity", "covenants"],
            ProviderCapabilities: ["ReferenceData", "CashFlow", "Position", "Covenant", "Valuation"],
            LifecycleEvents: ["Draw", "InterestAccrual", "Repayment", "Amendment", "Covenant", "Default", "Maturity"],
            ValidationRules: ["borrower retained", "facility terms retained", "cash-flow source retained", "covenant source retained"],
            LedgerBehaviorHints: ["loan carrying value", "interest accrual", "principal paydown", "PIK income", "impairment"],
            RiskModelHints: ["borrower concentration", "credit spread", "covenant status", "maturity ladder"]),
        new(
            InstrumentType: InstrumentType.Repo,
            DisplayName: "Repo",
            SecurityMasterAssetClass: "Repo",
            SecurityMasterAssetFamily: "RepurchaseAgreement",
            SecurityMasterSubType: "Repo",
            DefaultProviderSecurityType: "REPO",
            IsTradeable: true,
            IsReferenceOnly: false,
            IsDerivative: false,
            RequiresUnderlying: true,
            ProducesCashFlows: true,
            RequiresLotTracking: true,
            SettlementModel: "Repurchase-agreement financing with collateral, margin, accrual, roll, and maturity events.",
            CompatibleSecurityMasterAssetClasses: ["Repo"],
            PreferredIdentifierKinds: ["InternalCode", "ProviderSymbol", "ISIN", "CUSIP"],
            RequiredEconomicTerms: ["counterparty", "collateral", "repo rate", "start date", "maturity", "haircut"],
            ProviderCapabilities: ["ReferenceData", "Position", "Collateral", "CashFlow"],
            LifecycleEvents: ["Open", "InterestAccrual", "MarginCall", "Substitution", "Roll", "Close", "Maturity"],
            ValidationRules: ["counterparty retained", "collateral retained", "rate retained", "maturity retained"],
            LedgerBehaviorHints: ["financing payable or receivable", "repo interest accrual", "collateral exposure"],
            RiskModelHints: ["counterparty risk", "collateral concentration", "haircut", "rollover risk"]),
        new(
            InstrumentType: InstrumentType.Deposit,
            DisplayName: "Deposit",
            SecurityMasterAssetClass: "Deposit",
            SecurityMasterAssetFamily: "BankProduct",
            SecurityMasterSubType: "DemandDeposit",
            DefaultProviderSecurityType: "DEPOSIT",
            IsTradeable: false,
            IsReferenceOnly: false,
            IsDerivative: false,
            RequiresUnderlying: false,
            ProducesCashFlows: true,
            RequiresLotTracking: true,
            SettlementModel: "Deposit settlement with interest, maturity, renewal, and cash-sweep events.",
            CompatibleSecurityMasterAssetClasses: ["Deposit", "CertificateOfDeposit", "CashSweep"],
            PreferredIdentifierKinds: ["InternalCode", "ProviderSymbol", "ISIN", "CUSIP"],
            RequiredEconomicTerms: ["bank", "currency", "account or deposit id", "rate", "maturity or demand terms"],
            ProviderCapabilities: ["ReferenceData", "Position", "CashFlow", "InterestAccrual"],
            LifecycleEvents: ["Open", "InterestAccrual", "Maturity", "Renewal", "Withdrawal"],
            ValidationRules: ["bank retained", "currency retained", "rate terms retained", "cash-flow source retained"],
            LedgerBehaviorHints: ["cash equivalent", "interest income", "maturity proceeds", "sweep movement"],
            RiskModelHints: ["bank counterparty risk", "liquidity bucket", "cash concentration"])
    ];

    private static readonly IReadOnlyDictionary<InstrumentType, InstrumentTypeDescriptor> ByInstrumentType =
        Descriptors.ToDictionary(static descriptor => descriptor.InstrumentType);

    /// <summary>
    /// The descriptor placeholder for "no specific domain family". It is a descriptor-taxonomy
    /// label, not an F# <c>AssetFamily</c> name: the domain carries an unlisted family as
    /// <c>OtherFamily "value"</c>, stored as the bare caller value.
    /// </summary>
    public const string UnclassifiedAssetFamilyLabel = "OtherFamily";

    /// <summary>The descriptor placeholder for "no specific domain sub-type"; see <see cref="UnclassifiedAssetFamilyLabel"/>.</summary>
    public const string UnclassifiedSubTypeLabel = "OtherSubType";

    /// <summary>
    /// Every descriptor family/sub-type label that is intentionally NOT an F# domain
    /// <c>familyName</c>/<c>subTypeName</c> value. All other descriptor labels map one-to-one onto
    /// the domain label of the same text (see <see cref="ResolveDomainAssetFamilyLabel"/>); a
    /// coverage test fails when a descriptor label is neither. The descriptor values themselves are
    /// unchanged and still surface as passport fallbacks, so this reconciliation changes no output.
    /// <para>
    /// Some matching labels are reviewed approximations rather than identities: Warrant uses
    /// <c>OptionContract</c>, FX spot uses the <c>BankProduct</c> family and Deposit uses
    /// <c>DemandDeposit</c>, because the domain declares no closer case. They are listed in
    /// <see cref="ApproximateDomainLabelMappings"/>.
    /// </para>
    /// </summary>
    public static IReadOnlyList<InstrumentTypeDescriptorLabelException> DomainLabelExceptions { get; } =
    [
        new(InstrumentType.Forex, InstrumentTypeDescriptorLabelField.SubType, UnclassifiedSubTypeLabel,
            "The domain declares no FX spot sub-type; FX spot records carry OtherSubType with a caller value."),
        new(InstrumentType.Commodity, InstrumentTypeDescriptorLabelField.AssetFamily, UnclassifiedAssetFamilyLabel,
            "The domain declares no commodity family."),
        new(InstrumentType.Commodity, InstrumentTypeDescriptorLabelField.SubType, UnclassifiedSubTypeLabel,
            "The domain declares no commodity sub-type."),
        new(InstrumentType.Crypto, InstrumentTypeDescriptorLabelField.AssetFamily, UnclassifiedAssetFamilyLabel,
            "The domain declares no digital-asset family."),
        new(InstrumentType.Crypto, InstrumentTypeDescriptorLabelField.SubType, UnclassifiedSubTypeLabel,
            "The domain declares no digital-asset sub-type."),
        new(InstrumentType.Index, InstrumentTypeDescriptorLabelField.AssetFamily, UnclassifiedAssetFamilyLabel,
            "A reference-only index has no domain security family."),
        new(InstrumentType.Index, InstrumentTypeDescriptorLabelField.SubType, UnclassifiedSubTypeLabel,
            "A reference-only index has no domain security sub-type."),
        new(InstrumentType.CFD, InstrumentTypeDescriptorLabelField.SubType, UnclassifiedSubTypeLabel,
            "The domain declares no contract-for-difference sub-type.")
    ];

    /// <summary>
    /// Descriptor labels that equal a domain label but approximate its meaning (see
    /// <see cref="DomainLabelExceptions"/>). Recorded so the approximation is a reviewed decision
    /// rather than an implied identity; nothing consumes them as classification authority.
    /// </summary>
    public static IReadOnlyList<InstrumentTypeDescriptorLabelException> ApproximateDomainLabelMappings { get; } =
    [
        new(InstrumentType.Warrant, InstrumentTypeDescriptorLabelField.SubType, "OptionContract",
            "The domain declares no warrant sub-type; a warrant is recorded as an option contract."),
        new(InstrumentType.Forex, InstrumentTypeDescriptorLabelField.AssetFamily, "BankProduct",
            "The domain declares no FX family; FX spot is grouped with bank products."),
        new(InstrumentType.Deposit, InstrumentTypeDescriptorLabelField.SubType, "DemandDeposit",
            "A generic deposit descriptor defaults to the demand-deposit sub-type; time deposits use TimeDeposit.")
    ];

    /// <summary>
    /// Maps a descriptor's family label onto the domain family label: the same text, or
    /// <c>null</c> when the descriptor label is a listed <see cref="DomainLabelExceptions"/>
    /// placeholder that names no domain family.
    /// </summary>
    public static string? ResolveDomainAssetFamilyLabel(InstrumentTypeDescriptor descriptor)
        => ResolveDomainLabel(descriptor, InstrumentTypeDescriptorLabelField.AssetFamily, descriptor.SecurityMasterAssetFamily);

    /// <summary>Maps a descriptor's sub-type label onto the domain sub-type label; see <see cref="ResolveDomainAssetFamilyLabel"/>.</summary>
    public static string? ResolveDomainSubTypeLabel(InstrumentTypeDescriptor descriptor)
        => ResolveDomainLabel(descriptor, InstrumentTypeDescriptorLabelField.SubType, descriptor.SecurityMasterSubType);

    private static string? ResolveDomainLabel(
        InstrumentTypeDescriptor descriptor,
        InstrumentTypeDescriptorLabelField field,
        string? label)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        return DomainLabelExceptions.Any(exception =>
                exception.InstrumentType == descriptor.InstrumentType
                && exception.Field == field
                && string.Equals(exception.Label, label, StringComparison.Ordinal))
            ? null
            : label;
    }

    public static IReadOnlyList<InstrumentTypeDescriptor> All => Descriptors;

    public static InstrumentTypeDescriptor? Find(InstrumentType instrumentType) =>
        ByInstrumentType.TryGetValue(instrumentType, out var descriptor) ? descriptor : null;

    public static bool TryGet(InstrumentType instrumentType, out InstrumentTypeDescriptor descriptor) =>
        ByInstrumentType.TryGetValue(instrumentType, out descriptor!);

    /// <summary>
    /// True when a descriptor whose PRIMARY asset class is <paramref name="assetClass"/> declares a
    /// contract multiplier among its required economic terms (listed options and futures; not swaps,
    /// warrants or FX forwards). Compatible-class lists are deliberately not consulted: they let a
    /// class be recorded under a related instrument type (an equity as a single-stock future, a spot
    /// commodity as a future), which must not make every record of that class require a multiplier.
    /// Clients use this instead of matching the asset-class name so they cannot diverge.
    /// </summary>
    public static bool RequiresContractMultiplier(string? assetClass)
    {
        if (string.IsNullOrWhiteSpace(assetClass))
        {
            return false;
        }

        var normalized = assetClass.Trim();
        return Descriptors.Any(descriptor =>
            descriptor.SecurityMasterAssetClass.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            && descriptor.RequiredEconomicTerms.Contains("multiplier", StringComparer.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<InstrumentTypeDescriptor> FindBySecurityMasterAssetClass(string? assetClass)
    {
        if (string.IsNullOrWhiteSpace(assetClass))
        {
            return [];
        }

        var normalizedAssetClass = assetClass.Trim();
        return Descriptors
            .Where(descriptor =>
                descriptor.SecurityMasterAssetClass.Equals(normalizedAssetClass, StringComparison.OrdinalIgnoreCase) ||
                descriptor.CompatibleSecurityMasterAssetClasses.Contains(normalizedAssetClass, StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }
}

/// <summary>Which descriptor classification label an exception or mapping entry describes.</summary>
public enum InstrumentTypeDescriptorLabelField
{
    AssetFamily,
    SubType
}

/// <summary>A reviewed descriptor label whose relationship to the domain label is recorded explicitly.</summary>
public sealed record InstrumentTypeDescriptorLabelException(
    InstrumentType InstrumentType,
    InstrumentTypeDescriptorLabelField Field,
    string Label,
    string Reason);

public sealed record InstrumentTypeDescriptor(
    InstrumentType InstrumentType,
    string DisplayName,
    string SecurityMasterAssetClass,
    string? SecurityMasterAssetFamily,
    string? SecurityMasterSubType,
    string DefaultProviderSecurityType,
    bool IsTradeable,
    bool IsReferenceOnly,
    bool IsDerivative,
    bool RequiresUnderlying,
    bool ProducesCashFlows,
    bool RequiresLotTracking,
    string SettlementModel,
    IReadOnlyList<string> CompatibleSecurityMasterAssetClasses,
    IReadOnlyList<string> PreferredIdentifierKinds,
    IReadOnlyList<string> RequiredEconomicTerms,
    IReadOnlyList<string> ProviderCapabilities,
    IReadOnlyList<string> LifecycleEvents,
    IReadOnlyList<string> ValidationRules,
    IReadOnlyList<string> LedgerBehaviorHints,
    IReadOnlyList<string> RiskModelHints);
