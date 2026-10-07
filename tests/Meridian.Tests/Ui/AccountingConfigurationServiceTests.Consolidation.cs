using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingConfigurationServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConsolidationManualWorkflow_RejectsUntrustedEvidenceOrReservedIdentity(bool includeReceipt)
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var draft = ConsolidationManualDraft();
        if (!includeReceipt)
            draft = draft with { ConsolidationEvidenceJson = null, ConsolidationEvidenceDigest = null, RequiresConsolidationEvidence = false };

        var action = () => service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(draft, "ops-user"));
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*authoritative consolidation intake*");
    }

    [Fact]
    public async Task ConsolidationManualWorkflow_ResavePreservesSourceReceiptIdentityAndCorrectionAncestry()
    {
        using var fixture = await CreateConsolidationManualFixtureAsync();
        var guard = new ChangingConsolidationGuard();
        var service = ConsolidationManualService(fixture, guard);
        var sourceId = Guid.NewGuid();
        var saved = await service.SaveAutomatedDraftAsync(new SaveManualJournalEntryDraftRequest(
            ConsolidationManualDraft() with { RebookedFromJournalEntryId = sourceId }, "ops-user"), CancellationToken.None);

        var edited = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(saved with
        {
            ConsolidationEvidenceJson = "invented",
            ConsolidationEvidenceDigest = "invented",
            RequiresConsolidationEvidence = false,
            TreasuryContext = saved.TreasuryContext! with { IdempotencyKey = "different-key" },
            RebookedFromJournalEntryId = Guid.NewGuid()
        }, "ops-user", LedgerBookId: saved.LedgerBookId));

        edited.ConsolidationEvidenceJson.Should().Be(saved.ConsolidationEvidenceJson);
        edited.ConsolidationEvidenceDigest.Should().Be(saved.ConsolidationEvidenceDigest);
        edited.RequiresConsolidationEvidence.Should().BeTrue();
        edited.TreasuryContext!.IdempotencyKey.Should().Be(saved.TreasuryContext!.IdempotencyKey);
        edited.RebookedFromJournalEntryId.Should().Be(sourceId);
    }

    [Fact]
    public async Task ConsolidationManualWorkflow_RequiresLiveGuardAndRevalidatesCompletedSubmitReceipt()
    {
        using var fixture = await CreateConsolidationManualFixtureAsync();
        var guard = new ChangingConsolidationGuard();
        var service = ConsolidationManualService(fixture, guard);
        var saved = await service.SaveAutomatedDraftAsync(new SaveManualJournalEntryDraftRequest(
            ConsolidationManualDraft(), "ops-user"), CancellationToken.None);
        var request = ConsolidationSubmitRequest(saved);

        var unavailable = () => ConsolidationManualService(fixture, null).SubmitApprovalAsync(request);
        await unavailable.Should().ThrowAsync<InvalidOperationException>().WithMessage("*source validation is unavailable*");
        await service.SubmitApprovalAsync(request);
        guard.IsCurrent = false;
        var replay = () => service.SubmitApprovalAsync(request);
        await replay.Should().ThrowAsync<InvalidOperationException>().WithMessage("*renewed review*");
    }

    [Fact]
    public async Task ConsolidationManualWorkflow_BlocksChangedSourcesAtApprovalAndPostingAndRetainsPostedEvidence()
    {
        using var fixture = await CreateConsolidationManualFixtureAsync();
        var guard = new ChangingConsolidationGuard();
        var service = ConsolidationManualService(fixture, guard);
        var saved = await service.SaveAutomatedDraftAsync(new SaveManualJournalEntryDraftRequest(
            ConsolidationManualDraft(), "ops-user"), CancellationToken.None);
        var submitted = await service.SubmitApprovalAsync(ConsolidationSubmitRequest(saved));
        var approval = ConsolidationApprovalRequest(submitted);
        guard.IsCurrent = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyLifecycleActionAsync(approval));
        guard.IsCurrent = true;
        var approved = (await service.ApplyLifecycleActionAsync(approval)).JournalEntry;
        guard.IsCurrent = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyLifecycleActionAsync(RecoveryPostRequest(approved)));
        (await fixture.Ledger.GetByPeriodAsync(ManualJournalPeriodId)).Should().BeEmpty();
        guard.IsCurrent = true;
        var posted = await service.ApplyLifecycleActionAsync(RecoveryPostRequest(approved));
        var journal = (await fixture.Ledger.GetByPeriodAsync(ManualJournalPeriodId)).Should().ContainSingle().Subject;
        journal.Entry.Metadata!.Tags!["consolidation.evidence"].Should().Be(saved.ConsolidationEvidenceJson);
        journal.Entry.Metadata.Tags["consolidation.digest"].Should().Be(saved.ConsolidationEvidenceDigest);
        guard.IsCurrent = false;
        var replay = await service.ApplyLifecycleActionAsync(RecoveryPostRequest(approved));
        replay.PostedJournal!.JournalEntryId.Should().Be(posted.PostedJournal!.JournalEntryId);
        (await fixture.Ledger.GetByPeriodAsync(ManualJournalPeriodId)).Should().ContainSingle();

        foreach (var action in new[] { JournalEntryLifecycleActionDto.Reverse, JournalEntryLifecycleActionDto.Rebook })
        {
            var correction = () => service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
                saved.JournalEntryId, saved.FundProfileId, action, "controller", posted.JournalEntry.Version,
                LedgerBookId: saved.LedgerBookId));
            await correction.Should().ThrowAsync<InvalidOperationException>().WithMessage("*rerun consolidation*");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsolidationManualWorkflow_RecoveryRechecksUncommittedSourcesButRepairsCommittedPosting(bool committed)
    {
        using var fixture = await CreateConsolidationManualFixtureAsync();
        var guard = new ChangingConsolidationGuard();
        var service = ConsolidationManualService(fixture, guard);
        var saved = await service.SaveAutomatedDraftAsync(new SaveManualJournalEntryDraftRequest(
            ConsolidationManualDraft(), "ops-user"), CancellationToken.None);
        var submitted = await service.SubmitApprovalAsync(ConsolidationSubmitRequest(saved));
        var approved = (await service.ApplyLifecycleActionAsync(ConsolidationApprovalRequest(submitted))).JournalEntry;
        using var failure = new RecoveryFailingPostingTarget(fixture.Ledger, committed);
        var request = RecoveryPostRequest(approved);
        await Assert.ThrowsAsync<IOException>(() => ConsolidationManualService(fixture, guard, failure).ApplyLifecycleActionAsync(request));
        guard.IsCurrent = false;
        var retry = () => ConsolidationManualService(fixture, guard).ApplyLifecycleActionAsync(request);
        if (committed)
        {
            (await retry()).JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
            fixture.PendingFiles().Should().BeEmpty();
            (await fixture.Ledger.GetByPeriodAsync(ManualJournalPeriodId)).Should().ContainSingle();
        }
        else
        {
            await retry.Should().ThrowAsync<InvalidOperationException>().WithMessage("*renewed review*");
            fixture.PendingFiles().Should().ContainSingle();
            (await fixture.Ledger.GetByPeriodAsync(ManualJournalPeriodId)).Should().BeEmpty();
        }
        failure.Calls.Should().Be(1);
    }

    private static ManualJournalEntryWorkbenchService ConsolidationManualService(ManualRecoveryFixture fixture,
        IConsolidationDraftGuard? guard, IGovernedLedgerPostingTarget? posting = null)
        => new(fixture.Drafts(), fixture.Configuration, fixture.Audit(), new DailyValuationSecurityMasterQueryService(),
            fixture.Ledger, postingTarget: posting,
            mutationRecovery: new FileManualJournalMutationRecoveryStore(fixture.RecoveryDirectory), consolidationGuard: guard);

    private static async Task<ManualRecoveryFixture> CreateConsolidationManualFixtureAsync()
    {
        var fixture = await ManualRecoveryFixture.CreateAsync();
        await fixture.Configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha", new ChartOfAccountsNodeDto("consolidation-ar", ConsolidationService.ReceivableAccount,
                ConsolidationService.ReceivableAccount, "Asset"), "ops-user"));
        await fixture.Configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha", new ChartOfAccountsNodeDto("consolidation-ap", ConsolidationService.PayableAccount,
                ConsolidationService.PayableAccount, "Liability"), "ops-user"));
        return fixture;
    }

    private static ManualJournalEntryDraftDto ConsolidationManualDraft()
    {
        var draft = BalancedManualJournalEntry();
        var dimensions = new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master");
        draft = draft with
        {
            Dimensions = dimensions,
            Lines = draft.Lines.Select(line => line with
            {
                EntityId = "entity-master",
                Dimensions = dimensions,
                SecurityId = null,
                LedgerAccountSymbol = null,
                AccountPath = line.Side == AccountingTemplateLineSideDto.Debit
                    ? ConsolidationService.ReceivableAccount : ConsolidationService.PayableAccount
            }).ToArray(),
            RequiresConsolidationEvidence = true,
            TreasuryContext = new TreasuryLedgerContextDto(IdempotencyKey: "consolidation:group:revision-1")
        };
        var evidence = new ConsolidationEvidenceDto(
            new ConsolidationRequestDto(Guid.NewGuid(), Guid.NewGuid(), ManualJournalLedgerBookId,
                ManualJournalPeriodId, draft.AccountingDate),
            "group:period", "source-version", [
                new ConsolidationBookVersionDto(ManualJournalLedgerBookId, 0, 0),
                new ConsolidationBookVersionDto(Guid.NewGuid(), 1, 1),
                new ConsolidationBookVersionDto(Guid.NewGuid(), 2, 1)],
            "perimeter-version", "v1", [], draft.Lines);
        var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return draft with
        {
            ConsolidationEvidenceJson = json,
            ConsolidationEvidenceDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
        };
    }

    private static SubmitManualJournalEntryApprovalRequest ConsolidationSubmitRequest(ManualJournalEntryDraftDto draft)
        => new(draft.JournalEntryId, draft.FundProfileId, "controller", draft.Version,
            CorrelationId: "consolidation-submit", LedgerBookId: draft.LedgerBookId);

    private static JournalEntryLifecycleActionRequestDto ConsolidationApprovalRequest(ManualJournalEntryDraftDto draft)
        => new(draft.JournalEntryId, draft.FundProfileId, JournalEntryLifecycleActionDto.Approve, "controller", draft.Version,
            Notes: "Reviewed source-backed elimination.", CorrelationId: "consolidation-approve",
            EvidenceLinks: [ManualJournalApprovalEvidence(draft)], LedgerBookId: draft.LedgerBookId);

    private sealed class ChangingConsolidationGuard : IConsolidationDraftGuard
    {
        public bool IsCurrent { get; set; } = true;
        public Task ValidateCurrentAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default)
            => IsCurrent ? Task.CompletedTask : throw new InvalidOperationException("Source balances changed; renewed review is required.");
    }
}
