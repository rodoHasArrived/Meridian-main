using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.Integrations;

/// <summary>The identity of one immutable integration configuration snapshot.</summary>
public sealed record ProviderIntegrationManifestReferenceDto(
    string ManifestId,
    int ManifestVersion,
    string ContentDigest);

[JsonConverter(typeof(JsonStringEnumConverter<ProviderIntegrationReplayModeDto>))]
public enum ProviderIntegrationReplayModeDto
{
    Original,
    Remediation
}

/// <summary>Canonical snapshot hashing shared by persistence and ingestion/replay.</summary>
/// <remarks>
/// Manifest digest schema v1 is the explicit projection below, including nested records. Do not
/// replace it with current DTO serialization: a future optional property with a non-null default
/// would then change the digest of already-retained versions after an upgrade. A schema extension
/// requires a separately versioned digest format and a compatible reader for these v1 identities.
/// The schema inventory and fixed digest tests guard this persistence contract.
/// </remarks>
public static class ProviderIntegrationManifestIdentity
{
    private static readonly IReadOnlyDictionary<Type, string[]> EnumTokensV1 = new Dictionary<Type, string[]>
    {
        [typeof(IntegrationTypeDto)] = ["Rest", "OpenApiRest", "GraphQl", "Webhook", "SftpFile", "ManualUpload", "Hybrid", "StreamingTemplate", "CertifiedTradingAdapter"],
        [typeof(ProviderCapabilityKindDto)] = ["Accounts", "Balances", "Positions", "Holdings", "Transactions", "TaxLots", "SecurityReferenceData", "MarketPrices", "CorporateActions", "Documents", "Alerts", "Events", "OrderPreview", "OrderPlacement", "OrderCancellation", "OrderStatus", "Executions"],
        [typeof(ProviderIntegrationActivationStateDto)] = ["Draft", "Tested", "DryRunPassed", "PendingApproval", "Active", "Paused", "Failed", "Retired"],
        [typeof(ProviderIntegrationAuthTypeDto)] = ["None", "ApiKey", "BearerToken", "OAuth2", "ClientCredentials", "Basic", "Certificate", "CustomHeader"],
        [typeof(ProviderIntegrationHttpMethodDto)] = ["Get", "Post", "Put", "Patch", "Delete"],
        [typeof(ProviderIntegrationPaginationTypeDto)] = ["None", "PageNumber", "Offset", "Cursor", "NextUrl"],
        [typeof(ProviderIntegrationCursorTypeDto)] = ["None", "Timestamp", "Date", "CursorToken", "PageNumber", "Offset", "Watermark", "FullSnapshot"],
        [typeof(ProviderMappingConfidenceDto)] = ["Low", "Medium", "High", "Approved"],
        [typeof(ProviderIntegrationIssueSeverityDto)] = ["Info", "Warning", "Critical"]
    };

    public static ProviderIntegrationManifestReferenceDto Create(ProviderIntegrationManifestDto manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.ManifestId);
        ArgumentOutOfRangeException.ThrowIfLessThan(manifest.ManifestVersion, 1);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(writer, ManifestV1(manifest));
        }

        return new(manifest.ManifestId, manifest.ManifestVersion, Sha256Digest.Compute(buffer.ToArray()));
    }

    public static bool Matches(
        ProviderIntegrationManifestReferenceDto? left,
        ProviderIntegrationManifestReferenceDto? right)
        => left is not null && right is not null &&
           StringComparer.Ordinal.Equals(left.ManifestId, right.ManifestId) &&
           left.ManifestVersion == right.ManifestVersion &&
           Sha256Digest.FixedEquals(left.ContentDigest, right.ContentDigest);

    private static JsonObject ManifestV1(ProviderIntegrationManifestDto value)
        => Object(
            ("manifestId", value.ManifestId),
            ("manifestVersion", value.ManifestVersion),
            ("providerId", value.ProviderId),
            ("displayName", value.DisplayName),
            ("integrationType", EnumValue(value.IntegrationType)),
            ("environment", value.Environment),
            ("auth", Project(value.Auth, AuthV1)),
            ("capabilities", Array(value.Capabilities, item => Project(item, CapabilityV1))),
            ("endpoints", Array(value.Endpoints, item => Project(item, EndpointV1))),
            ("fieldMappings", Array(value.FieldMappings, item => Project(item, MappingV1))),
            ("sync", Project(value.Sync, SyncV1)),
            ("validationRules", Array(value.ValidationRules, item => Project(item, ValidationV1))),
            ("activation", Project(value.Activation, ActivationV1)),
            ("state", EnumValue(value.State)),
            ("createdBy", value.CreatedBy),
            ("createdAt", JsonValue.Create(value.CreatedAt)),
            ("approvedBy", value.ApprovedBy),
            ("approvedAt", JsonValue.Create(value.ApprovedAt)),
            ("changeReason", value.ChangeReason));

    private static JsonObject AuthV1(ProviderIntegrationAuthConfigDto value)
        => Object(
            ("type", EnumValue(value.Type)),
            ("tokenUrl", value.TokenUrl),
            ("scopes", Strings(value.Scopes)),
            ("metadata", Dictionary(value.Metadata)));

    private static JsonObject CapabilityV1(ProviderCapabilityDto value)
        => Object(
            ("capability", EnumValue(value.Capability)),
            ("enabled", value.Enabled),
            ("requiresCertifiedAdapter", value.RequiresCertifiedAdapter),
            ("requiredCanonicalFields", Strings(value.RequiredCanonicalFields)));

    private static JsonObject EndpointV1(EndpointDefinitionDto value)
        => Object(
            ("endpointKey", value.EndpointKey),
            ("capability", EnumValue(value.Capability)),
            ("method", EnumValue(value.Method)),
            ("path", value.Path),
            ("headers", Dictionary(value.Headers)),
            ("query", Dictionary(value.Query)),
            ("requestBodyTemplate", value.RequestBodyTemplate),
            ("dependsOn", Project(value.DependsOn, DependencyV1)),
            ("pagination", Project(value.Pagination, PaginationV1)),
            ("response", Project(value.Response, ResponseV1)));

    private static JsonObject DependencyV1(EndpointDependencyDto value)
        => Object(("endpointKey", value.EndpointKey), ("outputPath", value.OutputPath), ("parameterName", value.ParameterName));

    private static JsonObject PaginationV1(EndpointPaginationDto value)
        => Object(
            ("type", EnumValue(value.Type)),
            ("cursorPath", value.CursorPath),
            ("cursorParam", value.CursorParam),
            ("nextUrlPath", value.NextUrlPath),
            ("pageSize", JsonValue.Create(value.PageSize)));

    private static JsonObject ResponseV1(EndpointResponseShapeDto value)
        => Object(("recordsPath", value.RecordsPath), ("schemaFingerprint", value.SchemaFingerprint), ("requiredPaths", Strings(value.RequiredPaths)));

    private static JsonObject MappingV1(FieldMappingDto value)
        => Object(
            ("capability", EnumValue(value.Capability)),
            ("sourcePath", value.SourcePath),
            ("targetField", value.TargetField),
            ("transform", Project(value.Transform, TransformV1)),
            ("required", value.Required),
            ("confidence", EnumValue(value.Confidence)),
            ("defaultValue", value.DefaultValue),
            ("constantValue", value.ConstantValue));

    private static JsonObject TransformV1(TransformRuleDto value)
        => Object(("type", value.Type), ("parameters", Dictionary(value.Parameters)));

    private static JsonObject SyncV1(SyncScheduleDto value)
        => Object(
            ("mode", value.Mode),
            ("frequency", value.Frequency),
            ("time", value.Time),
            ("timezone", value.Timezone),
            ("cursorType", EnumValue(value.CursorType)),
            ("cursorField", value.CursorField),
            ("fullRefreshFrequency", value.FullRefreshFrequency));

    private static JsonObject ValidationV1(ValidationRuleDto value)
        => Object(
            ("capability", EnumValue(value.Capability)),
            ("ruleCode", value.RuleCode),
            ("severity", EnumValue(value.Severity)),
            ("message", value.Message),
            ("targetFields", Strings(value.TargetFields)));

    private static JsonObject ActivationV1(ProviderIntegrationActivationPolicyDto value)
        => Object(
            ("requiresAuthenticationTest", value.RequiresAuthenticationTest),
            ("requiresEndpointTest", value.RequiresEndpointTest),
            ("requiresDryRun", value.RequiresDryRun),
            ("requiresApproval", value.RequiresApproval),
            ("productionWriteCapabilitiesAllowed", value.ProductionWriteCapabilitiesAllowed),
            ("requiredIssueCodes", Strings(value.RequiredIssueCodes)));

    private static JsonNode? Project<T>(T? value, Func<T, JsonObject> projection) where T : class
        => value is null ? null : projection(value);

    private static JsonArray? Array<T>(IReadOnlyList<T>? values, Func<T, JsonNode?> projection)
        => values is null ? null : new JsonArray(values.Select(projection).ToArray());

    private static JsonArray? Strings(IReadOnlyList<string>? values)
        => Array(values, value => JsonValue.Create(value));

    private static JsonObject? Dictionary(IReadOnlyDictionary<string, string>? values)
        => values is null ? null : new JsonObject(values.Select(pair =>
            new KeyValuePair<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value))));

    private static JsonObject Object(params (string Name, JsonNode? Value)[] properties)
        => new(properties.Where(property => property.Value is not null).Select(property =>
            new KeyValuePair<string, JsonNode?>(property.Name, property.Value)));

    private static JsonNode EnumValue<T>(T value) where T : struct, Enum
    {
        var number = Convert.ToInt32(value);
        var tokens = EnumTokensV1[typeof(T)];
        return number >= 0 && number < tokens.Length ? JsonValue.Create(tokens[number])! : JsonValue.Create(number)!;
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? value)
    {
        switch (value)
        {
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var property in obj.OrderBy(property => property.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            case null:
                writer.WriteNullValue();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
