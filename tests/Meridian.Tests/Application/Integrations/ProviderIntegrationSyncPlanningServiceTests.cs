using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationSyncPlanningServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationSyncPlanningServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_sync_plan_test_{Guid.NewGuid():N}");
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
    public async Task PlanAsync_ReturnsDueWhenActiveDailyConnectionHasNoSuccessfulRun()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = ActiveCustodianManifest();
        var connection = ActiveConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationSyncPlanningService(store);

        var plan = await service.PlanAsync(Request(connection));

        plan.DueCount.Should().Be(1);
        plan.BlockedCount.Should().Be(0);
        plan.Items.Should().ContainSingle()
            .Which.Should().Match<ProviderIntegrationSyncPlanItemDto>(item =>
                item.Capability == ProviderCapabilityKindDto.Positions &&
                item.EndpointKey == "positions" &&
                item.IsDue &&
                !item.IsBlocked &&
                item.Reason == "due");
    }

    [Fact]
    public async Task PlanAsync_ReturnsNotDueWhenLastSuccessfulSyncIsInsideInterval()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = ActiveCustodianManifest();
        var connection = ActiveConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        await store.SaveSyncRunAsync(CreateSyncRun(
            manifest,
            connection,
            "sync-recent",
            DateTimeOffset.Parse("2026-06-16T10:00:00Z"),
            ProviderIntegrationProcessingStatusDto.Validated));
        var service = new ProviderIntegrationSyncPlanningService(store);

        var plan = await service.PlanAsync(Request(connection, "2026-06-16T12:00:00Z"));

        var item = plan.Items.Should().ContainSingle().Subject;
        item.IsDue.Should().BeFalse();
        item.IsBlocked.Should().BeFalse();
        item.Reason.Should().Be("not-due");
        item.LastSuccessfulSyncAt.Should().Be(DateTimeOffset.Parse("2026-06-16T10:02:00Z"));
        item.NextEligibleSyncAt.Should().Be(DateTimeOffset.Parse("2026-06-17T10:02:00Z"));
    }

    [Fact]
    public async Task PlanAsync_BlocksScheduledSyncUntilManifestAndConnectionAreActive()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = ActiveCustodianManifest() with { State = ProviderIntegrationActivationStateDto.DryRunPassed };
        var connection = ActiveConnection(manifest) with { State = ProviderIntegrationActivationStateDto.PendingApproval };
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationSyncPlanningService(store);

        var plan = await service.PlanAsync(Request(connection));

        plan.DueCount.Should().Be(0);
        plan.BlockedCount.Should().Be(1);
        plan.Items.Should().ContainSingle()
            .Which.Issues.Should().ContainSingle(issue =>
                issue.Code == "sync-plan.activation-required" &&
                issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
    }

    [Fact]
    public async Task PlanAsync_ReturnsManualOnlyForManualUploadSchedule()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-manual-csv-upload-v1")! with
        {
            State = ProviderIntegrationActivationStateDto.Active
        };
        var connection = new ProviderConnectionDto(
            "connection-manual-sync-plan",
            manifest.ProviderId,
            manifest.ManifestId,
            "Manual CSV Upload",
            "production",
            ProviderIntegrationActivationStateDto.Active,
            string.Empty,
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T09:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T09:00:00Z"),
            "approval-evidence-1");
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationSyncPlanningService(store);

        var plan = await service.PlanAsync(Request(connection));

        var item = plan.Items.Should().ContainSingle().Subject;
        item.IsDue.Should().BeFalse();
        item.IsBlocked.Should().BeFalse();
        item.Reason.Should().Be("manual-only");
        item.EndpointKey.Should().BeNull();
    }

    [Fact]
    public async Task PlanAsync_ObservesCancellationBeforeReadingStore()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationSyncPlanningService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.PlanAsync(
            new ProviderIntegrationSyncPlanRequestDto(
                "connection-alpha",
                DateTimeOffset.Parse("2026-06-16T12:00:00Z")),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ProviderIntegrationManifestDto ActiveCustodianManifest()
        => new ProviderIntegrationTemplateCatalog().GetManifest("template-custodian-positions-v1")! with
        {
            State = ProviderIntegrationActivationStateDto.Active,
            ApprovedBy = "approver@example.com",
            ApprovedAt = DateTimeOffset.Parse("2026-06-16T09:00:00Z")
        };

    private static ProviderConnectionDto ActiveConnection(ProviderIntegrationManifestDto manifest)
        => new(
            "connection-sync-plan",
            manifest.ProviderId,
            manifest.ManifestId,
            "Custodian Sync Plan",
            "production",
            ProviderIntegrationActivationStateDto.Active,
            "vault://provider-credentials/custodian-sync-plan",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T09:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T09:05:00Z"),
            "approval-evidence-1");

    private static ProviderIntegrationSyncPlanRequestDto Request(
        ProviderConnectionDto connection,
        string evaluatedAt = "2026-06-16T12:00:00Z")
        => new(connection.ConnectionId, DateTimeOffset.Parse(evaluatedAt));

    private static ProviderIntegrationSyncRunDto CreateSyncRun(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection,
        string syncRunId,
        DateTimeOffset startedAt,
        ProviderIntegrationProcessingStatusDto status)
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
            RecordsReceived: 10,
            RecordsAccepted: 10,
            RecordsQuarantined: 0,
            RawPayloadId: $"payload-{syncRunId}",
            Issues: []);
}
