using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Storage.AssetOperations;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AssetAccountingEventSpineServiceTests
{
    [Theory]
    [InlineData("face-only")]
    [InlineData("factor-only")]
    [InlineData("par-only")]
    [InlineData("face-factor")]
    [InlineData("face-par")]
    [InlineData("factor-par")]
    [InlineData("zero-face")]
    [InlineData("negative-face")]
    [InlineData("zero-factor")]
    [InlineData("negative-factor")]
    [InlineData("factor-above-one")]
    [InlineData("zero-par")]
    [InlineData("negative-par")]
    [InlineData("quantity-mismatch")]
    [InlineData("yield-minus-one")]
    [InlineData("yield-below-minus-one")]
    public async Task BuildPostingCandidateAsync_InvalidFaceTerms_FailsBeforeStoreRead(string fault)
    {
        var fixture = BuildFixture(AssetAccountingEventKindDto.Acquisition);
        var eventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        var service = BuildService(eventStore: eventStore);
        var acquisition = new AssetAcquisitionLotDto(Guid.NewGuid(), "invalid-face-acquisition",
            fixture.Request.EconomicEvent.EffectiveDate, 1m, fixture.Request.EventAmount,
            "assets/investments", OriginalFace: 100m, BookedFactor: 1m, ParBasis: 100m);
        acquisition = fault switch
        {
            "face-only" => acquisition with { BookedFactor = null, ParBasis = null },
            "factor-only" => acquisition with { OriginalFace = null, ParBasis = null },
            "par-only" => acquisition with { OriginalFace = null, BookedFactor = null },
            "face-factor" => acquisition with { ParBasis = null },
            "face-par" => acquisition with { BookedFactor = null },
            "factor-par" => acquisition with { OriginalFace = null },
            "zero-face" => acquisition with { OriginalFace = 0m },
            "negative-face" => acquisition with { OriginalFace = -100m },
            "zero-factor" => acquisition with { BookedFactor = 0m },
            "negative-factor" => acquisition with { BookedFactor = -1m },
            "factor-above-one" => acquisition with { BookedFactor = 1.01m },
            "zero-par" => acquisition with { ParBasis = 0m },
            "negative-par" => acquisition with { ParBasis = -100m },
            "quantity-mismatch" => acquisition with { OriginalFace = 200m },
            "yield-minus-one" => acquisition with { AmortizationMethod = BondAmortizationMethod.ConstantYield, EffectiveYield = -1m },
            "yield-below-minus-one" => acquisition with { AmortizationMethod = BondAmortizationMethod.ConstantYield, EffectiveYield = -1.01m },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        // Complete tuples must exercise their numeric refusal, not the missing-method guard.
        if (acquisition.HasFaceValueTerms && acquisition.AmortizationMethod is null)
            acquisition = acquisition with { AmortizationMethod = BondAmortizationMethod.NoAmortization };
        var request = BuildCandidateRequest(fixture) with
        {
            LotMutation = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.Acquire, acquisition)
        };

        var act = () => service.BuildPostingCandidateAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
        await eventStore.DidNotReceiveWithAnyArgs().GetAsync(default, default, default, default);
        await eventStore.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default, default);
    }
}
