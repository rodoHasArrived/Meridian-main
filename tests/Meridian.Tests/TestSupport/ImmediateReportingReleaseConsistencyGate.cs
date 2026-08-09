using Meridian.Reporting;

namespace Meridian.Tests.TestSupport;

internal sealed class ImmediateReportingReleaseConsistencyGate : IReportingReleaseConsistencyGate
{
    public ValueTask<IAsyncDisposable> AcquireAsync(
        string accountingPeriodId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountingPeriodId);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IAsyncDisposable>(Lease.Instance);
    }

    private sealed class Lease : IAsyncDisposable
    {
        internal static readonly Lease Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
