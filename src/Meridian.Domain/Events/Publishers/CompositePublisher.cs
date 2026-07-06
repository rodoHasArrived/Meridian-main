using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Domain.Events.Publishers;

/// <summary>
/// Composite publisher that publishes events to multiple publishers.
/// Returns true if at least one publisher succeeds.
/// </summary>
public sealed class CompositePublisher : IMarketEventPublisher
{
    private readonly IMarketEventPublisher[] _publishers;
    private readonly ILogger<CompositePublisher> _logger;

    public CompositePublisher(params IMarketEventPublisher[] publishers)
        : this(null, publishers)
    {
    }

    public CompositePublisher(ILogger<CompositePublisher>? logger, params IMarketEventPublisher[] publishers)
    {
        _publishers = publishers ?? throw new ArgumentNullException(nameof(publishers));
        _logger = logger ?? NullLogger<CompositePublisher>.Instance;
    }

    public bool TryPublish(in MarketEvent evt)
    {
        if (_publishers.Length == 0)
        {
            return false;
        }

        var anySucceeded = false;

        foreach (var publisher in _publishers)
        {
            try
            {
                if (publisher.TryPublish(in evt))
                {
                    anySucceeded = true;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                // Continue to other publishers even if one fails.
                // OutOfMemoryException and OperationCanceledException are re-thrown.
                _logger.LogWarning(
                    ex,
                    "Publisher {PublisherType} failed to publish market event; continuing with remaining publishers.",
                    publisher?.GetType().Name ?? "Unknown");
            }
        }

        return anySucceeded;
    }
}
