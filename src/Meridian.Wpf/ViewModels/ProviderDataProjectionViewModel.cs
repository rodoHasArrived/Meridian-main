using System.Collections.ObjectModel;
using Meridian.Ui.Shared.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>Desktop presentation shell over the provider-neutral shared projection.</summary>
public sealed class ProviderDataProjectionViewModel : BindableBase
{
    private readonly ProviderDataReadModelService _providerData;
    private ProviderDataProjectionSnapshot? _projection;

    public ProviderDataProjectionViewModel(ProviderDataReadModelService providerData)
    {
        _providerData = providerData ?? throw new ArgumentNullException(nameof(providerData));
    }

    public ObservableCollection<ProviderNewsReadModel> News { get; } = [];
    public ObservableCollection<ProviderScannerReadModel> ScannerResults { get; } = [];
    public ObservableCollection<ProviderPnlReadModel> PnlStreams { get; } = [];
    public ObservableCollection<ProviderCalendarReadModel> Calendars { get; } = [];
    public ObservableCollection<ProviderMarketRuleReadModel> MarketRules { get; } = [];
    public ObservableCollection<ProviderInstrumentDiscoveryReadModel> Instruments { get; } = [];
    public ProviderDataProjectionSnapshot? Projection { get => _projection; private set => SetProperty(ref _projection, value); }

    public void Refresh(string tenantId, string companyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        Projection = _providerData.GetProjection(tenantId.Trim(), companyId.Trim());
        Replace(News, Projection.News);
        Replace(ScannerResults, Projection.ScannerResults);
        Replace(PnlStreams, Projection.PnlStreams);
        Replace(Calendars, Projection.Calendars);
        Replace(MarketRules, Projection.MarketRules);
        Replace(Instruments, Projection.Instruments);
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }
}
