using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Meridian.Core.IO;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Storage.Archival;
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
    private readonly RootedPathGuard _pathGuard;
    private readonly ILogger<JsonlFilePaperSessionStore> _logger;

    // One lock for all append operations; paper-trading is not latency-sensitive.
    private readonly SemaphoreSlim _appendLock = new(1, 1);

    /// <summary>Root storage directory (guaranteed to be created on first write).</summary>
    public string BaseDirectory => _pathGuard.RootPath;

    public JsonlFilePaperSessionStore(
        string baseDirectory,
        ILogger<JsonlFilePaperSessionStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _pathGuard = new RootedPathGuard(baseDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ------------------------------------------------------------------
    // Write operations
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        EnsureSessionDirectory(record.SessionId);
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
        if (!Directory.Exists(BaseDirectory))
            return [];

        var sessions = new List<PersistedSessionRecord>();
        foreach (var directory in Directory.EnumerateDirectories(BaseDirectory))
        {
            ct.ThrowIfCancellationRequested();
            var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
            string metaPath;
            try
            {
                metaPath = _pathGuard.ResolvePath(directoryName, "session.json");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Skipped unsafe paper-session directory {Path}", directory);
                continue;
            }

            if (!File.Exists(metaPath))
                continue;

            var record = await TryLoadMetadataAsync(metaPath, ct).ConfigureAwait(false);
            if (record is null)
                continue;
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(record.SessionId, directoryName, pathComparison))
            {
                _logger.LogWarning(
                    "Skipped paper-session metadata whose SessionId {SessionId} does not match directory {Directory}",
                    record.SessionId,
                    directoryName);
                continue;
            }

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

    /// <inheritdoc />
    public async Task SaveLedgerJournalAsync(
        string sessionId,
        IReadOnlyList<PersistedJournalEntryDto> entries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        EnsureSessionDirectory(sessionId);

        // Build the full JSONL content in-memory then write atomically so a crash
        // during writing never leaves a partial ledger file.
        var sb = new System.Text.StringBuilder(entries.Count * 256);
        foreach (var entry in entries)
        {
            var line = JsonSerializer.Serialize(entry, ExecutionJsonContext.Default.PersistedJournalEntryDto);
            sb.AppendLine(line);
        }

        await WriteAtomicAsync(LedgerPath(sessionId), sb.ToString(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersistedJournalEntryDto>> LoadLedgerJournalAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        var path = LedgerPath(sessionId);
        if (!File.Exists(path))
            return [];

        return await LoadJsonlAsync(path, ExecutionJsonContext.Default.PersistedJournalEntryDto, _logger, ct)
            .ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Path helpers
    // ------------------------------------------------------------------

    private string SessionDir(string sessionId) =>
        _pathGuard.ResolvePath(sessionId);

    private string MetadataPath(string sessionId) =>
        _pathGuard.ResolvePath(sessionId, "session.json");

    private string FillsPath(string sessionId) =>
        _pathGuard.ResolvePath(sessionId, "fills.jsonl");

    private string OrdersPath(string sessionId) =>
        _pathGuard.ResolvePath(sessionId, "orders.jsonl");

    private string LedgerPath(string sessionId) =>
        _pathGuard.ResolvePath(sessionId, "ledger.jsonl");

    private void EnsureSessionDirectory(string sessionId)
    {
        var directory = SessionDir(sessionId);
        Directory.CreateDirectory(directory);
        _pathGuard.EnsurePath(directory);
    }

    // ------------------------------------------------------------------
    // IO helpers
    // ------------------------------------------------------------------

    private async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        _pathGuard.EnsurePath(path);
        await AtomicFileWriter.WriteAsync(path, content, ct).ConfigureAwait(false);
    }

    private async Task AppendLineAsync(string path, string line, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        _pathGuard.EnsurePath(path);

        await _appendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _pathGuard.EnsurePath(path);
            await AtomicFileWriter.AppendLinesAsync(path, [line], ct).ConfigureAwait(false);
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
            _pathGuard.EnsurePath(path);
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, ExecutionJsonContext.Default.PersistedSessionRecord);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Failed to load session metadata from {Path}", path);
            return null;
        }
    }

    private async Task<IReadOnlyList<T>> LoadJsonlAsync<T>(
        string path,
        JsonTypeInfo<T> typeInfo,
        ILogger logger,
        CancellationToken ct)
    {
        var results = new List<T>();
        _pathGuard.EnsurePath(path);
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
