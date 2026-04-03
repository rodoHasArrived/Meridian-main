using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Execution.Services;

/// <summary>
/// Result of a single reconciliation pass for one account.
/// </summary>
/// <param name="AccountId">The account that was reconciled.</param>
/// <param name="ProviderName">The brokerage provider used.</param>
/// <param name="MatchedSymbols">Symbols with matching quantities.</param>
/// <param name="DivergentSymbols">Symbols whose quantities diverged beyond tolerance.</param>
/// <param name="MissingInPortfolio">Symbols present in the brokerage but absent locally.</param>
/// <param name="MissingInBrokerage">Symbols held locally but not reported by brokerage.</param>
/// <param name="ReconciledAt">UTC timestamp of the pass.</param>
public sealed record ReconciliationReport(
    string AccountId,
    string ProviderName,
    IReadOnlyList<string> MatchedSymbols,
    IReadOnlyList<string> DivergentSymbols,
    IReadOnlyList<string> MissingInPortfolio,
    IReadOnlyList<string> MissingInBrokerage,
    DateTimeOffset ReconciledAt)
{
    /// <summary>True when no divergences were found.</summary>
    public bool IsClean => DivergentSymbols.Count == 0
        && MissingInPortfolio.Count == 0
        && MissingInBrokerage.Count == 0;
}

/// <summary>
/// Periodically reconciles live paper/brokerage portfolio positions against
/// the positions reported by a connected <see cref="IBrokeragePositionSync"/> provider.
/// <para>
/// Which provider is queried first is controlled by <see cref="PositionSyncOptions.ProviderPriority"/>,
/// allowing users to set <c>InteractiveBrokers</c> or <c>Alpaca</c> as the primary source.
/// </para>
/// </summary>
public sealed class PositionReconciliationService
{
    private readonly IReadOnlyList<(string Name, IBrokeragePositionSync Sync)> _providers;
    private readonly PortfolioRegistry _registry;
    private readonly PositionSyncOptions _options;
    private readonly ILogger<PositionReconciliationService> _logger;

    private ReconciliationReport? _lastReport;
    private int _consecutiveFailures;

    /// <summary>Exposes the report from the most recent reconciliation pass.</summary>
    public ReconciliationReport? GetLastReconciliationReport() => _lastReport;

    public PositionReconciliationService(
        IEnumerable<(string Name, IBrokeragePositionSync Sync)> providers,
        PortfolioRegistry registry,
        IOptions<PositionSyncOptions> options,
        ILogger<PositionReconciliationService> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        _registry = registry;
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Apply user-configured priority order.
        _providers = ApplyPriority(providers?.ToList() ?? [], _options.ProviderPriority);
    }

    /// <summary>
    /// Runs a single reconciliation pass against the highest-priority available provider.
    /// Emits a <see cref="ReconciliationReport"/> per account checked.
    /// </summary>
    public async Task<IReadOnlyList<ReconciliationReport>> ReconcileAsync(CancellationToken ct = default)
    {
        if (_providers.Count == 0)
        {
            _logger.LogDebug("No brokerage position-sync providers registered; skipping reconciliation");
            return [];
        }

        var reports = new List<ReconciliationReport>();

        foreach (var portfolio in _registry.GetAll().Values)
        {
            foreach (var account in portfolio.Accounts)
            {
                var report = await ReconcileAccountAsync(account.AccountId, portfolio, ct).ConfigureAwait(false);
                if (report is not null)
                {
                    reports.Add(report);
                    _lastReport = report;

                    if (!report.IsClean)
                    {
                        _logger.LogWarning(
                            "Position reconciliation divergence: account={AccountId} provider={Provider} " +
                            "divergent={Divergent} missingLocal={MissingLocal} missingBrokerage={MissingBrokerage}",
                            report.AccountId,
                            report.ProviderName,
                            string.Join(",", report.DivergentSymbols),
                            string.Join(",", report.MissingInPortfolio),
                            string.Join(",", report.MissingInBrokerage));
                    }
                }
            }
        }

        return reports;
    }

    private async Task<ReconciliationReport?> ReconcileAccountAsync(
        string accountId,
        Meridian.Execution.Models.IMultiAccountPortfolioState portfolio,
        CancellationToken ct)
    {
        foreach (var (providerName, sync) in _providers)
        {
            try
            {
                var brokeragePositions = await sync.GetCurrentPositionsAsync(accountId, ct).ConfigureAwait(false);
                var localAccount = portfolio.GetAccount(accountId);

                var localPositions = localAccount?.Positions
                    ?? new Dictionary<string, IPosition>(StringComparer.OrdinalIgnoreCase);

                var brokerageBySymbol = brokeragePositions.ToDictionary(
                    static p => p.Symbol, StringComparer.OrdinalIgnoreCase);

                var matched = new List<string>();
                var divergent = new List<string>();
                var missingLocal = new List<string>();
                var missingBrokerage = new List<string>();

                foreach (var (symbol, brokerPos) in brokerageBySymbol)
                {
                    if (localPositions.TryGetValue(symbol, out var localPos))
                    {
                        var diff = Math.Abs((decimal)localPos.Quantity - brokerPos.Quantity);
                        var tolerance = _options.DivergenceTolerance * Math.Abs((decimal)localPos.Quantity);

                        if (diff <= tolerance)
                            matched.Add(symbol);
                        else
                            divergent.Add(symbol);
                    }
                    else
                    {
                        missingLocal.Add(symbol);
                    }
                }

                foreach (var symbol in localPositions.Keys)
                {
                    if (!brokerageBySymbol.ContainsKey(symbol))
                        missingBrokerage.Add(symbol);
                }

                _consecutiveFailures = 0;
                return new ReconciliationReport(
                    AccountId: accountId,
                    ProviderName: providerName,
                    MatchedSymbols: matched,
                    DivergentSymbols: divergent,
                    MissingInPortfolio: missingLocal,
                    MissingInBrokerage: missingBrokerage,
                    ReconciledAt: DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _logger.LogWarning(ex,
                    "Position sync failed for provider={Provider} account={AccountId} (consecutive failures: {Count})",
                    providerName, accountId, _consecutiveFailures);
            }
        }

        return null;
    }

    private static IReadOnlyList<(string Name, IBrokeragePositionSync Sync)> ApplyPriority(
        List<(string Name, IBrokeragePositionSync Sync)> providers,
        IReadOnlyList<string> priority)
    {
        if (priority.Count == 0)
            return providers;

        var ordered = new List<(string Name, IBrokeragePositionSync Sync)>();

        // Add prioritised providers first.
        foreach (var name in priority)
        {
            var found = providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (found.Sync is not null)
                ordered.Add(found);
        }

        // Append any remaining providers not mentioned in the priority list.
        foreach (var p in providers)
        {
            if (!ordered.Any(o => string.Equals(o.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
                ordered.Add(p);
        }

        return ordered;
    }
}
