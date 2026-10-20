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
/// Bond and Swap now decode through <c>SecurityTermsProjectionRegistry</c> descriptors whose derived
/// columns call the same readers the hand-written decoders did; these guards hold the descriptors to
/// the flat contract and the legacy fallback. The generic decode path has its own guards in
/// <c>SecurityTermsProjectionRegistryTests</c>.
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterProjectionCodecTests
{
    private static readonly SecurityTermsProjectionDescriptor Bond =
        SecurityTermsProjectionRegistry.Descriptors.Single(static descriptor => descriptor.AssetClass == "Bond");

    [Fact]
    public void Bond_FlatFixedCoupon_PopulatesCouponColumns()
    {
        // The canonical serializer emits the coupon flat (couponType/couponRate/dayCount), never nested.
        var record = Record("Bond", new
        {
            schemaVersion = 1,
            maturity = "2091-01-01",
            couponType = "Fixed",
            couponRate = 5.0m,
            dayCount = "30/360",
            isCallable = false,
            subclass = "Corporate",
            issuerName = "ACME"
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Bond, record, out var plan).Should().BeTrue();

        plan.Value("coupon_kind").Should().Be("Fixed");
        plan.Value("fixed_coupon_rate").Should().Be(5.0m);
        plan.Value("maturity_date").Should().Be(new DateTime(2091, 1, 1));
        plan.SatelliteValue("bond_accrual_convention_projection", "day_count_convention").Should().Be("30/360");
        plan.SatelliteValue("bond_accrual_convention_projection", "coupon_kind").Should().Be("Fixed");
        // The lifecycle state reads the real UTC date; the maturity above is far enough out that
        // this stays Active rather than flipping to Matured as the calendar advances.
        plan.SatelliteValue("bond_lifecycle_projection", "lifecycle_stat").Should().Be("Active");
        plan.SatelliteColumns("bond_issuer_projection").Should().NotBeNull("the record names an issuer");
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

        PostgresSecurityMasterStore.TryBuildTermsProjection(Bond, record, out var plan).Should().BeTrue();

        plan.Value("coupon_kind").Should().Be("Floating");
        plan.Value("floating_rate_index").Should().Be("SOFR");
        plan.Value("floating_spread_bps").Should().Be(125m);
        plan.Value("fixed_coupon_rate").Should().Be(DBNull.Value);
        plan.SatelliteValue("bond_accrual_convention_projection", "day_count_convention").Should().Be("ACT/360");
        plan.SatelliteColumns("bond_issuer_projection").Should().BeNull("a bond that names no issuer has no issuer-ladder row");
    }

    [Fact]
    public void Bond_LegacyNestedCoupon_StillPopulatesCouponColumns()
    {
        // Backward compatibility: externally-authored payloads that still use the nested "coupon"
        // object are read via the fallback path.
        var record = Record("Bond", new
        {
            schemaVersion = 1,
            maturity = "2091-01-01",
            coupon = new { kind = "Fixed", rate = 4.5m, dayCountConvention = "30/360" },
            isCallable = false,
            subclass = "Corporate"
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Bond, record, out var plan).Should().BeTrue();

        plan.Value("coupon_kind").Should().Be("Fixed");
        plan.Value("fixed_coupon_rate").Should().Be(4.5m);
        plan.SatelliteValue("bond_accrual_convention_projection", "day_count_convention").Should().Be("30/360");
    }

    [Fact]
    public void Bond_MissingMaturity_HasNoProjection()
    {
        // The parent gates on maturity exactly as the hand-written writer did; without it the store
        // clears all four bond tables rather than projecting a bond with no term.
        var record = Record("Bond", new { schemaVersion = 1, couponType = "Fixed", couponRate = 5.0m, isCallable = false });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Bond, record, out _).Should().BeFalse();
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

        var swap = SecurityTermsProjectionRegistry.Descriptors.Single(static descriptor => descriptor.AssetClass == "Swap");
        PostgresSecurityMasterStore.TryBuildTermsProjection(swap, record, out var plan).Should().BeTrue();

        plan.Value("swap_type").Should().Be("FixedFloat");
        plan.Value("effective_date").Should().Be(new DateTime(2024, 1, 1));
        plan.Value("maturity_date").Should().Be(new DateTime(2029, 1, 1));
        plan.Value("lifecycle_stat").Should().Be("Active");
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
