using System.Threading;
using Meridian.Core.Services;
using Meridian.Domain.Events;

namespace Meridian.Storage.Interfaces;

/// <summary>
/// Defines the asynchronous append and flush boundary for persisted market events.
/// </summary>
public interface IStorageSink : IAsyncDisposable, IFlushable
{
    ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default);
    new Task FlushAsync(CancellationToken ct = default);
}
