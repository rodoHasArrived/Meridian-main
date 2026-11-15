using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Turns declared fixed-rate loan terms into an interest amount for a half-open accrual window.
/// Pure: it never reads a ledger, fetches a fixing, creates a draft or authorizes a journal.
/// </summary>
/// <remarks>
/// Proration (<see cref="IntercompanyAccrualRules.RuleVersion"/>): every day accrues under the approved
/// revision applicable on that day. The window is cut at each authority, schedule-period and principal
/// boundary; each slice is <c>principal x annual rate x day-count fraction</c> using its own revision's
/// rate, convention and schedule. A slice's fraction is the difference of two fractions measured from
/// its schedule period's start, so cutting a period never changes the period's total. Slices are summed unrounded and rounded once, half away from zero, to
/// the agreement currency's minor unit. Anything that would need an inferred term is refused whole.
/// </remarks>
public sealed class IntercompanyAccrualPreviewService(IBusinessDayCalendarSource calendars)
    : IIntercompanyAccrualPreviewService
{
    public IntercompanyAccrualPreviewDto Preview(IntercompanyAccrualComputationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var agreementId = input.Authority?.FirstOrDefault()?.LegalRevision?.AgreementId;
        var currency = input.Authority?.FirstOrDefault()?.LegalRevision?.Terms?.Currency;
        IntercompanyAccrualPreviewDto Unavailable(string reason) => new(IntercompanyAccrualRules.StatusUnavailable,
            IntercompanyAccrualRules.RuleVersion, agreementId, currency, input.AccrualFrom, input.AccrualTo, null, null,
            ScheduleGenerator.KernelVersion, [], [], [reason]);

        if (input.AccrualTo <= input.AccrualFrom)
            return Unavailable("The accrual window must be a nonempty [from, to) range.");
        if (input.AccrualTo.DayNumber - input.AccrualFrom.DayNumber > IntercompanyAccrualRules.MaximumWindowDays)
            return Unavailable($"One accrual request covers at most {IntercompanyAccrualRules.MaximumWindowDays} days.");
        if (Coverage(input.Authority?.Select(item => (item.From, item.To)), input) is { } authorityGap)
            return Unavailable("Approved loan-interest profile authority " + authorityGap);
        if (Coverage(input.Principal?.Select(item => (item.From, item.To)), input) is { } principalGap)
            return Unavailable("Retained principal evidence " + principalGap);
        if (input.Principal!.Any(item => item.Principal < 0m || string.IsNullOrWhiteSpace(item.EvidenceReference)))
            return Unavailable("Principal must be nonnegative and carry a retained evidence reference.");

        var slices = new List<IntercompanyAccrualSliceDto>();
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var authority in input.Authority!)
        {
            var legal = authority.LegalRevision;
            var draft = legal.Terms;
            if (legal.AgreementId != agreementId || !string.Equals(draft.Currency, currency, StringComparison.Ordinal))
                return Unavailable("Every accrual day must belong to one legal agreement and currency.");
            if (legal.Action == IntercompanyAgreementActionDto.Terminate || draft.AgreementType != IntercompanyAgreementTypeDto.Loan)
                return Unavailable("Interest accrues only under an approved, unterminated loan revision.");
            if (draft.Terms?.StructuredTerms is not { } terms)
                return Unavailable($"Agreement version {legal.AgreementVersion} has unstructured terms; no rate or schedule is inferred from prose.");
            try
            { IntercompanyAgreementRules.ValidateComplete(draft); }
            catch (IntercompanyAgreementValidationException error)
            { return Unavailable($"Agreement version {legal.AgreementVersion}: {error.Message}"); }
            if (terms.RateBasis != IntercompanyRateBasisDto.Fixed || terms.FixedAnnualRate is not { } rate)
                return Unavailable("Floating-rate loans need a governed fixing authority that this release does not provide.");
            if (terms.Compounding != IntercompanyCompoundingDto.Simple)
                return Unavailable("Only simple interest is calculated; compounding needs retained capitalization history.");
            var schedule = terms.Schedule!;
            if (!calendars.TryGet(schedule.CalendarId, out var calendar))
                return Unavailable("The declared business-day calendar is unavailable; no fallback was applied.");
            var generated = ScheduleGenerator.Generate(IntercompanyStructuredTermsPreviewService.BuildSpec(schedule, calendar));
            if (!generated.Succeeded)
                return Unavailable(generated.Failure.Message);
            if (generated.Diagnostics.Count > 0)
                return Unavailable(generated.Diagnostics[0].Message);
            pins[calendar.CalendarId] = calendar.ContentHash;
            var convention = Enum.Parse<DayCountConvention>(terms.DayCount!.Value.ToString());

            var cursor = authority.From;
            foreach (var period in generated.Periods)
            {
                var from = Later(period.AccrualStart, authority.From);
                var to = Earlier(period.AccrualEnd, authority.To);
                if (to <= from)
                    continue;
                if (from != cursor)
                    break;
                foreach (var principal in input.Principal)
                {
                    var start = Later(from, principal.From);
                    var end = Earlier(to, principal.To);
                    if (end <= start)
                        continue;
                    // Measure both ends from the period start so fractions stay additive: under 30/360 a cut on the
                    // 31st must not add a day that the unsplit period does not contain.
                    if (!ScheduleDayCount.TryFraction(convention, period, generated, period.AccrualStart, end, out var through, out var refusal)
                        || !ScheduleDayCount.TryFraction(convention, period, generated, period.AccrualStart, start, out var before, out refusal))
                        return Unavailable(refusal);
                    var fraction = through - before;
                    slices.Add(new(slices.Count + 1, start, end, authority.AccountingArrangementRevisionId,
                        legal.AgreementVersion, legal.PayloadHash, period.Sequence, principal.Principal, rate,
                        DayCountConventions.TermLabel(convention), fraction, principal.Principal * rate * fraction,
                        principal.EvidenceReference));
                }
                cursor = to;
            }
            if (cursor != authority.To)
                return Unavailable($"Agreement version {legal.AgreementVersion} declares no schedule period for {cursor:yyyy-MM-dd}; days outside the declared schedule do not accrue by inference.");
        }

        var unrounded = slices.Sum(slice => slice.UnroundedAmount);
        var amount = decimal.Round(unrounded, CurrencyMinorUnits.GetPrecision(currency!), MidpointRounding.AwayFromZero);
        var calendarPins = pins.OrderBy(pin => pin.Key, StringComparer.Ordinal)
            .Select(pin => new IntercompanyAccrualCalendarPinDto(pin.Key, pin.Value)).ToArray();
        if (amount <= 0m)
            return new(IntercompanyAccrualRules.StatusUnavailable, IntercompanyAccrualRules.RuleVersion, agreementId, currency,
                input.AccrualFrom, input.AccrualTo, null, null, ScheduleGenerator.KernelVersion, calendarPins, slices,
                ["The window produces no positive interest in the agreement currency; nothing is proposed."]);
        return new(IntercompanyAccrualRules.StatusReady, IntercompanyAccrualRules.RuleVersion, agreementId, currency,
            input.AccrualFrom, input.AccrualTo, amount, unrounded, ScheduleGenerator.KernelVersion, calendarPins, slices,
            ["Calculated proposal input only. Nothing is posted; each side still requires its own review."]);
    }

    /// <summary>Segments must tile the window exactly, in order, with no gap, overlap or overhang.</summary>
    private static string? Coverage(IEnumerable<(DateOnly From, DateOnly To)>? segments, IntercompanyAccrualComputationInput input)
    {
        var cursor = input.AccrualFrom;
        foreach (var (from, to) in segments ?? [])
        {
            if (to <= from || from != cursor)
                return $"does not continuously cover {cursor:yyyy-MM-dd}.";
            cursor = to;
        }
        return cursor == input.AccrualTo ? null : $"does not continuously cover {cursor:yyyy-MM-dd}.";
    }

    private static DateOnly Later(DateOnly left, DateOnly right) => left > right ? left : right;
    private static DateOnly Earlier(DateOnly left, DateOnly right) => left < right ? left : right;
}
