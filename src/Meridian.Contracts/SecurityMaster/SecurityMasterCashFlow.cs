using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Identifies the originating source of structured cash flow projections.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StructuredCashFlowSourceKind>))]
public enum StructuredCashFlowSourceKind
{
    MIAC,
    MoodysAnalytics,
    ClientProvided,
    CalculatedBullet,
    CalculatedSinker,
    /// <summary>
    /// Read-tolerance member: a source kind written by a newer node. Matches no provider and no
    /// calculated path, so projections resolve to null (no data) instead of the row failing to read.
    /// </summary>
    Unknown
}

/// <summary>
/// Rate-scenario under which cash flows are projected.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StructuredCashFlowScenario>))]
public enum StructuredCashFlowScenario
{
    Base,
    Up100,
    Up200,
    Up300,
    Down100,
    Down200,
    Down300,
    Stress
}

/// <summary>
/// Freshness classification for the cash flow source backing a projection. Consumers that post to
/// the ledger MUST treat every state other than <see cref="Fresh"/> as a hard gate rather than an
/// advisory log line.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StructuredCashFlowStaleness>))]
public enum StructuredCashFlowStaleness
{
    /// <summary>
    /// The source has no last-updated timestamp, so freshness cannot be evaluated and must not drive
    /// ledger postings.
    /// </summary>
    Unknown,

    /// <summary>The source was refreshed within the staleness threshold.</summary>
    Fresh,

    /// <summary>The source is older than the staleness threshold and must not drive ledger postings.</summary>
    Stale
}

/// <summary>
/// A single typed factor-schedule point: the outstanding pool factor effective on
/// <paramref name="AsOfDate"/>. Replaces the free-text "factor schedule" term for structured
/// (factor-based) instruments so amortization can be seeded from a dated schedule rather than a
/// single scalar factor.
/// </summary>
public sealed record StructuredFactorScheduleEntry(
    DateOnly AsOfDate,
    decimal Factor);

/// <summary>A contractual principal payment retained from the security's economic terms.</summary>
public sealed record StructuredPrincipalScheduleEntry(
    DateOnly PaymentDate,
    decimal Amount);

/// <summary>
/// Per-security assignment of the authoritative cash flow source.
/// </summary>
public sealed record SecurityCashFlowSourceDto(
    Guid SecurityId,
    StructuredCashFlowSourceKind SourceKind,
    DateTimeOffset? LastUpdatedUtc,
    bool IsClientOverride,
    string? ClientConfirmedBy,
    DateTimeOffset? ClientConfirmedAt);

/// <summary>
/// A single period in a structured cash flow schedule.
/// </summary>
public sealed record StructuredCashFlowScheduleEntry(
    DateTimeOffset PeriodDate,
    decimal PrincipalAmount,
    decimal InterestAmount,
    decimal Factor)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? UnadjustedStart { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? UnadjustedEnd { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? AccrualStart { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? AccrualEnd { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Meridian.Contracts.FixedIncome.BondFloatingRateEvidence? RateEvidence { get; init; }
}

/// <summary>How a cash-flow leg's rate is determined.</summary>
public enum CashFlowLegRateKind
{
    Fixed,
    Floating,
}

/// <summary>Which side of a multi-leg structure a leg sits on, from the holder's perspective.</summary>
public enum CashFlowLegDirection
{
    Receive,
    Pay,
}

/// <summary>
/// One leg of a multi-leg or floating-rate cash-flow structure (swap leg, floating-rate note,
/// funding leg). Per-leg terms fall back to the security-level <see cref="StructuredCashFlowTerms"/>
/// values when absent: <see cref="Notional"/> to <c>PrincipalFace</c>, <see cref="PaymentFrequency"/>
/// and <see cref="DayCountConvention"/> to the security-level equivalents. Floating legs reset each
/// future period at the <see cref="SecurityForwardCurve"/> for <see cref="IndexName"/> plus
/// <see cref="SpreadBps"/> when a curve is supplied; otherwise they project flat at
/// <see cref="CurrentIndexRate"/> (the last fixing) plus spread.
/// </summary>
/// <param name="Direction">
/// Null when the source terms carry no direction information. A single directionless leg projects
/// as Receive; a multi-leg structure with any directionless leg cannot be netted honestly, so only
/// per-leg schedules are produced for it.
/// </param>
public sealed record StructuredCashFlowLeg(
    string LegId,
    CashFlowLegRateKind RateKind,
    CashFlowLegDirection? Direction,
    decimal? FixedRate,
    string? IndexName,
    decimal? SpreadBps,
    decimal? CurrentIndexRate,
    decimal? Notional,
    string? PaymentFrequency,
    string? DayCountConvention,
    bool ExchangesPrincipal = false,
    IReadOnlyList<StructuredCashFlowNotionalStep>? NotionalSchedule = null,
    IReadOnlyList<StructuredCashFlowResetFixing>? ResetSchedule = null)
{
    /// <summary>The contractual cap in <see cref="RateUnit"/>; retained separately from forecasts.</summary>
    [JsonIgnore]
    public decimal? CapRate { get; init; }

    /// <summary>The contractual floor in <see cref="RateUnit"/>.</summary>
    [JsonIgnore]
    public decimal? FloorRate { get; init; }

    [JsonIgnore]
    public SecurityCouponRateUnit RateUnit { get; init; }
    [JsonIgnore]
    public bool HasDeclaredResetSchedule { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? CurrentIndexResetDate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentIndexRateSource { get; init; }

    /// <summary>
    /// The notional in effect on <paramref name="date"/>: the latest schedule step on or before
    /// it, otherwise <paramref name="baseNotional"/>.
    /// </summary>
    public decimal NotionalOn(DateOnly date, decimal baseNotional)
        => NotionalSchedule?
               .Where(step => step.EffectiveDate <= date)
               .OrderBy(static step => step.EffectiveDate)
               .LastOrDefault() is { } step
            ? step.Notional
            : baseNotional;

    /// <summary>
    /// The observed fixing of the latest reset on or before <paramref name="date"/>; null when there
    /// is no such reset or its fixing is not yet recorded (an earlier fixing is never carried forward).
    /// </summary>
    public decimal? FixingOn(DateOnly date)
        => ResetSchedule?
            .Where(entry => entry.ResetDate <= date)
            .OrderBy(static entry => entry.ResetDate)
            .LastOrDefault()?.FixingRate;
}

/// <summary>One dated step of an amortizing or accreting leg notional.</summary>
public sealed record StructuredCashFlowNotionalStep(DateOnly EffectiveDate, decimal Notional);

/// <summary>A floating-leg reset date with its observed fixing, when known.</summary>
public sealed record StructuredCashFlowResetFixing(DateOnly ResetDate, decimal? FixingRate);

/// <summary>A single leg's projected schedule, always unsigned (direction is carried alongside).</summary>
/// <param name="RateSource">
/// How the leg's rates were projected: <c>fixed</c>, <c>flatForward:lastFixing</c>, or the forward
/// curve used (<see cref="SecurityForwardCurve.Describe"/>). Null on projections from older nodes.
/// </param>
public sealed record StructuredCashFlowLegSchedule(
    string LegId,
    CashFlowLegRateKind RateKind,
    CashFlowLegDirection? Direction,
    IReadOnlyList<StructuredCashFlowScheduleEntry> Schedule,
    string? RateSource = null);

/// <summary>
/// One business-day calendar a declared schedule (ADR-025) was generated or measured with.
/// </summary>
/// <param name="CalendarId">The calendar id, or a joint id such as <c>GBLO+USNY</c>.</param>
/// <param name="ContentHash">The calendar's content hash, naming exactly which holiday data was used.</param>
/// <param name="Purpose">
/// What the calendar was used for: a <c>ScheduleCalendarPurpose</c> name (<c>Accrual</c>,
/// <c>EffectiveDate</c>, <c>TerminationDate</c> or <c>Payment</c>), or <c>DayCount</c> for the
/// calendar Business/252 counts business days on. A string, because the endpoint options carry no
/// string-enum converter.
/// </param>
public sealed record StructuredCashFlowCalendarEvidence(
    string CalendarId,
    string ContentHash,
    string Purpose);

/// <summary>
/// The dates a calendar was asked about outside its declared coverage while a declared schedule was
/// generated or measured: one row per calendar, from the first to the last such date. A gap is
/// informational (ADR-025 decision 2): a holiday on those dates may be missing from the calendar data.
/// </summary>
/// <param name="CalendarId">The calendar id.</param>
/// <param name="FirstDate">The first date asked outside coverage.</param>
/// <param name="LastDate">The last date asked outside coverage.</param>
public sealed record StructuredCashFlowCoverageGap(
    string CalendarId,
    DateOnly FirstDate,
    DateOnly LastDate);

/// <summary>
/// How a projection's schedule was generated from declared schedule conventions (ADR-025): the kernel
/// and mapping versions, the calendars used and any coverage gaps, so the schedule can be reproduced
/// and its calendar data identified.
/// </summary>
/// <param name="KernelVersion">The contractual schedule kernel version that generated the periods.</param>
/// <param name="DeclaredMappingVersion">The version of the mapping from the declared block to the kernel spec.</param>
/// <param name="Calendars">Each distinct calendar, content hash and purpose used.</param>
/// <param name="CoverageGaps">The calendars asked about dates outside their coverage; empty when none was.</param>
public sealed record StructuredCashFlowScheduleEvidence(
    string KernelVersion,
    string DeclaredMappingVersion,
    IReadOnlyList<StructuredCashFlowCalendarEvidence> Calendars,
    IReadOnlyList<StructuredCashFlowCoverageGap> CoverageGaps);

/// <summary>
/// Full projected cash flow schedule for a security under one rate scenario.
/// </summary>
/// <remarks>
/// The trailing <see cref="Staleness"/>, <see cref="SourceLastUpdatedUtc"/>,
/// <see cref="FactorSchedule"/>, and <see cref="TermsUsed"/> members are additive: existing callers
/// that build a projection from the five leading members keep compiling, while newer consumers can
/// gate on freshness and read typed terms instead of probing free-text term JSON.
/// <see cref="TermsUsed"/> carries the exact resolved terms the schedule was projected from, so
/// downstream ledger math (day-count weighting, maturity clamping) reuses the same economics as the
/// schedule itself instead of re-resolving terms and risking divergence.
/// <see cref="ScheduleEvidence"/> is set only on a schedule generated from declared schedule
/// conventions (ADR-025) and is omitted from JSON otherwise, so an undeclared projection keeps its wire
/// shape; a projection carrying it is not postable to the ledger yet.
/// </remarks>
public sealed record StructuredCashFlowProjectionDto(
    Guid SecurityId,
    StructuredCashFlowSourceKind SourceKind,
    StructuredCashFlowScenario Scenario,
    DateTimeOffset AsOf,
    IReadOnlyList<StructuredCashFlowScheduleEntry> Schedule,
    StructuredCashFlowStaleness Staleness = StructuredCashFlowStaleness.Unknown,
    DateTimeOffset? SourceLastUpdatedUtc = null,
    IReadOnlyList<StructuredFactorScheduleEntry>? FactorSchedule = null,
    StructuredCashFlowTerms? TermsUsed = null,
    IReadOnlyList<StructuredCashFlowLegSchedule>? LegSchedules = null,
    string? BlockedReason = null,
    bool IsNormalizedPer100 = false)
{
    /// <summary>Calendar and kernel evidence for a declared projection; absent on legacy projections.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StructuredCashFlowScheduleEvidence? ScheduleEvidence { get; init; }

    /// <summary>Exact dependencies selected by the context-aware calculation path.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Meridian.Contracts.FixedIncome.BondCalculationInput>? InputManifest { get; init; }
}

/// <summary>
/// One balanced ledger journal line projected from a structured cash flow accrual.
/// </summary>
public sealed record StructuredCashFlowLedgerLine(
    string Account,
    string AccountType,
    string? Symbol,
    string? FinancialAccountId,
    decimal Debit,
    decimal Credit);

/// <summary>
/// A balanced journal projection for one coupon-accrual period, ready to be routed to the ledger.
/// </summary>
public sealed record StructuredCashFlowLedgerPosting(
    DateTimeOffset PeriodDate,
    string Description,
    decimal TotalDebits,
    decimal TotalCredits,
    bool IsBalanced,
    IReadOnlyList<StructuredCashFlowLedgerLine> Lines);

/// <summary>
/// Result of converting a structured cash flow projection into ledger-ready coupon-accrual postings.
/// When <see cref="IsPostable"/> is <see langword="false"/>, <see cref="BlockedReason"/> explains
/// the gate that stopped posting (stale source, non-base scenario, or no accruable periods) and
/// <see cref="Postings"/> is empty.
/// </summary>
public sealed record StructuredCashFlowLedgerPostingResult(
    Guid SecurityId,
    bool IsPostable,
    string? BlockedReason,
    IReadOnlyList<StructuredCashFlowLedgerPosting> Postings)
{
    /// <summary>Creates a blocked result carrying the gate reason and no postings.</summary>
    public static StructuredCashFlowLedgerPostingResult Blocked(Guid securityId, string reason)
        => new(securityId, false, reason, Array.Empty<StructuredCashFlowLedgerPosting>());
}

/// <summary>
/// Request to assign or update the cash flow source for a security.
/// </summary>
public sealed record UpsertCashFlowSourceRequest(
    Guid SecurityId,
    StructuredCashFlowSourceKind SourceKind,
    bool IsClientOverride,
    string? ClientConfirmedBy,
    string Actor,
    // When true, a non-client update is allowed to replace an existing client-provided source.
    // This is the explicit operator path for clearing a stale or expired client override.
    bool Force = false);
