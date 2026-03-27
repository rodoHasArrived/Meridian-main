namespace Meridian.Infrastructure.Adapters.Polygon;

/// <summary>
/// Service for backfilling trading parameters (tick size, lot size, currency, etc.) 
/// from external data providers into the Security Master.
/// </summary>
public interface ITradingParametersBackfillService
{
    /// <summary>
    /// Backfill trading parameters for all active securities.
    /// </summary>
    Task BackfillAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Backfill trading parameters for a specific security.
    /// </summary>
    Task BackfillTickerAsync(string ticker, Guid securityId, CancellationToken ct = default);
}
