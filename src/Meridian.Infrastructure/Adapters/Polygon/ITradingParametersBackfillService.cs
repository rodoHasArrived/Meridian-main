namespace Meridian.Infrastructure.Adapters.Polygon;

/// <summary>
/// Service for backfilling trading parameters (tick size, lot size, currency, etc.)
/// from external data providers into the Security Master.
/// </summary>
public interface ITradingParametersBackfillService
{
    /// <summary>
    /// Backfill trading parameters for all active securities. Every resulting amendment is
    /// recorded against <paramref name="initiatedBy"/> — the validated operator or workload
    /// that triggered the run — so a backfill covering up to 1,000 securities stays
    /// attributable in the Security Master audit trail.
    /// </summary>
    Task BackfillAllAsync(string initiatedBy, CancellationToken ct = default);

    /// <summary>
    /// Backfill trading parameters for a specific security, recording the amendment against
    /// <paramref name="initiatedBy"/>.
    /// </summary>
    Task BackfillTickerAsync(string ticker, Guid securityId, string initiatedBy, CancellationToken ct = default);
}
