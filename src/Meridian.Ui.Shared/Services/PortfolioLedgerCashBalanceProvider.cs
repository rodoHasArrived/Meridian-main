using Meridian.Contracts.AssetOperations;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Supplies the portfolio cash ladder's opening cash by reading the actuals view
/// of the double-entry ledger. Cash is taken from the asset-side cash accounts
/// (<see cref="LedgerAccounts.Cash"/> and the per-currency
/// <see cref="LedgerAccounts.CashInCurrency"/> accounts) consolidated across the
/// project's ledger books.
/// </summary>
/// <remarks>
/// <para>
/// Only the <see cref="LedgerViewKind.Actual"/> view is consolidated. The project
/// ledger book holds parallel views of the same economics (historical replay,
/// parameterized P&amp;L scenarios, Security Master contractual projections); summing
/// cash across those views would multi-count, so they are deliberately excluded.
/// </para>
/// <para>
/// The plain <c>"Cash"</c> account carries no currency symbol, so its balance is
/// reported in the assumed reporting currency (<see cref="ReportingCurrency"/>).
/// Per-currency cash accounts created by <see cref="LedgerAccounts.CashInCurrency"/>
/// carry the ISO code in <see cref="LedgerAccount.Symbol"/> and are reported as-is;
/// the cash ladder engine flags any non-base-currency balance as a 1:1 assumption
/// rather than applying FX conversion.
/// </para>
/// </remarks>
public sealed class PortfolioLedgerCashBalanceProvider : IPortfolioCashBalanceProvider
{
    /// <summary>
    /// Currency assumed for the unscoped <c>"Cash"</c> account, matching the cash
    /// ladder read model's default base currency. A future revision can source this
    /// from configuration once a governed reporting-currency setting exists.
    /// </summary>
    private const string ReportingCurrency = "USD";

    private readonly ProjectLedgerBook? _ledgerBook;

    /// <summary>
    /// The <see cref="ProjectLedgerBook"/> is optional so that registering this provider
    /// can never fail read-model resolution in a host that has not composed the ledger
    /// feature: when it is absent the ladder simply opens from zero cash, exactly as it
    /// does today, and the engine emits its "no cash balances were provided" warning.
    /// </summary>
    public PortfolioLedgerCashBalanceProvider(ProjectLedgerBook? ledgerBook = null)
    {
        _ledgerBook = ledgerBook;
    }

    public Task<IReadOnlyList<PortfolioCashBalanceDto>> GetCashBalancesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (_ledgerBook is null)
        {
            return Task.FromResult<IReadOnlyList<PortfolioCashBalanceDto>>([]);
        }

        var summaries = _ledgerBook.ConsolidatedAccountSummaries(
            accountType: LedgerAccountType.Asset,
            ledgerView: LedgerViewKind.Actual);

        var balances = new List<PortfolioCashBalanceDto>();
        foreach (var summary in summaries)
        {
            var account = summary.Account;
            if (!IsCashAccount(account))
            {
                continue;
            }

            var amount = decimal.Round(summary.Balance, 4, MidpointRounding.AwayFromZero);
            if (amount == 0m)
            {
                continue;
            }

            var currency = string.IsNullOrWhiteSpace(account.Symbol)
                ? ReportingCurrency
                : account.Symbol!.Trim().ToUpperInvariant();

            balances.Add(new PortfolioCashBalanceDto(
                AccountId: account.ToString(),
                DisplayName: account.FinancialAccountId is { } scope ? $"{account.Name} — {scope}" : account.Name,
                Amount: amount,
                Currency: currency,
                SourceDomain: "Ledger",
                SourceEntityId: account.FinancialAccountId));
        }

        return Task.FromResult<IReadOnlyList<PortfolioCashBalanceDto>>(balances);
    }

    private static bool IsCashAccount(LedgerAccount account)
        => account.AccountType == LedgerAccountType.Asset
            && (string.Equals(account.Name, "Cash", StringComparison.Ordinal)
                || account.Name.StartsWith("Cash (", StringComparison.Ordinal));
}
