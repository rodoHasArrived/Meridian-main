using System.Text.Json;
using Meridian.Execution.Serialization;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.Services;

/// <summary>
/// Configuration for the durable execution audit trail.
/// </summary>
public sealed record ExecutionAuditTrailOptions(
    string RootDirectory,
    int InMemoryRetention = 1_000,
    WalSyncMode SyncMode = WalSyncMode.EveryWrite,
    /// <summary>
    /// How far back retained entries are kept regardless of <see cref="InMemoryRetention"/>.
    /// Consumers reason about this trail in time — "a breach in the last hour holds this rule
    /// constrained" — and a count cap cannot support a claim like that: enough unrelated activity
    /// inside the window silently evicts the very entry the claim is about. Two hours gives the
    /// one-hour risk-status window room on both sides.
    /// </summary>
    TimeSpan? InMemoryRetentionWindow = null)
{
    public static ExecutionAuditTrailOptions Default { get; } = new(
        Path.Combine(AppContext.BaseDirectory, "data", "execution", "audit"));

    public string WalDirectory => Path.Combine(RootDirectory, "wal");
}

/// <summary>
/// Durable audit record for live-execution operations, approvals, and control changes.
/// </summary>
public sealed record ExecutionAuditEntry(
    string AuditId,
    string Category,
    string Action,
    string Outcome,
    DateTimeOffset OccurredAt,
    string? Actor = null,
    string? BrokerName = null,
    string? OrderId = null,
    string? RunId = null,
    string? Symbol = null,
    string? CorrelationId = null,
    string? Message = null,
    /// <summary>Explicit reason or rationale for the action (e.g. "position-limit-exceeded").</summary>
    string? Reason = null,
    /// <summary>Explicit scope for the action (e.g. "AAPL:100" or "run:xyz/symbol:AAPL").</summary>
    string? Scope = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Durable execution audit trail backed by the platform WAL.
/// The payload volume is low compared with market-data paths, so we bias toward
/// explicit durability and traceability over raw throughput.
/// </summary>
public sealed class ExecutionAuditTrailService : IAsyncDisposable
{
    private const string AuditRecordType = "ExecutionAudit";

    private readonly WriteAheadLog _wal;
    private readonly ILogger<ExecutionAuditTrailService> _logger;
    private readonly int _inMemoryRetention;
    private readonly TimeSpan _inMemoryRetentionWindow;
    private bool _windowTruncationLogged;
    private DateTimeOffset? _newestDiscardedInsideWindow;

    /// <summary>
    /// Whether every entry inside the retention window is actually retained.
    /// <para>
    /// False once a burst exceeds the hard ceiling below. A consumer that reasons over a time
    /// window — "no breach in the last hour" — cannot make that claim from an incomplete window,
    /// and logging the shortfall does not make the claim true. Reading this lets such a consumer
    /// decline to assert safety instead, which is the only fail-closed answer available.
    /// </para>
    /// <para>
    /// Incompleteness is <b>sticky until the gap itself ages out</b>. It is judged from the newest
    /// entry ever discarded from inside the window, not from whether the currently retained set
    /// fits the cap — those differ exactly when it matters, because once entries have been dropped
    /// the retained set can fit again while the dropped ones are still inside the window.
    /// </para>
    /// <para>
    /// This answers for <em>this trail's</em> window. A consumer reasoning over a shorter span
    /// should ask <see cref="RetentionWindowCompleteFor"/> instead, or it will keep failing closed
    /// after the gap has already left its own horizon.
    /// </para>
    /// </summary>
    public bool RetentionWindowComplete => RetentionWindowCompleteFor(_inMemoryRetentionWindow);

    /// <summary>
    /// Whether every entry inside the caller's <paramref name="horizon"/> is retained — the same
    /// question <see cref="RetentionWindowComplete"/> answers, asked at the span the caller actually
    /// reasons over rather than at this trail's retention window.
    /// <para>
    /// The distinction is not cosmetic, because incompleteness blocks callers. A discard is only
    /// capable of hiding something from a caller if it fell inside <em>that caller's</em> horizon,
    /// so a trail retaining two hours and a consumer claiming one must stop reporting a gap once the
    /// discarded entry is an hour old, not two. Measuring against the longer window instead keeps
    /// every non-breached rule <c>Constrained</c> — and order readiness blocked — for an extra hour
    /// in which no breach the consumer could assert about was ever missing.
    /// </para>
    /// <para>
    /// This is orthogonal to whether the horizon fits at all: a caller whose horizon exceeds
    /// <see cref="InMemoryRetentionWindow"/> loses entries to age-based trimming that never register
    /// as discards, so it must compare the two windows as well. Neither check subsumes the other.
    /// </para>
    /// </summary>
    /// <param name="horizon">The span the caller reasons over. Non-positive values ask nothing.</param>
    public bool RetentionWindowCompleteFor(TimeSpan horizon)
    {
        if (horizon <= TimeSpan.Zero)
        {
            return true;
        }

        lock (_lock)
        {
            return _newestDiscardedInsideWindow is not { } discarded
                || discarded < DateTimeOffset.UtcNow - horizon;
        }
    }

    /// <summary>Default window kept regardless of the count cap. See the option's remarks.</summary>
    public static readonly TimeSpan DefaultInMemoryRetentionWindow = TimeSpan.FromHours(2);

    /// <summary>
    /// How far ahead of now a caller-stamped timestamp is still treated as real when deciding
    /// whether discarding it left a coverage gap. Writers do not share a clock, so a little drift is
    /// ordinary; a record stamped well beyond it is not evidence any consumer would have reasoned
    /// with, and counting its loss as a gap would let a caller hold completeness false — and order
    /// readiness closed — until wall-clock time caught up with the date it chose.
    /// <para>
    /// Consumers apply their own plausibility bound when reading (<c>RiskRuleRuntimeService</c> uses
    /// a five-minute violation clock-skew allowance). This is the trail's own, deliberately not
    /// shared: a consumer tightening its reading must not silently change what this records as lost.
    /// </para>
    /// </summary>
    public static readonly TimeSpan WriterClockSkewAllowance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The window this instance actually keeps. A consumer whose own claim spans longer than this
    /// cannot establish that claim from this trail, however complete the window is — completeness
    /// is measured against <em>this</em> window, so an entry older than it is trimmed without ever
    /// registering as a gap. Consumers are expected to compare their horizon against this value.
    /// </summary>
    public TimeSpan InMemoryRetentionWindow => _inMemoryRetentionWindow;

    /// <summary>
    /// Absolute ceiling on retained entries, as a multiple of the count cap. The window must not be
    /// able to grow memory without bound under a pathological burst; when this bites, the shortfall
    /// is logged rather than silently swallowed, because a consumer's time claim has just stopped
    /// being answerable.
    /// </summary>
    private const int RetentionHardCapMultiplier = 20;
    private readonly List<ExecutionAuditEntry> _entries = [];
    private readonly Lock _lock = new();
    private readonly Lock _initializationLock = new();
    private Task? _initializationTask;

    public ExecutionAuditTrailService(
        ExecutionAuditTrailOptions? options,
        ILogger<ExecutionAuditTrailService> logger)
    {
        options ??= ExecutionAuditTrailOptions.Default;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inMemoryRetention = Math.Max(100, options.InMemoryRetention);
        _inMemoryRetentionWindow = options.InMemoryRetentionWindow is { } window && window > TimeSpan.Zero
            ? window
            : DefaultInMemoryRetentionWindow;
        _wal = new WriteAheadLog(
            options.WalDirectory,
            new WalOptions
            {
                SyncMode = options.SyncMode,
                ArchiveAfterTruncate = false,
                MaxWalFileAge = TimeSpan.FromDays(1),
                MaxWalFileSizeBytes = 5 * 1024 * 1024,
                CorruptionMode = WalCorruptionMode.Alert
            });
    }

    /// <summary>
    /// Convenience constructor that accepts a root directory string directly.
    /// Useful for tests and simple host scenarios that do not need the full options object.
    /// </summary>
    public ExecutionAuditTrailService(
        string rootDirectory,
        ILogger<ExecutionAuditTrailService> logger)
        : this(new ExecutionAuditTrailOptions(rootDirectory), logger)
    {
    }

    /// <summary>
    /// Returns the most recent audit entries, newest first.
    /// </summary>
    public async Task<IReadOnlyList<ExecutionAuditEntry>> GetRecentAsync(int take = 100, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureInitialisedAsync(ct).ConfigureAwait(false);

        lock (_lock)
        {
            return
                _entries
                    .OrderByDescending(static entry => entry.OccurredAt)
                    .Take(Math.Max(1, take))
                    .ToArray();
        }
    }

    /// <summary>
    /// The newest <paramref name="take"/> entries <em>plus</em> every retained entry at or after
    /// <paramref name="since"/>, newest first.
    /// <para>
    /// A count-bounded query and a time-bounded claim do not mix. A caller that says "a breach in
    /// the last hour holds this rule constrained" and then reads a fixed number of newest entries
    /// silently drops that breach as soon as enough unrelated activity follows it — which on an
    /// active desk happens well inside the hour, turning a live breach into a healthy rule and
    /// reopening whatever gate reads it. The count still bounds ordinary history; the window is
    /// what the caller actually reasons about, so both are honoured.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<ExecutionAuditEntry>> GetRecentOrSinceAsync(
        int take,
        DateTimeOffset since,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureInitialisedAsync(ct).ConfigureAwait(false);

        lock (_lock)
        {
            var ordered = _entries.OrderByDescending(static entry => entry.OccurredAt).ToArray();
            var count = Math.Max(1, take);
            // Ordered newest-first, so everything inside the window is a prefix — except that a
            // misdated future entry sorts ahead of it. Counting forward past the window rather
            // than assuming a prefix keeps those from displacing genuine entries.
            var windowed = ordered.Count(entry => entry.OccurredAt >= since);
            return ordered.Take(Math.Max(count, windowed)).ToArray();
        }
    }

    /// <summary>
    /// Returns all retained audit entries in chronological order.
    /// </summary>
    public async Task<IReadOnlyList<ExecutionAuditEntry>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureInitialisedAsync(ct).ConfigureAwait(false);

        lock (_lock)
        {
            return _entries.ToArray();
        }
    }

    /// <summary>
    /// Appends a new audit entry and returns the persisted record.
    /// </summary>
    public async Task<ExecutionAuditEntry> RecordAsync(
        string category,
        string action,
        string outcome,
        string? actor = null,
        string? brokerName = null,
        string? orderId = null,
        string? runId = null,
        string? symbol = null,
        string? correlationId = null,
        string? message = null,
        string? reason = null,
        string? scope = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var entry = new ExecutionAuditEntry(
            AuditId: $"audit-{Guid.NewGuid():N}",
            Category: category,
            Action: action,
            Outcome: outcome,
            OccurredAt: DateTimeOffset.UtcNow,
            Actor: actor,
            BrokerName: brokerName,
            OrderId: orderId,
            RunId: runId,
            Symbol: symbol,
            CorrelationId: correlationId,
            Message: message,
            Reason: reason,
            Scope: scope,
            Metadata: metadata);

        await RecordAsync(entry, ct).ConfigureAwait(false);
        return entry;
    }

    /// <summary>
    /// Appends a pre-built audit entry.
    /// </summary>
    public async Task RecordAsync(ExecutionAuditEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await EnsureInitialisedAsync(ct).ConfigureAwait(false);

        var json = JsonSerializer.Serialize(entry, ExecutionJsonContext.Default.ExecutionAuditEntry);
        await _wal.AppendAsync(json, AuditRecordType, ct).ConfigureAwait(false);

        lock (_lock)
        {
            InsertOrdered(entry);
            TrimRetainedEntries();
        }

        _logger.LogInformation(
            "Execution audit {AuditId}: {Category}/{Action} {Outcome}",
            entry.AuditId,
            entry.Category,
            entry.Action,
            entry.Outcome);
    }

    public async ValueTask DisposeAsync()
    {
        var initialisationTask = GetInitialisationTask();
        if (initialisationTask is not null)
        {
            await initialisationTask.ConfigureAwait(false);
        }

        await _wal.DisposeAsync().ConfigureAwait(false);
    }

    private async Task EnsureInitialisedAsync(CancellationToken ct)
    {
        Task initialisationTask;
        lock (_initializationLock)
        {
            _initializationTask ??= InitialiseAsync(CancellationToken.None);
            initialisationTask = _initializationTask;
        }

        await initialisationTask.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task InitialiseAsync(CancellationToken ct)
    {
        await _wal.InitializeAsync(ct).ConfigureAwait(false);

        await foreach (var record in _wal.GetUncommittedRecordsAsync(ct).ConfigureAwait(false))
        {
            if (!string.Equals(record.RecordType, AuditRecordType, StringComparison.Ordinal))
            {
                continue;
            }

            var payload = record.DeserializePayload<string>();
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize(payload, ExecutionJsonContext.Default.ExecutionAuditEntry);
                if (entry is not null)
                {
                    lock (_lock)
                    {
                        _entries.Add(entry);
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize execution audit payload from WAL.");
            }
        }

        lock (_lock)
        {
            _entries.Sort(static (left, right) => left.OccurredAt.CompareTo(right.OccurredAt));
            TrimRetainedEntries();
        }
    }

    private Task? GetInitialisationTask()
    {
        lock (_initializationLock)
        {
            return _initializationTask;
        }
    }

    /// <summary>
    /// Inserts in timestamp order rather than appending.
    /// <para>
    /// <see cref="RecordAsync(ExecutionAuditEntry, CancellationToken)"/> accepts a caller-built
    /// entry with any timestamp, and concurrent callers can complete out of order, so append order
    /// is not timestamp order. Trimming reasoned about a time window over what it assumed was a
    /// sorted list: one back-dated entry at the tail ended the window scan immediately, the trim
    /// concluded nothing was inside the window, and it then dropped the oldest *insertion* — which
    /// could be a live breach — while keeping the back-dated tail. Sorting on the way in keeps that
    /// assumption true for every reader instead of re-deriving it at each one. The common case is
    /// still an append: the search starts at the end.
    /// </para>
    /// </summary>
    private void InsertOrdered(ExecutionAuditEntry entry)
    {
        var index = _entries.Count;
        while (index > 0 && _entries[index - 1].OccurredAt > entry.OccurredAt)
        {
            index--;
        }

        _entries.Insert(index, entry);
    }

    private void TrimRetainedEntries()
    {
        if (_entries.Count <= _inMemoryRetention)
        {
            return;
        }

        // Entries are held in chronological order, so anything inside the window is a suffix.
        // Keep whichever is larger: the count cap, or that whole suffix.
        var cutoff = DateTimeOffset.UtcNow - _inMemoryRetentionWindow;
        var insideWindow = 0;
        for (var index = _entries.Count - 1; index >= 0 && _entries[index].OccurredAt >= cutoff; index--)
        {
            insideWindow++;
        }

        var hardCap = _inMemoryRetention * RetentionHardCapMultiplier;
        var keep = Math.Min(Math.Max(_inMemoryRetention, insideWindow), hardCap);
        if (_entries.Count <= keep)
        {
            return;
        }

        var removeCount = _entries.Count - keep;
        // The list is timestamp-ordered, so the removed prefix is the oldest and the newest of
        // them is the last one removed. Recording it is what makes incompleteness *sticky*:
        // completeness has to be judged by what was discarded, not by what happens to be retained.
        // Recomputing "insideWindow <= hardCap" on each trim looked self-healing and was not — once
        // a burst had already dropped entries, a single backdated append could leave the retained
        // set fitting the cap and flip the window back to "complete" while the dropped entries were
        // still inside it, letting a consumer resume asserting safety over a gap.
        //
        // A gap is only a gap over records a consumer would have used. Timestamps come from the
        // caller, so an implausibly future-dated entry is not evidence anybody can reason with, and
        // recording one as the newest discard would keep completeness false until wall-clock time
        // reached that date — entries stamped years ahead would hold every rule Constrained, and
        // order readiness closed, for years. That turns a writable audit path into an availability
        // control, so the plausible-dated newest discard is what gets recorded. The removed prefix
        // is ascending, so scanning back finds it and stops immediately in the ordinary case where
        // nothing is future-dated.
        var plausibleThrough = DateTimeOffset.UtcNow + WriterClockSkewAllowance;
        var newestPlausibleIndex = removeCount - 1;
        while (newestPlausibleIndex >= 0 && _entries[newestPlausibleIndex].OccurredAt > plausibleThrough)
        {
            newestPlausibleIndex--;
        }

        if (newestPlausibleIndex >= 0 && _entries[newestPlausibleIndex].OccurredAt >= cutoff)
        {
            var newestRemoved = _entries[newestPlausibleIndex].OccurredAt;
            _newestDiscardedInsideWindow =
                _newestDiscardedInsideWindow is { } existing && existing > newestRemoved
                    ? existing
                    : newestRemoved;

            if (!_windowTruncationLogged)
            {
                // Once per process: this is a capacity signal, not per-append noise.
                _windowTruncationLogged = true;
                _logger.LogWarning(
                    "Execution audit retained {Kept} of {InsideWindow} entries inside the {Window} retention window; "
                    + "time-bounded consumers cannot assert the absence of an event over it.",
                    keep,
                    insideWindow,
                    _inMemoryRetentionWindow);
            }
        }

        _entries.RemoveRange(0, removeCount);
    }
}
