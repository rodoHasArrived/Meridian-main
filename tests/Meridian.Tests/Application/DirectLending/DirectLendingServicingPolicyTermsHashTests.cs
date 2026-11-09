using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLending.Aggregates;

namespace Meridian.Tests.Application.DirectLending;

/// <summary>
/// The PIK capitalization policy and the reset schedule are expand-only, hash-stable members of the
/// hashed terms record: absent, every pinned hash is unchanged; present, they are material terms.
/// </summary>
public sealed class DirectLendingServicingPolicyTermsHashTests
{
    [Fact]
    public void AbsentPolicies_AreOmittedSoPinnedHashesAreUnchanged()
    {
        var fixedTerms = DirectLendingTermsHashCharacterizationTests.FixedBulletTerms();
        var floatingTerms = DirectLendingTermsHashCharacterizationTests.FloatingWithFloorCapAndSecurityMasterTerms();

        System.Text.Json.JsonSerializer.Serialize(fixedTerms).Should().NotContain("PikCapitalization").And.NotContain("RateResetSchedule");
        DirectLendingServiceSupport.ComputeTermsHash(fixedTerms).Should().Be(DirectLendingTermsHashCharacterizationTests.FixedBulletHash);
        DirectLendingServiceSupport.ComputeTermsHash(floatingTerms)
            .Should().Be(DirectLendingTermsHashCharacterizationTests.FloatingWithFloorCapAndSecurityMasterHash);
    }

    [Fact]
    public void PresentPolicies_AreMaterialTerms_AndBothLanesHashThemIdentically()
    {
        var baseTerms = DirectLendingTermsHashCharacterizationTests.FloatingWithFloorCapAndSecurityMasterTerms();
        var withPolicies = baseTerms with
        {
            PikCapitalization = new PikCapitalizationPolicy(
                PikElection.WhenToggled,
                DeclaredRate.Percent(50m),
                PikCapitalizationFrequency.Quarterly,
                baseTerms.OriginationDate.AddMonths(3)),
            RateResetSchedule = new FloatingRateResetSchedule(
                baseTerms.InterestIndexName!,
                RateResetFrequency.Quarterly,
                baseTerms.OriginationDate,
                2,
                LookbackDayKind.Business,
                DeclaredRate.BasisPoints(250m),
                Floor: DeclaredRate.Percent(1m))
        };

        var csharpHash = DirectLendingServiceSupport.ComputeTermsHash(withPolicies);
        csharpHash.Should().NotBe(DirectLendingTermsHashCharacterizationTests.FloatingWithFloorCapAndSecurityMasterHash);

        var fsharpHash = DirectLendingAggregateInterop.CreateLoan(
                Guid.NewGuid(),
                new CreateLoanRequest(null, "Hash Loan", new BorrowerInfoDto(Guid.NewGuid(), "Borrower", null), baseTerms.OriginationDate, withPolicies),
                DateTimeOffset.UtcNow)
            .TermsVersion.TermsHash;
        fsharpHash.Should().Be(csharpHash);

        DirectLendingServiceSupport.ComputeTermsHash(withPolicies with { RateResetSchedule = withPolicies.RateResetSchedule! with { LookbackDays = 5 } })
            .Should().NotBe(csharpHash, "the lookback is a material reset term");
    }
}
