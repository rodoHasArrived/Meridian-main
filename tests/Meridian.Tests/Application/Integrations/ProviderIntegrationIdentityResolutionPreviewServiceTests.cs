using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.Integrations;
using NSubstitute;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationIdentityResolutionPreviewServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationIdentityResolutionPreviewServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_provider_identity_preview_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewAsync_ResolvesSecurityCandidatesAndFlagsAccountReview()
    {
        var securityId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store, includeMissingRecord: true);
        var service = new ProviderIntegrationIdentityResolutionPreviewService(
            store,
            new FakeSecurityMasterQueryService(securityId));

        var preview = await service.PreviewAsync("connection-alpha");

        preview.TotalRows.Should().Be(2);
        preview.SecurityResolvedCount.Should().Be(1);
        preview.MissingSecurityIdentifierCount.Should().Be(1);
        preview.AccountReviewRequiredCount.Should().Be(1);
        preview.MissingAccountIdentifierCount.Should().Be(1);
        var resolved = preview.Rows.Single(row => row.StagingRecordId == "staging-resolved");
        resolved.SecurityStatus.Should().Be(ProviderIntegrationIdentityResolutionStatusDto.Resolved);
        resolved.InternalSecurityId.Should().Be(securityId.ToString("D"));
        resolved.SecurityCandidates.Should().ContainSingle(candidate =>
            candidate.IdentifierKind == "Cusip" &&
            candidate.Status == ProviderIntegrationIdentityResolutionStatusDto.Resolved);
        resolved.AccountStatus.Should().Be(ProviderIntegrationIdentityResolutionStatusDto.ReviewRequired);
        var missing = preview.Rows.Single(row => row.StagingRecordId == "staging-missing");
        missing.SecurityStatus.Should().Be(ProviderIntegrationIdentityResolutionStatusDto.MissingIdentifier);
        missing.AccountStatus.Should().Be(ProviderIntegrationIdentityResolutionStatusDto.MissingIdentifier);
        missing.Issues.Should().Contain(issue => issue.Code == "identity.security.identifier.missing");
    }

    [Fact]
    public async Task PreviewAsync_LoadsOneTenantScopedDecisionSnapshotPerRunAndKeepsEachRowsEvidenceIsolated()
    {
        var firstId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var secondId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        const string connectionId = "connection-alpha";
        const string syncRunId = "sync-run-identity-1";
        var retainedStore = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(retainedStore, false,
            """{"internalAccountId":"account-1","security":{"cusip":"123456789","isin":"US1234567890"}}""");
        var original = (await retainedStore.ListStagingRecordsAsync(syncRunId)).Single();
        await retainedStore.SaveStagingRecordAsync(original with { StagingRecordId = "staging-other-choice" });
        await retainedStore.SaveStagingRecordAsync(original with { StagingRecordId = "staging-invalid-decisions" });
        var query = new FakeSecurityMasterQueryService(firstId, secondId);
        var evidenceHash = (await new ProviderIntegrationIdentityResolutionPreviewService(retainedStore, query)
            .PreviewAsync(connectionId)).Rows.First().EvidenceHash!;
        var reviewedAt = DateTimeOffset.Parse("2026-06-17T00:00:00Z");
        var firstChoice = new ProviderIntegrationIdentityDecisionDto("choice-first", connectionId, syncRunId,
            original.StagingRecordId, firstId, evidenceHash, "reviewer", reviewedAt, "Verified first choice.");
        IReadOnlyList<ProviderIntegrationIdentityDecisionDto> decisions =
        [
            firstChoice,
            firstChoice with { DecisionId = "choice-second", StagingRecordId = "staging-other-choice", SelectedSecurityId = secondId },
            firstChoice with { DecisionId = "stale-evidence", StagingRecordId = "staging-invalid-decisions", EvidenceHash = "changed" },
            firstChoice with { DecisionId = "foreign-connection", StagingRecordId = "staging-invalid-decisions", ConnectionId = "foreign" },
            firstChoice with { DecisionId = "foreign-run", StagingRecordId = "staging-invalid-decisions", SyncRunId = "foreign" }
        ];
        // Finish real asynchronous reads before configuring substitutes: awaiting between a
        // substitute call and Returns can lose NSubstitute's pending call on a thread switch.
        var retainedConnection = await retainedStore.GetConnectionAsync(connectionId);
        var retainedSyncRuns = await retainedStore.ListSyncRunsAsync(connectionId);
        var retainedStagingRecords = await retainedStore.ListStagingRecordsAsync(syncRunId);
        var scopedStore = Substitute.For<IProviderIntegrationManifestStore, IProviderIntegrationIdentityDecisionStore>();
        scopedStore.GetConnectionAsync(connectionId, Arg.Any<CancellationToken>())
            .Returns(retainedConnection);
        scopedStore.ListSyncRunsAsync(connectionId, Arg.Any<CancellationToken>())
            .Returns(retainedSyncRuns);
        scopedStore.ListStagingRecordsAsync(syncRunId, Arg.Any<CancellationToken>())
            .Returns(retainedStagingRecords);
        var decisionStore = (IProviderIntegrationIdentityDecisionStore)scopedStore;
        decisionStore.ListIdentityDecisionsAsync(syncRunId, Arg.Any<CancellationToken>()).Returns(decisions);
        var tenantRoot = Substitute.For<IProviderIntegrationManifestStore, IProviderIntegrationTenantManifestStoreFactory>();
        ((IProviderIntegrationTenantManifestStoreFactory)tenantRoot).ForTenant("tenant-one").Returns(scopedStore);

        var preview = await new ProviderIntegrationIdentityResolutionPreviewService(tenantRoot, query)
            .PreviewAsync("tenant-one", connectionId);

        preview.TotalRows.Should().Be(3);
        preview.Rows.Single(row => row.StagingRecordId == original.StagingRecordId).InternalSecurityId.Should().Be(firstId.ToString("D"));
        preview.Rows.Single(row => row.StagingRecordId == "staging-other-choice").InternalSecurityId.Should().Be(secondId.ToString("D"));
        var unresolved = preview.Rows.Single(row => row.StagingRecordId == "staging-invalid-decisions");
        unresolved.SecurityStatus.Should().Be(ProviderIntegrationIdentityResolutionStatusDto.Conflict);
        unresolved.OperatorResolution.Should().BeNull();
        await decisionStore.Received(1).ListIdentityDecisionsAsync(syncRunId, Arg.Any<CancellationToken>());
        await tenantRoot.DidNotReceive().GetConnectionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewAsync_ResolvesEveryIdentifierAndDetectsDisagreement(bool disagree)
    {
        var cusipId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var isinId = disagree ? Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb") : cusipId;
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store, false,
            """{"internalAccountId":"account-1","security":{"cusip":"123456789","isin":"US1234567890"}}""");
        var query = new FakeSecurityMasterQueryService(cusipId, isinId);
        var preview = await new ProviderIntegrationIdentityResolutionPreviewService(store, query).PreviewAsync("connection-alpha");

        var row = preview.Rows.Single();
        query.Lookups.Should().Equal(SecurityIdentifierKind.Cusip, SecurityIdentifierKind.Isin);
        row.SecurityCandidates.Should().HaveCount(2).And.OnlyContain(candidate => candidate.Status == ProviderIntegrationIdentityResolutionStatusDto.Resolved);
        row.HasIdentifierConflict.Should().Be(disagree);
        row.SecurityStatus.Should().Be(disagree ? ProviderIntegrationIdentityResolutionStatusDto.Conflict : ProviderIntegrationIdentityResolutionStatusDto.Resolved);
        if (disagree)
        {
            row.InternalSecurityId.Should().BeNull();
            preview.SecurityReviewRequiredCount.Should().Be(1);
            row.Issues.Should().Contain(issue => issue.Code == "identity.security.identifier-conflict" && issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
            var readiness = await new ProviderIntegrationPromotionReadinessService(
                new ProviderIntegrationIdentityResolutionPreviewService(store, query)).PreviewAsync("connection-alpha");
            readiness.Rows.Single().Status.Should().Be(ProviderIntegrationPromotionReadinessStatusDto.Blocked);
        }
    }

    [Fact]
    public async Task ResolveAsync_RetainsChoiceWithoutChangingSourceAndInvalidatesChangedEvidence()
    {
        var cusipId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var isinId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store, false,
            """{"internalAccountId":"account-1","security":{"cusip":"123456789","isin":"US1234567890"}}""");
        var service = new ProviderIntegrationIdentityResolutionPreviewService(store, new FakeSecurityMasterQueryService(cusipId, isinId));
        var before = (await service.PreviewAsync("connection-alpha")).Rows.Single();
        var sourceBefore = (await store.ListStagingRecordsAsync(before.SyncRunId)).Single().MappedRecord.GetRawText();
        var request = new ProviderIntegrationIdentityResolutionRequestDto(before.SyncRunId, before.StagingRecordId, isinId, before.EvidenceHash!, "CUSIP belongs to replaced issue; reviewed original provider file.");
        var decision = await service.ResolveAsync(null, "connection-alpha", request, "authenticated-operator");

        var reloadedStore = new FileProviderIntegrationManifestStore(testRoot);
        var after = (await new ProviderIntegrationIdentityResolutionPreviewService(reloadedStore,
            new FakeSecurityMasterQueryService(cusipId, isinId)).PreviewAsync("connection-alpha")).Rows.Single();
        after.SecurityStatus.Should().Be(ProviderIntegrationIdentityResolutionStatusDto.Resolved);
        after.InternalSecurityId.Should().Be(isinId.ToString("D"));
        after.HasIdentifierConflict.Should().BeTrue();
        after.OperatorResolution.Should().BeEquivalentTo(decision);
        var ready = await new ProviderIntegrationPromotionReadinessService(
            new ProviderIntegrationIdentityResolutionPreviewService(reloadedStore,
                new FakeSecurityMasterQueryService(cusipId, isinId))).PreviewAsync("connection-alpha");
        ready.Rows.Single().Status.Should().Be(ProviderIntegrationPromotionReadinessStatusDto.ReadyForReconciliation);
        (await reloadedStore.ListStagingRecordsAsync(before.SyncRunId)).Single().MappedRecord.GetRawText().Should().Be(sourceBefore);

        var changedService = new ProviderIntegrationIdentityResolutionPreviewService(reloadedStore,
            new FakeSecurityMasterQueryService(cusipId, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")));
        var changed = (await changedService.PreviewAsync("connection-alpha")).Rows.Single();
        changed.OperatorResolution.Should().BeNull();
        changed.SecurityStatus.Should().Be(ProviderIntegrationIdentityResolutionStatusDto.Conflict);
        var staleChoice = () => changedService.ResolveAsync(null, "connection-alpha", request, "authenticated-operator");
        await staleChoice.Should().ThrowAsync<InvalidOperationException>();
        var overwrite = () => reloadedStore.SaveIdentityDecisionAsync(decision with { Reason = "overwritten" });
        await overwrite.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task PreviewAsync_DoesNotBypassProviderEvidenceWhenInternalIdIsPresent()
    {
        var internalId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store, false,
            """{"security":{"internalSecurityId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","isin":"US1234567890"}}""");
        var service = new ProviderIntegrationIdentityResolutionPreviewService(store,
            new FakeSecurityMasterQueryService(internalId, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")));
        var row = (await service.PreviewAsync("connection-alpha")).Rows.Single();
        row.SecurityStatus.Should().Be(ProviderIntegrationIdentityResolutionStatusDto.Conflict);
        row.SecurityCandidates.Should().HaveCount(2);
    }

    [Fact]
    public async Task PreviewAsync_ChecksBothTickerAliases()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store, false,
            """{"security":{"cusip":"123456789","ticker":"BOND","symbol":"BOND-OTHER"}}""");
        var query = new FakeSecurityMasterQueryService(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            tickerSecurityId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var row = (await new ProviderIntegrationIdentityResolutionPreviewService(store, query).PreviewAsync("connection-alpha")).Rows.Single();
        row.SecurityStatus.Should().Be(ProviderIntegrationIdentityResolutionStatusDto.Conflict);
        row.SecurityCandidates.Should().HaveCount(3);
        row.SecurityCandidates.Where(candidate => candidate.IdentifierKind == "Ticker")
            .Select(candidate => candidate.IdentifierValue).Should().Equal("BOND", "BOND-OTHER");
    }

    [Fact]
    public async Task ResolveAsync_RejectsInactiveOrUnmatchedChoicesAndForeignConnections()
    {
        var cusipId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var isinId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store, false,
            """{"security":{"cusip":"123456789","isin":"US1234567890"}}""");
        var service = new ProviderIntegrationIdentityResolutionPreviewService(store, new FakeSecurityMasterQueryService(cusipId, isinId, inactiveIsin: true));
        var row = (await service.PreviewAsync("connection-alpha")).Rows.Single();
        var request = new ProviderIntegrationIdentityResolutionRequestDto(row.SyncRunId, row.StagingRecordId, isinId, row.EvidenceHash!, "Reviewed");
        var inactive = () => service.ResolveAsync(null, "connection-alpha", request, "operator");
        await inactive.Should().ThrowAsync<ArgumentException>();
        var unmatched = () => service.ResolveAsync(null, "connection-alpha", request with { SelectedSecurityId = Guid.NewGuid() }, "operator");
        await unmatched.Should().ThrowAsync<ArgumentException>();
        var foreignRun = () => service.ResolveAsync(null, "connection-alpha", request with { SyncRunId = "foreign-run" }, "operator");
        await foreignRun.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task PreviewAsync_ObservesCancellationBeforeReadingRuns()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store, includeMissingRecord: false);
        var service = new ProviderIntegrationIdentityResolutionPreviewService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.PreviewAsync("connection-alpha", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static async Task SeedStagingAsync(
        IProviderIntegrationManifestStore store,
        bool includeMissingRecord,
        string? mappedRecordJson = null)
    {
        var manifest = CreateManifest();
        var manifestReference = ProviderIntegrationManifestIdentity.Create(manifest);
        await store.SaveManifestAsync(manifest).ConfigureAwait(false);
        await store.SaveConnectionAsync(CreateConnection()).ConfigureAwait(false);
        await store.SaveSyncRunAsync(new ProviderIntegrationSyncRunDto(
            "sync-run-identity-1",
            "manifest-custodian-abc-v1",
            "connection-alpha",
            "custodian-abc",
            ProviderCapabilityKindDto.Positions,
            "positions",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:02:00Z"),
            ProviderIntegrationProcessingStatusDto.Validated,
            RecordsReceived: includeMissingRecord ? 2 : 1,
            RecordsAccepted: includeMissingRecord ? 2 : 1,
            RecordsQuarantined: 0,
            RawPayloadId: "payload-1",
            Issues: [])
        {
            ManifestReference = manifestReference,
            OriginalManifestReference = manifestReference
        }).ConfigureAwait(false);
        await store.SaveStagingRecordAsync(new IntegrationStagingRecordDto(
            "staging-resolved",
            "sync-run-identity-1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            "payload-1",
            "source-position-1",
            "connection-alpha|positions|source-position-1",
            Json(mappedRecordJson ?? """{"providerAccountId":"A1","security":{"cusip":"123456789"},"quantity":100,"asOf":"2026-06-16"}"""),
            ValidationWarnings: [],
            ProviderIntegrationProcessingStatusDto.Validated,
            DateTimeOffset.Parse("2026-06-16T12:02:00Z"))).ConfigureAwait(false);
        if (!includeMissingRecord)
        {
            return;
        }

        await store.SaveStagingRecordAsync(new IntegrationStagingRecordDto(
            "staging-missing",
            "sync-run-identity-1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            "payload-1",
            "source-position-2",
            "connection-alpha|positions|source-position-2",
            Json("""{"quantity":50,"asOf":"2026-06-16"}"""),
            ValidationWarnings: [],
            ProviderIntegrationProcessingStatusDto.Validated,
            DateTimeOffset.Parse("2026-06-16T12:03:00Z"))).ConfigureAwait(false);
    }

    private static ProviderIntegrationManifestDto CreateManifest()
        => new(
            "manifest-custodian-abc-v1",
            1,
            "custodian-abc",
            "Custodian ABC",
            IntegrationTypeDto.Rest,
            "production",
            new ProviderIntegrationAuthConfigDto(
                ProviderIntegrationAuthTypeDto.OAuth2,
                "https://api.example.com/oauth/token",
                ["positions.read"],
                new Dictionary<string, string>()),
            [
                new ProviderCapabilityDto(
                    ProviderCapabilityKindDto.Positions,
                    Enabled: true,
                    RequiresCertifiedAdapter: false,
                    RequiredCanonicalFields: ["providerAccountId", "quantity", "asOf"])
            ],
            [],
            [],
            new SyncScheduleDto(
                "incremental",
                "daily",
                "06:00",
                "America/New_York",
                ProviderIntegrationCursorTypeDto.Timestamp,
                "updated_at",
                "monthly"),
            [],
            new ProviderIntegrationActivationPolicyDto(
                RequiresAuthenticationTest: true,
                RequiresEndpointTest: true,
                RequiresDryRun: true,
                RequiresApproval: true,
                ProductionWriteCapabilitiesAllowed: false,
                RequiredIssueCodes: []),
            ProviderIntegrationActivationStateDto.Draft,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Identity preview test manifest");

    private static ProviderConnectionDto CreateConnection()
        => new(
            "connection-alpha",
            "custodian-abc",
            "manifest-custodian-abc-v1",
            "Custodian ABC General Account",
            "production",
            ProviderIntegrationActivationStateDto.Draft,
            "vault://provider-credentials/custodian-abc/general-account",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovalEvidenceId: null);

    private static JsonElement Json(string json)
        => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class FakeSecurityMasterQueryService(Guid securityId, Guid? isinSecurityId = null, bool inactiveIsin = false, Guid? tickerSecurityId = null) : ISecurityMasterQueryService
    {
        public List<SecurityIdentifierKind> Lookups { get; } = [];
        public async Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => await GetByIdentifierAsync(SecurityIdentifierKind.Cusip, "123456789", null, ct);

        public Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default)
            => GetByIdAsync(securityId, ct);

        public Task<SecurityDetailDto?> GetByIdentifierAsync(
            SecurityIdentifierKind identifierKind,
            string identifierValue,
            string? provider,
            CancellationToken ct = default,
            DateTimeOffset? asOfUtc = null)
        {
            Lookups.Add(identifierKind);
            var isIsin = identifierKind == SecurityIdentifierKind.Isin && identifierValue == "US1234567890" && isinSecurityId.HasValue;
            var isOtherTicker = identifierKind == SecurityIdentifierKind.Ticker && identifierValue == "BOND-OTHER" && tickerSecurityId.HasValue;
            if ((identifierKind == SecurityIdentifierKind.Cusip && identifierValue == "123456789") || isIsin ||
                (identifierKind == SecurityIdentifierKind.Ticker && identifierValue == "BOND") || isOtherTicker)
            {
                return Task.FromResult<SecurityDetailDto?>(new SecurityDetailDto(
                    isIsin ? isinSecurityId!.Value : isOtherTicker ? tickerSecurityId!.Value : securityId,
                    "FixedIncome",
                    isIsin && inactiveIsin ? SecurityStatusDto.Inactive : SecurityStatusDto.Active,
                    "ABC 5.00 2031",
                    "USD",
                    Json("{}"),
                    Json("{}"),
                    [
                        new SecurityIdentifierDto(
                            SecurityIdentifierKind.Cusip,
                            "123456789",
                            IsPrimary: true,
                            DateTimeOffset.Parse("2026-01-01T00:00:00Z"))
                    ],
                    [],
                    Version: 1,
                    DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    EffectiveTo: null));
            }

            return Task.FromResult<SecurityDetailDto?>(null);
        }

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
