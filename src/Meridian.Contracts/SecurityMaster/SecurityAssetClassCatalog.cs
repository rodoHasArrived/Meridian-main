namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Shared Security Master asset-class metadata used by edit workflows, projections,
/// and workstation capability summaries so asset support does not drift across surfaces.
/// </summary>
public static class SecurityAssetClassCatalog
{
    private static readonly SecurityAssetClassDescriptor DefaultDescriptor = new(
        AssetClass: "Unknown",
        SupportsCashflowScheduleByDefault: false,
        UsesFaceValueLots: false,
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
            SupportsCashflowScheduleByDefault: false,
            UsesFaceValueLots: false,
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
            ]),
        new(
            AssetClass: "Option",
            SupportsCashflowScheduleByDefault: false,
            UsesFaceValueLots: false,
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
            SupportsCashflowScheduleByDefault: false,
            UsesFaceValueLots: false,
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
            SupportsCashflowScheduleByDefault: true,
            UsesFaceValueLots: true,
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
            RequiresMaturity: true),
        new(
            AssetClass: "FxSpot",
            SupportsCashflowScheduleByDefault: false,
            UsesFaceValueLots: false,
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
            SupportsCashflowScheduleByDefault: true,
            UsesFaceValueLots: true,
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
            SupportsCashflowScheduleByDefault: true,
            UsesFaceValueLots: false,
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
            SupportsCashflowScheduleByDefault: true,
            UsesFaceValueLots: true,
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
            RequiresMaturity: true),
        new(
            AssetClass: "CommercialPaper",
            SupportsCashflowScheduleByDefault: true,
            UsesFaceValueLots: true,
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
            RequiresMaturity: true),
        new(
            AssetClass: "TreasuryBill",
            SupportsCashflowScheduleByDefault: true,
            UsesFaceValueLots: true,
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
            RequiresMaturity: true),
        new(
            AssetClass: "Repo",
            SupportsCashflowScheduleByDefault: true,
            UsesFaceValueLots: true,
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
            SupportsCashflowScheduleByDefault: true,
            UsesFaceValueLots: false,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.Ticker
            ]),
        new(
            AssetClass: "OtherSecurity",
            SupportsCashflowScheduleByDefault: false,
            UsesFaceValueLots: false,
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
            SupportsCashflowScheduleByDefault: false,
            UsesFaceValueLots: false,
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
            SupportsCashflowScheduleByDefault: true,
            UsesFaceValueLots: false,
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
            SupportsCashflowScheduleByDefault: true,
            UsesFaceValueLots: true,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.PermId,
                SecurityIdentifierKind.Lei,
                SecurityIdentifierKind.ProviderSymbol
            ],
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.DirectLending,
            AmortizesTowardPar: true,
            Aliases: ["Loan", "AmortizingLoan"]),
        new(
            AssetClass: "StructuredCredit",
            SupportsCashflowScheduleByDefault: true,
            UsesFaceValueLots: true,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.Cusip,
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.ProviderSymbol
            ],
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations,
            AmortizesTowardPar: true,
            SupportsProfileBackedTerms: true,
            Aliases: ["MortgageBacked", "MortgageBackedSecurity", "MBS", "AssetBacked", "AssetBackedSecurity", "ABS"]),
        new(
            AssetClass: "PrivateFundInterest",
            SupportsCashflowScheduleByDefault: true,
            UsesFaceValueLots: false,
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
            SupportsCashflowScheduleByDefault: false,
            UsesFaceValueLots: false,
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
            SupportsCashflowScheduleByDefault: true,
            UsesFaceValueLots: false,
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
            SupportsCashflowScheduleByDefault: true,
            UsesFaceValueLots: false,
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
            SupportsCashflowScheduleByDefault: false,
            UsesFaceValueLots: false,
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
            SupportsCashflowScheduleByDefault: false,
            UsesFaceValueLots: false,
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
            SupportsCashflowScheduleByDefault: false,
            UsesFaceValueLots: false,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.InternalCode,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.Ticker
            ]),
        new(
            AssetClass: "Warrant",
            SupportsCashflowScheduleByDefault: false,
            UsesFaceValueLots: false,
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
            AssetClass: "InvestmentFund",
            SupportsCashflowScheduleByDefault: false,
            UsesFaceValueLots: false,
            SupportsBasicCreateWorkflow: false,
            PreferredIdentifierKinds:
            [
                SecurityIdentifierKind.Ticker,
                SecurityIdentifierKind.Isin,
                SecurityIdentifierKind.Cusip,
                SecurityIdentifierKind.Figi,
                SecurityIdentifierKind.ProviderSymbol,
                SecurityIdentifierKind.InternalCode
            ])
    ];

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
}

/// <param name="AssetClass">Canonical Security Master asset-class name (matches the F# <c>AssetClassRegistry</c>).</param>
/// <param name="SupportsCashflowScheduleByDefault">Whether a cash-flow schedule projection is expected by default.</param>
/// <param name="UsesFaceValueLots">Whether positions are held as face-value lots (par-denominated quantity).</param>
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
public sealed record SecurityAssetClassDescriptor(
    string AssetClass,
    bool SupportsCashflowScheduleByDefault,
    bool UsesFaceValueLots,
    bool SupportsBasicCreateWorkflow,
    IReadOnlyList<SecurityIdentifierKind> PreferredIdentifierKinds,
    IReadOnlyList<string>? AssetOperationsCapabilities = null,
    bool AmortizesTowardPar = false,
    bool RequiresMaturity = false,
    bool SupportsProfileBackedTerms = false,
    IReadOnlyList<string>? Aliases = null);

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
