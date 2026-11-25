using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using NSubstitute;

namespace Meridian.Tests.Strategies;

/// <summary>
/// ADR-034 consumer migration for accruals: the source adapter reads the terms valid at the end of the
/// accrual period, flags an in-period economic change, and keeps reading the projection row when the
/// period-end economics are the latest recorded ones.
/// </summary>
public sealed class SecurityMasterAccountingValidTimeTests
{
    // The run's portfolio is dated mid-January 2041, so the accrual period is January:
    // PeriodStart 2041-01-01, PeriodEnd 2041-02-01 (exclusive). 30/360 over that month is 1/12.
    private static readonly DateTimeOffset RunAsOf = new(2041, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset IssueInstant = new(2041, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const decimal Par = 1_000_000m;
    private const decimal FivePercentJanuary = 4_166.67m;
    private const decimal SixPercentJanuary = 5_000.00m;
    private const decimal SevenPercentJanuary = 5_833.33m;

    [Fact]
    public async Task NoReader_ReadsTheLatestProjectionDefinition_WithoutValidity()
    {
        var id = Guid.NewGuid();
        var latest = Definition(id, 2, couponRate: 6m);
        var query = QueryReturning(id, latest);
        var adapter = new SecurityMasterAccountingEventSourceAdapter(query);

        var request = await adapter.BuildRequestAsync(Detail(id), new ReconciliationRunRequest("vt-run"));

        request.Should().NotBeNull();
        request!.PeriodStart.Should().Be(new DateOnly(2041, 1, 1));
        request.PeriodEnd.Should().Be(new DateOnly(2041, 2, 1));
        var security = request.Securities.Should().ContainSingle().Subject;
        security.TermsValidity.Should().BeNull();
        security.FixedIncomeTerms!.CouponRate.Should().Be(6m);
        var result = new SecurityMasterAccountingEventService().Generate(request);
        result.Issues.Select(static issue => issue.Code).Should().NotContain(SecurityMasterAccountingEventService.SecurityTermsChangedInPeriodCode);
        result.AccrualCalculations.Should().ContainSingle().Which.AccruedAmount.Should().Be(SixPercentJanuary);
        await query.Received(1).GetEconomicDefinitionByIdAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PeriodEndingBeforeALaterCouponChange_AccruesOnTheEarlierCoupon()
    {
        var id = Guid.NewGuid();
        var original = Definition(id, 1, couponRate: 5m);
        var amended = Definition(id, 2, couponRate: 6m);
        var changeFrom = new DateTimeOffset(2041, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var query = QueryReturning(id, amended);
        var reader = new FakeValidTimeReader(Segments((IssueInstant, original), (changeFrom, amended)));
        var adapter = new SecurityMasterAccountingEventSourceAdapter(query, validTime: reader);

        var request = await adapter.BuildRequestAsync(Detail(id), new ReconciliationRunRequest("vt-run"));

        var security = request!.Securities.Should().ContainSingle().Subject;
        security.FixedIncomeTerms!.CouponRate.Should().Be(5m, "January's terms are the 5% valid before the March change");
        security.TermsValidity.Should().Be(new SecurityAccountingTermsValidity(1, IssueInstant, changeFrom, null));
        var result = new SecurityMasterAccountingEventService().Generate(request);
        result.Issues.Select(static issue => issue.Code).Should().NotContain(SecurityMasterAccountingEventService.SecurityTermsChangedInPeriodCode);
        result.AccrualCalculations.Should().ContainSingle().Which.AccruedAmount.Should().Be(FivePercentJanuary);
        await query.DidNotReceive().GetEconomicDefinitionByIdAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeInsidePeriod_RaisesTheIssue_AndAccruesOnPeriodEndTerms()
    {
        var id = Guid.NewGuid();
        var original = Definition(id, 1, couponRate: 5m);
        var midJanuary = Definition(id, 2, couponRate: 6m);
        var later = Definition(id, 3, couponRate: 7m);
        var changeFrom = new DateTimeOffset(2041, 1, 16, 0, 0, 0, TimeSpan.Zero);
        var laterFrom = new DateTimeOffset(2041, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var query = QueryReturning(id, later);
        var reader = new FakeValidTimeReader(Segments((IssueInstant, original), (changeFrom, midJanuary), (laterFrom, later)));
        var adapter = new SecurityMasterAccountingEventSourceAdapter(query, validTime: reader);

        var request = await adapter.BuildRequestAsync(Detail(id), new ReconciliationRunRequest("vt-run"));

        var security = request!.Securities.Should().ContainSingle().Subject;
        security.FixedIncomeTerms!.CouponRate.Should().Be(6m, "the period-end segment differs from the latest, so its definition is used");
        security.TermsValidity.Should().Be(new SecurityAccountingTermsValidity(2, changeFrom, laterFrom, changeFrom));
        var result = new SecurityMasterAccountingEventService().Generate(request);
        var issue = result.Issues.Should().ContainSingle(static issue => issue.Code == SecurityMasterAccountingEventService.SecurityTermsChangedInPeriodCode).Subject;
        issue.Severity.Should().Be(ReconciliationBreakSeverity.Medium);
        issue.Reason.Should().Contain("2041-01-16").And.Contain("version 2");
        result.AccrualCalculations.Should().ContainSingle().Which.AccruedAmount.Should().Be(SixPercentJanuary,
            "the single-terms accrual uses the period-end terms for the whole period");
        await query.DidNotReceive().GetEconomicDefinitionByIdAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LatestTermsEqualPeriodEndTerms_UsesTheProjectionDefinition_AndAttachesValidity()
    {
        var id = Guid.NewGuid();
        var original = Definition(id, 1, couponRate: 5m);
        var amended = Definition(id, 2, couponRate: 6m);
        var changeFrom = new DateTimeOffset(2041, 1, 16, 0, 0, 0, TimeSpan.Zero);
        // The projection row carries the same economics as the period-end segment but a ticker the
        // segment does not, so reading the projection is visible in the resolved symbol.
        var projectionRow = amended with
        {
            Identifiers = [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "PROJ", true, IssueInstant.AddYears(-1))]
        };
        var query = QueryReturning(id, projectionRow);
        var reader = new FakeValidTimeReader(Segments((IssueInstant, original), (changeFrom, amended)));
        var adapter = new SecurityMasterAccountingEventSourceAdapter(query, validTime: reader);

        var request = await adapter.BuildRequestAsync(Detail(id), new ReconciliationRunRequest("vt-run"));

        var security = request!.Securities.Should().ContainSingle().Subject;
        security.Symbol.Should().Be("PROJ", "the projection row is read exactly as before");
        security.FixedIncomeTerms!.CouponRate.Should().Be(6m);
        security.TermsValidity.Should().Be(new SecurityAccountingTermsValidity(2, changeFrom, null, changeFrom));
        await query.Received(1).GetEconomicDefinitionByIdAsync(id, Arg.Any<CancellationToken>());
        var result = new SecurityMasterAccountingEventService().Generate(request);
        result.Issues.Should().ContainSingle(static issue => issue.Code == SecurityMasterAccountingEventService.SecurityTermsChangedInPeriodCode);
        result.AccrualCalculations.Should().ContainSingle().Which.AccruedAmount.Should().Be(SixPercentJanuary);
    }

    [Fact]
    public async Task IdentifierOnlyChangeInsidePeriod_IsNotAnEconomicChange()
    {
        var id = Guid.NewGuid();
        var original = Definition(id, 1, couponRate: 5m);
        var relabelled = Definition(id, 2, couponRate: 5m) with
        {
            Identifiers = [new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "987654321", true, IssueInstant.AddYears(-1))]
        };
        var changeFrom = new DateTimeOffset(2041, 1, 16, 0, 0, 0, TimeSpan.Zero);
        var query = QueryReturning(id, relabelled);
        var reader = new FakeValidTimeReader(Segments((IssueInstant, original), (changeFrom, relabelled)));
        var adapter = new SecurityMasterAccountingEventSourceAdapter(query, validTime: reader);

        var request = await adapter.BuildRequestAsync(Detail(id), new ReconciliationRunRequest("vt-run"));

        var security = request!.Securities.Should().ContainSingle().Subject;
        security.TermsValidity!.ChangedWithinPeriodAt.Should().BeNull();
        var result = new SecurityMasterAccountingEventService().Generate(request);
        result.Issues.Select(static issue => issue.Code).Should().NotContain(SecurityMasterAccountingEventService.SecurityTermsChangedInPeriodCode);
        result.AccrualCalculations.Should().ContainSingle().Which.AccruedAmount.Should().Be(FivePercentJanuary);
    }

    [Fact]
    public async Task ChangeValidFromTheExclusivePeriodEnd_DoesNotGovernThePeriod()
    {
        // PeriodEnd is exclusive (ResolvePeriod supplies the next month's first day, and the accrual
        // and coupon generators treat it as exclusive). Terms valid from 2041-02-01 never applied to a
        // January day, so January should accrue on the 5% and raise no in-period change.
        var id = Guid.NewGuid();
        var original = Definition(id, 1, couponRate: 5m);
        var february = Definition(id, 2, couponRate: 6m);
        var later = Definition(id, 3, couponRate: 7m);
        var changeFrom = new DateTimeOffset(2041, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var laterFrom = new DateTimeOffset(2041, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var query = QueryReturning(id, later);
        var reader = new FakeValidTimeReader(Segments((IssueInstant, original), (changeFrom, february), (laterFrom, later)));
        var adapter = new SecurityMasterAccountingEventSourceAdapter(query, validTime: reader);

        var request = await adapter.BuildRequestAsync(Detail(id), new ReconciliationRunRequest("vt-run"));

        var security = request!.Securities.Should().ContainSingle().Subject;
        security.TermsValidity.Should().Be(new SecurityAccountingTermsValidity(1, IssueInstant, changeFrom, null));
        security.FixedIncomeTerms!.CouponRate.Should().Be(5m);
        var result = new SecurityMasterAccountingEventService().Generate(request);
        result.Issues.Select(static issue => issue.Code).Should().NotContain(SecurityMasterAccountingEventService.SecurityTermsChangedInPeriodCode);
        result.AccrualCalculations.Should().ContainSingle().Which.AccruedAmount.Should().Be(FivePercentJanuary);
    }

    [Fact]
    public async Task NoTermsValidAtPeriodEnd_FallsBackToTheProjectionWithoutValidity()
    {
        var id = Guid.NewGuid();
        var future = Definition(id, 1, couponRate: 7m);
        var query = QueryReturning(id, future);
        var reader = new FakeValidTimeReader(Segments((new DateTimeOffset(2041, 6, 1, 0, 0, 0, TimeSpan.Zero), future)));
        var adapter = new SecurityMasterAccountingEventSourceAdapter(query, validTime: reader);

        var request = await adapter.BuildRequestAsync(Detail(id), new ReconciliationRunRequest("vt-run"));

        var security = request!.Securities.Should().ContainSingle().Subject;
        security.TermsValidity.Should().BeNull();
        security.FixedIncomeTerms!.CouponRate.Should().Be(7m);
        await query.Received(1).GetEconomicDefinitionByIdAsync(id, Arg.Any<CancellationToken>());
        new SecurityMasterAccountingEventService().Generate(request)
            .AccrualCalculations.Should().ContainSingle().Which.AccruedAmount.Should().Be(SevenPercentJanuary);
    }

    private static ISecurityMasterQueryService QueryReturning(Guid id, SecurityEconomicDefinitionRecord definition)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetEconomicDefinitionByIdAsync(id, Arg.Any<CancellationToken>()).Returns(definition);
        return query;
    }

    private static StrategyRunDetail Detail(Guid id)
    {
        var summary = new StrategyRunSummary("vt-run", "synthetic", "Synthetic", default, default, default,
            IssueInstant, RunAsOf, null, null, "synthetic", null, null, null, null, 0, RunAsOf);
        var reference = new WorkstationSecurityReference(id, "Synthetic CD", "CertificateOfDeposit", "USD", SecurityStatusDto.Active, "SYNTH");
        var portfolio = new PortfolioSummary("synthetic", "vt-run", RunAsOf, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            [new PortfolioPositionSummary("SYNTH", (long)Par, 100m, 0, 0, false, reference)]);
        return new StrategyRunDetail(summary, new Dictionary<string, string>(), portfolio, null);
    }

    // An economic-only certificate of deposit (the shape the rate-unit suite pins): 1,000,000 par,
    // fixed Percent coupon, quarterly on 30/360, issued 2041-01-01 and maturing 2042-01-01.
    private static SecurityEconomicDefinitionRecord Definition(Guid id, long version, decimal couponRate)
    {
        var economic = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 2,
            termsFormat = "economic",
            maturity = new { issueDate = "2041-01-01", maturityDate = "2042-01-01" },
            coupon = new { couponType = "Fixed", couponRate, paymentFrequency = "Quarterly", dayCount = "30/360" },
            rateConvention = new { rateUnit = "Percent" }
        });
        return new SecurityEconomicDefinitionRecord(id, "CertificateOfDeposit", null, "CertificateOfDeposit", "CertificateOfDeposit", null, null,
            SecurityStatusDto.Active, "Synthetic CD", "USD", Json("{}"), Json("""{"accountingClassification":"HeldToMaturity"}"""), economic, Json("{}"),
            version, IssueInstant, null, [], "CertificateOfDeposit", null);
    }

    private static SecurityTermsValidTimeSegment[] Segments(
        params (DateTimeOffset ValidFrom, SecurityEconomicDefinitionRecord Definition)[] events)
    {
        var ordered = events.OrderBy(static item => item.ValidFrom).ToArray();
        return ordered
            .Select((item, index) => new SecurityTermsValidTimeSegment(
                item.Definition.SecurityId,
                item.Definition.Version,
                item.ValidFrom,
                item.ValidFrom,
                index + 1 < ordered.Length ? ordered[index + 1].ValidFrom : null,
                item.Definition))
            .ToArray();
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private sealed class FakeValidTimeReader(IReadOnlyList<SecurityTermsValidTimeSegment> timeline) : ISecurityTermsValidTimeReader
    {
        public Task<SecurityTermsValidTimeSegment?> GetValidAtAsync(
            Guid securityId, DateTimeOffset validAt, DateTimeOffset? knownAt = null, CancellationToken ct = default)
            => Task.FromResult(timeline.LastOrDefault(segment => segment.SecurityId == securityId && segment.ValidFrom <= validAt));

        public Task<IReadOnlyList<SecurityTermsValidTimeSegment>> GetTimelineAsync(
            Guid securityId, DateTimeOffset? knownAt = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityTermsValidTimeSegment>>(
                timeline.Where(segment => segment.SecurityId == securityId).ToArray());
    }
}
