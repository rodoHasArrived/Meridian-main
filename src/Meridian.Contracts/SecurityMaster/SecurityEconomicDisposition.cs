namespace Meridian.Contracts.SecurityMaster;

/// <summary>How one consumer treats one declared economic structure.</summary>
public enum SecurityEconomicDispositionKind
{
    /// <summary>The consumer computes economics for the structure.</summary>
    Supported,

    /// <summary>The consumer refuses the structure with <see cref="SecurityEconomicDisposition.Code"/>.</summary>
    Blocked,

    /// <summary>The structure carries no economics for this consumer (for example a zero coupon's accrual).</summary>
    NotApplicable
}

/// <summary>A consumer's declared treatment of one structure.</summary>
public sealed record SecurityEconomicDisposition(
    SecurityEconomicDispositionKind Kind,
    string? Code = null,
    string? Reason = null)
{
    public static SecurityEconomicDisposition Supported { get; } = new(SecurityEconomicDispositionKind.Supported);

    public static SecurityEconomicDisposition NotApplicable(string reason) => new(SecurityEconomicDispositionKind.NotApplicable, null, reason);

    public static SecurityEconomicDisposition Blocked(string code, string reason) => new(SecurityEconomicDispositionKind.Blocked, code, reason);
}

/// <summary>
/// Review A3: one declared answer per (consumer, coupon structure) instead of scattered
/// <c>CouponType == "Fixed"</c> checks. A consumer dispatches through <see cref="ForCouponStructure"/>;
/// a structure the table does not list is BLOCKED, never silently passed. A coverage test enumerates
/// every case of the F# <c>BondCouponStructure</c> union, so adding a case without declaring it here fails.
/// </summary>
public static class SecurityEconomicDispositions
{
    /// <summary>Consumer id for expected coupon accrual and receipt generation.</summary>
    public const string AccountingAccrual = "SecurityMasterAccountingEventService";

    /// <summary>Issue code for a declared coupon structure a consumer does not support.</summary>
    public const string CouponStructureUnsupported = "SM_COUPON_STRUCTURE_UNSUPPORTED";

    /// <summary>Issue code for a floating coupon that needs reset terms before accrual.</summary>
    public const string RateResetTermsMissing = "SM_RATE_RESET_TERMS_MISSING";

    /// <summary>The coupon structure names the Bond codec writes (<c>couponType</c>).</summary>
    public static IReadOnlyList<string> CouponStructures { get; } = ["Fixed", "Floating", "ZeroCoupon", "Step", "InflationLinked"];

    private static readonly IReadOnlyDictionary<(string Consumer, string Structure), SecurityEconomicDisposition> Table =
        new Dictionary<(string, string), SecurityEconomicDisposition>
        {
            [(AccountingAccrual, "Fixed")] = SecurityEconomicDisposition.Supported,
            [(AccountingAccrual, "Floating")] = SecurityEconomicDisposition.Blocked(
                RateResetTermsMissing, "is floating-rate and requires reset terms before expected accruals can be generated."),
            [(AccountingAccrual, "ZeroCoupon")] = SecurityEconomicDisposition.NotApplicable("A zero-coupon bond pays no coupon to accrue."),
            [(AccountingAccrual, "Step")] = SecurityEconomicDisposition.Blocked(
                CouponStructureUnsupported, "declares coupon structure 'Step' (a dated step-rate schedule), which expected-accrual generation does not support."),
            [(AccountingAccrual, "InflationLinked")] = SecurityEconomicDisposition.Blocked(
                CouponStructureUnsupported, "declares coupon structure 'InflationLinked' (a real rate on index-adjusted principal), which expected-accrual generation does not support."),
        };

    /// <summary>The consumers this table declares.</summary>
    public static IReadOnlyList<string> Consumers { get; } = [AccountingAccrual];

    /// <summary>Whether the table declares the pair explicitly (coverage tests use this).</summary>
    public static bool IsDeclared(string consumer, string couponStructure)
        => Table.ContainsKey((consumer, couponStructure));

    /// <summary>
    /// The declared disposition, matching the structure case-insensitively. An undeclared structure is
    /// <see cref="SecurityEconomicDispositionKind.Blocked"/> with <see cref="CouponStructureUnsupported"/>.
    /// </summary>
    public static SecurityEconomicDisposition ForCouponStructure(string consumer, string couponStructure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        var trimmed = (couponStructure ?? string.Empty).Trim();
        var canonical = CouponStructures.FirstOrDefault(name => name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
        return Table.TryGetValue((consumer, canonical), out var disposition)
            ? disposition
            : SecurityEconomicDisposition.Blocked(
                CouponStructureUnsupported,
                $"declares coupon structure '{trimmed}', which this consumer does not declare a treatment for.");
    }
}
