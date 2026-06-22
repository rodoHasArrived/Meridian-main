using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationActivationServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationActivationServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_provider_activation_command_test_{Guid.NewGuid():N}");
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
    public async Task ActivateAsync_PersistsActiveManifestAndConnectionWhenReadinessPasses()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest(
            requiredFields: ["providerAccountId", "quantity", "asOf"],
            mappings:
            [
                Mapping("providerAccountId"),
                Mapping("quantity"),
                Mapping("asOf")
            ]);
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationActivationService(store);

        var result = await service.ActivateAsync(CreateRequest(manifest, connection));

        result.Activated.Should().BeTrue();
        result.ManifestState.Should().Be(ProviderIntegrationActivationStateDto.Active);
        result.ConnectionState.Should().Be(ProviderIntegrationActivationStateDto.Active);
        result.Readiness.IsReady.Should().BeTrue();
        result.ApprovalEvidenceId.Should().Be("approval-evidence-activation-1");
        var savedManifest = await store.GetManifestAsync(manifest.ManifestId);
        savedManifest.Should().NotBeNull();
        savedManifest!.State.Should().Be(ProviderIntegrationActivationStateDto.Active);
        savedManifest.ApprovedBy.Should().Be("approver@example.com");
        savedManifest.ApprovedAt.Should().Be(DateTimeOffset.Parse("2026-06-16T14:00:00Z"));
        savedManifest.ChangeReason.Should().Be("Approved after dry-run evidence review.");
        var savedConnection = await store.GetConnectionAsync(connection.ConnectionId);
        savedConnection.Should().NotBeNull();
        savedConnection!.State.Should().Be(ProviderIntegrationActivationStateDto.Active);
        savedConnection.ApprovalEvidenceId.Should().Be("approval-evidence-activation-1");
    }

    [Fact]
    public async Task ActivateAsync_DoesNotPersistWhenReadinessHasCriticalIssues()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest(
            requiredFields: ["providerAccountId", "quantity"],
            mappings: [Mapping("providerAccountId")]);
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationActivationService(store);

        var result = await service.ActivateAsync(CreateRequest(manifest, connection));

        result.Activated.Should().BeFalse();
        result.ManifestState.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
        result.ConnectionState.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
        result.Readiness.Issues.Should().Contain(issue =>
            issue.Code == "provider-manifest.required-mapping-missing" &&
            issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
        (await store.GetManifestAsync(manifest.ManifestId))!.State.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
        (await store.GetConnectionAsync(connection.ConnectionId))!.State.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
    }

    [Fact]
    public async Task ActivateAsync_ObservesCancellationBeforePersistingActivation()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest(
            requiredFields: ["providerAccountId", "quantity"],
            mappings: [Mapping("providerAccountId"), Mapping("quantity")]);
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationActivationService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.ActivateAsync(CreateRequest(manifest, connection), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await store.GetManifestAsync(manifest.ManifestId))!.State.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
        (await store.GetConnectionAsync(connection.ConnectionId))!.State.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
    }

    private static ProviderIntegrationActivationRequestDto CreateRequest(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection)
        => new(
            manifest.ManifestId,
            connection.ConnectionId,
            "approver@example.com",
            DateTimeOffset.Parse("2026-06-16T14:00:00Z"),
            "approval-evidence-activation-1",
            "Approved after dry-run evidence review.");

    private static ProviderIntegrationManifestDto CreateManifest(
        IReadOnlyList<string> requiredFields,
        IReadOnlyList<FieldMappingDto> mappings)
        => new(
            "manifest-activation-command-v1",
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
                    requiredFields)
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
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Activation command test manifest");

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

    private static ProviderConnectionDto CreateConnection(ProviderIntegrationManifestDto manifest)
        => new(
            "connection-activation-command",
            manifest.ProviderId,
            manifest.ManifestId,
            "Provider Alpha Production",
            manifest.Environment,
            ProviderIntegrationActivationStateDto.DryRunPassed,
            "vault://provider-credentials/provider-alpha/production",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:05:00Z"),
            ApprovalEvidenceId: null);
}
