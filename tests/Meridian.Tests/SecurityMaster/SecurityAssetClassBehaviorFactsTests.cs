using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The behaviour facts that replaced per-site asset-class name checks are declared on exactly the
/// classes that carried those checks, so moving a site onto the fact preserves which classes it
/// applied to.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetClassBehaviorFactsTests
{
    [Fact]
    public void RequiresProfileReference_IsCustomAssetOnly()
        => Classes(static d => d.RequiresProfileReference).Should().Equal("CustomAsset");

    [Fact]
    public void RequiresGovernedProfile_IsTheTwoProfileGovernedClasses()
        => Classes(static d => d.RequiresGovernedProfile).Should().BeEquivalentTo(["CustomAsset", "OtherSecurity"]);

    [Fact]
    public void SupportsEquityClassification_IsEquityOnly()
        => Classes(static d => d.SupportsEquityClassification).Should().Equal("Equity");

    [Fact]
    public void CarriesBondCouponStructure_IsBondOnly()
        => Classes(static d => d.CarriesBondCouponStructure).Should().Equal("Bond");

    [Fact]
    public void UsesOccOptionSymbols_IsOptionAndWarrant()
        => Classes(static d => d.UsesOccOptionSymbols).Should().BeEquivalentTo(["Option", "Warrant"]);

    [Fact]
    public void QuotesRatesAsFractions_IsDepositOnly()
        => Classes(static d => d.QuotesRatesAsFractions).Should().Equal("Deposit");

    [Fact]
    public void SingleClassBehaviours_ResolveTheClassTheSitesNamed()
    {
        Classes(static d => d.CashFlowBehavior == SecurityCashFlowBehavior.SwapLegs).Should().Equal("Swap");
        Classes(static d => d.CashFlowBehavior == SecurityCashFlowBehavior.FinancingInterest).Should().Equal("Repo");
        Classes(static d => d.ProjectionKind == SecurityTermsProjectionKind.Specialized).Should().Equal("Option");
        Classes(static d => d is { LotUnit: SecurityLotUnitKind.Shares, CashFlowBehavior: SecurityCashFlowBehavior.Dividends }).Should().Equal("Equity");
    }

    [Fact]
    public void SchemaDrivenCreate_FollowsTheProfileReferenceFact()
    {
        foreach (var definition in SecurityAssetClassRegistry.All)
        {
            SecurityAssetClassRegistry.SupportsSchemaDrivenCreate(definition)
                .Should().Be(!definition.Descriptor.RequiresProfileReference, definition.AssetClass);
        }
    }

    [Fact]
    public void UnknownClass_DeclaresNoBehaviourFact()
        => SecurityAssetClassCatalog.GetOrDefault("NotAClass").HasDeclaredBehaviorFacts.Should().BeFalse();

    private static string[] Classes(Func<SecurityAssetClassDescriptor, bool> fact)
        => SecurityAssetClassCatalog.All.Where(fact).Select(static d => d.AssetClass).ToArray();
}
