using System.Collections.ObjectModel;
using Meridian.Contracts.Integrity;
using Meridian.Ledger;

namespace Meridian.Backtesting.Portfolio;

/// <summary>
/// Tracks simulated cash, margin, positions, and a typed cash-flow ledger.
/// All mutations are single-threaded (called from the engine replay loop).
/// </summary>
internal sealed class SimulatedPortfolio
{
    private readonly BacktestLedger? _ledger;
    private readonly string _defaultBrokerageAccountId;
    private readonly Dictionary<string, AccountState> _accounts;
    private readonly Dictionary<string, decimal> _lastPrices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CashFlowEntry> _cashFlows = [];
    private decimal _prevEquity;

    // Rebuilding the aggregate views walks every account, every position, and every lot. The
    // engine reads them through IBacktestContext.PortfolioValue / .Positions, so a strategy that
    // checks its own equity once per bar previously paid a full rebuild per bar.
    //
    // Correctness rests on the mutating surface being closed: UpdateLastPrice, ProcessFill,
    // ApplyAssetEvent, and AccrueDailyInterest are the only members that change the state these
    // views derive from, and _lastPrices is handed out as IReadOnlyDictionary so callers cannot
    // mutate it behind the cache. Each of those four bumps _stateVersion; a missed bump would
    // serve a stale equity, so any new mutating member must bump it too.
    private int _stateVersion;
    private IReadOnlyDictionary<string, Position>? _cachedPositions;
    private int _cachedPositionsVersion = -1;
    private IReadOnlyDictionary<string, FinancialAccountSnapshot>? _cachedAccountSnapshots;
    private int _cachedAccountSnapshotsVersion = -1;

    public decimal Cash => _accounts.Values.Sum(static account => account.Cash);
    public decimal MarginBalance => _accounts.Values.Sum(static account => account.MarginBalance);
    public IReadOnlyDictionary<string, decimal> LastPrices => _lastPrices;

    public SimulatedPortfolio(
        decimal initialCash,
        ICommissionModel commission,
        double annualMarginRate,
        double annualShortRebateRate,
        BacktestLedger? ledger = null,
        DateTimeOffset startTimestamp = default)
        : this(
            [FinancialAccount.CreateDefaultBrokerage(initialCash, annualMarginRate, annualShortRebateRate)],
            BacktestDefaults.DefaultBrokerageAccountId,
            commission,
            ledger,
            startTimestamp)
    {
    }

    public SimulatedPortfolio(
        IReadOnlyList<FinancialAccount> accounts,
        string defaultBrokerageAccountId,
        ICommissionModel commission,
        BacktestLedger? ledger = null,
        DateTimeOffset startTimestamp = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultBrokerageAccountId);

        _ledger = ledger;
        _defaultBrokerageAccountId = defaultBrokerageAccountId.Trim();
        _accounts = accounts
            .Select(account => account.Normalize())
            .ToDictionary(account => account.AccountId, account => new AccountState(account), StringComparer.OrdinalIgnoreCase);

        if (_accounts.Count == 0)
            throw new ArgumentException("At least one financial account must be configured.", nameof(accounts));

        if (!_accounts.TryGetValue(_defaultBrokerageAccountId, out var defaultAccount))
            throw new ArgumentException($"Default brokerage account '{_defaultBrokerageAccountId}' was not configured.", nameof(defaultBrokerageAccountId));

        if (defaultAccount.Account.Kind != FinancialAccountKind.Brokerage)
            throw new ArgumentException($"Default account '{_defaultBrokerageAccountId}' must be a brokerage account.", nameof(defaultBrokerageAccountId));

        _prevEquity = _accounts.Values.Sum(static account => account.Cash);
        var openingTimestamp = startTimestamp == default ? DateTimeOffset.UtcNow : startTimestamp;

        foreach (var account in _accounts.Values)
        {
            if (account.Cash <= 0 || _ledger is null)
                continue;

            _ledger.PostLines(
                openingTimestamp,
                $"Initial capital deposit – {account.Account.DisplayName}",
                [
                    (LedgerAccounts.CashAccount(account.Account.AccountId), account.Cash, 0m),
                    (LedgerAccounts.CapitalAccountFor(account.Account.AccountId), 0m, account.Cash),
                ],
                BuildAccountMetadata(account, "capital"));
        }
    }

    // ── Price updates ────────────────────────────────────────────────────────

    public void UpdateLastPrice(string symbol, decimal price)
    {
        _lastPrices[symbol] = price;
        _stateVersion++;
    }

    // ── Order fill processing ────────────────────────────────────────────────

    public void ProcessFill(FillEvent fill)
    {
        _stateVersion++;
        var account = ResolveBrokerageAccount(fill.AccountId);
        var accountId = account.Account.AccountId;
        var symbol = fill.Symbol;
        var qty = fill.FilledQuantity;
        var price = fill.FillPrice;
        var commission = fill.Commission;

        account.Positions.TryGetValue(symbol, out var existingQty);

        if (qty < 0 && existingQty <= 0 && !account.Rules.AllowShortSelling)
            throw new InvalidOperationException($"Account '{accountId}' does not permit short selling.");

        var cashImpact = -(qty * price) - commission;
        var projectedCash = account.Cash + cashImpact;
        if (projectedCash < 0m && !account.Rules.AllowMargin)
            throw new InvalidOperationException($"Account '{accountId}' does not permit margin borrowing.");

        account.Cash = projectedCash;
        account.MarginBalance = account.Cash < 0m ? account.Cash : 0m;

        var newQty = existingQty + qty;
        account.Positions[symbol] = newQty;

        decimal? realised = null;
        decimal costBasisRemoved = 0m;
        decimal? shortRealised = null;
        decimal shortOriginalProceeds = 0m;
        long shortOpenQty = 0L;

        if (qty > 0)
        {
            if (!account.Lots.TryGetValue(symbol, out var lots))
            {
                lots = new LinkedList<OpenLot>();
                account.Lots[symbol] = lots;
            }

            var longBuyQty = existingQty >= 0 ? qty : Math.Max(qty + existingQty, 0L);
            if (longBuyQty > 0)
                lots.AddLast(new OpenLot(Guid.NewGuid(), symbol, longBuyQty, price, fill.FilledAt, fill.FillId, account.Account.AccountId));

            account.AvgCost[symbol] = ComputeAvgCost(account, symbol);
        }
        else if (qty < 0 && existingQty > 0)
        {
            var closeQty = Math.Min(-qty, existingQty);
            realised = RealiseLots(account, symbol, closeQty, price, fill.FilledAt, fill.FillId, fill.TargetLotId);
            account.RealizedPnl[symbol] = account.RealizedPnl.GetValueOrDefault(symbol) + realised.Value;
            costBasisRemoved = closeQty * price - realised.Value;
        }

        if (qty < 0)
        {
            shortOpenQty = existingQty <= 0
                ? -qty
                : Math.Max(-qty - existingQty, 0L);
        }

        if (shortOpenQty > 0)
        {
            if (!account.ShortLots.TryGetValue(symbol, out var shortLots))
            {
                shortLots = new LinkedList<OpenLot>();
                account.ShortLots[symbol] = shortLots;
            }

            shortLots.AddLast(new OpenLot(
                Guid.NewGuid(),
                symbol,
                shortOpenQty,
                price,
                fill.FilledAt,
                fill.FillId,
                account.Account.AccountId)
            {
                IsShort = true
            });
        }

        if (qty > 0 && existingQty < 0)
        {
            var coverQty = Math.Min(qty, -existingQty);
            (shortRealised, shortOriginalProceeds) = RealiseShortLots(account, symbol, coverQty, price, fill.FilledAt, fill.FillId, fill.TargetLotId);
            account.RealizedPnl[symbol] = account.RealizedPnl.GetValueOrDefault(symbol) + shortRealised.Value;
        }

        if (newQty == 0)
        {
            account.Positions.Remove(symbol);
            account.AvgCost.Remove(symbol);
        }
        else
        {
            account.AvgCost[symbol] = ComputeAvgCost(account, symbol);
        }

        _cashFlows.Add(new TradeCashFlow(fill.FilledAt, cashImpact, symbol, qty, price, accountId));

        if (commission > 0)
            _cashFlows.Add(new CommissionCashFlow(fill.FilledAt, -commission, symbol, fill.OrderId, accountId));

        // Post double-entry journal entries to ledger
        PostFillLedgerEntries(account, fill, qty, price, commission, existingQty, realised, costBasisRemoved, shortOpenQty, shortRealised, shortOriginalProceeds);
        CleanupSymbolIfFlat(account, symbol);
    }

    public void ApplyAssetEvent(AssetEvent assetEvent)
    {
        ArgumentNullException.ThrowIfNull(assetEvent);

        var symbol = assetEvent.Symbol;
        var targetSymbol = assetEvent.DestinationSymbol;
        if (assetEvent.HasPositionTransformation && assetEvent.PositionFactor <= 0m)
            throw new InvalidOperationException($"Asset event factor must be positive for {symbol}.");

        var authoritativeReferencePrice = assetEvent.HasPositionTransformation
            ? ResolveMarketReferencePrice(assetEvent)
            : null;
        // Freeze every impacted account and its cash-in-lieu reference before mutating any
        // position or global mark. This prevents account iteration order from changing a later
        // account's economics; an account-specific average-cost fallback remains account-specific.
        var impactedAccounts = _accounts.Values
            .Where(static account => account.Account.Kind == FinancialAccountKind.Brokerage)
            .Select(account => (
                Account: account,
                ExistingQuantity: account.Positions.GetValueOrDefault(symbol)))
            .Where(static item => item.ExistingQuantity != 0)
            .Select(item => (
                item.Account,
                item.ExistingQuantity,
                ReferencePrice: assetEvent.HasPositionTransformation
                    ? authoritativeReferencePrice ?? ResolveReferencePrice(
                        item.Account,
                        assetEvent,
                        item.ExistingQuantity,
                        assetEvent.PositionFactor)
                    : (decimal?)null))
            .ToArray();

        // Combining unlike directions requires explicit close/cover economics. A corporate action
        // cannot silently net a transformed source book against an existing opposite-direction
        // destination book because doing so would leave the aggregate quantity, lot direction,
        // and retained basis in disagreement. Validate the entire account set before any mutation.
        if (assetEvent.HasPositionTransformation &&
            !targetSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var impacted in impactedAccounts)
            {
                var transformedQuantity = ConvertToWholeUnits(
                    impacted.ExistingQuantity * assetEvent.PositionFactor);
                var existingTargetQuantity = impacted.Account.Positions.GetValueOrDefault(targetSymbol);
                if (transformedQuantity != 0 &&
                    existingTargetQuantity != 0 &&
                    (transformedQuantity > 0) != (existingTargetQuantity > 0))
                {
                    throw new InvalidOperationException(
                        $"Asset event {symbol} -> {targetSymbol} would combine opposite position directions " +
                        $"in account '{impacted.Account.Account.AccountId}'. Close or cover the destination " +
                        "position before applying the event.");
                }
            }
        }

        _stateVersion++;

        foreach (var impacted in impactedAccounts)
        {
            var account = impacted.Account;
            var existingQty = impacted.ExistingQuantity;

            decimal totalCashImpact = 0m;
            PositionTransformationResult transformation = default;
            if (assetEvent.HasPositionTransformation)
            {
                transformation = ApplyPositionTransformation(
                    account,
                    assetEvent,
                    existingQty,
                    targetSymbol,
                    impacted.ReferencePrice);
                totalCashImpact += transformation.CashInLieu;
            }

            if (assetEvent.CashPerShare != 0m)
                totalCashImpact += ApplyPerShareCashAdjustment(account, assetEvent, existingQty);

            account.MarginBalance = account.Cash < 0m ? account.Cash : 0m;
            _cashFlows.Add(new AssetEventCashFlow(
                assetEvent.EffectiveAt,
                totalCashImpact,
                symbol,
                assetEvent.EventType,
                existingQty,
                assetEvent.CashPerShare,
                assetEvent.TargetSymbol,
                assetEvent.PositionFactor,
                assetEvent.Description)
            {
                AccountId = account.Account.AccountId,
                FractionalUnits = transformation.FractionalUnits,
                BasisDisposed = transformation.BasisDisposed,
                RealizedPnl = transformation.RealizedPnl
            });
        }

        var hasSuccessorPosition = impactedAccounts.Any(item =>
            ConvertToWholeUnits(item.ExistingQuantity * assetEvent.PositionFactor) != 0);
        if (authoritativeReferencePrice is > 0m && hasSuccessorPosition)
        {
            _lastPrices[targetSymbol] = authoritativeReferencePrice.Value;
        }

        if (assetEvent.HasPositionTransformation &&
            targetSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) &&
            !hasSuccessorPosition)
        {
            _lastPrices.Remove(symbol);
        }

        if (assetEvent.HasPositionTransformation &&
            !targetSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
        {
            _lastPrices.Remove(symbol);
        }
    }

    // ── Day-end accruals ─────────────────────────────────────────────────────

    public void AccrueDailyInterest(DateOnly date)
    {
        _stateVersion++;
        var ts = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        foreach (var account in _accounts.Values)
        {
            if (account.MarginBalance < 0)
            {
                var interest = account.MarginBalance * (decimal)(account.Rules.AnnualMarginRate / 252.0);
                account.Cash += interest;
                account.MarginBalance = account.Cash < 0m ? account.Cash : 0m;
                _cashFlows.Add(new MarginInterestCashFlow(ts, interest, account.MarginBalance, account.Rules.AnnualMarginRate, account.Account.AccountId));

                var charge = Math.Abs(interest);
                _ledger?.PostLines(
                    ts,
                    $"Margin interest accrual – {account.Account.DisplayName} ({account.Rules.AnnualMarginRate:P2} p.a.)",
                    [
                        (LedgerAccounts.MarginInterestExpenseFor(account.Account.AccountId), charge, 0m),
                        (LedgerAccounts.CashAccount(account.Account.AccountId), 0m, charge),
                    ],
                    BuildAccountMetadata(account, "margin_interest"));
            }

            if (account.Rules.AnnualCashInterestRate > 0 && account.Cash > 0)
            {
                var cashInterest = account.Cash * (decimal)(account.Rules.AnnualCashInterestRate / 252.0);
                account.Cash += cashInterest;
                _cashFlows.Add(new CashInterestCashFlow(ts, cashInterest, account.Rules.AnnualCashInterestRate, account.Account.AccountId));
                _ledger?.PostLines(
                    ts,
                    $"Cash interest accrual – {account.Account.DisplayName} ({account.Rules.AnnualCashInterestRate:P2} p.a.)",
                    [
                        (LedgerAccounts.CashAccount(account.Account.AccountId), cashInterest, 0m),
                        (LedgerAccounts.CashInterestIncomeFor(account.Account.AccountId), 0m, cashInterest),
                    ],
                    BuildAccountMetadata(account, "cash_interest"));
            }

            foreach (var (symbol, qty) in account.Positions)
            {
                if (qty >= 0)
                    continue;

                var lastPrice = _lastPrices.GetValueOrDefault(symbol, 0m);
                if (lastPrice <= 0)
                    continue;

                var shortNotional = Math.Abs(qty) * lastPrice;
                var rebate = shortNotional * (decimal)(account.Rules.AnnualShortRebateRate / 252.0);
                account.Cash += rebate;
                _cashFlows.Add(new ShortRebateCashFlow(ts, rebate, symbol, Math.Abs(qty), account.Rules.AnnualShortRebateRate, account.Account.AccountId));

                _ledger?.PostLines(
                    ts,
                    $"Short rebate – {symbol} / {account.Account.DisplayName} ({account.Rules.AnnualShortRebateRate:P2} p.a.)",
                    [
                        (LedgerAccounts.CashAccount(account.Account.AccountId), rebate, 0m),
                        (LedgerAccounts.ShortRebateIncomeFor(account.Account.AccountId), 0m, rebate),
                    ],
                    BuildAccountMetadata(account, "short_rebate", symbol));
            }
        }
    }

    // ── Snapshot ─────────────────────────────────────────────────────────────

    public PortfolioSnapshot TakeSnapshot(DateTimeOffset timestamp, DateOnly date)
    {
        var positions = BuildAggregatePositions();
        var accountSnapshots = BuildAccountSnapshots();
        var longMv = accountSnapshots.Values.Sum(snapshot => snapshot.LongMarketValue);
        var shortMv = accountSnapshots.Values.Sum(snapshot => snapshot.ShortMarketValue);
        var equity = accountSnapshots.Values.Sum(snapshot => snapshot.Equity);
        var dailyReturn = _prevEquity == 0 ? 0m : (equity - _prevEquity) / _prevEquity;
        _prevEquity = equity;

        var dayCashFlows = _cashFlows.ToList();
        _cashFlows.Clear();

        return new PortfolioSnapshot(timestamp, date, Cash, MarginBalance, longMv, shortMv, equity, dailyReturn, positions, accountSnapshots, dayCashFlows);
    }

    public decimal ComputeCurrentEquity() => BuildAccountSnapshots().Values.Sum(snapshot => snapshot.Equity);

    public IReadOnlyDictionary<string, Position> GetCurrentPositions() => BuildAggregatePositions();

    public IReadOnlyDictionary<string, FinancialAccountSnapshot> GetAccountSnapshots() => BuildAccountSnapshots();

    /// <summary>Returns all open lots across all accounts, optionally filtered by symbol.</summary>
    public IReadOnlyList<OpenLot> GetOpenLots(string? symbol = null)
    {
        var result = new List<OpenLot>();
        foreach (var account in _accounts.Values)
        {
            foreach (var (sym, lots) in account.Lots)
            {
                if (symbol != null && !sym.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.AddRange(lots);
            }

            foreach (var (sym, lots) in account.ShortLots)
            {
                if (symbol != null && !sym.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.AddRange(lots);
            }
        }
        return result;
    }

    /// <summary>Returns all closed lots across all accounts, optionally filtered by symbol.</summary>
    public IReadOnlyList<ClosedLot> GetClosedLots(string? symbol = null)
    {
        var result = new List<ClosedLot>();
        foreach (var account in _accounts.Values)
        {
            foreach (var lot in account.ClosedLots)
            {
                if (symbol != null && !lot.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(lot);
            }
        }
        return result;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private decimal ApplyPerShareCashAdjustment(AccountState account, AssetEvent assetEvent, long quantity)
    {
        var amount = quantity * assetEvent.CashPerShare;
        account.Cash += amount;
        PostAssetCashLedgerEntry(account, assetEvent, amount, quantity, assetEvent.Symbol, assetEvent.TargetSymbol);
        return amount;
    }

    private PositionTransformationResult ApplyPositionTransformation(
        AccountState account,
        AssetEvent assetEvent,
        long existingQty,
        string targetSymbol,
        decimal? referencePriceOverride)
    {
        var factor = assetEvent.PositionFactor;
        if (factor <= 0m)
            throw new InvalidOperationException($"Asset event factor must be positive for {assetEvent.Symbol}.");

        var transformedQtyDecimal = existingQty * factor;
        var transformedQty = ConvertToWholeUnits(transformedQtyDecimal);
        var fractionalUnits = transformedQtyDecimal - transformedQty;
        var referencePrice = referencePriceOverride ?? ResolveReferencePrice(account, assetEvent, existingQty, factor);
        var cashInLieu = fractionalUnits * referencePrice;

        var sourceLongBasis = ComputeLotBasis(account.Lots.GetValueOrDefault(assetEvent.Symbol));
        var sourceShortBasis = ComputeLotBasis(account.ShortLots.GetValueOrDefault(assetEvent.Symbol));
        var transformedLotQuantity = Math.Abs(transformedQty);
        var transformedLongLots = TransformLots(
            account.Lots.GetValueOrDefault(assetEvent.Symbol),
            factor,
            transformedLotQuantity,
            targetSymbol);
        var transformedShortLots = TransformLots(
            account.ShortLots.GetValueOrDefault(assetEvent.Symbol),
            factor,
            transformedLotQuantity,
            targetSymbol);
        var sourceBasis = existingQty < 0 ? sourceShortBasis : sourceLongBasis;
        var retainedBasis = existingQty < 0
            ? ComputeLotBasis(transformedShortLots)
            : ComputeLotBasis(transformedLongLots);
        var fractionalBasis = Math.Max(0m, sourceBasis - retainedBasis);
        var fractionalRealized = fractionalUnits == 0m
            ? 0m
            : existingQty < 0
                ? fractionalBasis + cashInLieu
                : cashInLieu - fractionalBasis;
        var transformedRealized = account.RealizedPnl.GetValueOrDefault(assetEvent.Symbol);
        var existingTargetRealized = targetSymbol.Equals(assetEvent.Symbol, StringComparison.OrdinalIgnoreCase)
            ? 0m
            : account.RealizedPnl.GetValueOrDefault(targetSymbol);
        var successorRealized = existingTargetRealized + transformedRealized + fractionalRealized;

        RemoveSymbolState(account, assetEvent.Symbol);

        if (transformedQty != 0)
        {
            account.Positions[targetSymbol] = account.Positions.GetValueOrDefault(targetSymbol) + transformedQty;
            MergeLots(account.Lots, targetSymbol, transformedLongLots);
            MergeLots(account.ShortLots, targetSymbol, transformedShortLots);
            account.AvgCost[targetSymbol] = ComputeAvgCost(account, targetSymbol);
        }

        if (successorRealized != 0m || transformedQty != 0)
            account.RealizedPnl[targetSymbol] = successorRealized;

        if (cashInLieu != 0m)
            account.Cash += cashInLieu;

        PostAssetPositionTransformationLedgerEntries(
            account,
            assetEvent,
            existingQty,
            targetSymbol,
            retainedBasis,
            fractionalBasis,
            cashInLieu,
            fractionalRealized);

        return new PositionTransformationResult(
            cashInLieu,
            fractionalUnits,
            fractionalBasis,
            fractionalRealized);
    }

    private decimal ResolveReferencePrice(AccountState account, AssetEvent assetEvent, long existingQty, decimal factor)
    {
        if (assetEvent.ReferencePrice is { } explicitReference && explicitReference > 0m)
            return explicitReference;

        if (!assetEvent.DestinationSymbol.Equals(assetEvent.Symbol, StringComparison.OrdinalIgnoreCase) &&
            _lastPrices.TryGetValue(assetEvent.DestinationSymbol, out var destinationPrice) &&
            destinationPrice > 0m)
            return destinationPrice;

        if (_lastPrices.TryGetValue(assetEvent.Symbol, out var sourcePrice) && sourcePrice > 0m)
            return factor == 0m ? sourcePrice : sourcePrice / Math.Abs(factor);

        var avgCost = account.AvgCost.GetValueOrDefault(assetEvent.Symbol, 0m);
        if (avgCost > 0m)
            return factor == 0m ? avgCost : avgCost / Math.Abs(factor);

        return existingQty == 0 ? 0m : 1m;
    }

    private decimal? ResolveMarketReferencePrice(AssetEvent assetEvent)
    {
        if (assetEvent.ReferencePrice is > 0m)
            return assetEvent.ReferencePrice;

        if (!assetEvent.DestinationSymbol.Equals(assetEvent.Symbol, StringComparison.OrdinalIgnoreCase) &&
            _lastPrices.TryGetValue(assetEvent.DestinationSymbol, out var destinationPrice) &&
            destinationPrice > 0m)
        {
            return destinationPrice;
        }

        if (_lastPrices.TryGetValue(assetEvent.Symbol, out var sourcePrice) && sourcePrice > 0m)
        {
            return assetEvent.PositionFactor == 0m
                ? sourcePrice
                : sourcePrice / Math.Abs(assetEvent.PositionFactor);
        }

        return null;
    }

    private void PostAssetCashLedgerEntry(
        AccountState account,
        AssetEvent assetEvent,
        decimal amount,
        long quantity,
        string symbol,
        string? relatedSymbol,
        string? suffix = null)
    {
        if (_ledger is null || amount == 0m)
            return;

        var accountId = account.Account.AccountId;
        var counterpartyAccount = SelectAssetEventAccount(assetEvent.EventType, amount, accountId);
        var cashAccount = LedgerAccounts.CashAccount(accountId);
        var metadata = BuildAssetEventMetadata(account, assetEvent, symbol, relatedSymbol, quantity, amount, suffix);
        var description = string.IsNullOrWhiteSpace(assetEvent.Description)
            ? $"{assetEvent.EventType} – {symbol}" + (string.IsNullOrWhiteSpace(relatedSymbol) || relatedSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $" -> {relatedSymbol}") + (string.IsNullOrWhiteSpace(suffix) ? string.Empty : $" ({suffix})")
            : assetEvent.Description + (string.IsNullOrWhiteSpace(suffix) ? string.Empty : $" ({suffix})");

        if (amount > 0)
        {
            _ledger.PostLines(
                assetEvent.EffectiveAt,
                description,
                [
                    (cashAccount, amount, 0m),
                    (counterpartyAccount, 0m, amount),
                ],
                metadata);
        }
        else
        {
            var outflow = Math.Abs(amount);
            _ledger.PostLines(
                assetEvent.EffectiveAt,
                description,
                [
                    (counterpartyAccount, outflow, 0m),
                    (cashAccount, 0m, outflow),
                ],
                metadata);
        }
    }

    private static LedgerAccount SelectAssetEventAccount(AssetEventType eventType, decimal amount, string accountId) => eventType switch
    {
        AssetEventType.Dividend => amount >= 0m ? LedgerAccounts.DividendIncomeFor(accountId) : LedgerAccounts.DividendExpenseFor(accountId),
        AssetEventType.Coupon => amount >= 0m ? LedgerAccounts.CouponIncomeFor(accountId) : LedgerAccounts.CouponExpenseFor(accountId),
        AssetEventType.Fee => LedgerAccounts.CorporateActionExpenseFor(accountId),
        _ => amount >= 0m ? LedgerAccounts.CorporateActionIncomeFor(accountId) : LedgerAccounts.CorporateActionExpenseFor(accountId),
    };

    private JournalEntryMetadata BuildAssetEventMetadata(
        AccountState account,
        AssetEvent assetEvent,
        string symbol,
        string? relatedSymbol,
        long quantity,
        decimal amount,
        string? suffix)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["event_type"] = assetEvent.EventType.ToString(),
            ["cash_per_share"] = assetEvent.CashPerShare.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["position_factor"] = assetEvent.PositionFactor.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["units_impacted"] = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["cash_impact"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrWhiteSpace(relatedSymbol))
            tags["related_symbol"] = relatedSymbol!;

        if (!string.IsNullOrWhiteSpace(suffix))
            tags["ticket_variant"] = suffix!;

        return BuildAccountMetadata(account, $"asset_event_{assetEvent.EventType.ToString().ToLowerInvariant()}", symbol) with
        {
            Tags = tags
        };
    }

    private static long ConvertToWholeUnits(decimal quantity) => quantity >= 0m
        ? (long)Math.Floor(quantity)
        : (long)Math.Ceiling(quantity);

    private static LinkedList<OpenLot> TransformLots(
        LinkedList<OpenLot>? source,
        decimal factor,
        long transformedPositionQuantity,
        string targetSymbol)
    {
        var result = new LinkedList<OpenLot>();
        if (source is null || source.Count == 0 || transformedPositionQuantity <= 0)
            return result;

        var absoluteFactor = Math.Abs(factor);
        var exactTransformedQuantity = source.Sum(static lot => lot.Quantity) * absoluteFactor;
        if (exactTransformedQuantity <= 0m)
            return result;

        if (ConvertToWholeUnits(exactTransformedQuantity) != transformedPositionQuantity)
        {
            throw new InvalidOperationException(
                "Corporate-action lot quantity does not reconcile to the transformed position quantity.");
        }

        // Allocate exact successor entitlements in FIFO order. Existing component provenance is
        // flattened and scaled, so a second corporate action does not turn a prior composite into
        // one opaque source lot. A fractional entitlement carries its own basis into the next
        // component instead of assigning all earlier basis to whichever lot crosses a cumulative
        // whole-unit boundary. For example, lots of 3 @ $100 and 1 @ $200 in a 1-for-2 reverse
        // split become 1 @ $200 plus a synthesized 1 @ $300 lot assembled from the two remaining
        // half-share entitlements.
        var remainingWholeUnits = transformedPositionQuantity;
        var pendingQuantity = 0m;
        var pendingBasis = 0m;
        OpenLot? pendingTemplate = null;
        var pendingComponents = new List<OpenLotBasisComponent>();
        var pendingParentLotIds = new List<Guid>();
        var outputOrdinal = 0;

        foreach (var lot in source)
        {
            IReadOnlyList<OpenLotBasisComponent> sourceComponents = lot.BasisComponents is { Count: > 0 }
                ? lot.BasisComponents
                :
                [
                    new OpenLotBasisComponent(
                        lot.LotId,
                        lot.OpenFillId,
                        lot.OpenedAt,
                        lot.Quantity,
                        lot.CostBasis())
                ];

            if (sourceComponents.Sum(static component => component.SuccessorQuantity) != lot.Quantity)
            {
                throw new InvalidOperationException(
                    $"Corporate-action lot '{lot.LotId:N}' component quantities do not reconcile to its whole quantity.");
            }

            foreach (var sourceComponent in sourceComponents)
            {
                var unallocatedEntitlement = sourceComponent.SuccessorQuantity * absoluteFactor;
                var unallocatedBasis = sourceComponent.AllocatedBasis;

                if (pendingQuantity > 0m && remainingWholeUnits > 0)
                {
                    var consumed = Math.Min(1m - pendingQuantity, unallocatedEntitlement);
                    if (consumed > 0m)
                    {
                        var consumedBasis = TakeAllocatedBasis(
                            ref unallocatedEntitlement,
                            ref unallocatedBasis,
                            consumed);
                        pendingQuantity += consumed;
                        pendingBasis += consumedBasis;
                        AddBasisComponent(
                            pendingComponents,
                            sourceComponent,
                            consumed,
                            consumedBasis);
                        AddParentLot(pendingParentLotIds, lot.LotId);
                    }

                    if (pendingQuantity == 1m)
                    {
                        result.AddLast(CreateTransformedLot(
                            pendingTemplate!,
                            targetSymbol,
                            1,
                            pendingBasis,
                            pendingComponents,
                            absoluteFactor,
                            outputOrdinal,
                            pendingParentLotIds,
                            synthesizeIdentity: true));
                        outputOrdinal++;
                        remainingWholeUnits--;
                        pendingQuantity = 0m;
                        pendingBasis = 0m;
                        pendingTemplate = null;
                        pendingComponents.Clear();
                        pendingParentLotIds.Clear();
                    }
                }

                if (remainingWholeUnits > 0)
                {
                    var wholeFromComponent = Math.Min(
                        remainingWholeUnits,
                        ConvertToWholeUnits(unallocatedEntitlement));
                    if (wholeFromComponent > 0)
                    {
                        var directBasis = TakeAllocatedBasis(
                            ref unallocatedEntitlement,
                            ref unallocatedBasis,
                            wholeFromComponent);
                        var directComponent = sourceComponent with
                        {
                            SuccessorQuantity = wholeFromComponent,
                            AllocatedBasis = directBasis
                        };
                        result.AddLast(CreateTransformedLot(
                            lot,
                            targetSymbol,
                            wholeFromComponent,
                            directBasis,
                            [directComponent],
                            absoluteFactor,
                            outputOrdinal,
                            [lot.LotId],
                            synthesizeIdentity: lot.BasisComponents.Count > 1));
                        outputOrdinal++;
                        remainingWholeUnits -= wholeFromComponent;
                    }
                }

                if (unallocatedEntitlement > 0m)
                {
                    pendingTemplate ??= lot;
                    pendingQuantity += unallocatedEntitlement;
                    pendingBasis += unallocatedBasis;
                    AddBasisComponent(
                        pendingComponents,
                        sourceComponent,
                        unallocatedEntitlement,
                        unallocatedBasis);
                    AddParentLot(pendingParentLotIds, lot.LotId);
                }
            }
        }

        if (remainingWholeUnits != 0 || result.Last is null || pendingQuantity >= 1m)
            throw new InvalidOperationException("Corporate-action lot allocation did not match the transformed position quantity.");

        return result;
    }

    private static decimal TakeAllocatedBasis(
        ref decimal remainingQuantity,
        ref decimal remainingBasis,
        decimal quantity)
    {
        if (quantity <= 0m || quantity > remainingQuantity)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        var allocatedBasis = quantity == remainingQuantity
            ? remainingBasis
            : remainingBasis * quantity / remainingQuantity;
        remainingQuantity -= quantity;
        remainingBasis -= allocatedBasis;
        return allocatedBasis;
    }

    private static void AddBasisComponent(
        List<OpenLotBasisComponent> components,
        OpenLotBasisComponent source,
        decimal successorQuantity,
        decimal allocatedBasis)
    {
        if (components.Count > 0 &&
            components[^1].SourceLotId == source.SourceLotId &&
            components[^1].SourceOpenFillId == source.SourceOpenFillId &&
            components[^1].OpenedAt == source.OpenedAt)
        {
            var previous = components[^1];
            components[^1] = previous with
            {
                SuccessorQuantity = previous.SuccessorQuantity + successorQuantity,
                AllocatedBasis = previous.AllocatedBasis + allocatedBasis
            };
            return;
        }

        components.Add(source with
        {
            SuccessorQuantity = successorQuantity,
            AllocatedBasis = allocatedBasis
        });
    }

    private static void AddParentLot(List<Guid> parentLotIds, Guid lotId)
    {
        if (parentLotIds.Count == 0 || parentLotIds[^1] != lotId)
            parentLotIds.Add(lotId);
    }

    private static OpenLot CreateTransformedLot(
        OpenLot template,
        string targetSymbol,
        long quantity,
        decimal allocatedBasis,
        IReadOnlyList<OpenLotBasisComponent> basisComponents,
        decimal factor,
        int outputOrdinal,
        IReadOnlyList<Guid> parentLotIds,
        bool synthesizeIdentity)
    {
        var components = basisComponents.ToArray();
        var openedAt = components.Max(static component => component.OpenedAt);
        var lotId = synthesizeIdentity
            ? CreateTransformedIdentity(
                "lot",
                targetSymbol,
                factor,
                outputOrdinal,
                components,
                parentLotIds)
            : template.LotId;
        var openFillId = synthesizeIdentity
            ? CreateTransformedIdentity(
                "fill",
                targetSymbol,
                factor,
                outputOrdinal,
                components,
                parentLotIds)
            : template.OpenFillId;

        return template with
        {
            LotId = lotId,
            Symbol = targetSymbol,
            Quantity = quantity,
            EntryPrice = allocatedBasis / quantity,
            OpenedAt = openedAt,
            OpenFillId = openFillId,
            Notes = components.Length > 1
                ? BuildCompositeLotNotes(template.Notes, components)
                : template.Notes,
            BasisComponents = components
        };
    }

    private static Guid CreateTransformedIdentity(
        string identityKind,
        string targetSymbol,
        decimal factor,
        int outputOrdinal,
        IReadOnlyList<OpenLotBasisComponent> components,
        IReadOnlyList<Guid> parentLotIds)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var componentIdentity = string.Join(
            ",",
            components.Select(component => string.Join(
                ":",
                component.SourceLotId.ToString("N"),
                component.SourceOpenFillId.ToString("N"),
                component.OpenedAt.ToUniversalTime().Ticks.ToString(invariant),
                component.SuccessorQuantity.ToString("G29", invariant),
                component.AllocatedBasis.ToString("G29", invariant))));
        var identity = string.Join(
            "|",
            identityKind,
            targetSymbol.ToUpperInvariant(),
            factor.ToString("G29", invariant),
            outputOrdinal.ToString(invariant),
            string.Join(",", parentLotIds.Select(static id => id.ToString("N"))),
            componentIdentity);
        var hash = Sha256Digest.ComputeBytesUtf8(identity);
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string BuildCompositeLotNotes(
        string? existingNotes,
        IReadOnlyList<OpenLotBasisComponent> components)
    {
        var provenance = "Corporate-action composite from lots " +
            string.Join(
                ", ",
                components
                    .Select(static component => component.SourceLotId)
                    .Distinct()
                    .Select(static id => id.ToString("N")));
        return string.IsNullOrWhiteSpace(existingNotes)
            ? provenance
            : $"{existingNotes}; {provenance}";
    }

    private static decimal ComputeLotBasis(LinkedList<OpenLot>? lots) =>
        lots?.Sum(static lot => lot.CostBasis()) ?? 0m;

    private void PostAssetPositionTransformationLedgerEntries(
        AccountState account,
        AssetEvent assetEvent,
        long existingQuantity,
        string targetSymbol,
        decimal retainedBasis,
        decimal fractionalBasis,
        decimal cashInLieu,
        decimal fractionalRealized)
    {
        if (_ledger is null)
            return;

        var accountId = account.Account.AccountId;
        var sourceSymbol = assetEvent.Symbol;
        var isShort = existingQuantity < 0;
        var metadata = BuildAssetEventMetadata(
            account,
            assetEvent,
            sourceSymbol,
            targetSymbol,
            existingQuantity,
            cashInLieu,
            "position transformation");

        if (retainedBasis != 0m &&
            !targetSymbol.Equals(sourceSymbol, StringComparison.OrdinalIgnoreCase))
        {
            var sourceAccount = isShort
                ? LedgerAccounts.ShortSecuritiesPayable(sourceSymbol, accountId)
                : LedgerAccounts.Securities(sourceSymbol, accountId);
            var targetAccount = isShort
                ? LedgerAccounts.ShortSecuritiesPayable(targetSymbol, accountId)
                : LedgerAccounts.Securities(targetSymbol, accountId);
            IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> transferLines =
                isShort
                ?
                [
                    (sourceAccount, retainedBasis, 0m),
                    (targetAccount, 0m, retainedBasis)
                ]
                :
                [
                    (targetAccount, retainedBasis, 0m),
                    (sourceAccount, 0m, retainedBasis)
                ];
            _ledger.PostLines(
                assetEvent.EffectiveAt,
                $"Transfer retained basis {sourceSymbol} -> {targetSymbol} – {account.Account.DisplayName}",
                transferLines,
                metadata);
        }

        if (fractionalBasis == 0m && cashInLieu == 0m)
            return;

        var lines = new List<(LedgerAccount account, decimal debit, decimal credit)>();
        var cashAccount = LedgerAccounts.CashAccount(accountId);
        if (isShort)
        {
            if (fractionalBasis > 0m)
                lines.Add((LedgerAccounts.ShortSecuritiesPayable(sourceSymbol, accountId), fractionalBasis, 0m));
            if (cashInLieu < 0m)
                lines.Add((cashAccount, 0m, Math.Abs(cashInLieu)));
            else if (cashInLieu > 0m)
                lines.Add((cashAccount, cashInLieu, 0m));
        }
        else
        {
            if (cashInLieu > 0m)
                lines.Add((cashAccount, cashInLieu, 0m));
            else if (cashInLieu < 0m)
                lines.Add((cashAccount, 0m, Math.Abs(cashInLieu)));
            if (fractionalBasis > 0m)
                lines.Add((LedgerAccounts.Securities(sourceSymbol, accountId), 0m, fractionalBasis));
        }

        if (fractionalRealized > 0m)
            lines.Add((LedgerAccounts.RealizedGainFor(accountId), 0m, fractionalRealized));
        else if (fractionalRealized < 0m)
            lines.Add((LedgerAccounts.RealizedLossFor(accountId), Math.Abs(fractionalRealized), 0m));

        _ledger.PostLines(
            assetEvent.EffectiveAt,
            $"Cash in lieu {sourceSymbol} -> {targetSymbol} – {account.Account.DisplayName}",
            lines,
            metadata with { ActivityType = $"asset_event_{assetEvent.EventType.ToString().ToLowerInvariant()}_cash_in_lieu" });
    }

    private static void MergeLots(
        Dictionary<string, LinkedList<OpenLot>> store,
        string symbol,
        LinkedList<OpenLot> lots)
    {
        if (lots.Count == 0)
            return;

        if (!store.TryGetValue(symbol, out var existing))
        {
            store[symbol] = new LinkedList<OpenLot>(lots);
            return;
        }

        foreach (var lot in lots)
            existing.AddLast(lot);
    }

    private void RemoveSymbolState(AccountState account, string symbol)
    {
        account.Positions.Remove(symbol);
        account.AvgCost.Remove(symbol);
        account.Lots.Remove(symbol);
        account.ShortLots.Remove(symbol);
        account.RealizedPnl.Remove(symbol);
    }

    private void CleanupSymbolIfFlat(AccountState account, string symbol)
    {
        if (account.Positions.GetValueOrDefault(symbol) != 0)
            return;

        account.Positions.Remove(symbol);
        account.AvgCost.Remove(symbol);

        if (account.Lots.TryGetValue(symbol, out var lots) && lots.Count == 0)
            account.Lots.Remove(symbol);
        if (account.ShortLots.TryGetValue(symbol, out var shortLots) && shortLots.Count == 0)
            account.ShortLots.Remove(symbol);
    }

    private void PostFillLedgerEntries(
        AccountState account,
        FillEvent fill,
        long qty,
        decimal price,
        decimal commission,
        long existingQty,
        decimal? realised,
        decimal costBasisRemoved,
        long shortOpenQty,
        decimal? shortRealised,
        decimal shortOriginalProceeds)
    {
        if (_ledger is null)
            return;

        var ts = fill.FilledAt;
        var symbol = fill.Symbol;
        var accountId = account.Account.AccountId;
        var securitiesAccount = LedgerAccounts.Securities(symbol, accountId);
        var shortPayableAccount = LedgerAccounts.ShortSecuritiesPayable(symbol, accountId);
        var cashAccount = LedgerAccounts.CashAccount(accountId);
        var fillMetadata = BuildAccountMetadata(account, "fill", symbol, fill.OrderId, fill.FillId);

        var longBuyQty = qty > 0
            ? (existingQty >= 0 ? qty : Math.Max(qty + existingQty, 0L))
            : 0L;

        if (longBuyQty > 0)
        {
            var cost = longBuyQty * price;
            _ledger.PostLines(
                ts,
                $"Buy {longBuyQty} {symbol} @ {price:F4} – {account.Account.DisplayName}",
                [
                    (securitiesAccount, cost, 0m),
                    (cashAccount, 0m, cost),
                ],
                fillMetadata with { ActivityType = "buy" });
        }
        else if (qty < 0 && existingQty > 0 && realised.HasValue)
        {
            var closeQty = Math.Min(-qty, existingQty);
            var proceeds = closeQty * price;
            var gain = realised.Value;

            List<(LedgerAccount account, decimal debit, decimal credit)> lines;

            if (gain > 0)
            {
                lines =
                [
                    (cashAccount, proceeds, 0m),
                    (securitiesAccount, 0m, costBasisRemoved),
                    (LedgerAccounts.RealizedGainFor(accountId), 0m, gain),
                ];
            }
            else if (gain < 0)
            {
                lines =
                [
                    (cashAccount, proceeds, 0m),
                    (LedgerAccounts.RealizedLossFor(accountId), Math.Abs(gain), 0m),
                    (securitiesAccount, 0m, costBasisRemoved),
                ];
            }
            else
            {
                lines =
                [
                    (cashAccount, proceeds, 0m),
                    (securitiesAccount, 0m, costBasisRemoved),
                ];
            }

            _ledger.PostLines(ts, $"Sell {closeQty} {symbol} @ {price:F4} – {account.Account.DisplayName}", lines, fillMetadata with { ActivityType = "sell" });
        }

        if (shortOpenQty > 0)
        {
            var shortProceeds = shortOpenQty * price;
            _ledger.PostLines(
                ts,
                $"Short sell {shortOpenQty} {symbol} @ {price:F4} – {account.Account.DisplayName}",
                [
                    (cashAccount, shortProceeds, 0m),
                    (shortPayableAccount, 0m, shortProceeds),
                ],
                fillMetadata with { ActivityType = "short_sell" });
        }

        if (qty > 0 && existingQty < 0 && shortRealised.HasValue)
        {
            var coverQty = Math.Min(qty, -existingQty);
            var coverCost = coverQty * price;
            var gain = shortRealised.Value;

            List<(LedgerAccount account, decimal debit, decimal credit)> lines;

            if (gain > 0)
            {
                lines =
                [
                    (shortPayableAccount, shortOriginalProceeds, 0m),
                    (cashAccount, 0m, coverCost),
                    (LedgerAccounts.RealizedGainFor(accountId), 0m, gain),
                ];
            }
            else if (gain < 0)
            {
                lines =
                [
                    (shortPayableAccount, shortOriginalProceeds, 0m),
                    (LedgerAccounts.RealizedLossFor(accountId), Math.Abs(gain), 0m),
                    (cashAccount, 0m, coverCost),
                ];
            }
            else
            {
                lines =
                [
                    (shortPayableAccount, shortOriginalProceeds, 0m),
                    (cashAccount, 0m, coverCost),
                ];
            }

            _ledger.PostLines(ts, $"Cover short {coverQty} {symbol} @ {price:F4} – {account.Account.DisplayName}", lines, fillMetadata with { ActivityType = "cover_short" });
        }

        if (commission > 0)
        {
            _ledger.PostLines(
                ts,
                $"Commission – {symbol} order {fill.OrderId} – {account.Account.DisplayName}",
                [
                    (LedgerAccounts.CommissionExpenseFor(accountId), commission, 0m),
                    (cashAccount, 0m, commission),
                ],
                fillMetadata with { ActivityType = "commission" });
        }
    }

    private JournalEntryMetadata BuildAccountMetadata(
        AccountState account,
        string activityType,
        string? symbol = null,
        Guid? orderId = null,
        Guid? fillId = null)
        => new(
            ActivityType: activityType,
            Symbol: symbol,
            OrderId: orderId,
            FillId: fillId,
            FinancialAccountId: account.Account.AccountId,
            Institution: account.Account.Institution);

    private AccountState ResolveBrokerageAccount(string? accountId)
    {
        var normalizedAccountId = string.IsNullOrWhiteSpace(accountId)
            ? _defaultBrokerageAccountId
            : accountId.Trim();

        if (!_accounts.TryGetValue(normalizedAccountId, out var account))
            throw new InvalidOperationException($"Account '{normalizedAccountId}' was not configured.");

        if (account.Account.Kind != FinancialAccountKind.Brokerage)
            throw new InvalidOperationException($"Account '{normalizedAccountId}' is not a brokerage account.");

        return account;
    }

    private IReadOnlyDictionary<string, Position> BuildAggregatePositions()
    {
        if (_cachedPositions is not null && _cachedPositionsVersion == _stateVersion)
            return _cachedPositions;

        // Wrapped before caching: the cached instance is handed to every later reader and to
        // TakeSnapshot, so a caller that casts the returned view to IDictionary and mutates it
        // would otherwise corrupt subsequent reads. Rebuilding per call used to make that
        // harmless; caching does not, so the immutability has to be real.
        var built = new ReadOnlyDictionary<string, Position>(BuildAggregatePositionsCore());
        _cachedPositions = built;
        _cachedPositionsVersion = _stateVersion;
        return built;
    }

    private Dictionary<string, Position> BuildAggregatePositionsCore()
    {
        var grouped = new Dictionary<string, List<Position>>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in _accounts.Values)
        {
            foreach (var position in BuildPositions(account).Values)
            {
                if (!grouped.TryGetValue(position.Symbol, out var list))
                {
                    list = [];
                    grouped[position.Symbol] = list;
                }

                list.Add(position);
            }
        }

        var result = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase);
        foreach (var (symbol, positions) in grouped)
        {
            var totalQty = positions.Sum(static position => position.Quantity);
            if (totalQty == 0)
                continue;

            var totalCost = positions.Sum(position => Math.Abs(position.Quantity) * position.AverageCostBasis);
            var avgCost = positions.Sum(position => Math.Abs(position.Quantity)) == 0
                ? 0m
                : totalCost / positions.Sum(position => Math.Abs(position.Quantity));
            var unrealised = positions.Sum(static position => position.UnrealizedPnl);
            var realised = positions.Sum(static position => position.RealizedPnl);
            var openLots = positions.SelectMany(p => p.OpenLots ?? []).ToList();
            result[symbol] = new Position(symbol, totalQty, avgCost, unrealised, realised,
                openLots.Count > 0 ? openLots : null);
        }

        return result;
    }

    private IReadOnlyDictionary<string, FinancialAccountSnapshot> BuildAccountSnapshots()
    {
        if (_cachedAccountSnapshots is not null && _cachedAccountSnapshotsVersion == _stateVersion)
            return _cachedAccountSnapshots;

        var built = new ReadOnlyDictionary<string, FinancialAccountSnapshot>(BuildAccountSnapshotsCore());
        _cachedAccountSnapshots = built;
        _cachedAccountSnapshotsVersion = _stateVersion;
        return built;
    }

    private Dictionary<string, FinancialAccountSnapshot> BuildAccountSnapshotsCore()
    {
        return _accounts.Values.ToDictionary(
            account => account.Account.AccountId,
            account =>
            {
                var positions = BuildPositions(account);
                var longMv = positions.Values.Where(position => position.Quantity > 0)
                    .Sum(position => position.NotionalValue(_lastPrices.GetValueOrDefault(position.Symbol, position.AverageCostBasis)));
                var shortMv = positions.Values.Where(position => position.Quantity < 0)
                    .Sum(position => position.NotionalValue(_lastPrices.GetValueOrDefault(position.Symbol, position.AverageCostBasis)));
                var equity = account.Cash + longMv + shortMv;
                var openLots = account.Lots.Values
                    .Concat(account.ShortLots.Values)
                    .SelectMany(static lots => lots)
                    .ToList();
                return new FinancialAccountSnapshot(
                    account.Account.AccountId,
                    account.Account.DisplayName,
                    account.Account.Kind,
                    account.Account.Institution,
                    account.Cash,
                    account.MarginBalance,
                    longMv,
                    shortMv,
                    equity,
                    positions,
                    account.Rules,
                    openLots,
                    account.ClosedLots.ToList());
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyDictionary<string, Position> BuildPositions(AccountState account)
    {
        var result = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase);
        foreach (var (symbol, qty) in account.Positions)
        {
            if (qty == 0)
                continue;

            var avgCost = account.AvgCost.GetValueOrDefault(symbol, 0m);
            var lastPrice = _lastPrices.GetValueOrDefault(symbol, avgCost);
            var realised = account.RealizedPnl.GetValueOrDefault(symbol, 0m);
            IReadOnlyList<OpenLot> openLots;
            if (qty < 0 && account.ShortLots.TryGetValue(symbol, out var shortLots))
                openLots = shortLots.ToList();
            else if (account.Lots.TryGetValue(symbol, out var lots))
                openLots = lots.ToList();
            else
                openLots = Array.Empty<OpenLot>();
            var unrealised = openLots.Count > 0
                ? openLots.Sum(lot => lot.UnrealizedPnl(lastPrice))
                : (lastPrice - avgCost) * qty;
            result[symbol] = new Position(symbol, qty, avgCost, unrealised, realised, openLots);
        }

        return result;
    }

    private static decimal ComputeAvgCost(AccountState account, string symbol)
    {
        // Long and short lots never coexist for a symbol (covers consume short lots
        // before a residual buy opens long lots), so a net-short position's average
        // cost is the weighted average short entry price — not zero.
        if (account.Lots.TryGetValue(symbol, out var lots) && lots.Count > 0)
            return WeightedAverageEntryPrice(lots);

        if (account.ShortLots.TryGetValue(symbol, out var shortLots) && shortLots.Count > 0)
            return WeightedAverageEntryPrice(shortLots);

        return 0m;
    }

    private static decimal WeightedAverageEntryPrice(LinkedList<OpenLot> lots)
    {
        var totalQty = 0L;
        var totalCost = 0m;
        foreach (var lot in lots)
        {
            totalQty += lot.Quantity;
            totalCost += lot.CostBasis();
        }

        return totalQty == 0 ? 0m : totalCost / totalQty;
    }

    /// <summary>
    /// Realizes P&amp;L for a long position close using the account's configured lot selection method.
    /// <para>
    /// NOTE: This must stay consistent with <c>BacktestMetricsEngine.ComputeRealisedPnl</c>,
    /// which re-derives realized P&amp;L from the fill stream (honoring the same lot-selection
    /// method) for attribution. If you change this method, update the metrics counterpart in
    /// parallel.
    /// </para>
    /// </summary>
    private static decimal RealiseLots(
        AccountState account,
        string symbol,
        long closeQty,
        decimal sellPrice,
        DateTimeOffset closedAt,
        Guid closeFillId,
        Guid? targetLotId)
    {
        if (!account.Lots.TryGetValue(symbol, out var lots))
            return 0m;

        // Build an ordered sequence of lot nodes according to the selection method.
        var ordered = OrderLots(lots, account.Rules.LotSelection, targetLotId);
        var consumption = LotConsumption.Consume(ordered, closeQty, static node => node.Value.Quantity);

        var realised = 0m;

        foreach (var slice in consumption.Slices)
        {
            var node = slice.Lot;
            var lot = node.Value;
            var quantity = (long)slice.Quantity;
            var lotBasis = lot.CostBasis();
            var basisRemoved = slice.ClosesLot
                ? lotBasis
                : lotBasis * quantity / lot.Quantity;
            var closedEntryPrice = basisRemoved / quantity;

            realised += (quantity * sellPrice) - basisRemoved;
            account.ClosedLots.Add(new ClosedLot(
                lot.LotId, symbol, quantity, closedEntryPrice, lot.OpenedAt,
                lot.OpenFillId, sellPrice, closedAt, closeFillId, account.Account.AccountId));

            if (slice.ClosesLot)
            {
                lots.Remove(node);
            }
            else
            {
                // Replace lot with reduced quantity, preserve original LotId.
                lots.AddBefore(node, ReduceOpenLot(lot, lot.Quantity - quantity, lotBasis - basisRemoved));
                lots.Remove(node);
            }
        }

        account.AvgCost[symbol] = ComputeAvgCost(account, symbol);
        return realised;
    }

    private static (decimal realised, decimal shortSaleProceeds) RealiseShortLots(
        AccountState account,
        string symbol,
        long coverQty,
        decimal coverPrice,
        DateTimeOffset closedAt,
        Guid closeFillId,
        Guid? targetLotId)
    {
        if (!account.ShortLots.TryGetValue(symbol, out var lots))
            return (0m, coverQty * coverPrice);

        var ordered = OrderLots(lots, account.Rules.LotSelection, targetLotId);
        var consumption = LotConsumption.Consume(ordered, coverQty, static node => node.Value.Quantity);

        var realised = 0m;
        var shortSaleProceeds = 0m;

        foreach (var slice in consumption.Slices)
        {
            var node = slice.Lot;
            var lot = node.Value;
            var lotClose = (long)slice.Quantity;
            var lotBasis = lot.CostBasis();
            var lotProceeds = slice.ClosesLot
                ? lotBasis
                : lotBasis * lotClose / lot.Quantity;
            var closedEntryPrice = lotProceeds / lotClose;
            realised += lotProceeds - lotClose * coverPrice;
            shortSaleProceeds += lotProceeds;

            account.ClosedLots.Add(new ClosedLot(
                lot.LotId, symbol, lotClose, closedEntryPrice, lot.OpenedAt,
                lot.OpenFillId, coverPrice, closedAt, closeFillId, account.Account.AccountId, IsShort: true));

            if (slice.ClosesLot)
            {
                lots.Remove(node);
            }
            else
            {
                lots.AddBefore(node, ReduceOpenLot(lot, lot.Quantity - lotClose, lotBasis - lotProceeds));
                lots.Remove(node);
            }
        }

        return (realised, shortSaleProceeds);
    }

    private static OpenLot ReduceOpenLot(OpenLot lot, long remainingQuantity, decimal remainingBasis)
    {
        OpenLotBasisComponent[] basisComponents = [];
        if (lot.BasisComponents is { Count: > 0 })
        {
            var lotBasis = lot.CostBasis();
            var quantityRatio = (decimal)remainingQuantity / lot.Quantity;
            var basisRatio = lotBasis == 0m ? quantityRatio : remainingBasis / lotBasis;
            basisComponents = lot.BasisComponents
                .Select(component => component with
                {
                    SuccessorQuantity = component.SuccessorQuantity * quantityRatio,
                    AllocatedBasis = component.AllocatedBasis * basisRatio
                })
                .ToArray();
            var last = basisComponents[^1];
            basisComponents[^1] = last with
            {
                SuccessorQuantity = last.SuccessorQuantity +
                    (remainingQuantity - basisComponents.Sum(static component => component.SuccessorQuantity)),
                AllocatedBasis = last.AllocatedBasis +
                    (remainingBasis - basisComponents.Sum(static component => component.AllocatedBasis))
            };
        }

        return lot with
        {
            Quantity = remainingQuantity,
            EntryPrice = remainingBasis / remainingQuantity,
            BasisComponents = basisComponents
        };
    }

    /// <summary>
    /// Returns lot nodes in the order they should be consumed for the given
    /// <see cref="LotSelectionMethod"/>. For SpecificId the nominated lot is moved first;
    /// any remaining needed quantity falls back to FIFO.
    /// </summary>
    private static IEnumerable<LinkedListNode<OpenLot>> OrderLots(
        LinkedList<OpenLot> lots,
        LotSelectionMethod method,
        Guid? targetLotId)
    {
        return method switch
        {
            LotSelectionMethod.Lifo => IterateReverse(lots),
            LotSelectionMethod.Hifo => lots.EnumerateNodes()
                .OrderByDescending(n => n.Value.CostBasis() / n.Value.Quantity),
            LotSelectionMethod.SpecificId when targetLotId.HasValue =>
                SpecificIdFirst(lots, targetLotId.Value),
            _ => lots.EnumerateNodes(),   // Fifo + SpecificId fallback
        };
    }

    private static IEnumerable<LinkedListNode<OpenLot>> IterateReverse(LinkedList<OpenLot> lots)
    {
        var node = lots.Last;
        while (node is not null)
        {
            var prev = node.Previous;
            yield return node;
            node = prev;
        }
    }

    private static IEnumerable<LinkedListNode<OpenLot>> SpecificIdFirst(LinkedList<OpenLot> lots, Guid targetLotId)
    {
        var target = lots.EnumerateNodes().FirstOrDefault(n => n.Value.LotId == targetLotId);
        if (target is not null)
            yield return target;
        foreach (var node in lots.EnumerateNodes().Where(n => n.Value.LotId != targetLotId))
            yield return node;
    }

    private readonly record struct PositionTransformationResult(
        decimal CashInLieu,
        decimal FractionalUnits,
        decimal BasisDisposed,
        decimal RealizedPnl);

    private sealed class AccountState(FinancialAccount account)
    {
        public FinancialAccount Account { get; } = account;
        public FinancialAccountRules Rules { get; } = account.Rules ?? new FinancialAccountRules();
        public decimal Cash { get; set; } = account.InitialCash;
        public decimal MarginBalance { get; set; }
        public Dictionary<string, LinkedList<OpenLot>> Lots { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, LinkedList<OpenLot>> ShortLots { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ClosedLot> ClosedLots { get; } = [];
        public Dictionary<string, long> Positions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, decimal> AvgCost { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, decimal> RealizedPnl { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
