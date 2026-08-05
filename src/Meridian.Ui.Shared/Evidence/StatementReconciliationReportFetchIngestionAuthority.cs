using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Ui.Shared.Services;

namespace Meridian.Ui.Shared.Evidence;

/// <summary>
/// Adapts scheduled connector fetches into the existing statement reconciliation report workflow.
/// The scheduler uses this authority to re-resolve persisted account ownership before provider
/// access, and ingestion resolves it again before treating the fetched document as authoritative.
/// Success is reported only after Operations Continuity and canonical casework publication have
/// been retained.
/// </summary>
public sealed class StatementReconciliationReportFetchIngestionAuthority(
    StatementReconciliationReportWorkflowService workflow,
    IStatementReconciliationIntakeAuthority intakeAuthority) : IStatementFetchIngestionAuthority
{
    public Task<StatementAccountingScope> AuthorizeAsync(
        StatementFetchAuthorizationCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return intakeAuthority.ResolveAccountingScopeAsync(
            new StatementReconciliationIntakeScopeRequest(
                command.TenantId,
                command.CompanyId,
                command.FundAccountId,
                command.ExternalAccountId,
                command.SourceInstitution,
                command.PeriodStart,
                command.PeriodEnd,
                command.AccountingScope),
            ct);
    }

    public async Task<StatementImportCommitResultDto> IngestAsync(
        StatementFetchIngestionCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var accountingScope = await AuthorizeAsync(command.ToAuthorizationCommand(), ct)
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
