namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Reads the execution and simulation parameters for an already resolved security.
/// </summary>
public interface ISecurityTradingParametersQueryService
{
    /// <summary>
    /// Returns the trading parameters (lot size, tick size, etc.) for a security as of the
    /// specified point in time. Returns <c>null</c> when the security is not found.
    /// </summary>
    Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default);
}
