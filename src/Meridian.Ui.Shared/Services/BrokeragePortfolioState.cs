using Meridian.Execution.Models;
using Meridian.Execution.Sdk;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Read-only broker observations for the host's legacy portfolio and position-tracker consumers.
/// Trading authority belongs to LiveBrokeragePortfolioSyncService and the mandatory brokerage
/// risk rule. Retaining an observation here does not certify it as fresh or reconciled.
/// </summary>
public sealed class BrokeragePortfolioState(LiveBrokeragePortfolioSyncService synchronization)
    : IPortfolioState, IPositionTracker
{
    /// <summary>The local account owning the single retained broker observation.</summary>
    public Guid? FundAccountId => Observation()?.FundAccountId;
    public bool IsConnected => Observation()?.IsConnected == true;

    public decimal Cash => Observation()?.Snapshot?.Balance.Cash ?? 0m;
    public decimal BuyingPower => Observation()?.Snapshot?.Balance.BuyingPower ?? 0m;
    public decimal PortfolioValue => Observation()?.Snapshot?.Balance.Equity ?? 0m;
    public decimal UnrealisedPnl => Observation()?.Snapshot?.Positions.Sum(position => position.UnrealizedPnl) ?? 0m;

    // This projection does not maintain a separate session trade ledger. Realized P&L remains
    // with the existing accounting handoff; local fills must never mutate broker holdings.
    public decimal RealisedPnl => 0m;

    public IReadOnlyDictionary<string, IPosition> Positions
    {
        get
        {
            var observation = Observation();
            if (observation?.Snapshot is not { } snapshot)
                return new Dictionary<string, IPosition>(StringComparer.OrdinalIgnoreCase);

            return ProjectPositions(snapshot).ToDictionary(position => position.Symbol, position => (IPosition)new ExecutionPosition(
                position.Symbol, (long)position.Quantity, position.AverageEntryPrice,
                position.UnrealizedPnl, 0m)
            {
                ExactQuantity = position.Quantity,
                ContractMultiplier = position.AssetClass == "us_option" ? 100m : 1m,
                OwnerQuantities = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                {
                    [observation.FundAccountId.ToString("D")] = position.Quantity
                }
            }, StringComparer.OrdinalIgnoreCase);
        }
    }

    public PositionState GetPosition(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var observation = Observation();
        var position = observation?.Snapshot is { } snapshot
            ? ProjectPositions(snapshot).FirstOrDefault(position =>
                string.Equals(position.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            : null;
        return position is null
            ? new PositionState { Symbol = symbol, LastUpdated = observation?.Snapshot?.RetrievedAt ?? DateTimeOffset.MinValue }
            : Project(position, observation!.Snapshot!.RetrievedAt);
    }

    public IReadOnlyDictionary<string, PositionState> GetAllPositions()
    {
        var snapshot = Observation()?.Snapshot;
        return snapshot is null
            ? new Dictionary<string, PositionState>(StringComparer.OrdinalIgnoreCase)
            : ProjectPositions(snapshot).ToDictionary(position => position.Symbol,
                position => Project(position, snapshot.RetrievedAt), StringComparer.OrdinalIgnoreCase);
    }

    public decimal GetPortfolioValue() => PortfolioValue;
    public decimal GetCash() => Cash;
    public decimal GetUnrealizedPnl() => UnrealisedPnl;
    public decimal GetRealizedPnl() => RealisedPnl;

    private static IEnumerable<BrokeragePositionSnapshotDto> ProjectPositions(BrokeragePortfolioSnapshotDto snapshot)
    {
        // Invalid snapshots remain readable while the operator investigates them. These legacy
        // symbol-keyed views can express only net holdings, so aggregate every duplicate row
        // rather than selecting one or throwing. In particular, retain a zero-net row when
        // opposing observations offset. The authoritative risk feed still reads the original
        // rows, preserves their gross exposure, and blocks the inconsistent snapshot.
        foreach (var group in snapshot.Positions.GroupBy(position => position.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            var rows = group.ToArray();
            var first = rows[0];
            if (rows.Length == 1)
            {
                yield return first;
                continue;
            }

            var quantity = rows.Sum(position => position.Quantity);
            yield return first with
            {
                Quantity = quantity,
                AverageEntryPrice = quantity == 0m ? 0m
                    : rows.Sum(position => position.Quantity * position.AverageEntryPrice) / quantity,
                MarketPrice = quantity == 0m ? rows.Max(position => position.MarketPrice)
                    : rows.Sum(position => position.Quantity * position.MarketPrice) / quantity,
                MarketValue = rows.Sum(position => position.MarketValue),
                UnrealizedPnl = rows.Sum(position => position.UnrealizedPnl)
            };
        }
    }

    private static PositionState Project(BrokeragePositionSnapshotDto position, DateTimeOffset observedAt) => new()
    {
        Symbol = position.Symbol,
        Quantity = position.Quantity,
        AverageCostBasis = position.AverageEntryPrice,
        MarketPrice = position.MarketPrice,
        LastUpdated = observedAt
    };

    private LiveBrokeragePortfolioStatus? Observation()
    {
        // This host has one brokerage execution gateway. Never combine historical observations
        // from different broker accounts into a synthetic position book. The scoped risk feed
        // handles the account selection and blocks absent/inconsistent evidence independently.
        var observations = synchronization.GetStatuses().Where(status => status.Snapshot is not null).ToArray();
        if (observations.Select(status => status.Snapshot!.Account.AccountId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            throw new InvalidOperationException("The host portfolio has observations for multiple brokerage accounts; reconcile its account binding.");

        // A fill invalidates trading readiness before its accounting handoff reads Cash and P&L.
        // Keep the last broker observation readable through that window; filtering on IsReady
        // would discard known holdings or prevent the fill handoff itself from completing.
        return observations.OrderByDescending(status => status.LastSuccessfulSyncAt)
            .ThenByDescending(status => status.LastAttemptAt).FirstOrDefault();
    }
}
