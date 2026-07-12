using FluentAssertions;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Verifies the shared, data-driven reference taxonomies load from the embedded document and are
/// the single source of allowed-value vocabularies for both Security Master validation engines.
/// </summary>
public sealed class SecurityReferenceTaxonomyCatalogTests
{
    [Fact]
    public void Default_LoadsTaxonomiesFromEmbeddedData()
    {
        var catalog = SecurityReferenceTaxonomyCatalog.Default;

        catalog.GetValues(SecurityReferenceTaxonomyKeys.CollateralType)
            .Should().Equal("MBS", "CMBS", "ABS", "CLO", "CDO", "Other");
        catalog.GetValues(SecurityReferenceTaxonomyKeys.OptionPutCall)
            .Should().Equal("Put", "Call");
        catalog.GetTaxonomies().Select(static taxonomy => taxonomy.Key).Should().Contain(
        [
            SecurityReferenceTaxonomyKeys.CollateralType,
            SecurityReferenceTaxonomyKeys.PropertyType,
            SecurityReferenceTaxonomyKeys.ReportingCadence,
            SecurityReferenceTaxonomyKeys.OptionPutCall,
            SecurityReferenceTaxonomyKeys.WarrantType
        ]);

        // The embedded document is authoritative; the code fallback is only a wiring safety net.
        SecurityReferenceTaxonomyCatalog.LoadedFromEmbeddedResource.Should().BeTrue();
    }

    [Fact]
    public void SeededProfileEnumFields_ResolveAllowedValuesFromSharedTaxonomies()
    {
        var catalog = StaticSecurityAssetProfileCatalog.CreateDefault();
        var structuredCredit = catalog.GetProfiles()
            .Single(static profile => profile.ProfileId == "structured-credit-io-po");
        var collateralType = structuredCredit.Fields.Single(static field => field.Key == "collateralType");

        collateralType.AllowedValues.Should().Equal(
            SecurityReferenceTaxonomyCatalog.Default.GetValues(SecurityReferenceTaxonomyKeys.CollateralType));
    }

    [Fact]
    public void TryGetValues_UnknownKey_ReturnsFalseAndEmpty()
    {
        var found = SecurityReferenceTaxonomyCatalog.Default.TryGetValues("does-not-exist", out var values);

        found.Should().BeFalse();
        values.Should().BeEmpty();
    }
}
