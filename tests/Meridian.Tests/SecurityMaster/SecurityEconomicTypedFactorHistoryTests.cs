using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityEconomicTypedFactorHistoryTests
{
    [Fact]
    public void TypedFactorHistory_TravelsInEconomicModuleWithoutReadingLegacyPassthrough()
    {
        var terms = JsonSerializer.SerializeToNode(SecurityAssetTermsSchemaRoundTripTests.FullPayloads["StructuredCredit"])!;
        terms["factorScheduleEntries"] = JsonNode.Parse("""
            [{"asOfDate":"2026-02-21","factor":1.0},{"asOfDate":"2026-03-21","factor":0.97}]
            """);
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("StructuredCredit", terms);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("StructuredCredit", original));

        var history = economic.EconomicTerms.GetProperty("structuredProduct").GetProperty("factorScheduleEntries");
        history.GetArrayLength().Should().Be(2);
        history[1].GetProperty("factor").GetDecimal().Should().Be(0.97m);

        // The economic-only replay bridge retains fields its flat family cannot express; a later
        // rebuild must not erase typed factor history simply because the old copy is absent.
        var projection = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });
        var replayed = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        replayed.EconomicTerms.GetProperty("structuredProduct").GetProperty("factorScheduleEntries")
            .GetRawText().Should().Be(history.GetRawText());
    }
}
