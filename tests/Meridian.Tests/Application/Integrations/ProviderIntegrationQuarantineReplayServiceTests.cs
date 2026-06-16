using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationQuarantineReplayServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationQuarantineReplayServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_provider_quarantine_replay_test_{Guid.NewGuid():N}");
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
    public async Task ReplayAsync_StagesRecordsAfterMappingChange()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedQuarantineAsync(store, CreateReplayReadyManifest());
        var service = new ProviderIntegrationQuarantineReplayService(store);

        var result = await service.ReplayAsync(CreateRequest());

        result.ReplaySyncRunId.Should().Be("sync-run-replay-1");
        result.RecordsReplayed.Should().Be(1);
        result.RecordsAccepted.Should().Be(1);
        result.RecordsRequarantined.Should().Be(0);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Validated);
        result.Issues.Should().BeEmpty();
        (await store.GetRawPayloadAsync(result.ReplaySyncRunId, result.RawPayloadId)).Should().NotBeNull();
        (await store.GetSyncRunAsync(result.ReplaySyncRunId))!.RecordsAccepted.Should().Be(1);
        var staged = await store.ListStagingRecordsAsync(result.ReplaySyncRunId);
        staged.Should().ContainSingle();
        staged[0].SourceRecordId.Should().Be("POS-1");
        staged[0].MappedRecord.GetProperty("security").GetProperty("cusip").GetString().Should().Be("9128285M8");
        (await store.ListQuarantinedRecordsAsync(result.ReplaySyncRunId)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReplayAsync_RequarantinesRecordsWhenMappingStillFails()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedQuarantineAsync(store, CreateReplayReadyManifest(includeSecurityMapping: false));
        var service = new ProviderIntegrationQuarantineReplayService(store);

        var result = await service.ReplayAsync(CreateRequest());

        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
        result.RecordsAccepted.Should().Be(0);
        result.RecordsRequarantined.Should().Be(1);
        result.Issues.Should().Contain(issue =>
            issue.Code == "required.missing" &&
            issue.TargetField == "security.cusip");
        (await store.ListStagingRecordsAsync(result.ReplaySyncRunId)).Should().BeEmpty();
        (await store.ListQuarantinedRecordsAsync(result.ReplaySyncRunId)).Should().ContainSingle()
            .Which.ValidationErrors.Should().Contain(error => error.TargetField == "security.cusip");
    }

    [Fact]
    public async Task ReplayAsync_ObservesCancellationBeforeEvidence()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedQuarantineAsync(store, CreateReplayReadyManifest());
        var service = new ProviderIntegrationQuarantineReplayService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.ReplayAsync(CreateRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await store.GetSyncRunAsync("sync-run-replay-1")).Should().BeNull();
        (await store.ListStagingRecordsAsync("sync-run-replay-1")).Should().BeEmpty();
        (await store.ListQuarantinedRecordsAsync("sync-run-replay-1")).Should().BeEmpty();
    }

    private static ProviderIntegrationQuarantineReplayRequestDto CreateRequest()
        => new(
            "sync-run-replay-1",
            "sync-run-quarantine-1",
            "manifest-custodian-abc-v1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            ["quarantine-1"],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:30:00Z"));

    private static async Task SeedQuarantineAsync(
        IProviderIntegrationManifestStore store,
        ProviderIntegrationManifestDto manifest)
    {
        await store.SaveManifestAsync(manifest).ConfigureAwait(false);
        await store.SaveConnectionAsync(CreateConnection()).ConfigureAwait(false);
        await store.SaveSyncRunAsync(new ProviderIntegrationSyncRunDto(
            "sync-run-quarantine-1",
            manifest.ManifestId,
            "connection-alpha",
            manifest.ProviderId,
            ProviderCapabilityKindDto.Positions,
            "positions",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:02:00Z"),
            ProviderIntegrationProcessingStatusDto.Quarantined,
            RecordsReceived: 1,
            RecordsAccepted: 0,
            RecordsQuarantined: 1,
            RawPayloadId: "payload-1",
            Issues:
            [
                new ValidationIssueDto(
                    "required.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Required field 'security.cusip' is missing.",
                    "security.cusip",
                    "Map CUSIP, ISIN, ticker, or provider security id.")
            ])).ConfigureAwait(false);
        await store.SaveQuarantinedRecordAsync(new QuarantinedRecordDto(
            "quarantine-1",
            "sync-run-quarantine-1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            Json("""{"account_id":"A-100","quantity":"100","as_of_date":"2026-06-16","position_id":"POS-1"}"""),
            MappedRecord: Json("""{"providerAccountId":"A-100","quantity":"100","asOf":"2026-06-16"}"""),
            [
                new ValidationIssueDto(
                    "required.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Required field 'security.cusip' is missing.",
                    "security.cusip",
                    "Map CUSIP, ISIN, ticker, or provider security id.")
            ],
            ProviderIntegrationProcessingStatusDto.Quarantined,
            DateTimeOffset.Parse("2026-06-16T12:01:00Z"))).ConfigureAwait(false);
    }

    private static ProviderIntegrationManifestDto CreateReplayReadyManifest(bool includeSecurityMapping = true)
    {
        var mappings = new List<FieldMappingDto>
        {
            Mapping("$.account_id", "providerAccountId"),
            Mapping("$.quantity", "quantity", new TransformRuleDto("decimal", new Dictionary<string, string>())),
            Mapping("$.as_of_date", "asOf", new TransformRuleDto("date", new Dictionary<string, string>())),
            Mapping("$.position_id", "sourceRecordId")
        };
        if (includeSecurityMapping)
        {
            mappings.Add(new FieldMappingDto(
                ProviderCapabilityKindDto.Positions,
                "$.missing_cusip",
                "security.cusip",
                new TransformRuleDto("trimUppercase", new Dictionary<string, string>()),
                Required: true,
                ProviderMappingConfidenceDto.Approved,
                DefaultValue: null,
                ConstantValue: "9128285M8"));
        }

        return new ProviderIntegrationManifestDto(
            "manifest-custodian-abc-v1",
            2,
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
                    RequiredCanonicalFields: ["providerAccountId", "quantity", "asOf", "security.cusip"])
            ],
            [],
            mappings,
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
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:20:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Replay after mapping change");
    }

    private static FieldMappingDto Mapping(
        string sourcePath,
        string targetField,
        TransformRuleDto? transform = null)
        => new(
            ProviderCapabilityKindDto.Positions,
            sourcePath,
            targetField,
            transform,
            Required: true,
            ProviderMappingConfidenceDto.Approved,
            DefaultValue: null,
            ConstantValue: null);

    private static ProviderConnectionDto CreateConnection()
        => new(
            "connection-alpha",
            "custodian-abc",
            "manifest-custodian-abc-v1",
            "Custodian ABC General Account",
            "production",
            ProviderIntegrationActivationStateDto.DryRunPassed,
            "vault://provider-credentials/custodian-abc/general-account",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovalEvidenceId: null);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
