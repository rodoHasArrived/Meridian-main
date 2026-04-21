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

    public void UpdateLastPrice(string symbol, decimal price) => _lastPrices[symbol] = price;

    // ── Order fill processing ────────────────────────────────────────────────

    public void ProcessFill(FillEvent fill)
    {
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
            {
                lots.AddLast(new OpenLot(
                    LotId: Guid.NewGuid(),
                    Symbol: symbol,
                    Quantity: longBuyQty,
                    EntryPrice: price,
                    OpenedAt: fill.FilledAt,
                    OpenFillId: fill.FillId,
                    AccountId: accountId));
            }

            account.AvgCost[symbol] = ComputeAvgCost(account, symbol);
        }
        else if (qty < 0 && existingQty > 0)
        {
            var closeQty = Math.Min(-qty, existingQty);
            realised = RealiseFifo(account, symbol, closeQty, price, fill.FilledAt, fill.FillId, fill.TargetLotId);
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
                LotId: Guid.NewGuid(),
                Symbol: symbol,
                Quantity: shortOpenQty,
                EntryPrice: price,
                OpenedAt: fill.FilledAt,
                OpenFillId: fill.FillId,
                AccountId: accountId));
        }

        if (qty > 0 && existingQty < 0)
        {
            var coverQty = Math.Min(qty, -existingQty);
            (shortRealised, shortOriginalProceeds) = RealiseShortFifo(account, symbol, coverQty, price, fill.FilledAt, fill.FillId, fill.TargetLotId);
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

        var account = ResolveBrokerageAccount(null);
        var symbol = assetEvent.Symbol;
        var targetSymbol = assetEvent.DestinationSymbol;
        var existingQty = account.Positions.GetValueOrDefault(symbol);
        var impactedUnits = existingQty;
        decimal totalCashImpact = 0m;

        if (assetEvent.HasPositionTransformation && existingQty != 0)
        {
            totalCashImpact += ApplyPositionTransformation(account, assetEvent, existingQty, targetSymbol);
        }

        if (assetEvent.CashPerShare != 0m && existingQty != 0)
        {
            totalCashImpact += ApplyPerShareCashAdjustment(account, assetEvent, existingQty);
        }

        if (account.Cash < 0)
            account.MarginBalance = Math.Min(account.MarginBalance, account.Cash);

        _cashFlows.Add(new AssetEventCashFlow(
            assetEvent.EffectiveAt,
            totalCashImpact,
            symbol,
            assetEvent.EventType,
            impactedUnits,
            assetEvent.CashPerShare,
            assetEvent.TargetSymbol,
            assetEvent.PositionFactor,
            assetEvent.Description));
    }

    // ── Day-end accruals ─────────────────────────────────────────────────────

    public void AccrueDailyInterest(DateOnly date)
    {
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

        var openLots = _accounts.Values
            .SelectMany(a => a.Lots.Values.SelectMany(static ll => ll)
                .Concat(a.ShortLots.Values.SelectMany(static ll => ll)))
            .ToArray();

        var closedLotsSinceLastSnapshot = new List<ClosedLot>();
        foreach (var account in _accounts.Values)
        {
            closedLotsSinceLastSnapshot.AddRange(account.ClosedLots);
            account.ClosedLots.Clear();
        }

        var closedLots = closedLotsSinceLastSnapshot.ToArray();
        return new PortfolioSnapshot(timestamp, date, Cash, MarginBalance, longMv, shortMv, equity, dailyReturn, positions, accountSnapshots, dayCashFlows, openLots, closedLots);
    }

    public decimal ComputeCurrentEquity() => BuildAccountSnapshots().Values.Sum(snapshot => snapshot.Equity);

    public IReadOnlyDictionary<string, Position> GetCurrentPositions() => BuildAggregatePositions();

    public IReadOnlyDictionary<string, FinancialAccountSnapshot> GetAccountSnapshots() => BuildAccountSnapshots();

    /// <summary>
    /// Returns all closed lots accumulated across every account since portfolio creation.
    /// Suitable for populating <see cref="BacktestResult.AllClosedLots"/> at run completion.
    /// </summary>
    public IReadOnlyList<ClosedLot> GetAllClosedLots() =>
        _accounts.Values.SelectMany(static a => a.ClosedLots).ToArray();

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
            // Preserve LotId and OpenedAt: splits/mergers do not restart the holding-period clock.
            result.AddLast(lot with
            {
                Quantity = Math.Abs(transformedQty),
                EntryPrice = transformedPrice,
            });
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
        _lastPrices.Remove(symbol);
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
            result[symbol] = new Position(symbol, totalQty, avgCost, unrealised, realised);
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
                    account.Rules);
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
            result[symbol] = new Position(symbol, qty, avgCost, unrealised, realised);
        }

        return result;
    }

    private static decimal ComputeAvgCost(AccountState account, string symbol)
    {
        if (!account.Lots.TryGetValue(symbol, out var lots) || lots.Count == 0)
            return 0m;

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
    /// Emits <see cref="ClosedLot"/> records into <paramref name="account"/>'s closed-lot ledger.
    /// <para>
    /// NOTE: This must stay consistent with <c>BacktestMetricsEngine.ComputeRealisedPnl</c>,
    /// which re-implements the same FIFO logic for attribution. If you change this method,
    /// update the metrics counterpart in parallel.
    /// </para>
    /// </summary>
    private static decimal RealiseFifo(
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

        var method = account.Rules.LotSelection;
        var orderedLots = OrderLotsForSelection(lots, method, targetLotId);

        var realised = 0m;
        var remaining = closeQty;

        foreach (var node in orderedLots)
        {
            if (remaining <= 0)
                break;

            var lot = node.Value;
            var lotClose = Math.Min(lot.Quantity, remaining);
            realised += lotClose * (sellPrice - lot.EntryPrice);

            account.ClosedLots.Add(new ClosedLot(
                LotId: lot.LotId,
                Symbol: lot.Symbol,
                Quantity: lotClose,
                EntryPrice: lot.EntryPrice,
                OpenedAt: lot.OpenedAt,
                OpenFillId: lot.OpenFillId,
                ClosePrice: sellPrice,
                ClosedAt: closedAt,
                CloseFillId: closeFillId,
                AccountId: lot.AccountId));

            if (lot.Quantity <= remaining)
            {
                remaining -= lot.Quantity;
                lots.Remove(node);
            }
            else
            {
                // Partial close: reduce the lot in-place while preserving its list position.
                node.Value = lot with { Quantity = lot.Quantity - remaining };
                remaining = 0;
            }
        }

        account.AvgCost[symbol] = ComputeAvgCost(account, symbol);
        return realised;
    }

    private static (decimal realised, decimal shortSaleProceeds) RealiseShortFifo(
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

        var method = account.Rules.LotSelection;
        var orderedLots = OrderLotsForSelection(lots, method, targetLotId);

        var realised = 0m;
        var shortSaleProceeds = 0m;
        var remaining = coverQty;

        foreach (var node in orderedLots)
        {
            if (remaining <= 0)
                break;

            var lot = node.Value;
            var lotClose = Math.Min(lot.Quantity, remaining);
            var lotProceeds = lotClose * lot.EntryPrice;
            realised += lotProceeds - lotClose * coverPrice;
            shortSaleProceeds += lotProceeds;

            // For short lots the "realised P&L" direction is inverted: we record as a
            // ClosedLot with ClosePrice = coverPrice and EntryPrice = shortSalePrice.
            // RealizedPnl on the record computes (coverPrice − entryPrice) × qty,
            // which would be inverted for shorts, so we negate: store entryPrice as
            // the short-sale price and closePrice as coverPrice; the caller negates sign.
            account.ClosedLots.Add(new ClosedLot(
                LotId: lot.LotId,
                Symbol: lot.Symbol,
                Quantity: lotClose,
                EntryPrice: lot.EntryPrice,    // short-sale price
                OpenedAt: lot.OpenedAt,
                OpenFillId: lot.OpenFillId,
                ClosePrice: coverPrice,        // cover price
                ClosedAt: closedAt,
                CloseFillId: closeFillId,
                AccountId: lot.AccountId));

            if (lot.Quantity <= remaining)
            {
                remaining -= lot.Quantity;
                lots.Remove(node);
            }
            else
            {
                lots.Remove(node);
                if (method == LotSelectionMethod.Lifo)
                    lots.AddLast(lot with { Quantity = lot.Quantity - remaining });
                else
                    lots.AddFirst(lot with { Quantity = lot.Quantity - remaining });
                remaining = 0;
            }
        }

        return (realised, shortSaleProceeds);
    }

    /// <summary>
    /// Returns the lot nodes in the order they should be closed according to
    /// <paramref name="method"/>. For <see cref="LotSelectionMethod.SpecificId"/>
    /// the targeted node is returned first, followed by any remaining nodes in FIFO order.
    /// </summary>
    private static IEnumerable<LinkedListNode<OpenLot>> OrderLotsForSelection(
        LinkedList<OpenLot> lots,
        LotSelectionMethod method,
        Guid? targetLotId)
    {
        return method switch
        {
            LotSelectionMethod.Lifo => LotsLastFirst(lots),
            LotSelectionMethod.Hifo => LotsHighCostFirst(lots),
            LotSelectionMethod.SpecificId => LotsSpecificIdFirst(lots, targetLotId),
            _ => LotsFirstFirst(lots),   // Fifo (default)
        };
    }

    private static IEnumerable<LinkedListNode<OpenLot>> LotsFirstFirst(LinkedList<OpenLot> lots)
    {
        var node = lots.First;
        while (node is not null)
        {
            var next = node.Next;   // capture before possible removal
            yield return node;
            node = next;
        }
    }

    private static IEnumerable<LinkedListNode<OpenLot>> LotsLastFirst(LinkedList<OpenLot> lots)
    {
        var node = lots.Last;
        while (node is not null)
        {
            var prev = node.Previous;
            yield return node;
            node = prev;
        }
    }

    private static IEnumerable<LinkedListNode<OpenLot>> LotsHighCostFirst(LinkedList<OpenLot> lots)
    {
        // Materialise for sorting; lot counts are small so allocation is acceptable.
        var nodes = new List<LinkedListNode<OpenLot>>();
        var n = lots.First;
        while (n is not null)
        {
            nodes.Add(n);
            n = n.Next;
        }

        return nodes.OrderByDescending(static node => node.Value.EntryPrice);
    }

    private static IEnumerable<LinkedListNode<OpenLot>> LotsSpecificIdFirst(LinkedList<OpenLot> lots, Guid? targetLotId)
    {
        if (targetLotId.HasValue)
        {
            var n = lots.First;
            while (n is not null)
            {
                if (n.Value.LotId == targetLotId.Value)
                {
                    yield return n;
                    break;
                }

                n = n.Next;
            }
        }

        // Remaining lots in FIFO order.
        foreach (var node in LotsFirstFirst(lots))
        {
            if (!targetLotId.HasValue || node.Value.LotId != targetLotId.Value)
                yield return node;
        }
    }

    private sealed class AccountState(FinancialAccount account)
    {
        public FinancialAccount Account { get; } = account;
        public FinancialAccountRules Rules { get; } = account.Rules ?? new FinancialAccountRules();
        public decimal Cash { get; set; } = account.InitialCash;
        public decimal MarginBalance { get; set; }
        public Dictionary<string, LinkedList<OpenLot>> Lots { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, LinkedList<OpenLot>> ShortLots { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> Positions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, decimal> AvgCost { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, decimal> RealizedPnl { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ClosedLot> ClosedLots { get; } = [];
    }
}
