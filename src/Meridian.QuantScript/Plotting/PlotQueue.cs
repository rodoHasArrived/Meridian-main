using System.Collections.Concurrent;

namespace Meridian.QuantScript.Plotting;

/// <summary>
/// Collects <see cref="PlotRequest"/> objects emitted by script APIs (e.g.
/// <see cref="Api.PriceSeries.Plot"/>) during a single execution run and makes
/// them available to the host UI after the run completes.
/// </summary>
/// <remarks>
/// <see cref="Current"/> uses <see cref="ScriptContext"/> (AsyncLocal) so that plot
/// emission is correct across <c>await</c> continuations (ADR-004).
/// </remarks>
public sealed class PlotQueue : IDisposable
{
    private readonly ConcurrentQueue<PlotRequest> _queue = new();
    private bool _completed;

    /// <summary>
    /// The <see cref="PlotQueue"/> associated with the currently-executing script run,
    /// or null if no run is active. Uses <see cref="ScriptContext"/> (AsyncLocal) so
    /// that plot emission works correctly across <c>await</c> continuations.
    /// </summary>
    public static PlotQueue? Current
    {
        get => ScriptContext.PlotQueue;
        internal set => ScriptContext.PlotQueue = value;
    }

    /// <summary>Enqueues a plot request for rendering after the run completes.</summary>
    public void Enqueue(PlotRequest request) => _queue.Enqueue(request);

    /// <summary>
    /// Signals that the current script run has finished emitting plots.
    /// Called by <see cref="Compilation.ScriptRunner"/> in its finally block.
    /// </summary>
    public void Complete() => _completed = true;

    /// <summary>
    /// Drains all enqueued <see cref="PlotRequest"/> items into a list.
    /// Should be called once <see cref="Complete"/> has been signalled.
    /// </summary>
    internal IReadOnlyList<PlotRequest> DrainRemaining()
    {
        var result = new List<PlotRequest>();
        while (_queue.TryDequeue(out var item))
            result.Add(item);
        return result;
    }

    public void Dispose() { /* no resources to release */ }
}
