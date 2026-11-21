using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

[Trait("Category", "Unit")]
public sealed class FredEconomicObservationsTests
{
    [Fact]
    public async Task TypedFixings_RetainExplicitUnitsVintageAndMissingness_WithoutInventingBarsOrPublicationTimes()
    {
        string? query = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            query = request.RequestUri!.Query;
            return Json("""{"count":3,"observations":[{"date":"2026-11-09","value":"4.25","realtime_start":"2026-11-10"},{"date":"2026-11-10","value":".","realtime_start":"2026-11-11"},{"date":"2026-11-11","value":"-0.2","realtime_start":"2026-11-12"}]}""");
        });
        using var provider = new FredHistoricalDataProvider("synthetic-key", new HttpClient(handler));
        var result = await provider.GetEconomicObservationsAsync(new("SOFR", "SOFR", BondMarketObservationKind.Fixing,
            BondMarketValueUnit.Percent, "New York Fed via FRED", "USD"), new(2026, 11, 9), new(2026, 11, 11), new(2026, 11, 12));

        result.Observations.Should().HaveCount(2);
        result.Observations[0].Value.Should().Be(4.25m);
        result.Observations[0].Unit.Should().Be(BondMarketValueUnit.Percent);
        result.Observations[0].PublishedAtUtc.Should().BeNull("a FRED vintage day does not prove publication time");
        result.Observations[0].SourceVintageDate.Should().Be(new DateOnly(2026, 11, 10));
        result.Observations[1].Value.Should().Be(-0.2m);
        result.MissingDates.Should().Equal(new DateOnly(2026, 11, 10));
        query.Should().Contain("realtime_start=2026-11-12").And.Contain("realtime_end=2026-11-12");
        result.Observations.Should().OnlyContain(row => !row.EvidenceReference.Contains("synthetic-key"));
    }

    [Theory]
    [InlineData("{\"count\":2,\"observations\":[{\"date\":\"2026-11-10\",\"value\":\"4.25\"}]}")]
    [InlineData("{\"observations\":[{\"date\":\"2026-11-10\",\"value\":\"malformed\"}]}")]
    public async Task TruncatedOrMalformedEvidence_IsRejectedAsAWhole(string json)
    {
        using var handler = new StubHttpMessageHandler(_ => Json(json));
        using var provider = new FredHistoricalDataProvider("synthetic-key", new HttpClient(handler));
        Func<Task> request = () => provider.GetEconomicObservationsAsync(new("SOFR", "SOFR", BondMarketObservationKind.Fixing,
            BondMarketValueUnit.Percent, "Synthetic FRED source"), new(2026, 11, 10), new(2026, 11, 10), new(2026, 11, 12));
        await request.Should().ThrowAsync<InvalidDataException>();
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    { Content = new StringContent(value, Encoding.UTF8, "application/json") };
}
