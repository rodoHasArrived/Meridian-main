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
    bool Completed = false);

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

    public FileManualJournalMutationRecoveryStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public async Task<IManualJournalMutationSession> OpenSessionAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(Path.Combine(_directory, "pending"));
        Directory.CreateDirectory(Path.Combine(_directory, "completed"));
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Never unlink the lock file: replacing its inode could grant two leases.
                var lease = new FileStream(Path.Combine(_directory, "mutation.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new Session(_directory, lease);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33)
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }

    private sealed class Session(string directory, FileStream lease) : IManualJournalMutationSession
    {
        private string PathFor(string key, bool completed)
        {
            if (key.Length != 64 || !key.All(char.IsAsciiHexDigit))
                throw new InvalidOperationException("Invalid manual journal command identity.");
            return Path.Combine(directory, completed ? "completed" : "pending", key + ".json");
        }

        public async Task<ManualJournalMutationIntent?> GetAsync(string commandKey, CancellationToken ct)
            => await ReadAsync(PathFor(commandKey, false), ct).ConfigureAwait(false)
               ?? await ReadAsync(PathFor(commandKey, true), ct).ConfigureAwait(false);

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
            await WriteAsync(PathFor(intent.CommandKey, true), intent with { Completed = true }, ct).ConfigureAwait(false);
            File.Delete(PathFor(intent.CommandKey, false));
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
            if (!File.Exists(path)) return null;
            var envelope = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false),
                ManualJournalMutationJsonContext.Default.Payload)
                ?? throw new InvalidOperationException("Manual journal recovery receipt is empty.");
            if (!string.Equals(envelope.Digest, Sha256Digest.ComputeUtf8(envelope.Json), StringComparison.Ordinal))
                throw new InvalidOperationException("Manual journal recovery receipt integrity check failed.");
            return JsonSerializer.Deserialize(envelope.Json, ManualJournalMutationJsonContext.Default.ManualJournalMutationIntent)
                ?? throw new InvalidOperationException("Manual journal recovery receipt is invalid.");
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
