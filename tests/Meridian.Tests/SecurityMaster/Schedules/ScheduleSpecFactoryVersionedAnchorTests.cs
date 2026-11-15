using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Tests.SecurityMaster.Schedules;

public sealed class ScheduleSpecFactoryVersionedAnchorTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void RetainedCdAnchorsAboveItsStamp_DoNotChangeItsHistoricalScheduleRefusal(int version)
    {
        var security = Certificate(version);
        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        foreach (var result in new[] { ScheduleSpecFactory.ResolveShape(security, terms), ScheduleSpecFactory.Resolve(security, terms, null) })
        {
            result.Status.Should().Be(DeclaredScheduleStatus.Refused);
            result.Refusal!.Code.Should().Be(DeclaredScheduleRefusalCode.EffectiveDateMissing);
            result.Refusal.Message.Should().Contain("'CertificateOfDeposit' terms declare no issueDate");
        }
        var write = () => ScheduleTermsWriteRules.ValidateDeclarations(security.AssetClass, security.AssetSpecificTerms, version);
        write.Should().Throw<InvalidOperationException>().WithMessage("*terms declare no issueDate*");
    }

    [Fact]
    public void ExplicitCdV6Anchors_AreAvailableToTheScheduleFactory_WhileAccrualStillKeepsItsDeclaredScheduleGate()
    {
        var security = Certificate(6);
        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        ScheduleSpecFactory.ResolveShape(security, terms).Status.Should().Be(DeclaredScheduleStatus.Declared);
        ScheduleSpecFactory.Resolve(security, terms, null).Status.Should().Be(DeclaredScheduleStatus.Declared);
        var write = () => ScheduleTermsWriteRules.ValidateDeclarations(security.AssetClass, security.AssetSpecificTerms, 6);
        write.Should().NotThrow();
        var blocker = SecurityRecurringAccrualPolicies.EvaluateTerms(security, SecurityRecurringAccrualPolicies.FixedBulletActual365);
        blocker.Should().NotBeNull();
        blocker!.Code.Should().Be("AccrualEconomicsUnsupported");
    }

    private static SecurityDetailDto Certificate(int version) => new(Guid.NewGuid(), "CertificateOfDeposit",
        SecurityStatusDto.Active, "Synthetic versioned CD", "USD", JsonSerializer.SerializeToElement(new { }),
        JsonSerializer.SerializeToElement(new
        {
            termsFormat = SecurityTermsFormats.Flat,
            assetClassTermsVersion = version,
            issuerName = "Synthetic bank",
            maturity = "2027-01-01",
            issueDate = "2026-01-01",
            principal = 100_000m,
            paymentFrequency = "AtMaturity",
            couponRate = 5m,
            rateUnit = "Percent",
            dayCount = "ACT/365",
            scheduleConventions = new { couponRateUnit = "Percent", accrualAdjusted = false, couponCashBasis = "DayCount" }
        }), [], [], 1, new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null);
}
