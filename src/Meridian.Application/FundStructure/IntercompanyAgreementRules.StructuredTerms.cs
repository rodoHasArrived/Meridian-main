using System.Globalization;
using Meridian.Contracts.FundStructure;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Application.FundStructure;

public static partial class IntercompanyAgreementRules
{
    private static IntercompanyStructuredTermsDto NormalizeStructuredTerms(IntercompanyStructuredTermsDto terms)
    {
        if (terms.Allocations?.Any(item => item is null) == true)
            Fail("Allocation entries cannot be null.");
        return terms with
        {
            Schedule = terms.Schedule is null ? null : terms.Schedule with { CalendarId = terms.Schedule.CalendarId?.Trim() ?? string.Empty },
            FixedAnnualRate = CanonicalDecimal(terms.FixedAnnualRate),
            SpreadBps = CanonicalDecimal(terms.SpreadBps),
            FixedFeeAmount = CanonicalDecimal(terms.FixedFeeAmount),
            AnnualFeeRate = CanonicalDecimal(terms.AnnualFeeRate),
            FloatingIndexReference = NormalizeOptional(terms.FloatingIndexReference),
            CalculationBaseReference = NormalizeOptional(terms.CalculationBaseReference),
            AllocationDriverReference = NormalizeOptional(terms.AllocationDriverReference),
            Allocations = terms.Allocations?.OrderBy(item => item.EntityId)
                .Select(item => item with { Percent = CanonicalDecimal(item.Percent)!.Value }).ToArray(),
            Guarantee = terms.Guarantee is null ? null : terms.Guarantee with
            {
                BeneficiaryReference = terms.Guarantee.BeneficiaryReference?.Trim() ?? string.Empty,
                LimitAmount = CanonicalDecimal(terms.Guarantee.LimitAmount)
            }
        };
    }

    private static decimal? CanonicalDecimal(decimal? value) => value is { } amount
        ? decimal.Parse(amount.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) : null;

    private static void ValidateStructuredShape(IntercompanyStructuredTermsDto terms)
    {
        if (terms.SchemaVersion != 1)
            Fail("Unsupported structured agreement terms schema version.");
        if ((terms.RateBasis is { } basis && !Enum.IsDefined(basis)) ||
            (terms.DayCount is { } dayCount && !Enum.IsDefined(dayCount)) ||
            (terms.Compounding is { } compounding && !Enum.IsDefined(compounding)))
            Fail("Unknown rate basis, day count or compounding declaration.");
        if (terms.Schedule is { } schedule && (!Enum.IsDefined(schedule.Frequency) ||
            !Enum.IsDefined(schedule.BusinessDayConvention) || schedule.SettlementBusinessDays is < 0 or > 3660))
            Fail("Unknown schedule convention or unsupported settlement business days.");
        if (terms.Allocations?.Any(item => item is null) == true)
            Fail("Allocation entries cannot be null.");
    }

    private static void ValidateStructuredComplete(IntercompanyAgreementDraftDto draft, IntercompanyStructuredTermsDto terms)
    {
        ValidateStructuredShape(terms);
        if (draft.AgreementType == IntercompanyAgreementTypeDto.Guarantee)
        {
            if (terms.Guarantee is null || string.IsNullOrWhiteSpace(terms.Guarantee.BeneficiaryReference) || terms.Guarantee.LimitAmount < 0)
                Fail("Guarantee disclosure requires an explicit beneficiary reference and a nonnegative limit when stated.");
            if (terms.Schedule is not null || HasLoanFields(terms) || HasFeeFields(terms) || HasAllocationFields(terms) || draft.Terms?.FixedAnnualRate is not null)
                Fail("Guarantees are disclosure-only and cannot contain schedules, rates, fees or allocation terms.");
            return;
        }
        if (draft.AgreementType is not (IntercompanyAgreementTypeDto.Loan or IntercompanyAgreementTypeDto.ManagementFee or IntercompanyAgreementTypeDto.CostShare))
            Fail("This agreement type retains unstructured terms; no structured calculation contract is supported.");
        if (terms.Guarantee is not null)
            Fail("Guarantee disclosure fields belong only to guarantee agreements.");
        var schedule = terms.Schedule;
        if (schedule is null || string.IsNullOrWhiteSpace(schedule.CalendarId) || schedule.StartDate < draft.EffectiveFrom ||
            schedule.EndDate <= schedule.StartDate || (draft.EffectiveTo is { } end && schedule.EndDate > end))
            Fail("Structured terms require bounded schedule dates inside the legal term and an explicit business-day calendar.");
        if (draft.Terms?.SettlementDays is { } legacyDays && legacyDays != schedule!.SettlementBusinessDays)
            Fail("Legacy settlement days and structured settlement business days conflict.");
        if (draft.AgreementType == IntercompanyAgreementTypeDto.Loan)
        {
            if (terms.RateBasis is null || terms.DayCount is null || terms.Compounding is null)
                Fail("Structured loans require rate basis, day count and compounding declarations.");
            if (terms.RateBasis == IntercompanyRateBasisDto.Fixed)
            {
                if (terms.FixedAnnualRate is null || terms.FixedAnnualRate is < -1m or > 1m || terms.FloatingIndexReference is not null || terms.SpreadBps is not null)
                    Fail("Fixed annual rates use decimal fractions between -1 and 1; floating fields must be absent.");
                if (draft.Terms?.FixedAnnualRate is { } legacyRate && legacyRate != terms.FixedAnnualRate)
                    Fail("Legacy and structured fixed annual rates conflict.");
            }
            else if (terms.FixedAnnualRate is not null || string.IsNullOrWhiteSpace(terms.FloatingIndexReference) || terms.SpreadBps is null || draft.Terms?.FixedAnnualRate is not null)
                Fail("Floating terms require an index reference and spread in basis points, with no fixed annual rate.");
            if (HasFeeFields(terms) || HasAllocationFields(terms))
                Fail("Loan terms cannot contain fee or allocation fields.");
            return;
        }
        if (HasLoanFields(terms) || draft.Terms?.FixedAnnualRate is not null)
            Fail("Loan rate fields are not supported for this agreement type.");
        if (draft.AgreementType == IntercompanyAgreementTypeDto.ManagementFee)
        {
            if ((terms.FixedFeeAmount is null) == (terms.AnnualFeeRate is null) || terms.FixedFeeAmount < 0 || terms.AnnualFeeRate is < 0 or > 1m)
                Fail("Management fees require one nonnegative fixed amount or annual decimal rate between 0 and 1.");
            if (terms.AnnualFeeRate is not null && string.IsNullOrWhiteSpace(terms.CalculationBaseReference))
                Fail("Percentage management fees require an explicit calculation-base reference.");
            if (terms.FixedFeeAmount is not null && terms.CalculationBaseReference is not null)
                Fail("Fixed management fees do not use a percentage calculation base.");
            if (HasAllocationFields(terms))
                Fail("Management fee terms cannot contain allocation fields.");
            return;
        }
        if (HasFeeFields(terms))
            Fail("Cost-share terms cannot contain management fee fields.");
        if ((terms.Allocations is { Count: > 0 }) == !string.IsNullOrWhiteSpace(terms.AllocationDriverReference))
            Fail("Cost shares require either fixed percentages or an explicit allocation-driver reference.");
        if (terms.Allocations is { } allocations)
        {
            if (allocations.Count == 0 || allocations.Any(item => item.EntityId == Guid.Empty || item.Percent <= 0 || item.Percent > 100) ||
                allocations.Select(item => item.EntityId).Distinct().Count() != allocations.Count || allocations.Sum(item => item.Percent) != 100m ||
                allocations.Any(item => item.EntityId != draft.PartyOneId && item.EntityId != draft.PartyTwoId))
                Fail("Fixed cost-share percentages must name distinct agreement parties and total exactly 100 percent.");
        }
    }

    private static bool HasLoanFields(IntercompanyStructuredTermsDto terms) => terms.RateBasis is not null ||
        terms.FixedAnnualRate is not null || terms.FloatingIndexReference is not null || terms.SpreadBps is not null ||
        terms.DayCount is not null || terms.Compounding is not null;
    private static bool HasFeeFields(IntercompanyStructuredTermsDto terms) => terms.FixedFeeAmount is not null ||
        terms.AnnualFeeRate is not null || terms.CalculationBaseReference is not null;
    private static bool HasAllocationFields(IntercompanyStructuredTermsDto terms) => terms.Allocations is not null || terms.AllocationDriverReference is not null;
}
