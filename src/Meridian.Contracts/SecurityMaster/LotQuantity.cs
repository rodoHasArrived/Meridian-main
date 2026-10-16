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
public readonly record struct LotQuantity(decimal Amount, SecurityLotUnitKind UnitKind, decimal Multiplier = 1m)
{
    /// <summary>The quantity scaled by its multiplier: shares, face, notional or contract exposure units.</summary>
    public decimal EconomicUnits => Amount * Multiplier;

    /// <summary>The unitized quantity of any lot.</summary>
    public static LotQuantity Of(IUnitizedLot lot)
    {
        ArgumentNullException.ThrowIfNull(lot);
        return new LotQuantity(lot.Quantity, lot.UnitKind, lot.Multiplier);
    }

    /// <summary>
    /// The lot unit and multiplier an asset class's lots carry, read from the asset-class registry:
    /// the class's <see cref="SecurityAssetClassDescriptor.LotUnit"/> and, when it declares a
    /// <see cref="SecurityAssetClassDescriptor.LotMultiplierTermKey"/>, that term's value from
    /// <paramref name="assetSpecificTerms"/> (1 when absent or not positive).
    /// </summary>
    public static (SecurityLotUnitKind UnitKind, decimal Multiplier) UnitFor(string? assetClass, JsonElement? assetSpecificTerms = null)
    {
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(assetClass);
        var multiplier = 1m;
        if (descriptor.LotMultiplierTermKey is { } key
            && assetSpecificTerms is { ValueKind: JsonValueKind.Object } terms
            && terms.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var parsed)
            && parsed > 0m)
        {
            multiplier = parsed;
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
