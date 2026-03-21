using Meridian.Domain.Events;
using Meridian.Domain.Models;

namespace Meridian.Tests.TestHelpers;

/// <summary>
/// Test implementation of IMarketEventPublisher that captures published events.
/// This is necessary because Moq doesn't support callbacks with 'in' parameters.
/// </summary>
public sealed class TestMarketEventPublisher : IMarketEventPublisher
{
    private readonly List<MarketEvent> _publishedEvents = new();
    private bool _shouldReturnTrue = true;

    /// <summary>
    /// Gets the list of published events for assertions.
    /// </summary>
    public IReadOnlyList<MarketEvent> PublishedEvents => _publishedEvents;

    /// <summary>
    /// Sets whether TryPublish should return true or false.
    /// </summary>
    public void SetReturnValue(bool returnValue)
    {
        _shouldReturnTrue = returnValue;
    }

    /// <summary>
    /// Clears all published events.
    /// </summary>
    public void Clear()
    {
        _publishedEvents.Clear();
    }

    /// <inheritdoc />
    public bool TryPublish(in MarketEvent evt)
    {
        // Create a copy since 'in' parameter is a reference
        _publishedEvents.Add(evt);
        return _shouldReturnTrue;
    }
}
