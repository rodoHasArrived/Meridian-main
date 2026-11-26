namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// The declared semantics of one stored rate field: what the number means, the unit it is in when the
/// record does not say, the range it may take once converted to a fraction, and whether a negative
/// value is a supported economic fact.
/// </summary>
/// <param name="AssetClass">The canonical catalog asset class.</param>
/// <param name="FieldKey">
/// The schema key of the field (<see cref="SecurityAssetTermsSchema"/>); element fields are written as a
/// dotted path, for example <c>legs.fixedRate</c>.
/// </param>
/// <param name="Meaning">What the number is: an annual coupon, a spread, a strike, a recovery rate.</param>
/// <param name="DefaultUnit">
/// The unit the field declares when the record declares none, or <see langword="null"/> when no default
/// is declared: legacy values are then <see cref="RateUnitResolution.Undeclared"/> and are flagged, not
/// converted. Fields whose stored data is known to be written both ways deliberately declare none.
/// </param>
/// <param name="HonorsRecordUnit">
/// Whether a unit the record itself declares (<c>scheduleConventions.couponRateUnit</c> or a leg's rate
/// unit) governs this field. Basis-point fields are basis points by name and ignore it.
/// </param>
/// <param name="MinFraction">The lowest permitted value once converted to a fraction, if bounded.</param>
/// <param name="MaxFraction">The highest permitted value once converted to a fraction, if bounded.</param>
/// <param name="AllowsNegative">Whether a negative value is a supported economic fact for the field.</param>
/// <param name="ConsumedBy">The calculation readers that read the field through <see cref="RateUnitNormalizer"/>; empty for inventory only.</param>
public sealed record SecurityRateFieldDeclaration(
    string AssetClass,
    string FieldKey,
    string Meaning,
    RateUnit? DefaultUnit,
    bool HonorsRecordUnit,
    decimal? MinFraction,
    decimal? MaxFraction,
    bool AllowsNegative,
    string ConsumedBy = "");

/// <summary>
/// The rate-field unit table (security-master review D10, remediation slice 4, decision D3). Every rate
/// field in the flat terms of <c>SecurityMaster.fs</c> / <c>SecurityTermModules.fs</c> is inventoried here
/// with its declared default unit, range and negative-rate policy. <see cref="RateUnitNormalizer"/> reads
/// it; nothing infers a unit from a value's magnitude.
/// <para>
/// The defaults are <b>proposed, pending domain-owner review (D3)</b>. Only two kinds of field declare a
/// default: basis-point fields (basis points by name) and the Deposit and CashSweep rates, which match
/// the catalog's <see cref="SecurityAssetClassDescriptor.QuotesRatesAsFractions"/> convention. Every
/// other rate field declares none, because stored data writes it both ways (4.25 and 0.0425); a legacy
/// value on those fields is <see cref="RateUnitResolution.Undeclared"/> until the record declares a unit.
/// </para>
/// <para>
/// FX forward rates (<c>FxForward.forwardRate</c>, <c>NonDeliverableForward.forwardRate</c>) are prices,
/// not percentage rates, and are deliberately not in the table.
/// </para>
/// </summary>
public static class SecurityRateFieldUnits
{
    /// <summary>Every declared rate field, in catalog order.</summary>
    public static IReadOnlyList<SecurityRateFieldDeclaration> All { get; } =
        SecurityAssetClassModules.All.SelectMany(static module => module.RateFields).ToArray();

    private static readonly IReadOnlyDictionary<(string AssetClass, string FieldKey), SecurityRateFieldDeclaration> ByKey =
        All.ToDictionary(static field => (field.AssetClass, field.FieldKey), RateFieldKeyComparer.Instance);

    /// <summary>
    /// The declaration of <paramref name="fieldKey"/> on <paramref name="assetClass"/> (canonical name or
    /// registered alias), or <see langword="null"/> when the field is not a catalogued rate field.
    /// </summary>
    public static SecurityRateFieldDeclaration? Find(string? assetClass, string fieldKey)
    {
        ArgumentNullException.ThrowIfNull(fieldKey);
        var canonical = SecurityAssetClassCatalog.GetOrDefault(assetClass).AssetClass;
        return ByKey.TryGetValue((canonical, fieldKey), out var field) ? field : null;
    }

    /// <summary>
    /// The flat key that carries the scalar annual coupon for <paramref name="assetClass"/>, matching the
    /// class-specific coupon key <c>StructuredCashFlowTermsResolver</c> reads first.
    /// </summary>
    public static string CouponFieldKey(string? assetClass)
        => SecurityAssetClassModules.Find(SecurityAssetClassCatalog.GetOrDefault(assetClass).AssetClass)?.CouponRateFieldKey
           ?? "couponRate";

    private sealed class RateFieldKeyComparer : IEqualityComparer<(string AssetClass, string FieldKey)>
    {
        public static readonly RateFieldKeyComparer Instance = new();

        public bool Equals((string AssetClass, string FieldKey) x, (string AssetClass, string FieldKey) y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.AssetClass, y.AssetClass)
               && StringComparer.OrdinalIgnoreCase.Equals(x.FieldKey, y.FieldKey);

        public int GetHashCode((string AssetClass, string FieldKey) obj)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.AssetClass), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FieldKey));
    }
}
