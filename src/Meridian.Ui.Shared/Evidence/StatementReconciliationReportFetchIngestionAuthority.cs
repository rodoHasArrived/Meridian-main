using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Ui.Shared.Services;

namespace Meridian.Ui.Shared.Evidence;

/// <summary>
/// Adapts scheduled connector fetches into the existing statement reconciliation report workflow.
/// It re-resolves the persisted accounting scope before the remote document is treated as an
/// authoritative intake and reports success only after Operations Continuity and canonical casework
/// publication have been retained.
/// </summary>
public sealed class StatementReconciliationReportFetchIngestionAuthority(
    StatementReconciliationReportWorkflowService workflow,
    IStatementReconciliationIntakeAuthority intakeAuthority) : IStatementFetchIngestionAuthority
{
    public async Task<StatementImportCommitResultDto> IngestAsync(
        StatementFetchIngestionCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var accountingScope = await intakeAuthority
            .ResolveAccountingScopeAsync(
                new StatementReconciliationIntakeScopeRequest(
                    command.TenantId,
                    command.CompanyId,
                    command.FundAccountId,
                    command.ExternalAccountId,
                    command.SourceInstitution,
                    command.PeriodStart,
                    command.PeriodEnd,
                    command.AccountingScope),
                ct)
            .ConfigureAwait(false);
        var execution = await workflow
            .StartAsync(
                new StatementReconciliationReportStartCommand(
                    new StatementImportCommitRequest(
                        command.Document,
                        command.ConnectorId,
                        command.SourceKind,
                        command.SourceInstitution,
                        command.FundAccountId,
                        command.ExternalAccountId,
                        command.PeriodStart,
                        command.PeriodEnd,
                        command.ToleranceProfileId,
                        command.ImportedBy)
                    {
                        AccountingScope = accountingScope
                    },
                    command.TenantId,
                    command.CompanyId),
                ct)
            .ConfigureAwait(false);

        if (execution.Workflow.Status == StatementReconciliationReportWorkflowStatusDto.Failed)
        {
            throw new InvalidOperationException(
                execution.Workflow.FailureReason
                ?? "Statement reconciliation report ingestion failed after retaining the scheduled document.");
        }

        if (execution.ImportResult is null
            || execution.Workflow.OperationsWorkflowId is null
            || execution.Workflow.AccountingScope is null)
        {
            throw new InvalidOperationException(
                "Statement reconciliation report ingestion did not retain its Operations workflow and exact accounting scope.");
        }

        return execution.ImportResult with
        {
            StatementReconciliationReportWorkflowId = execution.Workflow.WorkflowId,
            StatementReconciliationReportStatusRoute = execution.Workflow.StatusRoute,
            OperationsWorkflowId = execution.Workflow.OperationsWorkflowId,
            AccountingScope = execution.Workflow.AccountingScope
        };
    }
}
