using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Pins the declared quote convention (remediation slice 8). Every catalogued class's declared
/// <see cref="SecurityAssetClassDescriptor.QuoteConvention"/> must match the legacy unit-kind
/// conversion (face-value lots quote as a percentage of par, everything else per unit), so the
/// declared fact changes no price today; when a class deliberately declares a different
/// convention, this pin fails and forces the class-aware callers onto
/// <see cref="LotQuantity.ValueAtPrice(decimal, SecurityQuoteConvention)"/> as a visible decision
/// rather than a silent repricing.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityQuoteConventionCoverageTests
{
    public static TheoryData<string> CatalogClasses()
    {
        var data = new TheoryData<string>();
        foreach (var definition in SecurityAssetClassRegistry.All)
        {
            data.Add(definition.AssetClass);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CatalogClasses))]
    public void Declared_convention_matches_the_legacy_unit_kind_rule(string assetClass)
    {
        var definition = SecurityAssetClassRegistry.Get(assetClass);
        var expected = definition.LotUnit == SecurityLotUnitKind.FaceValue
            ? SecurityQuoteConvention.PercentOfPar
            : SecurityQuoteConvention.PerUnit;

        definition.Descriptor.QuoteConvention.Should().Be(
            expected,
            $"{assetClass} must price identically through the declared convention and the legacy unit-kind rule; " +
            "a deliberate divergence updates this pin and moves class-aware callers onto the convention overload");
    }

    [Theory]
    [MemberData(nameof(CatalogClasses))]
    public void Both_pricing_routes_agree_for_every_catalog_class(string assetClass)
    {
        var definition = SecurityAssetClassRegistry.Get(assetClass);
        var lot = new LotQuantity(1_000m, definition.LotUnit, 1m);
        const decimal price = 99.5m;

        lot.ValueAtPrice(price).Should().Be(
            lot.ValueAtPrice(price, LotQuantity.QuoteConventionFor(assetClass)),
            $"the unit-kind default and {assetClass}'s declared convention must produce the same value");
    }

    [Fact]
    public void Face_value_and_per_unit_conversions_are_unchanged()
    {
        new LotQuantity(1_000m, SecurityLotUnitKind.FaceValue, 1m).ValueAtPrice(99.5m).Should().Be(995m);
        new LotQuantity(1_000m, SecurityLotUnitKind.Shares, 1m).ValueAtPrice(99.5m).Should().Be(99_500m);
        new LotQuantity(10m, SecurityLotUnitKind.Contracts, 100m).ValueAtPrice(2.5m).Should().Be(2_500m);
    }

    [Fact]
    public void Unknown_classes_have_no_quote_convention()
    {
        var resolve = static () => LotQuantity.QuoteConventionFor("NotACatalogClass");
        resolve.Should().Throw<ArgumentException>();
    }
}
