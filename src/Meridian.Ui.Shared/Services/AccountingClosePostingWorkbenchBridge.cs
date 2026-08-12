using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Reporting;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Projects the Financial Operations close gate onto the shared manual-journal workbench.
/// It may create drafts or governed reversal drafts, but approval and posting stay human actions.
/// </summary>
public sealed class AccountingClosePostingWorkbenchBridge :
    IAccountingClosePostingWorkbench,
    IAccountingCloseMutationGate
{
    private const string GateLabel = "Post closing entries";
    private readonly AutomatedJournalIntakeRunner _runner;
    private readonly IManualJournalEntryWorkbenchService _workbench;
    private readonly IManualJournalEntryLifecycleService _lifecycle;
    // Optional: the durable ledger book service is only registered when a persistence-backed
    // ledger (Postgres) is configured. Without it the close-posting gate degrades to Blocked
    // rather than failing composition, matching the workstation's "durable ledger optional" pattern.
    private readonly ILedgerBookService? _ledgerBookService;
    private readonly ReportingReconciliationEvidenceRetentionService? _reportingEvidenceRetention;
    private readonly IFundProfileTenancyRegistry? _tenancyRegistry;
    private readonly IReconciliationBreakQueueRepository? _breakQueue;
    private readonly IOperationsContinuityWorkflowService? _operationsWorkflowService;
    private readonly IReportingReleaseConsistencyGate? _releaseConsistencyGate;

    public AccountingClosePostingWorkbenchBridge(
        AutomatedJournalIntakeRunner runner,
        IManualJournalEntryWorkbenchService workbench,
        IManualJournalEntryLifecycleService lifecycle,
        ILedgerBookService? ledgerBookService,
        ReportingReconciliationEvidenceRetentionService? reportingEvidenceRetention = null,
        IFundProfileTenancyRegistry? tenancyRegistry = null,
        IReconciliationBreakQueueRepository? breakQueue = null,
        IReportingReleaseConsistencyGate? releaseConsistencyGate = null)
        : this(
            runner,
            workbench,
            lifecycle,
            ledgerBookService,
            reportingEvidenceRetention,
            tenancyRegistry,
            breakQueue,
            operationsWorkflowService: null,
            releaseConsistencyGate: releaseConsistencyGate)
    {
    }

    public AccountingClosePostingWorkbenchBridge(
        AutomatedJournalIntakeRunner runner,
        IManualJournalEntryWorkbenchService workbench,
        IManualJournalEntryLifecycleService lifecycle,
        ILedgerBookService? ledgerBookService,
        ReportingReconciliationEvidenceRetentionService? reportingEvidenceRetention,
        IFundProfileTenancyRegistry? tenancyRegistry,
        IReconciliationBreakQueueRepository? breakQueue,
        IOperationsContinuityWorkflowService? operationsWorkflowService,
        IReportingReleaseConsistencyGate? releaseConsistencyGate = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _workbench = workbench ?? throw new ArgumentNullException(nameof(workbench));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _ledgerBookService = ledgerBookService;
        _reportingEvidenceRetention = reportingEvidenceRetention;
        _tenancyRegistry = tenancyRegistry;
        _breakQueue = breakQueue;
        _operationsWorkflowService = operationsWorkflowService;
        _releaseConsistencyGate = releaseConsistencyGate;
    }

    internal IReconciliationBreakQueueRepository? BreakQueueAuthority => _breakQueue;

    private const string LedgerUnavailableDetail =
        "The durable ledger book service is not configured; period-close posting requires a persistence-backed ledger.";

    private ILedgerBookService RequireLedgerBookService()
        => _ledgerBookService ?? throw new InvalidOperationException(LedgerUnavailableDetail);

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        AccountingClosePostingContext context,
        CancellationToken ct = default)
    {
        ValidateContext(context);
        var scope = await ResolveScopeAsync(context, ct).ConfigureAwait(false);
        var releaseConsistencyGate = _releaseConsistencyGate
            ?? throw new InvalidOperationException(
                "The durable reporting release/close consistency authority is unavailable, so the governed ledger-period mutation is blocked.");
        return await releaseConsistencyGate
            .AcquireAsync(scope.Period.PeriodId.ToString("D"), ct)
            .ConfigureAwait(false);
    }

    public async Task<ClosePostingGateDto> EvaluateAsync(
        AccountingClosePostingContext context,
        CancellationToken ct = default)
    {
        ValidateContext(context);
        // Without a persistence-backed ledger the gate cannot resolve periods; degrade the read path
        // to Blocked explicitly rather than surfacing an exception through the catch below.
        if (_ledgerBookService is null)
        {
            return Blocked(context, LedgerUnavailableDetail);
        }

        try
        {
            var scope = await ResolveScopeAsync(context, ct).ConfigureAwait(false);
            var preview = await _runner
                .PreviewPeriodCloseAsync(ToIntakeRequest(context, scope, "close-gate-preview"), ct)
                .ConfigureAwait(false);
            var workbench = await _workbench
                .GetWorkbenchAsync(
                    scope.FundProfileId,
                    context.LedgerBookId,
                    ct,
                    context.TenantId,
                    context.CompanyId)
                .ConfigureAwait(false);
            return BuildGate(context, scope.Period.PeriodId, preview, workbench.Drafts);
        }
        catch (InvalidOperationException ex)
        {
            return Blocked(context, ex.Message);
        }
    }

    public async Task<ClosePostingGateDto> EnsureClosingDraftQueuedAsync(
        AccountingClosePostingContext context,
        AccountingClosePostingCommand command,
        CancellationToken ct = default)
    {
        ValidateContext(context);
        ValidateHumanCommand(command, requireController: false);

        var before = await EvaluateAsync(context, ct).ConfigureAwait(false);
        if (before.IsReadyForLock || before.State is ClosePostingGateStateDto.DraftQueued
            or ClosePostingGateStateDto.Submitted or ClosePostingGateStateDto.Approved)
        {
            return before;
        }

        if (before.State == ClosePostingGateStateDto.Blocked)
        {
            return before;
        }

        var scope = await ResolveScopeAsync(context, ct).ConfigureAwait(false);
        if (scope.Period.Status != LedgerPeriodStatusDto.SoftClosed)
        {
            throw new InvalidOperationException(
                $"Ledger period '{scope.Period.Label}' must be soft-closed before a closing-entry draft can be queued.");
        }

        await _runner.RunPeriodCloseIntakeAsync(
                ToIntakeRequest(context, scope, command.Actor),
                ct)
            .ConfigureAwait(false);
        return await EvaluateAsync(context, ct).ConfigureAwait(false);
    }

    public async Task<LedgerPeriodDto> FinalizeHardCloseAsync(
        AccountingClosePostingContext context,
        AccountingClosePostingCommand command,
        CancellationToken ct = default)
    {
        ValidateContext(context);
        ValidateHumanCommand(command, requireController: true);
        await using var consistencyLease = await AcquireConsistencyLeaseIfRequiredAsync(
                context,
                command,
                ct)
            .ConfigureAwait(false);
        var scope = await ResolveScopeAsync(context, ct).ConfigureAwait(false);
        var period = scope.Period;
        if (period.Status == LedgerPeriodStatusDto.HardClosed)
        {
            EnsureHardCloseEvidenceServices();
            var recoveredCheckpoint = await _breakQueue!
                .RecoverHardClosedScopeCheckpointAsync(
                    new ReconciliationCloseScope(
                        scope.FundProfileId,
                        context.LedgerBookId,
                        period.PeriodId,
                        period.EndDate),
                    ct)
                .ConfigureAwait(false);
            await CompleteHardCloseHandoffAsync(
                    context,
                    scope.FundProfileId,
                    period,
                    command,
                    recoveredCheckpoint,
                    ct)
                .ConfigureAwait(false);
            return period;
        }

        if (period.Status != LedgerPeriodStatusDto.SoftClosed)
        {
            throw new InvalidOperationException(
                $"Ledger period '{period.Label}' must be soft-closed before the governed hard-close gate can finalize it.");
        }

        // Re-evaluate at the mutation boundary. The management service normally evaluates the
        // gate first, but a direct caller or concurrent adjustment/reversal must not bypass the
        // governed approval state merely because temporary-account balances happen to be zero.
        var gate = await EvaluateAsync(context, ct).ConfigureAwait(false);
        if (!gate.IsReadyForLock)
        {
            throw new InvalidOperationException(
                $"Ledger period '{period.Label}' cannot be hard-closed while the closing-entry gate is {gate.State}: {gate.Detail}");
        }

        EnsureHardCloseEvidenceServices();
        var breakQueue = _breakQueue
            ?? throw new InvalidOperationException(
                "The canonical reconciliation queue is unavailable, so the hard-close scope cannot be frozen.");
        LedgerPeriodDto closedPeriod;
        LedgerPeriodSummaryDto? closedSummary = null;
        ReconciliationCloseScopeCheckpoint closeCheckpoint;
        await using (var closeScopeLease = await breakQueue
                         .AcquireCloseScopeLeaseAsync(
                             new ReconciliationCloseScope(
                                 scope.FundProfileId,
                                 context.LedgerBookId,
                                 period.PeriodId,
                                 period.EndDate),
                             ct)
                         .ConfigureAwait(false))
        {
            closeCheckpoint = new ReconciliationCloseScopeCheckpoint(
                closeScopeLease.Scope,
                closeScopeLease.Items.ToArray(),
                closeScopeLease.CheckpointHashSha256,
                closeScopeLease.Generation);

            // The durable queue lease fences reconciliation mutations, but the ledger remains the
            // authority for whether a prior owner committed before it died. Re-read that state
            // after acquiring (or reclaiming) the lease so a stale pre-lock SoftClosed read cannot
            // erase a checkpoint for a close that actually committed.
            ResolvedPostingScope boundaryScope;
            try
            {
                boundaryScope = await ResolveScopeAsync(context, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"The reconciliation close checkpoint is retained because ledger period '{period.Label}' could not be re-read at the hard-close boundary. Retry after ledger authority is available.",
                    exception);
            }

            if (boundaryScope.Period.Status == LedgerPeriodStatusDto.HardClosed)
            {
                closedPeriod = boundaryScope.Period;
            }
            else
            {
                if (boundaryScope.Period.Status != LedgerPeriodStatusDto.SoftClosed)
                {
                    await closeScopeLease
                        .AbandonBeforeLedgerCommitAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    throw new InvalidOperationException(
                        $"Ledger period '{boundaryScope.Period.Label}' changed to {boundaryScope.Period.Status} before hard close. The uncommitted reconciliation freeze was released; re-evaluate close readiness.");
                }

                try
                {
                    var boundaryGate = await EvaluateAsync(context, ct).ConfigureAwait(false);
                    if (!boundaryGate.IsReadyForLock)
                    {
                        throw new InvalidOperationException(
                            $"Ledger period '{boundaryScope.Period.Label}' cannot be hard-closed while the closing-entry gate is {boundaryGate.State}: {boundaryGate.Detail}");
                    }

                    var preCloseSummary = await RequireLedgerBookService()
                        .GetPeriodSummaryAsync(boundaryScope.Period.PeriodId, ct)
                        .ConfigureAwait(false)
                        ?? throw new InvalidOperationException(
                            $"Ledger period '{boundaryScope.Period.PeriodId:D}' has no retained close summary.");
                    var preCloseBreakEvidence = BuildExactReportingBreakEvidence(
                        closeCheckpoint.Items,
                        scope.FundProfileId,
                        context.LedgerBookId,
                        boundaryScope.Period.PeriodId,
                        boundaryScope.Period.EndDate,
                        preCloseSummary.OpenBreakCount);
                    EnsureNoOpenReportingBreaks(preCloseBreakEvidence, boundaryScope.Period);
                }
                catch
                {
                    await closeScopeLease
                        .AbandonBeforeLedgerCommitAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    throw;
                }

                try
                {
                    var closed = await RequireLedgerBookService().ClosePeriodAsync(
                            boundaryScope.Period.PeriodId,
                            new CloseLedgerPeriodRequest(
                                LedgerPeriodCloseKindDto.HardClose,
                                command.Actor,
                                command.Reason,
                                RequiredSignoffRole: command.Role ?? "Fund Controller",
                                ActionOrigin: command.ActionOrigin),
                            ct)
                        .ConfigureAwait(false);
                    closedPeriod = closed.Period;
                    closedSummary = closed.Summary;
                }
                catch (Exception closeException)
                {
                    ResolvedPostingScope observedScope;
                    try
                    {
                        observedScope = await ResolveScopeAsync(context, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception verificationException)
                    {
                        throw new ReportingCloseEvidenceHandoffException(
                            boundaryScope.Period,
                            $"hard-close-{boundaryScope.Period.PeriodId:N}-v{boundaryScope.Period.Version.ToString(CultureInfo.InvariantCulture)}",
                            $"The ledger hard-close call failed and its durable outcome could not be verified. The exact reconciliation checkpoint remains frozen for recovery: {verificationException.Message}",
                            new AggregateException(closeException, verificationException));
                    }

                    if (observedScope.Period.Status != LedgerPeriodStatusDto.HardClosed)
                    {
                        await closeScopeLease
                            .AbandonBeforeLedgerCommitAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                        throw;
                    }

                    // The command reported failure after the ledger postcondition committed.
                    // Preserve that verified result and converge the exact frozen checkpoint.
                    closedPeriod = observedScope.Period;
                }
            }

            try
            {
                // Once the authoritative ledger is hard-closed, checkpoint sealing is a required
                // recovery action and is intentionally cancellation-insensitive.
                await closeScopeLease.CommitHardCloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                var completionId =
                    $"hard-close-{closedPeriod.PeriodId:N}-v{closedPeriod.Version.ToString(CultureInfo.InvariantCulture)}";
                throw new ReportingCloseEvidenceHandoffException(
                    closedPeriod,
                    completionId,
                    $"Ledger hard close is committed, but the reconciliation close-scope checkpoint could not be sealed. The durable in-progress freeze remains blocking and must be recovered idempotently: {exception.Message}",
                    exception);
            }
        }

        await CompleteHardCloseHandoffAsync(
                context,
                scope.FundProfileId,
                closedPeriod,
                command,
                closeCheckpoint,
                ct,
                closedSummary)
            .ConfigureAwait(false);
        return closedPeriod;
    }

    private async Task CompleteHardCloseHandoffAsync(
        AccountingClosePostingContext context,
        string fundProfileId,
        LedgerPeriodDto period,
        AccountingClosePostingCommand command,
        ReconciliationCloseScopeCheckpoint closeCheckpoint,
        CancellationToken ct,
        LedgerPeriodSummaryDto? summary = null)
    {
        var completionId = $"hard-close-{period.PeriodId:N}-v{period.Version.ToString(CultureInfo.InvariantCulture)}";
        try
        {
            await LockPostedClosingBatchesAfterHardCloseAsync(
                    context,
                    fundProfileId,
                    period,
                    command,
                    ct)
                .ConfigureAwait(false);
            var workflowCompletion = await ResolveCommittedCloseWorkflowEvidenceAsync(
                    context,
                    period,
                    ct)
                .ConfigureAwait(false);
            if (_operationsWorkflowService is not null && workflowCompletion is null)
            {
                // The ledger hard close is durable, but the Operations Continuity close transition
                // has not committed yet. Do not mint certifiable reporting evidence. The close
                // management service will commit the workflow and retry this idempotent handoff.
                return;
            }
            await RetainHardCloseReportingEvidenceAsync(
                    context,
                    fundProfileId,
                    period,
                    closeCheckpoint,
                    ct,
                    summary,
                    workflowCompletion)
                .ConfigureAwait(false);
        }
        catch (ReportingCloseEvidenceHandoffException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReportingCloseEvidenceHandoffException(
                period,
                completionId,
                $"Ledger hard close is committed, but close-lock/reporting-evidence handoff is pending and must be retried idempotently: {exception.Message}",
                exception);
        }
    }

    private void EnsureHardCloseEvidenceServices()
    {
        if (_reportingEvidenceRetention is null || _tenancyRegistry is null || _breakQueue is null)
        {
            throw new InvalidOperationException(
                "Hard close is blocked because reporting evidence retention, authoritative tenancy, or the canonical reconciliation queue is unavailable. Repair service composition and retry before committing the close.");
        }
    }

    private async Task RetainHardCloseReportingEvidenceAsync(
        AccountingClosePostingContext context,
        string fundProfileId,
        LedgerPeriodDto period,
        ReconciliationCloseScopeCheckpoint closeCheckpoint,
        CancellationToken ct,
        LedgerPeriodSummaryDto? summary = null,
        ReportingCloseWorkflowCompletionEvidence? workflowCompletion = null)
    {
        if (period.Status != LedgerPeriodStatusDto.HardClosed)
        {
            throw new InvalidOperationException(
                $"Ledger period '{period.Label}' is not hard-closed and cannot produce final-reporting reconciliation evidence.");
        }
        ValidateCloseCheckpoint(
            closeCheckpoint,
            fundProfileId,
            context.LedgerBookId,
            period);

        var completionId = $"hard-close-{period.PeriodId:N}-v{period.Version.ToString(CultureInfo.InvariantCulture)}";
        try
        {
            var retention = _reportingEvidenceRetention
                ?? throw new InvalidOperationException(
                    "Reporting evidence retention is not configured.");
            var tenancy = _tenancyRegistry
                ?? throw new InvalidOperationException(
                    "The authoritative fund tenancy registry is not configured.");
            var ownership = await tenancy.ResolveAsync(fundProfileId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Fund profile '{fundProfileId}' has no authoritative tenant/company binding.");
            if (string.IsNullOrWhiteSpace(ownership.TenantId)
                || string.IsNullOrWhiteSpace(ownership.CompanyId))
            {
                throw new InvalidOperationException(
                    $"Fund profile '{fundProfileId}' has no complete tenant/company binding.");
            }

            summary ??= await RequireLedgerBookService()
                .GetPeriodSummaryAsync(period.PeriodId, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Hard-closed period '{period.PeriodId:D}' has no retained close summary.");
            var completedAtUtc = (period.ClosedAt ?? summary.CompletedAt).ToUniversalTime();
            var evidenceIds = ImmutableArray.Create(
                $"ledger-period:{period.PeriodId:D}:version:{period.Version.ToString(CultureInfo.InvariantCulture)}:hard-closed",
                $"trial-balance:{summary.TotalDebits.ToString("G29", CultureInfo.InvariantCulture)}:{summary.TotalCredits.ToString("G29", CultureInfo.InvariantCulture)}",
                $"reconciliation-breaks:{summary.OpenBreakCount.ToString(CultureInfo.InvariantCulture)}",
                $"close-signoff:{summary.SignoffStatus}",
                $"reconciliation-close-checkpoint:{closeCheckpoint.CheckpointHashSha256}",
                $"reconciliation-close-checkpoint-generation:{closeCheckpoint.Generation.ToString(CultureInfo.InvariantCulture)}");
            var breakEvidence = BuildExactReportingBreakEvidence(
                closeCheckpoint.Items,
                fundProfileId,
                context.LedgerBookId,
                period.PeriodId,
                period.EndDate,
                summary.OpenBreakCount);
            EnsureNoOpenReportingBreaks(breakEvidence, period);
            var completionHash = ComputeHardCloseCompletionHash(
                context,
                fundProfileId,
                period,
                summary,
                completedAtUtc,
                evidenceIds,
                breakEvidence,
                workflowCompletion);
            var parameters = new ReportingRunParametersDto(
                new ReportingRunScopeDto(fundProfileId),
                period.PeriodId.ToString("D"),
                period.EndDate,
                new ReportingLedgerBookSelectionDto(context.LedgerBookId),
                MapReportingBasis(period.AccountingBasis),
                context.Currency.Trim().ToUpperInvariant(),
                ReportingConsolidationLevelDto.Fund,
                ReportingOutputFormatDto.EvidenceVault,
                ReportingFinalityDto.Final,
                IncludeSupportingSchedules: true,
                IncludeEvidenceAppendix: true);
            var access = new ReportAccessQueryContext(
                ActorPrincipalId: "reporting-close-evidence-retention",
                GroupPrincipalIds: [],
                CompanyId: ownership.CompanyId.Trim(),
                HasGlobalOverride: false,
                TenantId: ownership.TenantId.Trim(),
                RequireBoundScope: true);
            await retention.RetainCompletionAsync(
                    parameters,
                    access,
                    new ReportingReconciliationCompletionEvidence(
                        completionId,
                        completionHash,
                        completedAtUtc,
                        HasOpenBreaks: false,
                        evidenceIds,
                        breakEvidence,
                        workflowCompletion),
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not ReportingCloseEvidenceHandoffException)
        {
            throw new ReportingCloseEvidenceHandoffException(
                period,
                completionId,
                $"Ledger hard close is committed, but final-reporting evidence retention is pending and must be retried idempotently: {exception.Message}",
                exception);
        }
    }

    private async Task<ReportingCloseWorkflowCompletionEvidence?> ResolveCommittedCloseWorkflowEvidenceAsync(
        AccountingClosePostingContext context,
        LedgerPeriodDto period,
        CancellationToken ct)
    {
        if (_operationsWorkflowService is null)
        {
            // Compatibility hosts can still retain a non-certifiable hard-close receipt. Production
            // composition supplies the workflow authority, and final certification rejects receipts
            // without the committed workflow envelope.
            return null;
        }

        var workflow = await _operationsWorkflowService
            .GetAsync(context.WorkflowId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Accounting-close workflow '{context.WorkflowId:D}' was not found while retaining final close evidence.");
        if (workflow.FundAccountId != context.FundAccountId
            || workflow.LedgerBookId != context.LedgerBookId
            || !string.Equals(workflow.PeriodId, context.PeriodId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Accounting-close workflow '{context.WorkflowId:D}' does not match the exact fund-account, ledger-book, and period close scope.");
        }

        if (workflow.Status != OperationsWorkflowStatusDto.Closed)
        {
            return null;
        }
        if (workflow.ApprovalState != OperationsApprovalStateDto.Approved)
        {
            throw new InvalidOperationException(
                $"Closed accounting workflow '{workflow.WorkflowId:D}' has no committed Approved workflow state.");
        }

        var approval = workflow.Approvals
            .Where(static item => item.Status == OperationsApprovalStateDto.Approved)
            .OrderBy(static item => item.DecidedAtUtc)
            .ThenBy(static item => item.ApprovalId, StringComparer.Ordinal)
            .LastOrDefault()
            ?? throw new InvalidOperationException(
                $"Closed accounting workflow '{workflow.WorkflowId:D}' has no retained approval decision.");
        if (approval.EvidenceLinks.Count == 0 || approval.DecidedAtUtc is null)
        {
            throw new InvalidOperationException(
                $"Closed accounting workflow '{workflow.WorkflowId:D}' has incomplete retained approval evidence.");
        }

        var closePackage = workflow.ClosePackage
            ?? throw new InvalidOperationException(
                $"Closed accounting workflow '{workflow.WorkflowId:D}' has no retained close-support package.");
        if (closePackage.ChecklistControlApprovals.Count == 0)
        {
            throw new InvalidOperationException(
                $"Closed accounting workflow '{workflow.WorkflowId:D}' has no retained checklist control approvals.");
        }
        if (!ReportingReconciliationEvidenceValidation.IsLowercaseSha256(closePackage.EvidenceHash))
        {
            throw new InvalidOperationException(
                $"Closed accounting workflow '{workflow.WorkflowId:D}' has an invalid close-support package evidence hash.");
        }

        var timeline = await _operationsWorkflowService
            .GetTimelineAsync(workflow.WorkflowId, ct)
            .ConfigureAwait(false);
        var closeAudit = timeline
            .Where(static item =>
                string.Equals(item.EventType, "workflow-closed", StringComparison.Ordinal)
                && item.ToState == OperationsWorkflowStatusDto.Closed)
            .OrderBy(static item => item.OccurredAtUtc)
            .ThenBy(static item => item.AuditId)
            .LastOrDefault()
            ?? throw new InvalidOperationException(
                $"Closed accounting workflow '{workflow.WorkflowId:D}' has no retained workflow-closed audit event.");
        if (closeAudit.WorkflowId != workflow.WorkflowId
            || closeAudit.FundAccountId != workflow.FundAccountId
            || !string.Equals(closeAudit.PeriodId, workflow.PeriodId, StringComparison.OrdinalIgnoreCase)
            || !ReportingReconciliationEvidenceValidation.IsLowercaseSha256(closeAudit.CurrentHash))
        {
            throw new InvalidOperationException(
                $"Closed accounting workflow '{workflow.WorkflowId:D}' has invalid or cross-scope close audit evidence.");
        }

        return new ReportingCloseWorkflowCompletionEvidence(
            workflow.WorkflowId.ToString("D"),
            workflow.Version,
            workflow.FundAccountId.ToString("D"),
            context.LedgerBookId.ToString("D"),
            period.PeriodId.ToString("D"),
            approval.ApprovalId.Trim(),
            ComputeApprovalEvidenceHash(approval),
            ComputeChecklistEvidenceHash(workflow.CloseChecklist, closePackage.ChecklistControlApprovals),
            closePackage.ClosePackageId.Trim(),
            closePackage.EvidenceHash,
            closeAudit.AuditId.ToString("D"),
            closeAudit.CurrentHash);
    }

    private static string ComputeApprovalEvidenceHash(OperationsApprovalDto approval)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("approvalId", approval.ApprovalId.Trim());
            writer.WriteString("status", approval.Status.ToString());
            writer.WriteString("operator", approval.Operator?.Trim());
            writer.WriteString("reviewer", approval.Reviewer?.Trim());
            writer.WriteString("rationale", approval.Rationale?.Trim());
            writer.WriteString("submittedAtUtc", approval.SubmittedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("decidedAtUtc", approval.DecidedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            WriteEvidenceLinks(writer, "evidenceLinks", approval.EvidenceLinks);
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static string ComputeChecklistEvidenceHash(
        IReadOnlyList<OperationsCloseChecklistTaskDto> checklist,
        IReadOnlyList<OperationsChecklistControlApprovalDto> approvals)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("tasks");
            foreach (var task in checklist.OrderBy(static item => item.TaskId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("taskId", task.TaskId.Trim());
                writer.WriteString("gate", task.Gate.ToString());
                writer.WriteNumber("requiredApprovalCount", task.RequiredApprovalCount);
                writer.WriteString("status", task.Status.Trim());
                writer.WriteString("blockingReason", task.BlockingReason?.Trim());
                writer.WriteString("evidencePointer", task.EvidencePointer?.Trim());
                writer.WriteString("acknowledgedBy", task.AcknowledgedBy?.Trim());
                writer.WriteString("acknowledgedAtUtc", task.AcknowledgedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("controlApprovals");
            foreach (var approval in approvals
                         .OrderBy(static item => item.TaskId, StringComparer.Ordinal)
                         .ThenBy(static item => item.ApprovedBy, StringComparer.Ordinal)
                         .ThenBy(static item => item.ApprovedAtUtc))
            {
                writer.WriteStartObject();
                writer.WriteString("taskId", approval.TaskId.Trim());
                writer.WriteString("approvedBy", approval.ApprovedBy.Trim());
                writer.WriteString("approvedAtUtc", approval.ApprovedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteEvidenceLinks(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<OperationsEvidenceLinkDto> links)
    {
        writer.WriteStartArray(propertyName);
        foreach (var link in links
                     .OrderBy(static item => item.EvidenceId, StringComparer.Ordinal)
                     .ThenBy(static item => item.Label, StringComparer.Ordinal)
                     .ThenBy(static item => item.Route, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("evidenceId", link.EvidenceId.Trim());
            writer.WriteString("label", link.Label.Trim());
            writer.WriteString("route", link.Route?.Trim());
            writer.WriteString("source", link.Source?.Trim());
            writer.WriteString("capturedAtUtc", link.CapturedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static string ComputeHardCloseCompletionHash(
        AccountingClosePostingContext context,
        string fundProfileId,
        LedgerPeriodDto period,
        LedgerPeriodSummaryDto summary,
        DateTimeOffset completedAtUtc,
        ImmutableArray<string> evidenceIds)
        => ComputeHardCloseCompletionHash(
            context,
            fundProfileId,
            period,
            summary,
            completedAtUtc,
            evidenceIds,
            ImmutableArray<ReportingReconciliationBreakEvidence>.Empty,
            workflowCompletion: null);

    private static string ComputeHardCloseCompletionHash(
        AccountingClosePostingContext context,
        string fundProfileId,
        LedgerPeriodDto period,
        LedgerPeriodSummaryDto summary,
        DateTimeOffset completedAtUtc,
        ImmutableArray<string> evidenceIds,
        ImmutableArray<ReportingReconciliationBreakEvidence> breakEvidence,
        ReportingCloseWorkflowCompletionEvidence? workflowCompletion)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("workflowId", context.WorkflowId);
            writer.WriteString("fundProfileId", fundProfileId.Trim());
            writer.WriteString("ledgerBookId", period.LedgerBookId);
            writer.WriteString("periodId", period.PeriodId);
            writer.WriteNumber("periodVersion", period.Version);
            writer.WriteString("accountingBasis", period.AccountingBasis.ToString());
            writer.WriteString("completedAtUtc", completedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("totalDebits", summary.TotalDebits.ToString("G29", CultureInfo.InvariantCulture));
            writer.WriteString("totalCredits", summary.TotalCredits.ToString("G29", CultureInfo.InvariantCulture));
            writer.WriteNumber("openBreakCount", summary.OpenBreakCount);
            writer.WriteString("signoffStatus", summary.SignoffStatus.ToString());
            writer.WriteStartArray("evidenceIds");
            foreach (var evidenceId in evidenceIds.OrderBy(static value => value, StringComparer.Ordinal))
            {
                writer.WriteStringValue(evidenceId);
            }
            writer.WriteEndArray();
            writer.WriteStartArray("breakEvidence");
            foreach (var item in breakEvidence.OrderBy(static item => item.BreakId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("breakId", item.BreakId);
                writer.WriteString("evidenceHashSha256", item.EvidenceHashSha256);
                writer.WriteString("disposition", item.Disposition?.ToString());
                writer.WriteString("supersedingBreakId", item.SupersedingBreakId);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            if (workflowCompletion is not null)
            {
                writer.WriteStartObject("closeWorkflowCompletion");
                writer.WriteString("workflowId", workflowCompletion.WorkflowId);
                writer.WriteNumber("workflowVersion", workflowCompletion.WorkflowVersion);
                writer.WriteString("fundAccountId", workflowCompletion.FundAccountId);
                writer.WriteString("ledgerBookId", workflowCompletion.LedgerBookId);
                writer.WriteString("accountingPeriodId", workflowCompletion.AccountingPeriodId);
                writer.WriteString("approvalId", workflowCompletion.ApprovalId);
                writer.WriteString("approvalEvidenceHash", workflowCompletion.ApprovalEvidenceHash);
                writer.WriteString("checklistEvidenceHash", workflowCompletion.ChecklistEvidenceHash);
                writer.WriteString("closePackageId", workflowCompletion.ClosePackageId);
                writer.WriteString("closePackageEvidenceHash", workflowCompletion.ClosePackageEvidenceHash);
                writer.WriteString("closeAuditEventId", workflowCompletion.CloseAuditEventId);
                writer.WriteString("closeAuditHash", workflowCompletion.CloseAuditHash);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void ValidateCloseCheckpoint(
        ReconciliationCloseScopeCheckpoint checkpoint,
        string fundProfileId,
        Guid ledgerBookId,
        LedgerPeriodDto period)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!string.Equals(
                checkpoint.Scope.FundProfileId,
                fundProfileId,
                StringComparison.OrdinalIgnoreCase)
            || checkpoint.Scope.LedgerBookId != ledgerBookId
            || checkpoint.Scope.AccountingPeriodId != period.PeriodId
            || checkpoint.Scope.AsOfDate != period.EndDate)
        {
            throw new InvalidOperationException(
                $"The retained reconciliation checkpoint does not match hard-close scope '{fundProfileId}/{ledgerBookId:D}/{period.PeriodId:D}/{period.EndDate:yyyy-MM-dd}'.");
        }

        if (checkpoint.Generation <= 0
            || checkpoint.CheckpointHashSha256.Length != 64
            || !checkpoint.CheckpointHashSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException(
                "The retained reconciliation close checkpoint has no positive generation or valid SHA-256 evidence hash.");
        }
    }

    internal static ImmutableArray<ReportingReconciliationBreakEvidence> BuildExactReportingBreakEvidence(
        IReadOnlyList<ReconciliationBreakQueueItem> items,
        string fundProfileId,
        Guid ledgerBookId,
        Guid accountingPeriodId,
        DateOnly asOfDate,
        int expectedOpenBreakCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        if (accountingPeriodId == Guid.Empty || asOfDate == default)
        {
            throw new ArgumentException("Exact accounting period and as-of scope are required for close evidence.");
        }

        var unscopedPending = items
            .Where(static item =>
                string.Equals(item.SourceType, "statement", StringComparison.OrdinalIgnoreCase))
            .Where(static item =>
                string.IsNullOrWhiteSpace(item.FundProfileId)
                || !item.LedgerBookId.HasValue
                || item.LedgerBookId.Value == Guid.Empty
                || !Guid.TryParse(item.AccountingPeriodId, out var retainedPeriodId)
                || retainedPeriodId == Guid.Empty
                || !item.AsOfDate.HasValue
                || item.AsOfDate.Value == default)
            .OrderBy(static item => item.BreakId, StringComparer.Ordinal)
            .ToArray();
        if (unscopedPending.Length > 0)
        {
            throw new InvalidOperationException(
                $"{unscopedPending.Length} statement casework evidence handoff obligation(s) have incomplete close scope and therefore block PeriodClose and FinalReport for every candidate scope until exact replay completes: {string.Join(", ", unscopedPending.Select(static item => item.BreakId))}.");
        }

        var scoped = items
            .Where(item => item.LedgerBookId == ledgerBookId
                && string.Equals(item.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.AccountingPeriodId, accountingPeriodId.ToString("D"), StringComparison.OrdinalIgnoreCase)
                && item.AsOfDate == asOfDate)
            .OrderBy(static item => item.BreakId, StringComparer.Ordinal)
            .ToArray();
        foreach (var item in scoped)
        {
            if (StatementCaseworkHandoffObligation.HasPending(item))
            {
                throw new InvalidOperationException(
                    $"Reconciliation break '{item.BreakId}' still has a pending statement-source/Operations evidence handoff; PeriodClose and FinalReport remain blocked even though the case disposition may already be terminal. Replay the exact casework command to complete the retained handoff.");
            }
            if (!HasCanonicalCaseState(item))
            {
                throw new InvalidOperationException(
                    $"Reconciliation break '{item.BreakId}' has contradictory lifecycle '{item.LifecycleState}', queue status '{item.Status}', and disposition '{item.Disposition?.ToString() ?? "none"}'; exact close evidence is blocked until the case is repaired.");
            }
        }
        var open = scoped
            .Where(static item => item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview)
            .ToArray();
        if (open.Length != expectedOpenBreakCount)
        {
            throw new InvalidOperationException(
                $"The close summary reports {expectedOpenBreakCount} open reconciliation break(s), but {open.Length} exact scoped case(s) were found in the canonical queue.");
        }

        var retained = open
            .Concat(scoped.Where(static item => item.Disposition.HasValue))
            .DistinctBy(static item => item.BreakId, StringComparer.Ordinal)
            .OrderBy(static item => item.BreakId, StringComparer.Ordinal);
        return retained
            .Select(static item =>
            {
                if (string.IsNullOrWhiteSpace(item.SourceFingerprint))
                {
                    throw new InvalidOperationException(
                        $"Reconciliation break '{item.BreakId}' has no retained source fingerprint and cannot support certified close evidence.");
                }
                if (item.Disposition.HasValue && string.IsNullOrWhiteSpace(item.DispositionEvidenceHash))
                {
                    throw new InvalidOperationException(
                        $"Disposed reconciliation break '{item.BreakId}' has no retained disposition evidence hash.");
                }

                var links = (item.EvidenceLinks ?? [])
                    .Append($"reconciliation-break:{item.BreakId}:source:{item.SourceFingerprint}")
                    .Append($"reconciliation-break:{item.BreakId}:version:{item.Version.ToString(CultureInfo.InvariantCulture)}")
                    .Append(item.DispositionEvidenceHash is null
                        ? null
                        : $"reconciliation-break:{item.BreakId}:disposition:{item.DispositionEvidenceHash}")
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return ReportingReconciliationEvidenceValidation.CreateBreakEvidence(
                    item with { EvidenceLinks = links },
                    item.BlockedOutputs ?? ["FinalReport", "PeriodClose"]);
            })
            .ToImmutableArray();
    }

    private static bool HasCanonicalCaseState(ReconciliationBreakQueueItem item) =>
        item.LifecycleState switch
        {
            ReconciliationCaseLifecycleState.Open or ReconciliationCaseLifecycleState.Reopened =>
                item.Status == ReconciliationBreakQueueStatus.Open && item.Disposition is null,
            ReconciliationCaseLifecycleState.InReview
                or ReconciliationCaseLifecycleState.Investigating
                or ReconciliationCaseLifecycleState.AwaitingEvidence =>
                item.Status == ReconciliationBreakQueueStatus.InReview && item.Disposition is null,
            ReconciliationCaseLifecycleState.Resolved =>
                item.Status == ReconciliationBreakQueueStatus.Resolved
                && item.Disposition is (ReconciliationBreakDispositionDto.Resolved
                    or ReconciliationBreakDispositionDto.Waived),
            ReconciliationCaseLifecycleState.Superseded =>
                item.Status == ReconciliationBreakQueueStatus.Dismissed
                && item.Disposition == ReconciliationBreakDispositionDto.Superseded,
            ReconciliationCaseLifecycleState.SignedOff =>
                item.Status == ReconciliationBreakQueueStatus.SignedOff
                && item.Disposition is not null,
            _ => false
        };

    internal static void EnsureNoOpenReportingBreaks(
        ImmutableArray<ReportingReconciliationBreakEvidence> breakEvidence,
        LedgerPeriodDto period)
    {
        var openBreakIds = breakEvidence
            .Where(static item => item.Disposition is null)
            .Select(static item => item.BreakId)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        if (openBreakIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"Ledger period '{period.Label}' cannot be hard-closed while {openBreakIds.Length} reconciliation break(s) remain unresolved: {string.Join(", ", openBreakIds)}. Assign and resolve, waive, or supersede each case, then retry hard close.");
        }
    }

    private static ReportingAccountingBasisDto MapReportingBasis(AccountingBasisKindDto basis) => basis switch
    {
        AccountingBasisKindDto.Gaap => ReportingAccountingBasisDto.Gaap,
        AccountingBasisKindDto.Tax => ReportingAccountingBasisDto.Tax,
        AccountingBasisKindDto.Cash => ReportingAccountingBasisDto.Cash,
        AccountingBasisKindDto.Statutory => ReportingAccountingBasisDto.Statutory,
        _ => ReportingAccountingBasisDto.Management
    };

    private async Task LockPostedClosingBatchesAfterHardCloseAsync(
        AccountingClosePostingContext context,
        string fundProfileId,
        LedgerPeriodDto period,
        AccountingClosePostingCommand command,
        CancellationToken ct)
    {
        if (period.Status != LedgerPeriodStatusDto.HardClosed)
        {
            throw new InvalidOperationException(
                $"Ledger period '{period.Label}' did not reach hard-closed status; retained closing batches were not close-locked.");
        }

        var workbench = await _workbench
            .GetWorkbenchAsync(
                fundProfileId,
                context.LedgerBookId,
                ct,
                context.TenantId,
                context.CompanyId)
            .ConfigureAwait(false);
        var postedClosingBatches = workbench.Drafts
            .Where(draft =>
                draft.EntryType == ManualJournalEntryTypeDto.ClosingEntry &&
                draft.Status == ManualJournalEntryStatusDto.Posted &&
                draft.LedgerBookId == context.LedgerBookId &&
                string.Equals(draft.PeriodId, period.PeriodId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            .OrderBy(static draft => draft.PostedAtUtc)
            .ThenBy(static draft => draft.JournalEntryId)
            .ToArray();

        foreach (var draft in postedClosingBatches)
        {
            var evidenceLinks = command.EvidenceLinks
                .Append(BuildCloseLockEvidence(draft, period.PeriodId, context.LedgerBookId))
                .Where(static link => !string.IsNullOrWhiteSpace(link))
                .Select(static link => link.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            await _lifecycle.ApplyLifecycleActionAsync(
                    new JournalEntryLifecycleActionRequestDto(
                        draft.JournalEntryId,
                        draft.FundProfileId,
                        JournalEntryLifecycleActionDto.LockAfterClose,
                        command.Actor,
                        draft.Version,
                        Notes: command.Reason,
                        CorrelationId: command.CorrelationId ??
                                       $"period-hard-close:{context.WorkflowId:N}:{period.PeriodId:N}",
                        EvidenceLinks: evidenceLinks,
                        ActionOrigin: command.ActionOrigin,
                        LedgerBookId: context.LedgerBookId,
                        TenantId: draft.TenantId,
                        CompanyId: draft.CompanyId),
                    ct)
                .ConfigureAwait(false);
        }
    }

    private static string BuildCloseLockEvidence(
        ManualJournalEntryDraftDto draft,
        Guid periodId,
        Guid ledgerBookId)
    {
        var scope = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(draft.TenantId))
        {
            scope.Add($"tenantId={draft.TenantId.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(draft.CompanyId))
        {
            scope.Add($"companyId={draft.CompanyId.Trim()}");
        }

        var query = scope.Count == 0 ? string.Empty : $"?{string.Join("&", scope)}";
        return $"/api/workstation/evidence/subjects/accounting-close/period-lock/ledger-book/{ledgerBookId:D}/period/{periodId:D}/journal-entry/{draft.JournalEntryId:D}{query}";
    }

    public async Task<ClosePostingGateDto> ReopenAndQueueClosingReversalsAsync(
        AccountingClosePostingContext context,
        AccountingClosePostingCommand command,
        CancellationToken ct = default)
    {
        ValidateContext(context);
        ValidateHumanCommand(command, requireController: true, requireReopenApproval: true);
        await using var consistencyLease = await AcquireConsistencyLeaseIfRequiredAsync(
                context,
                command,
                ct)
            .ConfigureAwait(false);
        var scope = await ResolveScopeAsync(context, ct).ConfigureAwait(false);
        var ledgerBookService = _ledgerBookService!;
        var breakQueue = _breakQueue
            ?? throw new InvalidOperationException(
                "The canonical reconciliation queue is unavailable, so the governed reopen cannot version and unseal its hard-close checkpoint.");
        var period = scope.Period;
        var reopenedPeriod = period;
        if (period.Status is not LedgerPeriodStatusDto.HardClosed and not LedgerPeriodStatusDto.SoftClosed)
        {
            throw new InvalidOperationException(
                $"Ledger period '{period.Label}' must be hard-closed, or soft-closed by an idempotent reopen retry, before closing batches can be reversed.");
        }

        var workbench = await _workbench
            .GetWorkbenchAsync(
                scope.FundProfileId,
                context.LedgerBookId,
                ct,
                context.TenantId,
                context.CompanyId)
            .ConfigureAwait(false);
        var retainedClosingBatches = workbench.Drafts
            .Where(draft =>
                draft.EntryType == ManualJournalEntryTypeDto.ClosingEntry &&
                draft.LedgerBookId == context.LedgerBookId &&
                string.Equals(draft.PeriodId, period.PeriodId.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
                draft.Status is ManualJournalEntryStatusDto.Posted
                    or ManualJournalEntryStatusDto.Reversed
                    or ManualJournalEntryStatusDto.CloseLocked)
            .OrderByDescending(static draft => draft.PostedAtUtc)
            .ThenByDescending(static draft => draft.JournalEntryId)
            .ToArray();
        var reversalDrafts = workbench.Drafts
            .Where(draft => draft.ReversalOfJournalEntryId.HasValue &&
                            retainedClosingBatches.Any(batch => batch.JournalEntryId == draft.ReversalOfJournalEntryId.Value))
            .GroupBy(static draft => draft.ReversalOfJournalEntryId!.Value)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderByDescending(static draft => draft.UpdatedAtUtc)
                    .ThenByDescending(static draft => draft.JournalEntryId)
                    .First());

        // A posted reversal fully unwinds an older closing batch and must not be reversed again on
        // a later restatement cycle. A Reversed source with a still-pending reversal is the partial
        // state of the current reopen attempt and remains active for retry.
        var activeClosingBatches = new List<ManualJournalEntryDraftDto>();
        foreach (var batch in retainedClosingBatches)
        {
            if (batch.Status is ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked)
            {
                activeClosingBatches.Add(batch);
                continue;
            }

            if (!reversalDrafts.TryGetValue(batch.JournalEntryId, out var retainedReversal))
            {
                throw new InvalidOperationException(
                    $"Closing batch '{batch.JournalEntryId:D}' is marked Reversed without its retained reversal draft; reopen fails closed.");
            }

            if (retainedReversal.Status is not ManualJournalEntryStatusDto.Posted
                and not ManualJournalEntryStatusDto.CloseLocked)
            {
                activeClosingBatches.Add(batch);
            }
        }

        var retainedActiveReversals = activeClosingBatches
            .Where(batch => reversalDrafts.ContainsKey(batch.JournalEntryId))
            .Select(batch => reversalDrafts[batch.JournalEntryId])
            .ToArray();
        if (retainedActiveReversals.Any(draft => !IsSameReopenReplay(draft, command)))
        {
            throw new InvalidOperationException(
                "Retained reversal drafts do not match this reopen actor, correlation, reason, and evidence; retry is rejected.");
        }

        // Retain the exact reopen intent before changing the durable period. If the ledger reopen
        // succeeds but reversal creation is interrupted, the SoftClosed period and this receipt
        // form an explicit recoverable state: only an exact command replay may continue.
        await RetainReopenIntentAsync(
                context,
                scope.FundProfileId,
                period,
                command,
                ct)
            .ConfigureAwait(false);

        if (period.Status == LedgerPeriodStatusDto.HardClosed)
        {
            try
            {
                var reopenResult = await RequireLedgerBookService().ReopenPeriodAsync(
                        period.PeriodId,
                        new ReopenLedgerPeriodRequest(
                            command.Actor,
                            command.Role!,
                            command.Reason,
                            command.ApprovalReference!,
                            command.EvidenceLinks,
                            command.ActionOrigin),
                        ct)
                    .ConfigureAwait(false);
                reopenedPeriod = reopenResult.Period;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"The governed reopen intent for ledger period '{period.Label}' was retained, but the ledger transition did not complete. Retry the exact reopen command to converge safely.",
                    ex);
            }
        }

        try
        {
            foreach (var batch in activeClosingBatches)
            {
                if (reversalDrafts.ContainsKey(batch.JournalEntryId))
                {
                    continue;
                }

                var lifecycleRequest = new JournalEntryLifecycleActionRequestDto(
                            batch.JournalEntryId,
                            scope.FundProfileId,
                            JournalEntryLifecycleActionDto.Reverse,
                            command.Actor,
                            batch.Version,
                            command.Reason,
                            command.CorrelationId,
                            command.EvidenceLinks,
                            command.ActionOrigin,
                            PeriodIsLocked: false,
                            LedgerBookId: context.LedgerBookId,
                            TenantId: context.TenantId,
                            CompanyId: context.CompanyId);
                var reversed = batch.Status == ManualJournalEntryStatusDto.CloseLocked
                    ? _lifecycle is ManualJournalEntryWorkbenchService governedLifecycle
                        ? await governedLifecycle.ReverseCloseLockedClosingEntryForGovernedReopenAsync(
                                lifecycleRequest,
                                period.PeriodId,
                                period.Version,
                                BuildReopenCommandHash(context, period.PeriodId, command),
                                ct)
                            .ConfigureAwait(false)
                        : throw new InvalidOperationException(
                            "The configured journal lifecycle service cannot verify and release a close-locked closing batch through the governed reopen receipt.")
                    : await _lifecycle.ApplyLifecycleActionAsync(lifecycleRequest, ct).ConfigureAwait(false);
                var generated = reversed.GeneratedJournalEntries.SingleOrDefault(draft =>
                    draft.ReversalOfJournalEntryId == batch.JournalEntryId)
                    ?? throw new InvalidOperationException(
                        $"Reversing closing batch '{batch.JournalEntryId:D}' did not retain a source-linked reversal draft.");
                reversalDrafts[batch.JournalEntryId] = generated;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Ledger period '{period.Label}' was reopened under a retained governed intent, but closing-entry reversal drafting did not complete. Retry the exact reopen command to converge the retained source and reversal state.",
                ex);
        }

        var activeReversals = activeClosingBatches
            .Select(batch => reversalDrafts.TryGetValue(batch.JournalEntryId, out var reversal)
                ? reversal
                : throw new InvalidOperationException(
                    $"Closing batch '{batch.JournalEntryId:D}' has no retained source-linked reversal draft."))
            .ToArray();
        if (activeReversals.Any(draft => !IsSameReopenReplay(draft, command)))
        {
            throw new InvalidOperationException(
                "Retained reversal drafts do not match this reopen actor, correlation, reason, and evidence; retry is rejected.");
        }

        var reopenCommandHash = BuildReopenCommandHash(context, period.PeriodId, command);
        await breakQueue
            .ReopenCloseScopeAsync(
                new ReconciliationCloseScope(
                    scope.FundProfileId,
                    context.LedgerBookId,
                    period.PeriodId,
                    period.EndDate),
                new ReconciliationCloseScopeReopenCommand(
                    command.Actor,
                    command.Role!,
                    command.Reason,
                    command.ApprovalReference!,
                    command.CorrelationId!,
                    command.EvidenceLinks,
                    reopenedPeriod.Version,
                    reopenCommandHash),
                ct)
            .ConfigureAwait(false);

        var evaluated = await EvaluateAsync(context, ct).ConfigureAwait(false);
        var pendingCount = activeReversals.Count(static draft =>
            draft.Status is not ManualJournalEntryStatusDto.Posted and not ManualJournalEntryStatusDto.CloseLocked);
        return evaluated with
        {
            State = pendingCount > 0 ? ClosePostingGateStateDto.ReversalQueued : evaluated.State,
            IsReadyForLock = false,
            Detail = pendingCount > 0
                ? $"{pendingCount} governed closing-entry reversal draft(s) await independent approval and posting before restatement can be reclosed."
                : activeClosingBatches.Count == 0
                    ? "No active retained closing batch requires reversal; apply the restatement and rerun the closing-entry gate before reclose."
                    : "All active closing-entry reversals are posted; apply the restatement and rerun the closing-entry delta before reclose.",
            EvidenceLinks = command.EvidenceLinks,
            ClosingBatchJournalEntryIds = activeClosingBatches.Select(static draft => draft.JournalEntryId).ToArray(),
            ReversalDraftJournalEntryIds = activeReversals.Select(static draft => draft.JournalEntryId).ToArray()
        };
    }

    private async Task RetainReopenIntentAsync(
        AccountingClosePostingContext context,
        string fundProfileId,
        LedgerPeriodDto period,
        AccountingClosePostingCommand command,
        CancellationToken ct)
    {
        if (_workbench is not ManualJournalEntryWorkbenchService receiptStore)
        {
            throw new InvalidOperationException(
                "A governed period reopen requires the durable accounting audit store to retain an exact-command intent receipt.");
        }

        var commandHash = BuildReopenCommandHash(context, period.PeriodId, command);
        var retention = await receiptStore.RetainCloseReopenReceiptAsync(
                fundProfileId,
                context.LedgerBookId,
                period.PeriodId,
                period.Version,
                command.Actor,
                command.CorrelationId!,
                commandHash,
                command.EvidenceLinks,
                context.TenantId,
                context.CompanyId,
                allowCreate: period.Status == LedgerPeriodStatusDto.HardClosed,
                ct)
            .ConfigureAwait(false);
        if (retention == CloseReopenReceiptRetention.Conflict)
        {
            throw new InvalidOperationException(
                "The retained governed reopen intent does not match this actor, correlation, reason, approval, and evidence; retry is rejected.");
        }

        if (retention == CloseReopenReceiptRetention.Missing)
        {
            throw new InvalidOperationException(
                "The ledger period is already soft-closed without a retained governed reopen intent; retry fails closed.");
        }
    }

    private static string BuildReopenCommandHash(
        AccountingClosePostingContext context,
        Guid ledgerPeriodId,
        AccountingClosePostingCommand command)
    {
        var normalizedEvidence = command.EvidenceLinks
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var canonical = string.Join(
            "|",
            context.WorkflowId.ToString("D"),
            context.FundAccountId.ToString("D"),
            context.LedgerBookId.ToString("D"),
            ledgerPeriodId.ToString("D"),
            context.TenantId?.Trim().ToLowerInvariant() ?? string.Empty,
            context.CompanyId?.Trim().ToLowerInvariant() ?? string.Empty,
            command.Actor.Trim().ToLowerInvariant(),
            command.Role?.Trim().ToLowerInvariant() ?? string.Empty,
            command.Reason.Trim(),
            command.ApprovalReference?.Trim().ToLowerInvariant() ?? string.Empty,
            command.CorrelationId?.Trim().ToLowerInvariant() ?? string.Empty,
            string.Join(';', normalizedEvidence));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static bool IsSameReopenReplay(
        ManualJournalEntryDraftDto reversalDraft,
        AccountingClosePostingCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.CorrelationId))
        {
            return false;
        }

        var transition = reversalDraft.LifecycleTransitions.LastOrDefault(item =>
            item.Action == JournalEntryLifecycleActionDto.Reverse &&
            string.Equals(item.Actor, command.Actor, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.CorrelationId, command.CorrelationId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Notes, command.Reason, StringComparison.Ordinal));
        if (transition is null)
        {
            return false;
        }

        var requestedEvidence = command.EvidenceLinks
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retainedEvidence = transition.EvidenceLinks
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requestedEvidence.SetEquals(retainedEvidence);
    }

    private static ClosePostingGateDto BuildGate(
        AccountingClosePostingContext context,
        Guid ledgerPeriodId,
        PeriodCloseDraftPreview preview,
        IReadOnlyList<ManualJournalEntryDraftDto> drafts)
    {
        var periodId = ledgerPeriodId.ToString("D");
        var periodDrafts = drafts
            .Where(draft =>
                draft.LedgerBookId == context.LedgerBookId &&
                string.Equals(draft.PeriodId, periodId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var closingBatches = periodDrafts
            .Where(static draft =>
                draft.EntryType == ManualJournalEntryTypeDto.ClosingEntry &&
                draft.Status is ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked or ManualJournalEntryStatusDto.Reversed)
            .Select(static draft => draft.JournalEntryId)
            .Distinct()
            .ToArray();
        var pendingReversals = periodDrafts
            .Where(static draft =>
                draft.ReversalOfJournalEntryId.HasValue &&
                draft.Status is not ManualJournalEntryStatusDto.Posted and not ManualJournalEntryStatusDto.CloseLocked)
            .Where(draft => closingBatches.Contains(draft.ReversalOfJournalEntryId!.Value))
            .ToArray();
        if (pendingReversals.Length > 0)
        {
            return new ClosePostingGateDto(
                GateId(context, ledgerPeriodId),
                GateLabel,
                ClosePostingGateStateDto.ReversalQueued,
                false,
                preview.Projection.NetIncome,
                preview.Projection.Lines.Count,
                $"{pendingReversals.Length} closing-entry reversal draft(s) await approval and posting.",
                Balances: ToBalances(preview),
                EvidenceLinks: pendingReversals.SelectMany(static draft => draft.EvidenceLinks).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                ClosingBatchJournalEntryIds: closingBatches,
                ReversalDraftJournalEntryIds: pendingReversals.Select(static draft => draft.JournalEntryId).ToArray());
        }

        if (preview.Draft is null)
        {
            var posted = closingBatches.Length > 0;
            return new ClosePostingGateDto(
                GateId(context, ledgerPeriodId),
                GateLabel,
                posted ? ClosePostingGateStateDto.Posted : ClosePostingGateStateDto.NotRequired,
                true,
                0m,
                0,
                posted
                    ? "All revenue and expense balances are zero; retained closing entries are posted."
                    : "All revenue and expense balances are zero; no closing entry is required.",
                ClosingBatchJournalEntryIds: closingBatches);
        }

        var key = preview.Draft.Event.IdempotencyKey;
        var matching = periodDrafts.FirstOrDefault(draft =>
            string.Equals(draft.TreasuryContext?.IdempotencyKey, key, StringComparison.OrdinalIgnoreCase));
        if (matching is null)
        {
            return new ClosePostingGateDto(
                GateId(context, ledgerPeriodId),
                GateLabel,
                ClosePostingGateStateDto.Required,
                false,
                preview.Projection.NetIncome,
                preview.Projection.Lines.Count,
                "Non-zero revenue or expense balances remain. Queue and post the projected closing-entry delta before period lock.",
                IdempotencyKey: key,
                Balances: ToBalances(preview),
                ClosingBatchJournalEntryIds: closingBatches);
        }

        var state = matching.Status switch
        {
            ManualJournalEntryStatusDto.Draft or ManualJournalEntryStatusDto.NeedsFix => ClosePostingGateStateDto.DraftQueued,
            ManualJournalEntryStatusDto.Submitted => ClosePostingGateStateDto.Submitted,
            ManualJournalEntryStatusDto.Approved => ClosePostingGateStateDto.Approved,
            ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked => ClosePostingGateStateDto.Blocked,
            _ => ClosePostingGateStateDto.Blocked
        };
        return new ClosePostingGateDto(
            GateId(context, ledgerPeriodId),
            GateLabel,
            state,
            false,
            preview.Projection.NetIncome,
            preview.Projection.Lines.Count,
            state == ClosePostingGateStateDto.Blocked
                ? "A matching closing batch is retained, but temporary balances remain non-zero; investigate ledger replay before lock."
                : $"Closing-entry draft is {matching.Status}; independent approval and posting are required before lock.",
            matching.JournalEntryId,
            matching.Status,
            key,
            ToBalances(preview),
            matching.EvidenceLinks,
            closingBatches);
    }

    private static IReadOnlyList<ClosePostingBalanceDto> ToBalances(PeriodCloseDraftPreview preview)
        => preview.Projection.Lines
            .Select(static line => new ClosePostingBalanceDto(
                line.Account.Name,
                line.Account.AccountType.ToString(),
                line.PeriodBalance,
                line.Account.Symbol,
                line.Account.FinancialAccountId,
                LedgerDimensionMapper.ToDto(line.Dimensions)))
            .ToArray();

    private static RunPeriodCloseDraftIntakeRequest ToIntakeRequest(
        AccountingClosePostingContext context,
        ResolvedPostingScope scope,
        string actor)
        => new(
            scope.FundProfileId,
            context.Currency,
            actor,
            scope.Period.PeriodId,
            context.LedgerBookId,
            TenantId: context.TenantId,
            CompanyId: context.CompanyId);

    private static ClosePostingGateDto Blocked(AccountingClosePostingContext context, string detail)
        => new(
            GateId(context, null),
            GateLabel,
            ClosePostingGateStateDto.Blocked,
            false,
            0m,
            0,
            detail);

    private static string GateId(AccountingClosePostingContext context, Guid? ledgerPeriodId)
        => $"period-close-posting:{context.LedgerBookId:N}:{ledgerPeriodId?.ToString("N") ?? context.PeriodId.Trim()}";

    private async Task<ResolvedPostingScope> ResolveScopeAsync(
        AccountingClosePostingContext context,
        CancellationToken ct)
    {
        var ledgerBookService = _ledgerBookService
            ?? throw new InvalidOperationException(
                LedgerUnavailableDetail);
        var book = await ledgerBookService.GetBookAsync(context.LedgerBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Ledger book '{context.LedgerBookId:D}' was not found for the closing-entry gate.");
        if (book.FundStructureNodeId != context.FundAccountId)
        {
            throw new InvalidOperationException(
                $"Close workflow '{context.WorkflowId:D}' fund account '{context.FundAccountId:D}' does not own ledger book '{context.LedgerBookId:D}'.");
        }

        if (!string.IsNullOrWhiteSpace(context.TenantId))
        {
            var tenancy = _tenancyRegistry
                ?? throw new InvalidOperationException(
                    "The authoritative fund tenancy registry is unavailable, so the scoped close mutation is blocked.");
            var ownership = await tenancy.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Fund profile '{book.FundProfileId}' has no authoritative tenant/company binding.");
            if (!ownership.IsHeldBy(context.TenantId) ||
                string.IsNullOrWhiteSpace(ownership.CompanyId) ||
                !string.Equals(
                    ownership.CompanyId.Trim(),
                    context.CompanyId!.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Close workflow '{context.WorkflowId:D}' is not owned by tenant '{context.TenantId}' and company '{context.CompanyId}'.");
            }
        }

        if (!string.Equals(book.BaseCurrency, context.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Close workflow currency '{context.Currency}' does not match ledger book '{context.LedgerBookId:D}' base currency '{book.BaseCurrency}'.");
        }

        var periods = await ledgerBookService
            .ListPeriodsAsync(new LedgerPeriodQuery(LedgerBookId: context.LedgerBookId), ct)
            .ConfigureAwait(false);
        var hasGuid = Guid.TryParse(context.PeriodId, out var requestedId);
        var period = periods.FirstOrDefault(candidate =>
                         (hasGuid && candidate.PeriodId == requestedId) ||
                         string.Equals(candidate.Label, context.PeriodId, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException(
                         $"Ledger period '{context.PeriodId}' was not found in book '{context.LedgerBookId:D}'.");
        return new ResolvedPostingScope(book.FundProfileId, book, period);
    }

    private static void ValidateContext(AccountingClosePostingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Currency);
        if (context.WorkflowId == Guid.Empty ||
            context.FundAccountId == Guid.Empty ||
            context.LedgerBookId == Guid.Empty ||
            string.IsNullOrWhiteSpace(context.PeriodId))
        {
            throw new ArgumentException("Period-close posting context requires workflow, fund account, ledger book, and period ids.", nameof(context));
        }

        var hasTenant = !string.IsNullOrWhiteSpace(context.TenantId);
        var hasCompany = !string.IsNullOrWhiteSpace(context.CompanyId);
        if (hasTenant != hasCompany)
        {
            throw new ArgumentException(
                "Period-close posting context requires both tenant and company scope, or neither for explicit local compatibility.",
                nameof(context));
        }
    }

    private async ValueTask<IAsyncDisposable?> AcquireConsistencyLeaseIfRequiredAsync(
        AccountingClosePostingContext context,
        AccountingClosePostingCommand command,
        CancellationToken ct)
        => command.ConsistencyLeaseHeld
            ? null
            : await AcquireAsync(context, ct).ConfigureAwait(false);

    private static void ValidateHumanCommand(
        AccountingClosePostingCommand command,
        bool requireController,
        bool requireReopenApproval = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OperationsOriginGuard.IsHumanOperator(command.ActionOrigin))
        {
            throw new HumanOperatorRequiredException(
                "queue closing entries or reversals",
                "Reviewed automation cannot queue closing entries or reversals; a human operator must perform the close action.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(command.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Reason);
        if (command.EvidenceLinks.Count == 0)
        {
            throw new InvalidOperationException("Closing-entry commands require retained evidence.");
        }

        if (requireController)
        {
            if (!string.Equals(command.Role, "Controller", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(command.Role, "Fund Controller", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Governed period close requires Controller or Fund Controller authority.");
            }
        }

        if (requireReopenApproval)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(command.ApprovalReference);
            ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
            if (!command.EvidenceLinks.Any(link =>
                    link.Contains(command.ApprovalReference, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Closing-entry reversal evidence must reference the governed reopen approval.");
            }
        }
    }

    private sealed record ResolvedPostingScope(
        string FundProfileId,
        LedgerBookDto Book,
        LedgerPeriodDto Period);
}
