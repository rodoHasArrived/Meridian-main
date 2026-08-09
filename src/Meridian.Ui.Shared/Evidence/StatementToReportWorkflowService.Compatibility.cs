using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Evidence;

#pragma warning disable CS0618 // This file intentionally retains the pre-rename public contract.

/// <summary>Compatibility command for callers compiled against the pre-rename operation.</summary>
[Obsolete("Use StatementReconciliationReportStartCommand.")]
public sealed record StatementToReportStartCommand(
    StatementImportCommitRequest Import,
    string TenantId,
    string? CompanyId);

/// <summary>Compatibility execution result for callers compiled against the pre-rename operation.</summary>
[Obsolete("Use StatementReconciliationReportWorkflowExecution.")]
public sealed record StatementToReportWorkflowExecution(
    StatementImportCommitResultDto? ImportResult,
    StatementToReportWorkflowDto Workflow);

/// <summary>Compatibility artifact result for callers compiled against the pre-rename operation.</summary>
[Obsolete("Use StatementReconciliationReportArtifactDownload.")]
public sealed record StatementToReportArtifactDownload(
    StatementToReportArtifactDto Descriptor,
    byte[] Content);

/// <summary>
/// Source- and binary-compatible adapter over the single statement reconciliation report workflow.
/// It retains the old public API and wire links without owning storage, orchestration, or rendering.
/// </summary>
[Obsolete("Use StatementReconciliationReportWorkflowService.")]
public sealed class StatementToReportWorkflowService
{
    private readonly StatementReconciliationReportWorkflowService _inner;

    public StatementToReportWorkflowService(
        IStatementImportCommitService imports,
        IStatementImportEvidenceRetainer evidence,
        IStatementRunWorkflowService statementRuns,
        string dataRoot,
        ILogger<StatementToReportWorkflowService>? logger = null)
        : this(new StatementReconciliationReportWorkflowService(
            imports,
            evidence,
            statementRuns,
            dataRoot))
    {
        _ = logger;
    }

    internal StatementToReportWorkflowService(StatementReconciliationReportWorkflowService inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public async Task<StatementToReportWorkflowExecution> StartAsync(
        StatementToReportStartCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var execution = await _inner.StartAsync(
            new StatementReconciliationReportStartCommand(
                command.Import,
                command.TenantId,
                command.CompanyId),
            ct).ConfigureAwait(false);
        return new StatementToReportWorkflowExecution(
            execution.ImportResult,
            ToLegacyWorkflow(execution.Workflow));
    }

    public async Task<StatementToReportWorkflowDto?> GetAsync(
        string workflowId,
        string tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        var workflow = await _inner.GetAsync(workflowId, tenantId, companyId, ct).ConfigureAwait(false);
        return workflow is null ? null : ToLegacyWorkflow(workflow);
    }

    public async Task<StatementToReportWorkflowExecution?> ResumeAsync(
        string workflowId,
        string tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        var execution = await _inner.ResumeAsync(workflowId, tenantId, companyId, ct).ConfigureAwait(false);
        return execution is null
            ? null
            : new StatementToReportWorkflowExecution(
                execution.ImportResult,
                ToLegacyWorkflow(execution.Workflow));
    }

    public async Task<StatementToReportArtifactDownload?> DownloadArtifactAsync(
        string workflowId,
        string artifactId,
        string tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        var artifact = await _inner.DownloadArtifactAsync(
            workflowId,
            artifactId,
            tenantId,
            companyId,
            ct).ConfigureAwait(false);
        return artifact is null
            ? null
            : new StatementToReportArtifactDownload(
                ToLegacyArtifact(workflowId, artifact.Descriptor),
                artifact.Content);
    }

    internal static StatementToReportWorkflowDto ToLegacyWorkflow(
        StatementReconciliationReportWorkflowDto workflow)
        => new(
            workflow.WorkflowId,
            workflow.Status switch
            {
                StatementReconciliationReportWorkflowStatusDto.InputRetained
                    => StatementToReportWorkflowStatusDto.InputRetained,
                StatementReconciliationReportWorkflowStatusDto.Importing
                    => StatementToReportWorkflowStatusDto.Importing,
                StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation
                    => StatementToReportWorkflowStatusDto.AwaitingReconciliation,
                StatementReconciliationReportWorkflowStatusDto.RenderingReconciliationReport
                    => StatementToReportWorkflowStatusDto.RenderingReport,
                StatementReconciliationReportWorkflowStatusDto.Completed
                    => StatementToReportWorkflowStatusDto.Completed,
                StatementReconciliationReportWorkflowStatusDto.Failed
                    => StatementToReportWorkflowStatusDto.Failed,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(workflow),
                    workflow.Status,
                    "Unknown workflow status.")
            },
            workflow.Version,
            workflow.TenantId,
            workflow.CompanyId,
            workflow.SourceInstitution,
            workflow.FundAccountId,
            workflow.ExternalAccountId,
            workflow.PeriodStart,
            workflow.PeriodEnd,
            workflow.StatementRunId,
            workflow.EvidenceVaultIdentity,
            workflow.RetainedArtifacts
                .Select(artifact => ToLegacyArtifact(workflow.WorkflowId, artifact))
                .ToArray(),
            workflow.EvidenceReferences,
            workflow.BreakCount,
            workflow.CaseCount,
            workflow.CreatedAtUtc,
            workflow.UpdatedAtUtc,
            workflow.CompletedAtUtc,
            workflow.FailureReason,
            workflow.RecoveryAction,
            BuildStatusRoute(workflow.WorkflowId),
            BuildResumeRoute(workflow.WorkflowId));

    private static StatementToReportArtifactDto ToLegacyArtifact(
        string workflowId,
        StatementReconciliationReportArtifactDto artifact)
        => new(
            artifact.ArtifactId,
            artifact.ArtifactKind,
            artifact.FileName,
            artifact.ContentType,
            artifact.ByteLength,
            artifact.ContentHashSha256,
            BuildArtifactRoute(workflowId, artifact.ArtifactId),
            artifact.RetainedAtUtc);

    private static string BuildStatusRoute(string workflowId)
        => UiApiRoutes.WithParam(
            UiApiRoutes.ReconciliationStatementToReportById,
            "workflowId",
            Uri.EscapeDataString(workflowId));

    private static string BuildResumeRoute(string workflowId)
        => UiApiRoutes.WithParam(
            UiApiRoutes.ReconciliationStatementToReportResume,
            "workflowId",
            Uri.EscapeDataString(workflowId));

    private static string BuildArtifactRoute(string workflowId, string artifactId)
        => UiApiRoutes.WithParam(
            UiApiRoutes.WithParam(
                UiApiRoutes.ReconciliationStatementToReportArtifact,
                "workflowId",
                Uri.EscapeDataString(workflowId)),
            "artifactId",
            Uri.EscapeDataString(artifactId));
}

#pragma warning restore CS0618
