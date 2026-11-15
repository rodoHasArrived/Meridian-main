using Meridian.Backtesting.Engine;

namespace Meridian.Backtesting.Portfolio;

internal sealed partial class SimulatedPortfolio
{
    private CanonicalBacktestActions? _canonicalActions;
    private readonly HashSet<(Guid Id, string Stage)> _appliedCanonicalStages = [];

    public void ConfigureCanonicalActions(CanonicalBacktestActions actions) => _canonicalActions = actions;

    public void ApplyCanonicalActions(DateOnly date, IReadOnlyList<Order>? pendingOrders = null) => _canonicalActions?.Apply(date, this, pendingOrders);

    public void ApplyCanonicalSplit(BacktestCanonicalAction item, string contentHash)
    {
        if (_appliedCanonicalStages.Contains((item.Action.CorpActId, "split")))
            return;
        var factor = item.Action.SplitRatio!.Value;
        if (_accounts.Values.Any(account =>
                account.Positions.GetValueOrDefault(item.Symbol) * factor is var quantity &&
                (quantity < 0 || quantity != decimal.Truncate(quantity) || quantity > long.MaxValue)))
            throw new NotSupportedException($"Split '{item.Action.CorpActId}' requires integral long-share entitlements in canonical replay.");
        ApplyAssetEvent(new AssetEvent(
            At(item.Action.ExDate), item.Symbol, AssetEventType.Split,
            PositionFactor: factor, Description: $"Canonical split {item.Action.CorpActId:D}; snapshot {contentHash}"));
        _appliedCanonicalStages.Add((item.Action.CorpActId, "split"));
    }

    public void DeclareCanonicalDividend(BacktestCanonicalAction item, string contentHash)
    {
        if (!_appliedCanonicalStages.Add((item.Action.CorpActId, "declaration")))
            return;
        _stateVersion++;
        foreach (var account in _accounts.Values)
        {
            var shares = account.Positions.GetValueOrDefault(item.Symbol);
            if (shares <= 0)
                continue;
            var amount = shares * item.Action.DividendPerShare!.Value;
            var accountId = account.Account.AccountId;
            PostCanonicalDividend(item, contentHash, "declaration", account,
                _chart.Resolve(LedgerAccountRole.DividendReceivable, accountId, item.Symbol),
                _chart.Resolve(LedgerAccountRole.DividendIncome, accountId), amount, item.Action.ExDate);
            account.DividendEntitlements.Add(item.Action.CorpActId, (amount, shares));
            // Non-cash recognition participates in income attribution, not in cash movement.
            _cashFlows.Add(new AssetEventCashFlow(
                At(item.Action.ExDate), 0m, item.Symbol, AssetEventType.Dividend,
                shares, item.Action.DividendPerShare.Value,
                Description: $"Canonical dividend entitlement {item.Action.CorpActId:D}")
            { AccountId = accountId });
        }
    }

    public void SettleCanonicalDividend(BacktestCanonicalAction item, string contentHash)
    {
        if (!_appliedCanonicalStages.Add((item.Action.CorpActId, "settlement")))
            return;
        _stateVersion++;
        foreach (var account in _accounts.Values)
        {
            if (!account.DividendEntitlements.TryGetValue(item.Action.CorpActId, out var entitlement))
                continue;
            var accountId = account.Account.AccountId;
            PostCanonicalDividend(item, contentHash, "settlement", account,
                _chart.Resolve(LedgerAccountRole.Cash, accountId),
                _chart.Resolve(LedgerAccountRole.DividendReceivable, accountId, item.Symbol), entitlement.Amount, item.Action.PayDate!.Value);
            account.DividendEntitlements.Remove(item.Action.CorpActId);
            account.Cash += entitlement.Amount;
            _cashFlows.Add(new DividendCashFlow(At(item.Action.PayDate.Value), entitlement.Amount,
                item.Symbol, entitlement.Shares, item.Action.DividendPerShare!.Value, accountId));
        }
    }

    private void PostCanonicalDividend(
        BacktestCanonicalAction item, string contentHash, string stage, AccountState account,
        LedgerAccount debit, LedgerAccount credit, decimal amount, DateOnly date)
    {
        _ledger?.PostLines(At(date), $"Canonical dividend {stage} {item.Symbol}",
            [
                (debit, amount, 0m, (LedgerLineDimensionSet?)null, new LedgerEntryCurrency("USD", "USD", amount, 0m, 1m)),
                (credit, 0m, amount, (LedgerLineDimensionSet?)null, new LedgerEntryCurrency("USD", "USD", 0m, amount, 1m))
            ], new JournalEntryMetadata(
                ActivityType: $"canonical_dividend_{stage}", Symbol: item.Symbol,
                SecurityId: item.Action.SecurityId, FinancialAccountId: account.Account.AccountId,
                EffectiveDate: date,
                IdempotencyKey: $"backtest-corporate-action/{item.Action.CorpActId:D}/{account.Account.AccountId}/{stage}",
                Tags: new Dictionary<string, string>
                {
                    ["corporateActionId"] = item.Action.CorpActId.ToString("D"),
                    ["corporateActionSnapshotHash"] = contentHash,
                    ["securityVersion"] = item.SecurityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
    }

    private static DateTimeOffset At(DateOnly date) => new(date, TimeOnly.MinValue, TimeSpan.Zero);
}
