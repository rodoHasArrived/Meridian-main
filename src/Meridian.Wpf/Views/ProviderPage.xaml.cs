using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for viewing and managing market data providers for real-time streaming and historical data.
/// Code-behind is thin lifecycle wiring only; all state and orchestration live in ProviderViewModel.
/// </summary>
public partial class ProviderPage : Page
{
    private readonly ProviderViewModel _viewModel;

    public ProviderPage(ProviderViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;

        ProviderSettingsList.ItemsSource = _viewModel.ProviderSettings;
        FallbackChainList.ItemsSource = _viewModel.FallbackChain;
        AuditLogList.ItemsSource = _viewModel.AuditLog;
        DryRunResultsList.ItemsSource = _viewModel.DryRunResults;

        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.StartAsync(CancellationToken.None);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Stop();
    }

    // ── Row-level event handlers ─────────────────────────────────────────────
    // These stay in code-behind because they need access to row DataContext
    // which is a ProviderSettingsViewModel instance, not the page DataContext.

    private async void ProviderToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: ProviderSettingsViewModel vm })
            return;
        await _viewModel.OnProviderToggleChangedAsync(vm);
    }

    private async void PriorityField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: ProviderSettingsViewModel vm } textBox)
            return;
        await _viewModel.OnPriorityLostFocusAsync(vm, textBox.Text);
    }

    private async void RateLimitField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: ProviderSettingsViewModel vm })
            return;
        await _viewModel.OnRateLimitLostFocusAsync(vm);
    }

    private async void ResetProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string providerId } || string.IsNullOrEmpty(providerId))
            return;
        await _viewModel.OnResetProviderAsync(providerId);
    }

    private void ConfigureProvider_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OnConfigureProvider();
    }
}

