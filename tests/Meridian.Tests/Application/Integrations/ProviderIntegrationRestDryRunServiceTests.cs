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

        var syncRun = await store.GetSyncRunAsync(result.SyncRunId);
        syncRun.Should().NotBeNull();
        syncRun!.EndpointKey.Should().Be("positions");
        syncRun.RecordsReceived.Should().Be(2);
        syncRun.RecordsAccepted.Should().Be(2);
        syncRun.RawPayloadId.Should().Be(result.RawPayloadId);
        (await store.ListSyncRunsAsync(connection.ConnectionId)).Should().ContainSingle()
            .Which.SyncRunId.Should().Be(result.SyncRunId);

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
        var syncRun = await store.GetSyncRunAsync(result.SyncRunId);
        syncRun.Should().NotBeNull();
        syncRun!.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
        syncRun.RecordsQuarantined.Should().Be(1);
        (await store.ListStagingRecordsAsync(result.SyncRunId)).Should().BeEmpty();
        var quarantine = await store.ListQuarantinedRecordsAsync(result.SyncRunId);
        quarantine.Should().ContainSingle()
            .Which.ValidationErrors.Should().Contain(error => error.TargetField == "security.cusip");
    }

    [Fact]
    public async Task RunRestDryRunAsync_StagesBrokerageTransactionsWithProviderTransactionLineage()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-brokerage-transactions-v1")!;
        var connection = CreateConnection(manifest, ProviderCapabilityKindDto.Transactions);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var transport = new RecordingTransport(
            new ProviderIntegrationHttpResponse(
                200,
                new Dictionary<string, string>(),
                """
                {
                  "transactions": [
                    {
                      "transaction_id": "TX-REST-1",
                      "account_id": "A-100",
                      "type": "SELL",
                      "trade_date": "2026-06-15",
                      "settlement_date": "2026-06-17",
                      "posting_date": "2026-06-16",
                      "quantity": "10",
                      "price": "99.50",
                      "amount": "995.00",
                      "currency": "usd",
                      "cusip": "9128285M8",
                      "description": "Treasury sale"
                    }
                  ]
                }
                """));
        var service = new ProviderIntegrationRestDryRunService(store, transport);

        var result = await service.RunRestDryRunAsync(CreateTransactionRequest(manifest, connection));

        result.RecordsReceived.Should().Be(1);
        result.RecordsAccepted.Should().Be(1);
        result.RecordsQuarantined.Should().Be(0);
        var staged = await store.ListStagingRecordsAsync(result.SyncRunId);
        staged.Should().ContainSingle();
        var mapped = staged.Single().MappedRecord;
        mapped.GetProperty("providerTransactionId").GetString().Should().Be("TX-REST-1");
        mapped.GetProperty("transactionType").GetString().Should().Be("sell");
        mapped.GetProperty("amount").GetProperty("amount").GetDecimal().Should().Be(-995.00m);
        mapped.GetProperty("amount").GetProperty("currency").GetString().Should().Be("USD");
        staged.Single().SourceRecordId.Should().Be("TX-REST-1");
        staged.Single().DedupeKey.Should().Contain("TX-REST-1");
    }

    [Fact]
    public async Task RunRestDryRunAsync_QuarantinesDuplicateProviderTransactionIdentity()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-brokerage-transactions-v1")!;
        var connection = CreateConnection(manifest, ProviderCapabilityKindDto.Transactions);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var transport = new RecordingTransport(
            new ProviderIntegrationHttpResponse(
                200,
                new Dictionary<string, string>(),
                """
                {
                  "transactions": [
                    {
                      "transaction_id": "TX-REST-DUP",
                      "account_id": "A-100",
                      "type": "BUY",
                      "posting_date": "2026-06-16",
                      "amount": "995.00",
                      "currency": "usd"
                    },
                    {
                      "transaction_id": "TX-REST-DUP",
                      "account_id": "A-100",
                      "type": "BUY",
                      "posting_date": "2026-06-16",
                      "amount": "995.00",
                      "currency": "usd"
                    }
                  ]
                }
                """));
        var service = new ProviderIntegrationRestDryRunService(store, transport);

        var result = await service.RunRestDryRunAsync(CreateTransactionRequest(manifest, connection));

        result.RecordsReceived.Should().Be(2);
        result.RecordsAccepted.Should().Be(1);
        result.RecordsQuarantined.Should().Be(1);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
        result.Issues.Should().Contain(issue =>
            issue.Code == "duplicate.dedupe-key" &&
            issue.TargetField == "providerTransactionId");
        (await store.ListStagingRecordsAsync(result.SyncRunId)).Should().ContainSingle()
            .Which.SourceRecordId.Should().Be("TX-REST-DUP");
        (await store.ListQuarantinedRecordsAsync(result.SyncRunId)).Should().ContainSingle()
            .Which.ValidationErrors.Should().Contain(error => error.Code == "duplicate.dedupe-key");
    }

    [Fact]
    public async Task RunRestDryRunAsync_QuarantinesInvalidMoneyCurrency()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-brokerage-transactions-v1")!;
        var connection = CreateConnection(manifest, ProviderCapabilityKindDto.Transactions);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var transport = new RecordingTransport(
            new ProviderIntegrationHttpResponse(
                200,
                new Dictionary<string, string>(),
                """
                {
                  "transactions": [
                    {
                      "transaction_id": "TX-REST-BAD-CCY",
                      "account_id": "A-100",
                      "type": "BUY",
                      "posting_date": "2026-06-16",
                      "amount": "995.00",
                      "currency": "US"
                    }
                  ]
                }
                """));
        var service = new ProviderIntegrationRestDryRunService(store, transport);

        var result = await service.RunRestDryRunAsync(CreateTransactionRequest(manifest, connection));

        result.RecordsAccepted.Should().Be(0);
        result.RecordsQuarantined.Should().Be(1);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
        result.Issues.Should().Contain(issue =>
            issue.Code == "currency.invalid" &&
            issue.TargetField == "amount.currency");
        (await store.ListStagingRecordsAsync(result.SyncRunId)).Should().BeEmpty();
        (await store.ListQuarantinedRecordsAsync(result.SyncRunId)).Should().ContainSingle()
            .Which.ValidationErrors.Should().Contain(error => error.Code == "currency.invalid");
    }

    [Fact]
    public async Task RunRestDryRunAsync_QuarantinesPositionWhenMarketValueToleranceIsExceeded()
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
                      "price": "10.00",
                      "market_value": "999.00",
                      "currency": "usd",
                      "as_of_date": "2026-06-16",
                      "position_id": "POS-BAD-MV"
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
            issue.Code == "market-value.tolerance.exceeded" &&
            issue.TargetField == "marketValue.amount");
        (await store.ListStagingRecordsAsync(result.SyncRunId)).Should().BeEmpty();
        (await store.ListQuarantinedRecordsAsync(result.SyncRunId)).Should().ContainSingle()
            .Which.ValidationErrors.Should().Contain(error => error.Code == "market-value.tolerance.exceeded");
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
        (await store.GetSyncRunAsync("sync-run-rest-1")).Should().BeNull();
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

    private static ProviderIntegrationRestDryRunRequestDto CreateTransactionRequest(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection)
        => new(
            "sync-run-rest-transactions-1",
            manifest.ManifestId,
            connection.ConnectionId,
            ProviderCapabilityKindDto.Transactions,
            "transactions",
            new Dictionary<string, string> { ["accountId"] = "A-100" },
            new Dictionary<string, string> { ["fromDate"] = "2026-06-16" },
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            MaxPages: 5);

    private static ProviderConnectionDto CreateConnection(
        ProviderIntegrationManifestDto manifest,
        ProviderCapabilityKindDto capability = ProviderCapabilityKindDto.Positions)
        => new(
            $"connection-{manifest.ProviderId}",
            manifest.ProviderId,
            manifest.ManifestId,
            $"{manifest.DisplayName} Test",
            "test",
            ProviderIntegrationActivationStateDto.Draft,
            $"vault://provider-credentials/{manifest.ProviderId}/test",
            [capability],
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
