using Meridian.Strategies.Models;

namespace Meridian.Strategies.Services;

/// <summary>
/// Manages the lifecycle of registered <see cref="ILiveStrategy"/> instances.
/// Tracks status transitions and coordinates startup, pause, and stop across strategies.
/// Enforced by ADR-016.
/// </summary>
[ImplementsAdr("ADR-016", "Strategies pillar — strategy lifecycle orchestration")]
public sealed class StrategyLifecycleManager : IAsyncDisposable
{
    private readonly IStrategyRepository _repository;
    private readonly ILogger<StrategyLifecycleManager> _logger;
    private readonly Dictionary<string, (ILiveStrategy Strategy, StrategyRunEntry? CurrentRun)> _registered = new();
    private readonly Lock _lock = new();

    /// <summary>Creates a new lifecycle manager.</summary>
    public StrategyLifecycleManager(
        IStrategyRepository repository,
        ILogger<StrategyLifecycleManager> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>Registers a strategy so it can be started by this manager.</summary>
    public void Register(ILiveStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        lock (_lock)
        {
            _registered[strategy.StrategyId] = (strategy, null);
        }

        _logger.LogInformation("Strategy registered: {StrategyId} ({Name})", strategy.StrategyId, strategy.Name);
    }

    /// <summary>
    /// Starts a registered strategy against <paramref name="ctx"/>.
    /// Opens a new run record in the repository.
    /// </summary>
    public async Task StartAsync(string strategyId, IExecutionContext ctx, RunType runType, CancellationToken ct = default)
    {
        ILiveStrategy strategy;
        lock (_lock)
        {
            if (!_registered.TryGetValue(strategyId, out var entry))
            {
                throw new InvalidOperationException($"Strategy '{strategyId}' is not registered.");
            }

            strategy = entry.Strategy;
        }

        var run = StrategyRunEntry.Start(strategyId, strategy.Name, runType);
        await _repository.RecordRunAsync(run, ct).ConfigureAwait(false);

        lock (_lock)
        {
            _registered[strategyId] = (strategy, run);
        }

        _logger.LogInformation("Starting strategy {StrategyId} (run {RunId}, mode {RunType})",
            strategyId, run.RunId, runType);

        await strategy.StartAsync(ctx, ct).ConfigureAwait(false);
    }

    /// <summary>Pauses a running strategy.</summary>
    public async Task PauseAsync(string strategyId, CancellationToken ct = default)
    {
        ILiveStrategy strategy = GetRegistered(strategyId);
        _logger.LogInformation("Pausing strategy {StrategyId}", strategyId);
        await strategy.PauseAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops a running strategy and closes its run record in the repository.
    /// </summary>
    public async Task StopAsync(string strategyId, CancellationToken ct = default)
    {
        ILiveStrategy strategy;
        StrategyRunEntry? currentRun;

        lock (_lock)
        {
            if (!_registered.TryGetValue(strategyId, out var entry))
            {
                throw new InvalidOperationException($"Strategy '{strategyId}' is not registered.");
            }

            strategy = entry.Strategy;
            currentRun = entry.CurrentRun;
        }

        _logger.LogInformation("Stopping strategy {StrategyId}", strategyId);
        await strategy.StopAsync(ct).ConfigureAwait(false);

        if (currentRun is not null)
        {
            var completed = currentRun.Complete(metrics: null);
            await _repository.RecordRunAsync(completed, ct).ConfigureAwait(false);

            lock (_lock)
            {
                _registered[strategyId] = (strategy, completed);
            }
        }
    }

    /// <summary>Returns a snapshot of all registered strategy IDs and their current status.</summary>
    public IReadOnlyDictionary<string, StrategyStatus> GetStatuses()
    {
        lock (_lock)
        {
            return _registered.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Strategy.Status);
        }
    }

    private ILiveStrategy GetRegistered(string strategyId)
    {
        lock (_lock)
        {
            if (!_registered.TryGetValue(strategyId, out var entry))
            {
                throw new InvalidOperationException($"Strategy '{strategyId}' is not registered.");
            }

            return entry.Strategy;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        string[] ids;
        lock (_lock)
        {
            ids = [.. _registered.Keys];
        }

        foreach (var id in ids)
        {
            try
            {
                await StopAsync(id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping strategy {StrategyId} during dispose", id);
            }
        }
    }
}
