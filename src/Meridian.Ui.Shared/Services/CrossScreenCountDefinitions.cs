using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// The one definition of each count that more than one workstation screen shows. The Trading,
/// Data and Accounting workspace metric cards and the cross-screen counts endpoint (which WPF
/// reads) all count with these, and the browser's <c>lib/cross-screen-counts.ts</c> mirrors them;
/// <c>tests/Shared/cross-screen-counts.fixture.json</c> holds both implementations to the same
/// expected values.
/// </summary>
public static class CrossScreenCountDefinitions
{
    public const string OpenOrdersDefinition =
        "Orders the order manager reports open (working, partially filled or pending routing) for the fund account in scope.";

    public const string ProvidersDefinition =
        "Providers whose status is Healthy, out of every provider the Data workspace lists.";

    public const string OpenBreaksDefinition =
        "Break-queue items not yet resolved, dismissed or signed off.";

    private const string HealthyProviderStatus = "Healthy";

    /// <summary>A provider row counts as healthy only when its status is exactly Healthy.</summary>
    public static bool IsHealthyProviderStatus(string? status) =>
        string.Equals(status?.Trim(), HealthyProviderStatus, StringComparison.OrdinalIgnoreCase);

    /// <summary>Healthy providers out of every listed provider.</summary>
    public static (int Healthy, int Total) CountProviderHealth(IEnumerable<string?> providerStatuses)
    {
        ArgumentNullException.ThrowIfNull(providerStatuses);
        var healthy = 0;
        var total = 0;
        foreach (var status in providerStatuses)
        {
            total++;
            if (IsHealthyProviderStatus(status))
            {
                healthy++;
            }
        }

        return (healthy, total);
    }

    /// <summary>
    /// A break is open until it is resolved, dismissed or signed off. Written as the exclusion so a
    /// status added later is counted as work still to do rather than silently dropped.
    /// </summary>
    public static bool IsOpenBreak(ReconciliationBreakQueueStatus status) =>
        status is not (ReconciliationBreakQueueStatus.Resolved
            or ReconciliationBreakQueueStatus.Dismissed
            or ReconciliationBreakQueueStatus.SignedOff);

    /// <summary>Open breaks in a break-queue read, each item counted once.</summary>
    public static int CountOpenBreaks(IEnumerable<ReconciliationBreakQueueItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Count(static item => IsOpenBreak(item.Status));
    }
}
