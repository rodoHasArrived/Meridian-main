using System.Collections.ObjectModel;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

public sealed class ProviderHealthCollectionsSectionViewModel
{
    public ProviderHealthCollectionsSectionViewModel(
        ObservableCollection<ProviderStatusModel> streamingProviders,
        ObservableCollection<BackfillProviderModel> backfillProviders,
        ObservableCollection<ConnectionEventModel> connectionHistory,
        ObservableCollection<ProviderManagementRowModel> providerManagementRows,
        ObservableCollection<ProviderManagementSummaryCardModel> providerManagementSummaryCards,
        ObservableCollection<WorkstationMetricModel> providerMetricTiles,
        ObservableCollection<ActionEntry> actions)
    {
        StreamingProviders = streamingProviders;
        BackfillProviders = backfillProviders;
        ConnectionHistory = connectionHistory;
        ProviderManagementRows = providerManagementRows;
        ProviderManagementSummaryCards = providerManagementSummaryCards;
        ProviderMetricTiles = providerMetricTiles;
        Actions = actions;
    }

    public ObservableCollection<ProviderStatusModel> StreamingProviders { get; }
    public ObservableCollection<BackfillProviderModel> BackfillProviders { get; }
    public ObservableCollection<ConnectionEventModel> ConnectionHistory { get; }
    public ObservableCollection<ProviderManagementRowModel> ProviderManagementRows { get; }
    public ObservableCollection<ProviderManagementSummaryCardModel> ProviderManagementSummaryCards { get; }
    public ObservableCollection<WorkstationMetricModel> ProviderMetricTiles { get; }
    public ObservableCollection<ActionEntry> Actions { get; }
}
