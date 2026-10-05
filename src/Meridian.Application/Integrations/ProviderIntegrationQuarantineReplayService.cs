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

public sealed class ProviderIntegrationQuarantineReplayService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IProviderIntegrationManifestStore store;
    private readonly ILogger<ProviderIntegrationQuarantineReplayService> logger;

    public ProviderIntegrationQuarantineReplayService(
        IProviderIntegrationManifestStore store,
        ILogger<ProviderIntegrationQuarantineReplayService>? logger = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.logger = logger ?? NullLogger<ProviderIntegrationQuarantineReplayService>.Instance;
    }

    public async Task<ProviderIntegrationQuarantineReplayResultDto> ReplayAsync(
        ProviderIntegrationQuarantineReplayRequestDto request,
        CancellationToken ct = default)
        => await ReplayAsync(null, request, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationQuarantineReplayResultDto> ReplayAsync(
        string? tenantId,
        ProviderIntegrationQuarantineReplayRequestDto request,
        CancellationToken ct = default)
        => await ProviderIntegrationServiceBoundary.RunAsync(
            logger,
            "quarantine-replay",
            new ProviderIntegrationBoundaryContext(
                TenantId: tenantId,
                ManifestId: request?.ManifestId,
                ConnectionId: request?.ConnectionId,
                Capability: request is null ? null : request.Capability.ToString(),
                SyncRunId: request?.ReplaySyncRunId),
            () => ReplayCoreAsync(tenantId, request, ct)).ConfigureAwait(false);

    private async Task<ProviderIntegrationQuarantineReplayResultDto> ReplayCoreAsync(
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
        if (StringComparer.Ordinal.Equals(request.ReplaySyncRunId, request.SourceSyncRunId))
        {
            throw new ArgumentException("Replay requires a new sync run id to preserve the source evidence.", nameof(request));
        }

        if (!Enum.IsDefined(request.Mode) ||
            (request.Mode == ProviderIntegrationReplayModeDto.Original &&
                (request.TargetManifestVersion is not null || request.TargetManifestDigest is not null)))
        {
            throw new ArgumentException("An original replay cannot select a different mapping; select remediation explicitly.", nameof(request));
        }

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
        if (await scopedStore.GetSyncRunAsync(request.ReplaySyncRunId, ct).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException("Replay requires an unused sync run id to preserve retained evidence.");
        }

        var connection = await scopedStore.GetConnectionAsync(request.ConnectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{request.ConnectionId}' was not found.");
        var sourceSyncRun = await scopedStore.GetSyncRunAsync(request.SourceSyncRunId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration sync run '{request.SourceSyncRunId}' was not found.");
        var originalReference = await ValidateSourceProvenanceAsync(scopedStore, request, sourceSyncRun, ct)
            .ConfigureAwait(false);
        var selectedReference = request.Mode == ProviderIntegrationReplayModeDto.Original
            ? originalReference
            : ResolveRemediationReference(request, originalReference);
        var manifest = await ResolveManifestAsync(scopedStore, selectedReference, ct).ConfigureAwait(false);
        var appliedReference = ProviderIntegrationManifestIdentity.Create(manifest);

        var capability = ValidateRequestScope(request, manifest, connection, sourceSyncRun);
        var sourceRecords = await ResolveRequestedRecordsAsync(scopedStore, request, ct).ConfigureAwait(false);
        var rawPayloadId = StableId("raw-payload", request.ReplaySyncRunId, request.SourceSyncRunId, "quarantine-replay");

        // Claim the run before publishing any evidence. Different source runs produce different
        // payload/row IDs, so their individual immutable writes cannot arbitrate a shared run ID.
        if (!await scopedStore.TryCreateSyncRunAsync(
            CreateSyncRun(request, manifest, originalReference, sourceSyncRun.EndpointKey, rawPayloadId, result: null), ct)
            .ConfigureAwait(false))
        {
            throw new InvalidOperationException("Replay requires an unused sync run id to preserve retained evidence.");
        }

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
                ProviderIntegrationProcessingStatusDto.Received)
            {
                ManifestReference = appliedReference,
                OriginalManifestReference = originalReference,
                SourceSyncRunId = request.SourceSyncRunId,
                ReplayMode = request.Mode
            },
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
            await scopedStore.SaveSyncRunAsync(
                CreateSyncRun(request, manifest, originalReference, sourceSyncRun.EndpointKey, rawPayloadId, blocked), ct)
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
        var dedupeValidator = new ProviderIntegrationStagingDedupeValidator();

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

            ProviderIntegrationMappedRecordValidation.AddValidationIssues(request.Capability, mappedRecord, rowIssues);
            allIssues.AddRange(rowIssues);
            var mappedElement = ToJsonElement(mappedRecord);
            var sourceRecordId = ProviderIntegrationMappedRecordIdentity.ResolveSourceRecordId(request.Capability, mappedRecord);
            var ordinal = (index + 1).ToString(CultureInfo.InvariantCulture);
            var dedupeKey = ProviderIntegrationMappedRecordIdentity.BuildDedupeKey(
                connection.ConnectionId,
                request.Capability,
                ordinal,
                sourceRecordId ?? sourceRecord.QuarantineRecordId);

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
        var result = new ProviderIntegrationQuarantineReplayResultDto(
            request.ReplaySyncRunId,
            rawPayloadId,
            request.Capability,
            sourceRecords.Count,
            accepted,
            requarantined,
            status,
            allIssues);
        await scopedStore.SaveSyncRunAsync(
            CreateSyncRun(request, manifest, originalReference, sourceSyncRun.EndpointKey, rawPayloadId, result), ct)
            .ConfigureAwait(false);
        return result;
    }

    private static async Task<ProviderIntegrationManifestReferenceDto> ValidateSourceProvenanceAsync(
        IProviderIntegrationManifestStore scopedStore,
        ProviderIntegrationQuarantineReplayRequestDto request,
        ProviderIntegrationSyncRunDto sourceSyncRun,
        CancellationToken ct)
    {
        if (sourceSyncRun.ManifestReference is not { } applied ||
            sourceSyncRun.OriginalManifestReference is not { } original ||
            string.IsNullOrWhiteSpace(sourceSyncRun.RawPayloadId))
        {
            throw new InvalidOperationException("The source sync run has no complete retained manifest provenance; its original mapping cannot be inferred.");
        }

        var payload = await scopedStore.GetRawPayloadAsync(request.SourceSyncRunId, sourceSyncRun.RawPayloadId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The source raw payload required for replay provenance was not retained.");
        if (!ProviderIntegrationManifestIdentity.Matches(payload.ManifestReference, applied) ||
            !ProviderIntegrationManifestIdentity.Matches(payload.OriginalManifestReference, original) ||
            payload.SourceSyncRunId != sourceSyncRun.SourceSyncRunId || payload.ReplayMode != sourceSyncRun.ReplayMode ||
            !StringComparer.Ordinal.Equals(sourceSyncRun.SyncRunId, request.SourceSyncRunId) ||
            !StringComparer.Ordinal.Equals(payload.SyncRunId, request.SourceSyncRunId) ||
            !StringComparer.Ordinal.Equals(payload.PayloadId, sourceSyncRun.RawPayloadId) ||
            !StringComparer.Ordinal.Equals(payload.ConnectionId, request.ConnectionId) ||
            !StringComparer.Ordinal.Equals(payload.ProviderId, sourceSyncRun.ProviderId) ||
            !StringComparer.Ordinal.Equals(payload.EndpointKey, sourceSyncRun.EndpointKey) ||
            payload.Capability != request.Capability ||
            !StringComparer.Ordinal.Equals(applied.ManifestId, request.ManifestId) ||
            !StringComparer.Ordinal.Equals(original.ManifestId, request.ManifestId) ||
            !StringComparer.Ordinal.Equals(payload.MappingVersion,
                $"{applied.ManifestId}:v{applied.ManifestVersion.ToString(CultureInfo.InvariantCulture)}"))
        {
            throw new InvalidOperationException("The source raw payload and sync run have inconsistent manifest provenance or replay scope.");
        }

        var appliedOriginal = ProviderIntegrationManifestIdentity.Matches(applied, original);
        var validLineage = sourceSyncRun.ReplayMode switch
        {
            null => sourceSyncRun.SourceSyncRunId is null && appliedOriginal,
            ProviderIntegrationReplayModeDto.Original => !string.IsNullOrWhiteSpace(sourceSyncRun.SourceSyncRunId) && appliedOriginal,
            ProviderIntegrationReplayModeDto.Remediation => !string.IsNullOrWhiteSpace(sourceSyncRun.SourceSyncRunId) &&
                applied.ManifestVersion > original.ManifestVersion,
            _ => false
        };
        if (!validLineage || StringComparer.Ordinal.Equals(sourceSyncRun.SourceSyncRunId, sourceSyncRun.SyncRunId))
        {
            throw new InvalidOperationException("The source sync run has invalid original or remediation provenance.");
        }

        var appliedManifest = await ResolveManifestAsync(scopedStore, applied, ct).ConfigureAwait(false);
        var originalManifest = appliedOriginal
            ? appliedManifest
            : await ResolveManifestAsync(scopedStore, original, ct).ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(appliedManifest.ProviderId, sourceSyncRun.ProviderId) ||
            !StringComparer.Ordinal.Equals(originalManifest.ProviderId, sourceSyncRun.ProviderId))
        {
            throw new InvalidOperationException("The retained manifest provenance is not linked to the source provider.");
        }

        return ProviderIntegrationManifestIdentity.Create(originalManifest);
    }

    private static ProviderIntegrationManifestReferenceDto ResolveRemediationReference(
        ProviderIntegrationQuarantineReplayRequestDto request,
        ProviderIntegrationManifestReferenceDto originalReference)
    {
        if (request.TargetManifestVersion is not { } version || version <= originalReference.ManifestVersion ||
            string.IsNullOrWhiteSpace(request.TargetManifestDigest))
        {
            throw new ArgumentException("Remediation requires an explicit newer manifest version and its content digest.", nameof(request));
        }

        return new ProviderIntegrationManifestReferenceDto(request.ManifestId, version, request.TargetManifestDigest);
    }

    private static async Task<ProviderIntegrationManifestDto> ResolveManifestAsync(
        IProviderIntegrationManifestStore scopedStore,
        ProviderIntegrationManifestReferenceDto reference,
        CancellationToken ct)
    {
        if (reference.ManifestVersion < 1 || string.IsNullOrWhiteSpace(reference.ContentDigest))
        {
            throw new InvalidOperationException("The retained manifest reference has no valid version and digest.");
        }

        var manifest = await scopedStore.GetManifestVersionAsync(reference.ManifestId, reference.ManifestVersion, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Retained manifest '{reference.ManifestId}' version {reference.ManifestVersion} is unavailable; its mapping cannot be inferred.");
        if (!ProviderIntegrationManifestIdentity.Matches(ProviderIntegrationManifestIdentity.Create(manifest), reference))
        {
            throw new InvalidOperationException("The retained manifest content does not match its recorded version and digest.");
        }

        return manifest;
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

            if (!StringComparer.Ordinal.Equals(record.SyncRunId, request.SourceSyncRunId) ||
                !StringComparer.Ordinal.Equals(record.ConnectionId, request.ConnectionId))
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
        if (!StringComparer.Ordinal.Equals(connection.ManifestId, manifest.ManifestId) ||
            !StringComparer.Ordinal.Equals(connection.ConnectionId, request.ConnectionId) ||
            !StringComparer.Ordinal.Equals(connection.ProviderId, manifest.ProviderId))
        {
            throw new InvalidOperationException("The provider connection is not linked to the requested manifest.");
        }

        if (!StringComparer.Ordinal.Equals(sourceSyncRun.ManifestId, request.ManifestId) ||
            !StringComparer.Ordinal.Equals(sourceSyncRun.ProviderId, manifest.ProviderId) ||
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

    private static ProviderIntegrationSyncRunDto CreateSyncRun(
        ProviderIntegrationQuarantineReplayRequestDto request,
        ProviderIntegrationManifestDto manifest,
        ProviderIntegrationManifestReferenceDto originalReference,
        string endpointKey,
        string rawPayloadId,
        ProviderIntegrationQuarantineReplayResultDto? result)
        => new(
            request.ReplaySyncRunId,
            manifest.ManifestId,
            request.ConnectionId,
            manifest.ProviderId,
            request.Capability,
            endpointKey,
            request.RequestedAt,
            result is null ? null : request.RequestedAt,
            result?.Status ?? ProviderIntegrationProcessingStatusDto.Received,
            result?.RecordsReplayed ?? 0,
            result?.RecordsAccepted ?? 0,
            result?.RecordsRequarantined ?? 0,
            rawPayloadId,
            result?.Issues ?? [])
        {
            ManifestReference = ProviderIntegrationManifestIdentity.Create(manifest),
            OriginalManifestReference = originalReference,
            SourceSyncRunId = request.SourceSyncRunId,
            ReplayMode = request.Mode
        };

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
            "signedamount" => ParseSignedAmount(
                value,
                mapping,
                path => ReadJsonString(record, path),
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
        return Sha256Digest.ComputeUtf8(input)[..24];
    }

    private sealed record QuarantineReplayRawPayload(
        string SourceSyncRunId,
        string? SourceRawPayloadId,
        IReadOnlyList<QuarantineReplayRawRecord> Records);

    private sealed record QuarantineReplayRawRecord(
        string QuarantineRecordId,
        JsonElement RawRecord);
}
