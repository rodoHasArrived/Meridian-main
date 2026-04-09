namespace Meridian.Ledger;

/// <summary>
/// Holds multiple independent double-entry ledgers for a single project.
/// Each logical ledger can represent a different view such as actuals,
/// historical replay, parameterized P&amp;L, or contractual security-master flows.
/// </summary>
public sealed class ProjectLedgerBook
{
    private static readonly StringComparer KeyTextComparer = StringComparer.OrdinalIgnoreCase;
    private readonly Dictionary<LedgerBookKey, Ledger> _ledgers = new(LedgerBookKeyComparer.Instance);

    public ProjectLedgerBook(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Project identifier must not be null or whitespace.", nameof(projectId));

        ProjectId = projectId.Trim();
    }

    public string ProjectId { get; }

    public IReadOnlyCollection<LedgerBookKey> LedgerKeys
        => _ledgers.Keys
            .OrderBy(key => key.LedgerBook, StringComparer.OrdinalIgnoreCase)
            .ThenBy(key => key.LedgerView)
            .ThenBy(key => key.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public Ledger GetOrCreate(LedgerBookKey key)
    {
        var normalized = NormalizeKey(key);
        if (!_ledgers.TryGetValue(normalized, out var ledger))
        {
            ledger = new Ledger();
            _ledgers[normalized] = ledger;
        }

        return ledger;
    }

    public bool TryGetLedger(LedgerBookKey key, out Ledger? ledger)
        => _ledgers.TryGetValue(NormalizeKey(key), out ledger);

    public IReadOnlyDictionary<LedgerBookKey, IReadOnlyLedger> Snapshot()
        => ReadOnlyCollectionHelpers.FreezeDictionary(
            FilterLedgers().ToDictionary(pair => pair.Key, pair => (IReadOnlyLedger)pair.Value),
            LedgerBookKeyComparer.Instance);

    /// <summary>
    /// Returns all ledgers that match the supplied optional key filters.
    /// </summary>
    public IReadOnlyDictionary<LedgerBookKey, IReadOnlyLedger> FilteredSnapshot(
        string? ledgerBook = null,
        LedgerViewKind? ledgerView = null,
        string? scenarioId = null)
        => ReadOnlyCollectionHelpers.FreezeDictionary(
            FilterLedgers(ledgerBook, ledgerView, scenarioId)
                .ToDictionary(pair => pair.Key, pair => (IReadOnlyLedger)pair.Value),
            LedgerBookKeyComparer.Instance);

    /// <summary>
    /// Returns ledger keys that match the supplied optional key filters.
    /// </summary>
    public IReadOnlyList<LedgerBookKey> FilteredLedgerKeys(
        string? ledgerBook = null,
        LedgerViewKind? ledgerView = null,
        string? scenarioId = null)
        => FilterLedgers(ledgerBook, ledgerView, scenarioId)
            .Select(pair => pair.Key)
            .OrderBy(key => key.LedgerBook, StringComparer.OrdinalIgnoreCase)
            .ThenBy(key => key.LedgerView)
            .ThenBy(key => key.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Returns a consolidated trial balance across all ledgers matching the supplied key filters.
    /// </summary>
    public IReadOnlyDictionary<LedgerAccount, decimal> ConsolidatedTrialBalance(
        string? ledgerBook = null,
        LedgerViewKind? ledgerView = null,
        string? scenarioId = null,
        string? financialAccountId = null)
    {
        var balances = new Dictionary<LedgerAccount, decimal>();

        foreach (var (_, ledger) in FilterLedgers(ledgerBook, ledgerView, scenarioId))
        {
            foreach (var (account, amount) in ledger.TrialBalance(financialAccountId))
            {
                balances.TryGetValue(account, out var current);
                balances[account] = current + amount;
            }
        }

        return ReadOnlyCollectionHelpers.FreezeDictionary(balances);
    }

    /// <summary>
    /// Returns a consolidated point-in-time snapshot across all ledgers matching the supplied key filters.
    /// </summary>
    public LedgerSnapshot ConsolidatedSnapshotAsOf(
        DateTimeOffset timestamp,
        string? ledgerBook = null,
        LedgerViewKind? ledgerView = null,
        string? scenarioId = null,
        string? financialAccountId = null)
    {
        var balances = new Dictionary<LedgerAccount, decimal>();
        var journalCount = 0;
        var ledgerEntryCount = 0;

        foreach (var (_, ledger) in FilterLedgers(ledgerBook, ledgerView, scenarioId))
        {
            var snapshot = ledger.SnapshotAsOf(timestamp, financialAccountId);
            foreach (var (account, amount) in snapshot.Balances)
            {
                balances.TryGetValue(account, out var current);
                balances[account] = current + amount;
            }

            journalCount += snapshot.JournalEntryCount;
            ledgerEntryCount += snapshot.LedgerEntryCount;
        }

        return new LedgerSnapshot(timestamp, balances, journalCount, ledgerEntryCount);
    }

    /// <summary>
    /// Returns journal entries across all matching ledgers, ordered by timestamp then journal ID.
    /// </summary>
    public IReadOnlyList<JournalEntry> ConsolidatedJournalEntries(
        LedgerQuery? query = null,
        string? ledgerBook = null,
        LedgerViewKind? ledgerView = null,
        string? scenarioId = null)
    {
        var resolvedQuery = query ?? new LedgerQuery();

        return FilterLedgers(ledgerBook, ledgerView, scenarioId)
            .SelectMany(pair => pair.Value.GetJournalEntries(resolvedQuery))
            .OrderBy(entry => entry.Timestamp)
            .ThenBy(entry => entry.JournalEntryId)
            .ToList();
    }

    /// <summary>
    /// Returns account summaries aggregated across all ledgers matching the supplied key filters.
    /// </summary>
    public IReadOnlyList<LedgerAccountSummary> ConsolidatedAccountSummaries(
        LedgerAccountType? accountType = null,
        string? financialAccountId = null,
        string? ledgerBook = null,
        LedgerViewKind? ledgerView = null,
        string? scenarioId = null)
    {
        var summaries = new Dictionary<LedgerAccount, (decimal Debits, decimal Credits, int EntryCount, DateTimeOffset? FirstPostedAt, DateTimeOffset? LastPostedAt)>();

        foreach (var (_, ledger) in FilterLedgers(ledgerBook, ledgerView, scenarioId))
        {
            foreach (var summary in ledger.SummarizeAccounts(accountType, financialAccountId))
            {
                summaries.TryGetValue(summary.Account, out var current);

                var firstPostedAt = current.FirstPostedAt is null || (summary.FirstPostedAt is not null && summary.FirstPostedAt < current.FirstPostedAt)
                    ? summary.FirstPostedAt
                    : current.FirstPostedAt;

                var lastPostedAt = current.LastPostedAt is null || (summary.LastPostedAt is not null && summary.LastPostedAt > current.LastPostedAt)
                    ? summary.LastPostedAt
                    : current.LastPostedAt;

                summaries[summary.Account] = (
                    current.Debits + summary.TotalDebits,
                    current.Credits + summary.TotalCredits,
                    current.EntryCount + summary.EntryCount,
                    firstPostedAt,
                    lastPostedAt);
            }
        }

        return summaries
            .Select(pair => new LedgerAccountSummary(
                pair.Key,
                CalculateNetBalance(pair.Key, pair.Value.Debits, pair.Value.Credits),
                pair.Value.Debits,
                pair.Value.Credits,
                pair.Value.EntryCount,
                pair.Value.FirstPostedAt,
                pair.Value.LastPostedAt))
            .OrderBy(summary => summary.Account.AccountType)
            .ThenBy(summary => summary.Account.Name, StringComparer.Ordinal)
            .ThenBy(summary => summary.Account.Symbol, StringComparer.Ordinal)
            .ThenBy(summary => summary.Account.FinancialAccountId, StringComparer.Ordinal)
            .ToList();
    }

    private LedgerBookKey NormalizeKey(LedgerBookKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var normalized = key.Normalize();
        if (!string.Equals(normalized.ProjectId, ProjectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Ledger book project '{normalized.ProjectId}' does not match container project '{ProjectId}'.",
                nameof(key));
        }

        return normalized;
    }

    private static string? NormalizeOptionalValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal CalculateNetBalance(LedgerAccount account, decimal debits, decimal credits)
        => account.AccountType is LedgerAccountType.Asset or LedgerAccountType.Expense
            ? debits - credits
            : credits - debits;

    private IEnumerable<KeyValuePair<LedgerBookKey, Ledger>> FilterLedgers(
        string? ledgerBook = null,
        LedgerViewKind? ledgerView = null,
        string? scenarioId = null)
    {
        var normalizedLedgerBook = NormalizeOptionalValue(ledgerBook);
        var normalizedScenarioId = NormalizeOptionalValue(scenarioId);

        return _ledgers
            .Where(pair => normalizedLedgerBook is null
                           || string.Equals(pair.Key.LedgerBook, normalizedLedgerBook, StringComparison.OrdinalIgnoreCase))
            .Where(pair => ledgerView is null || pair.Key.LedgerView == ledgerView)
            .Where(pair => normalizedScenarioId is null
                           || string.Equals(pair.Key.ScenarioId, normalizedScenarioId, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class LedgerBookKeyComparer : IEqualityComparer<LedgerBookKey>
    {
        public static LedgerBookKeyComparer Instance { get; } = new();

        public bool Equals(LedgerBookKey? x, LedgerBookKey? y)
        {
            if (ReferenceEquals(x, y))
                return true;

            if (x is null || y is null)
                return false;

            return KeyTextComparer.Equals(x.ProjectId, y.ProjectId)
                && KeyTextComparer.Equals(x.LedgerBook, y.LedgerBook)
                && x.LedgerView == y.LedgerView
                && KeyTextComparer.Equals(x.ScenarioId, y.ScenarioId);
        }

        public int GetHashCode(LedgerBookKey obj)
        {
            ArgumentNullException.ThrowIfNull(obj);

            var hash = new HashCode();
            hash.Add(obj.ProjectId, KeyTextComparer);
            hash.Add(obj.LedgerBook, KeyTextComparer);
            hash.Add(obj.LedgerView);
            hash.Add(obj.ScenarioId, KeyTextComparer);
            return hash.ToHashCode();
        }
    }
}
