using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationQuarantineReplayService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IProviderIntegrationManifestStore store;

    public ProviderIntegrationQuarantineReplayService(IProviderIntegrationManifestStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ProviderIntegrationQuarantineReplayResultDto> ReplayAsync(
        ProviderIntegrationQuarantineReplayRequestDto request,
        CancellationToken ct = default)
        => await ReplayAsync(null, request, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationQuarantineReplayResultDto> ReplayAsync(
        string? tenantId,
        ProviderIntegrationQuarantineReplayRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReplaySyncRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceSyncRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManifestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestedBy);
        if (request.QuarantineRecordIds is null || request.QuarantineRecordIds.Count == 0)
        {
            throw new ArgumentException("At least one quarantine record id is required.", nameof(request));
        }

        if (request.QuarantineRecordIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Quarantine record ids cannot be blank.", nameof(request));
        }

        ct.ThrowIfCancellationRequested();

        var scopedStore = ResolveStore(tenantId);
        var manifest = await scopedStore.GetManifestAsync(request.ManifestId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration manifest '{request.ManifestId}' was not found.");
        var connection = await scopedStore.GetConnectionAsync(request.ConnectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{request.ConnectionId}' was not found.");
        var sourceSyncRun = await scopedStore.GetSyncRunAsync(request.SourceSyncRunId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration sync run '{request.SourceSyncRunId}' was not found.");

        var capability = ValidateRequestScope(request, manifest, connection, sourceSyncRun);
        var sourceRecords = await ResolveRequestedRecordsAsync(scopedStore, request, ct).ConfigureAwait(false);
        var rawPayloadId = StableId("raw-payload", request.ReplaySyncRunId, request.SourceSyncRunId, "quarantine-replay");

        await scopedStore.SaveRawPayloadAsync(
            new RawIngestionPayloadDto(
                rawPayloadId,
                manifest.ProviderId,
                connection.ConnectionId,
                request.Capability,
                sourceSyncRun.EndpointKey,
                request.ReplaySyncRunId,
                request.RequestedAt,
                new Dictionary<string, string>
                {
                    ["sourceSyncRunId"] = request.SourceSyncRunId,
                    ["sourceRawPayloadId"] = sourceSyncRun.RawPayloadId ?? string.Empty,
                    ["requestedBy"] = request.RequestedBy,
                    ["recordCount"] = sourceRecords.Count.ToString(CultureInfo.InvariantCulture)
                },
                ToJsonElement(new QuarantineReplayRawPayload(
                    request.SourceSyncRunId,
                    sourceSyncRun.RawPayloadId,
                    sourceRecords.Select(record => new QuarantineReplayRawRecord(
                        record.QuarantineRecordId,
                        record.RawRecord)).ToArray())),
                $"{manifest.ManifestId}:v{manifest.ManifestVersion.ToString(CultureInfo.InvariantCulture)}",
                ProviderIntegrationProcessingStatusDto.Received),
            ct).ConfigureAwait(false);

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
                "Map the provider fields before replaying quarantined records.");
            var blocked = new ProviderIntegrationQuarantineReplayResultDto(
                request.ReplaySyncRunId,
                rawPayloadId,
                request.Capability,
                sourceRecords.Count,
                RecordsAccepted: 0,
                RecordsRequarantined: 0,
                ProviderIntegrationProcessingStatusDto.Blocked,
                [issue]);
            await SaveSyncRunAsync(scopedStore, request, manifest, sourceSyncRun.EndpointKey, rawPayloadId, blocked, ct)
                .ConfigureAwait(false);
            return blocked;
        }

        var requiredCanonicalFields = mappings
            .Where(mapping => mapping.Required)
            .Select(mapping => mapping.TargetField)
            .Concat(capability.RequiredCanonicalFields)
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var allIssues = new List<ValidationIssueDto>();
        var accepted = 0;
        var requarantined = 0;

        for (var index = 0; index < sourceRecords.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var sourceRecord = sourceRecords[index];
            var rowIssues = new List<ValidationIssueDto>();
            var mappedRecord = MapRecord(sourceRecord.RawRecord, mappings, rowIssues);
            foreach (var requiredField in requiredCanonicalFields)
            {
                if (!HasJsonPath(mappedRecord, requiredField))
                {
                    rowIssues.Add(new ValidationIssueDto(
                        "required.missing",
                        ProviderIntegrationIssueSeverityDto.Critical,
                        $"Required field '{requiredField}' is missing.",
                        requiredField,
                        "Map a provider response field, provide a constant, or configure a default value before replay."));
                }
            }

            allIssues.AddRange(rowIssues);
            var mappedElement = ToJsonElement(mappedRecord);
            var sourceRecordId = ReadJsonString(mappedRecord, "sourceRecordId");
            var ordinal = (index + 1).ToString(CultureInfo.InvariantCulture);

            if (rowIssues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical))
            {
                requarantined++;
                await scopedStore.SaveQuarantinedRecordAsync(
                    new QuarantinedRecordDto(
                        StableId("quarantine", request.ReplaySyncRunId, sourceRecord.QuarantineRecordId),
                        request.ReplaySyncRunId,
                        connection.ConnectionId,
                        request.Capability,
                        sourceRecord.RawRecord,
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
                    StableId("staging", request.ReplaySyncRunId, sourceRecord.QuarantineRecordId),
                    request.ReplaySyncRunId,
                    connection.ConnectionId,
                    request.Capability,
                    rawPayloadId,
                    sourceRecordId,
                    BuildDedupeKey(connection.ConnectionId, request.Capability, ordinal, sourceRecordId ?? sourceRecord.QuarantineRecordId),
                    mappedElement,
                    rowIssues.Where(issue => issue.Severity != ProviderIntegrationIssueSeverityDto.Critical).ToArray(),
                    ProviderIntegrationProcessingStatusDto.Validated,
                    request.RequestedAt),
                ct).ConfigureAwait(false);
        }

        var status = allIssues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical)
            ? ProviderIntegrationProcessingStatusDto.Quarantined
            : ProviderIntegrationProcessingStatusDto.Validated;
        var result = new ProviderIntegrationQuarantineReplayResultDto(
            request.ReplaySyncRunId,
            rawPayloadId,
            request.Capability,
            sourceRecords.Count,
            accepted,
            requarantined,
            status,
            allIssues);
        await SaveSyncRunAsync(scopedStore, request, manifest, sourceSyncRun.EndpointKey, rawPayloadId, result, ct)
            .ConfigureAwait(false);
        return result;
    }

    private async Task<IReadOnlyList<QuarantinedRecordDto>> ResolveRequestedRecordsAsync(
        IProviderIntegrationManifestStore scopedStore,
        ProviderIntegrationQuarantineReplayRequestDto request,
        CancellationToken ct)
    {
        var sourceRecords = await scopedStore.ListQuarantinedRecordsAsync(request.SourceSyncRunId, ct).ConfigureAwait(false);
        var byId = sourceRecords.ToDictionary(record => record.QuarantineRecordId, StringComparer.Ordinal);
        var requested = new List<QuarantinedRecordDto>();
        foreach (var quarantineRecordId in request.QuarantineRecordIds.Distinct(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (!byId.TryGetValue(quarantineRecordId, out var record))
            {
                throw new KeyNotFoundException($"Provider integration quarantine record '{quarantineRecordId}' was not found.");
            }

            if (!StringComparer.Ordinal.Equals(record.ConnectionId, request.ConnectionId))
            {
                throw new InvalidOperationException("Provider integration quarantine record is not linked to the requested connection.");
            }

            if (record.Capability != request.Capability)
            {
                throw new InvalidOperationException("Provider integration quarantine record capability does not match the replay request.");
            }

            requested.Add(record);
        }

        return requested;
    }

    private static ProviderCapabilityDto ValidateRequestScope(
        ProviderIntegrationQuarantineReplayRequestDto request,
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection,
        ProviderIntegrationSyncRunDto sourceSyncRun)
    {
        if (!StringComparer.Ordinal.Equals(connection.ManifestId, manifest.ManifestId))
        {
            throw new InvalidOperationException("The provider connection is not linked to the requested manifest.");
        }

        if (!StringComparer.Ordinal.Equals(sourceSyncRun.ManifestId, request.ManifestId) ||
            !StringComparer.Ordinal.Equals(sourceSyncRun.ConnectionId, request.ConnectionId) ||
            sourceSyncRun.Capability != request.Capability)
        {
            throw new InvalidOperationException("The source sync run is not linked to the requested manifest, connection, and capability.");
        }

        if (!connection.EnabledCapabilities.Contains(request.Capability))
        {
            throw new InvalidOperationException($"The provider connection has not enabled {request.Capability}.");
        }

        var capability = manifest.Capabilities.FirstOrDefault(candidate =>
                candidate.Capability == request.Capability && candidate.Enabled)
            ?? throw new InvalidOperationException($"The manifest does not enable {request.Capability}.");
        if (capability.RequiresCertifiedAdapter)
        {
            throw new InvalidOperationException("Quarantine replay does not execute capabilities that require certified adapters.");
        }

        return capability;
    }

    private Task SaveSyncRunAsync(
        IProviderIntegrationManifestStore scopedStore,
        ProviderIntegrationQuarantineReplayRequestDto request,
        ProviderIntegrationManifestDto manifest,
        string endpointKey,
        string rawPayloadId,
        ProviderIntegrationQuarantineReplayResultDto result,
        CancellationToken ct)
        => scopedStore.SaveSyncRunAsync(
            new ProviderIntegrationSyncRunDto(
                request.ReplaySyncRunId,
                manifest.ManifestId,
                request.ConnectionId,
                manifest.ProviderId,
                request.Capability,
                endpointKey,
                request.RequestedAt,
                request.RequestedAt,
                result.Status,
                result.RecordsReplayed,
                result.RecordsAccepted,
                result.RecordsRequarantined,
                rawPayloadId,
                result.Issues),
            ct);

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;

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
            "uppercase" or "trimuppercase" => value.Trim().ToUpperInvariant(),
            "lowercase" or "trimlowercase" => value.Trim().ToLowerInvariant(),
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

    private static string? ReadJsonString(JsonObject root, string targetField)
    {
        JsonNode? current = root;
        foreach (var part in targetField.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is not JsonObject currentObject ||
                !currentObject.TryGetPropertyValue(part, out current) ||
                current is null)
            {
                return null;
            }
        }

        return current is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
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

    private static string BuildDedupeKey(
        string connectionId,
        ProviderCapabilityKindDto capability,
        string ordinal,
        string? sourceRecordId)
        => string.Join(
            ':',
            connectionId,
            capability.ToString(),
            string.IsNullOrWhiteSpace(sourceRecordId) ? ordinal : sourceRecordId);

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

    private sealed record QuarantineReplayRawPayload(
        string SourceSyncRunId,
        string? SourceRawPayloadId,
        IReadOnlyList<QuarantineReplayRawRecord> Records);

    private sealed record QuarantineReplayRawRecord(
        string QuarantineRecordId,
        JsonElement RawRecord);
}
