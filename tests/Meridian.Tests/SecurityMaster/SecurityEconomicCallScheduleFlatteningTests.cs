using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Pins ADR-027 stage 1 increment 1: the economic Call module's schedule lists land in the flat
/// <c>callSchedule</c>/<c>putSchedule</c> arrays for the classes that declare them, in the frozen
/// ADR-024 entry form, stamped at the arrays' terms version. The conversion rules are declared
/// semantics, pinned here exactly: <c>callPx</c> is a percentage of par, a par call writes 100 at
/// that basis, <c>callType</c> "MakeWhole" is a price type carrying the module-level spread while
/// "European"/"American"/"Bermudan" are exercise styles, and members the economic family never
/// carried stay null (ADR-025's defaults govern their reads).
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityEconomicCallScheduleFlatteningTests
{
    [Fact]
    public void Bond_CallAndPutLists_LandInTheFrozenFlatForm()
    {
        var flat = Convert("""
            {"termsFormat":"economic","schemaVersion":2,
             "maturity":{"maturityDate":"2032-06-30"},
             "call":{"isCallable":true,"firstCallDate":"2027-01-01","makeWholeSpreadBps":null,
                     "callSchedule":[
                        {"callDt":"2027-01-01","callPx":102.5,"isParCall":false,"callType":"American"},
                        {"callDt":"2028-01-01","callPx":null,"isParCall":true,"callType":null}],
                     "isPuttable":true,
                     "putSchedule":[{"putDt":"2029-01-01","putPx":99.0}]}}
            """, "Bond");

        flat.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32()
            .Should().Be(3, "the flattened document holds the version-3 arrays, so it stamps at their version");

        var calls = flat.GetProperty("callSchedule");
        calls.GetArrayLength().Should().Be(2);
        AssertFrozenMembers(calls[0], ["callDate", "callEndDate", "callPrice", "exerciseFrequency", "exerciseStyle",
            "makeWholeSpreadBps", "maximumNoticeDays", "minimumNoticeDays", "noticeDayType",
            "partialRedemption", "priceBasis", "priceType"]);
        calls[0].GetProperty("callDate").GetString().Should().Be("2027-01-01");
        calls[0].GetProperty("callPrice").GetDecimal().Should().Be(102.5m);
        calls[0].GetProperty("priceBasis").GetString().Should().Be("PercentOfPar");
        calls[0].GetProperty("priceType").GetString().Should().Be("Fixed");
        calls[0].GetProperty("exerciseStyle").GetString().Should().Be("American");
        calls[0].GetProperty("callEndDate").ValueKind.Should().Be(JsonValueKind.Null, "windows were never carried by the economic family");

        // A par call without an explicit price is 100% of par: that is what a par call means.
        calls[1].GetProperty("callPrice").GetDecimal().Should().Be(100m);
        calls[1].GetProperty("priceBasis").GetString().Should().Be("PercentOfPar");
        calls[1].GetProperty("exerciseStyle").ValueKind.Should().Be(JsonValueKind.Null);

        var puts = flat.GetProperty("putSchedule");
        puts.GetArrayLength().Should().Be(1);
        AssertFrozenMembers(puts[0], ["exerciseFrequency", "exerciseStyle", "maximumNoticeDays", "minimumNoticeDays",
            "noticeDayType", "priceBasis", "putDate", "putEndDate", "putPrice"]);
        puts[0].GetProperty("putDate").GetString().Should().Be("2029-01-01");
        puts[0].GetProperty("putPrice").GetDecimal().Should().Be(99.0m);
        puts[0].GetProperty("priceBasis").GetString().Should().Be("PercentOfPar");

        // The source document stays retained until every module is recoverable (stage 1 end state).
        flat.TryGetProperty(SecurityTermsEnvelope.RetainedEconomicTermsKey, out _).Should().BeTrue();
    }

    [Fact]
    public void MakeWholeCallType_IsAPriceType_CarryingTheModuleSpread()
    {
        var flat = Convert("""
            {"call":{"makeWholeSpreadBps":50,
                     "callSchedule":[{"callDt":"2027-01-01","callPx":null,"isParCall":false,"callType":"MakeWhole"}]}}
            """, "Bond");

        var entry = flat.GetProperty("callSchedule")[0];
        entry.GetProperty("priceType").GetString().Should().Be("MakeWhole");
        entry.GetProperty("makeWholeSpreadBps").GetDecimal().Should().Be(50m);
        entry.GetProperty("exerciseStyle").ValueKind.Should().Be(JsonValueKind.Null, "MakeWhole names pricing, not an exercise style");
        entry.GetProperty("callPrice").ValueKind.Should().Be(JsonValueKind.Null);
        entry.GetProperty("priceBasis").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("Bond", 3)]
    [InlineData("CertificateOfDeposit", 3)]
    [InlineData("StructuredCredit", 4)]
    public void DeclaringClasses_StampAtTheirArrayVersion(string assetClass, int expectedVersion)
    {
        var flat = Convert("""
            {"call":{"callSchedule":[{"callDt":"2027-01-01","callPx":101.0,"isParCall":false,"callType":"European"}]}}
            """, assetClass);

        flat.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(expectedVersion);
        flat.GetProperty("callSchedule").GetArrayLength().Should().Be(1);
        flat.GetProperty("putSchedule").GetArrayLength().Should().Be(0, "the flat writer emits both arrays together");
    }

    [Fact]
    public void ConvertedDocument_ReadsBackAndItsStampDescribesItsContent()
    {
        var flat = Convert("""
            {"maturity":{"maturityDate":"2032-06-30"},
             "call":{"callSchedule":[{"callDt":"2027-01-01","callPx":102.5,"isParCall":false,"callType":"European"}],
                     "putSchedule":[]}}
            """, "Bond");

        var readable = () => SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms("Bond", flat);
        readable.Should().NotThrow("the recovered document must pass the read guard at its own stamp");
        SecurityTermsEnvelope.GetDocumentStampVersion("Bond", flat)
            .Should().Be(flat.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32(),
                "the S2 stamp describes the document: re-stamping it would not change it");
    }

    private static JsonElement Convert(string economicJson, string assetClass)
        => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster
            .Convert(JsonSerializer.Deserialize<JsonElement>(economicJson), assetClass)
            .Payload;

    private static void AssertFrozenMembers(JsonElement entry, string[] expected)
        => entry.EnumerateObject().Select(static member => member.Name).Should().Equal(
            expected, "the written entry form is frozen by ADR-024: every member, in alphabetical order");
}
