using System.Collections.Concurrent;
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
///       fills.jsonl          — one versioned fill claim per line (legacy raw reports still read)
///       fills.applied.jsonl  — idempotent apply acknowledgements
///       orders.jsonl         — one OrderState JSON object per line (append-only)
/// </code>
/// Atomic writes for <c>session.json</c> use write-to-temp-then-rename semantics
/// to guard against partial writes on crash. JSONL appends are serialised per base
/// directory inside this process so concurrent callers never interleave partial lines.
/// Exactly one process may write a base directory; cross-process transactional locking
/// is intentionally outside this local-file store's contract.
/// </summary>
public sealed class JsonlFilePaperSessionStore : IPaperSessionStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> AppendLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly RootedPathGuard _pathGuard;
    private readonly ILogger<JsonlFilePaperSessionStore> _logger;
    private readonly SemaphoreSlim _appendLock;

    /// <summary>Root storage directory (guaranteed to be created on first write).</summary>
    public string BaseDirectory => _pathGuard.RootPath;

    public JsonlFilePaperSessionStore(
        string baseDirectory,
        ILogger<JsonlFilePaperSessionStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _pathGuard = new RootedPathGuard(baseDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _appendLock = AppendLocks.GetOrAdd(BaseDirectory, static _ => new SemaphoreSlim(1, 1));
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
        var record = PaperSessionFillRecord.CreateCanonical(
            fill,
            fill.Timestamp);
        var result = await TryAppendFillAsync(sessionId, record, ct).ConfigureAwait(false);
        if (result.Status == PaperSessionFillAppendStatus.Conflict)
        {
            throw new InvalidDataException(
                $"Paper-session FillId '{record.FillId:D}' is already claimed by different content.");
        }

        // This compatibility API historically meant "append an already-applied fill". Preserve
        // that meaning through the durable protocol; a crash between claim and acknowledgement is
        // recoverable because the claim remains explicitly unapplied.
        await MarkFillAppliedAsync(
            sessionId,
            record.FillId,
            record.CanonicalHash,
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PaperSessionFillAppendResult> TryAppendFillAsync(
        string sessionId,
        PaperSessionFillRecord record,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Validate();

        await _appendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = (await LoadFillRecordsCoreAsync(sessionId, ct).ConfigureAwait(false))
                .FirstOrDefault(candidate => candidate.FillId == record.FillId);
            if (existing is not null)
            {
                return new PaperSessionFillAppendResult(
                    string.Equals(existing.CanonicalHash, record.CanonicalHash, StringComparison.Ordinal)
                        ? PaperSessionFillAppendStatus.ExistingSame
                        : PaperSessionFillAppendStatus.Conflict,
                    existing.CanonicalHash);
            }

            var persisted = record with { IsApplied = false };
            var json = JsonSerializer.Serialize(
                persisted,
                ExecutionJsonContext.Default.PaperSessionFillRecord);
            await AppendLineUnderLockAsync(FillsPath(sessionId), json, ct).ConfigureAwait(false);
            return new PaperSessionFillAppendResult(PaperSessionFillAppendStatus.Added);
        }
        finally
        {
            _appendLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task MarkFillAppliedAsync(
        string sessionId,
        Guid fillId,
        string canonicalHash,
        CancellationToken ct = default)
    {
        if (fillId == Guid.Empty)
            throw new ArgumentException("A paper-session fill acknowledgement requires a FillId.", nameof(fillId));
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalHash);

        await _appendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var records = await LoadFillRecordsCoreAsync(sessionId, ct).ConfigureAwait(false);
            var fill = records.FirstOrDefault(candidate => candidate.FillId == fillId)
                ?? throw new InvalidDataException(
                    $"Cannot acknowledge unknown paper-session fill '{fillId:D}'.");
            if (!string.Equals(fill.CanonicalHash, canonicalHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Paper-session fill acknowledgement '{fillId:D}' conflicts with the durable claim.");
            }

            if (fill.IsApplied)
                return;

            var acknowledgement = new PaperSessionFillAppliedRecord(
                PaperSessionFillRecord.CurrentSchemaVersion,
                fillId,
                canonicalHash,
                DateTimeOffset.UtcNow);
            var json = JsonSerializer.Serialize(
                acknowledgement,
                ExecutionJsonContext.Default.PaperSessionFillAppliedRecord);
            await AppendLineUnderLockAsync(FillApplicationsPath(sessionId), json, ct).ConfigureAwait(false);
        }
        finally
        {
            _appendLock.Release();
        }
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
        var records = await LoadFillRecordsAsync(sessionId, ct).ConfigureAwait(false);
        return records.Select(static record => record.Fill).ToArray();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PaperSessionFillRecord>> LoadFillRecordsAsync(
        string sessionId,
        CancellationToken ct = default)
        => LoadFillRecordsCoreAsync(sessionId, ct);

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

    private string FillApplicationsPath(string sessionId) =>
        _pathGuard.ResolvePath(sessionId, "fills.applied.jsonl");

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
            await AppendLineUnderLockAsync(path, line, ct).ConfigureAwait(false);
        }
        finally
        {
            _appendLock.Release();
        }
    }

    private async Task AppendLineUnderLockAsync(string path, string line, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        _pathGuard.EnsurePath(path);
        await AtomicFileWriter.AppendLinesAsync(path, [line], ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PaperSessionFillRecord>> LoadFillRecordsCoreAsync(
        string sessionId,
        CancellationToken ct)
    {
        var path = FillsPath(sessionId);
        var applications = await LoadFillApplicationsAsync(sessionId, ct).ConfigureAwait(false);
        if (!File.Exists(path))
        {
            if (applications.Count > 0)
            {
                throw new InvalidDataException(
                    $"Paper session '{sessionId}' has fill acknowledgements but no durable fill log.");
            }

            return [];
        }

        var results = new List<PaperSessionFillRecord>();
        var byId = new Dictionary<Guid, PaperSessionFillRecord>();
        _pathGuard.EnsurePath(path);
        await using var fs = File.OpenRead(path);
        using var reader = new StreamReader(fs, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            PaperSessionFillRecord record;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("schemaVersion", out _)
                    && root.TryGetProperty("fillId", out _)
                    && root.TryGetProperty("fill", out _))
                {
                    record = JsonSerializer.Deserialize(
                            line,
                            ExecutionJsonContext.Default.PaperSessionFillRecord)
                        ?? throw new InvalidDataException($"Paper-session fill envelope in '{path}' was null.");
                    record.Validate();

                    if (applications.TryGetValue(record.FillId, out var appliedHash))
                    {
                        if (!string.Equals(appliedHash, record.CanonicalHash, StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(
                                $"Paper-session fill acknowledgement '{record.FillId:D}' conflicts with its claim.");
                        }

                        record = record with { IsApplied = true };
                    }
                    else
                    {
                        record = record with { IsApplied = false };
                    }
                }
                else
                {
                    // Legacy files contained a raw ExecutionReport per line. They remain readable
                    // and are considered acknowledged because the old writer applied before append.
                    var legacyFill = JsonSerializer.Deserialize(
                            line,
                            ExecutionJsonContext.Default.ExecutionReport)
                        ?? throw new InvalidDataException($"Legacy paper-session fill in '{path}' was null.");
                    record = PaperSessionFillRecord.CreateCanonical(
                        legacyFill,
                        legacyFill.Timestamp,
                        isApplied: true);
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Corrupt paper-session fill record in '{path}'.", ex);
            }

            if (byId.TryGetValue(record.FillId, out var duplicate))
            {
                if (!string.Equals(duplicate.CanonicalHash, record.CanonicalHash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Paper-session FillId '{record.FillId:D}' has conflicting durable records.");
                }

                continue;
            }

            byId.Add(record.FillId, record);
            results.Add(record);
        }

        foreach (var acknowledgedFillId in applications.Keys)
        {
            if (!byId.ContainsKey(acknowledgedFillId))
            {
                throw new InvalidDataException(
                    $"Paper-session fill acknowledgement '{acknowledgedFillId:D}' has no durable fill claim.");
            }
        }

        return results;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadFillApplicationsAsync(
        string sessionId,
        CancellationToken ct)
    {
        var path = FillApplicationsPath(sessionId);
        if (!File.Exists(path))
            return new Dictionary<Guid, string>();

        var applications = new Dictionary<Guid, string>();
        _pathGuard.EnsurePath(path);
        await using var fs = File.OpenRead(path);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            PaperSessionFillAppliedRecord acknowledgement;
            try
            {
                acknowledgement = JsonSerializer.Deserialize(
                        line,
                        ExecutionJsonContext.Default.PaperSessionFillAppliedRecord)
                    ?? throw new InvalidDataException($"Paper-session fill acknowledgement in '{path}' was null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Corrupt paper-session fill acknowledgement in '{path}'.", ex);
            }

            if (acknowledgement.SchemaVersion != PaperSessionFillRecord.CurrentSchemaVersion
                || acknowledgement.FillId == Guid.Empty
                || string.IsNullOrWhiteSpace(acknowledgement.CanonicalHash))
            {
                throw new InvalidDataException($"Invalid paper-session fill acknowledgement in '{path}'.");
            }

            if (applications.TryGetValue(acknowledgement.FillId, out var existingHash)
                && !string.Equals(existingHash, acknowledgement.CanonicalHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Paper-session fill acknowledgement '{acknowledgement.FillId:D}' has conflicting hashes.");
            }

            applications[acknowledgement.FillId] = acknowledgement.CanonicalHash;
        }

        return applications;
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

internal sealed record PaperSessionFillAppliedRecord(
    int SchemaVersion,
    Guid FillId,
    string CanonicalHash,
    DateTimeOffset AppliedAt);
