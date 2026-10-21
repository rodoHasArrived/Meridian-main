namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// What one lot unit means and which canonical lot type carries it. Keyed on
/// <see cref="SecurityLotUnitKind"/>, so an asset class's lot behaviour is read from its
/// descriptor's <see cref="SecurityAssetClassDescriptor.LotUnit"/> and never from its name.
/// </summary>
/// <param name="UnitKind">The lot unit this entry describes.</param>
/// <param name="CanonicalLotType">
/// The typed lot aggregate that owns the unit's economics (face, notional, commitment, units), or
/// null when the unit is carried only by the generic <see cref="IUnitizedLot"/> shapes of the
/// execution, backtesting and ledger lots (shares, listed contracts, currency amounts).
/// </param>
/// <param name="QuantityMeaning">What the lot's <see cref="IUnitizedLot.Quantity"/> measures.</param>
/// <param name="TracksPaydownFactor">
/// True when the lot's quantity declines with a pool factor applied inside the lot
/// (<see cref="FaceValueLot.CurrentFactor"/>) rather than by relieving units.
/// </param>
/// <param name="AmortizesBySchedule">
/// True when the lot's quantity is replaced by a contractual schedule (<see cref="NotionalLot.WithNotional"/>)
/// rather than relieved by a disposal.
/// </param>
public sealed record SecurityLotUnitDescriptor(
    SecurityLotUnitKind UnitKind,
    Type? CanonicalLotType,
    string QuantityMeaning,
    bool TracksPaydownFactor = false,
    bool AmortizesBySchedule = false)
{
    /// <summary>True when a typed lot aggregate owns this unit's economics.</summary>
    public bool HasCanonicalLotType => CanonicalLotType is not null;
}

/// <summary>
/// The one lot model: every <see cref="SecurityLotUnitKind"/> maps to exactly one entry stating
/// what its quantity means and which typed lot carries it. Consumers that need to know how a
/// holding is sized read <see cref="For(SecurityLotUnitKind)"/> or
/// <see cref="ForAssetClass(string?)"/> instead of switching on asset-class strings.
/// </summary>
public static class SecurityLotModel
{
    private static readonly IReadOnlyList<SecurityLotUnitDescriptor> Descriptors =
    [
        new(SecurityLotUnitKind.Shares, CanonicalLotType: null, "Equity shares held."),
        new(SecurityLotUnitKind.Contracts, CanonicalLotType: null, "Listed derivative contracts held; the multiplier scales each to its exposure."),
        new(SecurityLotUnitKind.FaceValue, typeof(FaceValueLot), "Face outstanding under the lot's current pool factor.", TracksPaydownFactor: true),
        new(SecurityLotUnitKind.Notional, typeof(NotionalLot), "Notional outstanding that the contract's payments are computed on.", AmortizesBySchedule: true),
        new(SecurityLotUnitKind.Units, typeof(FundUnitLot), "Fund or vehicle units held."),
        new(SecurityLotUnitKind.CurrencyAmount, CanonicalLotType: null, "An amount of currency."),
        new(SecurityLotUnitKind.Commitment, typeof(CommitmentLot), "Amount committed; funded and unfunded are separate facts on the lot.")
    ];

    private static readonly IReadOnlyDictionary<SecurityLotUnitKind, SecurityLotUnitDescriptor> ByUnit =
        Descriptors.ToDictionary(static descriptor => descriptor.UnitKind);

    /// <summary>Every lot unit the model describes, in enum order.</summary>
    public static IReadOnlyList<SecurityLotUnitDescriptor> All => Descriptors;

    /// <summary>The entry for <paramref name="unitKind"/>.</summary>
    public static SecurityLotUnitDescriptor For(SecurityLotUnitKind unitKind)
        => ByUnit.TryGetValue(unitKind, out var descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(nameof(unitKind), unitKind, "The lot model has no entry for this lot unit.");

    /// <summary>The entry for the lot unit the asset-class registry assigns to <paramref name="assetClass"/>.</summary>
    public static SecurityLotUnitDescriptor ForAssetClass(string? assetClass)
        => For(SecurityAssetClassCatalog.GetOrDefault(assetClass).LotUnit);

    /// <summary>The entry for the unit a lot reports.</summary>
    public static SecurityLotUnitDescriptor For(IUnitizedLot lot)
    {
        ArgumentNullException.ThrowIfNull(lot);
        return For(lot.UnitKind);
    }

    /// <summary>The asset classes whose lots carry <paramref name="unitKind"/>, in catalog order.</summary>
    public static IReadOnlyList<string> AssetClassesWithUnit(SecurityLotUnitKind unitKind)
        => SecurityAssetClassCatalog.All
            .Where(descriptor => descriptor.LotUnit == unitKind)
            .Select(static descriptor => descriptor.AssetClass)
            .ToArray();

    /// <summary>
    /// True when <paramref name="lot"/> is the canonical typed lot for its own unit, or its unit has
    /// no typed lot and any <see cref="IUnitizedLot"/> may carry it.
    /// </summary>
    public static bool IsCanonicalLot(IUnitizedLot lot)
    {
        ArgumentNullException.ThrowIfNull(lot);
        var descriptor = For(lot.UnitKind);
        return descriptor.CanonicalLotType is null || descriptor.CanonicalLotType.IsInstanceOfType(lot);
    }
}
