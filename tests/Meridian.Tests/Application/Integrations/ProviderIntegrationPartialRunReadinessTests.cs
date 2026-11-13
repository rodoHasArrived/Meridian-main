using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationPartialRunReadinessTests : IDisposable
{
    private const string ConnectionId = "connection-partial-source";
    private const string RunId = "sync-partial-source";
    private const string IncompleteCode = "source.run.incomplete";
    private static readonly DateTimeOffset RequestedAt = DateTimeOffset.Parse("2026-06-16T12:00:00Z");
    private readonly string testRoot = Path.Combine(Path.GetTempPath(), $"mdc_partial_source_test_{Guid.NewGuid():N}");

    public ProviderIntegrationPartialRunReadinessTests()
    {
        Directory.CreateDirectory(testRoot);
    }

    [Theory]
    [InlineData(206, false)]
    [InlineData(200, true)]
    public async Task LaterPartialPage_RetainsRowsForReviewAndRefusesDownstreamHandoff(int statusCode, bool contentRange)
    {
        var transport = new TwoPageTransport(new ProviderIntegrationHttpResponse(
            statusCode,
            contentRange ? new Dictionary<string, string> { ["Content-Range"] = "items 0-0/2" } : new Dictionary<string, string>(),
            """{"positions":[]}"""));
        var (store, request) = await CreateSourceAsync();

        var result = await new ProviderIntegrationRestDryRunService(store, transport).RunRestDryRunAsync(request);

        transport.RequestCount.Should().Be(2);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
        result.RecordsAccepted.Should().Be(1);
        result.Issues.Should().Contain(issue => issue.Code == "http.response.incomplete");
        await AssertDownstreamBlockedAfterRestartAsync();
    }

    [Fact]
    public async Task PageLimitWithRemainingCursor_RefusesDownstreamHandoff()
    {
        var transport = new TwoPageTransport(new ProviderIntegrationHttpResponse(200, new Dictionary<string, string>(), """{"positions":[]}"""));
        var (store, request) = await CreateSourceAsync();

        var result = await new ProviderIntegrationRestDryRunService(store, transport).RunRestDryRunAsync(request with { MaxPages = 1 });

        transport.RequestCount.Should().Be(1);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
        result.RecordsAccepted.Should().Be(1);
        result.Issues.Should().Contain(issue => issue.Code == "http.response.incomplete");
        await AssertDownstreamBlockedAfterRestartAsync();
    }

    [Fact]
    public async Task InterruptedLaterPage_LeavesReceivedRunAndRefusesDownstreamHandoff()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new TwoPageTransport(lastResponse: null, interruption: cancellation);
        var (store, request) = await CreateSourceAsync();

        var interrupted = () => new ProviderIntegrationRestDryRunService(store, transport).RunRestDryRunAsync(request, cancellation.Token);

        await interrupted.Should().ThrowAsync<OperationCanceledException>();
        transport.RequestCount.Should().Be(2);
        var retainedRun = (await store.GetSyncRunAsync(RunId))!;
        retainedRun.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Received);
        retainedRun.CompletedAt.Should().BeNull();
        await AssertDownstreamBlockedAfterRestartAsync();
    }

    [Fact]
    public async Task InterruptedQuarantinedPage_CannotBeRemediatedWithoutCompleteSource()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new TwoPageTransport(lastResponse: null, interruption: cancellation, firstQuantity: "bad");
        var (store, request) = await CreateSourceAsync();
        var interrupted = () => new ProviderIntegrationRestDryRunService(store, transport).RunRestDryRunAsync(request, cancellation.Token);
        await interrupted.Should().ThrowAsync<OperationCanceledException>();
        var sourceRun = (await store.GetSyncRunAsync(RunId))!;
        sourceRun.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Received);
        sourceRun.CompletedAt.Should().BeNull();
        var quarantined = (await store.ListQuarantinedRecordsAsync(RunId)).Should().ContainSingle().Which;
        var sourcePayload = (await store.GetRawPayloadAsync(RunId, sourceRun.RawPayloadId!))!;
        sourcePayload.RawPayload.GetProperty("positions")[0].GetProperty("quantity").GetString().Should().Be("bad");
        var manifest = (await store.GetManifestAsync(request.ManifestId))!;
        var repaired = manifest with
        {
            ManifestVersion = manifest.ManifestVersion + 1,
            FieldMappings = manifest.FieldMappings.Select(mapping => mapping.TargetField == "quantity"
                ? mapping with { ConstantValue = "100" } : mapping).ToArray()
        };
        var repairedReference = ProviderIntegrationManifestIdentity.Create(repaired);
        await store.SaveManifestVersionAsync(repaired);
        (await store.CompareExchangeCurrentManifestAsync(manifest.ManifestId,
            ProviderIntegrationManifestIdentity.Create(manifest), repairedReference)).Should().BeTrue();
        var replayRequest = new ProviderIntegrationQuarantineReplayRequestDto(
            "replay-interrupted-source", RunId, manifest.ManifestId, ConnectionId, ProviderCapabilityKindDto.Positions,
            [quarantined.QuarantineRecordId], "operator@example.com", RequestedAt.AddHours(1))
        {
            Mode = ProviderIntegrationReplayModeDto.Remediation,
            TargetManifestVersion = repaired.ManifestVersion,
            TargetManifestDigest = repairedReference.ContentDigest
        };
        var captureFolder = Path.Combine(testRoot, "_integrations", "raw-payloads");
        var rawCaptures = Directory.GetFiles(captureFolder, "*.json", SearchOption.AllDirectories);

        var replay = () => new ProviderIntegrationQuarantineReplayService(store).ReplayAsync(replayRequest);

        await replay.Should().ThrowAsync<InvalidOperationException>().WithMessage("*incomplete*");
        (await store.GetSyncRunAsync(replayRequest.ReplaySyncRunId)).Should().BeNull();
        (await store.ListStagingRecordsAsync(replayRequest.ReplaySyncRunId)).Should().BeEmpty();
        (await store.ListQuarantinedRecordsAsync(replayRequest.ReplaySyncRunId)).Should().BeEmpty();
        Directory.GetFiles(captureFolder, "*.json", SearchOption.AllDirectories).Should().BeEquivalentTo(rawCaptures);
        (await store.GetRawPayloadAsync(RunId, sourceRun.RawPayloadId!))!.RawPayload.GetRawText().Should().Be(sourcePayload.RawPayload.GetRawText());
        (await store.GetSyncRunAsync(RunId)).Should().BeEquivalentTo(sourceRun);
    }

    [Theory]
    [InlineData("nextCursor")]
    [InlineData("next_token")]
    [InlineData("accessToken")]
    public async Task CompleteTerminalPage_PreservesReadyRowsAndAllowsHandoff(string cursorField)
    {
        var transport = new TwoPageTransport(new ProviderIntegrationHttpResponse(200, new Dictionary<string, string>(), """{"positions":[]}"""), cursorField: cursorField);
        var (store, request) = await CreateSourceAsync(cursorField);

        var result = await new ProviderIntegrationRestDryRunService(store, transport).RunRestDryRunAsync(request);

        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Validated);
        result.RecordsReceived.Should().Be(1);
        result.RecordsAccepted.Should().Be(1);
        result.Issues.Should().BeEmpty();
        var restarted = new FileProviderIntegrationManifestStore(testRoot);
        var retainedRun = (await restarted.GetSyncRunAsync(RunId))!;
        (await restarted.GetRawPayloadAsync(RunId, retainedRun.RawPayloadId!))!.RawPayload.GetProperty(cursorField).GetString().Should().Be("page-2");
        var review = await new ProviderIntegrationStagingReviewService(restarted).GetReviewAsync(ConnectionId);
        review.ReadyForReconciliationCount.Should().Be(1);
        var identity = new ProviderIntegrationIdentityResolutionPreviewService(restarted);
        var identityRow = (await identity.PreviewAsync(ConnectionId)).Rows.Should().ContainSingle().Which;
        identityRow.AccountStatus.Should().Be(ProviderIntegrationIdentityResolutionStatusDto.Resolved);
        identityRow.SecurityStatus.Should().Be(ProviderIntegrationIdentityResolutionStatusDto.Resolved);
        identityRow.Issues.Should().BeEmpty();
        var promotion = new ProviderIntegrationPromotionReadinessService(identity);
        (await promotion.PreviewAsync(ConnectionId)).ReadyForReconciliationCount.Should().Be(1);
        var record = review.Records.Single();
        var handoff = await new ProviderIntegrationReconciliationHandoffService(restarted, promotion).HandoffAsync(CreateHandoff(record.StagingRecordId));
        handoff.Accepted.Should().BeTrue();
        handoff.AcceptedRecordCount.Should().Be(1);
        (await restarted.ListReconciliationHandoffRecordsAsync(ConnectionId)).Should().ContainSingle();
    }

    [Theory]
    [InlineData("nextCursor")]
    [InlineData("next_token")]
    [InlineData("accessToken")]
    public async Task MissingTerminalCapture_AfterSuccessfulFetchRefusesDownstreamHandoff(string cursorField)
    {
        var transport = new TwoPageTransport(new ProviderIntegrationHttpResponse(200, new Dictionary<string, string>(), """{"positions":[]}"""), cursorField: cursorField);
        var (store, request) = await CreateSourceAsync(cursorField);
        var result = await new ProviderIntegrationRestDryRunService(store, transport).RunRestDryRunAsync(request);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Validated);
        var restarted = new FileProviderIntegrationManifestStore(testRoot);
        (await restarted.GetSyncRunAsync(RunId))!.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Validated);
        var terminalCapture = Directory.EnumerateFiles(testRoot, "*.json", SearchOption.AllDirectories)
            .Single(path =>
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                return document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("requestMetadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object &&
                    metadata.TryGetProperty("page", out var page) && page.GetString() == "2";
            });

        File.Delete(terminalCapture);

        await AssertDownstreamBlockedAfterRestartAsync();
    }

    private async Task AssertDownstreamBlockedAfterRestartAsync()
    {
        var restarted = new FileProviderIntegrationManifestStore(testRoot);
        var originalRun = (await restarted.GetSyncRunAsync(RunId))!;
        var originalRow = (await restarted.ListStagingRecordsAsync(RunId)).Should().ContainSingle().Which;
        originalRow.ValidationWarnings.Should().BeEmpty();
        (await restarted.GetRawPayloadAsync(RunId, originalRow.RawPayloadId)).Should().NotBeNull();

        var review = await new ProviderIntegrationStagingReviewService(restarted).GetReviewAsync(ConnectionId);
        review.TotalStagedRecords.Should().Be(1);
        review.ReadyForReconciliationCount.Should().Be(0);
        review.Records.Should().ContainSingle().Which.ValidationWarnings.Should().Contain(issue =>
            issue.Code == IncompleteCode && issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
        var identity = new ProviderIntegrationIdentityResolutionPreviewService(restarted);
        var identityRow = (await identity.PreviewAsync(ConnectionId)).Rows.Should().ContainSingle().Which;
        identityRow.AccountStatus.Should().Be(ProviderIntegrationIdentityResolutionStatusDto.Resolved);
        identityRow.SecurityStatus.Should().Be(ProviderIntegrationIdentityResolutionStatusDto.Resolved);
        identityRow.Issues.Should().Contain(issue => issue.Code == IncompleteCode && issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
        var promotion = new ProviderIntegrationPromotionReadinessService(identity);
        var readiness = await promotion.PreviewAsync(ConnectionId);
        readiness.ReadyForReconciliationCount.Should().Be(0);
        readiness.BlockedCount.Should().Be(1);
        readiness.Rows.Should().ContainSingle().Which.Issues.Should().Contain(issue => issue.Code == IncompleteCode);
        var handoff = await new ProviderIntegrationReconciliationHandoffService(restarted, promotion).HandoffAsync(CreateHandoff(originalRow.StagingRecordId));
        handoff.Accepted.Should().BeFalse();
        handoff.AcceptedRecordCount.Should().Be(0);
        handoff.RejectedRecordCount.Should().Be(1);
        (await restarted.ListReconciliationHandoffRecordsAsync(ConnectionId)).Should().BeEmpty();
        (await restarted.GetSyncRunAsync(RunId)).Should().BeEquivalentTo(originalRun);
        (await restarted.ListStagingRecordsAsync(RunId)).Single().ValidationWarnings.Should().BeEmpty();
    }

    private async Task<(FileProviderIntegrationManifestStore Store, ProviderIntegrationRestDryRunRequestDto Request)> CreateSourceAsync(string cursorField = "nextCursor")
    {
        var template = new ProviderIntegrationTemplateCatalog().GetManifest("template-custodian-positions-v1")!;
        var manifest = template with
        {
            Capabilities = [new ProviderCapabilityDto(ProviderCapabilityKindDto.Positions, true, false, ["providerAccountId", "quantity", "asOf", "security.internalSecurityId"])],
            FieldMappings = [.. template.FieldMappings.Where(mapping => mapping.Capability == ProviderCapabilityKindDto.Positions && !mapping.TargetField.StartsWith("security.", StringComparison.Ordinal)),
                Constant("internalAccountId", "internal-account-1"),
                Constant("security.internalSecurityId", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")],
            Endpoints = template.Endpoints.Select(endpoint => endpoint.Capability == ProviderCapabilityKindDto.Positions
                ? endpoint with { Pagination = endpoint.Pagination with { CursorPath = "$." + cursorField } }
                : endpoint).ToArray(),
            ValidationRules = []
        };
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(new ProviderConnectionDto(
            ConnectionId, manifest.ProviderId, manifest.ManifestId, "Partial source test", "test",
            ProviderIntegrationActivationStateDto.Draft, "vault://provider-credentials/test/partial-source",
            [ProviderCapabilityKindDto.Positions], "operator@example.com", RequestedAt, RequestedAt, null));
        var request = new ProviderIntegrationRestDryRunRequestDto(
            RunId, manifest.ManifestId, ConnectionId, ProviderCapabilityKindDto.Positions, "positions",
            new Dictionary<string, string> { ["accountId"] = "A-100" },
            new Dictionary<string, string> { ["asOf"] = "2026-06-16" },
            "operator@example.com", RequestedAt, MaxPages: 2);
        return (store, request);
    }

    private static FieldMappingDto Constant(string target, string value)
        => new(ProviderCapabilityKindDto.Positions, string.Empty, target, null, true, ProviderMappingConfidenceDto.Approved, null, value);

    private static ProviderIntegrationReconciliationHandoffRequestDto CreateHandoff(string stagingRecordId)
        => new(ConnectionId, [stagingRecordId], "operator@example.com", RequestedAt.AddHours(1), "approval-completeness-test", "Reviewed complete source evidence.", RecentRunLimit: 10);

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
            Directory.Delete(testRoot, recursive: true);
    }

    private sealed class TwoPageTransport(ProviderIntegrationHttpResponse? lastResponse, CancellationTokenSource? interruption = null, string cursorField = "nextCursor", string firstQuantity = "100") : IProviderIntegrationHttpTransport
    {
        public int RequestCount { get; private set; }

        public Task<ProviderIntegrationHttpResponse> SendAsync(ProviderIntegrationHttpRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            RequestCount++;
            if (RequestCount == 1)
            {
                var body = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["positions"] = new[] { new { account_id = "A-100", quantity = firstQuantity, currency = "USD", as_of_date = "2026-06-16", position_id = "POS-1" } },
                    [cursorField] = "page-2"
                });
                return Task.FromResult(new ProviderIntegrationHttpResponse(200, new Dictionary<string, string>(), body));
            }
            interruption?.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(lastResponse ?? throw new InvalidOperationException("No synthetic terminal response is available."));
        }
    }
}
