using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class ReportingSecurityInputFormatBoundaryTests
{
    [Fact]
    public void FrozenPreFormatReceipt_ReadsItsDeclaredRateWithoutChangingAnyRetainedBytesOrDigest()
    {
        var receipt = ReportingSecurityInputTestData.Receipt();
        var originalTerms = receipt.Security.AssetSpecificTerms.GetRawText();
        var originalPayload = SecurityMasterCashFlowService.ExportReceiptEvidence(receipt);
        using var payload = JsonDocument.Parse(originalPayload);
        SecurityTermsEnvelope.GetDeclaredFormat(receipt.Security.AssetSpecificTerms).Should().BeNull();
        ReportingSecurityInputEvidence.ReceiptContentHash(payload.RootElement).Should().Be(receipt.ContentSha256);

        var rates = ReleasedReportSecurityInputReadService.Rates(receipt.Security, retainedInput: true);

        rates.Single(rate => rate.Field == "couponRate").NormalizedFraction.Should().Be("0.0525");
        receipt.Security.AssetSpecificTerms.GetRawText().Should().Be(originalTerms);
        SecurityMasterCashFlowService.ExportReceiptEvidence(receipt).Should().Be(originalPayload);
        SecurityMasterCashFlowService.ReceiptHash(receipt).Should().Be(receipt.ContentSha256);
    }

    [Fact]
    public void LiveRead_DoesNotUseTheFrozenReceiptClassifier()
    {
        var security = ReportingSecurityInputTestData.Receipt().Security;
        var read = () => ReleasedReportSecurityInputReadService.Rates(security);
        read.Should().Throw<SecurityTermsFormatUndeclaredException>();

        var live = security with { AssetSpecificTerms = SecurityTermsEnvelope.StampDeclaredFormat(security.AssetSpecificTerms) };
        ReleasedReportSecurityInputReadService.Rates(live).Single(rate => rate.Field == "couponRate")
            .NormalizedFraction.Should().Be("0.0525");
    }

    [Theory]
    [InlineData("""{"termsFormat":"future-family","assetClassTermsVersion":2,"couponRate":5.25}""")]
    [InlineData("""{"termsFormat":null,"assetClassTermsVersion":2,"couponRate":5.25}""")]
    [InlineData("""{"termsFormat":12,"assetClassTermsVersion":2,"couponRate":5.25}""")]
    [InlineData("""{"termsFormat":"flat","termsFormat":"customProfile","assetClassTermsVersion":2,"couponRate":5.25}""")]
    [InlineData("""{"schemaVersion":2,"coupon":{"couponRate":5.25}}""")]
    public void FrozenRead_RefusesUnknownMalformedAndAmbiguousFamiliesInsteadOfFallingBack(string terms)
    {
        var security = ReportingSecurityInputTestData.Receipt().Security with
        { AssetSpecificTerms = JsonSerializer.Deserialize<JsonElement>(terms) };
        var read = () => ReleasedReportSecurityInputReadService.Rates(security, retainedInput: true);
        read.Should().Throw<InvalidDataException>();
        security.AssetSpecificTerms.GetRawText().Should().Be(terms);
    }
}
