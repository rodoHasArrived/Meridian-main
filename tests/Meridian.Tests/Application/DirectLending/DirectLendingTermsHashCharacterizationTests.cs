using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.Tests.Application.DirectLending;

/// <summary>
/// Pins the direct-lending terms hash for three canonical term sets, captured at base 7d072638
/// before schedule terms arrive on <see cref="DirectLendingTermsDto"/> (ADR-025, expand-then-contract).
/// </summary>
/// <remarks>
/// <para>The hash is the uppercase hex SHA-256 of the camel-case JSON of the terms record. It keys
/// every persisted terms version and projection run, so adding a member to the record, reordering
/// one, or changing the serializer options silently re-keys existing loans. A schema change must
/// keep these three values or ship an explicit terms-hash migration.</para>
/// <para>The F# aggregate computes the same hash independently
/// (<c>DirectLendingScheduleCharacterizationTests.fs</c> in <c>Meridian.FSharp.Tests</c> pins the same
/// three values), so the two lanes cannot drift apart unnoticed.</para>
/// </remarks>
public sealed class DirectLendingTermsHashCharacterizationTests
{
    /// <summary>Uppercase hex captured at base for <see cref="FixedBulletTerms"/>.</summary>
    public const string FixedBulletHash = "BF089DF1AF7A481E240BBF3F3C56F0C3FDD2E8164E62684B65BA7FEB85AB9724";

    /// <summary>Uppercase hex captured at base for <see cref="FloatingWithFloorCapAndSecurityMasterTerms"/>.</summary>
    public const string FloatingWithFloorCapAndSecurityMasterHash = "1D87D653D4189C9FEED4F9848DC9096AD6BF8AFB95023C9E4D9508AC3A42A2BB";

    /// <summary>Uppercase hex captured at base for <see cref="InterestOnlyWithGraceTerms"/>.</summary>
    public const string InterestOnlyWithGraceHash = "BB66416FE686BA4860EE7FE5AA4A0016D6DCE9CC8006F58DAC9E177CC6BB6E89";

    [Fact]
    public void ComputeTermsHash_FixedBullet_MatchesBaseCapture()
        => DirectLendingServiceSupport.ComputeTermsHash(FixedBulletTerms()).Should().Be(FixedBulletHash);

    [Fact]
    public void ComputeTermsHash_FloatingWithFloorCapAndSecurityMasterReference_MatchesBaseCapture()
        => DirectLendingServiceSupport.ComputeTermsHash(FloatingWithFloorCapAndSecurityMasterTerms())
            .Should().Be(FloatingWithFloorCapAndSecurityMasterHash);

    [Fact]
    public void ComputeTermsHash_InterestOnlyWithGrace_MatchesBaseCapture()
        => DirectLendingServiceSupport.ComputeTermsHash(InterestOnlyWithGraceTerms()).Should().Be(InterestOnlyWithGraceHash);

    [Fact]
    public void CreateTermsVersion_StampsTheSameHash()
        => DirectLendingServiceSupport.CreateTermsVersion(
                1, FixedBulletTerms(), "loan.created", null, DateTimeOffset.Parse("2026-01-15T00:00:00Z"))
            .TermsHash.Should().Be(FixedBulletHash);

    /// <summary>Fixed-rate bullet facility with a commitment fee and a covenant document.</summary>
    internal static DirectLendingTermsDto FixedBulletTerms()
        => new(
            OriginationDate: new DateOnly(2026, 1, 15),
            MaturityDate: new DateOnly(2029, 1, 15),
            CommitmentAmount: 5_000_000m,
            BaseCurrency: CurrencyCode.USD,
            RateTypeKind: RateTypeKind.Fixed,
            FixedAnnualRate: 0.0725m,
            InterestIndexName: null,
            SpreadBps: null,
            FloorRate: null,
            CapRate: null,
            DayCountBasis: DayCountBasis.Act360,
            PaymentFrequency: PaymentFrequency.Quarterly,
            AmortizationType: AmortizationType.Bullet,
            CommitmentFeeRate: 0.005m,
            DefaultRateSpreadBps: 200m,
            PrepaymentAllowed: true,
            CovenantsJson: """{"leverage":"<=4.0x"}""");

    /// <summary>Floating-rate facility with index floor and cap, rate bounds and a Security Master reference.</summary>
    internal static DirectLendingTermsDto FloatingWithFloorCapAndSecurityMasterTerms()
        => new(
            OriginationDate: new DateOnly(2026, 3, 22),
            MaturityDate: new DateOnly(2031, 3, 22),
            CommitmentAmount: 25_000_000m,
            BaseCurrency: CurrencyCode.EUR,
            RateTypeKind: RateTypeKind.Floating,
            FixedAnnualRate: null,
            InterestIndexName: "EURIBOR-3M",
            SpreadBps: 475m,
            FloorRate: 0.01m,
            CapRate: 0.12m,
            DayCountBasis: DayCountBasis.Act365F,
            PaymentFrequency: PaymentFrequency.Monthly,
            AmortizationType: AmortizationType.StraightLine,
            CommitmentFeeRate: 0.0025m,
            DefaultRateSpreadBps: null,
            PrepaymentAllowed: false,
            CovenantsJson: null,
            EffectiveRateFloor: 0.045m,
            EffectiveRateCap: 0.15m,
            SecurityMasterReference: new DirectLendingSecurityMasterReferenceDto(
                SecurityId: Guid.Parse("5c4ed0a5-0000-4000-8000-000000000001"),
                Symbol: "NWFLT31",
                Provenance: "source:security-master;definition:loan-facility",
                ApprovalReference: "sm-approval:nwflt31-controller",
                LedgerMappingReference: "ledger-map:nwflt31-direct-lending",
                Status: "Active"));

    /// <summary>Interest-only facility with an initial interest-only window, a grace period and prepayment economics.</summary>
    internal static DirectLendingTermsDto InterestOnlyWithGraceTerms()
        => new(
            OriginationDate: new DateOnly(2025, 11, 30),
            MaturityDate: new DateOnly(2030, 11, 30),
            CommitmentAmount: 12_500_000.50m,
            BaseCurrency: CurrencyCode.GBP,
            RateTypeKind: RateTypeKind.Fixed,
            FixedAnnualRate: 0.0915m,
            InterestIndexName: null,
            SpreadBps: null,
            FloorRate: null,
            CapRate: null,
            DayCountBasis: DayCountBasis.Thirty360,
            PaymentFrequency: PaymentFrequency.SemiAnnual,
            AmortizationType: AmortizationType.InterestOnly,
            CommitmentFeeRate: null,
            DefaultRateSpreadBps: 300m,
            PrepaymentAllowed: true,
            CovenantsJson: """{"minLiquidity":2500000,"tests":["dscr","ltv"]}""",
            InterestOnlyMonths: 18,
            GracePeriodDays: 10,
            PrepaymentPenaltyRate: 0.02m,
            PurchasePrice: 99.5m);
}
