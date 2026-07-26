using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Storage.Archival;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// File-backed authority for statement-break dispositions. A process-wide gate and an OS-level
/// lease serialize the complete load/mutate/checkpoint sequence across service instances.
/// </summary>
public sealed class FileStatementBreakDispositionTransactionStore : IStatementBreakDispositionTransactionStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _snapshotPath;
    private readonly string _mutationLockPath;
    private readonly Func<string, string, CancellationToken, Task> _stateWriter;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = CreateJsonOptions(writeIndented: true);

    public FileStatementBreakDispositionTransactionStore(
        string dataRoot,
        Func<string, string, CancellationToken, Task>? stateWriter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        var folder = Path.Combine(dataRoot, "reconciliation");
        Directory.CreateDirectory(folder);
        _snapshotPath = Path.Combine(folder, "statement-break-dispositions.json");
        _mutationLockPath = Path.Combine(folder, "statement-break-dispositions.lock");
        _stateWriter = stateWriter ?? AtomicFileWriter.WriteAsync;
    }

    public async Task<TResult> ExecuteExclusiveAsync<TResult>(
        Func<IStatementBreakDispositionTransactionSession, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var mutationLease = await AcquireMutationLeaseAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var session = new TransactionSession(this, snapshot);
            try
            {
                return await operation(session, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                session.Deactivate();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StatementBreakDispositionTransactionSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var mutationLease = await AcquireMutationLeaseAsync(cancellationToken).ConfigureAwait(false);
            return await LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveAsync(
        StatementBreakDispositionTransactionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported statement break disposition snapshot schema version '{snapshot.SchemaVersion}'.");
        }

        var stamped = snapshot with
        {
            ContentHashSha256 = StatementBreakDispositionHashing.ComputeSnapshotHash(snapshot)
        };
        ValidateSnapshot(stamped);
        var json = JsonSerializer.Serialize(stamped, _jsonOptions);
        await _stateWriter(_snapshotPath, json, cancellationToken).ConfigureAwait(false);
    }

    private async Task<StatementBreakDispositionTransactionSnapshot> LoadAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_snapshotPath))
        {
            var empty = StatementBreakDispositionTransactionSnapshot.Empty;
            return empty with
            {
                ContentHashSha256 = StatementBreakDispositionHashing.ComputeSnapshotHash(empty)
            };
        }

        try
        {
            await using var stream = new FileStream(
                _snapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshot = await JsonSerializer
                .DeserializeAsync<StatementBreakDispositionTransactionSnapshot>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException("Statement break disposition snapshot was empty.");
            ValidateSnapshot(snapshot);
            return snapshot;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Statement break disposition snapshot is invalid JSON.", ex);
        }
    }

    private static void ValidateSnapshot(StatementBreakDispositionTransactionSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported statement break disposition snapshot schema version '{snapshot.SchemaVersion}'.");
        }
        if (snapshot.Transactions is null || snapshot.CommandReceipts is null || snapshot.AuditHistory is null)
        {
            throw new InvalidDataException("Statement break disposition snapshot collections are required.");
        }

        var expectedSnapshotHash = StatementBreakDispositionHashing.ComputeSnapshotHash(snapshot);
        if (!StatementBreakDispositionHashing.IsSha256(snapshot.ContentHashSha256) ||
            !string.Equals(snapshot.ContentHashSha256, expectedSnapshotHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Statement break disposition snapshot failed content hash verification.");
        }

        var transactions = new Dictionary<string, StatementBreakDispositionTransaction>(StringComparer.Ordinal);
        var dispositionedBreakIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var transaction in snapshot.Transactions)
        {
            if (string.IsNullOrWhiteSpace(transaction.TransactionId) ||
                !transactions.TryAdd(transaction.TransactionId, transaction) ||
                string.IsNullOrWhiteSpace(transaction.CommandId) ||
                string.IsNullOrWhiteSpace(transaction.BreakId) ||
                !dispositionedBreakIds.Add(transaction.BreakId) ||
                string.IsNullOrWhiteSpace(transaction.CaseId) ||
                string.IsNullOrWhiteSpace(transaction.Actor) ||
                string.IsNullOrWhiteSpace(transaction.Rationale) ||
                transaction.EvidenceLinks is null ||
                transaction.EvidenceLinks.Count == 0 ||
                transaction.ExpectedVersion < 0 ||
                transaction.ExpectedVersion == long.MaxValue ||
                !Enum.IsDefined(transaction.State) ||
                !StatementBreakDispositionHashing.IsSha256(transaction.InputHashSha256) ||
                !StatementBreakDispositionHashing.IsSha256(transaction.EvidenceHashSha256) ||
                !string.Equals(
                    transaction.EvidenceHashSha256,
                    StatementBreakDispositionHashing.HashCanonical(transaction.EvidenceLinks),
                    StringComparison.Ordinal) ||
                transaction.Version != transaction.ExpectedVersion + 1 ||
                transaction.BreakAfter.Version != transaction.Version ||
                transaction.CaseAfter.Version != transaction.Version ||
                !string.Equals(transaction.BreakAfter.BreakId, transaction.BreakId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(transaction.CaseAfter.CaseId, transaction.CaseId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(transaction.BreakAfter.Disposition, transaction.Disposition.ToString(), StringComparison.Ordinal) ||
                !string.Equals(transaction.CaseAfter.Disposition, transaction.Disposition.ToString(), StringComparison.Ordinal) ||
                !string.Equals(transaction.BreakAfter.DispositionActor, transaction.Actor, StringComparison.Ordinal) ||
                !string.Equals(transaction.BreakAfter.DispositionRationale, transaction.Rationale, StringComparison.Ordinal) ||
                !transaction.BreakAfter.DispositionEvidenceLinks.SequenceEqual(transaction.EvidenceLinks, StringComparer.Ordinal) ||
                !string.Equals(transaction.BreakAfter.SupersedingBreakId, transaction.SupersedingBreakId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(transaction.CaseAfter.BreakId, transaction.BreakId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(transaction.CaseAfter.LastUpdatedBy, transaction.Actor, StringComparison.Ordinal) ||
                !string.Equals(transaction.CaseAfter.Rationale, transaction.Rationale, StringComparison.Ordinal) ||
                !string.Equals(transaction.BreakAfter.DispositionEvidenceHash, transaction.EvidenceHashSha256, StringComparison.Ordinal) ||
                !string.Equals(transaction.BreakAfter.DispositionTransactionId, transaction.TransactionId, StringComparison.Ordinal) ||
                !string.Equals(transaction.CaseAfter.DispositionTransactionId, transaction.TransactionId, StringComparison.Ordinal) ||
                (transaction.State == StatementBreakDispositionTransactionState.Completed) != transaction.CompletedAtUtc.HasValue)
            {
                throw new InvalidDataException(
                    $"Statement break disposition transaction '{transaction.TransactionId}' is inconsistent.");
            }
        }

        var receiptIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var receipt in snapshot.CommandReceipts)
        {
            if (string.IsNullOrWhiteSpace(receipt.CommandId) ||
                !receiptIds.Add(receipt.CommandId) ||
                !transactions.TryGetValue(receipt.TransactionId, out var transaction) ||
                !string.Equals(receipt.BreakId, transaction.BreakId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(receipt.CommandId, transaction.CommandId, StringComparison.Ordinal) ||
                !string.Equals(receipt.InputHashSha256, transaction.InputHashSha256, StringComparison.Ordinal) ||
                receipt.ExpectedVersion != transaction.ExpectedVersion)
            {
                throw new InvalidDataException(
                    $"Statement break disposition command receipt '{receipt.CommandId}' is inconsistent.");
            }
        }
        if (receiptIds.Count != transactions.Count)
        {
            throw new InvalidDataException("Every statement break disposition transaction must have one command receipt.");
        }

        string? previousHash = null;
        long expectedSequence = 1;
        var transactionIdsWithAudit = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in snapshot.AuditHistory)
        {
            if (string.IsNullOrWhiteSpace(entry.AuditId) ||
                entry.EvidenceLinks is null ||
                !StatementBreakDispositionHashing.IsSha256(entry.EntryHash) ||
                entry.Sequence != expectedSequence ||
                !string.Equals(entry.PreviousHash, previousHash, StringComparison.Ordinal) ||
                !transactions.TryGetValue(entry.TransactionId, out var transaction) ||
                !transactionIdsWithAudit.Add(entry.TransactionId) ||
                !string.Equals(entry.CommandId, transaction.CommandId, StringComparison.Ordinal) ||
                !string.Equals(entry.BreakId, transaction.BreakId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(entry.CaseId, transaction.CaseId, StringComparison.OrdinalIgnoreCase) ||
                entry.Version != transaction.Version ||
                entry.Disposition != transaction.Disposition ||
                !string.Equals(entry.Actor, transaction.Actor, StringComparison.Ordinal) ||
                !string.Equals(entry.Rationale, transaction.Rationale, StringComparison.Ordinal) ||
                !entry.EvidenceLinks.SequenceEqual(transaction.EvidenceLinks, StringComparer.Ordinal) ||
                !transaction.CaseAfter.AuditEvents.Any(caseAudit =>
                    string.Equals(caseAudit.EventId, entry.AuditId, StringComparison.Ordinal) &&
                    string.Equals(caseAudit.TransactionId, entry.TransactionId, StringComparison.Ordinal) &&
                    caseAudit.Version == entry.Version &&
                    string.Equals(caseAudit.Actor, entry.Actor, StringComparison.Ordinal) &&
                    string.Equals(caseAudit.Rationale, entry.Rationale, StringComparison.Ordinal) &&
                    string.Equals(caseAudit.PreviousHash, entry.PreviousHash, StringComparison.Ordinal) &&
                    string.Equals(caseAudit.EntryHash, entry.EntryHash, StringComparison.Ordinal)) ||
                !string.Equals(
                    entry.EntryHash,
                    StatementBreakDispositionHashing.ComputeAuditEntryHash(entry),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Statement break disposition audit entry '{entry.AuditId}' failed sequence or hash-chain validation.");
            }

            previousHash = entry.EntryHash;
            expectedSequence++;
        }
        if (transactionIdsWithAudit.Count != transactions.Count)
        {
            throw new InvalidDataException("Every statement break disposition transaction must have one audit entry.");
        }
    }

    private async Task<FileStream> AcquireMutationLeaseAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _mutationLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = writeIndented
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class TransactionSession(
        FileStatementBreakDispositionTransactionStore owner,
        StatementBreakDispositionTransactionSnapshot snapshot)
        : IStatementBreakDispositionTransactionSession
    {
        private bool _active = true;

        public StatementBreakDispositionTransactionSnapshot Snapshot { get; private set; } = snapshot;

        public async Task SaveAsync(
            StatementBreakDispositionTransactionSnapshot updatedSnapshot,
            CancellationToken cancellationToken = default)
        {
            if (!_active)
            {
                throw new InvalidOperationException("The statement break disposition transaction session is no longer active.");
            }

            ArgumentNullException.ThrowIfNull(updatedSnapshot);
            await owner.SaveAsync(updatedSnapshot, cancellationToken).ConfigureAwait(false);
            Snapshot = updatedSnapshot with
            {
                ContentHashSha256 = StatementBreakDispositionHashing.ComputeSnapshotHash(updatedSnapshot)
            };
        }

        public void Deactivate() => _active = false;
    }
}

internal static class StatementBreakDispositionHashing
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = CreateCanonicalJsonOptions();

    public static string HashCanonical<T>(T value)
        => HashUtf8(JsonSerializer.Serialize(value, CanonicalJsonOptions));

    public static string ComputeSnapshotHash(StatementBreakDispositionTransactionSnapshot snapshot)
        => HashCanonical(snapshot with { ContentHashSha256 = null });

    public static string ComputeAuditEntryHash(StatementBreakDispositionAuditEntry entry)
        => HashCanonical(entry with { EntryHash = string.Empty });

    public static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string HashUtf8(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static JsonSerializerOptions CreateCanonicalJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
