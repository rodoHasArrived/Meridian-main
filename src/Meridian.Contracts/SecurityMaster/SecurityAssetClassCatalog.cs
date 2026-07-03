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
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.FixedIncome),
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
            ]),
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
            ]),
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
            ]),
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
            ]),
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
            ]),
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
            ]),
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
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.DirectLending),
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
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations),
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
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations),
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
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations),
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
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations),
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
            AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations),
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
            ])
    ];

    private static readonly IReadOnlyDictionary<string, SecurityAssetClassDescriptor> ByAssetClass =
        Descriptors.ToDictionary(descriptor => descriptor.AssetClass, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SecurityAssetClassDescriptor> All => Descriptors;

    public static IReadOnlyList<string> AssetClasses { get; } =
        Descriptors.Select(descriptor => descriptor.AssetClass).ToArray();

    public static SecurityAssetClassDescriptor GetOrDefault(string? assetClass)
        => assetClass is not null && ByAssetClass.TryGetValue(assetClass, out var descriptor)
            ? descriptor
            : DefaultDescriptor;

    public static IReadOnlyList<SecurityIdentifierKind> GetPreferredIdentifierKinds(string? assetClass)
        => GetOrDefault(assetClass).PreferredIdentifierKinds;

    public static IReadOnlyList<string> GetAssetOperationsCapabilities(string? assetClass)
        => GetOrDefault(assetClass).AssetOperationsCapabilities ?? AssetOperationsCapabilitySet.IdentityOnly;
}

public sealed record SecurityAssetClassDescriptor(
    string AssetClass,
    bool SupportsCashflowScheduleByDefault,
    bool UsesFaceValueLots,
    bool SupportsBasicCreateWorkflow,
    IReadOnlyList<SecurityIdentifierKind> PreferredIdentifierKinds,
    IReadOnlyList<string>? AssetOperationsCapabilities = null);

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
