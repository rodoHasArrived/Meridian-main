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
    decimal Factor);

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
/// and <see cref="DayCountConvention"/> to the security-level equivalents. Floating legs project at
/// <see cref="CurrentIndexRate"/> (the last fixing) plus <see cref="SpreadBps"/> — a deliberate
/// flat-forward simplification; no forward curve is implied.
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
    bool ExchangesPrincipal = false);

/// <summary>A single leg's projected schedule, always unsigned (direction is carried alongside).</summary>
public sealed record StructuredCashFlowLegSchedule(
    string LegId,
    CashFlowLegRateKind RateKind,
    CashFlowLegDirection? Direction,
    IReadOnlyList<StructuredCashFlowScheduleEntry> Schedule);

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
    IReadOnlyList<StructuredCashFlowLegSchedule>? LegSchedules = null);

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
