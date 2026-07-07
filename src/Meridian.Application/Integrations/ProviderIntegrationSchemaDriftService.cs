using System.Text.Json;
using Meridian.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationSchemaDriftService
{
    private readonly ILogger<ProviderIntegrationSchemaDriftService> logger;
    private readonly IProviderIntegrationManifestStore store;
    private readonly ILogger<ProviderIntegrationSchemaDriftService> logger;

    public ProviderIntegrationSchemaDriftService(
        IProviderIntegrationManifestStore store,
        ILogger<ProviderIntegrationSchemaDriftService>? logger = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.logger = logger ?? NullLogger<ProviderIntegrationSchemaDriftService>.Instance;
    }

    public async Task<ProviderIntegrationSchemaDriftCheckResultDto> CheckAsync(
        ProviderIntegrationSchemaDriftCheckRequestDto request,
        CancellationToken ct = default)
        => await CheckAsync(null, request, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationSchemaDriftCheckResultDto> CheckAsync(
        string? tenantId,
        ProviderIntegrationSchemaDriftCheckRequestDto request,
        CancellationToken ct = default)
        => await ProviderIntegrationServiceBoundary.RunAsync(
            logger,
            "schema-drift-check",
            new ProviderIntegrationBoundaryContext(
                TenantId: tenantId,
                ManifestId: request?.ManifestId,
                ConnectionId: request?.ConnectionId,
                Capability: request is null ? null : request.Capability.ToString(),
                EndpointKey: request?.EndpointKey,
                SyncRunId: request?.SyncRunId),
            async () =>
    {
        logger.LogDebug(
            "Provider integration operation {Operation} starting for manifest {ManifestId}, connection {ConnectionId}.",
            nameof(CheckAsync),
            request?.ManifestId,
            request?.ConnectionId);
        try
        {
            return await CheckCoreAsync(tenantId, request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Provider integration operation {Operation} failed for manifest {ManifestId}, connection {ConnectionId}.",
                nameof(CheckAsync),
                request?.ManifestId,
                request?.ConnectionId);
            throw;
        }
    }

    private async Task<ProviderIntegrationSchemaDriftCheckResultDto> CheckCoreAsync(
        string? tenantId,
        ProviderIntegrationSchemaDriftCheckRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManifestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EndpointKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SyncRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RawPayloadId);

        ct.ThrowIfCancellationRequested();

        var scopedStore = ResolveStore(tenantId);
        var manifest = await scopedStore.GetManifestAsync(request.ManifestId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration manifest '{request.ManifestId}' was not found.");
        var connection = await scopedStore.GetConnectionAsync(request.ConnectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{request.ConnectionId}' was not found.");
        var payload = await scopedStore.GetRawPayloadAsync(request.SyncRunId, request.RawPayloadId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration raw payload '{request.RawPayloadId}' was not found for sync run '{request.SyncRunId}'.");

        ValidateScope(request, manifest, connection, payload);
        var endpoint = manifest.Endpoints.FirstOrDefault(candidate =>
                StringComparer.OrdinalIgnoreCase.Equals(candidate.EndpointKey, request.EndpointKey) &&
                candidate.Capability == request.Capability)
            ?? throw new InvalidOperationException($"Endpoint '{request.EndpointKey}' is not configured for {request.Capability}.");

        var issues = new List<ProviderIntegrationSchemaDriftIssueDto>();
        var records = ExtractRecords(payload.RawPayload, endpoint.Response.RecordsPath);
        if (records.Count == 0)
        {
            issues.Add(CreateIssue(
                "schema.records-path.missing",
                ProviderIntegrationIssueSeverityDto.Critical,
                request,
                endpoint.Response.RecordsPath,
                $"No records were found at configured records path '{endpoint.Response.RecordsPath}'.",
                "Pause this capability until the records path is remapped to the provider response shape."));
        }

        foreach (var requiredPath in endpoint.Response.RequiredPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (!HasPath(payload.RawPayload, requiredPath))
            {
                issues.Add(CreateIssue(
                    "schema.required-path.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    request,
                    requiredPath,
                    $"Required provider response path '{requiredPath}' is missing from the retained payload.",
                    "Update the endpoint response contract or pause this capability until the provider shape is confirmed."));
            }
        }

        foreach (var mapping in manifest.FieldMappings.Where(mapping => mapping.Capability == request.Capability && mapping.Required))
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(mapping.ConstantValue) || !string.IsNullOrWhiteSpace(mapping.DefaultValue))
            {
                continue;
            }

            if (!records.Any(record => HasPath(record, mapping.SourcePath)))
            {
                issues.Add(CreateIssue(
                    "schema.mapping-source.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    request,
                    mapping.SourcePath,
                    $"Required mapping source path '{mapping.SourcePath}' for '{mapping.TargetField}' is missing from sampled records.",
                    "Remap the source field, add a safe default or constant, or keep the capability paused."));
            }
        }

        var critical = issues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
        return new ProviderIntegrationSchemaDriftCheckResultDto(
            manifest.ManifestId,
            connection.ConnectionId,
            request.Capability,
            endpoint.EndpointKey,
            request.SyncRunId,
            payload.PayloadId,
            DriftDetected: issues.Count > 0,
            ShouldPauseCapability: critical,
            RecordsInspected: records.Count,
            Issues: issues);
    }).ConfigureAwait(false);

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;

    private static void ValidateScope(
        ProviderIntegrationSchemaDriftCheckRequestDto request,
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection,
        RawIngestionPayloadDto payload)
    {
        if (!StringComparer.Ordinal.Equals(connection.ManifestId, manifest.ManifestId))
        {
            throw new InvalidOperationException("The provider connection is not linked to the requested manifest.");
        }

        if (!connection.EnabledCapabilities.Contains(request.Capability))
        {
            throw new InvalidOperationException($"The provider connection has not enabled {request.Capability}.");
        }

        if (payload.ConnectionId != connection.ConnectionId ||
            payload.Capability != request.Capability ||
            !StringComparer.OrdinalIgnoreCase.Equals(payload.EndpointKey, request.EndpointKey))
        {
            throw new InvalidOperationException("The retained raw payload does not match the requested connection, capability, and endpoint.");
        }
    }

    private static ProviderIntegrationSchemaDriftIssueDto CreateIssue(
        string code,
        ProviderIntegrationIssueSeverityDto severity,
        ProviderIntegrationSchemaDriftCheckRequestDto request,
        string jsonPath,
        string message,
        string suggestedFix)
        => new(
            code,
            severity,
            request.Capability,
            request.EndpointKey,
            jsonPath,
            message,
            suggestedFix);

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

        var matches = ResolvePath(responseBody, recordsPath);
        return matches
            .SelectMany(match => match.ValueKind == JsonValueKind.Array
                ? match.EnumerateArray().Select(record => record.Clone())
                : match.ValueKind == JsonValueKind.Object
                    ? [match.Clone()]
                    : [])
            .ToArray();
    }

    private static bool HasPath(JsonElement root, string sourcePath)
        => ResolvePath(root, sourcePath).Any(IsPresentValue);

    private static IReadOnlyList<JsonElement> ResolvePath(JsonElement root, string sourcePath)
    {
        var parts = NormalizeSourcePath(sourcePath);
        if (parts.Length == 0)
        {
            return [root];
        }

        var current = new List<JsonElement> { root };
        foreach (var part in parts)
        {
            if (current.Count == 0)
            {
                break;
            }

            var next = new List<JsonElement>();
            foreach (var candidate in current)
            {
                if (part == "*")
                {
                    if (candidate.ValueKind == JsonValueKind.Array)
                    {
                        next.AddRange(candidate.EnumerateArray().Select(element => element.Clone()));
                    }

                    continue;
                }

                if (candidate.ValueKind == JsonValueKind.Object &&
                    candidate.TryGetProperty(part, out var property))
                {
                    next.Add(property.Clone());
                }
            }

            current = next;
        }

        return current;
    }

    private static bool IsPresentValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Undefined => false,
            JsonValueKind.Null => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(element.GetString()),
            JsonValueKind.Array => element.GetArrayLength() > 0,
            JsonValueKind.Object => element.EnumerateObject().Any(),
            _ => true
        };

    private static string[] NormalizeSourcePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return [];
        }

        var normalized = sourcePath.Trim();
        if (StringComparer.Ordinal.Equals(normalized, "$"))
        {
            return [];
        }

        if (normalized.StartsWith("$.", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized.StartsWith("$['", StringComparison.Ordinal) &&
            normalized.EndsWith("']", StringComparison.Ordinal))
        {
            normalized = normalized[3..^2];
        }

        normalized = normalized
            .Replace("[*]", ".*", StringComparison.Ordinal)
            .Replace("[]", ".*", StringComparison.Ordinal);

        return normalized.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
