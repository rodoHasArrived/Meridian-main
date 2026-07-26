using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Tests.Infrastructure;

namespace Meridian.Tests.Reconciliation;

/// <summary>
/// Scenario: a fund-operations analyst resolves a month-end custodian cash break with retained
/// support, and Meridian must preserve the paired decision across retries and process restarts.
/// </summary>
public sealed class StatementBreakDispositionTransactionTests : TempDirectoryTestBase
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 21, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DispositionAsync_CurrentVersion_ShouldCommitPairedAfterImagesAndHashChainedAudit()
    {
        var (breakStore, caseStore) = await SeedPairAsync();
        var service = CreateService(breakStore, caseStore);

        var result = await service.DispositionAsync(CreateCommand());

        result.Outcome.Should().Be(StatementBreakDispositionOutcome.Applied);
        result.Version.Should().Be(1);
        result.TransactionId.Should().NotBeNullOrWhiteSpace();
        result.Break.Should().NotBeNull();
        result.Case.Should().NotBeNull();
        result.Break!.Status.Should().Be("Resolved");
        result.Case!.Status.Should().Be("Resolved");
        result.Break.Version.Should().Be(result.Case.Version);
        result.Break.DispositionTransactionId.Should().Be(result.TransactionId);
        result.Case.DispositionTransactionId.Should().Be(result.TransactionId);
        result.Break.DispositionActor.Should().Be("fund-ops.alice");
        result.Break.DispositionRationale.Should().Be("Custodian confirmed the corrected settlement cash amount.");
        result.Break.DispositionEvidenceLinks.Should().Equal("evidence://custodian/confirmation-42");
        result.Break.DispositionEvidenceHash.Should().MatchRegex("^[0-9A-F]{64}$");
        result.Case.AuditEvents.Should().ContainSingle(item =>
            item.TransactionId == result.TransactionId &&
            item.Actor == "fund-ops.alice" &&
            item.Rationale == "Custodian confirmed the corrected settlement cash amount." &&
            item.EntryHash != null);

        var retainedBreak = await breakStore.GetAsync("break-cash-42");
        var retainedCase = await caseStore.GetAsync("case:break-cash-42");
        retainedBreak.Should().BeEquivalentTo(result.Break);
        retainedCase.Should().BeEquivalentTo(result.Case);

        var audit = await service.GetAuditHistoryAsync("break-cash-42");
        audit.Should().ContainSingle();
        audit[0].Sequence.Should().Be(1);
        audit[0].PreviousHash.Should().BeNull();
        audit[0].EntryHash.Should().MatchRegex("^[0-9A-F]{64}$");
        audit[0].EvidenceLinks.Should().Equal("evidence://custodian/confirmation-42");
    }

    [Fact]
    public async Task DispositionAsync_StalePairVersion_ShouldConflictWithoutMutationOrAudit()
    {
        var breakRecord = CreateBreak() with { Version = 1 };
        var reconciliationCase = CreateCase() with { Version = 1 };
        var (breakStore, caseStore) = await SeedPairAsync(breakRecord, reconciliationCase);
        var service = CreateService(breakStore, caseStore);

        var result = await service.DispositionAsync(CreateCommand(expectedVersion: 0));

        result.Outcome.Should().Be(StatementBreakDispositionOutcome.VersionConflict);
        (await breakStore.GetAsync(breakRecord.BreakId))!.Status.Should().Be("Open");
        (await caseStore.GetAsync(reconciliationCase.CaseId))!.Status.Should().Be("Open");
        (await service.GetAuditHistoryAsync(breakRecord.BreakId)).Should().BeEmpty();
    }

    [Fact]
    public async Task DispositionAsync_ExactReplayAfterRestart_ShouldReturnReceiptWithoutDuplicateMutation()
    {
        var (breakStore, caseStore) = await SeedPairAsync();
        var command = CreateCommand();
        var first = await CreateService(breakStore, caseStore).DispositionAsync(command);

        var restartedBreakStore = new JsonReconciliationBreakStore(TestDataRoot);
        var restartedCaseStore = new JsonReconciliationCaseStore(TestDataRoot);
        var restarted = CreateService(restartedBreakStore, restartedCaseStore);
        var replay = await restarted.DispositionAsync(command);
        var conflictingReplay = await restarted.DispositionAsync(command with
        {
            Rationale = "A changed explanation must not reuse the retained command id."
        });

        replay.Outcome.Should().Be(StatementBreakDispositionOutcome.IdempotentReplay);
        replay.TransactionId.Should().Be(first.TransactionId);
        replay.Version.Should().Be(1);
        replay.AuditHistory.Should().ContainSingle();
        conflictingReplay.Outcome.Should().Be(StatementBreakDispositionOutcome.CommandConflict);
        (await restarted.GetAuditHistoryAsync(command.BreakId)).Should().ContainSingle();
        (await restartedBreakStore.GetAsync(command.BreakId))!.Version.Should().Be(1);
        (await restartedCaseStore.GetAsync($"case:{command.BreakId}"))!.Version.Should().Be(1);
    }

    [Fact]
    public async Task DispositionAsync_MissingGovernanceEvidence_ShouldRejectBeforePersistence()
    {
        var (breakStore, caseStore) = await SeedPairAsync();
        var service = CreateService(breakStore, caseStore);

        var missingRationale = await service.DispositionAsync(CreateCommand(commandId: "cmd-no-rationale") with
        {
            Rationale = " "
        });
        var missingEvidence = await service.DispositionAsync(CreateCommand(commandId: "cmd-no-evidence") with
        {
            EvidenceLinks = []
        });
        var missingSuccessor = await service.DispositionAsync(CreateCommand(commandId: "cmd-no-successor") with
        {
            Disposition = ReconciliationBreakDispositionDto.Superseded
        });

        missingRationale.Outcome.Should().Be(StatementBreakDispositionOutcome.Rejected);
        missingEvidence.Outcome.Should().Be(StatementBreakDispositionOutcome.Rejected);
        missingSuccessor.Outcome.Should().Be(StatementBreakDispositionOutcome.Rejected);
        (await breakStore.GetAsync("break-cash-42"))!.Status.Should().Be("Open");
        (await caseStore.GetAsync("case:break-cash-42"))!.Status.Should().Be("Open");
        (await service.GetAuditHistoryAsync("break-cash-42")).Should().BeEmpty();
    }

    [Fact]
    public async Task DispositionAsync_MismatchedPairedCase_ShouldFailClosed()
    {
        var (breakStore, caseStore) = await SeedPairAsync(
            CreateBreak(),
            CreateCase() with { BreakId = "break-from-another-import" });
        var service = CreateService(breakStore, caseStore);

        var result = await service.DispositionAsync(CreateCommand());

        result.Outcome.Should().Be(StatementBreakDispositionOutcome.Rejected);
        (await breakStore.GetAsync("break-cash-42"))!.Status.Should().Be("Open");
        (await caseStore.GetAsync("case:break-cash-42"))!.Status.Should().Be("Open");
        (await service.GetAuditHistoryAsync("break-cash-42")).Should().BeEmpty();
    }

    [Fact]
    public async Task DispositionAsync_CaseProjectionFailure_ShouldResumeAfterRestartWithoutDuplicateAudit()
    {
        var (breakStore, caseStore) = await SeedPairAsync();
        var failingCaseStore = new FailNextSaveCaseStore(caseStore);
        var firstService = CreateService(breakStore, failingCaseStore);
        var command = CreateCommand();

        var pending = await firstService.DispositionAsync(command);

        pending.Outcome.Should().Be(StatementBreakDispositionOutcome.RecoveryPending);
        (await breakStore.GetAsync(command.BreakId))!.Status.Should().Be("Resolved");
        (await caseStore.GetAsync($"case:{command.BreakId}"))!.Status.Should().Be("Open");

        var restartedBreakStore = new JsonReconciliationBreakStore(TestDataRoot);
        var restartedCaseStore = new JsonReconciliationCaseStore(TestDataRoot);
        var restartedService = CreateService(restartedBreakStore, restartedCaseStore);
        var resumedCount = await restartedService.ResumePendingAsync();
        var replay = await restartedService.DispositionAsync(command);

        resumedCount.Should().Be(1);
        replay.Outcome.Should().Be(StatementBreakDispositionOutcome.IdempotentReplay);
        (await restartedBreakStore.GetAsync(command.BreakId))!.Status.Should().Be("Resolved");
        var retainedCase = await restartedCaseStore.GetAsync($"case:{command.BreakId}");
        retainedCase!.Status.Should().Be("Resolved");
        retainedCase.Version.Should().Be(1);
        retainedCase.AuditEvents.Should().ContainSingle(item => item.TransactionId == pending.TransactionId);
        (await restartedService.GetAuditHistoryAsync(command.BreakId)).Should().ContainSingle();
    }

    private StatementBreakDispositionService CreateService(
        IReconciliationBreakStore breakStore,
        IReconciliationCaseStore caseStore)
        => new(
            breakStore,
            caseStore,
            new FileStatementBreakDispositionTransactionStore(TestDataRoot),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 21, 16, 30, 0, TimeSpan.Zero)));

    private async Task<(JsonReconciliationBreakStore BreakStore, JsonReconciliationCaseStore CaseStore)> SeedPairAsync(
        ReconciliationBreakRecord? breakRecord = null,
        ReconciliationCase? reconciliationCase = null)
    {
        var breakStore = new JsonReconciliationBreakStore(TestDataRoot);
        var caseStore = new JsonReconciliationCaseStore(TestDataRoot);
        await breakStore.WriteAsync([breakRecord ?? CreateBreak()]);
        await caseStore.SaveAsync(reconciliationCase ?? CreateCase());
        return (breakStore, caseStore);
    }

    private static StatementBreakDispositionCommand CreateCommand(
        long expectedVersion = 0,
        string commandId = "cmd-resolve-break-cash-42")
        => new(
            BreakId: "break-cash-42",
            ExpectedVersion: expectedVersion,
            CommandId: commandId,
            Disposition: ReconciliationBreakDispositionDto.Resolved,
            Actor: "fund-ops.alice",
            Rationale: "Custodian confirmed the corrected settlement cash amount.",
            EvidenceLinks: ["evidence://custodian/confirmation-42"]);

    private static ReconciliationBreakRecord CreateBreak()
        => new(
            BreakId: "break-cash-42",
            RunId: "statement-run-july",
            ImportId: "statement-import-july",
            SourceReference: "statement-import-july:42",
            BreakCode: "CASH_AMOUNT_MISMATCH",
            Category: "Cash",
            Delta: 1250.25m,
            Tolerance: 0.01m,
            ToleranceBreached: true,
            CreatedAtUtc,
            Status: "Open")
        {
            EvidenceLink = "evidence://statement/row-42"
        };

    private static ReconciliationCase CreateCase()
        => new(
            CaseId: "case:break-cash-42",
            ImportId: "statement-import-july",
            Status: "Open",
            Reason: "Custodian cash did not tie to the retained ledger balance.",
            Confidence: 0.25m,
            Rationale: "The external statement amount differs from the ledger.",
            CreatedAtUtc,
            History:
            [
                new ReconciliationCaseHistoryEntry(
                    CreatedAtUtc,
                    "None",
                    "Open",
                    "Case created from custodian statement break.")
                {
                    Actor = "system",
                    EvidenceId = "evidence://statement/row-42"
                }
            ])
        {
            BreakId = "break-cash-42",
            Owner = "fund-ops",
            LastUpdatedAtUtc = CreatedAtUtc,
            LastUpdatedBy = "system",
            Disposition = "NeedsInvestigation",
            EvidenceReferences = ["evidence://statement/row-42"]
        };

    private sealed class FailNextSaveCaseStore(IReconciliationCaseStore inner) : IReconciliationCaseStore
    {
        private int _failureRemaining = 1;

        public Task SaveAsync(ReconciliationCase reconciliationCase, CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _failureRemaining, 0) == 1)
            {
                throw new IOException("Injected case projection failure after the disposition commit.");
            }

            return inner.SaveAsync(reconciliationCase, ct);
        }

        public Task<ReconciliationCase?> GetAsync(string caseId, CancellationToken ct = default)
            => inner.GetAsync(caseId, ct);

        public Task<IReadOnlyList<ReconciliationCase>> ListAsync(CancellationToken ct = default)
            => inner.ListAsync(ct);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
