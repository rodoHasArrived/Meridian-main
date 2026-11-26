using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Moq;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityRateRepairDiscoveryTests
{
    [Fact]
    public async Task MoreThanFiveHundredRecords_HaveAnExplicitContinuationAndNoSilentLoss()
    {
        var records = Enumerable.Range(0, 501).Select(index => Record(index)).ToArray();
        var query = new Mock<ISecurityMasterQueryService>();
        query.Setup(service => service.SearchAsync(It.IsAny<SecuritySearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SecuritySearchRequest request, CancellationToken _) => records.Skip(request.Skip).Take(request.Take)
                .Select(record => new SecuritySummaryDto(record.SecurityId, record.AssetClass, record.Status,
                    record.DisplayName, record.PrimaryIdentifierValue, record.Currency, record.Version)).ToArray());
        var canonical = new Mock<ISecurityMasterStore>();
        canonical.Setup(store => store.GetProjectionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => records.SingleOrDefault(record => record.SecurityId == id));
        var service = Service(query.Object, canonical.Object);
        var actor = new SecurityRateRepairActor("tenant", "company", "operator", true);

        var first = await service.DiscoverPageAsync(actor, 0, 500);
        var second = await service.DiscoverPageAsync(actor, first.NextSkip!.Value, 500);

        first.IsComplete.Should().BeFalse();
        first.NextSkip.Should().Be(500);
        first.ScannedCount.Should().Be(500);
        second.ScannedCount.Should().Be(1);
        second.NextSkip.Should().BeNull();
        second.IsComplete.Should().BeTrue();
        first.Candidates.Concat(second.Candidates).Select(static row => row.SecurityId)
            .Should().HaveCount(501).And.OnlyHaveUniqueItems();
        query.Verify(query => query.SearchAsync(It.Is<SecuritySearchRequest>(request => request.Skip == 500 && request.Take == 500),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MissingProjection_IsAnExplicitUnavailableRowAndIncompleteScan()
    {
        var summary = new SecuritySummaryDto(Guid.NewGuid(), "CertificateOfDeposit", SecurityStatusDto.Active,
            "Unavailable CD", "SYNTHETIC", "USD", 7);
        var query = new Mock<ISecurityMasterQueryService>();
        query.Setup(query => query.SearchAsync(It.IsAny<SecuritySearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([summary]);
        var result = await Service(query.Object, Mock.Of<ISecurityMasterStore>())
            .DiscoverPageAsync(new("tenant", "company", "operator", true), 0, 50);
        result.IsComplete.Should().BeFalse();
        result.NextSkip.Should().BeNull();
        result.Issues.Should().ContainSingle().Which.Code.Should().Be("ProjectionUnavailable");
        result.Candidates.Should().ContainSingle().Which.Eligible.Should().BeFalse();
    }

    [Fact]
    public async Task BondWithoutScalarUnit_IsExcludedWithItsFullScheduleMigrationReason()
    {
        var record = Record(0) with { AssetClass = SecurityAssetClassNames.Bond };
        var query = new Mock<ISecurityMasterQueryService>();
        query.Setup(query => query.SearchAsync(It.IsAny<SecuritySearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new(record.SecurityId, record.AssetClass, record.Status, record.DisplayName,
                record.PrimaryIdentifierValue, record.Currency, record.Version)]);
        var canonical = new Mock<ISecurityMasterStore>();
        canonical.Setup(store => store.GetProjectionAsync(record.SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var result = await Service(query.Object, canonical.Object).DiscoverAsync(new("tenant", "company", "operator", true));
        result.Candidates.Single().Eligible.Should().BeFalse();
        result.Candidates.Single().BlockerCode.Should().Be("FullScheduleMigrationRequired");
        result.Candidates.Single().FixPath.Should().Be($"/accounting/security-master?securityId={record.SecurityId:D}&tab=evidence&bondAction=migration");
    }

    private static SecurityProjectionRecord Record(int index)
        => new(Guid.NewGuid(), "CertificateOfDeposit", SecurityStatusDto.Active, $"Synthetic CD {index:D4}", "USD",
            "InternalCode", $"CD-{index}", JsonSerializer.SerializeToElement(new { issuerName = "Synthetic issuer" }),
            JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                termsFormat = SecurityTermsFormats.Flat,
                assetClassTermsVersion = SecurityAssetClassCatalog.GetTermsVersion("CertificateOfDeposit"),
                couponRate = 5.25m
            }),
            JsonSerializer.SerializeToElement(new { sourceSystem = "synthetic-vendor", sourceSchemaVersion = "v1" }),
            7, DateTimeOffset.UtcNow, null, [], []);

    private static SecurityRateRepairService Service(ISecurityMasterQueryService query, ISecurityMasterStore canonical)
        => new(Mock.Of<ISecurityRateRepairStore>(), query, canonical, Mock.Of<ISecurityMasterRevisionStore>(),
            Mock.Of<ISecurityMasterWorkbenchCommandService>(), Mock.Of<IOperationsContinuityWorkflowService>(),
            Mock.Of<ISecurityRateRepairEvidenceResolver>(), Mock.Of<ISecurityMasterImpactPreviewService>(), Mock.Of<ILedgerJournalStore>());
}
