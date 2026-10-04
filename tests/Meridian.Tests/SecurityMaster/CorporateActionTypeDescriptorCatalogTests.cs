using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Locks the canonical corporate-action taxonomy: every descriptor round-trips through the
/// catalog, provider aliases and CAEV codes resolve to canonical names, alias tokens stay
/// unambiguous, and asset-class validity lists stay aligned with the asset-class validator
/// registry's vocabulary.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CorporateActionTypeDescriptorCatalogTests
{
    [Fact]
    public void All_ContainsTwentyNineDescriptors_WithUniqueCanonicalNames()
    {
        CorporateActionTypeDescriptorCatalog.All.Should().HaveCount(29);
        CorporateActionTypeDescriptorCatalog.All
            .Select(static descriptor => descriptor.CanonicalName)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void All_EveryDescriptor_RoundTripsThroughFindAndTryNormalize()
    {
        foreach (var descriptor in CorporateActionTypeDescriptorCatalog.All)
        {
            CorporateActionTypeDescriptorCatalog.Find(descriptor.CanonicalName)
                .Should().BeSameAs(descriptor);
            CorporateActionTypeDescriptorCatalog.TryGet(descriptor.CanonicalName, out var byGet)
                .Should().BeTrue();
            byGet.Should().BeSameAs(descriptor);
            CorporateActionTypeDescriptorCatalog.TryNormalize(descriptor.CanonicalName, out var byToken)
                .Should().BeTrue();
            byToken.Should().BeSameAs(descriptor);
        }
    }

    [Theory]
    [InlineData("Split", CorporateActionEventTypes.StockSplit)]
    [InlineData("SPLIT", CorporateActionEventTypes.StockSplit)]
    [InlineData("SPLF", CorporateActionEventTypes.StockSplit)]
    [InlineData("reverse split", CorporateActionEventTypes.ReverseStockSplit)]
    [InlineData("SPLR", CorporateActionEventTypes.ReverseStockSplit)]
    [InlineData("Merger", CorporateActionEventTypes.MergerAbsorption)]
    [InlineData("Cash Dividend", CorporateActionEventTypes.Dividend)]
    [InlineData("DVCA", CorporateActionEventTypes.Dividend)]
    [InlineData("FactorReduction", CorporateActionEventTypes.PrincipalPaydown)]
    [InlineData("PRED", CorporateActionEventTypes.PrincipalPaydown)]
    [InlineData("RHTS", CorporateActionEventTypes.RightsIssue)]
    [InlineData("RHDI", CorporateActionEventTypes.RightsIssue)]
    [InlineData("MCAL", CorporateActionEventTypes.BondCall)]
    [InlineData("REDM", CorporateActionEventTypes.BondMaturityRedemption)]
    [InlineData("Redemption", CorporateActionEventTypes.BondMaturityRedemption)]
    [InlineData("DLST", CorporateActionEventTypes.Delisting)]
    [InlineData("TEND", CorporateActionEventTypes.TenderOffer)]
    [InlineData("CAPD", CorporateActionEventTypes.ReturnOfCapital)]
    [InlineData("Fork", CorporateActionEventTypes.CryptoFork)]
    public void TryNormalize_AliasesAndCaevCodes_ResolveToCanonicalName(string raw, string expectedCanonical)
    {
        CorporateActionTypeDescriptorCatalog.TryNormalize(raw, out var descriptor).Should().BeTrue();
        descriptor.CanonicalName.Should().Be(expectedCanonical);
    }

    [Theory]
    [InlineData("Divvy")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("CHAN")] // Ambiguous between SymbolChange and NameChange — deliberately not an alias.
    public void TryNormalize_UnknownOrAmbiguousTokens_ReturnFalse(string? raw)
    {
        CorporateActionTypeDescriptorCatalog.TryNormalize(raw, out _).Should().BeFalse();
    }

    [Fact]
    public void EventTypes_NormalizeAndIsKnown_DelegateToCatalog()
    {
        CorporateActionEventTypes.Normalize("Split").Should().Be(CorporateActionEventTypes.StockSplit);
        CorporateActionEventTypes.Normalize(" Divvy ").Should().Be("Divvy");
        CorporateActionEventTypes.IsKnown("SPLF").Should().BeTrue();
        CorporateActionEventTypes.IsKnown("Divvy").Should().BeFalse();
    }

    [Fact]
    public void All_AliasTokens_AreUnambiguousAcrossTheCatalog()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var descriptor in CorporateActionTypeDescriptorCatalog.All)
        {
            foreach (var value in descriptor.Aliases.Append(descriptor.CanonicalName))
            {
                var token = new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
                if (seen.TryGetValue(token, out var owner))
                {
                    owner.Should().Be(
                        descriptor.CanonicalName,
                        $"alias token '{token}' must resolve to exactly one canonical event type");
                }
                else
                {
                    seen[token] = descriptor.CanonicalName;
                }
            }
        }
    }

    [Fact]
    public void All_ValidAssetClasses_ExistInDefaultAssetClassValidatorRegistry()
    {
        var supported = AssetClassValidatorRegistry.CreateDefault().SupportedAssetClasses
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in CorporateActionTypeDescriptorCatalog.All)
        {
            foreach (var assetClass in descriptor.ValidSecurityMasterAssetClasses)
            {
                supported.Should().Contain(
                    assetClass,
                    $"descriptor '{descriptor.CanonicalName}' must only reference asset classes the validator registry knows");
            }
        }
    }

    [Fact]
    public void FindByAssetClass_IncludesUnrestrictedAndClassSpecificTypes()
    {
        var equityTypes = CorporateActionTypeDescriptorCatalog.FindByAssetClass("Equity")
            .Select(static descriptor => descriptor.CanonicalName)
            .ToArray();

        equityTypes.Should().Contain(CorporateActionEventTypes.Dividend);
        equityTypes.Should().Contain(CorporateActionEventTypes.StockSplit);
        // Empty validity list means valid for every asset class.
        equityTypes.Should().Contain(CorporateActionEventTypes.SymbolChange);
        equityTypes.Should().NotContain(CorporateActionEventTypes.BondCall);

        CorporateActionTypeDescriptorCatalog.FindByAssetClass(null).Should().BeEmpty();
    }

    [Fact]
    public void All_RedemptionKinds_RequireRedemptionPriceAndPostRedemptionMemo()
    {
        foreach (var canonical in new[]
                 {
                     CorporateActionEventTypes.BondCall,
                     CorporateActionEventTypes.BondMaturityRedemption
                 })
        {
            CorporateActionTypeDescriptorCatalog.TryGet(canonical, out var descriptor).Should().BeTrue();
            descriptor.RequiredFields.Should().Contain(CorporateActionField.RedemptionPricePercentOfPar);
            descriptor.LedgerPostingKind.Should().Be(CorporateActionLedgerPostingKind.RedemptionMemo);
        }
    }
}
