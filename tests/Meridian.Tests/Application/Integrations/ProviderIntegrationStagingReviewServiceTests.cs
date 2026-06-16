using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationStagingReviewServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationStagingReviewServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_provider_staging_review_test_{Guid.NewGuid():N}");
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
    public async Task GetReviewAsync_ReturnsAcceptedRecordsForRecentConnectionRuns()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store, connectionId: "connection-alpha");
        var service = new ProviderIntegrationStagingReviewService(store);

        var review = await service.GetReviewAsync("connection-alpha");

        review.ConnectionId.Should().Be("connection-alpha");
        review.SyncRunIds.Should().ContainSingle().Which.Should().Be("sync-run-staging-1");
        review.TotalStagedRecords.Should().Be(2);
        review.ReadyForReconciliationCount.Should().Be(1);
        review.WarningRecordCount.Should().Be(1);
        review.CapabilitySummaries.Should().ContainSingle(summary =>
            summary.Capability == ProviderCapabilityKindDto.Positions &&
            summary.RecordCount == 2 &&
            summary.WarningCount == 1);
        review.WarningGroups.Should().ContainSingle(group =>
            group.IssueCode == "position.market-value.currency-defaulted" &&
            group.RecordCount == 1);
        review.Records.Should().BeInDescendingOrder(record => record.CreatedAt);
    }

    [Fact]
    public async Task GetReviewAsync_ObservesCancellationBeforeReadingRuns()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedStagingAsync(store, connectionId: "connection-alpha");
        var service = new ProviderIntegrationStagingReviewService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.GetReviewAsync("connection-alpha", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static async Task SeedStagingAsync(
        IProviderIntegrationManifestStore store,
        string connectionId)
    {
        await store.SaveManifestAsync(CreateManifest()).ConfigureAwait(false);
        await store.SaveConnectionAsync(CreateConnection(connectionId)).ConfigureAwait(false);
        await store.SaveSyncRunAsync(new ProviderIntegrationSyncRunDto(
            "sync-run-staging-1",
            "manifest-custodian-abc-v1",
            connectionId,
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
            Issues: [])).ConfigureAwait(false);
        await store.SaveStagingRecordAsync(new IntegrationStagingRecordDto(
            "staging-ready",
            "sync-run-staging-1",
            connectionId,
            ProviderCapabilityKindDto.Positions,
            "payload-1",
            "source-position-1",
            "connection-alpha|positions|source-position-1",
            Json("""{"providerAccountId":"A1","security":{"cusip":"123456789"},"quantity":100,"asOf":"2026-06-16"}"""),
            ValidationWarnings: [],
            ProviderIntegrationProcessingStatusDto.Validated,
            DateTimeOffset.Parse("2026-06-16T12:02:00Z"))).ConfigureAwait(false);
        await store.SaveStagingRecordAsync(new IntegrationStagingRecordDto(
            "staging-warning",
            "sync-run-staging-1",
            connectionId,
            ProviderCapabilityKindDto.Positions,
            "payload-1",
            "source-position-2",
            "connection-alpha|positions|source-position-2",
            Json("""{"providerAccountId":"A2","security":{"cusip":"987654321"},"quantity":50,"marketValue":{"amount":1000,"currency":"USD"},"asOf":"2026-06-16"}"""),
            ValidationWarnings:
            [
                new ValidationIssueDto(
                    "position.market-value.currency-defaulted",
                    ProviderIntegrationIssueSeverityDto.Warning,
                    "Market value currency was defaulted from connection settings.",
                    "marketValue.currency",
                    "Confirm the account base currency before reconciliation.")
            ],
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
            ChangeReason: "Staging review test manifest");

    private static ProviderConnectionDto CreateConnection(string connectionId)
        => new(
            connectionId,
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
