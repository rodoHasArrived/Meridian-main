using Meridian.Application.FundStructure;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

/// <summary>
/// Rule intercompany-fee-v1 over synthetic management-fee agreements with monthly schedule periods from 1 January
/// 2026 (January has 31 days, February 28). Every expected amount is derived by hand in the test's comment.
/// </summary>
public sealed class IntercompanyFeeAccrualPreviewServiceTests
{
    private static readonly Guid AgreementId = Guid.Parse("73333333-3333-3333-3333-333333333333");
    private static readonly Guid RevisionOne = Guid.Parse("75555555-5555-5555-5555-555555555555");
    private static readonly Guid RevisionTwo = Guid.Parse("76666666-6666-6666-6666-666666666666");

    [Fact]
    public void AFullPeriodEarnsTheDeclaredPerPeriodFee_AndPinsItsInputs()
    {
        var preview = Preview(new(2026, 1, 1), new(2026, 2, 1), [Authority(new(2026, 1, 1), new(2026, 2, 1))]);

        Assert.Equal(IntercompanyAccrualRules.StatusReady, preview.Status);
        Assert.Equal(IntercompanyFeeAccrualRules.RuleVersion, preview.RuleVersion);
        Assert.Equal(30_000.00m, preview.Amount);
        var slice = Assert.Single(preview.Slices);
        Assert.Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), IntercompanyFixedFeeBasisDto.PerPeriod, 1m, (string?)null, RevisionOne, "hash-v1"),
            (slice.PeriodStart, slice.PeriodEnd, slice.Basis, slice.Fraction, slice.DayCount, slice.AccountingArrangementRevisionId, slice.AgreementPayloadHash));
        Assert.Equal(BusinessDayCalendars.WeekendsOnly.ContentHash, Assert.Single(preview.Calendars).ContentHash);
    }

    [Fact]
    public void APartialPeriodEarnsItsCoveredDaysOverThePeriodDays()
    {
        // 30,000 x 15/31 = 14,516.1290... -> 14,516.13
        var preview = Preview(new(2026, 1, 1), new(2026, 1, 16), [Authority(new(2026, 1, 1), new(2026, 1, 16))]);

        Assert.Equal(14_516.13m, preview.Amount);
    }

    [Fact]
    public void AWindowAcrossAPeriodBoundaryProratesEachPeriodByItsOwnLength_AndRoundsOnce()
    {
        // Jan 17-Feb 1: 30,000 x 15/31 = 14,516.1290...; Feb 1-Feb 15: 30,000 x 14/28 = 15,000; total 29,516.13
        var preview = Preview(new(2026, 1, 17), new(2026, 2, 15), [Authority(new(2026, 1, 17), new(2026, 2, 15))]);

        Assert.Equal(29_516.13m, preview.Amount);
        Assert.Equal([1, 2], preview.Slices.Select(slice => slice.SchedulePeriodSequence));
    }

    [Fact]
    public void APerAnnumFeeUsesTheDeclaredDayCount()
    {
        // 120,000 x 31/360 = 10,333.3333... -> 10,333.33
        var annual = Agreement(fee: 120_000m, basis: IntercompanyFixedFeeBasisDto.PerAnnum, dayCount: IntercompanyDayCountDto.Actual360);
        var preview = Preview(new(2026, 1, 1), new(2026, 2, 1), [Authority(new(2026, 1, 1), new(2026, 2, 1), annual)]);

        Assert.Equal(10_333.33m, preview.Amount);
        Assert.Equal("ACT/360", Assert.Single(preview.Slices).DayCount);
    }

    [Fact]
    public void AnAmendmentInsideAPeriodProratesEachSideUnderItsOwnRevision()
    {
        // Jan 1-Jan 16 at 30,000: 15/31 = 14,516.1290...; Jan 16-Feb 1 at 31,000: 16/31 = 16,000; total 30,516.13
        var amended = Agreement(version: 2, fee: 31_000m);
        var preview = Preview(new(2026, 1, 1), new(2026, 2, 1),
            [Authority(new(2026, 1, 1), new(2026, 1, 16)), Authority(new(2026, 1, 16), new(2026, 2, 1), amended, RevisionTwo)]);

        Assert.Equal(30_516.13m, preview.Amount);
        Assert.Equal([RevisionOne, RevisionTwo], preview.Slices.Select(slice => slice.AccountingArrangementRevisionId));
    }

    [Theory]
    [InlineData("no-basis", "no fixed-fee basis")]
    [InlineData("percentage", "Percentage management fees")]
    [InlineData("loan", "management-fee revision")]
    [InlineData("unstructured", "unstructured terms")]
    [InlineData("gap", "does not continuously cover 2026-01-20")]
    [InlineData("before-schedule", "no schedule period for 2025-12-20")]
    public void AnythingThatWouldNeedInferenceIsRefusedWithoutAnAmount(string variant, string expected)
    {
        var agreement = Agreement();
        var terms = agreement.Terms.Terms!.StructuredTerms!;
        IntercompanyAgreementRevisionDto With(IntercompanyAgreementDraftDto draft) => agreement with { Terms = draft };
        var (from, authority) = variant switch
        {
            "no-basis" => (new DateOnly(2026, 1, 1), new[] { Authority(new(2026, 1, 1), new(2026, 2, 1),
                With(agreement.Terms with { Terms = new() { StructuredTerms = terms with { FixedFeeBasis = null } } })) }),
            "percentage" => (new DateOnly(2026, 1, 1), new[] { Authority(new(2026, 1, 1), new(2026, 2, 1),
                With(agreement.Terms with { Terms = new() { StructuredTerms = terms with { FixedFeeAmount = null, FixedFeeBasis = null, AnnualFeeRate = 0.01m, CalculationBaseReference = "NAV" } } })) }),
            "loan" => (new DateOnly(2026, 1, 1), new[] { Authority(new(2026, 1, 1), new(2026, 2, 1),
                With(agreement.Terms with { AgreementType = IntercompanyAgreementTypeDto.Loan })) }),
            "unstructured" => (new DateOnly(2026, 1, 1), new[] { Authority(new(2026, 1, 1), new(2026, 2, 1),
                With(agreement.Terms with { Terms = new(Notes: "Thirty thousand a month") })) }),
            "gap" => (new DateOnly(2026, 1, 1), new[] { Authority(new(2026, 1, 1), new(2026, 1, 20)) }),
            _ => (new DateOnly(2025, 12, 20), new[] { Authority(new(2025, 12, 20), new(2026, 2, 1), Agreement(effectiveFrom: new(2025, 12, 1))) })
        };

        var preview = Preview(from, new(2026, 2, 1), authority);

        Assert.Equal(IntercompanyAccrualRules.StatusUnavailable, preview.Status);
        Assert.Null(preview.Amount);
        Assert.Contains(expected, Assert.Single(preview.Diagnostics));
    }

    [Fact]
    public void TheFeeBasisIsDeclaredOnlyForFixedManagementFees_AndAbsentFromRetainedJsonWhenUnset()
    {
        var draft = Agreement().Terms;
        var terms = draft.Terms!.StructuredTerms!;
        IntercompanyAgreementRules.ValidateComplete(draft);
        IntercompanyAgreementRules.ValidateComplete(Agreement(basis: IntercompanyFixedFeeBasisDto.PerAnnum, dayCount: IntercompanyDayCountDto.Actual365).Terms);
        foreach (var invalid in new[]
        {
            terms with { FixedFeeBasis = IntercompanyFixedFeeBasisDto.PerAnnum },
            terms with { DayCount = IntercompanyDayCountDto.Actual360 },
            terms with { FixedFeeAmount = null, AnnualFeeRate = 0.01m, CalculationBaseReference = "NAV" },
            terms with { FixedFeeBasis = (IntercompanyFixedFeeBasisDto)9 }
        })
            Assert.Throws<IntercompanyAgreementValidationException>(() => IntercompanyAgreementRules.ValidateComplete(
                draft with { Terms = new() { StructuredTerms = invalid } }));
        Assert.DoesNotContain("fixedFeeBasis", System.Text.Json.JsonSerializer.Serialize(draft.Terms with
        { StructuredTerms = terms with { FixedFeeBasis = null } }, FundStructureContractsJsonContext.Default.IntercompanyAgreementTermsDto));
    }

    private static IntercompanyFeeAccrualPreviewDto Preview(DateOnly from, DateOnly to, IReadOnlyList<IntercompanyAccrualAuthoritySegment> authority)
        => new IntercompanyFeeAccrualPreviewService(new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]))
            .Preview(new(from, to, authority));

    private static IntercompanyAccrualAuthoritySegment Authority(DateOnly from, DateOnly to, IntercompanyAgreementRevisionDto? legal = null, Guid? revision = null)
        => new(from, to, Guid.NewGuid(), revision ?? RevisionOne, legal ?? Agreement());

    private static IntercompanyAgreementRevisionDto Agreement(int version = 1, decimal fee = 30_000m,
        IntercompanyFixedFeeBasisDto basis = IntercompanyFixedFeeBasisDto.PerPeriod, IntercompanyDayCountDto? dayCount = null, DateOnly? effectiveFrom = null)
    {
        var start = effectiveFrom ?? new DateOnly(2026, 1, 1);
        var draft = new IntercompanyAgreementDraftDto("IC-FEE", "Synthetic management fee", Guid.Parse("71111111-1111-1111-1111-111111111111"),
            "Service provider", Guid.Parse("72222222-2222-2222-2222-222222222222"), "Recipient", IntercompanyAgreementTypeDto.ManagementFee, "USD",
            start, new(2027, 1, 1),
            new()
            {
                StructuredTerms = new(new(new DateOnly(2026, 1, 1), new(2027, 1, 1), IntercompanyFrequencyDto.Monthly, "WEEKENDS", BusinessDayConvention.Following),
                    DayCount: dayCount, FixedFeeAmount: fee, FixedFeeBasis: basis)
            },
            Action: version == 1 ? IntercompanyAgreementActionDto.Activate : IntercompanyAgreementActionDto.Amend);
        return new(AgreementId, version, version == 1 ? null : version - 1, version == 1 ? null : version - 1, draft.Action, draft,
            Guid.NewGuid(), $"hash-v{version}", ["preparer"], "submitter", "reviewer",
            new(2025, 11, 1, 0, 0, 0, TimeSpan.Zero), "Synthetic approved terms");
    }
}
