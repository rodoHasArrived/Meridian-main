using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Turns a declared fixed management fee into an amount for a half-open window (rule
/// <see cref="IntercompanyFeeAccrualRules.RuleVersion"/>). Pure: it reads no ledger and creates nothing.
/// </summary>
/// <remarks>
/// Every day accrues under the approved revision applicable on that day. The window is cut at each authority and
/// schedule-period boundary. A per-period fee earns <c>fee x covered days / period days</c>; a per-annum fee earns
/// <c>fee x day-count fraction</c>, each fraction measured from the period start so cutting a period never changes
/// its total. Slices are summed unrounded and rounded once, half away from zero, to the currency's minor unit.
/// Percentage fees, an undeclared basis and days outside the declared schedule are refused whole.
/// </remarks>
public sealed class IntercompanyFeeAccrualPreviewService(IBusinessDayCalendarSource calendars) : IIntercompanyFeeAccrualPreviewService
{
    public IntercompanyFeeAccrualPreviewDto Preview(IntercompanyFeeAccrualComputationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var agreementId = input.Authority?.FirstOrDefault()?.LegalRevision?.AgreementId;
        var currency = input.Authority?.FirstOrDefault()?.LegalRevision?.Terms?.Currency;
        IntercompanyFeeAccrualPreviewDto Unavailable(string reason) => new(IntercompanyAccrualRules.StatusUnavailable,
            IntercompanyFeeAccrualRules.RuleVersion, agreementId, currency, input.AccrualFrom, input.AccrualTo, null, null,
            ScheduleGenerator.KernelVersion, [], [], [reason]);

        if (input.AccrualTo <= input.AccrualFrom)
            return Unavailable("The fee window must be a nonempty [from, to) range.");
        if (input.AccrualTo.DayNumber - input.AccrualFrom.DayNumber > IntercompanyAccrualRules.MaximumWindowDays)
            return Unavailable($"One fee request covers at most {IntercompanyAccrualRules.MaximumWindowDays} days.");
        var cursorCheck = input.AccrualFrom;
        foreach (var segment in input.Authority ?? [])
        {
            if (segment.To <= segment.From || segment.From != cursorCheck)
                return Unavailable($"Approved management-fee profile authority does not continuously cover {cursorCheck:yyyy-MM-dd}.");
            cursorCheck = segment.To;
        }
        if (cursorCheck != input.AccrualTo)
            return Unavailable($"Approved management-fee profile authority does not continuously cover {cursorCheck:yyyy-MM-dd}.");

        var slices = new List<IntercompanyFeeAccrualSliceDto>();
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var authority in input.Authority!)
        {
            var legal = authority.LegalRevision;
            var draft = legal.Terms;
            if (legal.AgreementId != agreementId || !string.Equals(draft.Currency, currency, StringComparison.Ordinal))
                return Unavailable("Every fee day must belong to one legal agreement and currency.");
            if (legal.Action == IntercompanyAgreementActionDto.Terminate || draft.AgreementType != IntercompanyAgreementTypeDto.ManagementFee)
                return Unavailable("A fee accrues only under an approved, unterminated management-fee revision.");
            if (draft.Terms?.StructuredTerms is not { } terms)
                return Unavailable($"Agreement version {legal.AgreementVersion} has unstructured terms; no fee or schedule is inferred from prose.");
            try
            { IntercompanyAgreementRules.ValidateComplete(draft); }
            catch (IntercompanyAgreementValidationException error)
            { return Unavailable($"Agreement version {legal.AgreementVersion}: {error.Message}"); }
            if (terms.AnnualFeeRate is not null)
                return Unavailable("Percentage management fees need a measured calculation base that this release does not provide.");
            if (terms.FixedFeeAmount is not { } fee || terms.FixedFeeBasis is not { } basis)
                return Unavailable($"Agreement version {legal.AgreementVersion} declares no fixed-fee basis; whether the amount is per period or per year is not inferred.");
            var schedule = terms.Schedule!;
            if (!calendars.TryGet(schedule.CalendarId, out var calendar))
                return Unavailable("The declared business-day calendar is unavailable; no fallback was applied.");
            var generated = ScheduleGenerator.Generate(IntercompanyStructuredTermsPreviewService.BuildSpec(schedule, calendar));
            if (!generated.Succeeded)
                return Unavailable(generated.Failure.Message);
            if (generated.Diagnostics.Count > 0)
                return Unavailable(generated.Diagnostics[0].Message);
            pins[calendar.CalendarId] = calendar.ContentHash;
            var convention = basis == IntercompanyFixedFeeBasisDto.PerAnnum
                ? Enum.Parse<DayCountConvention>(terms.DayCount!.Value.ToString()) : (DayCountConvention?)null;

            var cursor = authority.From;
            foreach (var period in generated.Periods)
            {
                var from = Later(period.AccrualStart, authority.From);
                var to = Earlier(period.AccrualEnd, authority.To);
                if (to <= from)
                    continue;
                if (from != cursor)
                    break;
                decimal fraction;
                if (convention is { } dayCount)
                {
                    if (!ScheduleDayCount.TryFraction(dayCount, period, generated, period.AccrualStart, to, out var through, out var refusal)
                        || !ScheduleDayCount.TryFraction(dayCount, period, generated, period.AccrualStart, from, out var before, out refusal))
                        return Unavailable(refusal);
                    fraction = through - before;
                }
                else
                {
                    fraction = (decimal)(to.DayNumber - from.DayNumber) / (period.AccrualEnd.DayNumber - period.AccrualStart.DayNumber);
                }
                slices.Add(new(slices.Count + 1, from, to, authority.AccountingArrangementRevisionId, legal.AgreementVersion,
                    legal.PayloadHash, period.Sequence, period.AccrualStart, period.AccrualEnd, basis, fee,
                    convention is { } label ? DayCountConventions.TermLabel(label) : null, fraction, fee * fraction));
                cursor = to;
            }
            if (cursor != authority.To)
                return Unavailable($"Agreement version {legal.AgreementVersion} declares no schedule period for {cursor:yyyy-MM-dd}; days outside the declared schedule do not accrue by inference.");
        }

        var unrounded = slices.Sum(slice => slice.UnroundedAmount);
        var amount = decimal.Round(unrounded, CurrencyMinorUnits.GetPrecision(currency!), MidpointRounding.AwayFromZero);
        var calendarPins = pins.OrderBy(pin => pin.Key, StringComparer.Ordinal)
            .Select(pin => new IntercompanyAccrualCalendarPinDto(pin.Key, pin.Value)).ToArray();
        return amount <= 0m
            ? new(IntercompanyAccrualRules.StatusUnavailable, IntercompanyFeeAccrualRules.RuleVersion, agreementId, currency,
                input.AccrualFrom, input.AccrualTo, null, null, ScheduleGenerator.KernelVersion, calendarPins, slices,
                ["The window produces no positive fee in the agreement currency; nothing is proposed."])
            : new(IntercompanyAccrualRules.StatusReady, IntercompanyFeeAccrualRules.RuleVersion, agreementId, currency,
                input.AccrualFrom, input.AccrualTo, amount, unrounded, ScheduleGenerator.KernelVersion, calendarPins, slices,
                ["Calculated proposal input only. Nothing is posted; each side still requires its own review."]);
    }

    private static DateOnly Later(DateOnly left, DateOnly right) => left > right ? left : right;
    private static DateOnly Earlier(DateOnly left, DateOnly right) => left < right ? left : right;
}
