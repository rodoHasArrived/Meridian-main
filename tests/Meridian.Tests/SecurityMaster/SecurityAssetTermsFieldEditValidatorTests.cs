using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityAssetTermsFieldEditValidatorTests
{
    [Theory]
    [InlineData("Identity.Isin")]
    [InlineData("EconomicDefinition.Coupon")]
    [InlineData("operatorNote")]
    public void PathsOutsideAssetTermsNamespace_AreAlwaysValid(string fieldPath)
    {
        SecurityAssetTermsFieldEditValidator.TargetsAssetSpecificTerms(fieldPath).Should().BeFalse();
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", fieldPath, "anything", out _, out var error).Should().BeTrue();
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("Bond", "assetSpecificTerms.maturity", "2031-06-15")]
    [InlineData("Bond", "assetSpecificTerms.couponRate", "4.25")]
    [InlineData("Bond", "assetSpecificTerms.isCallable", "true")]
    [InlineData("Bond", "assetSpecificTerms.issuerName", "ACME Corp")]
    [InlineData("Option", "assetSpecificTerms.underlyingId", "6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("Future", "assetSpecificTerms.rollWindowDays", "5")]
    [InlineData("Swap", "assetSpecificTerms.legs", "[{\"legType\":\"Fixed\"}]")]
    public void DeclaredFieldWithTypeConformingValue_IsValid(string assetClass, string fieldPath, string value)
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(assetClass, fieldPath, value, out _, out var error)
            .Should().BeTrue(error);
    }

    [Theory]
    [InlineData("Bond", "assetSpecificTerms.maturity", "not-a-date")]
    [InlineData("Bond", "assetSpecificTerms.couponRate", "four-and-a-quarter")]
    [InlineData("Bond", "assetSpecificTerms.isCallable", "yes")]
    [InlineData("Option", "assetSpecificTerms.underlyingId", "not-a-guid")]
    [InlineData("Future", "assetSpecificTerms.rollWindowDays", "5.5")]
    [InlineData("Swap", "assetSpecificTerms.legs", "{\"legType\":\"Fixed\"}")]
    public void DeclaredFieldWithTypeViolatingValue_IsRejected(string assetClass, string fieldPath, string value)
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(assetClass, fieldPath, value, out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("does not parse as the declared type");
    }

    [Fact]
    public void UndeclaredField_IsRejectedWithDeclaredFieldList()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.strike", "100", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("'strike' is not a declared asset-specific term")
            .And.Contain("Bond")
            .And.Contain("maturity");
    }

    [Fact]
    public void SchemaVersion_IsNeverOperatorEditable()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.schemaVersion", "3", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("not operator-editable");
    }

    [Fact]
    public void LegacyAlias_IsAcceptedAsTheDeclaredField()
    {
        // "dayCountConvention" is a declared legacy alias of Bond "dayCount".
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.dayCountConvention", "30/360", out _, out var error)
            .Should().BeTrue(error);
    }

    [Fact]
    public void NestedPathUnderScalarField_IsRejected()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Option", "assetSpecificTerms.strike.value", "100", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("has no nested path");
    }

    [Fact]
    public void NestedPathUnderObjectField_IsAccepted()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(
                "Equity", "assetSpecificTerms.preferredTerms.dividendRate", "6.25", out _, out var error)
            .Should().BeTrue(error);
    }

    [Fact]
    public void ProfileFieldsPath_OnProfileBackedClass_IsAccepted()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(
                "CustomAsset", "assetSpecificTerms.profileFields.tranche", "A2", out _, out var error)
            .Should().BeTrue(error);
    }

    [Theory]
    [InlineData("CustomAsset")]
    [InlineData("OtherSecurity")]
    public void ProfileFieldsRoot_OnProfileBackedClass_RequiresJsonObject(string assetClass)
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(
                assetClass, "assetSpecificTerms.profileFields", "{\"tranche\":\"A2\"}", out _, out var validError)
            .Should().BeTrue(validError);

        SecurityAssetTermsFieldEditValidator.TryValidate(
                assetClass, "assetSpecificTerms.profileFields", "not-json", out _, out var textError)
            .Should().BeFalse();
        textError.Should().Contain("declared type Object");

        SecurityAssetTermsFieldEditValidator.TryValidate(
                assetClass, "assetSpecificTerms.profileFields", "[]", out _, out var arrayError)
            .Should().BeFalse();
        arrayError.Should().Contain("declared type Object");
    }

    [Fact]
    public void ProfileFieldsPath_OnNonProfileBackedClass_IsRejected()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(
                "Equity", "assetSpecificTerms.profileFields.custom", "x", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("not a declared asset-specific term");
    }

    [Fact]
    public void BlankValue_ClearsTheOverlayAndIsAccepted()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.couponRate", "  ", out _, out var error)
            .Should().BeTrue(error);
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.couponRate", null, out _, out error)
            .Should().BeTrue(error);
    }

    [Fact]
    public void BarePrefixWithoutKey_IsRejected()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.", "x", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("must name a term field");
    }

    [Theory]
    // An accepted legacy alias must normalize to the declared field key so both spellings share one
    // override key, revision lineage, and provenance row.
    [InlineData("Bond", "assetSpecificTerms.dayCountConvention", "30/360", "assetSpecificTerms.dayCount")]
    // Casing variants of the prefix and key collapse to the canonical spelling.
    [InlineData("Bond", "ASSETSPECIFICTERMS.CouponRate", "4.25", "assetSpecificTerms.couponRate")]
    // Nested paths keep their remainder while the declared root key is normalized.
    [InlineData("Equity", "assetSpecificTerms.PreferredTerms.dividendRate", "6.25", "assetSpecificTerms.preferredTerms.dividendRate")]
    // profileFields paths normalize the envelope key on profile-backed classes.
    [InlineData("CustomAsset", "assetSpecificTerms.ProfileFields.tranche", "A2", "assetSpecificTerms.profileFields.tranche")]
    public void AcceptedAssetTermPaths_NormalizeToTheDeclaredFieldKey(
        string assetClass, string fieldPath, string value, string expectedCanonicalPath)
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(assetClass, fieldPath, value, out var canonicalPath, out var error)
            .Should().BeTrue(error);
        canonicalPath.Should().Be(expectedCanonicalPath);
    }

    [Fact]
    public void PathsOutsideAssetTermsNamespace_PassThroughUnchanged()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "operatorNote", "context", out var canonicalPath, out _)
            .Should().BeTrue();
        canonicalPath.Should().Be("operatorNote");
    }

    [Fact]
    public void EveryDeclaredAssetClass_AcceptsItsOwnDeclaredKeys()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
            {
                SecurityAssetTermsFieldEditValidator
                    .TryValidate(assetClass, $"assetSpecificTerms.{field.Key}", null, out _, out var error)
                    .Should().BeTrue($"'{assetClass}.{field.Key}' is declared, but was rejected: {error}");
            }
        }
    }
}
