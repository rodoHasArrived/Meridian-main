using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Contracts.Configuration;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for managing multiple data source configurations.
/// Code-behind is limited to:
///  – constructor DI and DataContext wiring
///  – ComboBox-tag helpers (Tag-based ComboBoxItems cannot be bound with SelectedValue)
///  – PasswordBox read (WPF security restriction prevents binding Password)
///  – SourceEnabled CheckBox toggle (row-level command relay)
/// All business logic lives in <see cref="DataSourcesViewModel"/>.
/// </summary>
public partial class DataSourcesPage : Page
{
    private readonly DataSourcesViewModel _viewModel;

    public DataSourcesPage(WpfServices.ConfigService configService)
    {
        InitializeComponent();
        _viewModel = new DataSourcesViewModel(configService);
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    // ── ComboBox tag helpers ─────────────────────────────────────────────
    // These stay in code-behind because the items are static XAML ComboBoxItems
    // with Tag attributes; SelectedValuePath cannot resolve Tag on ComboBoxItem.

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var provider = GetComboTag(ProviderCombo) ?? "IB";
        _viewModel.OnProviderSelected(provider);
        // Keep combo in sync when PopulateEditForm changes SelectedProvider
        if (_viewModel.SelectedProvider != provider)
            SelectComboByTag(ProviderCombo, _viewModel.SelectedProvider);
    }

    private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectedType = GetComboTag(TypeCombo) ?? "RealTime";
    }

    private void AlpacaFeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.AlpacaFeed = GetComboTag(AlpacaFeedCombo) ?? "iex";
    }

    private void AlpacaEnvironmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.AlpacaSandbox = GetComboTag(AlpacaEnvironmentCombo) == "true";
    }

    private void PolygonFeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.PolygonFeed = GetComboTag(PolygonFeedCombo) ?? "stocks";
    }

    // ── Save – reads PasswordBox before delegating ────────────────────────

    private async void SaveDataSource_Click(object sender, RoutedEventArgs e)
    {
        // PasswordBox.Password cannot be data-bound; read it here before Save.
        _viewModel.PolygonApiKey = PolygonApiKeyBox.Password;
        await _viewModel.SaveSourceCommand.ExecuteAsync(null);

        // Sync back PasswordBox after a successful save / edit-start
        if (!_viewModel.IsEditPanelVisible)
            PolygonApiKeyBox.Password = string.Empty;
    }

    // ── Edit / delete row actions ─────────────────────────────────────────
    // Buttons in the DataTemplate carry the source Id in their Tag property.

    private void EditDataSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string sourceId })
            _viewModel.EditSourceCommand.Execute(sourceId);
    }

    private async void DeleteDataSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string sourceId })
            await _viewModel.DeleteSourceCommand.ExecuteAsync(sourceId);
    }

    // ── Edit form initialization – syncs combo selections ─────────────────
    // Raised when the edit panel becomes visible with a pre-populated form.

    private void EditPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;

        SelectComboByTag(ProviderCombo, _viewModel.SelectedProvider);
        SelectComboByTag(TypeCombo, _viewModel.SelectedType);
        SelectComboByTag(AlpacaFeedCombo, _viewModel.AlpacaFeed);
        SelectComboByTag(AlpacaEnvironmentCombo, _viewModel.AlpacaSandbox ? "true" : "false");
        SelectComboByTag(PolygonFeedCombo, _viewModel.PolygonFeed);

        // Restore PasswordBox when editing an existing Polygon source
        if (_viewModel.SelectedProvider == "Polygon")
            PolygonApiKeyBox.Password = _viewModel.PolygonApiKey;
    }

    // ── Row-level Edit / Delete buttons ──────────────────────────────────
    // Buttons in DataTemplates carry the source ID in their Tag property.

    private void EditDataSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string id })
            _viewModel.EditSourceCommand.Execute(id);
    }

    private async void DeleteDataSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string id })
            await _viewModel.DeleteSourceCommand.ExecuteAsync(id);
    }

    // ── Source enabled toggle (row-level; DataTemplate cannot bind commands
    //    from a parent context reliably without x:Name trickery) ──────────

    private async void SourceEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: DataSourceConfigDto source })
            await _viewModel.ToggleSourceEnabledCommand.ExecuteAsync(source);
    }

    // ── Utilities ────────────────────────────────────────────────────────

    private static string? GetComboTag(ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }
}
