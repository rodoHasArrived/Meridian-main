using System.Globalization;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

[JsonConverter(typeof(JsonStringEnumConverter<SecurityRateObservationKind>))]
public enum SecurityRateObservationKind { ObservedFixing, ForecastRate, Assumption, TreasuryConstantMaturityYield, EconomicObservation }

[JsonConverter(typeof(JsonStringEnumConverter<SecurityRateUnit>))]
public enum SecurityRateUnit { Fraction, Percent, BasisPoints, IndexLevel }

/// <summary>A source observation, never an OHLC bar. Unknown publication dates remain unknown.</summary>
public sealed record SecurityRateObservationDto(
    string Provider, string SeriesId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Value, string OriginalValue,
    [property: JsonRequired] SecurityRateUnit Unit,
    string SourceUnits, DateOnly ObservationDate, DateTimeOffset? PublishedAt,
    DateOnly? VintageDate, DateOnly? VintageEndDate, DateTimeOffset RetrievedAt,
    string Transformation, [property: JsonRequired] SecurityRateObservationKind Kind, string? Currency = null);

public interface ISecurityRateObservationProvider
{
    string Name { get; }
    Task<IReadOnlyList<SecurityRateObservationDto>> GetRateObservationsAsync(
        string seriesId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
}

[JsonConverter(typeof(JsonStringEnumConverter<SecurityRateConstructionMethod>))]
public enum SecurityRateConstructionMethod
{
    DirectForwardRates,
    ObservedFixingFlatForecast,
    ReviewedDiscountFactors
}

/// <summary>Raw observation and the explicit construction assumption retained with each immutable pillar.</summary>
public sealed record SecurityRatePointEvidenceDto(
    SecurityRateObservationDto Observation, SecurityRateConstructionMethod ConstructionMethod,
    string MethodDescription, string ReviewedBy);

public sealed record SecurityRateIngestionPointDto(
    DateOnly PointDate, SecurityRateObservationDto Observation,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? ReviewedDiscountFactor = null, string? Tenor = null);

public sealed record IngestSecurityRatesRequest(
    Guid CurveId, DateOnly AsOf, SecurityRateConstructionMethod ConstructionMethod,
    string MethodDescription, IReadOnlyList<SecurityRateIngestionPointDto> Points);

/// <summary>Explicit, reviewable conversion rules; a constant-maturity par yield is never a discount factor.</summary>
public static class SecurityRateIngestionRules
{
    public static PublishSecurityCurveVersionRequest BuildPublication(
        IngestSecurityRatesRequest request, SecurityCurveDefinitionDto definition, string actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(definition);
        if (request.CurveId != definition.CurveId || request.Points is not { Count: > 0 })
            throw new ArgumentException("Select a defined curve and at least one typed rate observation.");
        if (!Enum.IsDefined(request.ConstructionMethod) || string.IsNullOrWhiteSpace(request.MethodDescription) || string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Review and describe the construction method before publishing rates.");

        var points = request.Points.Select(point =>
        {
            var observation = point.Observation ?? throw new ArgumentException("Every pillar needs its source observation.");
            if (!Enum.IsDefined(observation.Unit) || !Enum.IsDefined(observation.Kind) ||
                string.IsNullOrWhiteSpace(observation.Provider) || string.IsNullOrWhiteSpace(observation.SeriesId) ||
                string.IsNullOrWhiteSpace(observation.SourceUnits) || string.IsNullOrWhiteSpace(observation.Transformation) ||
                !decimal.TryParse(observation.OriginalValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var original) || original != observation.Value)
                throw new ArgumentException("Observation provenance, units, transformation and exact original value are required.");
            if (observation.ObservationDate == default || observation.RetrievedAt == default ||
                observation.ObservationDate > DateOnly.FromDateTime(observation.RetrievedAt.UtcDateTime) ||
                observation.PublishedAt is { } published && published > observation.RetrievedAt)
                throw new ArgumentException("Observation and retrieval dates are required and publication cannot follow retrieval.");
            if (!string.Equals(observation.Currency, definition.Currency, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The observed rate currency must agree with the selected curve currency.");
            if (definition.Kind == SecurityCurveKind.Forward && !string.Equals(definition.IndexName, observation.SeriesId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The observed series must agree with the Forward curve index. Define a curve with the source series identity; aliases need an explicit reviewed mapping.");
            if (observation.ObservationDate > request.AsOf)
                throw new ArgumentException("A curve cannot use an observation later than its economic as-of date.");
            if (observation.VintageDate is { } vintage && observation.VintageEndDate is { } end && vintage > end)
                throw new ArgumentException("The observation vintage interval is invalid.");

            decimal value;
            switch (request.ConstructionMethod)
            {
                case SecurityRateConstructionMethod.ReviewedDiscountFactors:
                    if (definition.Kind != SecurityCurveKind.Discount || point.ReviewedDiscountFactor is not { } factor)
                        throw new ArgumentException("A reviewed discount construction needs a Discount curve and an explicitly supplied factor for each pillar. Treasury constant-maturity yields require an external reviewed construction, such as a par-curve bootstrap.");
                    value = factor;
                    break;
                case SecurityRateConstructionMethod.ObservedFixingFlatForecast:
                    if (definition.Kind != SecurityCurveKind.Forward || observation.Kind != SecurityRateObservationKind.ObservedFixing)
                        throw new ArgumentException("A flat forecast assumption requires an observed fixing and a Forward curve.");
                    value = Fraction(observation);
                    break;
                case SecurityRateConstructionMethod.DirectForwardRates:
                    if (definition.Kind != SecurityCurveKind.Forward || observation.Kind is not (SecurityRateObservationKind.ForecastRate or SecurityRateObservationKind.Assumption))
                        throw new ArgumentException("Direct forward inputs must be labeled forecast rates or assumptions. An observed fixing or Treasury constant-maturity yield cannot silently become a forecast or discount factor.");
                    value = Fraction(observation);
                    break;
                default:
                    throw new ArgumentException("Select a supported construction method.");
            }

            return new SecurityCurvePointDto(point.PointDate, value, point.Tenor)
            {
                RateEvidence = new(observation, request.ConstructionMethod, request.MethodDescription.Trim(), actor.Trim())
            };
        }).ToArray();

        var publication = new PublishSecurityCurveVersionRequest(request.CurveId, request.AsOf, points,
            string.Join(", ", points.Select(point => point.RateEvidence!.Observation.Provider).Distinct(StringComparer.OrdinalIgnoreCase)),
            PublishedBy: actor);
        return SecurityCurveRules.Normalize(publication, definition);
    }

    private static decimal Fraction(SecurityRateObservationDto observation) => observation.Unit switch
    {
        SecurityRateUnit.Fraction => observation.Value,
        SecurityRateUnit.Percent => observation.Value / 100m,
        SecurityRateUnit.BasisPoints => observation.Value / 10_000m,
        _ => throw new ArgumentException("An index level cannot be used as an interest rate.")
    };
}
