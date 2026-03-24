using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;

namespace Meridian.Execution;

/// <summary>
/// Concrete <see cref="IExecutionContext"/> for paper trading sessions.
/// Wires a <see cref="PaperTradingGateway"/> together with a
/// <see cref="PaperTradingPortfolio"/> and an optional double-entry
/// <see cref="Meridian.Ledger.Ledger"/> for full accounting coverage.
/// Enforced by ADR-015 (paper trading gateway — risk-free strategy validation).
/// </summary>
[ImplementsAdr("ADR-015", "PaperExecutionContext wires gateway, portfolio, and ledger for paper sessions")]
public sealed class PaperExecutionContext : IExecutionContext
{
    private readonly PaperTradingPortfolio _portfolio;

    /// <summary>
    /// Creates a paper execution context.
    /// </summary>
    /// <param name="gateway">The paper trading gateway for order routing.</param>
    /// <param name="feed">The live feed adapter for the session's universe.</param>
    /// <param name="universe">Symbols available in this session.</param>
    /// <param name="initialCash">Starting cash balance for the paper portfolio.</param>
    /// <param name="ledger">
    /// Optional ledger. When provided, every fill, commission, and opening capital
    /// posting is recorded as balanced double-entry journal entries.
    /// </param>
    public PaperExecutionContext(
        IOrderGateway gateway,
        ILiveFeedAdapter feed,
        IReadOnlySet<string> universe,
        decimal initialCash,
        Meridian.Ledger.Ledger? ledger = null)
    {
        Gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        Feed = feed ?? throw new ArgumentNullException(nameof(feed));
        Universe = universe ?? throw new ArgumentNullException(nameof(universe));
        _portfolio = new PaperTradingPortfolio(initialCash, ledger);
        Ledger = ledger;
    }

    /// <inheritdoc />
    public IOrderGateway Gateway { get; }

    /// <inheritdoc />
    public ILiveFeedAdapter Feed { get; }

    /// <inheritdoc />
    public IPortfolioState Portfolio => _portfolio;

    /// <inheritdoc />
    public IReadOnlySet<string> Universe { get; }

    /// <inheritdoc />
    public DateTimeOffset CurrentTime { get; private set; } = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public IReadOnlyLedger? Ledger { get; }

    /// <summary>
    /// Advances the context clock and optionally updates the last-known market
    /// price so that unrealised P&amp;L stays current.
    /// </summary>
    public void Tick(DateTimeOffset time, string? symbol = null, decimal? lastPrice = null)
    {
        CurrentTime = time;
        if (symbol is not null && lastPrice.HasValue)
        {
            _portfolio.UpdateMarketPrice(symbol, lastPrice.Value);
        }
    }

    /// <summary>
    /// Applies an execution report (fill or partial fill) to the portfolio and
    /// posts the corresponding double-entry journal entries to the ledger.
    /// </summary>
    public void ApplyFill(ExecutionReport report) => _portfolio.ApplyFill(report);
}
