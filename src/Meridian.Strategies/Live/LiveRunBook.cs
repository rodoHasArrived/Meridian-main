using Meridian.Backtesting.Sdk;
using Meridian.Execution.Models;
using ExecutionSdk = Meridian.Execution.Sdk;

namespace Meridian.Strategies.Live;

/// <summary>
/// The run's own book: the capital it started with, the cash its fills, commissions and
/// financing charges moved, and the positions its fills opened, marked at the feed price.
/// </summary>
/// <remarks>
/// A live or paper run trades against a portfolio shared with the rest of the host (other
/// runs, manual orders, holdings that predate the run), so that portfolio's equity is not the
/// run's equity. This book is the run's equity source for its metrics and snapshots, and,
/// for a run launched fresh, what its strategy sees as cash, portfolio value, positions and
/// accounts. Fills arrive on the session's event loop; lifecycle code may read the book from
/// other threads, so every access takes the book's lock.
/// </remarks>
internal sealed class LiveRunBook
{
    private readonly Dictionary<string, Holding> _holdings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _sync = new();
    private decimal _cash;
    private decimal _financingCharges;
    private readonly string _accountId;
    private readonly string? _institution;

    public LiveRunBook(decimal initialCapital, string accountId, string? institution)
    {
        InitialCapital = initialCapital;
        _cash = initialCapital;
        _accountId = accountId;
        _institution = institution;
    }

    public decimal InitialCapital { get; }

    public decimal Cash
    {
        get { lock (_sync) return _cash; }
    }

    /// <summary>Financing charges paid from the run's cash (positive = cost).</summary>
    public decimal FinancingCharges
    {
        get { lock (_sync) return _financingCharges; }
    }

    /// <param name="fill">The run's fill; a positive quantity buys, a negative one sells.</param>
    /// <param name="valueFactor">
    /// Money per unit of quantity per unit of price: the contract multiplier, or 1/100 for a
    /// face-value fill quoted as a percentage of par. The same factor the metrics tracker uses
    /// for the fill's cash flow, so the book and the cash-flow record agree.
    /// </param>
    public void ApplyFill(FillEvent fill, decimal valueFactor)
    {
        if (fill.FilledQuantity == 0)
            return;

        lock (_sync)
        {
            _cash -= fill.FilledQuantity * fill.FillPrice * valueFactor;
            _cash -= Math.Abs(fill.Commission);

            if (!_holdings.TryGetValue(fill.Symbol, out var holding))
            {
                holding = new Holding(valueFactor);
                _holdings[fill.Symbol] = holding;
            }

            holding.Apply(fill.FilledQuantity, fill.FillPrice);
        }
    }

    /// <summary>Pays a financing charge from the run's cash.</summary>
    public void ApplyFinancingCharge(decimal amount)
    {
        lock (_sync)
        {
            _cash -= amount;
            _financingCharges += amount;
        }
    }

    /// <summary>
    /// Values the book. A symbol the feed has no price for is marked at its last fill price.
    /// </summary>
    public LiveRunBookValuation Value(Func<string, decimal?> markPrice)
    {
        ArgumentNullException.ThrowIfNull(markPrice);
        lock (_sync)
        {
            return ValueCore(markPrice);
        }
    }

    /// <summary>The book as an execution portfolio, valued at <paramref name="markPrice"/> on each read.</summary>
    public IPortfolioState AsPortfolioState(Func<string, decimal?> markPrice) => new PortfolioStateView(this, markPrice);

    private LiveRunBookValuation ValueCore(Func<string, decimal?> markPrice)
    {
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase);
        decimal longMarketValue = 0m, shortMarketValue = 0m;
        foreach (var (symbol, holding) in _holdings)
        {
            var mark = markPrice(symbol) is { } price and > 0m ? price : holding.LastFillPrice;
            var value = holding.Quantity * mark * holding.ValueFactor;
            if (holding.Quantity > 0)
                longMarketValue += value;
            else if (holding.Quantity < 0)
                shortMarketValue += value;

            if (holding.Quantity == 0)
                continue;
            positions[symbol] = new Position(
                symbol,
                holding.Quantity,
                holding.AverageCost,
                holding.Quantity * (mark - holding.AverageCost) * holding.ValueFactor,
                holding.RealizedPnl);
        }

        var cash = _cash;
        var equity = cash + longMarketValue + shortMarketValue;
        var account = new FinancialAccountSnapshot(
            AccountId: _accountId,
            DisplayName: "Run Book",
            Kind: FinancialAccountKind.Brokerage,
            Institution: _institution,
            Cash: cash,
            MarginBalance: Math.Min(0m, cash),
            LongMarketValue: longMarketValue,
            ShortMarketValue: shortMarketValue,
            Equity: equity,
            Positions: positions,
            // What the book proves: borrowing when its cash is negative, shorting when it holds a short.
            Rules: new FinancialAccountRules(
                AllowMargin: cash < 0m,
                AllowShortSelling: _holdings.Values.Any(static h => h.Quantity < 0)));
        return new LiveRunBookValuation(
            cash,
            equity,
            positions,
            new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase) { [_accountId] = account });
    }

    /// <summary>
    /// The book seen through <see cref="IPortfolioState"/>, the portfolio surface strategies
    /// read. Buying power is the book's cash: the run book has no margin model of its own.
    /// </summary>
    private sealed class PortfolioStateView(LiveRunBook book, Func<string, decimal?> markPrice) : IPortfolioState
    {
        public decimal Cash => book.Cash;

        public decimal PortfolioValue => book.Value(markPrice).Equity;

        public decimal UnrealisedPnl => book.Value(markPrice).Positions.Values.Sum(static p => p.UnrealizedPnl);

        public decimal RealisedPnl
        {
            get { lock (book._sync) return book._holdings.Values.Sum(static h => h.RealizedPnl); }
        }

        public IReadOnlyDictionary<string, ExecutionSdk.IPosition> Positions
        {
            get
            {
                lock (book._sync)
                {
                    var valued = book.ValueCore(markPrice).Positions;
                    return valued.Values.ToDictionary(
                        static p => p.Symbol,
                        p => (ExecutionSdk.IPosition)new ExecutionPosition(
                            p.Symbol, p.Quantity, p.AverageCostBasis, p.UnrealizedPnl, p.RealizedPnl)
                        {
                            ContractMultiplier = book._holdings[p.Symbol].ValueFactor
                        },
                        StringComparer.OrdinalIgnoreCase);
                }
            }
        }
    }

    private sealed class Holding(decimal valueFactor)
    {
        public decimal ValueFactor { get; } = valueFactor;
        public long Quantity { get; private set; }
        public decimal AverageCost { get; private set; }
        public decimal RealizedPnl { get; private set; }
        public decimal LastFillPrice { get; private set; }

        public void Apply(long quantity, decimal price)
        {
            LastFillPrice = price;
            if (Quantity == 0 || Math.Sign(Quantity) == Math.Sign(quantity))
            {
                // Opening or adding: weighted average entry price.
                var total = Math.Abs(Quantity) + Math.Abs(quantity);
                AverageCost = (Math.Abs(Quantity) * AverageCost + Math.Abs(quantity) * price) / total;
                Quantity += quantity;
                return;
            }

            // Reducing, closing or reversing: realise the closed part at the average cost.
            var closed = Math.Min(Math.Abs(Quantity), Math.Abs(quantity));
            RealizedPnl += closed * (price - AverageCost) * Math.Sign(Quantity) * ValueFactor;
            Quantity += quantity;
            if (Quantity == 0)
                AverageCost = 0m;
            else if (Math.Sign(Quantity) == Math.Sign(quantity))
                AverageCost = price; // reversed through flat: the remainder opened at this fill
        }
    }
}

/// <summary>The run book valued at one point in time.</summary>
internal sealed record LiveRunBookValuation(
    decimal Cash,
    decimal Equity,
    IReadOnlyDictionary<string, Position> Positions,
    IReadOnlyDictionary<string, FinancialAccountSnapshot> Accounts);
