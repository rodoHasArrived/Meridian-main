using System.Reflection;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Holds the C# copies of per-class facts to the F# <c>AssetClassRegistry</c> classification table.
/// <para>
/// <c>Meridian.Contracts</c> cannot reference <c>Meridian.FSharp</c> (the reference runs the other
/// way), so the class-name constants and the instrument-type descriptor labels cannot be derived
/// from the F# table at compile time. These tests are the guard instead: a class added to, renamed
/// in, or reclassified in the F# table fails here until every C# copy agrees. The semantic guards
/// keep the table itself internally consistent (an OTC contract cannot be filed as listed, and a
/// derivative flag cannot disagree with the asset class).
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetClassFactParityTests
{
    private static readonly string[] RegistryClasses = AssetClassRegistry.assetClasses.ToArray();

    [Fact]
    public void AssetClassNameConstants_MatchTheFSharpRegistryExactly()
    {
        var constants = typeof(SecurityAssetClassNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.IsLiteral && field.FieldType == typeof(string))
            .ToArray();

        constants.Should().OnlyContain(field => (string)field.GetRawConstantValue()! == field.Name,
            "each constant's value is its own name");
        constants.Select(static field => (string)field.GetRawConstantValue()!)
            .Should().BeEquivalentTo(RegistryClasses,
                "every F# asset class needs exactly one SecurityAssetClassNames constant");
    }

    [Fact]
    public void InstrumentTypeDescriptors_AgreeWithTheFSharpClassificationTable()
    {
        foreach (var descriptor in InstrumentTypeDescriptorCatalog.All)
        {
            var registry = AssetClassRegistry.tryDescriptorByName(descriptor.SecurityMasterAssetClass);
            registry.Should().NotBeNull(
                $"{descriptor.InstrumentType} maps to '{descriptor.SecurityMasterAssetClass}', which the F# registry does not declare");

            var expectedFamily = registry!.Value.Family is null
                ? null
                : SecurityClassificationModule.familyName(registry.Value.Family.Value);
            var expectedSubType = SecurityClassificationModule.subTypeName(registry.Value.SubType);

            AssertAgrees(descriptor, InstrumentTypeDescriptorLabelField.AssetFamily, descriptor.SecurityMasterAssetFamily, expectedFamily);
            AssertAgrees(descriptor, InstrumentTypeDescriptorLabelField.SubType, descriptor.SecurityMasterSubType, expectedSubType);
        }
    }

    [Fact]
    public void EveryAssetClass_DeclaresAFamily_ExceptTheFreeTextOtherSecurity()
    {
        RegistryClasses
            .Where(static name => Descriptor(name).Family is null)
            .Should().BeEquivalentTo(
                [SecurityAssetClassNames.OtherSecurity],
                "a class with no family reports as 'partial' in certified reporting; only OtherSecurity, "
                + "whose family is caller-supplied, may leave it unset");
    }

    [Fact]
    public void DerivativeFlag_MatchesTheDerivativeAssetClass()
    {
        foreach (var name in RegistryClasses)
        {
            var descriptor = Descriptor(name);
            descriptor.IsDerivative.Should().Be(descriptor.AssetClass.IsDerivative,
                $"'{name}' must flag IsDerivative exactly when its asset class is Derivative");
        }
    }

    [Fact]
    public void DerivativeFamilies_MatchTheDerivativeAssetClass()
    {
        foreach (var name in RegistryClasses)
        {
            var descriptor = Descriptor(name);
            var family = descriptor.Family?.Value;
            var isDerivativeFamily = family is not null && (family.IsListedDerivative || family.IsOtcDerivative);

            isDerivativeFamily.Should().Be(descriptor.IsDerivative,
                $"'{name}' must use a ListedDerivative or OtcDerivative family exactly when it is a derivative");
        }
    }

    [Fact]
    public void NotionalLotClasses_AreOtcDerivatives()
    {
        foreach (var name in SecurityAssetClassRegistry.AssetClassesWithLotUnit(SecurityLotUnitKind.Notional))
        {
            Descriptor(name).Family?.Value.IsOtcDerivative.Should().BeTrue(
                $"'{name}' is booked by notional, which only bilateral (OTC) contracts carry");
        }
    }

    [Fact]
    public void ListedDerivativeClasses_AreBookedInContracts()
    {
        foreach (var name in RegistryClasses.Where(static name => Descriptor(name).Family?.Value.IsListedDerivative == true))
        {
            SecurityAssetClassCatalog.GetOrDefault(name).LotUnit.Should().Be(SecurityLotUnitKind.Contracts,
                $"'{name}' is exchange-traded, so its lots count contracts scaled by a multiplier");
        }
    }

    private static AssetClassDescriptor Descriptor(string name)
        => AssetClassRegistry.tryDescriptorByName(name)!.Value;

    private static void AssertAgrees(
        InstrumentTypeDescriptor descriptor,
        InstrumentTypeDescriptorLabelField field,
        string? label,
        string? expected)
    {
        if (IsListed(InstrumentTypeDescriptorCatalog.DomainLabelExceptions, descriptor, field, label)
            || IsListed(InstrumentTypeDescriptorCatalog.ApproximateDomainLabelMappings, descriptor, field, label))
        {
            return;
        }

        label.Should().Be(expected,
            $"{descriptor.InstrumentType} {field} must equal the F# registry's label for "
            + $"'{descriptor.SecurityMasterAssetClass}', or be a listed exception or approximation");
    }

    private static bool IsListed(
        IEnumerable<InstrumentTypeDescriptorLabelException> list,
        InstrumentTypeDescriptor descriptor,
        InstrumentTypeDescriptorLabelField field,
        string? label)
        => list.Any(entry => entry.InstrumentType == descriptor.InstrumentType
                             && entry.Field == field
                             && entry.Label == label);
}
