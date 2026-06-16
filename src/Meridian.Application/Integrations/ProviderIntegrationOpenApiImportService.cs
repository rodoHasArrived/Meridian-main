using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationOpenApiImportService
{
    private static readonly ProviderCapabilityKindDto[] SupportedCapabilities =
    [
        ProviderCapabilityKindDto.Accounts,
        ProviderCapabilityKindDto.Balances,
        ProviderCapabilityKindDto.Positions,
        ProviderCapabilityKindDto.Holdings,
        ProviderCapabilityKindDto.Transactions,
        ProviderCapabilityKindDto.TaxLots,
        ProviderCapabilityKindDto.SecurityReferenceData,
        ProviderCapabilityKindDto.MarketPrices,
        ProviderCapabilityKindDto.CorporateActions,
        ProviderCapabilityKindDto.Documents,
        ProviderCapabilityKindDto.Alerts,
        ProviderCapabilityKindDto.Events,
        ProviderCapabilityKindDto.OrderPreview,
        ProviderCapabilityKindDto.OrderPlacement,
        ProviderCapabilityKindDto.OrderCancellation,
        ProviderCapabilityKindDto.OrderStatus,
        ProviderCapabilityKindDto.Executions
    ];

    private readonly IProviderIntegrationManifestStore store;

    public ProviderIntegrationOpenApiImportService(IProviderIntegrationManifestStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ProviderIntegrationOpenApiImportResultDto> ImportAsync(
        ProviderIntegrationOpenApiImportRequestDto request,
        CancellationToken ct = default)
        => await ImportAsync(null, request, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationOpenApiImportResultDto> ImportAsync(
        string? tenantId,
        ProviderIntegrationOpenApiImportRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManifestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OpenApiDocumentJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ImportedBy);

        ct.ThrowIfCancellationRequested();

        var issues = new List<ValidationIssueDto>();
        using var document = JsonDocument.Parse(request.OpenApiDocumentJson);
        var root = document.RootElement;
        ValidateOpenApiRoot(root, issues);

        var selectedCapabilities = NormalizeCapabilities(request.Capabilities);
        var endpointCandidates = BuildEndpoints(root, selectedCapabilities, issues);
        var importedCapabilities = selectedCapabilities.Length == 0
            ? endpointCandidates.Select(endpoint => endpoint.Capability).Distinct().ToArray()
            : selectedCapabilities;
        var manifestCapabilities = importedCapabilities
            .Select(CreateCapability)
            .ToArray();
        var mappings = BuildFieldMappings(root, endpointCandidates, manifestCapabilities, issues);
        if (endpointCandidates.Count == 0)
        {
            issues.Add(new ValidationIssueDto(
                "openapi.endpoints.missing",
                ProviderIntegrationIssueSeverityDto.Critical,
                "No supported REST endpoints were found for the selected provider capabilities.",
                null,
                "Select a read-only capability with matching paths or use the guided endpoint builder."));
        }

        var manifest = new ProviderIntegrationManifestDto(
            request.ManifestId,
            ManifestVersion: 1,
            request.ProviderId,
            request.DisplayName,
            IntegrationTypeDto.OpenApiRest,
            request.Environment,
            BuildAuth(request, root),
            manifestCapabilities,
            endpointCandidates,
            mappings,
            new SyncScheduleDto(
                "incremental",
                "daily",
                "06:00",
                "America/New_York",
                ProviderIntegrationCursorTypeDto.Timestamp,
                "updated_at",
                "monthly"),
            manifestCapabilities.Select(capability =>
                new ValidationRuleDto(
                    capability.Capability,
                    "required-fields",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Required canonical fields must be mapped before activation.",
                    capability.RequiredCanonicalFields)).ToArray(),
            new ProviderIntegrationActivationPolicyDto(
                RequiresAuthenticationTest: request.AuthType != ProviderIntegrationAuthTypeDto.None,
                RequiresEndpointTest: true,
                RequiresDryRun: true,
                RequiresApproval: true,
                ProductionWriteCapabilitiesAllowed: false,
                RequiredIssueCodes:
                [
                    "provider-manifest.endpoint-test-required",
                    "provider-manifest.dry-run-required",
                    "provider-manifest.approval-evidence-required"
                ]),
            ProviderIntegrationActivationStateDto.Draft,
            request.ImportedBy,
            request.ImportedAt,
            ApprovedBy: null,
            ApprovedAt: null,
            request.ChangeReason ?? "Imported from OpenAPI specification.");

        var readiness = ProviderIntegrationActivationReadinessService.Evaluate(manifest);
        var result = new ProviderIntegrationOpenApiImportResultDto(
            Imported: !issues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical),
            manifest,
            readiness,
            issues,
            issues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical)
                ? "OpenAPI import created a draft manifest with critical issues to review."
                : "OpenAPI import draft manifest saved.");

        if (result.Imported)
        {
            var scopedStore = ResolveStore(tenantId);
            await scopedStore.SaveManifestAsync(manifest, ct).ConfigureAwait(false);
        }

        return result;
    }

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;

    private static void ValidateOpenApiRoot(JsonElement root, List<ValidationIssueDto> issues)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            (!root.TryGetProperty("openapi", out _) && !root.TryGetProperty("swagger", out _)))
        {
            issues.Add(new ValidationIssueDto(
                "openapi.document.invalid",
                ProviderIntegrationIssueSeverityDto.Critical,
                "The supplied document is not an OpenAPI or Swagger JSON object.",
                "openApiDocumentJson",
                "Paste a valid OpenAPI 3.x or Swagger 2.0 JSON document."));
        }
    }

    private static ProviderIntegrationAuthConfigDto BuildAuth(
        ProviderIntegrationOpenApiImportRequestDto request,
        JsonElement root)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = "openapi-import"
        };

        if (TryReadString(root, "$.info.title", out var title))
        {
            metadata["openApiTitle"] = title;
        }

        if (TryReadString(root, "$.info.version", out var version))
        {
            metadata["openApiVersion"] = version;
        }

        if (TryReadString(root, "$.servers.0.url", out var serverUrl) ||
            TryReadString(root, "$.host", out serverUrl))
        {
            metadata["baseUrl"] = serverUrl;
        }

        return new ProviderIntegrationAuthConfigDto(
            request.AuthType,
            request.TokenUrl,
            request.Scopes,
            metadata);
    }

    private static ProviderCapabilityKindDto[] NormalizeCapabilities(IReadOnlyList<ProviderCapabilityKindDto> capabilities)
        => capabilities
            .Where(capability => SupportedCapabilities.Contains(capability))
            .Distinct()
            .ToArray();

    private static ProviderCapabilityDto CreateCapability(ProviderCapabilityKindDto capability)
        => new(
            capability,
            Enabled: true,
            RequiresCertifiedAdapter: RequiresCertifiedAdapter(capability),
            RequiredCanonicalFields: RequiredFields(capability));

    private static bool RequiresCertifiedAdapter(ProviderCapabilityKindDto capability)
        => capability is ProviderCapabilityKindDto.OrderPreview
            or ProviderCapabilityKindDto.OrderPlacement
            or ProviderCapabilityKindDto.OrderCancellation;

    private static IReadOnlyList<string> RequiredFields(ProviderCapabilityKindDto capability)
        => capability switch
        {
            ProviderCapabilityKindDto.Accounts => ["providerAccountId", "accountName", "baseCurrency"],
            ProviderCapabilityKindDto.Balances => ["providerAccountId", "balance.amount", "balance.currency", "asOf"],
            ProviderCapabilityKindDto.Positions or ProviderCapabilityKindDto.Holdings =>
                ["providerAccountId", "security.cusip", "quantity", "asOf"],
            ProviderCapabilityKindDto.Transactions =>
                ["providerTransactionId", "providerAccountId", "transactionType", "postingDate", "amount.amount", "amount.currency"],
            ProviderCapabilityKindDto.TaxLots =>
                ["providerAccountId", "security.cusip", "quantity", "costBasis.amount", "asOf"],
            ProviderCapabilityKindDto.SecurityReferenceData =>
                ["security.cusip", "security.name"],
            ProviderCapabilityKindDto.MarketPrices =>
                ["security.cusip", "price.amount", "price.currency", "asOf"],
            ProviderCapabilityKindDto.CorporateActions =>
                ["security.cusip", "corporateActionType", "effectiveDate"],
            ProviderCapabilityKindDto.Documents =>
                ["documentId", "providerAccountId", "documentType"],
            ProviderCapabilityKindDto.Alerts or ProviderCapabilityKindDto.Events =>
                ["eventId", "eventType", "occurredAt"],
            ProviderCapabilityKindDto.OrderPreview or ProviderCapabilityKindDto.OrderPlacement or ProviderCapabilityKindDto.OrderCancellation =>
                ["providerAccountId", "orderId", "side", "quantity"],
            ProviderCapabilityKindDto.OrderStatus =>
                ["orderId", "orderStatus", "updatedAt"],
            ProviderCapabilityKindDto.Executions =>
                ["executionId", "orderId", "quantity", "price.amount"],
            _ => []
        };

    private static List<EndpointDefinitionDto> BuildEndpoints(
        JsonElement root,
        IReadOnlyList<ProviderCapabilityKindDto> selectedCapabilities,
        List<ValidationIssueDto> issues)
    {
        if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new ValidationIssueDto(
                "openapi.paths.missing",
                ProviderIntegrationIssueSeverityDto.Critical,
                "The OpenAPI document does not contain a paths object.",
                "paths",
                "Use the guided endpoint builder or import a complete OpenAPI document."));
            return [];
        }

        var endpointKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = selectedCapabilities.Count == 0 ? SupportedCapabilities : selectedCapabilities;
        var endpoints = new List<EndpointDefinitionDto>();
        foreach (var path in paths.EnumerateObject())
        {
            if (path.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var method in path.Value.EnumerateObject())
            {
                if (!TryMapMethod(method.Name, out var httpMethod) || method.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var capability = InferCapability(path.Name, method.Value, candidates);
                if (capability is null)
                {
                    continue;
                }

                var endpointKey = UniqueEndpointKey(endpointKeys, method.Value, capability.Value, httpMethod, path.Name);
                endpoints.Add(new EndpointDefinitionDto(
                    endpointKey,
                    capability.Value,
                    httpMethod,
                    path.Name,
                    Headers: new Dictionary<string, string>
                    {
                        ["Accept"] = "application/json"
                    },
                    Query: ExtractQueryParameters(method.Value),
                    RequestBodyTemplate: httpMethod == ProviderIntegrationHttpMethodDto.Get
                        ? null
                        : "{}",
                    DependsOn: null,
                    Pagination: new EndpointPaginationDto(
                        ProviderIntegrationPaginationTypeDto.Cursor,
                        CursorPath: "$.nextCursor",
                        CursorParam: "cursor",
                        NextUrlPath: null,
                        PageSize: 500),
                    Response: new EndpointResponseShapeDto(
                        InferRecordsPath(root, method.Value, capability.Value),
                        SchemaFingerprint: null,
                        RequiredPaths: [])));
            }
        }

        return endpoints
            .OrderBy(endpoint => endpoint.Capability)
            .ThenBy(endpoint => endpoint.EndpointKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> ExtractQueryParameters(JsonElement operation)
    {
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!operation.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Array)
        {
            return query;
        }

        foreach (var parameter in parameters.EnumerateArray())
        {
            if (parameter.ValueKind != JsonValueKind.Object ||
                !TryReadString(parameter, "$.in", out var location) ||
                !StringComparer.OrdinalIgnoreCase.Equals(location, "query") ||
                !TryReadString(parameter, "$.name", out var name) ||
                string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            query[name.Trim()] = $"{{{name.Trim()}}}";
        }

        return query;
    }

    private static IReadOnlyList<FieldMappingDto> BuildFieldMappings(
        JsonElement root,
        IReadOnlyList<EndpointDefinitionDto> endpoints,
        IReadOnlyList<ProviderCapabilityDto> capabilities,
        List<ValidationIssueDto> issues)
    {
        var mappings = new List<FieldMappingDto>();
        foreach (var capability in capabilities)
        {
            var endpoint = endpoints.FirstOrDefault(candidate => candidate.Capability == capability.Capability);
            if (endpoint is null)
            {
                continue;
            }

            var schema = GetResponseSchema(root, FindOperation(root, endpoint.Path, endpoint.Method));
            var recordSchema = ResolveRecordSchema(root, schema, endpoint.Response.RecordsPath);
            var sourceFields = FlattenSchemaFields(root, recordSchema);
            foreach (var requiredField in capability.RequiredCanonicalFields)
            {
                var suggestion = FindSourceField(requiredField, capability.Capability, sourceFields);
                if (suggestion is null)
                {
                    issues.Add(new ValidationIssueDto(
                        "openapi.mapping.suggestion-missing",
                        ProviderIntegrationIssueSeverityDto.Warning,
                        $"No OpenAPI schema field could be matched to required canonical field '{requiredField}'.",
                        requiredField,
                        "Review the sample response and map this field manually before activation."));
                    continue;
                }

                mappings.Add(new FieldMappingDto(
                    capability.Capability,
                    $"$.{suggestion.SourcePath}",
                    requiredField,
                    CreateTransform(requiredField),
                    Required: true,
                    suggestion.Confidence,
                    DefaultValue: DefaultValueFor(requiredField),
                    ConstantValue: null));
            }
        }

        return mappings
            .GroupBy(mapping => (mapping.Capability, mapping.TargetField), new MappingKeyComparer())
            .Select(group => group.First())
            .ToArray();
    }

    private static JsonElement FindOperation(
        JsonElement root,
        string path,
        ProviderIntegrationHttpMethodDto method)
    {
        if (!root.TryGetProperty("paths", out var paths) ||
            !paths.TryGetProperty(path, out var pathItem))
        {
            return default;
        }

        return pathItem.TryGetProperty(method.ToString().ToLowerInvariant(), out var operation)
            ? operation
            : default;
    }

    private static ProviderCapabilityKindDto? InferCapability(
        string path,
        JsonElement operation,
        IReadOnlyList<ProviderCapabilityKindDto> candidates)
    {
        var text = BuildOperationSearchText(path, operation);
        var bestScore = 0;
        ProviderCapabilityKindDto? bestCapability = null;
        foreach (var candidate in candidates)
        {
            var score = CapabilityKeywords(candidate).Count(keyword =>
                text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (score > bestScore)
            {
                bestScore = score;
                bestCapability = candidate;
            }
        }

        return bestScore == 0 ? null : bestCapability;
    }

    private static string BuildOperationSearchText(string path, JsonElement operation)
    {
        var builder = new StringBuilder(path);
        AppendIfPresent(builder, operation, "operationId");
        AppendIfPresent(builder, operation, "summary");
        AppendIfPresent(builder, operation, "description");
        if (operation.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tags.EnumerateArray())
            {
                if (tag.ValueKind == JsonValueKind.String)
                {
                    builder.Append(' ').Append(tag.GetString());
                }
            }
        }

        return builder.ToString();
    }

    private static void AppendIfPresent(StringBuilder builder, JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            builder.Append(' ').Append(property.GetString());
        }
    }

    private static IReadOnlyList<string> CapabilityKeywords(ProviderCapabilityKindDto capability)
        => capability switch
        {
            ProviderCapabilityKindDto.Accounts => ["account", "accounts"],
            ProviderCapabilityKindDto.Balances => ["balance", "balances", "cash"],
            ProviderCapabilityKindDto.Positions => ["position", "positions"],
            ProviderCapabilityKindDto.Holdings => ["holding", "holdings"],
            ProviderCapabilityKindDto.Transactions => ["transaction", "transactions", "activity"],
            ProviderCapabilityKindDto.TaxLots => ["taxlot", "taxlots", "tax-lot", "tax-lots", "lot", "lots"],
            ProviderCapabilityKindDto.SecurityReferenceData => ["security", "securities", "instrument", "instruments", "master"],
            ProviderCapabilityKindDto.MarketPrices => ["price", "prices", "quote", "quotes"],
            ProviderCapabilityKindDto.CorporateActions => ["corporateaction", "corporateactions", "corporate-action", "corporate-actions"],
            ProviderCapabilityKindDto.Documents => ["document", "documents", "statement", "statements"],
            ProviderCapabilityKindDto.Alerts => ["alert", "alerts"],
            ProviderCapabilityKindDto.Events => ["event", "events", "webhook", "webhooks"],
            ProviderCapabilityKindDto.OrderPreview => ["orderpreview", "order-preview", "preview"],
            ProviderCapabilityKindDto.OrderPlacement => ["orderplacement", "order-placement", "placeorder", "orders"],
            ProviderCapabilityKindDto.OrderCancellation => ["cancel", "cancellation"],
            ProviderCapabilityKindDto.OrderStatus => ["orderstatus", "order-status", "orders"],
            ProviderCapabilityKindDto.Executions => ["execution", "executions", "fill", "fills"],
            _ => []
        };

    private static bool TryMapMethod(string method, out ProviderIntegrationHttpMethodDto httpMethod)
    {
        var normalized = method.ToLowerInvariant();
        httpMethod = normalized switch
        {
            "get" => ProviderIntegrationHttpMethodDto.Get,
            "post" => ProviderIntegrationHttpMethodDto.Post,
            "put" => ProviderIntegrationHttpMethodDto.Put,
            "patch" => ProviderIntegrationHttpMethodDto.Patch,
            "delete" => ProviderIntegrationHttpMethodDto.Delete,
            _ => ProviderIntegrationHttpMethodDto.Get
        };
        return normalized is "get" or "post" or "put" or "patch" or "delete";
    }

    private static string UniqueEndpointKey(
        HashSet<string> used,
        JsonElement operation,
        ProviderCapabilityKindDto capability,
        ProviderIntegrationHttpMethodDto method,
        string path)
    {
        var baseKey = TryReadString(operation, "$.operationId", out var operationId)
            ? operationId
            : $"{capability}-{method}-{path}";
        var sanitized = SanitizeKey(baseKey);
        var candidate = sanitized;
        var suffix = 2;
        while (!used.Add(candidate))
        {
            candidate = string.Create(
                CultureInfo.InvariantCulture,
                $"{sanitized}-{suffix}");
            suffix++;
        }

        return candidate;
    }

    private static string SanitizeKey(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.Length == 0 ? "endpoint" : builder.ToString().Trim('-');
    }

    private static string InferRecordsPath(
        JsonElement root,
        JsonElement operation,
        ProviderCapabilityKindDto capability)
    {
        var schema = GetResponseSchema(root, operation);
        if (schema.ValueKind == JsonValueKind.Object &&
            TryReadString(schema, "$.type", out var type) &&
            StringComparer.OrdinalIgnoreCase.Equals(type, "array"))
        {
            return "$";
        }

        var resolved = ResolveSchema(root, schema);
        if (resolved.ValueKind != JsonValueKind.Object ||
            !resolved.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return "$";
        }

        foreach (var propertyName in RecordsPathCandidates(capability))
        {
            if (properties.TryGetProperty(propertyName, out var property) &&
                IsArraySchema(root, property))
            {
                return $"$.{propertyName}";
            }
        }

        foreach (var property in properties.EnumerateObject())
        {
            if (IsArraySchema(root, property.Value))
            {
                return $"$.{property.Name}";
            }
        }

        return "$";
    }

    private static IReadOnlyList<string> RecordsPathCandidates(ProviderCapabilityKindDto capability)
        => capability switch
        {
            ProviderCapabilityKindDto.Accounts => ["accounts", "data", "items", "results"],
            ProviderCapabilityKindDto.Balances => ["balances", "cash", "data", "items", "results"],
            ProviderCapabilityKindDto.Positions => ["positions", "holdings", "data", "items", "results"],
            ProviderCapabilityKindDto.Holdings => ["holdings", "positions", "data", "items", "results"],
            ProviderCapabilityKindDto.Transactions => ["transactions", "activity", "data", "items", "results"],
            ProviderCapabilityKindDto.TaxLots => ["taxLots", "tax_lots", "lots", "data", "items", "results"],
            ProviderCapabilityKindDto.SecurityReferenceData => ["securities", "securityMaster", "instruments", "data", "items", "results"],
            ProviderCapabilityKindDto.MarketPrices => ["prices", "quotes", "data", "items", "results"],
            ProviderCapabilityKindDto.CorporateActions => ["corporateActions", "corporate_actions", "data", "items", "results"],
            ProviderCapabilityKindDto.Documents => ["documents", "statements", "data", "items", "results"],
            ProviderCapabilityKindDto.Alerts => ["alerts", "data", "items", "results"],
            ProviderCapabilityKindDto.Events => ["events", "data", "items", "results"],
            ProviderCapabilityKindDto.OrderStatus => ["orders", "statuses", "data", "items", "results"],
            ProviderCapabilityKindDto.Executions => ["executions", "fills", "data", "items", "results"],
            _ => ["data", "items", "results"]
        };

    private static JsonElement GetResponseSchema(JsonElement root, JsonElement operation)
    {
        if (operation.ValueKind != JsonValueKind.Object ||
            !operation.TryGetProperty("responses", out var responses) ||
            responses.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        foreach (var statusCode in new[] { "200", "201", "default" })
        {
            if (responses.TryGetProperty(statusCode, out var response) &&
                TryGetJsonContentSchema(response, out var schema))
            {
                return ResolveSchema(root, schema);
            }
        }

        foreach (var response in responses.EnumerateObject())
        {
            if (TryGetJsonContentSchema(response.Value, out var schema))
            {
                return ResolveSchema(root, schema);
            }
        }

        return default;
    }

    private static bool TryGetJsonContentSchema(JsonElement response, out JsonElement schema)
    {
        schema = default;
        if (response.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (response.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.Object)
        {
            foreach (var mediaType in new[] { "application/json", "application/*+json" })
            {
                if (content.TryGetProperty(mediaType, out var media) &&
                    media.TryGetProperty("schema", out schema))
                {
                    return true;
                }
            }

            foreach (var media in content.EnumerateObject())
            {
                if (media.Name.Contains("json", StringComparison.OrdinalIgnoreCase) &&
                    media.Value.TryGetProperty("schema", out schema))
                {
                    return true;
                }
            }
        }

        return response.TryGetProperty("schema", out schema);
    }

    private static JsonElement ResolveRecordSchema(JsonElement root, JsonElement responseSchema, string recordsPath)
    {
        var schema = ResolveSchema(root, responseSchema);
        if (recordsPath == "$")
        {
            return ResolveArrayItemSchema(root, schema);
        }

        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("properties", out var properties))
        {
            return schema;
        }

        var normalized = recordsPath.StartsWith("$.", StringComparison.Ordinal)
            ? recordsPath[2..]
            : recordsPath;
        foreach (var part in normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!properties.TryGetProperty(part, out schema))
            {
                return default;
            }

            schema = ResolveSchema(root, schema);
            if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out properties))
            {
                continue;
            }
        }

        return ResolveArrayItemSchema(root, schema);
    }

    private static JsonElement ResolveArrayItemSchema(JsonElement root, JsonElement schema)
    {
        var resolved = ResolveSchema(root, schema);
        if (resolved.ValueKind == JsonValueKind.Object &&
            TryReadString(resolved, "$.type", out var type) &&
            StringComparer.OrdinalIgnoreCase.Equals(type, "array") &&
            resolved.TryGetProperty("items", out var items))
        {
            return ResolveSchema(root, items);
        }

        return resolved;
    }

    private static JsonElement ResolveSchema(JsonElement root, JsonElement schema, int depth = 0)
    {
        if (depth > 8 ||
            schema.ValueKind != JsonValueKind.Object ||
            !TryReadString(schema, "$.$ref", out var reference) ||
            !reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return schema;
        }

        var current = root;
        foreach (var segment in reference[2..].Split('/'))
        {
            var unescaped = segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(unescaped, out current))
            {
                return schema;
            }
        }

        return ResolveSchema(root, current, depth + 1);
    }

    private static bool IsArraySchema(JsonElement root, JsonElement schema)
    {
        var resolved = ResolveSchema(root, schema);
        return resolved.ValueKind == JsonValueKind.Object &&
            TryReadString(resolved, "$.type", out var type) &&
            StringComparer.OrdinalIgnoreCase.Equals(type, "array");
    }

    private static IReadOnlyList<SourceSchemaField> FlattenSchemaFields(JsonElement root, JsonElement schema)
    {
        var fields = new List<SourceSchemaField>();
        FlattenSchemaFields(root, ResolveSchema(root, schema), parentPath: null, fields, depth: 0);
        return fields;
    }

    private static void FlattenSchemaFields(
        JsonElement root,
        JsonElement schema,
        string? parentPath,
        List<SourceSchemaField> fields,
        int depth)
    {
        if (depth > 3)
        {
            return;
        }

        var resolved = ResolveSchema(root, schema);
        if (resolved.ValueKind != JsonValueKind.Object ||
            !resolved.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in properties.EnumerateObject())
        {
            var sourcePath = string.IsNullOrWhiteSpace(parentPath)
                ? property.Name
                : $"{parentPath}.{property.Name}";
            fields.Add(new SourceSchemaField(sourcePath, property.Name));
            FlattenSchemaFields(root, property.Value, sourcePath, fields, depth + 1);
        }
    }

    private static SourceFieldSuggestion? FindSourceField(
        string targetField,
        ProviderCapabilityKindDto capability,
        IReadOnlyList<SourceSchemaField> sourceFields)
    {
        var synonyms = SourceSynonyms(capability, targetField);
        foreach (var source in sourceFields)
        {
            if (synonyms.Any(synonym => FieldNameEquals(source.SourcePath, synonym)))
            {
                return new SourceFieldSuggestion(source.SourcePath, ProviderMappingConfidenceDto.High);
            }
        }

        foreach (var source in sourceFields)
        {
            if (synonyms.Any(synonym => FieldNameEquals(source.LeafName, synonym)))
            {
                return new SourceFieldSuggestion(source.SourcePath, ProviderMappingConfidenceDto.Medium);
            }
        }

        return null;
    }

    private static IReadOnlyList<string> SourceSynonyms(
        ProviderCapabilityKindDto capability,
        string targetField)
        => (capability, targetField) switch
        {
            (_, "providerAccountId") => ["providerAccountId", "provider_account_id", "accountId", "account_id", "accountNumber", "account_number", "id"],
            (_, "accountName") => ["accountName", "account_name", "name"],
            (_, "baseCurrency") => ["baseCurrency", "base_currency", "currency", "ccy"],
            (_, "security.cusip") => ["security.cusip", "cusip"],
            (_, "security.name") => ["security.name", "securityName", "security_name", "name", "description"],
            (_, "security.maturityDate") => ["security.maturityDate", "maturityDate", "maturity_date"],
            (_, "quantity") => ["quantity", "qty", "parAmount", "par_amount"],
            (_, "asOf") => ["asOf", "as_of", "asOfDate", "as_of_date", "date"],
            (_, "providerTransactionId") => ["providerTransactionId", "provider_transaction_id", "transactionId", "transaction_id", "id"],
            (_, "transactionType") => ["transactionType", "transaction_type", "type"],
            (_, "postingDate") => ["postingDate", "posting_date", "date"],
            (_, "tradeDate") => ["tradeDate", "trade_date"],
            (_, "settlementDate") => ["settlementDate", "settlement_date"],
            (_, "amount.amount") => ["amount.amount", "amount", "netAmount", "net_amount"],
            (_, "amount.currency") => ["amount.currency", "currency", "ccy"],
            (_, "balance.amount") => ["balance.amount", "balance", "cashBalance", "cash_balance", "amount"],
            (_, "balance.currency") => ["balance.currency", "currency", "ccy"],
            (_, "costBasis.amount") => ["costBasis.amount", "costBasis", "cost_basis"],
            (_, "price.amount") => ["price.amount", "price", "lastPrice", "last_price"],
            (_, "price.currency") => ["price.currency", "currency", "ccy"],
            (_, "corporateActionType") => ["corporateActionType", "corporate_action_type", "type"],
            (_, "effectiveDate") => ["effectiveDate", "effective_date", "date"],
            (_, "documentId") => ["documentId", "document_id", "id"],
            (_, "documentType") => ["documentType", "document_type", "type"],
            (_, "eventId") => ["eventId", "event_id", "id"],
            (_, "eventType") => ["eventType", "event_type", "type"],
            (_, "occurredAt") => ["occurredAt", "occurred_at", "timestamp", "created_at"],
            (_, "orderId") => ["orderId", "order_id", "id"],
            (_, "side") => ["side", "orderSide", "order_side"],
            (_, "orderStatus") => ["orderStatus", "order_status", "status"],
            (_, "updatedAt") => ["updatedAt", "updated_at"],
            (_, "executionId") => ["executionId", "execution_id", "fillId", "fill_id", "id"],
            _ => [targetField, targetField.Replace(".", "_", StringComparison.Ordinal)]
        };

    private static bool FieldNameEquals(string source, string candidate)
        => StringComparer.OrdinalIgnoreCase.Equals(NormalizeFieldName(source), NormalizeFieldName(candidate));

    private static string NormalizeFieldName(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static TransformRuleDto? CreateTransform(string targetField)
    {
        if (targetField.Contains("Date", StringComparison.OrdinalIgnoreCase) ||
            targetField.EndsWith("At", StringComparison.OrdinalIgnoreCase) ||
            StringComparer.OrdinalIgnoreCase.Equals(targetField, "asOf") ||
            StringComparer.OrdinalIgnoreCase.Equals(targetField, "effectiveDate"))
        {
            return new TransformRuleDto("date", new Dictionary<string, string>());
        }

        if (targetField.Contains("amount", StringComparison.OrdinalIgnoreCase) ||
            StringComparer.OrdinalIgnoreCase.Equals(targetField, "quantity") ||
            StringComparer.OrdinalIgnoreCase.Equals(targetField, "security.coupon"))
        {
            return new TransformRuleDto("decimal", new Dictionary<string, string>());
        }

        if (targetField.Contains("currency", StringComparison.OrdinalIgnoreCase) ||
            targetField.Contains("cusip", StringComparison.OrdinalIgnoreCase) ||
            targetField.Contains("isin", StringComparison.OrdinalIgnoreCase) ||
            targetField.Contains("symbol", StringComparison.OrdinalIgnoreCase))
        {
            return new TransformRuleDto("uppercase", new Dictionary<string, string>());
        }

        return new TransformRuleDto("trim", new Dictionary<string, string>());
    }

    private static string? DefaultValueFor(string targetField)
        => targetField.Contains("currency", StringComparison.OrdinalIgnoreCase) ? "USD" : null;

    private static bool TryReadString(JsonElement root, string path, out string value)
    {
        value = string.Empty;
        var current = root;
        var normalized = path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path;
        foreach (var part in normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind == JsonValueKind.Array &&
                int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                index >= 0 &&
                index < current.GetArrayLength())
            {
                current = current[index];
                continue;
            }

            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return false;
            }
        }

        if (current.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = current.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private sealed record SourceSchemaField(string SourcePath, string LeafName);

    private sealed record SourceFieldSuggestion(string SourcePath, ProviderMappingConfidenceDto Confidence);

    private sealed class MappingKeyComparer : IEqualityComparer<(ProviderCapabilityKindDto Capability, string TargetField)>
    {
        public bool Equals(
            (ProviderCapabilityKindDto Capability, string TargetField) x,
            (ProviderCapabilityKindDto Capability, string TargetField) y)
            => x.Capability == y.Capability &&
               StringComparer.OrdinalIgnoreCase.Equals(x.TargetField, y.TargetField);

        public int GetHashCode((ProviderCapabilityKindDto Capability, string TargetField) obj)
            => HashCode.Combine(obj.Capability, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TargetField));
    }
}
