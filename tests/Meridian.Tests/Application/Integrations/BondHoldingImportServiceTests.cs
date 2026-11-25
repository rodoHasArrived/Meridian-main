using System.Net;
using System.Text.Json;
using Meridian.Application.Integrations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.SecurityMaster;
using Meridian.PortfolioRecords.Accounts;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualMixedCsv_QuarantinedHoldingCannotBeDiscardedByReview_CompleteCorrectedRunCanRetry(bool missingAccount)
    {
        var fixture = await CsvFixture.CreateAsync();
        try
        {
            var result = await fixture.IngestAsync("mixed", missingAccount ? "" : fixture.AccountId.ToString("D"), "invalid");
            Assert.Equal(1, result.RecordsAccepted);
            Assert.Equal(1, result.RecordsQuarantined);
            await fixture.HandoffAsync("mixed");
            var quarantine = Assert.Single(await fixture.Store.ListQuarantinedRecordsAsync("mixed"));
            var request = fixture.Request("mixed");
            var initial = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Import.AcceptAsync(CsvFixture.Tenant, "operator", request));
            Assert.Contains("quarantined holdings", initial.Message);
            foreach (var action in Enum.GetValues<ProviderIntegrationQuarantineResolutionActionDto>())
            {
                // Real review decisions retain the rejected row; they do not create its missing economics.
                await new ProviderIntegrationQuarantineReviewService(fixture.Manifests).ResolveAsync(CsvFixture.Tenant,
                    new(CsvFixture.Connection, "mixed", quarantine.QuarantineRecordId, action, "reviewer",
                        fixture.Now.AddMinutes(2 + (int)action), "Synthetic review; retain the original rejected source."));
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Import.AcceptAsync(CsvFixture.Tenant, "operator", request));
                Assert.Contains("complete corrected statement", error.Message);
                Assert.Null(await fixture.Accounts.GetCustodianSnapshotAsync(fixture.AccountId, request.AsOfDate));
            }
            // Re-ingest both original positions, with corrected source facts, and review both rows.
            var corrected = await fixture.IngestAsync("complete-corrected", fixture.AccountId.ToString("D"), "79.2");
            Assert.Equal(2, corrected.RecordsAccepted);
            Assert.Equal(0, corrected.RecordsQuarantined);
            await fixture.HandoffAsync("complete-corrected");
            var complete = fixture.Request("complete-corrected");
            await fixture.Import.AcceptAsync(CsvFixture.Tenant, "operator", complete);
            var snapshot = (await fixture.Accounts.GetCustodianSnapshotAsync(fixture.AccountId, complete.AsOfDate))!;
            Assert.Equal(2, snapshot.Lines.Count);
            Assert.Equal(158.4m, snapshot.Lines.Sum(line => line.MarketValue));
            Assert.Single(await fixture.Store.ListQuarantinedRecordsAsync("mixed"));
        }
        finally { fixture.Dispose(); }
    }

    [Fact]
    public async Task ActualMixedCsv_ProvenOtherAccountQuarantineDoesNotBlockReviewedAccount()
    {
        var fixture = await CsvFixture.CreateAsync();
        try
        {
            var other = Guid.NewGuid();
            await fixture.Accounts.CreateAccountAsync(new(other, AccountTypeDto.Custody, "SYNTHETIC-OTHER", "Other CSV bonds", "USD",
                fixture.Now.AddDays(-1), "synthetic"));
            await fixture.IngestAsync("other-account", other.ToString("D"), "invalid");
            await fixture.HandoffAsync("other-account");
            var activation = await new ProviderIntegrationActivationReadinessService(fixture.Manifests).EvaluateAsync(
                tenantId: CsvFixture.Tenant, manifestId: fixture.Manifest.ManifestId, connectionId: CsvFixture.Connection);
            Assert.False(activation.IsReady);
            Assert.Contains(activation.Issues, issue => issue.Code == "provider-manifest.dry-run-evidence-required");
            var request = fixture.Request("other-account");
            await fixture.Import.AcceptAsync(CsvFixture.Tenant, "operator", request);
            var holding = Assert.Single((await fixture.Accounts.GetCustodianSnapshotAsync(fixture.AccountId, request.AsOfDate))!.Lines);
            Assert.Equal(79.2m, holding.MarketValue);
            Assert.Single(await fixture.Store.ListQuarantinedRecordsAsync("other-account"));
        }
        finally { fixture.Dispose(); }
    }

    [Theory]
    [InlineData("missing-quarantine")]
    [InlineData("forged-conserved-counts")]
    [InlineData("unexplained-critical-issue")]
    [InlineData("missing-capture")]
    public async Task ActualMixedCsv_SelectedHandoffRequiresCompleteRetainedPopulation(string corruption)
    {
        var fixture = await CsvFixture.CreateAsync();
        try
        {
            await fixture.IngestAsync("incomplete-selected-source", Guid.NewGuid().ToString("D"), "invalid");
            var run = (await fixture.Store.GetSyncRunAsync("incomplete-selected-source"))!;
            var rejected = Assert.Single(await fixture.Store.ListQuarantinedRecordsAsync(run.SyncRunId));
            if (corruption is "missing-quarantine" or "forged-conserved-counts" or "missing-capture")
            {
                var property = corruption == "missing-capture" ? "payloadId" : "quarantineRecordId";
                var identity = corruption == "missing-capture" ? run.RawPayloadId : rejected.QuarantineRecordId;
                var retainedFile = Directory.EnumerateFiles(fixture.Root, "*.json", SearchOption.AllDirectories).Single(path =>
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    return document.RootElement.TryGetProperty(property, out var value) && value.GetString() == identity;
                });
                File.Delete(retainedFile);
            }
            if (corruption == "forged-conserved-counts")
                await fixture.Store.SaveSyncRunAsync(run with
                {
                    Status = ProviderIntegrationProcessingStatusDto.Validated,
                    RecordsReceived = 1,
                    RecordsQuarantined = 0,
                    Issues = []
                });
            if (corruption == "unexplained-critical-issue")
                await fixture.Store.SaveSyncRunAsync(run with
                {
                    Issues = [.. run.Issues, new("source.synthetic-failure", ProviderIntegrationIssueSeverityDto.Critical,
                        "The source did not complete.", null, null)]
                });

            var handoff = await fixture.HandoffResultAsync(run.SyncRunId);

            Assert.False(handoff.Accepted);
            Assert.Contains(handoff.Issues, issue => issue.Code == "source.run.incomplete");
            Assert.Empty(await fixture.Store.ListReconciliationHandoffRecordsAsync(CsvFixture.Connection));
            Assert.Null(await fixture.Accounts.GetCustodianSnapshotAsync(fixture.AccountId, fixture.Request(run.SyncRunId).AsOfDate));
        }
        finally { fixture.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualMixedCsv_UnknownOrSubstitutedOtherAccountAuthorityCannotReleaseQuarantine(bool substitutesRequestedAccount)
    {
        var fixture = await CsvFixture.CreateAsync();
        try
        {
            await fixture.IngestAsync("unknown-other", Guid.NewGuid().ToString("D"), "invalid");
            await fixture.HandoffAsync("unknown-other");
            var service = fixture.Import;
            if (substitutesRequestedAccount)
            {
                var accountAuthority = new Mock<IAccountQueryService>();
                accountAuthority.Setup(query => query.GetAccountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(await fixture.Accounts.GetAccountAsync(fixture.AccountId));
                service = new(fixture.Manifests, fixture.Accounts, accountAuthority.Object);
            }
            var request = fixture.Request("unknown-other");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptAsync(CsvFixture.Tenant, "operator", request));
            Assert.Null(await fixture.Accounts.GetCustodianSnapshotAsync(fixture.AccountId, request.AsOfDate));
        }
        finally { fixture.Dispose(); }
    }

    [Fact]
    public async Task ActualValidCsv_SubsetHandoffCannotOmitAnUnreviewedInternalAccountHolding()
    {
        var fixture = await CsvFixture.CreateAsync(internalAccountOnly: true);
        try
        {
            await fixture.IngestAsync("partial-review", fixture.AccountId.ToString("D"), "79.2");
            Assert.All(await fixture.Store.ListStagingRecordsAsync("partial-review"), row =>
            {
                Assert.False(row.MappedRecord.TryGetProperty("accountId", out _));
                Assert.Equal(fixture.AccountId.ToString("D"), row.MappedRecord.GetProperty("internalAccountId").GetString());
            });
            await fixture.HandoffAsync("partial-review", 1);
            var request = fixture.Request("partial-review");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Import.AcceptAsync(CsvFixture.Tenant, "operator", request));
            Assert.Contains("unreviewed rows", error.Message);
            Assert.Null(await fixture.Accounts.GetCustodianSnapshotAsync(fixture.AccountId, request.AsOfDate));
        }
        finally { fixture.Dispose(); }
    }

    [Fact]
    public async Task ActualRestFetch_AcceptedFirstPageAndFailedNextPageCannotEstablishFullSnapshot()
    {
        var fixture = await CsvFixture.CreateAsync();
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                holdings = new[] { new { account_id = fixture.AccountId.ToString("D"), provider_account_id = "SYN-1",
                    as_of_date = "2026-11-12", cusip = "9128285M8", original_face = "100", current_face = "80",
                    principal_factor = "0.8", inflation_ratio = "1", quote_basis = "PercentOfCurrentFace",
                    invoice_basis = "Nominal", quote = "99", quote_is_dirty = "false", accrued_interest = "0",
                    market_value = "79.2", currency = "USD", valuation_basis = "ProviderReported", source_id = "POS-1" } },
                nextCursor = "second-page"
            });
            var handler = new FailedSecondPageHandler(payload);
            using var client = new HttpClient(handler);
            var transport = new ProviderIntegrationHttpClientTransport(client, hostResolver: new SyntheticPublicHostResolver());
            var result = await new ProviderIntegrationRestDryRunService(fixture.Manifests, transport).RunRestDryRunAsync(CsvFixture.Tenant,
                new("failed-page", fixture.Manifest.ManifestId, CsvFixture.Connection, ProviderCapabilityKindDto.Holdings,
                    "holdings", new Dictionary<string, string>(), new Dictionary<string, string>(), "operator", fixture.Now, 3));
            Assert.Equal(1, result.RecordsReceived);
            Assert.Equal(1, result.RecordsAccepted);
            Assert.Equal(0, result.RecordsQuarantined);
            Assert.Contains(result.Issues, issue => issue.Code == "http.status.failed");
            Assert.Equal(new[] { "https://provider.example.test/synthetic/holdings",
                "https://provider.example.test/synthetic/holdings?cursor=second-page" }, handler.Requests);
            var handoff = await fixture.HandoffResultAsync("failed-page");
            Assert.False(handoff.Accepted);
            Assert.Contains(handoff.Issues, issue => issue.Code == "source.run.incomplete");
            Assert.Empty(await fixture.Store.ListReconciliationHandoffRecordsAsync(CsvFixture.Connection));
            var request = fixture.Request("failed-page");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Import.AcceptAsync(CsvFixture.Tenant, "operator", request));
            Assert.Contains("complete source fetch", error.Message);
            Assert.Null(await fixture.Accounts.GetCustodianSnapshotAsync(fixture.AccountId, request.AsOfDate));
        }
        finally { fixture.Dispose(); }
    }

    [Fact]
    public async Task ActualRemediationReplay_ReviewedReleasedSubsetCannotReplaceCompleteOriginalStatement()
    {
        var fixture = await CsvFixture.CreateAsync();
        try
        {
            await fixture.IngestAsync("original", fixture.AccountId.ToString("D"), "invalid");
            var quarantine = Assert.Single(await fixture.Store.ListQuarantinedRecordsAsync("original"));
            var remediation = fixture.Manifest with
            {
                ManifestVersion = fixture.Manifest.ManifestVersion + 1,
                FieldMappings = fixture.Manifest.FieldMappings.Select(mapping => mapping.TargetField == "marketValue.amount"
                    ? mapping with { ConstantValue = "79.2" } : mapping).ToArray()
            };
            await fixture.Store.SaveManifestVersionAsync(remediation);
            Assert.True(await fixture.Store.CompareExchangeCurrentManifestAsync(remediation.ManifestId,
                ProviderIntegrationManifestIdentity.Create(fixture.Manifest), ProviderIntegrationManifestIdentity.Create(remediation)));
            var replay = await new ProviderIntegrationQuarantineReplayService(fixture.Manifests).ReplayAsync(CsvFixture.Tenant,
                new("released-subset", "original", remediation.ManifestId, CsvFixture.Connection,
                    ProviderCapabilityKindDto.Holdings, [quarantine.QuarantineRecordId], "reviewer", fixture.Now.AddMinutes(3))
                {
                    Mode = ProviderIntegrationReplayModeDto.Remediation,
                    TargetManifestVersion = remediation.ManifestVersion,
                    TargetManifestDigest = ProviderIntegrationManifestIdentity.Create(remediation).ContentDigest
                });
            Assert.Equal(1, replay.RecordsAccepted);
            Assert.Equal(0, replay.RecordsRequarantined);
            await fixture.HandoffAsync("released-subset");
            var request = fixture.Request("released-subset");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Import.AcceptAsync(CsvFixture.Tenant, "operator", request));
            Assert.Contains("replay subset", error.Message);
            Assert.Null(await fixture.Accounts.GetCustodianSnapshotAsync(fixture.AccountId, request.AsOfDate));
            Assert.Single(await fixture.Store.ListStagingRecordsAsync("original"));
            Assert.Single(await fixture.Store.ListQuarantinedRecordsAsync("original"));
        }
        finally { fixture.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualReviewedCsv_NonconservedOrTruncatedCompletedPopulationCannotPublish(bool retainedRowMissing)
    {
        var fixture = await CsvFixture.CreateAsync();
        try
        {
            await fixture.IngestAsync("incomplete", Guid.NewGuid().ToString("D"), "invalid");
            await fixture.HandoffAsync("incomplete");
            var run = (await fixture.Store.GetSyncRunAsync("incomplete"))!;
            await fixture.Store.SaveSyncRunAsync(retainedRowMissing
                ? run with { RecordsReceived = 3, RecordsAccepted = 2 }
                : run with { RecordsReceived = 3 });
            var request = fixture.Request("incomplete");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Import.AcceptAsync(CsvFixture.Tenant, "operator", request));
            Assert.Contains("completed source population", error.Message);
            Assert.Null(await fixture.Accounts.GetCustodianSnapshotAsync(fixture.AccountId, request.AsOfDate));
        }
        finally { fixture.Dispose(); }
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
        source.Setup(s => s.GetSyncRunAsync("run", It.IsAny<CancellationToken>())).ReturnsAsync(
            new ProviderIntegrationSyncRunDto("run", "manifest", "connection", "synthetic", ProviderCapabilityKindDto.Holdings,
                "holdings", now, now, ProviderIntegrationProcessingStatusDto.Validated, 1, 1, 0, "raw", []));
        source.Setup(s => s.ListQuarantinedRecordsAsync("run", It.IsAny<CancellationToken>())).ReturnsAsync([]);
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

    private sealed record CsvFixture(string Root, FileProviderIntegrationManifestStore Manifests,
        IProviderIntegrationManifestStore Store, InMemoryFundAccountService Accounts, Guid AccountId,
        ProviderIntegrationManifestDto Manifest, DateTimeOffset Now, BondHoldingImportService Import) : IDisposable
    {
        public const string Tenant = "tenant-quarantine", Connection = "mixed-csv";

        public static async Task<CsvFixture> CreateAsync(bool internalAccountOnly = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "bond-complete-csv-" + Guid.NewGuid().ToString("N"));
            var manifests = new FileProviderIntegrationManifestStore(root);
            var store = manifests.ForTenant(Tenant);
            var accounts = new InMemoryFundAccountService();
            var accountId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await accounts.CreateAccountAsync(new(accountId, AccountTypeDto.Custody, "SYNTHETIC", "Complete CSV bonds", "USD",
                now.AddDays(-1), "synthetic"));
            var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-fixed-income-security-master-v2", 2)!;
            manifest = manifest with
            {
                IntegrationType = IntegrationTypeDto.Hybrid,
                Auth = manifest.Auth with
                {
                    Metadata = new Dictionary<string, string>(manifest.Auth.Metadata)
                    { ["baseUrl"] = "https://provider.example.test" }
                },
                Endpoints = [new("holdings", ProviderCapabilityKindDto.Holdings, ProviderIntegrationHttpMethodDto.Get,
                    "/synthetic/holdings", new Dictionary<string, string>(), new Dictionary<string, string>(), null, null,
                    new(ProviderIntegrationPaginationTypeDto.Cursor, "$.nextCursor", "cursor", null, 100),
                    new("$.holdings", null, []))],
                FieldMappings = [.. manifest.FieldMappings,
                    new(ProviderCapabilityKindDto.Holdings, "$.account_id", "internalAccountId", null, true,
                        ProviderMappingConfidenceDto.Approved, null, null)]
            };
            if (internalAccountOnly)
                manifest = manifest with
                {
                    FieldMappings = manifest.FieldMappings.Where(mapping => mapping.TargetField != "accountId").ToArray(),
                    Capabilities = manifest.Capabilities.Select(capability => capability with
                    {
                        RequiredCanonicalFields = capability.RequiredCanonicalFields.Where(field => field != "accountId").ToArray()
                    }).ToArray()
                };
            await store.SaveManifestAsync(manifest);
            await store.SaveConnectionAsync(new(Connection, manifest.ProviderId, manifest.ManifestId, "Mixed CSV",
                "synthetic", ProviderIntegrationActivationStateDto.Draft, "synthetic-no-secret",
                [ProviderCapabilityKindDto.Holdings], "operator", now, now, null));
            return new(root, manifests, store, accounts, accountId, manifest, now,
                new BondHoldingImportService(manifests, accounts, accounts));
        }

        public Task<ProviderIntegrationDryRunResultDto> IngestAsync(string run, string secondAccount, string secondValue)
        {
            var csv = "account_id,provider_account_id,as_of_date,cusip,original_face,current_face,principal_factor,inflation_ratio,quote_basis,invoice_basis,quote,quote_is_dirty,accrued_interest,market_value,currency,valuation_basis,source_id\n"
                + $"{AccountId:D},SYN-1,2026-11-12,9128285M8,100,80,0.8,1,PercentOfCurrentFace,Nominal,99,false,0,79.2,USD,ProviderReported,POS-1\n"
                + $"{secondAccount},SYN-2,2026-11-12,9128285M8,100,80,0.8,1,PercentOfCurrentFace,Nominal,99,false,0,{secondValue},USD,ProviderReported,POS-2";
            return new ProviderIntegrationDryRunService(Manifests).RunManualCsvDryRunAsync(Tenant,
                new(run, Manifest.ManifestId, Connection, ProviderCapabilityKindDto.Holdings,
                    "synthetic-full-statement.csv", csv, "operator", Now));
        }

        public async Task HandoffAsync(string run, int? take = null)
        {
            var handoff = await HandoffResultAsync(run, take);
            Assert.True(handoff.Accepted, string.Join("; ", handoff.Issues.Select(issue => issue.Message)));
        }

        public async Task<ProviderIntegrationReconciliationHandoffResultDto> HandoffResultAsync(string run, int? take = null)
        {
            var securities = new Mock<ISecurityMasterQueryService>();
            securities.Setup(service => service.GetByIdentifierAsync(SecurityIdentifierKind.Cusip, "9128285M8", null,
                It.IsAny<CancellationToken>())).ReturnsAsync(new SecurityDetailDto(Guid.Parse("428f4a50-9674-4a9f-8b7f-8343c32ae6df"),
                    "Bond", SecurityStatusDto.Active, "Synthetic reviewed bond", "USD", JsonSerializer.SerializeToElement(new { }),
                    JsonSerializer.SerializeToElement(new { }), [], [], 1, Now.AddDays(-1), null));
            var rows = await Store.ListStagingRecordsAsync(run);
            Assert.NotEmpty(rows);
            var identity = new ProviderIntegrationIdentityResolutionPreviewService(Manifests, securities.Object);
            return await new ProviderIntegrationReconciliationHandoffService(Manifests,
                new ProviderIntegrationPromotionReadinessService(identity)).HandoffAsync(Tenant,
                    new(Connection, rows.Take(take ?? rows.Count).Select(row => row.StagingRecordId).ToArray(), "reviewer", Now.AddMinutes(1),
                        "synthetic-reviewed-" + run, "Reviewed retained complete account/security and holding economics", 10));
        }

        public BondHoldingImportRequest Request(string run) => new(Connection, run, AccountId, Guid.NewGuid(), new(2026, 11, 12));
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    private sealed class FailedSecondPageHandler(string firstPage) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(firstPage) },
                2 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                { Content = new StringContent("{\"error\":\"synthetic unavailable\"}") },
                _ => throw new InvalidOperationException("The synthetic statement has exactly two provider pages.")
            });
        }
    }

    private sealed class SyntheticPublicHostResolver : IProviderIntegrationHostResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
        {
            Assert.Equal("provider.example.test", host);
            return ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("203.0.113.10")]);
        }
    }
}
