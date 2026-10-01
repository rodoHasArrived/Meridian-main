using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>A complete command receipt, retained before any draft or journal write.</summary>
public sealed record ManualJournalMutationIntent(
    string CommandKey,
    string RequestHash,
    string ScopeKey,
    Guid JournalEntryId,
    IReadOnlyList<ManualJournalEntryDraftDto?> Before,
    IReadOnlyList<ManualJournalEntryDraftDto> After,
    IReadOnlyList<AccountingActionAuditEventDto> AuditEvents,
    LedgerJournalEntryWrite? Posting,
    JsonElement Result,
    bool Completed = false,
    DateTimeOffset? CompletedAtUtc = null);

public sealed record ManualJournalMutationRecoveryOptions
{
    public TimeSpan MaxCompletedAge { get; init; } = TimeSpan.FromDays(30);
    public int MaxCompletedCount { get; init; } = 1000;
    public long MaxCompletedBytes { get; init; } = 64 * 1024 * 1024;
}

/// <summary>
/// Serializes the entire manual command, including its draft read, validation, write and audit.
/// All writers sharing a draft snapshot must use the same recovery directory. The file store
/// holds an operating-system exclusive lock; an instance-only semaphore is insufficient.
/// </summary>
public interface IManualJournalMutationRecoveryStore
{
    Task<IManualJournalMutationSession> OpenSessionAsync(CancellationToken ct = default);
}

public interface IManualJournalMutationSession : IAsyncDisposable
{
    Task<ManualJournalMutationIntent?> GetAsync(string commandKey, CancellationToken ct);
    Task<IReadOnlyList<ManualJournalMutationIntent>> ListPendingAsync(CancellationToken ct);
    Task RetainAsync(ManualJournalMutationIntent intent, CancellationToken ct);
    Task CompleteAsync(ManualJournalMutationIntent intent, CancellationToken ct);
    Task DiscardUnappliedAsync(ManualJournalMutationIntent intent, CancellationToken ct);
}

/// <summary>Durable receipts and a cross-process lease beside the manual draft snapshot.</summary>
public sealed class FileManualJournalMutationRecoveryStore : IManualJournalMutationRecoveryStore
{
    private readonly string _directory;
    private readonly ManualJournalMutationRecoveryOptions _options;
    private readonly TimeProvider _clock;

    public FileManualJournalMutationRecoveryStore(string directory,
        ManualJournalMutationRecoveryOptions? options = null, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        _options = options ?? new ManualJournalMutationRecoveryOptions();
        _clock = clock ?? TimeProvider.System;
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxCompletedAge, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(_options.MaxCompletedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(_options.MaxCompletedBytes);
    }

    public async Task<IManualJournalMutationSession> OpenSessionAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(Path.Combine(_directory, "pending"));
        Directory.CreateDirectory(Path.Combine(_directory, "completed"));
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            FileStream lease;
            try
            {
                // Never unlink the lock file: replacing its inode could grant two leases.
                lease = new FileStream(Path.Combine(_directory, "mutation.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33)
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
                continue;
            }
            var session = new Session(_directory, lease, _options, _clock);
            try
            {
                await session.MaintainAsync(ct).ConfigureAwait(false);
                return session;
            }
            catch
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private sealed class Session(string directory, FileStream lease,
        ManualJournalMutationRecoveryOptions options, TimeProvider clock) : IManualJournalMutationSession
    {
        private string PathFor(string key, bool completed)
        {
            if (key.Length != 64 || !key.All(char.IsAsciiHexDigit))
                throw new InvalidOperationException("Invalid manual journal command identity.");
            return Path.Combine(directory, completed ? "completed" : "pending", key + ".json");
        }

        public async Task<ManualJournalMutationIntent?> GetAsync(string commandKey, CancellationToken ct)
        {
            var pending = await ReadAsync(PathFor(commandKey, false), ct).ConfigureAwait(false);
            var completed = await ReadAsync(PathFor(commandKey, true), ct).ConfigureAwait(false);
            var archived = await ReadArchiveAsync(commandKey, ct).ConfigureAwait(false);
            if (completed is not null && archived is not null)
                RequireSameBytes(await File.ReadAllBytesAsync(PathFor(commandKey, true), ct).ConfigureAwait(false), archived.Value.Bytes);
            var retained = completed ?? archived?.Intent;
            if (pending is not null && retained is not null &&
                JsonSerializer.Serialize(pending with { Completed = false, CompletedAtUtc = null }, ManualJournalMutationJsonContext.Default.ManualJournalMutationIntent) !=
                JsonSerializer.Serialize(retained with { Completed = false, CompletedAtUtc = null }, ManualJournalMutationJsonContext.Default.ManualJournalMutationIntent))
                throw new InvalidOperationException("Pending and completed manual journal receipts conflict.");
            return pending ?? retained;
        }

        private string ArchivePath(string key)
        {
            _ = PathFor(key, true);
            return Path.Combine(directory, "archive", key[..2], key[2..4], key + ".json.gz");
        }

        public async Task<IReadOnlyList<ManualJournalMutationIntent>> ListPendingAsync(CancellationToken ct)
        {
            var result = new List<ManualJournalMutationIntent>();
            foreach (var path in Directory.EnumerateFiles(Path.Combine(directory, "pending"), "*.json"))
                result.Add(await ReadAsync(path, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("A manual journal recovery receipt disappeared."));
            return result;
        }

        public async Task RetainAsync(ManualJournalMutationIntent intent, CancellationToken ct)
        {
            if (await GetAsync(intent.CommandKey, ct).ConfigureAwait(false) is not null)
                throw new InvalidOperationException("A manual journal command receipt already exists.");
            await WriteAsync(PathFor(intent.CommandKey, false), intent, ct).ConfigureAwait(false);
        }

        public async Task CompleteAsync(ManualJournalMutationIntent intent, CancellationToken ct)
        {
            // A crash between these operations leaves the original pending receipt repairable.
            _ = await GetAsync(intent.CommandKey, ct).ConfigureAwait(false);
            var existing = await ReadAsync(PathFor(intent.CommandKey, true), ct).ConfigureAwait(false)
                ?? (await ReadArchiveAsync(intent.CommandKey, ct).ConfigureAwait(false))?.Intent;
            if (existing is null)
                await WriteAsync(PathFor(intent.CommandKey, true), intent with { Completed = true, CompletedAtUtc = clock.GetUtcNow() }, ct).ConfigureAwait(false);
            File.Delete(PathFor(intent.CommandKey, false));
            await MaintainAsync(ct).ConfigureAwait(false);
        }

        public async Task MaintainAsync(CancellationToken ct)
        {
            try
            {
                await MaintainCoreAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"Manual journal receipt maintenance failed under '{directory}'. Preserve the recovery store, correct the storage or access problem, and retry; no unverified receipt copy was removed.", ex);
            }
        }

        private async Task MaintainCoreAsync(CancellationToken ct)
        {
            var candidates = new List<(string Path, ManualJournalMutationIntent Intent, long Size, DateTimeOffset CompletedAt)>();
            foreach (var path in Directory.EnumerateFiles(Path.Combine(directory, "completed"), "*.json"))
            {
                ct.ThrowIfCancellationRequested();
                var intent = await ReadAsync(path, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("A completed manual journal receipt disappeared during retention.");
                _ = await GetAsync(intent.CommandKey, ct).ConfigureAwait(false);
                var completedAt = intent.CompletedAtUtc ?? intent.AuditEvents.Select(audit => audit.RecordedAtUtc).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
                candidates.Add((path, intent, new FileInfo(path).Length, completedAt));
            }
            var count = candidates.Count;
            var bytes = candidates.Sum(item => item.Size);
            var now = clock.GetUtcNow();
            foreach (var item in candidates.OrderBy(item => item.CompletedAt).ThenBy(item => item.Intent.CommandKey, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                // A verified completed copy can move while an identical pending handoff remains.
                // Never move or delete the pending receipt here; recovery finishes that handoff.
                var archived = await ReadArchiveAsync(item.Intent.CommandKey, ct).ConfigureAwait(false);
                if (archived is null && now - item.CompletedAt <= options.MaxCompletedAge &&
                    count <= options.MaxCompletedCount && bytes <= options.MaxCompletedBytes)
                    continue;
                var original = await File.ReadAllBytesAsync(item.Path, ct).ConfigureAwait(false);
                if (archived is null)
                {
                    using var buffer = new MemoryStream();
                    await using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                        await gzip.WriteAsync(original, ct).ConfigureAwait(false);
                    await AtomicFileWriter.WriteAsync(ArchivePath(item.Intent.CommandKey), buffer.ToArray(), ct).ConfigureAwait(false);
                    archived = await ReadArchiveAsync(item.Intent.CommandKey, ct).ConfigureAwait(false);
                }
                if (archived is null)
                    throw new IOException("Manual journal receipt archive verification failed; retain the completed receipt and retry maintenance.");
                RequireSameBytes(original, archived.Value.Bytes);
                ct.ThrowIfCancellationRequested();
                File.Delete(item.Path);
                count--;
                bytes -= item.Size;
            }
        }

        private static void RequireSameBytes(byte[] first, byte[] second)
        {
            if (!first.AsSpan().SequenceEqual(second))
                throw new InvalidOperationException("Completed and archived manual journal receipts conflict; preserve both copies for investigation.");
        }

        private async Task<(ManualJournalMutationIntent Intent, byte[] Bytes)?> ReadArchiveAsync(string key, CancellationToken ct)
        {
            var path = ArchivePath(key);
            if (!File.Exists(path))
                return null;
            await using var stream = File.OpenRead(path);
            await using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var buffer = new MemoryStream();
            await gzip.CopyToAsync(buffer, ct).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            return (ReadPayload(Encoding.UTF8.GetString(bytes), key, completedLocation: true), bytes);
        }

        public Task DiscardUnappliedAsync(ManualJournalMutationIntent intent, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            File.Delete(PathFor(intent.CommandKey, false));
            return Task.CompletedTask;
        }

        private static async Task WriteAsync(string path, ManualJournalMutationIntent intent, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(intent, ManualJournalMutationJsonContext.Default.ManualJournalMutationIntent);
            // Hash the serialized payload, not an in-memory object after a potentially lossy reload.
            var envelope = JsonSerializer.Serialize(new Payload(Sha256Digest.ComputeUtf8(json), json),
                ManualJournalMutationJsonContext.Default.Payload);
            await AtomicFileWriter.WriteAsync(path, envelope, ct).ConfigureAwait(false);
        }

        private static async Task<ManualJournalMutationIntent?> ReadAsync(string path, CancellationToken ct)
        {
            if (!File.Exists(path))
                return null;
            var completedLocation = string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "completed", StringComparison.Ordinal);
            return ReadPayload(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), Path.GetFileNameWithoutExtension(path), completedLocation);
        }

        private static ManualJournalMutationIntent ReadPayload(string json, string commandKey, bool completedLocation)
        {
            var envelope = JsonSerializer.Deserialize(json,
                ManualJournalMutationJsonContext.Default.Payload)
                ?? throw new InvalidOperationException("Manual journal recovery receipt is empty.");
            if (!string.Equals(envelope.Digest, Sha256Digest.ComputeUtf8(envelope.Json), StringComparison.Ordinal))
                throw new InvalidOperationException("Manual journal recovery receipt integrity check failed.");
            var intent = JsonSerializer.Deserialize(envelope.Json, ManualJournalMutationJsonContext.Default.ManualJournalMutationIntent)
                ?? throw new InvalidOperationException("Manual journal recovery receipt is invalid.");
            if (!string.Equals(intent.CommandKey, commandKey, StringComparison.Ordinal) ||
                intent.Completed != completedLocation)
                throw new InvalidOperationException("Manual journal recovery receipt identity or state disagrees with its location.");
            return intent;
        }

        public ValueTask DisposeAsync() => lease.DisposeAsync();
    }

    internal sealed record Payload(string Digest, string Json);
}

/// <summary>Non-production recovery store for in-memory workbenches and focused tests.</summary>
public sealed class InMemoryManualJournalMutationRecoveryStore : IManualJournalMutationRecoveryStore,
    Meridian.Application.Composition.INonProductionOnlyService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ManualJournalMutationIntent> _intents = new(StringComparer.Ordinal);

    public async Task<IManualJournalMutationSession> OpenSessionAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        return new Session(this);
    }

    private sealed class Session(InMemoryManualJournalMutationRecoveryStore owner) : IManualJournalMutationSession
    {
        public Task<ManualJournalMutationIntent?> GetAsync(string key, CancellationToken ct)
            => Task.FromResult(owner._intents.GetValueOrDefault(key));
        public Task<IReadOnlyList<ManualJournalMutationIntent>> ListPendingAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ManualJournalMutationIntent>>(owner._intents.Values.Where(x => !x.Completed).ToArray());
        public Task RetainAsync(ManualJournalMutationIntent intent, CancellationToken ct)
        {
            owner._intents.Add(intent.CommandKey, intent);
            return Task.CompletedTask;
        }
        public Task CompleteAsync(ManualJournalMutationIntent intent, CancellationToken ct)
        {
            owner._intents[intent.CommandKey] = intent with { Completed = true };
            return Task.CompletedTask;
        }
        public Task DiscardUnappliedAsync(ManualJournalMutationIntent intent, CancellationToken ct)
        {
            owner._intents.Remove(intent.CommandKey);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() { owner._gate.Release(); return ValueTask.CompletedTask; }
    }
}

[JsonSerializable(typeof(ManualJournalMutationIntent))]
[JsonSerializable(typeof(FileManualJournalMutationRecoveryStore.Payload))]
[JsonSerializable(typeof(SaveManualJournalEntryDraftRequest))]
[JsonSerializable(typeof(SubmitManualJournalEntryApprovalRequest))]
[JsonSerializable(typeof(AttachManualJournalEntryEvidenceRequest))]
[JsonSerializable(typeof(JournalEntryLifecycleActionRequestDto))]
[JsonSerializable(typeof(JournalEntryLifecycleActionResultDto))]
[JsonSerializable(typeof(ManualJournalEntryDraftDto))]
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
internal partial class ManualJournalMutationJsonContext : JsonSerializerContext
{
}
