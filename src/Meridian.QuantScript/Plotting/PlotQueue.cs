using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Meridian.QuantScript.Plotting;

/// <summary>
/// Thread-safe unbounded queue of plot requests produced by scripts and
/// consumed by the WPF results panel. Backed by <see cref="Channel{PlotRequest}"/>.
/// </summary>
/// <remarks>
/// ADR-013 note: This is intentionally unbounded because scripts producing thousands of
/// charts is a user error, not a production throughput concern. The <see cref="QuantScriptOptions.MaxPlotsPerRun"/>
/// option provides a soft guard against runaway plot generation.
/// </remarks>
public sealed class PlotQueue : IDisposable
{
    private readonly Channel<PlotRequest> _channel =
        Channel.CreateUnbounded<PlotRequest>(new UnboundedChannelOptions { SingleReader = true });

    private int _count;

    public int MaxPlotsPerRun { get; set; } = 100;

    /// <summary>Enqueues a plot request. Excess requests beyond <see cref="MaxPlotsPerRun"/> are silently dropped.</summary>
    public void Enqueue(PlotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_count >= MaxPlotsPerRun) return;
        _channel.Writer.TryWrite(request);
        Interlocked.Increment(ref _count);
    }

    /// <summary>Returns an async enumerable that drains the queue until <see cref="Complete"/> is called.</summary>
    public async IAsyncEnumerable<PlotRequest> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(ct))
            yield return item;
    }

    /// <summary>Signals that no more plot requests will be enqueued by the current run.</summary>
    public void Complete()
    {
        _channel.Writer.TryComplete();
        Interlocked.Exchange(ref _count, 0);
    }

    public void Dispose() => _channel.Writer.TryComplete();
}
