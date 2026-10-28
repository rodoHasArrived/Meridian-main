using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// The one lot shape shared by execution, backtesting, the ledger and face-value accounting: a
/// decimal quantity, what one unit of it measures, and the contract multiplier that scales it to
/// economic exposure. A face-value lot is the <see cref="SecurityLotUnitKind.FaceValue"/> case with
/// a multiplier of one; a listed-option lot is the <see cref="SecurityLotUnitKind.Contracts"/> case
/// carrying the option's multiplier.
/// </summary>
public interface IUnitizedLot
{
    /// <summary>Lot quantity in <see cref="UnitKind"/> units. Never fractional-unsafe: always decimal.</summary>
    decimal Quantity { get; }

    /// <summary>What one unit of <see cref="Quantity"/> measures.</summary>
    SecurityLotUnitKind UnitKind { get; }

    /// <summary>The factor that scales one unit to economic exposure (1 unless the unit is a contract).</summary>
    decimal Multiplier { get; }
}

/// <summary>A unitized lot quantity value.</summary>
public readonly record struct LotQuantity(decimal Amount, SecurityLotUnitKind UnitKind, decimal Multiplier)
{
    /// <summary>The quantity scaled by its multiplier: shares, face, notional or contract exposure units.</summary>
    public decimal EconomicUnits => Amount * Multiplier;

    /// <summary>
    /// Value at a raw unit quote. Debt quotes are percentages of par; listed contracts
    /// scale the quote by their contract multiplier. Exact retained monetary basis must
    /// not pass through this conversion a second time.
    /// </summary>
    public decimal ValueAtPrice(decimal price)
    {
        if (!Enum.IsDefined(UnitKind) || Multiplier <= 0m)
            throw new InvalidOperationException("A defined lot unit and positive multiplier are required for pricing.");
        return EconomicUnits * price / (UnitKind == SecurityLotUnitKind.FaceValue ? 100m : 1m);
    }

    /// <summary>The unitized quantity of any lot.</summary>
    public static LotQuantity Of(IUnitizedLot lot)
    {
        ArgumentNullException.ThrowIfNull(lot);
        return new LotQuantity(lot.Quantity, lot.UnitKind, lot.Multiplier);
    }

    /// <summary>
    /// The lot unit and multiplier an asset class's lots carry, read from the asset-class registry:
    /// the class's <see cref="SecurityAssetClassDescriptor.LotUnit"/> and, when it declares a
    /// <see cref="SecurityAssetClassDescriptor.LotMultiplierTermKey"/>, that term's positive value from
    /// <paramref name="assetSpecificTerms"/>. Unknown classes and missing or invalid contract multipliers are rejected.
    /// </summary>
    public static (SecurityLotUnitKind UnitKind, decimal Multiplier) UnitFor(string? assetClass, JsonElement? assetSpecificTerms = null)
    {
        if (!SecurityAssetClassCatalog.TryGet(assetClass, out var descriptor))
            throw new ArgumentException("A registered asset class is required to determine lot units.", nameof(assetClass));
        var multiplier = 1m;
        if (descriptor.LotMultiplierTermKey is { } key)
        {
            if (assetSpecificTerms is not { ValueKind: JsonValueKind.Object } terms
                || !terms.TryGetProperty(key, out var value)
                || value.ValueKind != JsonValueKind.Number
                || !value.TryGetDecimal(out multiplier)
                || multiplier <= 0m)
            {
                throw new ArgumentException($"Asset class '{descriptor.AssetClass}' requires a positive '{key}' term to open a lot.", nameof(assetSpecificTerms));
            }
        }

        return (descriptor.LotUnit, multiplier);
    }

    /// <summary>A quantity of <paramref name="amount"/> for an asset class, unitized from the registry.</summary>
    public static LotQuantity For(string? assetClass, decimal amount, JsonElement? assetSpecificTerms = null)
    {
        var (unit, multiplier) = UnitFor(assetClass, assetSpecificTerms);
        return new LotQuantity(amount, unit, multiplier);
    }
}
