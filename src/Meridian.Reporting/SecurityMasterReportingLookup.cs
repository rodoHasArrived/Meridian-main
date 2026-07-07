using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Reporting;

/// <summary>
/// Shared, best-effort Security Master enrichment for reporting surfaces. Report generation and
/// NAV attribution both enrich ledger accounts by ticker and must degrade gracefully when the
/// Security Master is unavailable or has no match. This centralizes the "look up, swallow failure
/// into a debug log, continue" pattern so it is defined once instead of duplicated per service.
/// </summary>
internal static class SecurityMasterReportingLookup
{
    /// <summary>
    /// Resolves a security detail by ticker, returning <c>null</c> when the symbol is blank, no
    /// match exists, or the lookup fails (failures are logged at debug and swallowed).
    /// </summary>
    public static Task<SecurityDetailDto?> TryGetByTickerAsync(
        ISecurityMasterQueryService securityMaster,
        ILogger log,
        string? symbol,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return Task.FromResult<SecurityDetailDto?>(null);
        }

        return SwallowAsync(
            log,
            symbol,
            () => securityMaster.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, symbol, null, ct));
    }

    /// <summary>
    /// Resolves a security's economic definition by id, swallowing failures into a debug log the
    /// same way <see cref="TryGetByTickerAsync"/> does.
    /// </summary>
    public static Task<SecurityEconomicDefinitionRecord?> TryGetEconomicDefinitionAsync(
        ISecurityMasterQueryService securityMaster,
        ILogger log,
        Guid securityId,
        string? symbol,
        CancellationToken ct)
        => SwallowAsync(
            log,
            symbol,
            () => securityMaster.GetEconomicDefinitionByIdAsync(securityId, ct));

    private static async Task<T?> SwallowAsync<T>(
        ILogger log,
        string? symbol,
        Func<Task<T?>> lookup)
        where T : class
    {
        try
        {
            return await lookup().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Security Master lookup failed for symbol {Symbol}", symbol);
            return null;
        }
    }
}
