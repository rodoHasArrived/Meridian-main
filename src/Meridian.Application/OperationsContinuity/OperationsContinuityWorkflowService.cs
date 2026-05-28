using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Application.OperationsContinuity;

public interface IOperationsContinuityWorkflowService
{
    Task<OperationsTransitionResultDto> StartWorkflowAsync(OperationsStartWorkflowRequestDto request, CancellationToken ct = default);

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
        CancellationToken ct = default);

    Task<OperationsContinuityWorkflowDto?> GetAsync(Guid workflowId, CancellationToken ct = default);

    Task<IReadOnlyList<OperationsTimelineEntryDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default);
    Task<IReadOnlyList<OperationsCloseChecklistTaskDto>> GetChecklistAsync(Guid workflowId, CancellationToken ct = default);
    Task<OperationsTransitionResultDto> AcknowledgeChecklistTaskAsync(
        Guid workflowId,
        string taskId,
        OperationsChecklistAcknowledgeRequestDto request,
        CancellationToken ct = default);
}

public sealed class OperationsContinuityWorkflowService : IOperationsContinuityWorkflowService
{
    private static readonly Regex SensitiveAssignmentPattern = new(
        @"\b(?<key>api[_-]?key|secret|token|password|passphrase|client[_-]?secret|private[_-]?key|credential)\s*(?<separator>[:=])\s*(?<value>[^&;\s,]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BearerTokenPattern = new(
        @"\bbearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BasicAuthUriPattern = new(
        @"(?<scheme>https?://)(?<user>[^/@\s:]+):(?<password>[^/@\s]+)@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions WorkflowCloneJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IOperationsContinuityRepository _repository;
    private readonly IOperationsWorkflowAuditStore _auditStore;
    private readonly IOperationsStatusDerivationService _statusDerivation;
    private readonly ILedgerJournalStore? _ledgerJournalStore;
    private readonly IOperationsContinuityTransactionalCommitStore? _transactionalCommitStore;

    public OperationsContinuityWorkflowService(
        IOperationsContinuityRepository repository,
        IOperationsWorkflowAuditStore auditStore,
        IOperationsStatusDerivationService statusDerivation,
        ILedgerJournalStore? ledgerJournalStore = null,
        IOperationsContinuityTransactionalCommitStore? transactionalCommitStore = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _statusDerivation = statusDerivation ?? throw new ArgumentNullException(nameof(statusDerivation));
        _ledgerJournalStore = ledgerJournalStore;
        _transactionalCommitStore = transactionalCommitStore;
    }

    public async Task<OperationsTransitionResultDto> StartWorkflowAsync(
        OperationsStartWorkflowRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = ValidateStartRequest(request);
        if (validation.Count > 0)
        {
            return Failure("VALIDATION_FAILED", "Workflow start request is incomplete.", validation);
        }

        var existingWorkflows = await _repository
            .ListAsync(request.FundAccountId, request.PeriodId, status: null, ct)
            .ConfigureAwait(false);
        var openWorkflow = existingWorkflows.FirstOrDefault(static workflow => !workflow.IsClosed);
        if (openWorkflow is not null)
        {
            return Failure(
                "WORKFLOW_ALREADY_EXISTS",
                $"An operations continuity workflow already exists for fund account '{request.FundAccountId}' and period '{request.PeriodId.Trim()}'.",
                [
                    new OperationsWorkflowBlockerDto(
                        "OPERATIONS_CONTINUITY_WORKFLOW_ALREADY_EXISTS",
                        "Refresh the existing workflow instead of starting a duplicate close lane.",
                        null,
                        "Error",
                        [])
                ]);
        }

        var now = DateTimeOffset.UtcNow;
        var workflow = OperationsContinuityWorkflow.Start(
            Guid.NewGuid(),
            request.FundAccountId,
            request.PeriodId,
            request.SecurityMasterSnapshotId,
            request.BrokerSource,
            now);

        var evidence = NormalizeEvidence(request.EvidenceLinks);
        var auditDraft = new OperationsWorkflowAuditDraft(
            workflow.WorkflowId,
            workflow.FundAccountId,
            workflow.PeriodId,
            "workflow-started",
            OperationsWorkflowStatusDto.NotStarted,
            _statusDerivation.Derive(workflow),
            OperationsGateKeyDto.BrokerIngest,
            OperationsGateStatusDto.NotStarted,
            OperationsGateStatusDto.InProgress,
            request.Actor.Trim(),
            RedactSensitiveText(request.Rationale),
            RedactSensitiveText(request.CorrelationId),
            evidence);

        if (_auditStore is IOperationsContinuityWorkflowStartCommitStore startCommitStore)
        {
            var startCommit = await startCommitStore
                .CommitWorkflowStartAsync(workflow, auditDraft, ct)
                .ConfigureAwait(false);
            var committedDto = await ToDtoAsync(startCommit.Workflow, ct).ConfigureAwait(false);
            return Success(committedDto);
        }

        var audit = await _auditStore.AppendAsync(auditDraft, ct: ct).ConfigureAwait(false);

        workflow.Touch(audit.OccurredAtUtc);
        await _repository.SaveAsync(workflow, ct).ConfigureAwait(false);
        var dto = await ToDtoAsync(workflow, ct).ConfigureAwait(false);
        return Success(dto);
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
        var evidence = NormalizeEvidence(request.EvidenceLinks);
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
                RedactSensitiveText(request.Rationale),
                RedactSensitiveText(request.CorrelationId),
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

        var evidence = NormalizeEvidence(request.EvidenceLinks);
        var requestBlockers = ValidateLedgerPostRequest(request, evidence).ToList();
        var serviceBlockers = new List<OperationsWorkflowBlockerDto>();
        LedgerJournalEntryWrite? journalWrite = null;
        LedgerJournalEntryWrite builtJournalWrite = default!;
        if (requestBlockers.Count == 0)
        {
            if (_transactionalCommitStore is null && _ledgerJournalStore is null)
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

            if (requestBlockers.Count == 0 &&
                !TryBuildJournalWrite(workflow, request, out builtJournalWrite, out var journalBlockers))
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
            RedactSensitiveText(request.Rationale),
            RedactSensitiveText(request.CorrelationId),
            evidence);

        if (journalWrite is not null && _transactionalCommitStore is not null)
        {
            OperationsContinuityTransactionalCommitResult commitResult;
            try
            {
                commitResult = await _transactionalCommitStore
                    .CommitLedgerPostingAsync(workflowForCommit, auditDraft, journalWrite, ct)
                    .ConfigureAwait(false);
            }
            catch (LedgerValidationException ex)
            {
                return Failure(
                    "LEDGER_JOURNAL_APPEND_REJECTED",
                    "Ledger journal store rejected the posting candidate.",
                    [
                        new OperationsWorkflowBlockerDto(
                            "LEDGER_JOURNAL_APPEND_REJECTED",
                            ex.Message,
                            OperationsGateKeyDto.LedgerPosting,
                            "Critical",
                            evidence)
                    ]);
            }

            var transactionalDto = await ToDtoAsync(commitResult.Workflow, ct).ConfigureAwait(false);
            return Success(transactionalDto);
        }

        if (journalWrite is not null)
        {
            try
            {
                await _ledgerJournalStore!.AppendAsync(journalWrite, ct).ConfigureAwait(false);
            }
            catch (LedgerValidationException ex)
            {
                return Failure(
                    "LEDGER_JOURNAL_APPEND_REJECTED",
                    "Ledger journal store rejected the posting candidate.",
                    [
                        new OperationsWorkflowBlockerDto(
                            "LEDGER_JOURNAL_APPEND_REJECTED",
                            ex.Message,
                            OperationsGateKeyDto.LedgerPosting,
                            "Critical",
                            evidence)
                    ]);
            }
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
        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            EnsureIncidentEvidence(request.IncidentId, request.EvidenceLinks),
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
        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            request.EvidenceLinks,
            eventType: "reconciliation-break-resolved",
            gate: OperationsGateKeyDto.Reconciliation,
            precondition: null,
            command: (workflow, evidence, now) =>
            {
                var blocker = workflow.ResolveBreakCase(breakId, request, evidence, now);
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
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> ApproveWorkflowAsync(
        Guid workflowId,
        OperationsApprovalDecisionRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
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
            ct: ct).ConfigureAwait(false);
    }

    public async Task<OperationsTransitionResultDto> CloseWorkflowAsync(
        Guid workflowId,
        OperationsCloseWorkflowRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var existing = await _repository.GetAsync(workflowId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            var readiness = EvaluateCloseReadiness(existing);
            if (!readiness.IsReadyToClose)
            {
                await _auditStore.AppendAsync(new OperationsWorkflowAuditDraft(
                    existing.WorkflowId,
                    existing.FundAccountId,
                    existing.PeriodId,
                    "workflow-close-rejected",
                    _statusDerivation.Derive(existing),
                    _statusDerivation.Derive(existing),
                    OperationsGateKeyDto.Approval,
                    existing.ApprovalGate.Status,
                    existing.ApprovalGate.Status,
                    request.Actor?.Trim() ?? string.Empty,
                    RedactSensitiveText($"{request.Rationale} | close rejected: {string.Join("; ", readiness.Blockers.Select(static b => b.Code))}"),
                    RedactSensitiveText(request.CorrelationId),
                    EnsureReportPackEvidence(request.ReportPackId, request.EvidenceLinks)), ct).ConfigureAwait(false);

                return new OperationsTransitionResultDto(false, "CLOSE_READINESS_FAILED", "Close was rejected by fail-closed readiness gating.", null,
                    readiness.Blockers.Select(static b => new OperationsWorkflowBlockerDto(b.Code, b.Message, b.Gate, b.Severity, [])).ToArray(),
                    readiness.NextActions,
                    CloseReadiness: readiness);
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
            command: (workflow, _, now) =>
            {
                workflow.MarkClosed(now);
                return null;
            },
            requireIntactAuditChain: true,
            ct: ct).ConfigureAwait(false);

        return result with { CloseReadiness = result.Workflow?.CloseReadiness };
    }

    private async Task<OperationsTransitionResultDto> ApplyCommandAsync(
        Guid workflowId,
        long expectedVersion,
        string actor,
        string? rationale,
        string? correlationId,
        IReadOnlyList<OperationsEvidenceLinkDto>? evidenceLinks,
        string eventType,
        OperationsGateKeyDto? gate,
        Func<OperationsContinuityWorkflow, OperationsWorkflowBlockerDto?>? precondition = null,
        Func<OperationsContinuityWorkflow, IReadOnlyList<OperationsEvidenceLinkDto>, DateTimeOffset, OperationsWorkflowBlockerDto?>? command = null,
        bool allowClosedWorkflow = false,
        bool requireIntactAuditChain = false,
        CancellationToken ct = default)
    {
        if (workflowId == Guid.Empty)
        {
            return Failure("VALIDATION_FAILED", "Workflow id is required.",
            [
                new OperationsWorkflowBlockerDto("WORKFLOW_ID_REQUIRED", "Workflow id is required.", null, "Error", [])
            ]);
        }

        if (string.IsNullOrWhiteSpace(actor))
        {
            return Failure("VALIDATION_FAILED", "Actor is required for workflow transitions.",
            [
                new OperationsWorkflowBlockerDto("ACTOR_REQUIRED", "Actor is required for workflow transitions.", gate, "Error", [])
            ]);
        }

        var workflow = await _repository.GetAsync(workflowId, ct).ConfigureAwait(false);
        if (workflow is null)
        {
            return Failure("NOT_FOUND", "Workflow was not found.", []);
        }

        if (workflow.Version != expectedVersion)
        {
            return Failure(
                "VERSION_MISMATCH",
                $"Workflow version {workflow.Version} does not match expected version {expectedVersion}.",
                [
                    new OperationsWorkflowBlockerDto(
                        "WORKFLOW_VERSION_MISMATCH",
                        "Refresh the workflow before retrying the command.",
                        null,
                        "Error",
                        [])
                ]);
        }

        if (workflow.IsClosed && !allowClosedWorkflow)
        {
            var blocker = CreateClosedWorkflowBlocker(gate);
            return Failure("INVALID_STATE_TRANSITION", blocker.Message, [blocker]);
        }

        if (precondition?.Invoke(workflow) is { } preconditionBlocker)
        {
            return Failure("INVALID_STATE_TRANSITION", preconditionBlocker.Message, [preconditionBlocker]);
        }

        if (requireIntactAuditChain)
        {
            var auditTimeline = await _auditStore.GetTimelineAsync(workflow.WorkflowId, ct).ConfigureAwait(false);
            if (!OperationsWorkflowAuditHashing.TryValidateChain(auditTimeline, out var auditBlockerCode, out var auditMessage))
            {
                var blocker = new OperationsWorkflowBlockerDto(
                    auditBlockerCode,
                    auditMessage,
                    gate,
                    "Critical",
                    []);
                return Failure("INVALID_STATE_TRANSITION", blocker.Message, [blocker]);
            }
        }

        var fromStatus = _statusDerivation.Derive(workflow);
        var fromGateStatus = gate.HasValue ? GetGate(workflow, gate.Value).Status : (OperationsGateStatusDto?)null;
        var evidence = NormalizeEvidence(evidenceLinks);
        var now = DateTimeOffset.UtcNow;
        var workflowForCommit = CloneWorkflow(workflow);
        if (command?.Invoke(workflowForCommit, evidence, now) is { } commandBlocker)
        {
            return Failure("INVALID_STATE_TRANSITION", commandBlocker.Message, [commandBlocker]);
        }

        var toStatus = _statusDerivation.Derive(workflowForCommit);
        var toGateStatus = gate.HasValue ? GetGate(workflowForCommit, gate.Value).Status : (OperationsGateStatusDto?)null;
        var audit = await _auditStore.AppendAsync(
            new OperationsWorkflowAuditDraft(
                workflowForCommit.WorkflowId,
                workflowForCommit.FundAccountId,
                workflowForCommit.PeriodId,
                eventType,
                fromStatus,
                toStatus,
                gate,
                fromGateStatus,
                toGateStatus,
                actor.Trim(),
                RedactSensitiveText(rationale),
                RedactSensitiveText(correlationId),
                evidence),
            ct).ConfigureAwait(false);

        workflowForCommit.Touch(audit.OccurredAtUtc);
        await _repository.SaveAsync(workflowForCommit, ct).ConfigureAwait(false);
        var dto = await ToDtoAsync(workflowForCommit, ct).ConfigureAwait(false);
        return Success(dto);
    }

    public async Task<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>> ListAsync(
        Guid? fundAccountId = null,
        string? periodId = null,
        OperationsWorkflowStatusDto? status = null,
        CancellationToken ct = default)
    {
        var workflows = await _repository.ListAsync(fundAccountId, periodId, status, ct).ConfigureAwait(false);
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
        return BuildChecklist(workflow, timeline);
    }

    public async Task<OperationsTransitionResultDto> AcknowledgeChecklistTaskAsync(Guid workflowId, string taskId, OperationsChecklistAcknowledgeRequestDto request, CancellationToken ct = default)
    {
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

        return await ApplyCommandAsync(
            workflowId,
            request.ExpectedVersion,
            request.Actor,
            request.Rationale,
            request.CorrelationId,
            evidenceLinks: null,
            "checklist-task-acknowledged",
            task.Gate,
            command: (workflow, _, now) =>
            {
                var gate = GetGate(workflow, task.Gate);
                if (gate.Status != OperationsGateStatusDto.Passed)
                {
                    return new OperationsWorkflowBlockerDto("CHECKLIST_GATE_NOT_COMPLETE", "Checklist tasks can only be acknowledged when the gate is complete.", task.Gate, "Error", []);
                }

                workflow.ReplaceGate(gate.WithStatus(
                    gate.Status,
                    gate.Blockers,
                    gate.NextActions,
                    gate.CompletedAtUtc ?? now,
                    request.Actor.Trim()));
                return null;
            },
            ct: ct).ConfigureAwait(false);
    }

    private async Task<OperationsContinuityWorkflowDto> ToDtoAsync(OperationsContinuityWorkflow workflow, CancellationToken ct)
    {
        var timeline = await GetTimelineAsync(workflow.WorkflowId, ct).ConfigureAwait(false);
        var gates = workflow.Gates.Select(ToGateDto).ToArray();
        var blockers = gates.SelectMany(static gate => gate.Blockers).ToArray();
        var nextActions = gates.SelectMany(static gate => gate.NextActions).ToArray();
        var evidenceLinks = timeline.SelectMany(static entry => entry.References)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new OperationsContinuityWorkflowDto(
            workflow.WorkflowId,
            workflow.FundAccountId,
            workflow.PeriodId,
            workflow.SecurityMasterSnapshotId,
            workflow.BrokerSource,
            workflow.CreatedAtUtc,
            workflow.UpdatedAtUtc,
            workflow.Version,
            _statusDerivation.Derive(workflow),
            workflow.BrokerIntakeState,
            workflow.SecurityMasterState,
            workflow.LedgerPostingState,
            workflow.ReconciliationState,
            workflow.ApprovalState,
            gates,
            timeline,
            workflow.BreakCases,
            workflow.LedgerPreview,
            workflow.Approvals,
            workflow.ReportPackReadiness,
            BuildChecklist(workflow, timeline),
            evidenceLinks,
            blockers,
            nextActions,
            EvaluateCloseReadiness(workflow));
    }

    private static IReadOnlyList<OperationsCloseChecklistTaskDto> BuildChecklist(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline)
    {
        var dueBase = DateOnly.FromDateTime(workflow.CreatedAtUtc.UtcDateTime).AddDays(2);
        return workflow.Gates.Select((gate, index) =>
        {
            var evidence = timeline.SelectMany(static entry => entry.References).FirstOrDefault(link =>
                string.Equals(link.Source, "operations-continuity", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(link.Source, gate.GateKey.ToString(), StringComparison.OrdinalIgnoreCase));
            var status = gate.Status switch
            {
                OperationsGateStatusDto.Passed => "Done",
                OperationsGateStatusDto.Blocked => "Blocked",
                OperationsGateStatusDto.InProgress => "InProgress",
                _ => "Pending"
            };

            return new OperationsCloseChecklistTaskDto(
                $"close-gate-{gate.GateKey}".ToLowerInvariant(),
                gate.GateKey,
                $"{DisplayName(gate.GateKey)} close gate",
                gate.CompletedBy ?? "accounting-operator",
                RequiredEvidence: "Evidence link and gate completion audit",
                RequiredApprovalCount: gate.GateKey == OperationsGateKeyDto.Approval ? 2 : 1,
                ExpiresOn: dueBase.AddDays(index + 5),
                dueBase.AddDays(index),
                status,
                gate.Blockers.FirstOrDefault()?.Message,
                evidence?.EvidenceId,
                gate.NextActions.FirstOrDefault()?.Route,
                CanAcknowledge: gate.Status == OperationsGateStatusDto.Passed && evidence is not null,
                gate.CompletedAtUtc,
                gate.CompletedBy);
        }).ToArray();
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
        var gates = workflow.Gates.Select(ToGateDto).ToArray();
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
            gates.SelectMany(static gate => gate.NextActions).ToArray());
    }

    private static OperationsCloseReadinessDto EvaluateCloseReadiness(OperationsContinuityWorkflow workflow)
    {
        var blockers = new List<OperationsCloseReadinessBlockerDto>();
        if (workflow.BreakCases.Any(static item => !string.Equals(item.Status, "closed", StringComparison.OrdinalIgnoreCase) && !string.Equals(item.Status, "resolved", StringComparison.OrdinalIgnoreCase)))
        {
            blockers.Add(new("RECONCILIATION_BREAKS_OPEN", "Breaks", "Critical", "Unresolved reconciliation breaks still require disposition.", OperationsGateKeyDto.Reconciliation, "/workstation/accounting"));
        }

        if (workflow.ApprovalState != OperationsApprovalStateDto.Approved)
        {
            blockers.Add(new("APPROVAL_MISSING", "Approvals", "Critical", "Close requires final approval before execution.", OperationsGateKeyDto.Approval, "/workstation/accounting"));
        }

        if (workflow.LedgerPostingState != OperationsLedgerPostingStateDto.Posted && workflow.LedgerPostingState != OperationsLedgerPostingStateDto.Complete)
        {
            blockers.Add(new("POSTING_INCOMPLETE", "Posting", "Critical", "Ledger posting state is not complete for close.", OperationsGateKeyDto.LedgerPosting, "/workstation/accounting"));
        }

        if (!workflow.ReportPackReadiness.IsReady || string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId))
        {
            blockers.Add(new("EVIDENCE_INCOMPLETE", "Evidence", "Critical", "Close evidence is incomplete or report pack is missing.", OperationsGateKeyDto.Approval, "/workstation/reporting"));
        }

        var actions = blockers.Select(static b => new OperationsNextActionDto(b.Code, b.Message, b.RouteHint, b.Gate)).ToArray();
        return new OperationsCloseReadinessDto(blockers.Count == 0, blockers.Count == 0 ? "Info" : "Critical", blockers, actions);
    }

    private static OperationsGateDto ToGateDto(OperationsGateState gate) =>
        new(
            gate.GateKey,
            DisplayName(gate.GateKey),
            gate.Status,
            IsRequired: true,
            Description(gate.GateKey) ,
            gate.Blockers,
            gate.NextActions,
            gate.CompletedAtUtc,
            gate.CompletedBy);

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
            entry.CurrentHash);

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

        return blockers;
    }

    private static IReadOnlyList<OperationsEvidenceLinkDto> NormalizeEvidence(
        IReadOnlyList<OperationsEvidenceLinkDto>? evidenceLinks) =>
        evidenceLinks?
            .Where(static link => !string.IsNullOrWhiteSpace(link.EvidenceId))
            .Select(static link => link with
            {
                // Evidence ids are the durable lineage key for report packs, incidents, and audit joins.
                EvidenceId = link.EvidenceId.Trim(),
                Label = RedactSensitiveText(link.Label) ?? string.Empty,
                Route = RedactSensitiveText(link.Route),
                Source = RedactSensitiveText(link.Source)
            })
            .ToArray() ?? [];

    private static string? RedactSensitiveText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var redacted = SensitiveAssignmentPattern.Replace(
            value,
            match => $"{match.Groups["key"].Value}{match.Groups["separator"].Value}[redacted]");
        redacted = BearerTokenPattern.Replace(redacted, "Bearer [redacted]");
        return BasicAuthUriPattern.Replace(redacted, "${scheme}[redacted]@");
    }

    private static IReadOnlyList<OperationsWorkflowBlockerDto> ValidateLedgerPostRequest(
        OperationsLedgerPostRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks)
    {
        var blockers = new List<OperationsWorkflowBlockerDto>();
        if (string.IsNullOrWhiteSpace(request.LedgerBatchId))
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_BATCH_ID_REQUIRED",
                "Ledger posting must return a durable ledger batch id.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        if (string.IsNullOrWhiteSpace(request.PostingKind))
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_POSTING_KIND_REQUIRED",
                "Ledger posting kind is required.",
                OperationsGateKeyDto.LedgerPosting,
                "Error",
                evidenceLinks));
        }

        if (!request.HasValidatedJournal)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_VALIDATED_JOURNAL_REQUIRED",
                "Ledger posting requires a validated journal draft.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        if (!request.PeriodOpen)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_PERIOD_CLOSED",
                "Ledger posting into a closed or hard-closed period requires a governed reopen path before adjustment posting.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        if (request.HasDuplicatePostingCandidate)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_DUPLICATE_POSTING_CANDIDATE",
                "Duplicate posting candidate detected for this source activity or generated accounting event.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        return blockers;
    }

    private static bool TryBuildJournalWrite(
        OperationsContinuityWorkflow workflow,
        OperationsLedgerPostRequestDto request,
        out LedgerJournalEntryWrite journalWrite,
        out IReadOnlyList<OperationsWorkflowBlockerDto> blockers)
    {
        journalWrite = default!;
        var candidate = request.JournalCandidate;
        if (candidate is null)
        {
            blockers =
            [
                new OperationsWorkflowBlockerDto(
                    "LEDGER_JOURNAL_CANDIDATE_REQUIRED",
                    "Ledger posting requires a journal candidate that can be appended to the durable ledger journal store.",
                    OperationsGateKeyDto.LedgerPosting,
                    "Critical",
                    NormalizeEvidence(request.EvidenceLinks))
            ];
            return false;
        }

        var validationBlockers = ValidateJournalCandidate(workflow, request, candidate, request.EvidenceLinks);
        if (validationBlockers.Count > 0)
        {
            blockers = validationBlockers;
            return false;
        }

        try
        {
            var journalEntryId = candidate.JournalEntryId.GetValueOrDefault();
            if (journalEntryId == Guid.Empty)
            {
                journalEntryId = Guid.NewGuid();
            }

            var description = candidate.Description.Trim();
            var lines = candidate.Lines
                .Select(line =>
                {
                    _ = Enum.TryParse<LedgerAccountType>(line.AccountType, ignoreCase: true, out var accountType);
                    return new LedgerEntry(
                        line.EntryId.GetValueOrDefault() == Guid.Empty ? Guid.NewGuid() : line.EntryId.GetValueOrDefault(),
                        journalEntryId,
                        candidate.Timestamp,
                        new LedgerAccount(
                            line.AccountName.Trim(),
                            accountType,
                            NormalizeOptional(line.Symbol),
                            NormalizeOptional(line.FinancialAccountId)),
                        line.Debit,
                        line.Credit,
                        description);
                })
                .ToArray();

            var entry = new JournalEntry(
                journalEntryId,
                candidate.Timestamp,
                description,
                lines,
                ToJournalEntryMetadata(candidate));

            journalWrite = new LedgerJournalEntryWrite(
                entry,
                candidate.AggregateId,
                candidate.PeriodId,
                candidate.CommandId,
                candidate.CorrelationId,
                candidate.AccountingBasis,
                NormalizePolicy(candidate.AccountingPolicyId),
                NormalizePolicy(candidate.AccountingPolicyVersion),
                NormalizeOptional(candidate.RuleId),
                NormalizeOptional(candidate.RuleVersion),
                candidate.SourceEventId,
                candidate.SourceJournalEntryId,
                candidate.PostingKind,
                candidate.AdjustmentApproval);
            blockers = [];
            return true;
        }
        catch (LedgerValidationException ex)
        {
            blockers =
            [
                new OperationsWorkflowBlockerDto(
                    "LEDGER_JOURNAL_CANDIDATE_INVALID",
                    ex.Message,
                    OperationsGateKeyDto.LedgerPosting,
                    "Critical",
                    NormalizeEvidence(request.EvidenceLinks))
            ];
            return false;
        }
    }

    private static IReadOnlyList<OperationsWorkflowBlockerDto> ValidateJournalCandidate(
        OperationsContinuityWorkflow workflow,
        OperationsLedgerPostRequestDto request,
        OperationsLedgerJournalCandidateDto candidate,
        IReadOnlyList<OperationsEvidenceLinkDto>? evidenceLinks)
    {
        var evidence = NormalizeEvidence(evidenceLinks);
        var blockers = new List<OperationsWorkflowBlockerDto>();
        if (candidate.AggregateId == Guid.Empty)
        {
            blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_AGGREGATE_ID_REQUIRED", "Ledger journal candidate aggregate id is required.", evidence));
        }

        if (candidate.PeriodId == Guid.Empty)
        {
            blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_PERIOD_ID_REQUIRED", "Ledger journal candidate period id is required.", evidence));
        }
        else if (TryResolveWorkflowPeriodGuid(workflow.PeriodId, out var workflowPeriodId) && candidate.PeriodId != workflowPeriodId)
        {
            blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_PERIOD_ID_MISMATCH", "Ledger journal candidate period id must match the workflow period.", evidence));
        }

        if (candidate.AggregateId != Guid.Empty && candidate.AggregateId != workflow.FundAccountId)
        {
            blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_AGGREGATE_ID_MISMATCH", "Ledger journal candidate aggregate id must match the workflow fund account.", evidence));
        }

        if (candidate.CommandId.GetValueOrDefault() == Guid.Empty ||
            string.IsNullOrWhiteSpace(candidate.IdempotencyKey))
        {
            blockers.Add(CreateJournalCandidateBlocker(
                "LEDGER_IDEMPOTENCY_KEY_MISSING",
                "Ledger journal candidate requires a durable command id and idempotency key before posting.",
                evidence));
        }

        if (candidate.Metadata?.SecurityId is null ||
            string.IsNullOrWhiteSpace(candidate.SecurityMasterProvenance))
        {
            blockers.Add(CreateJournalCandidateBlocker(
                "LEDGER_JOURNAL_PROVENANCE_MISSING",
                "Ledger journal candidate requires Security Master security id and provenance before posting.",
                evidence));
        }

        if (candidate.Timestamp == default)
        {
            blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_TIMESTAMP_REQUIRED", "Ledger journal candidate timestamp is required.", evidence));
        }

        if (string.IsNullOrWhiteSpace(candidate.Description))
        {
            blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_DESCRIPTION_REQUIRED", "Ledger journal candidate description is required.", evidence));
        }

        if (candidate.Lines is null || candidate.Lines.Count == 0)
        {
            blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_LINES_REQUIRED", "Ledger journal candidate requires at least one debit or credit line.", evidence));
        }

        foreach (var line in candidate.Lines ?? [])
        {
            if (string.IsNullOrWhiteSpace(line.AccountName))
            {
                blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_ACCOUNT_NAME_REQUIRED", "Every ledger journal candidate line requires an account name.", evidence));
            }

            if (!Enum.TryParse<LedgerAccountType>(line.AccountType, ignoreCase: true, out _))
            {
                blockers.Add(CreateJournalCandidateBlocker(
                    "LEDGER_JOURNAL_ACCOUNT_TYPE_INVALID",
                    $"Ledger journal candidate account type '{line.AccountType}' is invalid.",
                    evidence));
            }
        }

        var totalDebits = candidate.Lines?.Sum(static line => line.Debit) ?? 0m;
        var totalCredits = candidate.Lines?.Sum(static line => line.Credit) ?? 0m;
        if (Math.Abs(totalDebits - totalCredits) > 0.000001m)
        {
            blockers.Add(CreateJournalCandidateBlocker(
                "LEDGER_DRAFT_IMBALANCED",
                "Ledger journal candidate debit and credit totals must balance before posting.",
                evidence));
        }

        return blockers;
    }

    private static bool TryResolveWorkflowPeriodGuid(string workflowPeriodId, out Guid resolvedPeriodId) =>
        Guid.TryParse(workflowPeriodId, out resolvedPeriodId);

    private static OperationsWorkflowBlockerDto CreateJournalCandidateBlocker(
        string code,
        string message,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks) =>
        new(code, message, OperationsGateKeyDto.LedgerPosting, "Critical", evidenceLinks);

    private static JournalEntryMetadata? ToJournalEntryMetadata(OperationsLedgerJournalCandidateDto candidate)
    {
        var metadata = candidate.Metadata;
        if (metadata is null)
        {
            return null;
        }

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (metadata.Tags is not null)
        {
            foreach (var pair in metadata.Tags)
            {
                tags[pair.Key] = pair.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate.IdempotencyKey))
        {
            tags["operationsContinuityIdempotencyKey"] = candidate.IdempotencyKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(candidate.SecurityMasterProvenance))
        {
            tags["securityMasterProvenance"] = candidate.SecurityMasterProvenance.Trim();
        }

        return new JournalEntryMetadata(
            ActivityType: NormalizeOptional(metadata.ActivityType),
            Symbol: NormalizeOptional(metadata.Symbol),
            SecurityId: metadata.SecurityId,
            OrderId: metadata.OrderId,
            FillId: metadata.FillId,
            ProjectId: NormalizeOptional(metadata.ProjectId),
            LedgerBook: NormalizeOptional(metadata.LedgerBook),
            LedgerView: null,
            ScenarioId: NormalizeOptional(metadata.ScenarioId),
            StrategyId: NormalizeOptional(metadata.StrategyId),
            FinancialAccountId: NormalizeOptional(metadata.FinancialAccountId),
            CounterpartyAccountId: NormalizeOptional(metadata.CounterpartyAccountId),
            Institution: NormalizeOptional(metadata.Institution),
            Tags: tags.Count == 0 ? null : tags);
    }

    private static string NormalizePolicy(string value) =>
        string.IsNullOrWhiteSpace(value) ? "legacy-v1" : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<OperationsEvidenceLinkDto> EnsureReportPackEvidence(
        string? reportPackId,
        IReadOnlyList<OperationsEvidenceLinkDto>? evidenceLinks)
    {
        var normalized = NormalizeEvidence(evidenceLinks).ToList();
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

    private static IReadOnlyList<OperationsEvidenceLinkDto> EnsureIncidentEvidence(
        string? incidentId,
        IReadOnlyList<OperationsEvidenceLinkDto>? evidenceLinks)
    {
        var normalized = NormalizeEvidence(evidenceLinks).ToList();
        if (!string.IsNullOrWhiteSpace(incidentId) &&
            normalized.All(link => !string.Equals(link.EvidenceId, incidentId, StringComparison.OrdinalIgnoreCase)))
        {
            normalized.Add(new OperationsEvidenceLinkDto(
                incidentId.Trim(),
                "Workflow reopen incident",
                "/workstation/accounting",
                "incident",
                DateTimeOffset.UtcNow));
        }

        return normalized;
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

    private static OperationsTransitionResultDto Success(OperationsContinuityWorkflowDto workflow) =>
        new(true, null, null, workflow, workflow.Blockers, workflow.NextActions);

    private static OperationsWorkflowBlockerDto CreateClosedWorkflowBlocker(OperationsGateKeyDto? gate) =>
        new(
            "WORKFLOW_CLOSED",
            "Closed operations continuity workflows are immutable; reopen the workflow through the governed reopen command before applying further transitions.",
            gate,
            "Critical",
            []);

    private static OperationsTransitionResultDto Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<OperationsWorkflowBlockerDto> blockers) =>
        new(false, errorCode, errorMessage, null, blockers, []);

    private static string DisplayName(OperationsGateKeyDto gateKey) => gateKey switch
    {
        OperationsGateKeyDto.BrokerIngest => "Broker, custodian, and bank intake",
        OperationsGateKeyDto.SecurityMaster => "Security Master resolution",
        OperationsGateKeyDto.LedgerPosting => "Ledger draft and posting",
        OperationsGateKeyDto.Reconciliation => "Reconciliation",
        OperationsGateKeyDto.Approval => "Approval and close readiness",
        _ => gateKey.ToString()
    };

    private static string Description(OperationsGateKeyDto gateKey) => gateKey switch
    {
        OperationsGateKeyDto.BrokerIngest => "Imports and normalizes external account activity before accounting use.",
        OperationsGateKeyDto.SecurityMaster => "Requires authoritative instrument identity, provenance, and accounting classifications.",
        OperationsGateKeyDto.LedgerPosting => "Controls journal preview, validation, idempotency, and posting readiness.",
        OperationsGateKeyDto.Reconciliation => "Connects expected Security Master events, actual activity, and ledger postings.",
        OperationsGateKeyDto.Approval => "Requires operator, reviewer, rationale, and linked evidence before close.",
        _ => gateKey.ToString()
    };
}
