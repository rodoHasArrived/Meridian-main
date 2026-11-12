using System.Text.Json;
using Meridian.Application.Integrations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrations;
using Meridian.PortfolioRecords.FundAccounts;
using Moq;

namespace Meridian.Tests.Application.Integrations;

public sealed class BondHoldingImportServiceTests
{
    [Fact]
    public async Task ReviewedPopulationPublishesObservedHoldingsAndReplaysIdempotently()
    {
        var fixture = await Fixture();
        var first = await fixture.Service.AcceptAsync("tenant-alpha", "operator", fixture.Request);
        var replay = await fixture.Service.AcceptAsync("tenant-alpha", "operator", fixture.Request);
        Assert.Equal(first, replay);
        var snapshot = await fixture.Accounts.GetCustodianSnapshotAsync(fixture.Request.AccountId, fixture.Request.AsOfDate);
        var holding = Assert.Single(snapshot!.Lines);
        Assert.Equal(80m, holding.Bond!.CurrentFace);
        Assert.Equal(100m, holding.Bond.OriginalFace);
        Assert.Equal(79.2m, holding.MarketValue);
        Assert.Equal("synthetic-approval", holding.Bond.SourceEvidenceId);
        fixture.Factory.Verify(f => f.ForTenant("tenant-alpha"), Times.Exactly(2));
    }

    [Theory]
    [InlineData("CostDerived", false)]
    [InlineData("ProviderReported", true)]
    public async Task CostDerivedOrUnreviewedInputsCannotBecomeAcceptedValuation(string valuationBasis, bool critical)
    {
        var fixture = await Fixture(valuationBasis, critical);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AcceptAsync("tenant-alpha", "operator", fixture.Request));
        Assert.Null(await fixture.Accounts.GetCustodianSnapshotAsync(fixture.Request.AccountId, fixture.Request.AsOfDate));
    }

    private static async Task<ImportFixture> Fixture(string valuationBasis = "ProviderReported", bool critical = false)
    {
        var accounts = new InMemoryFundAccountService();
        var accountId = Guid.NewGuid();
        var securityId = Guid.NewGuid();
        await accounts.CreateAccountAsync(new(accountId, AccountTypeDto.Custody, "SYNTHETIC", "Synthetic bonds", "USD",
            DateTimeOffset.UtcNow.AddDays(-1), "synthetic"));
        var source = new Mock<IProviderIntegrationManifestStore>();
        var factory = source.As<IProviderIntegrationTenantManifestStoreFactory>();
        factory.Setup(f => f.ForTenant("tenant-alpha")).Returns(source.Object);
        var now = DateTimeOffset.UtcNow;
        var economics = new BondHoldingEconomicsDto(100m, 80m, .8m, 1m, BondHoldingQuoteBasis.PercentOfCurrentFace,
            99m, false, 0m, InvoiceBasis: BondHoldingInvoiceBasis.Nominal);
        var mapped = JsonSerializer.SerializeToElement(new
        {
            accountId,
            asOfDate = "2026-11-12",
            assetClass = "Bond",
            valuationBasis,
            bond = economics,
            marketValue = new { amount = 79.2m, currency = "USD" }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        source.Setup(s => s.GetConnectionAsync("connection", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderConnectionDto("connection", "synthetic", "manifest", "Synthetic", "offline", default,
                "synthetic-no-secret", [ProviderCapabilityKindDto.Holdings], "synthetic", now, now, "synthetic-approval"));
        source.Setup(s => s.ListStagingRecordsAsync("run", It.IsAny<CancellationToken>())).ReturnsAsync([
            new("row", "run", "connection", ProviderCapabilityKindDto.Holdings, "raw", "source", "dedupe", mapped,
                critical ? [new("bad", ProviderIntegrationIssueSeverityDto.Critical, "Review required", null, null)] : [],
                ProviderIntegrationProcessingStatusDto.Loaded, now)]);
        source.Setup(s => s.ListReconciliationHandoffRecordsAsync("connection", It.IsAny<CancellationToken>())).ReturnsAsync([
            new("handoff", "connection", "run", "row", ProviderCapabilityKindDto.Holdings, "reconciliation-staging", "reviewer", now,
                "synthetic-approval", null, null, accountId.ToString(), securityId.ToString(), null, [])]);
        source.Setup(s => s.GetRawPayloadAsync("run", "raw", It.IsAny<CancellationToken>())).ReturnsAsync(
            new RawIngestionPayloadDto("raw", "synthetic", "connection", ProviderCapabilityKindDto.Holdings, "holdings", "run", now,
                new Dictionary<string, string>(), mapped, "v2", ProviderIntegrationProcessingStatusDto.Loaded));
        return new(new BondHoldingImportService(source.Object, accounts, accounts), accounts, factory,
            new("connection", "run", accountId, Guid.NewGuid(), new(2026, 11, 12)));
    }

    private sealed record ImportFixture(BondHoldingImportService Service, InMemoryFundAccountService Accounts,
        Mock<IProviderIntegrationTenantManifestStoreFactory> Factory, BondHoldingImportRequest Request);
}
