namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Identifies the fund account, external (custodian) account, statement window, reporting base
/// currency, and optional exact accounting authority a statement run reconciles against. Passed to
/// <see cref="IInternalReconciliationPopulationProvider"/> so an implementation can fetch the
/// matching internal portfolio, cash, and ledger populations.
/// </summary>
public sealed record InternalReconciliationPopulationContext(
    string FundAccountId,
    string ExternalAccountId,
    DateOnly StatementPeriodStart,
    DateOnly StatementPeriodEnd,
    string BaseCurrency,
    Guid? LedgerBookId = null,
    Guid? AccountingPeriodId = null);

/// <summary>
/// The internal book a statement is reconciled against: portfolio positions, cash balances, and
/// ledger transactions, expressed in the run's base currency and shaped for
/// <see cref="StatementMatchingEngine"/>.
/// </summary>
public sealed record InternalReconciliationPopulations(
    IReadOnlyList<InternalPortfolioPosition> Positions,
    IReadOnlyList<InternalCashBalance> CashBalances,
    IReadOnlyList<InternalLedgerTransaction> LedgerTransactions)
{
    /// <summary>An empty internal book. Every statement row then surfaces as an unmatched break.</summary>
    public static InternalReconciliationPopulations Empty { get; } = new([], [], []);
}

/// <summary>
/// Supplies the internal portfolio, cash, and ledger populations that a statement run is reconciled
/// against. This is the seam that connects the (previously file-only) reconciliation workflow to
/// Meridian's real books: a production wiring resolves positions/cash/ledger for the fund account and
/// period, while the default <see cref="EmptyInternalReconciliationPopulationProvider"/> supplies an
/// empty book so imports still run and surface every row as a break until a source is connected.
/// </summary>
public interface IInternalReconciliationPopulationProvider
{
    Task<InternalReconciliationPopulations> GetPopulationsAsync(
        InternalReconciliationPopulationContext context,
        CancellationToken ct = default);
}

/// <summary>
/// The safe default provider: returns an empty internal book. With no internal populations, the
/// matching engine reports every statement line as unmatched — the correct, fail-closed behavior
/// when no book is connected (as opposed to fabricating matches). Wire a real provider to reconcile
/// against live positions, cash, and ledger.
/// </summary>
public sealed class EmptyInternalReconciliationPopulationProvider : IInternalReconciliationPopulationProvider
{
    public static EmptyInternalReconciliationPopulationProvider Instance { get; } = new();

    public Task<InternalReconciliationPopulations> GetPopulationsAsync(
        InternalReconciliationPopulationContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(InternalReconciliationPopulations.Empty);
    }
}
