using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Xunit;
using Category = Meridian.Contracts.SecurityMaster.SecurityMasterConflictFieldCategory;

namespace Meridian.Contracts.UnitTests.SecurityMaster;

public sealed class SecurityMasterConflictFieldCategoriesTests
{
    // Every path SecurityMasterConflictDetection writes today. The expected categories match what
    // the WPF substring rule produced for these paths, so routing for emitted data is unchanged.
    [Theory]
    [InlineData("Identifiers.Isin", SecurityMasterConflictKinds.IdentifierAmbiguity, Category.Identifier)]
    [InlineData("Identifiers.Ric", SecurityMasterConflictKinds.IdentifierAmbiguity, Category.Identifier)]
    [InlineData("Identifiers.Primary", SecurityMasterConflictKinds.IdentifierAmbiguity, Category.Identifier)]
    [InlineData("EconomicTerms.maturityDate", SecurityMasterConflictKinds.EconomicTermMismatch, Category.None)]
    [InlineData("EconomicTerms.issueDate", SecurityMasterConflictKinds.EconomicTermMismatch, Category.None)]
    [InlineData("EconomicTerms.couponRate", SecurityMasterConflictKinds.EconomicTermMismatch, Category.None)]
    [InlineData("EconomicTerms.principalFace", SecurityMasterConflictKinds.EconomicTermMismatch, Category.None)]
    [InlineData("EconomicTerms.paymentFrequency", SecurityMasterConflictKinds.EconomicTermMismatch, Category.None)]
    [InlineData("EconomicTerms.dayCountConvention", SecurityMasterConflictKinds.EconomicTermMismatch, Category.None)]
    [InlineData("EconomicTerms.principalSchedule", SecurityMasterConflictKinds.EconomicTermMismatch, Category.None)]
    [InlineData("CommonTerms.currency", SecurityMasterConflictKinds.CommonTermMismatch, Category.Currency | Category.Classification)]
    [InlineData("CommonTerms.countryOfRisk", SecurityMasterConflictKinds.CommonTermMismatch, Category.None)]
    public void EmittedFieldPaths_KeepTheirRouting(string fieldPath, string kind, Category expected)
        => SecurityMasterConflictFieldCategories.For(fieldPath, kind).Should().Be(expected);

    // Seeded profile fields: the three the old rule matched by a real meaning keep it, and
    // transferRestrictions — matched only because it contains "ric" — is no longer an identifier.
    [Theory]
    [InlineData("ProfileFields.collateralType", Category.Classification)]
    [InlineData("ProfileFields.propertyType", Category.Classification)]
    [InlineData("ProfileFields.issuer", Category.Classification)]
    [InlineData("ProfileFields.transferRestrictions", Category.None)]
    [InlineData("ProfileFields.strategy", Category.None)]
    [InlineData("ProfileFields.economics", Category.None)]
    public void ProfileFields_UseDeclaredNamesOnly(string fieldPath, Category expected)
        => SecurityMasterConflictFieldCategories.For(fieldPath, SecurityMasterConflictKinds.EconomicTermMismatch)
            .Should().Be(expected);

    [Theory]
    [InlineData("ProfileFields.slotCount")]
    [InlineData("ProfileFields.allotment")]
    [InlineData("ProfileFields.pilotProgram")]
    [InlineData("ProfileFields.metric")]
    [InlineData("ProfileFields.ratingType")]
    [InlineData("ProfileFields.nickname")]
    public void SubstringLookalikes_HaveNoCategory(string fieldPath)
        => SecurityMasterConflictFieldCategories.For(fieldPath, SecurityMasterConflictKinds.EconomicTermMismatch)
            .Should().Be(Category.None);

    [Theory]
    [InlineData("TradingParameters.lotSize")]
    [InlineData("TradingParameters.tickSize")]
    [InlineData("TradingParameters.contractMultiplier")]
    [InlineData("TradingParameters.marginRequirementPct")]
    [InlineData("TradingParameters.tradingHoursUtc")]
    [InlineData("ProfileFields.contract_multiplier")]
    public void TradingParameterFields_AreTradingParameters(string fieldPath)
        => SecurityMasterConflictFieldCategories.For(fieldPath, conflictKind: null)
            .Should().HaveFlag(Category.TradingParameter);

    [Fact]
    public void EveryIdentifierKind_IsAnIdentifierSegment()
    {
        foreach (var kind in Enum.GetValues<SecurityIdentifierKind>())
        {
            SecurityMasterConflictFieldCategories.For($"ProfileFields.{kind}", conflictKind: null)
                .Should().HaveFlag(Category.Identifier, $"{kind} is a declared identifier kind");
        }
    }

    [Fact]
    public void IdentifierAmbiguity_IsAnIdentifierWhateverThePath()
        => SecurityMasterConflictFieldCategories.For("Unrecognised", SecurityMasterConflictKinds.IdentifierAmbiguity)
            .Should().Be(Category.Identifier);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankPath_HasNoCategory(string? fieldPath)
        => SecurityMasterConflictFieldCategories.For(fieldPath, SecurityMasterConflictKinds.CommonTermMismatch)
            .Should().Be(Category.None);
}
