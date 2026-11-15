using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Shared.Services;

public sealed record ThreeWayStoredRun(string RequestHash, ThreeWayReconciliationResultDto Result, bool Completed = false);
public sealed record ThreeWayRetainedSource(ThreeWayReconciliationScopeDto Scope, ThreeWaySourceSnapshotDto Snapshot);

/// <summary>Immutable captured operands and run receipts; the existing case queue remains case authority.</summary>
public sealed class FileThreeWayReconciliationEvidenceStore(ThreeWayReconciliationOptions options)
{
    public Task<IAsyncDisposable> AcquireOperationAsync(ThreeWayReconciliationScopeDto scope,
        string operationId, CancellationToken ct)
        => AcquireLeaseAsync(RunPath(scope, operationId) + ".lock", ct);

    public Task<IAsyncDisposable> AcquireCaseAsync(ThreeWayReconciliationScopeDto scope,
        string caseId, CancellationToken ct)
        => AcquireLeaseAsync(Path.Combine(ScopePath(scope), "case-locks", Sha256Digest.ComputeUtf8(caseId) + ".lock"), ct);

    private static async Task<IAsyncDisposable> AcquireLeaseAsync(string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new OperationLease(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException) when (attempt < 200)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false);
            }
        }
    }

    public async Task SaveSourceAsync(ThreeWayReconciliationScopeDto scope, ThreeWaySourceSnapshotDto source,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new ThreeWayRetainedSource(scope, source),
            ThreeWayReconciliationJsonContext.Default.ThreeWayRetainedSource);
        var hash = Sha256Digest.ComputeUtf8(json);
        var path = Path.Combine(ScopePath(scope), "sources", source.Source,
            $"{source.AsOfUtc.UtcTicks:D19}-{hash}.json");
        if (!File.Exists(path))
            await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);
    }

    public async Task<ThreeWaySourceSnapshotDto?> ReadSourceAsync(ThreeWayReconciliationScopeDto scope,
        string source, DateTimeOffset cutoff, CancellationToken ct)
    {
        var directory = Path.Combine(ScopePath(scope), "sources", source);
        if (!Directory.Exists(directory))
            return null;
        // Filename timestamps select one source cut without loading unrelated later history.
        var candidates = Directory.EnumerateFiles(directory, "*.json")
            .Where(path => long.TryParse(Path.GetFileName(path).AsSpan(0, 19), out var ticks) && ticks <= cutoff.UtcTicks)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0)
            return null;
        var latestTicks = Path.GetFileName(candidates[0])[..19];
        var sameTime = candidates.TakeWhile(path => Path.GetFileName(path).StartsWith(latestTicks, StringComparison.Ordinal)).ToArray();
        if (sameTime.Length != 1)
            throw new InvalidOperationException($"Conflicting {source} snapshots have the same source timestamp.");
        await using var stream = File.OpenRead(sameTime[0]);
        var retained = await JsonSerializer.DeserializeAsync(stream,
            ThreeWayReconciliationJsonContext.Default.ThreeWayRetainedSource, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Retained source evidence is unreadable.");
        if (retained.Scope != scope || retained.Snapshot.Source != source)
            throw new InvalidOperationException("Retained source evidence does not belong to the exact owner/source partition.");
        var result = retained.Snapshot;
        var json = JsonSerializer.Serialize(retained, ThreeWayReconciliationJsonContext.Default.ThreeWayRetainedSource);
        if (!Path.GetFileNameWithoutExtension(sameTime[0]).EndsWith(Sha256Digest.ComputeUtf8(json), StringComparison.Ordinal))
            throw new InvalidOperationException("Retained source evidence failed its content hash.");
        return result;
    }

    public async Task<ThreeWayStoredRun?> ReadRunAsync(ThreeWayReconciliationScopeDto scope,
        string operationId, CancellationToken ct)
    {
        var path = RunPath(scope, operationId);
        if (!File.Exists(path))
            return null;
        await using var stream = File.OpenRead(path);
        var result = await JsonSerializer.DeserializeAsync(stream, ThreeWayReconciliationJsonContext.Default.ThreeWayStoredRun, ct)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Retained reconciliation receipt is unreadable.");
        if (result.Result.Scope != scope || result.Result.OperationId != operationId)
            throw new InvalidOperationException("Retained reconciliation receipt does not belong to the exact owner/operation partition.");
        return result;
    }

    public Task SaveRunAsync(ThreeWayStoredRun run, CancellationToken ct)
        => AtomicFileWriter.WriteAsync(RunPath(run.Result.Scope, run.Result.OperationId),
            JsonSerializer.Serialize(run, ThreeWayReconciliationJsonContext.Default.ThreeWayStoredRun), ct);

    public async Task<ThreeWayReconciliationResultDto?> GetLatestAsync(ThreeWayReconciliationScopeDto scope, CancellationToken ct)
    {
        var directory = Path.Combine(ScopePath(scope), "runs");
        if (!Directory.Exists(directory))
            return null;
        ThreeWayReconciliationResultDto? latest = null;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(path);
            var retained = await JsonSerializer.DeserializeAsync(stream,
                ThreeWayReconciliationJsonContext.Default.ThreeWayStoredRun, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Retained reconciliation receipt is unreadable.");
            if (retained.Result.Scope != scope)
                throw new InvalidOperationException("Retained reconciliation receipt scope differs from its partition.");
            if (retained.Completed && (latest is null || retained.Result.RecordedAtUtc > latest.RecordedAtUtc))
                latest = retained.Result;
        }
        return latest;
    }

    private string RunPath(ThreeWayReconciliationScopeDto scope, string operationId)
        => Path.Combine(ScopePath(scope), "runs", Sha256Digest.ComputeUtf8(operationId) + ".json");

    private string ScopePath(ThreeWayReconciliationScopeDto scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RootDirectory);
        return Path.Combine(options.RootDirectory,
            Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(scope, ThreeWayReconciliationJsonContext.Default.ThreeWayReconciliationScopeDto)));
    }

    private sealed class OperationLease(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ThreeWayStoredRun))]
[JsonSerializable(typeof(ThreeWayRetainedSource))]
[JsonSerializable(typeof(ThreeWayReconciliationCheckDto))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(ThreeWaySourceSnapshotDto))]
[JsonSerializable(typeof(ThreeWayPositionDto[]))]
[JsonSerializable(typeof(ThreeWayReconciliationScopeDto))]
[JsonSerializable(typeof(ThreeWayReconciliationRequestDto))]
[JsonSerializable(typeof(ThreeWayReconciliationResultDto))]
internal sealed partial class ThreeWayReconciliationJsonContext : JsonSerializerContext;
