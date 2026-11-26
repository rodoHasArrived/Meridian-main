using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Services.Comparisons;
using Meridian.Wpf.ViewModels.Comparisons;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// The portfolio cash ladder today against the end of last month (UX Phase E parity). The desktop
/// ladder is per run; the portfolio ladder needs host-composed holdings and cash providers, so this
/// reads the shared comparison over the API, on request, as the browser's "Vs end of last month" does.
/// </summary>
public sealed partial class CashFlowViewModel
{
    private const string PortfolioLadderIdleMessage = "Compare the portfolio cash ladder today with the end of last month.";
    private readonly IPeriodComparisonApiClient? _periodComparisonClient;

    /// <summary>The portfolio ladder comparison, loaded by <see cref="LoadPortfolioLadderComparisonCommand"/>.</summary>
    public PeriodComparisonSectionViewModel PortfolioLadderComparison { get; } = new(
        "Portfolio ladder vs end of last month",
        "Today's portfolio cash ladder against the ladder rebuilt from the ledger's cash at the end of last month. Buckets are compared by position.",
        PortfolioLadderIdleMessage,
        countKeys: PeriodComparisonProjection.CashLadderCountKeys);

    public IAsyncRelayCommand LoadPortfolioLadderComparisonCommand { get; }

    private Task LoadPortfolioLadderComparisonAsync()
    {
        var client = _periodComparisonClient;
        if (client is null)
        {
            PortfolioLadderComparison.Reset("This comparison is not available in this host.");
            return Task.CompletedTask;
        }

        return PortfolioLadderComparison.LoadAsync(async token =>
        {
            var response = await client.GetCashLadderComparisonAsync(baselineAsOf: null, token);
            return response.Success && response.Data is { } comparison
                ? comparison
                : throw new InvalidOperationException(PeriodComparisonProjection.DescribeFailure(response));
        });
    }
}
