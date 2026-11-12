using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Serialization;
using Meridian.Ledger;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;

namespace Meridian.Execution.Events;

/// <summary>Retains financing source assessments and pending delivery, using the same
/// governed journal target as fills. This outbox is source evidence, not another ledger.</summary>
public sealed class PaperFinancingPostingService : IAsyncDisposable
{
    private readonly TradeFillLedgerPostingContext _context;
    private readonly ITradeFillLedgerPostingTarget _target;
    private readonly Func<Guid, CancellationToken, Task<LedgerAccountingPeriod?>> _readPeriod;
    private readonly Func<Guid, CancellationToken, Task<LedgerBookRecord?>> _readBook;
    private readonly string _snapshotPath;
    private readonly string _initializationMarkerPath;
    private readonly WriteAheadLog _wal;
    private readonly FileStream _writerLease;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, PaperFinancingPostingState> _charges = [];
    private long _sequence;
    private bool _initialized;
    private Task? _initialization;
    private bool _disposed;

    public TradeFillLedgerPostingContext PostingContext => _context;

    public PaperFinancingPostingService(string rootDirectory, TradeFillLedgerPostingContext context,
        ITradeFillLedgerPostingTarget target,
        Func<Guid, CancellationToken, Task<LedgerAccountingPeriod?>> readPeriod,
        Func<Guid, CancellationToken, Task<LedgerBookRecord?>> readBook)
    {
        _context = (context ?? throw new ArgumentNullException(nameof(context))).Validate();
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _readPeriod = readPeriod ?? throw new ArgumentNullException(nameof(readPeriod));
        _readBook = readBook ?? throw new ArgumentNullException(nameof(readBook));
        var root = Path.Combine(new TradeFillPostingStoreOptions(rootDirectory, context).ScopeDirectory, "financing");
        Directory.CreateDirectory(root);
        // The snapshot and WAL have one owner. A second host must fail before it can
        // retain a competing sequence or overwrite another writer's accepted assessments.
        _writerLease = new FileStream(Path.Combine(root, "writer.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        _snapshotPath = Path.Combine(root, "sources.snapshot.json");
        _initializationMarkerPath = Path.Combine(root, "initialized.json");
        _wal = new WriteAheadLog(Path.Combine(root, "wal"), new WalOptions
        {
            SyncMode = WalSyncMode.EveryWrite,
            CorruptionMode = WalCorruptionMode.Halt,
            ArchiveAfterTruncate = false
        });
    }

    /// <summary>Durably accepts and confirms a cash-paid charge. Cancellation is observed before
    /// acceptance; after acceptance failures retain the assessment for an idempotent retry.</summary>
    public async Task<bool> PostAsync(PaperFinancingCharge charge, CancellationToken ct = default)
    {
        charge.Validate();
        try
        { await _gate.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException cancellation)
        {
            throw new PaperFinancingAdmissionException(cancellation);
        }
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await (_initialization ??= InitializeCoreAsync()).ConfigureAwait(false);
            var isNew = !_charges.TryGetValue(charge.SourceEventId, out var state);
            if (!isNew && state!.Charge != charge)
                throw new InvalidDataException($"Financing source '{charge.SourceEventId}' was reused with different content.");
            if (isNew)
            {
                await ValidateNewAssessmentAsync(charge, ct).ConfigureAwait(false);
                state = new(charge, Posted: false);
                await RetainAsync(state, "FinancingAccepted").ConfigureAwait(false);
            }

            await PostCoreAsync(state!).ConfigureAwait(false);
            return isNew;
        }
        finally { _gate.Release(); }
    }

    private async Task ValidateNewAssessmentAsync(PaperFinancingCharge charge, CancellationToken ct)
    {
        try
        {
            // New assessments must fit the authoritative numeric(38,10) amount exactly.
            // Do not retain a zero-rounded/overflowing leg or diverge from session cash.
            if (charge.Amount >= 10000000000000000000000000000m
                || decimal.Round(charge.Amount, 10, MidpointRounding.AwayFromZero) != charge.Amount)
                throw new LedgerValidationException("The financing amount must fit the journal's numeric(38,10) precision exactly.");
            var write = AccountingPostingCommandValidator.NormalizeAndValidate(BuildWrite(charge),
                requirePostingCommand: true, requireExpectedVersion: true);
            var period = await _readPeriod(_context.PeriodId, ct).ConfigureAwait(false)
                ?? throw new LedgerValidationException("The financing accounting period is unavailable.");
            if (period.PeriodId != _context.PeriodId || period.LedgerBookId != _context.LedgerBookId)
                throw new LedgerValidationException("The financing accounting period does not belong to the bound ledger scope.");
            if (period.Version != _context.ExpectedPeriodVersion)
                throw new LedgerValidationException("The financing accounting period version differs from the bound posting context.");
            LedgerPeriodPostingGuard.Validate(write, period);
            var book = await _readBook(_context.LedgerBookId, ct).ConfigureAwait(false)
                ?? throw new LedgerValidationException("The financing ledger book is unavailable.");
            if (book.LedgerBookId != _context.LedgerBookId || book.AccountingBasis != write.AccountingBasis)
                throw new LedgerValidationException("The financing ledger book identity or accounting basis differs from the bound posting command.");
            if (!AuthoritativeTextMatches(book.AccountingPolicyId, write.AccountingPolicyId)
                || !AuthoritativeTextMatches(book.AccountingPolicyVersion, write.AccountingPolicyVersion))
                throw new LedgerValidationException("The financing accounting policy differs from the retained ledger book policy.");
            // Ordinary financing carries no reviewed consolidation evidence. Matching a
            // dedicated elimination book's policy does not authorize an ordinary journal.
            if (AuthoritativeTextMatches(book.AccountingPolicyId, "consolidation-v1"))
                throw new LedgerValidationException("Dedicated consolidation books do not accept ordinary financing assessments.");
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception error)
        {
            // Nothing has entered the WAL or snapshot. The session owner must preserve the
            // original refusal/cancellation without latching an unaccepted source as pending.
            throw new PaperFinancingAdmissionException(error);
        }
    }

    private static bool AuthoritativeTextMatches(string? retained, string? requested) =>
        !string.IsNullOrWhiteSpace(retained) && !string.IsNullOrWhiteSpace(requested)
        && string.Equals(retained.Trim(), requested.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Replays pending assessments before exposing a rebuilt session book.</summary>
    public async Task<IReadOnlyList<PaperFinancingCharge>> LoadAndRecoverAsync(
        string sessionId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await (_initialization ??= InitializeCoreAsync()).ConfigureAwait(false);
            foreach (var state in _charges.Values.Where(state => state.Charge.SessionId == sessionId).ToArray())
            {
                ct.ThrowIfCancellationRequested();
                await PostCoreAsync(state).ConfigureAwait(false);
            }
            return _charges.Values.Where(state => state.Charge.SessionId == sessionId)
                .Select(static state => state.Charge).OrderBy(static charge => charge.OccurredAt).ToArray();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Reads independent accepted source assessments for completeness controls.
    /// This does not post or acknowledge any journal.</summary>
    public async Task<IReadOnlyList<PaperFinancingCharge>> LoadSourcesAsync(
        string sessionId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await (_initialization ??= InitializeCoreAsync()).ConfigureAwait(false);
            return _charges.Values.Where(state => state.Charge.SessionId == sessionId)
                .Select(static state => state.Charge).OrderBy(static charge => charge.OccurredAt).ToArray();
        }
        finally { _gate.Release(); }
    }

    private async Task PostCoreAsync(PaperFinancingPostingState state)
    {
        var write = AccountingPostingCommandValidator.NormalizeAndValidate(BuildWrite(state.Charge));
        var confirmation = await _target.PostAndConfirmAsync(write, CancellationToken.None).ConfigureAwait(false);
        DurableLedgerPostingTarget.VerifyRetainedEntry(confirmation.RetainedRecord, write);
        if (!state.Posted)
            await RetainAsync(state with { Posted = true }, "FinancingPosted").ConfigureAwait(false);
    }

    internal LedgerJournalEntryWrite BuildWrite(PaperFinancingCharge charge)
    {
        var source = charge.SourceEventId;
        var journalId = PaperFinancingCharge.Identity("journal", _context.PostingScope, source.ToString("D"));
        var commandId = PaperFinancingCharge.Identity("command", _context.PostingScope, source.ToString("D"));
        var description = $"Cash-paid margin interest: {charge.SourceReference}";
        var key = $"execution:{_context.PostingScope}:{source:D}:margin-interest";
        var accountId = charge.AccountId == Services.PaperTradingPortfolio.DefaultAccountId ? null : charge.AccountId;
        var expense = accountId is null ? LedgerAccounts.MarginInterestExpense : LedgerAccounts.MarginInterestExpenseFor(accountId);
        var entry = new JournalEntry(journalId, charge.OccurredAt, description,
        [
            new(PaperFinancingCharge.Identity("debit", journalId.ToString("D")), journalId,
                charge.OccurredAt, expense, charge.Amount, 0m, description),
            new(PaperFinancingCharge.Identity("credit", journalId.ToString("D")), journalId,
                charge.OccurredAt, LedgerAccounts.CashFor(accountId), 0m, charge.Amount, description)
        ], new JournalEntryMetadata(ActivityType: "margin-interest", FinancialAccountId: accountId,
            LedgerBook: _context.LedgerBookId.ToString("D"), EffectiveDate: charge.AccrualDate,
            IdempotencyKey: key, Tags: new Dictionary<string, string>
            {
                ["source.reference"] = charge.SourceReference,
                ["source.sessionId"] = charge.SessionId,
                ["source.actorId"] = charge.ActorId,
                ["ledgerPosting.scope"] = _context.PostingScope
            }));
        return new LedgerJournalEntryWrite(entry, _context.AggregateId, _context.PeriodId,
            CommandId: commandId, CorrelationId: source, AccountingBasis: AccountingBasisKindDto.Primary,
            AccountingPolicyId: _context.AccountingPolicyId, AccountingPolicyVersion: _context.AccountingPolicyVersion,
            RuleId: "execution.margin-interest", RuleVersion: "1", SourceEventId: source,
            PostingKind: LedgerPostingKindDto.Originating,
            PostingCommand: new AccountingPostingCommandDto(commandId, _context.AggregateId, _context.PeriodId,
                charge.AccrualDate, charge.OccurredAt, key, AccountingPostingIntentDto.Originating,
                SourceEventId: source, CorrelationId: source, CausationId: commandId,
                ExpectedVersion: _context.ExpectedPeriodVersion, SourceEventType: "execution.margin-interest",
                ApprovalState: AccountingPostingApprovalStateDto.NotRequired,
                OperatorRationale: charge.Rationale,
                Evidence: [new AccountingPostingEvidenceReferenceDto(
                    $"financing:{source:D}", $"evidence://execution/financing/{source:D}",
                    AccountingPostingEvidenceKindDto.Source, "PaperFinancingPostingService", charge.OccurredAt,
                    charge.ActorId, SubjectId: charge.SessionId, Description: charge.SourceReference)],
                ActionOrigin: OperationsActionOriginDto.HumanOperator, LedgerBookId: _context.LedgerBookId)
            { Actor = charge.ActorId },
            LedgerBookId: _context.LedgerBookId);
    }

    private async Task InitializeCoreAsync()
    {
        if (_initialized)
            return;
        _charges.Clear();
        _sequence = 0;
        var hasInitializationMarker = File.Exists(_initializationMarkerPath);
        if (hasInitializationMarker)
        {
            var marker = JsonSerializer.Deserialize(
                await File.ReadAllTextAsync(_initializationMarkerPath).ConfigureAwait(false),
                ExecutionJsonContext.Default.PaperFinancingInitializationMarker)
                ?? throw new InvalidDataException("Financing initialization marker is empty.");
            if (marker.Version != 1 || marker.Scope != _context)
                throw new InvalidDataException("Financing initialization marker scope or version is invalid.");
            if (!File.Exists(_snapshotPath))
                throw new InvalidDataException("Initialized financing sources are missing their retained snapshot; recovery is required.");
        }
        if (File.Exists(_snapshotPath))
        {
            var snapshot = JsonSerializer.Deserialize(await File.ReadAllTextAsync(_snapshotPath).ConfigureAwait(false),
                ExecutionJsonContext.Default.PaperFinancingPostingSnapshot)
                ?? throw new InvalidDataException("Financing source snapshot is empty.");
            if (snapshot.Version != 1 || snapshot.Scope != _context || snapshot.Sequence < 0 || snapshot.States is null)
                throw new InvalidDataException("Financing source snapshot scope or version is invalid.");
            foreach (var state in snapshot.States)
            {
                state.Charge.Validate();
                if (!_charges.TryAdd(state.Charge.SourceEventId, state))
                    throw new InvalidDataException("Financing snapshot repeats a source identity.");
            }
            _sequence = snapshot.Sequence;
        }
        await _wal.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        await foreach (var record in _wal.GetUncommittedRecordsAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (record.Sequence <= _sequence)
                continue;
            var payload = JsonSerializer.Deserialize(record.DeserializePayload<string>()!,
                ExecutionJsonContext.Default.PaperFinancingWalPayload)
                ?? throw new InvalidDataException("Financing WAL record is empty.");
            if (payload.Scope != _context || record.RecordType is not ("FinancingAccepted" or "FinancingPosted")
                || payload.State.Posted != (record.RecordType == "FinancingPosted"))
                throw new InvalidDataException("Financing WAL scope or transition is invalid.");
            Apply(payload.State);
            _sequence = record.Sequence;
        }
        if (!hasInitializationMarker)
        {
            // Publish the first complete checkpoint before declaring this store initialized.
            // An interrupted first WAL handoff can still replay without a marker. Once any
            // history can be compacted, a lost checkpoint must never resemble a new empty store.
            await PersistSnapshotAsync().ConfigureAwait(false);
            await AtomicFileWriter.WriteAsync(_initializationMarkerPath,
                JsonSerializer.Serialize(new PaperFinancingInitializationMarker(1, _context),
                    ExecutionJsonContext.Default.PaperFinancingInitializationMarker),
                CancellationToken.None).ConfigureAwait(false);
        }
        _initialized = true;
    }

    private async Task RetainAsync(PaperFinancingPostingState state, string type)
    {
        var payload = JsonSerializer.Serialize(new PaperFinancingWalPayload(_context, state),
            ExecutionJsonContext.Default.PaperFinancingWalPayload);
        var record = await _wal.AppendAsync(payload, type, CancellationToken.None).ConfigureAwait(false);
        Apply(state);
        _sequence = record.Sequence;
        await PersistSnapshotAsync().ConfigureAwait(false);
        // Truncation follows durable snapshot publication. Accepted source assessments stay in
        // the snapshot permanently so rebuilding a portfolio never loses a paid charge.
        await _wal.CommitAsync(_sequence, CancellationToken.None).ConfigureAwait(false);
        await _wal.TruncateAsync(_sequence, CancellationToken.None).ConfigureAwait(false);
    }

    private Task PersistSnapshotAsync()
    {
        var snapshot = new PaperFinancingPostingSnapshot(1, _context, _sequence, _charges.Values.ToArray());
        return AtomicFileWriter.WriteAsync(_snapshotPath,
            JsonSerializer.Serialize(snapshot, ExecutionJsonContext.Default.PaperFinancingPostingSnapshot),
            CancellationToken.None);
    }

    private void Apply(PaperFinancingPostingState state)
    {
        state.Charge.Validate();
        if (_charges.TryGetValue(state.Charge.SourceEventId, out var retained))
        {
            if (retained.Charge != state.Charge || retained.Posted && !state.Posted)
                throw new InvalidDataException("Financing source content or posting state conflicts with retained evidence.");
        }
        else if (state.Posted)
            throw new InvalidDataException("Financing acknowledgement has no accepted source assessment.");
        _charges[state.Charge.SourceEventId] = state;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            { await _wal.DisposeAsync().ConfigureAwait(false); }
            finally { await _writerLease.DisposeAsync().ConfigureAwait(false); }
        }
        finally { _gate.Release(); }
    }
}

internal sealed record PaperFinancingPostingState(PaperFinancingCharge Charge, bool Posted);
internal sealed record PaperFinancingInitializationMarker(int Version, TradeFillLedgerPostingContext Scope);
internal sealed record PaperFinancingWalPayload(TradeFillLedgerPostingContext Scope, PaperFinancingPostingState State);
internal sealed record PaperFinancingPostingSnapshot(int Version, TradeFillLedgerPostingContext Scope,
    long Sequence, IReadOnlyList<PaperFinancingPostingState> States);

/// <summary>This call failed before accepting a new durable source. An earlier acceptance can
/// still exist on a retry. The original error preserves validation, cancellation and availability.</summary>
public sealed class PaperFinancingAdmissionException : Exception
{
    internal PaperFinancingAdmissionException(Exception error)
        : base("The financing assessment was not accepted.", error) { }
}
