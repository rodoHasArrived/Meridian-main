using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

public sealed partial class AssetAcquisitionLotPostgresRoundTripTests
{
    /// <summary>
    /// Synthetic analytics inputs; real settlement verification, source spine, review/approval,
    /// evidence retention and PostgreSQL atomic journal/lot posting. This does not test coupon schedules.
    /// </summary>
    [LedgerDatabaseFact]
    public Task GoldenBondSettlement_ReviewApprovePostAndReopen_PreservesCleanLotAndDirtyJournalExactlyOnce()
        => AssertSpineAcquisitionAsync(faceLot: true, receiptBacked: true);

    private static BondSettlementConsiderationService CreateGoldenBondSettlementService(Guid securityId)
    {
        var analytics = Substitute.For<IBondAnalyticsService>();
        analytics.CalculateAsync(Arg.Any<BondAnalyticsRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<BondAnalyticsRequest>();
            return new BondAnalyticsResult(securityId, "USD", "synthetic-golden-bond-v1", request.Context,
                BondYieldConvention.NominalPeriodic, 101.25m, 102.75m, 1.50m,
                0.05m, 0.049m, 4m, 3.9m, 20m, 0.04m, 0.05m, [], [],
                [new BondCalculationInput("SecurityTerms", securityId.ToString("D"), 1, new string('a', 64))], [], [], []);
        });
        return new BondSettlementConsiderationService(analytics);
    }

    private static async Task<(PostedPostingRuleJournalCandidateResultDto Posted, PostPostingRuleJournalCandidateRequestDto Request)>
        PostGoldenBondThroughReviewAsync(LedgerJournalStoreOptions options, AccountingConfigurationService configuration,
            IAccountingActionAuditStore audit, ILedgerJournalStore journal, IAssetAccountingEventSpineService spine,
            IAccountingPostingCandidatePostService posting, AssetAccountingPostingCandidateDto candidate, CancellationToken ct)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-golden-bond-approval-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var vault = new FileEvidenceArtifactStore(directory, NullLogger<FileEvidenceArtifactStore>.Instance);
            ManualJournalEntryWorkbenchService Reopen() => new(new PostgresManualJournalEntryDraftStore(options),
                configuration, audit, journalStore: journal, mutationRecovery: new PostgresManualJournalMutationRecoveryStore(options),
                evidenceArtifacts: vault, assetCandidatePosting: posting, assetAccountingSource: spine);
            var workbench = Reopen();
            var draft = await workbench.SaveAssetAccountingDraftAsync(candidate, Preparer, ct);
            var replayedDraft = await Reopen().SaveAssetAccountingDraftAsync(candidate, Preparer, ct);
            replayedDraft.JournalEntryId.Should().Be(draft.JournalEntryId);
            replayedDraft.Version.Should().Be(draft.Version);
            draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
            draft.TotalDebits.Should().Be(102_750m);
            draft.TotalCredits.Should().Be(102_750m);
            draft.AssetAccountingDraft!.Spine.EventAmount.Should().Be(101_250m,
                "the event carrying movement and lot basis exclude purchased accrued interest");
            draft.AssetAccountingDraft.Spine.DraftedLotMutation!.Acquisition!.SettlementConsideration!
                .PurchasedAccruedInterest.Should().Be(1_500m);
            (await journal.GetByPeriodAsync(candidate.Spine.Scope.PeriodId, ct: ct)).Should().BeEmpty();
            (await journal.ListOpenTaxLotsByAssetScopeAsync(candidate.Spine.Scope.LedgerBookId,
                candidate.Spine.Scope.SecurityId, candidate.Spine.Scope.BookPositionId, EffectiveDate, ct)).Should().BeEmpty();

            var submitted = await workbench.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
                draft.JournalEntryId, Fund, Preparer, draft.Version, CorrelationId: "golden-bond-submit",
                LedgerBookId: draft.LedgerBookId, TenantId: Tenant, CompanyId: Company), ct);
            JournalEntryLifecycleActionRequestDto Action(JournalEntryLifecycleActionDto action, ManualJournalEntryDraftDto current,
                string actor = Approver) => new(current.JournalEntryId, Fund, action, actor, current.Version,
                    Notes: "Review retained clean cost and purchased interest against the synthetic confirmation.",
                    CorrelationId: "golden-bond-" + action, LedgerBookId: current.LedgerBookId, TenantId: Tenant, CompanyId: Company);
            var selfApproval = () => workbench.ApplyLifecycleActionAsync(Action(JournalEntryLifecycleActionDto.Approve, submitted, Preparer), ct);
            await selfApproval.Should().ThrowAsync<InvalidOperationException>().WithMessage("*independent*");
            var approved = (await workbench.ApplyLifecycleActionAsync(Action(JournalEntryLifecycleActionDto.Approve, submitted), ct)).JournalEntry;
            approved.AssetAccountingApproval!.Approver.Should().Be(Approver);
            (await journal.GetByPeriodAsync(candidate.Spine.Scope.PeriodId, ct: ct)).Should().BeEmpty();
            var postCommand = Action(JournalEntryLifecycleActionDto.Post, approved);
            var posted = (await workbench.ApplyLifecycleActionAsync(postCommand, ct)).JournalEntry;
            posted.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
            var receipt = posted.AssetAccountingPostingResult!;
            receipt.TaxLotMutationBatchId.Should().NotBeNull();
            receipt.JournalImpact!.TotalDebits.Should().Be(102_750m);
            receipt.JournalImpact.TotalCredits.Should().Be(102_750m);
            var retry = (await Reopen().ApplyLifecycleActionAsync(postCommand, ct)).JournalEntry;
            retry.AssetAccountingPostingResult.Should().BeEquivalentTo(receipt);
            var queue = await Reopen().GetWorkbenchAsync(Fund, draft.LedgerBookId, ct, Tenant, Company);
            queue.Drafts.Should().ContainSingle().Which.AssetAccountingDraft.Should().BeEquivalentTo(candidate);
            return (receipt, posted.AssetAccountingPostIntent!.CanonicalRequest);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
