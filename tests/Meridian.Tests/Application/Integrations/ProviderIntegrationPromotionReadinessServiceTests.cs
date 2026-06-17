using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationPromotionReadinessServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationPromotionReadinessServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_provider_promotion_readiness_test_{Guid.NewGuid():N}");
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
    public async Task PreviewAsync_ClassifiesRowsBeforeReconciliationPromotion()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store).ConfigureAwait(false);
        var identityPreview = new ProviderIntegrationIdentityResolutionPreviewService(store);
        var service = new ProviderIntegrationPromotionReadinessService(identityPreview);

        var preview = await service.PreviewAsync("connection-alpha");

        preview.ConnectionId.Should().Be("connection-alpha");
        preview.SyncRunIds.Should().ContainSingle().Which.Should().Be("sync-run-promotion-1");
        preview.TotalRows.Should().Be(3);
        preview.ReadyForReconciliationCount.Should().Be(1);
        preview.ReviewRequiredCount.Should().Be(1);
        preview.BlockedCount.Should().Be(1);
        preview.Rows.Should().ContainSingle(row =>
            row.StagingRecordId == "staging-ready" &&
            row.PromotionTarget == "reconciliation-staging" &&
            row.Status == ProviderIntegrationPromotionReadinessStatusDto.ReadyForReconciliation);
        preview.Rows.Should().ContainSingle(row =>
            row.StagingRecordId == "staging-review" &&
            row.Status == ProviderIntegrationPromotionReadinessStatusDto.ReviewRequired &&
            row.Issues.Any(issue => issue.Code == "promotion.account.review-required"));
        preview.Rows.Should().ContainSingle(row =>
            row.StagingRecordId == "staging-blocked" &&
            row.Status == ProviderIntegrationPromotionReadinessStatusDto.Blocked &&
            row.Issues.Any(issue => issue.Code == "promotion.account.blocked") &&
            row.Issues.Any(issue => issue.Code == "promotion.security.blocked"));
    }

    [Fact]
    public async Task PreviewAsync_ObservesCancellationBeforeResolvingRows()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store).ConfigureAwait(false);
        var identityPreview = new ProviderIntegrationIdentityResolutionPreviewService(store);
        var service = new ProviderIntegrationPromotionReadinessService(identityPreview);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.PreviewAsync("connection-alpha", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static async Task SeedStagingAsync(IProviderIntegrationManifestStore store)
    {
        await store.SaveManifestAsync(CreateManifest()).ConfigureAwait(false);
        await store.SaveConnectionAsync(CreateConnection()).ConfigureAwait(false);
        await store.SaveSyncRunAsync(new ProviderIntegrationSyncRunDto(
            "sync-run-promotion-1",
            "manifest-custodian-abc-v1",
            "connection-alpha",
            "custodian-abc",
            ProviderCapabilityKindDto.Positions,
            "positions",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:02:00Z"),
            ProviderIntegrationProcessingStatusDto.Validated,
            RecordsReceived: 3,
            RecordsAccepted: 3,
            RecordsQuarantined: 0,
            RawPayloadId: "payload-1",
            Issues: [])).ConfigureAwait(false);
        await store.SaveStagingRecordAsync(new IntegrationStagingRecordDto(
            "staging-ready",
            "sync-run-promotion-1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            "payload-1",
            "source-position-1",
            "connection-alpha|positions|source-position-1",
            Json("""{"providerAccountId":"A1","internalAccountId":"internal-account-1","security":{"internalSecurityId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"},"quantity":100,"asOf":"2026-06-16"}"""),
            ValidationWarnings: [],
            ProviderIntegrationProcessingStatusDto.Validated,
            DateTimeOffset.Parse("2026-06-16T12:02:00Z"))).ConfigureAwait(false);
        await store.SaveStagingRecordAsync(new IntegrationStagingRecordDto(
            "staging-review",
            "sync-run-promotion-1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            "payload-1",
            "source-position-2",
            "connection-alpha|positions|source-position-2",
            Json("""{"providerAccountId":"A2","security":{"internalSecurityId":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"},"quantity":50,"asOf":"2026-06-16"}"""),
            ValidationWarnings: [],
            ProviderIntegrationProcessingStatusDto.Validated,
            DateTimeOffset.Parse("2026-06-16T12:03:00Z"))).ConfigureAwait(false);
        await store.SaveStagingRecordAsync(new IntegrationStagingRecordDto(
            "staging-blocked",
            "sync-run-promotion-1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            "payload-1",
            "source-position-3",
            "connection-alpha|positions|source-position-3",
            Json("""{"quantity":10,"asOf":"2026-06-16"}"""),
            ValidationWarnings: [],
            ProviderIntegrationProcessingStatusDto.Validated,
            DateTimeOffset.Parse("2026-06-16T12:04:00Z"))).ConfigureAwait(false);
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
            ChangeReason: "Promotion readiness test manifest");

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
}
