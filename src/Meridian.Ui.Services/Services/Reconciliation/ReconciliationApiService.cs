using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Ui.Services.Services.Reconciliation;

public sealed class ReconciliationApiService(IStatementRunWorkflowService statementRunWorkflowService)
    : Meridian.Ui.Shared.Services.ReconciliationApiService(statementRunWorkflowService);
