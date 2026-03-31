using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.Services;

/// <summary>
/// File-system backed <see cref="IPaperSessionStore"/>.
/// Each session occupies its own directory under <see cref="BaseDirectory"/>:
/// <code>
///   {BaseDirectory}/
///     {sessionId}/
///       session.json   — session metadata, atomically replaced on every change
///       fills.jsonl    — one ExecutionReport JSON object per line (append-only)
///       orders.jsonl   — one OrderState JSON object per line (append-only)
/// </code>
/// Atomic writes for <c>session.json</c> use write-to-temp-then-rename semantics
/// to guard against partial writes on crash.  JSONL appends are serialised through
/// a global lock so concurrent callers never interleave partial lines.
/// </summary>
public sealed class JsonlFilePaperSessionStore : IPaperSessionStore
{
    private readonly string _baseDirectory;
    private readonly ILogger<JsonlFilePaperSessionStore> _logger;

    // One lock for all append operations; paper-trading is not latency-sensitive.
    private readonly SemaphoreSlim _appendLock = new(1, 1);

    /// <summary>Root storage directory (guaranteed to be created on first write).</summary>
    public string BaseDirectory => _baseDirectory;

    public JsonlFilePaperSessionStore(
        string baseDirectory,
        ILogger<JsonlFilePaperSessionStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = baseDirectory;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ------------------------------------------------------------------
    // Write operations
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        Directory.CreateDirectory(SessionDir(record.SessionId));
        var json = JsonSerializer.Serialize(record, ExecutionJsonContext.Default.PersistedSessionRecord);
        await WriteAtomicAsync(MetadataPath(record.SessionId), json, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AppendFillAsync(string sessionId, ExecutionReport fill, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fill);
        var json = JsonSerializer.Serialize(fill, ExecutionJsonContext.Default.ExecutionReport);
        await AppendLineAsync(FillsPath(sessionId), json, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AppendOrderUpdateAsync(string sessionId, OrderState order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        var json = JsonSerializer.Serialize(order, ExecutionJsonContext.Default.OrderState);
        await AppendLineAsync(OrdersPath(sessionId), json, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Read operations
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_baseDirectory))
            return [];

        var sessions = new List<PersistedSessionRecord>();
        foreach (var dir in Directory.EnumerateDirectories(_baseDirectory))
        {
            ct.ThrowIfCancellationRequested();
            var metaPath = Path.Combine(dir, "session.json");
            if (!File.Exists(metaPath))
                continue;

            var record = await TryLoadMetadataAsync(metaPath, ct).ConfigureAwait(false);
            if (record is not null)
                sessions.Add(record);
        }

        return sessions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(string sessionId, CancellationToken ct = default)
    {
        var path = FillsPath(sessionId);
        if (!File.Exists(path))
            return [];

        return await LoadJsonlAsync(path, ExecutionJsonContext.Default.ExecutionReport, _logger, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(string sessionId, CancellationToken ct = default)
    {
        var path = OrdersPath(sessionId);
        if (!File.Exists(path))
            return [];

        return await LoadJsonlAsync(path, ExecutionJsonContext.Default.OrderState, _logger, ct)
            .ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Path helpers
    // ------------------------------------------------------------------

    private string SessionDir(string sessionId) =>
        Path.Combine(_baseDirectory, sessionId);

    private string MetadataPath(string sessionId) =>
        Path.Combine(SessionDir(sessionId), "session.json");

    private string FillsPath(string sessionId) =>
        Path.Combine(SessionDir(sessionId), "fills.jsonl");

    private string OrdersPath(string sessionId) =>
        Path.Combine(SessionDir(sessionId), "orders.jsonl");

    // ------------------------------------------------------------------
    // IO helpers
    // ------------------------------------------------------------------

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmpPath = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tmpPath, content, Encoding.UTF8, ct).ConfigureAwait(false);
            File.Move(tmpPath, path, overwrite: true);
        }
        catch
        {
            try
            { File.Delete(tmpPath); }
            catch { /* best-effort cleanup */ }
            throw;
        }
    }

    private async Task AppendLineAsync(string path, string line, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        await _appendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var writer = new StreamWriter(path, append: true, Encoding.UTF8);
            await writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
        }
        finally
        {
            _appendLock.Release();
        }
    }

    private async Task<PersistedSessionRecord?> TryLoadMetadataAsync(string path, CancellationToken ct)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, ExecutionJsonContext.Default.PersistedSessionRecord);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Failed to load session metadata from {Path}", path);
            return null;
        }
    }

    private static async Task<IReadOnlyList<T>> LoadJsonlAsync<T>(
        string path,
        JsonTypeInfo<T> typeInfo,
        ILogger logger,
        CancellationToken ct)
    {
        var results = new List<T>();
        await using var fs = File.OpenRead(path);
        using var reader = new StreamReader(fs, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var item = JsonSerializer.Deserialize(line, typeInfo);
                if (item is not null)
                    results.Add(item);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Skipping corrupt JSONL record in {Path}", path);
            }
        }

        return results;
    }
}
