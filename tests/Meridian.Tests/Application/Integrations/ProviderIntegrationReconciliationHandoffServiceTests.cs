using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationReconciliationHandoffServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationReconciliationHandoffServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_provider_reconciliation_handoff_test_{Guid.NewGuid():N}");
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
    public async Task HandoffAsync_PersistsReadyRowsForReconciliationStaging()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store);
        var service = CreateService(store);
        var request = CreateRequest(["staging-ready"]);

        var result = await service.HandoffAsync(request);

        result.Accepted.Should().BeTrue();
        result.HandoffId.Should().NotBeNullOrWhiteSpace();
        result.AcceptedRecordCount.Should().Be(1);
        result.RejectedRecordCount.Should().Be(0);
        result.DuplicateRecordCount.Should().Be(0);
        result.Records.Should().ContainSingle(record =>
            record.StagingRecordId == "staging-ready" &&
            record.PromotionTarget == "reconciliation-staging" &&
            record.ApprovalEvidenceId == "approval-provider-handoff-1");
        var retained = await store.ListReconciliationHandoffRecordsAsync("connection-alpha");
        retained.Should().ContainSingle(record =>
            record.HandoffId == result.HandoffId &&
            record.StagingRecordId == "staging-ready" &&
            record.InternalAccountId == "internal-account-1");
    }

    [Fact]
    public async Task HandoffAsync_DoesNotPersistWhenAnyRequestedRowIsNotReady()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store);
        var service = CreateService(store);
        var request = CreateRequest(["staging-ready", "staging-review"]);

        var result = await service.HandoffAsync(request);

        result.Accepted.Should().BeFalse();
        result.HandoffId.Should().BeNull();
        result.AcceptedRecordCount.Should().Be(0);
        result.RejectedRecordCount.Should().Be(1);
        result.DuplicateRecordCount.Should().Be(0);
        result.Issues.Should().Contain(issue => issue.Code == "handoff.record.not-ready");
        (await store.ListReconciliationHandoffRecordsAsync("connection-alpha")).Should().BeEmpty();
    }

    [Fact]
    public async Task HandoffAsync_DoesNotPersistDuplicateStagingRecords()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store);
        var service = CreateService(store);
        var request = CreateRequest(["staging-ready"]);

        var first = await service.HandoffAsync(request);
        var second = await service.HandoffAsync(request);

        first.Accepted.Should().BeTrue();
        second.Accepted.Should().BeFalse();
        second.HandoffId.Should().BeNull();
        second.AcceptedRecordCount.Should().Be(0);
        second.RejectedRecordCount.Should().Be(1);
        second.DuplicateRecordCount.Should().Be(1);
        second.Issues.Should().Contain(issue => issue.Code == "handoff.record.already-handed-off");
        (await store.ListReconciliationHandoffRecordsAsync("connection-alpha")).Should().ContainSingle(record =>
            record.StagingRecordId == "staging-ready");
    }

    [Fact]
    public async Task HandoffAsync_ObservesCancellationBeforePersistingRecords()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store);
        var service = CreateService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.HandoffAsync(CreateRequest(["staging-ready"]), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await store.ListReconciliationHandoffRecordsAsync("connection-alpha")).Should().BeEmpty();
    }

    private static ProviderIntegrationReconciliationHandoffService CreateService(IProviderIntegrationManifestStore store)
    {
        var identityPreview = new ProviderIntegrationIdentityResolutionPreviewService(store);
        var readiness = new ProviderIntegrationPromotionReadinessService(identityPreview);
        return new ProviderIntegrationReconciliationHandoffService(store, readiness);
    }

    private static ProviderIntegrationReconciliationHandoffRequestDto CreateRequest(IReadOnlyList<string> stagingRecordIds)
        => new(
            "connection-alpha",
            stagingRecordIds,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T13:00:00Z"),
            "approval-provider-handoff-1",
            "Approved for reconciliation staging handoff.",
            RecentRunLimit: 10);

    private static async Task SeedStagingAsync(IProviderIntegrationManifestStore store)
    {
        await store.SaveManifestAsync(CreateManifest());
        await store.SaveConnectionAsync(CreateConnection());
        await store.SaveSyncRunAsync(new ProviderIntegrationSyncRunDto(
            "sync-run-handoff-1",
            "manifest-custodian-abc-v1",
            "connection-alpha",
            "custodian-abc",
            ProviderCapabilityKindDto.Positions,
            "positions",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:02:00Z"),
            ProviderIntegrationProcessingStatusDto.Validated,
            RecordsReceived: 2,
            RecordsAccepted: 2,
            RecordsQuarantined: 0,
            RawPayloadId: "payload-1",
            Issues: []));
        await store.SaveStagingRecordAsync(new IntegrationStagingRecordDto(
            "staging-ready",
            "sync-run-handoff-1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            "payload-1",
            "source-position-1",
            "connection-alpha|positions|source-position-1",
            Json("""{"providerAccountId":"A1","internalAccountId":"internal-account-1","security":{"internalSecurityId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"},"quantity":100,"asOf":"2026-06-16"}"""),
            ValidationWarnings: [],
            ProviderIntegrationProcessingStatusDto.Validated,
            DateTimeOffset.Parse("2026-06-16T12:02:00Z")));
        await store.SaveStagingRecordAsync(new IntegrationStagingRecordDto(
            "staging-review",
            "sync-run-handoff-1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            "payload-1",
            "source-position-2",
            "connection-alpha|positions|source-position-2",
            Json("""{"providerAccountId":"A2","security":{"internalSecurityId":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"},"quantity":50,"asOf":"2026-06-16"}"""),
            ValidationWarnings: [],
            ProviderIntegrationProcessingStatusDto.Validated,
            DateTimeOffset.Parse("2026-06-16T12:03:00Z")));
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
            ChangeReason: "Reconciliation handoff test manifest");

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
