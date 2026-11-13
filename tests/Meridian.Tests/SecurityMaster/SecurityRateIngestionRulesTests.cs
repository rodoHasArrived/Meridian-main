using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
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

    [Theory]
    [InlineData("unit")]
    [InlineData("kind")]
    public void WireRequest_MustDeclareSourceUnitsAndEconomicRole_WithoutZeroEnumInference(string omitted)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var request = Request(Observation());
        var json = JsonNode.Parse(JsonSerializer.Serialize(request, options))!.AsObject();
        var observation = json["points"]!.AsArray()[0]!["observation"]!.AsObject();
        observation.Remove(omitted).Should().BeTrue();
        Action read = () => JsonSerializer.Deserialize<IngestSecurityRatesRequest>(json.ToJsonString(), options);
        read.Should().Throw<JsonException>("missing source units or role cannot become Fraction or ObservedFixing before curve admission");
        // Complete retained payloads still use the original exact values and unchanged wire fields.
        JsonSerializer.Deserialize<IngestSecurityRatesRequest>(JsonSerializer.Serialize(request, options), options)
            .Should().BeEquivalentTo(request);
    }

    [Fact]
    public void RetainedStoreSerializers_KeepExplicitZeroEnumMeanings_AndStillReplay()
    {
        var observation = Observation() with
        {
            Value = 0.04125m,
            OriginalValue = "0.04125",
            Unit = SecurityRateUnit.Fraction,
            SourceUnits = "Fraction",
            Kind = SecurityRateObservationKind.ObservedFixing
        };
        SecurityCurvePointDto[] points = [new(AsOf.AddYears(1), observation.Value)
        { RateEvidence = new(observation, SecurityRateConstructionMethod.ObservedFixingFlatForecast, "Reviewed flat forecast", "operator") }];
        var curveJson = JsonSerializer.Serialize(points, SecurityCurvesJsonContext.Default.SecurityCurvePointDtoArray);
        using var curve = JsonDocument.Parse(curveJson);
        var retainedObservation = curve.RootElement[0].GetProperty("RateEvidence").GetProperty("Observation");
        retainedObservation.GetProperty("Unit").GetString().Should().Be("Fraction");
        retainedObservation.GetProperty("Kind").GetString().Should().Be("ObservedFixing");
        JsonSerializer.Deserialize(curveJson, SecurityCurvesJsonContext.Default.SecurityCurvePointDtoArray)![0]
            .RateEvidence!.Observation.Should().BeEquivalentTo(observation);

        var candidate = new BondObservedFixingCandidate(Guid.NewGuid(), 1, "SOFR", observation.ObservationDate,
            observation.Value, observation, "operator", "Reviewed exact reset");
        var receipt = new BondObservedFixingReceipt(Guid.NewGuid(), candidate, observation.RetrievedAt, new string('a', 64));
        var receiptJson = JsonSerializer.Serialize(receipt, BondObservedFixingJsonContext.Default.BondObservedFixingReceipt);
        using var fixing = JsonDocument.Parse(receiptJson);
        var fixingObservation = fixing.RootElement.GetProperty("Fixing").GetProperty("Observation");
        fixingObservation.GetProperty("Unit").GetString().Should().Be("Fraction");
        fixingObservation.GetProperty("Kind").GetString().Should().Be("ObservedFixing");
        JsonSerializer.Deserialize(receiptJson, BondObservedFixingJsonContext.Default.BondObservedFixingReceipt)
            .Should().BeEquivalentTo(receipt);
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
