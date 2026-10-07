using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Tests.Storage;

namespace Meridian.Tests.AssetOperations;

public sealed class AmortizationHistoricalEvidenceTests
{
    [Fact]
    public void ReviewedOriginalAcquisitionAndReferenceDates_RemainValidWithoutRewritingThem()
    {
        var inputs = Inputs();
        foreach (var evidence in inputs.ExpectedLot.Acquisition.Evidence.Append(inputs.SecurityEvidence))
        {
            evidence.EffectiveDate.Should().BeBefore(inputs.AsOfDate);
            Matches(inputs, evidence, requireInstruction: false).Should().BeTrue();
            Matches(inputs, evidence, requireInstruction: true).Should().BeTrue();
        }
    }

    [Fact]
    public void HistoricalEvidence_RequiresExactReviewedIdentityAfterDrafting()
    {
        var inputs = Inputs();
        var original = inputs.ExpectedLot.Acquisition.Evidence.Single();
        foreach (var changed in new[]
        {
            original with { SubjectId = Guid.NewGuid().ToString("D") },
            original with { ContentHashSha256 = new string('f', 64) },
            original with { EvidenceUri = "document://changed/location" },
            original with { ReviewedBy = "substituted-reviewer" },
            inputs.SecurityEvidence with { SubjectId = Guid.NewGuid().ToString("D") },
            inputs.SecurityEvidence with { EvidenceVersion = inputs.Security.Version + 1 }
        })
            Matches(inputs, changed, requireInstruction: true).Should().BeFalse();
    }

    [Fact]
    public void OlderApprovalAndEventEvidence_KeepTheExistingExactDateRequirement()
    {
        var inputs = Inputs();
        foreach (var subject in new[] { AssetAccountingEvidenceSubjects.PostingApproval, AssetAccountingEvidenceSubjects.Event })
        {
            var evidence = inputs.SecurityEvidence with { SubjectType = subject };
            Matches(inputs, evidence, requireInstruction: false).Should().BeFalse();
            Matches(inputs, evidence, requireInstruction: true).Should().BeFalse();
        }
    }

    [Fact]
    public void MissingLotInstructionAndForeignEventKinds_CannotAcceptHistoricalPostingEvidence()
    {
        var inputs = Inputs();
        var evidence = inputs.ExpectedLot.Acquisition.Evidence.Single();
        AssetAccountingEvidenceSubjects.MatchesEventEvidenceDate(AssetAccountingEventKindDto.DepreciationAmortization,
            inputs.AsOfDate, inputs.Security.SecurityId, inputs.Security.Version, evidence, null, requireInstruction: true)
            .Should().BeFalse("ordinary depreciation has no reviewed canonical lot authority");
        foreach (var eventKind in Enum.GetValues<AssetAccountingEventKindDto>()
                     .Where(kind => kind != AssetAccountingEventKindDto.DepreciationAmortization))
            AssetAccountingEvidenceSubjects.MatchesEventEvidenceDate(eventKind, inputs.AsOfDate, inputs.Security.SecurityId,
                inputs.Security.Version, evidence, inputs, requireInstruction: true).Should().BeFalse();
        Matches(inputs, inputs.SecurityEvidence with { EffectiveDate = inputs.AsOfDate.AddDays(1) }, requireInstruction: false)
            .Should().BeFalse();
    }

    private static OpenLotAmortizationInstructionDto Inputs()
        => AtomicTaxLotJournalStoreTests.AmortPureInstruction(110m, 10m, BondAmortizationMethod.StraightLine, null);

    private static bool Matches(OpenLotAmortizationInstructionDto inputs, RetainedEvidenceIdentityDto evidence, bool requireInstruction)
        => AssetAccountingEvidenceSubjects.MatchesEventEvidenceDate(AssetAccountingEventKindDto.DepreciationAmortization,
            inputs.AsOfDate, inputs.Security.SecurityId, inputs.Security.Version, evidence, inputs, requireInstruction);
}
