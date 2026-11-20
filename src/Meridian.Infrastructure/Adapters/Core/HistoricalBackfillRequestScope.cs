namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>The adapter applies the run's budget and observes its own provider requests.</summary>
public interface IBackfillRequestMeteredProvider;

/// <summary>Run-local request admission and observation, shared through composite routing.</summary>
public sealed class HistoricalBackfillRequestScope : IDisposable
{
    private static readonly AsyncLocal<HistoricalBackfillRequestScope?> Ambient = new();
    private readonly HistoricalBackfillRequestScope? _previous;

    public HistoricalBackfillRequestScope(
        Func<IHistoricalDataProvider, int> headroomPercent,
        Func<IHistoricalDataProvider, CancellationToken, Task> beforeUnmeteredFetch,
        Action<string> requestStarted,
        Action<string> barsPublished,
        Action<string, string, TimeSpan?> throttled,
        bool retryRateLimited)
    {
        HeadroomPercent = headroomPercent;
        BeforeUnmeteredFetch = beforeUnmeteredFetch;
        RequestStarted = requestStarted;
        BarsPublished = barsPublished;
        Throttled = throttled;
        RetryRateLimited = retryRateLimited;
        _previous = Ambient.Value;
        Ambient.Value = this;
    }

    public static HistoricalBackfillRequestScope? Current => Ambient.Value;
    public Func<IHistoricalDataProvider, int> HeadroomPercent { get; }
    public Func<IHistoricalDataProvider, CancellationToken, Task> BeforeUnmeteredFetch { get; }
    public Action<string> RequestStarted { get; }
    public Action<string> BarsPublished { get; }
    public Action<string, string, TimeSpan?> Throttled { get; }
    public bool RetryRateLimited { get; }

    public void Dispose() => Ambient.Value = _previous;
}
