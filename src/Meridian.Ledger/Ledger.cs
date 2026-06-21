using Meridian.FSharp.Ledger;

namespace Meridian.Ledger;

/// <summary>
/// Double-entry accounting ledger.
/// Holds all <see cref="JournalEntry"/> records posted during a run and provides
/// account-balance queries, filtered journal views, and trial-balance summaries.
/// </summary>
/// <remarks>
/// <para>
/// Every economic event (fill, commission, margin interest, transfers, etc.) is recorded as
/// a balanced journal entry: the sum of debits always equals the sum of credits.
/// </para>
/// <para>
/// Normal-balance rules followed here:
/// <list type="bullet">
///   <item><term>Asset / Expense</term><description>Debit-normal (debit increases, credit decreases).</description></item>
///   <item><term>Liability / Equity / Revenue</term><description>Credit-normal (credit increases, debit decreases).</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class Ledger : IReadOnlyLedger
{
    private readonly List<JournalEntry> _journal = [];
    private readonly HashSet<Guid> _journalEntryIds = [];
    private readonly HashSet<Guid> _ledgerEntryIds = [];
    private readonly Dictionary<LedgerAccount, AccountTotals> _accountTotals = [];

    /// <summary>All journal entries in chronological posting order.</summary>
    public IReadOnlyList<JournalEntry> Journal => _journal;

    /// <summary>All accounts that have been posted to, in first-seen order.</summary>
    public IReadOnlyCollection<LedgerAccount> Accounts => _accountTotals.Keys;

    /// <summary>Total number of journal entries posted to this ledger.</summary>
    public int JournalEntryCount => _journal.Count;

    /// <summary>Total number of individual ledger entry lines (debit/credit rows) posted.</summary>
    public int TotalLedgerEntryCount => _ledgerEntryIds.Count;

    /// <summary>
    /// Posts a <see cref="JournalEntry"/> to the ledger.
    /// </summary>
    /// <param name="entry">The journal entry to post.</param>
    /// <exception cref="LedgerValidationException">Thrown when the entry fails validation.</exception>
    public void Post(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        ValidateJournalEntry(entry);

        _journal.Add(entry);
        _journalEntryIds.Add(entry.JournalEntryId);

        foreach (var line in entry.Lines)
        {
            _ledgerEntryIds.Add(line.EntryId);

            if (!_accountTotals.TryGetValue(line.Account, out var totals))
                totals = AccountTotals.Empty;

            _accountTotals[line.Account] = totals.Add(line.Debit, line.Credit, entry.Timestamp);
        }
    }

    /// <summary>Returns all individual ledger lines posted to <paramref name="account"/>.</summary>
    public IReadOnlyList<LedgerEntry> GetEntries(LedgerAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return _journal
            .SelectMany(j => j.Lines)
            .Where(l => l.Account == account)
            .ToList();
    }

    /// <summary>
    /// Returns all individual ledger lines posted to <paramref name="account"/> within the supplied time range.
    /// </summary>
    public IReadOnlyList<LedgerEntry> GetEntries(LedgerAccount account, DateTimeOffset? from, DateTimeOffset? to)
    {
        ArgumentNullException.ThrowIfNull(account);
        EnsureValidRange(from, to);

        return _journal
            .Where(j => IsWithinRange(j.Timestamp, from, to))
            .SelectMany(j => j.Lines)
            .Where(l => l.Account == account)
            .ToList();
    }

    /// <summary>
    /// Returns the net balance for <paramref name="account"/> using normal-balance rules.
    /// Assets and expenses carry debit-normal balances (debits − credits).
    /// Liabilities, equity, and revenues carry credit-normal balances (credits − debits).
    /// </summary>
    public decimal GetBalance(LedgerAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return _accountTotals.TryGetValue(account, out var totals)
            ? CalculateNetBalance(account, totals.Debits, totals.Credits)
            : 0m;
    }

    /// <summary>
    /// Returns the balance for <paramref name="account"/> considering only postings on or before <paramref name="timestamp"/>.
    /// </summary>
    public decimal GetBalanceAsOf(LedgerAccount account, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(account);

        var debits = 0m;
        var credits = 0m;

        foreach (var journalEntry in _journal)
        {
            if (journalEntry.Timestamp > timestamp)
                continue;

            foreach (var line in journalEntry.Lines)
            {
                if (line.Account != account)
                    continue;

                debits += line.Debit;
                credits += line.Credit;
            }
        }

        return CalculateNetBalance(account, debits, credits);
    }

    /// <summary>
    /// Returns journal entries matching the supplied range and optional description filter.
    /// </summary>
    public IReadOnlyList<JournalEntry> GetJournalEntries(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? descriptionContains = null)
    {
        return GetJournalEntries(new LedgerQuery(from, to, descriptionContains));
    }

    /// <summary>
    /// Returns journal entries matching the supplied structured query.
    /// </summary>
    public IReadOnlyList<JournalEntry> GetJournalEntries(LedgerQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureValidRange(query.From, query.To);

        IEnumerable<JournalEntry> filtered = _journal;

        if (query.From is not null || query.To is not null)
            filtered = filtered.Where(entry => IsWithinRange(entry.Timestamp, query.From, query.To));

        if (!string.IsNullOrWhiteSpace(query.DescriptionContains))
            filtered = filtered.Where(entry => entry.Description.Contains(query.DescriptionContains, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.ActivityType))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.ActivityType, query.ActivityType, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.Symbol))
        {
            var normalizedSymbol = query.Symbol.Trim().ToUpperInvariant();
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.Symbol, normalizedSymbol, StringComparison.OrdinalIgnoreCase));
        }

        if (query.OrderId is not null)
            filtered = filtered.Where(entry => entry.Metadata.OrderId == query.OrderId);

        if (query.FillId is not null)
            filtered = filtered.Where(entry => entry.Metadata.FillId == query.FillId);

        if (query.SecurityId is not null)
            filtered = filtered.Where(entry => entry.Metadata.SecurityId == query.SecurityId);

        if (!string.IsNullOrWhiteSpace(query.ProjectId))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.ProjectId, query.ProjectId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.LedgerBook))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.LedgerBook, query.LedgerBook, StringComparison.OrdinalIgnoreCase));

        if (query.LedgerView is not null)
            filtered = filtered.Where(entry => entry.Metadata.LedgerView == query.LedgerView);

        if (!string.IsNullOrWhiteSpace(query.ScenarioId))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.ScenarioId, query.ScenarioId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.StrategyId))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.StrategyId, query.StrategyId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.FinancialAccountId))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.FinancialAccountId, query.FinancialAccountId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.CounterpartyAccountId))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.CounterpartyAccountId, query.CounterpartyAccountId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.Institution))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.Institution, query.Institution, StringComparison.OrdinalIgnoreCase));

        if (query.AccountType is not null)
            filtered = filtered.Where(entry => entry.Lines.Any(l => l.Account.AccountType == query.AccountType.Value));

        if (query.EffectiveDate is not null)
            filtered = filtered.Where(entry => entry.Metadata.EffectiveDate == query.EffectiveDate.Value);

        if (!string.IsNullOrWhiteSpace(query.IdempotencyKey))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.IdempotencyKey, query.IdempotencyKey.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.FundEventId))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.FundEventId, query.FundEventId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.FundEventType))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.FundEventType, query.FundEventType.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.CapitalAccountId))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.CapitalAccountId, query.CapitalAccountId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.InvestorId))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.InvestorId, query.InvestorId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.PaymentIntentId))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.PaymentIntentId, query.PaymentIntentId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.SettlementReference))
            filtered = filtered.Where(entry => string.Equals(entry.Metadata.SettlementReference, query.SettlementReference.Trim(), StringComparison.OrdinalIgnoreCase));

        if (query.LineDimensions is not null)
            filtered = filtered.Where(entry => entry.Lines.Any(line => MatchesLineDimensions(line.Dimensions, query.LineDimensions)));

        return filtered.ToList();
    }

    /// <summary>
    /// Returns whether the ledger contains postings for <paramref name="account"/>.
    /// </summary>
    public bool HasAccount(LedgerAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return _accountTotals.ContainsKey(account);
    }

    /// <summary>
    /// Returns a summarized view of a posted account, including totals and posting metadata.
    /// Accounts with no activity return a zero-valued summary.
    /// </summary>
    public LedgerAccountSummary GetAccountSummary(LedgerAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (!_accountTotals.TryGetValue(account, out var totals))
            return LedgerAccountSummary.Empty(account);

        return new LedgerAccountSummary(
            account,
            CalculateNetBalance(account, totals.Debits, totals.Credits),
            totals.Debits,
            totals.Credits,
            totals.EntryCount,
            totals.FirstPostedAt,
            totals.LastPostedAt);
    }

    /// <summary>
    /// Returns account summaries for every posted account, optionally filtered by account type and account scope.
    /// </summary>
    public IReadOnlyList<LedgerAccountSummary> SummarizeAccounts(
        LedgerAccountType? accountType = null,
        string? financialAccountId = null,
        LedgerLineDimensionSet? lineDimensions = null)
    {
        if (lineDimensions is not null)
        {
            return BuildAccountTotalsFromLines(timestamp: null, accountType, financialAccountId, lineDimensions)
                .Select(pair => BuildAccountSummary(pair.Key, pair.Value))
                .OrderBy(summary => summary.Account.AccountType)
                .ThenBy(summary => summary.Account.Name, StringComparer.Ordinal)
                .ThenBy(summary => summary.Account.Symbol, StringComparer.Ordinal)
                .ThenBy(summary => summary.Account.FinancialAccountId, StringComparer.Ordinal)
                .ToList();
        }

        return _accountTotals
            .Where(pair => accountType is null || pair.Key.AccountType == accountType)
            .Where(pair => MatchesFinancialAccount(pair.Key, financialAccountId))
            .Select(pair => BuildAccountSummary(pair.Key, pair.Value))
            .OrderBy(summary => summary.Account.AccountType)
            .ThenBy(summary => summary.Account.Name, StringComparer.Ordinal)
            .ThenBy(summary => summary.Account.Symbol, StringComparer.Ordinal)
            .ThenBy(summary => summary.Account.FinancialAccountId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Returns a trial balance mapping every account that has been posted to its net balance.
    /// If accounting is correct the sum of asset and expense balances equals the sum of liability,
    /// equity, and revenue balances (the accounting equation holds).
    /// </summary>
    public IReadOnlyDictionary<LedgerAccount, decimal> TrialBalance(
        string? financialAccountId = null,
        LedgerLineDimensionSet? lineDimensions = null)
    {
        if (lineDimensions is not null)
            return BuildTrialBalanceFromLines(timestamp: null, financialAccountId, lineDimensions);

        var result = new Dictionary<LedgerAccount, decimal>(_accountTotals.Count);
        foreach (var (account, totals) in _accountTotals)
        {
            if (!MatchesFinancialAccount(account, financialAccountId))
                continue;

            result[account] = CalculateNetBalance(account, totals.Debits, totals.Credits);
        }

        return result;
    }

    /// <summary>
    /// Returns a trial balance as of the supplied timestamp.
    /// </summary>
    public IReadOnlyDictionary<LedgerAccount, decimal> TrialBalanceAsOf(
        DateTimeOffset timestamp,
        string? financialAccountId = null,
        LedgerLineDimensionSet? lineDimensions = null)
        => BuildTrialBalanceFromLines(timestamp, financialAccountId, lineDimensions);

    private IReadOnlyDictionary<LedgerAccount, decimal> BuildTrialBalanceFromLines(
        DateTimeOffset? timestamp,
        string? financialAccountId,
        LedgerLineDimensionSet? lineDimensions)
    {
        var result = new Dictionary<LedgerAccount, decimal>();

        foreach (var journalEntry in _journal)
        {
            if (timestamp is not null && journalEntry.Timestamp > timestamp.Value)
                continue;

            foreach (var line in journalEntry.Lines)
            {
                if (!MatchesFinancialAccount(line.Account, financialAccountId))
                    continue;

                if (!MatchesLineDimensions(line.Dimensions, lineDimensions))
                    continue;

                result.TryGetValue(line.Account, out var currentBalance);
                var delta = CalculateNetBalance(line.Account, line.Debit, line.Credit);
                result[line.Account] = currentBalance + delta;
            }
        }

        return result;
    }

    /// <summary>
    /// Returns running-balance checkpoints for an account in chronological order.
    /// </summary>
    public IReadOnlyList<LedgerBalancePoint> GetRunningBalance(LedgerAccount account, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        EnsureValidRange(from, to);

        var points = new List<LedgerBalancePoint>();
        var runningBalance = from.HasValue
            ? GetBalanceAsOf(account, from.Value.AddTicks(-1))
            : 0m;

        foreach (var journalEntry in _journal)
        {
            if (!IsWithinRange(journalEntry.Timestamp, from, to))
                continue;

            var debit = 0m;
            var credit = 0m;

            foreach (var line in journalEntry.Lines)
            {
                if (line.Account != account)
                    continue;

                debit += line.Debit;
                credit += line.Credit;
            }

            if (debit == 0m && credit == 0m)
                continue;

            runningBalance += CalculateNetBalance(account, debit, credit);
            points.Add(new LedgerBalancePoint(
                journalEntry.Timestamp,
                journalEntry.JournalEntryId,
                journalEntry.Description,
                debit,
                credit,
                runningBalance,
                journalEntry.Metadata));
        }

        return points;
    }

    /// <summary>
    /// Returns point-in-time balances and posting counts.
    /// </summary>
    public LedgerSnapshot SnapshotAsOf(
        DateTimeOffset timestamp,
        string? financialAccountId = null,
        LedgerLineDimensionSet? lineDimensions = null)
    {
        var balances = TrialBalanceAsOf(timestamp, financialAccountId, lineDimensions);
        var journalCount = 0;
        var ledgerEntryCount = 0;

        foreach (var journalEntry in _journal)
        {
            if (journalEntry.Timestamp > timestamp)
                continue;

            var scopedLines = journalEntry.Lines
                .Where(line => MatchesFinancialAccount(line.Account, financialAccountId))
                .Where(line => MatchesLineDimensions(line.Dimensions, lineDimensions))
                .ToList();
            if (scopedLines.Count == 0)
                continue;

            journalCount++;
            ledgerEntryCount += scopedLines.Count;
        }

        return new LedgerSnapshot(timestamp, balances, journalCount, ledgerEntryCount);
    }

    /// <summary>
    /// Creates a balanced <see cref="JournalEntry"/> from a list of (account, debit, credit) tuples
    /// and immediately posts it. All lines share the same journal entry ID and timestamp.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="description"/> is null or whitespace, or when <paramref name="lines"/> is empty.
    /// </exception>
    public void PostLines(
        DateTimeOffset timestamp,
        string description,
        IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> lines)
        => PostLines(timestamp, description, lines, metadata: null);

    /// <summary>
    /// Creates and posts a balanced journal entry with optional audit metadata.
    /// </summary>
    public void PostLines(
        DateTimeOffset timestamp,
        string description,
        IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> lines,
        JournalEntryMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Journal entry description must not be null or whitespace.", nameof(description));
        if (lines.Count == 0)
            throw new ArgumentException("A journal entry must have at least one line.", nameof(lines));

        var journalId = Guid.NewGuid();
        var entries = lines
            .Select(l => new LedgerEntry(Guid.NewGuid(), journalId, timestamp, l.account, l.debit, l.credit, description))
            .ToList();

        Post(new JournalEntry(journalId, timestamp, description, entries, metadata));
    }

    /// <summary>
    /// Creates and posts a balanced journal entry with optional line-level dimensional accounting scope.
    /// </summary>
    public void PostLines(
        DateTimeOffset timestamp,
        string description,
        IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit, LedgerLineDimensionSet? dimensions)> lines,
        JournalEntryMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Journal entry description must not be null or whitespace.", nameof(description));
        if (lines.Count == 0)
            throw new ArgumentException("A journal entry must have at least one line.", nameof(lines));

        var journalId = Guid.NewGuid();
        var entries = lines
            .Select(l => new LedgerEntry(Guid.NewGuid(), journalId, timestamp, l.account, l.debit, l.credit, description, l.dimensions))
            .ToList();

        Post(new JournalEntry(journalId, timestamp, description, entries, metadata));
    }

    private static LedgerAccountSummary BuildAccountSummary(LedgerAccount account, AccountTotals totals)
        => new(
            account,
            CalculateNetBalance(account, totals.Debits, totals.Credits),
            totals.Debits,
            totals.Credits,
            totals.EntryCount,
            totals.FirstPostedAt,
            totals.LastPostedAt);

    private IReadOnlyDictionary<LedgerAccount, AccountTotals> BuildAccountTotalsFromLines(
        DateTimeOffset? timestamp,
        LedgerAccountType? accountType,
        string? financialAccountId,
        LedgerLineDimensionSet? lineDimensions)
    {
        var result = new Dictionary<LedgerAccount, AccountTotals>();

        foreach (var journalEntry in _journal)
        {
            if (timestamp is not null && journalEntry.Timestamp > timestamp.Value)
                continue;

            foreach (var line in journalEntry.Lines)
            {
                if (accountType is not null && line.Account.AccountType != accountType.Value)
                    continue;

                if (!MatchesFinancialAccount(line.Account, financialAccountId))
                    continue;

                if (!MatchesLineDimensions(line.Dimensions, lineDimensions))
                    continue;

                result.TryGetValue(line.Account, out var current);
                result[line.Account] = current.Add(line.Debit, line.Credit, journalEntry.Timestamp);
            }
        }

        return result;
    }

    private void ValidateJournalEntry(JournalEntry entry)
    {
        var validation = LedgerInterop.ValidateJournalEntry(
            entry.JournalEntryId,
            entry.Timestamp,
            entry.Description,
            entry.Lines.Select(ToLedgerLineInput),
            _journalEntryIds,
            _ledgerEntryIds);

        if (!validation.IsValid)
            throw new LedgerValidationException(validation.Errors[0]);
    }

    private static bool MatchesFinancialAccount(LedgerAccount account, string? financialAccountId)
        => string.IsNullOrWhiteSpace(financialAccountId)
            || string.Equals(account.FinancialAccountId, financialAccountId.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesLineDimensions(LedgerLineDimensionSet? actual, LedgerLineDimensionSet? expected)
    {
        if (expected is null || !HasAnyDimension(expected))
            return true;

        if (actual is null)
            return false;

        return Matches(actual.FundId, expected.FundId)
            && Matches(actual.EntityId, expected.EntityId)
            && Matches(actual.SleeveId, expected.SleeveId)
            && Matches(actual.StrategyId, expected.StrategyId)
            && Matches(actual.InvestorId, expected.InvestorId)
            && Matches(actual.CapitalAccountId, expected.CapitalAccountId)
            && Matches(actual.InstrumentId, expected.InstrumentId)
            && Matches(actual.TaxLotId, expected.TaxLotId)
            && Matches(actual.CostCenterId, expected.CostCenterId)
            && Matches(actual.CounterpartyId, expected.CounterpartyId)
            && Matches(actual.OrganizationId, expected.OrganizationId)
            && Matches(actual.PortfolioId, expected.PortfolioId)
            && Matches(actual.BookId, expected.BookId)
            && Matches(actual.AccountId, expected.AccountId)
            && Matches(actual.CustomerId, expected.CustomerId)
            && Matches(actual.VendorId, expected.VendorId)
            && Matches(actual.ProjectId, expected.ProjectId)
            && MatchesExternalGlDimensions(actual.ExternalGlDimensions, expected.ExternalGlDimensions);
    }

    private static bool HasAnyDimension(LedgerLineDimensionSet dimensions)
        => HasValue(dimensions.FundId)
            || HasValue(dimensions.EntityId)
            || HasValue(dimensions.SleeveId)
            || HasValue(dimensions.StrategyId)
            || HasValue(dimensions.InvestorId)
            || HasValue(dimensions.CapitalAccountId)
            || dimensions.InstrumentId is not null
            || HasValue(dimensions.TaxLotId)
            || HasValue(dimensions.CostCenterId)
            || HasValue(dimensions.CounterpartyId)
            || HasValue(dimensions.OrganizationId)
            || HasValue(dimensions.PortfolioId)
            || HasValue(dimensions.BookId)
            || HasValue(dimensions.AccountId)
            || HasValue(dimensions.CustomerId)
            || HasValue(dimensions.VendorId)
            || HasValue(dimensions.ProjectId)
            || dimensions.ExternalGlDimensions.Count > 0;

    private static bool Matches(string? actual, string? expected)
    {
        if (!HasValue(expected))
            return true;

        var expectedValue = expected!.Trim();
        return string.Equals(actual?.Trim(), expectedValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(Guid? actual, Guid? expected)
        => expected is null || actual == expected;

    private static bool MatchesExternalGlDimensions(
        IReadOnlyDictionary<string, string> actual,
        IReadOnlyDictionary<string, string> expected)
    {
        if (expected.Count == 0)
            return true;

        foreach (var (key, expectedValue) in expected)
        {
            if (!actual.TryGetValue(key, out var actualValue))
            {
                var matchingPair = actual.FirstOrDefault(
                    pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
                if (matchingPair.Key is null)
                    return false;

                actualValue = matchingPair.Value;
            }

            if (!string.Equals(actualValue?.Trim(), expectedValue?.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool HasValue(string? value)
        => !string.IsNullOrWhiteSpace(value);

    private static decimal CalculateNetBalance(LedgerAccount account, decimal debits, decimal credits)
        => LedgerInterop.CalculateNetBalance((int)account.AccountType, debits, credits);

    private static LedgerLineInput ToLedgerLineInput(LedgerEntry line) =>
        new()
        {
            EntryId = line.EntryId,
            JournalEntryId = line.JournalEntryId,
            Timestamp = line.Timestamp,
            AccountName = line.Account.Name,
            AccountType = (int)line.Account.AccountType,
            Symbol = line.Account.Symbol ?? string.Empty,
            FinancialAccountId = line.Account.FinancialAccountId ?? string.Empty,
            Debit = line.Debit,
            Credit = line.Credit,
            Description = line.Description,
        };

    private static bool IsWithinRange(DateTimeOffset timestamp, DateTimeOffset? from, DateTimeOffset? to)
        => (!from.HasValue || timestamp >= from.Value) && (!to.HasValue || timestamp <= to.Value);

    private static void EnsureValidRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is not null && to is not null && from > to)
            throw new ArgumentException("The start of the range must be less than or equal to the end of the range.");
    }

    private readonly record struct AccountTotals(
        decimal Debits,
        decimal Credits,
        int EntryCount,
        DateTimeOffset? FirstPostedAt,
        DateTimeOffset? LastPostedAt)
    {
        public static AccountTotals Empty => new(0m, 0m, 0, null, null);

        public AccountTotals Add(decimal debit, decimal credit, DateTimeOffset timestamp)
        {
            var firstPostedAt = FirstPostedAt is null || timestamp < FirstPostedAt ? timestamp : FirstPostedAt;
            var lastPostedAt = LastPostedAt is null || timestamp > LastPostedAt ? timestamp : LastPostedAt;
            return new AccountTotals(Debits + debit, Credits + credit, EntryCount + 1, firstPostedAt, lastPostedAt);
        }
    }
}
