using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Settings page — thin code-behind.
/// All state, service calls, and commands live in <see cref="SettingsViewModel"/>.
/// </summary>
public partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Initialize();

        // Wire storage preview combos: delegate to ViewModel on selection change.
        PreviewNamingCombo.SelectionChanged += OnPreviewSettingsChanged;
        PreviewCompressionCombo.SelectionChanged += OnPreviewSettingsChanged;

        // Wire hotkeys panel
        GlobalHotkeysEnabledCheckBox.IsChecked = WpfServices.GlobalHotkeyService.Instance.IsEnabled;
        GlobalHotkeysList.ItemsSource = WpfServices.GlobalHotkeyService.Instance.Definitions;

        // React to reset request by restoring default UI control values.
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.ResetRequested))
                ApplyDefaultControlValues();
        };
    }

    // Storage preview combo change handler — retrieves combo tags and delegates to ViewModel.
    private void OnPreviewSettingsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StoragePreviewText == null) return;
        var naming = GetSelectedTag(PreviewNamingCombo) ?? "BySymbol";
        var compression = GetSelectedTag(PreviewCompressionCombo) ?? "gzip";
        _viewModel.RefreshStoragePreview(naming, compression);
    }

    // Credential test/remove delegates — tag carries resource or name string.
    private void TestCredential_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string resource })
            _viewModel.TestCredentialCommand.Execute(resource);
    }

    private void RemoveCredential_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
            _viewModel.RemoveCredentialCommand.Execute(name);
    }

    private void ProfileCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string profileId })
            _viewModel.SelectProfileCommand.Execute(profileId);
    }

    private void GlobalHotkeysEnabled_Click(object sender, RoutedEventArgs e)
    {
        WpfServices.GlobalHotkeyService.Instance.IsEnabled = GlobalHotkeysEnabledCheckBox.IsChecked ?? true;
    }

    private void NotificationsEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (NotificationSettingsPanel != null)
            NotificationSettingsPanel.Opacity = NotificationsEnabledToggle.IsChecked.GetValueOrDefault() ? 1.0 : 0.5;
    }

    // Restore WPF control default values after a settings reset (controls not data-bound).
    private void ApplyDefaultControlValues()
    {
        ThemeCombo.SelectedIndex = 0;
        AccentColorCombo.SelectedIndex = 0;
        CompactModeToggle.IsChecked = false;
        NotificationsEnabledToggle.IsChecked = true;
        MaxConcurrentDownloadsBox.Text = "4";
        WriteBufferSizeBox.Text = "64";
        EnableMetricsToggle.IsChecked = true;
        EnableDebugLoggingToggle.IsChecked = false;
        ApiBaseUrlBox.Text = "http://localhost:8080";
        StatusRefreshIntervalBox.Text = "2";
    }

    private static string? GetSelectedTag(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
}
