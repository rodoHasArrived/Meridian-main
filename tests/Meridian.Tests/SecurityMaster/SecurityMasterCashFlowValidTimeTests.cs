using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecurityMasterQueryContract = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// ADR-034 consumer migration for calculated cash flows: each schedule row takes the terms valid at
/// its accrual start, consecutive economically-equal periods merge, and without a reader (or with a
/// single economic period) the projection is exactly the latest-terms projection.
/// </summary>
public sealed class SecurityMasterCashFlowValidTimeTests
{
    // The golden bond: par 1,000,000, annual 30/360, issued 2026-01-01. Four years gives four rows
    // whose accrual starts are 2026-01-01, 2027-01-01, 2028-01-01 and 2029-01-01.
    private static readonly DateOnly Maturity = new(2030, 1, 1);
    private static readonly DateTimeOffset IssueInstant = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AsOf = IssueInstant;

    [Fact]
    public async Task NoReader_ProjectsFromTheLatestTerms_WithoutTermsPeriods()
    {
        var id = Guid.NewGuid();
        var latest = Definition(id, 2, couponRate: 6m);

        var projection = await Project(id, latest, reader: null);

        projection!.BlockedReason.Should().BeNull();
        projection.TermsPeriods.Should().BeNull();
        projection.Schedule.Should().HaveCount(4);
        projection.Schedule.Select(static row => row.InterestAmount).Should().AllBeEquivalentTo(60_000m,
            "without a reader every row uses the latest recorded 6% coupon");
    }

    [Fact]
    public async Task SingleEconomicPeriod_IsIdenticalToTheNoReaderProjection()
    {
        var id = Guid.NewGuid();
        var only = Definition(id, 1, couponRate: 5m);
        var reader = new FakeValidTimeReader(Segments((IssueInstant, only)));

        var baseline = await Project(id, only, reader: null);
        var withReader = await Project(id, only, reader);

        withReader!.TermsPeriods.Should().BeNull();
        withReader.BlockedReason.Should().Be(baseline!.BlockedReason);
        withReader.Schedule.Should().BeEquivalentTo(baseline.Schedule, options => options.WithStrictOrdering());
        withReader.Schedule.Select(static row => row.InterestAmount).Should().AllBeEquivalentTo(50_000m);
        reader.TimelineReads.Should().Be(1);
    }

    [Fact]
    public async Task CouponChangeMidLife_RowsBeforeTheChangeKeepTheOldCoupon_RowsFromItTakeTheNew()
    {
        var id = Guid.NewGuid();
        var original = Definition(id, 1, couponRate: 5m);
        var amended = Definition(id, 2, couponRate: 6m);
        var changeFrom = new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var reader = new FakeValidTimeReader(Segments((IssueInstant, original), (changeFrom, amended)));

        var projection = await Project(id, amended, reader);

        projection!.BlockedReason.Should().BeNull();
        projection.Schedule.Select(static row => DateOnly.FromDateTime(row.PeriodDate.UtcDateTime)).Should().Equal(
            new DateOnly(2027, 1, 1), new DateOnly(2028, 1, 1), new DateOnly(2029, 1, 1), new DateOnly(2030, 1, 1));
        projection.Schedule.Select(static row => row.InterestAmount).Should().Equal(50_000m, 50_000m, 60_000m, 60_000m);
        projection.Schedule.Sum(static row => row.PrincipalAmount).Should().Be(1_000_000m, "principal is repaid once, at maturity");
        projection.TermsPeriods.Should().Equal(
            new StructuredCashFlowTermsPeriod(1, IssueInstant, changeFrom),
            new StructuredCashFlowTermsPeriod(2, changeFrom, null));
    }

    [Fact]
    public async Task CouponChangeInsideAnAccrualPeriod_AppliesFromTheNextAccrualStart()
    {
        var id = Guid.NewGuid();
        var original = Definition(id, 1, couponRate: 5m);
        var amended = Definition(id, 2, couponRate: 6m);
        // Valid from mid-way through the period that starts 2028-01-01: that row still accrues on the
        // terms valid at its accrual start, so the change first applies to the 2029-01-01 accrual.
        var changeFrom = new DateTimeOffset(2028, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var reader = new FakeValidTimeReader(Segments((IssueInstant, original), (changeFrom, amended)));

        var projection = await Project(id, amended, reader);

        projection!.BlockedReason.Should().BeNull();
        projection.Schedule.Select(static row => row.InterestAmount).Should().Equal(50_000m, 50_000m, 50_000m, 60_000m);
        projection.TermsPeriods.Should().HaveCount(2);
    }

    [Fact]
    public async Task RowsBeforeTheFirstValidFrom_UseTheFirstPeriod()
    {
        var id = Guid.NewGuid();
        var original = Definition(id, 1, couponRate: 5m);
        var amended = Definition(id, 2, couponRate: 6m);
        // The security was set up after issue: its first recorded valid-from is 2027-06-01.
        var firstValidFrom = new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var changeFrom = new DateTimeOffset(2029, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var reader = new FakeValidTimeReader(Segments((firstValidFrom, original), (changeFrom, amended)));

        var projection = await Project(id, amended, reader);

        projection!.BlockedReason.Should().BeNull();
        projection.Schedule.Should().HaveCount(4, "no row is dropped for predating the first valid-from");
        projection.Schedule.Select(static row => row.InterestAmount).Should().Equal(50_000m, 50_000m, 50_000m, 60_000m);
    }

    [Fact]
    public async Task IdentifierOnlyAmendment_IsOneEconomicPeriod()
    {
        var id = Guid.NewGuid();
        var original = Definition(id, 1, couponRate: 5m);
        var relabelled = Definition(id, 2, couponRate: 5m, cusip: "987654321");
        SecurityTermsValidTime.EconomicallyEqual(original, relabelled).Should().BeTrue("the fixture differs only in identifiers");
        var reader = new FakeValidTimeReader(Segments(
            (IssueInstant, original),
            (new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero), relabelled)));

        var baseline = await Project(id, relabelled, reader: null);
        var projection = await Project(id, relabelled, reader);

        projection!.TermsPeriods.Should().BeNull();
        projection.BlockedReason.Should().Be(baseline!.BlockedReason);
        projection.Schedule.Should().BeEquivalentTo(baseline.Schedule, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task AnUnprojectablePeriod_BlocksTheComposedProjection()
    {
        var id = Guid.NewGuid();
        // The earlier terms declare no coupon unit, so they cannot be projected on their own.
        var undeclared = Definition(id, 1, couponRate: 5m, declareUnit: false);
        var amended = Definition(id, 2, couponRate: 6m);
        var changeFrom = new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var reader = new FakeValidTimeReader(Segments((IssueInstant, undeclared), (changeFrom, amended)));

        var alone = await Project(id, undeclared, reader: null);
        alone!.BlockedReason.Should().NotBeNull("precondition: the undeclared-unit terms block on their own");

        var projection = await Project(id, amended, reader);

        projection!.Schedule.Should().BeEmpty();
        projection.BlockedReason.Should().StartWith("The terms valid from 2026-01-01 (version 1) cannot be projected: ");
        projection.TermsPeriods.Should().BeNull();
    }

    [Fact]
    public void MergeEconomicallyEqualPeriods_JoinsConsecutiveEqualPeriods_KeepingEarlierValidFromAndLaterState()
    {
        var id = Guid.NewGuid();
        var v1 = Definition(id, 1, couponRate: 5m);
        var v2 = Definition(id, 2, couponRate: 5m, cusip: "987654321");
        var v3 = Definition(id, 3, couponRate: 6m);
        var v4 = Definition(id, 4, couponRate: 5m);
        var d1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var d2 = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var d3 = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var d4 = new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // Supplied out of order: the merge orders by valid-from first.
        var timeline = Segments((d1, v1), (d2, v2), (d3, v3), (d4, v4)).Reverse().ToArray();

        var merged = SecurityMasterCashFlowService.MergeEconomicallyEqualPeriods(timeline);

        merged.Select(static segment => (segment.StreamVersion, segment.ValidFrom, segment.ValidTo)).Should().Equal(
            (2L, d1, (DateTimeOffset?)d3),
            (3L, d3, (DateTimeOffset?)d4),
            (4L, d4, (DateTimeOffset?)null));
        merged[0].Definition.Should().BeSameAs(v2, "the joined period keeps the later state");
        merged[2].Definition.Should().BeSameAs(v4, "equal economics that are not consecutive stay separate");
    }

    [Fact]
    public void MergeEconomicallyEqualPeriods_EmptyAndSingle_AreUnchanged()
    {
        SecurityMasterCashFlowService.MergeEconomicallyEqualPeriods([]).Should().BeEmpty();

        var single = Segments((IssueInstant, Definition(Guid.NewGuid(), 1, couponRate: 5m)));
        SecurityMasterCashFlowService.MergeEconomicallyEqualPeriods(single).Should().Equal(single);
    }

    [Fact]
    public async Task LegSetChangesBetweenTermPeriods_BlocksWithCompositionUnsupported()
    {
        // A swap restructured from one leg pair to another: the rows of different legs cannot be joined
        // into one schedule, so the composition refuses rather than mixing them.
        var id = Guid.NewGuid();
        var before = Definition(id, version: 1, couponRate: 5m);
        var after = Definition(id, version: 2, couponRate: 6m);
        var reader = new FakeValidTimeReader(Segments((IssueInstant, before), (new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero), after)));
        var service = Service(reader);
        var latest = SecurityMasterValidTimeQuery.ToDetail(after);

        var result = await service.ComposeValidTimeProjectionAsync(
            latest,
            LegProjection(id, "fixed-pay", "float-receive"),
            AsOf,
            detail => detail.Version == 1
                ? LegProjection(id, "fixed-pay", "float-receive")
                : LegProjection(id, "fixed-pay", "float-receive-sofr"),
            CancellationToken.None);

        result.Schedule.Should().BeEmpty();
        result.BlockedReason.Should().StartWith(SecurityMasterCashFlowService.ValidTimeCompositionUnsupportedCode)
            .And.Contain("legs differ between term periods");
        result.TermsPeriods.Should().BeNull();
    }

    [Fact]
    public async Task SameLegSetAcrossTermPeriods_ComposesEachLegByPeriod()
    {
        var id = Guid.NewGuid();
        var before = Definition(id, version: 1, couponRate: 5m);
        var after = Definition(id, version: 2, couponRate: 6m);
        var change = new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var reader = new FakeValidTimeReader(Segments((IssueInstant, before), (change, after)));
        var service = Service(reader);

        var result = await service.ComposeValidTimeProjectionAsync(
            SecurityMasterValidTimeQuery.ToDetail(after),
            LegProjection(id, ["fixed-pay", "float-receive"], interest: 6m),
            AsOf,
            detail => LegProjection(id, ["float-receive", "fixed-pay"], interest: detail.Version == 1 ? 5m : 6m),
            CancellationToken.None);

        result.BlockedReason.Should().BeNull();
        result.TermsPeriods.Should().HaveCount(2);
        result.LegSchedules.Should().HaveCount(2);
        foreach (var leg in result.LegSchedules!)
        {
            leg.Schedule.Select(static row => row.InterestAmount).Should().Equal(5m, 5m, 6m, 6m);
        }
    }

    private static SecurityMasterCashFlowService Service(ISecurityTermsValidTimeReader reader)
        => new(
            null,
            Substitute.For<ISecurityMasterCashFlowStore>(),
            Array.Empty<IStructuredCashFlowProvider>(),
            Substitute.For<SecurityMasterQueryContract>(),
            NullLogger<SecurityMasterCashFlowService>.Instance,
            validTime: reader);

    private static StructuredCashFlowProjectionDto LegProjection(Guid id, params string[] legIds)
        => LegProjection(id, legIds, interest: 5m);

    /// <summary>Annual rows on 2027-01-01 .. 2030-01-01, accruing from the prior anniversary.</summary>
    private static StructuredCashFlowProjectionDto LegProjection(Guid id, string[] legIds, decimal interest)
    {
        var rows = Enumerable.Range(2027, 4)
            .Select(year => new StructuredCashFlowScheduleEntry(
                new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero), 0m, interest, 1m)
            {
                AccrualStart = new DateOnly(year - 1, 1, 1),
                AccrualEnd = new DateOnly(year, 1, 1)
            })
            .ToArray();
        return new StructuredCashFlowProjectionDto(
            id,
            StructuredCashFlowSourceKind.CalculatedBullet,
            StructuredCashFlowScenario.Base,
            AsOf,
            rows,
            LegSchedules: legIds
                .Select(legId => new StructuredCashFlowLegSchedule(legId, CashFlowLegRateKind.Fixed, null, rows))
                .ToArray());
    }

    private static async Task<StructuredCashFlowProjectionDto?> Project(
        Guid id,
        SecurityEconomicDefinitionRecord latest,
        ISecurityTermsValidTimeReader? reader)
    {
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(id, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(id, StructuredCashFlowSourceKind.CalculatedBullet, DateTimeOffset.UtcNow, false, null, null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        // The projection row is the latest definition read the way the valid-time composition reads
        // every other period, so the only difference between periods is their terms.
        query.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(SecurityMasterValidTimeQuery.ToDetail(latest));
        var service = new SecurityMasterCashFlowService(
            null,
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance,
            validTime: reader);

        return await service.GetProjectionAsOfAsync(id, StructuredCashFlowScenario.Base, AsOf);
    }

    private static SecurityEconomicDefinitionRecord Definition(
        Guid id,
        long version,
        decimal couponRate,
        string cusip = "123456789",
        bool declareUnit = true)
    {
        object terms = declareUnit
            ? new
            {
                issueDate = DateOnly.FromDateTime(IssueInstant.UtcDateTime),
                maturity = Maturity,
                par = 1_000_000m,
                couponRate,
                paymentFrequency = "Annual",
                dayCountConvention = "30/360",
                assetClassTermsVersion = 2,
                scheduleConventions = new { accrualAdjusted = false, couponRateUnit = "Percent" }
            }
            : new
            {
                issueDate = DateOnly.FromDateTime(IssueInstant.UtcDateTime),
                maturity = Maturity,
                par = 1_000_000m,
                couponRate,
                paymentFrequency = "Annual",
                dayCountConvention = "30/360",
                assetClassTermsVersion = 2
            };

        return new SecurityEconomicDefinitionRecord(
            id,
            "FixedIncome",
            null,
            "Bond",
            "Bond",
            null,
            null,
            SecurityStatusDto.Active,
            "Valid-time bond",
            "USD",
            Json("{}"),
            JsonSerializer.SerializeToElement(new { displayName = "Valid-time bond", currency = "USD" }),
            Json("{}"),
            Json("{}"),
            version,
            IssueInstant.AddYears(-1),
            null,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, cusip, true, IssueInstant.AddYears(-1))],
            "Bond",
            JsonSerializer.SerializeToElement(terms));
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
        public int TimelineReads { get; private set; }

        public Task<SecurityTermsValidTimeSegment?> GetValidAtAsync(
            Guid securityId, DateTimeOffset validAt, DateTimeOffset? knownAt = null, CancellationToken ct = default)
            => Task.FromResult(timeline.LastOrDefault(segment => segment.SecurityId == securityId && segment.ValidFrom <= validAt));

        public Task<IReadOnlyList<SecurityTermsValidTimeSegment>> GetTimelineAsync(
            Guid securityId, DateTimeOffset? knownAt = null, CancellationToken ct = default)
        {
            TimelineReads++;
            return Task.FromResult<IReadOnlyList<SecurityTermsValidTimeSegment>>(
                timeline.Where(segment => segment.SecurityId == securityId).ToArray());
        }
    }
}
