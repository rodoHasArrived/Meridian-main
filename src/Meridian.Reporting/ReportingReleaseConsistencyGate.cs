namespace Meridian.Reporting;

/// <summary>
/// Serializes final reporting release and governed accounting-period reopen for the same
/// authoritative ledger period. Production implementations must coordinate across host processes;
/// an in-process mutex alone is not sufficient for a durable reporting authority.
/// </summary>
public interface IReportingReleaseConsistencyGate
{
    ValueTask<IAsyncDisposable> AcquireAsync(
        string accountingPeriodId,
        CancellationToken cancellationToken = default);
}
