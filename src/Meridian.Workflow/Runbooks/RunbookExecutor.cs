using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Operations;

namespace Meridian.Workflow.Runbooks;

public interface IRunbookExecutor
{
    Task<RunbookExecutionResult> ExecuteAsync(RunbookDefinition definition, bool dryRun, CancellationToken ct = default);
}

public interface IRunbookStepHandler
{
    string Kind { get; }
    Task<VerifiedOperationOutcome> ExecuteAsync(RunbookStep step, CancellationToken ct = default);
}

public sealed class RunbookExecutor : IRunbookExecutor
{
    private const string RunbookCaseType = "runbook-execution";
    private const string RunbookActor = "system:runbook-executor";
    private const string ExpectedPreviousCaseSequenceDataKey = "expectedPreviousCaseSequence";
    private const string ExpectedPreviousCaseRecordHashDataKey = "expectedPreviousCaseRecordHashSha256";

    private readonly IOperationalCaseHistoryStore _caseHistoryStore;
    private readonly IReadOnlyDictionary<string, IRunbookStepHandler> _stepHandlers;

    public RunbookExecutor(
        IOperationalCaseHistoryStore caseHistoryStore,
        IEnumerable<IRunbookStepHandler>? stepHandlers = null)
    {
        _caseHistoryStore = caseHistoryStore ?? throw new ArgumentNullException(nameof(caseHistoryStore));
        _stepHandlers = (stepHandlers ?? [])
            .GroupBy(handler => handler.Kind, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<RunbookExecutionResult> ExecuteAsync(
        RunbookDefinition definition,
        bool dryRun,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ct.ThrowIfCancellationRequested();

        var started = DateTimeOffset.UtcNow;
        var operationId = $"runbook:{definition.Id}:{Guid.NewGuid():N}";
        var inputHash = ComputeInputHash(definition);
        var messages = new List<string> { $"Runbook '{definition.Name}' started ({(dryRun ? "dry-run" : "execute")})." };
        OperationalCaseHistoryRecord? historyPredecessor = null;

        if (dryRun)
        {
            var inspected = 0;
            try
            {
                for (var i = 0; i < definition.Steps.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    messages.Add($"Step {i + 1}: {definition.Steps[i].Kind} inspected.");
                    historyPredecessor = await AppendInspectionHistoryAsync(
                        definition,
                        operationId,
                        inputHash,
                        i,
                        DateTimeOffset.UtcNow,
                        historyPredecessor,
                        ct).ConfigureAwait(false);
                    inspected++;
                }
            }
            catch (OperationCanceledException cancellation)
            {
                return await TerminalizeCancellationAsync(
                    definition,
                    started,
                    operationId,
                    inputHash,
                    messages,
                    $"Inspected {inspected} of {definition.Steps.Count} step(s) before cancellation.",
                    [],
                    [],
                    historyPredecessor,
                    cancellation).ConfigureAwait(false);
            }
            catch (CaseHistoryAppendException failure)
            {
                return CreateCaseHistoryAppendFailureResult(
                    definition,
                    started,
                    operationId,
                    inputHash,
                    messages,
                    "dry-run step inspection",
                    failure.InnerException ?? failure);
            }

            messages.Add("Runbook dry-run completed; no steps were executed.");
            var completed = DateTimeOffset.UtcNow;
            var evidence = Evidence(operationId, "dry-run-inspection", $"Inspected {definition.Steps.Count} step kind(s); raw step payloads were not retained.", completed);
            var outcome = Validate(new VerifiedOperationOutcome(
                operationId,
                "runbook.execute",
                OperationTerminalState.Succeeded,
                started,
                completed,
                1,
                definition.Id,
                inputHash,
                [
                    Postcondition("steps-inspected", $"All {definition.Steps.Count} runbook step(s) were inspected.", OperationPostconditionState.Satisfied, evidence.EvidenceId),
                    Postcondition("steps-executed", "Dry-run executed zero runbook steps.", OperationPostconditionState.Satisfied, evidence.EvidenceId)
                ],
                [evidence],
                [],
                [],
                []));
            try
            {
                await AppendTerminalHistoryAsync(
                        definition,
                        outcome,
                        inputHash,
                        "Dry-run inspection completed.",
                        historyPredecessor,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (CaseHistoryAppendException failure)
            {
                return CreateCaseHistoryAppendFailureResult(
                    definition,
                    started,
                    operationId,
                    inputHash,
                    messages,
                    "dry-run terminalization",
                    failure.InnerException ?? failure);
            }
            return new RunbookExecutionResult(definition.Id, started, completed, outcome, messages);
        }

        var missingKinds = definition.Steps
            .Select(step => step.Kind)
            .Where(kind => !_stepHandlers.ContainsKey(kind))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingKinds.Length > 0)
        {
            var completed = DateTimeOffset.UtcNow;
            messages.Add($"Runbook blocked: no execution handler is registered for {string.Join(", ", missingKinds)}. No steps were executed.");
            var evidence = Evidence(operationId, "handler-discovery", $"Missing handler kind(s): {string.Join(", ", missingKinds)}.", completed);
            var outcome = Validate(new VerifiedOperationOutcome(
                operationId,
                "runbook.execute",
                OperationTerminalState.Blocked,
                started,
                completed,
                1,
                definition.Id,
                inputHash,
                [
                    Postcondition("handlers-available", "Every step kind has a registered execution handler.", OperationPostconditionState.NotSatisfied, evidence.EvidenceId),
                    Postcondition("steps-executed", "No runbook step was executed while prerequisites were missing.", OperationPostconditionState.Satisfied, evidence.EvidenceId)
                ],
                [evidence],
                [],
                [new OperationIssue(
                    "runbook-handlers-missing",
                    $"No execution handler is registered for {string.Join(", ", missingKinds)}.",
                    OperationIssueSeverity.Error,
                    EvidenceId: evidence.EvidenceId)
                {
                    IsBlocking = true
                }],
                [new OperationRecoveryAction(
                    "register-runbook-handlers",
                    "Register runbook handlers",
                    $"Register reviewed handlers for {string.Join(", ", missingKinds)} and retry the runbook.",
                    Retryable: true,
                    RequiresHumanAction: true)
                {
                    EvidenceIds = [evidence.EvidenceId]
                }]));
            try
            {
                await AppendTerminalHistoryAsync(
                        definition,
                        outcome,
                        inputHash,
                        messages[^1],
                        historyPredecessor,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (CaseHistoryAppendException failure)
            {
                return CreateCaseHistoryAppendFailureResult(
                    definition,
                    started,
                    operationId,
                    inputHash,
                    messages,
                    "blocked terminalization",
                    failure.InnerException ?? failure);
            }
            return new RunbookExecutionResult(definition.Id, started, completed, outcome, messages);
        }

        var stepEvidence = new List<OperationEvidenceReference>();
        var artifacts = new List<OperationArtifactReference>();
        var issues = new List<OperationIssue>();
        var recovery = new List<OperationRecoveryAction>();
        var executed = 0;
        var terminalState = OperationTerminalState.Succeeded;

        for (var i = 0; i < definition.Steps.Count; i++)
        {
            var step = definition.Steps[i];
            var handler = _stepHandlers[step.Kind];
            try
            {
                ct.ThrowIfCancellationRequested();
                var stepOutcome = Validate(await handler.ExecuteAsync(step, ct).ConfigureAwait(false));
                executed++;
                var artifactIdMap = stepOutcome.Artifacts.ToDictionary(
                    artifact => artifact.ArtifactId,
                    artifact => $"step-{i + 1}:{artifact.ArtifactId}",
                    StringComparer.Ordinal);
                artifacts.AddRange(stepOutcome.Artifacts.Select(artifact => artifact with
                {
                    ArtifactId = artifactIdMap[artifact.ArtifactId]
                }));
                var evidenceId = $"{operationId}:step:{i + 1}";
                stepEvidence.Add(EvidenceReference(
                    evidenceId,
                    "runbook-step-outcome",
                    $"Step {i + 1} ({step.Kind}) returned {stepOutcome.State}; child operation {stepOutcome.OperationId}.",
                    DateTimeOffset.UtcNow));
                messages.Add($"Step {i + 1}: {step.Kind} returned {stepOutcome.State}.");

                try
                {
                    historyPredecessor = await AppendStepHistoryAsync(
                        definition,
                        operationId,
                        inputHash,
                        i,
                        step,
                        stepOutcome,
                        historyPredecessor,
                        ct).ConfigureAwait(false);
                }
                catch (CaseHistoryAppendException failure)
                {
                    return CreateCaseHistoryAppendFailureResult(
                        definition,
                        started,
                        operationId,
                        inputHash,
                        messages,
                        $"step {i + 1} terminal receipt",
                        failure.InnerException ?? failure);
                }

                issues.AddRange(stepOutcome.Issues.Select(issue => issue with
                {
                    Code = $"step-{i + 1}:{issue.Code}",
                    EvidenceId = evidenceId,
                    ArtifactId = issue.ArtifactId is { } artifactId
                        ? artifactIdMap[artifactId]
                        : null
                }));
                recovery.AddRange(stepOutcome.Recovery.Select(action => action with
                {
                    ActionId = $"step-{i + 1}:{action.ActionId}",
                    EvidenceIds = [evidenceId],
                    ArtifactIds = action.ArtifactIds.Select(artifactId => artifactIdMap[artifactId]).ToArray()
                }));

                if (stepOutcome.State == OperationTerminalState.CompletedWithWarnings)
                {
                    terminalState = OperationTerminalState.CompletedWithWarnings;
                }
                else if (stepOutcome.State is OperationTerminalState.Failed or OperationTerminalState.Blocked)
                {
                    terminalState = stepOutcome.State;
                    if (recovery.Count == 0)
                    {
                        recovery.Add(new OperationRecoveryAction(
                            $"retry-step-{i + 1}",
                            "Recover and retry",
                            $"Follow the recovery guidance for {step.Kind}, then rerun the runbook.",
                            Retryable: true,
                            RequiresHumanAction: true)
                        {
                            EvidenceIds = [evidenceId]
                        });
                    }
                    break;
                }
            }
            catch (OperationCanceledException cancellation)
            {
                return await TerminalizeCancellationAsync(
                    definition,
                    started,
                    operationId,
                    inputHash,
                    messages,
                    $"Executed {executed} of {definition.Steps.Count} step(s) before cancellation.",
                    stepEvidence,
                    artifacts,
                    historyPredecessor,
                    cancellation).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                terminalState = OperationTerminalState.Failed;
                var evidenceId = $"{operationId}:step:{i + 1}:exception";
                var exceptionEvidence = EvidenceReference(
                    evidenceId,
                    "runbook-step-exception",
                    $"Step {i + 1} ({step.Kind}) threw {ex.GetType().Name}.",
                    DateTimeOffset.UtcNow);
                stepEvidence.Add(exceptionEvidence);
                issues.Add(new OperationIssue(
                    $"step-{i + 1}-exception",
                    ex.Message,
                    OperationIssueSeverity.Error,
                    ex.GetType().FullName,
                    evidenceId));
                recovery.Add(new OperationRecoveryAction(
                    $"retry-step-{i + 1}",
                    "Correct and retry step",
                    $"Correct the {step.Kind} handler failure, verify its evidence, and rerun the runbook.",
                    Retryable: true,
                    RequiresHumanAction: true)
                {
                    EvidenceIds = [evidenceId]
                });
                messages.Add($"Step {i + 1}: {step.Kind} failed. No later steps were executed.");
                var childFailure = Validate(new VerifiedOperationOutcome(
                    $"{operationId}:step:{i + 1}:handler",
                    $"runbook.step.{step.Kind}",
                    OperationTerminalState.Failed,
                    exceptionEvidence.CapturedAtUtc ?? DateTimeOffset.UtcNow,
                    exceptionEvidence.CapturedAtUtc ?? DateTimeOffset.UtcNow,
                    1,
                    operationId,
                    ComputeStepInputHash(step),
                    [Postcondition(
                        "step-executed",
                        $"The {step.Kind} handler completed its required work.",
                        OperationPostconditionState.NotSatisfied,
                        evidenceId)],
                    [exceptionEvidence],
                    [],
                    [new OperationIssue(
                        "handler-exception",
                        ex.Message,
                        OperationIssueSeverity.Error,
                        ex.GetType().FullName,
                        evidenceId)],
                    [new OperationRecoveryAction(
                        "correct-and-retry-handler",
                        "Correct and retry handler",
                        $"Correct the {step.Kind} handler failure and retry the runbook.",
                        Retryable: true,
                        RequiresHumanAction: true)
                    {
                        EvidenceIds = [evidenceId]
                    }]));
                try
                {
                    historyPredecessor = await AppendStepHistoryAsync(
                        definition,
                        operationId,
                        inputHash,
                        i,
                        step,
                        childFailure,
                        historyPredecessor,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (CaseHistoryAppendException failure)
                {
                    return CreateCaseHistoryAppendFailureResult(
                        definition,
                        started,
                        operationId,
                        inputHash,
                        messages,
                        $"failed step {i + 1} terminalization",
                        failure.InnerException ?? failure);
                }
                break;
            }
        }

        var finished = DateTimeOffset.UtcNow;
        var allStepsExecuted =
            executed == definition.Steps.Count &&
            terminalState is OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings;
        var summaryEvidence = EvidenceReference(
            $"{operationId}:execution-summary",
            "runbook-execution-summary",
            $"Executed {executed} of {definition.Steps.Count} runbook step(s); raw step payloads were not retained.",
            finished);
        stepEvidence.Add(summaryEvidence);
        if (terminalState == OperationTerminalState.Succeeded)
            messages.Add("Runbook completed and all step outcomes were verified.");
        else if (terminalState == OperationTerminalState.CompletedWithWarnings)
            messages.Add("Runbook completed with warnings; review retained step evidence.");

        var outcomeResult = Validate(new VerifiedOperationOutcome(
            operationId,
            "runbook.execute",
            terminalState,
            started,
            finished,
            1,
            definition.Id,
            inputHash,
            [
                Postcondition("handlers-available", "Every step kind has a registered execution handler.", OperationPostconditionState.Satisfied, summaryEvidence.EvidenceId),
                Postcondition(
                    "steps-executed",
                    $"All {definition.Steps.Count} runbook step(s) returned a non-blocking terminal outcome.",
                    allStepsExecuted ? OperationPostconditionState.Satisfied : OperationPostconditionState.NotSatisfied,
                    stepEvidence.Select(item => item.EvidenceId).ToArray()) with
                {
                    ArtifactIds = artifacts.Select(item => item.ArtifactId).ToArray()
                }
            ],
            stepEvidence,
            artifacts,
            issues,
            terminalState == OperationTerminalState.CompletedWithWarnings && recovery.Count == 0
                ? [new OperationRecoveryAction(
                    "review-runbook-warnings",
                    "Review runbook warnings",
                    "Review the retained step outcome evidence before relying on downstream results.",
                    Retryable: false,
                    RequiresHumanAction: true)
                {
                    EvidenceIds = stepEvidence.Select(item => item.EvidenceId).ToArray()
                }]
                : recovery));

        try
        {
            await AppendTerminalHistoryAsync(
                definition,
                outcomeResult,
                inputHash,
                messages[^1],
                historyPredecessor,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (CaseHistoryAppendException failure)
        {
            return CreateCaseHistoryAppendFailureResult(
                definition,
                started,
                operationId,
                inputHash,
                messages,
                "runbook terminalization",
                failure.InnerException ?? failure);
        }
        return new RunbookExecutionResult(definition.Id, started, finished, outcomeResult, messages);
    }

    private async Task<RunbookExecutionResult> TerminalizeCancellationAsync(
        RunbookDefinition definition,
        DateTimeOffset started,
        string operationId,
        string inputHash,
        List<string> messages,
        string progressDescription,
        IReadOnlyList<OperationEvidenceReference> retainedEvidence,
        IReadOnlyList<OperationArtifactReference> retainedArtifacts,
        OperationalCaseHistoryRecord? historyPredecessor,
        OperationCanceledException cancellation)
    {
        var completed = DateTimeOffset.UtcNow;
        var cancellationEvidence = EvidenceReference(
            $"{operationId}:cancellation",
            "runbook-cancellation",
            $"{progressDescription} The cancellation was converted to a terminal failure after execution admission.",
            completed);
        var evidence = retainedEvidence.Concat([cancellationEvidence]).ToArray();
        messages.Add($"Runbook cancelled after admission. {progressDescription}");
        var outcome = Validate(new VerifiedOperationOutcome(
            operationId,
            "runbook.execute",
            OperationTerminalState.Failed,
            started,
            completed,
            1,
            definition.Id,
            inputHash,
            [Postcondition(
                "runbook-completed",
                "The runbook completed every required inspection or execution step.",
                OperationPostconditionState.NotSatisfied,
                evidence.Select(item => item.EvidenceId).ToArray()) with
            {
                ArtifactIds = retainedArtifacts.Select(item => item.ArtifactId).ToArray()
            }],
            evidence,
            retainedArtifacts,
            [new OperationIssue(
                "runbook-cancelled-after-admission",
                "The runbook was cancelled after admission and did not complete all required work.",
                OperationIssueSeverity.Error,
                cancellation.GetType().FullName,
                cancellationEvidence.EvidenceId)],
            [new OperationRecoveryAction(
                "review-and-resume-runbook",
                "Review and resume runbook",
                "Review retained step evidence for side effects, repair any incomplete step, and rerun the runbook from a safe point.",
                Retryable: true,
                RequiresHumanAction: true)
            {
                EvidenceIds = evidence.Select(item => item.EvidenceId).ToArray(),
                ArtifactIds = retainedArtifacts.Select(item => item.ArtifactId).ToArray()
            }]));

        try
        {
            await AppendTerminalHistoryAsync(
                definition,
                outcome,
                inputHash,
                messages[^1],
                historyPredecessor,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (CaseHistoryAppendException failure)
        {
            return CreateCaseHistoryAppendFailureResult(
                definition,
                started,
                operationId,
                inputHash,
                messages,
                "cancellation terminalization",
                failure.InnerException ?? failure);
        }
        return new RunbookExecutionResult(definition.Id, started, completed, outcome, messages);
    }

    private static RunbookExecutionResult CreateCaseHistoryAppendFailureResult(
        RunbookDefinition definition,
        DateTimeOffset started,
        string operationId,
        string inputHash,
        List<string> messages,
        string stage,
        Exception failure)
    {
        var completed = DateTimeOffset.UtcNow;
        var evidence = EvidenceReference(
            $"{operationId}:case-history-append-failure",
            "case-history-append-failure",
            $"The executor observed a durable case-history append failure while recording {stage}; the attempted case-history record is not claimed as retained.",
            completed);
        var message = $"Runbook cannot be verified because durable case-history persistence failed while recording {stage}. The attempted case-history record is not claimed as retained.";
        messages.Add(message);
        var outcome = Validate(new VerifiedOperationOutcome(
            operationId,
            "runbook.execute",
            OperationTerminalState.Failed,
            started,
            completed,
            1,
            definition.Id,
            inputHash,
            [Postcondition(
                "case-history-terminal-receipt-persisted",
                "The runbook terminal outcome was durably recorded in case history.",
                OperationPostconditionState.NotSatisfied,
                evidence.EvidenceId)],
            [evidence],
            [],
            [new OperationIssue(
                "runbook-case-history-append-failed",
                $"Durable case-history append failed while recording {stage}: {failure.Message}",
                OperationIssueSeverity.Error,
                failure.GetType().FullName,
                evidence.EvidenceId)],
            [new OperationRecoveryAction(
                "restore-case-history-and-reconcile-runbook",
                "Restore case-history persistence and reconcile runbook effects",
                "Restore durable case-history persistence, determine whether any runbook step side effects occurred, record the reconciled terminal receipt, and retry only from a safe point.",
                Retryable: true,
                RequiresHumanAction: true)
            {
                EvidenceIds = [evidence.EvidenceId]
            }]));
        return new RunbookExecutionResult(definition.Id, started, completed, outcome, messages);
    }

    private async ValueTask<OperationalCaseHistoryRecord> AppendInspectionHistoryAsync(
        RunbookDefinition definition,
        string operationId,
        string inputHash,
        int stepIndex,
        DateTimeOffset occurredAtUtc,
        OperationalCaseHistoryRecord? predecessor,
        CancellationToken ct)
    {
        var step = definition.Steps[stepIndex];
        var evidence = EvidenceReference(
            $"{operationId}:inspection:{stepIndex + 1}",
            "runbook-step-inspection",
            $"Step {stepIndex + 1} ({step.Kind}) was inspected without execution or raw payload retention.",
            occurredAtUtc);
        try
        {
            return await _caseHistoryStore.AppendAsync(new OperationalCaseHistoryAppendRequest
            {
                CaseId = operationId,
                CaseType = RunbookCaseType,
                HistoryEventId = $"{operationId}:inspection:{stepIndex + 1}",
                EventType = "runbook.step.inspected",
                OccurredAtUtc = occurredAtUtc,
                ActorId = RunbookActor,
                Reason = $"Dry-run inspected step {stepIndex + 1} ({step.Kind}); no handler executed.",
                CorrelationId = definition.Id,
                InputHashSha256 = inputHash,
                Data = WithExpectedPredecessor(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["definitionId"] = definition.Id,
                        ["stepIndex"] = (stepIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["stepKind"] = step.Kind,
                        ["stepInputHashSha256"] = ComputeStepInputHash(step)
                    },
                    predecessor),
                Transition = new OperationalCaseStateTransition
                {
                    PreviousState = predecessor?.Transition?.CurrentState,
                    CurrentState = "Inspected",
                    TransitionedAtUtc = occurredAtUtc
                },
                Evidence = [evidence]
            }, ct).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            throw new CaseHistoryAppendException(failure);
        }
    }

    private async ValueTask<OperationalCaseHistoryRecord> AppendStepHistoryAsync(
        RunbookDefinition definition,
        string operationId,
        string inputHash,
        int stepIndex,
        RunbookStep step,
        VerifiedOperationOutcome childOutcome,
        OperationalCaseHistoryRecord? predecessor,
        CancellationToken ct)
    {
        var exceptions = childOutcome.Issues
            .Where(issue => issue.Severity == OperationIssueSeverity.Error)
            .Select(issue => new OperationalCaseException
            {
                ExceptionType = issue.ExceptionType ?? "RunbookStepFailure",
                Message = issue.Message,
                OccurredAtUtc = childOutcome.CompletedAtUtc,
                EvidenceIds = issue.EvidenceId is null ? [] : [issue.EvidenceId]
            })
            .ToArray();
        try
        {
            return await _caseHistoryStore.AppendAsync(new OperationalCaseHistoryAppendRequest
            {
                CaseId = operationId,
                CaseType = RunbookCaseType,
                HistoryEventId = childOutcome.OperationId,
                EventType = "runbook.step.terminal",
                OccurredAtUtc = childOutcome.CompletedAtUtc,
                ActorId = RunbookActor,
                Reason = $"Step {stepIndex + 1} ({step.Kind}) returned {childOutcome.State}.",
                CorrelationId = childOutcome.CorrelationId!,
                InputHashSha256 = childOutcome.InputHashSha256!,
                Data = WithExpectedPredecessor(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["definitionId"] = definition.Id,
                        ["stepIndex"] = (stepIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["stepKind"] = step.Kind,
                        ["stepInputHashSha256"] = ComputeStepInputHash(step),
                        ["childOperationId"] = childOutcome.OperationId
                    },
                    predecessor),
                Transition = new OperationalCaseStateTransition
                {
                    PreviousState = predecessor?.Transition?.CurrentState,
                    CurrentState = childOutcome.State.ToString(),
                    TransitionedAtUtc = childOutcome.CompletedAtUtc
                },
                Exceptions = exceptions,
                Evidence = childOutcome.Evidence,
                Artifacts = childOutcome.Artifacts,
                TerminalOutcome = childOutcome
            }, ct).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            throw new CaseHistoryAppendException(failure);
        }
    }

    private async ValueTask AppendTerminalHistoryAsync(
        RunbookDefinition definition,
        VerifiedOperationOutcome outcome,
        string inputHash,
        string reason,
        OperationalCaseHistoryRecord? predecessor,
        CancellationToken ct)
    {
        var exceptions = outcome.Issues
            .Where(issue => issue.Severity == OperationIssueSeverity.Error)
            .Select(issue => new OperationalCaseException
            {
                ExceptionType = issue.ExceptionType ?? "RunbookExecutionFailure",
                Message = issue.Message,
                OccurredAtUtc = outcome.CompletedAtUtc,
                EvidenceIds = issue.EvidenceId is null ? [] : [issue.EvidenceId]
            })
            .ToArray();
        try
        {
            await _caseHistoryStore.AppendAsync(new OperationalCaseHistoryAppendRequest
            {
                CaseId = outcome.OperationId,
                CaseType = RunbookCaseType,
                HistoryEventId = outcome.OperationId,
                EventType = $"runbook.terminal.{outcome.State.ToString().ToLowerInvariant()}",
                OccurredAtUtc = outcome.CompletedAtUtc,
                ActorId = RunbookActor,
                Reason = reason,
                CorrelationId = outcome.CorrelationId!,
                InputHashSha256 = outcome.InputHashSha256!,
                Data = WithExpectedPredecessor(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["definitionId"] = definition.Id,
                        ["stepCount"] = definition.Steps.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["terminalState"] = outcome.State.ToString()
                    },
                    predecessor),
                Transition = new OperationalCaseStateTransition
                {
                    PreviousState = predecessor?.Transition?.CurrentState,
                    CurrentState = outcome.State.ToString(),
                    TransitionedAtUtc = outcome.CompletedAtUtc
                },
                Exceptions = exceptions,
                Evidence = outcome.Evidence,
                Artifacts = outcome.Artifacts,
                TerminalOutcome = outcome
            }, ct).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            throw new CaseHistoryAppendException(failure);
        }
    }

    private static IReadOnlyDictionary<string, string> WithExpectedPredecessor(
        IReadOnlyDictionary<string, string> data,
        OperationalCaseHistoryRecord? predecessor)
    {
        var coordinated = new Dictionary<string, string>(data, StringComparer.Ordinal)
        {
            [ExpectedPreviousCaseSequenceDataKey] =
                (predecessor?.Sequence ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (predecessor is not null)
            coordinated[ExpectedPreviousCaseRecordHashDataKey] = predecessor.RecordHashSha256;
        return coordinated;
    }

    private static OperationPostcondition Postcondition(
        string code,
        string description,
        OperationPostconditionState state,
        params string[] evidenceIds) =>
        new(code, description, state, Required: true, EvidenceIds: evidenceIds);

    private static OperationEvidenceReference Evidence(
        string operationId,
        string kind,
        string description,
        DateTimeOffset capturedAtUtc) =>
        EvidenceReference($"{operationId}:evidence", kind, description, capturedAtUtc);

    private static OperationEvidenceReference EvidenceReference(
        string evidenceId,
        string kind,
        string description,
        DateTimeOffset capturedAtUtc)
    {
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{evidenceId}\n{kind}\n{description}\n{capturedAtUtc:O}")));
        return new OperationEvidenceReference(
            evidenceId,
            kind,
            description,
            Uri: $"urn:sha256:{contentHash}",
            ContentHashSha256: contentHash,
            CapturedAtUtc: capturedAtUtc);
    }

    private static VerifiedOperationOutcome Validate(VerifiedOperationOutcome outcome) =>
        VerifiedOperationOutcomeValidator.ValidateAndThrow(outcome);

    private static string ComputeInputHash(RunbookDefinition definition)
    {
        var builder = new StringBuilder()
            .Append(definition.Id).Append('\n')
            .Append(definition.UpdatedAtUtc.ToUniversalTime().ToString("O"));
        foreach (var step in definition.Steps)
        {
            var payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(step.Payload ?? string.Empty)));
            builder.Append('\n').Append(step.Kind).Append(':').Append(payloadHash);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string ComputeStepInputHash(RunbookStep step)
    {
        var value = $"{step.Kind}\n{step.Payload}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private sealed class CaseHistoryAppendException(Exception innerException)
        : Exception("The durable case-history append failed.", innerException);
}
