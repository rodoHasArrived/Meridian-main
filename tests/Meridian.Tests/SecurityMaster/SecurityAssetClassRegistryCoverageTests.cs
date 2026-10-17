using FluentAssertions;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.Storage.SecurityMaster;
using Microsoft.FSharp.Reflection;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Locks the single asset-class registry to every surface that dispatches on asset class. Adding a
/// case to the F# <c>SecurityKind</c> union fails here until the registry, the term schema, the F#
/// classification table, the validator registry and the storage writers all know it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetClassRegistryCoverageTests
{
    private static readonly string[] SecurityKindCases =
        FSharpType.GetUnionCases(typeof(SecurityKind), null).Select(static unionCase => unionCase.Name).ToArray();

    [Fact]
    public void Registry_CoversEveryCaseOfTheFSharpSecurityKindUnion()
    {
        SecurityAssetClassRegistry.All.Select(static definition => definition.AssetClass)
            .Should().BeEquivalentTo(SecurityKindCases,
                "every SecurityKind case needs exactly one registry definition, and the registry must not define a class the domain cannot represent");
    }

    [Fact]
    public void FSharpClassificationTable_CoversExactlyTheRegistry()
    {
        AssetClassRegistry.assetClasses.ToArray().Should().BeEquivalentTo(
            SecurityAssetClassRegistry.All.Select(static definition => definition.AssetClass));
    }

    [Fact]
    public void TermSchema_CoversExactlyTheRegistry()
    {
        SecurityAssetTermsSchema.AssetClasses.Should().BeEquivalentTo(
            SecurityAssetClassRegistry.All.Select(static definition => definition.AssetClass));
    }

    [Fact]
    public void ValidatorRegistry_CoversExactlyTheRegistry()
    {
        AssetClassValidatorRegistry.CreateDefault().SupportedAssetClasses.Should().BeEquivalentTo(
            SecurityAssetClassRegistry.All.Select(static definition => definition.AssetClass));
    }

    [Fact]
    public void DeclarativeProjectionKind_MatchesTheDeclarativeProjectionRegistry()
    {
        SecurityAssetClassRegistry.AssetClassesWithProjection(SecurityTermsProjectionKind.Declarative)
            .Should().BeEquivalentTo(SecurityTermsProjectionRegistry.Descriptors.Select(static descriptor => descriptor.AssetClass));
    }

    [Fact]
    public void SpecializedProjectionKind_MatchesTheSpecializedWriters()
    {
        SecurityAssetClassRegistry.AssetClassesWithProjection(SecurityTermsProjectionKind.Specialized)
            .Should().BeEquivalentTo(PostgresSecurityMasterStore.SpecializedProjectionAssetClasses);
    }

    [Fact]
    public void TermsJsonOnlyClasses_HaveNoProjectionWriter()
    {
        SecurityAssetClassRegistry.AssetClassesWithProjection(SecurityTermsProjectionKind.TermsJsonOnly)
            .Should().NotIntersectWith(PostgresSecurityMasterStore.ProjectedAssetClasses);
    }

    [Fact]
    public void DerivedFlags_ReproduceTheFaceValueAndCalculatedScheduleClasses()
    {
        // These were independently maintained booleans; they are now derived from the lot unit and
        // cash-flow behaviour. The sets are pinned so a re-declaration cannot silently change them.
        SecurityAssetClassCatalog.All.Where(static descriptor => descriptor.UsesFaceValueLots)
            .Select(static descriptor => descriptor.AssetClass)
            .Should().BeEquivalentTo("Bond", "Deposit", "CertificateOfDeposit", "CommercialPaper", "TreasuryBill", "Repo", "DirectLoan", "StructuredCredit");
        SecurityAssetClassCatalog.All.Where(static descriptor => descriptor.SupportsCashflowScheduleByDefault)
            .Select(static descriptor => descriptor.AssetClass)
            .Should().BeEquivalentTo("Bond", "Deposit", "CertificateOfDeposit", "CommercialPaper", "TreasuryBill", "Repo", "Swap", "DirectLoan", "StructuredCredit");
    }

    [Fact]
    public void LotMultiplierTermKey_NamesADeclaredDecimalTermOfItsClass()
    {
        foreach (var definition in SecurityAssetClassRegistry.All.Where(static definition => definition.Descriptor.LotMultiplierTermKey is not null))
        {
            var key = definition.Descriptor.LotMultiplierTermKey!;
            definition.Fields.Should().ContainSingle(field => field.Key == key && field.Type == SecurityAssetTermFieldType.Decimal,
                $"{definition.AssetClass}'s lot multiplier must be read from a declared decimal term");
            definition.LotUnit.Should().BeOneOf(SecurityLotUnitKind.Contracts, SecurityLotUnitKind.Units);
        }
    }

    [Fact]
    public void ContractLots_DeclareAMultiplierTerm()
    {
        SecurityAssetClassRegistry.All
            .Where(static definition => definition.LotUnit == SecurityLotUnitKind.Contracts)
            .Should().OnlyContain(static definition => definition.Descriptor.LotMultiplierTermKey != null);
    }

    [Fact]
    public void UiHints_CoverEveryDeclaredField_InDeclarationOrder()
    {
        foreach (var definition in SecurityAssetClassRegistry.All)
        {
            definition.UiHints.Select(static hint => hint.Key)
                .Should().Equal(definition.Fields.Select(static field => field.Key), $"{definition.AssetClass} hints follow the schema");
            foreach (var (field, hint) in definition.Fields.Zip(definition.UiHints))
            {
                hint.FieldPath.Should().Be($"assetSpecificTerms.{field.Key}");
                hint.Required.Should().Be(field.Required);
                (hint.Editor == SecurityAssetTermEditorKind.Choice).Should().Be(field.AllowedValues.Count > 0);
                (hint.Editor == SecurityAssetTermEditorKind.Convention)
                    .Should().Be(field.AllowedValues.Count == 0 && field.Convention != SecurityTermConventionKind.None);
                hint.ScalarEditable.Should().Be(field.Type is not (SecurityAssetTermFieldType.Array or SecurityAssetTermFieldType.Object));
            }
        }
    }

    [Fact]
    public void ScalarHintPaths_AreAcceptedByTheFieldEditValidator()
    {
        // A form built from the hints must produce paths the governed edit route accepts.
        foreach (var definition in SecurityAssetClassRegistry.All.Where(static definition => definition.AssetClass != "CustomAsset"))
        {
            foreach (var hint in definition.UiHints.Where(static hint => hint.ScalarEditable))
            {
                SecurityAssetTermsFieldEditValidator.TryValidate(definition.AssetClass, hint.FieldPath, newValue: null, out var canonical, out var error)
                    .Should().BeTrue($"{definition.AssetClass} {hint.FieldPath}: {error}");
                canonical.Should().Be(hint.FieldPath);
            }
        }
    }

    [Theory]
    [InlineData("couponRate", "Coupon rate")]
    [InlineData("isCallable", "Is callable")]
    [InlineData("maturity", "Maturity")]
    [InlineData("weightedAverageMaturityDays", "Weighted average maturity days")]
    public void Humanize_ProducesReadableLabels(string key, string expected)
        => SecurityAssetClassRegistry.Humanize(key).Should().Be(expected);

    [Fact]
    public void Dto_CarriesEveryFieldAndBehaviour()
    {
        var bond = SecurityAssetClassRegistry.ToDto(SecurityAssetClassRegistry.Get("Bond"));

        bond.LotUnit.Should().Be(SecurityLotUnitKind.FaceValue);
        bond.CashFlowBehavior.Should().Be(SecurityCashFlowBehavior.ContractualSchedule);
        bond.ProjectionKind.Should().Be(SecurityTermsProjectionKind.Specialized);
        bond.SupportsSchemaDrivenCreate.Should().BeTrue();
        bond.Fields.Select(static field => field.Key).Should().Equal(SecurityAssetTermsSchema.Fields("Bond").Select(static field => field.Key));
        SecurityAssetClassRegistry.Get("MBS").AssetClass.Should().Be("StructuredCredit", "aliases resolve through the catalog");
    }
}
