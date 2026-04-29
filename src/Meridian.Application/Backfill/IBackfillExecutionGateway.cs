using Meridian.Application.Backfill;

namespace Meridian.Application.Backfill;

/// <summary>
/// Abstraction over backfill execution so auto-remediation can be tested deterministically.
/// </summary>
public interface IBackfillExecutionGateway
{
    Task<BackfillResult> RunAsync(BackfillRequest request, CancellationToken ct = default);
}
