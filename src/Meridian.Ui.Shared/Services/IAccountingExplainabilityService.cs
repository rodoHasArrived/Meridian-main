using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public interface IAccountingExplainabilityService
{
    ValueTask<EquityChangeExplanationDto?> ExplainEquityChangeAsync(EquityChangeQuery query, CancellationToken ct = default);

    ValueTask<PnlReconciliationDto?> ReconcilePnlAsync(PnlReconciliationQuery query, CancellationToken ct = default);
}
