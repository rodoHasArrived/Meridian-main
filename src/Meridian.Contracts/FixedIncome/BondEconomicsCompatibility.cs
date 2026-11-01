namespace Meridian.Contracts.FixedIncome;

/// <summary>
/// Selects which retained arithmetic the shared bond economics kernels reproduce. Every retained
/// calculation version maps to exactly one mode so historical receipts replay bit-identically.
/// </summary>
public enum BondEconomicsCompatibility
{
    /// <summary>
    /// Current declared economics: Level or DayCount accrued coupon through reference-period units,
    /// payment-lag discount time measured from the accrual end, and refusal of an exercise that
    /// crosses an earned but unpaid coupon.
    /// </summary>
    Current,

    /// <summary>
    /// <c>bond-analytics-v1</c> arithmetic retained for settlement-receipt verification: accrued
    /// coupon is the whole coupon prorated by the day-count fraction, discount time runs to the
    /// payment date, and exercise accrual and time are prorated on the exercise date without the
    /// delayed-coupon entitlement refusal.
    /// </summary>
    AnalyticsV1
}
