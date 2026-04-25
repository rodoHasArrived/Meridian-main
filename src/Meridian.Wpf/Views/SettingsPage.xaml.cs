using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services.Contracts;
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

        // Wire hotkeys panel
        GlobalHotkeysEnabledCheckBox.IsChecked = WpfServices.GlobalHotkeyService.Instance.IsEnabled;
        GlobalHotkeysList.ItemsSource = WpfServices.GlobalHotkeyService.Instance.Definitions;
    }

    // Storage preview combo change handler — keeps legacy XAML event names intact.
    private void PreviewSettings_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (StoragePreviewText == null)
            return;
        var naming = GetSelectedTag(PreviewNamingCombo) ?? "BySymbol";
        var compression = GetSelectedTag(PreviewCompressionCombo) ?? "gzip";
        _viewModel.RefreshStoragePreview(naming, compression);
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedTheme = GetSelectedTag(ThemeCombo) switch
        {
            "Dark" => AppTheme.Dark,
            "Light" => AppTheme.Light,
            _ => WpfServices.ThemeService.Instance.GetSystemTheme()
        };

        WpfServices.ThemeService.Instance.SetTheme(selectedTheme);
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

    private static string? GetSelectedTag(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
}
