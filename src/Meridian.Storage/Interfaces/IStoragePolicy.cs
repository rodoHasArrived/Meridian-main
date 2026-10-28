using Meridian.Domain.Events;

namespace Meridian.Storage.Interfaces;

/// <summary>
/// Selects the storage path for a market event.
/// </summary>
public interface IStoragePolicy
{
    string GetPath(MarketEvent evt);
}
