using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The single rate-unit reader (security-master review D10, remediation slice 4): a record's declared
/// unit wins, then the field's declared default, and otherwise the value is Undeclared. Magnitude never
/// selects a unit.
/// </summary>
public sealed class RateUnitNormalizerTests
{
    [Theory]
    [InlineData(RateUnit.Percent, 0.5, 0.005)]
    [InlineData(RateUnit.Percent, 5.0, 0.05)]
    [InlineData(RateUnit.Fraction, 0.5, 0.5)]
    [InlineData(RateUnit.Fraction, 0.05, 0.05)]
    public void ARecordDeclaredUnitConverts_RegardlessOfMagnitude(RateUnit unit, double raw, double expected)
    {
        var result = RateUnitNormalizer.Normalize("Bond", "couponRate", (decimal)raw, unit);

        result.Should().Be(new RateUnitResolution.Resolved((decimal)raw, (decimal)expected, unit, RateUnitSource.RecordDeclared));
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(0.5)]
    [InlineData(0.05)]
    [InlineData(4.25)]
    public void AnUndeclaredLegacyBondCoupon_IsFlaggedAndNotConverted(double raw)
    {
        // A legacy 5.0 is not divided by 100, and a legacy 0.5 is not read as fifty percent.
        var result = RateUnitNormalizer.Normalize("Bond", "couponRate", (decimal)raw);

        result.Should().Be(new RateUnitResolution.Undeclared((decimal)raw));
        result.TryGetFraction(out _).Should().BeFalse();
        RateUnitNormalizer.DiagnosticCode(result).Should().Be(RateUnitNormalizer.UndeclaredCode);
        RateUnitNormalizer.DescribeRefusal(result, "Bond", "couponRate")
            .Should().Contain("has no declared unit").And.Contain(((decimal)raw).ToString(System.Globalization.CultureInfo.CurrentCulture));
    }

    [Fact]
    public void ZeroIsTheSameAmountInEveryUnit()
    {
        RateUnitNormalizer.Normalize("Bond", "couponRate", 0m)
            .Should().Be(new RateUnitResolution.Resolved(0m, 0m, RateUnit.Fraction, RateUnitSource.UnitInvariantZero));
    }

    [Fact]
    public void BasisPointFieldsAreBasisPointsByName_AndIgnoreTheRecordUnit()
    {
        RateUnitNormalizer.Normalize("Swap", "legs.spreadBps", 125m, RateUnit.Percent)
            .Should().Be(new RateUnitResolution.Resolved(125m, 0.0125m, RateUnit.BasisPoints, RateUnitSource.FieldDefault));
        RateUnitNormalizer.Normalize("Bond", "spreadBps", -15m)
            .Should().Be(new RateUnitResolution.Resolved(-15m, -0.0015m, RateUnit.BasisPoints, RateUnitSource.FieldDefault),
                "a negative spread is a supported economic fact");
    }

    [Fact]
    public void DepositDeclaresFraction_AndSupportsANegativeRate()
    {
        RateUnitNormalizer.Normalize("Deposit", "interestRate", 0.045m)
            .Should().Be(new RateUnitResolution.Resolved(0.045m, 0.045m, RateUnit.Fraction, RateUnitSource.FieldDefault));
        RateUnitNormalizer.Normalize("Deposit", "interestRate", -0.005m)
            .Should().Be(new RateUnitResolution.Resolved(-0.005m, -0.005m, RateUnit.Fraction, RateUnitSource.FieldDefault));
    }

    [Fact]
    public void ALegacyPercentagePointDeposit_IsOutsideTheDeclaredRange_NotReinterpreted()
    {
        var result = RateUnitNormalizer.Normalize("Deposit", "interestRate", 4.5m);

        result.Should().BeOfType<RateUnitResolution.OutOfRange>();
        result.TryGetFraction(out _).Should().BeFalse();
        RateUnitNormalizer.DiagnosticCode(result).Should().Be(RateUnitNormalizer.OutOfRangeCode);
    }

    [Fact]
    public void ABondCouponDoesNotPermitANegativeRate()
    {
        RateUnitNormalizer.Normalize("Bond", "couponRate", -0.5m, RateUnit.Percent)
            .Should().BeOfType<RateUnitResolution.OutOfRange>()
            .Which.Reason.Should().Contain("does not permit a negative rate");
    }

    [Fact]
    public void ASwapFixedRateMayBeNegativeUnderADeclaredUnit()
    {
        RateUnitNormalizer.Normalize("Swap", "legs.fixedRate", -0.25m, RateUnit.Percent)
            .Should().Be(new RateUnitResolution.Resolved(-0.25m, -0.0025m, RateUnit.Percent, RateUnitSource.RecordDeclared));
    }

    [Theory]
    [InlineData("Percent", RateUnit.Percent)]
    [InlineData("Fraction", RateUnit.Fraction)]
    [InlineData("percent", null)]
    [InlineData("BasisPoints", null)]
    [InlineData(null, null)]
    public void OnlyTheBlocksClosedVocabularyDeclaresARecordUnit(string? token, RateUnit? expected)
        => RateUnitNormalizer.ParseCouponRateUnit(token).Should().Be(expected);

    [Fact]
    public void TheUnitTableNamesOnlySchemaFieldsOfCataloguedClasses()
    {
        foreach (var field in SecurityRateFieldUnits.All)
        {
            SecurityAssetClassCatalog.TryGet(field.AssetClass, out var descriptor).Should().BeTrue(field.AssetClass);
            descriptor!.AssetClass.Should().Be(field.AssetClass, "the table is keyed by canonical class");
            var path = field.FieldKey.Split('.');
            if (path.Length == 1 && field.FieldKey is not ("currentIndexRate"))
            {
                SecurityAssetTermsSchema.Field(field.AssetClass, field.FieldKey)
                    .Should().NotBeNull($"{field.AssetClass}.{field.FieldKey} must be a declared schema field");
            }

            if (field.FieldKey.EndsWith("Bps", StringComparison.Ordinal))
            {
                field.DefaultUnit.Should().Be(RateUnit.BasisPoints, $"{field.FieldKey} is basis points by name");
                field.HonorsRecordUnit.Should().BeFalse();
            }
        }

        SecurityRateFieldUnits.All.Should().NotContain(field => field.FieldKey == "forwardRate",
            "an FX forward rate is a price, not a percentage rate");
        SecurityRateFieldUnits.All.Where(static field => field.DefaultUnit is RateUnit.Fraction or RateUnit.Percent)
            .Select(static field => $"{field.AssetClass}.{field.FieldKey}")
            .Should().BeEquivalentTo(["Deposit.interestRate", "CashSweep.yieldRate"],
                "fields written both ways declare no default (decision D3)");
    }

    [Fact]
    public void TheCouponFieldKeyNamesTheClassSpecificCouponTerm()
    {
        SecurityRateFieldUnits.CouponFieldKey("Bond").Should().Be("couponRate");
        SecurityRateFieldUnits.CouponFieldKey("Deposit").Should().Be("interestRate");
        SecurityRateFieldUnits.CouponFieldKey("Repo").Should().Be("repoRate");
        SecurityRateFieldUnits.CouponFieldKey("DirectLoan").Should().Be("currentCouponRate");
        SecurityRateFieldUnits.CouponFieldKey("StructuredCredit").Should().Be("couponOrIndex");
    }
}
