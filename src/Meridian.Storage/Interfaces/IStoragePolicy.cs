using Meridian.Domain.Events;

namespace Meridian.Storage.Interfaces;

public interface IStoragePolicy
{
    string GetPath(MarketEvent evt);
}
