using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Guards the single declarative asset-specific-terms field/type table (<see cref="SecurityAssetTermsSchema"/>)
/// against drift: it must stay in lock-step with the authoritative asset-class catalog, declare each
/// class's fields without duplication, and remain the shared reference the projection and validation
/// codecs are measured against. Also binds the store's projection coverage to the catalog so the
/// catalog-vs-projection gap stays explicit and enforced rather than accidental.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetTermsSchemaTests
{
    // The asset classes the platform deliberately does not give a dedicated relational projection.
    // Adding a catalog class without a projection forces this list to be updated in review, so the
    // 26-vs-11 catalog-vs-projection gap is a conscious decision instead of silent drift.
    private static readonly string[] IntentionallyUnprojectedAssetClasses =
    [
        "CommercialPaper",
        "TreasuryBill",
        "Repo",
        "CashSweep",
        "OtherSecurity",
        "CustomAsset",
        "DirectLoan",
        "StructuredCredit",
        "PrivateFundInterest",
        "PrivateCompanyEquity",
        "RealEstateHolding",
        "CommitmentGuarantee",
        "Cfd",
        "Warrant",
        "InvestmentFund"
    ];

    [Fact]
    public void Schema_DeclaresEveryCatalogAssetClass()
    {
        SecurityAssetTermsSchema.AssetClasses
            .Should().BeEquivalentTo(
                SecurityAssetClassCatalog.AssetClasses,
                "the terms schema is the single source of truth for every catalog asset class");
    }

    [Fact]
    public void Schema_HasNoDuplicateFieldKeysWithinAnyAssetClass()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            var fields = SecurityAssetTermsSchema.Fields(assetClass);
            var distinctKeys = fields
                .Select(static field => field.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            distinctKeys.Should().Be(fields.Count, $"'{assetClass}' must not repeat a field key");
        }
    }

    [Fact]
    public void Schema_EveryFieldHasKeyAndNonNullAliases()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
            {
                field.Key.Should().NotBeNullOrWhiteSpace();
                field.Aliases.Should().NotBeNull();
            }
        }
    }

    [Fact]
    public void Schema_ExposesTheFlatCouponContractTheProjectionReads()
    {
        // Regression anchor for the bond silent-data-loss fix: the coupon is a flat contract, not a
        // nested "coupon" object. If these move, the projection decoder and serializer must move with them.
        SecurityAssetTermsSchema.Field("Bond", "couponType").Should().NotBeNull();
        SecurityAssetTermsSchema.Field("Bond", "couponRate")!.Type.Should().Be(SecurityAssetTermFieldType.Decimal);
        SecurityAssetTermsSchema.Field("Bond", "dayCount").Should().NotBeNull();
        SecurityAssetTermsSchema.Field("Bond", "coupon").Should().BeNull("the coupon is serialized flat, not nested");
    }

    [Fact]
    public void Schema_SwapExposesLegsNotSwapType()
    {
        SecurityAssetTermsSchema.Field("Swap", "legs")!.Type.Should().Be(SecurityAssetTermFieldType.Array);
        SecurityAssetTermsSchema.Field("Swap", "swapType").Should().BeNull("swap economics are carried as legs");
    }

    [Fact]
    public void ProjectionCoverage_IsASubsetOfTheCatalogWithASchemaEntry()
    {
        foreach (var assetClass in PostgresSecurityMasterStore.ProjectedAssetClasses)
        {
            SecurityAssetClassCatalog.AssetClasses.Should().Contain(assetClass,
                $"projected class '{assetClass}' must be a real catalog asset class");
            SecurityAssetTermsSchema.TryGetFields(assetClass, out _).Should().BeTrue(
                $"projected class '{assetClass}' must have a declared terms schema");
        }
    }

    [Fact]
    public void ProjectionCoverage_PartitionsTheCatalogIntoProjectedAndDeclaredGaps()
    {
        var projected = PostgresSecurityMasterStore.ProjectedAssetClasses;

        projected.Should().NotIntersectWith(IntentionallyUnprojectedAssetClasses,
            "a class is either projected or a declared gap, never both");

        projected
            .Concat(IntentionallyUnprojectedAssetClasses)
            .Should().BeEquivalentTo(SecurityAssetClassCatalog.AssetClasses,
                "every catalog class must be either projected or an explicitly declared projection gap");
    }
}
