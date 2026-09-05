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
/// catalog-vs-projection gap stays explicit and enforced rather than accidental, and holds the
/// Asset Operations classes to a stricter rule: a class whose declared capabilities include
/// LedgerProjection cannot be written off as an intentional gap, only tracked on a shrinking
/// backlog.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetTermsSchemaTests
{
    // The asset classes the platform deliberately does not give a dedicated relational projection.
    // Adding a catalog class without a projection forces this list to be updated in review, so the
    // catalog-vs-projection gap is a conscious decision instead of silent drift.
    private static readonly string[] IntentionallyUnprojectedAssetClasses =
    [
        "CommercialPaper",
        "TreasuryBill",
        "Repo",
        "CashSweep",
        "OtherSecurity",
        "CustomAsset",
        "Cfd",
        "Warrant",
        "InvestmentFund"
    ];

    // The Asset Operations classes still waiting for a relational terms projection. These are NOT
    // intentional gaps: an ops-capable class declares ProjectedCashFlows, Reconciliation and
    // LedgerProjection, so its economic terms drive money movement and leaving them reachable only
    // by parsing the JSONB blob one security at a time is a coverage gap. They sit in their own list
    // because the guards below hold the backlog to a different standard than a declared gap — it may
    // only ever shrink, and each entry costs a SecurityTermsProjectionRegistry descriptor plus its
    // DDL to clear.
    private static readonly string[] OpsCapableProjectionBacklog =
    [
        "PrivateFundInterest",
        "PrivateCompanyEquity",
        "RealEstateHolding",
        "CommitmentGuarantee"
    ];

    // The ops-capable classes that were unprojected when the backlog guard was written. The backlog
    // may only ever be a SUBSET of this: an entry can leave it by gaining a projection, and nothing
    // can join it. Without this frozen ceiling the "shrink-only" guards below are satisfied by
    // deleting a descriptor and adding the class back to the backlog, which is the regression the
    // ratchet exists to prevent.
    private static readonly string[] OpsCapableProjectionBacklogCeiling =
    [
        "PrivateFundInterest",
        "PrivateCompanyEquity",
        "RealEstateHolding",
        "CommitmentGuarantee"
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

        projected.Should().NotIntersectWith(OpsCapableProjectionBacklog,
            "a class is either projected or on the backlog, never both");

        IntentionallyUnprojectedAssetClasses.Should().NotIntersectWith(OpsCapableProjectionBacklog,
            "a missing projection is either a decision or a backlog item, and the two carry different obligations");

        projected
            .Concat(IntentionallyUnprojectedAssetClasses)
            .Concat(OpsCapableProjectionBacklog)
            .Should().BeEquivalentTo(SecurityAssetClassCatalog.AssetClasses,
                "every catalog class must be projected, an explicitly declared projection gap, or a declared backlog item");
    }

    [Fact]
    public void ProjectionCoverage_ReachesEveryOpsCapableAssetClassOutsideTheBacklog()
    {
        // The partition guard above is satisfied by naming a class an intentional gap. For an
        // Asset Operations class that answer is not available: the catalog gives it
        // ProjectedCashFlows, Reconciliation and LedgerProjection, so its terms are economics the
        // platform acts on, and "we chose not to project it" would contradict the capabilities it
        // publishes. This guard is what makes the backlog the only way to be ops-capable and
        // unprojected.
        SecurityAssetClassCatalog.AssetOperationsCapableAssetClasses
            .Except(OpsCapableProjectionBacklog, StringComparer.Ordinal)
            .Should().BeSubsetOf(PostgresSecurityMasterStore.ProjectedAssetClasses,
                "an ops-capable class declares LedgerProjection, so its economic terms need a relational "
                + "projection rather than living only inside the asset_specific_terms blob");
    }

    [Fact]
    public void OpsCapableProjectionBacklog_NamesOnlyOpsCapableClassesThatAreStillUnprojected()
    {
        OpsCapableProjectionBacklog.Should().OnlyContain(
            assetClass => SecurityAssetClassCatalog.AssetOperationsCapableAssetClasses.Contains(assetClass),
            "the backlog tracks the ops-capable set; a class outside it belongs in IntentionallyUnprojectedAssetClasses");

        OpsCapableProjectionBacklog.Should().NotIntersectWith(
            PostgresSecurityMasterStore.ProjectedAssetClasses,
            "a class that gained a projection must leave the backlog, or the coverage guard above silently stops covering it");

        OpsCapableProjectionBacklog.Should().BeSubsetOf(
            OpsCapableProjectionBacklogCeiling,
            "the backlog is a ratchet: a class may leave it by gaining a projection, but a projected "
            + "class may never be dropped back onto it to satisfy the coverage guard");
    }
}
