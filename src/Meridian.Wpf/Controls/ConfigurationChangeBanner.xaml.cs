using Meridian.Contracts.Configuration;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Controls;

/// <summary>Owns only view lifetime and refresh wiring; status projection remains in the view model.</summary>
public partial class ConfigurationChangeBanner : UserControl
{
    private readonly ConfigurationChangeStatusViewModel _model = new(ct =>
        WpfRemoteWorkstationClient.Instance.GetAsync<ConfigurationChangeStatus>("/api/config/changes", ct));
    private CancellationTokenSource? _lifetime;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(15) };

    public ConfigurationChangeBanner()
    {
        InitializeComponent();
        BannerRoot.DataContext = _model;
        _refreshTimer.Tick += OnRefreshTick;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_lifetime is not null) return;
        _lifetime = new CancellationTokenSource();
        ConfigService.Instance.ConfigurationSaved += OnConfigurationSaved;
        _refreshTimer.Start();
        await RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ConfigService.Instance.ConfigurationSaved -= OnConfigurationSaved;
        _refreshTimer.Stop();
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _model.CancelPending();
    }

    private async void OnRefreshTick(object? sender, EventArgs e)
    {
        if (!_model.IsBusy) await RefreshAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void OnConfigurationSaved(object? sender, EventArgs e)
    {
        if (_lifetime is null) return;
        _ = Dispatcher.InvokeAsync(async () => await RefreshAsync());
    }

    private Task RefreshAsync() => _lifetime is null
        ? Task.CompletedTask
        : _model.RefreshAsync(ConfigService.Instance.HasUnverifiedLocalChanges, _lifetime.Token);
}
