using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.PortfolioRecords.Accounts;

namespace Meridian.Ui.Services.Services.Reconciliation;

public sealed class ReconciliationApiService(
    IStatementRunWorkflowService statementRunWorkflowService,
    IAccountQueryService? accounts = null,
    IFundProfileTenancyRegistry? tenancy = null)
    : Meridian.Ui.Shared.Services.ReconciliationApiService(statementRunWorkflowService, accounts, tenancy);
