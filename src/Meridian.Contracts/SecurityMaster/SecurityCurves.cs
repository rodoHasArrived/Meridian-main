using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>What a stored curve's values are: discount factors, projected index fixings or index levels.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityCurveKind>))]
public enum SecurityCurveKind
{
    /// <summary>Discount factors in (0, 1] that never rise with time; a pillar on the as-of date is 1.</summary>
    Discount,

    /// <summary>Projected annual fixings of one floating-rate index, stored as non-negative fractions (0.04, never 4).</summary>
    Forward,

    /// <summary>Projected levels of one inflation index; always positive.</summary>
    Inflation
}

/// <summary>
/// How a value between two pillars is read. <see cref="Linear"/> is linear in
/// <see cref="DateOnly.DayNumber"/> on the stored value (the arithmetic of
/// <see cref="SecurityForwardCurve.RateAt"/>). Before the first pillar Forward and Inflation curves
/// hold flat, while a Discount curve is 1 on or before its as-of date and linear from (as-of, 1) to
/// the first pillar; after the last pillar Forward and Inflation curves hold flat and a Discount
/// curve refuses.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityCurveInterpolation>))]
public enum SecurityCurveInterpolation
{
    Linear
}

/// <summary>
/// One dated pillar. <paramref name="Value"/> is a discount factor (Discount), an annual rate as a
/// FRACTION (Forward: 0.04, never 4) or an index level (Inflation). <paramref name="Tenor"/> is a
/// label such as <c>3M</c>; the date is authoritative.
/// </summary>
public sealed record SecurityCurvePointDto(DateOnly Date, decimal Value, string? Tenor = null);

/// <summary>The write-once identity of a stored curve.</summary>
public sealed record SecurityCurveDefinitionDto(
    Guid CurveId,
    string CurveKey,
    SecurityCurveKind Kind,
    string Currency,
    string? IndexName,
    string? Tenor,
    string DayCount,
    SecurityCurveInterpolation Interpolation,
    string Source,
    string? Description,
    string CreatedBy,
    DateTimeOffset CreatedAt);

/// <summary>
/// One immutable published version of a curve. <paramref name="AsOf"/> is the economic date the
/// points describe, <paramref name="RecordedAt"/> the knowledge time the version was stored,
/// <paramref name="SourcedAt"/> the vendor timestamp when known, and <paramref name="ContentSha256"/>
/// the canonical hash of the as-of and sorted points.
/// </summary>
public sealed record SecurityCurveVersionDto(
    Guid CurveVersionId,
    Guid CurveId,
    string CurveKey,
    long Version,
    DateOnly AsOf,
    DateTimeOffset RecordedAt,
    DateTimeOffset? SourcedAt,
    string Source,
    string PublishedBy,
    IReadOnlyList<SecurityCurvePointDto> Points,
    string ContentSha256);

/// <summary>Defines a curve identity. The key is unique, ignoring case, and is write-once.</summary>
public sealed record DefineSecurityCurveRequest(
    string CurveKey,
    SecurityCurveKind Kind,
    string Currency,
    string? IndexName,
    string? Tenor,
    string DayCount,
    SecurityCurveInterpolation Interpolation,
    string Source,
    string? Description,
    string? CreatedBy = null);

/// <summary>Publishes the points of a curve as of an economic date. Identical content for the same curve and as-of returns the retained version.</summary>
public sealed record PublishSecurityCurveVersionRequest(
    Guid CurveId,
    DateOnly AsOf,
    IReadOnlyList<SecurityCurvePointDto> Points,
    string Source,
    DateTimeOffset? SourcedAt = null,
    string? PublishedBy = null);

/// <summary>Persistence for stored curves and their append-only versions.</summary>
public interface ISecurityCurveStore
{
    /// <summary>Defines a curve; an existing key with identical facts returns the stored definition. Throws <see cref="InvalidOperationException"/> when the key exists with different facts.</summary>
    Task<SecurityCurveDefinitionDto> DefineAsync(DefineSecurityCurveRequest request, CancellationToken ct = default);

    Task<SecurityCurveDefinitionDto?> GetDefinitionAsync(Guid curveId, CancellationToken ct = default);

    Task<IReadOnlyList<SecurityCurveDefinitionDto>> ListDefinitionsAsync(
        SecurityCurveKind? kind = null,
        string? currency = null,
        string? indexName = null,
        CancellationToken ct = default);

    /// <summary>Appends a version; identical content for the same curve and as-of returns the retained version and writes nothing.</summary>
    Task<SecurityCurveVersionDto> PublishVersionAsync(PublishSecurityCurveVersionRequest request, CancellationToken ct = default);

    Task<SecurityCurveVersionDto?> GetVersionAsync(Guid curveId, long version, CancellationToken ct = default);

    /// <summary>The version with id <paramref name="curveVersionId"/> when it belongs to <paramref name="curveId"/>; null otherwise.</summary>
    Task<SecurityCurveVersionDto?> GetVersionByIdAsync(Guid curveId, Guid curveVersionId, CancellationToken ct = default);

    /// <summary>The latest version whose as-of is on or before <paramref name="asOf"/> and that was recorded by <paramref name="knownAt"/> (now when null).</summary>
    Task<SecurityCurveVersionDto?> GetLatestVersionAsOfAsync(Guid curveId, DateOnly asOf, DateTimeOffset? knownAt = null, CancellationToken ct = default);

    Task<IReadOnlyList<SecurityCurveVersionDto>> ListVersionsAsync(Guid curveId, CancellationToken ct = default);
}

/// <summary>
/// The rules every stored curve satisfies and the pure arithmetic a valuation reads it with, kept
/// in Contracts so they are testable without a database and identical in every store and model.
/// Each <c>Normalize</c> returns the normalized request or throws <see cref="ArgumentException"/>
/// naming the rule.
/// </summary>
public static class SecurityCurveRules
{
    private const int HashPrefixLength = 12;

    public static DefineSecurityCurveRequest Normalize(DefineSecurityCurveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = NormalizeOptional(request.CurveKey) ?? throw new ArgumentException("A curve must be named by a non-empty key.", nameof(request));
        if (!Enum.IsDefined(request.Kind))
            throw new ArgumentException("A curve kind must be Discount, Forward or Inflation.", nameof(request));
        if (!Enum.IsDefined(request.Interpolation))
            throw new ArgumentException("A curve interpolation must be a declared method.", nameof(request));

        var currency = RequireCurrency(request.Currency);
        var indexName = NormalizeOptional(request.IndexName);
        if (request.Kind == SecurityCurveKind.Discount && indexName is not null)
            throw new ArgumentException("A discount curve has no index; name the currency only.", nameof(request));
        if (request.Kind != SecurityCurveKind.Discount && indexName is null)
            throw new ArgumentException("A forward or inflation curve must name its index.", nameof(request));

        if (!TryParseDayCount(request.DayCount, out var dayCount))
            throw new ArgumentException($"Day count '{request.DayCount}' is not a recognized convention.", nameof(request));

        return request with
        {
            CurveKey = key,
            Currency = currency,
            IndexName = indexName,
            Tenor = NormalizeOptional(request.Tenor),
            DayCount = dayCount.ToString(),
            Source = NormalizeOptional(request.Source) ?? throw new ArgumentException("A curve must name its source.", nameof(request)),
            Description = NormalizeOptional(request.Description),
            CreatedBy = RequireActor(request.CreatedBy)
        };
    }

    public static PublishSecurityCurveVersionRequest Normalize(PublishSecurityCurveVersionRequest request, SecurityCurveDefinitionDto definition)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(definition);
        if (request.CurveId != definition.CurveId)
            throw new ArgumentException("A version is published to the curve it names.", nameof(request));
        if (request.Points is not { Count: > 0 })
            throw new ArgumentException("A curve version needs at least one point.", nameof(request));

        var points = new List<SecurityCurvePointDto>(request.Points.Count);
        foreach (var point in request.Points)
        {
            ArgumentNullException.ThrowIfNull(point);
            points.Add(point with { Tenor = NormalizeOptional(point.Tenor) });
        }

        points.Sort(static (left, right) => left.Date.CompareTo(right.Date));
        for (var i = 1; i < points.Count; i++)
        {
            if (points[i].Date == points[i - 1].Date)
                throw new ArgumentException($"A curve version carries one value per date; {points[i].Date:yyyy-MM-dd} appears twice.", nameof(request));
        }

        var tenors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in points)
        {
            if (point.Date < request.AsOf)
                throw new ArgumentException($"Every point is dated on or after the as-of date; {point.Date:yyyy-MM-dd} precedes {request.AsOf:yyyy-MM-dd}.", nameof(request));
            if (point.Tenor is not null && !tenors.Add(point.Tenor))
                throw new ArgumentException($"A tenor label names one point; '{point.Tenor}' appears twice.", nameof(request));
            RequireValueForKind(definition.Kind, point, request.AsOf);
        }

        // A discount factor that rises from one pillar to the next encodes a negative forward rate,
        // the same thing a factor above 1 does, so both are refused by the one rule.
        if (definition.Kind == SecurityCurveKind.Discount)
        {
            for (var i = 1; i < points.Count; i++)
            {
                if (points[i].Value > points[i - 1].Value)
                    throw new ArgumentException(
                        $"Discount factors never rise with time; {points[i].Date:yyyy-MM-dd} carries {points[i].Value}, above {points[i - 1].Value} on {points[i - 1].Date:yyyy-MM-dd}, which encodes a negative rate.",
                        nameof(request));
            }
        }

        return request with
        {
            Points = points,
            Source = NormalizeOptional(request.Source) ?? throw new ArgumentException("A curve version must name its source.", nameof(request)),
            PublishedBy = RequireActor(request.PublishedBy)
        };
    }

    /// <summary>
    /// The canonical SHA-256 over <c>asOf|date=value[@tenor]|…</c> of the points sorted by date, so
    /// the same content in any input order gives the same hash.
    /// </summary>
    public static string ContentSha256(DateOnly asOf, IReadOnlyList<SecurityCurvePointDto> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var builder = new StringBuilder(asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        foreach (var point in points.OrderBy(static point => point.Date))
        {
            builder.Append('|')
                .Append(point.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append('=')
                .Append(point.Value.ToString("G29", CultureInfo.InvariantCulture));
            if (NormalizeOptional(point.Tenor) is { } tenor)
                builder.Append('@').Append(tenor);
        }

        return Sha256Digest.ComputeUtf8(builder.ToString());
    }

    /// <summary>
    /// The curve's value on <paramref name="date"/>: linear in day number between pillars; before
    /// the first pillar flat for Forward and Inflation curves, while a Discount curve is 1 on or
    /// before the version's as-of date and linear from (as-of, 1) to the first pillar (a discount
    /// factor is 1 at its own as-of, so it is never held at the first pillar's value back to that
    /// date); after the last pillar flat for Forward and Inflation curves and null for a Discount
    /// curve (a discount factor is never extrapolated). Null for a version with no points.
    /// </summary>
    public static decimal? ValueAt(SecurityCurveVersionDto version, SecurityCurveKind kind, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.Points is not { Count: > 0 })
            return null;

        var ordered = version.Points.OrderBy(static point => point.Date).ToArray();
        if (kind == SecurityCurveKind.Discount && date < ordered[0].Date)
        {
            if (date <= version.AsOf)
                return 1m;

            var lead = ordered[0].Date.DayNumber - version.AsOf.DayNumber;
            var along = (decimal)(date.DayNumber - version.AsOf.DayNumber) / lead;
            return 1m + ((ordered[0].Value - 1m) * along);
        }

        if (date <= ordered[0].Date)
            return ordered[0].Value;

        for (var i = 1; i < ordered.Length; i++)
        {
            var right = ordered[i];
            if (date > right.Date)
                continue;

            var left = ordered[i - 1];
            var span = right.Date.DayNumber - left.Date.DayNumber;
            if (span == 0)
                return right.Value;

            var weight = (decimal)(date.DayNumber - left.Date.DayNumber) / span;
            return left.Value + ((right.Value - left.Value) * weight);
        }

        return kind == SecurityCurveKind.Discount ? null : ordered[^1].Value;
    }

    /// <summary>
    /// The factor that discounts a payment on <paramref name="paymentDate"/> to
    /// <paramref name="valuationDate"/> from a Discount curve: 1 for a payment on or before the
    /// valuation date, otherwise the curve's <see cref="ValueAt"/> at the payment date rebased by its
    /// <see cref="ValueAt"/> at the valuation date (1 on or before the version's as-of, so a curve
    /// whose as-of precedes the valuation date still discounts to the valuation date). Null when the
    /// payment lies beyond the last pillar.
    /// </summary>
    public static decimal? DiscountFactorAt(SecurityCurveVersionDto discount, DateOnly valuationDate, DateOnly paymentDate)
    {
        ArgumentNullException.ThrowIfNull(discount);
        if (paymentDate <= valuationDate)
            return 1m;

        var atPayment = ValueAt(discount, SecurityCurveKind.Discount, paymentDate);
        if (atPayment is null)
            return null;

        var atValuation = ValueAt(discount, SecurityCurveKind.Discount, valuationDate);
        if (atValuation is not > 0m)
            return null;

        return atPayment.Value / atValuation.Value;
    }

    /// <summary>
    /// Adapts a Forward version to the projection seam. The <see cref="SecurityForwardCurve.Source"/>
    /// carries the curve key, version and hash prefix, so <see cref="SecurityForwardCurve.Describe"/>
    /// and every leg's <c>RateSource</c> name exactly which stored content projected the leg.
    /// </summary>
    public static SecurityForwardCurve ToForwardCurve(SecurityCurveVersionDto version, SecurityCurveDefinitionDto definition)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Kind != SecurityCurveKind.Forward)
            throw new ArgumentException($"Only a Forward curve projects floating legs; '{definition.CurveKey}' is {definition.Kind}.", nameof(definition));
        if (version.CurveId != definition.CurveId)
            throw new ArgumentException("The version does not belong to the definition.", nameof(version));

        var hashPrefix = version.ContentSha256.Length > HashPrefixLength
            ? version.ContentSha256[..HashPrefixLength]
            : version.ContentSha256;
        return new SecurityForwardCurve(
            definition.IndexName ?? definition.CurveKey,
            version.AsOf,
            version.Points.Select(static point => new SecurityForwardCurvePoint(point.Date, point.Value)).ToList(),
            $"{version.CurveKey} v{version.Version} {hashPrefix}");
    }

    private static void RequireValueForKind(SecurityCurveKind kind, SecurityCurvePointDto point, DateOnly asOf)
    {
        switch (kind)
        {
            case SecurityCurveKind.Discount:
                if (point.Value is <= 0m or > 1m)
                    throw new ArgumentException($"A discount factor lies in (0, 1]; {point.Date:yyyy-MM-dd} carries {point.Value}.", nameof(point));
                if (point.Date == asOf && point.Value != 1m)
                    throw new ArgumentException("The discount factor on the as-of date is 1.", nameof(point));
                break;
            case SecurityCurveKind.Forward:
                if (Math.Abs(point.Value) > 1m)
                    throw new ArgumentException($"Rates are stored as fractions; publish 0.04, not 4 ({point.Date:yyyy-MM-dd} carries {point.Value}).", nameof(point));
                // The cash-flow projection floors every rate at zero, so a stored negative fixing
                // would value a zero-floored leg while the curve claims otherwise.
                if (point.Value < 0m)
                    throw new ArgumentException(
                        $"A forward rate is not negative: the cash-flow projection floors every rate at zero, so a stored negative curve would silently value a zero-floored leg ({point.Date:yyyy-MM-dd} carries {point.Value}).",
                        nameof(point));
                break;
            case SecurityCurveKind.Inflation:
                if (point.Value <= 0m)
                    throw new ArgumentException($"An inflation index level is positive; {point.Date:yyyy-MM-dd} carries {point.Value}.", nameof(point));
                break;
            default:
                throw new ArgumentException("A curve kind must be Discount, Forward or Inflation.", nameof(kind));
        }
    }

    // The stored name of every convention must read back as itself, so an exact convention name
    // (ignoring case; never Unknown, never a number) is matched first. The market-spelling parser
    // comes second: it reads "ThirtyE360" and "ThirtyE360Isda" as 30/360 US.
    private static bool TryParseDayCount(string? raw, out DayCountConvention convention)
    {
        var trimmed = raw?.Trim();
        foreach (var candidate in Enum.GetValues<DayCountConvention>())
        {
            if (candidate != DayCountConvention.Unknown && string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                convention = candidate;
                return true;
            }
        }

        return DayCountConventions.TryParse(raw, out convention);
    }

    private static string RequireCurrency(string? value)
    {
        var currency = NormalizeOptional(value);
        if (currency is null || currency.Length != 3 || !currency.All(char.IsAsciiLetter))
            throw new ArgumentException("A currency is a three-letter ISO code.");
        return currency.ToUpperInvariant();
    }

    // A curve write is governed and names its actor; "unknown" is never recorded in its place.
    private static string RequireActor(string? value)
        => NormalizeOptional(value) ?? throw new ArgumentException("A curve write must name the actor.");
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SecurityCurvePointDto[]))]
public sealed partial class SecurityCurvesJsonContext : JsonSerializerContext;
