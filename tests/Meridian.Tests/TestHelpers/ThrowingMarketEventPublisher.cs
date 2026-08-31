using Meridian.Domain.Events;

namespace Meridian.Tests.TestHelpers;

/// <summary>
/// Test implementation of <see cref="IMarketEventPublisher"/> whose <see cref="TryPublish"/>
/// always throws. Used to prove that best-effort integrity disclosure fails open: a broken
/// publisher must never break the primary flow (remediation, failover, gap handling) around it.
/// </summary>
public sealed class ThrowingMarketEventPublisher : IMarketEventPublisher
{
    /// <summary>Number of publish attempts observed before throwing.</summary>
    public int Attempts { get; private set; }

    /// <inheritdoc />
    public bool TryPublish(in MarketEvent evt)
    {
        Attempts++;
        throw new InvalidOperationException("Simulated integrity publisher failure.");
    }
}
