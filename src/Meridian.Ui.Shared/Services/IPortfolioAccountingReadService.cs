using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public interface IPortfolioAccountingReadService
{
    ValueTask<PortfolioOverviewDto?> GetPortfolioOverviewAsync(PortfolioOverviewQuery query, CancellationToken ct = default);

    ValueTask<LedgerDrillDownDto?> GetLedgerDrillDownAsync(LedgerDrillDownQuery query, CancellationToken ct = default);

    ValueTask<FinancingAnalysisDto?> GetFinancingAnalysisAsync(FinancingAnalysisQuery query, CancellationToken ct = default);
}
