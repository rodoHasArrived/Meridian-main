using System.Diagnostics.CodeAnalysis;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.FSharp.Domain;

namespace Meridian.Application.SecurityMaster.CashFlow;

/// <summary>
/// Maps an already decoded, declared convention block to the contractual schedule kernel.
/// Does not select a legacy schedule or infer coupon cash amounts.
/// </summary>
internal static class ScheduleSpecFactory
{
    /// <summary>
    /// Resolves declared scheduling terms. Missing calendars and unrecognized conventions refuse;
    /// they never select a weekends-only calendar or the legacy coupon-frequency fallback.
    /// </summary>
    internal static bool TryCreate(
        ScheduleConventions conventions,
        DateOnly effectiveDate,
        DateOnly terminationDate,
        string? paymentFrequency,
        IBusinessDayCalendarSource calendars,
        string? commonCalendarId,
        [NotNullWhen(true)] out ScheduleSpec? spec,
        [NotNullWhen(false)] out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(conventions);
        ArgumentNullException.ThrowIfNull(calendars);
        spec = null;
        refusal = null;

        if (ScheduleConventionsModule.IsEmpty(conventions))
        {
            refusal = "No schedule conventions are declared; the caller must retain its legacy schedule.";
            return false;
        }

        if (terminationDate <= effectiveDate)
        {
            refusal = "Declared schedule termination must be after its effective date.";
            return false;
        }

        if (!TryTenor(paymentFrequency, out var tenor))
        {
            refusal = "Declared schedules require a recognized regular payment frequency or AtMaturity.";
            return false;
        }

        var direction = ScheduleDirection.Forward;
        if (conventions.Direction is { } directionTerm)
        {
            // These are the exact values frozen in the versioned container, not enum numbers.
            if (directionTerm.Value is not ("Forward" or "Backward"))
            {
                refusal = "Schedule direction must be Forward or Backward.";
                return false;
            }

            direction = directionTerm.Value == "Backward" ? ScheduleDirection.Backward : ScheduleDirection.Forward;
        }

        var roll = ScheduleRoll.AnchorDay;
        if (conventions.RollConvention is { } rollTerm
            && !SecurityTermConventions.TryParseRollConvention(rollTerm.Value, out roll))
        {
            refusal = "Unrecognized schedule roll convention.";
            return false;
        }

        if (conventions.EndOfMonthRule is { Value: true })
        {
            if (conventions.RollConvention is not null && roll != ScheduleRoll.EndOfMonth)
            {
                refusal = "endOfMonthRule conflicts with the declared roll convention.";
                return false;
            }

            roll = ScheduleRoll.EndOfMonth;
        }
        else if (conventions.EndOfMonthRule is { Value: false } && roll == ScheduleRoll.EndOfMonth)
        {
            refusal = "endOfMonthRule=false conflicts with an end-of-month roll.";
            return false;
        }

        var stubPolicy = ScheduleStubPolicy.Short;
        if (conventions.StubPolicy is { } stubTerm
            && !SecurityTermConventions.TryParseStubPolicy(stubTerm.Value, out stubPolicy))
        {
            refusal = "Unrecognized schedule stub policy.";
            return false;
        }

        var businessDayConvention = BusinessDayConvention.Unadjusted;
        if (conventions.BusinessDayConvention is { } businessDayTerm
            && !SecurityTermConventions.TryParseBusinessDayConvention(businessDayTerm.Value, out businessDayConvention))
        {
            refusal = "Unrecognized business-day convention.";
            return false;
        }

        var lag = conventions.PaymentLagBusinessDays?.Value ?? 0;
        if (lag < 0)
        {
            refusal = "Payment lag must not be negative.";
            return false;
        }

        // Explicit payment calendars are authoritative. A common calendar is used only when the
        // block names none; an explicit unknown id must never fall back to the common calendar.
        var ids = conventions.PaymentCalendars.ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace))
        {
            refusal = "Payment calendar ids must not be blank.";
            return false;
        }

        if (ids.Length == 0 && !string.IsNullOrWhiteSpace(commonCalendarId))
        {
            ids = [commonCalendarId];
        }

        IBusinessDayCalendar? calendar = null;
        if (ids.Length > 0 && !calendars.TryResolve(ids, out calendar))
        {
            refusal = "One or more declared payment calendars could not be resolved.";
            return false;
        }

        if (calendar is null && (businessDayConvention != BusinessDayConvention.Unadjusted || lag != 0))
        {
            refusal = "Business-day adjustment and payment lag require a resolvable payment calendar.";
            return false;
        }

        var paymentAdjustment = new BusinessDayAdjustment(businessDayConvention, calendar);
        var candidate = new ScheduleSpec(
            effectiveDate,
            terminationDate,
            tenor,
            Direction: direction,
            Roll: roll,
            StubPolicy: stubPolicy,
            FirstRegularDate: conventions.FirstRegularDate?.Value,
            LastRegularDate: conventions.LastRegularDate?.Value,
            AccrualAdjustment: conventions.AccrualAdjusted is { Value: true }
                ? paymentAdjustment
                : BusinessDayAdjustment.Unadjusted,
            PaymentAdjustment: paymentAdjustment,
            PaymentLagBusinessDays: lag);

        // The kernel owns grid validation (regular dates, tenor/roll compatibility, collapsed
        // accrual periods and range limits). Do not duplicate those invariants in this adapter.
        var generated = ScheduleGenerator.Generate(candidate);
        if (!generated.Succeeded)
        {
            refusal = generated.Failure.Message;
            return false;
        }

        spec = candidate;
        return true;
    }

    private static bool TryTenor(string? raw, out ScheduleTenor tenor)
    {
        tenor = default;
        if (!SecurityTermConventions.TryParsePaymentFrequency(raw, out var frequency))
        {
            return false;
        }

        switch (frequency)
        {
            case SecurityPaymentFrequency.Daily:
                tenor = ScheduleTenor.Days(1);
                break;
            case SecurityPaymentFrequency.Weekly:
                tenor = ScheduleTenor.Weeks(1);
                break;
            case SecurityPaymentFrequency.BiWeekly:
                tenor = ScheduleTenor.Weeks(2);
                break;
            case SecurityPaymentFrequency.AtMaturity:
                tenor = ScheduleTenor.Term;
                break;
            default:
                if (frequency.MonthsPerPeriod() is not { } months)
                {
                    return false;
                }

                tenor = ScheduleTenor.Months(months);
                break;
        }

        return true;
    }
}
