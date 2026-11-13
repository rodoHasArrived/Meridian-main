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
    /// <summary>Accounting differences with explicit economic categories.</summary>
    public IReadOnlyList<BrokerAccountingBreak> AccountingBreaks { get; init; } = [];

    /// <summary>Missing evidence prevents this report from proving full accounting coverage.</summary>
    public IReadOnlyList<string> CoverageIssues { get; init; } = [];

    public bool HasCompleteAccountingCoverage => CoverageIssues.Count == 0;

    /// <summary>Broker evidence timestamp; absent for position-only providers.</summary>
    public DateTimeOffset? BrokerAsOf { get; init; }

    /// <summary>True when no divergences were found.</summary>
    public bool IsClean => DivergentSymbols.Count == 0
        && MissingInPortfolio.Count == 0
        && MissingInBrokerage.Count == 0
        && AccountingBreaks.Count == 0;
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
        if (_options.DivergenceTolerance < 0m || _options.CashTolerance < 0m || _options.CostBasisTolerance < 0m)
            throw new ArgumentOutOfRangeException(nameof(options), "Reconciliation tolerances must be non-negative.");
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
                var snapshot = sync is IBrokeragePortfolioSync fullSync
                    ? await fullSync.GetPortfolioSnapshotAsync(accountId, ct).ConfigureAwait(false)
                    : null;
                if (snapshot is not null && !string.Equals(snapshot.Account.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Broker reconciliation returned a different account.");
                var brokeragePositions = snapshot is null
                    ? await sync.GetCurrentPositionsAsync(accountId, ct).ConfigureAwait(false)
                    : snapshot.Positions.Select(p => new BrokeragePositionDto(p.Symbol, p.Quantity, p.AverageEntryPrice, p.UnrealizedPnl)).ToArray();
                var localAccount = portfolio.GetAccount(accountId)?.TakeSnapshot();

                var localPositions = localAccount?.Positions.ToDictionary(p => p.Symbol, p => (IPosition)p, StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, IPosition>(StringComparer.OrdinalIgnoreCase);

                var matched = new List<string>();
                var divergent = new List<string>();
                var missingLocal = new List<string>();
                var missingBrokerage = new List<string>();

                var comparison = ReconciliationSetComparer.Compare(
                    localPositions.Values,
                    brokeragePositions,
                    static position => position.Symbol,
                    static position => position.Symbol,
                    StringComparer.OrdinalIgnoreCase);

                foreach (var match in comparison.Matches)
                {
                    var localPos = match.Local;
                    var brokerPos = match.External;
                    var diff = Math.Abs(localPos.ExactQuantity - brokerPos.Quantity);
                    var tolerance = _options.DivergenceTolerance * Math.Abs(localPos.ExactQuantity);

                    if (diff <= tolerance)
                        matched.Add(match.Key);
                    else
                        divergent.Add(match.Key);
                }

                foreach (var brokerPos in comparison.MissingLocal)
                {
                    missingLocal.Add(brokerPos.Symbol);
                }

                foreach (var localPos in comparison.MissingExternal)
                {
                    missingBrokerage.Add(localPos.Symbol);
                }

                var accountingBreaks = new List<BrokerAccountingBreak>();
                var coverageIssues = new List<string>();
                DateTimeOffset? brokerAsOf = null;
                // Position-only adapters retain their existing capability. Full snapshot
                // adapters additionally prove account ownership, cash and carrying prices.
                if (snapshot is not null && localAccount is not null)
                {
                    brokerAsOf = snapshot.RetrievedAt;
                    coverageIssues.Add("Realized P&L was not compared: the broker snapshot does not supply cumulative realized P&L for the local book's inception period. Reconcile retained fill/lot statements for that period.");
                    if (!snapshot.IsComplete)
                        accountingBreaks.Add(new(BrokerAccountingBreakCategory.UnknownActivity,
                            "IncompleteSnapshot", null, null, string.Join("; ", snapshot.CompletenessIssues ?? [])));
                    if (Math.Abs(localAccount.Cash - snapshot.Balance.Cash) > _options.CashTolerance)
                        accountingBreaks.Add(new(BrokerAccountingBreakCategory.AmountMismatch,
                            "Cash", localAccount.Cash, snapshot.Balance.Cash));
                    if (Math.Abs(localAccount.MarginBalance - snapshot.Balance.MarginBalance) > _options.CashTolerance)
                        accountingBreaks.Add(new(BrokerAccountingBreakCategory.AmountMismatch,
                            "MarginLoan", localAccount.MarginBalance, snapshot.Balance.MarginBalance));
                    foreach (var match in comparison.Matches)
                    {
                        if (Math.Abs(match.Local.AverageCostBasis - match.External.CostBasis) > _options.CostBasisTolerance)
                            accountingBreaks.Add(new(BrokerAccountingBreakCategory.AmountMismatch,
                                $"CostBasis:{match.Key}", match.Local.AverageCostBasis, match.External.CostBasis));
                    }
                    foreach (var symbol in divergent)
                        accountingBreaks.Add(new(BrokerAccountingBreakCategory.AmountMismatch,
                            $"Quantity:{symbol}", localPositions[symbol].ExactQuantity,
                            comparison.Matches.Single(m => string.Equals(m.Key, symbol, StringComparison.OrdinalIgnoreCase)).External.Quantity));
                    foreach (var symbol in missingLocal.Concat(missingBrokerage))
                        accountingBreaks.Add(new(BrokerAccountingBreakCategory.MissingEntry, $"Position:{symbol}", null, null));
                    if (sync is IBrokerageActivitySync activitySync)
                    {
                        var start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
                        var activity = await activitySync.GetActivitySnapshotAsync(accountId, start, ct).ConfigureAwait(false);
                        if (!string.Equals(activity.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Broker activity returned a different account.");
                        foreach (var entry in activity.Activities ?? [])
                        {
                            if (entry.Category == BrokerageActivityCategory.Other || entry.Subtype == BrokerageActivitySubtype.Other)
                                accountingBreaks.Add(new(BrokerAccountingBreakCategory.UnknownActivity,
                                    entry.EventId, null, entry.NetAmount, entry.ProviderCode));
                        }
                        if (activity.Cursor is { IsComplete: false })
                            accountingBreaks.Add(new(BrokerAccountingBreakCategory.UnknownActivity,
                                "ActivityRetrievalIncomplete", null, null,
                                "The activity cursor has not reached the end of the provider population."));
                    }
                }
                _consecutiveFailures = 0;
                return new ReconciliationReport(
                    AccountId: accountId,
                    ProviderName: providerName,
                    MatchedSymbols: matched,
                    DivergentSymbols: divergent,
                    MissingInPortfolio: missingLocal,
                    MissingInBrokerage: missingBrokerage,
                    ReconciledAt: DateTimeOffset.UtcNow)
                {
                    AccountingBreaks = accountingBreaks,
                    CoverageIssues = snapshot is null
                        ? ["Position-only provider: cash, carrying basis, realized P&L and activity coverage were not compared."]
                        : coverageIssues,
                    BrokerAsOf = brokerAsOf
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
