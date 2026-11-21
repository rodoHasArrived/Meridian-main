namespace Meridian.Ledger;

/// <summary>The economic source populations covered by an accounting completeness run.</summary>
public enum LedgerAccountingSourceKind
{
    Fill,
    AssetEvent,
    LoanEvent,
    FinancingCharge
}

/// <summary>
/// Authoritative scope supplied by the source/store adapter. Book and financial-account identities
/// are compared ordinally; a journal in another scope cannot satisfy an expected posting.
/// </summary>
public sealed record LedgerAccountingScope(string LedgerBook, string FinancialAccountId);

/// <summary>
/// One retained source event and the exact originating journal purposes required by its policy.
/// A fill may require both "trade-fill" and "commission". Journal legs are never counted as entries.
/// Sources with no accounting consequence use an empty list, explicitly, rather than being omitted.
/// </summary>
public sealed record LedgerAccountingExpectation(
    LedgerAccountingScope Scope,
    LedgerAccountingSourceKind SourceKind,
    string SourceId,
    IReadOnlyList<string> RequiredPostingKeys);

/// <summary>
/// An authoritative retained journal and its source attribution. The adapter must supply the exact
/// store scope and policy purpose; it must not infer either from a matching amount. A missing source
/// identity is retained as a completeness break rather than silently dropping the journal.
/// </summary>
public sealed record LedgerAccountingJournalObservation(
    JournalEntry Entry,
    LedgerAccountingScope Scope,
    LedgerAccountingSourceKind SourceKind,
    string? SourceId,
    string PostingKey)
{
    /// <summary>Reads existing fill provenance without manufacturing a fill identity.</summary>
    public static LedgerAccountingJournalObservation FromFill(
        JournalEntry entry,
        LedgerAccountingScope scope,
        string postingKey)
        => new(entry, scope, LedgerAccountingSourceKind.Fill,
            entry.Metadata.FillId?.ToString("D"), postingKey);

    /// <summary>Reads the retained source-event tag used by loan and asset adapters.</summary>
    public static LedgerAccountingJournalObservation FromSourceEvent(
        JournalEntry entry,
        LedgerAccountingScope scope,
        LedgerAccountingSourceKind kind,
        string postingKey)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var sourceId = entry.Metadata.Tags?
            .FirstOrDefault(pair => string.Equals(pair.Key, "sourceEventId", StringComparison.OrdinalIgnoreCase))
            .Value;
        return new(entry, scope, kind, sourceId, postingKey);
    }
}

public enum LedgerAccountingCompletenessIssueKind
{
    MissingJournal,
    DuplicateJournal,
    UnexpectedJournal,
    UnlinkedJournal,
    ScopeMismatch
}

public sealed record LedgerAccountingCompletenessIssue(
    LedgerAccountingCompletenessIssueKind Kind,
    LedgerAccountingScope Scope,
    LedgerAccountingSourceKind SourceKind,
    string? SourceId,
    string PostingKey,
    IReadOnlyList<Guid> JournalEntryIds);

/// <summary>
/// Counts describe this declared population, not global ledger coverage. A complete result requires
/// both independently read populations to be complete; truncated/unavailable inputs fail closed.
/// </summary>
public sealed record LedgerAccountingCompletenessReport(
    int SourceEventCount,
    int ExpectedJournalCount,
    int ObservedJournalCount,
    int MatchedJournalCount,
    bool SourcePopulationComplete,
    bool JournalPopulationComplete,
    IReadOnlyList<LedgerAccountingCompletenessIssue> Issues)
{
    public bool IsComplete => SourcePopulationComplete && JournalPopulationComplete && Issues.Count == 0;
}

/// <summary>
/// Reconciles independently retained economic events to originating journal receipts. Corrections,
/// reversals and manual journals belong in separately declared posting populations; adapters must
/// not present them as another originating receipt for the original event.
/// </summary>
public static class LedgerAccountingCompleteness
{
    public static LedgerAccountingCompletenessReport Reconcile(
        IEnumerable<LedgerAccountingExpectation> sources,
        IEnumerable<LedgerAccountingJournalObservation> journals,
        bool sourcePopulationComplete,
        bool journalPopulationComplete)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(journals);
        var expected = new Dictionary<PostingIdentity, LedgerAccountingScope>();
        var events = new HashSet<EventIdentity>();
        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            ValidateScope(source.Scope);
            ValidateKind(source.SourceKind);
            var sourceId = Required(source.SourceId, nameof(source.SourceId));
            var eventIdentity = new EventIdentity(source.Scope, source.SourceKind, sourceId);
            if (!events.Add(eventIdentity))
                throw new LedgerValidationException("The accounting source population contains a duplicate source event in one scope.");
            ArgumentNullException.ThrowIfNull(source.RequiredPostingKeys);
            foreach (var rawKey in source.RequiredPostingKeys)
            {
                var key = Required(rawKey, nameof(source.RequiredPostingKeys));
                if (!expected.TryAdd(new PostingIdentity(eventIdentity, key), source.Scope))
                    throw new LedgerValidationException("An accounting source event declares a duplicate required posting key.");
            }
        }

        var observed = new Dictionary<PostingIdentity, List<Guid>>();
        var issues = new List<LedgerAccountingCompletenessIssue>();
        var seenJournalIds = new HashSet<Guid>();
        var observedCount = 0;
        foreach (var journal in journals)
        {
            ArgumentNullException.ThrowIfNull(journal);
            ArgumentNullException.ThrowIfNull(journal.Entry);
            ValidateScope(journal.Scope);
            ValidateKind(journal.SourceKind);
            var key = Required(journal.PostingKey, nameof(journal.PostingKey));
            if (journal.Entry.JournalEntryId == Guid.Empty)
                throw new LedgerValidationException("A retained journal must have a nonempty identity for completeness reconciliation.");
            if (!journal.Entry.IsBalanced)
                throw new LedgerValidationException("An unbalanced retained journal cannot satisfy an accounting completeness obligation.");
            // Duplicate query rows must not be mistaken for separately posted journals. They also
            // cannot be silently deduplicated: the caller's authoritative population is ambiguous.
            if (!seenJournalIds.Add(journal.Entry.JournalEntryId))
                throw new LedgerValidationException("The accounting journal population repeats one retained journal identity.");
            observedCount++;
            if (string.IsNullOrWhiteSpace(journal.SourceId))
            {
                issues.Add(new(LedgerAccountingCompletenessIssueKind.UnlinkedJournal, journal.Scope,
                    journal.SourceKind, null, key, Array.AsReadOnly(new[] { journal.Entry.JournalEntryId })));
                continue;
            }
            var sourceId = Required(journal.SourceId, nameof(journal.SourceId));
            var identity = new PostingIdentity(new EventIdentity(journal.Scope, journal.SourceKind, sourceId), key);
            if (!observed.TryGetValue(identity, out var ids))
            {
                ids = [];
                observed.Add(identity, ids);
            }
            ids.Add(journal.Entry.JournalEntryId);
        }

        var matchedCount = 0;
        foreach (var (identity, scope) in expected)
        {
            if (!observed.TryGetValue(identity, out var ids))
            {
                issues.Add(Issue(LedgerAccountingCompletenessIssueKind.MissingJournal, identity, []));
                continue;
            }
            if (ids.Count == 1)
                matchedCount++;
            else
                issues.Add(Issue(LedgerAccountingCompletenessIssueKind.DuplicateJournal, identity, ids));
        }
        foreach (var (identity, ids) in observed)
        {
            if (expected.ContainsKey(identity))
                continue;
            var wrongScope = expected.Keys.Any(item => item.Event.SourceKind == identity.Event.SourceKind
                && item.Event.SourceId == identity.Event.SourceId && item.PostingKey == identity.PostingKey
                && item.Event.Scope != identity.Event.Scope);
            issues.Add(Issue(wrongScope ? LedgerAccountingCompletenessIssueKind.ScopeMismatch
                : LedgerAccountingCompletenessIssueKind.UnexpectedJournal, identity, ids));
            if (ids.Count > 1)
                issues.Add(Issue(LedgerAccountingCompletenessIssueKind.DuplicateJournal, identity, ids));
        }

        var ordered = issues.OrderBy(item => item.Scope.LedgerBook, StringComparer.Ordinal)
            .ThenBy(item => item.Scope.FinancialAccountId, StringComparer.Ordinal)
            .ThenBy(item => item.SourceKind).ThenBy(item => item.SourceId, StringComparer.Ordinal)
            .ThenBy(item => item.PostingKey, StringComparer.Ordinal).ThenBy(item => item.Kind).ToArray();
        return new(events.Count, expected.Count, observedCount, matchedCount,
            sourcePopulationComplete, journalPopulationComplete, Array.AsReadOnly(ordered));
    }

    private static LedgerAccountingCompletenessIssue Issue(
        LedgerAccountingCompletenessIssueKind kind, PostingIdentity identity, IEnumerable<Guid> ids)
        => new(kind, identity.Event.Scope, identity.Event.SourceKind, identity.Event.SourceId,
            identity.PostingKey, Array.AsReadOnly(ids.Order().ToArray()));

    private static void ValidateScope(LedgerAccountingScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Required(scope.LedgerBook, nameof(scope.LedgerBook));
        Required(scope.FinancialAccountId, nameof(scope.FinancialAccountId));
    }

    private static void ValidateKind(LedgerAccountingSourceKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new LedgerValidationException("Unknown accounting source kind.");
    }

    private static string Required(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            throw new LedgerValidationException($"{field} must contain an exact nonempty identity without surrounding whitespace.");
        return value;
    }

    private sealed record EventIdentity(LedgerAccountingScope Scope, LedgerAccountingSourceKind SourceKind, string SourceId);
    private sealed record PostingIdentity(EventIdentity Event, string PostingKey);
}
