using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Integrations;

public sealed class FileProviderIntegrationManifestStore : IProviderIntegrationManifestStore
{
    private readonly string _rootPath;

    public FileProviderIntegrationManifestStore(string dataRoot)
    {
        var root = string.IsNullOrWhiteSpace(dataRoot) ? "data" : dataRoot;
        _rootPath = Path.Combine(root, "_integrations");
        Directory.CreateDirectory(_rootPath);
    }

    public Task SaveManifestAsync(ProviderIntegrationManifestDto manifest, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.ManifestId);

        return WriteAsync(GetManifestPath(manifest.ManifestId), manifest, ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto, ct);
    }

    public Task<ProviderIntegrationManifestDto?> GetManifestAsync(string manifestId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestId);
        return ReadAsync(GetManifestPath(manifestId), ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto, ct);
    }

    public Task<IReadOnlyList<ProviderIntegrationManifestDto>> ListManifestsAsync(CancellationToken ct = default)
        => ListAsync(GetDirectory("manifests"), ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto, ct);

    public Task SaveConnectionAsync(ProviderConnectionDto connection, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.ConnectionId);

        return WriteAsync(GetConnectionPath(connection.ConnectionId), connection, ProviderIntegrationContractsJsonContext.Default.ProviderConnectionDto, ct);
    }

    public Task<ProviderConnectionDto?> GetConnectionAsync(string connectionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        return ReadAsync(GetConnectionPath(connectionId), ProviderIntegrationContractsJsonContext.Default.ProviderConnectionDto, ct);
    }

    public Task SaveRawPayloadAsync(RawIngestionPayloadDto payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.SyncRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.PayloadId);

        return WriteAsync(
            GetSyncRunScopedPath("raw-payloads", payload.SyncRunId, payload.PayloadId),
            payload,
            ProviderIntegrationContractsJsonContext.Default.RawIngestionPayloadDto,
            ct);
    }

    public Task<RawIngestionPayloadDto?> GetRawPayloadAsync(string syncRunId, string payloadId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadId);

        return ReadAsync(
            GetSyncRunScopedPath("raw-payloads", syncRunId, payloadId),
            ProviderIntegrationContractsJsonContext.Default.RawIngestionPayloadDto,
            ct);
    }

    public Task SaveQuarantinedRecordAsync(QuarantinedRecordDto record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.SyncRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.QuarantineRecordId);

        return WriteAsync(
            GetSyncRunScopedPath("quarantine", record.SyncRunId, record.QuarantineRecordId),
            record,
            ProviderIntegrationContractsJsonContext.Default.QuarantinedRecordDto,
            ct);
    }

    public Task<IReadOnlyList<QuarantinedRecordDto>> ListQuarantinedRecordsAsync(string syncRunId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRunId);
        return ListAsync(
            GetSyncRunScopedDirectory("quarantine", syncRunId),
            ProviderIntegrationContractsJsonContext.Default.QuarantinedRecordDto,
            ct);
    }

    public Task SaveStagingRecordAsync(IntegrationStagingRecordDto record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.SyncRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.StagingRecordId);

        return WriteAsync(
            GetSyncRunScopedPath("staging", record.SyncRunId, record.StagingRecordId),
            record,
            ProviderIntegrationContractsJsonContext.Default.IntegrationStagingRecordDto,
            ct);
    }

    public Task<IReadOnlyList<IntegrationStagingRecordDto>> ListStagingRecordsAsync(string syncRunId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRunId);
        return ListAsync(
            GetSyncRunScopedDirectory("staging", syncRunId),
            ProviderIntegrationContractsJsonContext.Default.IntegrationStagingRecordDto,
            ct);
    }

    private string GetManifestPath(string manifestId) => Path.Combine(GetDirectory("manifests"), $"{HashSegment(manifestId)}.json");

    private string GetConnectionPath(string connectionId) => Path.Combine(GetDirectory("connections"), $"{HashSegment(connectionId)}.json");

    private string GetSyncRunScopedPath(string category, string syncRunId, string recordId)
        => Path.Combine(GetSyncRunScopedDirectory(category, syncRunId), $"{HashSegment(recordId)}.json");

    private string GetSyncRunScopedDirectory(string category, string syncRunId)
        => Path.Combine(GetDirectory(category), HashSegment(syncRunId));

    private string GetDirectory(string category)
    {
        var directory = Path.Combine(_rootPath, category);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task WriteAsync<TValue>(
        string path,
        TValue value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, jsonTypeInfo);
        await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);
    }

    private static async Task<TValue?> ReadAsync<TValue>(
        string path,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo,
        CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, jsonTypeInfo);
    }

    private static async Task<IReadOnlyList<TValue>> ListAsync<TValue>(
        string directory,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo,
        CancellationToken ct)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<TValue>();
        }

        var records = new List<TValue>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var record = await ReadAsync(file, jsonTypeInfo, ct).ConfigureAwait(false);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        return records;
    }

    private static string HashSegment(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
