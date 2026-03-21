namespace Meridian.Domain.Events.Publishers;

/// <summary>
/// Composite publisher that publishes events to multiple publishers.
/// Returns true if at least one publisher succeeds.
/// </summary>
public sealed class CompositePublisher : IMarketEventPublisher
{
    private readonly IMarketEventPublisher[] _publishers;

    public CompositePublisher(params IMarketEventPublisher[] publishers)
    {
        _publishers = publishers ?? throw new ArgumentNullException(nameof(publishers));
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
            }
        }

        return anySucceeded;
    }
}
