using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Reads the income and principal cash a brokerage reported for held securities from the retained
/// brokerage sync projection, so expected coupons and paydowns reconcile against broker evidence.
/// </summary>
/// <remarks>
/// Deliberately narrow, because every row returned is compared and an unmatched row is itself a
/// break: only rows that name a held symbol in an account whose id is a linked fund account are
/// returned; a stale projection with no retained history contributes nothing; and only
/// interest/coupon and principal/paydown types are classified. Dividends are not returned until
/// expected dividend events exist, since each would otherwise report as unexpected.
/// </remarks>
public sealed class BrokerageSyncActualCashActivitySource : ISecurityActualCashActivitySource
{
    public const string SourceName = "brokerage-sync";

    private readonly Func<Guid, CancellationToken, Task<FundAccountBrokerageSyncActivityDto?>> _loadActivity;
    private readonly Func<Guid, CancellationToken, Task<IReadOnlyList<FundAccountBrokerageCashTransactionDto>>>? _loadCashHistory;

    /// <param name="loadActivity">Loads the account's current sync projection.</param>
    /// <param name="loadCashHistory">
    /// Optional: loads every cash transaction retained for the account. When supplied it is
    /// preferred over the projection, whose single activity snapshot can miss cash paid earlier
    /// in the period, and it is used even when the projection is stale because each retained row
    /// was a real provider observation.
    /// </param>
    public BrokerageSyncActualCashActivitySource(
        Func<Guid, CancellationToken, Task<FundAccountBrokerageSyncActivityDto?>> loadActivity,
        Func<Guid, CancellationToken, Task<IReadOnlyList<FundAccountBrokerageCashTransactionDto>>>? loadCashHistory = null)
    {
        ArgumentNullException.ThrowIfNull(loadActivity);
        _loadActivity = loadActivity;
        _loadCashHistory = loadCashHistory;
    }

    public async Task<IReadOnlyList<SecurityActualCashActivity>> GetActualActivityAsync(
        IReadOnlyList<SecurityMasterAccountingPosition> positions,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var rows = new List<SecurityActualCashActivity>();
        foreach (var accountPositions in positions
            .Where(static position => position.SecurityId is not null)
            .GroupBy(static position => position.AccountId, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (!Guid.TryParse(accountPositions.Key, out var fundAccountId))
            {
                continue;
            }

            var projection = await _loadActivity(fundAccountId, ct).ConfigureAwait(false);
            if (projection is null)
            {
                continue;
            }

            var history = _loadCashHistory is null
                ? []
                : await _loadCashHistory(fundAccountId, ct).ConfigureAwait(false);
            if (history.Count == 0 && projection.Status.IsStale)
            {
                continue;
            }

            var cashTransactions = history.Count > 0 ? history : projection.CashTransactions;

            var securityBySymbol = accountPositions
                .GroupBy(static position => position.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.First().SecurityId,
                    StringComparer.OrdinalIgnoreCase);

            foreach (var cash in cashTransactions)
            {
                var payDate = DateOnly.FromDateTime(cash.PostedAt.UtcDateTime);
                if (string.IsNullOrWhiteSpace(cash.Symbol)
                    || payDate < periodStart
                    || payDate > periodEnd
                    || !securityBySymbol.TryGetValue(cash.Symbol.Trim(), out var securityId)
                    || Classify(cash.TransactionType) is not { } classification)
                {
                    continue;
                }

                var amount = Math.Abs(cash.Amount);
                rows.Add(new SecurityActualCashActivity(
                    SourceName,
                    cash.TransactionId,
                    accountPositions.Key,
                    securityId,
                    cash.Symbol.Trim().ToUpperInvariant(),
                    CashAmount: amount,
                    PrincipalAmount: classification == "Principal" ? amount : 0m,
                    IncomeAmount: classification == "Income" ? amount : 0m,
                    payDate,
                    classification,
                    EvidenceLink: projection.RawSnapshotPath));
            }
        }

        return rows;
    }

    internal static string? Classify(string? transactionType)
    {
        if (string.IsNullOrWhiteSpace(transactionType))
        {
            return null;
        }

        if (transactionType.Contains("principal", StringComparison.OrdinalIgnoreCase)
            || transactionType.Contains("paydown", StringComparison.OrdinalIgnoreCase))
        {
            return "Principal";
        }

        return transactionType.Contains("interest", StringComparison.OrdinalIgnoreCase)
            || transactionType.Contains("coupon", StringComparison.OrdinalIgnoreCase)
            ? "Income"
            : null;
    }
}
