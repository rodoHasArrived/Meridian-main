using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Reconciles <see cref="InstrumentTypeDescriptorCatalog"/>'s family/sub-type labels with the F#
/// domain's <c>familyName</c>/<c>subTypeName</c> labels (Security Master review slice 3, D6). A
/// descriptor label must either be a domain label or appear in the explicit exceptions list.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InstrumentTypeDescriptorDomainLabelTests
{
    private static readonly HashSet<string> DomainFamilyLabels = ClassificationLabelReaderTests
        .NullaryCases<AssetFamily>()
        .Select(SecurityClassificationModule.familyName)
        .ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> DomainSubTypeLabels = ClassificationLabelReaderTests
        .NullaryCases<SecuritySubType>()
        .Select(SecurityClassificationModule.subTypeName)
        .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryDescriptorLabel_IsADomainLabelOrAListedException()
    {
        foreach (var descriptor in InstrumentTypeDescriptorCatalog.All)
        {
            AssertCovered(descriptor, InstrumentTypeDescriptorLabelField.AssetFamily, descriptor.SecurityMasterAssetFamily, DomainFamilyLabels);
            AssertCovered(descriptor, InstrumentTypeDescriptorLabelField.SubType, descriptor.SecurityMasterSubType, DomainSubTypeLabels);
        }
    }

    [Fact]
    public void EveryException_NamesAnExistingDescriptorLabelThatIsNotADomainLabel()
    {
        InstrumentTypeDescriptorCatalog.DomainLabelExceptions
            .Select(static exception => (exception.InstrumentType, exception.Field))
            .Should().OnlyHaveUniqueItems();

        foreach (var exception in InstrumentTypeDescriptorCatalog.DomainLabelExceptions)
        {
            var descriptor = InstrumentTypeDescriptorCatalog.Find(exception.InstrumentType);
            descriptor.Should().NotBeNull($"exception {exception.InstrumentType}/{exception.Field} must name a descriptor");
            LabelOf(descriptor!, exception.Field).Should().Be(exception.Label, "a stale exception must be removed");
            DomainLabelsFor(exception.Field).Should().NotContain(exception.Label, "a domain label needs no exception");
            exception.Reason.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void EveryApproximateMapping_NamesAnExistingDescriptorDomainLabel()
    {
        foreach (var mapping in InstrumentTypeDescriptorCatalog.ApproximateDomainLabelMappings)
        {
            var descriptor = InstrumentTypeDescriptorCatalog.Find(mapping.InstrumentType);
            descriptor.Should().NotBeNull();
            LabelOf(descriptor!, mapping.Field).Should().Be(mapping.Label);
            DomainLabelsFor(mapping.Field).Should().Contain(mapping.Label);
            mapping.Reason.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void DomainLabelMapping_IsIdentityForDomainLabels_AndNullForPlaceholders()
    {
        foreach (var descriptor in InstrumentTypeDescriptorCatalog.All)
        {
            var family = InstrumentTypeDescriptorCatalog.ResolveDomainAssetFamilyLabel(descriptor);
            var subType = InstrumentTypeDescriptorCatalog.ResolveDomainSubTypeLabel(descriptor);

            if (family is not null)
            {
                family.Should().Be(descriptor.SecurityMasterAssetFamily);
                DomainFamilyLabels.Should().Contain(family);
            }

            if (subType is not null)
            {
                subType.Should().Be(descriptor.SecurityMasterSubType);
                DomainSubTypeLabels.Should().Contain(subType);
            }
        }

        var commodity = InstrumentTypeDescriptorCatalog.Find(Meridian.Contracts.Domain.Enums.InstrumentType.Commodity)!;
        InstrumentTypeDescriptorCatalog.ResolveDomainAssetFamilyLabel(commodity).Should().BeNull();
        commodity.SecurityMasterAssetFamily.Should().Be(
            InstrumentTypeDescriptorCatalog.UnclassifiedAssetFamilyLabel,
            "the reconciliation is behavior-preserving: descriptor values are unchanged");
    }

    [Fact]
    public void DescriptorPlaceholders_AreNotDecodedByTheLabelReader()
    {
        ClassificationLabelReader.Normalize(InstrumentTypeDescriptorCatalog.UnclassifiedAssetFamilyLabel)
            .Should().Be(InstrumentTypeDescriptorCatalog.UnclassifiedAssetFamilyLabel);
        ClassificationLabelReader.Normalize(InstrumentTypeDescriptorCatalog.UnclassifiedSubTypeLabel)
            .Should().Be(InstrumentTypeDescriptorCatalog.UnclassifiedSubTypeLabel);
    }

    private static void AssertCovered(
        InstrumentTypeDescriptor descriptor,
        InstrumentTypeDescriptorLabelField field,
        string? label,
        IReadOnlySet<string> domainLabels)
    {
        if (label is null || domainLabels.Contains(label))
        {
            return;
        }

        InstrumentTypeDescriptorCatalog.DomainLabelExceptions.Should().Contain(
            exception => exception.InstrumentType == descriptor.InstrumentType
                         && exception.Field == field
                         && exception.Label == label,
            $"{descriptor.InstrumentType} {field} label '{label}' is neither an F# domain label nor a listed exception");
    }

    private static string? LabelOf(InstrumentTypeDescriptor descriptor, InstrumentTypeDescriptorLabelField field)
        => field == InstrumentTypeDescriptorLabelField.AssetFamily
            ? descriptor.SecurityMasterAssetFamily
            : descriptor.SecurityMasterSubType;

    private static HashSet<string> DomainLabelsFor(InstrumentTypeDescriptorLabelField field)
        => field == InstrumentTypeDescriptorLabelField.AssetFamily ? DomainFamilyLabels : DomainSubTypeLabels;
}
