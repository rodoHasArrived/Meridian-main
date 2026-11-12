using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Instruments.FixedIncome;

/// <summary>Bounded combined screening over the existing reference keyset pages and retained agency observations.</summary>
public sealed class BondScreeningService(IBondReferenceService reference, IBondMarketDataService market,
    TimeProvider? clock = null) : IBondScreeningService
{
    public const int MaximumSourcePages = 16;
    public const int SourcePageSize = 100;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private const string ReferenceLimitation = "Reference terms are read through the economic-as-of projection. That source does not verify a recorded-information cutoff; retained ratings use the explicit knowledge cutoff.";

    public async Task<BondScreeningPageDto> ScreenAsync(string tenantId, BondScreeningQuery query, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(query);
        query = Normalize(query);
        var now = _clock.GetUtcNow();
        if (query.AsOf == default || query.AsOf > DateOnly.FromDateTime(now.UtcDateTime)
            || query.KnownAtUtc == default || query.KnownAtUtc.Offset != TimeSpan.Zero || query.KnownAtUtc > now
            || query.MaturityFrom == default || query.MaturityTo < query.MaturityFrom
            || query.MaturityTo.DayNumber - query.MaturityFrom.DayNumber > 18366
            || query.PageSize is < 1 or > 100 || query.CallProtectedThrough < query.AsOf
            || query.Currency is { } currency && (currency.Length != 3 || currency.Any(ch => ch is < 'A' or > 'Z'))
            || query.AcceptedRatings is { Count: > 0 } && query.RatingAgency is null)
            throw new ArgumentException("Screening requires explicit bounded dates, a retained UTC cutoff, page size 1–100 and an agency for exact rating filters.", nameof(query));
        var binding = Sha256Digest.ComputeUtf8(tenantId.Trim() + "\n" + JsonSerializer.Serialize(query with { Cursor = null }, BondScreeningJsonContext.Default.BondScreeningQuery));
        var cursor = ReadCursor(query.Cursor, binding);
        var rows = new List<BondScreeningRow>();
        var gaps = new List<BondScreeningGap>();
        var pages = 0;
        string? next = null;
        for (; pages < MaximumSourcePages;)
        {
            ct.ThrowIfCancellationRequested();
            var page = await reference.GetMaturityLadderPageAsync(query.MaturityFrom, query.MaturityTo,
                SourcePageSize, cursor.SourceCursor, query.AsOf, ct).ConfigureAwait(false);
            pages++;
            if (page.Items.Count > SourcePageSize)
                throw new InvalidDataException("Reference screening returned more than its bounded source page.");
            var pageHash = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(page, BondScreeningJsonContext.Default.BondReferencePageDto));
            if (cursor.PageHash is not null && cursor.PageHash != pageHash || cursor.Offset > page.Items.Count)
                throw new ArgumentException("Reference data changed while resuming screening; restart from the first page.", nameof(query));
            var index = cursor.Offset;
            for (; index < page.Items.Count; index++)
            {
                var bond = page.Items[index];
                if (!BaseFilters(bond, query, gaps))
                    continue;
                var observations = await market.QueryAsync(tenantId, new(Kind: BondMarketObservationKind.CreditRating,
                    SecurityId: bond.SecurityId, ThroughDate: query.AsOf, KnownAtUtc: query.KnownAtUtc), ct).ConfigureAwait(false);
                // Source revisions remain separate by provider and agency. Never manufacture a composite rating.
                var ratings = observations.Where(row => row.Input.Kind == BondMarketObservationKind.CreditRating
                    && row.Input.SecurityId == bond.SecurityId && row.Input.EconomicDate <= query.AsOf
                    && row.RecordedAtUtc <= query.KnownAtUtc && (row.Input.PublishedAtUtc is null || row.Input.PublishedAtUtc <= query.KnownAtUtc)
                    && (query.RatingAgency is null || string.Equals(row.Input.RatingAgency, query.RatingAgency, StringComparison.OrdinalIgnoreCase)))
                    .GroupBy(row => (Agency: row.Input.RatingAgency?.ToUpperInvariant(), row.Input.ProviderId))
                    .SelectMany(group => group.Where(row => row.Input.EconomicDate == group.Max(item => item.Input.EconomicDate)))
                    .OrderBy(row => row.Input.RatingAgency, StringComparer.Ordinal).ThenBy(row => row.Input.ProviderId, StringComparer.Ordinal)
                    .ThenBy(row => row.ObservationId).ToArray();
                var localGaps = new List<string>();
                if (string.IsNullOrWhiteSpace(bond.IssuerName))
                    localGaps.Add("Issuer identity is unavailable.");
                if (bond.CouponStructure?.Kind is null && bond.AccrualConvention?.CouponKind is null)
                    localGaps.Add("Coupon structure is unavailable.");
                if (ratings.Length == 0)
                {
                    var missing = query.RatingAgency is null ? "No eligible retained agency rating is available."
                        : $"No eligible retained {query.RatingAgency} rating is available.";
                    localGaps.Add(missing);
                    gaps.Add(new(bond.SecurityId, missing));
                }
                if (query.AcceptedRatings is { Count: > 0 } accepted)
                {
                    if (ratings.Length == 0)
                        continue;
                    // Competing selected-agency observations cannot be silently reduced to the favorable source.
                    var codes = ratings.Select(row => row.Input.Rating).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if (codes.Length != 1)
                    { gaps.Add(new(bond.SecurityId, "Competing selected-agency ratings require source selection before an exact-code filter can establish a match.")); continue; }
                    if (!accepted.Contains(codes[0]!, StringComparer.OrdinalIgnoreCase))
                        continue;
                }
                rows.Add(new(bond, ratings, localGaps));
                if (rows.Count == query.PageSize)
                {
                    index++;
                    next = index < page.Items.Count
                        ? WriteCursor(new(binding, cursor.SourceCursor, index, pageHash))
                        : page.NextCursor is null ? null : WriteCursor(new(binding, page.NextCursor, 0, null));
                    return new(rows, next, gaps, pages, false, [ReferenceLimitation]);
                }
            }
            if (page.NextCursor is null)
                return new(rows, null, gaps, pages, false, [ReferenceLimitation]);
            if (page.NextCursor == cursor.SourceCursor)
                throw new InvalidDataException("The bounded reference screening cursor did not advance.");
            cursor = new(binding, page.NextCursor, 0, null);
            next = WriteCursor(cursor);
        }
        return new(rows, next, gaps, pages, false, [ReferenceLimitation,
            "The bounded source scan limit was reached; continue with the returned cursor to inspect remaining candidates."]);
    }

    private static bool BaseFilters(BondReferenceDto bond, BondScreeningQuery query, List<BondScreeningGap> gaps)
    {
        if (bond.Lifecycle is not { } maturity)
        { gaps.Add(new(bond.SecurityId, "Retained maturity terms are unavailable for the bounded maturity filter.")); return false; }
        if (maturity.MaturityDate < query.MaturityFrom || maturity.MaturityDate > query.MaturityTo)
            return false;
        if (query.Issuer is not null && (bond.IssuerName is null || !bond.IssuerName.Contains(query.Issuer, StringComparison.OrdinalIgnoreCase)))
        {
            if (bond.IssuerName is null)
                gaps.Add(new(bond.SecurityId, "Issuer identity is unavailable for the requested filter."));
            return false;
        }
        if (query.Currency is not null && !string.Equals(query.Currency, bond.Currency, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(bond.Currency))
                gaps.Add(new(bond.SecurityId, "Currency is unavailable for the requested filter."));
            return false;
        }
        var coupon = bond.CouponStructure?.Kind ?? bond.AccrualConvention?.CouponKind;
        if (query.CouponKind is not null && !string.Equals(query.CouponKind, coupon, StringComparison.OrdinalIgnoreCase))
        {
            if (coupon is null)
                gaps.Add(new(bond.SecurityId, "Coupon structure is unavailable for the requested filter."));
            return false;
        }
        if (query.CallProtectedThrough is not { } protectedThrough)
            return true;
        if (bond.Lifecycle is not { } lifecycle)
        { gaps.Add(new(bond.SecurityId, "Call protection is unavailable without retained lifecycle terms.")); return false; }
        if (lifecycle.MaturityDate < protectedThrough)
            return false;
        var dates = new List<DateOnly>();
        if (lifecycle.CallDate is { } scalar)
            dates.Add(scalar);
        var declaredCalls = bond.CallSchedule is { ValueKind: JsonValueKind.Array } schedule && schedule.GetArrayLength() > 0;
        if (declaredCalls)
        {
            foreach (var entry in bond.CallSchedule!.Value.EnumerateArray())
            {
                DateOnly? date = null;
                foreach (var name in new[] { "exerciseDate", "callDate", "date", "windowStart" })
                    if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var value)
                        && value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", out var parsed))
                    { date = parsed; break; }
                if (date is null)
                { gaps.Add(new(bond.SecurityId, "A declared call entry lacks a supported eligibility date; call protection is unverified.")); return false; }
                dates.Add(date.Value);
            }
        }
        if (!lifecycle.IsCallable && !declaredCalls && dates.Count == 0)
            return true;
        if (dates.Count == 0)
        { gaps.Add(new(bond.SecurityId, "Callable terms do not establish an earliest eligible call date.")); return false; }
        return dates.Min() > protectedThrough;
    }

    private static BondScreeningQuery Normalize(BondScreeningQuery query)
    {
        var accepted = query.AcceptedRatings?.Select(row => NormalizeOptional(row) ?? throw new ArgumentException("Rating labels cannot be empty."))
            .Select(row => row.ToUpperInvariant()).Distinct(StringComparer.Ordinal).OrderBy(row => row, StringComparer.Ordinal).ToArray();
        if (accepted?.Length > 100 || query.Issuer?.Length > 200 || query.CouponKind?.Length > 100 || query.RatingAgency?.Length > 100)
            throw new ArgumentException("Screening filter text and rating populations must be bounded.");
        return query with
        {
            Issuer = NormalizeOptional(query.Issuer)?.ToUpperInvariant(),
            Currency = NormalizeOptional(query.Currency)?.ToUpperInvariant(),
            CouponKind = NormalizeOptional(query.CouponKind)?.ToUpperInvariant(),
            RatingAgency = NormalizeOptional(query.RatingAgency)?.ToUpperInvariant(),
            AcceptedRatings = accepted is { Length: > 0 } ? accepted : null
        };
    }

    private static BondScreeningCursor ReadCursor(string? token, string binding)
    {
        if (token is null)
            return new(binding, null, 0, null);
        if (token.Length is 0 or > 4096)
            throw new ArgumentException("Invalid screening cursor.");
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2 || Sha256Digest.ComputeUtf8(parts[0]) != parts[1])
                throw new FormatException();
            var encoded = parts[0].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            var value = JsonSerializer.Deserialize(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)),
                BondScreeningCursorJsonContext.Default.BondScreeningCursor);
            if (value is null || value.QueryHash != binding || value.Offset is < 0 or > SourcePageSize
                || value.SourceCursor?.Length > 256 || value.PageHash is not null && !Sha256Digest.IsCanonical(value.PageHash))
                throw new FormatException();
            return value;
        }
        catch (Exception error) when (error is FormatException or JsonException)
        { throw new ArgumentException("Invalid screening cursor or filters/cutoffs changed; restart screening.", nameof(token), error); }
    }

    private static string WriteCursor(BondScreeningCursor value)
    {
        var encoded = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value, BondScreeningCursorJsonContext.Default.BondScreeningCursor))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return encoded + "." + Sha256Digest.ComputeUtf8(encoded);
    }
}

internal sealed record BondScreeningCursor(string QueryHash, string? SourceCursor, int Offset, string? PageHash);
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BondScreeningCursor))]
internal partial class BondScreeningCursorJsonContext : JsonSerializerContext;
