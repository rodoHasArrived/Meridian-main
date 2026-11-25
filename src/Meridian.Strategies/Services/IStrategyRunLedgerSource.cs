using Meridian.Ledger;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Services;

/// <summary>Reads posted journals for an independently authorized run and accounting scope.
/// A missing source is unavailable, never an inferred empty book.</summary>
public interface IStrategyRunLedgerSource
{
    Task<IReadOnlyLedger?> ReadAsync(StrategyRunEntry run, StrategyRunReadScope scope,
        CancellationToken ct = default);
}
