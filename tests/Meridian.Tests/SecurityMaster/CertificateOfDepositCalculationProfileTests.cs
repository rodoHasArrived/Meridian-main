using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class CertificateOfDepositCalculationProfileTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task LegacyRecords_DoNotAcquireTheExpandedAccrualModel(int termsVersion)
    {
        var security = LegacySecurity(termsVersion);
        var receipts = Substitute.For<ISecurityCashFlowReceiptStore>();
        var receipt = (await Service(security, receipts).CaptureProjectionAsync(security.SecurityId,
            StructuredCashFlowScenario.Base, new(AsOf, KnownAt: AsOf)))!;

        receipt.ModelVersion.Should().Be(SecurityMasterCashFlowService.ContextCalculationVersion);
        receipt.Projection.InputManifest.Should().NotContain(input => input.Kind == "AccrualPolicy");
        SecurityRecurringAccrualPolicies.IsProfileDeclared(security, SecurityRecurringAccrualPolicies.FixedBulletActual365)
            .Should().BeFalse();
        if (termsVersion == 1)
            receipt.Projection.BlockedReason.Should().Contain("no declared unit");
        else
        {
            receipt.Projection.BlockedReason.Should().BeNull();
            receipt.Projection.Schedule.Sum(row => row.InterestAmount).Should().Be(10m);
            receipt.Projection.Schedule.Should().OnlyContain(row => row.InterestSegments == null);
        }
    }

    [Fact]
    public async Task LiteralLegacyV5Receipt_AndANewLegacyCaptureKeepTheSameAnalyticalRows()
    {
        var security = LegacySecurity(5);
        const string literalRows = """
            [{"PeriodDate":"2027-01-01T00:00:00+00:00","PrincipalAmount":0,"InterestAmount":5.00,"Factor":1,
              "RowVersion":2,"InterestMeasuredFrom":"2026-01-01","InterestMeasuredTo":"2027-01-01"},
             {"PeriodDate":"2028-01-01T00:00:00+00:00","PrincipalAmount":100,"InterestAmount":5.00,"Factor":0,
              "RowVersion":2,"InterestMeasuredFrom":"2027-01-01","InterestMeasuredTo":"2028-01-01"}]
            """;
        var rows = JsonSerializer.Deserialize<List<StructuredCashFlowScheduleEntry>>(literalRows)!;
        var source = Source(security);
        var projection = new StructuredCashFlowProjectionDto(security.SecurityId, source.SourceKind,
            StructuredCashFlowScenario.Base, AsOf, rows, StructuredCashFlowStaleness.Fresh, AsOf,
            [], StructuredCashFlowTermsResolver.Resolve(security));
        var retainedTerms = JsonNode.Parse(security.AssetSpecificTerms.GetRawText())!.AsObject();
        retainedTerms.Remove(SecurityTermsEnvelope.FormatKey);
        var retainedSecurity = security with { AssetSpecificTerms = JsonSerializer.SerializeToElement(retainedTerms) };
        var historical = new SecurityCashFlowCalculationReceipt(SecurityMasterCashFlowService.ContextCalculationVersion,
            retainedSecurity, source, StructuredCashFlowScenario.Base, new(AsOf, KnownAt: AsOf, SecurityVersion: security.Version),
            [], projection, string.Empty);
        historical = historical with { ContentSha256 = SecurityMasterCashFlowService.ReceiptHash(historical) };
        var directory = Path.Combine(Path.GetTempPath(), "legacy-cd-receipt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSecurityCashFlowReceiptStore(directory);
            await store.RetainAsync(historical);
            historical.Security.AssetSpecificTerms.TryGetProperty(SecurityTermsEnvelope.FormatKey, out _).Should().BeFalse();
            var path = Path.Combine(directory, historical.ContentSha256 + ".json");
            var bytes = await File.ReadAllBytesAsync(path);
            var service = Service(security, new FileSecurityCashFlowReceiptStore(directory));

            var replay = (await service.ReplayProjectionAsync(historical.ContentSha256))!;
            var captured = (await service.CaptureProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base,
                new(AsOf, KnownAt: AsOf)))!;

            replay.BlockedReason.Should().BeNull();
            replay.Schedule.Should().Equal(rows);
            captured.ModelVersion.Should().Be(SecurityMasterCashFlowService.ContextCalculationVersion);
            captured.Projection.Schedule.Should().Equal(rows);
            (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
            (await store.GetAsync(historical.ContentSha256))!.Security.AssetSpecificTerms
                .TryGetProperty(SecurityTermsEnvelope.FormatKey, out _).Should().BeFalse("replay never rewrites retained security terms");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task UntaggedLiveTerms_AreRefusedInsteadOfUsingTheRetainedReceiptClassifier()
    {
        var security = LegacySecurity(5);
        var terms = JsonNode.Parse(security.AssetSpecificTerms.GetRawText())!.AsObject();
        terms.Remove(SecurityTermsEnvelope.FormatKey);
        security = security with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) };
        var receipts = Substitute.For<ISecurityCashFlowReceiptStore>();
        var receipt = (await Service(security, receipts).CaptureProjectionAsync(security.SecurityId,
            StructuredCashFlowScenario.Base, new(AsOf, KnownAt: AsOf)))!;

        receipt.Projection.Schedule.Should().BeEmpty("a live untagged record cannot select the legacy calculation path");
        receipt.Projection.Blocker.Should().NotBeNull();
        receipt.Projection.Blocker!.Code.Should().Be("TermsFormatUndeclared");
        receipt.Projection.BlockedReason.Should().Be(new SecurityTermsFormatUndeclaredException(JsonValueKind.Object).Message);
        receipt.Security.AssetSpecificTerms.GetRawText().Should().Be(security.AssetSpecificTerms.GetRawText());
        receipt.Security.AssetSpecificTerms.TryGetProperty(SecurityTermsEnvelope.FormatKey, out _).Should().BeFalse("live capture does not infer or stamp a family");

        await receipts.Received(1).RetainAsync(Arg.Is<SecurityCashFlowCalculationReceipt>(retained =>
            retained.ContentSha256 == receipt.ContentSha256 && retained.Projection.Schedule.Count == 0
            && retained.Projection.Blocker!.Code == "TermsFormatUndeclared"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("{\"termsFormat\":null}")]
    [InlineData("{\"termsFormat\":12}")]
    [InlineData("{\"termsFormat\":\"future-family\"}")]
    [InlineData("{\"termsFormat\":\"flat\",\"termsFormat\":\"customProfile\"}")]
    [InlineData("{\"schemaVersion\":2,\"coupon\":{\"couponRate\":5}}")]
    public async Task RetainedReplay_RefusesMalformedOrUnknownFamiliesWithoutReclassifyingThem(string malformedTerms)
    {
        var security = LegacySecurity(5);
        var receipts = Substitute.For<ISecurityCashFlowReceiptStore>();
        var service = Service(security, receipts);
        var original = (await service.CaptureProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base,
            new(AsOf, KnownAt: AsOf)))!;
        var invalid = original with
        {
            Security = security with { AssetSpecificTerms = JsonSerializer.Deserialize<JsonElement>(malformedTerms) },
            ContentSha256 = string.Empty
        };
        invalid = invalid with { ContentSha256 = SecurityMasterCashFlowService.ReceiptHash(invalid) };
        receipts.GetAsync(invalid.ContentSha256, Arg.Any<CancellationToken>()).Returns(invalid);

        var replay = (await service.ReplayProjectionAsync(invalid.ContentSha256))!;

        replay.Schedule.Should().BeEmpty();
        replay.BlockedReason.Should().Contain("terms");
        invalid.Security.AssetSpecificTerms.GetRawText().Should().Be(malformedTerms);
        SecurityMasterCashFlowService.ReceiptHash(invalid).Should().Be(invalid.ContentSha256);
    }

    [Fact]
    public async Task ExpandedDeclarationWithMissingAnchors_IsRefusedInsteadOfUsingLegacyFallbacks()
    {
        var security = SecurityRecurringAccrualPolicyTests.Security("principal", "null");
        var receipt = (await Service(security, Substitute.For<ISecurityCashFlowReceiptStore>()).CaptureProjectionAsync(
            security.SecurityId, StructuredCashFlowScenario.Base, new(AsOf, KnownAt: AsOf)))!;

        receipt.ModelVersion.Should().Be(SecurityMasterCashFlowService.ReviewedAccrualContextCalculationVersion);
        receipt.Projection.Schedule.Should().BeEmpty();
        receipt.Projection.Blocker!.Code.Should().Be("AccrualAnchorMissing");
    }

    [Theory]
    [InlineData("rateUnit", "null")]
    [InlineData("couponRate", "150")]
    public async Task ExpandedProfile_PreservesTheExistingScalarUnitAndRangeRefusal(string key, string value)
    {
        var security = SecurityRecurringAccrualPolicyTests.Security(key, value);
        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        var field = SecurityRateFieldUnits.CouponFieldKey(security.AssetClass);
        var normalized = RateUnitNormalizer.Normalize(security.AssetClass, field, terms.CouponRate!.Value, terms.RecordRateUnit);
        var expected = RateUnitNormalizer.DescribeRefusal(normalized, security.AssetClass, field);
        var receipt = (await Service(security, Substitute.For<ISecurityCashFlowReceiptStore>()).CaptureProjectionAsync(
            security.SecurityId, StructuredCashFlowScenario.Base, new(AsOf, KnownAt: AsOf)))!;

        receipt.Projection.Schedule.Should().BeEmpty();
        receipt.Projection.BlockedReason.Should().Be(expected);
    }

    private static SecurityDetailDto LegacySecurity(int termsVersion) => new(Guid.NewGuid(), "CertificateOfDeposit",
        SecurityStatusDto.Active, "Synthetic legacy CD", "USD",
        // These vendor aliases were already read by legacy analytics; they do not declare the
        // new canonical scalar contract on the asset-specific document.
        JsonSerializer.SerializeToElement(new { par = 100m, issueDate = "2026-01-01", paymentFrequency = "Annual" }),
        JsonSerializer.SerializeToElement(new
        {
            termsFormat = SecurityTermsFormats.Flat,
            assetClassTermsVersion = termsVersion,
            issuerName = "Synthetic bank",
            maturity = "2028-01-01",
            couponRate = 5m,
            rateUnit = "Percent",
            dayCount = "ACT/365"
        }),
        [], [], 1, AsOf, null);

    private static SecurityCashFlowSourceDto Source(SecurityDetailDto security)
        => new(security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, AsOf, false, null, null);

    private static SecurityMasterCashFlowService Service(SecurityDetailDto security, ISecurityCashFlowReceiptStore receipts)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(security.SecurityId, AsOf, Arg.Any<CancellationToken>()).Returns(security);
        var source = Substitute.For<ISecurityMasterCashFlowStore>();
        source.GetSourceAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(Source(security));
        return new(source, [], query, NullLogger<SecurityMasterCashFlowService>.Instance, receiptStore: receipts);
    }
}
