using Meridian.Application.FundStructure;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

/// <summary>
/// Rule intercompany-accrual-v3 over a synthetic 1,000,000 loan at 5% ACT/360, compounding monthly on a monthly
/// schedule from 1 January 2026. January's posted accrual (31 days) is 4,305.56, dated 31 January.
/// </summary>
public sealed class IntercompanyCompoundingAccrualPreviewTests
{
    private static readonly Guid AgreementId = Guid.Parse("83333333-3333-3333-3333-333333333333");
    private static readonly Guid Revision = Guid.Parse("85555555-5555-5555-5555-555555555555");
    private static readonly DateOnly January = new(2026, 1, 1);
    private static readonly DateOnly February = new(2026, 2, 1);
    private static readonly DateOnly March = new(2026, 3, 1);

    [Fact]
    public void TheFirstPeriodHasNothingToCompound_AndIsV3WithAZeroBase()
    {
        // 1,000,000 x 5% x 31/360 = 4,305.5555... -> 4,305.56
        var preview = Preview(January, February, interest: [], windows: []);

        Assert.Equal(IntercompanyAccrualRules.StatusReady, preview.Status);
        Assert.Equal(IntercompanyAccrualRules.CompoundingRuleVersion, preview.RuleVersion);
        Assert.Equal(4305.56m, preview.Amount);
        Assert.Equal(0m, Assert.Single(preview.Slices).CompoundedInterest);
    }

    [Fact]
    public void ALaterPeriodCompoundsOnThePostedAccruedInterestAtItsStart()
    {
        // (1,000,000 + 4,305.56) x 5% x 28/360 = 3,905.6327... -> 3,905.63
        var preview = Preview(February, March,
            interest: [Balance(January, new(2026, 1, 31), 0m), Balance(new(2026, 1, 31), March, 4305.56m)],
            windows: [new(January, February)]);

        Assert.Equal(3905.63m, preview.Amount);
        var slice = Assert.Single(preview.Slices);
        Assert.Equal((1_000_000m, 4305.56m), (slice.Principal, slice.CompoundedInterest));
    }

    [Fact]
    public void SettledInterestNoLongerCompounds()
    {
        // The January interest was paid on 1 February, so the base is principal only: 1,000,000 x 5% x 28/360 = 3,888.89
        var preview = Preview(February, March,
            interest: [Balance(January, new(2026, 1, 31), 0m), Balance(new(2026, 1, 31), February, 4305.56m), Balance(February, March, 0m)],
            windows: [new(January, February)]);

        Assert.Equal(3888.89m, preview.Amount);
        Assert.Equal(0m, Assert.Single(preview.Slices).CompoundedInterest);
    }

    [Fact]
    public void AnEarlierDayWithoutAPostedAccrualRefusesTheWindow_RatherThanCompoundingOnAnEstimate()
    {
        var preview = Preview(February, March, interest: [], windows: [new(January, new(2026, 1, 20))]);

        Assert.Null(preview.Amount);
        Assert.Contains("do not cover 2026-01-20", Assert.Single(preview.Diagnostics));
    }

    [Fact]
    public void OneCompoundingRequestStaysInsideOneSchedulePeriod()
    {
        var preview = Preview(new(2026, 1, 15), new(2026, 2, 15), interest: [], windows: [new(January, new(2026, 1, 15))]);

        Assert.Null(preview.Amount);
        Assert.Contains("one schedule period at most", Assert.Single(preview.Diagnostics));
    }

    [Fact]
    public void ASimpleLoanStaysV1WithoutACompoundingField()
    {
        var preview = Preview(January, February, interest: [], windows: [], compounding: IntercompanyCompoundingDto.Simple);

        Assert.Equal(IntercompanyAccrualRules.RuleVersion, preview.RuleVersion);
        Assert.Null(Assert.Single(preview.Slices).CompoundedInterest);
    }

    private static IntercompanyAccrualPreviewDto Preview(DateOnly from, DateOnly to, IReadOnlyList<IntercompanyAccrualPrincipalSegmentDto> interest,
        IReadOnlyList<IntercompanyAccrualWindowDto> windows, IntercompanyCompoundingDto compounding = IntercompanyCompoundingDto.Monthly)
        => new IntercompanyAccrualPreviewService(new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]))
            .Preview(new(from, to, [new(from, to, Guid.NewGuid(), Revision, Loan(compounding))], [new(from, to, 1_000_000m, "principal-evidence")])
            {
                AccruedInterest = interest,
                PostedAccrualWindows = windows,
                FirstPrincipalDate = January
            });

    private static IntercompanyAccrualPrincipalSegmentDto Balance(DateOnly from, DateOnly to, decimal amount) => new(from, to, amount, "interest-evidence");

    private static IntercompanyAgreementRevisionDto Loan(IntercompanyCompoundingDto compounding)
    {
        var draft = new IntercompanyAgreementDraftDto("IC-COMP", "Synthetic compounding loan", Guid.Parse("81111111-1111-1111-1111-111111111111"),
            "Lender", Guid.Parse("82222222-2222-2222-2222-222222222222"), "Borrower", IntercompanyAgreementTypeDto.Loan, "USD", January, new(2027, 1, 1),
            new()
            {
                StructuredTerms = new(new(January, new(2027, 1, 1), IntercompanyFrequencyDto.Monthly, "WEEKENDS", BusinessDayConvention.Following),
                    IntercompanyRateBasisDto.Fixed, 0.05m, DayCount: IntercompanyDayCountDto.Actual360, Compounding: compounding)
            });
        return new(AgreementId, 1, null, null, IntercompanyAgreementActionDto.Activate, draft, Guid.NewGuid(), "hash-v1",
            ["preparer"], "submitter", "reviewer", new(2025, 12, 1, 0, 0, 0, TimeSpan.Zero), "Synthetic approved terms");
    }
}
