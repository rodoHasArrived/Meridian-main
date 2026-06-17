using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.OperationsContinuity;

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

    private static readonly string[] ReviewedAutomationAllowedUseCases =
    [
        "Extract source fields",
        "Classify source records",
        "Suggest reconciliation matches",
        "Explain variances",
        "Detect duplicate records",
        "Draft journal templates",
        "Summarize retained evidence",
        "Flag missing support",
        "Draft report commentary",
        "Draft audit request lists"
    ];

    private static readonly string[] ReviewedAutomationProhibitedActions =
    [
        "Approve its own work",
        "Post material journals without approval",
        "Override period locks",
        "Release payments",
        "Publish reports",
        "Edit posted entries",
        "Erase evidence"
    ];

    private static readonly string[] RequiredReconciliationCoverageLaneIds =
    [
        "cash-reconciliation",
        "position-reconciliation",
        "trade-reconciliation",
        "income-reconciliation",
        "mbs-factor-reconciliation",
        "bank-reconciliation",
        "gl-reconciliation"
    ];

    private readonly IOperationsContinuityRepository _repository;
    private readonly IOperationsWorkflowAuditStore _auditStore;
    private readonly IOperationsStatusDerivationService _statusDerivation;
    private readonly ILedgerJournalStore? _ledgerJournalStore;
    private readonly IOperationsContinuityTransactionalCommitStore? _transactionalCommitStore;
    private readonly ISecurityMasterQueryService? _securityMasterQueryService;

    public OperationsContinuityWorkflowService(
        IOperationsContinuityRepository repository,
        IOperationsWorkflowAuditStore auditStore,
        IOperationsStatusDerivationService statusDerivation,
        ILedgerJournalStore? ledgerJournalStore = null,
        IOperationsContinuityTransactionalCommitStore? transactionalCommitStore = null,
        ISecurityMasterQueryService? securityMasterQueryService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _statusDerivation = statusDerivation ?? throw new ArgumentNullException(nameof(statusDerivation));
        _ledgerJournalStore = ledgerJournalStore;
        _transactionalCommitStore = transactionalCommitStore;
        _securityMasterQueryService = securityMasterQueryService;
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

            var authoritativeSecurityStatuses = requestBlockers.Count == 0
                ? await ResolveAuthoritativeSecurityStatusesAsync(request.JournalCandidate?.Lines, ct).ConfigureAwait(false)
                : new Dictionary<Guid, SecurityStatusDto>();

            if (requestBlockers.Count == 0 &&
                !TryBuildJournalWrite(workflow, request, authoritativeSecurityStatuses, out builtJournalWrite, out var journalBlockers))
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
            command: (workflow, evidence, now) =>
            {
                workflow.MarkClosed(request, evidence, now);
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
        var closeReadiness = EvaluateCloseReadiness(workflow);
        var accountingRecordSummary = BuildAccountingRecordSummary(workflow, timeline, evidenceLinks);
        var evidencePackages = BuildEvidencePackages(workflow, timeline, accountingRecordSummary, evidenceLinks);
        var dashboardSummary = BuildDashboardSummary(
            workflow,
            timeline,
            closeReadiness,
            accountingRecordSummary,
            evidencePackages,
            evidenceLinks);
        var reviewedAutomation = BuildReviewedAutomationSummary(workflow, evidenceLinks);

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
            closeReadiness,
            workflow.ClosePackage,
            accountingRecordSummary,
            workflow.ReconciliationLanes,
            dashboardSummary,
            evidencePackages,
            reviewedAutomation);
    }

    private static OperationsReviewedAutomationSummaryDto BuildReviewedAutomationSummary(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks)
    {
        if (workflow.IsClosed || workflow.ClosePackage is not null)
        {
            var retainedEvidence = (workflow.ClosePackage?.EvidenceLinks ?? evidenceLinks)
                .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return ReviewedAutomationSummary(
                stage: "Reviewed evidence retained",
                status: EvidenceStatusDto.Ready,
                requiresHumanReview: false,
                summary: "Reviewed automation suggestions are closed behind retained approval and evidence package history.",
                evidenceLinks: retainedEvidence,
                requiredActions: []);
        }

        if (workflow.BrokerIntakeState is OperationsBrokerIntakeStateDto.Imported or OperationsBrokerIntakeStateDto.Normalized)
        {
            return ReviewedAutomationSummary(
                stage: "Extraction review",
                status: EvidenceStatusDto.ReviewRequired,
                requiresHumanReview: true,
                summary: "Automation may extract and classify intake data, but normalized source records require review before matching.",
                evidenceLinks,
                requiredActions:
                [
                    "Review extracted fields and classification confidence before reconciliation.",
                    "Retain intake evidence before downstream ledger or report usage."
                ]);
        }

        if (workflow.ReconciliationState == OperationsReconciliationStateDto.AutoMatched)
        {
            return ReviewedAutomationSummary(
                stage: "Suggested matches require review",
                status: EvidenceStatusDto.ReviewRequired,
                requiresHumanReview: true,
                summary: "Automation may suggest reconciliation matches, but match promotion remains operator-reviewed.",
                evidenceLinks,
                requiredActions:
                [
                    "Review suggested matches, variance explanations, and duplicate flags before approval.",
                    "Resolve or approve exceptions before downstream ledger, close, or reporting actions."
                ]);
        }

        if (workflow.LedgerPostingState is OperationsLedgerPostingStateDto.Drafted or OperationsLedgerPostingStateDto.Validated)
        {
            return ReviewedAutomationSummary(
                stage: "Journal draft review",
                status: EvidenceStatusDto.ReviewRequired,
                requiresHumanReview: true,
                summary: "Automation may draft journal templates, but material journal posting remains approval-gated.",
                evidenceLinks,
                requiredActions:
                [
                    "Review the ledger draft and retained source evidence before posting.",
                    "Do not post material journals from automation output without governed approval."
                ]);
        }

        if (workflow.ReportPackReadiness.IsReady &&
            workflow.ApprovalState == OperationsApprovalStateDto.Pending)
        {
            return ReviewedAutomationSummary(
                stage: "Report commentary and audit request list draft review",
                status: EvidenceStatusDto.ReviewRequired,
                requiresHumanReview: true,
                summary: "Automation may draft report commentary and audit request lists from retained evidence, but report approval and publication remain human-gated.",
                evidenceLinks,
                requiredActions:
                [
                    "Review drafted report commentary and audit request lists against retained evidence before submission.",
                    "Do not publish reports or release support packages from automation output without governed approval."
                ]);
        }

        if (workflow.ApprovalState is OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned)
        {
            return ReviewedAutomationSummary(
                stage: "Reviewer approval required",
                status: EvidenceStatusDto.ReviewRequired,
                requiresHumanReview: true,
                summary: "Automation may draft or summarize support, but approval remains a human reviewer control.",
                evidenceLinks,
                requiredActions:
                [
                    "Complete reviewer approval before close evidence can be released.",
                    "Retain approval rationale and evidence links before publishing reports or closing the period."
                ]);
        }

        return ReviewedAutomationSummary(
            stage: "Suggestions only",
            status: EvidenceStatusDto.ReviewRequired,
            requiresHumanReview: true,
            summary: "Automation is limited to suggestions, summaries, drafts, and missing-support flags until an operator reviews the workflow.",
            evidenceLinks,
            requiredActions:
            [
                "Keep automation output in the review queue before approval, posting, publication, payment, or evidence-retention actions."
            ]);
    }

    private static OperationsReviewedAutomationSummaryDto ReviewedAutomationSummary(
        string stage,
        EvidenceStatusDto status,
        bool requiresHumanReview,
        string summary,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        IReadOnlyList<string> requiredActions)
        => new(
            SummaryId: "reviewed-automation",
            Stage: stage,
            Status: status,
            RequiresHumanReview: requiresHumanReview,
            Summary: summary,
            AllowedUseCases: ReviewedAutomationAllowedUseCases,
            ProhibitedActions: ReviewedAutomationProhibitedActions,
            EvidenceLinks: evidenceLinks,
            RequiredActions: requiredActions,
            Artifacts: BuildReviewedAutomationArtifacts(stage, status, requiresHumanReview, evidenceLinks, requiredActions));

    private static IReadOnlyList<OperationsReviewedAutomationArtifactDto> BuildReviewedAutomationArtifacts(
        string stage,
        EvidenceStatusDto status,
        bool requiresHumanReview,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        IReadOnlyList<string> requiredActions)
    {
        var reviewChecklist = requiredActions.Count == 0
            ? ["Confirm retained approvals and evidence before relying on automation output."]
            : requiredActions;
        var linkedEvidence = evidenceLinks
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        if (stage.Equals("Extraction review", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ReviewedAutomationArtifact(
                    "intake-extraction",
                    "Extraction",
                    "Intake field extraction draft",
                    status,
                    requiresHumanReview,
                    82m,
                    "Broker and administrator intake fields are suggested from retained source evidence.",
                    "Review extracted fields and classification confidence before reconciliation.",
                    "Cannot normalize source records or promote matches without human review.",
                    linkedEvidence,
                    reviewChecklist),
                ReviewedAutomationArtifact(
                    "intake-classification",
                    "Classification",
                    "Source classification suggestion",
                    status,
                    requiresHumanReview,
                    78m,
                    "Automation can classify imported activity for downstream matching.",
                    "Confirm source category and retained evidence before ledger or report usage.",
                    "Cannot mutate normalized records from classification output.",
                    linkedEvidence,
                    reviewChecklist)
            ];
        }

        if (stage.Equals("Suggested matches require review", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ReviewedAutomationArtifact(
                    "reconciliation-match-suggestion",
                    "Suggested match",
                    "Reconciliation match candidate",
                    status,
                    requiresHumanReview,
                    88m,
                    "Suggested matches, variance explanations, and duplicate flags are retained for operator review.",
                    "Review match rationale and variance evidence before resolving breaks.",
                    "Cannot resolve reconciliation breaks or approve exceptions.",
                    linkedEvidence,
                    reviewChecklist),
                ReviewedAutomationArtifact(
                    "duplicate-variance-flag",
                    "Variance flag",
                    "Duplicate and variance explanation",
                    status,
                    requiresHumanReview,
                    74m,
                    "Potential duplicate movements and unresolved variance drivers are flagged from evidence links.",
                    "Confirm duplicate disposition and variance explanation before downstream close use.",
                    "Cannot clear exceptions or change report readiness.",
                    linkedEvidence,
                    reviewChecklist)
            ];
        }

        if (stage.Equals("Journal draft review", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ReviewedAutomationArtifact(
                    "journal-template-draft",
                    "Journal draft",
                    "Journal template draft",
                    status,
                    requiresHumanReview,
                    81m,
                    "Automation can draft journal templates from validated source and reconciliation evidence.",
                    "Review the draft, balance, period, and retained source evidence before posting.",
                    "Cannot post material journals or edit posted entries.",
                    linkedEvidence,
                    reviewChecklist)
            ];
        }

        if (stage.Equals("Report commentary and audit request list draft review", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ReviewedAutomationArtifact(
                    "report-commentary-draft",
                    "Report commentary",
                    "Report commentary draft",
                    status,
                    requiresHumanReview,
                    84m,
                    "Draft commentary is generated from retained close, ledger, reconciliation, and report-pack evidence.",
                    "Review commentary against retained evidence before report approval or publication.",
                    "Cannot publish reports or release support packages.",
                    linkedEvidence,
                    reviewChecklist),
                ReviewedAutomationArtifact(
                    "audit-request-list-draft",
                    "Audit request list",
                    "Audit request list draft",
                    status,
                    requiresHumanReview,
                    79m,
                    "Draft audit request lists summarize missing support and unresolved evidence gaps.",
                    "Review each requested support item and assign an owner before audit release.",
                    "Cannot erase evidence or satisfy audit requests without retained support.",
                    linkedEvidence,
                    reviewChecklist),
                ReviewedAutomationArtifact(
                    "missing-support-flag",
                    "Missing support",
                    "Missing support flag",
                    status,
                    requiresHumanReview,
                    72m,
                    "Missing support flags are derived from incomplete evidence package categories.",
                    "Attach or waive missing support through governed human review.",
                    "Cannot approve its own missing-support disposition.",
                    linkedEvidence,
                    reviewChecklist)
            ];
        }

        if (stage.Equals("Reviewer approval required", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ReviewedAutomationArtifact(
                    "approval-support-summary",
                    "Evidence summary",
                    "Reviewer support summary",
                    status,
                    requiresHumanReview,
                    86m,
                    "Automation may summarize retained support while approval remains human-owned.",
                    "Complete reviewer approval and retain rationale before publication or close.",
                    "Cannot approve its own work or release evidence packages.",
                    linkedEvidence,
                    reviewChecklist)
            ];
        }

        if (stage.Equals("Reviewed evidence retained", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ReviewedAutomationArtifact(
                    "retained-review-record",
                    "Retained review",
                    "Reviewed automation closure record",
                    status,
                    requiresHumanReview,
                    null,
                    "Automation output is closed behind retained approval and evidence package history.",
                    "Use retained audit and evidence package history for downstream support.",
                    "No material automation action is permitted after close.",
                    linkedEvidence,
                    reviewChecklist)
            ];
        }

        return
        [
            ReviewedAutomationArtifact(
                "review-queue-intake",
                "Review queue",
                "Reviewed automation intake queue",
                status,
                requiresHumanReview,
                70m,
                "Automation output is retained as suggestions, summaries, drafts, and missing-support flags.",
                "Route automation output through a human review queue before material action.",
                "Cannot approve, post, publish, pay, reopen, or erase evidence.",
                linkedEvidence,
                reviewChecklist)
        ];
    }

    private static OperationsReviewedAutomationArtifactDto ReviewedAutomationArtifact(
        string artifactId,
        string artifactKind,
        string title,
        EvidenceStatusDto status,
        bool requiresHumanReview,
        decimal? confidencePercent,
        string sourceSummary,
        string suggestedOperatorAction,
        string blockedMaterialAction,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        IReadOnlyList<string> reviewChecklist)
        => new(
            ArtifactId: $"reviewed-automation:{artifactId}",
            ArtifactKind: artifactKind,
            Title: title,
            Status: status,
            RequiresHumanReview: requiresHumanReview,
            ConfidencePercent: confidencePercent,
            SourceSummary: sourceSummary,
            SuggestedOperatorAction: suggestedOperatorAction,
            BlockedMaterialAction: blockedMaterialAction,
            EvidenceLinks: evidenceLinks,
            ReviewChecklist: reviewChecklist);

    private static IReadOnlyList<OperationsEvidencePackageSummaryDto> BuildEvidencePackages(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline,
        OperationsAccountingRecordSummaryDto accountingRecordSummary,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks)
    {
        var accountingEvidence = accountingRecordSummary.EvidenceCategories
            .SelectMany(static category => category.EvidenceLinks)
            .Concat(accountingRecordSummary.EvidenceLinks)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reportEvidence = workflow.ReportPackReadiness.EvidenceLinks
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var closeEvidence = (workflow.ClosePackage?.EvidenceLinks ?? [])
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var exceptionManagementPackage = BuildExceptionManagementEvidencePackage(workflow, timeline);
        var approvalHistoryPackage = BuildApprovalHistoryEvidencePackage(workflow, timeline, closeEvidence);
        var auditEvidence = accountingEvidence
            .Concat(reportEvidence)
            .Concat(closeEvidence)
            .Concat(evidenceLinks)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reopenTimeline = timeline
            .Where(static entry => string.Equals(entry.EventType, "workflow-reopened", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var reopenEvidence = reopenTimeline
            .SelectMany(static entry => entry.References)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var periodLockEvidence = closeEvidence
            .Concat(reopenEvidence)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var auditPack = accountingRecordSummary.AuditPackReadiness;
        var periodLocked = workflow.IsClosed && workflow.ClosePackage is not null;
        var reopenPostureComplete = reopenTimeline.Length == 0 || reopenEvidence.Length > 0;
        var periodLockCategoryCount = (periodLocked ? 1 : 0) + (reopenPostureComplete ? 1 : 0);

        return
        [
            new OperationsEvidencePackageSummaryDto(
                accountingRecordSummary.RecordId,
                "Accounting record evidence",
                accountingRecordSummary.IsAuditReady
                    ? EvidenceStatusDto.Ready
                    : accountingRecordSummary.CompleteCategoryCount == 0
                        ? EvidenceStatusDto.Missing
                        : EvidenceStatusDto.ReviewRequired,
                accountingRecordSummary.IsAuditReady,
                accountingRecordSummary.Summary,
                "/workstation/accounting/operations-continuity",
                accountingRecordSummary.CompleteCategoryCount,
                accountingRecordSummary.RequiredCategoryCount,
                accountingEvidence.Length,
                accountingEvidence,
                accountingRecordSummary.IsAuditReady
                    ? []
                    : ["Complete all accounting-record evidence categories before publishing the evidence package."]),
            BuildReconciliationCoverageEvidencePackage(workflow, timeline),
            exceptionManagementPackage,
            new OperationsEvidencePackageSummaryDto(
                string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId)
                    ? $"report-pack:{workflow.FundAccountId:D}:{workflow.PeriodId}"
                    : workflow.ReportPackReadiness.ReportPackId,
                "Report pack evidence",
                workflow.ReportPackReadiness.IsReady &&
                !string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId)
                    ? EvidenceStatusDto.Ready
                    : string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId)
                        ? EvidenceStatusDto.Missing
                        : EvidenceStatusDto.ReviewRequired,
                workflow.ReportPackReadiness.IsReady &&
                !string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId),
                string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId)
                    ? workflow.ReportPackReadiness.BlockingReason ?? "Report-pack evidence has not been linked."
                    : $"Report pack {workflow.ReportPackReadiness.ReportPackId} is linked for retained close evidence.",
                "/workstation/reporting/report-packs",
                workflow.ReportPackReadiness.IsReady ? 1 : 0,
                1,
                reportEvidence.Length,
                reportEvidence,
                workflow.ReportPackReadiness.IsReady &&
                !string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId)
                    ? []
                    : ["Link ready report-pack evidence before close publication."]),
            new OperationsEvidencePackageSummaryDto(
                workflow.ClosePackage?.ClosePackageId ?? $"close-package:{workflow.FundAccountId:D}:{workflow.PeriodId}",
                "Close package manifest",
                workflow.ClosePackage is not null
                    ? EvidenceStatusDto.Ready
                    : workflow.ReportPackReadiness.IsReady
                        ? EvidenceStatusDto.ReviewRequired
                        : EvidenceStatusDto.Missing,
                workflow.ClosePackage is not null,
                workflow.ClosePackage is null
                    ? "Close package manifest and retained evidence hash have not been published."
                    : $"Close package {workflow.ClosePackage.ClosePackageId} retained manifest {workflow.ClosePackage.RetainedManifestId} and evidence hash.",
                workflow.ClosePackage?.RetainedManifestRoute ?? "/workstation/accounting/operations-continuity",
                workflow.ClosePackage is null ? 0 : 1,
                1,
                closeEvidence.Length,
                closeEvidence,
                workflow.ClosePackage is not null
                    ? []
                    : ["Publish the close package manifest and retain the evidence hash."]),
            new OperationsEvidencePackageSummaryDto(
                $"audit-support:{workflow.FundAccountId:D}:{workflow.PeriodId}",
                "Audit support package",
                auditPack?.IsComplete == true
                    ? EvidenceStatusDto.Ready
                    : auditPack is null || accountingRecordSummary.CompleteCategoryCount == 0
                        ? EvidenceStatusDto.Missing
                        : EvidenceStatusDto.ReviewRequired,
                auditPack?.IsComplete == true,
                auditPack?.IsComplete == true
                    ? "Audit support package includes all required accounting-record evidence categories."
                    : auditPack is null
                        ? "Audit support package readiness has not been calculated."
                        : $"{auditPack.MissingEvidenceCategories.Count} audit evidence categor{(auditPack.MissingEvidenceCategories.Count == 1 ? "y is" : "ies are")} missing.",
                "/workstation/reporting/evidence",
                auditPack?.EvidenceCategorySummaries.Count(static category => category.IsComplete) ?? 0,
                auditPack?.EvidenceCategorySummaries.Count ?? accountingRecordSummary.RequiredCategoryCount,
                auditEvidence.Length,
                auditEvidence,
                auditPack?.IsComplete == true
                    ? []
                    : ["Complete missing audit evidence categories before releasing the package."]),
            new OperationsEvidencePackageSummaryDto(
                $"period-lock-reopen:{workflow.FundAccountId:D}:{workflow.PeriodId}",
                "Period lock and reopen evidence",
                ResolvePeriodLockReopenStatus(periodLocked, reopenPostureComplete, workflow.ClosePackage, reopenEvidence),
                periodLocked && reopenPostureComplete,
                BuildPeriodLockReopenSummary(workflow, periodLocked, reopenTimeline.Length, reopenEvidence.Length),
                "/workstation/accounting/operations-continuity",
                periodLockCategoryCount,
                2,
                periodLockEvidence.Length,
                periodLockEvidence,
                BuildPeriodLockReopenRequiredActions(workflow, periodLocked, reopenPostureComplete)),
            approvalHistoryPackage
        ];
    }

    private static OperationsEvidencePackageSummaryDto BuildReconciliationCoverageEvidencePackage(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline)
    {
        var reconciliationRunEvidence = timeline
            .Where(static entry => string.Equals(entry.EventType, "reconciliation-run", StringComparison.OrdinalIgnoreCase))
            .SelectMany(static entry => entry.References)
            .ToArray();
        var lanesById = workflow.ReconciliationLanes
            .Where(lane => RequiredReconciliationCoverageLaneIds.Any(id => string.Equals(id, lane.LaneId, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(static lane => lane.LaneId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static lane => lane.LaneId, static lane => lane, StringComparer.OrdinalIgnoreCase);
        var packageLanes = RequiredReconciliationCoverageLaneIds
            .Select(laneId => lanesById.TryGetValue(laneId, out var lane) ? lane : null)
            .Where(static lane => lane is not null)
            .Cast<OperationsReconciliationLaneSummaryDto>()
            .ToArray();
        var hasReconciliationRun = packageLanes.Length > 0 &&
            (reconciliationRunEvidence.Length > 0 ||
             workflow.ReconciliationState is not OperationsReconciliationStateDto.Pending);
        var completeCategoryCount = hasReconciliationRun
            ? packageLanes.Count(static lane => lane.IsReady)
            : 0;
        var requiredCategoryCount = RequiredReconciliationCoverageLaneIds.Length;
        var evidence = packageLanes
            .SelectMany(static lane => lane.EvidenceLinks)
            .Concat(reconciliationRunEvidence)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var blockedLaneCount = hasReconciliationRun
            ? packageLanes.Count(static lane => lane.Status == OperationsReconciliationLaneStatusDto.Blocked)
            : 0;
        var missingLaneCount = RequiredReconciliationCoverageLaneIds.Length - packageLanes.Length;
        var status = !hasReconciliationRun
            ? EvidenceStatusDto.Missing
            : blockedLaneCount > 0
                ? EvidenceStatusDto.Blocked
                : completeCategoryCount == requiredCategoryCount
                    ? EvidenceStatusDto.Ready
                    : EvidenceStatusDto.ReviewRequired;
        var actions = new List<string>();
        if (!hasReconciliationRun)
        {
            actions.Add("Run reconciliation and retain cash, position, trade, income, MBS factor, bank, and GL coverage before evidence release.");
        }

        if (missingLaneCount > 0)
        {
            actions.Add("Retain all Financial Operations reconciliation lane summaries before audit release.");
        }

        actions.AddRange(packageLanes
            .Where(static lane => !lane.IsReady)
            .SelectMany(static lane => lane.RequiredActions ?? [])
            .Where(static action => !string.IsNullOrWhiteSpace(action)));

        return new OperationsEvidencePackageSummaryDto(
            $"reconciliation-coverage:{workflow.FundAccountId:D}:{workflow.PeriodId}",
            "Reconciliation coverage evidence",
            status,
            status == EvidenceStatusDto.Ready,
            BuildReconciliationCoverageSummary(hasReconciliationRun, completeCategoryCount, requiredCategoryCount, blockedLaneCount, evidence.Length),
            "/workstation/accounting/reconciliation",
            completeCategoryCount,
            requiredCategoryCount,
            evidence.Length,
            evidence,
            actions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string BuildReconciliationCoverageSummary(
        bool hasReconciliationRun,
        int completeCategoryCount,
        int requiredCategoryCount,
        int blockedLaneCount,
        int evidenceLinkCount)
    {
        if (!hasReconciliationRun)
        {
            return "Reconciliation coverage has not been retained by a Financial Operations reconciliation run.";
        }

        if (completeCategoryCount == requiredCategoryCount)
        {
            return "Reconciliation coverage evidence confirms cash, position, trade, income, MBS factor, bank, and GL support lanes are ready.";
        }

        if (blockedLaneCount > 0)
        {
            return $"{blockedLaneCount} reconciliation lane(s) are blocked; {completeCategoryCount}/{requiredCategoryCount} coverage categories are ready across {evidenceLinkCount:N0} retained evidence link(s).";
        }

        return $"Reconciliation coverage evidence is incomplete; {completeCategoryCount}/{requiredCategoryCount} coverage categories are ready across {evidenceLinkCount:N0} retained evidence link(s).";
    }

    private static OperationsEvidencePackageSummaryDto BuildExceptionManagementEvidencePackage(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline)
    {
        var exceptionTimeline = timeline
            .Where(static entry =>
                string.Equals(entry.EventType, "reconciliation-run", StringComparison.OrdinalIgnoreCase) ||
                entry.EventType.StartsWith("reconciliation-break-", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var unresolvedBreaks = workflow.BreakCases
            .Where(static breakCase => !IsClosedBreakStatus(breakCase.Status))
            .ToArray();
        var exceptionEvidence = exceptionTimeline
            .SelectMany(static entry => entry.References)
            .Concat(workflow.BreakCases.SelectMany(static breakCase => breakCase.EvidenceLinks))
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hasCaseInventory = exceptionTimeline.Any(static entry =>
            string.Equals(entry.EventType, "reconciliation-run", StringComparison.OrdinalIgnoreCase)) ||
            workflow.ReconciliationState == OperationsReconciliationStateDto.Complete;
        var allExceptionsResolved = hasCaseInventory && unresolvedBreaks.Length == 0;
        var retainedCaseEvidence = hasCaseInventory &&
            (workflow.BreakCases.Count == 0 || exceptionEvidence.Length > 0);
        var completeCategoryCount = (hasCaseInventory ? 1 : 0) +
            (allExceptionsResolved ? 1 : 0) +
            (retainedCaseEvidence ? 1 : 0);
        const int requiredCategoryCount = 3;
        var hasCriticalException = unresolvedBreaks.Any(static breakCase =>
            string.Equals(breakCase.Severity, "Critical", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(breakCase.Severity, "High", StringComparison.OrdinalIgnoreCase));
        var status = completeCategoryCount == requiredCategoryCount
            ? EvidenceStatusDto.Ready
            : !hasCaseInventory
                ? EvidenceStatusDto.Missing
                : hasCriticalException
                    ? EvidenceStatusDto.Blocked
                    : EvidenceStatusDto.ReviewRequired;
        var actions = new List<string>();
        if (!hasCaseInventory)
        {
            actions.Add("Run reconciliation and retain exception case inventory before evidence release.");
        }

        if (unresolvedBreaks.Length > 0)
        {
            actions.AddRange(BuildResolveExceptionsRequiredActions(unresolvedBreaks));
        }

        if (!retainedCaseEvidence)
        {
            actions.Add("Retain exception assignment, escalation, or resolution evidence before audit release.");
        }

        return new OperationsEvidencePackageSummaryDto(
            $"exception-management:{workflow.FundAccountId:D}:{workflow.PeriodId}",
            "Exception management evidence",
            status,
            status == EvidenceStatusDto.Ready,
            BuildExceptionManagementSummary(workflow, hasCaseInventory, unresolvedBreaks.Length, exceptionEvidence.Length),
            "/workstation/accounting/reconciliation",
            completeCategoryCount,
            requiredCategoryCount,
            exceptionEvidence.Length,
            exceptionEvidence,
            actions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string BuildExceptionManagementSummary(
        OperationsContinuityWorkflow workflow,
        bool hasCaseInventory,
        int unresolvedBreakCount,
        int evidenceLinkCount)
    {
        if (!hasCaseInventory)
        {
            return "Exception case inventory has not been retained by a reconciliation run.";
        }

        if (unresolvedBreakCount == 0)
        {
            return workflow.BreakCases.Count == 0
                ? "Reconciliation run retained no open exception casework for this workflow."
                : $"Exception management evidence confirms {workflow.BreakCases.Count} break case(s) are closed with {evidenceLinkCount:N0} retained evidence link(s).";
        }

        return $"{unresolvedBreakCount} exception case(s) remain open; assignment, escalation, or resolution evidence is required before audit release.";
    }

    private static OperationsEvidencePackageSummaryDto BuildApprovalHistoryEvidencePackage(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline,
        IReadOnlyList<OperationsEvidenceLinkDto> closeEvidence)
    {
        var approvalTimeline = timeline
            .Where(static entry => entry.EventType.StartsWith("approval-", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.EventType, "workflow-closed", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var approvalEvidence = approvalTimeline
            .SelectMany(static entry => entry.References)
            .Concat(workflow.Approvals.SelectMany(static approval => approval.EvidenceLinks))
            .Concat(closeEvidence)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hasSubmission = workflow.Approvals.Any(static approval =>
            approval.Status is OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned);
        var hasApprovedDecision = workflow.ApprovalState == OperationsApprovalStateDto.Approved &&
            workflow.Approvals.Any(static approval => approval.Status == OperationsApprovalStateDto.Approved);
        var retainedChecklistApprovalCount = workflow.ClosePackage?.ChecklistControlApprovals.Count ?? 0;
        var hasChecklistApprovals = retainedChecklistApprovalCount > 0;
        var completeCategoryCount = (hasSubmission ? 1 : 0) +
            (hasApprovedDecision ? 1 : 0) +
            (hasChecklistApprovals ? 1 : 0);
        const int requiredCategoryCount = 3;
        var status = completeCategoryCount == requiredCategoryCount
            ? EvidenceStatusDto.Ready
            : completeCategoryCount == 0
                ? EvidenceStatusDto.Missing
                : EvidenceStatusDto.ReviewRequired;
        var actions = new List<string>();
        if (!hasSubmission)
        {
            actions.Add("Submit workflow approval with reviewer, rationale, and report-pack evidence.");
        }

        if (!hasApprovedDecision)
        {
            actions.Add(workflow.ApprovalState == OperationsApprovalStateDto.Rejected
                ? "Resolve rejected approval and retain an approved reviewer decision before audit release."
                : "Record reviewer approval decision with retained rationale and report-pack evidence.");
        }

        if (!hasChecklistApprovals)
        {
            actions.Add("Publish close package with retained checklist-control approvals before audit release.");
        }

        return new OperationsEvidencePackageSummaryDto(
            $"approval-history:{workflow.FundAccountId:D}:{workflow.PeriodId}",
            "Approval history evidence",
            status,
            status == EvidenceStatusDto.Ready,
            status == EvidenceStatusDto.Ready
                ? $"Approval history includes submission, reviewer decision, and {retainedChecklistApprovalCount} retained checklist-control approval(s)."
                : $"Approval history has {completeCategoryCount} of {requiredCategoryCount} required evidence categories complete.",
            "/workstation/accounting/approvals",
            completeCategoryCount,
            requiredCategoryCount,
            approvalEvidence.Length,
            approvalEvidence,
            actions);
    }

    private static EvidenceStatusDto ResolvePeriodLockReopenStatus(
        bool periodLocked,
        bool reopenPostureComplete,
        OperationsClosePackagePublicationDto? closePackage,
        IReadOnlyList<OperationsEvidenceLinkDto> reopenEvidence)
    {
        if (periodLocked && reopenPostureComplete)
        {
            return EvidenceStatusDto.Ready;
        }

        return closePackage is not null || reopenEvidence.Count > 0
            ? EvidenceStatusDto.ReviewRequired
            : EvidenceStatusDto.Missing;
    }

    private static string BuildPeriodLockReopenSummary(
        OperationsContinuityWorkflow workflow,
        bool periodLocked,
        int reopenEventCount,
        int reopenEvidenceCount)
    {
        if (periodLocked)
        {
            return reopenEventCount == 0
                ? $"Period {workflow.PeriodId} is locked by close package {workflow.ClosePackage!.ClosePackageId}; no governed reopen incident is active."
                : $"Period {workflow.PeriodId} is locked by close package {workflow.ClosePackage!.ClosePackageId} with {reopenEvidenceCount:N0} retained reopen incident evidence link(s).";
        }

        if (workflow.ClosePackage is not null)
        {
            return reopenEventCount == 0
                ? $"Close package {workflow.ClosePackage.ClosePackageId} exists, but period {workflow.PeriodId} is not currently locked."
                : $"Workflow was reopened after close package {workflow.ClosePackage.ClosePackageId}; {reopenEvidenceCount:N0} incident evidence link(s) are retained and the period must be locked again after remediation.";
        }

        return $"Period {workflow.PeriodId} has not been locked by a close package; governed reopen evidence will be required if a closed workflow is reopened.";
    }

    private static IReadOnlyList<string> BuildPeriodLockReopenRequiredActions(
        OperationsContinuityWorkflow workflow,
        bool periodLocked,
        bool reopenPostureComplete)
    {
        if (periodLocked && reopenPostureComplete)
        {
            return [];
        }

        var actions = new List<string>();
        if (workflow.ClosePackage is null)
        {
            actions.Add("Close the workflow and retain the period-lock package before evidence release.");
        }
        else if (!workflow.IsClosed)
        {
            actions.Add("Complete reopened incident remediation and close the period again with retained evidence.");
        }

        if (!reopenPostureComplete)
        {
            actions.Add("Attach governed incident evidence before reopening a closed workflow.");
        }

        return actions;
    }

    private static OperationsDashboardSummaryDto BuildDashboardSummary(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline,
        OperationsCloseReadinessDto? closeReadiness,
        OperationsAccountingRecordSummaryDto? accountingRecordSummary,
        IReadOnlyList<OperationsEvidencePackageSummaryDto> evidencePackages,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks)
    {
        var unresolvedBreaks = workflow.BreakCases
            .Where(static breakCase => !IsClosedBreakStatus(breakCase.Status))
            .ToArray();
        var criticalBreaks = unresolvedBreaks.Count(static breakCase =>
            string.Equals(breakCase.Severity, "Critical", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(breakCase.Severity, "High", StringComparison.OrdinalIgnoreCase));
        var readyLaneCount = workflow.ReconciliationLanes.Count(static lane => lane.IsReady);
        var reconciliationEvidence = workflow.ReconciliationLanes
            .SelectMany(static lane => lane.EvidenceLinks)
            .Concat(workflow.BreakCases.SelectMany(static breakCase => breakCase.EvidenceLinks))
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var approvalEvidence = workflow.Approvals
            .SelectMany(static approval => approval.EvidenceLinks)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var packageEvidence = evidencePackages
            .SelectMany(static package => package.EvidenceLinks)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var closePackage = workflow.ClosePackage;
        var periodLocked = workflow.IsClosed && closePackage is not null;

        var metrics = new[]
        {
            new OperationsDashboardMetricDto(
                "receive-activity",
                "Receive Activity",
                workflow.BrokerIntakeState.ToString(),
                workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Complete
                    ? EvidenceStatusDto.Ready
                    : EvidenceStatusDto.ReviewRequired,
                workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Complete
                    ? "Broker, custodian, or bank activity has been received and normalized for this account-period workflow."
                    : "Activity intake is still pending before matching and accounting controls can finish.",
                "/workstation/accounting",
                EvidenceForGate(timeline, OperationsGateKeyDto.BrokerIngest),
                workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Complete
                    ? []
                    : ["Receive and normalize account activity before matching records."]),
            new OperationsDashboardMetricDto(
                "match-records",
                "Match Records",
                $"{readyLaneCount}/{workflow.ReconciliationLanes.Count} lanes ready",
                ResolveLaneMetricStatus(workflow.ReconciliationLanes),
                workflow.ReconciliationLanes.Count == 0
                    ? "No reconciliation lane coverage has been projected for this workflow."
                    : "Cash, position, trade, income, MBS factor, bank, and GL reconciliation lanes are tracked from the shared workflow detail.",
                "/workstation/accounting/reconciliation",
                reconciliationEvidence,
                BuildMatchRecordsRequiredActions(workflow.ReconciliationLanes)),
            new OperationsDashboardMetricDto(
                "resolve-exceptions",
                "Resolve Exceptions",
                $"{unresolvedBreaks.Length} open",
                unresolvedBreaks.Length == 0
                    ? EvidenceStatusDto.Ready
                    : criticalBreaks > 0
                        ? EvidenceStatusDto.Blocked
                        : EvidenceStatusDto.ReviewRequired,
                unresolvedBreaks.Length == 0
                    ? "No unresolved reconciliation breaks remain for this workflow."
                    : $"{unresolvedBreaks.Length} reconciliation break(s) require assignment, escalation, or resolution evidence.",
                "/workstation/accounting/reconciliation",
                workflow.BreakCases.SelectMany(static breakCase => breakCase.EvidenceLinks)
                    .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                unresolvedBreaks.Length == 0
                    ? []
                    : BuildResolveExceptionsRequiredActions(unresolvedBreaks)),
            new OperationsDashboardMetricDto(
                "approve-results",
                "Approve Results",
                workflow.ApprovalState.ToString(),
                workflow.ApprovalState == OperationsApprovalStateDto.Approved
                    ? EvidenceStatusDto.Ready
                    : EvidenceStatusDto.ReviewRequired,
                workflow.ApprovalState == OperationsApprovalStateDto.Approved
                    ? "Approval history is complete for this workflow."
                    : "Approval history is not complete for this workflow.",
                "/workstation/accounting/approvals",
                approvalEvidence,
                workflow.ApprovalState == OperationsApprovalStateDto.Approved
                    ? []
                    : BuildApproveResultsRequiredActions(workflow)),
            new OperationsDashboardMetricDto(
                "produce-evidence",
                "Produce Evidence",
                periodLocked
                    ? "Close package retained"
                    : closePackage is not null
                        ? "Reopened period lock pending"
                    : workflow.ReportPackReadiness.IsReady
                        ? "Report pack ready"
                        : "Evidence package pending",
                periodLocked
                    ? EvidenceStatusDto.Ready
                    : workflow.ReportPackReadiness.IsReady
                        ? EvidenceStatusDto.ReviewRequired
                        : EvidenceStatusDto.Missing,
                periodLocked
                    ? $"Close package {closePackage!.ClosePackageId} retained manifest {closePackage.RetainedManifestId}."
                    : closePackage is not null
                        ? $"Close package {closePackage.ClosePackageId} remains retained, but reopened incident remediation must be closed again before evidence release."
                    : workflow.ReportPackReadiness.BlockingReason ?? "Report-pack and close-package evidence still need publication.",
                "/workstation/reporting/report-packs",
                packageEvidence,
                periodLocked
                    ? []
                    : BuildProduceEvidenceRequiredActions(evidencePackages, periodLocked)),
            new OperationsDashboardMetricDto(
                "close-support",
                "Close Support",
                closeReadiness is null
                    ? "Close readiness pending"
                    : closeReadiness.IsReadyToClose
                        ? "Ready to close"
                        : $"{closeReadiness.Score}% ready",
                ResolveCloseSupportMetricStatus(closeReadiness, periodLocked),
                closeReadiness?.Blockers.FirstOrDefault()?.Message ??
                    "Close checklist, period lock, and reopen evidence are governed by the shared workflow.",
                "/workstation/accounting/operations-continuity",
                periodLocked
                    ? closePackage!.EvidenceLinks
                    : evidenceLinks,
                closeReadiness is { IsReadyToClose: true } || periodLocked
                    ? []
                    : BuildCloseSupportRequiredActions(closeReadiness, periodLocked))
        };
        var status = ResolveDashboardStatus(metrics);
        var readyCount = metrics.Count(static metric => metric.Status == EvidenceStatusDto.Ready);
        var stage = ResolveDashboardStage(workflow, unresolvedBreaks.Length);
        var dashboardEvidence = evidenceLinks
            .Concat(metrics.SelectMany(static metric => metric.EvidenceLinks))
            .Concat(accountingRecordSummary?.EvidenceLinks ?? [])
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new OperationsDashboardSummaryDto(
            $"operations-dashboard:{workflow.FundAccountId:D}:{workflow.PeriodId}",
            stage,
            status,
            status == EvidenceStatusDto.Ready,
            readyCount,
            metrics.Length,
            status == EvidenceStatusDto.Ready
                ? $"Financial Operations dashboard is ready to produce evidence for period {workflow.PeriodId}."
                : $"Financial Operations dashboard is in {stage} with {metrics.Length - readyCount} metric(s) requiring review.",
            metrics,
            dashboardEvidence,
            metrics.SelectMany(static metric => metric.RequiredActions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static IReadOnlyList<string> BuildMatchRecordsRequiredActions(
        IReadOnlyList<OperationsReconciliationLaneSummaryDto> lanes)
    {
        if (lanes.Count > 0 && lanes.All(static lane => lane.IsReady))
        {
            return [];
        }

        var laneActions = lanes
            .Where(static lane => !lane.IsReady)
            .SelectMany(static lane => lane.RequiredActions ?? [])
            .ToArray();

        return BuildCappedDashboardRequiredActions(
            laneActions,
            "Complete source-backed reconciliation lanes before approval.",
            "reconciliation lane action");
    }

    private static IReadOnlyList<string> BuildResolveExceptionsRequiredActions(
        IReadOnlyList<OperationsBreakCaseDto> unresolvedBreaks)
    {
        if (unresolvedBreaks.Count == 0)
        {
            return [];
        }

        var actions = new List<string>();
        foreach (var breakCase in unresolvedBreaks)
        {
            AddDashboardRequiredAction(actions, breakCase.SuggestedAction);
        }

        var unassignedCount = unresolvedBreaks.Count(static breakCase => string.IsNullOrWhiteSpace(breakCase.Owner));
        if (unassignedCount > 0)
        {
            AddDashboardRequiredAction(
                actions,
                $"Assign {FormatOpenExceptionCount(unassignedCount)} to an accountable owner.");
        }

        var escalatedBreaks = unresolvedBreaks
            .Where(static breakCase => !string.IsNullOrWhiteSpace(breakCase.EscalationLevel))
            .ToArray();
        if (escalatedBreaks.Length > 0)
        {
            var firstEscalation = escalatedBreaks[0].EscalationLevel!.Trim();
            var escalationReason = string.IsNullOrWhiteSpace(escalatedBreaks[0].EscalationReason)
                ? string.Empty
                : $": {escalatedBreaks[0].EscalationReason!.Trim()}";
            AddDashboardRequiredAction(
                actions,
                $"Review {firstEscalation} escalation for {FormatOpenExceptionCount(escalatedBreaks.Length)}{escalationReason}.");
        }

        var blockedOutputs = unresolvedBreaks
            .SelectMany(static breakCase => breakCase.BlockedOutputs ?? [])
            .Where(static output => !string.IsNullOrWhiteSpace(output))
            .Select(static output => output.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (blockedOutputs.Length > 0)
        {
            var outputSummary = string.Join(", ", blockedOutputs.Take(3));
            if (blockedOutputs.Length > 3)
            {
                outputSummary = $"{outputSummary}, and {blockedOutputs.Length - 3} more";
            }

            AddDashboardRequiredAction(
                actions,
                $"Retain resolution evidence before releasing blocked output(s): {outputSummary}.");
        }

        return BuildCappedDashboardRequiredActions(
            actions,
            "Assign, escalate, or resolve open exceptions and retain resolution evidence.",
            "exception action");
    }

    private static IReadOnlyList<string> BuildApproveResultsRequiredActions(
        OperationsContinuityWorkflow workflow)
    {
        if (workflow.ApprovalState == OperationsApprovalStateDto.Approved)
        {
            return [];
        }

        var actions = new List<string>();
        switch (workflow.ApprovalState)
        {
            case OperationsApprovalStateDto.Pending:
                if (workflow.ReportPackReadiness.IsReady &&
                    !string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId))
                {
                    AddDashboardRequiredAction(
                        actions,
                        $"Submit workflow approval for report pack {workflow.ReportPackReadiness.ReportPackId.Trim()} with reviewer and rationale.");
                }
                else
                {
                    AddDashboardRequiredAction(actions, workflow.ReportPackReadiness.BlockingReason);
                    AddDashboardRequiredAction(actions, "Link a ready report pack before approval submission.");
                }

                AddChecklistControlApprovalActions(actions, workflow, includeApprovalGate: false, transitionLabel: "approval submission");
                break;

            case OperationsApprovalStateDto.Submitted:
            case OperationsApprovalStateDto.ReviewerAssigned:
                var reviewer = GetAssignedApprovalReviewer(workflow) ?? "the assigned reviewer";
                AddDashboardRequiredAction(
                    actions,
                    $"Record approval decision from {reviewer} with retained rationale{ReadyReportPackSuffix(workflow)}");
                AddChecklistControlApprovalActions(actions, workflow, includeApprovalGate: true, transitionLabel: "approval decision");
                break;

            case OperationsApprovalStateDto.Rejected:
                var rejectionReviewer = GetLatestApprovalReviewer(workflow, OperationsApprovalStateDto.Rejected) ?? "the rejecting reviewer";
                AddDashboardRequiredAction(
                    actions,
                    $"Resolve rejected approval from {rejectionReviewer} and resubmit with retained remediation evidence.");
                if (!workflow.ReportPackReadiness.IsReady)
                {
                    AddDashboardRequiredAction(actions, workflow.ReportPackReadiness.BlockingReason);
                    AddDashboardRequiredAction(actions, "Link a ready report pack before resubmission.");
                }

                AddChecklistControlApprovalActions(actions, workflow, includeApprovalGate: false, transitionLabel: "approval resubmission");
                break;

            default:
                AddDashboardRequiredAction(actions, "Complete workflow approval and checklist-control approvals.");
                break;
        }

        return BuildCappedDashboardRequiredActions(
            actions,
            "Complete workflow approval and checklist-control approvals.",
            "approval action",
            "approval workspace");
    }

    private static void AddChecklistControlApprovalActions(
        List<string> actions,
        OperationsContinuityWorkflow workflow,
        bool includeApprovalGate,
        string transitionLabel)
    {
        var requiredGates = workflow.Gates
            .Where(gate => gate.Status == OperationsGateStatusDto.Passed ||
                (includeApprovalGate && gate.GateKey == OperationsGateKeyDto.Approval))
            .Select(static gate => gate.GateKey)
            .Distinct()
            .ToArray();

        foreach (var gate in requiredGates)
        {
            var requiredApprovalCount = gate == OperationsGateKeyDto.Approval ? 2 : 1;
            AddDashboardRequiredAction(
                actions,
                $"Retain {FormatChecklistApprovalCount(requiredApprovalCount)} for {DisplayName(gate)} close gate ({CloseChecklistTaskId(gate)}) before {transitionLabel}.");
        }
    }

    private static string FormatChecklistApprovalCount(int requiredApprovalCount) =>
        requiredApprovalCount == 1
            ? "1 checklist-control approval"
            : $"{requiredApprovalCount} checklist-control approvals";

    private static string? GetAssignedApprovalReviewer(OperationsContinuityWorkflow workflow) =>
        workflow.Approvals
            .LastOrDefault(approval =>
                (approval.Status is OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned) &&
                !string.IsNullOrWhiteSpace(approval.Reviewer))
            ?.Reviewer?.Trim();

    private static string? GetLatestApprovalReviewer(
        OperationsContinuityWorkflow workflow,
        OperationsApprovalStateDto approvalState) =>
        workflow.Approvals
            .LastOrDefault(approval => approval.Status == approvalState && !string.IsNullOrWhiteSpace(approval.Reviewer))
            ?.Reviewer?.Trim();

    private static string ReadyReportPackSuffix(OperationsContinuityWorkflow workflow) =>
        workflow.ReportPackReadiness.IsReady &&
        !string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId)
            ? $" for report pack {workflow.ReportPackReadiness.ReportPackId.Trim()}."
            : ".";

    private static IReadOnlyList<string> BuildCloseSupportRequiredActions(
        OperationsCloseReadinessDto? closeReadiness,
        bool periodLocked)
    {
        if (periodLocked || closeReadiness is { IsReadyToClose: true })
        {
            return [];
        }

        var actions = closeReadiness?.Blockers
            .Select(static blocker => string.IsNullOrWhiteSpace(blocker.Category)
                ? blocker.Message
                : $"Resolve {blocker.Category}: {blocker.Message}")
            .ToArray() ?? [];

        return BuildCappedDashboardRequiredActions(
            actions,
            "Clear close readiness blockers and retain period-lock or reopen evidence.",
            "close-support action",
            "operations continuity workspace");
    }

    private static IReadOnlyList<string> BuildProduceEvidenceRequiredActions(
        IReadOnlyList<OperationsEvidencePackageSummaryDto> evidencePackages,
        bool periodLocked)
    {
        if (periodLocked ||
            evidencePackages.Count > 0 && evidencePackages.All(static package => package.IsReady))
        {
            return [];
        }

        var actions = evidencePackages
            .Where(static package => !package.IsReady)
            .SelectMany(static package => package.RequiredActions ?? [])
            .ToArray();

        return BuildCappedDashboardRequiredActions(
            actions,
            "Publish and retain the evidence package before period close.",
            "evidence-package action",
            "reporting evidence workspace");
    }

    private static IReadOnlyList<string> BuildCappedDashboardRequiredActions(
        IEnumerable<string?> requiredActions,
        string fallbackAction,
        string actionLabel,
        string overflowWorkspace = "reconciliation workspace")
    {
        var actions = requiredActions
            .Where(static action => !string.IsNullOrWhiteSpace(action))
            .Select(static action => action!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (actions.Length == 0)
        {
            return [fallbackAction];
        }

        const int maxDashboardRequiredActions = 6;
        if (actions.Length <= maxDashboardRequiredActions)
        {
            return actions;
        }

        return actions
            .Take(maxDashboardRequiredActions)
            .Append($"Review {actions.Length - maxDashboardRequiredActions} additional {actionLabel}(s) in the {overflowWorkspace}.")
            .ToArray();
    }

    private static void AddDashboardRequiredAction(List<string> actions, string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        actions.Add(action.Trim());
    }

    private static string FormatOpenExceptionCount(int count) =>
        count == 1 ? "1 open exception" : $"{count} open exceptions";

    private static EvidenceStatusDto ResolveLaneMetricStatus(IReadOnlyList<OperationsReconciliationLaneSummaryDto> lanes)
    {
        if (lanes.Count == 0)
        {
            return EvidenceStatusDto.Missing;
        }

        if (lanes.Any(static lane => lane.Status == OperationsReconciliationLaneStatusDto.Blocked))
        {
            return EvidenceStatusDto.Blocked;
        }

        return lanes.All(static lane => lane.IsReady)
            ? EvidenceStatusDto.Ready
            : EvidenceStatusDto.ReviewRequired;
    }

    private static EvidenceStatusDto ResolveCloseSupportMetricStatus(
        OperationsCloseReadinessDto? closeReadiness,
        bool periodLocked)
    {
        if (periodLocked || closeReadiness is { IsReadyToClose: true })
        {
            return EvidenceStatusDto.Ready;
        }

        if (closeReadiness is null)
        {
            return EvidenceStatusDto.Missing;
        }

        return string.Equals(closeReadiness.Severity, "Critical", StringComparison.OrdinalIgnoreCase) ||
               closeReadiness.Blockers.Any(static blocker =>
                   string.Equals(blocker.Severity, "Critical", StringComparison.OrdinalIgnoreCase))
            ? EvidenceStatusDto.Blocked
            : EvidenceStatusDto.ReviewRequired;
    }

    private static EvidenceStatusDto ResolveDashboardStatus(IReadOnlyList<OperationsDashboardMetricDto> metrics)
    {
        if (metrics.Any(static metric => metric.Status == EvidenceStatusDto.Blocked))
        {
            return EvidenceStatusDto.Blocked;
        }

        if (metrics.Any(static metric => metric.Status == EvidenceStatusDto.Missing))
        {
            return EvidenceStatusDto.Missing;
        }

        return metrics.All(static metric => metric.Status == EvidenceStatusDto.Ready)
            ? EvidenceStatusDto.Ready
            : EvidenceStatusDto.ReviewRequired;
    }

    private static string ResolveDashboardStage(OperationsContinuityWorkflow workflow, int unresolvedBreakCount)
    {
        if (workflow.BrokerIntakeState is OperationsBrokerIntakeStateDto.Pending or OperationsBrokerIntakeStateDto.Imported or OperationsBrokerIntakeStateDto.Normalized)
        {
            return "Receive Activity";
        }

        if (workflow.ReconciliationState is OperationsReconciliationStateDto.Pending or OperationsReconciliationStateDto.AutoMatched)
        {
            return "Match Records";
        }

        if (unresolvedBreakCount > 0 ||
            workflow.ReconciliationState is OperationsReconciliationStateDto.ExceptionsOpen or OperationsReconciliationStateDto.InReview)
        {
            return "Resolve Exceptions";
        }

        if (workflow.ApprovalState != OperationsApprovalStateDto.Approved)
        {
            return "Approve Results";
        }

        return "Produce Evidence";
    }

    private static bool IsClosedBreakStatus(string? status)
        => string.Equals(status, "closed", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, "resolved", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, "matched", StringComparison.OrdinalIgnoreCase);

    private static OperationsAccountingRecordSummaryDto BuildAccountingRecordSummary(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks)
    {
        var categories = new[]
        {
            BuildAccountingRecordCategory(
                "source-records",
                "Retained source data",
                workflow.BrokerIngestGate.Status == OperationsGateStatusDto.Passed &&
                    workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Complete,
                workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Pending
                    ? "Broker, custodian, or bank source data has not been imported."
                    : "Provider and account source data is retained for the close lane.",
                "/workstation/accounting",
                EvidenceForGate(timeline, OperationsGateKeyDto.BrokerIngest),
                ["provider statement", "custodian activity file", "bank or account source record"]),
            BuildAccountingRecordCategory(
                "normalized-activity",
                "Normalized transactions and positions",
                workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Complete,
                workflow.BrokerIntakeState is OperationsBrokerIntakeStateDto.Normalized or OperationsBrokerIntakeStateDto.Complete
                    ? "Imported activity has been normalized for accounting review."
                    : "Imported activity still needs normalization.",
                "/workstation/accounting",
                EvidenceForGate(timeline, OperationsGateKeyDto.BrokerIngest),
                ["normalized transactions", "normalized positions", "balance or cash activity projection"]),
            BuildAccountingRecordCategory(
                "reconciliation-case-history",
                "Reconciliation case history",
                workflow.ReconciliationGate.Status == OperationsGateStatusDto.Passed &&
                    workflow.ReconciliationState == OperationsReconciliationStateDto.Complete,
                workflow.BreakCases.Count == 0
                    ? "No unresolved reconciliation casework remains for this record."
                    : $"{workflow.BreakCases.Count} reconciliation case(s) are linked to this record.",
                "/workstation/accounting",
                EvidenceForGate(timeline, OperationsGateKeyDto.Reconciliation)
                    .Concat(workflow.BreakCases.SelectMany(static breakCase => breakCase.EvidenceLinks))
                    .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ["reconciliation run", "break-case decision history", "resolved exception evidence"]),
            BuildAccountingRecordCategory(
                "ledger-evidence",
                "Journal and ledger evidence",
                workflow.LedgerPostingGate.Status == OperationsGateStatusDto.Passed &&
                    workflow.LedgerPostingState is OperationsLedgerPostingStateDto.Posted or OperationsLedgerPostingStateDto.Complete,
                workflow.LedgerPreview is null
                    ? "Ledger preview or posting evidence has not been linked."
                    : "Ledger preview and posting evidence are linked.",
                "/workstation/accounting",
                EvidenceForGate(timeline, OperationsGateKeyDto.LedgerPosting)
                    .Concat(workflow.LedgerPreview?.EvidenceLinks ?? [])
                    .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ["journal preview", "posted ledger batch", "trial-balance support"]),
            BuildAccountingRecordCategory(
                "approvals",
                "Approval history",
                workflow.ApprovalGate.Status == OperationsGateStatusDto.Passed &&
                    workflow.ApprovalState == OperationsApprovalStateDto.Approved,
                workflow.Approvals.Count == 0
                    ? "Approval history has not been submitted."
                    : $"{workflow.Approvals.Count} approval record(s) are linked.",
                "/workstation/accounting",
                EvidenceForGate(timeline, OperationsGateKeyDto.Approval)
                    .Concat(workflow.Approvals.SelectMany(static approval => approval.EvidenceLinks))
                    .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ["approval submission", "reviewer decision", "checklist control approvals"]),
            BuildAccountingRecordCategory(
                "report-pack",
                "Report pack",
                workflow.ReportPackReadiness.IsReady &&
                    !string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId),
                string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId)
                    ? "Report-pack readiness evidence has not been linked."
                    : $"Report pack {workflow.ReportPackReadiness.ReportPackId} is linked for the accounting record.",
                "/workstation/reporting/report-packs",
                workflow.ReportPackReadiness.EvidenceLinks,
                ["report-pack manifest", "report-pack provenance", "report-pack validation"]),
            BuildAccountingRecordCategory(
                "exports",
                "Exports and retained evidence",
                workflow.ClosePackage is not null &&
                    !string.IsNullOrWhiteSpace(workflow.ClosePackage.RetainedManifestId) &&
                    !string.IsNullOrWhiteSpace(workflow.ClosePackage.EvidenceHash),
                workflow.ClosePackage is null
                    ? "Export manifest and retained evidence hash still need close-package publication."
                    : $"Close package {workflow.ClosePackage.ClosePackageId} retains manifest {workflow.ClosePackage.RetainedManifestId} and evidence hash.",
                "/workstation/reporting/report-packs",
                workflow.ReportPackReadiness.EvidenceLinks
                    .Concat(workflow.ClosePackage?.EvidenceLinks ?? [])
                    .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ["export manifest", "retained evidence hash", "close-package publication"]),
            BuildAccountingRecordCategory(
                "restatement-lineage",
                "Restatement lineage",
                workflow.ClosePackage is not null,
                workflow.ClosePackage is null
                    ? "Restatement baseline is pending until the close package is published."
                    : "Closed package establishes the retained baseline for future restatements.",
                "/workstation/reporting/report-packs",
                workflow.ClosePackage?.EvidenceLinks ?? [],
                ["published baseline", "prior-version pointer when restated", "changed-line evidence"])
        };

        var completeCount = categories.Count(static category => category.IsComplete);
        var requiredCount = categories.Length;
        var recordId = $"accounting-record-{workflow.FundAccountId:N}-{workflow.PeriodId}";
        var summary = completeCount == requiredCount
            ? "Accounting record links retained source data, normalized activity, reconciliation case history, ledger evidence, approvals, report pack, exports, and restatement lineage."
            : $"Accounting record has {completeCount} of {requiredCount} required evidence categories complete.";
        var auditPackReadiness = BuildAuditPackReadiness(categories, workflow, completeCount, requiredCount);

        return new OperationsAccountingRecordSummaryDto(
            recordId,
            completeCount == requiredCount,
            completeCount,
            requiredCount,
            summary,
            categories,
            evidenceLinks,
            auditPackReadiness);
    }

    private static OperationsAccountingRecordEvidenceCategoryDto BuildAccountingRecordCategory(
        string key,
        string label,
        bool isComplete,
        string status,
        string routeHint,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        IReadOnlyList<string> requiredEvidence) =>
        new(key, label, isComplete, status, routeHint, evidenceLinks, requiredEvidence);

    private static FundAuditPackReadinessDto BuildAuditPackReadiness(
        IReadOnlyList<OperationsAccountingRecordEvidenceCategoryDto> categories,
        OperationsContinuityWorkflow workflow,
        int completeCount,
        int requiredCount)
    {
        const int slaTargetSeconds = 60;
        var generatedInSeconds = workflow.ClosePackage is null
            ? 0d
            : Math.Max(0d, (workflow.ClosePackage.PublishedAtUtc - workflow.UpdatedAtUtc).Duration().TotalSeconds);
        var summaries = categories
            .Select(category => new FundAuditEvidenceCategorySummaryDto(
                MapAuditCategoryKey(category.Key),
                category.Label,
                category.IsComplete,
                category.Status,
                category.EvidenceLinks.Count,
                category.EvidenceLinks
                    .Select(static link => link.EvidenceId)
                    .Where(static id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                category.RouteHint))
            .ToArray();
        var missing = summaries
            .Where(static category => !category.IsComplete)
            .Select(static category => category.Key)
            .ToArray();

        return new FundAuditPackReadinessDto(
            IsComplete: completeCount == requiredCount,
            GeneratedInSeconds: Math.Round(generatedInSeconds, 3, MidpointRounding.AwayFromZero),
            SlaTargetSeconds: slaTargetSeconds,
            SlaMet: generatedInSeconds <= slaTargetSeconds,
            MissingEvidenceCategories: missing,
            Warnings: categories
                .Where(static category => !category.IsComplete)
                .Select(static category => category.Status)
                .Where(static status => !string.IsNullOrWhiteSpace(status))
                .ToArray(),
            EvidenceCategorySummaries: summaries);
    }

    private static FundAuditEvidenceCategoryKeyDto MapAuditCategoryKey(string key)
        => key switch
        {
            "source-records" => FundAuditEvidenceCategoryKeyDto.SourceRecords,
            "normalized-activity" => FundAuditEvidenceCategoryKeyDto.NormalizedActivity,
            "reconciliation-case-history" => FundAuditEvidenceCategoryKeyDto.ReconciliationCases,
            "ledger-evidence" => FundAuditEvidenceCategoryKeyDto.LedgerEvidence,
            "approvals" => FundAuditEvidenceCategoryKeyDto.Approvals,
            "report-pack" => FundAuditEvidenceCategoryKeyDto.ReportPack,
            "exports" => FundAuditEvidenceCategoryKeyDto.Exports,
            "restatement-lineage" => FundAuditEvidenceCategoryKeyDto.RestatementLineage,
            _ => FundAuditEvidenceCategoryKeyDto.SourceRecords
        };

    private static string BuildReportPackLineageStatus(OperationsContinuityWorkflow workflow)
    {
        if (string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId))
        {
            return "Report-pack publication, export, retained document, and restatement lineage have not been linked.";
        }

        if (workflow.ClosePackage is null)
        {
            return $"Report pack {workflow.ReportPackReadiness.ReportPackId} is linked; close-package publication, export manifest, retained document evidence, and restatement lineage still need retained evidence.";
        }

        return $"Report pack {workflow.ClosePackage.ReportPackId} is linked with retained manifest {workflow.ClosePackage.RetainedManifestId}, publication/export evidence hash, retained document evidence, and close-package restatement lineage.";
    }

    private static IReadOnlyList<OperationsEvidenceLinkDto> EvidenceForGate(
        IReadOnlyList<OperationsTimelineEntryDto> timeline,
        OperationsGateKeyDto gate) =>
        timeline
            .Where(entry => entry.Gate == gate)
            .SelectMany(static entry => entry.References)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

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
                CloseChecklistTaskId(gate.GateKey),
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

    private static string CloseChecklistTaskId(OperationsGateKeyDto gate) =>
        $"close-gate-{gate}".ToLowerInvariant();

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
                workflow.BreakCases.All(static item =>
                    string.Equals(item.Status, "closed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.Status, "resolved", StringComparison.OrdinalIgnoreCase)),
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

    private static OperationsGateDto ToGateDto(OperationsGateState gate) =>
        new(
            gate.GateKey,
            DisplayName(gate.GateKey),
            gate.Status,
            IsRequired: true,
            Description(gate.GateKey),
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
        IReadOnlyDictionary<Guid, SecurityStatusDto> authoritativeSecurityStatuses,
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

        var validationBlockers = ValidateJournalCandidate(workflow, request, candidate, authoritativeSecurityStatuses, request.EvidenceLinks);
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
                ToJournalEntryMetadata(candidate, authoritativeSecurityStatuses));

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
        IReadOnlyDictionary<Guid, SecurityStatusDto> authoritativeSecurityStatuses,
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
        else if (!SecurityMasterProvenanceReferences(candidate.SecurityMasterProvenance, candidate.Metadata.SecurityId.Value))
        {
            blockers.Add(CreateJournalCandidateBlocker(
                "LEDGER_JOURNAL_SECURITY_MASTER_PROVENANCE_MISMATCH",
                "Ledger journal candidate Security Master provenance must reference the candidate security id before posting.",
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

            if (IsInstrumentBearingJournalLine(line))
            {
                var lineLabel = string.IsNullOrWhiteSpace(line.Symbol)
                    ? string.IsNullOrWhiteSpace(line.AccountName)
                        ? "instrument-bearing line"
                        : $"account '{line.AccountName.Trim()}'"
                    : $"symbol '{line.Symbol.Trim()}'";
                if (string.IsNullOrWhiteSpace(line.Symbol))
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_SYMBOL_MISSING",
                        $"Ledger journal candidate line for {lineLabel} requires an explicit Security Master symbol before posting.",
                        evidence));
                }
                else if (!string.IsNullOrWhiteSpace(candidate.Metadata?.Symbol) &&
                    !string.Equals(line.Symbol.Trim(), candidate.Metadata.Symbol.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_SYMBOL_MISMATCH",
                        $"Ledger journal candidate line for {lineLabel} must use the same Security Master symbol as the journal candidate metadata.",
                        evidence));
                }

                if (line.SecurityId.GetValueOrDefault() == Guid.Empty)
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_ID_MISSING",
                        $"Ledger journal candidate line for {lineLabel} requires a resolved Security Master security id before posting.",
                        evidence));
                }
                else
                {
                    var lineSecurityId = line.SecurityId.GetValueOrDefault();
                    if (candidate.Metadata?.SecurityId is Guid candidateSecurityId && candidateSecurityId != lineSecurityId)
                    {
                        blockers.Add(CreateJournalCandidateBlocker(
                            "LEDGER_LINE_SECURITY_MASTER_ID_MISMATCH",
                            $"Ledger journal candidate line for {lineLabel} must use the same Security Master security id as the journal candidate metadata.",
                            evidence));
                    }

                    if (!string.IsNullOrWhiteSpace(line.SecurityMasterProvenance) &&
                        !SecurityMasterProvenanceReferences(line.SecurityMasterProvenance, lineSecurityId))
                    {
                        blockers.Add(CreateJournalCandidateBlocker(
                            "LEDGER_LINE_SECURITY_MASTER_PROVENANCE_MISMATCH",
                            $"Ledger journal candidate line for {lineLabel} must carry Security Master provenance that references the resolved security id.",
                            evidence));
                    }
                }

                if (!line.SecurityMasterApproved)
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_APPROVAL_REQUIRED",
                        $"Ledger journal candidate line for {lineLabel} requires approved Security Master identity before posting.",
                        evidence));
                }

                if (!TryGetAuthoritativeActiveSecurityStatus(line.SecurityId, authoritativeSecurityStatuses))
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_ACTIVE_STATUS_REQUIRED",
                        $"Ledger journal candidate line for {lineLabel} requires active Security Master status from the authoritative Security Master before posting.",
                        evidence));
                }

                if (string.IsNullOrWhiteSpace(line.SecurityMasterApprovalReference))
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_APPROVAL_EVIDENCE_MISSING",
                        $"Ledger journal candidate line for {lineLabel} requires Security Master approval evidence before posting.",
                        evidence));
                }

                if (string.IsNullOrWhiteSpace(line.SecurityMasterProvenance))
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_PROVENANCE_MISSING",
                        $"Ledger journal candidate line for {lineLabel} requires Security Master provenance before posting.",
                        evidence));
                }

                if (string.IsNullOrWhiteSpace(line.LedgerMappingReference))
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_MAPPING_MISSING",
                        $"Ledger journal candidate line for {lineLabel} requires a Security Master ledger mapping reference before posting.",
                        evidence));
                }
                else if (!LedgerMappingReferencesInstrument(line.LedgerMappingReference, line.Symbol, line.SecurityId))
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_MAPPING_MISMATCH",
                        $"Ledger journal candidate line for {lineLabel} requires a Security Master ledger mapping reference tied to the resolved symbol or security id.",
                        evidence));
                }
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


    private async Task<IReadOnlyDictionary<Guid, SecurityStatusDto>> ResolveAuthoritativeSecurityStatusesAsync(
        IReadOnlyList<OperationsLedgerJournalLineDto>? lines,
        CancellationToken ct)
    {
        var securityIds = (lines ?? [])
            .Where(static line => IsInstrumentBearingJournalLine(line))
            .Select(static line => line.SecurityId.GetValueOrDefault())
            .Where(static securityId => securityId != Guid.Empty)
            .Distinct()
            .ToArray();

        if (securityIds.Length == 0 || _securityMasterQueryService is null)
        {
            return new Dictionary<Guid, SecurityStatusDto>();
        }

        var statuses = new Dictionary<Guid, SecurityStatusDto>();
        foreach (var securityId in securityIds)
        {
            var security = await _securityMasterQueryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            if (security is not null)
            {
                statuses[securityId] = security.Status;
            }
        }

        return statuses;
    }

    private static bool TryResolveWorkflowPeriodGuid(string workflowPeriodId, out Guid resolvedPeriodId) =>
        Guid.TryParse(workflowPeriodId, out resolvedPeriodId);

    private static OperationsWorkflowBlockerDto CreateJournalCandidateBlocker(
        string code,
        string message,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks) =>
        new(code, message, OperationsGateKeyDto.LedgerPosting, "Critical", evidenceLinks);

    private static bool SecurityMasterProvenanceReferences(string? provenance, Guid securityId)
    {
        if (string.IsNullOrWhiteSpace(provenance) || securityId == Guid.Empty)
        {
            return false;
        }

        return provenance.Contains(securityId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            provenance.Contains(securityId.ToString("N"), StringComparison.OrdinalIgnoreCase);
    }

    private static bool LedgerMappingReferencesInstrument(string? mappingReference, string? symbol, Guid? securityId)
    {
        if (string.IsNullOrWhiteSpace(mappingReference))
        {
            return false;
        }

        var mapping = mappingReference.Trim();
        var resolvedSecurityId = securityId.GetValueOrDefault();
        if (resolvedSecurityId != Guid.Empty &&
            (mapping.Contains(resolvedSecurityId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
             mapping.Contains(resolvedSecurityId.ToString("N"), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(symbol) &&
            mapping.Contains(symbol.Trim(), StringComparison.OrdinalIgnoreCase);
    }


    private static bool TryGetAuthoritativeActiveSecurityStatus(
        Guid? securityId,
        IReadOnlyDictionary<Guid, SecurityStatusDto> authoritativeSecurityStatuses) =>
        TryGetAuthoritativeSecurityStatus(securityId, authoritativeSecurityStatuses, out var status) &&
        status == SecurityStatusDto.Active;

    private static bool TryGetAuthoritativeSecurityStatus(
        Guid? securityId,
        IReadOnlyDictionary<Guid, SecurityStatusDto> authoritativeSecurityStatuses,
        out SecurityStatusDto status)
    {
        status = default;
        var resolvedSecurityId = securityId.GetValueOrDefault();
        return resolvedSecurityId != Guid.Empty &&
            authoritativeSecurityStatuses.TryGetValue(resolvedSecurityId, out status);
    }

    private static bool IsInstrumentBearingJournalLine(OperationsLedgerJournalLineDto line)
        => !string.IsNullOrWhiteSpace(line.Symbol) ||
           line.SecurityId.GetValueOrDefault() != Guid.Empty ||
           !string.IsNullOrWhiteSpace(line.SecurityMasterProvenance) ||
           !string.IsNullOrWhiteSpace(line.LedgerMappingReference) ||
           IsInstrumentAccountName(line.AccountName);

    private static bool IsInstrumentAccountName(string? accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return false;
        }

        var normalized = accountName.Trim();
        return normalized.Equals("Securities", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Dividend Receivable", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Accrued Interest Receivable", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Corporate Action Distribution", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Short Securities Payable", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Option Premium Asset", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Option Premium Liability", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Futures MTM Settlement", StringComparison.OrdinalIgnoreCase);
    }

    private static JournalEntryMetadata? ToJournalEntryMetadata(
        OperationsLedgerJournalCandidateDto candidate,
        IReadOnlyDictionary<Guid, SecurityStatusDto> authoritativeSecurityStatuses)
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

        var securityMasterLineage = BuildSecurityMasterLineageTag(candidate.Lines, authoritativeSecurityStatuses);
        if (!string.IsNullOrWhiteSpace(securityMasterLineage))
        {
            tags["securityMasterLineage"] = securityMasterLineage;
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

    private static string? BuildSecurityMasterLineageTag(
        IReadOnlyList<OperationsLedgerJournalLineDto>? lines,
        IReadOnlyDictionary<Guid, SecurityStatusDto> authoritativeSecurityStatuses)
    {
        var mappedLines = (lines ?? [])
            .Where(static line => !string.IsNullOrWhiteSpace(line.Symbol))
            .Select(line =>
            {
                var symbol = line.Symbol!.Trim();
                var securityIdValue = line.SecurityId.GetValueOrDefault();
                var securityId = securityIdValue == Guid.Empty
                    ? "missing"
                    : securityIdValue.ToString("N");
                var provenance = string.IsNullOrWhiteSpace(line.SecurityMasterProvenance)
                    ? "missing"
                    : line.SecurityMasterProvenance.Trim();
                var mapping = string.IsNullOrWhiteSpace(line.LedgerMappingReference)
                    ? "missing"
                    : line.LedgerMappingReference.Trim();
                var approval = string.IsNullOrWhiteSpace(line.SecurityMasterApprovalReference)
                    ? "missing"
                    : line.SecurityMasterApprovalReference.Trim();
                var status = TryGetAuthoritativeSecurityStatus(line.SecurityId, authoritativeSecurityStatuses, out var authoritativeStatus)
                    ? $"security-status:{authoritativeStatus}"
                    : "missing";
                return $"{symbol}:{securityId}:{mapping}:{approval}:{status}:{provenance}";
            })
            .ToArray();

        return mappedLines.Length == 0 ? null : string.Join("|", mappedLines);
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
        var normalized = NormalizeEvidence(evidenceLinks).ToList();
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

    private static OperationsTransitionResultDto Success(OperationsContinuityWorkflowDto workflow) =>
        new(true, null, null, workflow, workflow.Blockers, workflow.NextActions);

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
        if (actionOrigin == OperationsActionOriginDto.HumanOperator)
        {
            return null;
        }

        var blocker = new OperationsWorkflowBlockerDto(
            "REVIEWED_AUTOMATION_MATERIAL_ACTION_REJECTED",
            $"{actionLabel} requires a human operator origin; reviewed automation may suggest, summarize, draft, and flag but cannot mutate the operating record.",
            gate,
            "Critical",
            []);

        return Failure("REVIEWED_AUTOMATION_REVIEW_REQUIRED", blocker.Message, [blocker]);
    }

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
