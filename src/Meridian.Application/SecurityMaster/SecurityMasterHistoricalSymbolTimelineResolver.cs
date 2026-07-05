using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Security-master-backed <see cref="IHistoricalSymbolTimelineResolver"/>: resolves the
/// requested symbol to a security (as-of the chunk date first, then as-of now, so both the
/// current and the retired ticker of a renamed security land on the same record), then picks
/// the ticker identifier whose validity interval covers the chunk date.
/// </summary>
public sealed class SecurityMasterHistoricalSymbolTimelineResolver : IHistoricalSymbolTimelineResolver
{
    private readonly ISecurityResolver _securityResolver;
    private readonly ISecurityMasterStore _store;
    private readonly ILogger<SecurityMasterHistoricalSymbolTimelineResolver> _logger;

    public SecurityMasterHistoricalSymbolTimelineResolver(
        ISecurityResolver securityResolver,
        ISecurityMasterStore store,
        ILogger<SecurityMasterHistoricalSymbolTimelineResolver> logger)
    {
        _securityResolver = securityResolver ?? throw new ArgumentNullException(nameof(securityResolver));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> ResolveTickerForDateAsync(string symbol, DateOnly date, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return symbol;
        }

        var normalized = symbol.Trim().ToUpperInvariant();
        var asOf = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var securityId = await _securityResolver
                .ResolveAsync(new ResolveSecurityRequest(SecurityIdentifierKind.Ticker, normalized, null, asOf), ct)
                .ConfigureAwait(false)
            ?? await _securityResolver
                .ResolveAsync(new ResolveSecurityRequest(SecurityIdentifierKind.Ticker, normalized, null, DateTimeOffset.UtcNow), ct)
                .ConfigureAwait(false);
        if (securityId is null)
        {
            return symbol;
        }

        var projection = await _store.GetProjectionAsync(securityId.Value, ct).ConfigureAwait(false);
        if (projection is null)
        {
            return symbol;
        }

        var eraTicker = projection.Identifiers
            .Where(identifier => identifier.Kind == SecurityIdentifierKind.Ticker
                && identifier.ValidFrom <= asOf
                && (!identifier.ValidTo.HasValue || identifier.ValidTo.Value > asOf))
            .OrderByDescending(static identifier => identifier.IsPrimary)
            .ThenByDescending(static identifier => identifier.ValidFrom)
            .FirstOrDefault()?.Value;

        if (string.IsNullOrWhiteSpace(eraTicker)
            || string.Equals(eraTicker, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return symbol;
        }

        _logger.LogInformation(
            "Backfill chunk starting {Date} for {Symbol} resolves to era ticker {EraTicker}",
            date, normalized, eraTicker);
        return eraTicker.Trim().ToUpperInvariant();
    }
}
