using Meridian.Ledger;

namespace Meridian.Backtesting.Portfolio;

/// <summary>
/// Tracks simulated cash, margin, positions, and a typed cash-flow ledger.
/// All mutations are single-threaded (called from the engine replay loop).
/// </summary>
internal sealed class SimulatedPortfolio
{
    private readonly BacktestLedger? _ledger;
    private readonly ICommissionModel _commission;
    private readonly string _defaultBrokerageAccountId;
    private readonly Dictionary<string, AccountState> _accounts;
    private readonly Dictionary<string, decimal> _lastPrices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CashFlowEntry> _cashFlows = [];
    private decimal _prevEquity;

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
        ArgumentNullException.ThrowIfNull(commission);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultBrokerageAccountId);

        _ledger = ledger;
        _commission = commission;
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

    public void UpdateLastPrice(string symbol, decimal price) => _lastPrices[symbol] = price;

    // ── Order fill processing ────────────────────────────────────────────────

    public FillEvent ProcessFill(FillEvent fill)
    {
        ArgumentNullException.ThrowIfNull(fill);
        if (fill.OrderId == Guid.Empty)
        {
            // Administrative executions (for example forced delisting liquidations) do not
            // originate from a brokerage order and must neither incur nor consume order fees.
            return ProcessFill(fill, commissionQuote: null);
        }

        var commissionQuote = _commission.Quote(
            fill.OrderId,
            fill.Symbol,
            fill.FilledQuantity,
            fill.FillPrice);
        return ProcessFill(fill, commissionQuote);
    }

    /// <summary>
    /// Preflights an ordered set of slices for one order, including chained commission quotes,
    /// before committing any portfolio or fee state. This is the all-or-none execution seam for
    /// fill-or-kill orders whose liquidity spans multiple book levels.
    /// </summary>
    public IReadOnlyList<FillEvent> ProcessFillsAtomically(IReadOnlyList<FillEvent> fills)
    {
        ArgumentNullException.ThrowIfNull(fills);
        if (fills.Count == 0)
            return [];

        var first = fills[0];
        ArgumentNullException.ThrowIfNull(first);
        if (first.OrderId == Guid.Empty)
            throw new ArgumentException("Atomic fill batches require a brokerage order identifier.", nameof(fills));
        if (first.FilledQuantity == 0)
            throw new ArgumentException("Atomic fill batches cannot contain zero-quantity slices.", nameof(fills));

        var account = ResolveBrokerageAccount(first.AccountId);
        var accountId = account.Account.AccountId;
        var direction = Math.Sign(first.FilledQuantity);
        var commissionFills = new CommissionFill[fills.Count];
        for (var index = 0; index < fills.Count; index++)
        {
            var fill = fills[index];
            ArgumentNullException.ThrowIfNull(fill);
            if (fill.OrderId != first.OrderId ||
                !fill.Symbol.Equals(first.Symbol, StringComparison.OrdinalIgnoreCase) ||
                Math.Sign(fill.FilledQuantity) != direction)
            {
                throw new ArgumentException(
                    "Atomic fill batches must contain one order, symbol, and trade direction.",
                    nameof(fills));
            }

            var fillAccount = ResolveBrokerageAccount(fill.AccountId);
            if (!fillAccount.Account.AccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Atomic fill batches must resolve to one brokerage account.",
                    nameof(fills));
            }

            commissionFills[index] = new CommissionFill(
                fill.Symbol,
                fill.FilledQuantity,
                fill.FillPrice);
        }

        var commissionQuotes = _commission.QuoteBatch(first.OrderId, commissionFills);
        if (commissionQuotes.Count != fills.Count)
            throw new InvalidOperationException("Commission model returned an incomplete atomic batch quote.");

        var projectedCash = account.Cash;
        var projectedQuantity = (decimal)account.Positions.GetValueOrDefault(first.Symbol);
        for (var index = 0; index < fills.Count; index++)
        {
            var fill = fills[index];
            var quote = commissionQuotes[index];
            ValidateCommissionQuote(fill, quote);

            projectedQuantity += fill.FilledQuantity;
            if (fill.FilledQuantity < 0 && projectedQuantity < 0m && !account.Rules.AllowShortSelling)
                throw new InvalidOperationException($"Account '{accountId}' does not permit short selling.");

            projectedCash -= fill.FilledQuantity * fill.FillPrice + quote.Amount;
            if (projectedCash < 0m && !account.Rules.AllowMargin)
                throw new InvalidOperationException($"Account '{accountId}' does not permit margin borrowing.");
        }

        var accepted = new List<FillEvent>(fills.Count);
        for (var index = 0; index < fills.Count; index++)
            accepted.Add(ProcessFill(fills[index], commissionQuotes[index]));
        return accepted;
    }

    private FillEvent ProcessFill(FillEvent fill, CommissionQuote? commissionQuote)
    {
        if (commissionQuote is not null)
            ValidateCommissionQuote(fill, commissionQuote);
        var account = ResolveBrokerageAccount(fill.AccountId);
        var accountId = account.Account.AccountId;
        var symbol = fill.Symbol;
        var qty = fill.FilledQuantity;
        var price = fill.FillPrice;
        var commission = commissionQuote?.Amount ?? 0m;
        var authoritativeFill = fill with { Commission = commission, AccountId = accountId };

        account.Positions.TryGetValue(symbol, out var existingQty);

        if (qty < 0 && (decimal)existingQty + qty < 0m && !account.Rules.AllowShortSelling)
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
                lots.AddLast(new OpenLot(Guid.NewGuid(), symbol, longBuyQty, price, authoritativeFill.FilledAt, authoritativeFill.FillId, account.Account.AccountId));

            account.AvgCost[symbol] = ComputeAvgCost(account, symbol);
        }
        else if (qty < 0 && existingQty > 0)
        {
            var closeQty = Math.Min(-qty, existingQty);
            realised = RealiseLots(account, symbol, closeQty, price, authoritativeFill.FilledAt, authoritativeFill.FillId, authoritativeFill.TargetLotId);
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

            shortLots.AddLast(new OpenLot(Guid.NewGuid(), symbol, shortOpenQty, price, authoritativeFill.FilledAt, authoritativeFill.FillId, account.Account.AccountId));
        }

        if (qty > 0 && existingQty < 0)
        {
            var coverQty = Math.Min(qty, -existingQty);
            (shortRealised, shortOriginalProceeds) = RealiseShortLots(account, symbol, coverQty, price, authoritativeFill.FilledAt, authoritativeFill.FillId, authoritativeFill.TargetLotId);
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

        var tradeCashImpact = -(qty * price);
        _cashFlows.Add(new TradeCashFlow(authoritativeFill.FilledAt, tradeCashImpact, symbol, qty, price, accountId));

        if (commission > 0)
            _cashFlows.Add(new CommissionCashFlow(authoritativeFill.FilledAt, -commission, symbol, authoritativeFill.OrderId, accountId));

        // Post double-entry journal entries to ledger
        PostFillLedgerEntries(account, authoritativeFill, qty, price, commission, existingQty, realised, costBasisRemoved, shortOpenQty, shortRealised, shortOriginalProceeds);
        CleanupSymbolIfFlat(account, symbol);
        if (commissionQuote is not null)
            _commission.Commit(commissionQuote);
        return authoritativeFill;
    }

    private static void ValidateCommissionQuote(FillEvent fill, CommissionQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        if (quote.OrderId != fill.OrderId ||
            !quote.Symbol.Equals(fill.Symbol, StringComparison.OrdinalIgnoreCase) ||
            quote.Quantity != fill.FilledQuantity ||
            quote.FillPrice != fill.FillPrice ||
            quote.Amount < 0m)
        {
            throw new InvalidOperationException("Commission quote does not match its candidate fill.");
        }
    }

    public void ApplyAssetEvent(AssetEvent assetEvent)
    {
        ArgumentNullException.ThrowIfNull(assetEvent);

        var symbol = assetEvent.Symbol;
        var targetSymbol = assetEvent.DestinationSymbol;
        foreach (var account in _accounts.Values)
        {
            if (account.Account.Kind != FinancialAccountKind.Brokerage)
                continue;

            var existingQty = account.Positions.GetValueOrDefault(symbol);
            if (existingQty == 0)
                continue;

            decimal totalCashImpact = 0m;
            if (assetEvent.HasPositionTransformation)
                totalCashImpact += ApplyPositionTransformation(account, assetEvent, existingQty, targetSymbol);

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
                AccountId = account.Account.AccountId
            });
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
        var ts = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        foreach (var account in _accounts.Values)
        {
            if (account.MarginBalance < 0)
            {
                var interest = account.MarginBalance * (decimal)(account.Rules.AnnualMarginRate / 365.0);
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
                var cashInterest = account.Cash * (decimal)(account.Rules.AnnualCashInterestRate / 365.0);
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
                var rebate = shortNotional * (decimal)(account.Rules.AnnualShortRebateRate / 365.0);
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

    private decimal ApplyPositionTransformation(AccountState account, AssetEvent assetEvent, long existingQty, string targetSymbol)
    {
        var factor = assetEvent.PositionFactor;
        if (factor == 0m)
            throw new InvalidOperationException($"Asset event factor cannot be zero for {assetEvent.Symbol}.");

        var transformedQtyDecimal = existingQty * factor;
        var transformedQty = ConvertToWholeUnits(transformedQtyDecimal);
        var fractionalUnits = transformedQtyDecimal - transformedQty;
        var referencePrice = ResolveReferencePrice(account, assetEvent, existingQty, factor);
        var cashInLieu = fractionalUnits * referencePrice;

        var transformedLongLots = TransformLots(account.Lots.GetValueOrDefault(assetEvent.Symbol), factor);
        var transformedShortLots = TransformLots(account.ShortLots.GetValueOrDefault(assetEvent.Symbol), factor);
        var transformedRealized = account.RealizedPnl.GetValueOrDefault(assetEvent.Symbol);
        var existingTargetRealized = account.RealizedPnl.GetValueOrDefault(targetSymbol);
        var transformedPrice = referencePrice > 0m ? referencePrice : _lastPrices.GetValueOrDefault(assetEvent.Symbol, 0m);

        RemoveSymbolState(account, assetEvent.Symbol);

        if (transformedQty != 0)
        {
            account.Positions[targetSymbol] = account.Positions.GetValueOrDefault(targetSymbol) + transformedQty;
            MergeLots(account.Lots, targetSymbol, transformedLongLots);
            MergeLots(account.ShortLots, targetSymbol, transformedShortLots);
            account.AvgCost[targetSymbol] = ComputeAvgCost(account, targetSymbol);
            account.RealizedPnl[targetSymbol] = existingTargetRealized + transformedRealized;
            if (transformedPrice > 0m)
                _lastPrices[targetSymbol] = transformedPrice;
        }

        if (cashInLieu != 0m)
        {
            account.Cash += cashInLieu;
            PostAssetCashLedgerEntry(account, assetEvent, cashInLieu, existingQty, assetEvent.Symbol, targetSymbol, suffix: "cash in lieu");
        }

        return cashInLieu;
    }

    private decimal ResolveReferencePrice(AccountState account, AssetEvent assetEvent, long existingQty, decimal factor)
    {
        if (assetEvent.ReferencePrice is { } explicitReference && explicitReference > 0m)
            return explicitReference;

        if (_lastPrices.TryGetValue(assetEvent.DestinationSymbol, out var destinationPrice) && destinationPrice > 0m)
            return destinationPrice;

        if (_lastPrices.TryGetValue(assetEvent.Symbol, out var sourcePrice) && sourcePrice > 0m)
            return factor == 0m ? sourcePrice : sourcePrice / Math.Abs(factor);

        var avgCost = account.AvgCost.GetValueOrDefault(assetEvent.Symbol, 0m);
        if (avgCost > 0m)
            return factor == 0m ? avgCost : avgCost / Math.Abs(factor);

        return existingQty == 0 ? 0m : 1m;
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

    private static LinkedList<OpenLot> TransformLots(LinkedList<OpenLot>? source, decimal factor)
    {
        var result = new LinkedList<OpenLot>();
        if (source is null || source.Count == 0)
            return result;

        foreach (var lot in source)
        {
            var transformedQty = ConvertToWholeUnits(lot.Quantity * factor);
            if (transformedQty == 0)
                continue;

            var transformedPrice = factor == 0m ? lot.EntryPrice : lot.EntryPrice / Math.Abs(factor);
            result.AddLast(lot with { Quantity = Math.Abs(transformedQty), EntryPrice = transformedPrice });
        }

        return result;
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
            var unrealised = (lastPrice - avgCost) * qty;
            var realised = account.RealizedPnl.GetValueOrDefault(symbol, 0m);
            IReadOnlyList<OpenLot> openLots;
            if (qty < 0 && account.ShortLots.TryGetValue(symbol, out var shortLots))
                openLots = shortLots.ToList();
            else if (account.Lots.TryGetValue(symbol, out var lots))
                openLots = lots.ToList();
            else
                openLots = Array.Empty<OpenLot>();
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
            totalCost += lot.Quantity * lot.EntryPrice;
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

            realised += quantity * (sellPrice - lot.EntryPrice);
            account.ClosedLots.Add(new ClosedLot(
                lot.LotId, symbol, quantity, lot.EntryPrice, lot.OpenedAt,
                lot.OpenFillId, sellPrice, closedAt, closeFillId, account.Account.AccountId));

            if (slice.ClosesLot)
            {
                lots.Remove(node);
            }
            else
            {
                // Replace lot with reduced quantity, preserve original LotId.
                lots.AddBefore(node, lot with { Quantity = lot.Quantity - quantity });
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
            var lotProceeds = lotClose * lot.EntryPrice;
            realised += lotProceeds - lotClose * coverPrice;
            shortSaleProceeds += lotProceeds;

            account.ClosedLots.Add(new ClosedLot(
                lot.LotId, symbol, lotClose, lot.EntryPrice, lot.OpenedAt,
                lot.OpenFillId, coverPrice, closedAt, closeFillId, account.Account.AccountId, IsShort: true));

            if (slice.ClosesLot)
            {
                lots.Remove(node);
            }
            else
            {
                lots.AddBefore(node, lot with { Quantity = lot.Quantity - lotClose });
                lots.Remove(node);
            }
        }

        return (realised, shortSaleProceeds);
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
            LotSelectionMethod.Hifo => lots.EnumerateNodes().OrderByDescending(n => n.Value.EntryPrice),
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
