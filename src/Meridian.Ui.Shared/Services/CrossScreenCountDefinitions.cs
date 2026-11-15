using Meridian.Contracts.Api;
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
        "Providers in use (with metrics, a saved connection, a routing connection or a trust snapshot) whose status is Healthy. A provider's status is the worse of its listed status and its readiness status.";

    public const string OpenBreaksDefinition =
        "Break-queue items not yet resolved, dismissed or signed off.";

    private const string HealthyProviderStatus = "Healthy";

    /// <summary>A provider row counts as healthy only when its status is exactly Healthy.</summary>
    public static bool IsHealthyProviderStatus(string? status) =>
        string.Equals(status?.Trim(), HealthyProviderStatus, StringComparison.OrdinalIgnoreCase);

    /// <summary>A readiness status in the provider list's words; Review and Unknown are a Warning.</summary>
    public static string ProviderStatusFromReadiness(ProviderReadinessStatusDto readinessStatus) =>
        readinessStatus switch
        {
            ProviderReadinessStatusDto.Ready => HealthyProviderStatus,
            ProviderReadinessStatusDto.Blocked => "Blocked",
            ProviderReadinessStatusDto.Degraded => "Degraded",
            _ => "Warning"
        };

    /// <summary>
    /// The one status a listed provider shows: the worse of its listed status (connection health or
    /// trust rationale) and its readiness status, when it has one. The Data workspace's provider
    /// list, the "Providers healthy" count and the browser's provider cards all use this, so a
    /// provider cannot count as Healthy while its card reads Blocked, and readiness can never
    /// hide a listed warning. Beside a readiness status, a listed status outside Healthy, Warning,
    /// Degraded and Blocked is a Warning.
    /// </summary>
    public static string ResolveProviderStatus(string? listedStatus, ProviderReadinessStatusDto? readinessStatus)
    {
        if (readinessStatus is null)
        {
            // Nothing to compare with: the listed status goes out exactly as its source reported it.
            return listedStatus ?? "Warning";
        }

        var listed = NormalizeProviderStatus(listedStatus);
        var readiness = ProviderStatusFromReadiness(readinessStatus.Value);
        return ProviderStatusRank(readiness) > ProviderStatusRank(listed) ? readiness : listed;
    }

    private static string NormalizeProviderStatus(string? status) =>
        status?.Trim().ToUpperInvariant() switch
        {
            "HEALTHY" => HealthyProviderStatus,
            "DEGRADED" => "Degraded",
            "BLOCKED" => "Blocked",
            _ => "Warning"
        };

    private static int ProviderStatusRank(string status) =>
        status switch
        {
            HealthyProviderStatus => 0,
            "Degraded" => 2,
            "Blocked" => 3,
            _ => 1
        };

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
