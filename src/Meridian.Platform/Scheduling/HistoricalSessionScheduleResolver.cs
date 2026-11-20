using System.Data.Common;
using Meridian.Contracts.Backfill;
using Meridian.Contracts.Domain;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;

namespace Meridian.Platform.Scheduling;

/// <summary>Resolves session expectations only after positively identifying a supported listing.</summary>
public sealed class HistoricalSessionScheduleResolver(
    ISecurityIdentityQueryService? identities,
    IOperationalTradingCalendar? calendar = null,
    DateTimeOffset? identityAsOfUtc = null,
    IReadOnlyDictionary<string, SecurityDetailDto?>? resolvedIdentities = null)
{
    private readonly IOperationalTradingCalendar _calendar = calendar ?? new TradingCalendar();

    public async Task<HistoricalSessionSchedule?> ResolveAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
            throw new ArgumentException("The coverage end must be on or after the start.", nameof(to));

        if (ListingId.TryParse(symbol, out var explicitListing) && HistoricalSessionEvidence.IsUsEquityListing(explicitListing.Mic.Value))
            return UsSessions(from, to);

        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        SecurityDetailDto? instrument;
        if (resolvedIdentities is not null && resolvedIdentities.TryGetValue(symbol, out var resolved))
        {
            instrument = resolved;
        }
        else
        {
            if (identities is null)
                return null;

            try
            {
                instrument = await identities.GetByIdentifierAsync(
                    SecurityIdentifierKind.Ticker, symbol, null, ct, identityAsOfUtc ?? end).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is DbException or TimeoutException)
            {
                ct.ThrowIfCancellationRequested();
                return null;
            }
        }
        if (instrument is null)
            return null;

        var listings = instrument.Identifiers
            .Where(id => id.Kind == SecurityIdentifierKind.ListingId && id.ValidFrom <= end &&
                         (id.ValidTo is null || id.ValidTo > start)).ToArray();
        if (listings.Length == 0 || !listings.All(id => ListingId.TryParse(id.Value, out var listing) &&
                                                       HistoricalSessionEvidence.IsUsEquityListing(listing.Mic.Value)) ||
            !listings.Any(id => id.ValidFrom <= start && (id.ValidTo is null || id.ValidTo > end)))
            return null;

        return UsSessions(from, to);
    }

    public HistoricalSessionSchedule UsSessions(DateOnly from, DateOnly to)
    {
        var sessions = new List<DateOnly>();
        for (var day = from.DayNumber; day <= to.DayNumber; day++)
        {
            var date = DateOnly.FromDayNumber(day);
            if (_calendar.IsTradingDay(date))
                sessions.Add(date);
        }

        return new HistoricalSessionSchedule(HistoricalSessionEvidence.UsEquities, sessions);
    }
}
