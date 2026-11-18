using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.Accounting;

/// <summary>
/// The posting-boundary rule for a lot's quantity basis: only face-value classes may be booked by
/// face. A face-value class may still be booked by units (a posting without face-value terms).
/// </summary>
[Trait("Category", "Unit")]
public sealed class LotQuantityBasisRulesTests
{
    public static TheoryData<string> CatalogClasses()
    {
        var data = new TheoryData<string>();
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
            data.Add(assetClass);
        return data;
    }

    [Theory]
    [MemberData(nameof(CatalogClasses))]
    public void FaceBasis_IsPermittedExactlyForFaceValueClasses(string assetClass)
    {
        var isFaceValueClass = SecurityAssetClassCatalog.GetOrDefault(assetClass).LotUnit == SecurityLotUnitKind.FaceValue;

        LotQuantityBasisRules.Permits(assetClass, LotQuantityBasis.Face).Should().Be(isFaceValueClass,
            $"'{assetClass}' lots are counted in {SecurityAssetClassCatalog.GetOrDefault(assetClass).LotUnit}");
    }

    [Theory]
    [MemberData(nameof(CatalogClasses))]
    public void UnitsBasis_IsPermittedForEveryClass(string assetClass)
    {
        LotQuantityBasisRules.Permits(assetClass, LotQuantityBasis.Units).Should().BeTrue();
    }

    [Theory]
    [InlineData("Bond", true)]
    [InlineData("TreasuryBill", true)]
    [InlineData("StructuredCredit", true)]
    [InlineData("Equity", false)]
    [InlineData("Option", false)]
    [InlineData("Swap", false)]
    [InlineData("MoneyMarketFund", false)]
    public void FaceBasis_FollowsTheLotUnit_ForRepresentativeClasses(string assetClass, bool permitted)
    {
        LotQuantityBasisRules.Permits(assetClass, LotQuantityBasis.Face).Should().Be(permitted);
    }

    [Fact]
    public void UnknownOrMissingClass_IsNotBookedByFace()
    {
        LotQuantityBasisRules.Permits("TokenizedCarbonCredit", LotQuantityBasis.Face).Should().BeFalse();
        LotQuantityBasisRules.Permits(null, LotQuantityBasis.Face).Should().BeFalse();
        LotQuantityBasisRules.Permits(null, LotQuantityBasis.Units).Should().BeTrue();
    }
}
