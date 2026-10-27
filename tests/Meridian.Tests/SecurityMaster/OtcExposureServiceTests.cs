using FluentAssertions;
using Meridian.Application.SecurityMaster.Otc;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The exposure orchestrator against substitute stores: it refuses an empty netting set, a default
/// as-of date and a blank actor before reading anything, answers null for an unknown netting set,
/// reads no balances without a collateral agreement, values only the contracts that enter, values
/// every contract at one knowledge time and passes the netting sets that share the agreement to the rules.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OtcExposureServiceTests
{
    private static readonly DateOnly Trade = new(2026, 3, 2);
    private static readonly DateOnly AsOf = new(2026, 3, 31);
    private static readonly Guid SwapId = Guid.NewGuid();
    private static readonly Guid MasterId = Guid.NewGuid();
    private static readonly Guid CsaId = Guid.NewGuid();
    private static readonly Guid NettingSetId = Guid.NewGuid();

    private readonly IOtcContractStore _contracts = Substitute.For<IOtcContractStore>();
    private readonly IOtcCollateralStore _collateral = Substitute.For<IOtcCollateralStore>();
    private readonly IOtcContractValuationSource _valuation = Substitute.For<IOtcContractValuationSource>();
    private readonly List<OtcContractValuationRequest> _requests = [];

    public OtcExposureServiceTests()
    {
        _valuation.ValueAsync(Arg.Do<OtcContractValuationRequest>(_requests.Add), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Valued(call.Arg<OtcContractValuationRequest>())));
        _collateral.AppendExposureSnapshotAsync(Arg.Any<OtcExposureSnapshotDto>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<OtcExposureSnapshotDto>() with { Version = 1, PayloadSha256 = new string('0', 64) }));
        _collateral.ListCollateralBalancesAsync(CsaId, Arg.Any<CancellationToken>()).Returns(Balances());
        _collateral.ListNettingSetsUnderCollateralAgreementAsync(CsaId, Arg.Any<CancellationToken>()).Returns(new[] { NettingSetId });
    }

    [Fact]
    public async Task Refuses_AnEmptyNettingSet_ADefaultDate_AndABlankActor_BeforeReadingAnything()
    {
        var service = Service();

        await service.Invoking(s => s.ComputeAsync(Guid.Empty, AsOf, "casey")).Should().ThrowAsync<ArgumentException>().WithMessage("*names the netting set*");
        await service.Invoking(s => s.ComputeAsync(NettingSetId, default, "casey")).Should().ThrowAsync<ArgumentException>()
            .WithMessage("*states the date it values*", "an omitted asOfDate binds to 0001-01-01 and must not be computed");
        await service.Invoking(s => s.ComputeAsync(NettingSetId, AsOf, " ")).Should().ThrowAsync<ArgumentException>().WithMessage("*names the operator*");

        await _contracts.DidNotReceiveWithAnyArgs().GetNettingSetAsync(default, default);
        await _collateral.DidNotReceiveWithAnyArgs().AppendExposureSnapshotAsync(default!, default);
    }

    [Fact]
    public async Task AnUnknownNettingSet_IsNull_AndNothingIsValuedOrRecorded()
    {
        _contracts.GetNettingSetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((OtcNettingSetViewDto?)null);

        (await Service().ComputeAsync(NettingSetId, AsOf, "casey")).Should().BeNull();

        await _valuation.DidNotReceiveWithAnyArgs().ValueAsync(default!, default);
        await _collateral.DidNotReceiveWithAnyArgs().ListCollateralBalancesAsync(default, default);
        await _collateral.DidNotReceiveWithAnyArgs().AppendExposureSnapshotAsync(default!, default);
    }

    [Fact]
    public async Task WithoutACollateralAgreement_NoBalancesAreRead_AndTheSnapshotReportsExposuresOnly()
    {
        var view = View() with { CollateralAgreement = null };
        view = view with
        {
            NettingSet = view.NettingSet with { CollateralAgreementId = null },
            Contracts = [view.Contracts[0] with { ContractId = Guid.NewGuid(), NotionalCurrency = "EUR", Status = OtcContractStatus.Matured }, .. view.Contracts]
        };
        _contracts.GetNettingSetAsync(NettingSetId, Arg.Any<CancellationToken>()).Returns(view);

        var snapshot = (await Service().ComputeAsync(NettingSetId, AsOf, " casey "))!;

        await _collateral.DidNotReceiveWithAnyArgs().ListCollateralBalancesAsync(default, default);
        await _collateral.DidNotReceiveWithAnyArgs().ListNettingSetsUnderCollateralAgreementAsync(default, default);
        _requests.Should().HaveCount(2, "the matured contract does not enter and is not valued");
        _requests.Should().OnlyContain(r => r.Currency == "USD", "the currency comes from a contract that enters, not the matured EUR one");
        snapshot.Currency.Should().Be("USD");
        snapshot.Status.Should().Be(OtcExposureSnapshotStatus.Incomplete);
        snapshot.Blockers.Select(b => b.Code).Should().Equal(OtcValuationBlockerCodes.NoCollateralAgreement);
        snapshot.NetExposure.Should().Be(75_000m);
        snapshot.FundAsSecuredParty.Should().BeNull();
        snapshot.CounterpartyAsSecuredParty.Should().BeNull();
        snapshot.Movements.Should().BeEmpty();
        snapshot.ComputedBy.Should().Be("casey");
        await _collateral.Received(1).AppendExposureSnapshotAsync(Arg.Any<OtcExposureSnapshotDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuppliedKnowledgeCutoff_IsForwardedToEveryContract()
    {
        _contracts.GetNettingSetAsync(NettingSetId, Arg.Any<CancellationToken>()).Returns(View());
        var knownAt = new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero);

        var snapshot = (await Service().ComputeAsync(NettingSetId, AsOf, "casey", knownAt))!;

        _requests.Should().HaveCount(2).And.OnlyContain(r => r.KnowledgeAsOf == knownAt && r.AsOfDate == AsOf && r.Currency == "USD");
        snapshot.Status.Should().Be(OtcExposureSnapshotStatus.Complete);
        snapshot.Movements.Should().ContainSingle().Which.Amount.Should().Be(330_000m, "case A through the orchestrator");
    }

    [Fact]
    public async Task WithoutAKnowledgeCutoff_EveryContractIsValuedAtTheComputationInstant()
    {
        _contracts.GetNettingSetAsync(NettingSetId, Arg.Any<CancellationToken>()).Returns(View());

        var snapshot = (await Service().ComputeAsync(NettingSetId, AsOf, "casey"))!;

        _requests.Should().HaveCount(2);
        _requests.Select(r => r.KnowledgeAsOf).Distinct().Should().ContainSingle("one knowledge time for the whole snapshot")
            .Which.Should().Be(snapshot.ComputedAt, "the knowledge time is the instant the snapshot is stamped with");
    }

    [Fact]
    public async Task TheNettingSetsUnderTheAgreement_ReachTheRules()
    {
        var other = Guid.NewGuid();
        _contracts.GetNettingSetAsync(NettingSetId, Arg.Any<CancellationToken>()).Returns(View());
        _collateral.ListNettingSetsUnderCollateralAgreementAsync(CsaId, Arg.Any<CancellationToken>()).Returns(new[] { NettingSetId, other });

        var snapshot = (await Service().ComputeAsync(NettingSetId, AsOf, "casey"))!;

        snapshot.Status.Should().Be(OtcExposureSnapshotStatus.Incomplete);
        snapshot.Blockers.Should().ContainSingle(b => b.Code == OtcValuationBlockerCodes.SharedCollateralAgreement && b.Reason.Contains(other.ToString("D")));
        snapshot.Movements.Should().BeEmpty();
    }

    private OtcExposureService Service() => new(_contracts, _collateral, _valuation, NullLogger<OtcExposureService>.Instance);

    private static OtcNettingSetViewDto View()
    {
        var now = DateTimeOffset.UtcNow;
        var master = new OtcMasterAgreementDto(MasterId, "Big Bank", null, "Meridian Master Fund", null, OtcMasterAgreementType.Isda2002, new DateOnly(2024, 1, 15), "New York", null, 1, "casey", now);
        var csa = new OtcCollateralAgreementDto(
            CsaId, MasterId, "USD", 0m, 250_000m, 100_000m, 1_000_000m, 10_000m, "Daily",
            [new OtcEligibleCollateralDto("USD cash", null, 0m), new OtcEligibleCollateralDto("US Treasuries", "Bond", 2m)],
            new DateOnly(2024, 1, 15), null, null, 2, "casey", now);
        var nettingSet = new OtcNettingSetDto(NettingSetId, MasterId, CsaId, "Big Bank ISDA", null, 1, "casey", now);
        return new OtcNettingSetViewDto(nettingSet, master, csa,
        [
            Contract(OtcContractDirection.Buy, 10_000_000m, OtcContractStatus.Pending),
            Contract(OtcContractDirection.Sell, 4_000_000m, OtcContractStatus.Confirmed)
        ]);
    }

    private static OtcContractDto Contract(OtcContractDirection direction, decimal notional, OtcContractStatus status)
        => new(Guid.NewGuid(), SwapId, NettingSetId, null, direction, notional, "USD", Trade, Trade.AddDays(2), Trade.AddYears(5), status, null, null, 1, "casey", DateTimeOffset.UtcNow);

    private static OtcCollateralBalanceDto[] Balances() =>
    [
        new(Guid.NewGuid(), CsaId, OtcCollateralDirection.Posted, OtcCollateralKind.Cash, "USD cash", null, "USD", 400_000m, 1m, 400_000m, 0m, AsOf.AddDays(-1), null, null, 1, "casey", DateTimeOffset.UtcNow),
        new(Guid.NewGuid(), CsaId, OtcCollateralDirection.Posted, OtcCollateralKind.Security, "US Treasuries", Guid.NewGuid(), "USD", 2_000m, 100.125m, 200_250m, 2m, AsOf.AddDays(-1), null, null, 1, "casey", DateTimeOffset.UtcNow)
    ];

    /// <summary>Values the contract at 1.25% of notional to the buyer, as of the requested date.</summary>
    private static OtcContractValuationDto Valued(OtcContractValuationRequest request)
    {
        var contract = request.Contract;
        return new OtcContractValuationDto(contract.ContractId, contract.SecurityId, request.AsOfDate, OtcValuationStatus.Valued,
            (contract.Direction == OtcContractDirection.Buy ? 1m : -1m) * contract.Notional * 1.25m / 100m, contract.NotionalCurrency, "test", [], [], DataProvenance.Real);
    }
}
