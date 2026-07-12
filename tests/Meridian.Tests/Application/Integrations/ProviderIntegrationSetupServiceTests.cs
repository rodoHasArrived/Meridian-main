using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationSetupServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationSetupServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_provider_setup_service_test_{Guid.NewGuid():N}");
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
    public async Task SaveDraftAsync_PersistsManifestAndConnectionInTenantPartition()
    {
        var rootStore = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationSetupService(rootStore);
        var request = CreateRequest();

        var result = await service.SaveDraftAsync("tenant-alpha", request);

        result.Saved.Should().BeTrue();
        result.ManifestId.Should().Be(request.Manifest.ManifestId);
        result.ConnectionId.Should().Be(request.Connection.ConnectionId);
        result.Readiness.Issues.Should().Contain(issue => issue.Code == "provider-manifest.endpoint-test-required");
        var tenantStore = ((IProviderIntegrationTenantManifestStoreFactory)rootStore).ForTenant("tenant-alpha");
        (await tenantStore.GetManifestAsync(request.Manifest.ManifestId))!.ChangeReason.Should()
            .Be("Saved by setup wizard.");
        (await tenantStore.GetConnectionAsync(request.Connection.ConnectionId))!.UpdatedAt.Should()
            .Be(DateTimeOffset.Parse("2026-06-16T15:00:00Z"));
        (await rootStore.GetManifestAsync(request.Manifest.ManifestId)).Should().BeNull();
    }

    [Fact]
    public async Task SaveDraftAsync_RejectsConnectionManifestMismatch()
    {
        var service = new ProviderIntegrationSetupService(new FileProviderIntegrationManifestStore(testRoot));
        var request = CreateRequest(connectionManifestId: "manifest-other");

        var act = () => service.SaveDraftAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*manifest id must match*");
    }

    [Fact]
    public async Task SaveDraftAsync_AggregatesAllValidationIssuesInOneFailure()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationSetupService(store);
        var request = CreateRequest() with { SavedBy = " " };
        request = request with
        {
            Connection = request.Connection with
            {
                CredentialSecretRef = "",
                ProviderId = "provider-other",
                Environment = "sandbox",
                EnabledCapabilities =
                [
                    ProviderCapabilityKindDto.Positions,
                    ProviderCapabilityKindDto.Transactions
                ]
            }
        };

        var act = () => service.SaveDraftAsync(request);

        var assertion = await act.Should().ThrowAsync<ProviderIntegrationSetupValidationException>();
        assertion.Which.Issues.Select(issue => issue.Code).Should().BeEquivalentTo(
        [
            "provider-setup.saved-by-required",
            "provider-setup.credential-secret-ref-required",
            "provider-setup.connection-provider-mismatch",
            "provider-setup.connection-environment-mismatch",
            "provider-setup.connection-capability-not-declared"
        ]);
        assertion.Which.Issues.Should().OnlyContain(issue =>
            !string.IsNullOrWhiteSpace(issue.Field) && !string.IsNullOrWhiteSpace(issue.Message));
        (await store.GetManifestAsync(request.Manifest.ManifestId)).Should().BeNull();
    }

    [Fact]
    public async Task SaveDraftAsync_NormalizesActiveAndRetiredStatesToDraft()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationSetupService(store);
        var request = CreateRequest();
        request = request with
        {
            Manifest = request.Manifest with { State = ProviderIntegrationActivationStateDto.Active },
            Connection = request.Connection with { State = ProviderIntegrationActivationStateDto.Retired }
        };

        var result = await service.SaveDraftAsync(request);

        result.ManifestState.Should().Be(ProviderIntegrationActivationStateDto.Draft);
        result.ConnectionState.Should().Be(ProviderIntegrationActivationStateDto.Draft);
        result.Message.Should().Contain("reset to Draft");
        (await store.GetManifestAsync(request.Manifest.ManifestId))!.State.Should()
            .Be(ProviderIntegrationActivationStateDto.Draft);
        (await store.GetConnectionAsync(request.Connection.ConnectionId))!.State.Should()
            .Be(ProviderIntegrationActivationStateDto.Draft);
    }

    [Fact]
    public async Task SaveDraftAsync_ObservesCancellationBeforePersisting()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationSetupService(store);
        var request = CreateRequest();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.SaveDraftAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await store.GetManifestAsync(request.Manifest.ManifestId)).Should().BeNull();
    }

    [Fact]
    public async Task SaveDraftAsync_WhenStoreFails_LogsBoundaryContextAndRethrows()
    {
        var store = Substitute.For<IProviderIntegrationManifestStore>();
        var logger = new RecordingLogger<ProviderIntegrationSetupService>();
        var service = new ProviderIntegrationSetupService(store, logger);
        var request = CreateRequest();
        store.SaveManifestAsync(Arg.Any<ProviderIntegrationManifestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("storage unavailable")));

        var act = () => service.SaveDraftAsync("tenant-alpha", request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("storage unavailable");
        logger.Entries.Should().Contain(entry =>
            entry.LogLevel == LogLevel.Warning &&
            entry.Exception is InvalidOperationException &&
            entry.Message.Contains("setup-save-draft", StringComparison.Ordinal) &&
            entry.Message.Contains("tenant-alpha", StringComparison.Ordinal) &&
            entry.Message.Contains(request.Manifest.ManifestId, StringComparison.Ordinal) &&
            entry.Message.Contains(request.Connection.ConnectionId, StringComparison.Ordinal));
    }

    private static ProviderIntegrationSetupSaveRequestDto CreateRequest(
        string connectionManifestId = "manifest-setup-command-v1")
    {
        var manifest = new ProviderIntegrationManifestDto(
            "manifest-setup-command-v1",
            1,
            "provider-alpha",
            "Provider Alpha",
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
                    RequiredCanonicalFields: ["providerAccountId", "quantity"])
            ],
            [],
            [Mapping("providerAccountId")],
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
            DateTimeOffset.Parse("2026-06-16T14:30:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Initial setup draft");
        var connection = new ProviderConnectionDto(
            "connection-setup-command",
            manifest.ProviderId,
            connectionManifestId,
            "Provider Alpha Production",
            manifest.Environment,
            ProviderIntegrationActivationStateDto.Draft,
            "vault://provider-credentials/provider-alpha/production",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T14:30:00Z"),
            DateTimeOffset.Parse("2026-06-16T14:30:00Z"),
            ApprovalEvidenceId: null);
        return new ProviderIntegrationSetupSaveRequestDto(
            manifest,
            connection,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T15:00:00Z"),
            "Saved by setup wizard.");
    }

    private static FieldMappingDto Mapping(string targetField)
        => new(
            ProviderCapabilityKindDto.Positions,
            $"$.{targetField.Replace('.', '_')}",
            targetField,
            null,
            Required: true,
            ProviderMappingConfidenceDto.Approved,
            DefaultValue: null,
            ConstantValue: null);
}
