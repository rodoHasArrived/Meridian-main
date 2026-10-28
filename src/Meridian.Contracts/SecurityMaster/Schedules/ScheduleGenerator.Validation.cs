using Meridian.Contracts.Calendars;

namespace Meridian.Contracts.SecurityMaster.Schedules;

// Spec validation for the schedule kernel. Checks run in a fixed order and the first failure wins, so
// the same spec always reports the same refusal.
public static partial class ScheduleGenerator
{
    private static ScheduleFailure? Validate(ScheduleSpec spec, out bool empty)
    {
        empty = false;
        if (spec.Tenor.Count <= 0
            || !Enum.IsDefined(spec.Direction)
            || !Enum.IsDefined(spec.AnchorMode)
            || !Enum.IsDefined(spec.StubPolicy)
            || Adjustments(spec).Any(static entry => entry.Adjustment is { } adjustment && !Enum.IsDefined(adjustment.Convention)))
        {
            return Failure(ScheduleFailureCode.InvalidSpecification, "The schedule spec needs a positive tenor and defined direction, anchor mode, stub policy and business-day conventions.");
        }

        if (spec.MaxPeriods is < 1)
        {
            return Failure(ScheduleFailureCode.InvalidPeriodLimit, "The period limit must be null or at least 1.");
        }

        if (spec.PaymentLagBusinessDays < 0)
        {
            return Failure(ScheduleFailureCode.NegativePaymentLag, "The payment lag must be zero or more business days.");
        }

        var (effective, termination) = (spec.EffectiveDate, spec.TerminationDate);
        if (effective >= termination)
        {
            empty = spec.StubPolicy != ScheduleStubPolicy.RequireRegular;
            return empty ? null : Failure(ScheduleFailureCode.DoesNotFitRegularGrid, "An empty span does not fit a regular grid.", termination);
        }

        var (first, last) = (spec.FirstRegularDate, spec.LastRegularDate);
        var term = spec.Tenor.Unit == ScheduleTenorUnit.Term;
        if (term && (first.HasValue || last.HasValue))
        {
            return Failure(ScheduleFailureCode.TermTenorDisallowsRegularDates, "A term tenor has no regular dates.");
        }

        if (!term && !spec.Tenor.IsMonthBased)
        {
            if (spec.Direction != ScheduleDirection.Forward)
            {
                return Failure(ScheduleFailureCode.TenorNotAllowedForDirection, $"A {spec.Tenor} tenor generates forward only.");
            }

            if (spec.Roll.Kind != ScheduleRollKind.AnchorDay)
            {
                return Failure(ScheduleFailureCode.RollNotAllowedForTenor, $"A {spec.Tenor} tenor allows only the anchor-day roll.");
            }
        }

        if (spec.AnchorMode == ScheduleAnchorMode.Compounded
            && (spec.Direction != ScheduleDirection.Forward
                || spec.Roll.Kind != ScheduleRollKind.AnchorDay
                || first.HasValue
                || last.HasValue
                || spec.StubPolicy != ScheduleStubPolicy.Short))
        {
            return Failure(ScheduleFailureCode.CompoundedModeRestricted, "Compounded mode requires forward generation, the anchor-day roll, no regular dates and a short stub.");
        }

        if (first is { } f && (f < effective || f >= termination))
        {
            return Failure(ScheduleFailureCode.RegularDateOutOfRange, "The first regular date must be on or after the effective date and before the termination date.", f);
        }

        if (last is { } l && (l <= (first ?? effective) || l > termination))
        {
            return Failure(ScheduleFailureCode.RegularDateOutOfRange, "The last regular date must be after the first regular (or effective) date and on or before the termination date.", l);
        }

        if ((first.HasValue && spec.Direction != ScheduleDirection.Forward)
            || (!first.HasValue && last.HasValue && spec.Direction != ScheduleDirection.Backward))
        {
            return Failure(ScheduleFailureCode.DirectionConflictsWithRegularDates, "A first regular date requires forward generation; a last regular date alone requires backward generation.");
        }

        foreach (var (name, adjustment) in Adjustments(spec))
        {
            if (adjustment is { IsUnadjusted: false, Calendar: null })
            {
                return Failure(ScheduleFailureCode.AdjustmentRequiresCalendar, $"The {name} adjustment's {adjustment.Convention} convention requires a calendar.");
            }
        }

        if (spec.PaymentLagBusinessDays > 0 && spec.PaymentAdjustment?.Calendar is null)
        {
            return Failure(ScheduleFailureCode.PaymentLagRequiresCalendar, "A payment lag requires a payment calendar.");
        }

        if (!term)
        {
            var anchor = spec.Direction == ScheduleDirection.Forward ? first ?? effective : last ?? termination;
            if (!spec.Roll.Matches(anchor))
            {
                return Failure(ScheduleFailureCode.AnchorNotOnRollDay, $"The generation anchor {Format(anchor)} is not on the {spec.Roll} roll.", anchor);
            }
        }

        return null;
    }

    private static IEnumerable<(string Name, BusinessDayAdjustment? Adjustment)> Adjustments(ScheduleSpec spec)
    {
        yield return ("accrual", spec.AccrualAdjustment);
        yield return ("payment", spec.PaymentAdjustment);
        yield return ("effective-date", spec.EffectiveDateAdjustment);
        yield return ("termination-date", spec.TerminationDateAdjustment);
    }
}
