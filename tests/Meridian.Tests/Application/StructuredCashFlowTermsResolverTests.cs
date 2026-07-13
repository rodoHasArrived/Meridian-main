using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.Application;

public sealed class StructuredCashFlowTermsResolverTests
{
    [Fact]
    public void Resolve_ShouldReadTypedTermsAndTypedFactorSchedule()
    {
        var security = Build(JsonSerializer.SerializeToElement(new
        {
            maturityDate = "2030-06-30",
            issueDate = "2020-06-30",
            originalFace = 250m,
            currentFactor = 0.95m,
            couponRate = 4.5m,
            paymentFrequency = "SemiAnnual",
            dayCountConvention = "30/360",
            factorSchedule = new object[]
            {
                new { asOfDate = "2020-01-01", factor = 0.9m },
                new { factorDate = "2021-01-01", currentFactor = 0.8m }
            }
        }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.MaturityDate.Should().Be(new DateOnly(2030, 6, 30));
        terms.IssueDate.Should().Be(new DateOnly(2020, 6, 30));
        terms.PrincipalFace.Should().Be(250m);
        terms.CurrentFactor.Should().Be(0.95m);
        terms.CouponRate.Should().Be(4.5m);
        terms.PaymentFrequency.Should().Be("SemiAnnual");
        terms.DayCountConvention.Should().Be("30/360");
        terms.HasFactorSchedule.Should().BeTrue();
        terms.FactorSchedule.Should().SatisfyRespectively(
            first => { first.AsOfDate.Should().Be(new DateOnly(2020, 1, 1)); first.Factor.Should().Be(0.9m); },
            second => { second.AsOfDate.Should().Be(new DateOnly(2021, 1, 1)); second.Factor.Should().Be(0.8m); });
    }

    [Fact]
    public void FactorAsOf_ShouldPickLatestScheduledFactorAndFallBackToScalar()
    {
        var security = Build(JsonSerializer.SerializeToElement(new
        {
            currentFactor = 0.95m,
            factorSchedule = new object[]
            {
                new { asOfDate = "2020-01-01", factor = 0.9m },
                new { asOfDate = "2021-01-01", factor = 0.8m }
            }
        }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.FactorAsOf(new DateOnly(2020, 6, 1)).Should().Be(0.9m);
        terms.FactorAsOf(new DateOnly(2022, 1, 1)).Should().Be(0.8m);
        // Before any schedule point, fall back to the scalar current factor.
        terms.FactorAsOf(new DateOnly(2019, 1, 1)).Should().Be(0.95m);
    }

    [Fact]
    public void Resolve_ShouldHonorAliasPriority_ParBeatsOriginalFace()
    {
        var security = Build(JsonSerializer.SerializeToElement(new { par = 100m, originalFace = 200m }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.PrincipalFace.Should().Be(100m);
    }

    [Fact]
    public void Resolve_ShouldReadFromNestedProfileFields()
    {
        var security = Build(JsonSerializer.SerializeToElement(new
        {
            profileFields = new { couponRate = 3.25m }
        }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.CouponRate.Should().Be(3.25m);
    }

    [Fact]
    public void Resolve_WithNoStructuredTerms_ShouldReturnNullsAndEmptySchedule()
    {
        var security = Build(JsonSerializer.SerializeToElement(new { }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.PrincipalFace.Should().BeNull();
        terms.CouponRate.Should().BeNull();
        terms.MaturityDate.Should().BeNull();
        terms.HasFactorSchedule.Should().BeFalse();
        terms.FactorSchedule.Should().BeEmpty();
    }

    private static SecurityDetailDto Build(JsonElement assetSpecificTerms)
        => new(
            Guid.NewGuid(),
            "Bond",
            SecurityStatusDto.Active,
            "Test bond",
            "USD",
            JsonSerializer.SerializeToElement(new { }),
            assetSpecificTerms,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "123456789", true, DateTimeOffset.UtcNow)],
            [],
            1,
            DateTimeOffset.UtcNow,
            null);
}
