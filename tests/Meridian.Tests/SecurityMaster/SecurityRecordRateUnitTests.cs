using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityRecordRateUnitTests
{
    [Theory]
    [InlineData("Fraction", "0.005")]
    [InlineData("Percent", "0.5")]
    [InlineData("BasisPoints", "50")]
    public void DeclaredUnit_NormalizesSubOneRateWithoutMagnitudeInference(string unit, string raw)
    {
        var terms = Json($$"""{"assetClassTermsVersion":5,"rateUnit":"{{unit}}"}""");
        var read = SecurityRecordRateUnit.Read("CertificateOfDeposit", terms);
        var result = RateUnitNormalizer.Normalize("CertificateOfDeposit", "couponRate",
            decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture), read);
        result.Should().BeOfType<RateUnitResolution.Resolved>().Which.Fraction.Should().Be(0.005m);
    }

    [Fact]
    public void OlderStoredStamp_DoesNotActivateStrayUnit()
    {
        var terms = Json("""{"assetClassTermsVersion":1,"rateUnit":"Percent"}""");
        SecurityRecordRateUnit.Read("CertificateOfDeposit", terms).Should().BeNull();
        RateUnitNormalizer.Normalize("CertificateOfDeposit", "couponRate", 0.5m,
            SecurityRecordRateUnit.Read("CertificateOfDeposit", terms)).Should().BeOfType<RateUnitResolution.Undeclared>();
    }

    [Fact]
    public void ExplicitInitialWriteDeclaration_IsReadAtTheWriterCeilingWhileStoredResidueStaysInert()
    {
        var terms = Json("""{"assetClassTermsVersion":1,"rateUnit":"Percent"}""");
        SecurityRecordRateUnit.ReadToken("Deposit", terms).Should().BeNull();
        SecurityRecordRateUnit.ReadToken("Deposit", terms, writeMode: true).Should().Be("Percent");
    }

    [Theory]
    [InlineData("\"percent\"")]
    [InlineData("42")]
    [InlineData("{}")]
    public void MalformedDeclaration_DoesNotFallBackToDepositFractionDefault(string value)
    {
        var terms = Json($$"""{"assetClassTermsVersion":2,"rateUnit":{{value}}}""");
        RateUnitNormalizer.Normalize("Deposit", "interestRate", 0.5m,
            SecurityRecordRateUnit.Read("Deposit", terms)).Should().BeOfType<RateUnitResolution.Undeclared>();
    }

    [Fact]
    public void UnitField_DoesNotChangeDefaultsOrBasisPointFields()
    {
        SecurityRateFieldUnits.Find("CertificateOfDeposit", "couponRate")!.DefaultUnit.Should().BeNull();
        var result = RateUnitNormalizer.Normalize("DirectLoan", "spreadBps", 125m, RateUnit.Percent);
        result.Should().BeOfType<RateUnitResolution.Resolved>().Which.Fraction.Should().Be(0.0125m);
    }

    [Theory]
    [InlineData("Fraction")]
    [InlineData("Percent")]
    [InlineData("BasisPoints")]
    public void GovernedEdit_AcceptsClosedUnitVocabulary(string unit)
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("CertificateOfDeposit", "assetSpecificTerms.rateUnit",
            unit, out var path, out var reason).Should().BeTrue(reason);
        path.Should().Be("assetSpecificTerms.rateUnit");
        SecurityAssetTermsFieldEditValidator.TryValidate("CertificateOfDeposit", "assetSpecificTerms.rateUnit",
            "percent", out _, out _).Should().BeFalse();
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.rateUnit",
            unit, out _, out _).Should().BeFalse("Bond declares its unit in scheduleConventions");
    }

    [Fact]
    public void EconomicOnlyReplay_RestoresDeclaredUnitAndItsVersion()
    {
        var economic = Json("""{"schemaVersion":2,"termsFormat":"economic","rateConvention":{"rateUnit":"Percent"}}""");
        var flat = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(economic, "CertificateOfDeposit").Payload;
        SecurityRecordRateUnit.Read("CertificateOfDeposit", flat).Should().Be(RateUnit.Percent);
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(flat).Should().Be(5);
    }

    [Fact]
    public void NewUnitVersion_DoesNotActivateCalculationOnlyCompanion()
    {
        var terms = Json("""{"assetClassTermsVersion":5,"rateUnit":"Percent","cashFlowContract":{"version":1}}""");
        SecurityTermsEnvelope.TryGetWriteRefusal("CertificateOfDeposit", terms, out var reason).Should().BeTrue();
        reason.Should().Contain("cashFlowContract");
        SecurityAssetTermsFieldEditValidator.TryValidate("CertificateOfDeposit", "assetSpecificTerms.cashFlowContract",
            "{}", out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("CertificateOfDeposit")]
    [InlineData("StructuredCredit")]
    [InlineData("Swap")]
    public void DeclaredUnit_PromotesBeforeFrozenContainerFill(string assetClass)
    {
        var terms = System.Text.Json.JsonSerializer.SerializeToNode(SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass])!.AsObject();
        terms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = SecurityAssetTermsSchema.Field(assetClass, "rateUnit")!.SinceTermsVersion;
        terms["rateUnit"] = "Percent";
        terms.Remove("scheduleConventions");
        terms.Remove("callSchedule");
        terms.Remove("putSchedule");
        if (assetClass == "Swap")
            foreach (var leg in terms["legs"]!.AsArray())
                leg!.AsObject().Remove("scheduleConventions");
        var record = Meridian.Application.SecurityMaster.SecurityMasterMapping.ToRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, JsonSerializer.SerializeToElement(terms)));
        using var written = JsonDocument.Parse(new Meridian.FSharp.SecurityMasterInterop.SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson);
        var payload = written.RootElement;
        if (assetClass == "Swap")
            foreach (var leg in payload.GetProperty("legs").EnumerateArray())
                leg.GetProperty("scheduleConventions").ValueKind.Should().Be(JsonValueKind.Null);
        else
        {
            payload.GetProperty("scheduleConventions").ValueKind.Should().Be(JsonValueKind.Null);
            payload.GetProperty("callSchedule").GetArrayLength().Should().Be(0);
            payload.GetProperty("putSchedule").GetArrayLength().Should().Be(0);
        }
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
