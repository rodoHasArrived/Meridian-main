using System.Collections.Concurrent;
using Meridian.Application.SecurityMaster;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Margin;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Ledger;

namespace Meridian.Execution.Services;

/// <summary>
/// Tracks cash, positions, and realised P&amp;L for a paper trading session.
/// Supports both single-account (backward-compatible) and multi-account modes.
/// Optionally posts double-entry journal entries to a <see cref="Ledger"/> after each fill.
/// </summary>
public sealed class PaperTradingPortfolio : IMultiAccountPortfolioState
{
    /// <summary>Key used for the default / backwards-compatible account.</summary>
    public const string DefaultAccountId = "default";

    private readonly Meridian.Ledger.Ledger? _ledger;
    private readonly ILivePositionCorporateActionAdjuster? _corporateActionAdjuster;
    private readonly Lock _lock = new();

    // Multi-account state — the default account always exists at key DefaultAccountId.
    private readonly Dictionary<string, AccountState> _accounts =
        new(StringComparer.OrdinalIgnoreCase);

    // ─── Constructors ────────────────────────────────────────────────────────

    /// <summary>
    /// Single-account initialiser for backwards compatibility.
    /// Creates one brokerage account with id <see cref="DefaultAccountId"/>.
    /// </summary>
    public PaperTradingPortfolio(
        decimal initialCash,
        Meridian.Ledger.Ledger? ledger = null,
        ILivePositionCorporateActionAdjuster? corporateActionAdjuster = null)
    {
        if (initialCash < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCash), "Initial cash must be non-negative.");

        _ledger = ledger;
        _corporateActionAdjuster = corporateActionAdjuster;

        var defaultAccount = new AccountState(DefaultAccountId, "Default Paper Account", AccountKind.Brokerage, initialCash);
        _accounts[DefaultAccountId] = defaultAccount;

        if (ledger is not null && initialCash > 0)
        {
            ledger.PostLines(
                DateTimeOffset.UtcNow,
                "Initial capital — paper trading session",
                [
                    (LedgerAccounts.Cash, initialCash, 0m),
                    (LedgerAccounts.CapitalAccount, 0m, initialCash),
                ]);
        }
    }

    /// <summary>
    /// Multi-account initialiser.  Each entry in <paramref name="accounts"/> is registered
    /// with its own cash balance, kind, and display name.
    /// The first entry becomes the <em>primary</em> account (equivalent of <see cref="DefaultAccountId"/>
    /// for single-account callers).
    /// </summary>
    /// <param name="accounts">At least one account must be supplied.</param>
    /// <param name="ledger">Optional shared ledger.</param>
    /// <param name="corporateActionAdjuster">Optional corporate-action adjuster.</param>
    public PaperTradingPortfolio(
        IReadOnlyList<AccountDefinition> accounts,
        Meridian.Ledger.Ledger? ledger = null,
        ILivePositionCorporateActionAdjuster? corporateActionAdjuster = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        if (accounts.Count == 0)
            throw new ArgumentException("At least one account must be supplied.", nameof(accounts));

        _ledger = ledger;
        _corporateActionAdjuster = corporateActionAdjuster;

        foreach (var def in accounts)
        {
            if (def.InitialCash < 0)
                throw new ArgumentOutOfRangeException(nameof(accounts), $"Account '{def.AccountId}' initial cash must be non-negative.");

        _accounts[def.AccountId] = new AccountState(
            def.AccountId, def.DisplayName, def.Kind, def.InitialCash,
            def.MarginType, def.MarginModel);

            if (ledger is not null && def.InitialCash > 0)
            {
                ledger.PostLines(
                    DateTimeOffset.UtcNow,
                    $"Initial capital — {def.DisplayName}",
                    [
                        (LedgerAccounts.Cash, def.InitialCash, 0m),
                        (LedgerAccounts.CapitalAccount, 0m, def.InitialCash),
                    ]);
            }
        }
    }

    // ─── IPortfolioState (aggregate across all accounts) ────────────────────

    /// <inheritdoc />
    public decimal Cash
    {
        get { lock (_lock) { return _accounts.Values.Sum(static a => a.Cash); } }
    }

    /// <summary>Total margin borrowed across all accounts (0 for all-cash portfolios).</summary>
    public decimal MarginBalance
    {
        get { lock (_lock) { return _accounts.Values.Sum(static a => a.MarginBalance); } }
    }

    /// <inheritdoc />
    public decimal PortfolioValue
    {
        get { lock (_lock) { return _accounts.Values.Sum(static a => a.Cash + a.Positions.Values.Sum(static p => p.MarketValue)); } }
    }

    /// <inheritdoc />
    public decimal UnrealisedPnl
    {
        get { lock (_lock) { return _accounts.Values.Sum(static a => a.Positions.Values.Sum(static p => p.UnrealisedPnl)); } }
    }

    /// <inheritdoc />
    public decimal RealisedPnl
    {
        get { lock (_lock) { return _accounts.Values.Sum(static a => a.RealisedPnl); } }
    }

    /// <inheritdoc />
    /// <remarks>Aggregates positions across all accounts; same-symbol positions are netted.</remarks>
    public IReadOnlyDictionary<string, IPosition> Positions
    {
        get
        {
            lock (_lock)
            {
                var netted = new Dictionary<string, (long qty, decimal costBasis, decimal unrealised, decimal realised)>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var account in _accounts.Values)
                {
                    foreach (var pos in account.Positions.Values)
                    {
                        if (netted.TryGetValue(pos.Symbol, out var existing))
                        {
                            netted[pos.Symbol] = (
                                existing.qty + (long)pos.Quantity,
                                existing.costBasis, // keep first account's cost basis for simplicity
                                existing.unrealised + pos.UnrealisedPnl,
                                existing.realised + account.RealisedPnl);
                        }
                        else
                        {
                            netted[pos.Symbol] = (
                                (long)pos.Quantity,
                                pos.CostBasis,
                                pos.UnrealisedPnl,
                                account.RealisedPnl);
                        }
                    }
                }

                return netted.ToDictionary(
                    static kv => kv.Key,
                    static kv => (IPosition)new ExecutionPosition(kv.Key, kv.Value.qty, kv.Value.costBasis, kv.Value.unrealised, kv.Value.realised),
                    StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    // ─── IMultiAccountPortfolioState ────────────────────────────────────────

    /// <inheritdoc />
    public IReadOnlyList<IAccountPortfolio> Accounts
    {
        get { lock (_lock) { return _accounts.Values.Cast<IAccountPortfolio>().ToArray(); } }
    }

    /// <inheritdoc />
    public IAccountPortfolio? GetAccount(string accountId)
    {
        lock (_lock)
        {
            return _accounts.TryGetValue(accountId, out var acc) ? acc : null;
        }
    }

    /// <inheritdoc />
    public MultiAccountPortfolioSnapshot GetAggregateSnapshot()
    {
        lock (_lock)
        {
            var snapshots = _accounts.Values
                .Select(static a => a.TakeSnapshot())
                .ToArray();

            return MultiAccountPortfolioSnapshot.FromAccounts(snapshots);
        }
    }

    // ─── Per-account snapshot convenience ───────────────────────────────────

    /// <summary>
    /// Returns a detailed snapshot for a single account.
    /// Returns <see langword="null"/> when the account does not exist.
    /// </summary>
    public ExecutionAccountDetailSnapshot? GetAccountSnapshot(string accountId)
    {
        lock (_lock)
        {
            return _accounts.TryGetValue(accountId, out var acc) ? acc.TakeSnapshot() : null;
        }
    }

    /// <summary>
    /// Returns detailed snapshots for all tracked accounts.
    /// </summary>
    public IReadOnlyList<ExecutionAccountDetailSnapshot> GetAllAccountSnapshots()
    {
        lock (_lock)
        {
            return _accounts.Values.Select(static a => a.TakeSnapshot()).ToArray();
        }
    }

    // ─── Fill application ────────────────────────────────────────────────────

    /// <summary>
    /// Applies a fill to the <see cref="DefaultAccountId"/> account (single-account path).
    /// </summary>
    public void ApplyFill(ExecutionReport report) => ApplyFill(DefaultAccountId, report);

    /// <summary>
    /// Applies a fill to a specific <paramref name="accountId"/>.
    /// When the account does not exist it is ignored (no-op).
    /// </summary>
    public void ApplyFill(string accountId, ExecutionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.ReportType is not (ExecutionReportType.Fill or ExecutionReportType.PartialFill))
            return;

        if (report.FillPrice is null || report.FilledQuantity <= 0)
            return;

        var signedQty = report.Side == OrderSide.Buy
            ? report.FilledQuantity
            : -report.FilledQuantity;

        lock (_lock)
        {
            if (!_accounts.TryGetValue(accountId, out var account))
                return;

            ApplyFillToAccount(account, report.Symbol, signedQty, report.FillPrice.Value,
                report.Commission ?? 0m, report.Timestamp, report.OrderId);
        }
    }

    /// <summary>Updates the last-known market price for <paramref name="symbol"/> across all accounts.</summary>
    public void UpdateMarketPrice(string symbol, decimal price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        lock (_lock)
        {
            foreach (var account in _accounts.Values)
            {
                if (account.Positions.TryGetValue(symbol, out var pos))
                    pos.MarketPrice = price;
            }
        }
    }

    /// <summary>Updates the last-known market price for <paramref name="symbol"/> in a specific account.</summary>
    public void UpdateMarketPrice(string accountId, string symbol, decimal price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        lock (_lock)
        {
            if (_accounts.TryGetValue(accountId, out var account)
                && account.Positions.TryGetValue(symbol, out var pos))
            {
                pos.MarketPrice = price;
            }
        }
    }

    /// <summary>
    /// Read-only view of the double-entry ledger for this session.
    /// Returns <see langword="null"/> when no ledger was supplied at construction time.
    /// </summary>
    public IReadOnlyLedger? Ledger => _ledger;

    // ─── Corporate-action adjustment ────────────────────────────────────────

    /// <summary>
    /// Applies any corporate-action adjustments (splits, dividends) to an existing open position
    /// in the default account.  No-op when no adjuster was supplied.
    /// </summary>
    public Task ApplyCorporateActionsAsync(
        string symbol,
        DateTimeOffset positionOpenedAt,
        CancellationToken ct = default)
        => ApplyCorporateActionsAsync(DefaultAccountId, symbol, positionOpenedAt, ct);

    /// <summary>
    /// Applies corporate-action adjustments to a position in a specific <paramref name="accountId"/>.
    /// </summary>
    public async Task ApplyCorporateActionsAsync(
        string accountId,
        string symbol,
        DateTimeOffset positionOpenedAt,
        CancellationToken ct = default)
    {
        if (_corporateActionAdjuster is null || string.IsNullOrWhiteSpace(symbol))
            return;

        PaperPosition? pos;
        decimal originalQuantity;
        decimal originalCostBasis;

        lock (_lock)
        {
            if (!_accounts.TryGetValue(accountId, out var acc)
                || !acc.Positions.TryGetValue(symbol, out pos))
            {
                return;
            }

            originalQuantity = pos.Quantity;
            originalCostBasis = pos.CostBasis;
        }

        var adjustment = await _corporateActionAdjuster
            .AdjustPositionAsync(symbol, originalQuantity, originalCostBasis, positionOpenedAt, ct)
            .ConfigureAwait(false);

        if (adjustment.ActionCount == 0)
            return;

        lock (_lock)
        {
            if (!_accounts.TryGetValue(accountId, out var acc)
                || !acc.Positions.TryGetValue(symbol, out pos))
            {
                return;
            }

            if (pos.Quantity == originalQuantity && pos.CostBasis == originalCostBasis)
            {
                pos.Quantity = adjustment.AdjustedQuantity;
                pos.CostBasis = adjustment.AdjustedCostBasis;
            }
        }
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private void ApplyFillToAccount(
        AccountState account,
        string symbol,
        decimal signedQty,
        decimal price,
        decimal commission,
        DateTimeOffset ts,
        string orderId)
    {
        if (!account.Positions.TryGetValue(symbol, out var pos))
        {
            pos = new PaperPosition(symbol, price);
            account.Positions[symbol] = pos;
        }

        if (signedQty > 0 && pos.Quantity < 0)
            ApplyCoverShort(account, pos, symbol, signedQty, price, commission, ts);
        else if (signedQty > 0)
            ApplyBuy(account, pos, symbol, signedQty, price, commission, ts);
        else if (signedQty < 0 && pos.Quantity > 0)
            ApplySellLong(account, pos, symbol, -signedQty, price, commission, ts);
        else if (signedQty < 0)
            ApplyShortSell(account, pos, symbol, -signedQty, price, commission, ts);

        if (commission > 0)
            PostCommissionEntry(symbol, commission, ts);

        pos.MarketPrice = price;

        if (pos.Quantity == 0m)
            account.Positions.Remove(symbol);
    }

    private void ApplyBuy(
        AccountState account,
        PaperPosition pos,
        string symbol,
        decimal qty,
        decimal price,
        decimal commission,
        DateTimeOffset ts)
    {
        var notional = qty * price;
        var newQty = pos.Quantity + qty;
        pos.CostBasis = newQty == 0m ? 0m : (pos.CostBasis * pos.Quantity + notional) / newQty;
        pos.Quantity = newQty;

        if (account.MarginModel is RegTMarginModel regt)
        {
            // Reg T: the trader funds only the initial margin (50 %); the broker loans the rest.
            var cashRequired = notional * regt.LongInitialRate;
            var borrowed = notional - cashRequired;
            account.Cash -= cashRequired + commission;
            pos.MarginBorrowed += borrowed;
        }
        else
        {
            // Cash account: full notional is deducted from cash.
            account.Cash -= notional + commission;
        }

        _ledger?.PostLines(ts, $"Buy {qty} {symbol} @ {price:F4}",
        [
            (LedgerAccounts.Securities(symbol), notional, 0m),
            (LedgerAccounts.Cash, 0m, notional),
        ]);
    }

    private void ApplyCoverShort(
        AccountState account,
        PaperPosition pos,
        string symbol,
        decimal qty,
        decimal price,
        decimal commission,
        DateTimeOffset ts)
    {
        var coverQty = Math.Min(qty, Math.Abs(pos.Quantity));
        var proceedsRemoved = coverQty * pos.CostBasis;
        var coverCost = coverQty * price;
        var realised = proceedsRemoved - coverCost;

        pos.Quantity += coverQty;
        if (pos.Quantity == 0m) pos.CostBasis = 0m;

        account.Cash -= coverCost + commission;
        account.RealisedPnl += realised;

        if (_ledger is not null)
            PostCoverShortEntry(symbol, coverQty, price, proceedsRemoved, realised, ts);
    }

    private void ApplySellLong(
        AccountState account,
        PaperPosition pos,
        string symbol,
        decimal qty,
        decimal price,
        decimal commission,
        DateTimeOffset ts)
    {
        var closeQty = Math.Min(qty, pos.Quantity);
        var costBasisRemoved = closeQty * pos.CostBasis;
        var proceeds = closeQty * price;
        var realised = proceeds - costBasisRemoved;

        // For margin positions, proportionally repay the broker loan before crediting cash.
        var loanRepaid = 0m;
        if (pos.MarginBorrowed > 0m && pos.Quantity > 0m)
        {
            var closingRatio = closeQty / pos.Quantity;
            loanRepaid = pos.MarginBorrowed * closingRatio;
            pos.MarginBorrowed -= loanRepaid;
        }

        pos.Quantity -= closeQty;
        if (pos.Quantity == 0m) pos.CostBasis = 0m;

        // The trader receives proceeds minus the loan repayment.
        account.Cash += proceeds - loanRepaid - commission;
        account.RealisedPnl += realised;

        if (_ledger is not null)
            PostSellLongEntry(symbol, closeQty, price, costBasisRemoved, realised, ts);
    }

    private void ApplyShortSell(
        AccountState account,
        PaperPosition pos,
        string symbol,
        decimal qty,
        decimal price,
        decimal commission,
        DateTimeOffset ts)
    {
        var proceeds = qty * price;
        var newQty = pos.Quantity - qty;
        pos.CostBasis = newQty == 0m
            ? 0m
            : (pos.CostBasis * Math.Abs(pos.Quantity) + proceeds) / Math.Abs(newQty);
        pos.Quantity = newQty;
        account.Cash += proceeds - commission;

        _ledger?.PostLines(ts, $"Short sell {qty} {symbol} @ {price:F4}",
        [
            (LedgerAccounts.Cash, proceeds, 0m),
            (LedgerAccounts.ShortSecuritiesPayable(symbol), 0m, proceeds),
        ]);
    }

    private void PostSellLongEntry(string symbol, decimal closeQty, decimal price,
        decimal costBasisRemoved, decimal realised, DateTimeOffset ts)
    {
        var proceeds = closeQty * price;
        var description = $"Sell {closeQty} {symbol} @ {price:F4}";
        if (realised > 0)
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.Cash, proceeds, 0m),
                (LedgerAccounts.Securities(symbol), 0m, costBasisRemoved),
                (LedgerAccounts.RealizedGain, 0m, realised),
            ]);
        else if (realised < 0)
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.Cash, proceeds, 0m),
                (LedgerAccounts.RealizedLoss, Math.Abs(realised), 0m),
                (LedgerAccounts.Securities(symbol), 0m, costBasisRemoved),
            ]);
        else
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.Cash, proceeds, 0m),
                (LedgerAccounts.Securities(symbol), 0m, costBasisRemoved),
            ]);
    }

    private void PostCoverShortEntry(string symbol, decimal coverQty, decimal price,
        decimal proceedsRemoved, decimal realised, DateTimeOffset ts)
    {
        var coverCost = coverQty * price;
        var description = $"Cover {coverQty} {symbol} @ {price:F4}";
        if (realised > 0m)
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.ShortSecuritiesPayable(symbol), proceedsRemoved, 0m),
                (LedgerAccounts.Cash, 0m, coverCost),
                (LedgerAccounts.RealizedGain, 0m, realised),
            ]);
        else if (realised < 0m)
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.ShortSecuritiesPayable(symbol), proceedsRemoved, 0m),
                (LedgerAccounts.RealizedLoss, Math.Abs(realised), 0m),
                (LedgerAccounts.Cash, 0m, coverCost),
            ]);
        else
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.ShortSecuritiesPayable(symbol), proceedsRemoved, 0m),
                (LedgerAccounts.Cash, 0m, coverCost),
            ]);
    }

    private void PostCommissionEntry(string symbol, decimal commission, DateTimeOffset ts)
    {
        _ledger?.PostLines(ts, $"Commission — {symbol}",
        [
            (LedgerAccounts.CommissionExpense, commission, 0m),
            (LedgerAccounts.Cash, 0m, commission),
        ]);
    }
}

// ─── Supporting types ───────────────────────────────────────────────────────

/// <summary>
/// Defines one account within a multi-account paper trading session.
/// </summary>
/// <param name="AccountId">Unique account identifier.</param>
/// <param name="DisplayName">Human-readable name.</param>
/// <param name="Kind">Brokerage or Bank.</param>
/// <param name="InitialCash">Opening cash balance.</param>
/// <param name="MarginType">
///   Margin regime for this account.
///   Defaults to <see cref="MarginAccountType.Cash"/> (no borrowing).
/// </param>
/// <param name="MarginModel">
///   Optional explicit margin model. When <see langword="null"/> a default model is
///   created automatically for <see cref="MarginAccountType.RegT"/> and
///   <see cref="MarginAccountType.PortfolioMargin"/> accounts.
/// </param>
public sealed record AccountDefinition(
    string AccountId,
    string DisplayName,
    AccountKind Kind,
    decimal InitialCash,
    MarginAccountType MarginType = MarginAccountType.Cash,
    IMarginModel? MarginModel = null);

/// <summary>
/// Mutable per-account state maintained by <see cref="PaperTradingPortfolio"/>.
/// Also implements <see cref="IAccountPortfolio"/> for the read-only public surface.
/// </summary>
/// <summary>
/// Mutable per-account state maintained by <see cref="PaperTradingPortfolio"/>.
/// Also implements <see cref="IAccountPortfolio"/> for the read-only public surface.
/// </summary>
internal sealed class AccountState : IAccountPortfolio
{
    public string AccountId { get; }
    public string DisplayName { get; }
    public AccountKind Kind { get; }
    public decimal Cash { get; set; }
    public decimal RealisedPnl { get; set; }
    public MarginAccountType MarginType { get; }

    /// <summary>Active margin model; <see langword="null"/> for cash accounts.</summary>
    public IMarginModel? MarginModel { get; }

    public Dictionary<string, PaperPosition> Positions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public AccountState(
        string accountId,
        string displayName,
        AccountKind kind,
        decimal cash,
        MarginAccountType marginType = MarginAccountType.Cash,
        IMarginModel? marginModel = null)
    {
        AccountId = accountId;
        DisplayName = displayName;
        Kind = kind;
        Cash = cash;
        MarginType = marginType;
        MarginModel = marginModel ?? marginType switch
        {
            MarginAccountType.RegT => new RegTMarginModel(),
            MarginAccountType.PortfolioMargin => new PortfolioMarginModel(),
            _ => null,
        };
    }

    // IAccountPortfolio explicit implementation (read-only projection)
    IReadOnlyDictionary<string, IPosition> IAccountPortfolio.Positions =>
        Positions.ToDictionary(
            static kv => kv.Key,
            static kv => (IPosition)kv.Value.ToExecutionPosition(),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Total amount borrowed from the broker across all open positions.
    /// Zero for cash accounts.
    /// </summary>
    public decimal MarginBalance => Positions.Values.Sum(static p => p.MarginBorrowed);

    public decimal UnrealisedPnl => Positions.Values.Sum(static p => p.UnrealisedPnl);

    public decimal LongMarketValue => Positions.Values
        .Where(static p => p.Quantity > 0)
        .Sum(static p => p.MarketValue);

    public decimal ShortMarketValue => Positions.Values
        .Where(static p => p.Quantity < 0)
        .Sum(static p => p.MarketValue);

    /// <summary>
    /// Available buying power based on the margin regime.
    /// <list type="bullet">
    ///   <item>Cash account: equals <see cref="Cash"/>.</item>
    ///   <item>Reg T: up to 2× cash equity (50 % initial margin rate).</item>
    ///   <item>Portfolio margin: model-specific; defaults to <see cref="Cash"/>.</item>
    /// </list>
    /// </summary>
    public decimal BuyingPower =>
        MarginModel is RegTMarginModel regt && regt.LongInitialRate > 0m
            ? Cash / regt.LongInitialRate
            : Cash;

    public ExecutionAccountDetailSnapshot TakeSnapshot()
    {
        var positionList = Positions.Values
            .Select(static p => p.ToExecutionPosition())
            .ToArray();

        var longMv = LongMarketValue;
        var shortMv = ShortMarketValue;

        return new ExecutionAccountDetailSnapshot(
            AccountId: AccountId,
            DisplayName: DisplayName,
            Kind: Kind,
            Cash: Cash,
            MarginBalance: MarginBalance,
            LongMarketValue: longMv,
            ShortMarketValue: shortMv,
            GrossExposure: longMv + shortMv,
            NetExposure: longMv - shortMv,
            UnrealisedPnl: UnrealisedPnl,
            RealisedPnl: RealisedPnl,
            Positions: positionList,
            AsOf: DateTimeOffset.UtcNow,
            MarginType: MarginType,
            BuyingPower: BuyingPower);
    }
}

/// <summary>
/// Mutable position state used internally by <see cref="PaperTradingPortfolio"/>.
/// </summary>
internal sealed class PaperPosition(string symbol, decimal marketPrice = 0m)
{
    public string Symbol { get; } = symbol;
    public decimal Quantity { get; set; }
    public decimal CostBasis { get; set; }
    public decimal MarketPrice { get; set; } = marketPrice;

    /// <summary>
    /// Amount borrowed from the broker to fund this position.
    /// Non-zero only for margin accounts; zero for cash accounts and short positions.
    /// </summary>
    public decimal MarginBorrowed { get; set; }

    public decimal MarketValue => Math.Abs(Quantity) * MarketPrice;

    public decimal UnrealisedPnl => Quantity > 0
        ? (MarketPrice - CostBasis) * Quantity
        : 0m; // short unrealised P&L tracked separately

    public ExecutionPosition ToExecutionPosition() => new(
        Symbol,
        (long)Quantity,
        CostBasis,
        UnrealisedPnl,
        0m);  // realised P&L is carried at the account level
}
