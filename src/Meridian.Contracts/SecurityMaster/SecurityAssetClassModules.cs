namespace Meridian.Contracts.SecurityMaster;

/// <summary>A class-owned nested term shape, with its retained registry enumeration order.</summary>
public sealed record SecurityAssetElementShape(
    string Key,
    int NestedOrder,
    IReadOnlyList<SecurityAssetTermField> Fields);

/// <summary>
/// Compile-time contract contributions for one canonical asset class. Layer-specific validators
/// and projections compose against this identity; these declarations grant no posting authority.
/// </summary>
public sealed record SecurityAssetClassModule(
    SecurityAssetClassDescriptor Descriptor,
    IReadOnlyList<SecurityAssetTermField> Fields,
    int SchemaOrder,
    IReadOnlyList<SecurityAssetElementShape> ElementShapes)
{
    /// <summary>The class's declared field units, ranges and consuming readers.</summary>
    public IReadOnlyList<SecurityRateFieldDeclaration> RateFields { get; init; } = [];

    /// <summary>The scalar rate key used by the existing coupon-unit reader.</summary>
    public string CouponRateFieldKey { get; init; } = "couponRate";

    /// <summary>The existing generated flat codec; null for the opaque custom-profile codec.</summary>
    public SecurityClassCodecBinding? CodecBinding { get; init; }

    /// <summary>Explicitly retains the opaque approved-profile codec instead of generated flat rows.</summary>
    public bool UsesOpaqueProfileCodec { get; init; }

    // Compatibility ordinals retain the existing generated-code and economic-recovery ordering.
    internal int CodecOrder { get; init; }
    internal int EconomicFlatteningOrder { get; init; }
    internal IReadOnlyList<SecurityEconomicTermFlattening>? EconomicFlattenings { get; init; }
}

/// <summary>
/// Explicit, closed-world composition of class-owned contracts. The catalog, schema, rate and
/// codec registries are compatibility facades over this list, never independent class tables.
/// </summary>
public static class SecurityAssetClassModules
{
    /// <summary>All compiled class modules, in the existing catalog order.</summary>
    public static IReadOnlyList<SecurityAssetClassModule> All { get; } = Compose(
    [
        EquityAssetModule.Create(),
        OptionAssetModule.Create(),
        FutureAssetModule.Create(),
        BondAssetModule.Create(),
        FxSpotAssetModule.Create(),
        DepositAssetModule.Create(),
        MoneyMarketFundAssetModule.Create(),
        CertificateOfDepositAssetModule.Create(),
        CommercialPaperAssetModule.Create(),
        TreasuryBillAssetModule.Create(),
        RepoAssetModule.Create(),
        CashSweepAssetModule.Create(),
        OtherSecurityAssetModule.Create(),
        CustomAssetModule.Create(),
        SwapAssetModule.Create(),
        DirectLoanAssetModule.Create(),
        StructuredCreditAssetModule.Create(),
        PrivateFundInterestAssetModule.Create(),
        PrivateCompanyEquityAssetModule.Create(),
        RealEstateHoldingAssetModule.Create(),
        CommitmentGuaranteeAssetModule.Create(),
        CommodityAssetModule.Create(),
        CryptoCurrencyAssetModule.Create(),
        CfdAssetModule.Create(),
        WarrantAssetModule.Create(),
        FxForwardAssetModule.Create(),
        NonDeliverableForwardAssetModule.Create(),
        CreditDefaultSwapAssetModule.Create(),
        SwaptionAssetModule.Create(),
        CapFloorAssetModule.Create(),
        StructuredNoteAssetModule.Create(),
        InvestmentFundAssetModule.Create()
    ]);

    private static readonly IReadOnlyDictionary<string, SecurityAssetClassModule> ByAssetClass =
        All.ToDictionary(static module => module.Descriptor.AssetClass, StringComparer.OrdinalIgnoreCase);

    /// <summary>Finds a canonical class contribution; alias resolution stays with the catalog.</summary>
    public static SecurityAssetClassModule? Find(string assetClass)
        => ByAssetClass.TryGetValue(assetClass, out var module) ? module : null;

    internal static IReadOnlyList<SecurityAssetClassModule> Compose(IReadOnlyList<SecurityAssetClassModule> modules)
    {
        var classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var schemaOrders = new HashSet<int>();
        var nestedOrders = new HashSet<int>();
        foreach (var module in modules)
        {
            var assetClass = module.Descriptor.AssetClass;
            if (string.IsNullOrWhiteSpace(assetClass) || !classes.Add(assetClass))
                throw new InvalidOperationException($"Duplicate or empty asset module identity '{assetClass}'.");
            if (!schemaOrders.Add(module.SchemaOrder))
                throw new InvalidOperationException($"Duplicate asset schema order on '{assetClass}'.");
            if (module.UsesOpaqueProfileCodec)
            {
                if (module.CodecBinding is not null || module.EconomicFlattenings is not null)
                    throw new InvalidOperationException($"Opaque profile module '{assetClass}' cannot also supply flat codec or economic-recovery rows.");
            }
            else if (module.CodecBinding is null || module.EconomicFlattenings is null)
                throw new InvalidOperationException($"Asset module '{assetClass}' must supply its generated codec and economic-recovery rows.");
            if (module.CodecBinding is { } codec && !string.Equals(codec.AssetClass, assetClass, StringComparison.Ordinal))
                throw new InvalidOperationException($"Asset module '{assetClass}' has another class's codec binding.");
            if (module.RateFields.Any(field => !string.Equals(field.AssetClass, assetClass, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Asset module '{assetClass}' has another class's rate declaration.");

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var shape in module.ElementShapes)
            {
                if (!paths.Add(shape.Key) || !nestedOrders.Add(shape.NestedOrder))
                    throw new InvalidOperationException($"Duplicate asset element shape or order on '{assetClass}.{shape.Key}'.");
                var root = shape.Key.Split('.')[0];
                if (!module.Fields.Any(field => string.Equals(field.Key, root, StringComparison.Ordinal)))
                    throw new InvalidOperationException($"Asset element shape '{assetClass}.{shape.Key}' has no declared root term.");
            }
        }

        return modules;
    }
}
