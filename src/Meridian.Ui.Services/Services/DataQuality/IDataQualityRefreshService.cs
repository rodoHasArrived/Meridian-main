using System;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services.DataQuality;

/// <summary>
/// Shared refresh loop abstraction so viewmodels do not own timer setup directly.
/// </summary>
public interface IDataQualityRefreshService : IDisposable
{
    void Start(TimeSpan interval, Func<CancellationToken, Task> onRefresh);
    void Stop();
}
