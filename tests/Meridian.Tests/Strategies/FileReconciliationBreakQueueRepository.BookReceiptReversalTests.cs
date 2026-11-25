using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Strategies;

public sealed class FileReconciliationBreakQueueBookReceiptReversalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-receipt-reversal-" + Guid.NewGuid().ToString("N"));
    private static readonly ReconciliationBreakQueueScope Scope = new("tenant-a", "company-a");
    private static readonly Guid BookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherBookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly CaseDate = new(2026, 10, 6);
    private static readonly DateTimeOffset DetectedAt = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ReconciliationBreakQueueStatus.Resolved)]
    [InlineData(ReconciliationBreakQueueStatus.SignedOff)]
    [InlineData(ReconciliationBreakQueueStatus.Dismissed)]
    public async Task Source_reversal_reopens_terminal_case_and_retains_owner_scope_and_audit(ReconciliationBreakQueueStatus status)
    {
        var repository = Repository();
        var before = await SeedAsync(repository, Item(status));
        var command = Command(before);

        var result = await repository.ApplyBookReceiptReversalAsync(Scope, command);

        result.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        result.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        var reopened = result.Item!;
        reopened.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
        reopened.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Reopened);
        reopened.Version.Should().Be(before.Version + 1);
        reopened.AssignedTo.Should().Be(before.AssignedTo);
        reopened.AssigneeId.Should().Be(before.AssigneeId);
        reopened.AssigneeDisplayName.Should().Be(before.AssigneeDisplayName);
        reopened.Team.Should().Be(before.Team);
        reopened.TenantId.Should().Be(Scope.TenantId);
        reopened.CompanyId.Should().Be(Scope.CompanyId);
        reopened.LedgerBookId.Should().Be(BookId);
        reopened.ResolvedBy.Should().BeNull();
        reopened.ResolvedAt.Should().BeNull();
        reopened.SignedOffBy.Should().BeNull();
        reopened.SignedOffAt.Should().BeNull();
        reopened.Disposition.Should().BeNull();
        reopened.ResolutionCode.Should().BeNull();
        reopened.EvidenceLinks.Should().Contain("evidence://receipts/original");
        reopened.EvidenceLinks.Should().Contain("evidence://receipts/reversal");
        reopened.BlockedOutputs.Should().Contain("PeriodClose");
        reopened.BlockedOutputs.Should().Contain("FinalReport");
        var restarted = Repository();
        (await restarted.GetByIdAsync(before.BreakId)).Should().BeEquivalentTo(reopened);
        var audit = (await restarted.GetAuditHistoryAsync(before.BreakId)).Should()
            .ContainSingle(entry => entry.EventType == "ReceiptReversalReopened").Subject;
        audit.CommandId.Should().Be(command.CommandId);
        audit.PreviousStatus.Should().Be(before.Status);
        audit.NewStatus.Should().Be(ReconciliationBreakQueueStatus.Open);
        audit.NewLifecycleState.Should().Be(ReconciliationCaseLifecycleState.Reopened);
        audit.BeforePayloadHash.Should().MatchRegex("^[0-9a-f]{64}$");
        audit.AfterPayloadHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("book")]
    [InlineData("source")]
    [InlineData("source-case")]
    public async Task A_reversal_requires_the_exact_case_scope_book_and_source(string mismatch)
    {
        var repository = Repository();
        var item = Item() with { SourceType = mismatch == "source" ? "Statement" : mismatch == "source-case" ? "bookreceipt" : "BookReceipt" };
        var before = await SeedAsync(repository, item);
        var scope = mismatch switch
        {
            "tenant" => new ReconciliationBreakQueueScope("tenant-b", Scope.CompanyId),
            "company" => new ReconciliationBreakQueueScope(Scope.TenantId, "company-b"),
            _ => Scope
        };
        var command = Command(before) with { LedgerBookId = mismatch == "book" ? OtherBookId : BookId };
        var auditCount = (await repository.GetAuditHistoryAsync(before.BreakId)).Count;

        var result = await repository.ApplyBookReceiptReversalAsync(scope, command);

        result.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.NotFound);
        result.Item.Should().BeNull();
        (await Repository().GetByIdAsync(before.BreakId)).Should().BeEquivalentTo(before);
        (await repository.GetAuditHistoryAsync(before.BreakId)).Should().HaveCount(auditCount);
    }

    [Theory]
    [InlineData("zero-version")]
    [InlineData("stale-version")]
    [InlineData("backdated")]
    [InlineData("missing-case-date")]
    [InlineData("missing-observation-date")]
    [InlineData("missing-evidence")]
    [InlineData("missing-reversal")]
    public async Task Missing_or_stale_observation_authority_cannot_reopen_a_case(string invalid)
    {
        var repository = Repository();
        var before = await SeedAsync(repository, Item() with { AsOfDate = invalid == "missing-case-date" ? null : CaseDate });
        var command = Command(before);
        command = invalid switch
        {
            "zero-version" => command with { ExpectedVersion = 0 },
            "stale-version" => command with { ExpectedVersion = before.Version + 1 },
            "backdated" => command with { AsOfDate = CaseDate.AddDays(-1) },
            "missing-observation-date" => command with { AsOfDate = default },
            "missing-evidence" => command with { EvidenceLinks = [] },
            "missing-reversal" => command with { ReversalIds = [] },
            _ => command
        };
        var auditCount = (await repository.GetAuditHistoryAsync(before.BreakId)).Count;

        var result = await repository.ApplyBookReceiptReversalAsync(Scope, command);

        result.Status.Should().NotBe(ReconciliationBreakQueueTransitionStatus.Success);
        result.ErrorCode.Should().Be(invalid == "stale-version"
            ? ReconciliationBreakQueueTransitionErrorCode.ConcurrencyConflict
            : ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        (await Repository().GetByIdAsync(before.BreakId)).Should().BeEquivalentTo(before);
        (await repository.GetAuditHistoryAsync(before.BreakId)).Should().HaveCount(auditCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_reversal_cannot_introduce_casework_control_markers(bool completed)
    {
        var repository = Repository();
        var before = await SeedAsync(repository, Item());
        var marker = completed ? StatementCaseworkHandoffObligation.CreateCompletedMarker("other-command")
            : StatementCaseworkHandoffObligation.CreatePendingMarker("other-command");
        var command = Command(before) with { EvidenceLinks = ["evidence://receipts/reversal", marker] };

        var result = await repository.ApplyBookReceiptReversalAsync(Scope, command);

        result.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        (await Repository().GetByIdAsync(before.BreakId)).Should().BeEquivalentTo(before);
        (await repository.GetAuditHistoryAsync(before.BreakId)).Should().NotContain(entry => entry.EventType == "ReceiptReversalReopened");
    }

    [Fact]
    public async Task An_active_case_cannot_be_reopened_as_a_new_source_mutation()
    {
        var repository = Repository();
        var before = await SeedAsync(repository, Item(ReconciliationBreakQueueStatus.Open));

        var result = await repository.ApplyBookReceiptReversalAsync(Scope, Command(before));

        result.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.ReopenNotAllowed);
        (await Repository().GetByIdAsync(before.BreakId)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Identical_reversal_retry_after_restart_returns_the_retained_result_without_another_mutation()
    {
        var repository = Repository();
        var before = await SeedAsync(repository, Item());
        var command = Command(before) with
        {
            ReversalIds = ["reversal-b", "reversal-a"],
            EvidenceLinks = ["evidence://receipts/b", "evidence://receipts/a"]
        };
        var first = await repository.ApplyBookReceiptReversalAsync(Scope, command);
        first.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        var restarted = Repository();

        var replay = await restarted.ApplyBookReceiptReversalAsync(Scope, command with
        {
            ReversalIds = ["reversal-a", "reversal-b", "reversal-a"],
            EvidenceLinks = ["evidence://receipts/a", "evidence://receipts/b", "evidence://receipts/a"]
        });

        replay.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        replay.Item.Should().BeEquivalentTo(first.Item);
        replay.Outcome.Should().BeEquivalentTo(first.Outcome);
        (await restarted.GetAuditHistoryAsync(before.BreakId)).Should()
            .ContainSingle(entry => entry.EventType == "ReceiptReversalReopened");
        (await restarted.GetByIdAsync(before.BreakId))!.Version.Should().Be(first.Item!.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_reversal_command_id_cannot_be_reused_for_different_retained_evidence(bool changeReversalIdentity)
    {
        var repository = Repository();
        var before = await SeedAsync(repository, Item());
        var command = Command(before);
        var first = await repository.ApplyBookReceiptReversalAsync(Scope, command);
        first.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        var changed = changeReversalIdentity ? command with { ReversalIds = ["different-reversal"] }
            : command with { EvidenceLinks = ["evidence://receipts/different-reversal"] };

        var result = await Repository().ApplyBookReceiptReversalAsync(Scope, changed);

        result.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Conflict);
        result.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.CommandIdConflict);
        (await Repository().GetByIdAsync(before.BreakId))!.Version.Should().Be(first.Item!.Version);
        (await Repository().GetAuditHistoryAsync(before.BreakId)).Should()
            .ContainSingle(entry => entry.EventType == "ReceiptReversalReopened");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retained_close_freeze_blocks_reversal_even_without_case_fund_or_period_binding(bool sealedHardClosed)
    {
        var repository = Repository();
        var before = await SeedAsync(repository, Item());
        before.FundProfileId.Should().BeNull();
        before.AccountingPeriodId.Should().BeNull();
        var closeScope = new ReconciliationCloseScope("fund-a", BookId,
            Guid.Parse("33333333-3333-3333-3333-333333333333"), CaseDate.AddDays(5));
        await using (var lease = await repository.AcquireCloseScopeLeaseAsync(closeScope))
        {
            if (sealedHardClosed)
                await lease.CommitHardCloseAsync();
            // An unsealed disposed lease models owner loss and retains the durable Closing freeze.
        }

        var result = await Repository().ApplyBookReceiptReversalAsync(Scope,
            Command(before) with { AsOfDate = CaseDate.AddDays(10) });

        result.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.AccountingPeriodHardClosed);
        (await Repository().GetByIdAsync(before.BreakId)).Should().BeEquivalentTo(before);
        (await Repository().GetAuditHistoryAsync(before.BreakId)).Should()
            .NotContain(entry => entry.EventType == "ReceiptReversalReopened");
    }

    [Fact]
    public async Task A_freeze_for_another_book_does_not_block_the_owned_receipt_case()
    {
        var repository = Repository();
        var before = await SeedAsync(repository, Item());
        var closeScope = new ReconciliationCloseScope("fund-a", OtherBookId,
            Guid.Parse("33333333-3333-3333-3333-333333333333"), CaseDate.AddDays(5));
        await using (var lease = await repository.AcquireCloseScopeLeaseAsync(closeScope))
            await lease.CommitHardCloseAsync();

        var result = await Repository().ApplyBookReceiptReversalAsync(Scope, Command(before));

        result.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        result.Item!.LedgerBookId.Should().Be(BookId);
    }

    [Fact]
    public async Task Dedicated_source_reversal_does_not_allow_generic_automated_privileged_reopen()
    {
        var repository = Repository();
        var before = await SeedAsync(repository, Item(ReconciliationBreakQueueStatus.SignedOff));
        var command = new ReconciliationCaseworkCommand(before.BreakId, ReconciliationCaseworkAction.Reopen,
            "automation", "generic-reopen", "generic-reopen", "book-receipts-v1", before.Version,
            Reason: "A receipt was reversed.", EvidenceLinks: ["evidence://receipts/reversal"],
            Privileged: true, ActionOrigin: OperationsActionOriginDto.AutomationAssistant);

        var result = await repository.ApplyCaseworkCommandAsync(Scope, command);

        result.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MaterialActionRequiresHumanOperator);
        (await Repository().GetByIdAsync(before.BreakId))!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.SignedOff);
        (await Repository().GetAuditHistoryAsync(before.BreakId)).Should()
            .NotContain(entry => entry.EventType == "ReceiptReversalReopened");
    }

    private FileReconciliationBreakQueueRepository Repository()
        => new(_root, NullLogger<FileReconciliationBreakQueueRepository>.Instance);

    private static async Task<ReconciliationBreakQueueItem> SeedAsync(
        FileReconciliationBreakQueueRepository repository, ReconciliationBreakQueueItem item)
    {
        (await repository.CreateIfMissingAsync(item)).Should().BeTrue();
        var retained = (await repository.GetByIdAsync(item.BreakId))!;
        retained.Version.Should().BeGreaterThan(0);
        return retained;
    }

    private static BookReceiptReversalCommand Command(ReconciliationBreakQueueItem item)
        => new(item.BreakId, BookId, "source-reversal-command", item.Version,
            ["receipt-reversal-1"], ["evidence://receipts/reversal"], CaseDate.AddDays(1));

    private static ReconciliationBreakQueueItem Item(ReconciliationBreakQueueStatus status = ReconciliationBreakQueueStatus.Resolved)
        => new("receipt:coupon-1", "book-receipts:2026-10-06", "Treasury coupon", ReconciliationBreakCategory.CashMismatch,
            status, 0m, "Cash and ledger previously reconciled.", "owner-a", DetectedAt, DetectedAt,
            ResolvedBy: "controller", ResolvedAt: DetectedAt, ResolutionNote: "Cash receipt verified.",
            SignoffStatus: "signed-off", LifecycleState: status switch
            {
                ReconciliationBreakQueueStatus.Open => ReconciliationCaseLifecycleState.Open,
                ReconciliationBreakQueueStatus.SignedOff => ReconciliationCaseLifecycleState.SignedOff,
                ReconciliationBreakQueueStatus.Dismissed => ReconciliationCaseLifecycleState.Superseded,
                _ => ReconciliationCaseLifecycleState.Resolved
            },
            Team: "cash-operations", AssigneeId: "owner-a", AssigneeDisplayName: "Cash Owner",
            ResolutionCode: "LedgerAdjusted", SignedOffBy: "independent-controller", SignedOffAt: DetectedAt,
            EvidenceLinks: ["evidence://receipts/original"], SourceType: "BookReceipt", SourceSystem: "book-receipts-v1",
            SourceReference: "coupon-1", LedgerBookId: BookId,
            Disposition: status == ReconciliationBreakQueueStatus.Dismissed
                ? ReconciliationBreakDispositionDto.Superseded : ReconciliationBreakDispositionDto.Resolved,
            DispositionReason: "Prior receipt completed the obligation.", DisposedAt: DetectedAt, AsOfDate: CaseDate)
        { TenantId = Scope.TenantId, CompanyId = Scope.CompanyId };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
