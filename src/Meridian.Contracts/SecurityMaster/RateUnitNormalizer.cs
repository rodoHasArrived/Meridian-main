namespace Meridian.Contracts.SecurityMaster;

/// <summary>The declared unit a stored rate value is expressed in.</summary>
public enum RateUnit
{
    /// <summary>A plain fraction: 0.045 is four and a half percent.</summary>
    Fraction,

    /// <summary>Percentage points: 4.5 is four and a half percent, and 0.5 is half a percent.</summary>
    Percent,

    /// <summary>Basis points: 125 is one and a quarter percent.</summary>
    BasisPoints
}

/// <summary>Where the unit of a resolved rate came from.</summary>
public enum RateUnitSource
{
    /// <summary>The record itself declared the unit (<c>scheduleConventions.couponRateUnit</c> or a leg's rate unit).</summary>
    RecordDeclared,

    /// <summary>The unit the field declares by default in <see cref="SecurityRateFieldUnits"/>.</summary>
    FieldDefault,

    /// <summary>
    /// The value is zero, which is the same amount in every unit. No unit is inferred: the resolved
    /// fraction is exactly zero whatever the unit would have been.
    /// </summary>
    UnitInvariantZero
}

/// <summary>
/// The typed outcome of reading a stored rate with <see cref="RateUnitNormalizer"/>. A rate is either
/// <see cref="Resolved"/> to a fraction under a declared unit, <see cref="Undeclared"/> (no unit is
/// declared, so the raw value is retained and must be flagged, not converted), or
/// <see cref="OutOfRange"/> (a unit is declared but the value falls outside the field's declared range
/// or negative-rate policy).
/// </summary>
public abstract record RateUnitResolution
{
    private RateUnitResolution(decimal rawValue) => RawValue = rawValue;

    /// <summary>The stored value, exactly as the record holds it.</summary>
    public decimal RawValue { get; }

    /// <summary>True only for <see cref="Resolved"/>, with its fraction.</summary>
    public bool TryGetFraction(out decimal fraction)
    {
        if (this is Resolved resolved)
        {
            fraction = resolved.Fraction;
            return true;
        }

        fraction = 0m;
        return false;
    }

    /// <summary>The rate as a fraction under a declared unit.</summary>
    public sealed record Resolved(decimal Raw, decimal Fraction, RateUnit Unit, RateUnitSource Source) : RateUnitResolution(Raw);

    /// <summary>No unit is declared for the value; magnitude alone never decides one.</summary>
    public sealed record Undeclared(decimal Raw) : RateUnitResolution(Raw);

    /// <summary>A unit is declared, but the converted value breaks the field's declared range or sign policy.</summary>
    public sealed record OutOfRange(decimal Raw, decimal Fraction, RateUnit Unit, RateUnitSource Source, string Reason)
        : RateUnitResolution(Raw);
}

/// <summary>
/// The single reader of stored rate units (security-master review D10, remediation slice 4). Precedence:
/// a unit the record declares (<c>scheduleConventions.couponRateUnit</c>, or a leg's rate unit) wins for
/// fields the record unit governs, then the default the field declares in
/// <see cref="SecurityRateFieldUnits"/>, and otherwise the value is <see cref="RateUnitResolution.Undeclared"/>.
/// The magnitude of a value is never used to choose a unit: a stored 0.5 with no declared unit is not
/// read as fifty percent, and a stored 5.0 is not divided by 100.
/// </summary>
public static class RateUnitNormalizer
{
    /// <summary>Diagnostic code for a rate whose unit is not declared.</summary>
    public const string UndeclaredCode = "RateUnitUndeclared";

    /// <summary>Diagnostic code for a rate outside its field's declared range or sign policy.</summary>
    public const string OutOfRangeCode = "RateOutsideDeclaredRange";

    /// <summary>
    /// Reads <paramref name="value"/> of the rate field <paramref name="fieldKey"/> on
    /// <paramref name="assetClass"/>, honouring <paramref name="recordDeclaredUnit"/> when the field is
    /// governed by the record's unit (or the field is not catalogued).
    /// </summary>
    public static RateUnitResolution Normalize(string? assetClass, string fieldKey, decimal value, RateUnit? recordDeclaredUnit = null)
        => Normalize(SecurityRateFieldUnits.Find(assetClass, fieldKey), value, recordDeclaredUnit);

    /// <summary>Reads <paramref name="value"/> against an already-resolved field declaration.</summary>
    public static RateUnitResolution Normalize(SecurityRateFieldDeclaration? field, decimal value, RateUnit? recordDeclaredUnit = null)
    {
        RateUnit unit;
        RateUnitSource source;
        if (recordDeclaredUnit is { } declared && (field is null || field.HonorsRecordUnit))
        {
            unit = declared;
            source = RateUnitSource.RecordDeclared;
        }
        else if (field?.DefaultUnit is { } fieldDefault)
        {
            unit = fieldDefault;
            source = RateUnitSource.FieldDefault;
        }
        else if (value == 0m)
        {
            return new RateUnitResolution.Resolved(value, 0m, RateUnit.Fraction, RateUnitSource.UnitInvariantZero);
        }
        else
        {
            return new RateUnitResolution.Undeclared(value);
        }

        var fraction = ToFraction(value, unit);
        if (field is not null)
        {
            if (fraction < 0m && !field.AllowsNegative)
            {
                return new RateUnitResolution.OutOfRange(value, fraction, unit, source,
                    $"{field.AssetClass}.{field.FieldKey} does not permit a negative rate.");
            }

            if (field.MinFraction is { } min && fraction < min || field.MaxFraction is { } max && fraction > max)
            {
                return new RateUnitResolution.OutOfRange(value, fraction, unit, source,
                    $"{field.AssetClass}.{field.FieldKey} read as {unit} is {fraction} as a fraction, outside the declared range [{field.MinFraction?.ToString() ?? "-inf"}, {field.MaxFraction?.ToString() ?? "+inf"}].");
            }
        }

        return new RateUnitResolution.Resolved(value, fraction, unit, source);
    }

    /// <summary>Converts a value in <paramref name="unit"/> to a fraction.</summary>
    public static decimal ToFraction(decimal value, RateUnit unit) => unit switch
    {
        RateUnit.Fraction => value,
        RateUnit.Percent => value / 100m,
        RateUnit.BasisPoints => value / 10_000m,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null)
    };

    /// <summary>
    /// The record-declared unit a <c>couponRateUnit</c> token states, or <see langword="null"/> when the
    /// token is absent or not one of the block's closed vocabulary (Percent, Fraction).
    /// </summary>
    public static RateUnit? ParseCouponRateUnit(string? token) => token switch
    {
        "Percent" => RateUnit.Percent,
        "Fraction" => RateUnit.Fraction,
        _ => null
    };

    /// <summary>The record-declared unit of a resolved <see cref="SecurityCouponRateUnit"/>, if any.</summary>
    public static RateUnit? FromCouponRateUnit(SecurityCouponRateUnit unit) => unit switch
    {
        SecurityCouponRateUnit.Percent => RateUnit.Percent,
        SecurityCouponRateUnit.Fraction => RateUnit.Fraction,
        _ => null
    };

    /// <summary>A diagnostic code for a resolution that is not <see cref="RateUnitResolution.Resolved"/>.</summary>
    public static string DiagnosticCode(RateUnitResolution resolution)
        => resolution is RateUnitResolution.OutOfRange ? OutOfRangeCode : UndeclaredCode;

    /// <summary>
    /// The operator-facing reason a resolution that is not <see cref="RateUnitResolution.Resolved"/>
    /// blocks a calculation. The raw value is quoted unconverted.
    /// </summary>
    public static string DescribeRefusal(RateUnitResolution resolution, string? assetClass, string fieldKey)
        => resolution switch
        {
            RateUnitResolution.OutOfRange outOfRange =>
                $"Rate {fieldKey} = {outOfRange.RawValue} cannot be used: {outOfRange.Reason} The value is not converted.",
            RateUnitResolution.Undeclared undeclared =>
                $"Rate {fieldKey} = {undeclared.RawValue} on {assetClass ?? "this record"} has no declared unit (Fraction, Percent or BasisPoints). "
                + "A legacy value is flagged rather than converted; its unit cannot be inferred from its magnitude.",
            _ => string.Empty
        };
}
