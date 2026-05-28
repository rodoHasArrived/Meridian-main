using System.Diagnostics;
using Meridian.Domain.Events;

namespace Meridian.Tests.TestHelpers;

/// <summary>
/// Test publisher that captures ambient activity context for trace propagation assertions.
/// </summary>
public sealed class ActivityCapturingPublisher : IMarketEventPublisher
{
    public List<string?> TraceIds { get; } = new();
    public List<string?> OperationNames { get; } = new();

    public bool TryPublish(in MarketEvent evt)
    {
        TraceIds.Add(Activity.Current?.TraceId.ToString());
        OperationNames.Add(Activity.Current?.OperationName);
        return true;
    }
}
