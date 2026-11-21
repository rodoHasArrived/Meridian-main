using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using NSubstitute;
using SecurityMasterQueryContract = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Application;

public sealed partial class SecurityMasterCashFlowServiceTests
{
    [Theory]
    [InlineData("Fraction", 0.005)]
    [InlineData("Percent", 0.5)]
    [InlineData("BasisPoints", 50)]
    public async Task GovernedRecordUnit_RepairsSubOneCdRateToIdenticalMonetaryCash(string unit, double rate)
    {
        var securityId = Guid.NewGuid();
        var issue = WholePeriodIssueDate();
        var terms = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            termsFormat = "flat",
            assetClassTermsVersion = 5,
            rateUnit = unit,
            issueDate = issue,
            maturityDate = issue.AddYears(1),
            par = 100m,
            couponRate = (decimal)rate,
            paymentFrequency = "SemiAnnual",
            dayCountConvention = "30/360"
        });
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(securityId, terms) with { AssetClass = "CertificateOfDeposit" });
        var service = BuildService(StoreWith(securityId, StructuredCashFlowSourceKind.CalculatedBullet), query);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection!.BlockedReason.Should().BeNull();
        projection.Schedule.Sum(row => row.InterestAmount).Should().Be(0.5m);
        projection.Schedule.Sum(row => row.PrincipalAmount).Should().Be(100m);
    }
}
