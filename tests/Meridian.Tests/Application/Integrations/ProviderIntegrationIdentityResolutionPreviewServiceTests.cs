using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.Integrations;

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
        bool includeMissingRecord)
    {
        await store.SaveManifestAsync(CreateManifest()).ConfigureAwait(false);
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
            Issues: [])).ConfigureAwait(false);
        await store.SaveStagingRecordAsync(new IntegrationStagingRecordDto(
            "staging-resolved",
            "sync-run-identity-1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            "payload-1",
            "source-position-1",
            "connection-alpha|positions|source-position-1",
            Json("""{"providerAccountId":"A1","security":{"cusip":"123456789"},"quantity":100,"asOf":"2026-06-16"}"""),
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

    private sealed class FakeSecurityMasterQueryService(Guid securityId) : ISecurityMasterQueryService
    {
        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<SecurityDetailDto?> GetByIdentifierAsync(
            SecurityIdentifierKind identifierKind,
            string identifierValue,
            string? provider,
            CancellationToken ct = default,
            DateTimeOffset? asOfUtc = null)
        {
            if (identifierKind == SecurityIdentifierKind.Cusip && identifierValue == "123456789")
            {
                return Task.FromResult<SecurityDetailDto?>(new SecurityDetailDto(
                    securityId,
                    "FixedIncome",
                    SecurityStatusDto.Active,
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
