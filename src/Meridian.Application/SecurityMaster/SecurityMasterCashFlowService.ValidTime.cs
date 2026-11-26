using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// ADR-034 consumer migration: period-correct cash-flow generation. A schedule row takes the terms that
/// were valid at its accrual start (as known now), so a back-dated coupon correction changes the rows it
/// covers and leaves earlier rows on the terms that applied to them.
/// </summary>
public sealed partial class SecurityMasterCashFlowService
{
    /// <summary>Refusal code when a term change cannot be composed into one period-correct schedule.</summary>
    public const string ValidTimeCompositionUnsupportedCode = "ValidTimeCompositionUnsupported";

    /// <summary>
    /// Recomposes a calculated projection from the security's valid-time term periods. Returns
    /// <paramref name="latestProjection"/> unchanged when no reader is composed, or when the economic
    /// terms never changed in valid time (identifier-only amendments, events written before ADR-034).
    /// </summary>
    internal async Task<StructuredCashFlowProjectionDto> ComposeValidTimeProjectionAsync(
        SecurityDetailDto latest,
        StructuredCashFlowProjectionDto latestProjection,
        DateTimeOffset asOf,
        Func<SecurityDetailDto, StructuredCashFlowProjectionDto> project,
        CancellationToken ct)
    {
        if (_validTime is null)
            return latestProjection;

        var timeline = await _validTime.GetTimelineAsync(latest.SecurityId, knownAt: null, ct).ConfigureAwait(false);
        var periods = MergeEconomicallyEqualPeriods(timeline);
        if (periods.Count <= 1)
            return latestProjection;

        var projections = new List<(SecurityTermsValidTimeSegment Period, StructuredCashFlowProjectionDto Projection)>(periods.Count);
        foreach (var period in periods)
        {
            // The period carrying the projection row's version keeps the projection row itself, so its
            // terms are read exactly as the single-terms path reads them.
            var detail = period.StreamVersion == latest.Version
                ? latest
                : SecurityMasterValidTimeQuery.ToDetail(period.Definition);
            var periodProjection = project(detail);
            if (periodProjection.BlockedReason is { } blocked)
            {
                return latestProjection with
                {
                    Schedule = [],
                    BlockedReason = $"The terms valid from {period.ValidFrom:yyyy-MM-dd} (version {period.StreamVersion}) cannot be projected: {blocked}"
                };
            }

            projections.Add((period, periodProjection));
        }

        var legIds = projections
            .Select(static item => string.Join("|", (item.Projection.LegSchedules ?? []).Select(static leg => leg.LegId).OrderBy(static id => id, StringComparer.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (legIds > 1)
        {
            return Refuse(latestProjection, "the legs differ between term periods, so their rows cannot be joined into one schedule.");
        }

        var governing = projections.LastOrDefault(item => item.Period.ValidFrom <= asOf);
        if (governing.Projection is null)
            governing = projections[0];

        var schedule = projections
            .SelectMany(item => item.Projection.Schedule.Where(row => Covers(projections, item.Period, RowKey(row))))
            .OrderBy(static row => row.PeriodDate)
            .ToArray();

        var legSchedules = governing.Projection.LegSchedules is { Count: > 0 } governingLegs
            ? governingLegs
                .Select(leg => leg with
                {
                    Schedule = projections
                        .SelectMany(item => (item.Projection.LegSchedules ?? [])
                            .Where(candidate => string.Equals(candidate.LegId, leg.LegId, StringComparison.Ordinal))
                            .SelectMany(candidate => candidate.Schedule)
                            .Where(row => Covers(projections, item.Period, RowKey(row))))
                        .OrderBy(static row => row.PeriodDate)
                        .ToArray()
                })
                .ToArray()
            : governing.Projection.LegSchedules;

        var factorSchedule = governing.Projection.FactorSchedule is null
            ? null
            : projections
                .SelectMany(item => (item.Projection.FactorSchedule ?? [])
                    .Where(row => Covers(projections, item.Period, row.AsOfDate)))
                .OrderBy(static row => row.AsOfDate)
                .ToArray();

        return governing.Projection with
        {
            Schedule = schedule,
            LegSchedules = legSchedules,
            FactorSchedule = factorSchedule,
            TermsPeriods = projections
                .Select(static item => new StructuredCashFlowTermsPeriod(item.Period.StreamVersion, item.Period.ValidFrom, item.Period.ValidTo))
                .ToArray()
        };
    }

    /// <summary>
    /// Joins consecutive periods whose economic content is identical, so an amendment that only added
    /// identifiers does not split the schedule. The joined period keeps the earlier valid-from and the
    /// later state.
    /// </summary>
    internal static IReadOnlyList<SecurityTermsValidTimeSegment> MergeEconomicallyEqualPeriods(
        IReadOnlyList<SecurityTermsValidTimeSegment> timeline)
    {
        var merged = new List<SecurityTermsValidTimeSegment>(timeline.Count);
        foreach (var segment in timeline.OrderBy(static item => item.ValidFrom))
        {
            if (merged.Count > 0 && SecurityTermsValidTime.EconomicallyEqual(merged[^1].Definition, segment.Definition))
            {
                merged[^1] = segment with { ValidFrom = merged[^1].ValidFrom };
                continue;
            }

            merged.Add(segment);
        }

        return merged;
    }

    /// <summary>A row belongs to the period valid at its accrual start: a mid-period change applies from the next period.</summary>
    private static DateOnly RowKey(StructuredCashFlowScheduleEntry row)
        => row.AccrualStart ?? row.UnadjustedStart ?? DateOnly.FromDateTime(row.PeriodDate.UtcDateTime);

    /// <summary>
    /// True when <paramref name="date"/> belongs to <paramref name="period"/>. Dates before the first
    /// valid-from belong to the first period: a security created after its issue date has no earlier
    /// terms, so its earliest recorded terms apply to its earliest rows.
    /// </summary>
    private static bool Covers(
        IReadOnlyList<(SecurityTermsValidTimeSegment Period, StructuredCashFlowProjectionDto Projection)> projections,
        SecurityTermsValidTimeSegment period,
        DateOnly date)
    {
        var instant = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return period.Covers(instant)
               || (ReferenceEquals(period, projections[0].Period) && instant < period.ValidFrom);
    }

    private static StructuredCashFlowProjectionDto Refuse(StructuredCashFlowProjectionDto latestProjection, string reason)
        => latestProjection with
        {
            Schedule = [],
            BlockedReason = $"{ValidTimeCompositionUnsupportedCode}: the security's terms changed in valid time and {reason}"
        };
}
