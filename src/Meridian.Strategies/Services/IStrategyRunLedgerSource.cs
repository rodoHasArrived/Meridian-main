using Meridian.Ledger;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Services;

/// <summary>Reads posted journals for an independently authorized run and accounting scope.
/// A missing source is unavailable, never an inferred empty book.</summary>
public interface IStrategyRunLedgerSource
{
    Task<IReadOnlyLedger?> ReadAsync(StrategyRunEntry run, StrategyRunReadScope scope,
        CancellationToken ct = default);

    /// <summary>Explicit retained posting-role registrations for this selected ledger population.</summary>
    IChartOfAccounts? GetPostingChart(IReadOnlyLedger ledger, DateTimeOffset? asOf = null) => null;

    /// <summary>Override with null only when this source proves a complete whole-account population.</summary>
    string? GetBalanceComparisonUnavailableReason(IReadOnlyLedger ledger)
        => "The selected journal source has not established complete account opening balances and all cash-flow population coverage.";
}
