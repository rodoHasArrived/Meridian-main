using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Store;

/// <summary>
/// Base class for whole-file JSON snapshot stores: state lives in a single JSON document that is
/// loaded on demand, mutated in memory, and persisted with <see cref="AtomicFileWriter"/> so the
/// file is never left partially written. A <see cref="SemaphoreSlim"/> serializes read-modify-write
/// cycles. Derived stores supply the empty snapshot, choose reflection- or source-generated
/// serialization via the constructor, and can override the corrupt-snapshot and post-load hooks to
/// keep their historical recovery semantics (propagate, log-and-start-fresh, or fail fast).
/// </summary>
public abstract class JsonFileSnapshotStore<TSnapshot> where TSnapshot : class
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions? _serializerOptions;
    private readonly JsonTypeInfo<TSnapshot>? _typeInfo;

    protected JsonFileSnapshotStore(string snapshotPath, JsonSerializerOptions serializerOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        ArgumentNullException.ThrowIfNull(serializerOptions);
        SnapshotPath = snapshotPath;
        _serializerOptions = serializerOptions;
    }

    protected JsonFileSnapshotStore(string snapshotPath, JsonTypeInfo<TSnapshot> typeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        ArgumentNullException.ThrowIfNull(typeInfo);
        SnapshotPath = snapshotPath;
        _typeInfo = typeInfo;
    }

    /// <summary>Full path of the snapshot file.</summary>
    protected string SnapshotPath { get; }

    /// <summary>Snapshot used when the file does not exist yet (or after recoverable corruption).</summary>
    protected abstract TSnapshot CreateEmptySnapshot();

    /// <summary>
    /// Called when the snapshot file exists but cannot be parsed. The default rethrows so data
    /// problems are not silently discarded; stores whose contract is "start fresh on corruption"
    /// override this to log and return <see cref="CreateEmptySnapshot"/>.
    /// </summary>
    protected virtual TSnapshot HandleCorruptSnapshot(JsonException exception) => throw exception;

    /// <summary>
    /// Called after a snapshot is successfully deserialized. Stores with versioned snapshots
    /// override this to validate or upgrade the document. Default: return it unchanged.
    /// </summary>
    protected virtual TSnapshot OnSnapshotLoaded(TSnapshot snapshot) => snapshot;

    /// <summary>Runs a read against the current snapshot under the store gate.</summary>
    protected async Task<TResult> ReadSnapshotAsync<TResult>(
        Func<TSnapshot, TResult> read,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(read);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return read(await LoadSnapshotAsync(ct).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Runs a read-modify-write cycle under the store gate: the current snapshot is loaded, the
    /// update produces the replacement snapshot (and a result), and the replacement is written
    /// atomically before the result is returned.
    /// </summary>
    protected async Task<TResult> UpdateSnapshotAsync<TResult>(
        Func<TSnapshot, (TSnapshot Snapshot, TResult Result)> update,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await LoadSnapshotAsync(ct).ConfigureAwait(false);
            var (replacement, result) = update(current);
            await WriteSnapshotAsync(replacement, ct).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Runs a read-modify-write cycle that does not produce a result.</summary>
    protected Task UpdateSnapshotAsync(Func<TSnapshot, TSnapshot> update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        return UpdateSnapshotAsync(snapshot => (update(snapshot), true), ct);
    }

    /// <summary>Loads the snapshot without taking the gate. For use inside derived-class flows
    /// that manage their own locking; most stores should use the gated helpers instead.</summary>
    protected async Task<TSnapshot> LoadSnapshotAsync(CancellationToken ct = default)
    {
        if (!File.Exists(SnapshotPath))
        {
            return CreateEmptySnapshot();
        }

        TSnapshot? snapshot;
        try
        {
            await using var stream = new FileStream(
                SnapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            snapshot = _typeInfo is not null
                ? await JsonSerializer.DeserializeAsync(stream, _typeInfo, ct).ConfigureAwait(false)
                : await JsonSerializer.DeserializeAsync<TSnapshot>(stream, _serializerOptions, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return HandleCorruptSnapshot(ex);
        }

        return snapshot is null ? CreateEmptySnapshot() : OnSnapshotLoaded(snapshot);
    }

    /// <summary>Serializes and atomically persists the snapshot without taking the gate.</summary>
    protected async Task WriteSnapshotAsync(TSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var json = _typeInfo is not null
            ? JsonSerializer.Serialize(snapshot, _typeInfo)
            : JsonSerializer.Serialize(snapshot, _serializerOptions);
        await AtomicFileWriter.WriteAsync(SnapshotPath, json, ct).ConfigureAwait(false);
    }
}
