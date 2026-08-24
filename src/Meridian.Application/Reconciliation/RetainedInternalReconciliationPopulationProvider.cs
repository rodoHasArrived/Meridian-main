using Meridian.Contracts.Domain;
using Meridian.Contracts.FundStructure;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.PortfolioRecords.Accounts;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Reconciliation;

/// <summary>
/// Resolves the internal book (positions, cash, and journal-projected ledger transactions) a
/// statement run reconciles against from Meridian's own retained records, replacing the fail-closed
/// empty default so shipped imports actually reconcile instead of surfacing every external row as an
/// unmatched break. Lives in the application layer so both the browser workstation service graph and
/// the CLI command graph resolve the same retained-book provider over shared account, position, and
/// ledger-journal stores.
/// </summary>
/// <remarks>
/// Sources and assumptions (documented for operator/domain review):
/// <list type="bullet">
///   <item>Cash is read from the account's retained balance timeline as of the statement period end
///     (<see cref="IAccountQueryService.GetBalanceTimelineAsync"/>) — the most recent internal balance
///     at or before the period close, not the account's newest balance, so a closed statement is never
///     reconciled against a balance recorded after the period.</item>
///   <item>Positions are read best-effort from the retained position snapshot effective at or before the
///     statement period end, selected from snapshot history (see <c>ResolvePeriodSnapshotAsync</c>) so a
///     later snapshot never displaces the period-appropriate book; market value is left unspecified so the
///     engine matches on quantity and account/security identity. Known limitation: this reads the legacy
///     unowned snapshot partition via the two-argument <see cref="IPositionSnapshotStore.GetLatestSnapshotAsync(string,string,System.Threading.CancellationToken)"/>
///     and history overloads. Snapshots written through the owner-scoped accounting-capture path
///     (tenant/company/fund/book/entity) are invisible here and fail closed to position breaks until an
///     appropriately authorized owner-scoped query seam is resolved for the account.</item>
///   <item>The statement run's <c>FundAccountId</c> must be a Meridian fund-account GUID; an operator
///     label that is not a GUID resolves no internal book, and every row fails closed to a break.</item>
///   <item>Internal records are labeled with the run's external (custodian) account key — the same key
///     the statement side normalizes to — so a statement row reconciles against Meridian's book for the
///     account under reconciliation regardless of the per-row account string the custodian emits.</item>
///   <item>Ledger transactions are projected from posted journals by the composed
///     <see cref="IInternalLedgerTransactionSource"/> (see
///     <see cref="LedgerJournalInternalTransactionSource"/>): for the statement window, journals
///     attributable to this account (metadata or line-level <c>FinancialAccountId</c> equal to the
///     fund-account GUID, the account's ledger reference, or the external custodian key) that move a
///     well-known cash account project into custodian-visible transactions — net cash amount per
///     currency, canonical trade/fee/dividend/transaction type, effective date, and any stamped
///     external (FITID) identity. Pure internal postings (accruals, valuation marks, period close,
///     reversal pairs) are excluded by a conservative rule documented on the source. Journal reads go
///     through the tenant-scoped <c>ILedgerJournalStore.QueryAsync</c> seam. When no journal source is
///     composed, the store is unavailable, or nothing attributes to the account, the population stays
///     empty exactly as before, so the matcher keeps stamping transaction breaks with the
///     informational <c>internal-transaction-population-unavailable</c> classification instead of
///     comparing against a fabricated book; a genuinely projected population restores full blocking
///     authority automatically.</item>
/// </list>
/// Every resolution failure degrades to <see cref="InternalReconciliationPopulations.Empty"/> so the
/// matcher never fabricates a match and the import workflow never throws.
/// </remarks>
public sealed class RetainedInternalReconciliationPopulationProvider(
    IAccountQueryService? accounts = null,
    IPositionSnapshotStore? positionSnapshots = null,
    ILogger<RetainedInternalReconciliationPopulationProvider>? logger = null,
    IInternalLedgerTransactionSource? ledgerTransactionSource = null)
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
            var accountLabel = string.IsNullOrWhiteSpace(context.ExternalAccountId)
                ? context.FundAccountId
                : context.ExternalAccountId.Trim();

            // A default (unset) period end means "no ceiling" — fall back to the newest retained
            // records rather than filtering everything out against DateOnly.MinValue.
            var asOfCeiling = context.StatementPeriodEnd == default
                ? (DateOnly?)null
                : context.StatementPeriodEnd;

            var cash = await ReadCashAsync(accounts, accountId, accountLabel, asOfCeiling, ct).ConfigureAwait(false);
            var positions = await ReadPositionsAsync(positionSnapshots, account, context.FundAccountId, accountLabel, asOfCeiling, ct).ConfigureAwait(false);
            var ledgerTransactions = await ReadLedgerTransactionsAsync(ledgerTransactionSource, account, context, accountLabel, ct).ConfigureAwait(false);

            return new InternalReconciliationPopulations(positions, cash, ledgerTransactions);
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
        string accountLabel,
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
                    $"internal-cash:{accountLabel}:{group.Key}",
                    accountLabel,
                    latest.Currency,
                    latest.CashBalance,
                    $"internal:balance:{latest.SnapshotId:D}",
                    latest.AsOfDate);
            })
            .ToArray();
    }

    /// <summary>
    /// Projects the account's posted journals for the statement window into custodian-visible
    /// internal transactions. The aliases are every identifier a journal may be stamped with for
    /// this account; the source projects only journals attributable to one of them and fails
    /// closed to an empty population itself, so a missing or unavailable journal source degrades
    /// the transaction lane alone — cash and positions keep reconciling.
    /// </summary>
    private static async Task<IReadOnlyList<InternalLedgerTransaction>> ReadLedgerTransactionsAsync(
        IInternalLedgerTransactionSource? ledgerTransactionSource,
        AccountSummaryDto account,
        InternalReconciliationPopulationContext context,
        string accountLabel,
        CancellationToken ct)
    {
        if (ledgerTransactionSource is null)
        {
            return [];
        }

        var aliases = new List<string> { account.AccountId.ToString("D") };
        if (!string.IsNullOrWhiteSpace(account.LedgerReference))
        {
            aliases.Add(account.LedgerReference.Trim());
        }

        if (!string.IsNullOrWhiteSpace(context.ExternalAccountId))
        {
            aliases.Add(context.ExternalAccountId.Trim());
        }

        return await ledgerTransactionSource
            .GetTransactionsAsync(
                new InternalLedgerTransactionQuery(
                    accountLabel,
                    aliases,
                    context.StatementPeriodStart,
                    context.StatementPeriodEnd,
                    context.BaseCurrency),
                ct)
            .ConfigureAwait(false) ?? [];
    }

    private static async Task<IReadOnlyList<InternalPortfolioPosition>> ReadPositionsAsync(
        IPositionSnapshotStore? positionSnapshots,
        AccountSummaryDto account,
        string accountIdText,
        string accountLabel,
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
                $"internal-pos:{accountLabel}:{position.Symbol}",
                accountLabel,
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
            if (candidate.AsOf > ceiling)
            {
                continue;
            }

            if (latest is null || candidate.AsOf.ToUniversalTime() > latest.AsOf.ToUniversalTime())
            {
                latest = candidate;
                continue;
            }

            if (candidate.AsOf.ToUniversalTime() == latest.AsOf.ToUniversalTime() &&
                !PositionSnapshotEquivalence.AreEquivalent(candidate, latest))
            {
                throw new PositionSnapshotConflictException(
                    candidate.RunId,
                    candidate.AccountId,
                    candidate.AsOf);
            }
        }

        return latest;
    }
}
