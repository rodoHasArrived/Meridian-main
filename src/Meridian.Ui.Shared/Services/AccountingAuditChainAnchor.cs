using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Shared.Services;

/// <summary>Raised when the external head journal is unreadable or internally inconsistent.</summary>
public sealed class AccountingAuditChainAnchorException : Exception
{
    public AccountingAuditChainAnchorException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The monotonic head of a file-backed accounting audit chain, retained <b>outside</b> the snapshot
/// it protects.
/// </summary>
/// <remarks>
/// <para><b>Why the head cannot live in the snapshot.</b> A whole-file snapshot store replaces the
/// document on every write. A predecessor-hash chain stored inside that document therefore verifies
/// happily after a rollback to an older valid copy, or after the newest events are removed together
/// with the stored head: what remains is a shorter chain that is internally perfect. Chaining
/// detects <i>reordering and mutation</i>; only an anchor the writer cannot rewrite in the same
/// replacement detects <i>deletion and rollback</i>.</para>
///
/// <para><b>Write-ahead ordering.</b> An append records a <see cref="AccountingAuditChainAnchorPhase.Pending"/>
/// line, writes the snapshot, then records <see cref="AccountingAuditChainAnchorPhase.Committed"/>.
/// A crash therefore leaves the journal at most one append ahead of the snapshot, which is
/// distinguishable from a rollback — where the snapshot falls behind a <i>committed</i> head — so an
/// interrupted write is not reported as tampering and tampering is not excused as a crash.</para>
///
/// <para><b>What this does not claim.</b> The journal is append-only by discipline, not by the
/// filesystem. An actor who can delete or rewrite <i>both</i> the snapshot and this journal
/// consistently defeats it; a WORM or remote authority is what removes that residue. What it does
/// close is the case the snapshot store makes easy: replacing the audited document alone.</para>
/// </remarks>
public sealed class FileAccountingAuditChainAnchor
{
    /// <summary>Anchor journal format version.</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly TimeSpan CrossProcessLockTimeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // Serializes read-head → append within this process; the lock file covers other processes.
    private readonly SemaphoreSlim _appendLock = new(1, 1);

    public FileAccountingAuditChainAnchor(string anchorPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(anchorPath);
        AnchorPath = anchorPath;
    }

    /// <summary>Full path of the head journal.</summary>
    public string AnchorPath { get; }

    /// <summary>The conventional journal path for a snapshot file.</summary>
    public static string AnchorPathFor(string snapshotPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        return snapshotPath + ".audit-head.log";
    }

    /// <summary>
    /// Reads the current head, verifying the journal's own hash chain first so a forged or edited
    /// line is rejected rather than trusted. Returns null when no append has ever been anchored.
    /// </summary>
    public async Task<AccountingAuditChainAnchorRecord?> ReadHeadAsync(CancellationToken ct = default)
    {
        var records = await ReadAllAsync(ct).ConfigureAwait(false);
        return records.Count == 0 ? null : records[^1];
    }

    /// <summary>
    /// Records the intent to append <paramref name="sequence"/>. Refuses a sequence that does not
    /// advance the journal, so a replayed or rewound append cannot quietly reuse a position.
    /// </summary>
    public Task<AccountingAuditChainAnchorRecord> DeclareAsync(
        long sequence,
        string entryHash,
        CancellationToken ct = default)
        => AppendAsync(sequence, entryHash, AccountingAuditChainAnchorPhase.Pending, ct);

    /// <summary>Confirms that the snapshot carrying <paramref name="sequence"/> was written.</summary>
    public Task<AccountingAuditChainAnchorRecord> CommitAsync(
        long sequence,
        string entryHash,
        CancellationToken ct = default)
        => AppendAsync(sequence, entryHash, AccountingAuditChainAnchorPhase.Committed, ct);

    private async Task<AccountingAuditChainAnchorRecord> AppendAsync(
        long sequence,
        string entryHash,
        AccountingAuditChainAnchorPhase phase,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryHash);

        var directory = Path.GetDirectoryName(AnchorPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await _appendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var crossProcessLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);

            var records = await ReadAllUnlockedAsync(ct).ConfigureAwait(false);
            var head = records.Count == 0 ? null : records[^1];
            EnsureAdvances(head, sequence, phase);

            var recordedAtUtc = DateTimeOffset.UtcNow;
            var anchorHash = ComputeAnchorHash(sequence, entryHash, phase, recordedAtUtc, head?.AnchorHash);
            var record = new AccountingAuditChainAnchorRecord(
                CurrentSchemaVersion,
                sequence,
                entryHash,
                phase,
                recordedAtUtc,
                head?.AnchorHash,
                anchorHash);

            // Copy-on-write append (temp → fsync → rename → dir fsync) so a crash mid-write cannot
            // leave a torn line that later reads as a broken journal.
            await AtomicFileWriter
                .AppendLinesAsync(AnchorPath, [JsonSerializer.Serialize(record, JsonOptions)], ct)
                .ConfigureAwait(false);

            return record;
        }
        finally
        {
            _appendLock.Release();
        }
    }

    private static void EnsureAdvances(
        AccountingAuditChainAnchorRecord? head,
        long sequence,
        AccountingAuditChainAnchorPhase phase)
    {
        if (head is null)
        {
            return;
        }

        // A commit confirms the pending line at the same sequence; anything else must move forward.
        var confirmsPendingHead = phase == AccountingAuditChainAnchorPhase.Committed
            && head.Phase == AccountingAuditChainAnchorPhase.Pending
            && head.Sequence == sequence;

        if (confirmsPendingHead || sequence > head.Sequence)
        {
            return;
        }

        throw new AccountingAuditChainAnchorException(
            $"Accounting audit head cannot move from sequence {head.Sequence.ToString(CultureInfo.InvariantCulture)} "
            + $"({head.Phase}) to {sequence.ToString(CultureInfo.InvariantCulture)} ({phase}).");
    }

    private async Task<IReadOnlyList<AccountingAuditChainAnchorRecord>> ReadAllAsync(CancellationToken ct)
    {
        await _appendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var crossProcessLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            return await ReadAllUnlockedAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _appendLock.Release();
        }
    }

    /// <summary>
    /// Reads and verifies every journal line. The journal grows by one line per accounting mutation,
    /// so a full verification is cheap; a partial read would let an edited earlier line pass.
    /// </summary>
    private async Task<IReadOnlyList<AccountingAuditChainAnchorRecord>> ReadAllUnlockedAsync(CancellationToken ct)
    {
        if (!File.Exists(AnchorPath))
        {
            return [];
        }

        var records = new List<AccountingAuditChainAnchorRecord>();
        string? previousAnchorHash = null;
        var lineNumber = 0;

        await using var stream = new FileStream(
            AnchorPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            AccountingAuditChainAnchorRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<AccountingAuditChainAnchorRecord>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new AccountingAuditChainAnchorException(
                    $"Accounting audit head journal line {lineNumber.ToString(CultureInfo.InvariantCulture)} is unreadable: {ex.Message}");
            }

            if (record is null)
            {
                throw new AccountingAuditChainAnchorException(
                    $"Accounting audit head journal line {lineNumber.ToString(CultureInfo.InvariantCulture)} is empty.");
            }

            if (record.SchemaVersion != CurrentSchemaVersion)
            {
                throw new AccountingAuditChainAnchorException(
                    $"Accounting audit head journal line {lineNumber.ToString(CultureInfo.InvariantCulture)} "
                    + $"declares unsupported schema version {record.SchemaVersion.ToString(CultureInfo.InvariantCulture)}.");
            }

            if (!string.Equals(record.PreviousAnchorHash, previousAnchorHash, StringComparison.Ordinal))
            {
                throw new AccountingAuditChainAnchorException(
                    $"Accounting audit head journal line {lineNumber.ToString(CultureInfo.InvariantCulture)} "
                    + "does not follow its predecessor.");
            }

            var expected = ComputeAnchorHash(
                record.Sequence, record.EntryHash, record.Phase, record.RecordedAtUtc, record.PreviousAnchorHash);
            if (!string.Equals(expected, record.AnchorHash, StringComparison.Ordinal))
            {
                throw new AccountingAuditChainAnchorException(
                    $"Accounting audit head journal line {lineNumber.ToString(CultureInfo.InvariantCulture)} "
                    + "failed hash verification.");
            }

            records.Add(record);
            previousAnchorHash = record.AnchorHash;
        }

        return records;
    }

    private static string ComputeAnchorHash(
        long sequence,
        string entryHash,
        AccountingAuditChainAnchorPhase phase,
        DateTimeOffset recordedAtUtc,
        string? previousAnchorHash)
    {
        var material = string.Join(
            '\n',
            CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
            sequence.ToString(CultureInfo.InvariantCulture),
            entryHash,
            phase.ToString(),
            recordedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            previousAnchorHash ?? string.Empty);
        return Sha256Digest.ComputeUtf8(material);
    }

    private async Task<FileStream> AcquireCrossProcessLockAsync(CancellationToken ct)
    {
        var lockPath = AnchorPath + ".lock";
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CrossProcessLockTimeout);
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token).ConfigureAwait(false);
            }
        }
    }
}
