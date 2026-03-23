using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.Etl;
using Meridian.Application.Logging;
using Meridian.Contracts.Etl;
using Meridian.Storage.Archival;
using Serilog;

namespace Meridian.Storage.Etl;

public sealed class EtlStagingStore
{
    private readonly ILogger _log = LoggingSetup.ForContext<EtlStagingStore>();
    private readonly string _rootPath;

    public EtlStagingStore(string dataRoot)
    {
        _rootPath = Path.Combine(dataRoot, "_etl", "staging");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<EtlStagedFile> StageAsync(string jobId, EtlRemoteFile file, Stream sourceStream, CancellationToken ct = default)
    {
        var jobPath = Path.Combine(_rootPath, jobId);
        Directory.CreateDirectory(jobPath);
        var destinationPath = Path.Combine(jobPath, file.Name);
        string checksum;
        long totalBytes = 0;

        await using (var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, useAsync: true))
        using (var sha = SHA256.Create())
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                sha.TransformBlock(buffer, 0, read, null, 0);
                totalBytes += read;
            }
            sha.TransformFinalBlock([], 0, 0);
            checksum = Convert.ToHexStringLower(sha.Hash!);
        }

        _log.Information("Staged ETL file {FileName} for job {JobId}", file.Name, jobId);
        return new EtlStagedFile
        {
            OriginalPath = file.Path,
            StagedPath = destinationPath,
            FileName = file.Name,
            ChecksumSha256 = checksum,
            SizeBytes = totalBytes
        };
    }
}

public sealed class EtlAuditStore
{
    private readonly string _auditRoot;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    public EtlAuditStore(string dataRoot)
    {
        _auditRoot = Path.Combine(dataRoot, "_etl", "audit");
        Directory.CreateDirectory(_auditRoot);
    }

    public async Task WriteEventAsync(string jobId, EtlAuditEvent auditEvent, CancellationToken ct = default)
    {
        var path = GetAuditPath(jobId, "events.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.AppendAllTextAsync(path, JsonSerializer.Serialize(auditEvent, _jsonOptions) + Environment.NewLine, ct).ConfigureAwait(false);
    }

    public async Task SaveCheckpointAsync(string jobId, EtlCheckpointToken checkpoint, CancellationToken ct = default)
    {
        var path = GetAuditPath(jobId, "checkpoint.json");
        await AtomicFileWriter.WriteTextAsync(path, JsonSerializer.Serialize(checkpoint, _jsonOptions), ct).ConfigureAwait(false);
    }

    public async Task<EtlCheckpointToken?> LoadCheckpointAsync(string jobId, CancellationToken ct = default)
    {
        var path = GetAuditPath(jobId, "checkpoint.json");
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<EtlCheckpointToken>(json, _jsonOptions);
    }

    public string GetAuditPath(string jobId, string fileName)
        => Path.Combine(_auditRoot, jobId, fileName);
}

public sealed class EtlRejectSink
{
    private readonly string _rejectRoot;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    public EtlRejectSink(string dataRoot)
    {
        _rejectRoot = Path.Combine(dataRoot, "_etl", "rejects");
        Directory.CreateDirectory(_rejectRoot);
    }

    public async Task AppendAsync(string jobId, EtlRejectRecord record, CancellationToken ct = default)
    {
        var path = Path.Combine(_rejectRoot, jobId, "rejects.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.AppendAllTextAsync(path, JsonSerializer.Serialize(record, _jsonOptions) + Environment.NewLine, ct).ConfigureAwait(false);
    }
}
