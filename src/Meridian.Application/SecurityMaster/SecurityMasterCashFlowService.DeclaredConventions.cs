using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// The declared mode of the calculated projection (ADR-025): a record whose declared schedule
/// conventions <see cref="ScheduleSpecFactory"/> maps to a <see cref="ScheduleSpec"/> projects the
/// periods <see cref="ScheduleGenerator"/> generates, accrues each through
/// <see cref="ScheduleDayCount"/> with its period context, and carries
/// <see cref="StructuredCashFlowScheduleEvidence"/>. Coupon, face, factor and day count still come
/// from <see cref="StructuredCashFlowTermsResolver"/>, and principal follows the bullet, sinker and
/// asserted-bullet rules of the legacy walk.
/// </summary>
/// <remarks>
/// Nothing here throws for a declared spec, and nothing re-evaluates the grid date that would close a
/// schedule: a spec the kernel refuses blocks the projection with the kernel's reason.
/// </remarks>
public sealed partial class SecurityMasterCashFlowService
{
    private const string DeclaredContractualPrincipalReason =
        "Declared schedule conventions are not yet followed for a contractual principal schedule; the projection is blocked.";

    private const string DeclaredDayCountReason =
        "Declared schedule conventions need a recognized dayCount to compute interest";

    private const string DeclaredKernelReason = "Declared schedule could not be generated: ";

    private const string DeclaredLevelReason =
        "Declared couponCashBasis 'Level' needs a periodic paymentFrequency.";

    private const string DeclaredInterestReason = "Declared schedule interest could not be computed: ";

    // The block's closed vocabularies, which ScheduleSpecFactory has already checked exactly against
    // the block schema; an absent member keeps the legacy reading.
    private const string PercentCouponRateUnit = "Percent";
    private const string FractionCouponRateUnit = "Fraction";
    private const string LevelCouponCashBasis = "Level";

    // The evidence purpose of the calendar Business/252 counts business days on, which the kernel does
    // not record because the day count reads the spec's calendar directly.
    private const string DayCountCalendarPurpose = "DayCount";

    private static StructuredCashFlowProjectionDto BuildDeclaredProjection(
        SecurityDetailDto security,
        StructuredCashFlowSourceKind sourceKind,
        StructuredCashFlowScenario scenario,
        StructuredCashFlowStaleness staleness,
        DateTimeOffset? sourceLastUpdatedUtc,
        DateTimeOffset asOf,
        DateOnly asOfDate,
        StructuredCashFlowTerms terms,
        DeclaredScheduleResolution declared,
        decimal principalBasis,
        decimal openingOutstanding,
        bool hasContractualPrincipal,
        bool assertedBulletSchedule)
    {
        StructuredCashFlowProjectionDto Empty(string? reason = null) => new(
            security.SecurityId, sourceKind, scenario, asOf, [], staleness, sourceLastUpdatedUtc, terms.FactorSchedule, terms, BlockedReason: reason);

        // 1. Contractual principal instalments are not merged into a declared schedule yet. An asserted
        // bullet (principalSchedule: []) and the calculated bullet and sinker walks are followed.
        if (hasContractualPrincipal)
        {
            return Empty(DeclaredContractualPrincipalReason);
        }

        var block = declared.Conventions;
        var spec = declared.Spec!;

        // 2. A declared coupon unit replaces the legacy heuristic that reads a coupon at or below 1 as a
        // fraction (SCH-DEF-003); without one the heuristic stays.
        var coupon = terms.CouponRate.GetValueOrDefault();
        var annualRate = block.CouponRateUnit switch
        {
            PercentCouponRateUnit => coupon / 100m,
            FractionCouponRateUnit => coupon,
            _ => NormalizeAnnualRate(coupon)
        } + ScenarioRateShift(scenario);
        if (annualRate < 0m)
        {
            annualRate = 0m;
        }

        // 3. A Level coupon pays rate / periods-per-year on each regular period; otherwise every period
        // accrues by its day-count fraction.
        var levelBasis = block.CouponCashBasis == LevelCouponCashBasis;

        // 4. Interest needs a recognized day count; the Actual/365 fallback is never assumed.
        var dayCount = DayCountConvention.Unknown;
        if (annualRate > 0m
            && (!DayCountConventions.TryParse(terms.DayCountConvention, out dayCount) || dayCount == DayCountConvention.Unknown))
        {
            return Empty(string.IsNullOrWhiteSpace(terms.DayCountConvention)
                ? $"{DeclaredDayCountReason}; the record carries none."
                : $"{DeclaredDayCountReason}; '{terms.DayCountConvention}' is not one.");
        }

        // 5. The kernel's refusal blocks the projection, including the period bound a far-dated or
        // perpetual-sentinel maturity reaches (SCH-DEF-010).
        var schedule = ScheduleGenerator.Generate(spec);
        if (!schedule.Succeeded)
        {
            return Empty(DeclaredKernelReason + schedule.Failure.Message);
        }

        // 6. Periods paying before the projection's as-of date (today for GetProjectionAsync) are
        // dropped, on their payment dates.
        var periods = schedule.Periods
            .Where(period => period.PaymentDate >= asOfDate)
            .ToArray();
        if (periods.Length == 0)
        {
            return Empty();
        }

        // 7. A Term period (AtMaturity, SCH-DEF-001) has no periods per year to divide a Level coupon by.
        if (levelBasis && annualRate > 0m && spec.Tenor.Unit == ScheduleTenorUnit.Term)
        {
            return Empty(DeclaredLevelReason);
        }

        // 8. The walk: interest over each period's [AccrualStart, AccrualEnd) on the balance outstanding
        // at its start, principal as the legacy walk pays it, each row dated on the payment date.
        var dayCountCalendar = spec.AccrualAdjustment?.Calendar ?? spec.PaymentAdjustment?.Calendar;
        var business252Windows = new List<(DateOnly Start, DateOnly End)>();
        var periodsPerYear = spec.Tenor.PeriodsPerYear;
        var outstanding = openingOutstanding;
        var entries = new List<StructuredCashFlowScheduleEntry>(periods.Length);
        var sinkerPrincipal = sourceKind == StructuredCashFlowSourceKind.CalculatedSinker && !assertedBulletSchedule
            ? RoundCash(outstanding / periods.Length)
            : 0m;
        for (var i = 0; i < periods.Length; i++)
        {
            var period = periods[i];
            var interest = 0m;
            if (annualRate > 0m && outstanding > 0m)
            {
                if (levelBasis && period.Kind == SchedulePeriodKind.Regular && periodsPerYear is { } perYear)
                {
                    interest = RoundCash(outstanding * annualRate / perYear);
                }
                else if (ScheduleDayCount.TryPeriodFraction(dayCount, period, schedule, out var fraction, out var refusal))
                {
                    interest = RoundCash(outstanding * annualRate * fraction);
                    if (dayCount == DayCountConvention.Business252)
                    {
                        business252Windows.Add((period.AccrualStart, period.AccrualEnd));
                    }
                }
                else
                {
                    return Empty(DeclaredInterestReason + refusal);
                }
            }

            var principal = 0m;
            var isLast = i == periods.Length - 1;
            if ((sourceKind == StructuredCashFlowSourceKind.CalculatedBullet || assertedBulletSchedule) && isLast)
            {
                principal = outstanding;
            }
            else if (sourceKind == StructuredCashFlowSourceKind.CalculatedSinker && !assertedBulletSchedule)
            {
                principal = isLast ? outstanding : decimal.Min(outstanding, sinkerPrincipal);
            }

            outstanding = RoundCash(outstanding - principal);
            entries.Add(new StructuredCashFlowScheduleEntry(
                ToUtcDateTimeOffset(period.PaymentDate),
                RoundCash(principal),
                interest,
                principalBasis == 0m ? 0m : RoundCash(outstanding / principalBasis)));
        }

        // 9. Evidence.
        var evidence = BuildScheduleEvidence(
            schedule,
            business252Windows.Count > 0 ? dayCountCalendar : null,
            business252Windows);

        // 10. Declared.
        return new StructuredCashFlowProjectionDto(
            security.SecurityId, sourceKind, scenario, asOf, entries, staleness, sourceLastUpdatedUtc, terms.FactorSchedule, terms)
        {
            ScheduleEvidence = evidence
        };
    }

    /// <summary>
    /// The evidence of a generated declared schedule: the kernel and mapping versions, every calendar
    /// the kernel used plus the one Business/252 counted business days on, and per calendar the first
    /// and last date asked outside its coverage, by the kernel or by a Business/252 accrual window.
    /// </summary>
    private static StructuredCashFlowScheduleEvidence BuildScheduleEvidence(
        ScheduleGenerationResult schedule,
        IBusinessDayCalendar? dayCountCalendar,
        IReadOnlyList<(DateOnly Start, DateOnly End)> business252Windows)
    {
        var calendars = new List<StructuredCashFlowCalendarEvidence>(schedule.CalendarsUsed.Count + 1);
        foreach (var usage in schedule.CalendarsUsed)
        {
            AddCalendar(new StructuredCashFlowCalendarEvidence(usage.CalendarId, usage.ContentHash, usage.Purpose.ToString()));
        }

        var gapOrder = new List<string>();
        var gaps = new Dictionary<string, (DateOnly First, DateOnly Last)>(StringComparer.Ordinal);
        foreach (var diagnostic in schedule.Diagnostics)
        {
            if (diagnostic.Code == ScheduleDiagnosticCode.CalendarCoverageExceeded)
            {
                AddGap(diagnostic.CalendarId, diagnostic.Date, diagnostic.Date);
            }
        }

        if (dayCountCalendar is not null)
        {
            AddCalendar(new StructuredCashFlowCalendarEvidence(dayCountCalendar.CalendarId, dayCountCalendar.ContentHash, DayCountCalendarPurpose));

            // Business/252 asks the calendar about every date of [start, end).
            var coverage = dayCountCalendar.Coverage;
            foreach (var (start, end) in business252Windows)
            {
                if (end <= start)
                {
                    continue;
                }

                var lastAsked = end.AddDays(-1);
                if (coverage.From is { } from && start < from)
                {
                    AddGap(dayCountCalendar.CalendarId, start, lastAsked < from ? lastAsked : from.AddDays(-1));
                }

                if (coverage.To is { } to && lastAsked > to)
                {
                    AddGap(dayCountCalendar.CalendarId, start > to ? start : to.AddDays(1), lastAsked);
                }
            }
        }

        return new StructuredCashFlowScheduleEvidence(
            schedule.KernelVersion,
            ScheduleSpecFactory.DeclaredMappingVersion,
            calendars,
            gapOrder.Select(calendarId => new StructuredCashFlowCoverageGap(calendarId, gaps[calendarId].First, gaps[calendarId].Last)).ToList());

        void AddCalendar(StructuredCashFlowCalendarEvidence calendar)
        {
            if (!calendars.Contains(calendar))
            {
                calendars.Add(calendar);
            }
        }

        void AddGap(string calendarId, DateOnly first, DateOnly last)
        {
            if (gaps.TryGetValue(calendarId, out var gap))
            {
                gaps[calendarId] = (first < gap.First ? first : gap.First, last > gap.Last ? last : gap.Last);
                return;
            }

            gapOrder.Add(calendarId);
            gaps.Add(calendarId, (first, last));
        }
    }
}
