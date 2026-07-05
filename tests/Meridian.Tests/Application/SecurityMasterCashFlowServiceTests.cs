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
