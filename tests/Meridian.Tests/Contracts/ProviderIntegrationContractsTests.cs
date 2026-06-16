using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Integrations;

namespace Meridian.Tests.Contracts;

public sealed class ProviderIntegrationContractsTests
{
    [Fact]
    public void ProviderIntegrationManifest_RoundTripsWithCamelCaseAndStringEnums()
    {
        var manifest = CreateManifest();

        var json = JsonSerializer.Serialize(
            manifest,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto);
        var roundTrip = JsonSerializer.Deserialize(
            json,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto);

        json.Should().Contain("\"manifestId\"");
        json.Should().Contain("\"integrationType\": \"Rest\"");
        json.Should().Contain("\"capability\": \"Positions\"");
        json.Should().Contain("\"targetField\": \"security.cusip\"");
        json.Should().NotContain("\"ManifestId\"");
        roundTrip.Should().BeEquivalentTo(manifest);
    }

    [Fact]
    public void ProviderConnection_DoesNotRequireCredentialSecretMaterial()
    {
        var connection = new ProviderConnectionDto(
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

        var json = JsonSerializer.Serialize(
            connection,
            ProviderIntegrationContractsJsonContext.Default.ProviderConnectionDto);

        json.Should().Contain("\"credentialSecretRef\"");
        json.Should().NotContain("clientSecret");
        json.Should().NotContain("apiKey");
        json.Should().NotContain("password");
    }

    [Fact]
    public void ProviderIntegrationActivationReadiness_RoundTripsWithOperatorSafeIssues()
    {
        var readiness = new ProviderIntegrationActivationReadinessDto(
            IsReady: false,
            Issues:
            [
                new ProviderIntegrationActivationIssueDto(
                    "provider-manifest.required-mapping-missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Required canonical field 'quantity' is not mapped for Positions.",
                    ProviderCapabilityKindDto.Positions,
                    "Map the required canonical field before activation.")
            ],
            RequiredEvidence: ["dry-run-result", "activation-approval"]);

        var json = JsonSerializer.Serialize(
            readiness,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationActivationReadinessDto);
        var roundTrip = JsonSerializer.Deserialize(
            json,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationActivationReadinessDto);

        json.Should().Contain("\"isReady\": false");
        json.Should().Contain("\"severity\": \"Critical\"");
        json.Should().Contain("\"capability\": \"Positions\"");
        json.Should().NotContain("\"IsReady\"");
        roundTrip.Should().BeEquivalentTo(readiness);
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
                new Dictionary<string, string> { ["credentialField"] = "clientCredentials" }),
            [
                new ProviderCapabilityDto(
                    ProviderCapabilityKindDto.Positions,
                    Enabled: true,
                    RequiresCertifiedAdapter: false,
                    RequiredCanonicalFields: ["providerAccountId", "quantity", "asOf"])
            ],
            [
                new EndpointDefinitionDto(
                    "positions",
                    ProviderCapabilityKindDto.Positions,
                    ProviderIntegrationHttpMethodDto.Get,
                    "/v1/accounts/{accountId}/positions",
                    new Dictionary<string, string> { ["Accept"] = "application/json" },
                    new Dictionary<string, string> { ["asOf"] = "{asOfDate}" },
                    RequestBodyTemplate: null,
                    DependsOn: new EndpointDependencyDto("accounts", "$.id", "accountId"),
                    Pagination: new EndpointPaginationDto(
                        ProviderIntegrationPaginationTypeDto.Cursor,
                        "$.nextCursor",
                        "cursor",
                        NextUrlPath: null,
                        PageSize: 100),
                    Response: new EndpointResponseShapeDto(
                        "$.positions",
                        "sha256:shape",
                        ["$.positions[*].account_id", "$.positions[*].quantity"]))
            ],
            [
                new FieldMappingDto(
                    ProviderCapabilityKindDto.Positions,
                    "$.cusip",
                    "security.cusip",
                    new TransformRuleDto("trimUppercase", new Dictionary<string, string>()),
                    Required: false,
                    ProviderMappingConfidenceDto.High,
                    DefaultValue: null,
                    ConstantValue: null)
            ],
            new SyncScheduleDto(
                "incremental",
                "daily",
                "06:00",
                "America/New_York",
                ProviderIntegrationCursorTypeDto.Timestamp,
                "updated_at",
                "monthly"),
            [
                new ValidationRuleDto(
                    ProviderCapabilityKindDto.Positions,
                    "position.required-provider-account",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Provider account id is required.",
                    ["providerAccountId"])
            ],
            new ProviderIntegrationActivationPolicyDto(
                RequiresAuthenticationTest: true,
                RequiresEndpointTest: true,
                RequiresDryRun: true,
                RequiresApproval: true,
                ProductionWriteCapabilitiesAllowed: false,
                RequiredIssueCodes:
                [
                    "integration.connection-alpha.credential-state",
                    "integration.connection-alpha.required-mapping"
                ]),
            ProviderIntegrationActivationStateDto.Draft,
            "ops@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Initial custodian positions setup");
}
