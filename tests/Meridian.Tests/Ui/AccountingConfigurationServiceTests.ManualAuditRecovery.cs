using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingConfigurationServiceTests
{
    [Fact]
    public async Task ManualAuditRecovery_DraftWriteThenFailure_RestartRepairsOriginalAuditExactlyOnce()
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var request = new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user", "draft-command");
        var broken = fixture.Service(drafts: new RecoveryFailingDraftStore(fixture.Drafts(), afterWrite: true));
        await Assert.ThrowsAsync<IOException>(() => broken.SaveDraftAsync(request));
        (await fixture.Drafts().GetAsync(request.Draft.FundProfileId, request.Draft.JournalEntryId))!.Version.Should().Be(1);
        (await fixture.Audit().ListAsync()).Should().BeEmpty();

        var restarted = fixture.Service();
        var repaired = await restarted.SaveDraftAsync(request);
        var replayed = await fixture.Service().SaveDraftAsync(request);
        repaired.Version.Should().Be(1);
        replayed.Should().BeEquivalentTo(repaired);
        var audit = (await fixture.Audit().ListAsync()).Should().ContainSingle().Subject;
        audit.Action.Should().Be("manual-je.save-draft");
        audit.Actor.Should().Be("ops-user");
        audit.BeforeHash.Should().NotBe(audit.AfterHash);
        fixture.PendingFiles().Should().BeEmpty();

        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.SaveDraftAsync(request with { Actor = "other-user" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.SaveDraftAsync(request with { Draft = request.Draft with { Memo = "Changed content" } }));
        (await fixture.Audit().ListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ManualAuditRecovery_UnavailableAudit_NeverReturnsAnUnauditedIdempotentSuccess()
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var request = new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user", "audit-outage");
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(audit: new RecoveryFailingAudit(fixture.Audit(), "manual-je.save-draft")).SaveDraftAsync(request));
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(audit: new RecoveryFailingAudit(fixture.Audit(), "manual-je.save-draft")).SaveDraftAsync(request));
        fixture.PendingFiles().Should().ContainSingle();
        (await fixture.Service().SaveDraftAsync(request)).Version.Should().Be(1);
        (await fixture.Audit().ListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ManualAuditRecovery_PostCommittedBeforeFailure_RestartVerifiesWithoutPostingAgain()
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var approved = await fixture.ApprovedAsync();
        var request = RecoveryPostRequest(approved);
        using var target = new RecoveryFailingPostingTarget(fixture.Ledger, failAfterCommit: true);
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(posting: target).ApplyLifecycleActionAsync(request));
        fixture.Ledger.Appended.Should().ContainSingle();
        fixture.Ledger.Appended[0].PostingCommand!.Actor.Should().Be("controller");
        (await fixture.Drafts().GetAsync(approved.FundProfileId, approved.JournalEntryId))!.Status.Should().Be(ManualJournalEntryStatusDto.Approved);

        // Closing later cannot erase an already committed financial fact. Recovery verifies it
        // and finishes only the original draft/audit outcome, without invoking posting again.
        fixture.Ledger.SetPeriodStatus("HardClosed");
        using var forbiddenPosting = new RecoveryFailingPostingTarget(fixture.Ledger, failAfterCommit: false);
        var repaired = await fixture.Service(posting: forbiddenPosting).ApplyLifecycleActionAsync(request);
        var replay = await fixture.Service(posting: forbiddenPosting).ApplyLifecycleActionAsync(request);
        forbiddenPosting.Calls.Should().Be(0);
        fixture.Ledger.Appended.Should().ContainSingle();
        repaired.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        replay.Should().BeEquivalentTo(repaired);
        (await fixture.Audit().ListAsync()).Should().ContainSingle(x => x.Action == "manual-je.post");
        fixture.PendingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task ManualAuditRecovery_UnappliedPostingIntent_RechecksChangedPeriodAuthority()
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var approved = await fixture.ApprovedAsync();
        var request = RecoveryPostRequest(approved);
        using var target = new RecoveryFailingPostingTarget(fixture.Ledger, failAfterCommit: false);
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(posting: target).ApplyLifecycleActionAsync(request));
        fixture.PendingFiles().Should().ContainSingle();
        fixture.Ledger.Appended.Should().BeEmpty();
        fixture.Ledger.SetPeriodStatus("HardClosed");

        var retry = () => fixture.Service().ApplyLifecycleActionAsync(request);
        await retry.Should().ThrowAsync<InvalidOperationException>().WithMessage("*period is locked*");
        fixture.Ledger.Appended.Should().BeEmpty();
        (await fixture.Audit().ListAsync()).Should().NotContain(x => x.Action == "manual-je.post");
    }

    [Fact]
    public async Task ManualAuditRecovery_UnappliedPostingIntent_RefusesChangedApproval()
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var approved = await fixture.ApprovedAsync();
        var request = RecoveryPostRequest(approved);
        using var target = new RecoveryFailingPostingTarget(fixture.Ledger, failAfterCommit: false);
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(posting: target).ApplyLifecycleActionAsync(request));
        await fixture.Drafts().SaveAsync(approved with { Status = ManualJournalEntryStatusDto.Rejected, ApprovalId = null, Version = approved.Version + 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service().ApplyLifecycleActionAsync(request));
        fixture.Ledger.Appended.Should().BeEmpty();
        fixture.PendingFiles().Should().ContainSingle();
        (await fixture.Drafts().GetAsync(approved.FundProfileId, approved.JournalEntryId))!.Status.Should().Be(ManualJournalEntryStatusDto.Rejected);
    }

    [Theory]
    [InlineData(JournalEntryLifecycleActionDto.Reverse, "manual-je.reverse", "manual-je.reverse-draft")]
    [InlineData(JournalEntryLifecycleActionDto.Rebook, "manual-je.rebook", "manual-je.rebook-draft")]
    public async Task ManualAuditRecovery_CorrectionSecondAuditFails_RestartRepairsBothDraftsExactlyOnce(
        JournalEntryLifecycleActionDto action, string firstAction, string secondAction)
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var approved = await fixture.ApprovedAsync();
        var posted = (await fixture.Service().ApplyLifecycleActionAsync(RecoveryPostRequest(approved))).JournalEntry;
        var request = new JournalEntryLifecycleActionRequestDto(posted.JournalEntryId, posted.FundProfileId,
            action, "controller", posted.Version, Notes: "Correct retained close adjustment.", CorrelationId: "correction-command",
            EvidenceLinks: [$"/api/workstation/evidence/subjects/accounting-record/{(action == JournalEntryLifecycleActionDto.Reverse ? "reversal" : "rebook")}/ledger-book/{posted.LedgerBookId:D}/{posted.PeriodId}"],
            LedgerBookId: posted.LedgerBookId);
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(audit: new RecoveryFailingAudit(fixture.Audit(), secondAction)).ApplyLifecycleActionAsync(request));
        (await fixture.Drafts().ListAsync(posted.FundProfileId)).Should().HaveCount(2);
        (await fixture.Audit().ListAsync()).Should().ContainSingle(x => x.Action == firstAction)
            .And.NotContain(x => x.Action == secondAction);

        var repaired = await fixture.Service().ApplyLifecycleActionAsync(request);
        var replay = await fixture.Service().ApplyLifecycleActionAsync(request);
        repaired.GeneratedJournalEntries.Should().ContainSingle();
        replay.Should().BeEquivalentTo(repaired);
        (await fixture.Drafts().ListAsync(posted.FundProfileId)).Should().HaveCount(2);
        var audits = await fixture.Audit().ListAsync();
        audits.Should().ContainSingle(x => x.Action == firstAction).And.ContainSingle(x => x.Action == secondAction);
        audits.Select(x => x.AuditEventId).Should().OnlyHaveUniqueItems();
        fixture.Ledger.Appended.Should().ContainSingle();
        fixture.PendingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task ManualAuditRecovery_ChangedRetainedJournal_RefusesAuditRepair()
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var approved = await fixture.ApprovedAsync();
        var request = RecoveryPostRequest(approved);
        using var target = new RecoveryFailingPostingTarget(fixture.Ledger, failAfterCommit: true);
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(posting: target).ApplyLifecycleActionAsync(request));
        var record = (await fixture.Ledger.GetByPeriodAsync(ManualJournalPeriodId)).Single();
        var altered = record with { AccountingPolicyVersion = "unrelated-policy" };
        var journal = NSubstitute.Substitute.For<ILedgerJournalStore>();
        journal.GetByAggregateAsync(ManualJournalLedgerBookId, NSubstitute.Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([altered]));

        await Assert.ThrowsAsync<Meridian.Ledger.LedgerValidationException>(() => fixture.Service(ledger: journal).ApplyLifecycleActionAsync(request));
        (await fixture.Audit().ListAsync()).Should().NotContain(x => x.Action == "manual-je.post");
        fixture.PendingFiles().Should().ContainSingle();
    }

    [Fact]
    public async Task ManualAuditRecovery_FileLease_ExcludesIndependentInstancesAndHonorsCancellation()
    {
        using var fixture = await ManualRecoveryFixture.CreateAsync();
        var first = new FileManualJournalMutationRecoveryStore(fixture.RecoveryDirectory);
        var second = new FileManualJournalMutationRecoveryStore(fixture.RecoveryDirectory);
        await using (var held = await first.OpenSessionAsync())
        {
            using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.OpenSessionAsync(cancelled.Token));
        }
        await using var acquired = await second.OpenSessionAsync();
        (await acquired.ListPendingAsync(CancellationToken.None)).Should().BeEmpty();
    }

    private static JournalEntryLifecycleActionRequestDto RecoveryPostRequest(ManualJournalEntryDraftDto draft)
        => new(draft.JournalEntryId, draft.FundProfileId, JournalEntryLifecycleActionDto.Post,
            "controller", draft.Version, "Post approved close adjustment.", "post-command",
            [ManualJournalPostingEvidence(draft)], LedgerBookId: draft.LedgerBookId);

    private sealed class ManualRecoveryFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "meridian-manual-recovery-" + Guid.NewGuid().ToString("N"));
        private readonly AccountingConfigurationService _configuration = CreateService();
        public WritableManualJournalLedgerJournalStore Ledger { get; } = WritableManualJournalLedgerJournalStore.Default();
        public string RecoveryDirectory => Path.Combine(_directory, "manual.json.mutations");
        public FileManualJournalEntryDraftStore Drafts() => new(Path.Combine(_directory, "manual.json"));
        public FileAccountingConfigurationStore Audit() => new(Path.Combine(_directory, "audit.json"));
        public string[] PendingFiles() => Directory.GetFiles(Path.Combine(RecoveryDirectory, "pending"), "*.json");

        public static async Task<ManualRecoveryFixture> CreateAsync()
        {
            var fixture = new ManualRecoveryFixture();
            Directory.CreateDirectory(fixture._directory);
            await SeedBalancedConfigurationAsync(fixture._configuration);
            return fixture;
        }

        public ManualJournalEntryWorkbenchService Service(IManualJournalEntryDraftStore? drafts = null,
            IAccountingActionAuditStore? audit = null, IGovernedLedgerPostingTarget? posting = null, ILedgerJournalStore? ledger = null)
            => new(drafts ?? Drafts(), _configuration, audit ?? Audit(), new DailyValuationSecurityMasterQueryService(),
                ledger ?? Ledger, postingTarget: posting,
                mutationRecovery: new FileManualJournalMutationRecoveryStore(RecoveryDirectory));

        public async Task<ManualJournalEntryDraftDto> ApprovedAsync()
        {
            var service = Service();
            var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user", "prepare-command"));
            var submitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
                saved.JournalEntryId, saved.FundProfileId, "controller", saved.Version, Notes: "Submit close adjustment.", CorrelationId: "submit-command", LedgerBookId: saved.LedgerBookId));
            return (await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(submitted.JournalEntryId,
                submitted.FundProfileId, JournalEntryLifecycleActionDto.Approve, "controller", submitted.Version,
                Notes: "Independent approval.", CorrelationId: "approve-command", EvidenceLinks: [ManualJournalApprovalEvidence(submitted)],
                LedgerBookId: submitted.LedgerBookId))).JournalEntry;
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    private sealed class RecoveryFailingPostingTarget(ILedgerJournalStore store, bool failAfterCommit) : IGovernedLedgerPostingTarget, IDisposable
    {
        private readonly DurableLedgerPostingTarget _inner = new(store);
        public int Calls { get; private set; }
        public async Task<GovernedLedgerPostingResult> PostAsync(LedgerJournalEntryWrite write, CancellationToken ct = default)
        {
            Calls++;
            if (failAfterCommit) await _inner.PostAsync(write, ct);
            throw new IOException("Injected posting handoff interruption.");
        }
        public void Dispose() => _inner.Dispose();
    }

    private sealed class RecoveryFailingAudit(IAccountingActionAuditStore inner, string action) : IAccountingActionAuditStore
    {
        public Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default)
            => auditEvent.Action == action ? Task.FromException(new IOException("Injected audit outage.")) : inner.AppendAsync(auditEvent, ct);
        public Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(string? fundProfileId = null, Guid? ledgerBookId = null,
            CancellationToken ct = default, string? tenantId = null, string? companyId = null)
            => inner.ListAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId);
    }

    private sealed class RecoveryFailingDraftStore(IManualJournalEntryDraftStore inner, bool afterWrite) : IManualJournalEntryDraftStore
    {
        public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default) => inner.ListFundProfileIdsAsync(ct);
        public Task<IReadOnlyList<ManualJournalEntryDraftDto>> ListAsync(string fundProfileId, Guid? ledgerBookId = null,
            CancellationToken ct = default, string? tenantId = null, string? companyId = null)
            => inner.ListAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId);
        public Task<ManualJournalEntryDraftDto?> GetAsync(string fundProfileId, Guid journalEntryId, CancellationToken ct = default,
            string? tenantId = null, string? companyId = null) => inner.GetAsync(fundProfileId, journalEntryId, ct, tenantId, companyId);
        public Task SaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default) => SaveBatchAsync([draft], ct);
        public async Task SaveBatchAsync(IReadOnlyList<ManualJournalEntryDraftDto> drafts, CancellationToken ct = default)
        {
            if (afterWrite) await inner.SaveBatchAsync(drafts, ct);
            throw new IOException("Injected draft handoff interruption.");
        }
    }
}
