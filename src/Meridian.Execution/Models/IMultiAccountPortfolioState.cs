using Meridian.Execution.Interfaces;

namespace Meridian.Execution.Models;

/// <summary>
/// Extends <see cref="IPortfolioState"/> with per-account granularity.
/// Implementations should aggregate across all owned accounts for the
/// single-account <see cref="IPortfolioState"/> surface (backward-compatibility).
/// </summary>
public interface IMultiAccountPortfolioState : IPortfolioState
{
    /// <summary>Read-only list of all tracked accounts (brokerage + bank).</summary>
    IReadOnlyList<IAccountPortfolio> Accounts { get; }

    /// <summary>
    /// Retrieves a single account by <paramref name="accountId"/>.
    /// Returns <see langword="null"/> when no account with that ID is registered.
    /// </summary>
    IAccountPortfolio? GetAccount(string accountId);

    /// <summary>
    /// Produces an aggregate snapshot across all accounts.
    /// </summary>
    MultiAccountPortfolioSnapshot GetAggregateSnapshot();
}

/// <summary>
/// Aggregate snapshot across all accounts tracked by an <see cref="IMultiAccountPortfolioState"/>.
/// </summary>
/// <param name="Accounts">Per-account detailed snapshots.</param>
/// <param name="TotalCash">Sum of cash across all accounts.</param>
/// <param name="TotalLongMarketValue">Sum of long market values.</param>
/// <param name="TotalShortMarketValue">Sum of absolute short market values.</param>
/// <param name="TotalGrossExposure">TotalLongMarketValue + TotalShortMarketValue.</param>
/// <param name="TotalNetExposure">TotalLongMarketValue − TotalShortMarketValue.</param>
/// <param name="TotalUnrealisedPnl">Aggregate unrealised P&amp;L.</param>
/// <param name="TotalRealisedPnl">Aggregate realised P&amp;L.</param>
/// <param name="AsOf">UTC timestamp.</param>
public sealed record MultiAccountPortfolioSnapshot(
    IReadOnlyList<ExecutionAccountDetailSnapshot> Accounts,
    decimal TotalCash,
    decimal TotalLongMarketValue,
    decimal TotalShortMarketValue,
    decimal TotalGrossExposure,
    decimal TotalNetExposure,
    decimal TotalUnrealisedPnl,
    decimal TotalRealisedPnl,
    DateTimeOffset AsOf)
{
    /// <summary>Creates an aggregate snapshot from a list of per-account snapshots.</summary>
    public static MultiAccountPortfolioSnapshot FromAccounts(
        IReadOnlyList<ExecutionAccountDetailSnapshot> accounts)
    {
        var totalCash = accounts.Sum(a => a.Cash);
        var totalLong = accounts.Sum(a => a.LongMarketValue);
        var totalShort = accounts.Sum(a => a.ShortMarketValue);
        var totalUnrealised = accounts.Sum(a => a.UnrealisedPnl);
        var totalRealised = accounts.Sum(a => a.RealisedPnl);

        return new MultiAccountPortfolioSnapshot(
            Accounts: accounts,
            TotalCash: totalCash,
            TotalLongMarketValue: totalLong,
            TotalShortMarketValue: totalShort,
            TotalGrossExposure: totalLong + totalShort,
            TotalNetExposure: totalLong - totalShort,
            TotalUnrealisedPnl: totalUnrealised,
            TotalRealisedPnl: totalRealised,
            AsOf: DateTimeOffset.UtcNow);
    }
}
