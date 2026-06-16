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
    public async Task ReplayAsync_StagesReviewedQuarantineRecordAfterMappingChange()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedReplaySourceAsync(store, includeCusipConstant: true, includeReplayDecision: true);
        var service = new ProviderIntegrationQuarantineReplayService(store);

        var result = await service.ReplayAsync(CreateRequest());

        result.RecordsReplayed.Should().Be(1);
        result.RecordsAccepted.Should().Be(1);
        result.RecordsRequarantined.Should().Be(0);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Validated);
        (await store.GetRawPayloadAsync(result.ReplaySyncRunId, result.RawPayloadId)).Should().NotBeNull();
        var staged = await store.ListStagingRecordsAsync(result.ReplaySyncRunId);
        staged.Should().ContainSingle();
        staged.Single().MappedRecord.GetProperty("security").GetProperty("cusip").GetString().Should().Be("9128285M8");
        (await store.ListQuarantinedRecordsAsync(result.ReplaySyncRunId)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReplayAsync_RequarantinesRecordWhenMappingStillFails()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedReplaySourceAsync(store, includeCusipConstant: false, includeReplayDecision: true);
        var service = new ProviderIntegrationQuarantineReplayService(store);

        var result = await service.ReplayAsync(CreateRequest());

        result.RecordsAccepted.Should().Be(0);
        result.RecordsRequarantined.Should().Be(1);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
        var quarantined = await store.ListQuarantinedRecordsAsync(result.ReplaySyncRunId);
        quarantined.Should().ContainSingle()
            .Which.ValidationErrors.Should().Contain(issue => issue.TargetField == "security.cusip");
    }

    [Fact]
    public async Task ReplayAsync_BlocksRecordsWithoutReplayDecision()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedReplaySourceAsync(store, includeCusipConstant: true, includeReplayDecision: false);
        var service = new ProviderIntegrationQuarantineReplayService(store);

        var act = () => service.ReplayAsync(CreateRequest());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*approved for replay*");
    }

    [Fact]
    public async Task ReplayAsync_ObservesCancellationBeforeWritingReplay()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedReplaySourceAsync(store, includeCusipConstant: true, includeReplayDecision: true);
        var service = new ProviderIntegrationQuarantineReplayService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.ReplayAsync(CreateRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await store.GetSyncRunAsync("sync-run-replay-1")).Should().BeNull();
    }

    private static ProviderIntegrationQuarantineReplayRequestDto CreateRequest()
        => new(
            "sync-run-replay-1",
            "sync-run-source-1",
            "manifest-custodian-abc-v1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            ["quarantine-source-1"],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:20:00Z"));

    private static async Task SeedReplaySourceAsync(
        IProviderIntegrationManifestStore store,
        bool includeCusipConstant,
        bool includeReplayDecision)
    {
        var manifest = CreateManifest(includeCusipConstant);
        await store.SaveManifestAsync(manifest).ConfigureAwait(false);
        await store.SaveConnectionAsync(CreateConnection(manifest)).ConfigureAwait(false);
        await store.SaveSyncRunAsync(new ProviderIntegrationSyncRunDto(
            "sync-run-source-1",
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
            RawPayloadId: "payload-source-1",
            Issues:
            [
                new ValidationIssueDto(
                    "required.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Security identifier is required.",
                    "security.cusip",
                    "Map CUSIP before replay.")
            ])).ConfigureAwait(false);
        await store.SaveQuarantinedRecordAsync(new QuarantinedRecordDto(
            "quarantine-source-1",
            "sync-run-source-1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            Json("""{"account_id":"A-100","quantity":"100","as_of_date":"2026-06-16"}"""),
            MappedRecord: null,
            [
                new ValidationIssueDto(
                    "required.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Security identifier is required.",
                    "security.cusip",
                    "Map CUSIP before replay.")
            ],
            ProviderIntegrationProcessingStatusDto.Quarantined,
            DateTimeOffset.Parse("2026-06-16T12:02:00Z"))).ConfigureAwait(false);

        if (includeReplayDecision)
        {
            await store.SaveQuarantineDecisionAsync(new ProviderIntegrationQuarantineDecisionDto(
                "decision-source-1",
                "sync-run-source-1",
                "quarantine-source-1",
                "connection-alpha",
                ProviderIntegrationQuarantineResolutionActionDto.ReplayAfterMappingChange,
                "operator@example.com",
                DateTimeOffset.Parse("2026-06-16T12:10:00Z"),
                "Mapping updated.")).ConfigureAwait(false);
        }
    }

    private static ProviderIntegrationManifestDto CreateManifest(bool includeCusipConstant)
    {
        var mappings = new List<FieldMappingDto>
        {
            Mapping("$.account_id", "providerAccountId", "trim", required: true),
            Mapping("$.quantity", "quantity", "decimal", required: true),
            Mapping("$.as_of_date", "asOf", "date", required: true)
        };
        if (includeCusipConstant)
        {
            mappings.Add(new FieldMappingDto(
                ProviderCapabilityKindDto.Positions,
                "$.cusip",
                "security.cusip",
                new TransformRuleDto("uppercase", new Dictionary<string, string>()),
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
                    RequiredCanonicalFields: ["providerAccountId", "security.cusip", "quantity", "asOf"])
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
            DateTimeOffset.Parse("2026-06-16T12:15:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Replay mapping update");
    }

    private static FieldMappingDto Mapping(
        string sourcePath,
        string targetField,
        string? transform,
        bool required)
        => new(
            ProviderCapabilityKindDto.Positions,
            sourcePath,
            targetField,
            string.IsNullOrWhiteSpace(transform)
                ? null
                : new TransformRuleDto(transform, new Dictionary<string, string>()),
            required,
            ProviderMappingConfidenceDto.Approved,
            DefaultValue: null,
            ConstantValue: null);

    private static ProviderConnectionDto CreateConnection(ProviderIntegrationManifestDto manifest)
        => new(
            "connection-alpha",
            manifest.ProviderId,
            manifest.ManifestId,
            "Custodian ABC General Account",
            manifest.Environment,
            ProviderIntegrationActivationStateDto.DryRunPassed,
            "vault://provider-credentials/custodian-abc/general-account",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:15:00Z"),
            ApprovalEvidenceId: null);

    private static JsonElement Json(string json)
        => JsonDocument.Parse(json).RootElement.Clone();
}
