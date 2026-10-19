using Meridian.Contracts.Domain.Enums;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Shared Security Master asset-class metadata used by edit workflows, projections,
/// and workstation capability summaries so asset support does not drift across surfaces.
/// </summary>
public static class SecurityAssetClassCatalog
{
    private static readonly SecurityAssetClassDescriptor DefaultDescriptor = new(
        AssetClass: "Unknown",
        LotUnit: SecurityLotUnitKind.Units,
        CashFlowBehavior: SecurityCashFlowBehavior.None,
        ProjectionKind: SecurityTermsProjectionKind.TermsJsonOnly,
        SupportsBasicCreateWorkflow: false,
        PreferredIdentifierKinds:
        [
            SecurityIdentifierKind.Ticker,
            SecurityIdentifierKind.Isin,
            SecurityIdentifierKind.Cusip,
            SecurityIdentifierKind.Figi,
            SecurityIdentifierKind.ProviderSymbol,
            SecurityIdentifierKind.InternalCode
        ]);

    private static readonly IReadOnlyList<SecurityAssetClassDescriptor> Descriptors =
    [
        new(
            AssetClass: "Equity",
            AssetPackIds: ["public-equity-etf"],
            InstrumentTypes: [InstrumentType.Equity],
            LotUnit: SecurityLotUnitKind.Shares,
            CashFlowBehavior: SecurityCashFlowBehavior.Dividends,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: true,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.Ticker,
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Cusip,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.Sedol,
                SecurityIdentifierKind.Bbgid,
                SecurityIdentifierKind.PermTicker,
                SecurityIdentifierKind.Ric,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.InternalCode
            ],
            // Every Equity asset-specific term is optional, so a name/currency/identifier row
            // carries the whole contract.
            SupportsIdentifierOnlyImport: true),
        new(
            AssetClass: "Option",
            AssetPackIds: ["derivatives-fx"],
            InstrumentTypes: [InstrumentType.EquityOption, InstrumentType.IndexOption, InstrumentType.FuturesOption],
            LotUnit: SecurityLotUnitKind.Contracts,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Specialized,
            LotMultiplierTermKey: "multiplier",
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.OccOptionSymbol,
                SecurityIdentifierKind.Ticker,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.InternalCode
            ]),
        new(
            AssetClass: "Future",
            AssetPackIds: ["derivatives-fx"],
            InstrumentTypes: [InstrumentType.Future, InstrumentType.SingleStockFuture],
            LotUnit: SecurityLotUnitKind.Contracts,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            LotMultiplierTermKey: "multiplier",
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.Ticker,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.Ric,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.InternalCode
            ]),
        new(
            AssetClass: "Bond",
            AssetPackIds: ["fixed-income"],
            SupportsCanonicalBulletAmortization: true,
            InstrumentTypes: [InstrumentType.Bond],
            LotUnit: SecurityLotUnitKind.FaceValue,
            CashFlowBehavior: SecurityCashFlowBehavior.ContractualSchedule,
            ProjectionKind: SecurityTermsProjectionKind.Specialized,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Cusip,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.Bbgid,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.InternalCode
            ],
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.FixedIncome,
            AmortizesTowardPar: true,
            RequiresMaturity: true,
            // Legacy classification spellings that reach the accounting adapter as a sub-type or
            // type name rather than as the canonical asset class.
            Aliases: ["CorporateBond", "MunicipalBond"],
            AccountingInstrumentClass: SecurityAccountingInstrumentClasses.Bond),
        new(
            AssetClass: "FxSpot",
            AssetPackIds: ["derivatives-fx"],
            InstrumentTypes: [InstrumentType.Forex],
            LotUnit: SecurityLotUnitKind.CurrencyAmount,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.Ticker,
                SecurityIdentifierKind.Ric,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.InternalCode
            ]),
        new(
            AssetClass: "Deposit",
            AssetPackIds: ["cash-bank"],
            InstrumentTypes: [InstrumentType.Deposit],
            LotUnit: SecurityLotUnitKind.FaceValue,
            CashFlowBehavior: SecurityCashFlowBehavior.ContractualSchedule,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Cusip
            ]),
        new(
            AssetClass: "MoneyMarketFund",
            AssetPackIds: ["cash-bank"],
            InstrumentTypes: [InstrumentType.Equity],
            LotUnit: SecurityLotUnitKind.Units,
            CashFlowBehavior: SecurityCashFlowBehavior.FundDistributions,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.Ticker,
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Cusip,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.InternalCode
            ]),
        new(
            AssetClass: "CertificateOfDeposit",
            AssetPackIds: ["fixed-income"],
            InstrumentTypes: [InstrumentType.Deposit],
            LotUnit: SecurityLotUnitKind.FaceValue,
            CashFlowBehavior: SecurityCashFlowBehavior.ContractualSchedule,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Cusip,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.InternalCode
            ],
            AmortizesTowardPar: true,
            RequiresMaturity: true,
            AccountingInstrumentClass: SecurityAccountingInstrumentClasses.Bond),
        new(
            AssetClass: "CommercialPaper",
            AssetPackIds: ["fixed-income"],
            SupportsCanonicalBulletAmortization: true,
            InstrumentTypes: [InstrumentType.Bond],
            LotUnit: SecurityLotUnitKind.FaceValue,
            CashFlowBehavior: SecurityCashFlowBehavior.ContractualSchedule,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Cusip,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.InternalCode
            ],
            AmortizesTowardPar: true,
            RequiresMaturity: true,
            AccountingInstrumentClass: SecurityAccountingInstrumentClasses.Bond),
        new(
            AssetClass: "TreasuryBill",
            AssetPackIds: ["fixed-income"],
            SupportsCanonicalBulletAmortization: true,
            InstrumentTypes: [InstrumentType.Bond],
            LotUnit: SecurityLotUnitKind.FaceValue,
            CashFlowBehavior: SecurityCashFlowBehavior.ContractualSchedule,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.Cusip,
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.Bbgid,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.InternalCode
            ],
            AmortizesTowardPar: true,
            RequiresMaturity: true,
            AccountingInstrumentClass: SecurityAccountingInstrumentClasses.Bond),
        new(
            AssetClass: "Repo",
            AssetPackIds: ["fixed-income"],
            InstrumentTypes: [InstrumentType.Repo],
            LotUnit: SecurityLotUnitKind.FaceValue,
            CashFlowBehavior: SecurityCashFlowBehavior.FinancingInterest,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Cusip
            ]),
        new(
            AssetClass: "CashSweep",
            AssetPackIds: ["cash-bank"],
            InstrumentTypes: [InstrumentType.Deposit],
            LotUnit: SecurityLotUnitKind.CurrencyAmount,
            CashFlowBehavior: SecurityCashFlowBehavior.ProviderSupplied,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.Ticker
            ]),
        new(
            AssetClass: "OtherSecurity",
            AssetPackIds: ["controlled-other-asset"],
            RequiresGovernedProfile: true,
            LotUnit: SecurityLotUnitKind.Units,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.Ticker,
                SecurityIdentifierKind.Figi
            ],
            SupportsProfileBackedTerms: true),
        new(
            AssetClass: "CustomAsset",
            AssetPackIds: ["controlled-other-asset"],
            RequiresGovernedProfile: true,
            LotUnit: SecurityLotUnitKind.Units,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.TermsJsonOnly,
            SupportsBasicCreateWorkflow: true,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.Cusip,
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.Lei
            ],
            SupportsProfileBackedTerms: true),
        new(
            AssetClass: "Swap",
            AssetPackIds: ["derivatives-fx"],
            InstrumentTypes: [InstrumentType.Swap],
            LotUnit: SecurityLotUnitKind.Notional,
            CashFlowBehavior: SecurityCashFlowBehavior.SwapLegs,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.PermId,
                SecurityIdentifierKind.Lei,
                SecurityIdentifierKind.ProviderSymbol
            ],
            RequiresMaturity: true),
        new(
            AssetClass: "DirectLoan",
            AssetPackIds: ["private-loan-credit", "mortgage-facility-intercompany"],
            InstrumentTypes: [InstrumentType.DirectLoan],
            LotUnit: SecurityLotUnitKind.FaceValue,
            CashFlowBehavior: SecurityCashFlowBehavior.ContractualSchedule,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.LoanX,
                SecurityIdentifierKind.Clip,
                SecurityIdentifierKind.PermId,
                SecurityIdentifierKind.Lei,
                SecurityIdentifierKind.ProviderSymbol
            ],
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.DirectLending,
            AmortizesTowardPar: true,
            Aliases: ["Loan", "AmortizingLoan"]),
        new(
            AssetClass: "StructuredCredit",
            AssetPackIds: ["fixed-income"],
            LotUnit: SecurityLotUnitKind.FaceValue,
            CashFlowBehavior: SecurityCashFlowBehavior.FactorAmortizing,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.Clip,
                SecurityIdentifierKind.Cusip,
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.ProviderSymbol
            ],
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations,
            AmortizesTowardPar: true,
            // Version 2 added the typed tranche terms (structuredCreditKind, seniority, attachment and
            // detachment points, WAC/WAM/WALA, CLO period dates); every addition is optional, so a
            // version-1 payload reads unchanged and needs no field transformation (ADR-024).
            TermsVersion: 2,
            SupportsProfileBackedTerms: true,
            Aliases: ["MortgageBacked", "MortgageBackedSecurity", "MBS", "AssetBacked", "AssetBackedSecurity", "ABS"],
            AccountingInstrumentClass: SecurityAccountingInstrumentClasses.AssetBackedSecurity),
        new(
            AssetClass: "PrivateFundInterest",
            AssetPackIds: ["private-fund-partnership"],
            LotUnit: SecurityLotUnitKind.Commitment,
            CashFlowBehavior: SecurityCashFlowBehavior.ProviderSupplied,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.Lei,
                SecurityIdentifierKind.ProviderSymbol
            ],
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations,
            SupportsProfileBackedTerms: true),
        new(
            AssetClass: "PrivateCompanyEquity",
            AssetPackIds: ["private-fund-partnership"],
            LotUnit: SecurityLotUnitKind.Shares,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.Lei,
                SecurityIdentifierKind.Cik,
                SecurityIdentifierKind.ProviderSymbol
            ],
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations,
            SupportsProfileBackedTerms: true),
        new(
            AssetClass: "RealEstateHolding",
            AssetPackIds: ["real-estate"],
            LotUnit: SecurityLotUnitKind.Units,
            CashFlowBehavior: SecurityCashFlowBehavior.ProviderSupplied,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.Lei,
                SecurityIdentifierKind.ProviderSymbol
            ],
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations,
            SupportsProfileBackedTerms: true),
        new(
            AssetClass: "CommitmentGuarantee",
            AssetPackIds: ["commitment-guarantee"],
            LotUnit: SecurityLotUnitKind.Commitment,
            CashFlowBehavior: SecurityCashFlowBehavior.ProviderSupplied,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.Lei,
                SecurityIdentifierKind.ProviderSymbol
            ],
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations,
            SupportsProfileBackedTerms: true),
        new(
            AssetClass: "Commodity",
            AssetPackIds: ["controlled-other-asset"],
            InstrumentTypes: [InstrumentType.Commodity],
            LotUnit: SecurityLotUnitKind.Units,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            LotMultiplierTermKey: "contractSize",
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.Ticker,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.InternalCode
            ]),
        new(
            AssetClass: "CryptoCurrency",
            AssetPackIds: ["controlled-other-asset"],
            InstrumentTypes: [InstrumentType.Crypto],
            LotUnit: SecurityLotUnitKind.Units,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.Ticker,
                SecurityIdentifierKind.Ric,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.InternalCode
            ]),
        new(
            AssetClass: "Cfd",
            AssetPackIds: ["derivatives-fx"],
            InstrumentTypes: [InstrumentType.CFD],
            LotUnit: SecurityLotUnitKind.Units,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.Ticker
            ]),
        new(
            AssetClass: "Warrant",
            AssetPackIds: ["derivatives-fx"],
            InstrumentTypes: [InstrumentType.Warrant],
            LotUnit: SecurityLotUnitKind.Contracts,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            LotMultiplierTermKey: "multiplier",
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.Ticker,
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Cusip,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.InternalCode
            ]),
        new(
            AssetClass: "FxForward",
            AssetPackIds: ["derivatives-fx"],
            LotUnit: SecurityLotUnitKind.CurrencyAmount,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.Ric
            ]),
        new(
            AssetClass: "NonDeliverableForward",
            AssetPackIds: ["derivatives-fx"],
            LotUnit: SecurityLotUnitKind.CurrencyAmount,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.Ric
            ]),
        new(
            AssetClass: "CreditDefaultSwap",
            AssetPackIds: ["derivatives-fx"],
            LotUnit: SecurityLotUnitKind.Notional,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.PermId,
                SecurityIdentifierKind.Lei,
                SecurityIdentifierKind.ProviderSymbol
            ],
            RequiresMaturity: true),
        new(
            AssetClass: "Swaption",
            AssetPackIds: ["derivatives-fx"],
            LotUnit: SecurityLotUnitKind.Notional,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.PermId,
                SecurityIdentifierKind.ProviderSymbol
            ]),
        new(
            AssetClass: "CapFloor",
            AssetPackIds: ["derivatives-fx"],
            LotUnit: SecurityLotUnitKind.Notional,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.PermId,
                SecurityIdentifierKind.ProviderSymbol
            ],
            RequiresMaturity: true),
        new(
            AssetClass: "StructuredNote",
            AssetPackIds: ["fixed-income"],
            LotUnit: SecurityLotUnitKind.FaceValue,
            CashFlowBehavior: SecurityCashFlowBehavior.None,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Cusip,
                SecurityIdentifierKind.InternalCode
            ],
            RequiresMaturity: true),
        new(
            AssetClass: "InvestmentFund",
            AssetPackIds: ["public-equity-etf"],
            LotUnit: SecurityLotUnitKind.Units,
            CashFlowBehavior: SecurityCashFlowBehavior.FundDistributions,
            ProjectionKind: SecurityTermsProjectionKind.Declarative,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.Ticker,
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Cusip,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.InternalCode
            ],
            // Every InvestmentFund asset-specific term is optional; see Equity above.
            SupportsIdentifierOnlyImport: true)
    ];

    /// <summary>
    /// Accounting instrument classes for the coarse classification taxonomy — the F# <c>AssetClass</c>
    /// vocabulary that a record carries alongside its canonical asset class. Consulted only after
    /// every declared asset-class name has failed to resolve, mirroring the precedence the previous
    /// token-matching resolver had: a specific class always wins over the family-level bucket.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AccountingInstrumentClassByTaxonomy =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FixedIncome"] = SecurityAccountingInstrumentClasses.Bond
        };

    private static readonly IReadOnlyDictionary<string, SecurityAssetClassDescriptor> ByAssetClass = BuildLookup();

    private static IReadOnlyDictionary<string, SecurityAssetClassDescriptor> BuildLookup()
    {
        var lookup = new Dictionary<string, SecurityAssetClassDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in Descriptors)
        {
            lookup.Add(descriptor.AssetClass, descriptor);
        }

        // Second pass so an alias colliding with a canonical name (or another alias) fails loudly
        // at type initialization instead of silently shadowing a descriptor.
        foreach (var descriptor in Descriptors)
        {
            foreach (var alias in descriptor.Aliases ?? [])
            {
                lookup.Add(alias, descriptor);
            }
        }

        return lookup;
    }

    public static IReadOnlyList<SecurityAssetClassDescriptor> All => Descriptors;

    public static IReadOnlyList<string> AssetClasses { get; } =
        Descriptors.Select(descriptor => descriptor.AssetClass).ToArray();

    /// <summary>
    /// Resolves the descriptor for a canonical asset-class name or a registered alias
    /// (case-insensitive, whitespace-trimmed). Unknown or blank input returns the non-throwing
    /// Unknown descriptor so read paths degrade instead of failing.
    /// </summary>
    public static SecurityAssetClassDescriptor GetOrDefault(string? assetClass)
        => assetClass is not null && ByAssetClass.TryGetValue(assetClass.Trim(), out var descriptor)
            ? descriptor
            : DefaultDescriptor;

    public static IReadOnlyList<SecurityIdentifierKind> GetPreferredIdentifierKinds(string? assetClass)
        => GetOrDefault(assetClass).PreferredIdentifierKinds;

    public static IReadOnlyList<string> GetAssetOperationsCapabilities(string? assetClass)
        => GetOrDefault(assetClass).AssetOperationsCapabilities ?? AssetOperationsCapabilitySet.IdentityOnly;

    /// <summary>
    /// The asset classes that declare an Asset Operations capability set of their own, rather than
    /// falling through to <see cref="AssetOperationsCapabilitySet.IdentityOnly"/>. Every declared
    /// set adds LifecycleState, ProjectedCashFlows, ActualActivity, Reconciliation, LedgerProjection
    /// and WorkflowAudit on top of identity, so declaring one is what makes a class ops-capable.
    /// <para>
    /// Naming the set once lets surfaces that owe these classes more than identity — relational term
    /// projections, readiness reporting, coverage guards — agree on which classes those are, instead
    /// of each re-deriving it from a null check or a capability-string match.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> AssetOperationsCapableAssetClasses { get; } =
        Descriptors
            .Where(static descriptor => descriptor.AssetOperationsCapabilities is not null)
            .Select(static descriptor => descriptor.AssetClass)
            .ToArray();

    /// <summary>
    /// Resolves the accounting-slice instrument class a record posts as, from the class names it
    /// declares — most specific first (canonical asset class, then type name, then sub-type), with
    /// the coarse classification taxonomy last. Returns <see langword="null"/> when no declared name
    /// names a class the accounting slice covers, which callers should treat as "outside the slice"
    /// rather than as a failure.
    /// <para>
    /// This is a lookup over declared values, deliberately: the classification a record posts under
    /// must come from what it says it IS. Deriving it by substring-matching classification prose put
    /// cash sweeps into the securitized fixed-income slice, because <c>CashSweep</c> and
    /// <c>StructuredCredit</c> once shared an asset-family label.
    /// </para>
    /// </summary>
    /// <param name="declaredClassNames">
    /// Declared class names in precedence order. Blank entries are skipped; entries that name no
    /// catalog class (or name one outside the slice) fall through to the next.
    /// </param>
    public static string? ResolveAccountingInstrumentClass(params string?[] declaredClassNames)
    {
        ArgumentNullException.ThrowIfNull(declaredClassNames);

        foreach (var declaredClassName in declaredClassNames)
        {
            if (!string.IsNullOrWhiteSpace(declaredClassName) &&
                GetOrDefault(declaredClassName).AccountingInstrumentClass is { } accountingInstrumentClass)
            {
                return accountingInstrumentClass;
            }
        }

        foreach (var declaredClassName in declaredClassNames)
        {
            if (!string.IsNullOrWhiteSpace(declaredClassName) &&
                AccountingInstrumentClassByTaxonomy.TryGetValue(declaredClassName.Trim(), out var taxonomyClass))
            {
                return taxonomyClass;
            }
        }

        return null;
    }

    /// <summary>
    /// The asset classes the named asset pack covers today, in catalog order. Pack membership is a
    /// fact about the class (<see cref="SecurityAssetClassDescriptor.AssetPackIds"/>), so
    /// <c>SecurityAssetPackRegistry</c> reads it from here rather than keeping a second list.
    /// </summary>
    public static IReadOnlyList<string> AssetClassesInPack(string packId)
        => Descriptors
            .Where(descriptor => descriptor.AssetPackIds is not null
                && descriptor.AssetPackIds.Contains(packId, StringComparer.OrdinalIgnoreCase))
            .Select(static descriptor => descriptor.AssetClass)
            .ToArray();

    /// <summary>
    /// The asset classes a bulk import carrying only identity columns may create. See
    /// <see cref="SecurityAssetClassDescriptor.SupportsIdentifierOnlyImport"/>.
    /// </summary>
    public static IReadOnlyList<string> IdentifierOnlyImportableAssetClasses { get; } =
        Descriptors
            .Where(static descriptor => descriptor.SupportsIdentifierOnlyImport)
            .Select(static descriptor => descriptor.AssetClass)
            .ToArray();

    /// <summary>
    /// Resolves <paramref name="assetClass"/> (canonical name or registered alias) to the canonical
    /// asset class an identity-only import may create, or <see langword="null"/> when the class is
    /// unknown or needs asset-specific terms the import cannot supply.
    /// </summary>
    public static string? ResolveIdentifierOnlyImportableAssetClass(string? assetClass)
    {
        var descriptor = GetOrDefault(assetClass);
        return descriptor.SupportsIdentifierOnlyImport ? descriptor.AssetClass : null;
    }
}

/// <param name="AssetClass">Canonical Security Master asset-class name (matches the F# <c>AssetClassRegistry</c>).</param>
/// <param name="LotUnit">
/// What one unit of a position lot in this class measures (shares, contracts, face value, notional,
/// fund units, currency amount, commitment). Execution, backtesting and the ledger read it from
/// here; <see cref="UsesFaceValueLots"/> is derived from it.
/// </param>
/// <param name="CashFlowBehavior">
/// How the class produces cash flows. <see cref="SupportsCashflowScheduleByDefault"/> is derived
/// from it.
/// </param>
/// <param name="ProjectionKind">
/// How the class's terms reach a relational projection: a declarative
/// <c>SecurityTermsProjectionRegistry</c> descriptor, a specialized writer, or the terms JSON only.
/// A storage-level guard asserts the declaration matches the writers that actually exist.
/// </param>
/// <param name="SupportsBasicCreateWorkflow">Whether the basic workstation create workflow supports the class.</param>
/// <param name="PreferredIdentifierKinds">Identifier kinds in preference order for resolution and display.</param>
/// <param name="AssetOperationsCapabilities">Asset Operations capability set; null means identity-only.</param>
/// <param name="AmortizesTowardPar">
/// Whether lots record unit cost as a price per 100 of par and premium/discount amortizes toward par
/// (drives cost-basis amortization). Deliberately narrower than <paramref name="UsesFaceValueLots"/>:
/// Deposit and Repo hold face-value lots but are booked at par, so nothing amortizes.
/// </param>
/// <param name="RequiresMaturity">Whether the canonical validator requires a maturity date (data-quality rule MA001).</param>
/// <param name="SupportsProfileBackedTerms">Whether asset-specific terms may be backed by a custom profile payload.</param>
/// <param name="Aliases">
/// Non-canonical spellings (vendor feeds, legacy imports) that resolve to this descriptor,
/// e.g. "MBS" for StructuredCredit. Aliases must not collide with canonical names or each other.
/// </param>
/// <param name="AccountingInstrumentClass">
/// The accounting-slice instrument class this asset class posts as
/// (<see cref="SecurityAccountingInstrumentClasses"/>), or <see langword="null"/> when the class is
/// outside the fixed-income accounting slice. Declaring it here is what lets the accounting adapter
/// read a class's accounting treatment instead of inferring it by substring-matching the record's
/// classification prose.
/// </param>
/// <param name="SupportsIdentifierOnlyImport">
/// Whether a record of this class can be created from identity columns alone — display name,
/// currency, and identifiers — because <see cref="SecurityAssetTermsSchema"/> declares no REQUIRED
/// asset-specific term for it. Bulk import surfaces that carry no term columns (CSV) use this to
/// decide what they may accept, instead of each keeping a private list. A class with required terms
/// must not be admitted here: an importer cannot invent a maturity or a strike, and defaulting one
/// would mint a governed record on a fabricated economic fact.
/// </param>
/// <param name="TermsVersion">
/// The current version of this asset class's flat terms contract, stamped onto every flat payload
/// as <see cref="SecurityTermsEnvelope.AssetClassTermsVersionKey"/>. Bump it when the class's term
/// fields change shape (a renamed key, a changed type); readers refuse payloads stamped with a
/// newer version than they know, rather than silently dropping the newer fields. Versions are
/// per asset class, so evolving one class never reserves or collides with another's numbers.
/// </param>
/// <param name="LotMultiplierTermKey">
/// The asset-specific term that carries the contract multiplier a lot's quantity is scaled by
/// (an option's or future's <c>multiplier</c>, a commodity's <c>contractSize</c>), or
/// <see langword="null"/> when one lot unit is one economic unit.
/// </param>
/// <param name="InstrumentTypes">
/// The market-data <see cref="InstrumentType"/> values that map to this asset class, or
/// <see langword="null"/> when the class has no direct market-data instrument type. The reverse
/// direction (instrument type to asset class) stays in <c>SecurityKindMapping</c> because it is
/// not one-to-one.
/// </param>
/// <param name="AssetPackIds">
/// The <c>SecurityAssetPackRegistry</c> pack ids that cover this class today. Declared here so the
/// registry derives each pack's claimed classes from the catalog instead of keeping its own list;
/// a class may belong to more than one pack (DirectLoan sits in private credit and in mortgages,
/// facilities and intercompany loans).
/// </param>
/// <param name="SupportsCanonicalBulletAmortization">
/// Whether canonical lot amortization (<c>OpenLotAmortization</c>) accepts face lots of this class:
/// bullet structures whose premium or discount accretes on a fixed schedule. Narrower than
/// <paramref name="AmortizesTowardPar"/>, which also covers factor-amortizing and drawn structures
/// the canonical bullet kernel does not model.
/// </param>
/// <param name="RequiresGovernedProfile">
/// Whether a record of this class must carry an approved custom profile before readiness and
/// accounting workflows may rely on it. Narrower than <paramref name="SupportsProfileBackedTerms"/>:
/// the first-class alternative asset kinds MAY be profile-backed; CustomAsset and OtherSecurity have
/// no terms contract of their own and so MUST be.
/// </param>
public sealed record SecurityAssetClassDescriptor(
    string AssetClass,
    SecurityLotUnitKind LotUnit,
    SecurityCashFlowBehavior CashFlowBehavior,
    SecurityTermsProjectionKind ProjectionKind,
    bool SupportsBasicCreateWorkflow,
    IReadOnlyList<SecurityIdentifierKind> PreferredIdentifierKinds,
    IReadOnlyList<string>? AssetOperationsCapabilities = null,
    bool AmortizesTowardPar = false,
    bool RequiresMaturity = false,
    bool SupportsProfileBackedTerms = false,
    IReadOnlyList<string>? Aliases = null,
    string? AccountingInstrumentClass = null,
    bool SupportsIdentifierOnlyImport = false,
    int TermsVersion = SecurityTermsEnvelope.DefaultAssetClassTermsVersion,
    string? LotMultiplierTermKey = null,
    IReadOnlyList<InstrumentType>? InstrumentTypes = null,
    IReadOnlyList<string>? AssetPackIds = null,
    bool SupportsCanonicalBulletAmortization = false,
    bool RequiresGovernedProfile = false)
{
    /// <summary>
    /// Whether positions are held as face-value lots (par-denominated quantity). Derived from
    /// <see cref="LotUnit"/>; there is no separately maintained flag to drift from it.
    /// </summary>
    public bool UsesFaceValueLots => LotUnit == SecurityLotUnitKind.FaceValue;

    /// <summary>
    /// Whether a calculated cash-flow schedule projection is expected by default. Derived from
    /// <see cref="CashFlowBehavior"/>: contractual, factor-amortizing, swap-leg and financing
    /// behaviour are calculated from terms; dividends, fund distributions and provider-supplied
    /// flows are not.
    /// </summary>
    public bool SupportsCashflowScheduleByDefault => CashFlowBehavior.IsCalculatedFromTerms();
}

/// <summary>
/// The closed vocabulary of accounting-slice instrument classes a Security Master record can post
/// as. These are the values the Security Master accounting event slice gates on; keeping them here
/// stops producers and that gate drifting on spelling.
/// </summary>
public static class SecurityAccountingInstrumentClasses
{
    /// <summary>Coupon-bearing and discount fixed income booked against par.</summary>
    public const string Bond = "Bond";

    /// <summary>Securitized credit tranches whose principal amortizes by pool factor (ADR-022).</summary>
    public const string AssetBackedSecurity = "AssetBackedSecurity";
}

public static class AssetOperationsCapabilitySet
{
    public static readonly IReadOnlyList<string> IdentityOnly =
    [
        "Identity",
        "TermsHistory",
        "Evidence",
        "Readiness"
    ];

    public static readonly IReadOnlyList<string> FixedIncome =
    [
        "Identity",
        "TermsHistory",
        "LifecycleState",
        "ProjectedCashFlows",
        "ActualActivity",
        "Reconciliation",
        "LedgerProjection",
        "Evidence",
        "WorkflowAudit",
        "Readiness"
    ];

    public static readonly IReadOnlyList<string> DirectLending =
    [
        "Identity",
        "TermsHistory",
        "LifecycleState",
        "ProjectedCashFlows",
        "ActualActivity",
        "Reconciliation",
        "LedgerProjection",
        "Evidence",
        "WorkflowAudit",
        "Readiness"
    ];

    public static readonly IReadOnlyList<string> AlternativeAssetOperations =
    [
        "Identity",
        "TermsHistory",
        "LifecycleState",
        "ProjectedCashFlows",
        "ActualActivity",
        "Reconciliation",
        "LedgerProjection",
        "Evidence",
        "WorkflowAudit",
        "Readiness"
    ];
}
