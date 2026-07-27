using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class StatementReconciliationReportWorkflowService
{
    private static bool IsOpenCase(ReconciliationCase item)
        => !string.Equals(item.Status, "Resolved", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(item.Status, "Closed", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(item.Status, "Waived", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(item.Status, "Superseded", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(item.Status, "Dismissed", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(item.Status, "SignedOff", StringComparison.OrdinalIgnoreCase);

    private async Task<CurrentReconciliationGate> EvaluateCurrentReconciliationAsync(
        WorkflowSnapshot snapshot,
        CancellationToken ct)
    {
        var import = snapshot.ImportResult
            ?? throw new InvalidDataException(
                $"Statement reconciliation report workflow '{snapshot.Workflow.WorkflowId}' has no retained import checkpoint.");
        var reconciliation = await _statementRuns
            .GetAsync(import.RunId, ct)
            .ConfigureAwait(false);
        var queueGate = await EvaluateCanonicalQueueHandoffAsync(
                import,
                snapshot.Workflow.TenantId,
                snapshot.Workflow.CompanyId,
                ct)
            .ConfigureAwait(false);
        if (reconciliation is null)
        {
            var durableEvidenceVerified = await HasVerifiedCanonicalRunEvidenceAsync(
                    snapshot,
                    ct)
                .ConfigureAwait(false);
            if (durableEvidenceVerified && queueGate.IsSatisfied)
            {
                return CurrentReconciliationGate.Satisfied(reconciliation);
            }

            var unavailableRecoveryActions = new List<string>(capacity: 2)
            {
                durableEvidenceVerified
                    ? "The node-local statement run is unavailable. Complete every exact canonical queue obligation, then resume this workflow."
                    : "The canonical statement run is unavailable and no matching durable raw, canonical, and run-evidence snapshot could be verified. Restore the run authority or its immutable evidence, then resume this workflow."
            };
            if (!queueGate.IsSatisfied
                && (import.BreakCount > 0 || import.CaseCount > 0))
            {
                unavailableRecoveryActions.Add(
                    "Resolve or disposition the linked reconciliation breaks and cases, then resume this workflow.");
            }
            if (!queueGate.IsSatisfied)
            {
                unavailableRecoveryActions.Add(queueGate.RecoveryAction);
            }

            return new CurrentReconciliationGate(
                false,
                reconciliation,
                Math.Max(import.BreakCount, queueGate.OpenCaseCount),
                Math.Max(import.CaseCount, queueGate.BlockingCaseCount),
                string.Join(" ", unavailableRecoveryActions));
        }

        var openBreaks = reconciliation.Breaks.Count;
        var openCases = reconciliation.Cases.Count(IsOpenCase);

        if (openBreaks <= 0 && openCases <= 0 && queueGate.IsSatisfied)
        {
            return CurrentReconciliationGate.Satisfied(reconciliation);
        }

        var recoveryActions = new List<string>(capacity: 2);
        if (openBreaks > 0 || openCases > 0)
        {
            recoveryActions.Add(
                "Resolve or disposition the linked reconciliation breaks and cases, then resume this workflow.");
        }

        if (!queueGate.IsSatisfied)
        {
            recoveryActions.Add(queueGate.RecoveryAction);
        }

        return new CurrentReconciliationGate(
            false,
            reconciliation,
            Math.Max(openBreaks, queueGate.OpenCaseCount),
            Math.Max(openCases, queueGate.BlockingCaseCount),
            string.Join(" ", recoveryActions));
    }

    private Task<bool> HasVerifiedCanonicalRunEvidenceAsync(
        WorkflowSnapshot snapshot,
        CancellationToken ct)
    {
        if (!IsDurablyComposed
            || _evidence is not ReportingStatementImportEvidenceRetainer retainer
            || snapshot.ImportResult is not { } import)
        {
            return Task.FromResult(false);
        }

        return retainer.HasVerifiedCanonicalRunEvidenceAsync(
            import,
            BuildEvidenceRetentionRequest(snapshot),
            ct);
    }

    private static WorkflowSnapshot AwaitReconciliation(
        WorkflowSnapshot snapshot,
        CurrentReconciliationGate gate,
        bool advanceVersion,
        StatementReconciliationReportArtifactGenerationDto? archivedGeneration = null)
    {
        var import = snapshot.ImportResult;
        var artifactHistory = (snapshot.Workflow.ArtifactHistory ?? []).ToList();
        if (archivedGeneration is not null
            && artifactHistory.All(item => item.Generation != archivedGeneration.Generation))
        {
            artifactHistory.Add(archivedGeneration);
        }

        artifactHistory.Sort(static (left, right) => left.Generation.CompareTo(right.Generation));
        var evidenceReferences = snapshot.Workflow.EvidenceReferences
            .Concat(import is null ? [] : BuildEvidenceReferences(import))
            .Concat(archivedGeneration?.EvidenceReferences ?? [])
            .Where(static item =>
                !item.StartsWith("artifact:", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        var workflow = snapshot.Workflow with
        {
            Status = StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation,
            Version = advanceVersion
                ? snapshot.Workflow.Version + 1
                : snapshot.Workflow.Version,
            StatementRunId = import?.RunId ?? snapshot.Workflow.StatementRunId,
            EvidenceVaultIdentity =
                import?.EvidenceVaultIdentity ?? snapshot.Workflow.EvidenceVaultIdentity,
            RetainedArtifacts = [],
            EvidenceReferences = evidenceReferences,
            BreakCount = gate.OpenBreakCount,
            CaseCount = gate.OpenCaseCount,
            UpdatedAtUtc = advanceVersion
                ? DateTimeOffset.UtcNow
                : snapshot.Workflow.UpdatedAtUtc,
            CompletedAtUtc = null,
            FailureReason = null,
            RecoveryAction = gate.RecoveryAction,
            ArtifactGeneration = Math.Max(
                snapshot.Workflow.ArtifactGeneration,
                archivedGeneration?.Generation ?? 0),
            ArtifactHistory = artifactHistory
        };
        return snapshot with
        {
            Workflow = workflow,
            ResumeStatus = StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation,
            RenderingReconciliationReportAtUtc = null
        };
    }
}
