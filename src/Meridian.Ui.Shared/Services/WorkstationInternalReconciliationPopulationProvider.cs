using Meridian.Contracts.Domain;
using Meridian.Contracts.FundStructure;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.PortfolioRecords.Accounts;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Resolves the internal book (positions and cash) a statement run reconciles against from Meridian's
/// own retained account records, replacing the fail-closed empty default so shipped imports actually
/// reconcile instead of surfacing every external row as an unmatched break.
/// </summary>
/// <remarks>
/// Sources and assumptions (documented for operator/domain review):
/// <list type="bullet">
///   <item>Cash is read from the fund account's latest retained balance snapshot
///     (<see cref="IAccountQueryService.GetLatestBalanceSnapshotAsync"/>) — the same "internal" side
///     the provider-ledger reconciliation compares against, distinct from the custodian activity feed.</item>
///   <item>Positions are read best-effort from the retained position snapshot for the account's
///     strategy run (<see cref="IPositionSnapshotStore.GetLatestSnapshotAsync(string,string,System.Threading.CancellationToken)"/>).
///     Market value is left unspecified so the engine matches on quantity and account/security identity.</item>
///   <item>The statement run's <c>FundAccountId</c> must be a Meridian fund-account GUID; an operator
///     label that is not a GUID resolves no internal book, and every row fails closed to a break.</item>
///   <item>Internal records are labeled with the account's <c>AccountCode</c>, so a statement row
///     reconciles when its account column carries that same code.</item>
///   <item>Ledger-transaction population is not sourced here yet; statement transaction rows therefore
///     continue to fail closed to breaks until a ledger-journal mapping is added.</item>
/// </list>
/// Every resolution failure degrades to <see cref="InternalReconciliationPopulations.Empty"/> so the
/// matcher never fabricates a match and the import workflow never throws.
/// </remarks>
public sealed class WorkstationInternalReconciliationPopulationProvider(
    IAccountQueryService? accounts = null,
    IPositionSnapshotStore? positionSnapshots = null,
    ILogger<WorkstationInternalReconciliationPopulationProvider>? logger = null)
    : IInternalReconciliationPopulationProvider
{
    public async Task<InternalReconciliationPopulations> GetPopulationsAsync(
        InternalReconciliationPopulationContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (accounts is null || !Guid.TryParse(context.FundAccountId, out var accountId))
        {
            return InternalReconciliationPopulations.Empty;
        }

        try
        {
            var account = await accounts.GetAccountAsync(accountId, ct).ConfigureAwait(false);
            if (account is null || !account.IsActive)
            {
                return InternalReconciliationPopulations.Empty;
            }

            var accountKey = string.IsNullOrWhiteSpace(account.AccountCode)
                ? context.FundAccountId
                : account.AccountCode.Trim();

            var cash = await ReadCashAsync(accounts, accountId, accountKey, ct).ConfigureAwait(false);
            var positions = await ReadPositionsAsync(positionSnapshots, account, context.FundAccountId, accountKey, ct).ConfigureAwait(false);

            return new InternalReconciliationPopulations(positions, cash, []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail closed: a resolution error must surface statement rows as breaks for operator
            // review rather than throw out of the import workflow.
            logger?.LogWarning(
                ex,
                "Failed to resolve internal reconciliation populations for fund account {FundAccountId}; reconciling against an empty book.",
                context.FundAccountId);
            return InternalReconciliationPopulations.Empty;
        }
    }

    private static async Task<IReadOnlyList<InternalCashBalance>> ReadCashAsync(
        IAccountQueryService accounts,
        Guid accountId,
        string accountKey,
        CancellationToken ct)
    {
        var balance = await accounts.GetLatestBalanceSnapshotAsync(accountId, ct).ConfigureAwait(false);
        if (balance is null || string.IsNullOrWhiteSpace(balance.Currency))
        {
            return [];
        }

        return
        [
            new InternalCashBalance(
                $"internal-cash:{accountId:D}:{balance.Currency}",
                accountKey,
                balance.Currency,
                balance.CashBalance,
                $"internal:balance:{balance.SnapshotId:D}")
        ];
    }

    private static async Task<IReadOnlyList<InternalPortfolioPosition>> ReadPositionsAsync(
        IPositionSnapshotStore? positionSnapshots,
        AccountSummaryDto account,
        string accountIdText,
        string accountKey,
        CancellationToken ct)
    {
        if (positionSnapshots is null || string.IsNullOrWhiteSpace(account.RunId))
        {
            return [];
        }

        var snapshot = await positionSnapshots
            .GetLatestSnapshotAsync(account.RunId.Trim(), accountIdText, ct)
            .ConfigureAwait(false);
        if (snapshot is null || snapshot.Positions.Count == 0)
        {
            return [];
        }

        var asOfDate = DateOnly.FromDateTime(snapshot.AsOf.UtcDateTime);
        var positions = new List<InternalPortfolioPosition>(snapshot.Positions.Count);
        foreach (var position in snapshot.Positions)
        {
            if (position.Quantity == 0m || string.IsNullOrWhiteSpace(position.Symbol))
            {
                continue;
            }

            positions.Add(new InternalPortfolioPosition(
                $"internal-pos:{account.AccountId:D}:{position.Symbol}",
                accountKey,
                position.Symbol,
                asOfDate,
                position.Quantity,
                MarketValue: null,
                $"internal:position:{snapshot.RunId}:{position.Symbol}"));
        }

        return positions;
    }
}
