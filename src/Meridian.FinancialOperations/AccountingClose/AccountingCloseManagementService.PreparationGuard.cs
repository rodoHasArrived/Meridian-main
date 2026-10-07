using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.AccountingClose;

public sealed partial class AccountingCloseManagementService
{
    private static void EnsurePreparedConfigurationDoesNotOverwrite(UpsertClosePeriodPlanConfigurationRequestDto request,
        ClosePeriodPlanConfigurationDto? retainedConfiguration)
    {
        // Called under the configuration write gate. A normal setup may have been retained after
        // the preparation service inspected the fresh workflow; that setup must remain authoritative.
        if (request.Preparation is not null && retainedConfiguration is not null)
            throw new ClosePreparationRecoveryRequiredException(
                "The target acquired a close configuration during preparation. Preserve the original request and review the retained configuration before retrying.");
    }
}
