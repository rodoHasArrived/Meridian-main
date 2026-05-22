using System.Collections.ObjectModel;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

internal sealed class ProviderHealthCollectionsSectionViewModel
{
    public ObservableCollection<ProviderStatusModel> StreamingProviders { get; } = new();
    public ObservableCollection<BackfillProviderModel> BackfillProviders { get; } = new();
    public ObservableCollection<ConnectionEventModel> ConnectionHistory { get; } = new();
    public ObservableCollection<ProviderManagementRowModel> ProviderManagementRows { get; } = new();
    public ObservableCollection<ProviderManagementSummaryCardModel> ProviderManagementSummaryCards { get; } = new();
    public ObservableCollection<WorkstationMetricModel> ProviderMetricTiles { get; } = new();
    public ObservableCollection<ActionEntry> Actions { get; } = new();
}
