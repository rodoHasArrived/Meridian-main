using System.Globalization;

namespace Meridian.Contracts.SecurityMaster.Schedules;

/// <summary>
/// The contractual schedule kernel: one pure, deterministic generator of period dates for every
/// Meridian schedule consumer. It owns date and period arithmetic, business-day adjustment and the
/// reference periods day counts are measured against; economic interpretation stays with its owners.
/// </summary>
/// <remarks>
/// <para>
/// For a month or year tenor of m months, the anchored grid from anchor A is
/// <c>G(k) = Roll.Apply(A.AddMonths(k × m))</c> for any integer k; the compounded grid is
/// <c>raw(k) = raw(k − 1).AddMonths(m)</c>; a day or week tenor steps <c>A.AddDays(k × days)</c>. A
/// grid date outside the <see cref="DateOnly"/> range counts as an overshoot and never throws.
/// </para>
/// <para>
/// Forward generation walks from <c>FirstRegularDate ?? EffectiveDate</c> to
/// <c>LastRegularDate ?? TerminationDate</c>; backward generation walks from
/// <c>LastRegularDate ?? TerminationDate</c> to <c>EffectiveDate</c>. Unadjusted dates and reference
/// periods always come from the grid; adjusted dates are derived from the unadjusted grid, never
/// chained from earlier adjusted dates.
/// </para>
/// <para>
/// <see cref="Generate"/> throws only <see cref="ArgumentNullException"/>; every data problem comes back
/// as a <see cref="ScheduleFailure"/>. The kernel reads no clock and performs no I/O.
/// </para>
/// </remarks>
public static partial class ScheduleGenerator
{
    /// <summary>The version of the kernel's generation semantics, stamped on every result.</summary>
    public const string KernelVersion = "contractual-schedule-kernel-v1";

    /// <summary>Generates the schedule <paramref name="spec"/> describes, or the reason it cannot be generated.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    public static ScheduleGenerationResult Generate(ScheduleSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var failure = Validate(spec, out var empty);
        if (failure is not null)
        {
            return Failed(spec, failure, [], []);
        }

        if (empty)
        {
            return new ScheduleGenerationResult(spec, [], null, [], []);
        }

        var raw = new List<RawPeriod>();
        failure = spec.Tenor.Unit == ScheduleTenorUnit.Term
            ? BuildTerm(spec, raw)
            : spec.Direction == ScheduleDirection.Forward ? BuildForward(spec, raw) : BuildBackward(spec, raw);
        if (failure is null && ExceedsLimit(spec, raw.Count))
        {
            failure = LimitExceeded(spec);
        }

        return failure is null ? Assemble(spec, raw) : Failed(spec, failure, [], []);
    }

    /// <summary>
    /// The grid dates from <paramref name="anchor"/>, lazily: k = 0, 1, 2, … until the next date would
    /// leave the <see cref="DateOnly"/> range. The anchored grid rolls each date with
    /// <paramref name="roll"/>; the compounded grid steps from the previous date.
    /// </summary>
    /// <exception cref="ArgumentException">The tenor is a term or default tenor, or the roll is not the anchor day for compounded mode or a day or week tenor.</exception>
    public static IEnumerable<DateOnly> RollDates(DateOnly anchor, ScheduleTenor tenor, ScheduleAnchorMode mode, ScheduleRoll roll = default)
    {
        if (tenor.Count <= 0 || tenor.Unit == ScheduleTenorUnit.Term)
        {
            throw new ArgumentException("Roll dates require a day, week, month or year tenor.", nameof(tenor));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentException("Unknown schedule anchor mode.", nameof(mode));
        }

        if (roll.Kind != ScheduleRollKind.AnchorDay && (mode == ScheduleAnchorMode.Compounded || !tenor.IsMonthBased))
        {
            throw new ArgumentException("Compounded mode and day or week tenors require the anchor-day roll.", nameof(roll));
        }

        return mode == ScheduleAnchorMode.Compounded ? CompoundedDates(anchor, tenor) : AnchoredDates(anchor, tenor, roll);
    }

    private static IEnumerable<DateOnly> AnchoredDates(DateOnly anchor, ScheduleTenor tenor, ScheduleRoll roll)
    {
        for (var k = 0; tenor.TryAdvance(anchor, k, out var raw); k++)
        {
            yield return roll.Apply(raw);
        }
    }

    private static IEnumerable<DateOnly> CompoundedDates(DateOnly anchor, ScheduleTenor tenor)
    {
        var current = anchor;
        yield return current;
        while (tenor.TryAdvance(current, 1, out current))
        {
            yield return current;
        }
    }

    private static ScheduleFailure? BuildTerm(ScheduleSpec spec, List<RawPeriod> output)
    {
        output.Add(new RawPeriod(spec.EffectiveDate, spec.TerminationDate, SchedulePeriodKind.Term, [new(spec.EffectiveDate, spec.TerminationDate)]));
        return null;
    }

    private static ScheduleFailure? BuildForward(ScheduleSpec spec, List<RawPeriod> output)
    {
        var (effective, termination, last) = (spec.EffectiveDate, spec.TerminationDate, spec.LastRegularDate);
        var anchor = spec.FirstRegularDate ?? effective;
        var bound = last ?? termination;
        var grid = new Grid(anchor, spec.Tenor, spec.Roll);
        var compounded = spec.AnchorMode == ScheduleAnchorMode.Compounded;
        var extras = (anchor > effective ? 1 : 0) + (bound < termination ? 1 : 0);

        var boundaries = new List<DateOnly> { anchor };
        DateOnly? overshoot = null;
        var boundStep = 0;
        for (var k = 1; ; k++)
        {
            DateOnly? candidate = compounded ? grid.After(boundaries[^1]) : grid.At(k);
            if (candidate is not { } date || date > bound)
            {
                overshoot = candidate;
                break;
            }

            boundaries.Add(date);
            if (date == bound)
            {
                boundStep = k;
                break;
            }

            if (ExceedsLimit(spec, boundaries.Count - 1 + extras))
            {
                return LimitExceeded(spec);
            }
        }

        var exact = boundaries[^1] == bound;
        if (!exact && last is { } lastRegular)
        {
            return Failure(ScheduleFailureCode.RegularDatesNotOnGrid, $"The last regular date {Format(lastRegular)} is not on the {spec.Tenor} grid from {Format(anchor)}.", lastRegular);
        }

        if (!exact && spec.StubPolicy == ScheduleStubPolicy.RequireRegular)
        {
            return Failure(ScheduleFailureCode.DoesNotFitRegularGrid, $"{Format(anchor)} to {Format(bound)} is not a whole number of {spec.Tenor} periods.", bound);
        }

        if (anchor > effective)
        {
            output.Add(InitialStub(effective, anchor, grid));
        }

        AddRegular(output, boundaries, 1);
        if (!exact)
        {
            var lastGrid = boundaries[^1];
            if (spec.StubPolicy == ScheduleStubPolicy.Long && boundaries.Count >= 2)
            {
                output.RemoveAt(output.Count - 1);
                var previous = boundaries[^2];
                output.Add(new RawPeriod(previous, bound, SchedulePeriodKind.LongFinalStub, Chain(null, [previous, lastGrid], overshoot)));
            }
            else
            {
                output.Add(new RawPeriod(lastGrid, bound, SchedulePeriodKind.ShortFinalStub, Chain(null, [lastGrid], overshoot)));
            }
        }

        if (bound < termination)
        {
            output.Add(FinalStub(bound, termination, grid, boundStep));
        }

        return null;
    }

    private static ScheduleFailure? BuildBackward(ScheduleSpec spec, List<RawPeriod> output)
    {
        var (effective, termination) = (spec.EffectiveDate, spec.TerminationDate);
        var anchor = spec.LastRegularDate ?? termination;
        var grid = new Grid(anchor, spec.Tenor, spec.Roll);
        var extras = anchor < termination ? 1 : 0;

        var boundaries = new List<DateOnly> { anchor };
        DateOnly? quasi = null;
        for (var k = 1; ; k++)
        {
            var candidate = grid.At(-k);
            if (candidate is not { } date || date < effective)
            {
                quasi = candidate;
                break;
            }

            boundaries.Add(date);
            if (date == effective)
            {
                break;
            }

            if (ExceedsLimit(spec, boundaries.Count - 1 + extras))
            {
                return LimitExceeded(spec);
            }
        }

        boundaries.Reverse();
        var exact = boundaries[0] == effective;
        if (!exact && spec.StubPolicy == ScheduleStubPolicy.RequireRegular)
        {
            return Failure(ScheduleFailureCode.DoesNotFitRegularGrid, $"{Format(effective)} to {Format(anchor)} is not a whole number of {spec.Tenor} periods.", effective);
        }

        var firstRegular = 1;
        if (!exact)
        {
            var firstGrid = boundaries[0];
            if (spec.StubPolicy == ScheduleStubPolicy.Long && boundaries.Count >= 2)
            {
                var secondGrid = boundaries[1];
                output.Add(new RawPeriod(effective, secondGrid, SchedulePeriodKind.LongInitialStub, Chain(quasi, [firstGrid, secondGrid], null)));
                firstRegular = 2;
            }
            else
            {
                output.Add(new RawPeriod(effective, firstGrid, SchedulePeriodKind.ShortInitialStub, Chain(quasi, [firstGrid], null)));
            }
        }

        AddRegular(output, boundaries, firstRegular);
        if (anchor < termination)
        {
            output.Add(FinalStub(anchor, termination, grid, 0));
        }

        return null;
    }

    private static void AddRegular(List<RawPeriod> output, List<DateOnly> boundaries, int from)
    {
        for (var i = from; i < boundaries.Count; i++)
        {
            output.Add(new RawPeriod(boundaries[i - 1], boundaries[i], SchedulePeriodKind.Regular, [new(boundaries[i - 1], boundaries[i])]));
        }
    }

    // An explicit initial stub [E, F] in a forward schedule anchored at F. The grid is stepped from F,
    // never re-anchored at E, so the notional dates are the regular dates the stub would have had.
    private static RawPeriod InitialStub(DateOnly effective, DateOnly firstRegular, Grid grid)
    {
        var kind = grid.At(-1) is not { } previous || effective > previous
            ? SchedulePeriodKind.ShortInitialStub
            : effective < previous ? SchedulePeriodKind.LongInitialStub : SchedulePeriodKind.Regular;
        var references = new List<DayCountReferencePeriod>();
        var referenceEnd = firstRegular;
        for (var j = 1; grid.At(-j) is { } referenceStart; j++)
        {
            references.Add(new DayCountReferencePeriod(referenceStart, referenceEnd));
            if (referenceStart <= effective)
            {
                break;
            }

            referenceEnd = referenceStart;
        }

        references.Reverse();
        return new RawPeriod(effective, firstRegular, kind, [.. references]);
    }

    // An explicit final stub [L, M], where L = G(kL) on the generation grid.
    private static RawPeriod FinalStub(DateOnly lastRegular, DateOnly termination, Grid grid, int lastRegularStep)
    {
        var kind = grid.At(lastRegularStep + 1) is not { } next || termination < next
            ? SchedulePeriodKind.ShortFinalStub
            : termination > next ? SchedulePeriodKind.LongFinalStub : SchedulePeriodKind.Regular;
        var references = new List<DayCountReferencePeriod>();
        var referenceStart = lastRegular;
        for (var j = 1; grid.At(lastRegularStep + j) is { } referenceEnd; j++)
        {
            references.Add(new DayCountReferencePeriod(referenceStart, referenceEnd));
            if (referenceEnd >= termination)
            {
                break;
            }

            referenceStart = referenceEnd;
        }

        return new RawPeriod(lastRegular, termination, kind, [.. references]);
    }

    // Consecutive notional periods over head?, dates…, tail?; a null end (a grid date beyond the
    // supported range) is omitted, so the references then do not cover the stub.
    private static DayCountReferencePeriod[] Chain(DateOnly? head, DateOnly[] dates, DateOnly? tail)
    {
        var chain = new List<DateOnly>(dates.Length + 2);
        if (head is { } h)
        {
            chain.Add(h);
        }

        chain.AddRange(dates);
        if (tail is { } t)
        {
            chain.Add(t);
        }

        var references = new DayCountReferencePeriod[Math.Max(0, chain.Count - 1)];
        for (var i = 1; i < chain.Count; i++)
        {
            references[i - 1] = new DayCountReferencePeriod(chain[i - 1], chain[i]);
        }

        return references;
    }

    private static bool ExceedsLimit(ScheduleSpec spec, int periodCount) => spec.MaxPeriods is { } max && periodCount > max;

    private static ScheduleFailure LimitExceeded(ScheduleSpec spec)
        => Failure(ScheduleFailureCode.PeriodLimitExceeded, string.Create(CultureInfo.InvariantCulture, $"The schedule would exceed {spec.MaxPeriods} periods."));

    private static ScheduleFailure Failure(ScheduleFailureCode code, string message, DateOnly? date = null) => new(code, message, date);

    private static ScheduleGenerationResult Failed(
        ScheduleSpec spec,
        ScheduleFailure failure,
        IReadOnlyList<ScheduleDiagnostic> diagnostics,
        IReadOnlyList<ScheduleCalendarUsage> calendarsUsed)
        => new(spec, [], failure, diagnostics, calendarsUsed);

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private readonly record struct RawPeriod(DateOnly Start, DateOnly End, SchedulePeriodKind Kind, DayCountReferencePeriod[] References);

    // The anchored grid G(k) = Roll.Apply(Tenor.Advance(anchor, k)); null when the date leaves the range.
    private readonly struct Grid(DateOnly anchor, ScheduleTenor tenor, ScheduleRoll roll)
    {
        public DateOnly? At(int k) => tenor.TryAdvance(anchor, k, out var raw) ? roll.Apply(raw) : null;

        public DateOnly? After(DateOnly previous) => tenor.TryAdvance(previous, 1, out var next) ? next : null;
    }
}
