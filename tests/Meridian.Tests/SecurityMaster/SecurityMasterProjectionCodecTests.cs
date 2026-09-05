using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Regression guards for the relational projection decoders in <see cref="PostgresSecurityMasterStore"/>.
/// These previously read field shapes the serializer never wrote — a nested <c>coupon</c> object for bonds
/// and a <c>swapType</c> field for swaps — so canonically serialized records silently projected null
/// coupon columns and a null swap type. The decoders now read the flat contract declared in
/// <see cref="SecurityAssetTermsSchema"/>; these tests assert the columns are populated end-to-end from a
/// canonical payload without a live database.
/// <para>
/// Covers the hand-written decoders only. The schema-driven projections declared in
/// <c>SecurityTermsProjectionRegistry</c> decode through one shared path, so their equivalent guards
/// live in <c>SecurityTermsProjectionRegistryTests</c> instead of being restated per class here.
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterProjectionCodecTests
{
    [Fact]
    public void Bond_FlatFixedCoupon_PopulatesCouponColumns()
    {
        // The canonical serializer emits the coupon flat (couponType/couponRate/dayCount), never nested.
        var record = Record("Bond", new
        {
            schemaVersion = 1,
            maturity = "2030-01-01",
            couponType = "Fixed",
            couponRate = 5.0m,
            dayCount = "30/360",
            isCallable = false,
            subclass = "Corporate",
            issuerName = "ACME"
        });

        PostgresSecurityMasterStore.TryBuildBondProjection(record, out var projection).Should().BeTrue();

        projection.CouponKind.Should().Be("Fixed");
        projection.FixedCouponRate.Should().Be(5.0m);
        projection.DayCountConvention.Should().Be("30/360");
        projection.MaturityDate.Should().Be(new DateOnly(2030, 1, 1));
    }

    [Fact]
    public void Bond_FlatFloatingCoupon_PopulatesFloatingColumns()
    {
        var record = Record("Bond", new
        {
            schemaVersion = 1,
            maturity = "2032-06-30",
            couponType = "Floating",
            floatingIndex = "SOFR",
            spreadBps = 125m,
            dayCount = "ACT/360",
            isCallable = false,
            subclass = "FloatingRate"
        });

        PostgresSecurityMasterStore.TryBuildBondProjection(record, out var projection).Should().BeTrue();

        projection.CouponKind.Should().Be("Floating");
        projection.FloatingRateIndex.Should().Be("SOFR");
        projection.FloatingSpreadBps.Should().Be(125m);
        projection.DayCountConvention.Should().Be("ACT/360");
    }

    [Fact]
    public void Bond_LegacyNestedCoupon_StillPopulatesCouponColumns()
    {
        // Backward compatibility: externally-authored payloads that still use the nested "coupon"
        // object are read via the fallback path.
        var record = Record("Bond", new
        {
            schemaVersion = 1,
            maturity = "2030-01-01",
            coupon = new { kind = "Fixed", rate = 4.5m, dayCountConvention = "30/360" },
            isCallable = false,
            subclass = "Corporate"
        });

        PostgresSecurityMasterStore.TryBuildBondProjection(record, out var projection).Should().BeTrue();

        projection.CouponKind.Should().Be("Fixed");
        projection.FixedCouponRate.Should().Be(4.5m);
        projection.DayCountConvention.Should().Be("30/360");
    }

    [Fact]
    public void Swap_FixedAndFloatingLegs_DerivesFixedFloatSwapType()
    {
        var record = Record("Swap", new
        {
            schemaVersion = 1,
            effectiveDate = "2024-01-01",
            maturityDate = "2029-01-01",
            legs = new object[]
            {
                new { legType = "Fixed", currency = "USD", fixedRate = 3.5m },
                new { legType = "Floating", currency = "USD", index = "SOFR" }
            }
        });

        PostgresSecurityMasterStore.TryBuildSwapProjection(record, out var projection).Should().BeTrue();

        projection.SwapType.Should().Be("FixedFloat");
        projection.EffectiveDate.Should().Be(new DateOnly(2024, 1, 1));
        projection.MaturityDate.Should().Be(new DateOnly(2029, 1, 1));
    }

    [Fact]
    public void Swap_TwoFloatingLegs_DerivesBasisFloatSwapType()
    {
        var terms = JsonSerializer.SerializeToElement(new
        {
            legs = new object[]
            {
                new { legType = "Floating", currency = "USD", index = "SOFR" },
                new { legType = "Floating", currency = "USD", index = "FEDFUNDS" }
            }
        });

        PostgresSecurityMasterStore.DeriveSwapType(terms).Should().Be("BasisFloat");
    }

    [Fact]
    public void Swap_ExplicitSwapType_IsHonouredOverLegDerivation()
    {
        var terms = JsonSerializer.SerializeToElement(new
        {
            swapType = "OIS",
            legs = new object[] { new { legType = "Fixed", currency = "USD" } }
        });

        PostgresSecurityMasterStore.DeriveSwapType(terms).Should().Be("OIS");
    }

    [Fact]
    public void Swap_NoLegsAndNoSwapType_YieldsNull()
    {
        var terms = JsonSerializer.SerializeToElement(new { });

        PostgresSecurityMasterStore.DeriveSwapType(terms).Should().BeNull();
    }

    private static SecurityProjectionRecord Record(string assetClass, object assetSpecificTerms)
        => new(
            SecurityId: Guid.NewGuid(),
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: "Test Security",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "TEST",
            CommonTerms: JsonSerializer.SerializeToElement(new { }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(assetSpecificTerms),
            Provenance: JsonSerializer.SerializeToElement(new { }),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-1),
            EffectiveTo: null,
            Identifiers: Array.Empty<SecurityIdentifierDto>(),
            Aliases: Array.Empty<SecurityAliasDto>());
}
