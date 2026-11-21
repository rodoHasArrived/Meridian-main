using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Ledger;

/// <summary>
/// File-backed journal store for execution trade-fill postings when no Postgres ledger is
/// configured. Journal entries live in a hash-chained <see cref="FileLedgerJournalLog"/>; the
/// posting envelope that the governed posting path compares on replay (aggregate, period, command,
/// policy, rule and source identities) lives in a separate fsynced write-ahead log.
/// </summary>
/// <remarks>
/// <para>
/// Only the journal append, aggregate/period reads and posting-identity collision lookup used by
/// <see cref="DurableLedgerPostingTarget"/> are supported. Ledger books, accounting periods, tax
/// lots and scoped queries remain Postgres-only and throw <see cref="NotSupportedException"/>.
/// The store is therefore composed privately behind the trade-fill posting target and must not be
/// registered as the host's <see cref="ILedgerJournalStore"/>.
/// </para>
/// <para>
/// Crash safety: the envelope is made durable before the journal entry. A crash between the two
/// leaves an envelope without an entry, which is ignored on load and superseded when the fill is
/// replayed. An entry is never durable without its envelope, and every entry is bound to its
/// envelope by the SHA-256 of the entry's exact journal JSON.
/// </para>
/// <para>Single writer per directory.</para>
/// </remarks>
public sealed class FileTradeFillLedgerJournalStore
    : ILedgerJournalStore, ILedgerPostingIdentityCollisionLookup, IAsyncDisposable
{
    internal const string EnvelopeRecordType = "TradeFillJournalEnvelopeV1";
    private const string EnvelopeDirectoryName = "envelopes";
    private const string JournalDirectoryName = "journal";

    private readonly WriteAheadLog _envelopes;
    private readonly FileLedgerJournalLog _journal;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<LedgerJournalEntryRecord> _records = [];
    private readonly Dictionary<Guid, int> _recordIndexByJournalId = [];
    private bool _loaded;
    private bool _envelopesInitialized;
    private int _disposed;

    /// <param name="directory">Directory that holds this scope's envelope WAL and journal.</param>
    /// <param name="scopeId">Posting scope; part of the journal's genesis hash.</param>
    /// <param name="policy">
    /// Posting rules every journal must satisfy, or <c>null</c> for the unbound ledger the legacy
    /// journal-only trade-fill protocol posts (balanced journals with unique ids, no per-line
    /// currency detail) — the same journals the Postgres store accepts for that protocol. A journal
    /// remembers the policy it was created with and refuses to reopen under another.
    /// </param>
    public FileTradeFillLedgerJournalStore(string directory, string scopeId, LedgerPolicy? policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        RootDirectory = Path.GetFullPath(directory);
        ScopeId = scopeId;
        _envelopes = new WriteAheadLog(Path.Combine(RootDirectory, EnvelopeDirectoryName), new WalOptions
        {
            SyncMode = WalSyncMode.EveryWrite,
            CorruptionMode = WalCorruptionMode.Halt,
            ArchiveAfterTruncate = false,
            MaxWalFileSizeBytes = 64 * 1024 * 1024
        });
        _journal = new FileLedgerJournalLog(Path.Combine(RootDirectory, JournalDirectoryName), scopeId, policy);
    }

    /// <summary>Directory that holds this scope's envelope WAL and journal.</summary>
    public string RootDirectory { get; }

    /// <summary>Posting scope the journal belongs to.</summary>
    public string ScopeId { get; }

    /// <summary>
    /// Default directory for <paramref name="scopeId"/> under <paramref name="root"/>. Scopes are
    /// hashed so any scope id is a safe directory name and two scopes can never share a journal.
    /// </summary>
    public static string DirectoryFor(string root, string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        return Path.Combine(Path.GetFullPath(root), Sha256Digest.ComputeUtf8(scopeId));
    }

    /// <summary>
    /// Loads and verifies the journal chain and its envelope bindings. Call before the host starts
    /// so a damaged ledger stops startup instead of surfacing on the first fill.
    /// </summary>
    /// <exception cref="InvalidDataException">The journal chain or an envelope binding fails verification.</exception>
    public async Task<LedgerJournalChainVerification> VerifyAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var verification = await _journal.VerifyAsync(ct).ConfigureAwait(false);
            if (!verification.IsIntact)
                return verification;

            await EnsureLoadedLockedAsync(ct).ConfigureAwait(false);
            return verification;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns a fresh ledger rebuilt from every retained journal, in posting order.</summary>
    public async Task<global::Meridian.Ledger.Ledger> LoadLedgerAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedLockedAsync(ct).ConfigureAwait(false);
            return await _journal.LoadLedgerAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Every retained journal record, in posting order.</summary>
    public async Task<IReadOnlyList<LedgerJournalEntryRecord>> GetAllAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedLockedAsync(ct).ConfigureAwait(false);
            return _records.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Entry);
        if (entry.AdjustmentApproval is not null)
        {
            throw new NotSupportedException(
                "The file-backed trade-fill journal does not retain adjustment approvals; approved adjustments require the Postgres ledger.");
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedLockedAsync(ct).ConfigureAwait(false);
            var journalId = entry.Entry.JournalEntryId;
            if (_recordIndexByJournalId.ContainsKey(journalId))
            {
                throw new InvalidOperationException(
                    $"Journal entry '{journalId:D}' is already retained in trade-fill journal '{ScopeId}'.");
            }

            var createdAt = DateTimeOffset.UtcNow;
            var envelope = TradeFillJournalEnvelope.FromWrite(entry, EntryHash(entry.Entry), createdAt);
            try
            {
                // The envelope must be durable first: an entry without its envelope cannot be
                // compared on replay, while an envelope without its entry is harmless.
                await _envelopes.AppendAsync(
                        JsonSerializer.Serialize(envelope, FileTradeFillLedgerJsonContext.Default.TradeFillJournalEnvelope),
                        EnvelopeRecordType,
                        ct)
                    .ConfigureAwait(false);
                await _journal.AppendAsync(entry.Entry, ct).ConfigureAwait(false);
            }
            catch
            {
                // Whether either record reached disk is unknown. Rebuild from disk on the next call.
                _loaded = false;
                throw;
            }

            AddRecord(envelope.ToRecord(entry.Entry, _records.Count + 1));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerJournalEntryRecord>> FindPostingIdentityCollisionsAsync(
        LedgerPostingIdentity identity,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedLockedAsync(ct).ConfigureAwait(false);
            return _records
                .Where(record => LedgerPostingIdentityCollisionLookupExtensions.IsCollision(record, identity))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedLockedAsync(ct).ConfigureAwait(false);
            return _records.Where(record => record.AggregateId == aggregateId).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedLockedAsync(ct).ConfigureAwait(false);
            return _records.Where(record => record.PeriodId == periodId).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
        => throw PostgresOnly("accounting periods");

    /// <inheritdoc />
    public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
        Guid? ledgerBookId = null,
        string? status = null,
        string? fundProfileId = null,
        Guid? fundStructureNodeId = null,
        CancellationToken ct = default)
        => throw PostgresOnly("accounting periods");

    /// <inheritdoc />
    public Task<LedgerAccountingPeriod> SavePeriodAsync(
        LedgerAccountingPeriod period,
        long expectedVersion,
        PeriodCloseEventRecord? closeEvent = null,
        CancellationToken ct = default)
        => throw PostgresOnly("accounting periods");

    /// <inheritdoc />
    public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
        => throw PostgresOnly("ledger books");

    /// <inheritdoc />
    public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
        string? fundProfileId = null,
        Guid? fundStructureNodeId = null,
        FundStructureNodeKindDto? fundStructureNodeKind = null,
        CancellationToken ct = default)
        => throw PostgresOnly("ledger books");

    /// <inheritdoc />
    public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default)
        => throw PostgresOnly("ledger books");

    private async Task EnsureLoadedLockedAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_loaded)
            return;

        _records.Clear();
        _recordIndexByJournalId.Clear();

        if (!_envelopesInitialized)
        {
            await _envelopes.InitializeAsync(ct).ConfigureAwait(false);
            _envelopesInitialized = true;
        }

        // The last envelope for a journal id wins: an earlier one can only be an envelope whose
        // entry never reached the journal, superseded by the replay that retained it. Its WAL
        // sequence also records posting order: each entry's final envelope is made durable
        // immediately before the entry, under the store gate.
        var envelopes = new Dictionary<Guid, (TradeFillJournalEnvelope Envelope, long WalSequence)>();
        await foreach (var walRecord in _envelopes.GetUncommittedRecordsAsync(ct).ConfigureAwait(false))
        {
            if (walRecord.RecordType != EnvelopeRecordType)
            {
                throw new InvalidDataException(
                    $"Trade-fill journal '{ScopeId}' envelope log contains an unknown record type '{walRecord.RecordType}'.");
            }

            TradeFillJournalEnvelope envelope;
            try
            {
                var json = walRecord.DeserializePayload<string>()
                    ?? throw new InvalidDataException("The envelope payload is empty.");
                envelope = JsonSerializer.Deserialize(json, FileTradeFillLedgerJsonContext.Default.TradeFillJournalEnvelope)
                    ?? throw new InvalidDataException("The envelope is empty.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"Trade-fill journal '{ScopeId}' envelope {walRecord.Sequence} is unreadable: {ex.Message}", ex);
            }

            envelopes[envelope.JournalEntryId] = (envelope, walRecord.Sequence);
        }

        // The rebuilt ledger orders its journal by entry timestamp, which a late fill can precede.
        // Retained records keep posting order instead, so their sequences survive a restart.
        var ledger = await _journal.LoadLedgerAsync(ct).ConfigureAwait(false);
        var retained = new List<(long WalSequence, TradeFillJournalEnvelope Envelope, JournalEntry Entry)>(ledger.Journal.Count);
        foreach (var journalEntry in ledger.Journal)
        {
            if (!envelopes.TryGetValue(journalEntry.JournalEntryId, out var bound))
            {
                throw new InvalidDataException(
                    $"Trade-fill journal '{ScopeId}' entry '{journalEntry.JournalEntryId:D}' has no posting envelope.");
            }

            if (!Sha256Digest.FixedEquals(bound.Envelope.EntryHash, EntryHash(journalEntry)))
            {
                throw new InvalidDataException(
                    $"Trade-fill journal '{ScopeId}' entry '{journalEntry.JournalEntryId:D}' does not match its posting envelope.");
            }

            retained.Add((bound.WalSequence, bound.Envelope, journalEntry));
        }

        var sequence = 0L;
        foreach (var (_, envelope, journalEntry) in retained.OrderBy(static item => item.WalSequence))
            AddRecord(envelope.ToRecord(journalEntry, ++sequence));

        _loaded = true;
    }

    private void AddRecord(LedgerJournalEntryRecord record)
    {
        _recordIndexByJournalId.Add(record.Entry.JournalEntryId, _records.Count);
        _records.Add(record);
    }

    private static string EntryHash(JournalEntry entry)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(entry, LedgerJournalLogJsonContext.Default.JournalEntry));

    private static NotSupportedException PostgresOnly(string feature)
        => new($"The file-backed trade-fill journal does not manage {feature}; configure the Postgres ledger for them.");

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _envelopes.DisposeAsync().ConfigureAwait(false);
            await _journal.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}

/// <summary>
/// Everything a <see cref="LedgerJournalEntryRecord"/> carries besides the journal entry itself,
/// bound to that entry by the SHA-256 of its exact journal-log JSON.
/// </summary>
internal sealed record TradeFillJournalEnvelope(
    Guid JournalEntryId,
    string EntryHash,
    Guid AggregateId,
    Guid PeriodId,
    Guid? CommandId,
    Guid? CorrelationId,
    DateTimeOffset CreatedAt,
    AccountingBasisKindDto AccountingBasis,
    string AccountingPolicyId,
    string AccountingPolicyVersion,
    string? RuleId,
    string? RuleVersion,
    Guid? SourceEventId,
    Guid? SourceJournalEntryId,
    LedgerPostingKindDto PostingKind)
{
    public static TradeFillJournalEnvelope FromWrite(LedgerJournalEntryWrite write, string entryHash, DateTimeOffset createdAt)
        => new(
            write.Entry.JournalEntryId,
            entryHash,
            write.AggregateId,
            write.PeriodId,
            write.CommandId,
            write.CorrelationId,
            createdAt,
            write.AccountingBasis,
            write.AccountingPolicyId,
            write.AccountingPolicyVersion,
            write.RuleId,
            write.RuleVersion,
            write.SourceEventId,
            write.SourceJournalEntryId,
            write.PostingKind);

    public LedgerJournalEntryRecord ToRecord(JournalEntry entry, long globalSequence)
        => new(
            entry,
            AggregateId,
            PeriodId,
            CommandId,
            CorrelationId,
            globalSequence,
            CreatedAt,
            AccountingBasis,
            AccountingPolicyId,
            AccountingPolicyVersion,
            RuleId,
            RuleVersion,
            SourceEventId,
            SourceJournalEntryId,
            PostingKind,
            AdjustmentApproval: null);
}

/// <summary>Source-generated JSON metadata for the file-backed trade-fill journal envelopes.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(TradeFillJournalEnvelope))]
internal sealed partial class FileTradeFillLedgerJsonContext : JsonSerializerContext;
