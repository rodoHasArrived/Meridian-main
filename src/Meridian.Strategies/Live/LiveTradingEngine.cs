using System.Collections.Concurrent;
using Meridian.Execution;
using Meridian.Execution.Live;
using Meridian.Execution.Services;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Strategies.Live;

/// <summary>
/// Runs promoted paper/live strategy runs against the live market data feed. This is the live
/// counterpart of <c>BacktestEngine</c>: promotion approval (or the startup resume sweep) hands
/// a recorded <see cref="StrategyRunEntry"/> to <see cref="TryLaunchAsync"/>, which resolves the
/// concrete strategy from the catalog, subscribes the run's universe on the feed, drives the
/// strategy callbacks per event, and routes resulting orders through the governed
/// <see cref="IOrderManager"/> (OMS pre-trade gates included). Fills stream back from the OMS
/// execution-report channel into <c>OnOrderFill</c>, closing the trading loop.
/// </summary>
public sealed class LiveTradingEngine : IPromotedRunLauncher, IAsyncDisposable
{
    private const string ActorId = "live-trading-engine";

    private readonly ILiveStrategyCatalog _catalog;
    private readonly ILiveMarketEventFeed _feed;
    private readonly ILiveFeedAdapter _feedAdapter;
    private readonly IOrderGateway _orderGateway;
    private readonly IOrderManager _orderManager;
    private readonly Meridian.Execution.Models.IPortfolioState _portfolioState;
    private readonly IStrategyRepository _repository;
    private readonly LiveTradingEngineOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LiveTradingEngine> _logger;
    private readonly StrategyLifecycleManager? _lifecycleManager;
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly BrokerageConfiguration? _brokerageConfiguration;
    private readonly CancellationTokenSource _engineCts = new();
    private readonly ConcurrentDictionary<string, ActiveRun> _activeRuns = new(StringComparer.Ordinal);
    // Cross-strategy aggregation and the portfolio-aware risk rails read the registry, so
    // an active run's portfolio must appear under its real run id rather than only under
    // the host placeholder. Registering the shared host instance a second time is safe:
    // AggregatePortfolioService deduplicates by instance.
    private readonly Meridian.Execution.Services.PortfolioRegistry? _portfolioRegistry;
    private readonly Lock _pumpLock = new();
    private Task? _reportPumpTask;
    private int _disposed;

    public LiveTradingEngine(
        ILiveStrategyCatalog catalog,
        ILiveMarketEventFeed feed,
        ILiveFeedAdapter feedAdapter,
        IOrderGateway orderGateway,
        IOrderManager orderManager,
        Meridian.Execution.Models.IPortfolioState portfolioState,
        IStrategyRepository repository,
        LiveTradingEngineOptions? options = null,
        ILoggerFactory? loggerFactory = null,
        StrategyLifecycleManager? lifecycleManager = null,
        ExecutionAuditTrailService? auditTrail = null,
        BrokerageConfiguration? brokerageConfiguration = null,
        Meridian.Execution.Services.PortfolioRegistry? portfolioRegistry = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _feedAdapter = feedAdapter ?? throw new ArgumentNullException(nameof(feedAdapter));
        _orderGateway = orderGateway ?? throw new ArgumentNullException(nameof(orderGateway));
        _orderManager = orderManager ?? throw new ArgumentNullException(nameof(orderManager));
        _portfolioState = portfolioState ?? throw new ArgumentNullException(nameof(portfolioState));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _options = options ?? new LiveTradingEngineOptions();
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<LiveTradingEngine>();
        _lifecycleManager = lifecycleManager;
        _auditTrail = auditTrail;
        _brokerageConfiguration = brokerageConfiguration;
        _portfolioRegistry = portfolioRegistry;
    }

    /// <summary>Run ids currently executing on this engine.</summary>
    public IReadOnlyCollection<string> ActiveRunIds => _activeRuns.Keys.ToArray();

    /// <inheritdoc/>
    public async Task<RunLaunchResult> TryLaunchAsync(StrategyRunEntry run, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (!_options.Enabled)
        {
            return await DeferAsync(run, "The live trading engine is disabled on this host.", ct).ConfigureAwait(false);
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            return await DeferAsync(run, "The live trading engine is shutting down.", ct).ConfigureAwait(false);
        }

        if (run.RunType is not (RunType.Paper or RunType.Live))
        {
            return RunLaunchResult.Deferred($"Run type {run.RunType} is not executable on the live trading engine.");
        }

        if (run.EndedAt.HasValue)
        {
            return RunLaunchResult.Deferred("Run has already ended.");
        }

        if (run.RunType == RunType.Live)
        {
            if (!_options.AllowLiveRuns)
            {
                return await DeferAsync(
                    run,
                    $"Live runs are disabled ({LiveTradingEngineOptions.SectionKey}:AllowLiveRuns is false); the run stays retained until live execution is enabled.",
                    ct).ConfigureAwait(false);
            }

            if (_brokerageConfiguration is not { LiveExecutionEnabled: true })
            {
                return await DeferAsync(
                    run,
                    "Live runs require BrokerageConfiguration.LiveExecutionEnabled; the run stays retained until brokerage routing is enabled.",
                    ct).ConfigureAwait(false);
            }
        }

        if (_activeRuns.ContainsKey(run.RunId))
        {
            return RunLaunchResult.Success();
        }

        if (!_catalog.TryCreate(run.StrategyId, run.ParameterSet, out var strategy, out var failureReason) || strategy is null)
        {
            return await DeferAsync(run, failureReason ?? "No live strategy implementation available.", ct).ConfigureAwait(false);
        }

        var universe = ResolveUniverse(run);
        if (universe.Count == 0)
        {
            return await DeferAsync(
                run,
                "Run has no trading universe: set the 'symbols' run parameter or configure " +
                $"{LiveTradingEngineOptions.SectionKey}:DefaultSymbols.",
                ct).ConfigureAwait(false);
        }

        var context = new LiveStrategyExecutionContext(_orderGateway, _feedAdapter, _portfolioState, universe);
        var session = new LiveStrategyRunSession(
            run,
            strategy,
            context,
            _feed,
            _orderManager,
            _repository,
            _auditTrail,
            _loggerFactory.CreateLogger<LiveStrategyRunSession>(),
            useSynchronousFillFallback: !StartReportPumpIfNeeded(),
            fillReportQueueCapacity: Math.Max(16, _options.FillReportQueueCapacity));

        var activeRun = new ActiveRun(session);
        if (!_activeRuns.TryAdd(run.RunId, activeRun))
        {
            return RunLaunchResult.Success();
        }

        if (_portfolioRegistry is not null &&
            _portfolioState is Meridian.Execution.Models.IMultiAccountPortfolioState runPortfolio)
        {
            _portfolioRegistry.Register(run.RunId, runPortfolio);
        }

        TryRegisterWithLifecycleManager(strategy);
        activeRun.Execution = Task.Run(
            async () =>
            {
                try
                {
                    await session.ExecuteAsync(_engineCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    _activeRuns.TryRemove(run.RunId, out _);
                    _portfolioRegistry?.Deregister(run.RunId);
                }
            },
            CancellationToken.None);

        _logger.LogInformation(
            "Live trading engine activated run {RunId} (strategy {StrategyId}, mode {RunType}, universe [{Universe}])",
            run.RunId, run.StrategyId, run.RunType, string.Join(", ", universe));
        await RecordAuditAsync(
            run,
            action: "LiveRunActivated",
            outcome: "Activated",
            message: $"Run activated with universe [{string.Join(", ", universe)}].",
            ct).ConfigureAwait(false);

        return RunLaunchResult.Success();
    }

    /// <summary>
    /// Stops an active run, records its terminal state and metrics, and returns whether the
    /// run was active on this engine.
    /// </summary>
    public async Task<bool> StopRunAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (!_activeRuns.TryGetValue(runId, out var activeRun))
        {
            return false;
        }

        activeRun.Session.RequestStop(completeRun: true);
        if (activeRun.Execution is { } execution)
        {
            await execution.WaitAsync(ct).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Startup sweep: re-activates promoted paper/live runs that were recorded but never
    /// finished (host restart, engine previously missing). Returns the number of runs
    /// activated.
    /// </summary>
    public async Task<int> ResumePendingRunsAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return 0;
        }

        var resumed = 0;
        await foreach (var run in _repository.GetAllRunsAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (run.RunType is not (RunType.Paper or RunType.Live)
                || run.EndedAt.HasValue
                || run.TerminalStatus is not null)
            {
                continue;
            }

            var result = await TryLaunchAsync(run, ct).ConfigureAwait(false);
            if (result.Launched)
            {
                resumed++;
            }
            else
            {
                _logger.LogInformation(
                    "Run {RunId} (strategy {StrategyId}) was not resumed: {Reason}",
                    run.RunId, run.StrategyId, result.Reason);
            }
        }

        if (resumed > 0)
        {
            _logger.LogInformation("Live trading engine resumed {Count} pending run(s).", resumed);
        }

        return resumed;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Leave run entries open so a restarted host resumes them; only the sessions stop.
        foreach (var activeRun in _activeRuns.Values)
        {
            activeRun.Session.RequestStop(completeRun: false);
        }

        _engineCts.Cancel();
        var executions = _activeRuns.Values
            .Select(static activeRun => activeRun.Execution)
            .Where(static execution => execution is not null)
            .Cast<Task>()
            .ToArray();
        if (executions.Length > 0)
        {
            try
            {
                await Task.WhenAll(executions).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "One or more live sessions faulted during engine shutdown.");
            }
        }

        if (_reportPumpTask is { } pump)
        {
            try
            {
                await pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: the pump consumes the OMS report stream with the engine token.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Execution report pump faulted during engine shutdown.");
            }
        }

        _engineCts.Dispose();
    }

    private IReadOnlySet<string> ResolveUniverse(StrategyRunEntry run)
    {
        static IEnumerable<string> Split(string raw) =>
            raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (run.ParameterSet is { } parameters)
        {
            if (parameters.TryGetValue("symbols", out var multi) && !string.IsNullOrWhiteSpace(multi))
            {
                symbols.UnionWith(Split(multi));
            }

            if (parameters.TryGetValue("symbol", out var single) && !string.IsNullOrWhiteSpace(single))
            {
                symbols.UnionWith(Split(single));
            }
        }

        // A null-valued configuration binding can leave DefaultSymbols null despite the
        // non-null property default; treat that the same as "no fallback configured".
        if (symbols.Count == 0 && _options.DefaultSymbols is { } defaultSymbols)
        {
            symbols.UnionWith(defaultSymbols.Where(static symbol => !string.IsNullOrWhiteSpace(symbol)));
        }

        return symbols;
    }

    /// <summary>
    /// Starts the shared OMS execution-report pump once. Returns <c>true</c> when a pump is
    /// available (fills arrive via the report stream), <c>false</c> when the configured order
    /// manager exposes no report channel and sessions must use the synchronous fill fallback.
    /// </summary>
    private bool StartReportPumpIfNeeded()
    {
        if (_orderManager is not OrderManagementSystem oms)
        {
            return false;
        }

        lock (_pumpLock)
        {
            _reportPumpTask ??= Task.Run(() => PumpExecutionReportsAsync(oms, _engineCts.Token), CancellationToken.None);
        }

        return true;
    }

    private async Task PumpExecutionReportsAsync(OrderManagementSystem oms, CancellationToken ct)
    {
        await foreach (var report in oms.ExecutionReports.ReadAllAsync(ct).ConfigureAwait(false))
        {
            var clientOrderId = report.ClientOrderId ?? report.OrderId;
            if (!LiveStrategyRunSession.TryParseRunId(clientOrderId, out var runId))
            {
                continue;
            }

            if (_activeRuns.TryGetValue(runId, out var activeRun))
            {
                activeRun.Session.EnqueueExecutionReport(report);
            }
        }
    }

    private void TryRegisterWithLifecycleManager(ILiveStrategy strategy)
    {
        if (_lifecycleManager is null)
        {
            return;
        }

        try
        {
            _lifecycleManager.Register(strategy);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Strategy {StrategyId} could not be registered with the lifecycle manager; cockpit pause/stop will be unavailable for it.",
                strategy.StrategyId);
        }
    }

    private async Task<RunLaunchResult> DeferAsync(StrategyRunEntry run, string reason, CancellationToken ct)
    {
        _logger.LogWarning(
            "Run {RunId} (strategy {StrategyId}) was not activated: {Reason}",
            run.RunId, run.StrategyId, reason);
        await RecordAuditAsync(run, "LiveRunActivationDeferred", "Deferred", reason, ct).ConfigureAwait(false);
        return RunLaunchResult.Deferred(reason);
    }

    private async Task RecordAuditAsync(
        StrategyRunEntry run,
        string action,
        string outcome,
        string message,
        CancellationToken ct)
    {
        if (_auditTrail is null)
        {
            return;
        }

        try
        {
            await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: Guid.NewGuid().ToString("N"),
                Category: "Execution",
                Action: action,
                Outcome: outcome,
                OccurredAt: DateTimeOffset.UtcNow,
                Actor: ActorId,
                RunId: run.RunId,
                CorrelationId: run.RunId,
                Message: message,
                Scope: $"run:{run.RunId}/strategy:{run.StrategyId}/mode:{run.RunType}"), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Live engine audit entry {Action} for run {RunId} could not be recorded.", action, run.RunId);
        }
    }

    private sealed class ActiveRun(LiveStrategyRunSession session)
    {
        public LiveStrategyRunSession Session { get; } = session;

        public Task? Execution { get; set; }
    }
}
