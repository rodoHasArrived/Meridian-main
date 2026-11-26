using FluentAssertions;
using Meridian.Application.SecurityMaster;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// Review D3: resolving a conflict to its non-current candidate stages a governed draft whose
/// justification carries a marker; the canonical-merge handler reads it back to close the conflict.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ConflictCandidateDraftTests
{
    [Theory]
    [InlineData("EconomicTerms.couponRate", "assetSpecificTerms.couponRate")]
    [InlineData("EconomicTerms.maturityDate", "assetSpecificTerms.maturityDate")]
    [InlineData("ProfileFields.commitment", "assetSpecificTerms.profileFields.commitment")]
    public void GovernedFieldPaths_MapToWorkbenchPaths(string conflictPath, string expected)
        => SecurityMasterWorkbenchCommandService.WorkbenchPathForConflictField(conflictPath).Should().Be(expected);

    [Theory]
    [InlineData("CommonTerms.currency")]
    [InlineData("CommonTerms.countryOfRisk")]
    [InlineData("Identifier.Cusip")]
    public void PathsWithoutAGovernedFieldEdit_MapToNothing(string conflictPath)
        => SecurityMasterWorkbenchCommandService.WorkbenchPathForConflictField(conflictPath).Should().BeNull();

    [Fact]
    public void Marker_RoundTripsConflictIdAndSource_InsideAFreeTextJustification()
    {
        var conflictId = Guid.NewGuid();
        var justification = $"{SecurityMasterWorkbenchCommandService.ConflictCandidateMarker(conflictId, " Bloomberg Data ")} Adopt Bloomberg's value. Term sheet agrees.";

        var match = SecurityMasterWorkbenchCommandService.ConflictCandidateMarkerPattern.Match(justification);

        match.Success.Should().BeTrue();
        Guid.Parse(match.Groups["id"].Value).Should().Be(conflictId);
        match.Groups["source"].Value.Should().Be("Bloomberg Data");
    }

    [Fact]
    public void OrdinaryJustification_CarriesNoMarker()
        => SecurityMasterWorkbenchCommandService.ConflictCandidateMarkerPattern
            .IsMatch("Corrected coupon per agent term sheet [ref 42].").Should().BeFalse();
}
