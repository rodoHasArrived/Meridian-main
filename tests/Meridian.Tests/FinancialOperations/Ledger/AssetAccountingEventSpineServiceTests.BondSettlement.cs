using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Storage.AssetOperations;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AssetAccountingEventSpineServiceTests
{
    [Theory]
    [InlineData("currency")]
    [InlineData("terms-version")]
    [InlineData("cash-evidence")]
    public async Task BondSettlement_InvalidAuthorityOrMissingCashEvidence_IsRefusedBeforeEventStoreRead(string fault)
    {
        var fixture = BuildFixture(AssetAccountingEventKindDto.Acquisition);
        var eventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        var service = BuildService(eventStore: eventStore);
        var candidate = BuildCandidateRequest(fixture);
        var date = candidate.EconomicEvent.EffectiveDate;
        var face = candidate.EventAmount;
        var currency = fault == "currency" ? "EUR" : candidate.Currency;
        var inputs = new[]
        {
            new BondCalculationInput("SecurityTerms", candidate.Scope.SecurityId.ToString("D"),
                candidate.Scope.ExpectedSecurityVersion + (fault == "terms-version" ? 1 : 0), new string('a', 64))
        };
        var receipt = new BondSettlementConsiderationDto(candidate.Scope.SecurityId, currency, "bond-analytics-v1",
            new BondCalculationContext(candidate.AccountingTimestamp, date), BondYieldConvention.NominalPeriodic,
            face, 1m, 100m, 0m, face, 0m, 0m, face, face,
            fault == "cash-evidence" ? "not-retained" : candidate.RetainedEvidence[0].EvidenceId,
            "Assets/Cash", "Assets/PurchasedInterest", null, inputs);
        var acquisition = new AssetAcquisitionLotDto(Guid.NewGuid(), "bond-settlement-lot", date,
            face / 100m, 100m, "Assets/Bonds", face, 1m, 100m,
            BondAmortizationMethod.NoAmortization, SettlementConsideration: receipt);
        candidate = candidate with
        {
            LotMutation = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.Acquire, acquisition)
        };

        var act = () => service.BuildPostingCandidateAsync(candidate);

        await act.Should().ThrowAsync<ArgumentException>();
        await eventStore.DidNotReceiveWithAnyArgs().GetAsync(default, default, default, default);
        await eventStore.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default, default);
    }
}
