namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Contract-owned metadata for canonical corporate-action event types. The canonical
/// name (see <see cref="CorporateActionEventTypes"/>) remains the compact wire value;
/// this catalog carries the ISO 15022 alignment, provider aliases, required fields,
/// asset-class validity, and downstream behavior consumers need for validation,
/// price adjustment, ledger posting, and workbench workflows.
/// </summary>
public static class CorporateActionTypeDescriptorCatalog
{
    private static readonly IReadOnlyList<CorporateActionTypeDescriptor> Descriptors =
    [
        new(
            CanonicalName: CorporateActionEventTypes.Dividend,
            CaevCode: "DVCA",
            DisplayName: "Cash dividend",
            Description: "Regular cash dividend paid per share in the instrument's currency.",
            Aliases: ["CashDividend", "DividendPayment", "DIV", "DVCA"],
            ValidSecurityMasterAssetClasses: ["Equity", "MoneyMarketFund", "PrivateCompanyEquity", "PrivateFundInterest"],
            RequiredFields: [CorporateActionField.DividendPerShare, CorporateActionField.Currency],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.CashDistribution,
            LedgerPostingKind: CorporateActionLedgerPostingKind.DividendDeclaration,
            IsPriceAffecting: true,
            LedgerBehaviorHints: ["dividend income", "cash receivable at pay date"]),
        new(
            CanonicalName: CorporateActionEventTypes.SpecialDividend,
            CaevCode: "DVCA",
            DisplayName: "Special dividend",
            Description: "Extraordinary cash dividend with tax treatment distinct from regular dividends.",
            Aliases: ["SpecialCashDividend", "ExtraordinaryDividend"],
            ValidSecurityMasterAssetClasses: ["Equity", "PrivateCompanyEquity"],
            RequiredFields: [CorporateActionField.DividendPerShare, CorporateActionField.Currency],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.CashDistribution,
            LedgerPostingKind: CorporateActionLedgerPostingKind.DividendDeclaration,
            IsPriceAffecting: true,
            LedgerBehaviorHints: ["special dividend income", "tax-distinct distribution"]),
        new(
            CanonicalName: CorporateActionEventTypes.ReturnOfCapital,
            CaevCode: "CAPD",
            DisplayName: "Return of capital",
            Description: "Capital distribution that reduces cost basis; tax-distinct from dividend income.",
            Aliases: ["CapitalDistribution", "CAPD", "ROC"],
            ValidSecurityMasterAssetClasses: ["Equity", "PrivateFundInterest", "PrivateCompanyEquity"],
            RequiredFields: [CorporateActionField.DividendPerShare, CorporateActionField.Currency],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.CashDistribution,
            LedgerPostingKind: CorporateActionLedgerPostingKind.DividendDeclaration,
            IsPriceAffecting: true,
            LedgerBehaviorHints: ["cost-basis reduction", "capital distribution, not income"]),
        new(
            CanonicalName: CorporateActionEventTypes.StockSplit,
            CaevCode: "SPLF",
            DisplayName: "Stock split",
            Description: "Forward stock split; SplitRatio is strictly greater than 1.",
            Aliases: ["Split", "ForwardSplit", "SPLF"],
            ValidSecurityMasterAssetClasses: ["Equity", "PrivateCompanyEquity"],
            RequiredFields: [CorporateActionField.SplitRatio],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.PriceScaling,
            LedgerPostingKind: CorporateActionLedgerPostingKind.SplitMemo,
            IsPriceAffecting: true,
            LedgerBehaviorHints: ["quantity restatement memo", "no cash movement"]),
        new(
            CanonicalName: CorporateActionEventTypes.ReverseStockSplit,
            CaevCode: "SPLR",
            DisplayName: "Reverse stock split",
            Description: "Reverse stock split; SplitRatio is strictly between 0 and 1.",
            Aliases: ["ReverseSplit", "SPLR"],
            ValidSecurityMasterAssetClasses: ["Equity", "PrivateCompanyEquity"],
            RequiredFields: [CorporateActionField.SplitRatio],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.PriceScaling,
            LedgerPostingKind: CorporateActionLedgerPostingKind.SplitMemo,
            IsPriceAffecting: true,
            LedgerBehaviorHints: ["quantity restatement memo", "no cash movement"]),
        new(
            CanonicalName: CorporateActionEventTypes.SpinOff,
            CaevCode: "SOFF",
            DisplayName: "Spin-off",
            Description: "A portion of the issuer is distributed as shares of a new entity.",
            Aliases: ["SOFF", "Demerger"],
            ValidSecurityMasterAssetClasses: ["Equity"],
            RequiredFields: [CorporateActionField.NewSecurityId, CorporateActionField.DistributionRatio],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.PositionTransformation,
            LedgerPostingKind: CorporateActionLedgerPostingKind.Distribution,
            IsPriceAffecting: true,
            LedgerBehaviorHints: ["new position in spun-off entity", "cost-basis allocation"]),
        new(
            CanonicalName: CorporateActionEventTypes.MergerAbsorption,
            CaevCode: "MRGR",
            DisplayName: "Merger",
            Description: "The security is absorbed into an acquirer at an exchange ratio.",
            Aliases: ["Merger", "MRGR", "Acquisition"],
            ValidSecurityMasterAssetClasses: ["Equity", "PrivateCompanyEquity"],
            RequiredFields: [CorporateActionField.AcquirerSecurityId, CorporateActionField.ExchangeRatio],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.PositionTransformation,
            LedgerPostingKind: CorporateActionLedgerPostingKind.Distribution,
            IsPriceAffecting: true,
            LedgerBehaviorHints: ["position exchange into acquirer", "realized P&L on absorption"]),
        new(
            CanonicalName: CorporateActionEventTypes.RightsIssue,
            CaevCode: "RHDI",
            DisplayName: "Rights issue",
            Description: "Existing holders are offered additional shares at a subscription price. ISO 15022 models this as RHDI (distribution) plus EXRI (exercise); the retired RHTS code is accepted as an alias.",
            Aliases: ["Rights", "RightsOffering", "RHDI", "RHTS", "EXRI"],
            ValidSecurityMasterAssetClasses: ["Equity"],
            RequiredFields: [CorporateActionField.SubscriptionPricePerShare, CorporateActionField.RightsPerShare],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.PositionTransformation,
            LedgerPostingKind: CorporateActionLedgerPostingKind.Distribution,
            IsPriceAffecting: true,
            LedgerBehaviorHints: ["rights entitlement", "subscription cash on exercise"]),
        new(
            CanonicalName: CorporateActionEventTypes.TenderOffer,
            CaevCode: "TEND",
            DisplayName: "Tender offer",
            Description: "An offeror proposes to purchase shares, typically at a premium. Offer economics are retained on the domain event; the corporate-action envelope has no offer-price carrier.",
            Aliases: ["Tender", "TEND"],
            ValidSecurityMasterAssetClasses: ["Equity", "Bond"],
            RequiredFields: [],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.None,
            LedgerPostingKind: CorporateActionLedgerPostingKind.None,
            IsPriceAffecting: false,
            LedgerBehaviorHints: ["voluntary election", "no automatic posting"]),
        new(
            CanonicalName: CorporateActionEventTypes.PrincipalPaydown,
            CaevCode: "PRED",
            DisplayName: "Principal paydown",
            Description: "Pro-rata principal repayment via pool-factor reduction on factor-based debt.",
            Aliases: ["FactorPaydown", "FactorReduction", "PRED", "PCAL", "Paydown"],
            ValidSecurityMasterAssetClasses: ["Bond", "StructuredCredit", "DirectLoan"],
            RequiredFields: [CorporateActionField.DistributionRatio],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.None,
            LedgerPostingKind: CorporateActionLedgerPostingKind.FactorPaydown,
            IsPriceAffecting: false,
            LedgerBehaviorHints: ["principal paydown", "factor reduction"]),
        new(
            CanonicalName: CorporateActionEventTypes.BondCall,
            CaevCode: "MCAL",
            DisplayName: "Bond call",
            Description: "Issuer exercises an early-redemption call at a price expressed as percent of par.",
            Aliases: ["Call", "MCAL", "FullCall", "EarlyRedemption"],
            ValidSecurityMasterAssetClasses: ["Bond", "StructuredCredit", "CertificateOfDeposit"],
            RequiredFields: [CorporateActionField.RedemptionPricePercentOfPar],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.None,
            LedgerPostingKind: CorporateActionLedgerPostingKind.RedemptionMemo,
            IsPriceAffecting: false,
            LedgerBehaviorHints: ["early redemption proceeds", "position close-out at call price"]),
        new(
            CanonicalName: CorporateActionEventTypes.BondMaturityRedemption,
            CaevCode: "REDM",
            DisplayName: "Maturity redemption",
            Description: "Principal is redeemed at scheduled maturity at a price expressed as percent of par.",
            Aliases: ["Redemption", "REDM", "MaturityRedemption", "Maturity"],
            ValidSecurityMasterAssetClasses: ["Bond", "TreasuryBill", "CertificateOfDeposit", "CommercialPaper", "StructuredCredit"],
            RequiredFields: [CorporateActionField.RedemptionPricePercentOfPar],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.None,
            LedgerPostingKind: CorporateActionLedgerPostingKind.RedemptionMemo,
            IsPriceAffecting: false,
            LedgerBehaviorHints: ["maturity proceeds", "position close-out at redemption price"]),
        new(
            CanonicalName: CorporateActionEventTypes.SymbolChange,
            CaevCode: "CHAN",
            DisplayName: "Symbol change",
            Description: "Ticker symbol change; reference-data only, no economic effect.",
            Aliases: ["TickerChange", "SymbolRename"],
            ValidSecurityMasterAssetClasses: [],
            RequiredFields: [],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.None,
            LedgerPostingKind: CorporateActionLedgerPostingKind.None,
            IsPriceAffecting: false,
            LedgerBehaviorHints: ["reference-data change only"]),
        new(
            CanonicalName: CorporateActionEventTypes.NameChange,
            CaevCode: "CHAN",
            DisplayName: "Name change",
            Description: "Issuer legal-name change; reference-data only, no economic effect.",
            Aliases: ["Rename", "IssuerNameChange"],
            ValidSecurityMasterAssetClasses: [],
            RequiredFields: [],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.None,
            LedgerPostingKind: CorporateActionLedgerPostingKind.None,
            IsPriceAffecting: false,
            LedgerBehaviorHints: ["reference-data change only"]),
        new(
            CanonicalName: CorporateActionEventTypes.Delisting,
            CaevCode: "DLST",
            DisplayName: "Delisting",
            Description: "The security is removed from exchange listing.",
            Aliases: ["Delist", "DLST"],
            ValidSecurityMasterAssetClasses: ["Equity", "Bond", "Warrant"],
            RequiredFields: [],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.None,
            LedgerPostingKind: CorporateActionLedgerPostingKind.DelistingMemo,
            IsPriceAffecting: false,
            LedgerBehaviorHints: ["trading-status memo", "valuation-source review"]),
        new(
            CanonicalName: CorporateActionEventTypes.FuturesExpiry,
            CaevCode: null,
            DisplayName: "Futures expiry",
            Description: "Futures contract reaches expiry. Internal extension; outside ISO 15022 corporate-action scope.",
            Aliases: ["FutureExpiry", "FuturesExpiration", "FutureExpiration"],
            ValidSecurityMasterAssetClasses: ["Future", "Commodity"],
            RequiredFields: [],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.None,
            LedgerPostingKind: CorporateActionLedgerPostingKind.None,
            IsPriceAffecting: false,
            LedgerBehaviorHints: ["expiry settlement handled by execution lane"]),
        new(
            CanonicalName: CorporateActionEventTypes.OptionContractAdjustment,
            CaevCode: null,
            DisplayName: "Option contract adjustment",
            Description: "OCC-style contract-terms adjustment. Internal extension; outside ISO 15022 corporate-action scope.",
            Aliases: ["OptionAdjustment", "ContractAdjustment"],
            ValidSecurityMasterAssetClasses: ["Option", "Warrant"],
            RequiredFields: [],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.None,
            LedgerPostingKind: CorporateActionLedgerPostingKind.None,
            IsPriceAffecting: false,
            LedgerBehaviorHints: ["contract-terms restatement"]),
        new(
            CanonicalName: CorporateActionEventTypes.CryptoFork,
            CaevCode: null,
            DisplayName: "Crypto fork",
            Description: "Chain fork that may produce a new asset. Internal extension; outside ISO 15022 corporate-action scope.",
            Aliases: ["Fork", "HardFork", "ChainSplit"],
            ValidSecurityMasterAssetClasses: ["CryptoCurrency"],
            RequiredFields: [],
            AdjustmentBehavior: CorporateActionAdjustmentBehavior.None,
            LedgerPostingKind: CorporateActionLedgerPostingKind.None,
            IsPriceAffecting: false,
            LedgerBehaviorHints: ["possible new digital-asset position"])
    ];

    private static readonly IReadOnlyDictionary<string, CorporateActionTypeDescriptor> ByCanonicalName =
        Descriptors.ToDictionary(static descriptor => descriptor.CanonicalName, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, CorporateActionTypeDescriptor> ByCompactToken =
        BuildTokenIndex();

    public static IReadOnlyList<CorporateActionTypeDescriptor> All => Descriptors;

    public static CorporateActionTypeDescriptor? Find(string? canonicalName) =>
        canonicalName is not null && ByCanonicalName.TryGetValue(canonicalName, out var descriptor)
            ? descriptor
            : null;

    public static bool TryGet(string canonicalName, out CorporateActionTypeDescriptor descriptor) =>
        ByCanonicalName.TryGetValue(canonicalName, out descriptor!);

    /// <summary>
    /// Alias-tolerant resolution: canonical names, provider aliases, and CAEV codes are all
    /// accepted, compared on the letters-and-digits compact token so spacing, casing, and
    /// punctuation variants resolve to the same descriptor.
    /// </summary>
    public static bool TryNormalize(string? rawEventType, out CorporateActionTypeDescriptor descriptor)
    {
        descriptor = null!;
        if (string.IsNullOrWhiteSpace(rawEventType))
        {
            return false;
        }

        return ByCompactToken.TryGetValue(CompactToken(rawEventType), out descriptor!);
    }

    public static IReadOnlyList<CorporateActionTypeDescriptor> FindByAssetClass(string? assetClass)
    {
        if (string.IsNullOrWhiteSpace(assetClass))
        {
            return [];
        }

        var normalizedAssetClass = assetClass.Trim();
        return Descriptors
            .Where(descriptor => descriptor.AppliesToAssetClass(normalizedAssetClass))
            .ToArray();
    }

    private static Dictionary<string, CorporateActionTypeDescriptor> BuildTokenIndex()
    {
        var index = new Dictionary<string, CorporateActionTypeDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in Descriptors)
        {
            AddToken(index, descriptor.CanonicalName, descriptor);
            foreach (var alias in descriptor.Aliases)
            {
                AddToken(index, alias, descriptor);
            }
        }

        return index;
    }

    private static void AddToken(
        Dictionary<string, CorporateActionTypeDescriptor> index,
        string value,
        CorporateActionTypeDescriptor descriptor)
    {
        var token = CompactToken(value);
        if (token.Length == 0)
        {
            throw new InvalidOperationException(
                $"Corporate-action descriptor '{descriptor.CanonicalName}' declares a blank alias.");
        }

        if (index.TryGetValue(token, out var existing) && !ReferenceEquals(existing, descriptor))
        {
            throw new InvalidOperationException(
                $"Corporate-action alias token '{token}' is claimed by both '{existing.CanonicalName}' and '{descriptor.CanonicalName}'.");
        }

        index[token] = descriptor;
    }

    internal static string CompactToken(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}

/// <summary>
/// Metadata for one canonical corporate-action event type. <see cref="CanonicalName"/> is the
/// stored wire value; <see cref="CaevCode"/> is ISO 15022 alignment metadata only (not unique —
/// e.g. regular and special dividends both map to DVCA) and null for internal-only extensions.
/// An empty <see cref="ValidSecurityMasterAssetClasses"/> list means the event type is valid for
/// every asset class.
/// </summary>
public sealed record CorporateActionTypeDescriptor(
    string CanonicalName,
    string? CaevCode,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> ValidSecurityMasterAssetClasses,
    IReadOnlyList<CorporateActionField> RequiredFields,
    CorporateActionAdjustmentBehavior AdjustmentBehavior,
    CorporateActionLedgerPostingKind LedgerPostingKind,
    bool IsPriceAffecting,
    IReadOnlyList<string> LedgerBehaviorHints)
{
    public bool AppliesToAssetClass(string? assetClass)
        => ValidSecurityMasterAssetClasses.Count == 0
           || (!string.IsNullOrWhiteSpace(assetClass)
               && ValidSecurityMasterAssetClasses.Contains(assetClass.Trim(), StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// The <see cref="CorporateActionDto"/> fields a descriptor can require at append time.
/// </summary>
public enum CorporateActionField
{
    DividendPerShare,
    Currency,
    SplitRatio,
    NewSecurityId,
    DistributionRatio,
    AcquirerSecurityId,
    ExchangeRatio,
    SubscriptionPricePerShare,
    RightsPerShare,
    PayDate,
    RedemptionPricePercentOfPar
}

/// <summary>
/// How historical-price and open-position adjustment treats the event type.
/// </summary>
public enum CorporateActionAdjustmentBehavior
{
    None,
    PriceScaling,
    CashDistribution,
    PositionTransformation
}

/// <summary>
/// Which ledger posting the Security Master ledger bridge produces for the event type.
/// </summary>
public enum CorporateActionLedgerPostingKind
{
    None,
    DividendDeclaration,
    SplitMemo,
    Distribution,
    FactorPaydown,
    RedemptionMemo,
    DelistingMemo
}
