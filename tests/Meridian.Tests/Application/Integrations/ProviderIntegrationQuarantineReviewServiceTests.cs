using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationQuarantineReviewServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationQuarantineReviewServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_provider_quarantine_review_test_{Guid.NewGuid():N}");
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
    public async Task GetReviewAsync_GroupsQuarantinedIssuesForConnection()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedQuarantineAsync(store, connectionId: "connection-alpha");
        var service = new ProviderIntegrationQuarantineReviewService(store);

        var review = await service.GetReviewAsync("connection-alpha");

        review.ConnectionId.Should().Be("connection-alpha");
        review.SyncRunIds.Should().ContainSingle().Which.Should().Be("sync-run-quarantine-1");
        review.TotalQuarantinedRecords.Should().Be(2);
        review.CriticalIssueCount.Should().Be(2);
        review.WarningIssueCount.Should().Be(0);
        review.IssueGroups.Should().ContainSingle(group =>
            group.IssueCode == "position.security-id.missing" &&
            group.RecordCount == 2);
        review.Records.Should().HaveCount(2);
    }

    [Fact]
    public async Task ResolveAsync_PersistsQuarantineDecisionEvidence()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedQuarantineAsync(store, connectionId: "connection-alpha");
        var service = new ProviderIntegrationQuarantineReviewService(store);
        var request = CreateResolutionRequest();

        var result = await service.ResolveAsync(request);

        result.Resolved.Should().BeTrue();
        result.Record.QuarantineRecordId.Should().Be(request.QuarantineRecordId);
        result.Decision.Action.Should().Be(ProviderIntegrationQuarantineResolutionActionDto.ReplayAfterMappingChange);
        result.Decision.ReviewedBy.Should().Be("operator@example.com");
        result.Decision.DecisionId.Should().NotBeNullOrWhiteSpace();
        var decisions = await store.ListQuarantineDecisionsAsync(request.SyncRunId);
        decisions.Should().ContainSingle()
            .Which.QuarantineRecordId.Should().Be(request.QuarantineRecordId);
    }

    [Fact]
    public async Task ResolveAsync_RejectsConnectionMismatch()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedQuarantineAsync(store, connectionId: "connection-alpha");
        await store.SaveConnectionAsync(CreateConnection("connection-beta"));
        var service = new ProviderIntegrationQuarantineReviewService(store);
        var request = CreateResolutionRequest(connectionId: "connection-beta");

        var act = () => service.ResolveAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not linked to the requested connection*");
    }

    [Fact]
    public async Task ResolveAsync_ObservesCancellationBeforePersisting()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedQuarantineAsync(store, connectionId: "connection-alpha");
        var service = new ProviderIntegrationQuarantineReviewService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.ResolveAsync(CreateResolutionRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await store.ListQuarantineDecisionsAsync("sync-run-quarantine-1")).Should().BeEmpty();
    }

    private static ProviderIntegrationQuarantineResolutionRequestDto CreateResolutionRequest(
        string connectionId = "connection-alpha")
        => new(
            connectionId,
            "sync-run-quarantine-1",
            "quarantine-1",
            ProviderIntegrationQuarantineResolutionActionDto.ReplayAfterMappingChange,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:05:00Z"),
            "CUSIP mapping was added; replay after mapping change.");

    private static async Task SeedQuarantineAsync(
        IProviderIntegrationManifestStore store,
        string connectionId)
    {
        await store.SaveManifestAsync(CreateManifest()).ConfigureAwait(false);
        await store.SaveConnectionAsync(CreateConnection(connectionId)).ConfigureAwait(false);
        await store.SaveSyncRunAsync(new ProviderIntegrationSyncRunDto(
            "sync-run-quarantine-1",
            "manifest-custodian-abc-v1",
            connectionId,
            "custodian-abc",
            ProviderCapabilityKindDto.Positions,
            "positions",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:02:00Z"),
            ProviderIntegrationProcessingStatusDto.Quarantined,
            RecordsReceived: 2,
            RecordsAccepted: 0,
            RecordsQuarantined: 2,
            RawPayloadId: "payload-1",
            Issues:
            [
                new ValidationIssueDto(
                    "position.security-id.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Security identifier is required.",
                    "security",
                    "Map CUSIP, ISIN, ticker, or provider security id.")
            ])).ConfigureAwait(false);
        for (var index = 1; index <= 2; index++)
        {
            await store.SaveQuarantinedRecordAsync(new QuarantinedRecordDto(
                $"quarantine-{index}",
                "sync-run-quarantine-1",
                connectionId,
                ProviderCapabilityKindDto.Positions,
                Json($$"""{"account_id":"A{{index}}"}"""),
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
                DateTimeOffset.Parse($"2026-06-16T12:0{index}:00Z"))).ConfigureAwait(false);
        }
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
            ChangeReason: "Quarantine review test manifest");

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
