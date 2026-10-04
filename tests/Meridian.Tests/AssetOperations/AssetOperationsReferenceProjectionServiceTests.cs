using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

/// <summary>
/// Guards for the DirectLoan and StructuredCredit reference services: the asset-class gate, the
/// blank-key convention shared with every other instrument projection service, and the schedule
/// reads that are the whole point of projecting these two classes relationally.
/// </summary>
public sealed class AssetOperationsReferenceProjectionServiceTests
{
    [Fact]
    public async Task DirectLoan_GetReferenceAsync_MapsTheProjectedLoanTerms()
    {
        var securityId = Guid.NewGuid();
        var projectionStore = Substitute.For<IDirectLoanReferenceProjectionStore>();
        var securityMasterStore = Substitute.For<ISecurityMasterStore>();

        projectionStore.GetDirectLoanAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DirectLoanProjectionRow?>(new(
                securityId, "Meridian Industrials Term Loan B", "USD", "Meridian Industrials LLC",
                new DateOnly(2030, 3, 31), "SOFR", 425m, 9.55m, "Quarterly", "IHSMarkit", "LOAN-1", 4)));
        securityMasterStore.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SecurityProjectionRecord?>(Projection(securityId, "DirectLoan")));

        var result = await new DirectLoanProjectionService(securityMasterStore, projectionStore)
            .GetReferenceAsync(securityId);

        result.Should().NotBeNull();
        result!.Borrower.Should().Be("Meridian Industrials LLC");
        result.ReferenceIndex.Should().Be("SOFR");
        result.SpreadBps.Should().Be(425m);
        result.CurrentCouponRate.Should().Be(9.55m);
        result.Maturity.Should().Be(new DateOnly(2030, 3, 31));
        result.PrimaryIdentifier.Should().Be("LOAN-1");
        result.Version.Should().Be(4);
    }

    [Fact]
    public async Task DirectLoan_GetReferenceAsync_ReturnsNullForAnotherAssetClass()
    {
        var securityId = Guid.NewGuid();
        var projectionStore = Substitute.For<IDirectLoanReferenceProjectionStore>();
        var securityMasterStore = Substitute.For<ISecurityMasterStore>();
        securityMasterStore.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SecurityProjectionRecord?>(Projection(securityId, "Bond")));

        var result = await new DirectLoanProjectionService(securityMasterStore, projectionStore)
            .GetReferenceAsync(securityId);

        result.Should().BeNull();
        await projectionStore.DidNotReceive().GetDirectLoanAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DirectLoan_GetPrincipalPaymentsDueAsync_MapsTheWindowAndRefusesAnInvertedOne()
    {
        var securityId = Guid.NewGuid();
        var projectionStore = Substitute.For<IDirectLoanReferenceProjectionStore>();
        IReadOnlyList<DirectLoanPrincipalPaymentRow> rows =
        [
            new(securityId, 0, new DateOnly(2028, 3, 31), 1_250_000m),
            new(securityId, 1, new DateOnly(2028, 9, 30), 1_500_000m)
        ];
        projectionStore
            .GetPrincipalPaymentsDueAsync(new DateOnly(2028, 1, 1), new DateOnly(2028, 12, 31), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(rows));

        var service = new DirectLoanProjectionService(Substitute.For<ISecurityMasterStore>(), projectionStore);

        var due = await service.GetPrincipalPaymentsDueAsync(new(2028, 1, 1), new(2028, 12, 31));
        due.Select(payment => payment.Amount).Should().Equal(1_250_000m, 1_500_000m);

        var inverted = await service.GetPrincipalPaymentsDueAsync(new(2028, 12, 31), new(2028, 1, 1));
        inverted.Should().BeEmpty();
        await projectionStore.DidNotReceive()
            .GetPrincipalPaymentsDueAsync(new DateOnly(2028, 12, 31), new DateOnly(2028, 1, 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DirectLoan_GetCovenantsAsync_KeepsTheContractedThresholdText()
    {
        var securityId = Guid.NewGuid();
        var projectionStore = Substitute.For<IDirectLoanReferenceProjectionStore>();
        IReadOnlyList<DirectLoanCovenantRow> rows =
        [
            new(securityId, 0, "MaxLeverage", "4.5x", "Tested quarterly")
        ];
        projectionStore.GetCovenantsAsync(securityId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(rows));

        var covenants = await new DirectLoanProjectionService(Substitute.For<ISecurityMasterStore>(), projectionStore)
            .GetCovenantsAsync(securityId);

        covenants.Should().ContainSingle();
        covenants[0].Threshold.Should().Be("4.5x");
        covenants[0].Notes.Should().Be("Tested quarterly");
    }

    [Fact]
    public async Task DirectLoan_GetByBorrowerAsync_ReturnsEmptyForABlankKey()
    {
        var projectionStore = Substitute.For<IDirectLoanReferenceProjectionStore>();

        var results = await new DirectLoanProjectionService(Substitute.For<ISecurityMasterStore>(), projectionStore)
            .GetByBorrowerAsync("   ");

        results.Should().BeEmpty();
        await projectionStore.DidNotReceive().GetByBorrowerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StructuredCredit_GetReferenceAsync_MapsTheTrancheTerms()
    {
        var securityId = Guid.NewGuid();
        var projectionStore = Substitute.For<IStructuredCreditReferenceProjectionStore>();
        var securityMasterStore = Substitute.For<ISecurityMasterStore>();

        projectionStore.GetStructuredCreditAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StructuredCreditProjectionRow?>(new(
                securityId, "MRDN 2026-1 B", "USD", "B", "MRDN-2026-1", "CLO", 10_000_000m, 0.8235m,
                "SOFR+250", "See trustee report 2026-07", new DateOnly(2031, 6, 15), "SC-1", 2)));
        securityMasterStore.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SecurityProjectionRecord?>(Projection(securityId, "StructuredCredit")));

        var result = await new StructuredCreditProjectionService(securityMasterStore, projectionStore)
            .GetReferenceAsync(securityId);

        result.Should().NotBeNull();
        result!.Tranche.Should().Be("B");
        result.PoolId.Should().Be("MRDN-2026-1");
        result.OriginalFace.Should().Be(10_000_000m);
        result.CurrentFactor.Should().Be(0.8235m);
        result.FactorScheduleReference.Should().Be("See trustee report 2026-07");
        result.Maturity.Should().Be(new DateOnly(2031, 6, 15));
    }

    [Fact]
    public async Task StructuredCredit_GetFactorAsOfAsync_ReturnsTheEffectivePointOrNull()
    {
        var securityId = Guid.NewGuid();
        var projectionStore = Substitute.For<IStructuredCreditReferenceProjectionStore>();
        projectionStore.GetFactorAsOfAsync(securityId, new DateOnly(2026, 6, 15), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StructuredCreditFactorScheduleRow?>(
                new(securityId, 0, new DateOnly(2026, 6, 1), 0.8412m)));
        projectionStore.GetFactorAsOfAsync(securityId, new DateOnly(2026, 5, 1), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StructuredCreditFactorScheduleRow?>(null));

        var service = new StructuredCreditProjectionService(Substitute.For<ISecurityMasterStore>(), projectionStore);

        var effective = await service.GetFactorAsOfAsync(securityId, new(2026, 6, 15));
        effective.Should().NotBeNull();
        effective!.AsOfDate.Should().Be(new DateOnly(2026, 6, 1));
        effective.Factor.Should().Be(0.8412m);

        (await service.GetFactorAsOfAsync(securityId, new(2026, 5, 1))).Should().BeNull();
    }

    [Fact]
    public async Task NullServices_DegradeToEmptyRatherThanThrowing()
    {
        var securityId = Guid.NewGuid();

        var loan = new NullDirectLoanReferenceService();
        (await loan.GetReferenceAsync(securityId)).Should().BeNull();
        (await loan.GetCovenantsAsync(securityId)).Should().BeEmpty();
        (await loan.GetPrincipalScheduleAsync(securityId)).Should().BeEmpty();
        (await loan.GetPrincipalPaymentsDueAsync(new(2028, 1, 1), new(2028, 12, 31))).Should().BeEmpty();
        (await loan.GetByBorrowerAsync("anyone")).Should().BeEmpty();
        (await loan.GetByReferenceIndexAsync("SOFR")).Should().BeEmpty();
        (await loan.GetMaturityLadderAsync(new(2028, 1, 1), new(2030, 1, 1))).Should().BeEmpty();

        var structured = new NullStructuredCreditReferenceService();
        (await structured.GetReferenceAsync(securityId)).Should().BeNull();
        (await structured.GetFactorScheduleAsync(securityId)).Should().BeEmpty();
        (await structured.GetFactorAsOfAsync(securityId, new(2026, 6, 15))).Should().BeNull();
        (await structured.GetByPoolAsync("MRDN-2026-1")).Should().BeEmpty();
        (await structured.GetByCollateralTypeAsync("CLO")).Should().BeEmpty();
    }

    private static SecurityProjectionRecord Projection(Guid securityId, string assetClass)
        => new(
            securityId, assetClass, SecurityStatusDto.Active, "Test", "USD",
            "InternalCode", "TEST",
            JsonDocument.Parse("{}").RootElement,
            JsonDocument.Parse("{}").RootElement,
            JsonDocument.Parse("{}").RootElement,
            1, DateTimeOffset.UtcNow, null, [], []);
}
