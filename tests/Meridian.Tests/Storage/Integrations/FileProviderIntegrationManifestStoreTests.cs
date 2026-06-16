using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Storage.Integrations;

public sealed class FileProviderIntegrationManifestStoreTests : IDisposable
{
    private readonly string _testRoot;

    public FileProviderIntegrationManifestStoreTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"mdc_integrations_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SaveManifestAsync_PersistsManifestAcrossStoreInstances()
    {
        var manifest = CreateManifest();
        var store = new FileProviderIntegrationManifestStore(_testRoot);

        await store.SaveManifestAsync(manifest);
        var reloaded = await new FileProviderIntegrationManifestStore(_testRoot)
            .GetManifestAsync(manifest.ManifestId);

        reloaded.Should().BeEquivalentTo(manifest);
        Directory.EnumerateFiles(Path.Combine(_testRoot, "_integrations", "manifests"), "*.json")
            .Should()
            .ContainSingle();
    }

    [Fact]
    public async Task SaveConnectionAsync_PersistsSafeCredentialReferenceOnly()
    {
        var connection = CreateConnection();
        var store = new FileProviderIntegrationManifestStore(_testRoot);

        await store.SaveConnectionAsync(connection);
        var reloaded = await store.GetConnectionAsync(connection.ConnectionId);

        reloaded.Should().BeEquivalentTo(connection);
        var json = await File.ReadAllTextAsync(
            Directory.EnumerateFiles(Path.Combine(_testRoot, "_integrations", "connections"), "*.json").Single());
        json.Should().Contain("credentialSecretRef");
        json.Should().NotContain("api-key-value");
        json.Should().NotContain("client-secret-value");
    }

    [Fact]
    public async Task SaveRawPayloadQuarantineAndStaging_KeepRunScopedEvidence()
    {
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        var rawPayload = new RawIngestionPayloadDto(
            "payload-1",
            "custodian-abc",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            "positions",
            "sync-run-1",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            new Dictionary<string, string> { ["method"] = "GET", ["path"] = "/v1/positions" },
            Json("""{"positions":[{"account_id":"A1","quantity":"100"}]}"""),
            "manifest-custodian-abc-v1",
            ProviderIntegrationProcessingStatusDto.Received);
        var quarantineRecord = new QuarantinedRecordDto(
            "quarantine-1",
            "sync-run-1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            Json("""{"account_id":"A1"}"""),
            MappedRecord: null,
            [
                new ValidationIssueDto(
                    "position.security-id.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Security identifier is required.",
                    "security",
                    "Map CUSIP, ISIN, ticker, or provider security id.")
            ],
            ProviderIntegrationProcessingStatusDto.Quarantined,
            DateTimeOffset.Parse("2026-06-16T12:01:00Z"));
        var stagingRecord = new IntegrationStagingRecordDto(
            "staging-1",
            "sync-run-1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            "payload-1",
            "source-row-1",
            "connection-alpha|A1|CUSIP123|2026-06-16",
            Json("""{"providerAccountId":"A1","quantity":100,"security":{"cusip":"CUSIP123"}}"""),
            [],
            ProviderIntegrationProcessingStatusDto.Validated,
            DateTimeOffset.Parse("2026-06-16T12:02:00Z"));

        await store.SaveRawPayloadAsync(rawPayload);
        await store.SaveQuarantinedRecordAsync(quarantineRecord);
        await store.SaveStagingRecordAsync(stagingRecord);

        var reloadedPayload = await store.GetRawPayloadAsync("sync-run-1", "payload-1");
        var quarantined = await store.ListQuarantinedRecordsAsync("sync-run-1");
        var staged = await store.ListStagingRecordsAsync("sync-run-1");

        reloadedPayload.Should().NotBeNull();
        reloadedPayload!.PayloadId.Should().Be(rawPayload.PayloadId);
        reloadedPayload.RawPayload.GetProperty("positions")[0].GetProperty("account_id").GetString().Should().Be("A1");
        reloadedPayload.RawPayload.GetProperty("positions")[0].GetProperty("quantity").GetString().Should().Be("100");

        var reloadedQuarantine = quarantined.Should().ContainSingle().Which;
        reloadedQuarantine.QuarantineRecordId.Should().Be(quarantineRecord.QuarantineRecordId);
        reloadedQuarantine.RawRecord.GetProperty("account_id").GetString().Should().Be("A1");
        reloadedQuarantine.ValidationErrors.Should().ContainSingle()
            .Which.Code.Should().Be("position.security-id.missing");

        var reloadedStaging = staged.Should().ContainSingle().Which;
        reloadedStaging.StagingRecordId.Should().Be(stagingRecord.StagingRecordId);
        reloadedStaging.DedupeKey.Should().Be(stagingRecord.DedupeKey);
        reloadedStaging.MappedRecord.GetProperty("security").GetProperty("cusip").GetString().Should().Be("CUSIP123");
    }

    [Fact]
    public async Task SaveManifestAsync_HonorsCancellation()
    {
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => store.SaveManifestAsync(CreateManifest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
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
                RequiredIssueCodes: ["integration.connection-alpha.approval-evidence"]),
            ProviderIntegrationActivationStateDto.Draft,
            "ops@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Initial setup");

    private static ProviderConnectionDto CreateConnection()
        => new(
            "connection-alpha",
            "custodian-abc",
            "manifest-custodian-abc-v1",
            "General Account",
            "production",
            ProviderIntegrationActivationStateDto.PendingApproval,
            "vault://provider-credentials/custodian-abc/general-account",
            [ProviderCapabilityKindDto.Positions],
            "ops@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:05:00Z"),
            "approval-evidence-1");

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
