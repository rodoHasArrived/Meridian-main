using Meridian.Contracts.Domain;
using Meridian.Contracts.FundStructure;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.PortfolioRecords.Accounts;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Reconciliation;

/// <summary>
/// Resolves the internal book (positions and cash) a statement run reconciles against from Meridian's
/// own retained account records, replacing the fail-closed empty default so shipped imports actually
/// reconcile instead of surfacing every external row as an unmatched break. Lives in the application
/// layer so both the browser workstation service graph and the CLI command graph resolve the same
/// retained-book provider over shared account and position stores.
/// </summary>
/// <remarks>
/// Sources and assumptions (documented for operator/domain review):
/// <list type="bullet">
///   <item>Cash is read from the account's retained balance timeline as of the statement period end
///     (<see cref="IAccountQueryService.GetBalanceTimelineAsync"/>) — the most recent internal balance
///     at or before the period close, not the account's newest balance, so a closed statement is never
///     reconciled against a balance recorded after the period.</item>
///   <item>Positions are read best-effort from the retained position snapshot for the account's
///     strategy run (<see cref="IPositionSnapshotStore.GetLatestSnapshotAsync(string,string,System.Threading.CancellationToken)"/>).
///     A snapshot captured after the statement period end fails closed to no positions; market value is
///     left unspecified so the engine matches on quantity and account/security identity.</item>
///   <item>The statement run's <c>FundAccountId</c> must be a Meridian fund-account GUID; an operator
///     label that is not a GUID resolves no internal book, and every row fails closed to a break.</item>
///   <item>Internal records are labeled with the run's external (custodian) account key — the same key
///     the statement side normalizes to — so a statement row reconciles against Meridian's book for the
///     account under reconciliation regardless of the per-row account string the custodian emits.</item>
///   <item>Ledger-transaction population is not sourced here yet; statement transaction rows therefore
///     continue to fail closed to breaks until a ledger-journal mapping is added.</item>
/// </list>
/// Every resolution failure degrades to <see cref="InternalReconciliationPopulations.Empty"/> so the
/// matcher never fabricates a match and the import workflow never throws.
/// </remarks>
public sealed class RetainedInternalReconciliationPopulationProvider(
    IAccountQueryService? accounts = null,
    IPositionSnapshotStore? positionSnapshots = null,
    ILogger<RetainedInternalReconciliationPopulationProvider>? logger = null)
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

            // Both sides of the match are keyed by the run's external (custodian) account so the
            // per-row account string a statement carries (an IBAN, a bank id, a broker account number)
            // does not have to equal Meridian's internal account code for the books to reconcile.
            var accountKey = string.IsNullOrWhiteSpace(context.ExternalAccountId)
                ? context.FundAccountId
                : context.ExternalAccountId.Trim();

            // A default (unset) period end means "no ceiling" — fall back to the newest retained
            // records rather than filtering everything out against DateOnly.MinValue.
            var asOfCeiling = context.StatementPeriodEnd == default
                ? (DateOnly?)null
                : context.StatementPeriodEnd;

            var cash = await ReadCashAsync(accounts, accountId, accountKey, asOfCeiling, ct).ConfigureAwait(false);
            var positions = await ReadPositionsAsync(positionSnapshots, account, context.FundAccountId, accountKey, asOfCeiling, ct).ConfigureAwait(false);

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
        DateOnly? asOfCeiling,
        CancellationToken ct)
    {
        // Resolve the balances the statement period closes on, not the account's newest balances:
        // reconciling a closed statement period against a balance recorded after the period end would
        // compare across time and manufacture spurious breaks. Bound the timeline by the period end and
        // keep only balances at or before it.
        var timeline = await accounts
            .GetBalanceTimelineAsync(accountId, null, asOfCeiling, ct)
            .ConfigureAwait(false);

        var eligible = (timeline ?? [])
            .Where(snapshot => snapshot is not null
                && !string.IsNullOrWhiteSpace(snapshot.Currency)
                && (asOfCeiling is null || snapshot.AsOfDate <= asOfCeiling.Value));

        // One balance per currency: an account can retain balances in several currencies at the period
        // close (USD and EUR, ...), and the matcher reconciles a separate cash row per currency. Emitting
        // only the globally-latest snapshot would drop every other currency and turn each into a spurious
        // unmatched break, so take the latest eligible snapshot per normalized currency and return them all.
        return eligible
            .GroupBy(snapshot => snapshot.Currency.Trim().ToUpperInvariant())
            .Select(group =>
            {
                var latest = group
                    .OrderByDescending(snapshot => snapshot.AsOfDate)
                    .ThenByDescending(snapshot => snapshot.RecordedAt)
                    .First();
                return new InternalCashBalance(
                    $"internal-cash:{accountKey}:{group.Key}",
                    accountKey,
                    latest.Currency,
                    latest.CashBalance,
                    $"internal:balance:{latest.SnapshotId:D}");
            })
            .ToArray();
    }

    private static async Task<IReadOnlyList<InternalPortfolioPosition>> ReadPositionsAsync(
        IPositionSnapshotStore? positionSnapshots,
        AccountSummaryDto account,
        string accountIdText,
        string accountKey,
        DateOnly? asOfCeiling,
        CancellationToken ct)
    {
        if (positionSnapshots is null || string.IsNullOrWhiteSpace(account.RunId))
        {
            return [];
        }

        var snapshot = await ResolvePeriodSnapshotAsync(positionSnapshots, account.RunId.Trim(), accountIdText, asOfCeiling, ct)
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
                $"internal-pos:{accountKey}:{position.Symbol}",
                accountKey,
                position.Symbol,
                asOfDate,
                position.Quantity,
                MarketValue: null,
                $"internal:position:{snapshot.RunId}:{position.Symbol}"));
        }

        return positions;
    }

    // Resolve the position snapshot the statement period closes on. With a period ceiling, select the
    // latest snapshot at or before it from history rather than the account's newest snapshot: a run with
    // both a period-end snapshot and a later one must still reconcile against the period-appropriate book
    // instead of discarding every position because the newest snapshot post-dates the period. Without a
    // ceiling, fall back to the newest retained snapshot.
    private static async Task<AccountSnapshotRecord?> ResolvePeriodSnapshotAsync(
        IPositionSnapshotStore positionSnapshots,
        string runId,
        string accountIdText,
        DateOnly? asOfCeiling,
        CancellationToken ct)
    {
        if (asOfCeiling is null)
        {
            return await positionSnapshots.GetLatestSnapshotAsync(runId, accountIdText, ct).ConfigureAwait(false);
        }

        var ceiling = new DateTimeOffset(asOfCeiling.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        AccountSnapshotRecord? latest = null;
        await foreach (var candidate in positionSnapshots
            .GetSnapshotHistoryAsync(runId, accountIdText, DateTimeOffset.MinValue, ceiling, ct)
            .ConfigureAwait(false))
        {
            // Keep the latest snapshot effective at or before the period close; the explicit ceiling
            // check stays correct even if a store streams a record outside the requested bound.
            if (candidate.AsOf <= ceiling && (latest is null || candidate.AsOf > latest.AsOf))
            {
                latest = candidate;
            }
        }

        return latest;
    }
}
