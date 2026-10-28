using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.PortfolioRecords.Accounts;

namespace Meridian.Ui.Services.Services.Reconciliation;

/// <summary>
/// Preserves the UI-services reconciliation API entrypoint over the shared statement-run workflow adapter.
/// </summary>
public sealed class ReconciliationApiService(
    IStatementRunWorkflowService statementRunWorkflowService,
    IAccountQueryService? accounts = null,
    IFundProfileTenancyRegistry? tenancy = null)
    : Meridian.Ui.Shared.Services.ReconciliationApiService(statementRunWorkflowService, accounts, tenancy);
