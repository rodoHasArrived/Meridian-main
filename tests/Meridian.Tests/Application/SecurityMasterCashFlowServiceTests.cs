using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecurityMasterQueryContract = Meridian.Application.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Application;

public sealed class SecurityMasterCashFlowServiceTests
{
    [Fact]
    public async Task GetProjectionAsync_CalculatedBullet_ShouldGenerateCouponAndMaturityScheduleFromRetainedTerms()
    {
        var securityId = Guid.Parse("11111111-aaaa-aaaa-aaaa-111111111111");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow,
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate = asOf,
                    maturityDate = asOf.AddYears(1),
                    par = 100m,
                    couponRate = 6m,
                    paymentFrequency = "SemiAnnual",
                    dayCountConvention = "30/360"
                })));
        var service = new SecurityMasterCashFlowService(
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.SourceKind.Should().Be(StructuredCashFlowSourceKind.CalculatedBullet);
        projection.Schedule.Should().HaveCount(2);
        projection.Schedule.Should().Contain(static row => row.InterestAmount == 3m && row.PrincipalAmount == 0m);
        projection.Schedule.Last().Should().Match<StructuredCashFlowScheduleEntry>(row =>
            row.InterestAmount == 3m &&
            row.PrincipalAmount == 100m &&
            row.Factor == 0m);
    }

    [Fact]
    public async Task GetProjectionAsync_CalculatedSinker_ShouldAmortizePrincipalAcrossScheduleDates()
    {
        var securityId = Guid.Parse("22222222-bbbb-bbbb-bbbb-222222222222");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedSinker,
                DateTimeOffset.UtcNow,
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate = asOf,
                    maturityDate = asOf.AddYears(1),
                    originalFace = 120m,
                    couponRate = 0.12m,
                    paymentFrequency = "Quarterly",
                    dayCountConvention = "ACT/360"
                })));
        var service = new SecurityMasterCashFlowService(
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Down100);

        projection.Should().NotBeNull();
        projection!.SourceKind.Should().Be(StructuredCashFlowSourceKind.CalculatedSinker);
        projection.Schedule.Should().HaveCount(4);
        projection.Schedule.Select(static row => row.PrincipalAmount).Should().OnlyContain(static amount => amount > 0m);
        projection.Schedule.Sum(static row => row.PrincipalAmount).Should().Be(120m);
        projection.Schedule.Last().Factor.Should().Be(0m);
        projection.Schedule.First().InterestAmount.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task GetProjectionAsync_StaleSource_ShouldFlagStalenessButStillReturnForDisplay()
    {
        var securityId = Guid.Parse("33333333-cccc-cccc-cccc-333333333333");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow.AddDays(-30),
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate = asOf,
                    maturityDate = asOf.AddYears(1),
                    par = 100m,
                    couponRate = 6m,
                    paymentFrequency = "SemiAnnual",
                    dayCountConvention = "30/360"
                })));
        var service = BuildService(store, query);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.Staleness.Should().Be(StructuredCashFlowStaleness.Stale);
        projection.SourceLastUpdatedUtc.Should().NotBeNull();
        // Stale projections are still returned so the UI can render a flagged view.
        projection.Schedule.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetProjectionAsync_WithTypedFactorSchedule_ShouldExposeScheduleAndSeedOutstanding()
    {
        var securityId = Guid.Parse("44444444-dddd-dddd-dddd-444444444444");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedSinker,
                DateTimeOffset.UtcNow,
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate = asOf,
                    maturityDate = asOf.AddYears(1),
                    originalFace = 120m,
                    couponRate = 0.12m,
                    paymentFrequency = "Quarterly",
                    dayCountConvention = "ACT/360",
                    factorSchedule = new[]
                    {
                        new { asOfDate = new DateOnly(2020, 1, 1), factor = 0.5m }
                    }
                })));
        var service = BuildService(store, query);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.FactorSchedule.Should().NotBeNull();
        projection.FactorSchedule!.Should().ContainSingle(entry => entry.Factor == 0.5m);
        // Outstanding seeded from the 0.5 factor: 120 * 0.5 = 60 amortized across the sinker schedule.
        projection.Schedule.Sum(static row => row.PrincipalAmount).Should().Be(60m);
    }

    [Fact]
    public async Task BuildLedgerPostingsAsync_FreshBaseProjection_ShouldProduceBalancedCouponAccruals()
    {
        var securityId = Guid.Parse("55555555-eeee-eeee-eeee-555555555555");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow,
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate = asOf,
                    maturityDate = asOf.AddYears(1),
                    par = 100m,
                    couponRate = 6m,
                    paymentFrequency = "SemiAnnual",
                    dayCountConvention = "30/360"
                })));
        var service = BuildService(store, query);

        var result = await service.BuildLedgerPostingsAsync(securityId);

        result.IsPostable.Should().BeTrue();
        result.BlockedReason.Should().BeNull();
        result.Postings.Should().HaveCount(2);
        result.Postings.Should().OnlyContain(posting =>
            posting.IsBalanced && posting.TotalDebits == 3m && posting.TotalCredits == 3m);
        result.Postings[0].Lines.Should().Contain(line =>
            line.Account == "Accrued Interest Receivable" && line.Debit == 3m && line.Credit == 0m);
        result.Postings[0].Lines.Should().Contain(line =>
            line.Account == "Coupon Income" && line.Credit == 3m && line.Debit == 0m);
    }

    [Fact]
    public async Task BuildLedgerPostingsAsync_StaleSource_ShouldBlockPostingAsAGate()
    {
        var securityId = Guid.Parse("66666666-ffff-ffff-ffff-666666666666");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow.AddDays(-30),
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate = asOf,
                    maturityDate = asOf.AddYears(1),
                    par = 100m,
                    couponRate = 6m,
                    paymentFrequency = "SemiAnnual",
                    dayCountConvention = "30/360"
                })));
        var service = BuildService(store, query);

        var result = await service.BuildLedgerPostingsAsync(securityId);

        result.IsPostable.Should().BeFalse();
        result.BlockedReason.Should().Contain("stale");
        result.Postings.Should().BeEmpty();
    }

    private static SecurityMasterCashFlowService BuildService(
        ISecurityMasterCashFlowStore store,
        SecurityMasterQueryContract query)
        => new(
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance);

    private static SecurityDetailDto BuildSecurity(Guid securityId, JsonElement assetSpecificTerms)
        => new(
            securityId,
            "Bond",
            SecurityStatusDto.Active,
            "Calculated bond",
            "USD",
            JsonSerializer.SerializeToElement(new { displayName = "Calculated bond", currency = "USD" }),
            assetSpecificTerms,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "123456789", true, DateTimeOffset.UtcNow)],
            [],
            1,
            DateTimeOffset.UtcNow,
            null);
}
