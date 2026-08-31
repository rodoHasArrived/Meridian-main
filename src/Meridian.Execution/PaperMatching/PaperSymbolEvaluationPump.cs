using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.PaperMatching;

/// <summary>
/// Coalescing per-symbol work scheduler for resting-order re-evaluation. A poke while idle
/// schedules one evaluation on the thread pool; a poke while an evaluation runs marks the
/// symbol dirty so exactly one follow-up evaluation reruns against the newest observation.
/// This keeps the market-data path non-blocking and bounds concurrent evaluations to one
/// per symbol.
/// </summary>
internal sealed class PaperSymbolEvaluationPump : IAsyncDisposable
{
    private const int Idle = 0;
    private const int Running = 1;
    private const int RunningDirty = 2;

    private readonly Func<string, Task> _evaluateSymbolAsync;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, int> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task> _pumpTasks = new(StringComparer.OrdinalIgnoreCase);
    private int _disposed;

    public PaperSymbolEvaluationPump(Func<string, Task> evaluateSymbolAsync, ILogger logger)
    {
        _evaluateSymbolAsync = evaluateSymbolAsync ?? throw new ArgumentNullException(nameof(evaluateSymbolAsync));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Schedules (or coalesces) an evaluation for <paramref name="symbol"/>.</summary>
    public void Poke(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        while (true)
        {
            var current = _states.GetOrAdd(symbol, Idle);
            switch (current)
            {
                case Idle:
                    if (_states.TryUpdate(symbol, Running, Idle))
                    {
                        _pumpTasks[symbol] = Task.Run(() => PumpAsync(symbol));
                        return;
                    }

                    continue;
                case Running:
                    if (_states.TryUpdate(symbol, RunningDirty, Running))
                    {
                        return;
                    }

                    continue;
                default:
                    // Already dirty: the queued follow-up run will observe the newest data.
                    return;
            }
        }
    }

    private async Task PumpAsync(string symbol)
    {
        while (true)
        {
            try
            {
                await _evaluateSymbolAsync(symbol).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Paper resting-order evaluation failed for {Symbol}.", symbol);
            }

            // Transition Running -> Idle; a RunningDirty state means new data arrived while
            // evaluating, so run once more against the latest observation.
            if (_states.TryUpdate(symbol, Idle, Running))
            {
                _pumpTasks.TryRemove(symbol, out _);
                return;
            }

            _states.TryUpdate(symbol, Running, RunningDirty);
        }
    }

    /// <summary>Waits for in-flight evaluations so disposal cannot orphan a fill emission.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var pending = _pumpTasks.Values.ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paper resting-order evaluation observed a failure during disposal.");
        }
    }
}
