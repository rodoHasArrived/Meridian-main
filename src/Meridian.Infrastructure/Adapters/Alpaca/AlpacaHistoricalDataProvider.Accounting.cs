using System.Net;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Infrastructure.Adapters.Alpaca;

public sealed partial class AlpacaHistoricalDataProvider
{
    /// <summary>
    /// Requests unadjusted observations independently of the configured research dataset. The
    /// shared HTTP/quota owner is retained; no provider mutation or second credential context is used.
    /// </summary>
    public override async Task<AccountingClosePrice?> GetRawAccountingCloseAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ValidateSymbol(symbol);
        ValidateCredentials();
        if (to < from || to == DateOnly.MaxValue)
            throw new ArgumentException("Accounting close dates require an ordered, bounded session window.");

        var normalizedSymbol = NormalizeSymbol(symbol);
        var observations = new Dictionary<DateOnly, AccountingClosePrice>();
        var pagination = new PaginationCursorGuard("Alpaca accounting close");
        string? nextPageToken = null;
        var pages = 0;
        do
        {
            ct.ThrowIfCancellationRequested();
            pagination.Visit(nextPageToken);
            if (++pages > 50)
                throw new InvalidOperationException("Alpaca accounting-close pagination exceeded its page limit.");

            var url = BuildUrl(normalizedSymbol, from, to, nextPageToken, adjustment: "raw");
            using var response = await ExecuteGetAsync(url, normalizedSymbol, "accounting-close", ct).ConfigureAwait(false);
            await HandleHttpResponseOrThrowAsync(response, normalizedSymbol, "accounting-close", ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentRange is not null)
                throw new InvalidOperationException("Alpaca returned a partial accounting-close response.");

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("symbol", out var responseSymbol)
                || responseSymbol.ValueKind != JsonValueKind.String
                || !string.Equals(responseSymbol.GetString(), normalizedSymbol, StringComparison.OrdinalIgnoreCase)
                || !root.TryGetProperty("bars", out var bars)
                || bars.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null)
                || !root.TryGetProperty("next_page_token", out var pageToken)
                || pageToken.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new InvalidOperationException("Alpaca accounting-close response has missing or mismatched source evidence.");

            nextPageToken = pageToken.ValueKind == JsonValueKind.Null ? null : pageToken.GetString();
            if (bars.ValueKind == JsonValueKind.Array)
            {
                foreach (var sourceRecord in bars.EnumerateArray())
                {
                    ct.ThrowIfCancellationRequested();
                    var bar = sourceRecord.Deserialize<AlpacaBar>();
                    if (bar?.Timestamp is not { } timestamp || bar.Close <= 0m
                        || !IsValidOhlc(bar.Open, bar.High, bar.Low, bar.Close))
                        throw new InvalidOperationException("Alpaca accounting close requires a dated valid raw observation.");

                    var sessionDate = DateOnly.FromDateTime(timestamp.UtcDateTime);
                    if (sessionDate < from || sessionDate > to)
                        throw new InvalidOperationException("Alpaca accounting close returned a session outside the requested window.");

                    var observation = new AccountingClosePrice(normalizedSymbol, sessionDate, bar.Close,
                        Name, _feed, AccountingClosePriceBasis.Raw, Sha256Digest.ComputeUtf8(sourceRecord.GetRawText()));
                    if (observations.TryGetValue(sessionDate, out var existing) && existing != observation)
                        throw new InvalidOperationException("Alpaca accounting close returned conflicting observations for one session.");
                    observations[sessionDate] = observation;
                }
            }
        } while (!string.IsNullOrEmpty(nextPageToken));

        ct.ThrowIfCancellationRequested();
        return observations.Values.OrderByDescending(static quote => quote.SessionDate).FirstOrDefault();
    }
}
