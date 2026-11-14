using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;
using Microsoft.FSharp.Reflection;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityDebtModuleContractionReplayTests
{
    [Theory]
    [InlineData("Bond", "call", new[] { "isCallable", "callDate" })]
    [InlineData("Deposit", "call", new[] { "isCallable" })]
    [InlineData("CertificateOfDeposit", "call", new[] { "callableDate" })]
    [InlineData("TreasuryBill", "auction", new[] { "auctionDate" })]
    [InlineData("Bond", "coupon", new[] { "paymentFrequency" })]
    [InlineData("Bond", "debtClassification", new[] { "subclass", "seniority" })]
    [InlineData("Bond", "bondRedemption", new[] { "par", "legalFinalMaturity", "preRefundDate", "mandatoryPutDate" })]
    public void EachDebtGroup_PreservesFlatBytesAndEconomicOnlyReplay(string assetClass, string module, string[] fields)
    {
        var payload = FullPayload(assetClass);
        if (assetClass == "Bond")
        {
            payload["legalFinalMaturity"] = "2038-06-15";
            payload["preRefundDate"] = "2030-04-15";
            payload["mandatoryPutDate"] = "2031-03-15";
            payload["par"] = 1234.567890m;
        }
        AssertReplay(assetClass, module, fields, payload);
    }

    [Theory]
    [InlineData("Other \"historical\" subclass", "Every 13 weeks", "Unranked / retained")]
    [InlineData("Corporate", "Quarterly", "Senior Unsecured")]
    public void DebtClassificationAndCadence_PreserveOpenLabels(string subclass, string frequency, string seniority)
    {
        var payload = FullPayload("Bond");
        payload["subclass"] = subclass;
        payload["paymentFrequency"] = frequency;
        payload["seniority"] = seniority;
        var replay = AssertReplay("Bond", "debtClassification", ["subclass", "seniority", "paymentFrequency"], payload);
        replay.GetProperty("subclass").GetString().Should().Be(subclass);
        replay.GetProperty("paymentFrequency").GetString().Should().Be(frequency);
        replay.GetProperty("seniority").GetString().Should().Be(seniority);
    }

    [Fact]
    public void DeclaredCalls_DoNotReplaceBondOrDepositScalarCallability()
    {
        foreach (var assetClass in new[] { "Bond", "CertificateOfDeposit" })
        {
            var payload = FullPayload(assetClass);
            payload["assetClassTermsVersion"] = 3;
            payload["callSchedule"] = JsonNode.Parse("""[{"callDate":"2029-06-15","exerciseStyle":"European"}]""");
            if (assetClass == "Bond")
            {
                payload["isCallable"] = false;
                payload["callDate"] = "2030-06-15";
            }
            else
                payload["callableDate"] = null;
            var before = StoredCodec(assetClass, JsonSerializer.SerializeToElement(payload));
            var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
                SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, before));
            economic.EconomicTerms.GetProperty("call").GetProperty("isCallable").GetBoolean().Should().BeFalse();
            if (assetClass == "Bond")
                economic.EconomicTerms.GetProperty("call").GetProperty("firstCallDate").GetString().Should().Be("2030-06-15");
            else
                economic.EconomicTerms.GetProperty("call").GetProperty("firstCallDate").ValueKind.Should().Be(JsonValueKind.Null);
            AssertReplay(assetClass, "call", assetClass == "Bond" ? ["isCallable", "callDate"] : ["callableDate"], payload);
        }
    }

    [Fact]
    public void HistoricalDebtEventWithoutNewModules_RetainsItsFlatImage()
    {
        var before = StoredCodec("Bond", JsonSerializer.SerializeToElement(FullPayload("Bond")));
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", before));
        var historical = JsonNode.Parse(economic.EconomicTerms.GetRawText())!.AsObject();
        historical.Remove("debtClassification");
        historical.Remove("bondRedemption");
        var oldDocument = JsonSerializer.SerializeToElement(historical);
        var oldEvent = economic with { EconomicTerms = oldDocument };
        SecurityEconomicDefinitionAdapter.ToProjection(oldEvent).AssetSpecificTerms.GetRawText().Should().Be(before.GetRawText());
        oldEvent.EconomicTerms.GetRawText().Should().Be(oldDocument.GetRawText());
    }

    [Fact]
    public void OptionalRedemptionAndAuctionFacts_RemainUnset()
    {
        var bond = FullPayload("Bond");
        foreach (var field in new[] { "par", "legalFinalMaturity", "preRefundDate", "mandatoryPutDate" })
            bond[field] = null;
        var replayed = AssertReplay("Bond", "bondRedemption", ["par", "legalFinalMaturity", "preRefundDate", "mandatoryPutDate"], bond);
        replayed.GetProperty("par").ValueKind.Should().Be(JsonValueKind.Null);
        var bill = FullPayload("TreasuryBill");
        bill["auctionDate"] = null;
        AssertReplay("TreasuryBill", "auction", ["auctionDate"], bill).GetProperty("auctionDate")
            .ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(typeof(BondTerms), new[] { "IsCallable", "CallDate", "Seniority", "Subclass", "Par", "PaymentFrequency", "LegalFinalMaturity", "PreRefundDate", "MandatoryPutDate" })]
    [InlineData(typeof(DepositTerms), new[] { "IsCallable" })]
    [InlineData(typeof(CertificateOfDepositTerms), new[] { "CallableDate" })]
    [InlineData(typeof(TreasuryBillTerms), new[] { "AuctionDate" })]
    public void Records_KeepDebtFactsOnlyInTheirModules(Type recordType, string[] scalarFields)
        => FSharpType.GetRecordFields(recordType, null).Select(static field => field.Name).Should().NotIntersectWith(scalarFields);

    private static JsonElement AssertReplay(string assetClass, string module, string[] fields, JsonObject payload)
    {
        var before = StoredCodec(assetClass, JsonSerializer.SerializeToElement(payload));
        StoredCodec(assetClass, before).GetRawText().Should().Be(before.GetRawText());
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, before));
        economic.EconomicTerms.GetProperty(module).ValueKind.Should().Be(JsonValueKind.Object);
        SecurityEconomicDefinitionAdapter.ToProjection(economic).AssetSpecificTerms.GetRawText().Should().Be(before.GetRawText());
        var replayed = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });
        var after = StoredCodec(assetClass, replayed.AssetSpecificTerms);
        foreach (var field in fields)
            JsonElement.DeepEquals(after.GetProperty(field), before.GetProperty(field)).Should().BeTrue($"{assetClass}.{field} survives {module} replay");
        return after;
    }

    private static JsonObject FullPayload(string assetClass)
        => JsonSerializer.SerializeToNode(SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass])!.AsObject();

    private static JsonElement StoredCodec(string assetClass, JsonElement payload)
    {
        var record = SecurityMasterMapping.ToRecord(SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, payload));
        using var document = JsonDocument.Parse(new SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson);
        return document.RootElement.Clone();
    }
}
