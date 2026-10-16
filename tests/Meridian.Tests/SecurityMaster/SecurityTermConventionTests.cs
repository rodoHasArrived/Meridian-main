using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityTermConventionTests
{
    [Fact]
    public void DayCountFields_AreTypedConventions_OnEveryClassThatCarriesOne()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            if (SecurityAssetTermsSchema.Field(assetClass, "dayCount") is { } field)
            {
                field.Convention.Should().Be(SecurityTermConventionKind.DayCount, assetClass);
            }
        }
    }

    [Theory]
    [InlineData("Option")]
    [InlineData("Future")]
    public void DerivativeSettlementType_IsATypedConvention(string assetClass)
        => SecurityAssetTermsSchema.Field(assetClass, "settlementType")!.Convention
            .Should().Be(SecurityTermConventionKind.SettlementMethod);

    [Theory]
    [InlineData("Cash", SecuritySettlementMethod.Cash)]
    [InlineData("cash settled", SecuritySettlementMethod.Cash)]
    [InlineData("Physical Delivery", SecuritySettlementMethod.Physical)]
    [InlineData("Delivery", SecuritySettlementMethod.Physical)]
    [InlineData("Elective", SecuritySettlementMethod.Elective)]
    public void SettlementMethod_ParsesCommonSpellings(string raw, SecuritySettlementMethod expected)
    {
        SecurityTermConventions.TryParseSettlementMethod(raw, out var method).Should().BeTrue();
        method.Should().Be(expected);
    }

    [Fact]
    public void Readers_ReturnTypedValues()
    {
        var terms = JsonSerializer.SerializeToElement(new { dayCount = "ACT/360", settlementType = "Physical", putCall = "Put" });

        SecurityTermConventions.ReadDayCount(terms).Should().Be(DayCountConvention.Actual360);
        SecurityTermConventions.ReadSettlementMethod(terms).Should().Be(SecuritySettlementMethod.Physical);
        SecurityTermConventions.ReadOptionRight(terms).Should().Be(SecurityOptionRight.Put);
    }

    [Fact]
    public void FieldEdit_RejectsAnUnrecognizedDayCount_AndAcceptsARecognizedSpelling()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.dayCount", "every other tuesday", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("day-count convention");

        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.dayCount", "30/360", out _, out _)
            .Should().BeTrue();
    }

    [Fact]
    public void ConventionHints_OfferTheCanonicalSpellings()
    {
        var hint = SecurityAssetClassRegistry.Get("Bond").UiHints.Single(static hint => hint.Key == "dayCount");

        hint.Editor.Should().Be(SecurityAssetTermEditorKind.Convention);
        hint.Choices.Should().Contain(DayCountConvention.Actual360.ToString()).And.NotContain(DayCountConvention.Unknown.ToString());
    }
}
