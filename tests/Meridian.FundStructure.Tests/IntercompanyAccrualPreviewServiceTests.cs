using Meridian.Application.FundStructure;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

/// <summary>Synthetic fixed-rate loans; every expected amount is derived by hand in the test's comment.</summary>
public sealed class IntercompanyAccrualPreviewServiceTests
{
    private static readonly Guid Lender = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Borrower = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AgreementId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ArrangementId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid RevisionOne = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid RevisionTwo = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public void FullMonth_UsesDeclaredRateAndDayCount_AndPinsEveryInput()
    {
        // 1,000,000 x 5% x 31/360 = 4,305.5555... -> 4,305.56
        var preview = Preview(new(2026, 1, 1), new(2026, 2, 1), [Authority(new(2026, 1, 1), new(2026, 2, 1))],
            [Principal(new(2026, 1, 1), new(2026, 2, 1), 1_000_000m)]);

        Assert.Equal(IntercompanyAccrualRules.StatusReady, preview.Status);
        Assert.Equal(4305.56m, preview.Amount);
        Assert.Equal("USD", preview.Currency);
        Assert.Equal(AgreementId, preview.AgreementId);
        Assert.Equal(IntercompanyAccrualRules.RuleVersion, preview.RuleVersion);
        var slice = Assert.Single(preview.Slices);
        Assert.Equal((1, "hash-v1", RevisionOne, 0.05m, "ACT/360"),
            (slice.AgreementVersion, slice.AgreementPayloadHash, slice.AccountingArrangementRevisionId, slice.AnnualRate, slice.DayCount));
        Assert.Equal("principal-evidence", slice.PrincipalEvidenceReference);
        var calendar = Assert.Single(preview.Calendars);
        Assert.Equal(BusinessDayCalendars.WeekendsOnly.ContentHash, calendar.ContentHash);
    }

    [Fact]
    public void RateResetInsideTheWindow_ProratesEachSideUnderItsOwnApprovedRevision()
    {
        // 15 days at 5% = 2,083.3333...; 16 days at 6% = 2,666.6666...; total 4,750.00
        var amended = Loan(version: 2, rate: 0.06m, effectiveFrom: new(2026, 1, 16));
        var preview = Preview(new(2026, 1, 1), new(2026, 2, 1),
            [Authority(new(2026, 1, 1), new(2026, 1, 16)), Authority(new(2026, 1, 16), new(2026, 2, 1), amended, RevisionTwo)],
            [Principal(new(2026, 1, 1), new(2026, 2, 1), 1_000_000m)]);

        Assert.Equal(4750.00m, preview.Amount);
        Assert.Equal([(1, 0.05m, new DateOnly(2026, 1, 16)), (2, 0.06m, new DateOnly(2026, 2, 1))],
            preview.Slices.Select(slice => (slice.AgreementVersion, slice.AnnualRate, slice.To)));
        Assert.Equal([RevisionOne, RevisionTwo], preview.Slices.Select(slice => slice.AccountingArrangementRevisionId));
    }

    [Fact]
    public void PrincipalChangeAndSchedulePeriodBoundary_EachCutTheWindow()
    {
        // Jan 20-Feb 1: 12 days on 1,000,000 = 1,666.6666...; Feb 1-Feb 5: 4 days on 1,000,000 = 555.5555...;
        // Feb 5-Feb 10: 5 days on 400,000 = 277.7777...; total 2,500.00
        var preview = Preview(new(2026, 1, 20), new(2026, 2, 10), [Authority(new(2026, 1, 20), new(2026, 2, 10))],
            [Principal(new(2026, 1, 20), new(2026, 2, 5), 1_000_000m), Principal(new(2026, 2, 5), new(2026, 2, 10), 400_000m)]);

        Assert.Equal(2500.00m, preview.Amount);
        Assert.Equal([1, 2, 2], preview.Slices.Select(slice => slice.SchedulePeriodSequence));
        Assert.Equal([1_000_000m, 1_000_000m, 400_000m], preview.Slices.Select(slice => slice.Principal));
    }

    [Fact]
    public void SlicesAreSummedUnroundedAndRoundedOnce()
    {
        // Three one-day slices of 100 x 5% / 360 = 0.01388... each: 0.0416... -> 0.04, never 3 x 0.01.
        var preview = Preview(new(2026, 1, 5), new(2026, 1, 8), [Authority(new(2026, 1, 5), new(2026, 1, 8))],
            [Principal(new(2026, 1, 5), new(2026, 1, 6), 100m), Principal(new(2026, 1, 6), new(2026, 1, 7), 100m),
             Principal(new(2026, 1, 7), new(2026, 1, 8), 100m)]);

        Assert.Equal(3, preview.Slices.Count);
        Assert.Equal(0.04m, preview.Amount);
        Assert.True(preview.UnroundedAmount is > 0.0416m and < 0.0417m);
    }

    [Theory]
    [InlineData(IntercompanyDayCountDto.Thirty360)]
    [InlineData(IntercompanyDayCountDto.ThirtyE360)]
    [InlineData(IntercompanyDayCountDto.Actual360)]
    public void CuttingAPeriodNeverChangesItsTotal_EvenOnThe31stUnderThirty360(IntercompanyDayCountDto dayCount)
    {
        // 30/360: January is 30 convention days, so 1,000,000 x 5% x 30/360 = 4,166.67 however it is cut.
        // Restarting the convention at Jan 31 would count 30 + 1 days and give 4,305.56.
        var loan = Loan(dayCount: dayCount);
        var whole = Preview(new(2026, 1, 1), new(2026, 2, 1), [Authority(new(2026, 1, 1), new(2026, 2, 1), loan)],
            [Principal(new(2026, 1, 1), new(2026, 2, 1), 1_000_000m)]);
        var cut = Preview(new(2026, 1, 1), new(2026, 2, 1), [Authority(new(2026, 1, 1), new(2026, 2, 1), loan)],
            [Principal(new(2026, 1, 1), new(2026, 1, 15), 1_000_000m), Principal(new(2026, 1, 15), new(2026, 1, 31), 1_000_000m),
             Principal(new(2026, 1, 31), new(2026, 2, 1), 1_000_000m)]);

        Assert.Equal(3, cut.Slices.Count);
        Assert.Equal(whole.Amount, cut.Amount);
        Assert.Equal(whole.Slices.Sum(slice => slice.YearFraction), cut.Slices.Sum(slice => slice.YearFraction));
        Assert.Equal(dayCount == IntercompanyDayCountDto.Actual360 ? 4305.56m : 4166.67m, cut.Amount);
    }

    [Fact]
    public void ZeroMinorUnitCurrency_RoundsToWholeUnits()
    {
        // 1,000,000 x 5% x 31/360 = 4,305.55... -> 4,306 JPY
        var preview = Preview(new(2026, 1, 1), new(2026, 2, 1),
            [Authority(new(2026, 1, 1), new(2026, 2, 1), Loan(currency: "JPY"))],
            [Principal(new(2026, 1, 1), new(2026, 2, 1), 1_000_000m)]);

        Assert.Equal(4306m, preview.Amount);
    }

    [Theory]
    [InlineData("floating", "fixing authority")]
    [InlineData("compounding", "simple interest")]
    [InlineData("unstructured", "unstructured terms")]
    [InlineData("terminated", "unterminated loan")]
    [InlineData("fee", "unterminated loan")]
    public void TermsThatWouldNeedInference_AreRefusedWithoutAnAmount(string variant, string expected)
    {
        var loan = Loan();
        var structured = loan.Terms.Terms!.StructuredTerms!;
        var draft = variant switch
        {
            "floating" => loan.Terms with { Terms = new() { StructuredTerms = structured with { RateBasis = IntercompanyRateBasisDto.Floating, FixedAnnualRate = null, FloatingIndexReference = "SOFR", SpreadBps = 150m } } },
            "compounding" => loan.Terms with { Terms = new() { StructuredTerms = structured with { Compounding = IntercompanyCompoundingDto.Monthly } } },
            "unstructured" => loan.Terms with { Terms = new(RateDescription: "Five percent, monthly") },
            "fee" => loan.Terms with { AgreementType = IntercompanyAgreementTypeDto.ManagementFee },
            _ => loan.Terms
        };
        var revision = loan with { Terms = draft, Action = variant == "terminated" ? IntercompanyAgreementActionDto.Terminate : loan.Action };

        var preview = Preview(new(2026, 1, 1), new(2026, 2, 1), [Authority(new(2026, 1, 1), new(2026, 2, 1), revision)],
            [Principal(new(2026, 1, 1), new(2026, 2, 1), 1_000_000m)]);

        Assert.Equal(IntercompanyAccrualRules.StatusUnavailable, preview.Status);
        Assert.Null(preview.Amount);
        Assert.Contains(expected, Assert.Single(preview.Diagnostics));
    }

    [Fact]
    public void AuthorityOrPrincipalGap_RefusesTheWholeWindowInsteadOfAccruingPartOfIt()
    {
        var authorityGap = Preview(new(2026, 1, 1), new(2026, 2, 1), [Authority(new(2026, 1, 1), new(2026, 1, 20))],
            [Principal(new(2026, 1, 1), new(2026, 2, 1), 1_000_000m)]);
        var principalGap = Preview(new(2026, 1, 1), new(2026, 2, 1), [Authority(new(2026, 1, 1), new(2026, 2, 1))],
            [Principal(new(2026, 1, 1), new(2026, 1, 10), 1_000_000m), Principal(new(2026, 1, 12), new(2026, 2, 1), 1_000_000m)]);

        Assert.Contains("2026-01-20", Assert.Single(authorityGap.Diagnostics));
        Assert.Contains("2026-01-10", Assert.Single(principalGap.Diagnostics));
        Assert.All(new[] { authorityGap, principalGap }, preview => Assert.Null(preview.Amount));
    }

    [Fact]
    public void DaysOutsideTheDeclaredSchedule_DoNotAccrueByInference()
    {
        var lateSchedule = Loan(scheduleStart: new(2026, 2, 1));
        var preview = Preview(new(2026, 1, 15), new(2026, 2, 15), [Authority(new(2026, 1, 15), new(2026, 2, 15), lateSchedule)],
            [Principal(new(2026, 1, 15), new(2026, 2, 15), 1_000_000m)]);

        Assert.Equal(IntercompanyAccrualRules.StatusUnavailable, preview.Status);
        Assert.Contains("no schedule period for 2026-01-15", Assert.Single(preview.Diagnostics));
    }

    [Fact]
    public void MissingCalendar_ZeroPrincipal_AndOversizedWindow_AreUnavailable()
    {
        var input = new IntercompanyAccrualComputationInput(new(2026, 1, 1), new(2026, 2, 1),
            [Authority(new(2026, 1, 1), new(2026, 2, 1))], [Principal(new(2026, 1, 1), new(2026, 2, 1), 1_000_000m)]);
        var noCalendar = new IntercompanyAccrualPreviewService(new StaticBusinessDayCalendarSource([])).Preview(input);
        var zero = Preview(new(2026, 1, 1), new(2026, 2, 1), [Authority(new(2026, 1, 1), new(2026, 2, 1))],
            [Principal(new(2026, 1, 1), new(2026, 2, 1), 0m)]);
        var oversized = Preview(new(2026, 1, 1), new(2027, 6, 1), [Authority(new(2026, 1, 1), new(2027, 6, 1))],
            [Principal(new(2026, 1, 1), new(2027, 6, 1), 1m)]);

        Assert.Contains("calendar is unavailable", Assert.Single(noCalendar.Diagnostics));
        Assert.Contains("no positive interest", Assert.Single(zero.Diagnostics));
        Assert.Contains("at most", Assert.Single(oversized.Diagnostics));
        Assert.All(new[] { noCalendar, zero, oversized }, preview => Assert.Null(preview.Amount));
    }

    private static IntercompanyAccrualPreviewDto Preview(DateOnly from, DateOnly to,
        IReadOnlyList<IntercompanyAccrualAuthoritySegment> authority, IReadOnlyList<IntercompanyAccrualPrincipalSegmentDto> principal)
        => new IntercompanyAccrualPreviewService(new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]))
            .Preview(new(from, to, authority, principal));

    private static IntercompanyAccrualAuthoritySegment Authority(DateOnly from, DateOnly to,
        IntercompanyAgreementRevisionDto? legal = null, Guid? revision = null)
        => new(from, to, ArrangementId, revision ?? RevisionOne, legal ?? Loan());

    private static IntercompanyAccrualPrincipalSegmentDto Principal(DateOnly from, DateOnly to, decimal amount)
        => new(from, to, amount, "principal-evidence");

    private static IntercompanyAgreementRevisionDto Loan(int version = 1, decimal rate = 0.05m, DateOnly? effectiveFrom = null,
        DateOnly? scheduleStart = null, string currency = "USD", IntercompanyDayCountDto dayCount = IntercompanyDayCountDto.Actual360)
    {
        var start = effectiveFrom ?? new DateOnly(2026, 1, 1);
        var draft = new IntercompanyAgreementDraftDto("IC-LOAN", "Synthetic loan", Lender, "Lender", Borrower, "Borrower",
            IntercompanyAgreementTypeDto.Loan, currency, start, new(2027, 1, 1),
            new()
            {
                StructuredTerms = new(new(scheduleStart ?? start, new(2027, 1, 1), IntercompanyFrequencyDto.Monthly, "WEEKENDS",
                        BusinessDayConvention.Following, 1), IntercompanyRateBasisDto.Fixed, rate,
                    DayCount: dayCount, Compounding: IntercompanyCompoundingDto.Simple)
            },
            Action: version == 1 ? IntercompanyAgreementActionDto.Activate : IntercompanyAgreementActionDto.Amend);
        return new(AgreementId, version, version == 1 ? null : version - 1, version == 1 ? null : version - 1, draft.Action, draft,
            Guid.NewGuid(), $"hash-v{version}", ["preparer"], "submitter", "reviewer",
            new(2025, 12, 1, 0, 0, 0, TimeSpan.Zero), "Synthetic approved terms");
    }
}
