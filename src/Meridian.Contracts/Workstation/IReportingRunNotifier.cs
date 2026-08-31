namespace Meridian.Contracts.Workstation;

/// <summary>
/// Fired after a reporting run's manifest is persisted — on generation, failure, or an approval
/// transition. Implementations MUST be non-blocking and exception-safe: this runs on the
/// run-execution path and must never stall or fail it. The only production implementation is the
/// UI stream broadcaster; the null-object below keeps the reporting layer free of a UI dependency.
/// </summary>
public interface IReportingRunNotifier
{
    void NotifyRunChanged(string runId);

    void NotifyRunChanged(
        string tenantId,
        string? companyId,
        string runId) =>
        NotifyRunChanged(runId);
}

/// <summary>No-op notifier used when no UI stream broadcaster is registered (headless hosts, tests).</summary>
public sealed class NullReportingRunNotifier : IReportingRunNotifier
{
    public static readonly NullReportingRunNotifier Instance = new();

    private NullReportingRunNotifier()
    {
    }

    public void NotifyRunChanged(string runId)
    {
    }
}
