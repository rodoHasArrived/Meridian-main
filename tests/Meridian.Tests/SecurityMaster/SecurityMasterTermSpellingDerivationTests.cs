using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The conflict-detection profile-field exclusions and the data-quality maturity reads used to
/// restate term-key spellings by hand. They now derive the spellings from the schema's declared
/// aliases. These tests pin each derived list to the exact set it replaced, so the derivation is
/// provably behavior-preserving, and a later schema alias change is a deliberate decision here.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterTermSpellingDerivationTests
{
    [Fact]
    public void DeclaredSpellings_PutTheKeyFirst_ThenTopLevelAliasesOnly()
    {
        SecurityAssetTermsSchema.DeclaredSpellings("maturity").Should().Equal("maturity", "maturityDate");
        // The swap leg element's dayCountBasis alias is an element field and must not widen this.
        SecurityAssetTermsSchema.DeclaredSpellings("dayCount").Should().Equal("dayCount", "dayCountConvention");
        SecurityAssetTermsSchema.DeclaredSpellings("noSuchTerm").Should().Equal("noSuchTerm");
    }

    [Fact]
    public void ProfileFieldComparisonExclusions_DeriveTheRetainedKeySet()
    {
        // The literal set as it stood before the derivation.
        SecurityMasterConflictDetection.ProfileFieldComparisonExclusions.Should().BeEquivalentTo(
        [
            "maturity", "maturityDate", "issueDate", "couponRate", "par", "principalFace",
            "paymentFrequency", "dayCount", "dayCountConvention",
            "principalSchedule",
            "currentFactor", "factor", "factorDate", "factorSchedule", "factorScheduleEntries",
        ]);
        SecurityMasterConflictDetection.ProfileFieldComparisonExclusions.Comparer
            .Equals("MATURITYDATE", "maturityDate").Should().BeTrue("lookups stay case-insensitive");
    }

    [Fact]
    public void ProfileFieldComparisonExclusions_DoNotPullInOtherClassesAliases()
    {
        // A CDS's "frequency" and a structured note's "coupon" are aliases of excluded keys in other
        // classes; excluding them would leave a governed profile field with no comparator.
        SecurityMasterConflictDetection.ProfileFieldComparisonExclusions
            .Should().NotContain(["frequency", "coupon", "dayCountBasis"]);
    }

    [Fact]
    public void MaturitySpellings_DeriveTheRetainedPair()
    {
        SecurityMasterDataQualityService.MaturitySpellings.Should().Equal("maturity", "maturityDate");
    }
}
