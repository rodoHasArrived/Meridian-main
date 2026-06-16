using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationRestDryRunServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationRestDryRunServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_rest_dry_run_test_{Guid.NewGuid():N}");
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
    public async Task RunRestDryRunAsync_PaginatesRetainsRawPayloadsAndStagesAcceptedRows()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-custodian-positions-v1")!;
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var transport = new RecordingTransport(
            new ProviderIntegrationHttpResponse(
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
                  ],
                  "nextCursor": "cursor-2"
                }
                """),
            new ProviderIntegrationHttpResponse(
                200,
                new Dictionary<string, string>(),
                """
                {
                  "positions": [
                    {
                      "account_id": "A-100",
                      "cusip": "3133EP3T5",
                      "quantity": "250",
                      "currency": "usd",
                      "as_of_date": "2026-06-16",
                      "position_id": "POS-2"
                    }
                  ]
                }
                """));
        var service = new ProviderIntegrationRestDryRunService(store, transport);

        var result = await service.RunRestDryRunAsync(CreateRequest(manifest, connection));

        result.RecordsReceived.Should().Be(2);
        result.RecordsAccepted.Should().Be(2);
        result.RecordsQuarantined.Should().Be(0);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Validated);
        result.Issues.Should().BeEmpty();
        result.RawPayloadId.Should().NotBeNullOrWhiteSpace();

        transport.Requests.Should().HaveCount(2);
        transport.Requests[0].Path.Should().Be("/v1/accounts/A-100/positions");
        transport.Requests[0].Query.Should().NotContainKey("cursor");
        transport.Requests[1].Query.Should().Contain("cursor", "cursor-2");

        var firstRawPayload = await store.GetRawPayloadAsync(result.SyncRunId, result.RawPayloadId);
        firstRawPayload.Should().NotBeNull();
        firstRawPayload!.RawPayload.GetProperty("positions")[0].GetProperty("position_id").GetString().Should().Be("POS-1");

        var staged = await store.ListStagingRecordsAsync(result.SyncRunId);
        staged.Should().HaveCount(2);
        staged.Select(record => record.SourceRecordId).Should().BeEquivalentTo("POS-1", "POS-2");
        staged.Select(record => record.MappedRecord.GetProperty("marketValue").GetProperty("currency").GetString())
            .Should()
            .OnlyContain(currency => currency == "USD");
    }

    [Fact]
    public async Task RunRestDryRunAsync_QuarantinesRecordsMissingRequiredFields()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-custodian-positions-v1")!;
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var transport = new RecordingTransport(
            new ProviderIntegrationHttpResponse(
                200,
                new Dictionary<string, string>(),
                """
                {
                  "positions": [
                    {
                      "account_id": "A-100",
                      "quantity": "100",
                      "currency": "usd",
                      "as_of_date": "2026-06-16",
                      "position_id": "POS-1"
                    }
                  ]
                }
                """));
        var service = new ProviderIntegrationRestDryRunService(store, transport);

        var result = await service.RunRestDryRunAsync(CreateRequest(manifest, connection));

        result.RecordsReceived.Should().Be(1);
        result.RecordsAccepted.Should().Be(0);
        result.RecordsQuarantined.Should().Be(1);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
        result.Issues.Should().Contain(issue =>
            issue.Code == "required.missing" &&
            issue.TargetField == "security.cusip");
        (await store.ListStagingRecordsAsync(result.SyncRunId)).Should().BeEmpty();
        var quarantine = await store.ListQuarantinedRecordsAsync(result.SyncRunId);
        quarantine.Should().ContainSingle()
            .Which.ValidationErrors.Should().Contain(error => error.TargetField == "security.cusip");
    }

    [Fact]
    public async Task RunRestDryRunAsync_ObservesCancellationBeforeTransportAndEvidence()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-custodian-positions-v1")!;
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var transport = new RecordingTransport(
            new ProviderIntegrationHttpResponse(200, new Dictionary<string, string>(), """{"positions":[]}"""));
        var service = new ProviderIntegrationRestDryRunService(store, transport);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.RunRestDryRunAsync(CreateRequest(manifest, connection), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        transport.Requests.Should().BeEmpty();
        (await store.ListStagingRecordsAsync("sync-run-rest-1")).Should().BeEmpty();
        (await store.ListQuarantinedRecordsAsync("sync-run-rest-1")).Should().BeEmpty();
    }

    private static ProviderIntegrationRestDryRunRequestDto CreateRequest(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection)
        => new(
            "sync-run-rest-1",
            manifest.ManifestId,
            connection.ConnectionId,
            ProviderCapabilityKindDto.Positions,
            "positions",
            new Dictionary<string, string> { ["accountId"] = "A-100" },
            new Dictionary<string, string> { ["asOf"] = "2026-06-16" },
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            MaxPages: 5);

    private static ProviderConnectionDto CreateConnection(ProviderIntegrationManifestDto manifest)
        => new(
            "connection-custodian-positions",
            manifest.ProviderId,
            manifest.ManifestId,
            "Custodian Positions Test",
            "test",
            ProviderIntegrationActivationStateDto.Draft,
            "vault://provider-credentials/custodian-positions/test",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovalEvidenceId: null);

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
