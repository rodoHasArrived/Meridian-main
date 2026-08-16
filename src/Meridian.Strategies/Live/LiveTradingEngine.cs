using System.Collections.Concurrent;
using System.Threading.Channels;
using Meridian.Execution;
using Meridian.Execution.Live;
using Meridian.Execution.Services;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging.Abstractions;
using ExecutionSdk = Meridian.Execution.Sdk;

namespace Meridian.Strategies.Live;

/// <summary>
/// Runs promoted paper/live strategy runs against the live market data feed. This is the live
/// counterpart of <c>BacktestEngine</c>: promotion approval (or the startup resume sweep) hands
/// a recorded <see cref="StrategyRunEntry"/> to <see cref="TryLaunchAsync"/>, which resolves the
/// concrete strategy from the catalog, subscribes the run's universe on the feed, drives the
/// strategy callbacks per event, and routes resulting orders through the governed
/// <see cref="IOrderManager"/> (OMS pre-trade gates included). Fills stream back from an OMS
/// delivery-or-accounted execution-report subscription into <c>OnOrderFill</c>, closing the
/// trading loop without allowing one slow run to block every other run.
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
    private readonly object _disposeSync = new();
    private Task? _reportPumpTask;
    private Task? _disposeTask;
    private TaskCompletionSource? _launchesDrained;
    private int _activeLaunches;
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

        var launchLease = TryEnterLaunch();
        if (launchLease is null)
        {
            return RunLaunchResult.Deferred("The live trading engine is shutting down.");
        }

        using (launchLease)
        {
            if (!_options.Enabled)
            {
                return await DeferAsync(run, "The live trading engine is disabled on this host.", ct).ConfigureAwait(false);
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

            var fillQueueCapacity = Math.Max(1, _options.FillReportQueueCapacity);
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
                useSynchronousFillFallback: _orderManager is not OrderManagementSystem,
                fillReportQueueCapacity: fillQueueCapacity);

            var activeRun = new ActiveRun(session, fillQueueCapacity);
            lock (_disposeSync)
            {
                // This commit check and DisposeAsync's admission close share one lock. A launch is
                // therefore either visible to shutdown or deferred; it cannot materialize after
                // the shutdown snapshot and escape lifecycle ownership.
                if (_disposed != 0)
                {
                    activeRun.CompleteReportAdmission();
                    session.CompleteExecutionReportAdmission();
                    return RunLaunchResult.Deferred("The live trading engine is shutting down.");
                }

                if (!_activeRuns.TryAdd(run.RunId, activeRun))
                {
                    activeRun.CompleteReportAdmission();
                    session.CompleteExecutionReportAdmission();
                    return RunLaunchResult.Success();
                }
            }

            try
            {
                StartReportPumpIfNeeded();
                activeRun.StartDeliveryWorker(() => DeliverReportsToRunAsync(activeRun));

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
                            activeRun.CompleteReportAdmission();
                            try
                            {
                                await activeRun.Delivery.ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex,
                                    "Execution-report delivery worker for run {RunId} faulted during retirement.",
                                    run.RunId);
                            }

                            _activeRuns.TryRemove(run.RunId, out _);
                            _portfolioRegistry?.Deregister(run.RunId);
                        }
                    },
                    CancellationToken.None);
                activeRun.CompleteInitialization();
            }
            catch (Exception ex)
            {
                _activeRuns.TryRemove(run.RunId, out _);
                _portfolioRegistry?.Deregister(run.RunId);
                activeRun.CompleteReportAdmission();
                session.CompleteExecutionReportAdmission();
                activeRun.CompleteInitialization();
                return await DeferAsync(
                    run,
                    $"The live run could not initialize its execution-report lifecycle: {ex.Message}",
                    ct).ConfigureAwait(false);
            }

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

        await activeRun.Initialized.WaitAsync(ct).ConfigureAwait(false);
        if (!_activeRuns.TryGetValue(runId, out var initializedRun)
            || !ReferenceEquals(activeRun, initializedRun))
        {
            return false;
        }

        activeRun.Session.CloseOutboundOrderAdmission();
        activeRun.CompleteReportAdmission();
        await activeRun.Delivery.WaitAsync(ct).ConfigureAwait(false);
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
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            Volatile.Write(ref _disposed, 1);
            foreach (var activeRun in _activeRuns.Values)
            {
                activeRun.Session.CloseOutboundOrderAdmission();
            }

            var launchesDrained = _activeLaunches == 0
                ? Task.CompletedTask
                : (_launchesDrained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;

            // Closing engine launch admission and OMS outbound-order admission occurs under this
            // one lifecycle critical section. OMS operations that crossed their own gate first
            // remain admitted and are drained; later submissions fail closed.
            var omsShutdown = _orderManager is OrderManagementSystem oms
                ? oms.DisposeAsync().AsTask()
                : null;
            _disposeTask = DisposeCoreAsync(launchesDrained, omsShutdown);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync(Task launchesDrained, Task? omsShutdown)
    {
        var shutdownFailures = new List<Exception>();

        // An admitted launch either committed its run before the lifecycle gate closed or observed
        // shutdown and deferred. No launch can appear after this barrier.
        await launchesDrained.ConfigureAwait(false);

        // Keep the subscription pump, per-run delivery workers, and session event loops alive while
        // OMS order operations and dequeued gateway reports complete their authoritative handoffs.
        if (omsShutdown is not null)
        {
            try
            {
                await omsShutdown.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                shutdownFailures.Add(ex);
                _logger.LogError(ex, "Order management shutdown failed while live fill consumers remained active.");
            }
        }

        Task? reportPump;
        lock (_pumpLock)
        {
            reportPump = _reportPumpTask;
        }

        if (reportPump is not null)
        {
            try
            {
                await reportPump.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_engineCts.IsCancellationRequested)
            {
                // Expected only if another shutdown path already cancelled the engine token.
            }
            catch (Exception ex)
            {
                shutdownFailures.Add(ex);
                _logger.LogError(ex, "Execution report pump faulted during engine shutdown.");
            }
        }

        var activeRuns = _activeRuns.Values.ToArray();
        foreach (var activeRun in activeRuns)
        {
            activeRun.CompleteReportAdmission();
        }

        var deliveryWorkers = activeRuns.Select(static activeRun => activeRun.Delivery).ToArray();
        if (deliveryWorkers.Length > 0)
        {
            try
            {
                await Task.WhenAll(deliveryWorkers).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                shutdownFailures.Add(ex);
                _logger.LogError(ex, "One or more per-run execution-report delivery workers failed during shutdown.");
            }
        }

        // Leave run entries open so a restarted host resumes them; only the sessions stop after
        // every report admitted before the OMS/subscription boundary has drained.
        foreach (var activeRun in activeRuns)
        {
            activeRun.Session.RequestStop(completeRun: false);
        }

        try
        {
            await _engineCts.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            shutdownFailures.Add(ex);
        }

        var executions = activeRuns
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
                shutdownFailures.Add(ex);
                _logger.LogError(ex, "One or more live sessions faulted during engine shutdown.");
            }
        }

        _engineCts.Dispose();

        if (shutdownFailures.Count == 1)
        {
            throw shutdownFailures[0];
        }

        if (shutdownFailures.Count > 1)
        {
            throw new AggregateException(shutdownFailures);
        }
    }

    private LaunchLease? TryEnterLaunch()
    {
        lock (_disposeSync)
        {
            if (_disposed != 0)
            {
                return null;
            }

            checked
            {
                _activeLaunches++;
            }

            return new LaunchLease(this);
        }
    }

    private void ExitLaunch()
    {
        lock (_disposeSync)
        {
            _activeLaunches--;
            if (_activeLaunches == 0)
            {
                _launchesDrained?.TrySetResult();
            }
        }
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
    /// Starts the shared OMS accounted execution-report pump once. The pump itself creates and
    /// owns the subscription so every exit path unsubscribes in its <c>finally</c> block.
    /// </summary>
    private void StartReportPumpIfNeeded()
    {
        if (_orderManager is not OrderManagementSystem oms)
        {
            return;
        }

        lock (_pumpLock)
        {
            if (_reportPumpTask is null)
            {
                // Calling the async method directly runs subscription creation synchronously up to
                // its first read await, so launch fails deterministically if OMS admission closed.
                _reportPumpTask = PumpExecutionReportsAsync(oms, _engineCts.Token);
            }
            else if (_reportPumpTask.IsCompleted)
            {
                throw new InvalidOperationException(
                    "The authoritative execution-report pump exited before engine shutdown.",
                    _reportPumpTask.Exception);
            }
        }
    }

    private async Task PumpExecutionReportsAsync(
        OrderManagementSystem oms,
        CancellationToken ct)
    {
        var subscription = oms.SubscribeLosslessExecutionReports(
            subscriberName: "live-trading-engine",
            undeliverableHandler: AccountUndeliverableExecutionReportAsync);
        var subscriptionFailed = false;
        try
        {
            await foreach (var report in subscription.Reports.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var clientOrderId = report.ClientOrderId ?? report.OrderId;
                if (!LiveStrategyRunSession.TryParseRunId(clientOrderId, out var runId))
                {
                    continue;
                }

                if (_activeRuns.TryGetValue(runId, out var activeRun)
                    && activeRun.TryAdmitReport(report))
                {
                    continue;
                }

                await AccountUndeliverableExecutionReportAsync(
                        report,
                        _activeRuns.ContainsKey(runId)
                            ? $"Run '{runId}' reached its bounded delivery capacity."
                            : $"Run '{runId}' retired before the report reached its session.",
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            subscriptionFailed = true;
            try
            {
                await subscription.FailAsync(ex).ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "The live execution-report consumer and its fail-closed cleanup both failed.",
                    ex,
                    cleanupFailure);
            }

            throw;
        }
        finally
        {
            if (!subscriptionFailed)
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task DeliverReportsToRunAsync(ActiveRun activeRun)
    {
        await foreach (var report in activeRun.Reports.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            try
            {
                await activeRun.Session
                    .EnqueueExecutionReportAsync(report, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                await AccountUndeliverableExecutionReportAsync(
                        report,
                        $"Run '{activeRun.Session.RunId}' closed its fill inbox before delivery.",
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await AccountUndeliverableExecutionReportAsync(
                        report,
                        $"Run '{activeRun.Session.RunId}' rejected fill delivery: {ex.Message}",
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        activeRun.Session.CompleteExecutionReportAdmission();
    }

    private async ValueTask AccountUndeliverableExecutionReportAsync(
        ExecutionSdk.ExecutionReport report,
        string reason,
        CancellationToken ct)
    {
        var clientOrderId = report.ClientOrderId ?? report.OrderId;
        if (!LiveStrategyRunSession.TryParseRunId(clientOrderId, out var runId))
        {
            throw new OrderManagementSystem.ExecutionReportDeliveryException(
                report,
                $"{reason} The report has no live-run identity.");
        }

        var failure = new InvalidOperationException(
            $"Accepted execution report for run '{runId}' could not reach its strategy session. "
            + $"Order '{clientOrderId}', gateway order '{report.GatewayOrderId ?? "missing"}', "
            + $"report {report.ReportType}, status {report.OrderStatus}, symbol '{report.Symbol}', "
            + $"side {report.Side}, order quantity "
            + $"{report.OrderQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}, fill "
            + $"{report.FilledQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)} at "
            + $"{report.FillPrice?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "missing"}, "
            + $"commission {report.Commission?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "missing"}, "
            + $"report time {report.Timestamp:O}. {reason}");

        _activeRuns.TryGetValue(runId, out var activeRun);
        activeRun?.CompleteReportAdmission();

        Exception? repositoryFailure = null;
        try
        {
            var retainedRun = await _repository.GetRunByIdAsync(runId, CancellationToken.None).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"No retained strategy run '{runId}' exists for the late fill.");
            await _repository.RecordLifecycleEventAsync(
                    retainedRun.Fail(failure, "An accepted broker fill could not reach the live strategy session."),
                    StrategyRunLifecycleEventType.Failed,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            repositoryFailure = ex;
        }

        var auditRetained = false;
        if (_auditTrail is not null)
        {
            try
            {
                await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                        AuditId: $"audit-{Guid.NewGuid():N}",
                        Category: "Execution",
                        Action: "LiveRunFillDeliveryFailed",
                        Outcome: "Failed",
                        OccurredAt: DateTimeOffset.UtcNow,
                        Actor: ActorId,
                        BrokerName: _orderGateway.BrokerName,
                        OrderId: clientOrderId,
                        RunId: runId,
                        Symbol: report.Symbol,
                        CorrelationId: runId,
                        Message: failure.Message,
                        Reason: "strategy-fill-undeliverable",
                        Scope: $"run:{runId}/order:{clientOrderId}",
                        Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["reportType"] = report.ReportType.ToString(),
                            ["orderStatus"] = report.OrderStatus.ToString(),
                            ["side"] = report.Side.ToString(),
                            ["orderQuantity"] = report.OrderQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["filledQuantity"] = report.FilledQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["fillPrice"] = report.FillPrice?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "missing",
                            ["commission"] = report.Commission?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "missing",
                            ["gatewayOrderId"] = report.GatewayOrderId ?? string.Empty,
                            ["reportTimestampUtc"] = report.Timestamp.ToUniversalTime().ToString("O")
                        }), CancellationToken.None)
                    .ConfigureAwait(false);
                auditRetained = true;
            }
            catch (Exception ex)
            {
                repositoryFailure = repositoryFailure is null
                    ? ex
                    : new AggregateException(repositoryFailure, ex);
            }
        }

        activeRun?.Session.RequestFailure(failure);
        _logger.LogError(
            failure,
            "Live execution report for run {RunId}, order {OrderId}, was not delivered to its session.",
            runId,
            clientOrderId);

        if (repositoryFailure is not null && !auditRetained)
        {
            throw new OrderManagementSystem.ExecutionReportDeliveryException(
                report,
                $"{reason} Neither the run repository nor durable execution audit retained the failure: {repositoryFailure.Message}");
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

    private sealed class ActiveRun
    {
        private readonly Channel<ExecutionSdk.ExecutionReport> _reports;
        private readonly TaskCompletionSource _initialized =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reportAdmissionClosed;

        public ActiveRun(LiveStrategyRunSession session, int reportCapacity)
        {
            Session = session;
            _reports = Channel.CreateBounded<ExecutionSdk.ExecutionReport>(new BoundedChannelOptions(reportCapacity)
            {
                SingleReader = true,
                // The pump is the only data writer, but run stop/session retirement can complete
                // admission concurrently with that write.
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        }

        public LiveStrategyRunSession Session { get; }

        public ChannelReader<ExecutionSdk.ExecutionReport> Reports => _reports.Reader;

        public Task Delivery { get; private set; } = Task.CompletedTask;

        public Task? Execution { get; set; }

        public Task Initialized => _initialized.Task;

        public bool TryAdmitReport(ExecutionSdk.ExecutionReport report)
            => Volatile.Read(ref _reportAdmissionClosed) == 0
               && _reports.Writer.TryWrite(report);

        public void CompleteReportAdmission()
        {
            if (Interlocked.Exchange(ref _reportAdmissionClosed, 1) == 0)
            {
                _reports.Writer.TryComplete();
            }
        }

        public void StartDeliveryWorker(Func<Task> worker)
        {
            ArgumentNullException.ThrowIfNull(worker);
            Delivery = Task.Run(worker, CancellationToken.None);
        }

        public void CompleteInitialization() => _initialized.TrySetResult();
    }

    private sealed class LaunchLease(LiveTradingEngine owner) : IDisposable
    {
        private LiveTradingEngine? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.ExitLaunch();
    }
}
