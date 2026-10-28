using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.FSharp.Domain;
using Meridian.Storage.SecurityMaster;
using NSubstitute;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityTermsS2StampTests
{
    [Theory]
    [InlineData("Bond", 3, 1)]
    [InlineData("CertificateOfDeposit", 3, 1)]
    [InlineData("StructuredCredit", 4, 2)]
    [InlineData("Swap", 1, 1)]
    public void WritableCeiling_AndDocumentFloor_AreSeparate(string assetClass, int writes, int floor)
    {
        SecurityAssetClassCatalog.GetTermsVersion(assetClass).Should().Be(writes);
        SecurityAssetClassCatalog.GetStampFloor(assetClass).Should().Be(floor);
        const string json = "{\"schemaVersion\":1,\"maturity\":\"2030-01-01\"}";
        SecurityTermsEnvelope.StampFlat(json, assetClass).Should().Be(SecurityTermsEnvelope.StampFlat(json, floor));
        for (var version = floor; version <= SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass); version++)
            SecurityTermsEnvelope.StampFlat(json, assetClass, version).Should().Be(SecurityTermsEnvelope.StampFlat(json, version));
    }

    [Fact]
    public void LaterTransform_RaisesTheFloor_ButIdentityExpansionDoesNot()
    {
        var baseline = SecurityAssetClassCatalog.GetOrDefault("Bond");
        var identity = baseline with
        {
            TermsVersion = 4,
            TermsUpgrades = [.. baseline.TermsUpgrades!, SecurityAssetClassTermsUpgrade.Identity(3, "Optional expansion")]
        };
        var transform = identity with
        {
            TermsUpgrades = [.. baseline.TermsUpgrades!, SecurityAssetClassTermsUpgrade.Transforming(3, "Required transformation", node => node)]
        };
        SecurityAssetClassCatalog.GetStampFloor(identity).Should().Be(1);
        SecurityAssetClassCatalog.GetStampFloor(transform).Should().Be(4);
    }

    [Fact]
    public void TransformThenIdentity_StampsOnlyTheTransformFloor_AndDoesNotPromoteLaterStrayContainers()
    {
        var descriptor = SecurityAssetClassCatalog.GetOrDefault("Bond") with
        {
            TermsUpgrades =
            [
                SecurityAssetClassTermsUpgrade.Transforming(1, "Synthetic version-two issuer rename", node =>
                {
                    node["issuerName"] = node["legacyIssuer"]?.DeepClone();
                    node.Remove("legacyIssuer");
                    return node;
                }),
                SecurityAssetClassTermsUpgrade.Identity(2, "Version-three call arrays remain optional")
            ]
        };
        var source = JsonSerializer.Deserialize<JsonElement>("""
            {"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1,"legacyIssuer":"Issuer",
             "maturity":"2031-01-01","couponRate":4,"couponType":"Fixed","isCallable":false,"subclass":"Corporate",
             "callSchedule":[{"callDate":"2030-01-01","exerciseStyle":"European"}]}
            """);
        var before = source.GetRawText();
        SecurityAssetClassTermsUpgrader.TryUpgradeForRead(descriptor, source, out var upgraded).Should().BeTrue();
        upgraded.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(2);
        upgraded.GetProperty("issuerName").GetString().Should().Be("Issuer");
        upgraded.GetProperty("callSchedule").GetArrayLength().Should().Be(1, "the later key is retained as stray evidence without activating it");
        var kind = (SecurityKind.Bond)SecurityMasterMapping.ToRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", upgraded)).Kind;
        kind.Item.CallPutSchedules.Calls.ToArray().Should().BeEmpty("the transformed document stamp does not reach the later container");
        source.GetRawText().Should().Be(before, "transform execution never mutates the retained source");
        SecurityAssetClassTermsUpgrader.TryUpgradeForRead(descriptor, upgraded, out var unchanged).Should().BeFalse();
        unchanged.GetRawText().Should().Be(upgraded.GetRawText());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public void DecodedBlock_RaisesDocumentStamp_WhileLowerStampedStrayBlockDoesNot(int stamp, int expected)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            termsFormat = "flat",
            assetClassTermsVersion = stamp,
            scheduleConventions = new { businessDayConvention = "Unadjusted", couponRateUnit = "Percent", couponCashBasis = "DayCount" }
        });
        SecurityTermsEnvelope.GetDocumentStampVersion("Bond", payload).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 3)]
    public void CallSchedule_OnlyRaisesTheStampWhenTheDocumentDeclaresIt(int stamp, int expected)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            termsFormat = "flat",
            assetClassTermsVersion = stamp,
            callSchedule = new[] { new { callDate = "2030-01-01", exerciseStyle = "European" } }
        });
        SecurityTermsEnvelope.GetDocumentStampVersion("Bond", payload).Should().Be(expected);
        SecurityAssetClassTermsUpgrader.UpgradeForRead("Bond", payload).GetRawText().Should().Be(payload.GetRawText(),
            "identity-only expansion never restamps a retained lower-version document or activates its stray keys");
    }

    [Theory]
    [InlineData("Bond", 1)]
    [InlineData("CertificateOfDeposit", 1)]
    [InlineData("StructuredCredit", 2)]
    public void UndeclaredFloorStampedRecord_DoesNotGetAWriteCeilingBacklogNotice(string assetClass, int stamp)
    {
        var effective = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var terms = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            termsFormat = "flat",
            assetClassTermsVersion = stamp,
            maturity = "2030-01-01",
            issuerName = "Issuer",
            isCallable = false,
            subclass = "Corporate",
            tranche = "A",
            collateralType = "CLO",
            originalFace = 1000000m,
            couponOrIndex = "SOFR"
        });
        var record = new SecurityProjectionRecord(Guid.NewGuid(), assetClass, SecurityStatusDto.Active,
            "S2", "USD", "Internal", "S2", JsonSerializer.SerializeToElement(new { }), terms,
            JsonSerializer.SerializeToElement(new { }), 1, effective, null, [], []);
        var report = new SecurityValidationService(Substitute.For<ISecurityMasterStore>(), AssetClassValidatorRegistry.CreateDefault())
            .ValidateRecord(record, [record], effective);
        report.Issues.Should().NotContain(issue => issue.Code == "SM_ASSET_CLASS_TERMS_VERSION_BEHIND");
    }

    [Fact]
    public void ProfileEnvelope_DoesNotDeclareVersionedClassTerms()
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 3,
            termsFormat = "customProfile",
            assetClassTermsVersion = 4,
            customProfileId = "structured-credit-io-po",
            profileVersion = 1,
            profileFields = new { },
            callSchedule = new[] { new { callDate = "2030-01-01" } }
        });
        SecurityTermsEnvelope.GetDocumentStampVersion("StructuredCredit", payload).Should().Be(2);
    }
}
