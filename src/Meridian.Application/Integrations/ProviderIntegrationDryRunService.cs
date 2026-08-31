using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Meridian.Application.Integrations.ProviderIntegrationFieldTransforms;
using Meridian.Contracts.Integrity;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationDryRunService
{
    private const string ManualCsvEndpointKey = "manual-csv-upload";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IProviderIntegrationManifestStore store;
    private readonly ILogger<ProviderIntegrationDryRunService> logger;

    public ProviderIntegrationDryRunService(
        IProviderIntegrationManifestStore store,
        ILogger<ProviderIntegrationDryRunService>? logger = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.logger = logger ?? NullLogger<ProviderIntegrationDryRunService>.Instance;
    }

    public async Task<ProviderIntegrationDryRunResultDto> RunManualCsvDryRunAsync(
        ManualCsvProviderIntegrationDryRunRequestDto request,
        CancellationToken ct = default)
        => await RunManualCsvDryRunAsync(null, request, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationDryRunResultDto> RunManualCsvDryRunAsync(
        string? tenantId,
        ManualCsvProviderIntegrationDryRunRequestDto request,
        CancellationToken ct = default)
        => await ProviderIntegrationServiceBoundary.RunAsync(
            logger,
            "manual-csv-dry-run",
            new ProviderIntegrationBoundaryContext(
                TenantId: tenantId,
                ManifestId: request?.ManifestId,
                ConnectionId: request?.ConnectionId,
                Capability: request is null ? null : request.Capability.ToString(),
                EndpointKey: ManualCsvEndpointKey,
                SyncRunId: request?.SyncRunId),
            () => RunManualCsvDryRunCoreAsync(tenantId, request, ct)).ConfigureAwait(false);

    private async Task<ProviderIntegrationDryRunResultDto> RunManualCsvDryRunCoreAsync(
        string? tenantId,
        ManualCsvProviderIntegrationDryRunRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SyncRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManifestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);

        ct.ThrowIfCancellationRequested();

        var scopedStore = ResolveStore(tenantId);
        var manifest = await scopedStore.GetManifestAsync(request.ManifestId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration manifest '{request.ManifestId}' was not found.");
        var connection = await scopedStore.GetConnectionAsync(request.ConnectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{request.ConnectionId}' was not found.");

        ValidateRequestScope(request, manifest, connection);

        var csvRecords = ParseCsv(request.CsvContent);
        var payloadId = StableId("raw-payload", request.SyncRunId, request.FileName);
        var mappingVersion = $"{manifest.ManifestId}:v{manifest.ManifestVersion.ToString(CultureInfo.InvariantCulture)}";
        var rawPayload = new RawIngestionPayloadDto(
            payloadId,
            manifest.ProviderId,
            connection.ConnectionId,
            request.Capability,
            ManualCsvEndpointKey,
            request.SyncRunId,
            request.RequestedAt,
            new Dictionary<string, string>
            {
                ["fileName"] = request.FileName,
                ["requestedBy"] = request.RequestedBy,
                ["integrationType"] = manifest.IntegrationType.ToString()
            },
            ToJsonElement(new ManualCsvRawPayload(request.FileName, "text/csv", csvRecords.Count, csvRecords)),
            mappingVersion,
            ProviderIntegrationProcessingStatusDto.Received);

        await scopedStore.SaveRawPayloadAsync(rawPayload, ct).ConfigureAwait(false);

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
                "Map the uploaded file columns to the required canonical fields before running a dry run.");

            var blockedResult = new ProviderIntegrationDryRunResultDto(
                request.SyncRunId,
                payloadId,
                request.Capability,
                csvRecords.Count,
                RecordsAccepted: 0,
                RecordsQuarantined: 0,
                ProviderIntegrationProcessingStatusDto.Blocked,
                [issue]);
            await SaveSyncRunAsync(scopedStore, request, manifest, connection, payloadId, blockedResult, ct).ConfigureAwait(false);
            return blockedResult;
        }

        var capability = manifest.Capabilities.FirstOrDefault(candidate => candidate.Capability == request.Capability);
        var requiredCanonicalFields = mappings
            .Where(mapping => mapping.Required)
            .Select(mapping => mapping.TargetField)
            .Concat(capability?.RequiredCanonicalFields ?? [])
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var allIssues = new List<ValidationIssueDto>();
        var accepted = 0;
        var quarantined = 0;
        var dedupeValidator = new ProviderIntegrationStagingDedupeValidator();

        if (csvRecords.Count == 0)
        {
            allIssues.Add(new ValidationIssueDto(
                "csv.no-records",
                ProviderIntegrationIssueSeverityDto.Critical,
                "The uploaded CSV did not contain any data records.",
                null,
                "Upload a CSV with one header row and at least one data row."));
        }

        foreach (var record in csvRecords)
        {
            ct.ThrowIfCancellationRequested();

            var rowIssues = new List<ValidationIssueDto>();
            var mappedRecord = MapRecord(record, mappings, rowIssues);
            foreach (var requiredField in requiredCanonicalFields)
            {
                if (!HasJsonPath(mappedRecord, requiredField))
                {
                    rowIssues.Add(new ValidationIssueDto(
                        "required.missing",
                        ProviderIntegrationIssueSeverityDto.Critical,
                        $"Required field '{requiredField}' is missing.",
                        requiredField,
                    "Map a source column, provide a constant, or configure a default value."));
                }
            }

            ProviderIntegrationMappedRecordValidation.AddValidationIssues(request.Capability, mappedRecord, rowIssues);
            allIssues.AddRange(rowIssues);
            var mappedElement = ToJsonElement(mappedRecord);
            var sourceRecordId = ProviderIntegrationMappedRecordIdentity.ResolveSourceRecordId(request.Capability, mappedRecord);
            var dedupeKey = ProviderIntegrationMappedRecordIdentity.BuildDedupeKey(
                connection.ConnectionId,
                request.Capability,
                record.RowNumber,
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

            var rawElement = ToJsonElement(record);

            if (rowIssues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical))
            {
                quarantined++;
                await scopedStore.SaveQuarantinedRecordAsync(
                    new QuarantinedRecordDto(
                        StableId("quarantine", request.SyncRunId, record.RowNumber.ToString(CultureInfo.InvariantCulture)),
                        request.SyncRunId,
                        connection.ConnectionId,
                        request.Capability,
                        rawElement,
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
                    StableId("staging", request.SyncRunId, record.RowNumber.ToString(CultureInfo.InvariantCulture)),
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

        var status = allIssues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical)
            ? ProviderIntegrationProcessingStatusDto.Quarantined
            : ProviderIntegrationProcessingStatusDto.Validated;

        var result = new ProviderIntegrationDryRunResultDto(
            request.SyncRunId,
            payloadId,
            request.Capability,
            csvRecords.Count,
            accepted,
            quarantined,
            status,
            allIssues);
        await SaveSyncRunAsync(scopedStore, request, manifest, connection, payloadId, result, ct).ConfigureAwait(false);
        return result;
    }

    private Task SaveSyncRunAsync(
        IProviderIntegrationManifestStore scopedStore,
        ManualCsvProviderIntegrationDryRunRequestDto request,
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection,
        string rawPayloadId,
        ProviderIntegrationDryRunResultDto result,
        CancellationToken ct)
        => scopedStore.SaveSyncRunAsync(
            new ProviderIntegrationSyncRunDto(
                request.SyncRunId,
                manifest.ManifestId,
                connection.ConnectionId,
                manifest.ProviderId,
                request.Capability,
                ManualCsvEndpointKey,
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
        ManualCsvProviderIntegrationDryRunRequestDto request,
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

        if (manifest.IntegrationType is not IntegrationTypeDto.ManualUpload and not IntegrationTypeDto.Hybrid and not IntegrationTypeDto.SftpFile)
        {
            throw new InvalidOperationException("Manual CSV dry runs require a manual-upload, file, or hybrid integration manifest.");
        }
    }

    private static JsonObject MapRecord(
        ManualCsvRawRecord record,
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
                        $"Required source column '{mapping.SourcePath}' is blank or missing.",
                        mapping.TargetField,
                        "Map a populated source column, provide a constant, or configure a default value."));
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

    private static string? ResolveMappedValue(ManualCsvRawRecord record, FieldMappingDto mapping)
    {
        if (!string.IsNullOrWhiteSpace(mapping.ConstantValue))
        {
            return mapping.ConstantValue;
        }

        var column = NormalizeSourcePath(mapping.SourcePath);
        if (record.Fields.TryGetValue(column, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return mapping.DefaultValue;
    }

    private static object? ApplyTransform(
        string value,
        FieldMappingDto mapping,
        ManualCsvRawRecord record,
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
            "signedamount" => ParseSignedAmount(
                value,
                mapping,
                path => record.Fields.TryGetValue(NormalizeSourcePath(path), out var conditionValue)
                    ? conditionValue
                    : null,
                issues),
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

    private static IReadOnlyList<ManualCsvRawRecord> ParseCsv(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var rows = TokenizeCsv(content);
        if (rows.Count == 0)
        {
            return [];
        }

        var headers = rows[0].Select(header => header.Trim()).ToArray();
        var records = new List<ManualCsvRawRecord>();
        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var columnIndex = 0; columnIndex < headers.Length; columnIndex++)
            {
                var header = headers[columnIndex];
                if (string.IsNullOrWhiteSpace(header))
                {
                    continue;
                }

                fields[header] = columnIndex < row.Count ? row[columnIndex].Trim() : string.Empty;
            }

            records.Add(new ManualCsvRawRecord(index + 1, fields));
        }

        return records;
    }

    private static List<List<string>> TokenizeCsv(string content)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < content.Length; index++)
        {
            var current = content[index];
            if (current == '"')
            {
                if (inQuotes && index + 1 < content.Length && content[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (!inQuotes && current == ',')
            {
                row.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (!inQuotes && (current == '\r' || current == '\n'))
            {
                if (current == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                {
                    index++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
                continue;
            }

            field.Append(current);
        }

        row.Add(field.ToString());
        if (row.Count > 1 || !string.IsNullOrWhiteSpace(row[0]))
        {
            rows.Add(row);
        }

        return rows;
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

    private static JsonElement ToJsonElement<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string StableId(params string[] parts)
    {
        var input = string.Join("|", parts);
        return Sha256Digest.ComputeUtf8(input)[..24];
    }

    private sealed record ManualCsvRawPayload(
        string FileName,
        string ContentType,
        int RecordCount,
        IReadOnlyList<ManualCsvRawRecord> Records);

    private sealed record ManualCsvRawRecord(
        int RowNumber,
        IReadOnlyDictionary<string, string> Fields);
}
