using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationMonitoringServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationMonitoringServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_provider_monitoring_test_{Guid.NewGuid():N}");
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
    public async Task GetConnectionMonitorAsync_ReturnsRecentRunsWithDurableEvidenceCounts()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest();
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        await SaveRunEvidenceAsync(
            store,
            CreateSyncRun(
                "sync-run-old",
                manifest,
                connection,
                DateTimeOffset.Parse("2026-06-16T10:00:00Z"),
                ProviderIntegrationProcessingStatusDto.Validated,
                received: 2,
                accepted: 2,
                quarantined: 0,
                issues: []),
            stagingCount: 2,
            quarantineCount: 0);
        await SaveRunEvidenceAsync(
            store,
            CreateSyncRun(
                "sync-run-new",
                manifest,
                connection,
                DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
                ProviderIntegrationProcessingStatusDto.Quarantined,
                received: 3,
                accepted: 1,
                quarantined: 2,
                issues:
                [
                    new ValidationIssueDto(
                        "required.missing",
                        ProviderIntegrationIssueSeverityDto.Critical,
                        "Security identifier is required.",
                        "security.cusip",
                        "Map CUSIP before activation."),
                    new ValidationIssueDto(
                        "mapping.review",
                        ProviderIntegrationIssueSeverityDto.Warning,
                        "Mapping confidence needs review.",
                        "marketValue.amount",
                        "Approve or remap this field.")
                ]),
            stagingCount: 1,
            quarantineCount: 2);
        var service = new ProviderIntegrationMonitoringService(store);

        var monitor = await service.GetConnectionMonitorAsync(connection.ConnectionId, recentRunLimit: 2);

        monitor.ConnectionId.Should().Be(connection.ConnectionId);
        monitor.DisplayName.Should().Be(manifest.DisplayName);
        monitor.EnabledCapabilities.Should().BeEquivalentTo(connection.EnabledCapabilities);
        monitor.RecentSyncRuns.Should().HaveCount(2);
        monitor.LastSyncRun.Should().NotBeNull();
        monitor.LastSyncRun!.SyncRunId.Should().Be("sync-run-new");
        monitor.LastSyncRun.CriticalIssueCount.Should().Be(1);
        monitor.LastSyncRun.WarningIssueCount.Should().Be(1);
        monitor.LastSyncRun.DurableStagingRecordCount.Should().Be(1);
        monitor.LastSyncRun.DurableQuarantinedRecordCount.Should().Be(2);
        monitor.RecentRecordsReceived.Should().Be(5);
        monitor.RecentRecordsAccepted.Should().Be(3);
        monitor.RecentRecordsQuarantined.Should().Be(2);
        monitor.DurableStagingRecordCount.Should().Be(3);
        monitor.DurableQuarantinedRecordCount.Should().Be(2);
        monitor.HasCriticalIssues.Should().BeTrue();

        var history = await service.GetConnectionSyncRunsAsync(connection.ConnectionId, recentRunLimit: 1);

        history.ConnectionId.Should().Be(connection.ConnectionId);
        history.TotalSyncRuns.Should().Be(2);
        history.ReturnedSyncRuns.Should().Be(1);
        history.LatestStartedAt.Should().Be(DateTimeOffset.Parse("2026-06-16T12:00:00Z"));
        history.SyncRuns.Should().ContainSingle()
            .Which.Should().Match<ProviderIntegrationSyncRunEvidenceDto>(run =>
                run.SyncRunId == "sync-run-new" &&
                run.DurableStagingRecordCount == 1 &&
                run.DurableQuarantinedRecordCount == 2 &&
                run.CriticalIssueCount == 1 &&
                run.WarningIssueCount == 1);
    }

    [Fact]
    public async Task GetConnectionMonitorAsync_ReturnsEmptyRunEvidenceForNewConnection()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest();
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationMonitoringService(store);

        var monitor = await service.GetConnectionMonitorAsync(connection.ConnectionId);

        monitor.LastSyncRun.Should().BeNull();
        monitor.RecentSyncRuns.Should().BeEmpty();
        monitor.RecentRecordsReceived.Should().Be(0);
        monitor.DurableStagingRecordCount.Should().Be(0);
        monitor.DurableQuarantinedRecordCount.Should().Be(0);
        monitor.HasCriticalIssues.Should().BeFalse();
    }

    [Fact]
    public async Task GetConnectionMonitorAsync_ObservesCancellationBeforeReadingStore()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationMonitoringService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.GetConnectionMonitorAsync("connection-alpha", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetConnectionSyncRunsAsync_ReturnsMissingConnectionFailure()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationMonitoringService(store);

        var act = () => service.GetConnectionSyncRunsAsync("missing-connection");

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("Provider integration connection 'missing-connection' was not found.");
    }

    [Fact]
    public async Task GetConnectionSyncRunsAsync_ObservesCancellationBeforeReadingStore()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationMonitoringService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.GetConnectionSyncRunsAsync("connection-alpha", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static async Task SaveRunEvidenceAsync(
        IProviderIntegrationManifestStore store,
        ProviderIntegrationSyncRunDto syncRun,
        int stagingCount,
        int quarantineCount)
    {
        await store.SaveSyncRunAsync(syncRun);
        for (var index = 0; index < stagingCount; index++)
        {
            await store.SaveStagingRecordAsync(
                new IntegrationStagingRecordDto(
                    $"staging-{syncRun.SyncRunId}-{index}",
                    syncRun.SyncRunId,
                    syncRun.ConnectionId,
                    syncRun.Capability,
                    syncRun.RawPayloadId ?? "payload-missing",
                    $"source-{index}",
                    $"{syncRun.ConnectionId}:{syncRun.Capability}:source-{index}",
                    Json("""{"providerAccountId":"A-100","quantity":100,"asOf":"2026-06-16"}"""),
                    [],
                    ProviderIntegrationProcessingStatusDto.Validated,
                    syncRun.StartedAt));
        }

        for (var index = 0; index < quarantineCount; index++)
        {
            await store.SaveQuarantinedRecordAsync(
                new QuarantinedRecordDto(
                    $"quarantine-{syncRun.SyncRunId}-{index}",
                    syncRun.SyncRunId,
                    syncRun.ConnectionId,
                    syncRun.Capability,
                    Json("""{"account_id":"A-100"}"""),
                    Json("""{"providerAccountId":"A-100"}"""),
                    syncRun.Issues,
                    ProviderIntegrationProcessingStatusDto.Quarantined,
                    syncRun.StartedAt));
        }
    }

    private static ProviderIntegrationSyncRunDto CreateSyncRun(
        string syncRunId,
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection,
        DateTimeOffset startedAt,
        ProviderIntegrationProcessingStatusDto status,
        int received,
        int accepted,
        int quarantined,
        IReadOnlyList<ValidationIssueDto> issues)
        => new(
            syncRunId,
            manifest.ManifestId,
            connection.ConnectionId,
            manifest.ProviderId,
            ProviderCapabilityKindDto.Positions,
            "positions",
            startedAt,
            startedAt.AddMinutes(2),
            status,
            received,
            accepted,
            quarantined,
            $"payload-{syncRunId}",
            issues);

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
            ProviderIntegrationActivationStateDto.DryRunPassed,
            "ops@example.com",
            DateTimeOffset.Parse("2026-06-16T09:00:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Monitoring test manifest");

    private static ProviderConnectionDto CreateConnection(ProviderIntegrationManifestDto manifest)
        => new(
            "connection-alpha",
            manifest.ProviderId,
            manifest.ManifestId,
            "General Account",
            manifest.Environment,
            ProviderIntegrationActivationStateDto.DryRunPassed,
            "vault://provider-credentials/custodian-abc/general-account",
            [ProviderCapabilityKindDto.Positions],
            "ops@example.com",
            DateTimeOffset.Parse("2026-06-16T09:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T09:05:00Z"),
            ApprovalEvidenceId: null);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
