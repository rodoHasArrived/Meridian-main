using System.Data.Common;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed partial class OperationsContinuityWorkflowService
{
    private const string PreparedWorkflowStartEvent = "prepared-workflow-started";

    public Task<OperationsTransitionResultDto> StartWorkflowAsync(
        OperationsStartWorkflowRequestDto request,
        CancellationToken ct = default) =>
        StartWorkflowCoreAsync(request, Guid.NewGuid(), "workflow-started", ct);

    private async Task<OperationsTransitionResultDto> StartWorkflowCoreAsync(
        OperationsStartWorkflowRequestDto request,
        Guid workflowId,
        string startEventType,
        CancellationToken ct)
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
        var openWorkflow = existingWorkflows.FirstOrDefault(workflow =>
            !workflow.IsClosed &&
            WorkflowScopesCollide(workflow.LedgerBookId, request.LedgerBookId));
        if (openWorkflow is not null)
        {
            return Failure(
                "WORKFLOW_ALREADY_EXISTS",
                $"An operations continuity workflow already exists for fund account '{request.FundAccountId}', period '{request.PeriodId.Trim()}', and ledger book '{FormatLedgerBookScope(request.LedgerBookId)}'.",
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
            workflowId,
            request.FundAccountId,
            request.PeriodId,
            request.SecurityMasterSnapshotId,
            request.BrokerSource,
            now,
            request.LedgerBookId);

        var evidence = OperationsContinuityWorkflowText.NormalizeEvidence(request.EvidenceLinks);
        var auditDraft = new OperationsWorkflowAuditDraft(
            workflow.WorkflowId,
            workflow.FundAccountId,
            workflow.PeriodId,
            startEventType,
            OperationsWorkflowStatusDto.NotStarted,
            _statusDerivation.Derive(workflow),
            OperationsGateKeyDto.BrokerIngest,
            OperationsGateStatusDto.NotStarted,
            OperationsGateStatusDto.InProgress,
            request.Actor.Trim(),
            OperationsContinuityWorkflowText.RedactSensitiveText(request.Rationale),
            OperationsContinuityWorkflowText.RedactSensitiveText(request.CorrelationId),
            evidence);

        if (_workflowStartCommitStore is not null)
        {
            var startCommit = await _workflowStartCommitStore
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

    /// <summary>
    /// Creates a fresh workflow under an identity retained by the preparation operation before this call.
    /// Recovery accepts only that exact prepared identity and accounting scope; it never adopts a plan by scope.
    /// </summary>
    public async Task<OperationsTransitionResultDto> StartPreparedWorkflowAsync(
        OperationsStartWorkflowRequestDto request,
        Guid workflowId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = ValidateStartRequest(request).ToList();
        if (workflowId == Guid.Empty)
            validation.Add(new OperationsWorkflowBlockerDto("WORKFLOW_ID_REQUIRED", "A retained preparation workflow identity is required.", null, "Error", []));
        if (request.LedgerBookId is null)
            validation.Add(new OperationsWorkflowBlockerDto("LEDGER_BOOK_REQUIRED", "A prepared workflow requires an authoritative ledger book.", null, "Error", []));
        if (validation.Count > 0)
            return Failure("VALIDATION_FAILED", "Prepared workflow start request is incomplete.", validation);

        var retained = await ReadPreparedWorkflowRecoveryAsync(request, workflowId, ct).ConfigureAwait(false);
        if (retained is not null)
            return retained;

        if (_workflowStartCommitStore is null)
            return Failure("ATOMIC_WORKFLOW_START_UNAVAILABLE", "Prepared workflows require atomic workflow and audit retention.", []);

        // Source snapshots and reviewed evidence belong to their original period, even if supplied by a caller.
        var freshRequest = request with { SecurityMasterSnapshotId = null, EvidenceLinks = null };
        try
        {
            var result = await StartWorkflowCoreAsync(freshRequest, workflowId, PreparedWorkflowStartEvent, ct).ConfigureAwait(false);
            if (result.Success)
                return result;

            // A concurrent request may have committed after the initial read but before the scope check.
            return await ReadPreparedWorkflowRecoveryAsync(request, workflowId, ct).ConfigureAwait(false) ?? result;
        }
        catch (Exception exception) when (exception is InvalidOperationException or DbException)
        {
            // Atomic stores refuse a racing insert. Recover only a retained, matching prepared workflow.
            var recovery = await ReadPreparedWorkflowRecoveryAsync(request, workflowId, ct).ConfigureAwait(false);
            if (recovery is not null)
                return recovery;
            throw;
        }
    }

    private async Task<OperationsTransitionResultDto?> ReadPreparedWorkflowRecoveryAsync(
        OperationsStartWorkflowRequestDto request,
        Guid workflowId,
        CancellationToken ct)
    {
        var retained = await _repository.GetAsync(workflowId, ct).ConfigureAwait(false);
        if (retained is null)
            return null;

        if (retained.FundAccountId != request.FundAccountId ||
            retained.LedgerBookId != request.LedgerBookId ||
            !string.Equals(retained.PeriodId, request.PeriodId.Trim(), StringComparison.OrdinalIgnoreCase))
            return Failure("PREPARED_WORKFLOW_ID_CONFLICT", "The retained preparation identity belongs to a different accounting scope.", []);

        var timeline = await _auditStore.GetTimelineAsync(workflowId, ct).ConfigureAwait(false);
        if (!timeline.Any(entry => entry.EventType == PreparedWorkflowStartEvent))
            return Failure("PREPARED_WORKFLOW_ID_CONFLICT", "The retained workflow was not created by this preparation operation.", []);

        return Success(await ToDtoAsync(retained, ct).ConfigureAwait(false));
    }
}
