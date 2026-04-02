using Meridian.Application.SecurityMaster;
using Meridian.Backtesting.Sdk;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Ledger;

namespace Meridian.Execution.Services;

/// <summary>
/// Tracks cash, positions, and realised P&amp;L for a paper trading session.
/// Optionally posts double-entry journal entries to a <see cref="Ledger"/> after each fill.
/// </summary>
public sealed class PaperTradingPortfolio : IPortfolioState
{
    private readonly Meridian.Ledger.Ledger? _ledger;
    private readonly ILivePositionCorporateActionAdjuster? _corporateActionAdjuster;
    private readonly Lock _lock = new();
    private decimal _cash;
    private decimal _realisedPnl;
    private readonly Dictionary<string, PaperPosition> _positions =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initialises the portfolio with <paramref name="initialCash"/> and an optional ledger.
    /// If a ledger is provided, an opening capital entry is posted immediately.
    /// </summary>
    public PaperTradingPortfolio(
        decimal initialCash,
        Meridian.Ledger.Ledger? ledger = null,
        ILivePositionCorporateActionAdjuster? corporateActionAdjuster = null)
    {
        if (initialCash < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCash), "Initial cash must be non-negative.");
        }

        _cash = initialCash;
        _ledger = ledger;
        _corporateActionAdjuster = corporateActionAdjuster;

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

    /// <inheritdoc />
    public decimal Cash
    {
        get { lock (_lock) { return _cash; } }
    }

    /// <inheritdoc />
    public decimal PortfolioValue
    {
        get { lock (_lock) { return _cash + _positions.Values.Sum(static p => p.MarketValue); } }
    }

    /// <inheritdoc />
    public decimal UnrealisedPnl
    {
        get { lock (_lock) { return _positions.Values.Sum(static p => p.UnrealisedPnl); } }
    }

    /// <inheritdoc />
    public decimal RealisedPnl
    {
        get { lock (_lock) { return _realisedPnl; } }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ExecutionPosition> Positions
    {
        get
        {
            lock (_lock)
            {
                return _positions.Values.ToDictionary(
                    static p => p.Symbol,
                    static p => p.ToExecutionPosition(),
                    StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>All currently open lots across every position in this session.</summary>
    public IReadOnlyList<OpenLot> OpenLots
    {
        get
        {
            lock (_lock)
            {
                return _positions.Values.SelectMany(static p => p.Lots).ToArray();
            }
        }
    }

    /// <summary>All lots that have been closed since the session began.</summary>
    public IReadOnlyList<ClosedLot> ClosedLots
    {
        get
        {
            lock (_lock)
            {
                return _positions.Values.SelectMany(static p => p.ClosedLots).ToArray();
            }
        }
    }

    /// <summary>
    /// Read-only view of the double-entry ledger for this session.
    /// Returns <see langword="null"/> when no ledger was supplied at construction time.
    /// </summary>
    public IReadOnlyLedger? Ledger => _ledger;

    /// <summary>
    /// Updates portfolio state from a fill or partial-fill execution report.
    /// No-ops for non-fill reports (accepted, cancelled, rejected).
    /// </summary>
    public void ApplyFill(ExecutionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.ReportType is not (ExecutionReportType.Fill or ExecutionReportType.PartialFill))
        {
            return;
        }

        if (report.FillPrice is null || report.FilledQuantity <= 0)
        {
            return;
        }

        var signedQty = report.Side == OrderSide.Buy
            ? report.FilledQuantity
            : -report.FilledQuantity;

        lock (_lock)
        {
            ApplyFillInternal(report.Symbol, signedQty, report.FillPrice.Value,
                report.Commission ?? 0m, report.Timestamp, report.OrderId);
        }
    }

    /// <summary>Updates the last-known market price for <paramref name="symbol"/> so that
    /// unrealised P&amp;L and portfolio value remain current between fills.</summary>
    public void UpdateMarketPrice(string symbol, decimal price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        lock (_lock)
        {
            if (_positions.TryGetValue(symbol, out var pos))
            {
                pos.MarketPrice = price;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void ApplyFillInternal(
        string symbol,
        decimal signedQty,
        decimal price,
        decimal commission,
        DateTimeOffset ts,
        string orderId)
    {
        if (!_positions.TryGetValue(symbol, out var pos))
        {
            pos = new PaperPosition(symbol, price);
            _positions[symbol] = pos;
        }

        if (signedQty > 0 && pos.Quantity < 0)
        {
            ApplyCoverShort(pos, symbol, signedQty, price, commission, ts);
        }
        else if (signedQty > 0)
        {
            ApplyBuy(pos, symbol, signedQty, price, commission, ts, orderId);
        }
        else if (signedQty < 0 && pos.Quantity > 0)
        {
            ApplySellLong(pos, symbol, -signedQty, price, commission, ts, orderId);
        }
        else if (signedQty < 0)
        {
            ApplyShortSell(pos, symbol, -signedQty, price, commission, ts, orderId);
        }

        if (commission > 0)
        {
            PostCommissionEntry(symbol, commission, ts);
        }

        pos.MarketPrice = price;
        CleanUpFlatPosition(symbol, pos);
    }

    private void ApplyBuy(
        PaperPosition pos,
        string symbol,
        decimal qty,
        decimal price,
        decimal commission,
        DateTimeOffset ts,
        string orderId)
    {
        var notional = qty * price;
        var newQty = pos.Quantity + qty;
        pos.CostBasis = newQty == 0m
            ? 0m
            : (pos.CostBasis * pos.Quantity + notional) / newQty;
        pos.Quantity = newQty;
        _cash -= notional + commission;

        // Append a new open lot for lot-level tracking.
        pos.Lots.AddLast(new OpenLot(
            LotId: Guid.NewGuid(),
            Symbol: symbol,
            Quantity: (long)qty,
            EntryPrice: price,
            OpenedAt: ts,
            OpenFillId: Guid.NewGuid()));

        if (_ledger is not null)
        {
            _ledger.PostLines(
                ts,
                $"Buy {qty} {symbol} @ {price:F4}",
                [
                    (LedgerAccounts.Securities(symbol), notional, 0m),
                    (LedgerAccounts.Cash, 0m, notional),
                ]);
        }
    }

    private void ApplyCoverShort(
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
        if (pos.Quantity == 0m)
        {
            pos.CostBasis = 0m;
        }

        _cash -= coverCost + commission;
        _realisedPnl += realised;

        if (_ledger is null)
        {
            return;
        }

        PostCoverShortEntry(symbol, coverQty, price, proceedsRemoved, realised, ts);
    }

    private void ApplySellLong(
        PaperPosition pos,
        string symbol,
        decimal qty,
        decimal price,
        decimal commission,
        DateTimeOffset ts,
        string orderId)
    {
        var closeQty = Math.Min(qty, pos.Quantity);
        var costBasisRemoved = closeQty * pos.CostBasis;
        var proceeds = closeQty * price;
        var realised = proceeds - costBasisRemoved;

        pos.Quantity -= closeQty;
        if (pos.Quantity == 0m)
        {
            pos.CostBasis = 0m;
        }

        _cash += proceeds - commission;
        _realisedPnl += realised;

        // FIFO lot-level close: drain from the front of the lot queue.
        var closeFillId = Guid.NewGuid();
        var remaining = (long)closeQty;
        while (remaining > 0 && pos.Lots.Count > 0)
        {
            var node = pos.Lots.First!;
            var lot = node.Value;
            var lotClose = Math.Min(lot.Quantity, remaining);

            pos.ClosedLots.Add(new ClosedLot(
                LotId: lot.LotId,
                Symbol: lot.Symbol,
                Quantity: lotClose,
                EntryPrice: lot.EntryPrice,
                OpenedAt: lot.OpenedAt,
                OpenFillId: lot.OpenFillId,
                ClosePrice: price,
                ClosedAt: ts,
                CloseFillId: closeFillId));

            pos.Lots.RemoveFirst();
            if (lot.Quantity > remaining)
            {
                // Partial close: add back the reduced lot.
                pos.Lots.AddFirst(lot with { Quantity = lot.Quantity - remaining });
                remaining = 0;
            }
            else
            {
                remaining -= lot.Quantity;
            }
        }

        if (_ledger is not null)
        {
            PostSellLongEntry(symbol, closeQty, price, costBasisRemoved, realised, ts);
        }
    }

    private void ApplyShortSell(
        PaperPosition pos,
        string symbol,
        decimal qty,
        decimal price,
        decimal commission,
        DateTimeOffset ts,
        string orderId)
    {
        var proceeds = qty * price;
        var newQty = pos.Quantity - qty; // goes negative
        pos.CostBasis = newQty == 0m
            ? 0m
            : (pos.CostBasis * Math.Abs(pos.Quantity) + proceeds) / Math.Abs(newQty);
        pos.Quantity = newQty;
        _cash += proceeds - commission;

        if (_ledger is not null)
        {
            _ledger.PostLines(
                ts,
                $"Short sell {qty} {symbol} @ {price:F4}",
                [
                    (LedgerAccounts.Cash, proceeds, 0m),
                    (LedgerAccounts.ShortSecuritiesPayable(symbol), 0m, proceeds),
                ]);
        }
    }

    private void PostSellLongEntry(
        string symbol,
        decimal closeQty,
        decimal price,
        decimal costBasisRemoved,
        decimal realised,
        DateTimeOffset ts)
    {
        var proceeds = closeQty * price;
        var description = $"Sell {closeQty} {symbol} @ {price:F4}";

        if (realised > 0)
        {
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.Cash, proceeds, 0m),
                (LedgerAccounts.Securities(symbol), 0m, costBasisRemoved),
                (LedgerAccounts.RealizedGain, 0m, realised),
            ]);
        }
        else if (realised < 0)
        {
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.Cash, proceeds, 0m),
                (LedgerAccounts.RealizedLoss, Math.Abs(realised), 0m),
                (LedgerAccounts.Securities(symbol), 0m, costBasisRemoved),
            ]);
        }
        else
        {
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.Cash, proceeds, 0m),
                (LedgerAccounts.Securities(symbol), 0m, costBasisRemoved),
            ]);
        }
    }

    private void PostCoverShortEntry(
        string symbol,
        decimal coverQty,
        decimal price,
        decimal proceedsRemoved,
        decimal realised,
        DateTimeOffset ts)
    {
        var coverCost = coverQty * price;
        var description = $"Cover {coverQty} {symbol} @ {price:F4}";
        if (realised > 0m)
        {
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.ShortSecuritiesPayable(symbol), proceedsRemoved, 0m),
                (LedgerAccounts.Cash, 0m, coverCost),
                (LedgerAccounts.RealizedGain, 0m, realised),
            ]);
        }
        else if (realised < 0m)
        {
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.ShortSecuritiesPayable(symbol), proceedsRemoved, 0m),
                (LedgerAccounts.RealizedLoss, Math.Abs(realised), 0m),
                (LedgerAccounts.Cash, 0m, coverCost),
            ]);
        }
        else
        {
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.ShortSecuritiesPayable(symbol), proceedsRemoved, 0m),
                (LedgerAccounts.Cash, 0m, coverCost),
            ]);
        }
    }

    private void PostCommissionEntry(string symbol, decimal commission, DateTimeOffset ts)
    {
        _ledger?.PostLines(
            ts,
            $"Commission — {symbol}",
            [
                (LedgerAccounts.CommissionExpense, commission, 0m),
                (LedgerAccounts.Cash, 0m, commission),
            ]);
    }

    private void CleanUpFlatPosition(string symbol, PaperPosition pos)
    {
        if (pos.Quantity == 0m)
        {
            _positions.Remove(symbol);
        }
    }

    /// <summary>
    /// Applies any corporate-action adjustments (splits, dividends) that occurred between
    /// <paramref name="positionOpenedAt"/> and the current time to an existing open position
    /// identified by <paramref name="symbol"/>.
    /// <para>
    /// This method is a no-op when no <see cref="ILivePositionCorporateActionAdjuster"/> was
    /// supplied to the constructor, or when the symbol is not currently held.
    /// </para>
    /// </summary>
    /// <param name="symbol">Ticker symbol of the position to adjust.</param>
    /// <param name="positionOpenedAt">When the position was first opened (UTC).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ApplyCorporateActionsAsync(
        string symbol,
        DateTimeOffset positionOpenedAt,
        CancellationToken ct = default)
    {
        if (_corporateActionAdjuster is null || string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        PaperPosition? pos;
        decimal originalQuantity;
        decimal originalCostBasis;

        lock (_lock)
        {
            if (!_positions.TryGetValue(symbol, out pos))
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
        {
            return;
        }

        lock (_lock)
        {
            if (!_positions.TryGetValue(symbol, out pos))
            {
                return; // Position closed between the async call and the lock
            }

            // Only update when the position hasn't changed under our feet
            if (pos.Quantity == originalQuantity && pos.CostBasis == originalCostBasis)
            {
                pos.Quantity = adjustment.AdjustedQuantity;
                pos.CostBasis = adjustment.AdjustedCostBasis;
            }
        }
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

    /// <summary>FIFO queue of open lots for this position.</summary>
    public LinkedList<OpenLot> Lots { get; } = new();

    /// <summary>Append-only closed lot ledger for this position.</summary>
    public List<ClosedLot> ClosedLots { get; } = [];

    public decimal MarketValue => Math.Abs(Quantity) * MarketPrice;

    public decimal UnrealisedPnl => Quantity > 0
        ? (MarketPrice - CostBasis) * Quantity
        : 0m; // short unrealised tracked separately

    public ExecutionPosition ToExecutionPosition() => new(
        Symbol,
        (long)Quantity,
        CostBasis,
        UnrealisedPnl,
        0m);  // realised P&L is carried at the portfolio level
}
