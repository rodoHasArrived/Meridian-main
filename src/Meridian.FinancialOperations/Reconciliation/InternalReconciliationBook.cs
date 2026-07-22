using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Meridian's own view of the positions, cash balances, and ledger transactions that a broker or
/// custodian statement is reconciled against. This is the "internal book" side of a two-sided
/// reconciliation: the sided <see cref="StatementMatchingEngine"/> compares each statement row
/// against these records instead of against the row's own values.
/// </summary>
public sealed record InternalReconciliationBook(
    IReadOnlyList<InternalPortfolioPosition> Positions,
    IReadOnlyList<InternalCashBalance> CashBalances,
    IReadOnlyList<InternalLedgerTransaction> Transactions)
{
    /// <summary>An internal book with no records. Every statement row reconciles as an unmatched break.</summary>
    public static InternalReconciliationBook Empty { get; } = new([], [], []);

    /// <summary>Total number of internal records available for matching across all three kinds.</summary>
    public int RecordCount => Positions.Count + CashBalances.Count + Transactions.Count;
}

/// <summary>
/// Supplies the internal book (positions, cash, ledger transactions) that a given statement run is
/// reconciled against. Registering a real implementation is how a deployment wires reconciliation
/// against its authoritative internal records; the default <see cref="EmptyInternalReconciliationBookSource"/>
/// yields an empty book so unprovisioned accounts surface honest unmatched breaks rather than
/// fabricated self-matches.
/// </summary>
public interface IInternalReconciliationBookSource
{
    Task<InternalReconciliationBook> GetBookAsync(
        StatementRunRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default internal-book source that returns an empty book. It never fabricates an internal side:
/// with no internal records, the sided matcher reports every statement row as an unmatched break,
/// which is the truthful state for an account whose internal book has not been wired in.
/// </summary>
public sealed class EmptyInternalReconciliationBookSource : IInternalReconciliationBookSource
{
    public static EmptyInternalReconciliationBookSource Instance { get; } = new();

    public Task<InternalReconciliationBook> GetBookAsync(
        StatementRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(InternalReconciliationBook.Empty);
    }
}
