using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;
using Microsoft.FSharp.Reflection;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityExactRateScalarContractionReplayTests
{
    [Theory]
    [InlineData("CommitmentGuarantee", "contractualRate", new[] { "feeRate", "collateral" })]
    [InlineData("CreditDefaultSwap", "contractualRate", new[] { "fixedCouponBps", "paymentFrequency", "dayCount" })]
    [InlineData("Swaption", "underlyingSwap", new[] { "underlyingMaturityDate", "floatingIndex", "fixedLegPaymentFrequency", "fixedLegDayCount", "settlementType" })]
    [InlineData("CapFloor", "contractualRate", new[] { "dayCount" })]
    [InlineData("StructuredNote", "contractualRate", new[] { "couponRate", "paymentFrequency" })]
    public void EachRemainingRateGroup_PreservesItsExactStoredFieldsOnEconomicOnlyReplay(string assetClass, string module, string[] fields)
    {
        var terms = Mutable(assetClass);
        var before = StoredBytes(assetClass, JsonSerializer.SerializeToElement(terms));
        AssertReplay(assetClass, before, fields, module);
    }

    [Theory]
    [InlineData("Quarterly")]
    [InlineData(" SemiAnnual ")]
    [InlineData("Unregistered cadence")]
    public void RawCdsBasisPointsAndCadence_AreNeverRoundTrippedThroughAFractionOrEnum(string cadence)
    {
        var terms = Mutable("CreditDefaultSwap");
        const decimal exactBps = 0.1234567890123456789012345678m;
        terms["fixedCouponBps"] = exactBps;
        terms["paymentFrequency"] = cadence;
        var before = StoredBytes("CreditDefaultSwap", JsonSerializer.SerializeToElement(terms));
        before.GetProperty("fixedCouponBps").GetDecimal().Should().Be(exactBps);
        before.GetProperty("paymentFrequency").GetString().Should().Be(cadence);
        AssertReplay("CreditDefaultSwap", before, ["fixedCouponBps", "paymentFrequency", "dayCount"], "contractualRate");
    }

    [Theory]
    [InlineData(null, 0d)]
    [InlineData("", -0.125d)]
    [InlineData("Pledged assets; vendor wording preserved", 0.000125d)]
    public void CommitmentCollateralAndFee_PreserveOptionalRawFacts(string? collateral, double fee)
    {
        var terms = Mutable("CommitmentGuarantee");
        terms["collateral"] = collateral;
        terms["feeRate"] = (decimal)fee;
        AssertReplay("CommitmentGuarantee", StoredBytes("CommitmentGuarantee", JsonSerializer.SerializeToElement(terms)),
            ["feeRate", "collateral"], "contractualRate");
    }

    [Fact]
    public void ContingentNoteCadenceWithoutRate_RemainsAnExactDeclaredCadence()
    {
        var terms = Mutable("StructuredNote");
        terms["couponRate"] = null;
        terms["paymentFrequency"] = " contingent settlement cadence ";
        AssertReplay("StructuredNote", StoredBytes("StructuredNote", JsonSerializer.SerializeToElement(terms)),
            ["couponRate", "paymentFrequency"], "contractualRate");
    }

    [Theory]
    [InlineData("Deposit")]
    [InlineData("CertificateOfDeposit")]
    [InlineData("CommercialPaper")]
    [InlineData("TreasuryBill")]
    [InlineData("Repo")]
    [InlineData("CashSweep")]
    [InlineData("Swap")]
    [InlineData("DirectLoan")]
    [InlineData("StructuredCredit")]
    [InlineData("Swaption")]
    [InlineData("CapFloor")]
    [InlineData("StructuredNote")]
    public void RateConventionStorage_IsTheOnlyUnitHomeAndPreservesTheCodecBytes(string assetClass)
    {
        foreach (var unit in new[] { "Fraction", "Percent", "BasisPoints", "Retained unknown unit" })
        {
            var terms = Mutable(assetClass);
            terms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = SecurityAssetTermsSchema.Field(assetClass, "rateUnit")!.SinceTermsVersion;
            terms["rateUnit"] = unit;
            var before = StoredBytes(assetClass, JsonSerializer.SerializeToElement(terms));
            AssertReplay(assetClass, before, ["rateUnit"], "rateConvention");
            var kind = SecurityMasterMapping.ToRecord(SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, before)).Kind;
            var kindPayload = FSharpValue.GetUnionFields(kind, typeof(SecurityKind), null).Item2.Single();
            FSharpType.GetRecordFields(kindPayload.GetType(), null).Select(field => field.Name)
                .Should().Contain("RateConvention").And.NotContain("RateUnit");
        }
    }

    private static void AssertReplay(string assetClass, JsonElement before, string[] fields, string module)
    {
        var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, before);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        economic.EconomicTerms.GetProperty(module).ValueKind.Should().Be(JsonValueKind.Object);
        var source = economic.EconomicTerms.GetRawText();
        var replayed = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });
        foreach (var field in fields)
        {
            var expected = before.TryGetProperty(field, out var old) ? old.GetRawText() : "null";
            var actual = replayed.AssetSpecificTerms.TryGetProperty(field, out var next) ? next.GetRawText() : "null";
            actual.Should().Be(expected, $"{assetClass}.{field} is an exact retained fact");
        }
        StoredBytes(assetClass, replayed.AssetSpecificTerms).GetRawText().Should().Be(before.GetRawText());
        economic.EconomicTerms.GetRawText().Should().Be(source, "replay never rewrites the historical source");
    }

    private static JsonObject Mutable(string assetClass)
        => JsonSerializer.SerializeToNode(SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass])!.AsObject();

    private static JsonElement StoredBytes(string assetClass, JsonElement terms)
    {
        var record = SecurityMasterMapping.ToRecord(SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, terms));
        using var document = JsonDocument.Parse(new SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson);
        return document.RootElement.Clone();
    }
}
