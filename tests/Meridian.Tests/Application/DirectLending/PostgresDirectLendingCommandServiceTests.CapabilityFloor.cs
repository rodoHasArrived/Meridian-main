using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Meridian.Tests.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.Application.DirectLending;

// Publishing a loan into the Security Master Asset Operations projection writes terms history and
// schedule-derived flows, so a node the terms capability floor refuses publishes nothing; the durable
// outbox delivery keeps the publication for a retry.
public sealed partial class PostgresDirectLendingCommandServiceTests
{
    [Fact]
    public async Task PublishAssetOperations_RefusedByTheFloor_UpsertsNothing()
    {
        var (service, assetOperations, loanId) = ComposeFloorGated(SecurityMasterRebuildCapabilityFloorTests.RefusingVerifier());

        var action = () => service.PublishAssetOperationsAsync(loanId);

        await action.Should().ThrowAsync<SecurityMasterTermsCapabilityRefusedException>();
        await assetOperations.DidNotReceive().UpsertProjectionAsync(Arg.Any<AssetOperationsProjectionDto>(),
            Arg.Any<AssetOperationsWriteApprovalDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAssetOperations_AcceptedByTheFloor_Upserts()
    {
        var verifier = Substitute.For<ISecurityMasterTermsCapabilityVerifier>();
        var (service, assetOperations, loanId) = ComposeFloorGated(verifier);

        (await service.PublishAssetOperationsAsync(loanId)).IsSuccess.Should().BeTrue();

        await verifier.Received(1).VerifyAsync(Arg.Any<CancellationToken>());
        await assetOperations.Received(1).UpsertProjectionAsync(Arg.Any<AssetOperationsProjectionDto>(),
            Arg.Any<AssetOperationsWriteApprovalDto>(), Arg.Any<CancellationToken>());
    }

    private static (PostgresDirectLendingCommandService Service, IAssetOperationsCommandService AssetOperations, Guid LoanId) ComposeFloorGated(
        ISecurityMasterTermsCapabilityVerifier verifier)
    {
        var loanId = Guid.NewGuid();
        var operations = Substitute.For<IDirectLendingOperationsStore>();
        operations.GetProjectionRunsAsync(loanId, Arg.Any<CancellationToken>()).Returns([]);
        operations.GetCashTransactionsAsync(loanId, Arg.Any<CancellationToken>()).Returns([]);
        operations.GetReconciliationRunsAsync(loanId, Arg.Any<CancellationToken>()).Returns([]);
        var query = Substitute.For<IDirectLendingQueryService>();
        query.LoadAggregateAsync(loanId, Arg.Any<CancellationToken>()).Returns(new PersistedDirectLendingState(loanId, 7,
            BuildContract(loanId), BuildServicing(loanId)));
        var assetOperations = Substitute.For<IAssetOperationsCommandService>();
        var service = new PostgresDirectLendingCommandService(Substitute.For<IDirectLendingStateStore>(), operations, query,
            new LoanAccountingProjector(BuildLedgerJournalStore(), new Meridian.FinancialOperations.Ledger.AccountingPolicyService(),
                BuildSecurityMasterQueryService()),
            new DirectLendingOptions(), assetOperations, verifier);
        return (service, assetOperations, loanId);
    }
}
