using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Ledger;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Ledger;

/// <summary>Outcome of re-verifying a journal log's hash chain from its genesis record.</summary>
/// <param name="ScopeId">The run, session or book the journal belongs to.</param>
/// <param name="IsIntact"><c>true</c> when every record chains to its predecessor and every entry re-posts cleanly.</param>
/// <param name="EntryCount">Journal entries verified before the first break, or all of them when intact.</param>
/// <param name="HeadHash">Hash of the last verified record (the genesis hash for an empty journal).</param>
/// <param name="FunctionalCurrency">The bound functional currency, or <c>null</c> for an unbound legacy journal.</param>
/// <param name="FirstBreakSequence">Sequence of the first record that failed verification, if any.</param>
/// <param name="Reason">Why verification failed, if it did.</param>
public sealed record LedgerJournalChainVerification(
    string ScopeId,
    bool IsIntact,
    long EntryCount,
    string HeadHash,
    string? FunctionalCurrency,
    long? FirstBreakSequence = null,
    string? Reason = null);

/// <summary>An append-only, hash-chained journal of posted <see cref="JournalEntry"/> records.</summary>
public interface ILedgerJournalLog : IAsyncDisposable
{
    /// <summary>The run, session or book the journal belongs to.</summary>
    string ScopeId { get; }

    /// <summary>
    /// Durably appends <paramref name="entry"/>. Re-appending an entry id with identical content is a
    /// no-op; the same id with different content is rejected, because posted history never changes.
    /// </summary>
    Task AppendAsync(JournalEntry entry, CancellationToken ct = default);

    /// <summary>Verifies the chain and returns every entry re-posted into a ledger bound to the log's policy.</summary>
    /// <exception cref="InvalidDataException">The chain or any entry fails verification.</exception>
    Task<global::Meridian.Ledger.Ledger> LoadLedgerAsync(CancellationToken ct = default);

    /// <summary>Re-verifies the chain from its genesis record without throwing on a break.</summary>
    Task<LedgerJournalChainVerification> VerifyAsync(CancellationToken ct = default);
}

/// <summary>
/// File-backed <see cref="ILedgerJournalLog"/>. Records live in a <see cref="WriteAheadLog"/> that is
/// fsynced on every write and halts on a corrupt record. Each record holds the journal entry's exact
/// JSON and a SHA-256 hash chained to the previous record, so editing, reordering or dropping a record
/// breaks the chain. A small <c>chain-head.json</c>, replaced atomically after each append, anchors the
/// latest sequence and hash, so cutting records off the end of the log is also detected.
/// </summary>
/// <remarks>
/// Single writer per directory. The chain makes tampering evident; it does not stop someone who can
/// rewrite both the log and its head. Anchoring the head elsewhere is a separate control.
/// </remarks>
public sealed class FileLedgerJournalLog : ILedgerJournalLog
{
    internal const string Schema = "meridian.ledger-journal.v1";
    private const string HeaderRecordType = "LedgerJournalHeaderV1";
    private const string EntryRecordType = "LedgerJournalEntryV1";
    private const string ChainHeadFileName = "chain-head.json";

    private readonly string _directory;
    private LedgerPolicy? _policy;
    private readonly IChartOfAccounts? _adoptHeaderPolicyChart;
    private readonly WriteAheadLog _wal;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, string> _entryHashes = [];
    private global::Meridian.Ledger.Ledger? _ledger;
    private string _headHash = string.Empty;
    private long _sequence;
    private bool _initialized;

    /// <param name="directory">Directory that holds this journal's WAL segments and chain head.</param>
    /// <param name="scopeId">The run, session or book the journal belongs to; part of the genesis hash.</param>
    /// <param name="policy">
    /// Posting rules every entry must satisfy, or <c>null</c> for a journal of a transitional unbound
    /// ledger. A journal remembers the policy it was created with and refuses to reopen under another.
    /// </param>
    public FileLedgerJournalLog(string directory, string scopeId, LedgerPolicy? policy)
        : this(directory, scopeId, policy, adoptHeaderPolicyChart: null)
    {
    }

    private FileLedgerJournalLog(string directory, string scopeId, LedgerPolicy? policy, IChartOfAccounts? adoptHeaderPolicyChart)
    {
        _adoptHeaderPolicyChart = adoptHeaderPolicyChart;
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        _directory = Path.GetFullPath(directory);
        ScopeId = scopeId;
        _policy = policy;
        _wal = new WriteAheadLog(_directory, new WalOptions
        {
            SyncMode = WalSyncMode.EveryWrite,
            CorruptionMode = WalCorruptionMode.Halt,
            ArchiveAfterTruncate = false,
            MaxWalFileSizeBytes = 64 * 1024 * 1024
        });
    }

    /// <inheritdoc />
    public string ScopeId { get; }

    private bool IsReadOnly => _adoptHeaderPolicyChart is not null;

    /// <summary>The hash every journal for <paramref name="scopeId"/> chains its first entry to.</summary>
    public static string GenesisHash(string scopeId) => Sha256Digest.ComputeUtf8(Schema + "|" + scopeId);

    /// <inheritdoc />
    public async Task AppendAsync(JournalEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (IsReadOnly)
            throw new InvalidOperationException("A journal opened for verification is read-only.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await InitializeLockedAsync(ct).ConfigureAwait(false);
            var entryJson = SerializeEntry(entry);
            var contentHash = Sha256Digest.ComputeUtf8(entryJson);
            if (_entryHashes.TryGetValue(entry.JournalEntryId, out var retained))
            {
                if (!Sha256Digest.FixedEquals(retained, contentHash))
                {
                    throw new InvalidDataException(
                        $"Journal entry '{entry.JournalEntryId}' is already in ledger journal '{ScopeId}' with different content; posted history cannot be changed.");
                }

                return;
            }

            // Validate against the policy-bound ledger before anything reaches disk.
            _ledger!.Post(entry);

            var sequence = _sequence + 1;
            var record = new LedgerJournalChainRecord(
                sequence, _headHash, ChainHash(_headHash, sequence, entryJson), entryJson);
            try
            {
                await _wal.AppendAsync(
                        JsonSerializer.Serialize(record, LedgerJournalLogJsonContext.Default.LedgerJournalChainRecord),
                        EntryRecordType,
                        ct)
                    .ConfigureAwait(false);

                _sequence = sequence;
                _headHash = record.EntryHash;
                _entryHashes.Add(entry.JournalEntryId, contentHash);
                await WriteChainHeadAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // The in-memory ledger already holds the entry; whether the record reached disk is
                // unknown. Rebuild state from disk on the next call instead of trusting either side.
                _initialized = false;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<global::Meridian.Ledger.Ledger> LoadLedgerAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await InitializeLockedAsync(ct).ConfigureAwait(false);
            var copy = CreateLedger(_policy);
            foreach (var entry in _ledger!.Journal)
                copy.Post(entry);
            return copy;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<LedgerJournalChainVerification> VerifyAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await InitializeLockedAsync(ct).ConfigureAwait(false);
            return new LedgerJournalChainVerification(
                ScopeId, true, _sequence, _headHash, _policy?.FunctionalCurrency);
        }
        catch (InvalidDataException ex) when (ex.Data["sequence"] is long sequence)
        {
            return new LedgerJournalChainVerification(
                ScopeId, false, Math.Max(0, sequence - 1), _headHash, _policy?.FunctionalCurrency, sequence, ex.Message);
        }
        catch (InvalidDataException ex)
        {
            return new LedgerJournalChainVerification(
                ScopeId, false, 0, _headHash, _policy?.FunctionalCurrency, null, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Verifies a journal directory without opening it for writes. Returns <c>null</c> when the
    /// directory holds no journal.
    /// </summary>
    /// <remarks>
    /// The journal is verified under the functional currency recorded in its own header, with
    /// <paramref name="chart"/> as the account registry, so a reader need not know how it was created.
    /// </remarks>
    public static async Task<LedgerJournalChainVerification?> VerifyDirectoryAsync(
        string directory, string scopeId, IChartOfAccounts chart, CancellationToken ct = default)
    {
        await using var log = OpenReadOnly(directory, scopeId, chart);
        return log is null ? null : await log.VerifyAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens an existing journal for verification and reads only, alongside its writer. Returns
    /// <c>null</c> when the directory holds no journal. The journal is checked under the functional
    /// currency in its own header, with <paramref name="chart"/> as the account registry.
    /// </summary>
    public static FileLedgerJournalLog? OpenReadOnly(string directory, string scopeId, IChartOfAccounts chart)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(chart);
        if (!Directory.Exists(directory) || !Directory.EnumerateFiles(directory, "*.wal").Any())
            return null;

        return new FileLedgerJournalLog(directory, scopeId, policy: null, adoptHeaderPolicyChart: chart);
    }

    private async Task InitializeLockedAsync(CancellationToken ct)
    {
        if (_initialized)
            return;

        // Reset partial state from an earlier failed attempt so a retry re-verifies from scratch.
        _entryHashes.Clear();
        _sequence = 0;
        _headHash = GenesisHash(ScopeId);
        global::Meridian.Ledger.Ledger? ledger = null;

        // A read-only verifier never initializes the WAL: initialization opens a new segment for
        // writing, which would race the journal's single writer.
        if (!IsReadOnly)
        {
            try
            {
                await _wal.InitializeAsync(ct).ConfigureAwait(false);
            }
            catch (InvalidDataException ex)
            {
                throw Break(1, $"Ledger journal '{ScopeId}' has a corrupt write-ahead record: {ex.Message}", ex);
            }
        }

        var sawHeader = false;
        await foreach (var walRecord in ReadRecordsAsync(ct).ConfigureAwait(false))
        {
            if (!sawHeader)
            {
                if (walRecord.RecordType != HeaderRecordType)
                    throw Break(1, $"Ledger journal '{ScopeId}' does not start with a header record.");
                VerifyHeader(DeserializePayload(walRecord, LedgerJournalLogJsonContext.Default.LedgerJournalHeader));
                ledger = CreateLedger(_policy);
                sawHeader = true;
                continue;
            }

            if (walRecord.RecordType != EntryRecordType)
                throw Break(_sequence + 1, $"Ledger journal '{ScopeId}' contains an unknown record type '{walRecord.RecordType}'.");

            var record = DeserializePayload(walRecord, LedgerJournalLogJsonContext.Default.LedgerJournalChainRecord);
            var expectedSequence = _sequence + 1;
            if (record.Sequence != expectedSequence)
                throw Break(expectedSequence, $"Ledger journal '{ScopeId}' expected record {expectedSequence} but found {record.Sequence}; a record was removed or reordered.");
            if (!Sha256Digest.FixedEquals(record.PreviousHash, _headHash))
                throw Break(expectedSequence, $"Ledger journal '{ScopeId}' record {expectedSequence} does not chain to its predecessor.");
            if (!Sha256Digest.FixedEquals(record.EntryHash, ChainHash(_headHash, expectedSequence, record.EntryJson)))
                throw Break(expectedSequence, $"Ledger journal '{ScopeId}' record {expectedSequence} content does not match its hash; the entry was altered.");

            JournalEntry entry;
            try
            {
                entry = JsonSerializer.Deserialize(record.EntryJson, LedgerJournalLogJsonContext.Default.JournalEntry)
                    ?? throw new InvalidDataException("The journal entry is empty.");
                ledger!.Post(entry);
            }
            catch (Exception ex) when (ex is JsonException or LedgerValidationException or InvalidDataException)
            {
                throw Break(expectedSequence, $"Ledger journal '{ScopeId}' record {expectedSequence} is not a valid journal entry: {ex.Message}", ex);
            }

            _entryHashes.Add(entry.JournalEntryId, Sha256Digest.ComputeUtf8(record.EntryJson));
            _sequence = expectedSequence;
            _headHash = record.EntryHash;
        }

        if (!sawHeader)
        {
            if (IsReadOnly)
                throw Break(1, $"Ledger journal '{ScopeId}' has no header record.");
            ledger = CreateLedger(_policy);
            await _wal.AppendAsync(
                    JsonSerializer.Serialize(
                        new LedgerJournalHeader(Schema, ScopeId, _policy?.FunctionalCurrency, GenesisHash(ScopeId)),
                        LedgerJournalLogJsonContext.Default.LedgerJournalHeader),
                    HeaderRecordType,
                    ct)
                .ConfigureAwait(false);
        }

        await VerifyChainHeadAsync(ct).ConfigureAwait(false);
        _ledger = ledger;
        _initialized = true;
    }

    private void VerifyHeader(LedgerJournalHeader header)
    {
        if (header.Schema != Schema || header.ScopeId != ScopeId ||
            !Sha256Digest.FixedEquals(header.GenesisHash, GenesisHash(ScopeId)))
        {
            throw Break(1, $"Ledger journal header does not belong to scope '{ScopeId}'.");
        }

        if (_adoptHeaderPolicyChart is not null)
        {
            _policy = header.FunctionalCurrency is null
                ? null
                : new LedgerPolicy(_adoptHeaderPolicyChart, header.FunctionalCurrency);
            return;
        }

        if (!string.Equals(header.FunctionalCurrency, _policy?.FunctionalCurrency, StringComparison.Ordinal))
        {
            throw Break(1,
                $"Ledger journal '{ScopeId}' was created with functional currency '{header.FunctionalCurrency ?? "(unbound)"}' " +
                $"but is being opened with '{_policy?.FunctionalCurrency ?? "(unbound)"}'.");
        }
    }

    private async Task VerifyChainHeadAsync(CancellationToken ct)
    {
        var path = Path.Combine(_directory, ChainHeadFileName);
        if (!File.Exists(path))
        {
            // A head is written after every append, so only a crash right after the first record
            // can leave a journal with entries and no head. More than that means the head was lost.
            if (_sequence > 1)
                throw Break(_sequence, $"Ledger journal '{ScopeId}' has {_sequence} entries but its chain head is missing.");
            return;
        }

        LedgerJournalChainHead head;
        try
        {
            head = JsonSerializer.Deserialize(
                    await File.ReadAllTextAsync(path, ct).ConfigureAwait(false),
                    LedgerJournalLogJsonContext.Default.LedgerJournalChainHead)
                ?? throw new InvalidDataException("The chain head is empty.");
        }
        catch (JsonException ex)
        {
            throw Break(_sequence, $"Ledger journal '{ScopeId}' chain head is unreadable: {ex.Message}", ex);
        }

        if (head.Schema != Schema || head.ScopeId != ScopeId)
            throw Break(_sequence, $"Ledger journal chain head does not belong to scope '{ScopeId}'.");

        // The head is replaced after the record is durable, so a crash can leave the log one record
        // ahead of it. The log can never be behind it: that means records were cut off the end.
        if (head.Sequence > _sequence)
            throw Break(_sequence + 1, $"Ledger journal '{ScopeId}' ends at record {_sequence} but its chain head records {head.Sequence}; records were removed.");
        if (head.Sequence == _sequence && !Sha256Digest.FixedEquals(head.HeadHash, _headHash))
            throw Break(_sequence, $"Ledger journal '{ScopeId}' chain head hash does not match record {_sequence}.");
        if (head.Sequence < _sequence - 1)
            throw Break(head.Sequence + 1, $"Ledger journal '{ScopeId}' chain head is {_sequence - head.Sequence} records behind the log.");

        if (head.Sequence != _sequence && !IsReadOnly)
            await WriteChainHeadAsync(ct).ConfigureAwait(false);
    }

    private Task WriteChainHeadAsync(CancellationToken ct)
        => AtomicFileWriter.WriteAsync(
            Path.Combine(_directory, ChainHeadFileName),
            JsonSerializer.Serialize(
                new LedgerJournalChainHead(Schema, ScopeId, _sequence, _headHash),
                LedgerJournalLogJsonContext.Default.LedgerJournalChainHead),
            ct);

    private async IAsyncEnumerable<WalRecord> ReadRecordsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var enumerator = _wal.GetUncommittedRecordsAsync(ct).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (InvalidDataException ex)
                {
                    throw Break(_sequence + 1, $"Ledger journal '{ScopeId}' has a corrupt write-ahead record: {ex.Message}", ex);
                }

                if (!moved)
                    yield break;
                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private T DeserializePayload<T>(WalRecord walRecord, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : class
    {
        try
        {
            var json = walRecord.DeserializePayload<string>()
                ?? throw new InvalidDataException("The record payload is empty.");
            return JsonSerializer.Deserialize(json, typeInfo)
                ?? throw new InvalidDataException("The record is empty.");
        }
        catch (JsonException ex)
        {
            throw Break(_sequence + 1, $"Ledger journal '{ScopeId}' record {_sequence + 1} is unreadable: {ex.Message}", ex);
        }
    }

    private static string SerializeEntry(JournalEntry entry)
        => JsonSerializer.Serialize(entry, LedgerJournalLogJsonContext.Default.JournalEntry);

    // The sequence is part of the hash, so a record cannot be replayed at another position.
    private static string ChainHash(string previousHash, long sequence, string entryJson)
        => Sha256Digest.ComputeUtf8(string.Concat(
            previousHash, "\n", sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), "\n", entryJson));

    private static global::Meridian.Ledger.Ledger CreateLedger(LedgerPolicy? policy)
        => policy is null ? new global::Meridian.Ledger.Ledger() : new global::Meridian.Ledger.Ledger(policy);

    private static InvalidDataException Break(long sequence, string message, Exception? inner = null)
    {
        var exception = new InvalidDataException(message, inner);
        exception.Data["sequence"] = sequence;
        return exception;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _wal.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}

internal sealed record LedgerJournalHeader(string Schema, string ScopeId, string? FunctionalCurrency, string GenesisHash);

internal sealed record LedgerJournalChainRecord(long Sequence, string PreviousHash, string EntryHash, string EntryJson);

internal sealed record LedgerJournalChainHead(string Schema, string ScopeId, long Sequence, string HeadHash);
