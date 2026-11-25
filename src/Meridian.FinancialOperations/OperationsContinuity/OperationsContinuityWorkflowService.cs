using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.OperationsContinuity;

/// <summary>
/// Defines governed operations workflow creation, transitions, checklist controls, and close-
/// readiness actions.
/// </summary>
public interface IOperationsContinuityWorkflowService
{
    Task<OperationsApprovalCollaborationDto?> GetApprovalCollaborationAsync(Guid workflowId, CancellationToken ct = default)
        => Task.FromResult<OperationsApprovalCollaborationDto?>(null);
    Task<OperationsTransitionResultDto> AddApprovalCommentAsync(Guid workflowId, OperationsApprovalCommentRequestDto request, CancellationToken ct = default)
        => throw new NotSupportedException("Approval discussion is not supported by this service.");
    Task<OperationsTransitionResultDto> ReassignApprovalAsync(Guid workflowId, OperationsApprovalReassignRequestDto request, CancellationToken ct = default)
        => throw new NotSupportedException("Approval reassignment is not supported by this service.");
    Task<OperationsTransitionResultDto> StartWorkflowAsync(OperationsStartWorkflowRequestDto request, CancellationToken ct = default);

    Task<OperationsTransitionResultDto> StartPreparedWorkflowAsync(
        OperationsStartWorkflowRequestDto request,
        Guid workflowId,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Prepared close workflows are not supported by this service.");

    Task<OperationsTransitionResultDto> ImportBrokerDataAsync(
        Guid workflowId,
        OperationsTransitionRequestDto request,
        CancellationToken ct = default);

    Task<OperationsTransitionResultDto> NormalizeBrokerTransactionsAsync(
        Guid workflowId,
        OperationsTransitionRequestDto request,
        CancellationToken ct = default);

    Task<OperationsTransitionResultDto> RefreshGatePostureAsync(
        Guid workflowId,
        OperationsGatePostureRequestDto request,
        CancellationToken ct = default);

    Task<OperationsTransitionResultDto> ResolveSecurityMasterMappingsAsync(
        Guid workflowId,
        OperationsSecurityMasterResolveRequestDto request,
        CancellationToken ct = default);

    Task<OperationsTransitionResultDto> ApproveSecurityMasterOverrideAsync(
        Guid workflowId,
        string overrideId,
        OperationsSecurityMasterOverrideApprovalRequestDto request,
        CancellationToken ct = default);

    Task<OperationsTransitionResultDto> BuildLedgerDraftAsync(
        Guid workflowId,
        OperationsLedgerDraftRequestDto request,
        CancellationToken ct = default);
    Task<OperationsTransitionResultDto> ValidateLedgerDraftAsync(
        Guid workflowId,
        OperationsLedgerValidationRequestDto request,
        CancellationToken ct = default);
    Task<OperationsTransitionResultDto> PostLedgerEntriesAsync(
        Guid workflowId,
        OperationsLedgerPostRequestDto request,
        CancellationToken ct = default);
    Task<OperationsTransitionResultDto> RunReconciliationAsync(
        Guid workflowId,
        OperationsReconciliationRunRequestDto request,
        CancellationToken ct = default);
    Task<OperationsTransitionResultDto> ResolveBreakCaseAsync(
        Guid workflowId,
        string breakId,
        OperationsResolveBreakCaseRequestDto request,
        CancellationToken ct = default);
    Task<OperationsTransitionResultDto> AssignBreakCaseAsync(
        Guid workflowId,
        string breakId,
        OperationsAssignBreakCaseRequestDto request,
        CancellationToken ct = default);
    Task<OperationsTransitionResultDto> SubmitForApprovalAsync(
        Guid workflowId,
        OperationsSubmitApprovalRequestDto request,
        CancellationToken ct = default);
    Task<OperationsTransitionResultDto> ApproveWorkflowAsync(
        Guid workflowId,
        OperationsApprovalDecisionRequestDto request,
        CancellationToken ct = default);
    Task<OperationsTransitionResultDto> RejectWorkflowAsync(
        Guid workflowId,
        OperationsRejectWorkflowRequestDto request,
        CancellationToken ct = default);
    Task<OperationsTransitionResultDto> CloseWorkflowAsync(
        Guid workflowId,
        OperationsCloseWorkflowRequestDto request,
        CancellationToken ct = default);
    Task<OperationsTransitionResultDto> ReopenWorkflowAsync(
        Guid workflowId,
        OperationsReopenWorkflowRequestDto request,
        CancellationToken ct = default);

    Task<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>> ListAsync(
        Guid? fundAccountId = null,
        string? periodId = null,
        OperationsWorkflowStatusDto? status = null,
        CancellationToken ct = default,
        Guid? ledgerBookId = null);

    Task<OperationsContinuityWorkflowDto?> GetAsync(Guid workflowId, CancellationToken ct = default);

    Task<IReadOnlyList<OperationsTimelineEntryDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default);
    Task<IReadOnlyList<OperationsCloseChecklistTaskDto>> GetChecklistAsync(Guid workflowId, CancellationToken ct = default);
    Task<OperationsTransitionResultDto> AcknowledgeChecklistTaskAsync(
        Guid workflowId,
        string taskId,
        OperationsChecklistAcknowledgeRequestDto request,
        CancellationToken ct = default);
}

/// <summary>
/// Coordinates governed operations workflow transitions, checklist controls, and close readiness.
/// </summary>
public sealed partial class OperationsContinuityWorkflowService : IOperationsContinuityWorkflowService
{

    private static readonly JsonSerializerOptions WorkflowCloneJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IOperationsContinuityRepository _repository;
    private readonly IOperationsWorkflowAuditStore _auditStore;
    private readonly IOperationsStatusDerivationService _statusDerivation;
    private readonly OperationsWorkflowReadModelBuilder _readModelBuilder;
    private readonly IOperationsContinuityWorkflowStartCommitStore? _workflowStartCommitStore;
    private readonly IOperationsContinuityTransitionCommitStore? _transitionCommitStore;
    private readonly OperationsLedgerPostingService _ledgerPosting;
    private readonly IClosePublicationReadinessGuard? _closeReadinessGuard;

    public OperationsContinuityWorkflowService(
        IOperationsContinuityRepository repository,
        IOperationsWorkflowAuditStore auditStore,
        IOperationsStatusDerivationService statusDerivation,
        ILedgerJournalStore? ledgerJournalStore = null,
        IOperationsContinuityTransactionalCommitStore? transactionalCommitStore = null,
        ISecurityMasterQueryService? securityMasterQueryService = null,
        IClosePublicationReadinessGuard? closeReadinessGuard = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _statusDerivation = statusDerivation ?? throw new ArgumentNullException(nameof(statusDerivation));
        _readModelBuilder = new OperationsWorkflowReadModelBuilder(_statusDerivation);
        _workflowStartCommitStore = transactionalCommitStore as IOperationsContinuityWorkflowStartCommitStore ??
            repository as IOperationsContinuityWorkflowStartCommitStore ??
            auditStore as IOperationsContinuityWorkflowStartCommitStore ??
            (repository is InMemoryOperationsContinuityRepository startRepository &&
             auditStore is InMemoryOperationsWorkflowAuditStore startAuditStore
                ? new InMemoryOperationsContinuityWorkflowStartCommitStore(startRepository, startAuditStore)
                : null);
        _transitionCommitStore = transactionalCommitStore ??
            repository as IOperationsContinuityTransitionCommitStore ??
            (repository is InMemoryOperationsContinuityRepository inMemoryRepository
                ? new InMemoryOperationsContinuityTransitionCommitStore(inMemoryRepository, auditStore)
                : null);
        _ledgerPosting = new OperationsLedgerPostingService(ledgerJournalStore, transactionalCommitStore, securityMasterQueryService);
        _closeReadinessGuard = closeReadinessGuard;
    }

    public async Task<OperationsTransitionResultDto> ImportBrokerDataAsync(
        Guid workflowId,
        OperationsTransitionRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (workflowId == Guid.Empty)
        {
            return Failure("VALIDATION_FAILED", "Workflow id is required.",
            [
                new OperationsWorkflowBlockerDto(
                    "WORKFLOW_ID_REQUIRED",
                    "Workflow id is required.",
                    null,
                    "Error",
                    [])
            ]);
        }

        if (string.IsNullOrWhiteSpace(request.Actor))
        {
            return Failure("VALIDATION_FAILED", "Actor is required for workflow transitions.",
            [
                new OperationsWorkflowBlockerDto(
                    "ACTOR_REQUIRED",
                    "Actor is required for workflow transitions.",
                    null,
                    "Error",
                    [])
            ]);
        }

        var workflow = await _repository.GetAsync(workflowId, ct).ConfigureAwait(false);
        if (workflow is null)
        {
            return Failure("NOT_FOUND", "Workflow was not found.", []);
        }

        if (workflow.Version != request.ExpectedVersion)
        {
            return Failure(
                "VERSION_MISMATCH",
                $"Workflow version {workflow.Version} does not match expected version {request.ExpectedVersion}.",
                [
                    new OperationsWorkflowBlockerDto(
                        "WORKFLOW_VERSION_MISMATCH",
                        "Refresh the workflow before retrying the command.",
                        null,
                        "Error",
                        [])
                ]);
        }

        if (workflow.GetBrokerImportTransitionBlocker() is { } transitionBlocker)
        {
            return Failure(
                "INVALID_STATE_TRANSITION",
                transitionBlocker.Message,
                [transitionBlocker]);
        }

        var fromStatus = _statusDerivation.Derive(workflow);
        var fromGate = workflow.BrokerIngestGate.Status;
        var evidence = OperationsContinuityWorkflowText.NormalizeEvidence(request.EvidenceLinks);
        var workflowForCommit = CloneWorkflow(workflow);
        workflowForCommit.MarkBrokerImported(DateTimeOffset.UtcNow, evidence);
        var toStatus = _statusDerivation.Derive(workflowForCommit);

        var audit = await _auditStore.AppendAsync(
            new OperationsWorkflowAuditDraft(
                workflowForCommit.WorkflowId,
                workflowForCommit.FundAccountId,
                workflowForCommit.PeriodId,
                "broker-imported",
                fromStatus,
                toStatus,
                OperationsGateKeyDto.BrokerIngest,
                fromGate,
                workflowForCommit.BrokerIngestGate.Status,
                request.Actor.Trim(),
                OperationsContinuityWorkflowText.RedactSensitiveText(request.Rationale),
                OperationsContinuityWorkflowText.RedactSensitiveText(request.CorrelationId),
                evidence),
            ct: ct).ConfigureAwait(false);

        workflowForCommit.Touch(audit.OccurredAtUtc);
        await _repository.SaveAsync(workflowForCommit, ct).ConfigureAwait(false);
        var dto = await ToDtoAsync(workflowForCommit, ct).ConfigureAwait(false);
        return Success(dto);
    }

    public async Task<OperationsTransitionResultDto> RefreshGatePostureAsync(
        Guid workflowId,
        OperationsGatePostureRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            request.EvidenceLinks,
            eventType: "gate-posture-refreshed",
            gate: null,
            precondition: null,
            command: (workflow, evidence, now) =>
            {
                workflow.ApplyGatePosture(request, evidence, now);
                return null;
            },
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> NormalizeBrokerTransactionsAsync(
        Guid workflowId,
        OperationsTransitionRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            request.EvidenceLinks,
            eventType: "broker-transactions-normalized",
            gate: OperationsGateKeyDto.BrokerIngest,
            precondition: static workflow => workflow.GetBrokerNormalizeTransitionBlocker(),
            command: (workflow, evidence, now) =>
            {
                workflow.NormalizeBrokerTransactions(request, evidence, now);
                return null;
            },
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> ResolveSecurityMasterMappingsAsync(
        Guid workflowId,
        OperationsSecurityMasterResolveRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            request.EvidenceLinks,
            eventType: "security-master-resolved",
            gate: OperationsGateKeyDto.SecurityMaster,
            precondition: static workflow => workflow.GetSecurityMasterResolveTransitionBlocker(),
            command: (workflow, evidence, now) =>
            {
                workflow.ResolveSecurityMasterMappings(request, evidence, now);
                return null;
            },
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> ApproveSecurityMasterOverrideAsync(
        Guid workflowId,
        string overrideId,
        OperationsSecurityMasterOverrideApprovalRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RejectAutomationMaterialAction(request.ActionOrigin, OperationsGateKeyDto.SecurityMaster, "Security Master override approval") is { } automationFailure)
        {
            return automationFailure;
        }

        if (!string.IsNullOrWhiteSpace(overrideId) &&
            !string.IsNullOrWhiteSpace(request.OverrideId) &&
            !string.Equals(overrideId, request.OverrideId, StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                "VALIDATION_FAILED",
                "Route override id does not match the approval request payload.",
                [
                    new OperationsWorkflowBlockerDto(
                        "SM_OVERRIDE_ID_MISMATCH",
                        "Use the same Security Master override id in the route and request body.",
                        OperationsGateKeyDto.SecurityMaster,
                        "Error",
                        [])
                ]);
        }

        var effectiveRequest = request with
        {
            OverrideId = !string.IsNullOrWhiteSpace(overrideId) ? overrideId : request.OverrideId
        };

        return await ApplyCommandAsync(
            workflowId,
            effectiveRequest.ExpectedVersion,
            effectiveRequest.Actor,
            effectiveRequest.Rationale,
            effectiveRequest.CorrelationId,
            effectiveRequest.EvidenceLinks,
            eventType: "security-master-override-approved",
            gate: OperationsGateKeyDto.SecurityMaster,
            precondition: workflow => workflow.GetApproveSecurityMasterOverrideTransitionBlocker(effectiveRequest),
            command: (workflow, evidence, now) =>
            {
                workflow.ApproveSecurityMasterOverride(effectiveRequest, evidence, now);
                return null;
            },
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> BuildLedgerDraftAsync(
        Guid workflowId,
        OperationsLedgerDraftRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            request.EvidenceLinks,
            eventType: "ledger-draft-built",
            gate: OperationsGateKeyDto.LedgerPosting,
            precondition: static workflow => workflow.GetBuildLedgerDraftTransitionBlocker(),
            command: (workflow, evidence, now) =>
            {
                workflow.BuildLedgerDraft(request, evidence, now);
                return null;
            },
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> PostLedgerEntriesAsync(
        Guid workflowId,
        OperationsLedgerPostRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RejectAutomationMaterialAction(request.ActionOrigin, OperationsGateKeyDto.LedgerPosting, "Ledger posting") is { } automationFailure)
        {
            return automationFailure;
        }

        if (workflowId == Guid.Empty)
        {
            return Failure("VALIDATION_FAILED", "Workflow id is required.",
            [
                new OperationsWorkflowBlockerDto("WORKFLOW_ID_REQUIRED", "Workflow id is required.", null, "Error", [])
            ]);
        }

        if (string.IsNullOrWhiteSpace(request.Actor))
        {
            return Failure("VALIDATION_FAILED", "Actor is required for workflow transitions.",
            [
                new OperationsWorkflowBlockerDto(
                    "ACTOR_REQUIRED",
                    "Actor is required for workflow transitions.",
                    OperationsGateKeyDto.LedgerPosting,
                    "Error",
                    [])
            ]);
        }

        var workflow = await _repository.GetAsync(workflowId, ct).ConfigureAwait(false);
        if (workflow is null)
        {
            return Failure("NOT_FOUND", "Workflow was not found.", []);
        }

        if (workflow.Version != request.ExpectedVersion)
        {
            return Failure(
                "VERSION_MISMATCH",
                $"Workflow version {workflow.Version} does not match expected version {request.ExpectedVersion}.",
                [
                    new OperationsWorkflowBlockerDto(
                        "WORKFLOW_VERSION_MISMATCH",
                        "Refresh the workflow before retrying the command.",
                        null,
                        "Error",
                        [])
                ]);
        }

        if (workflow.IsClosed)
        {
            var blocker = CreateClosedWorkflowBlocker(OperationsGateKeyDto.LedgerPosting);
            return Failure("INVALID_STATE_TRANSITION", blocker.Message, [blocker]);
        }

        if (workflow.GetPostLedgerEntriesTransitionBlocker() is { } preconditionBlocker)
        {
            return Failure("INVALID_STATE_TRANSITION", preconditionBlocker.Message, [preconditionBlocker]);
        }

        var evidence = OperationsContinuityWorkflowText.NormalizeEvidence(request.EvidenceLinks);
        var requestBlockers = _ledgerPosting.ValidateLedgerPostRequest(request, evidence).ToList();
        var serviceBlockers = new List<OperationsWorkflowBlockerDto>();
        LedgerJournalEntryWrite? journalWrite = null;
        LedgerJournalEntryWrite builtJournalWrite = default!;
        if (requestBlockers.Count == 0)
        {
            if (!_ledgerPosting.IsLedgerStoreAvailable)
            {
                var blocker = new OperationsWorkflowBlockerDto(
                    "LEDGER_JOURNAL_STORE_UNAVAILABLE",
                    "Register ILedgerJournalStore or IOperationsContinuityTransactionalCommitStore before posting Operations Continuity ledger entries.",
                    OperationsGateKeyDto.LedgerPosting,
                    "Critical",
                    evidence);
                requestBlockers.Add(blocker);
                serviceBlockers.Add(blocker);
            }

            var authoritativeSecurityStatuses = requestBlockers.Count == 0
                ? await _ledgerPosting.ResolveAuthoritativeSecurityStatusesAsync(request.JournalCandidate?.Lines, ct).ConfigureAwait(false)
                : new Dictionary<Guid, SecurityStatusDto>();

            if (requestBlockers.Count == 0 &&
                !_ledgerPosting.TryBuildJournalWrite(workflow, request, authoritativeSecurityStatuses, out builtJournalWrite, out var journalBlockers))
            {
                return Failure(
                    "VALIDATION_FAILED",
                    "Ledger journal candidate is invalid.",
                    journalBlockers);
            }

            if (requestBlockers.Count == 0)
            {
                journalWrite = builtJournalWrite;
            }
        }

        var fromStatus = _statusDerivation.Derive(workflow);
        var fromGateStatus = workflow.LedgerPostingGate.Status;
        var now = DateTimeOffset.UtcNow;
        var workflowForCommit = CloneWorkflow(workflow);
        workflowForCommit.PostLedgerEntries(request, evidence, now, serviceBlockers);
        var toStatus = _statusDerivation.Derive(workflowForCommit);
        var toGateStatus = workflowForCommit.LedgerPostingGate.Status;
        var auditDraft = new OperationsWorkflowAuditDraft(
            workflowForCommit.WorkflowId,
            workflowForCommit.FundAccountId,
            workflowForCommit.PeriodId,
            requestBlockers.Count == 0 ? "ledger-posted" : "ledger-posting-blocked",
            fromStatus,
            toStatus,
            OperationsGateKeyDto.LedgerPosting,
            fromGateStatus,
            toGateStatus,
            request.Actor.Trim(),
            OperationsContinuityWorkflowText.RedactSensitiveText(request.Rationale),
            OperationsContinuityWorkflowText.RedactSensitiveText(request.CorrelationId),
            evidence);

        var commitOutcome = await _ledgerPosting
            .CommitOrAppendAsync(workflowForCommit, auditDraft, journalWrite, evidence, ct)
            .ConfigureAwait(false);
        switch (commitOutcome.Disposition)
        {
            case LedgerCommitOutcome.LedgerCommitDisposition.Rejected:
                return Failure(
                    "LEDGER_JOURNAL_APPEND_REJECTED",
                    "Ledger journal store rejected the posting candidate.",
                    [commitOutcome.Blocker!]);
            case LedgerCommitOutcome.LedgerCommitDisposition.Committed:
                return Success(await ToDtoAsync(commitOutcome.Workflow!, ct).ConfigureAwait(false));
            case LedgerCommitOutcome.LedgerCommitDisposition.Proceed:
            default:
                break;
        }

        var audit = await _auditStore.AppendAsync(auditDraft, ct).ConfigureAwait(false);
        workflowForCommit.Touch(audit.OccurredAtUtc);
        await _repository.SaveAsync(workflowForCommit, ct).ConfigureAwait(false);
        var dto = await ToDtoAsync(workflowForCommit, ct).ConfigureAwait(false);
        return Success(dto);
    }

    public async Task<OperationsTransitionResultDto> ValidateLedgerDraftAsync(
        Guid workflowId,
        OperationsLedgerValidationRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            request.EvidenceLinks,
            eventType: "ledger-draft-validated",
            gate: OperationsGateKeyDto.LedgerPosting,
            precondition: static workflow => workflow.GetValidateLedgerDraftTransitionBlocker(),
            command: (workflow, evidence, now) =>
            {
                workflow.ValidateLedgerDraft(request, evidence, now);
                return null;
            },
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> RejectWorkflowAsync(
        Guid workflowId,
        OperationsRejectWorkflowRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RejectAutomationMaterialAction(request.ActionOrigin, OperationsGateKeyDto.Approval, "Approval rejection") is { } automationFailure)
        {
            return automationFailure;
        }

        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            request.EvidenceLinks,
            eventType: "approval-rejected",
            gate: OperationsGateKeyDto.Approval,
            precondition: workflow => workflow.GetRejectTransitionBlocker(request),
            command: (workflow, evidence, now) =>
            {
                workflow.Reject(request, evidence, now);
                return null;
            },
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> RunReconciliationAsync(
        Guid workflowId,
        OperationsReconciliationRunRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            request.EvidenceLinks,
            eventType: "reconciliation-run",
            gate: OperationsGateKeyDto.Reconciliation,
            precondition: static workflow => workflow.GetRunReconciliationTransitionBlocker(),
            command: (workflow, evidence, now) =>
            {
                workflow.RunReconciliation(request, evidence, now);
                return null;
            },
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> ReopenWorkflowAsync(
        Guid workflowId,
        OperationsReopenWorkflowRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RejectAutomationMaterialAction(request.ActionOrigin, OperationsGateKeyDto.Reconciliation, "Governed reopen") is { } automationFailure)
        {
            return automationFailure;
        }

        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            BuildReopenGovernanceRationale(request),
            request.CorrelationId,
            EnsureReopenGovernanceEvidence(request, request.EvidenceLinks),
            eventType: "workflow-reopened",
            gate: OperationsGateKeyDto.Reconciliation,
            precondition: workflow => workflow.GetReopenTransitionBlocker(request),
            command: (workflow, evidence, now) =>
            {
                workflow.Reopen(request, evidence, now);
                return null;
            },
            allowClosedWorkflow: true,
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> ResolveBreakCaseAsync(
        Guid workflowId,
        string breakId,
        OperationsResolveBreakCaseRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RejectAutomationMaterialAction(request.ActionOrigin, OperationsGateKeyDto.Reconciliation, "Reconciliation break resolution") is { } automationFailure)
        {
            return automationFailure;
        }

        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            request.EvidenceLinks,
            eventType: string.Equals(request.ResolutionStatus, "waived", StringComparison.OrdinalIgnoreCase)
                ? "reconciliation-break-waived"
                : string.Equals(request.ResolutionStatus, "superseded", StringComparison.OrdinalIgnoreCase)
                    ? "reconciliation-break-superseded"
                    : "reconciliation-break-resolved",
            gate: OperationsGateKeyDto.Reconciliation,
            precondition: null,
            command: (workflow, evidence, now) =>
            {
                var blocker = workflow.ResolveBreakCase(breakId, request, evidence, now);
                return blocker is null ? null : blocker;
            },
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> AssignBreakCaseAsync(
        Guid workflowId,
        string breakId,
        OperationsAssignBreakCaseRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RejectAutomationMaterialAction(request.ActionOrigin, OperationsGateKeyDto.Reconciliation, "Reconciliation break assignment") is { } automationFailure)
        {
            return automationFailure;
        }

        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            request.EvidenceLinks,
            eventType: string.IsNullOrWhiteSpace(request.EscalationLevel)
                ? "reconciliation-break-assigned"
                : "reconciliation-break-escalated",
            gate: OperationsGateKeyDto.Reconciliation,
            precondition: null,
            command: (workflow, evidence, now) =>
            {
                var blocker = workflow.AssignBreakCase(breakId, request, evidence, now);
                return blocker is null ? null : blocker;
            },
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> SubmitForApprovalAsync(
        Guid workflowId,
        OperationsSubmitApprovalRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RejectAutomationMaterialAction(request.ActionOrigin, OperationsGateKeyDto.Approval, "Approval submission") is { } automationFailure)
        {
            return automationFailure;
        }

        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            EnsureReportPackEvidence(request.ReportPackId, request.EvidenceLinks),
            eventType: "approval-submitted",
            gate: OperationsGateKeyDto.Approval,
            precondition: workflow => workflow.GetSubmitForApprovalTransitionBlocker(request),
            command: (workflow, evidence, now) =>
            {
                workflow.SubmitForApproval(request, evidence, now);
                return null;
            },
            auditPrecondition: (workflow, timeline) => ValidateRetainedChecklistControls(workflow, timeline, request.ChecklistControlApprovals),
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> ApproveWorkflowAsync(
        Guid workflowId,
        OperationsApprovalDecisionRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RejectAutomationMaterialAction(request.ActionOrigin, OperationsGateKeyDto.Approval, "Approval decision") is { } automationFailure)
        {
            return automationFailure;
        }

        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            EnsureReportPackEvidence(request.ReportPackId, request.EvidenceLinks),
            eventType: "approval-approved",
            gate: OperationsGateKeyDto.Approval,
            precondition: workflow => workflow.GetApproveTransitionBlocker(request),
            command: (workflow, evidence, now) =>
            {
                workflow.Approve(request, evidence, now);
                return null;
            },
            auditPrecondition: (workflow, timeline) => ValidateRetainedChecklistControls(workflow, timeline, request.ChecklistControlApprovals),
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> CloseWorkflowAsync(
        Guid workflowId,
        OperationsCloseWorkflowRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RejectAutomationMaterialAction(request.ActionOrigin, OperationsGateKeyDto.Approval, "Close package publication") is { } automationFailure)
        {
            return automationFailure;
        }

        var existing = await _repository.GetAsync(workflowId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            var readiness = EvaluateCloseReadiness(existing);
            if (!readiness.IsReadyToClose)
            {
                var blockers = readiness.Blockers
                    .Select(static blocker => new OperationsWorkflowBlockerDto(
                        blocker.Code,
                        blocker.Message,
                        blocker.Gate,
                        blocker.Severity,
                        []))
                    .ToArray();
                var blockedResult = await PersistBlockedAttemptAsync(
                    existing,
                    request.ExpectedVersion,
                    request.Actor,
                    request.Rationale,
                    request.CorrelationId,
                    "workflow-closed",
                    OperationsGateKeyDto.Approval,
                    "CLOSE_READINESS_FAILED",
                    "Close was rejected by fail-closed readiness gating.",
                    blockers,
                    EnsureReportPackEvidence(request.ReportPackId, request.EvidenceLinks),
                    ct).ConfigureAwait(false);
                return blockedResult with { CloseReadiness = readiness };
            }
        }

        var result = await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            EnsureReportPackEvidence(request.ReportPackId, request.EvidenceLinks),
            eventType: "workflow-closed",
            gate: OperationsGateKeyDto.Approval,
            precondition: workflow => workflow.GetCloseTransitionBlocker(request),
            command: (workflow, evidence, now) =>
            {
                workflow.MarkClosed(request, evidence, now);
                return null;
            },
            requireIntactAuditChain: true,
            boundaryPrecondition: (workflow, cancellationToken) =>
                ValidatePublicationReadinessAsync(workflow.WorkflowId, request, cancellationToken),
            auditPrecondition: (workflow, timeline) => ValidateRetainedChecklistControls(workflow, timeline, request.ChecklistControlApprovals),
            ct: ct).ConfigureAwait(false);

        return result with { CloseReadiness = result.Workflow?.CloseReadiness };
    }


    public async Task<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>> ListAsync(
        Guid? fundAccountId = null,
        string? periodId = null,
        OperationsWorkflowStatusDto? status = null,
        CancellationToken ct = default,
        Guid? ledgerBookId = null)
    {
        var workflows = await _repository.ListAsync(fundAccountId, periodId, status, ct, ledgerBookId: ledgerBookId).ConfigureAwait(false);
        return workflows.Select(ToSummary).ToArray();
    }

    public async Task<OperationsContinuityWorkflowDto?> GetAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await _repository.GetAsync(workflowId, ct).ConfigureAwait(false);
        return workflow is null ? null : await ToDtoAsync(workflow, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OperationsTimelineEntryDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default)
    {
        var timeline = await _auditStore.GetTimelineAsync(workflowId, ct).ConfigureAwait(false);
        return timeline.Select(ToTimelineEntry).ToArray();
    }

    public async Task<IReadOnlyList<OperationsCloseChecklistTaskDto>> GetChecklistAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await _repository.GetAsync(workflowId, ct).ConfigureAwait(false);
        if (workflow is null)
        {
            return [];
        }

        var timeline = await GetTimelineAsync(workflowId, ct).ConfigureAwait(false);
        return OperationsWorkflowReadModelBuilder.BuildChecklist(workflow, timeline);
    }

    public async Task<OperationsTransitionResultDto> AcknowledgeChecklistTaskAsync(Guid workflowId, string taskId, OperationsChecklistAcknowledgeRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var checklist = await GetChecklistAsync(workflowId, ct).ConfigureAwait(false);
        var task = checklist.FirstOrDefault(item => string.Equals(item.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
        if (task is null)
        {
            return Failure("NOT_FOUND", "Checklist task was not found.", []);
        }

        if (!task.CanAcknowledge)
        {
            return Failure("EVIDENCE_REQUIRED", "Checklist task evidence is required before acknowledgment.", [
                new OperationsWorkflowBlockerDto("CHECKLIST_EVIDENCE_REQUIRED", "Checklist task cannot be acknowledged before gate evidence exists.", task.Gate, "Error", [])
            ]);
        }

        var timeline = await GetTimelineAsync(workflowId, ct).ConfigureAwait(false);
        var evidence = OperationsWorkflowReadModelBuilder.OrderChecklistTimeline(timeline)
            .LastOrDefault(entry => OperationsWorkflowReadModelBuilder.CompletesChecklistGate(entry, task.Gate)) is { } completion
                ? OperationsWorkflowReadModelBuilder.ChecklistCompletionEvidence(completion, task.Gate)
                : null;

        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            evidenceLinks: evidence is null ? [] : [evidence],
            "checklist-task-acknowledged",
            task.Gate,
            command: (workflow, _, _) =>
            {
                var gate = GetGate(workflow, task.Gate);
                if (gate.Status != OperationsGateStatusDto.Passed || evidence is null)
                {
                    return new OperationsWorkflowBlockerDto("CHECKLIST_GATE_NOT_COMPLETE", "Checklist tasks can only be acknowledged when the gate is complete.", task.Gate, "Error", []);
                }

                return null;
            },
            requireIntactAuditChain: true,
            ct: ct).ConfigureAwait(false);
    }

    private static OperationsTimelineEntryDto ToTimelineEntry(OperationsWorkflowAuditDto entry) =>
        new(
            entry.AuditId,
            entry.OccurredAtUtc,
            entry.WorkflowId,
            entry.FundAccountId,
            entry.PeriodId,
            entry.EventType,
            entry.FromState,
            entry.ToState,
            entry.Gate,
            entry.FromGateStatus,
            entry.ToGateStatus,
            entry.Actor,
            entry.Rationale,
            entry.CorrelationId,
            entry.CorrelationKeys,
            entry.References,
            entry.PreviousHash,
            entry.CurrentHash,
            entry.Outcome)
        { CloseChecklistSnapshot = entry.CloseChecklistSnapshot };

    private async Task<OperationsContinuityWorkflowDto> ToDtoAsync(OperationsContinuityWorkflow workflow, CancellationToken ct)
    {
        var timeline = await GetTimelineAsync(workflow.WorkflowId, ct).ConfigureAwait(false);
        return _readModelBuilder.Build(workflow, timeline, EvaluateCloseReadiness(workflow));
    }

    private static OperationsContinuityWorkflow CloneWorkflow(OperationsContinuityWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var json = JsonSerializer.Serialize(workflow, WorkflowCloneJsonOptions);
        return JsonSerializer.Deserialize<OperationsContinuityWorkflow>(json, WorkflowCloneJsonOptions)
            ?? throw new InvalidOperationException("Operations continuity workflow clone failed.");
    }

    private OperationsContinuityWorkflowSummaryDto ToSummary(OperationsContinuityWorkflow workflow)
    {
        var gates = workflow.Gates.Select(OperationsWorkflowReadModelBuilder.ToGateDto).ToArray();
        return new OperationsContinuityWorkflowSummaryDto(
            workflow.WorkflowId,
            workflow.FundAccountId,
            workflow.PeriodId,
            workflow.SecurityMasterSnapshotId,
            workflow.BrokerSource,
            _statusDerivation.Derive(workflow),
            workflow.Version,
            workflow.CreatedAtUtc,
            workflow.UpdatedAtUtc,
            gates,
            gates.SelectMany(static gate => gate.NextActions).ToArray(),
            workflow.LedgerBookId);
    }

    private static OperationsCloseReadinessDto EvaluateCloseReadiness(OperationsContinuityWorkflow workflow)
    {
        var components = new List<OperationsCloseReadinessComponentDto>(capacity: 9);
        var blockers = new List<OperationsCloseReadinessBlockerDto>();
        AddComponent(components, blockers, "security-master", "Security Master", 15,
            workflow.SecurityMasterGate.Status == OperationsGateStatusDto.Passed &&
                workflow.SecurityMasterState is OperationsSecurityMasterStateDto.Complete or OperationsSecurityMasterStateDto.OverridesApproved,
            "SECURITY_MASTER_RESOLUTION_REQUIRED",
            "Security Master mappings, overrides, and instrument confidence must be complete.",
            OperationsGateKeyDto.SecurityMaster,
            "/workstation/data");
        var brokerIngestReadinessBlocker = GetBrokerIngestReadinessBlocker(workflow);
        AddComponent(components, blockers, "provider-freshness", "Provider data freshness", 10,
            workflow.BrokerIngestGate.Status == OperationsGateStatusDto.Passed &&
                !HasBlocker(workflow.BrokerIngestGate, "BROKER_SYNC_STALE"),
            brokerIngestReadinessBlocker.Code,
            brokerIngestReadinessBlocker.Message,
            OperationsGateKeyDto.BrokerIngest,
            "/workstation/accounting",
            brokerIngestReadinessBlocker.Severity);
        AddComponent(components, blockers, "positions", "Positions", 10,
            workflow.BrokerIngestGate.Status == OperationsGateStatusDto.Passed &&
                workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Complete,
            "POSITION_COVERAGE_INCOMPLETE",
            "Broker or custodian position coverage is not complete.",
            OperationsGateKeyDto.BrokerIngest,
            "/workstation/accounting");
        AddComponent(components, blockers, "cash", "Cash", 10,
            workflow.BrokerIngestGate.Status == OperationsGateStatusDto.Passed &&
                workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Complete,
            "BROKER_CASH_COVERAGE_INCOMPLETE",
            "Broker or custodian cash activity coverage is not complete.",
            OperationsGateKeyDto.BrokerIngest,
            "/workstation/accounting");
        AddComponent(components, blockers, "ledger", "Ledger", 15,
            workflow.LedgerPostingGate.Status == OperationsGateStatusDto.Passed &&
                workflow.LedgerPostingState is OperationsLedgerPostingStateDto.Posted or OperationsLedgerPostingStateDto.Complete,
            "LEDGER_POSTING_REQUIRED",
            "Ledger posting state is not complete for close.",
            OperationsGateKeyDto.LedgerPosting,
            "/workstation/accounting");
        AddComponent(components, blockers, "pricing", "Pricing", 5,
            workflow.SecurityMasterGate.Status == OperationsGateStatusDto.Passed,
            "PRICING_COVERAGE_INCOMPLETE",
            "Pricing and valuation coverage is not complete.",
            OperationsGateKeyDto.SecurityMaster,
            "/workstation/data");
        AddComponent(components, blockers, "reconciliation", "Reconciliation", 15,
            workflow.ReconciliationGate.Status == OperationsGateStatusDto.Passed &&
                workflow.ReconciliationState == OperationsReconciliationStateDto.Complete &&
                workflow.BreakCases.All(static item => OperationsWorkflowReadModelBuilder.IsClosedBreakStatus(item.Status)),
            "RECONCILIATION_CRITICAL_BREAKS_OPEN",
            "Unresolved reconciliation breaks still require disposition.",
            OperationsGateKeyDto.Reconciliation,
            "/workstation/accounting");
        AddComponent(components, blockers, "reports", "Reports", 10,
            workflow.ReportPackReadiness.IsReady &&
                !string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId),
            "REPORT_PACK_REQUIRED",
            "Close evidence is incomplete or report pack is missing.",
            OperationsGateKeyDto.Approval,
            "/workstation/reporting");
        AddComponent(components, blockers, "approvals", "Approvals", 10,
            workflow.ApprovalGate.Status == OperationsGateStatusDto.Passed &&
                workflow.ApprovalState == OperationsApprovalStateDto.Approved,
            "APPROVAL_REQUIRED",
            "Close requires final approval before execution.",
            OperationsGateKeyDto.Approval,
            "/workstation/accounting");

        var score = components.Sum(static component => component.Score);
        var severity = score == 100 ? "Info" : "Critical";
        var actions = blockers.Select(static b => new OperationsNextActionDto(b.Code, b.Message, b.RouteHint, b.Gate)).ToArray();
        return new OperationsCloseReadinessDto(score == 100, severity, score, components, blockers, actions);
    }

    private static bool HasBlocker(OperationsGateState gate, string blockerCode) =>
        gate.Blockers.Any(blocker => string.Equals(blocker.Code, blockerCode, StringComparison.OrdinalIgnoreCase));

    private static (string Code, string Message, string Severity) GetBrokerIngestReadinessBlocker(OperationsContinuityWorkflow workflow)
    {
        var blocker = workflow.BrokerIngestGate.Blockers.FirstOrDefault(blocker =>
            string.Equals(blocker.Code, "BROKER_PROVIDER_REQUIRED_CAPABILITY_UNROUTABLE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(blocker.Code, "BROKER_PROVIDER_CAPABILITY_DEGRADED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(blocker.Code, "BROKER_SYNC_STALE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(blocker.Code, "BROKER_PROVIDER_ACCOUNT_UNLINKED", StringComparison.OrdinalIgnoreCase));
        return blocker is null
            ? ("BROKER_SYNC_STALE", "Provider data freshness is stale or has not been proven for this close.", "Critical")
            : (blocker.Code, blocker.Message, blocker.Severity);
    }

    private static void AddComponent(
        ICollection<OperationsCloseReadinessComponentDto> components,
        ICollection<OperationsCloseReadinessBlockerDto> blockers,
        string key,
        string label,
        int weight,
        bool isReady,
        string blockerCode,
        string blockingReason,
        OperationsGateKeyDto gate,
        string routeHint,
        string severity = "Critical")
    {
        components.Add(new OperationsCloseReadinessComponentDto(
            key,
            label,
            isReady ? weight : 0,
            weight,
            isReady,
            isReady ? "Info" : severity,
            isReady ? null : blockingReason,
            gate,
            routeHint));

        if (!isReady)
        {
            blockers.Add(new OperationsCloseReadinessBlockerDto(
                blockerCode,
                label,
                severity,
                blockingReason,
                gate,
                routeHint));
        }
    }

    private static IReadOnlyList<OperationsWorkflowBlockerDto> ValidateStartRequest(OperationsStartWorkflowRequestDto request)
    {
        var blockers = new List<OperationsWorkflowBlockerDto>();
        if (request.FundAccountId == Guid.Empty)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "FUND_ACCOUNT_REQUIRED",
                "Fund account id is required.",
                null,
                "Error",
                []));
        }

        if (string.IsNullOrWhiteSpace(request.PeriodId))
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "PERIOD_REQUIRED",
                "Accounting period id is required.",
                null,
                "Error",
                []));
        }

        if (string.IsNullOrWhiteSpace(request.Actor))
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "ACTOR_REQUIRED",
                "Actor is required for workflow transitions.",
                null,
                "Error",
                []));
        }

        if (request.LedgerBookId == Guid.Empty)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_BOOK_REQUIRED",
                "Ledger book id must be omitted for fund-level workflows or set to a valid ledger book id.",
                null,
                "Error",
                []));
        }

        return blockers;
    }

    private static bool WorkflowScopesCollide(Guid? existingLedgerBookId, Guid? requestedLedgerBookId)
        => existingLedgerBookId is null ||
           requestedLedgerBookId is null ||
           existingLedgerBookId == requestedLedgerBookId;

    private static string FormatLedgerBookScope(Guid? ledgerBookId)
        => ledgerBookId?.ToString("D") ?? "fund-level";

    private static IReadOnlyList<OperationsEvidenceLinkDto> EnsureReportPackEvidence(
        string? reportPackId,
        IReadOnlyList<OperationsEvidenceLinkDto>? evidenceLinks)
    {
        var normalized = OperationsContinuityWorkflowText.NormalizeEvidence(evidenceLinks).ToList();
        if (!string.IsNullOrWhiteSpace(reportPackId) &&
            normalized.All(link => !string.Equals(link.EvidenceId, reportPackId, StringComparison.OrdinalIgnoreCase)))
        {
            normalized.Add(new OperationsEvidenceLinkDto(
                reportPackId.Trim(),
                "Operations report pack",
                "/workstation/reporting",
                "report-pack",
                DateTimeOffset.UtcNow));
        }

        return normalized;
    }

    private static string BuildReopenGovernanceRationale(OperationsReopenWorkflowRequestDto request)
    {
        var parts = new List<string>();
        AddRationalePart(parts, "Rationale", request.Rationale);
        AddRationalePart(parts, "Justification", request.Justification);
        AddRationalePart(parts, "Approval reference", request.ApprovalReference);
        AddRationalePart(parts, "Impact summary", request.ImpactSummary);
        return string.Join(" | ", parts);
    }

    private static void AddRationalePart(List<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label}: {value.Trim()}");
        }
    }

    private static IReadOnlyList<OperationsEvidenceLinkDto> EnsureReopenGovernanceEvidence(
        OperationsReopenWorkflowRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto>? evidenceLinks)
    {
        var normalized = OperationsContinuityWorkflowText.NormalizeEvidence(evidenceLinks).ToList();
        EnsureReopenGovernanceEvidenceLink(
            normalized,
            request.IncidentId,
            "Workflow reopen incident",
            "incident");
        EnsureReopenGovernanceEvidenceLink(
            normalized,
            request.ApprovalReference,
            "Governed reopen approval reference",
            "approval-reference");

        return normalized;
    }

    private static void EnsureReopenGovernanceEvidenceLink(
        List<OperationsEvidenceLinkDto> normalized,
        string? evidenceId,
        string label,
        string source)
    {
        if (string.IsNullOrWhiteSpace(evidenceId) ||
            normalized.Any(link => string.Equals(link.EvidenceId, evidenceId.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        normalized.Add(new OperationsEvidenceLinkDto(
            evidenceId.Trim(),
            label,
            "/workstation/accounting",
            source,
            DateTimeOffset.UtcNow));
    }

    private static OperationsGateState GetGate(OperationsContinuityWorkflow workflow, OperationsGateKeyDto gate) =>
        gate switch
        {
            OperationsGateKeyDto.BrokerIngest => workflow.BrokerIngestGate,
            OperationsGateKeyDto.SecurityMaster => workflow.SecurityMasterGate,
            OperationsGateKeyDto.LedgerPosting => workflow.LedgerPostingGate,
            OperationsGateKeyDto.Reconciliation => workflow.ReconciliationGate,
            OperationsGateKeyDto.Approval => workflow.ApprovalGate,
            _ => throw new ArgumentOutOfRangeException(nameof(gate), gate, "Unsupported operations continuity gate.")
        };

    private static OperationsTransitionResultDto Success(OperationsContinuityWorkflowDto workflow)
    {
        var outcome = CreateOperationOutcome(
            workflow.WorkflowId,
            Math.Max(0, workflow.Version - 1),
            "operations-continuity-transition",
            "operations-continuity-service",
            null,
            null,
            OperationTerminalState.Succeeded,
            [],
            [],
            null,
            DateTimeOffset.UtcNow);
        return CreateResult(outcome, null, null, workflow, workflow.Blockers, workflow.NextActions);
    }

    private static OperationsWorkflowBlockerDto CreateClosedWorkflowBlocker(OperationsGateKeyDto? gate) =>
        new(
            "WORKFLOW_CLOSED",
            "Closed operations continuity workflows are immutable; reopen the workflow through the governed reopen command before applying further transitions.",
            gate,
            "Critical",
            []);

    private static OperationsTransitionResultDto? RejectAutomationMaterialAction(
        OperationsActionOriginDto actionOrigin,
        OperationsGateKeyDto? gate,
        string actionLabel)
    {
        if (OperationsOriginGuard.IsHumanOperator(actionOrigin))
        {
            return null;
        }

        var blocker = new OperationsWorkflowBlockerDto(
            "REVIEWED_AUTOMATION_MATERIAL_ACTION_REJECTED",
            OperationsOriginGuard.BlockerMessage(actionLabel),
            gate,
            "Critical",
            []);

        return Failure("REVIEWED_AUTOMATION_REVIEW_REQUIRED", blocker.Message, [blocker]);
    }

    private static OperationsTransitionResultDto Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<OperationsWorkflowBlockerDto> blockers)
    {
        var nextActions = BuildRecoveryNextActions(blockers);
        var outcome = CreateOperationOutcome(
            Guid.Empty,
            0,
            "operations-continuity-transition",
            "operations-continuity-service",
            null,
            null,
            blockers.Count > 0 ? OperationTerminalState.Blocked : OperationTerminalState.Failed,
            blockers,
            nextActions,
            errorMessage,
            DateTimeOffset.UtcNow);
        return CreateResult(outcome, errorCode, errorMessage, null, blockers, nextActions);
    }

}
