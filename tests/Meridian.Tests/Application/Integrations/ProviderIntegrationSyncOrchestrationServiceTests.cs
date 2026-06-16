using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationSyncOrchestrationServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationSyncOrchestrationServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_sync_orchestration_test_{Guid.NewGuid():N}");
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
    public async Task RunDueAsync_StartsDueRestCapabilityAndStagesAcceptedRecords()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = ActiveCustodianManifest();
        var connection = ActiveConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var transport = new RecordingTransport(new ProviderIntegrationHttpResponse(
            200,
            new Dictionary<string, string>(),
            """
            {
              "positions": [
                {
                  "account_id": "A-100",
                  "cusip": "9128285M8",
                  "quantity": "100",
                  "currency": "usd",
                  "as_of_date": "2026-06-16",
                  "position_id": "POS-1"
                }
              ]
            }
            """));
        var service = CreateService(store, transport);

        var result = await service.RunDueAsync(CreateRequest(connection));

        result.StartedCount.Should().Be(1);
        result.SkippedCount.Should().Be(0);
        var item = result.Items.Should().ContainSingle().Subject;
        item.Started.Should().BeTrue();
        item.Skipped.Should().BeFalse();
        item.Reason.Should().Be("started");
        item.SyncRunId.Should().StartWith("run-due-");
        item.DryRunResult.Should().NotBeNull();
        item.DryRunResult!.RecordsAccepted.Should().Be(1);
        transport.Requests.Should().ContainSingle()
            .Which.Path.Should().Be("/v1/accounts/A-100/positions");

        var staged = await store.ListStagingRecordsAsync(item.SyncRunId!);
        staged.Should().ContainSingle()
            .Which.MappedRecord.GetProperty("security").GetProperty("cusip").GetString().Should().Be("9128285M8");
    }

    [Fact]
    public async Task RunDueAsync_SkipsNotDueCapabilityWithoutCallingTransport()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = ActiveCustodianManifest();
        var connection = ActiveConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        await store.SaveSyncRunAsync(CreateSyncRun(manifest, connection, "sync-recent"));
        var transport = new RecordingTransport();
        var service = CreateService(store, transport);

        var result = await service.RunDueAsync(CreateRequest(connection));

        result.StartedCount.Should().Be(0);
        result.SkippedCount.Should().Be(1);
        result.Items.Should().ContainSingle()
            .Which.Should().Match<ProviderIntegrationRunDueSyncItemResultDto>(item =>
                item.Skipped &&
                !item.Started &&
                item.Reason == "not-due" &&
                item.DryRunResult == null);
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RunDueAsync_ReturnsRuntimeBlockedWhenEndpointParametersAreMissing()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = ActiveCustodianManifest();
        var connection = ActiveConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var transport = new RecordingTransport();
        var service = CreateService(store, transport);

        var result = await service.RunDueAsync(CreateRequest(connection, includePathParameters: false));

        result.StartedCount.Should().Be(0);
        result.SkippedCount.Should().Be(1);
        var item = result.Items.Should().ContainSingle().Subject;
        item.Reason.Should().Be("runtime-blocked");
        item.Issues.Should().ContainSingle(issue =>
            issue.Code == "sync-run.runtime-blocked" &&
            issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RunDueAsync_ObservesCancellationBeforePlanning()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = CreateService(store, new RecordingTransport());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.RunDueAsync(
            new ProviderIntegrationRunDueSyncRequestDto(
                "connection-alpha",
                DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
                "operator@example.com",
                MaxPages: 1,
                new Dictionary<string, IReadOnlyDictionary<string, string>>(),
                new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ProviderIntegrationSyncOrchestrationService CreateService(
        IProviderIntegrationManifestStore store,
        IProviderIntegrationHttpTransport transport)
        => new(
            new ProviderIntegrationSyncPlanningService(store),
            new ProviderIntegrationRestDryRunService(store, transport));

    private static ProviderIntegrationRunDueSyncRequestDto CreateRequest(
        ProviderConnectionDto connection,
        bool includePathParameters = true)
        => new(
            connection.ConnectionId,
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            "operator@example.com",
            MaxPages: 1,
            includePathParameters
                ? new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    [ProviderCapabilityKindDto.Positions.ToString()] = new Dictionary<string, string>
                    {
                        ["accountId"] = "A-100"
                    }
                }
                : new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>());

    private static ProviderIntegrationManifestDto ActiveCustodianManifest()
        => new ProviderIntegrationTemplateCatalog().GetManifest("template-custodian-positions-v1")! with
        {
            State = ProviderIntegrationActivationStateDto.Active,
            ApprovedBy = "approver@example.com",
            ApprovedAt = DateTimeOffset.Parse("2026-06-16T09:00:00Z")
        };

    private static ProviderConnectionDto ActiveConnection(ProviderIntegrationManifestDto manifest)
        => new(
            "connection-sync-run-due",
            manifest.ProviderId,
            manifest.ManifestId,
            "Custodian Run Due",
            "production",
            ProviderIntegrationActivationStateDto.Active,
            "vault://provider-credentials/custodian-run-due",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T09:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T09:05:00Z"),
            "approval-evidence-1");

    private static ProviderIntegrationSyncRunDto CreateSyncRun(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection,
        string syncRunId)
        => new(
            syncRunId,
            manifest.ManifestId,
            connection.ConnectionId,
            manifest.ProviderId,
            ProviderCapabilityKindDto.Positions,
            "positions",
            DateTimeOffset.Parse("2026-06-16T10:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T10:01:00Z"),
            ProviderIntegrationProcessingStatusDto.Validated,
            RecordsReceived: 1,
            RecordsAccepted: 1,
            RecordsQuarantined: 0,
            RawPayloadId: $"payload-{syncRunId}",
            Issues: []);

    private sealed class RecordingTransport : IProviderIntegrationHttpTransport
    {
        private readonly Queue<ProviderIntegrationHttpResponse> responses;

        public RecordingTransport(params ProviderIntegrationHttpResponse[] responses)
        {
            this.responses = new Queue<ProviderIntegrationHttpResponse>(responses);
        }

        public List<ProviderIntegrationHttpRequest> Requests { get; } = [];

        public Task<ProviderIntegrationHttpResponse> SendAsync(
            ProviderIntegrationHttpRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (responses.Count == 0)
            {
                throw new InvalidOperationException("No recorded provider response is available.");
            }

            return Task.FromResult(responses.Dequeue());
        }
    }
}
