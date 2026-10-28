using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>How a derivative settles at expiry or exercise.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecuritySettlementMethod>))]
public enum SecuritySettlementMethod
{
    /// <summary>Settled by a cash payment of the intrinsic value or final variation.</summary>
    Cash,

    /// <summary>Settled by delivery of the underlying.</summary>
    Physical,

    /// <summary>The holder elects cash or physical settlement.</summary>
    Elective
}

/// <summary>The right an option grants.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityOptionRight>))]
public enum SecurityOptionRight
{
    Call,
    Put
}

/// <summary>
/// The cadence of a contractual payment, reset or sweep. This is the one payment-frequency
/// vocabulary for Security Master terms: the F# <c>PaymentFrequency</c> union labels
/// (<c>Daily</c> … <c>Annual</c>) and the direct-lending <c>PaymentFrequency</c> enum
/// (<c>Monthly</c> … <c>Bullet</c>) both map onto it through
/// <see cref="SecurityTermConventions.TryParsePaymentFrequency"/> and
/// <see cref="SecurityPaymentFrequencyExtensions"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityPaymentFrequency>))]
public enum SecurityPaymentFrequency
{
    Daily,
    Weekly,
    /// <summary>Every two weeks (fortnightly).</summary>
    BiWeekly,
    Monthly,
    /// <summary>Every two months.</summary>
    BiMonthly,
    Quarterly,
    SemiAnnual,
    Annual,
    /// <summary>A single payment at maturity (a bullet or zero-coupon structure).</summary>
    AtMaturity,
    /// <summary>No fixed cadence; the schedule is declared explicitly.</summary>
    Irregular
}

/// <summary>Period arithmetic over <see cref="SecurityPaymentFrequency"/>.</summary>
public static class SecurityPaymentFrequencyExtensions
{
    /// <summary>
    /// Regular periods per year, or null for <see cref="SecurityPaymentFrequency.AtMaturity"/> and
    /// <see cref="SecurityPaymentFrequency.Irregular"/>, which have no fixed cadence.
    /// </summary>
    public static int? PeriodsPerYear(this SecurityPaymentFrequency frequency)
        => frequency switch
        {
            SecurityPaymentFrequency.Daily => 365,
            SecurityPaymentFrequency.Weekly => 52,
            SecurityPaymentFrequency.BiWeekly => 26,
            SecurityPaymentFrequency.Monthly => 12,
            SecurityPaymentFrequency.BiMonthly => 6,
            SecurityPaymentFrequency.Quarterly => 4,
            SecurityPaymentFrequency.SemiAnnual => 2,
            SecurityPaymentFrequency.Annual => 1,
            _ => null
        };

    /// <summary>
    /// Months between regular payments, or null when the cadence is shorter than a month or has no
    /// fixed period.
    /// </summary>
    public static int? MonthsPerPeriod(this SecurityPaymentFrequency frequency)
        => frequency switch
        {
            SecurityPaymentFrequency.Monthly => 1,
            SecurityPaymentFrequency.BiMonthly => 2,
            SecurityPaymentFrequency.Quarterly => 3,
            SecurityPaymentFrequency.SemiAnnual => 6,
            SecurityPaymentFrequency.Annual => 12,
            _ => null
        };

    /// <summary>
    /// The Security Master cadence for a direct-lending payment frequency; the loan enum stays the
    /// persisted direct-lending vocabulary, and this is the one place the two are reconciled.
    /// </summary>
    public static SecurityPaymentFrequency ToSecurityPaymentFrequency(this DirectLending.PaymentFrequency frequency)
        => frequency switch
        {
            DirectLending.PaymentFrequency.Monthly => SecurityPaymentFrequency.Monthly,
            DirectLending.PaymentFrequency.Quarterly => SecurityPaymentFrequency.Quarterly,
            DirectLending.PaymentFrequency.SemiAnnual => SecurityPaymentFrequency.SemiAnnual,
            DirectLending.PaymentFrequency.Annual => SecurityPaymentFrequency.Annual,
            DirectLending.PaymentFrequency.Bullet => SecurityPaymentFrequency.AtMaturity,
            _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Unknown direct-lending payment frequency.")
        };
}

/// <summary>
/// The typed convention a string term field carries. A field declared with a convention keeps its
/// raw text verbatim in the payload, so every value already stored still reads back, but writes and
/// governed edits must name a value the convention's parser recognizes, and consumers read the
/// typed value through <see cref="SecurityTermConventions"/> instead of matching strings.
/// </summary>
public enum SecurityTermConventionKind
{
    None = 0,

    /// <summary>A day-count convention (<see cref="DayCountConvention"/>).</summary>
    DayCount,

    /// <summary>A derivative settlement method (<see cref="SecuritySettlementMethod"/>).</summary>
    SettlementMethod,

    /// <summary>A payment, reset or sweep cadence (<see cref="SecurityPaymentFrequency"/>).</summary>
    PaymentFrequency,

    /// <summary>
    /// A coupon cadence that drives a calculated cash-flow schedule: a <see cref="SecurityPaymentFrequency"/>
    /// whose period is a whole number of months, or <see cref="SecurityPaymentFrequency.AtMaturity"/>.
    /// The schedule engines step coupon periods in months, so a sub-monthly or irregular cadence on
    /// these fields would silently produce the wrong number of coupons.
    /// </summary>
    CouponFrequency,

    /// <summary>
    /// A business-day convention (<see cref="Meridian.Contracts.Calendars.BusinessDayConvention"/>):
    /// how a schedule date that falls on a non-business day is moved.
    /// </summary>
    BusinessDayConvention,

    /// <summary>
    /// A schedule roll (<see cref="Meridian.Contracts.SecurityMaster.Schedules.ScheduleRoll"/>): the
    /// anchor's day, the end of the month, the IMM date or a fixed day of the month from 1 to 31.
    /// </summary>
    RollConvention,

    /// <summary>
    /// What a schedule does with a remainder that does not fill a regular period
    /// (<see cref="Meridian.Contracts.SecurityMaster.Schedules.ScheduleStubPolicy"/>).
    /// </summary>
    StubPolicy
}

/// <summary>Parsers and typed readers for the term conventions.</summary>
public static class SecurityTermConventions
{
    /// <summary>The canonical spellings offered when a form edits a convention field.</summary>
    public static IReadOnlyList<string> CanonicalValues(SecurityTermConventionKind kind)
        => kind switch
        {
            SecurityTermConventionKind.DayCount => Enum.GetValues<DayCountConvention>()
                .Where(static convention => convention != DayCountConvention.Unknown)
                .Select(static convention => convention.ToString())
                .ToArray(),
            SecurityTermConventionKind.SettlementMethod => Enum.GetNames<SecuritySettlementMethod>(),
            SecurityTermConventionKind.PaymentFrequency => Enum.GetNames<SecurityPaymentFrequency>(),
            SecurityTermConventionKind.CouponFrequency => Enum.GetValues<SecurityPaymentFrequency>()
                .Where(static frequency => IsSchedulableCoupon(frequency))
                .Select(static frequency => frequency.ToString())
                .ToArray(),
            SecurityTermConventionKind.BusinessDayConvention => Enum.GetNames<BusinessDayConvention>(),
            SecurityTermConventionKind.RollConvention => CanonicalRollConventions,
            SecurityTermConventionKind.StubPolicy => Enum.GetNames<ScheduleStubPolicy>(),
            _ => []
        };

    // The canonical roll spellings: the three named rolls, then the fixed days of the month.
    private static readonly string[] CanonicalRollConventions =
    [
        nameof(ScheduleRollKind.AnchorDay),
        nameof(ScheduleRollKind.EndOfMonth),
        nameof(ScheduleRollKind.Imm),
        .. Enumerable.Range(1, 31).Select(static day => day.ToString(CultureInfo.InvariantCulture)),
    ];

    /// <summary>Whether <paramref name="raw"/> names a recognized value of the convention.</summary>
    public static bool IsRecognized(SecurityTermConventionKind kind, string? raw)
        => kind switch
        {
            SecurityTermConventionKind.DayCount => DayCountConventions.TryParse(raw, out _),
            SecurityTermConventionKind.SettlementMethod => TryParseSettlementMethod(raw, out _),
            SecurityTermConventionKind.PaymentFrequency => TryParsePaymentFrequency(raw, out _),
            SecurityTermConventionKind.CouponFrequency => TryParsePaymentFrequency(raw, out var frequency) && IsSchedulableCoupon(frequency),
            SecurityTermConventionKind.BusinessDayConvention => TryParseBusinessDayConvention(raw, out _),
            SecurityTermConventionKind.RollConvention => TryParseRollConvention(raw, out _),
            SecurityTermConventionKind.StubPolicy => TryParseStubPolicy(raw, out _),
            _ => true
        };

    /// <summary>
    /// Parses business-day convention spellings: the canonical names and the common abbreviations
    /// (<c>Unadjusted</c>/<c>None</c>, <c>Following</c>/<c>F</c>, <c>ModifiedFollowing</c>/<c>MF</c>,
    /// <c>Preceding</c>/<c>P</c>, <c>ModifiedPreceding</c>/<c>MP</c>). The FpML <c>NEAREST</c> and
    /// <c>FRN</c> conventions have no kernel adjustment and are refused.
    /// </summary>
    public static bool TryParseBusinessDayConvention(string? raw, out BusinessDayConvention convention)
    {
        switch (Normalize(raw))
        {
            case "UNADJUSTED":
            case "NONE":
                convention = BusinessDayConvention.Unadjusted;
                return true;
            case "FOLLOWING":
            case "F":
                convention = BusinessDayConvention.Following;
                return true;
            case "MODIFIEDFOLLOWING":
            case "MODFOLLOWING":
            case "MF":
                convention = BusinessDayConvention.ModifiedFollowing;
                return true;
            case "PRECEDING":
            case "P":
                convention = BusinessDayConvention.Preceding;
                return true;
            case "MODIFIEDPRECEDING":
            case "MODPRECEDING":
            case "MP":
                convention = BusinessDayConvention.ModifiedPreceding;
                return true;
            default:
                convention = default;
                return false;
        }
    }

    /// <summary>
    /// Parses roll spellings: <c>AnchorDay</c>/<c>Anchor</c>, <c>EndOfMonth</c>/<c>EOM</c>, <c>IMM</c>, a
    /// day of the month from 1 to 31, or <c>FixedDay(n)</c>. A day of the month is read from the
    /// trimmed text itself, so <c>"1.5"</c> and <c>"-3"</c> are refused rather than read as 15 and 3.
    /// </summary>
    public static bool TryParseRollConvention(string? raw, out ScheduleRoll roll)
    {
        roll = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed.All(char.IsAsciiDigit))
        {
            return TryFixedDay(trimmed, out roll);
        }

        switch (Normalize(trimmed))
        {
            case "ANCHORDAY":
            case "ANCHOR":
                roll = ScheduleRoll.AnchorDay;
                return true;
            case "ENDOFMONTH":
            case "EOM":
                roll = ScheduleRoll.EndOfMonth;
                return true;
            case "IMM":
                roll = ScheduleRoll.Imm;
                return true;
        }

        const string FixedDayPrefix = "FixedDay(";
        return trimmed.StartsWith(FixedDayPrefix, StringComparison.OrdinalIgnoreCase)
               && trimmed.EndsWith(')')
               && trimmed[FixedDayPrefix.Length..^1].Trim() is { Length: > 0 } day
               && day.All(char.IsAsciiDigit)
               && TryFixedDay(day, out roll);

        static bool TryFixedDay(string digits, out ScheduleRoll fixedDay)
        {
            fixedDay = default;
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var dayOfMonth)
                || dayOfMonth is < 1 or > 31)
            {
                return false;
            }

            fixedDay = ScheduleRoll.FixedDay(dayOfMonth);
            return true;
        }
    }

    /// <summary>
    /// Parses stub-policy spellings: <c>Short</c>/<c>ShortStub</c>, <c>Long</c>/<c>LongStub</c> and
    /// <c>RequireRegular</c>/<c>Regular</c>/<c>NoStub</c>. FpML spellings that also name the stub's
    /// position (<c>ShortInitial</c>, <c>LongFinal</c>) are refused: the position follows from the
    /// generation direction.
    /// </summary>
    public static bool TryParseStubPolicy(string? raw, out ScheduleStubPolicy policy)
    {
        switch (Normalize(raw))
        {
            case "SHORT":
            case "SHORTSTUB":
                policy = ScheduleStubPolicy.Short;
                return true;
            case "LONG":
            case "LONGSTUB":
                policy = ScheduleStubPolicy.Long;
                return true;
            case "REQUIREREGULAR":
            case "REGULAR":
            case "NOSTUB":
                policy = ScheduleStubPolicy.RequireRegular;
                return true;
            default:
                policy = default;
                return false;
        }
    }

    /// <summary>
    /// Whether a cadence can drive a calculated coupon schedule: a whole number of months per period,
    /// or a single payment at maturity.
    /// </summary>
    public static bool IsSchedulableCoupon(SecurityPaymentFrequency frequency)
        => frequency == SecurityPaymentFrequency.AtMaturity || frequency.MonthsPerPeriod() is not null;

    /// <summary>
    /// Coupon payments per year for a schedule-driving cadence spelling, or null when the text is not
    /// a recognized monthly-or-longer cadence. <see cref="LegacyCouponPaymentsPerYear"/>, the reader the
    /// cash-flow and asset-operations schedulers share, consults it first, so they agree with the
    /// convention the write path enforces.
    /// </summary>
    public static int? CouponPaymentsPerYear(string? raw)
        => TryParsePaymentFrequency(raw, out var frequency) && frequency.MonthsPerPeriod() is { } months
            ? 12 / months
            : null;

    /// <summary>
    /// Coupon payments per year as the legacy schedulers read a stored cadence. The Security Master
    /// cash-flow projection and the asset-operations bond projection
    /// (<c>fixed-income-cash-flow-projection-v1</c>) both step <c>Math.Max(1, 12 / n)</c> months per
    /// coupon period from it. It never returns null and never refuses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A spelling <see cref="CouponPaymentsPerYear"/> recognizes reads the same here. Anything else
    /// keeps the reading those schedulers had before the convention existed, so a stored value
    /// projects exactly as it always has:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Blank text, <see cref="SecurityPaymentFrequency.AtMaturity"/>,
    /// <see cref="SecurityPaymentFrequency.Irregular"/>, the sub-monthly cadence names and tenors
    /// (<c>Daily</c>, <c>Weekly</c>, <c>BiWeekly</c>, <c>2W</c>) and unrecognized text read as 1.</description></item>
    /// <item><description>Any other positive whole number, once hyphens, underscores and spaces are
    /// removed, is a payments-per-year count clamped to 1 through 12: <c>"5"</c> reads as 5, and
    /// <c>"13"</c> and the numeric sub-monthly spellings <c>"52"</c> and <c>"365"</c> as 12. Zero, a
    /// number too large for an <see cref="int"/> and a decimal such as <c>"1.5"</c> read as 1. The
    /// convention drops separators before it is consulted, so <c>"1.2"</c> reads as <c>"12"</c>, which
    /// is 12.</description></item>
    /// </list>
    /// <para>
    /// Reading <c>AtMaturity</c> as one payment a year is the pinned defect SCH-DEF-001, kept for records
    /// that declare no schedule conventions. New readers use <see cref="CouponPaymentsPerYear"/> or
    /// <see cref="TryParsePaymentFrequency"/>, which say when they do not recognize a value.
    /// </para>
    /// </remarks>
    public static int LegacyCouponPaymentsPerYear(string? paymentFrequency)
    {
        if (string.IsNullOrWhiteSpace(paymentFrequency))
        {
            return 1;
        }

        // The write path admits only the cadences the shared convention recognizes; read them the
        // same way (this adds BiMonthly and tenor spellings such as "3M"). Anything else keeps the
        // legacy interpretation below so stored values read exactly as before.
        if (SecurityTermConventions.CouponPaymentsPerYear(paymentFrequency) is { } paymentsPerYear)
        {
            return paymentsPerYear;
        }

        var normalized = paymentFrequency
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
        if (int.TryParse(normalized, out var parsed) && parsed > 0)
        {
            return Math.Clamp(parsed, 1, 12);
        }

        return normalized switch
        {
            "MONTHLY" => 12,
            "QUARTERLY" => 4,
            "SEMIANNUAL" or "SEMIANNUALLY" or "SEMIYEARLY" or "HALFYEARLY" => 2,
            "ANNUAL" or "ANNUALLY" or "YEARLY" => 1,
            _ => 1
        };
    }

    /// <summary>The shared rejection reason for an unrecognized convention value.</summary>
    public static string DescribeUnrecognized(SecurityTermConventionKind kind, string assetClass, string key, string? raw)
        => $"Value '{raw}' is not a recognized {Describe(kind)} for '{key}' on asset class '{assetClass}'. " +
           $"Recognized values include: {string.Join(", ", CanonicalValues(kind))}.";

    /// <summary>Parses settlement-method spellings (Cash, Cash settled, Physical, Physical delivery, Delivery, Elective).</summary>
    public static bool TryParseSettlementMethod(string? raw, out SecuritySettlementMethod method)
    {
        var token = Normalize(raw);
        switch (token)
        {
            case "CASH":
            case "CASHSETTLED":
            case "CASHSETTLEMENT":
                method = SecuritySettlementMethod.Cash;
                return true;
            case "PHYSICAL":
            case "PHYSICALDELIVERY":
            case "PHYSICALLYSETTLED":
            case "DELIVERY":
            case "DELIVERABLE":
                method = SecuritySettlementMethod.Physical;
                return true;
            case "ELECTIVE":
            case "CASHORPHYSICAL":
                method = SecuritySettlementMethod.Elective;
                return true;
            default:
                method = default;
                return false;
        }
    }

    /// <summary>
    /// Parses payment-frequency spellings: the canonical names, common prose variants
    /// ("Semi-Annual", "half-yearly", "fortnightly", "bullet", "zero coupon"), ISDA-style period
    /// tenors ("1M", "3M", "6M", "1Y", "2W") and payments-per-year counts ("12", "4", "2", "1").
    /// Every spelling the cash-flow, accounting and amortization resolvers accept parses here, so a
    /// written value can never be accepted by validation and then read as an unknown cadence.
    /// </summary>
    public static bool TryParsePaymentFrequency(string? raw, out SecurityPaymentFrequency frequency)
    {
        var token = Normalize(raw);
        switch (token)
        {
            case "DAILY":
            case "DAY":
            case "1D":
            case "365":
                frequency = SecurityPaymentFrequency.Daily;
                return true;
            case "WEEKLY":
            case "WEEK":
            case "1W":
            case "52":
                frequency = SecurityPaymentFrequency.Weekly;
                return true;
            case "BIWEEKLY":
            case "FORTNIGHTLY":
            case "EVERYTWOWEEKS":
            case "2W":
            case "26":
                frequency = SecurityPaymentFrequency.BiWeekly;
                return true;
            case "MONTHLY":
            case "MONTH":
            case "1M":
            case "12":
                frequency = SecurityPaymentFrequency.Monthly;
                return true;
            case "BIMONTHLY":
            case "EVERYTWOMONTHS":
            case "2M":
            case "6":
                frequency = SecurityPaymentFrequency.BiMonthly;
                return true;
            case "QUARTERLY":
            case "QUARTER":
            case "3M":
            case "4":
                frequency = SecurityPaymentFrequency.Quarterly;
                return true;
            case "SEMIANNUAL":
            case "SEMIANNUALLY":
            case "SEMIYEARLY":
            case "HALFYEARLY":
            case "HALFYEAR":
            case "BIANNUAL":
            case "6M":
            case "2":
                frequency = SecurityPaymentFrequency.SemiAnnual;
                return true;
            case "ANNUAL":
            case "ANNUALLY":
            case "YEARLY":
            case "YEAR":
            case "12M":
            case "1Y":
            case "1":
                frequency = SecurityPaymentFrequency.Annual;
                return true;
            case "ATMATURITY":
            case "MATURITY":
            case "BULLET":
            case "ZEROCOUPON":
            case "ZERO":
            case "SINGLE":
            case "ONCE":
                frequency = SecurityPaymentFrequency.AtMaturity;
                return true;
            case "IRREGULAR":
            case "CUSTOM":
            case "SCHEDULED":
            case "PERSCHEDULE":
                frequency = SecurityPaymentFrequency.Irregular;
                return true;
            default:
                frequency = default;
                return false;
        }
    }

    /// <summary>Parses an option right ("Call"/"Put", case-insensitive, also "C"/"P").</summary>
    public static bool TryParseOptionRight(string? raw, out SecurityOptionRight right)
    {
        switch (Normalize(raw))
        {
            case "CALL":
            case "C":
                right = SecurityOptionRight.Call;
                return true;
            case "PUT":
            case "P":
                right = SecurityOptionRight.Put;
                return true;
            default:
                right = default;
                return false;
        }
    }

    /// <summary>The typed day count a terms document declares at <paramref name="key"/>, or null.</summary>
    public static DayCountConvention? ReadDayCount(JsonElement terms, string key = "dayCount")
        => ReadString(terms, key) is { } raw && DayCountConventions.TryParse(raw, out var convention) ? convention : null;

    /// <summary>The typed settlement method a terms document declares, or null.</summary>
    public static SecuritySettlementMethod? ReadSettlementMethod(JsonElement terms, string key = "settlementType")
        => ReadString(terms, key) is { } raw && TryParseSettlementMethod(raw, out var method) ? method : null;

    /// <summary>The typed payment frequency a terms document declares at <paramref name="key"/>, or null.</summary>
    public static SecurityPaymentFrequency? ReadPaymentFrequency(JsonElement terms, string key = "paymentFrequency")
        => ReadString(terms, key) is { } raw && TryParsePaymentFrequency(raw, out var frequency) ? frequency : null;

    /// <summary>The typed option right an option's terms declare, or null.</summary>
    public static SecurityOptionRight? ReadOptionRight(JsonElement terms, string key = "putCall")
        => ReadString(terms, key) is { } raw && TryParseOptionRight(raw, out var right) ? right : null;

    private static string? ReadString(JsonElement terms, string key)
        => terms.ValueKind == JsonValueKind.Object
           && terms.TryGetProperty(key, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Describe(SecurityTermConventionKind kind)
        => kind switch
        {
            SecurityTermConventionKind.DayCount => "day-count convention",
            SecurityTermConventionKind.SettlementMethod => "settlement method",
            SecurityTermConventionKind.PaymentFrequency => "payment frequency",
            SecurityTermConventionKind.CouponFrequency => "coupon frequency (monthly or longer, or at maturity)",
            SecurityTermConventionKind.BusinessDayConvention => "business-day convention",
            SecurityTermConventionKind.RollConvention => "roll convention (AnchorDay, EndOfMonth, Imm or a day of the month from 1 to 31)",
            SecurityTermConventionKind.StubPolicy => "stub policy",
            _ => "value"
        };

    private static string Normalize(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? string.Empty
            : new string(raw.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
