using System.Text.Json.Serialization;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Contracts.FixedIncome;

/// <summary>Where an exercise notice window stands on the evaluation date.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BondExerciseNoticeStatus>))]
public enum BondExerciseNoticeStatus
{
    /// <summary>The declared entry states no notice bounds.</summary>
    NotStated,
    /// <summary>The earliest notice date is after the evaluation date.</summary>
    Upcoming,
    /// <summary>The evaluation date is inside the notice window.</summary>
    Open,
    /// <summary>The latest notice date has passed but the exercise date is today or later; retained notice evidence decides the actual status.</summary>
    DeadlineElapsed
}

/// <summary>One contractual exercise date and its notice window. Not an exercise forecast or instruction.</summary>
public sealed record BondExerciseNoticeWindow(
    string Kind,
    DateOnly ExerciseDate,
    decimal RedemptionPrice,
    DateOnly? EarliestNoticeDate,
    DateOnly? LatestNoticeDate,
    BondExerciseNoticeStatus Status,
    int? DaysToDeadline);

/// <summary>
/// Upcoming exercise notice windows for one security over a horizon. Blocked results carry no windows.
/// </summary>
public sealed record BondExerciseNoticeWatchResult(
    Guid SecurityId,
    long? SecurityVersion,
    DateOnly AsOf,
    int HorizonDays,
    IReadOnlyList<BondExerciseNoticeWindow> Windows,
    IReadOnlyList<BondAnalyticsBlocker> Blockers,
    IReadOnlyList<string> Diagnostics)
{
    public bool IsBlocked => Blockers.Count > 0;
}

/// <summary>
/// Reads the declared call and put schedule notice windows of a retained Bond row. It resolves scenarios
/// through <see cref="BondExerciseEconomics"/>'s TryResolveScenarios,
/// so exercise and notice dates are the ones analytics and the cash ladder use, under the same stamp gate
/// and retained-declaration rules. It only reads terms and never writes them.
/// </summary>
public static class BondExerciseNoticeWatch
{
    public const int DefaultHorizonDays = 90;
    public const int MaxHorizonDays = 366;

    public const string AuthorityDiagnostic =
        "Notice windows grant no exercise, notice, payment or posting authority.";

    public const string ElapsedDiagnostic =
        "An exercise notice deadline precedes the evaluation date; retained notice evidence is required to determine actual exercise status.";

    /// <summary>
    /// Lists notice windows that are open, or that open or close on or before <paramref name="asOf"/> plus
    /// <paramref name="horizonDays"/>, along with elapsed deadlines whose exercise date is today or later. A window
    /// is anchored on its earliest notice date, then its latest, then the exercise date when no bounds are stated.
    /// </summary>
    public static BondExerciseNoticeWatchResult Evaluate(SecurityDetailDto security, IBusinessDayCalendarSource? calendars,
        DateOnly asOf, int horizonDays = DefaultHorizonDays)
        => EvaluateGuarded(security, calendars, asOf, horizonDays);

    /// <summary>
    /// Lists every notice window whose exercise date is on or after <paramref name="asOf"/>, with no horizon:
    /// the same preconditions, scenarios, dates and classification as <see cref="Evaluate"/>, without its
    /// horizon bound or filter. The result's <see cref="BondExerciseNoticeWatchResult.HorizonDays"/> is the
    /// number of days from <paramref name="asOf"/> to the retained maturity, or 0 on a result blocked before
    /// the maturity is read. For consumers that lay out a bond's whole remaining life, such as the asset
    /// operations obligation timeline.
    /// </summary>
    public static BondExerciseNoticeWatchResult EvaluateToMaturity(SecurityDetailDto security,
        IBusinessDayCalendarSource? calendars, DateOnly asOf)
        => EvaluateGuarded(security, calendars, asOf, null);

    private static BondExerciseNoticeWatchResult EvaluateGuarded(SecurityDetailDto security,
        IBusinessDayCalendarSource? calendars, DateOnly asOf, int? horizonDays)
    {
        ArgumentNullException.ThrowIfNull(security);
        // Retained-data refusals stay actionable blocked results, as in bond analytics, rather than faults.
        try
        {
            return EvaluateCore(security, calendars, asOf, horizonDays);
        }
        catch (SecurityScheduleValidationException exception)
        {
            return Blocked(security, asOf, horizonDays ?? 0, "InvalidEconomicSchedule", exception.Message);
        }
        catch (ArithmeticException)
        {
            return Blocked(security, asOf, horizonDays ?? 0, "CalculationBoundsExceeded",
                "Bond inputs exceed the supported decimal calculation bounds.");
        }
    }

    /// <summary>A blocked result: the refusal, no windows and the authority diagnostic.</summary>
    public static BondExerciseNoticeWatchResult Blocked(SecurityDetailDto security, DateOnly asOf, int horizonDays,
        string code, string reason)
        => new(security.SecurityId, security.Version, asOf, horizonDays, [], [new(code, reason)], [AuthorityDiagnostic]);

    private static BondExerciseNoticeWatchResult EvaluateCore(SecurityDetailDto security, IBusinessDayCalendarSource? calendars,
        DateOnly asOf, int? horizonDays)
    {
        BondExerciseNoticeWatchResult Refuse(string code, string reason) => Blocked(security, asOf, horizonDays ?? 0, code, reason);

        // A null horizon is the to-maturity read: no bound to validate and no window filtered out.
        if (horizonDays is < 1 or > MaxHorizonDays)
            return Refuse("InvalidHorizon", $"The notice horizon must be between 1 and {MaxHorizonDays} days.");
        if (!SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).CarriesBondCouponStructure)
            return Refuse("UnsupportedAssetClass", "Exercise notice windows support canonical Bond securities only.");
        if (SecurityContractualCashFlowTerms.IsDeclared(security.AssetClass, security.AssetSpecificTerms))
            return Refuse("UnsupportedCashFlowContract",
                "This bond declares a contractual cash-flow companion; use its contractual cash-flow preview for exercise notices.");

        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        var couponKind = BondContractualEconomics.Text(security.AssetSpecificTerms, "couponType");
        if (couponKind is not ("Fixed" or "ZeroCoupon"))
            return Refuse("UnsupportedCouponStructure", "Exercise notice windows support explicitly typed Fixed and ZeroCoupon bonds.");
        if (terms.HasPrincipalSchedule || terms.HasFactorSchedule || terms.CurrentFactor is not null
            || terms.HasLegs || terms.HasStepCouponSchedule || terms.InflationIndex is not null)
            return Refuse("UnsupportedPrincipalStructure", "Exercise notice windows require unfactored bullet principal and a fixed or zero coupon.");
        if (terms.IssueDate is not { } issue || terms.MaturityDate is not { } maturity || asOf < issue || asOf >= maturity)
            return Refuse("EvaluationOutsideLife", "The evaluation date must be on or after the retained issue date and before maturity.");

        var declared = ScheduleSpecFactory.Resolve(security, terms, calendars);
        if (declared.Status != DeclaredScheduleStatus.Declared)
            return Refuse("DeclaredScheduleRequired", declared.Refusal?.Message
                ?? "Exercise notice windows require reviewed explicit schedule conventions; legacy defaults are not inferred.");
        var zeroCoupon = couponKind == "ZeroCoupon";
        var annualRate = zeroCoupon ? 0m : declared.Conventions.CouponRateUnit switch
        {
            "Percent" => terms.CouponRate / 100m,
            "Fraction" => terms.CouponRate,
            _ => null
        };
        if (annualRate is null or < 0m)
            return Refuse("CouponUnitRequired", "A fixed bond requires a nonnegative retained coupon and an explicit Percent or Fraction coupon unit.");
        var schedule = ScheduleGenerator.Generate(declared.Spec!);
        if (!schedule.Succeeded)
            return Refuse("ScheduleRefused", schedule.Failure.Message);

        // Cash flows, and with them the delayed-payment entitlement check, are anchored to the evaluation date.
        // The pricing resolver keeps only exercises strictly after its settlement date, while a notice watch is
        // inclusive of today, so scenario selection alone starts from the day before and keeps exercises on or
        // after the evaluation date. The yield convention only sets discount times, which the windows do not use.
        var resolveFrom = asOf > issue ? asOf.AddDays(-1) : asOf;
        if (!BondContractualEconomics.TryBuild(security.SecurityId, terms, declared, schedule, asOf, annualRate.Value,
                zeroCoupon, BondYieldConvention.AnnualEffective, out var flows, out _, out var refusal))
            return Refuse("CashFlowRefused", refusal!);
        if (!BondExerciseEconomics.TryResolveScenarios(security, terms, declared, schedule, resolveFrom, annualRate.Value,
                BondYieldConvention.AnnualEffective, declared.Spec!.Tenor.PeriodsPerYear ?? 1, flows, calendars,
                out var scenarios, out refusal))
            return Refuse("ExerciseScheduleRefused", refusal!);

        DateOnly? horizonEnd = horizonDays is { } days ? asOf.AddDays(days) : null;
        var windows = new List<BondExerciseNoticeWindow>();
        foreach (var scenario in scenarios.Where(item => item.ExerciseDate >= asOf))
        {
            var status = Classify(scenario, asOf);
            var anchor = scenario.EarliestNoticeDate ?? scenario.LatestNoticeDate ?? scenario.ExerciseDate;
            if (status != BondExerciseNoticeStatus.DeadlineElapsed && horizonEnd is { } end && anchor > end)
                continue;
            int? daysToDeadline = scenario.LatestNoticeDate is { } latest ? latest.DayNumber - asOf.DayNumber : null;
            windows.Add(new(scenario.Kind, scenario.ExerciseDate, scenario.RedemptionPrice, scenario.EarliestNoticeDate,
                scenario.LatestNoticeDate, status, daysToDeadline));
        }

        var ordered = windows
            .OrderBy(static window => window.LatestNoticeDate ?? window.ExerciseDate)
            .ThenBy(static window => window.ExerciseDate)
            .ThenBy(static window => window.Kind, StringComparer.Ordinal)
            .ToArray();
        var diagnostics = new List<string> { AuthorityDiagnostic };
        if (ordered.Any(static window => window.Status == BondExerciseNoticeStatus.DeadlineElapsed))
            diagnostics.Add(ElapsedDiagnostic);
        return new(security.SecurityId, security.Version, asOf, horizonDays ?? maturity.DayNumber - asOf.DayNumber,
            ordered, [], diagnostics);
    }

    private static BondExerciseNoticeStatus Classify(BondRedemptionScenario scenario, DateOnly asOf)
    {
        if (scenario.LatestNoticeDate is null && scenario.EarliestNoticeDate is null)
            return BondExerciseNoticeStatus.NotStated;
        if (scenario.LatestNoticeDate is { } latest && latest < asOf)
            return BondExerciseNoticeStatus.DeadlineElapsed;
        if (scenario.EarliestNoticeDate is { } earliest && earliest > asOf)
            return BondExerciseNoticeStatus.Upcoming;
        return BondExerciseNoticeStatus.Open;
    }
}
