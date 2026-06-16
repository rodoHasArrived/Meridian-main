using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationActivationReadinessServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationActivationReadinessServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_provider_activation_test_{Guid.NewGuid():N}");
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
    public async Task EvaluateAsync_BlocksGenericManifestWithProductionWriteCapability()
    {
        var manifest = CreateManifest(
            IntegrationTypeDto.Rest,
            ProviderCapabilityKindDto.OrderPlacement,
            requiresCertifiedAdapter: true,
            requiredFields: [],
            fieldMappings: [],
            activation: new ProviderIntegrationActivationPolicyDto(
                RequiresAuthenticationTest: false,
                RequiresEndpointTest: false,
                RequiresDryRun: false,
                RequiresApproval: false,
                ProductionWriteCapabilitiesAllowed: false,
                RequiredIssueCodes: []),
            state: ProviderIntegrationActivationStateDto.PendingApproval);
        var connection = CreateConnection(manifest, [ProviderCapabilityKindDto.OrderPlacement]);
        var service = await CreateServiceAsync(manifest, connection);

        var readiness = await service.EvaluateAsync(manifest.ManifestId, connection.ConnectionId);

        readiness.IsReady.Should().BeFalse();
        readiness.RequiredEvidence.Should().Contain("certified-adapter:OrderPlacement");
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "provider-manifest.certified-adapter-required" &&
            issue.Capability == ProviderCapabilityKindDto.OrderPlacement &&
            issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "provider-manifest.production-write-policy-blocked" &&
            issue.Capability == ProviderCapabilityKindDto.OrderPlacement &&
            issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
    }

    [Fact]
    public async Task EvaluateAsync_BlocksActivationWhenRequiredCanonicalMappingIsMissing()
    {
        var manifest = CreateManifest(
            IntegrationTypeDto.Rest,
            ProviderCapabilityKindDto.Positions,
            requiresCertifiedAdapter: false,
            requiredFields: ["providerAccountId", "quantity"],
            fieldMappings:
            [
                Mapping(ProviderCapabilityKindDto.Positions, "providerAccountId")
            ],
            activation: new ProviderIntegrationActivationPolicyDto(
                RequiresAuthenticationTest: false,
                RequiresEndpointTest: false,
                RequiresDryRun: false,
                RequiresApproval: false,
                ProductionWriteCapabilitiesAllowed: false,
                RequiredIssueCodes: []),
            state: ProviderIntegrationActivationStateDto.DryRunPassed);
        var service = await CreateServiceAsync(manifest);

        var readiness = await service.EvaluateAsync(manifest.ManifestId);

        readiness.IsReady.Should().BeFalse();
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "provider-manifest.required-mapping-missing" &&
            issue.Capability == ProviderCapabilityKindDto.Positions &&
            issue.Message.Contains("quantity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsync_AllowsReadOnlyManifestWithApprovalEvidence()
    {
        var manifest = CreateManifest(
            IntegrationTypeDto.Rest,
            ProviderCapabilityKindDto.Positions,
            requiresCertifiedAdapter: false,
            requiredFields: ["providerAccountId", "quantity", "asOf"],
            fieldMappings:
            [
                Mapping(ProviderCapabilityKindDto.Positions, "providerAccountId"),
                Mapping(ProviderCapabilityKindDto.Positions, "quantity"),
                Mapping(ProviderCapabilityKindDto.Positions, "asOf")
            ],
            activation: new ProviderIntegrationActivationPolicyDto(
                RequiresAuthenticationTest: true,
                RequiresEndpointTest: true,
                RequiresDryRun: true,
                RequiresApproval: true,
                ProductionWriteCapabilitiesAllowed: false,
                RequiredIssueCodes: []),
            state: ProviderIntegrationActivationStateDto.Active,
            approvedBy: "controller@example.com",
            approvedAt: DateTimeOffset.Parse("2026-06-16T13:00:00Z"));
        var connection = CreateConnection(manifest, [ProviderCapabilityKindDto.Positions], "approval-evidence-1");
        var service = await CreateServiceAsync(manifest, connection);

        var readiness = await service.EvaluateAsync(manifest.ManifestId, connection.ConnectionId);

        readiness.IsReady.Should().BeTrue();
        readiness.Issues.Should().BeEmpty();
        readiness.RequiredEvidence.Should().BeEquivalentTo(
            "activation-approval",
            "authentication-test",
            "dry-run-result",
            "endpoint-test");
    }

    private async Task<ProviderIntegrationActivationReadinessService> CreateServiceAsync(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto? connection = null)
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await store.SaveManifestAsync(manifest);
        if (connection is not null)
        {
            await store.SaveConnectionAsync(connection);
        }

        return new ProviderIntegrationActivationReadinessService(store);
    }

    private static ProviderIntegrationManifestDto CreateManifest(
        IntegrationTypeDto integrationType,
        ProviderCapabilityKindDto capability,
        bool requiresCertifiedAdapter,
        IReadOnlyList<string> requiredFields,
        IReadOnlyList<FieldMappingDto> fieldMappings,
        ProviderIntegrationActivationPolicyDto activation,
        ProviderIntegrationActivationStateDto state,
        string? approvedBy = null,
        DateTimeOffset? approvedAt = null)
        => new(
            $"manifest-{capability.ToString().ToLowerInvariant()}-v1",
            1,
            "provider-alpha",
            "Provider Alpha",
            integrationType,
            "production",
            new ProviderIntegrationAuthConfigDto(
                ProviderIntegrationAuthTypeDto.OAuth2,
                "https://api.example.com/oauth/token",
                ["positions.read"],
                new Dictionary<string, string>()),
            [
                new ProviderCapabilityDto(
                    capability,
                    true,
                    requiresCertifiedAdapter,
                    requiredFields)
            ],
            [],
            fieldMappings,
            new SyncScheduleDto(
                "incremental",
                "daily",
                "06:00",
                "America/New_York",
                ProviderIntegrationCursorTypeDto.Timestamp,
                "updated_at",
                "monthly"),
            [],
            activation,
            state,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            approvedBy,
            approvedAt,
            "Activation readiness test manifest");

    private static FieldMappingDto Mapping(
        ProviderCapabilityKindDto capability,
        string targetField,
        ProviderMappingConfidenceDto confidence = ProviderMappingConfidenceDto.Approved)
        => new(
            capability,
            $"$.{targetField.Replace('.', '_')}",
            targetField,
            null,
            true,
            confidence,
            null,
            null);

    private static ProviderConnectionDto CreateConnection(
        ProviderIntegrationManifestDto manifest,
        IReadOnlyList<ProviderCapabilityKindDto> enabledCapabilities,
        string? approvalEvidenceId = null)
        => new(
            "connection-provider-alpha",
            manifest.ProviderId,
            manifest.ManifestId,
            "Provider Alpha Production",
            manifest.Environment,
            manifest.State,
            "vault://provider-credentials/provider-alpha/production",
            enabledCapabilities,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:05:00Z"),
            approvalEvidenceId);
}
