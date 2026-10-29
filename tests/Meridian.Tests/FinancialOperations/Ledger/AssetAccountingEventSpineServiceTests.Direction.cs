using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AssetAccountingEventSpineServiceTests
{
    [Theory]
    [InlineData(AssetAccountingEventKindDto.Acquisition, BookPositionSides.Short)]
    [InlineData(AssetAccountingEventKindDto.Acquisition, BookPositionSides.Liability)]
    [InlineData(AssetAccountingEventKindDto.Acquisition, "unknown")]
    [InlineData(AssetAccountingEventKindDto.Disposal, BookPositionSides.Short)]
    [InlineData(AssetAccountingEventKindDto.Disposal, BookPositionSides.Liability)]
    [InlineData(AssetAccountingEventKindDto.Disposal, "unknown")]
    public async Task LongLotProjection_RejectsOtherPositionDirectionsBeforeRetainingAnEvent(
        AssetAccountingEventKindDto kind, string side)
    {
        var fixture = BuildFixture(kind);
        fixture = fixture with { Position = fixture.Position with { PositionSide = side } };
        var (service, store) = DirectionService(fixture);

        var project = () => service.ProjectAsync(fixture.Request);

        await project.Should().ThrowAsync<InvalidOperationException>().WithMessage("*requires a Long or Asset position*");
        await store.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default, default);
    }

    [Fact]
    public async Task RetainedAcquisitionProjection_CannotDraftALongLotForAShortPosition()
    {
        var fixture = BuildFixture(AssetAccountingEventKindDto.Acquisition);
        fixture = fixture with { Position = fixture.Position with { PositionSide = BookPositionSides.Short } };
        var (service, store) = DirectionService(fixture);
        var retained = BuildProjectedSpine(fixture);
        store.GetAsync(retained.EventId, 1, 2, Arg.Any<CancellationToken>())
            .Returns(new AssetAccountingEventProjectionRecord(retained, ComputeFingerprint(retained)));
        var request = BuildCandidateRequest(fixture) with
        {
            LotMutation = new(AssetLotMutationIntentDto.Acquire,
                new AssetAcquisitionLotDto(Guid.NewGuid(), "long-only", fixture.Request.EconomicEvent.EffectiveDate,
                    1m, fixture.Request.EventAmount, "Assets:Investment"))
        };

        var draft = () => service.BuildPostingCandidateAsync(request);

        await draft.Should().ThrowAsync<InvalidOperationException>().WithMessage("*requires a Long or Asset position*");
        await store.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default, default);
    }

    [Theory]
    [InlineData((OpenLotDirection)99)]
    public async Task UnsupportedInstructionDirection_IsRejectedBeforeAuthorityReads(OpenLotDirection direction)
    {
        var fixture = BuildFixture(AssetAccountingEventKindDto.Acquisition);
        var store = Substitute.For<IAssetAccountingEventProjectionStore>();
        var service = BuildService(eventStore: store);
        var request = BuildCandidateRequest(fixture) with
        {
            LotMutation = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.Acquire,
                new AssetAcquisitionLotDto(Guid.NewGuid(), "unsupported-direction", fixture.Request.EconomicEvent.EffectiveDate,
                    1m, fixture.Request.EventAmount, "Assets:Investment"))
            { Direction = direction }
        };

        var draft = () => service.BuildPostingCandidateAsync(request);

        await draft.Should().ThrowAsync<ArgumentException>();
        await store.DidNotReceiveWithAnyArgs().GetAsync(default, default, default, default);
        await store.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default, default);
    }

    [Fact]
    public async Task ShortProjection_RetainsExplicitDirectionBeforeDrafting()
    {
        var fixture = BuildFixture(AssetAccountingEventKindDto.Acquisition);
        fixture = fixture with { Position = fixture.Position with { PositionSide = BookPositionSides.Short } };
        var (service, store) = DirectionService(fixture);
        store.AppendAsync(Arg.Any<AssetAccountingEventSpineDto>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var projection = call.Arg<AssetAccountingEventSpineDto>();
                return Task.FromResult(new AssetAccountingEventAppendResult(projection, ComputeFingerprint(projection), false));
            });
        var effect = fixture.Request.ProjectedEffect;
        var request = fixture.Request with { Direction = OpenLotDirection.Short, ProjectedEffect = effect with
        { Lines = [new("Assets:Cash", fixture.Request.EventAmount, 0m, "USD", Dimensions: effect.Lines[0].Dimensions),
            new("Liabilities:ShortInvestments", 0m, fixture.Request.EventAmount, "USD", Dimensions: effect.Lines[0].Dimensions)] } };
        var result = await service.ProjectAsync(request);
        result.Spine.Direction.Should().Be(OpenLotDirection.Short);
        System.Text.Json.JsonSerializer.Serialize(result.Spine).Should().Contain("\"Direction\":\"Short\"");
        await store.Received(2).AppendAsync(Arg.Is<AssetAccountingEventSpineDto>(projection => projection.Direction == OpenLotDirection.Short),
            Arg.Any<long>(), fixture.Position.Version, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DraftCannotChangeTheRetainedProjectedDirection()
    {
        var fixture = BuildFixture(AssetAccountingEventKindDto.Acquisition);
        var (service, store) = DirectionService(fixture);
        var retained = BuildProjectedSpine(fixture);
        store.GetAsync(retained.EventId, 1, 2, Arg.Any<CancellationToken>())
            .Returns(new AssetAccountingEventProjectionRecord(retained, ComputeFingerprint(retained)));
        var request = BuildCandidateRequest(fixture) with { LotMutation = new AssetLotMutationInstructionDto(
            AssetLotMutationIntentDto.Acquire, new AssetAcquisitionLotDto(Guid.NewGuid(), "changed-direction",
                fixture.Request.EconomicEvent.EffectiveDate, 1m, fixture.Request.EventAmount, "Liabilities:ShortInvestments"))
            { Direction = OpenLotDirection.Short } };
        var draft = () => service.BuildPostingCandidateAsync(request);
        await draft.Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable Projected*direction*");
        await store.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default, default);
    }

    private static (AssetAccountingEventSpineService Service, IAssetAccountingEventProjectionStore Store)
        DirectionService(Fixture fixture)
    {
        var store = Substitute.For<IAssetAccountingEventProjectionStore>();
        var positions = Substitute.For<IInstrumentPositionProjectionStore>();
        positions.GetBookPositionAsync(fixture.Position.PositionId, Arg.Any<CancellationToken>()).Returns(fixture.Position);
        var securities = Substitute.For<ISecurityMasterQueryService>();
        securities.GetRecordedByIdAsOfAsync(fixture.Security.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(fixture.Security);
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(fixture.Book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(fixture.Book);
        books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>()).Returns([fixture.Period]);
        return (new AssetAccountingEventSpineService(store, positions, securities, books,
            Substitute.For<IAccountingPolicyService>(), Substitute.For<IAccountingConfigurationService>(),
            Substitute.For<IAccountingPostingCandidateAuthorityBuilder>(), Substitute.For<ILedgerJournalStore>()), store);
    }
}
