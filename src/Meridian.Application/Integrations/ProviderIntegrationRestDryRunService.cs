using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

public interface IProviderIntegrationHttpTransport
{
    Task<ProviderIntegrationHttpResponse> SendAsync(
        ProviderIntegrationHttpRequest request,
        CancellationToken ct = default);
}

public sealed record ProviderIntegrationHttpRequest(
    ProviderIntegrationHttpMethodDto Method,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Query,
    string? BodyTemplate);

public sealed record ProviderIntegrationHttpResponse(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    string Body);

public sealed class ProviderIntegrationRestDryRunService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IProviderIntegrationManifestStore store;
    private readonly IProviderIntegrationHttpTransport transport;

    public ProviderIntegrationRestDryRunService(
        IProviderIntegrationManifestStore store,
        IProviderIntegrationHttpTransport transport)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<ProviderIntegrationDryRunResultDto> RunRestDryRunAsync(
        ProviderIntegrationRestDryRunRequestDto request,
        CancellationToken ct = default)
        => await RunRestDryRunAsync(null, request, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationDryRunResultDto> RunRestDryRunAsync(
        string? tenantId,
        ProviderIntegrationRestDryRunRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SyncRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManifestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EndpointKey);

        ct.ThrowIfCancellationRequested();

        var scopedStore = ResolveStore(tenantId);
        var manifest = await scopedStore.GetManifestAsync(request.ManifestId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration manifest '{request.ManifestId}' was not found.");
        var connection = await scopedStore.GetConnectionAsync(request.ConnectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{request.ConnectionId}' was not found.");

        ValidateRequestScope(request, manifest, connection);
        var endpoint = manifest.Endpoints.FirstOrDefault(candidate =>
                StringComparer.OrdinalIgnoreCase.Equals(candidate.EndpointKey, request.EndpointKey) &&
                candidate.Capability == request.Capability)
            ?? throw new InvalidOperationException($"Endpoint '{request.EndpointKey}' is not configured for {request.Capability}.");
        var mappings = manifest.FieldMappings
            .Where(mapping => mapping.Capability == request.Capability)
            .ToArray();
        if (mappings.Length == 0)
        {
            var issue = new ValidationIssueDto(
                "mapping.missing",
                ProviderIntegrationIssueSeverityDto.Critical,
                $"No field mappings are configured for {request.Capability}.",
                null,
                "Map provider response fields to the required canonical fields before running a REST dry run.");

            var blockedResult = new ProviderIntegrationDryRunResultDto(
                request.SyncRunId,
                RawPayloadId: string.Empty,
                request.Capability,
                RecordsReceived: 0,
                RecordsAccepted: 0,
                RecordsQuarantined: 0,
                ProviderIntegrationProcessingStatusDto.Blocked,
                [issue]);
            await SaveSyncRunAsync(scopedStore, request, manifest, connection, endpoint.EndpointKey, null, blockedResult, ct).ConfigureAwait(false);
            return blockedResult;
        }

        var capability = manifest.Capabilities.First(candidate => candidate.Capability == request.Capability);
        var requiredCanonicalFields = mappings
            .Where(mapping => mapping.Required)
            .Select(mapping => mapping.TargetField)
            .Concat(capability.RequiredCanonicalFields)
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var allIssues = new List<ValidationIssueDto>();
        var received = 0;
        var accepted = 0;
        var quarantined = 0;
        string? firstPayloadId = null;
        string? cursor = null;
        var maxPages = request.MaxPages <= 0 ? 1 : request.MaxPages;
        var dedupeValidator = new ProviderIntegrationStagingDedupeValidator();

        for (var page = 1; page <= maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            var httpRequest = BuildHttpRequest(endpoint, request, cursor);
            var response = await transport.SendAsync(httpRequest, ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(response.Body);
            var responseBody = document.RootElement.Clone();
            var payloadId = StableId("raw-payload", request.SyncRunId, endpoint.EndpointKey, page.ToString(CultureInfo.InvariantCulture));
            firstPayloadId ??= payloadId;

            await scopedStore.SaveRawPayloadAsync(
                new RawIngestionPayloadDto(
                    payloadId,
                    manifest.ProviderId,
                    connection.ConnectionId,
                    request.Capability,
                    endpoint.EndpointKey,
                    request.SyncRunId,
                    request.RequestedAt,
                    new Dictionary<string, string>
                    {
                        ["method"] = endpoint.Method.ToString(),
                        ["path"] = httpRequest.Path,
                        ["page"] = page.ToString(CultureInfo.InvariantCulture),
                        ["statusCode"] = response.StatusCode.ToString(CultureInfo.InvariantCulture),
                        ["requestedBy"] = request.RequestedBy
                    },
                    responseBody,
                    $"{manifest.ManifestId}:v{manifest.ManifestVersion.ToString(CultureInfo.InvariantCulture)}",
                    ProviderIntegrationProcessingStatusDto.Received),
                ct).ConfigureAwait(false);

            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                allIssues.Add(new ValidationIssueDto(
                    "http.status.failed",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    $"Provider endpoint returned HTTP {response.StatusCode}.",
                    null,
                    "Confirm credentials, endpoint path, parameters, and provider availability before activation."));
                break;
            }

            var rawRecords = ExtractRecords(responseBody, endpoint.Response.RecordsPath);
            received += rawRecords.Count;
            foreach (var rawRecord in rawRecords)
            {
                ct.ThrowIfCancellationRequested();
                var rowIssues = new List<ValidationIssueDto>();
                var mappedRecord = MapRecord(rawRecord, mappings, rowIssues);
                foreach (var requiredField in requiredCanonicalFields)
                {
                    if (!HasJsonPath(mappedRecord, requiredField))
                    {
                        rowIssues.Add(new ValidationIssueDto(
                            "required.missing",
                            ProviderIntegrationIssueSeverityDto.Critical,
                            $"Required field '{requiredField}' is missing.",
                            requiredField,
                        "Map a provider response field, provide a constant, or configure a default value."));
                    }
                }

                ProviderIntegrationMappedRecordValidation.AddValidationIssues(request.Capability, mappedRecord, rowIssues);
                allIssues.AddRange(rowIssues);
                var mappedElement = ToJsonElement(mappedRecord);
                var sourceRecordId = ProviderIntegrationMappedRecordIdentity.ResolveSourceRecordId(request.Capability, mappedRecord);
                var recordOrdinal = (accepted + quarantined + 1).ToString(CultureInfo.InvariantCulture);
                var dedupeKey = ProviderIntegrationMappedRecordIdentity.BuildDedupeKey(
                    connection.ConnectionId,
                    request.Capability,
                    recordOrdinal,
                    sourceRecordId);

                if (!rowIssues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical))
                {
                    var duplicateIssue = dedupeValidator.TryAccept(
                        dedupeKey,
                        request.Capability,
                        ProviderIntegrationMappedRecordIdentity.ResolveSourceIdentityTargetField(request.Capability));
                    if (duplicateIssue is not null)
                    {
                        rowIssues.Add(duplicateIssue);
                        allIssues.Add(duplicateIssue);
                    }
                }

                if (rowIssues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical))
                {
                    quarantined++;
                    await scopedStore.SaveQuarantinedRecordAsync(
                        new QuarantinedRecordDto(
                            StableId("quarantine", request.SyncRunId, endpoint.EndpointKey, recordOrdinal),
                            request.SyncRunId,
                            connection.ConnectionId,
                            request.Capability,
                            rawRecord,
                            mappedElement,
                            rowIssues,
                            ProviderIntegrationProcessingStatusDto.Quarantined,
                            request.RequestedAt),
                        ct).ConfigureAwait(false);
                    continue;
                }

                accepted++;
                await scopedStore.SaveStagingRecordAsync(
                    new IntegrationStagingRecordDto(
                        StableId("staging", request.SyncRunId, endpoint.EndpointKey, recordOrdinal),
                        request.SyncRunId,
                        connection.ConnectionId,
                        request.Capability,
                        payloadId,
                        sourceRecordId,
                        dedupeKey,
                        mappedElement,
                        rowIssues.Where(issue => issue.Severity != ProviderIntegrationIssueSeverityDto.Critical).ToArray(),
                        ProviderIntegrationProcessingStatusDto.Validated,
                        request.RequestedAt),
                    ct).ConfigureAwait(false);
            }

            cursor = endpoint.Pagination.Type == ProviderIntegrationPaginationTypeDto.Cursor &&
                !string.IsNullOrWhiteSpace(endpoint.Pagination.CursorPath)
                    ? ReadJsonString(responseBody, endpoint.Pagination.CursorPath)
                    : null;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        if (received == 0 && allIssues.All(issue => issue.Code != "http.status.failed"))
        {
            allIssues.Add(new ValidationIssueDto(
                "response.no-records",
                ProviderIntegrationIssueSeverityDto.Warning,
                $"No records were found at '{endpoint.Response.RecordsPath}'.",
                endpoint.Response.RecordsPath,
                "Confirm the response records path or fetch a representative sample response."));
        }

        var status = allIssues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical)
            ? ProviderIntegrationProcessingStatusDto.Quarantined
            : ProviderIntegrationProcessingStatusDto.Validated;

        var result = new ProviderIntegrationDryRunResultDto(
            request.SyncRunId,
            firstPayloadId ?? string.Empty,
            request.Capability,
            received,
            accepted,
            quarantined,
            status,
            allIssues);
        await SaveSyncRunAsync(scopedStore, request, manifest, connection, endpoint.EndpointKey, firstPayloadId, result, ct).ConfigureAwait(false);
        return result;
    }

    private Task SaveSyncRunAsync(
        IProviderIntegrationManifestStore scopedStore,
        ProviderIntegrationRestDryRunRequestDto request,
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection,
        string endpointKey,
        string? rawPayloadId,
        ProviderIntegrationDryRunResultDto result,
        CancellationToken ct)
        => scopedStore.SaveSyncRunAsync(
            new ProviderIntegrationSyncRunDto(
                request.SyncRunId,
                manifest.ManifestId,
                connection.ConnectionId,
                manifest.ProviderId,
                request.Capability,
                endpointKey,
                request.RequestedAt,
                request.RequestedAt,
                result.Status,
                result.RecordsReceived,
                result.RecordsAccepted,
                result.RecordsQuarantined,
                rawPayloadId,
                result.Issues),
            ct);

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;

    private static void ValidateRequestScope(
        ProviderIntegrationRestDryRunRequestDto request,
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection)
    {
        if (!StringComparer.Ordinal.Equals(connection.ManifestId, manifest.ManifestId))
        {
            throw new InvalidOperationException("The provider connection is not linked to the requested manifest.");
        }

        if (!connection.EnabledCapabilities.Contains(request.Capability))
        {
            throw new InvalidOperationException($"The provider connection has not enabled {request.Capability}.");
        }

        if (manifest.IntegrationType is not IntegrationTypeDto.Rest and not IntegrationTypeDto.OpenApiRest and not IntegrationTypeDto.Hybrid)
        {
            throw new InvalidOperationException("REST dry runs require a REST, OpenAPI REST, or hybrid integration manifest.");
        }

        var capability = manifest.Capabilities.FirstOrDefault(candidate => candidate.Capability == request.Capability && candidate.Enabled)
            ?? throw new InvalidOperationException($"The manifest does not enable {request.Capability}.");
        if (capability.RequiresCertifiedAdapter)
        {
            throw new InvalidOperationException("REST dry runs do not execute capabilities that require certified adapters.");
        }
    }

    private static ProviderIntegrationHttpRequest BuildHttpRequest(
        EndpointDefinitionDto endpoint,
        ProviderIntegrationRestDryRunRequestDto request,
        string? cursor)
    {
        var path = endpoint.Path;
        foreach (var parameter in request.PathParameters)
        {
            path = path.Replace($"{{{parameter.Key}}}", parameter.Value, StringComparison.OrdinalIgnoreCase);
        }

        if (path.Contains('{', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Endpoint path '{endpoint.Path}' still has unresolved path parameters.");
        }

        var query = new Dictionary<string, string>(endpoint.Query, StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in request.QueryParameters)
        {
            query[parameter.Key] = parameter.Value;
        }

        if (!string.IsNullOrWhiteSpace(cursor) &&
            !string.IsNullOrWhiteSpace(endpoint.Pagination.CursorParam))
        {
            query[endpoint.Pagination.CursorParam] = cursor;
        }

        return new ProviderIntegrationHttpRequest(
            endpoint.Method,
            path,
            new Dictionary<string, string>(endpoint.Headers, StringComparer.OrdinalIgnoreCase),
            query,
            endpoint.RequestBodyTemplate);
    }

    private static IReadOnlyList<JsonElement> ExtractRecords(JsonElement responseBody, string recordsPath)
    {
        if (string.IsNullOrWhiteSpace(recordsPath) ||
            StringComparer.Ordinal.Equals(recordsPath.Trim(), "$"))
        {
            if (responseBody.ValueKind == JsonValueKind.Array)
            {
                return responseBody.EnumerateArray().Select(record => record.Clone()).ToArray();
            }

            return responseBody.ValueKind == JsonValueKind.Object ? [responseBody.Clone()] : [];
        }

        var current = responseBody;
        foreach (var part in NormalizeSourcePath(recordsPath).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(part, out current))
            {
                return [];
            }
        }

        if (current.ValueKind == JsonValueKind.Array)
        {
            return current.EnumerateArray().Select(record => record.Clone()).ToArray();
        }

        return current.ValueKind == JsonValueKind.Object ? [current.Clone()] : [];
    }

    private static JsonObject MapRecord(
        JsonElement record,
        IReadOnlyList<FieldMappingDto> mappings,
        List<ValidationIssueDto> issues)
    {
        var mapped = new JsonObject();
        foreach (var mapping in mappings)
        {
            var value = ResolveMappedValue(record, mapping);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (mapping.Required)
                {
                    issues.Add(new ValidationIssueDto(
                        "source.required.missing",
                        ProviderIntegrationIssueSeverityDto.Critical,
                        $"Required source path '{mapping.SourcePath}' is blank or missing.",
                        mapping.TargetField,
                        "Map a populated provider response field, provide a constant, or configure a default value."));
                }

                continue;
            }

            var transformed = ApplyTransform(value, mapping, record, issues);
            if (transformed is not null)
            {
                SetJsonPath(mapped, mapping.TargetField, transformed);
            }
        }

        return mapped;
    }

    private static string? ResolveMappedValue(JsonElement record, FieldMappingDto mapping)
    {
        if (!string.IsNullOrWhiteSpace(mapping.ConstantValue))
        {
            return mapping.ConstantValue;
        }

        var value = ReadJsonString(record, mapping.SourcePath);
        return string.IsNullOrWhiteSpace(value) ? mapping.DefaultValue : value;
    }

    private static object? ApplyTransform(
        string value,
        FieldMappingDto mapping,
        JsonElement record,
        List<ValidationIssueDto> issues)
    {
        var transformType = mapping.Transform?.Type.Trim();
        if (string.IsNullOrWhiteSpace(transformType))
        {
            return value;
        }

        return transformType.ToLowerInvariant() switch
        {
            "trim" => value.Trim(),
            "uppercase" => value.Trim().ToUpperInvariant(),
            "lowercase" => value.Trim().ToLowerInvariant(),
            "decimal" or "decimalparsing" => ParseDecimal(value, mapping.TargetField, issues),
            "signedamount" => ParseSignedAmount(value, mapping, record, issues),
            "date" or "dateparsing" or "isodate" => ParseDate(value, mapping.TargetField, issues),
            "enum" or "enummapping" => MapEnum(value, mapping, issues),
            _ => value
        };
    }

    private static object? MapEnum(string value, FieldMappingDto mapping, List<ValidationIssueDto> issues)
    {
        var normalized = value.Trim();
        if (mapping.Transform?.Parameters.TryGetValue(normalized, out var mapped) == true ||
            mapping.Transform?.Parameters.TryGetValue(normalized.ToUpperInvariant(), out mapped) == true)
        {
            return mapped;
        }

        issues.Add(new ValidationIssueDto(
            "transform.enum.unmapped",
            ProviderIntegrationIssueSeverityDto.Critical,
            $"Value '{value}' is not mapped to an allowed canonical value.",
            mapping.TargetField,
            "Add this provider value to the enum mapping before activation."));
        return null;
    }

    private static object? ParseSignedAmount(
        string value,
        FieldMappingDto mapping,
        JsonElement record,
        List<ValidationIssueDto> issues)
    {
        var parsed = ParseDecimal(value, mapping.TargetField, issues);
        if (parsed is not decimal amount)
        {
            return null;
        }

        var conditionPath = GetTransformParameter(mapping, "conditionSourcePath") ??
            GetTransformParameter(mapping, "conditionColumn");
        if (string.IsNullOrWhiteSpace(conditionPath))
        {
            return amount;
        }

        var conditionValue = ReadJsonString(record, conditionPath);
        if (string.IsNullOrWhiteSpace(conditionValue))
        {
            return amount;
        }

        var negativeValues = SplitTransformList(GetTransformParameter(mapping, "negativeValues"));
        return negativeValues.Contains(conditionValue.Trim(), StringComparer.OrdinalIgnoreCase)
            ? -Math.Abs(amount)
            : amount;
    }

    private static object? ParseDecimal(string value, string targetField, List<ValidationIssueDto> issues)
    {
        var normalized = value.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        issues.Add(new ValidationIssueDto(
            "transform.decimal.invalid",
            ProviderIntegrationIssueSeverityDto.Critical,
            $"Value '{value}' could not be parsed as a decimal.",
            targetField,
            "Confirm the source number format or choose the correct decimal parsing transform."));
        return null;
    }

    private static object? ParseDate(string value, string targetField, List<ValidationIssueDto> issues)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        issues.Add(new ValidationIssueDto(
            "transform.date.invalid",
            ProviderIntegrationIssueSeverityDto.Critical,
            $"Value '{value}' could not be parsed as a date.",
            targetField,
            "Confirm the provider date format or choose the correct date parsing transform."));
        return null;
    }

    private static string? GetTransformParameter(FieldMappingDto mapping, string key)
        => mapping.Transform?.Parameters.TryGetValue(key, out var value) == true ? value : null;

    private static IReadOnlyList<string> SplitTransformList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? ReadJsonString(JsonElement root, string sourcePath)
    {
        var current = root;
        foreach (var part in NormalizeSourcePath(sourcePath).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(part, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static bool HasJsonPath(JsonObject root, string targetField)
    {
        JsonNode? current = root;
        foreach (var part in targetField.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is not JsonObject currentObject ||
                !currentObject.TryGetPropertyValue(part, out current) ||
                current is null)
            {
                return false;
            }
        }

        return current switch
        {
            JsonValue value when value.TryGetValue<string>(out var text) => !string.IsNullOrWhiteSpace(text),
            _ => true
        };
    }

    private static void SetJsonPath(JsonObject root, string targetField, object value)
    {
        var parts = targetField.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return;
        }

        var current = root;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (current[parts[index]] is not JsonObject child)
            {
                child = [];
                current[parts[index]] = child;
            }

            current = child;
        }

        current[parts[^1]] = value switch
        {
            decimal decimalValue => JsonValue.Create(decimalValue),
            bool boolValue => JsonValue.Create(boolValue),
            _ => JsonValue.Create(value.ToString())
        };
    }

    private static string NormalizeSourcePath(string sourcePath)
    {
        var normalized = sourcePath.Trim();
        if (normalized.StartsWith("$.", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized.StartsWith("$['", StringComparison.Ordinal) &&
            normalized.EndsWith("']", StringComparison.Ordinal))
        {
            normalized = normalized[3..^2];
        }

        return normalized;
    }

    private static JsonElement ToJsonElement<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string StableId(params string[] parts)
    {
        var input = string.Join("|", parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }
}
