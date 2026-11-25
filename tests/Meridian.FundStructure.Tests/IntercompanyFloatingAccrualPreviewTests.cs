using Meridian.Application.FundStructure;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

/// <summary>
/// Rule intercompany-accrual-v2 over synthetic floating loans on the weekends-only calendar. April and May 2026 both
/// start on business days, so each fixing date is derived by hand: Apr 1 (Wed) less two business days is Mar 30 (Mon),
/// and May 1 (Fri) less two is Apr 29 (Wed).
/// </summary>
public sealed class IntercompanyFloatingAccrualPreviewTests
{
    private static readonly Guid AgreementId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ArrangementId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid RevisionOne = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid RevisionTwo = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly IntercompanyApprovedRateFixingDto MarchFixing = Fixing(new(2026, 3, 30), 0.04m);
    private static readonly IntercompanyApprovedRateFixingDto AprilFixing = Fixing(new(2026, 4, 29), 0.045m);

    [Fact]
    public void EachPeriodUsesItsOwnInAdvanceFixingPlusTheDeclaredSpread_AndPinsIt()
    {
        // Apr 20-May 1: 11 days x 1,000,000 x (4.00% + 1.50%) / 360 = 1,680.5555...
        // May 1-May 10: 9 days x 1,000,000 x (4.50% + 1.50%) / 360 = 1,500.0000; total 3,180.5555... -> 3,180.56
        var preview = Preview(new(2026, 4, 20), new(2026, 5, 10), [Authority(new(2026, 4, 20), new(2026, 5, 10))],
            [MarchFixing, AprilFixing, Fixing(new(2026, 4, 28), 0.09m)]);

        Assert.Equal(IntercompanyAccrualRules.StatusReady, preview.Status);
        Assert.Equal(IntercompanyAccrualRules.FloatingRuleVersion, preview.RuleVersion);
        Assert.Equal(3180.56m, preview.Amount);
        Assert.Equal([0.055m, 0.06m], preview.Slices.Select(slice => slice.AnnualRate));
        Assert.Equal([new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 29)], preview.Slices.Select(slice => slice.Fixing!.FixingDate));
        var pin = preview.Slices[1].Fixing!;
        Assert.Equal((AprilFixing.FixingId, "SOFR", 0.045m, 150m, 2, AprilFixing.ValueDigest),
            (pin.FixingId, pin.IndexReference, pin.IndexRate, pin.SpreadBps, pin.FixingLagBusinessDays, pin.ValueDigest));
    }

    [Fact]
    public void AMissingFixingRefusesTheWholeWindow_AndANeighbouringDateIsNeverUsed()
    {
        var preview = Preview(new(2026, 4, 20), new(2026, 5, 10), [Authority(new(2026, 4, 20), new(2026, 5, 10))],
            [MarchFixing, Fixing(new(2026, 4, 28), 0.045m), Fixing(new(2026, 4, 30), 0.045m)]);

        Assert.Equal(IntercompanyAccrualRules.StatusUnavailable, preview.Status);
        Assert.Null(preview.Amount);
        Assert.Empty(preview.Slices);
        var reason = Assert.Single(preview.Diagnostics);
        Assert.Contains("SOFR fixing exists for 2026-04-29", reason);
        Assert.Contains("never inferred", reason);
    }

    [Fact]
    public void ZeroLagUsesThePeriodStartItself()
    {
        // Apr 1-Apr 11: 10 days x 1,000,000 x (3.60% + 1.50%) / 360 = 1,416.6666... -> 1,416.67
        var preview = Preview(new(2026, 4, 1), new(2026, 4, 11), [Authority(new(2026, 4, 1), new(2026, 4, 11), Loan(lag: 0))],
            [Fixing(new(2026, 4, 1), 0.036m), MarchFixing]);

        Assert.Equal(1416.67m, preview.Amount);
        Assert.Equal(new DateOnly(2026, 4, 1), Assert.Single(preview.Slices).Fixing!.FixingDate);
    }

    [Fact]
    public void AnAmendmentFromFixedToFloatingIsOneV2Window_WithFixedDaysUnpinned()
    {
        // Apr 20-Apr 25 fixed 5%: 5 days = 694.4444...; Apr 25-May 1 floating 5.50%: 6 days = 916.6666...; total 1,611.11
        var preview = Preview(new(2026, 4, 20), new(2026, 5, 1),
            [Authority(new(2026, 4, 20), new(2026, 4, 25), Loan(fixedRate: 0.05m)),
             Authority(new(2026, 4, 25), new(2026, 5, 1), Loan(version: 2), RevisionTwo)],
            [MarchFixing]);

        Assert.Equal(IntercompanyAccrualRules.FloatingRuleVersion, preview.RuleVersion);
        Assert.Equal(1611.11m, preview.Amount);
        Assert.Null(preview.Slices[0].Fixing);
        Assert.Equal(new DateOnly(2026, 3, 30), preview.Slices[1].Fixing!.FixingDate);
    }

    [Fact]
    public void AFloatingLoanWithoutADeclaredLag_IsRefused()
    {
        var preview = Preview(new(2026, 4, 1), new(2026, 5, 1), [Authority(new(2026, 4, 1), new(2026, 5, 1), Loan(lag: null))], [MarchFixing]);

        Assert.Null(preview.Amount);
        Assert.Contains("without a fixing lag", Assert.Single(preview.Diagnostics));
    }

    [Fact]
    public void FixingsOfAnotherIndexAreIgnored()
    {
        var preview = Preview(new(2026, 4, 1), new(2026, 4, 11), [Authority(new(2026, 4, 1), new(2026, 4, 11))],
            [MarchFixing with { IndexReference = "SONIA" }]);

        Assert.Null(preview.Amount);
        Assert.Contains("SOFR fixing exists for 2026-03-30", Assert.Single(preview.Diagnostics));
    }

    private static IntercompanyAccrualPreviewDto Preview(DateOnly from, DateOnly to, IReadOnlyList<IntercompanyAccrualAuthoritySegment> authority,
        IReadOnlyList<IntercompanyApprovedRateFixingDto> fixings)
        => new IntercompanyAccrualPreviewService(new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]))
            .Preview(new(from, to, authority, [new(from, to, 1_000_000m, "principal-evidence")]) { Fixings = fixings });

    private static IntercompanyAccrualAuthoritySegment Authority(DateOnly from, DateOnly to, IntercompanyAgreementRevisionDto? legal = null, Guid? revision = null)
        => new(from, to, ArrangementId, revision ?? RevisionOne, legal ?? Loan());

    private static IntercompanyApprovedRateFixingDto Fixing(DateOnly date, decimal rate)
        => new(IntercompanyRateFixingRules.FixingId("tenant-a", "SOFR", date), "SOFR", date, rate, $"digest-{date:yyyyMMdd}");

    /// <summary>A floating SOFR + 150 bps loan, declared with surrounding space and lower case as an operator might type it.</summary>
    private static IntercompanyAgreementRevisionDto Loan(int version = 1, int? lag = 2, decimal? fixedRate = null)
    {
        var start = new DateOnly(2026, 4, 1);
        var structured = new IntercompanyStructuredTermsDto(
            new(start, new(2027, 4, 1), IntercompanyFrequencyDto.Monthly, "WEEKENDS", BusinessDayConvention.Following, 1),
            fixedRate is null ? IntercompanyRateBasisDto.Floating : IntercompanyRateBasisDto.Fixed, fixedRate,
            fixedRate is null ? " sofr " : null, fixedRate is null ? 150m : null,
            IntercompanyDayCountDto.Actual360, IntercompanyCompoundingDto.Simple,
            FixingLagBusinessDays: fixedRate is null ? lag : null);
        var draft = new IntercompanyAgreementDraftDto("IC-FLOAT", "Synthetic floating loan", Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Lender", Guid.Parse("22222222-2222-2222-2222-222222222222"), "Borrower", IntercompanyAgreementTypeDto.Loan, "USD", start, new(2027, 4, 1),
            new() { StructuredTerms = structured },
            Action: version == 1 ? IntercompanyAgreementActionDto.Activate : IntercompanyAgreementActionDto.Amend);
        return new(AgreementId, version, version == 1 ? null : version - 1, version == 1 ? null : version - 1, draft.Action, draft,
            Guid.NewGuid(), $"hash-v{version}", ["preparer"], "submitter", "reviewer",
            new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), "Synthetic approved terms");
    }
}
