using System.Text.Json.Serialization;

namespace Meridian.Contracts.DirectLending;

/// <summary>
/// Whether PIK applies on every accrual day or only while the borrower's PIK toggle is elected.
/// See <c>docs/engineering/blueprints/direct-lending-pik-capitalization-and-rate-resets.md</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PikElection>))]
public enum PikElection : byte
{
    /// <summary>Contractual PIK: the PIK share applies every accrual day.</summary>
    Always = 0,

    /// <summary>PIK toggle: the PIK share applies only while <see cref="LoanServicingStateDto.IsPikToggled"/> is set.</summary>
    WhenToggled = 1
}

/// <summary>How often accrued PIK interest is capitalized into principal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PikCapitalizationFrequency>))]
public enum PikCapitalizationFrequency : byte
{
    Monthly = 0,
    Quarterly = 1,
    SemiAnnual = 2,
    Annual = 3,

    /// <summary>Capitalize on the loan's interest payment dates (<see cref="DirectLendingTermsDto.PaymentFrequency"/>).</summary>
    OnInterestPaymentDate = 4
}

/// <summary>How often a floating rate resets.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RateResetFrequency>))]
public enum RateResetFrequency : byte
{
    Monthly = 0,
    Quarterly = 1,
    SemiAnnual = 2,
    Annual = 3
}

/// <summary>How a fixing lookback counts days.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LookbackDayKind>))]
public enum LookbackDayKind : byte
{
    /// <summary>Calendar days.</summary>
    Calendar = 0,

    /// <summary>Weekdays (Monday to Friday). No holiday calendar is applied yet.</summary>
    Business = 1
}

/// <summary>What a reset schedule's floor and cap bound.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FloorCapApplication>))]
public enum FloorCapApplication : byte
{
    /// <summary>Bound the index fixing before the spread is added (the usual leveraged-loan floor).</summary>
    Index = 0,

    /// <summary>Bound the all-in rate (index plus spread).</summary>
    AllIn = 1
}

/// <summary>Whether a rate reset came from an operator request or the reset schedule.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RateResetKind>))]
public enum RateResetKind : byte
{
    Manual = 0,
    Scheduled = 1
}

/// <summary>
/// Contractual PIK capitalization rule. Accrued PIK interest is carried as a PIK receivable and
/// moved into principal on each capitalization date, so it compounds from the following day.
/// </summary>
/// <param name="Mode">Whether PIK always applies or only while the PIK toggle is elected.</param>
/// <param name="PikShare">Share of each day's interest paid in kind (declared unit, normalizes to [0, 1]).</param>
/// <param name="Frequency">Capitalization cadence.</param>
/// <param name="AnchorDate">First capitalization date; later dates step from it by <paramref name="Frequency"/>.</param>
public sealed record PikCapitalizationPolicy(
    PikElection Mode,
    DeclaredRate PikShare,
    PikCapitalizationFrequency Frequency,
    DateOnly AnchorDate);

/// <summary>
/// Floating-rate reset schedule. On each reset date the rate is set from the index fixing observed
/// <paramref name="LookbackDays"/> earlier; a missing fixing blocks accrual (fail closed).
/// </summary>
/// <param name="IndexName">Benchmark name; must match <see cref="DirectLendingTermsDto.InterestIndexName"/>.</param>
/// <param name="Frequency">Reset cadence from <paramref name="FirstResetDate"/>.</param>
/// <param name="FirstResetDate">First reset date (usually the origination or first drawdown date).</param>
/// <param name="LookbackDays">Days between the fixing observation date and the reset date (0 or more).</param>
/// <param name="LookbackDayKind">Whether <paramref name="LookbackDays"/> counts calendar days or weekdays.</param>
/// <param name="Spread">Margin over the index, in its declared unit.</param>
/// <param name="Floor">Optional floor, in its declared unit.</param>
/// <param name="Cap">Optional cap, in its declared unit.</param>
/// <param name="FloorCapAppliesTo">Whether the floor and cap bound the index or the all-in rate.</param>
/// <param name="ResetDates">Optional explicit reset dates; when set they replace the generated cadence.</param>
public sealed record FloatingRateResetSchedule(
    string IndexName,
    RateResetFrequency Frequency,
    DateOnly FirstResetDate,
    int LookbackDays,
    LookbackDayKind LookbackDayKind,
    DeclaredRate Spread,
    DeclaredRate? Floor = null,
    DeclaredRate? Cap = null,
    FloorCapApplication FloorCapAppliesTo = FloorCapApplication.Index,
    IReadOnlyList<DateOnly>? ResetDates = null);

/// <summary>One capitalization of accrued PIK interest into principal.</summary>
public sealed record PikCapitalizationDto(
    DateOnly CapitalizationDate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal Amount,
    DateTimeOffset RecordedAt);

/// <summary>An observed index fixing held in the direct-lending fixing store.</summary>
public sealed record DirectLendingIndexFixingDto(
    Guid FixingId,
    string IndexName,
    DateOnly ObservationDate,
    int Revision,
    DeclaredRate DeclaredRate,
    decimal RateFraction,
    string? SourceRef,
    DateTimeOffset RecordedAt,
    string? RecordedBy);

/// <summary>Records (or revises) one index fixing. A later revision for the same index and date supersedes earlier ones.</summary>
public sealed record RecordIndexFixingRequest(
    string IndexName,
    DateOnly ObservationDate,
    DeclaredRate Rate,
    string? SourceRef,
    string? RecordedBy = null);

/// <summary>Applies the reset scheduled for <paramref name="ResetDate"/> from the fixing store.</summary>
public sealed record ApplyScheduledRateResetRequest(DateOnly ResetDate);

/// <summary>Capitalizes accrued PIK interest on <paramref name="CapitalizationDate"/>.</summary>
public sealed record CapitalizePikRequest(DateOnly CapitalizationDate);

/// <summary>One upcoming reset date and the fixing it needs.</summary>
public sealed record ScheduledRateResetPreviewDto(
    DateOnly ResetDate,
    DateOnly FixingObservationDate,
    string IndexName,
    bool FixingAvailable,
    decimal? FixingRate,
    bool Applied);

/// <summary>Read-only view of a loan's automatic servicing schedule.</summary>
public sealed record LoanServicingSchedulePreviewDto(
    Guid LoanId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ScheduledRateResetPreviewDto> RateResets,
    IReadOnlyList<DateOnly> PikCapitalizationDates,
    decimal PikInterestAccruedUnpaid);
