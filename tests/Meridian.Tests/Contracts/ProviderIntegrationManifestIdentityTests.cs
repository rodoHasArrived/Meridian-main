using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Integrations;

namespace Meridian.Tests.Contracts;

public sealed class ProviderIntegrationManifestIdentityTests
{
    [Fact]
    public void Create_V1Fixture_HasStableDigestAcrossSerializationRoundTrip()
    {
        var manifest = ReadFixture();
        var serialized = JsonSerializer.Serialize(manifest, ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto);
        var reloaded = JsonSerializer.Deserialize(serialized, ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto)!;

        var reference = ProviderIntegrationManifestIdentity.Create(manifest);

        reference.ContentDigest.Should().Be("ab1e5d4083e9e256f5d6f1a8e87a38bf43de0d282aa016728e083b33cbc5f100");
        ProviderIntegrationManifestIdentity.Create(reloaded).Should().Be(reference);
    }

    [Fact]
    public void Create_DictionaryInsertionOrder_DoesNotChangeIdentity()
    {
        var manifest = ReadFixture();
        var reordered = manifest with
        {
            Auth = manifest.Auth with { Metadata = Reverse(manifest.Auth.Metadata) },
            Endpoints = manifest.Endpoints.Select(endpoint => endpoint with
            {
                Headers = Reverse(endpoint.Headers),
                Query = Reverse(endpoint.Query)
            }).ToArray(),
            FieldMappings = manifest.FieldMappings.Select(mapping => mapping with
            {
                Transform = mapping.Transform! with { Parameters = Reverse(mapping.Transform!.Parameters) }
            }).ToArray()
        };

        ProviderIntegrationManifestIdentity.Create(reordered).Should().Be(ProviderIntegrationManifestIdentity.Create(manifest));
    }

    [Fact]
    public void Create_MappingAndApprovalChanges_ChangeIdentity()
    {
        var manifest = ReadFixture();
        var reference = ProviderIntegrationManifestIdentity.Create(manifest);
        var changedMapping = manifest with { FieldMappings = [manifest.FieldMappings[0] with { ConstantValue = "101" }] };
        var changedApproval = manifest with { ApprovedBy = "second-approver" };

        ProviderIntegrationManifestIdentity.Create(changedMapping).Should().NotBe(reference);
        ProviderIntegrationManifestIdentity.Create(changedApproval).Should().NotBe(reference);
    }

    [Theory]
    [MemberData(nameof(V1RecordSchemas))]
    public void V1Projection_CoversTheExactPersistedRecordSchema(Type type, string members)
    {
        // A new semantic field needs an explicit digest-schema evolution decision. It must not
        // silently escape hashing, nor alter the digest of a previously retained v1 snapshot.
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name)
            .Should().BeEquivalentTo(members.Split(' '));
    }

    public static IEnumerable<object[]> V1RecordSchemas()
    {
        yield return [typeof(ProviderIntegrationManifestDto), "ManifestId ManifestVersion ProviderId DisplayName IntegrationType Environment Auth Capabilities Endpoints FieldMappings Sync ValidationRules Activation State CreatedBy CreatedAt ApprovedBy ApprovedAt ChangeReason"];
        yield return [typeof(ProviderIntegrationAuthConfigDto), "Type TokenUrl Scopes Metadata"];
        yield return [typeof(ProviderCapabilityDto), "Capability Enabled RequiresCertifiedAdapter RequiredCanonicalFields"];
        yield return [typeof(EndpointDefinitionDto), "EndpointKey Capability Method Path Headers Query RequestBodyTemplate DependsOn Pagination Response"];
        yield return [typeof(EndpointDependencyDto), "EndpointKey OutputPath ParameterName"];
        yield return [typeof(EndpointPaginationDto), "Type CursorPath CursorParam NextUrlPath PageSize"];
        yield return [typeof(EndpointResponseShapeDto), "RecordsPath SchemaFingerprint RequiredPaths"];
        yield return [typeof(FieldMappingDto), "Capability SourcePath TargetField Transform Required Confidence DefaultValue ConstantValue"];
        yield return [typeof(TransformRuleDto), "Type Parameters"];
        yield return [typeof(SyncScheduleDto), "Mode Frequency Time Timezone CursorType CursorField FullRefreshFrequency"];
        yield return [typeof(ValidationRuleDto), "Capability RuleCode Severity Message TargetFields"];
        yield return [typeof(ProviderIntegrationActivationPolicyDto), "RequiresAuthenticationTest RequiresEndpointTest RequiresDryRun RequiresApproval ProductionWriteCapabilitiesAllowed RequiredIssueCodes"];
    }

    private static Dictionary<string, string> Reverse(IReadOnlyDictionary<string, string> values)
        => values.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);

    private static ProviderIntegrationManifestDto ReadFixture()
        => JsonSerializer.Deserialize(ManifestV1Json, ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto)!;

    private const string ManifestV1Json = """
        {
          "manifestId": "manifest-v1-fixture",
          "manifestVersion": 7,
          "providerId": "custodian",
          "displayName": "Custodian Positions",
          "integrationType": "Rest",
          "environment": "sandbox",
          "auth": {
            "type": "OAuth2",
            "tokenUrl": "https://example.test/token",
            "scopes": ["positions.read", "accounts.read"],
            "metadata": { "z-setting": "last", "a-setting": "first" }
          },
          "capabilities": [{
            "capability": "Positions", "enabled": true, "requiresCertifiedAdapter": false,
            "requiredCanonicalFields": ["quantity", "security.cusip"]
          }],
          "endpoints": [{
            "endpointKey": "positions", "capability": "Positions", "method": "Get",
            "path": "/accounts/{account}/positions",
            "headers": { "z-header": "last", "a-header": "first" },
            "query": { "z-query": "last", "a-query": "first" },
            "requestBodyTemplate": "sample-body",
            "dependsOn": { "endpointKey": "accounts", "outputPath": "items[*].id", "parameterName": "account" },
            "pagination": { "type": "Cursor", "cursorPath": "next", "cursorParam": "cursor", "nextUrlPath": "links.next", "pageSize": 100 },
            "response": { "recordsPath": "items", "schemaFingerprint": "schema-v1", "requiredPaths": ["quantity", "cusip"] }
          }],
          "fieldMappings": [{
            "capability": "Positions", "sourcePath": "qty", "targetField": "quantity",
            "transform": { "type": "decimal", "parameters": { "z-parameter": "last", "a-parameter": "first" } },
            "required": true, "confidence": "Approved", "defaultValue": "0", "constantValue": "100"
          }],
          "sync": { "mode": "incremental", "frequency": "daily", "time": "06:00", "timezone": "UTC", "cursorType": "Timestamp", "cursorField": "updatedAt", "fullRefreshFrequency": "monthly" },
          "validationRules": [{ "capability": "Positions", "ruleCode": "required", "severity": "Critical", "message": "Quantity required", "targetFields": ["quantity"] }],
          "activation": { "requiresAuthenticationTest": true, "requiresEndpointTest": true, "requiresDryRun": true, "requiresApproval": true, "productionWriteCapabilitiesAllowed": false, "requiredIssueCodes": ["review-required"] },
          "state": "Active",
          "createdBy": "operator",
          "createdAt": "2026-10-05T14:30:00+00:00",
          "approvedBy": "approver",
          "approvedAt": "2026-10-05T15:00:00+00:00",
          "changeReason": "Initial approved mapping"
        }
        """;
}
