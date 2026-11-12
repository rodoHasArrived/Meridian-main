using System.Text.Json;
using Meridian.Application.Integrations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.SecurityMaster;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage.Integrations;
using Moq;

namespace Meridian.Tests.Application.Integrations;

public sealed class BondHoldingImportServiceTests
{
    [Fact]
    public async Task ActualCsvValidatedPopulation_RequiresRealRetainedHandoff_ThenPublishesWithoutStatusPromotion()
    {
        var root = Path.Combine(Path.GetTempPath(), "bond-reviewed-csv-" + Guid.NewGuid().ToString("N"));
        try
        {
            const string tenant = "tenant-alpha", connectionId = "reviewed-csv", runId = "csv-holdings-run";
            var manifests = new FileProviderIntegrationManifestStore(root);
            var store = manifests.ForTenant(tenant);
            var accounts = new InMemoryFundAccountService();
            var accountId = Guid.NewGuid();
            var securityId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await accounts.CreateAccountAsync(new(accountId, AccountTypeDto.Custody, "SYNTHETIC", "CSV bonds", "USD",
                now.AddDays(-1), "synthetic"));
            var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-fixed-income-security-master-v2", 2)!;
            manifest = manifest with
            {
                FieldMappings = [.. manifest.FieldMappings,
                new(ProviderCapabilityKindDto.Holdings, "$.account_id", "internalAccountId", null, true,
                    ProviderMappingConfidenceDto.Approved, null, null)]
            };
            await store.SaveManifestAsync(manifest);
            await store.SaveConnectionAsync(new(connectionId, manifest.ProviderId, manifest.ManifestId, "Reviewed CSV",
                "synthetic", ProviderIntegrationActivationStateDto.Draft, "synthetic-no-secret",
                [ProviderCapabilityKindDto.Holdings], "operator", now, now, null));
            var csv = "account_id,provider_account_id,as_of_date,cusip,original_face,current_face,principal_factor,inflation_ratio,quote_basis,invoice_basis,quote,quote_is_dirty,accrued_interest,market_value,currency,valuation_basis,source_id\n"
                + $"{accountId:D},SYN-1,2026-11-12,9128285M8,100,80,0.8,1,PercentOfCurrentFace,Nominal,99,false,0,79.2,USD,ProviderReported,POS-1";
            var dryRun = await new ProviderIntegrationDryRunService(manifests).RunManualCsvDryRunAsync(tenant,
                new(runId, manifest.ManifestId, connectionId, ProviderCapabilityKindDto.Holdings,
                    "synthetic-holdings.csv", csv, "operator", now));
            Assert.Equal(1, dryRun.RecordsAccepted);
            Assert.Equal(0, dryRun.RecordsQuarantined);
            Assert.Equal(ProviderIntegrationProcessingStatusDto.Validated, dryRun.Status);
            var staged = Assert.Single(await store.ListStagingRecordsAsync(runId));
            Assert.Equal(ProviderIntegrationProcessingStatusDto.Validated, staged.Status);
            Assert.NotNull(await store.GetRawPayloadAsync(runId, staged.RawPayloadId));
            var import = new BondHoldingImportService(manifests, accounts, accounts);
            var request = new BondHoldingImportRequest(connectionId, runId, accountId, Guid.NewGuid(), new(2026, 11, 12));
            await Assert.ThrowsAsync<InvalidOperationException>(() => import.AcceptAsync(tenant, "operator", request));
            Assert.Null(await accounts.GetCustodianSnapshotAsync(accountId, request.AsOfDate));

            var securities = new Mock<ISecurityMasterQueryService>();
            securities.Setup(service => service.GetByIdentifierAsync(SecurityIdentifierKind.Cusip, "9128285M8", null,
                It.IsAny<CancellationToken>())).ReturnsAsync(new SecurityDetailDto(securityId, "Bond", SecurityStatusDto.Active,
                    "Synthetic reviewed bond", "USD", JsonSerializer.SerializeToElement(new { }),
                    JsonSerializer.SerializeToElement(new { }), [], [], 1, now.AddDays(-1), null));
            var identity = new ProviderIntegrationIdentityResolutionPreviewService(manifests, securities.Object);
            var handoff = await new ProviderIntegrationReconciliationHandoffService(manifests,
                new ProviderIntegrationPromotionReadinessService(identity)).HandoffAsync(tenant,
                    new(connectionId, [staged.StagingRecordId], "reviewer", now.AddMinutes(1), "reviewed-csv-approval",
                        "Reviewed account/security and holding economics", 10));
            Assert.True(handoff.Accepted);
            var retainedReview = Assert.Single(await store.ListReconciliationHandoffRecordsAsync(connectionId));
            Assert.Equal("reviewed-csv-approval", retainedReview.ApprovalEvidenceId);
            Assert.Equal(ProviderIntegrationProcessingStatusDto.Validated,
                Assert.Single(await store.ListStagingRecordsAsync(runId)).Status);

            await import.AcceptAsync(tenant, "operator", request);
            var holding = Assert.Single((await accounts.GetCustodianSnapshotAsync(accountId, request.AsOfDate))!.Lines);
            Assert.Equal(securityId, holding.SecurityId);
            Assert.Equal(80m, holding.Bond!.CurrentFace);
            Assert.Equal(79.2m, holding.MarketValue);
            Assert.Equal(retainedReview.ApprovalEvidenceId, holding.Bond.SourceEvidenceId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

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
