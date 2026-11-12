using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityRateIngestionRulesTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 10);
    private static readonly Guid CurveId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public void ObservedFixing_BecomesForecastOnlyWithExplicitAssumption_AndRetainsSourceUnitsAndVintage()
    {
        var observation = Observation();
        var publication = SecurityRateIngestionRules.BuildPublication(Request(observation), Definition(), "operator");
        publication.Points[0].Value.Should().Be(0.0412345678901234567890123456m);
        publication.Points[0].RateEvidence!.Observation.Should().Be(observation);
        publication.Points[0].RateEvidence!.ConstructionMethod.Should().Be(SecurityRateConstructionMethod.ObservedFixingFlatForecast);
        publication.Points[0].RateEvidence!.ReviewedBy.Should().Be("operator");
        var json = JsonSerializer.Serialize(publication.Points.ToArray(), SecurityCurvesJsonContext.Default.SecurityCurvePointDtoArray);
        var restored = JsonSerializer.Deserialize(json, SecurityCurvesJsonContext.Default.SecurityCurvePointDtoArray)!;
        restored[0].RateEvidence.Should().BeEquivalentTo(publication.Points[0].RateEvidence);
    }

    [Fact]
    public void TreasuryConstantMaturityYield_NeverImplicitlyBecomesDiscountFactor()
    {
        var observation = Observation() with { SeriesId = "DGS10", Kind = SecurityRateObservationKind.TreasuryConstantMaturityYield };
        Action implicitConversion = () => SecurityRateIngestionRules.BuildPublication(Request(observation), Definition(SecurityCurveKind.Discount), "operator");
        implicitConversion.Should().Throw<ArgumentException>();
        var request = Request(observation) with
        {
            ConstructionMethod = SecurityRateConstructionMethod.ReviewedDiscountFactors,
            MethodDescription = "Externally bootstrapped Treasury par curve; semiannual ACT/ACT conventions reviewed.",
            Points = [new(AsOf.AddYears(10), observation, 0.65m, "10Y")]
        };
        var result = SecurityRateIngestionRules.BuildPublication(request, Definition(SecurityCurveKind.Discount), "operator");
        result.Points[0].Value.Should().Be(0.65m);
        result.Points[0].RateEvidence!.Observation.Value.Should().Be(observation.Value);
    }

    [Fact]
    public void Ingestion_RefusesWrongCurrencyIndexMissingDatesAndAmbiguousRole()
    {
        var observation = Observation();
        var invalid = new[]
        {
            observation with { Currency = "EUR" }, observation with { SeriesId = "EURIBOR" },
            observation with { ObservationDate = default }, observation with { RetrievedAt = default },
            observation with { PublishedAt = observation.RetrievedAt.AddDays(1) },
            observation with { OriginalValue = "4.0" }, observation with { Kind = SecurityRateObservationKind.EconomicObservation }
        };
        foreach (var candidate in invalid)
        {
            Action ingest = () => SecurityRateIngestionRules.BuildPublication(Request(candidate), Definition(), "operator");
            ingest.Should().Throw<ArgumentException>();
        }
        Action implicitForecast = () => SecurityRateIngestionRules.BuildPublication(Request(observation) with
        { ConstructionMethod = SecurityRateConstructionMethod.DirectForwardRates }, Definition(), "operator");
        implicitForecast.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Hash_PreservesLegacyBytes_AndBindsNewObservationVintageAndConstruction()
    {
        var legacy = new SecurityCurvePointDto(AsOf.AddYears(1), 0.04m);
        SecurityCurveRules.ContentSha256(AsOf, [legacy]).Should().Be(
            Sha256Digest.ComputeUtf8("2026-11-10|2027-11-10=0.04"));
        var first = SecurityRateIngestionRules.BuildPublication(Request(Observation()), Definition(), "operator");
        var revised = SecurityRateIngestionRules.BuildPublication(Request(Observation() with { VintageDate = AsOf.AddDays(-1) }), Definition(), "operator");
        SecurityCurveRules.ContentSha256(AsOf, first.Points).Should().NotBe(SecurityCurveRules.ContentSha256(AsOf, revised.Points));
    }

    private static SecurityRateObservationDto Observation() => new("fred", "SOFR", 4.12345678901234567890123456m,
        "4.12345678901234567890123456", SecurityRateUnit.Percent, "Percent", AsOf.AddDays(-1), null,
        AsOf, new DateOnly(9999, 12, 31), new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero),
        "lin (untransformed provider level)", SecurityRateObservationKind.ObservedFixing, "USD");

    private static IngestSecurityRatesRequest Request(SecurityRateObservationDto observation) => new(CurveId, AsOf,
        SecurityRateConstructionMethod.ObservedFixingFlatForecast, "Reviewed flat forecast assumption, not a future fixing.",
        [new(AsOf.AddYears(1), observation)]);

    private static SecurityCurveDefinitionDto Definition(SecurityCurveKind kind = SecurityCurveKind.Forward) => new(
        CurveId, "USD-SOFR-TEST", kind, "USD", kind == SecurityCurveKind.Forward ? "SOFR" : null, null,
        "Actual360", SecurityCurveInterpolation.Linear, "fred", null, "operator", DateTimeOffset.UtcNow);
}
