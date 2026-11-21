using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityDeclaredScheduleModuleContractionTests
{
    [Theory]
    [InlineData("Bond")]
    [InlineData("CertificateOfDeposit")]
    [InlineData("StructuredCredit")]
    public void CompleteDeclaredSource_SurvivesEconomicOnlyReplayWithoutLosingWindowsNoticesOrConventions(string assetClass)
    {
        var flat = DeclaringFlat(assetClass);
        var image = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, flat));
        var exact = image.EconomicTerms.GetProperty("declaredSchedule");
        foreach (var key in new[] { "scheduleConventions", "callSchedule", "putSchedule" })
            JsonElement.DeepEquals(exact.GetProperty(key), flat.GetProperty(key)).Should().BeTrue(key);
        var replay = SecurityEconomicDefinitionAdapter.ToProjection(image with { LegacyAssetSpecificTerms = null });
        foreach (var key in new[] { "scheduleConventions", "callSchedule", "putSchedule" })
            JsonElement.DeepEquals(replay.AssetSpecificTerms.GetProperty(key), flat.GetProperty(key)).Should().BeTrue(key);
        ReadCodec(assetClass, replay.AssetSpecificTerms).GetRawText().Should().Be(flat.GetRawText());
    }

    [Theory]
    [InlineData("businessDayConvention")]
    [InlineData("callPrice")]
    [InlineData("callType")]
    [InlineData("isParCall")]
    [InlineData("isPuttable")]
    [InlineData("makeWholeSpreadBps")]
    public void ContradictoryLegacySummaries_AreRefused(string field)
    {
        var flat = DeclaringFlat("Bond");
        var image = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", flat));
        var document = JsonNode.Parse(image.EconomicTerms.GetRawText())!.AsObject();
        if (field == "businessDayConvention")
            document["accrual"]![field] = "Preceding";
        else if (field == "callPrice")
            document["call"]!["callSchedule"]![0]!["callPx"] = 123m;
        else if (field == "callType")
            document["call"]!["callSchedule"]![0]![field] = "ContradictoryStyle";
        else if (field == "isParCall")
            document["call"]!["callSchedule"]![0]![field] = !document["call"]!["callSchedule"]![0]![field]!.GetValue<bool>();
        else if (field == "isPuttable")
            document["call"]![field] = !document["call"]![field]!.GetValue<bool>();
        else
            document["call"]![field] = 999m;
        Action replay = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(JsonSerializer.SerializeToElement(document), "Bond");
        replay.Should().Throw<InvalidOperationException>().WithMessage("*contradict*");
    }

    [Fact]
    public void PartialExactSource_IsRefusedRatherThanDefaultingMissingNoticeOrCalendarFields()
    {
        var flat = DeclaringFlat("Bond");
        var image = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", flat));
        var document = JsonNode.Parse(image.EconomicTerms.GetRawText())!.AsObject();
        document["declaredSchedule"]!["callSchedule"]![0]!.AsObject().Remove("maximumNoticeDays");
        Action replay = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(JsonSerializer.SerializeToElement(document), "Bond");
        replay.Should().Throw<InvalidOperationException>().WithMessage("*canonical member*");
    }

    [Fact]
    public void AnExerciseOnlyExactHeader_IsRefusedBecauseTheCanonicalBlockKeyIsMissing()
    {
        var image = Image();
        var document = JsonNode.Parse(image.GetRawText())!.AsObject();
        document["declaredSchedule"]!.AsObject().Remove("scheduleConventions");

        Action replay = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(JsonSerializer.SerializeToElement(document), "Bond");
        replay.Should().Throw<InvalidOperationException>().WithMessage("*convention block*");
    }

    [Theory]
    [InlineData("callSchedule", "callDate")]
    [InlineData("putSchedule", "putDate")]
    [InlineData("scheduleConventions", "paymentCalendars")]
    public void NullPrimaryDatesOrCalendarArrays_AreRefusedBeforeTypedReadersCanDropThem(string container, string field)
    {
        var document = JsonNode.Parse(Image().GetRawText())!.AsObject();
        var row = container == "scheduleConventions" ? document["declaredSchedule"]![container]!
            : document["declaredSchedule"]![container]![0]!;
        row[field] = null;

        Action replay = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(JsonSerializer.SerializeToElement(document), "Bond");
        replay.Should().Throw<InvalidOperationException>().WithMessage("*invalid canonical value*");
    }

    [Fact]
    public void CalendarSummary_UsesTheFrozenJointIdWhileRetainingTheOriginalArray()
    {
        var flat = JsonNode.Parse(DeclaringFlat("Bond").GetRawText())!.AsObject();
        flat["scheduleConventions"]!["paymentCalendars"] = new JsonArray(" USNY ", "usny", "GBLO");
        var image = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", JsonSerializer.SerializeToElement(flat)));
        image.EconomicTerms.GetProperty("accrual").GetProperty("holidayCalendar").GetString().Should().Be("GBLO+USNY");

        var replay = SecurityEconomicDefinitionAdapter.ToProjection(image with { LegacyAssetSpecificTerms = null });
        JsonElement.DeepEquals(replay.AssetSpecificTerms.GetProperty("scheduleConventions").GetProperty("paymentCalendars"),
            image.EconomicTerms.GetProperty("declaredSchedule").GetProperty("scheduleConventions").GetProperty("paymentCalendars"))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("MakeWhole", "PercentOfPar")]
    [InlineData("Fixed", "PercentOfAccretedValue")]
    public void APriceNotCarriedAsPar_InTheFrozenSummary_CannotAcquireAParSummary(string priceType, string priceBasis)
    {
        var flat = JsonNode.Parse(DeclaringFlat("Bond").GetRawText())!.AsObject();
        flat["callSchedule"]![0]!["priceType"] = priceType;
        flat["callSchedule"]![0]!["priceBasis"] = priceBasis;
        flat["callSchedule"]![0]!["callPrice"] = 101m;
        var image = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", JsonSerializer.SerializeToElement(flat)));
        var document = JsonNode.Parse(image.EconomicTerms.GetRawText())!.AsObject();
        var modifiedDate = flat["callSchedule"]![0]!["callDate"]!.GetValue<string>();
        document["call"]!["callSchedule"]!.AsArray()
            .Single(row => row!["callDt"]!.GetValue<string>() == modifiedDate)!["callPx"] = 101m;

        Action replay = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(JsonSerializer.SerializeToElement(document), "Bond");
        replay.Should().Throw<InvalidOperationException>().WithMessage("*contradict*");
    }

    [Theory]
    [InlineData("declaredSchedule")]
    [InlineData("call")]
    [InlineData("accrual")]
    public void RepeatedSourceOrSummaryModules_AreRefused(string module)
    {
        var image = Image();
        var json = image.GetRawText();
        json = json[..^1] + "," + JsonSerializer.Serialize(module) + ":" + image.GetProperty(module).GetRawText() + "}";

        Action replay = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(JsonDocument.Parse(json).RootElement, "Bond");
        replay.Should().Throw<InvalidOperationException>().WithMessage("*Repeated*");
    }

    private static JsonElement Image()
        => SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", DeclaringFlat("Bond"))).EconomicTerms;

    internal static JsonElement DeclaringFlat(string assetClass)
    {
        var document = JsonSerializer.SerializeToNode(SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass])!.AsObject();
        document["schemaVersion"] = 1;
        document["termsFormat"] = "flat";
        document["assetClassTermsVersion"] = SecurityAssetTermsSchema.Field(assetClass, "callSchedule")!.SinceTermsVersion;
        document["scheduleConventions"] = JsonNode.Parse(SecurityAssetTermsSchemaRoundTripTests.ReadableTwinBlocks[assetClass][0]);
        foreach (var pair in SecurityAssetTermsSchemaRoundTripTests.ReadableTwinSchedules[assetClass])
            document[pair.Key] = JsonNode.Parse(pair.Value);
        return ReadCodec(assetClass, JsonSerializer.SerializeToElement(document));
    }

    private static JsonElement ReadCodec(string assetClass, JsonElement document)
        => JsonDocument.Parse(new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, document))).AssetSpecificTermsJson).RootElement.Clone();
}
