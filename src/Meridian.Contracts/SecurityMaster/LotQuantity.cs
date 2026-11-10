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
    /// Value at a raw unit quote, scaling by the unit kind's default quote convention: face-value
    /// lots read the quote as a percentage of par, everything else per unit, and listed contracts
    /// scale by their contract multiplier. Exact retained monetary basis must not pass through
    /// this conversion a second time.
    /// <para>
    /// The unit-kind default and the catalog's declared <see cref="SecurityAssetClassDescriptor.QuoteConvention"/>
    /// agree for every catalogued class today (pinned by <c>SecurityQuoteConventionCoverageTests</c>).
    /// A class-aware caller pricing a class that declares a different convention uses
    /// <see cref="ValueAtPrice(decimal, SecurityQuoteConvention)"/> with
    /// <see cref="QuoteConventionFor(string?)"/> instead of this overload.
    /// </para>
    /// </summary>
    public decimal ValueAtPrice(decimal price)
        => ValueAtPrice(price, UnitKind == SecurityLotUnitKind.FaceValue
            ? SecurityQuoteConvention.PercentOfPar
            : SecurityQuoteConvention.PerUnit);

    /// <summary>Value at a raw unit quote under a declared quote convention.</summary>
    public decimal ValueAtPrice(decimal price, SecurityQuoteConvention quoteConvention)
    {
        if (!Enum.IsDefined(UnitKind) || Multiplier <= 0m)
            throw new InvalidOperationException("A defined lot unit and positive multiplier are required for pricing.");
        return EconomicUnits * price / (quoteConvention == SecurityQuoteConvention.PercentOfPar ? 100m : 1m);
    }

    /// <summary>
    /// The quote convention an asset class declares in the catalog, for class-aware pricing paths.
    /// Unknown classes are rejected rather than guessed.
    /// </summary>
    public static SecurityQuoteConvention QuoteConventionFor(string? assetClass)
        => SecurityAssetClassCatalog.TryGet(assetClass, out var descriptor)
            ? descriptor.QuoteConvention
            : throw new ArgumentException($"Unknown asset class '{assetClass}' has no declared quote convention.", nameof(assetClass));

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
        if (!TryUnitFor(assetClass, assetSpecificTerms, out var unitKind, out var multiplier, out var error, out var parameterName))
            throw new ArgumentException(error, parameterName);
        return (unitKind, multiplier);
    }

    /// <summary>A quantity of <paramref name="amount"/> for an asset class, unitized from the registry.</summary>
    public static LotQuantity For(string? assetClass, decimal amount, JsonElement? assetSpecificTerms = null)
    {
        var (unit, multiplier) = UnitFor(assetClass, assetSpecificTerms);
        return new LotQuantity(amount, unit, multiplier);
    }

    /// <summary>
    /// <see cref="For"/> without the exception: false, with the reason, when the class is not
    /// registered or its contract multiplier term is missing or not positive. Callers that report
    /// a skipped posting rather than fail (journal producers, ledger bridges) use this form.
    /// </summary>
    public static bool TryFor(
        string? assetClass,
        decimal amount,
        JsonElement? assetSpecificTerms,
        out LotQuantity quantity,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error)
    {
        if (!TryUnitFor(assetClass, assetSpecificTerms, out var unitKind, out var multiplier, out error, out _))
        {
            quantity = default;
            return false;
        }

        quantity = new LotQuantity(amount, unitKind, multiplier);
        return true;
    }

    private static bool TryUnitFor(
        string? assetClass,
        JsonElement? assetSpecificTerms,
        out SecurityLotUnitKind unitKind,
        out decimal multiplier,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error,
        out string parameterName)
    {
        unitKind = default;
        multiplier = 1m;
        error = null;
        parameterName = nameof(assetClass);
        if (!SecurityAssetClassCatalog.TryGet(assetClass, out var descriptor))
        {
            error = "A registered asset class is required to determine lot units.";
            return false;
        }

        if (descriptor.LotMultiplierTermKey is { } key)
        {
            if (assetSpecificTerms is not { ValueKind: JsonValueKind.Object } terms
                || !terms.TryGetProperty(key, out var value)
                || value.ValueKind != JsonValueKind.Number
                || !value.TryGetDecimal(out multiplier)
                || multiplier <= 0m)
            {
                multiplier = 1m;
                error = $"Asset class '{descriptor.AssetClass}' requires a positive '{key}' term to open a lot.";
                parameterName = nameof(assetSpecificTerms);
                return false;
            }
        }

        unitKind = descriptor.LotUnit;
        return true;
    }
}
