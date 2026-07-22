namespace Meridian.Ledger;

/// <summary>
/// Input for a fiscal-year-end close. Unlike a single-period close, a year-end close gates on all of
/// its constituent periods being closed first (<see cref="RequiredPeriodIds"/> vs
/// <see cref="ClosedPeriodIds"/>) and rolls the year's net income forward into next year's opening
/// retained earnings.
/// </summary>
public sealed record YearEndCloseInput
{
    public YearEndCloseInput(
        string fiscalYearLabel,
        DateTimeOffset fiscalYearEndUtc,
        IReadOnlyList<PeriodCloseAccountBalance> trialBalance,
        string closedBy,
        IReadOnlyList<string>? requiredPeriodIds = null,
        IReadOnlyList<string>? closedPeriodIds = null)
    {
        if (string.IsNullOrWhiteSpace(fiscalYearLabel))
            throw new ArgumentException("Fiscal year label must not be null or whitespace.", nameof(fiscalYearLabel));
        ArgumentNullException.ThrowIfNull(trialBalance);
        if (string.IsNullOrWhiteSpace(closedBy))
            throw new ArgumentException("Close actor must not be null or whitespace.", nameof(closedBy));

        FiscalYearLabel = fiscalYearLabel.Trim();
        FiscalYearEndUtc = fiscalYearEndUtc.ToUniversalTime();
        TrialBalance = trialBalance;
        ClosedBy = closedBy.Trim();
        RequiredPeriodIds = NormalizeIds(requiredPeriodIds);
        ClosedPeriodIds = NormalizeIds(closedPeriodIds);
    }

    /// <summary>Convenience overload for a dimension-flat trial balance.</summary>
    public YearEndCloseInput(
        string fiscalYearLabel,
        DateTimeOffset fiscalYearEndUtc,
        IReadOnlyDictionary<LedgerAccount, decimal> trialBalance,
        string closedBy,
        IReadOnlyList<string>? requiredPeriodIds = null,
        IReadOnlyList<string>? closedPeriodIds = null)
        : this(
            fiscalYearLabel,
            fiscalYearEndUtc,
            (trialBalance ?? throw new ArgumentNullException(nameof(trialBalance)))
                .Select(static pair => new PeriodCloseAccountBalance(pair.Key, pair.Value))
                .ToArray(),
            closedBy,
            requiredPeriodIds,
            closedPeriodIds)
    {
    }

    public string FiscalYearLabel { get; }

    public DateTimeOffset FiscalYearEndUtc { get; }

    public IReadOnlyList<PeriodCloseAccountBalance> TrialBalance { get; }

    public string ClosedBy { get; }

    /// <summary>Constituent period ids (months/quarters) that must be closed before year-end.</summary>
    public IReadOnlyList<string> RequiredPeriodIds { get; }

    /// <summary>Constituent period ids that are actually closed.</summary>
    public IReadOnlyList<string> ClosedPeriodIds { get; }

    private static IReadOnlyList<string> NormalizeIds(IReadOnlyList<string>? ids)
        => (ids ?? [])
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

/// <summary>
/// One (financial-account, dimensional) retained-earnings scope carried into next year. Keeping the
/// dimensional scope lets a caller seed each entity/sleeve's opening retained earnings independently,
/// rather than a single merged financial-account total.
/// </summary>
public sealed record YearEndRetainedEarningsRoll(
    string FinancialScope,
    LedgerLineDimensionSet? Dimensions,
    decimal OpeningBalance);

/// <summary>
/// The projected result of a fiscal-year-end close: the annual closing entries (reusing the
/// single-period projector over the year-end trial balance), the readiness gate over constituent
/// periods, and next year's opening retained earnings per dimensional scope.
/// </summary>
public sealed record YearEndCloseProjection(
    YearEndCloseInput Input,
    bool IsReady,
    IReadOnlyList<string> MissingPeriods,
    PeriodCloseProjection ClosingEntries,
    IReadOnlyList<YearEndRetainedEarningsRoll> OpeningRetainedEarnings)
{
    /// <summary>Net income rolled to retained earnings for the fiscal year.</summary>
    public decimal NetIncome => ClosingEntries.NetIncome;

    /// <summary>True when there were temporary-account balances to close for the year.</summary>
    public bool HasClosingEntries => ClosingEntries.HasClosingEntries;

    /// <summary>Total opening retained earnings carried into next year across all scopes.</summary>
    public decimal TotalOpeningRetainedEarnings => OpeningRetainedEarnings.Sum(static roll => roll.OpeningBalance);
}

/// <summary>
/// Projects fiscal-year-end closing entries and the retained-earnings roll-forward. The projection is
/// always produced; <see cref="YearEndCloseProjection.IsReady"/> tells the caller whether every
/// required constituent period has been closed, so the workflow can refuse to post an early year-end.
/// </summary>
public static class YearEndCloseProjector
{
    public static YearEndCloseProjection Project(YearEndCloseInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var closedSet = new HashSet<string>(input.ClosedPeriodIds, StringComparer.OrdinalIgnoreCase);
        var missing = input.RequiredPeriodIds
            .Where(periodId => !closedSet.Contains(periodId))
            .ToArray();

        var closingEntries = PeriodCloseProjector.Project(new PeriodCloseInput(
            input.FiscalYearLabel,
            input.FiscalYearEndUtc,
            input.TrialBalance,
            input.ClosedBy));

        var retainedEarningsName = LedgerAccounts.RetainedEarnings.Name;

        // Accumulate opening retained earnings per (financial-account scope, dimensional scope) so
        // entity/sleeve retained earnings roll forward independently — matching how
        // PeriodCloseProjector preserves dimensional scopes in its closing entries.
        var rolls = new Dictionary<string, YearEndRollAccumulator>(StringComparer.Ordinal);

        // Prior-year retained earnings from the year-end trial balance.
        foreach (var row in input.TrialBalance)
        {
            if (row.Account.AccountType == LedgerAccountType.Equity
                && string.Equals(row.Account.Name, retainedEarningsName, StringComparison.OrdinalIgnoreCase))
            {
                AccumulateRoll(rolls, row.Account.FinancialAccountId, row.Dimensions, row.Balance);
            }
        }

        // Current-year net income, taken from the closing entries' retained-earnings roll lines so each
        // roll keeps its dimensional scope (a credit increases equity; a debit is a net loss).
        foreach (var line in closingEntries.JournalLines)
        {
            if (string.Equals(line.account.Name, retainedEarningsName, StringComparison.OrdinalIgnoreCase))
            {
                AccumulateRoll(rolls, line.account.FinancialAccountId, line.dimensions, line.credit - line.debit);
            }
        }

        var openingRetainedEarnings = rolls.Values
            .Select(static roll => new YearEndRetainedEarningsRoll(roll.FinancialScope, roll.Dimensions, roll.Opening))
            .OrderBy(static roll => roll.FinancialScope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static roll => LedgerLineDimensionSetFields.BuildScopeKey(roll.Dimensions), StringComparer.Ordinal)
            .ToArray();

        return new YearEndCloseProjection(
            input,
            missing.Length == 0,
            missing,
            closingEntries,
            openingRetainedEarnings);
    }

    private static void AccumulateRoll(
        IDictionary<string, YearEndRollAccumulator> rolls,
        string? financialAccountId,
        LedgerLineDimensionSet? dimensions,
        decimal amount)
    {
        var financialScope = financialAccountId ?? PeriodCloseProjection.DefaultScope;
        var key = FormattableString.Invariant($"{financialScope}|{LedgerLineDimensionSetFields.BuildScopeKey(dimensions)}");
        rolls[key] = rolls.TryGetValue(key, out var current)
            ? current with { Dimensions = current.Dimensions ?? dimensions, Opening = current.Opening + amount }
            : new YearEndRollAccumulator(financialScope, dimensions, amount);
    }

    private sealed record YearEndRollAccumulator(string FinancialScope, LedgerLineDimensionSet? Dimensions, decimal Opening);
}
