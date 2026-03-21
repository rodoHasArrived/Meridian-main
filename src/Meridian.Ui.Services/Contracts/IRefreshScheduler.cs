using System;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services.Contracts;

/// <summary>
/// Schedules recurring asynchronous refresh work behind a platform-neutral abstraction.
/// Consumers can keep refresh orchestration testable without depending on UI timers.
/// </summary>
public interface IRefreshScheduler : IDisposable
{
    /// <summary>
    /// Starts recurring execution of the supplied callback.
    /// Replaces any previously scheduled callback owned by this scheduler instance.
    /// </summary>
    void Start(TimeSpan interval, Func<CancellationToken, Task> callback, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the active recurring callback, if any.
    /// </summary>
    void Stop();
}
