using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Instruments.AssetOperations;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

public sealed partial class CanonicalLotCorporateActionServiceTests
{
    [Fact]
    public async Task CapitalReturn_PreviewReloadsTheLotAndReferenceWithoutPosting()
    {
        var fixture = new Fixture();
        var reviewed = CapitalReturnTestData.Build(fixture.Reviewed.ExpectedLot,
            securityHash: fixture.Reviewed.ExpectedSecurityHash);
        var mapped = new CorporateActionAssetAccountingEventMapper().Map(CapitalReturnTestData.MapRequest(reviewed)).Projection!;
        var prepared = await fixture.Service.PreviewAsync(mapped, reviewed, "Assets:Investment");
        prepared.CapitalReturn.Should().BeSameAs(reviewed);
        prepared.Intent.Should().Be(AssetLotMutationIntentDto.CapitalReturn);
        prepared.AssetAccountId.Should().Be("Assets:Investment");
        fixture.Spine.ReceivedCalls().Should().BeEmpty();
        fixture.Lots.ReceivedCalls().Should().OnlyContain(call => call.GetMethodInfo().Name == "GetTaxLotsByIdsAsync");
    }

    [Fact]
    public async Task CapitalReturn_DraftForwardsExactEvidenceAndRefusesChangedLotBeforeSpineWrite()
    {
        var fixture = new Fixture();
        var reviewed = CapitalReturnTestData.Build(fixture.Reviewed.ExpectedLot,
            securityHash: fixture.Reviewed.ExpectedSecurityHash);
        var mapped = new CorporateActionAssetAccountingEventMapper().Map(CapitalReturnTestData.MapRequest(reviewed)).Projection!;
        var source = mapped.Event;
        var spine = fixture.Candidate.Spine with
        {
            EventId = source.EconomicEvent.EventId,
            EconomicEvent = source.EconomicEvent,
            ProjectionLineage = source.ProjectionLineage,
            Scope = source.Scope,
            EventAmount = source.EventAmount,
            CorporateAction = null,
            CapitalReturn = reviewed
        };
        fixture.Spine.ProjectAsync(source, Arg.Any<CancellationToken>()).Returns(new AssetAccountingEventSpineAppendResultDto(spine, false));
        fixture.Spine.BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(fixture.Candidate);
        await fixture.Service.DraftAsync(mapped, reviewed, "Assets:Investment", "preparer", source.ProjectedAtUtc, "Return of capital");
        await fixture.Spine.Received(1).BuildPostingCandidateAsync(Arg.Is<AssetAccountingPostingCandidateRequestDto>(request =>
            request.LotMutation!.CapitalReturn == reviewed && request.LotMutation.AssetAccountId == "Assets:Investment"
            && request.ExpectedSpineVersion == spine.SpineVersion && request.RetainedEvidence == source.RetainedEvidence),
            Arg.Any<CancellationToken>());
        fixture.Spine.ClearReceivedCalls();
        fixture.RetainedLot = fixture.RetainedLot with { Version = fixture.RetainedLot.Version + 1 };
        var stale = () => fixture.Service.DraftAsync(mapped, reviewed, "Assets:Investment", "preparer", source.ProjectedAtUtc, "Stale return");
        await stale.Should().ThrowAsync<InvalidOperationException>().WithMessage("*stale*");
        fixture.Spine.ReceivedCalls().Should().BeEmpty();
    }
}
