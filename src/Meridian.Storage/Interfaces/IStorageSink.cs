using System.Threading;
using Meridian.Application.Services;
using Meridian.Domain.Events;

namespace Meridian.Storage.Interfaces;

public interface IStorageSink : IAsyncDisposable, IFlushable
{
    ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default);
    new Task FlushAsync(CancellationToken ct = default);
}
