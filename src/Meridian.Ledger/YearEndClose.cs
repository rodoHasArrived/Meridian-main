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
/// The projected result of a fiscal-year-end close: the annual closing entries (reusing the
/// single-period projector over the year-end trial balance), the readiness gate over constituent
/// periods, and next year's opening retained earnings by scope.
/// </summary>
public sealed record YearEndCloseProjection(
    YearEndCloseInput Input,
    bool IsReady,
    IReadOnlyList<string> MissingPeriods,
    PeriodCloseProjection ClosingEntries,
    IReadOnlyDictionary<string, decimal> OpeningRetainedEarningsByScope)
{
    /// <summary>Net income rolled to retained earnings for the fiscal year.</summary>
    public decimal NetIncome => ClosingEntries.NetIncome;

    /// <summary>True when there were temporary-account balances to close for the year.</summary>
    public bool HasClosingEntries => ClosingEntries.HasClosingEntries;

    /// <summary>Total opening retained earnings carried into next year across all scopes.</summary>
    public decimal TotalOpeningRetainedEarnings => OpeningRetainedEarningsByScope.Values.Sum();
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
        var existingRetainedEarnings = input.TrialBalance
            .Where(row => row.Account.AccountType == LedgerAccountType.Equity
                          && string.Equals(row.Account.Name, retainedEarningsName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                row => row.Account.FinancialAccountId ?? PeriodCloseProjection.DefaultScope,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(static row => row.Balance),
                StringComparer.OrdinalIgnoreCase);

        var openingRetainedEarnings = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in existingRetainedEarnings.Keys.Union(closingEntries.NetIncomeByScope.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var opening = existingRetainedEarnings.GetValueOrDefault(scope)
                          + closingEntries.NetIncomeByScope.GetValueOrDefault(scope);
            openingRetainedEarnings[scope] = opening;
        }

        return new YearEndCloseProjection(
            input,
            missing.Length == 0,
            missing,
            closingEntries,
            openingRetainedEarnings);
    }
}
