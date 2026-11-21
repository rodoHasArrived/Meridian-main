using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Services;

/// <summary>
/// Builds workstation-facing portfolio read models from recorded run results.
/// </summary>
public sealed class PortfolioReadService
{
    public const string PortfolioSeam = "portfolio";
    private readonly ISecurityReferenceLookup? _securityReferenceLookup;

    public PortfolioReadService()
    {
    }

    public PortfolioReadService(ISecurityReferenceLookup securityReferenceLookup)
    {
        _securityReferenceLookup = securityReferenceLookup ?? throw new ArgumentNullException(nameof(securityReferenceLookup));
    }

    public PortfolioSummary? BuildSummary(StrategyRunEntry entry)
        => BuildBaseSummary(entry);

    public async Task<PortfolioSummary?> BuildSummaryAsync(StrategyRunEntry entry, CancellationToken ct = default)
    {
        var summary = BuildBaseSummary(entry);
        if (summary is null || _securityReferenceLookup is null || (summary.Positions.Count == 0 && summary.AccountingPositions.Count == 0))
        {
            return summary;
        }

        var lookupRequests = BuildLookupRequests(entry, summary);
        var lookup = await ResolveSecuritiesAsync(lookupRequests, ct).ConfigureAwait(false);

        var positions = summary.Positions
            .Select(position => position with
            {
                Security = lookup.GetValueOrDefault(position.Symbol)
            })
            .ToArray();

        var resolvedCount = lookup.Values.Count(static value => value is not null);
        var missingCount = lookup.Count - resolvedCount;

        return summary with
        {
            Positions = positions,
            SecurityResolvedCount = resolvedCount,
            SecurityMissingCount = missingCount
        };
    }

    public IReadOnlyList<StrategyRunContinuityWarning> BuildContinuityWarnings(string runId, PortfolioSummary? summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (summary is not null)
        {
            return Array.Empty<StrategyRunContinuityWarning>();
        }

        return
        [
            new StrategyRunContinuityWarning(
                Code: "missing-portfolio",
                Severity: StrategyRunContinuityWarningSeverity.Warning,
                Message: "Run does not have a shared portfolio summary yet.",
                SourceSeam: PortfolioSeam)
        ];
    }

    private static PortfolioSummary? BuildBaseSummary(StrategyRunEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var result = entry.Metrics;
        var latestSnapshot = result?.Snapshots.LastOrDefault();
        if (latestSnapshot is null)
        {
            return null;
        }

        var scope = StrategyRunScopeMetadataResolver.Resolve(entry);

        var positions = latestSnapshot.Positions.Values
            .OrderBy(static position => position.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(position => new PortfolioPositionSummary(
                Symbol: position.Symbol,
                Quantity: position.Quantity,
                AverageCostBasis: position.AverageCostBasis,
                RealizedPnl: position.RealizedPnl,
                UnrealizedPnl: position.UnrealizedPnl,
                IsShort: position.ExactQuantity < 0m,
                AccountScopeId: scope.AccountId,
                AccountScopeDisplayName: scope.AccountDisplayName,
                EntityScopeId: scope.EntityId,
                EntityScopeDisplayName: scope.EntityDisplayName,
                SleeveScopeId: scope.SleeveId,
                SleeveScopeDisplayName: scope.SleeveDisplayName,
                VehicleScopeId: scope.VehicleId,
                VehicleScopeDisplayName: scope.VehicleDisplayName,
                MarkFreshness: new MarkFreshnessAssessmentDto(position.Symbol, null, scope.AccountId,
                    DateOnly.FromDateTime(latestSnapshot.Timestamp.UtcDateTime), null, null,
                    "Unassessed", "ReviewRequired",
                    "The retained position has no mark observation date; snapshot time is not mark evidence.")))
            .ToArray();

        var longMarketValue = latestSnapshot.LongMarketValue;
        var shortMarketValue = latestSnapshot.ShortMarketValue;
        var grossExposure = longMarketValue + Math.Abs(shortMarketValue);
        var netExposure = longMarketValue + shortMarketValue;
        var realizedPnl = positions.Sum(static position => position.RealizedPnl);
        var unrealizedPnl = positions.Sum(static position => position.UnrealizedPnl);
        var financing = result!.Metrics.TotalMarginInterest - result.Metrics.TotalShortRebates;

        return new PortfolioSummary(
            PortfolioId: entry.PortfolioId ?? entry.RunId,
            RunId: entry.RunId,
            AsOf: latestSnapshot.Timestamp,
            Cash: latestSnapshot.Cash,
            LongMarketValue: longMarketValue,
            ShortMarketValue: shortMarketValue,
            GrossExposure: grossExposure,
            NetExposure: netExposure,
            TotalEquity: latestSnapshot.TotalEquity,
            RealizedPnl: realizedPnl,
            UnrealizedPnl: unrealizedPnl,
            Commissions: result.Metrics.TotalCommissions,
            Financing: financing,
            Positions: positions,
            AccountScopeId: scope.AccountId,
            AccountScopeDisplayName: scope.AccountDisplayName,
            EntityScopeId: scope.EntityId,
            EntityScopeDisplayName: scope.EntityDisplayName,
            SleeveScopeId: scope.SleeveId,
            SleeveScopeDisplayName: scope.SleeveDisplayName,
            VehicleScopeId: scope.VehicleId,
            VehicleScopeDisplayName: scope.VehicleDisplayName)
        {
            AccountingPositions = BuildAccountingPositions(latestSnapshot),
            HasRetainedAccountingPositionPopulation = latestSnapshot.AccountingPositions is not null,
            FinancingCoverageStatus = result.EngineMetadata?.FinancingCoverageStatus,
            RunAttributedMarginInterest = result.EngineMetadata?.RunAttributedMarginInterest
        };
    }

    private static IReadOnlyList<StrategyPositionAccountingEvidence> BuildAccountingPositions(PortfolioSnapshot snapshot)
    {
        if (snapshot.AccountingPositions is not null)
            return Array.AsReadOnly(snapshot.AccountingPositions.Select(position => position with
            {
                EvidenceReferences = position.EvidenceReferences is null ? null : Array.AsReadOnly(position.EvidenceReferences.ToArray())
            }).ToArray());
        var rows = new List<StrategyPositionAccountingEvidence>();
        foreach (var account in snapshot.Accounts.Values.OrderBy(static item => item.AccountId, StringComparer.OrdinalIgnoreCase))
        foreach (var position in account.Positions.Values.OrderBy(static item => item.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            var lots = (position.OpenLots ?? account.OpenLots ?? [])
                .Where(lot => string.Equals(lot.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase)
                    && lot.IsShort == (position.ExactQuantity < 0m)).ToArray();
            var units = lots.Select(static lot => lot.UnitKind).Distinct().ToArray();
            var multipliers = lots.Select(static lot => lot.Multiplier).Distinct().ToArray();
            var complete = lots.Length > 0 && units.Length == 1 && multipliers.Length == 1
                && lots.Sum(static lot => lot.Quantity) == Math.Abs(position.ExactQuantity)
                && lots.All(lot => lot.FunctionalCurrency is not null && lot.FunctionalCurrency == account.FunctionalCurrency);
            var ids = lots.Select(static lot => lot.SecurityId).Distinct().ToArray();
            rows.Add(new(position.Symbol, account.AccountId, ids.Length == 1 ? ids[0] : null, position.ExactQuantity < 0m,
                position.ExactQuantity, complete ? lots.Sum(static lot => lot.CostBasis()) : null,
                units.Length == 1 ? units[0].ToString() : null, multipliers.Length == 1 ? multipliers[0] : null,
                account.FunctionalCurrency, complete ? null : "A complete unitized, currency-declared open-lot population is unavailable.",
                lots.Select(static lot => $"simulation-lot:{lot.LotId:D}").ToArray()));
        }
        return rows;
    }

    private static IReadOnlyDictionary<string, SecurityReferenceLookupRequest> BuildLookupRequests(
        StrategyRunEntry entry,
        PortfolioSummary summary)
    {
        var symbolsToSecurityId = entry.Metrics?.Ledger?.Journal
            .Where(static item => !string.IsNullOrWhiteSpace(item.Metadata.Symbol))
            .GroupBy(
                static item => item.Metadata.Symbol!,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group =>
                {
                    var candidates = group
                        .Select(static entry => entry.Metadata.SecurityId)
                        .Where(static id => id.HasValue)
                        .Select(static id => id!.Value)
                        .Distinct()
                        .ToArray();
                    return candidates.Length == 1 ? candidates[0] : (Guid?)null;
                },
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);

        return summary.Positions.Select(static position => position.Symbol)
            .Concat(summary.AccountingPositions.Select(static position => position.Symbol))
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static symbol => symbol,
                symbol =>
                {
                    var retainedIds = summary.AccountingPositions.Where(position =>
                            string.Equals(position.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                        .Select(static position => position.SecurityId).Where(static id => id.HasValue && id != Guid.Empty)
                        .Distinct().ToArray();
                    if (retainedIds.Length > 0)
                        return new SecurityReferenceLookupRequest(SecurityId: retainedIds.Length == 1 ? retainedIds[0] : null,
                            Symbol: symbol, Source: retainedIds.Length == 1 ? "native-accounting-security-id" : "native-accounting-identity-conflict");
                    var resolvedSecurityId = symbolsToSecurityId.GetValueOrDefault(symbol);
                    var source = resolvedSecurityId is null ? "portfolio-position-symbol" : "ledger-metadata-security-id";
                    return new SecurityReferenceLookupRequest(
                        SecurityId: resolvedSecurityId,
                        IdentifierKind: SecurityIdentifierKind.Ticker.ToString(),
                        IdentifierValue: symbol,
                        Symbol: symbol,
                        Source: source);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, WorkstationSecurityReference?>> ResolveSecuritiesAsync(
        IReadOnlyDictionary<string, SecurityReferenceLookupRequest> requests,
        CancellationToken ct)
    {
        var lookup = new Dictionary<string, WorkstationSecurityReference?>(StringComparer.OrdinalIgnoreCase);
        if (_securityReferenceLookup is null)
        {
            return lookup;
        }

        foreach (var (symbol, request) in requests)
        {
            if (request.Source == "native-accounting-identity-conflict")
            {
                lookup[symbol] = null;
                continue;
            }
            var resolved = await _securityReferenceLookup.GetByCanonicalAsync(request, ct).ConfigureAwait(false);
            if (request.Source == "native-accounting-security-id")
                resolved = resolved?.SecurityId == request.SecurityId ? resolved : null;
            else
                resolved ??= await _securityReferenceLookup.GetBySymbolAsync(symbol, ct).ConfigureAwait(false);

            lookup[symbol] = resolved is null
                ? null
                : resolved with
                {
                    LookupSource = request.Source,
                    LookupPath = resolved.LookupPath ?? (request.SecurityId is null ? "symbol" : "security-id"),
                    IsInferredMatch = request.SecurityId is null && string.IsNullOrWhiteSpace(request.IdentifierValue)
                };
        }

        return lookup;
    }
}
