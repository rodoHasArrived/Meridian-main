using System.Threading.Channels;
using Meridian.Infrastructure.Contracts;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Meridian.QuantScript.Plotting;

/// <summary>
/// Collects <see cref="PlotRequest"/> instances produced during a script run and delivers them
/// to the ViewModel for rendering.
/// </summary>
/// <remarks>
/// The channel is intentionally unbounded because scripts produce plots in bursts, not at the
/// rate of UI rendering, and applying backpressure here would deadlock the script thread.
/// </remarks>
[ImplementsAdr("ADR-004", "ReadAllAsync uses IAsyncEnumerable with CancellationToken")]
public sealed class PlotQueue
{
    // AsyncLocal allows PlotRequest.Plot() extension methods to reach the queue without
    // explicit parameter threading.
    [ThreadStatic]
    private static PlotQueue? _current;

    /// <summary>The queue active on the current thread (or null if outside a script run).</summary>
    public static PlotQueue? Current
    {
        get => _current;
        internal set => _current = value;
    }

    private readonly Channel<PlotRequest> _channel =
        Channel.CreateUnbounded<PlotRequest>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true
        });

    /// <summary>Enqueues a plot request. Thread-safe.</summary>
    public void Enqueue(PlotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _channel.Writer.TryWrite(request);
    }

    /// <summary>Tries to read the next plot request without blocking.</summary>
    public bool TryRead(out PlotRequest? request)
    {
        if (_channel.Reader.TryRead(out var r))
        {
            request = r;
            return true;
        }
        request = null;
        return false;
    }

    /// <summary>Returns an async enumerable of all requests until the queue is completed.</summary>
    public IAsyncEnumerable<PlotRequest> ReadAllAsync(CancellationToken ct = default)
        => _channel.Reader.ReadAllAsync(ct);

    /// <summary>Signals that no more plots will be enqueued.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <summary>Drains any remaining plots into a list after a run completes.</summary>
    public List<PlotRequest> DrainRemaining()
    {
        var result = new List<PlotRequest>();
        while (_channel.Reader.TryRead(out var r))
            result.Add(r);
        return result;
    }
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
