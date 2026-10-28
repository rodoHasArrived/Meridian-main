using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;

namespace Meridian.Ui.Shared.Services;

public sealed partial class LiveBrokeragePortfolioSyncService
{
    // An IPosition has a nonnullable multiplier. Refuse that legacy projection when the retained
    // contract is unresolved rather than make up exposure; raw broker evidence remains readable.
    internal static decimal GetObservedOptionMultiplier(LiveBrokeragePortfolioStatus observation, string symbol)
        => observation.OptionContractMultipliers.TryGetValue(symbol, out var multiplier)
                ? multiplier
                : throw new InvalidOperationException(
                    $"The Security Master contract multiplier is unresolved for broker option '{symbol}'; synchronize the account after resolving its contract.");

    private async Task<IReadOnlyDictionary<string, ObservedOptionContract>> ResolveContractMultipliersAsync(
        BrokeragePortfolioSnapshotDto snapshot, CancellationToken ct)
    {
        var contracts = new Dictionary<string, ObservedOptionContract>(StringComparer.OrdinalIgnoreCase);
        if (_securityMaster is null)
            return contracts;
        foreach (var symbol in snapshot.Positions.Where(position => position.AssetClass == "us_option")
            .Select(position => position.Symbol).Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // This API refuses missing or ambiguous retained identity. In particular, it never
            // substitutes a current projection or legacy identity for missing historical evidence.
            var security = await _securityMaster.GetRecordedByIdentifierAsOfAsync(
                SecurityIdentifierKind.OccOptionSymbol, symbol, null, snapshot.RetrievedAt,
                snapshot.RetrievedAt, ct).ConfigureAwait(false);
            if (security is not { Status: SecurityStatusDto.Active })
                continue;
            var descriptor = SecurityAssetClassCatalog.GetOrDefault(security.AssetClass);
            if (!descriptor.UsesOccOptionSymbols || descriptor.LotMultiplierTermKey is not { } key)
                continue;
            var terms = SecurityAssetClassTermsUpgrader.UpgradeForRead(security.AssetClass, security.AssetSpecificTerms);
            var multiplier = SecurityTermReader.ReadDecimal(terms, key);
            if (multiplier is not > 0m)
                continue;
            contracts[symbol] = new(security.SecurityId, security.Version, multiplier.Value);
        }
        return contracts;
    }

    private static bool ContractMultipliersMatch(
        IReadOnlyDictionary<string, ObservedOptionContract> before,
        IReadOnlyDictionary<string, ObservedOptionContract> after)
        => before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var contract) && contract == pair.Value);

    private sealed record ObservedOptionContract(Guid SecurityId, long Version, decimal Multiplier);
}
