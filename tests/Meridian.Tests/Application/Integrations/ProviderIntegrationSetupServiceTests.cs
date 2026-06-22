using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

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
