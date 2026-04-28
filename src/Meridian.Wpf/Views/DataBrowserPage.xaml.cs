using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

public partial class DataBrowserPage : Page
{
    private const string PageTag = "DataBrowser";
    private readonly DataBrowserViewModel _viewModel = new();

    public DataBrowserPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        SavePageFilterState();
    }

    private void SavePageFilterState()
    {
        var ws = WpfServices.WorkspaceService.Instance;
        ws.UpdatePageFilterState(PageTag, "SymbolFilter", _viewModel.SymbolFilter);
        ws.UpdatePageFilterState(PageTag, "SelectedDataType", _viewModel.SelectedDataType);
        ws.UpdatePageFilterState(PageTag, "SelectedVenue", _viewModel.SelectedVenue);
        ws.UpdatePageFilterState(PageTag, "SortField", _viewModel.SortField);
        ws.UpdatePageFilterState(PageTag, "PageSize", _viewModel.PageSize.ToString());
        ws.UpdatePageFilterState(PageTag, "SelectedTimePeriodKey", _viewModel.SelectedTimePeriodKey);
        ws.UpdatePageFilterState(PageTag, "FromDate", _viewModel.FromDate?.ToString("yyyy-MM-dd"));
        ws.UpdatePageFilterState(PageTag, "ToDate", _viewModel.ToDate?.ToString("yyyy-MM-dd"));
    }

    private void RestorePageFilterState()
    {
        var ws = WpfServices.WorkspaceService.Instance;

        var symbolFilter = ws.GetPageFilterState(PageTag, "SymbolFilter");
        if (symbolFilter is not null)
            _viewModel.SymbolFilter = symbolFilter;

        var dataType = ws.GetPageFilterState(PageTag, "SelectedDataType");
        if (dataType is not null)
            _viewModel.SelectedDataType = dataType;

        var venue = ws.GetPageFilterState(PageTag, "SelectedVenue");
        if (venue is not null)
            _viewModel.SelectedVenue = venue;

        var sortField = ws.GetPageFilterState(PageTag, "SortField");
        if (sortField is not null)
            _viewModel.SortField = sortField;

        if (int.TryParse(ws.GetPageFilterState(PageTag, "PageSize"), out var pageSize) && pageSize > 0)
            _viewModel.PageSize = pageSize;

        var selectedTimePeriodKey = ws.GetPageFilterState(PageTag, "SelectedTimePeriodKey");
        if (!string.IsNullOrWhiteSpace(selectedTimePeriodKey) &&
            !string.Equals(selectedTimePeriodKey, DataBrowserTimePeriodOption.CustomKey, StringComparison.Ordinal))
        {
            _viewModel.SelectedTimePeriodKey = selectedTimePeriodKey;
            return;
        }

        if (DateTime.TryParse(ws.GetPageFilterState(PageTag, "FromDate"), out var fromDate))
            _viewModel.FromDate = fromDate;

        if (DateTime.TryParse(ws.GetPageFilterState(PageTag, "ToDate"), out var toDate))
            _viewModel.ToDate = toDate;

        if (string.Equals(selectedTimePeriodKey, DataBrowserTimePeriodOption.CustomKey, StringComparison.Ordinal))
            _viewModel.SelectedTimePeriodKey = DataBrowserTimePeriodOption.CustomKey;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        RestorePageFilterState();
        _viewModel.RefreshResults();
    }

    private void ApplyFilters_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RefreshResults();
    }

    private void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.GoToPreviousPage();
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.GoToNextPage();
    }

    private void SortChanged_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortCombo.SelectedItem is ComboBoxItem item && item.Tag is string sortKey)
        {
            _viewModel.SortField = sortKey;
            _viewModel.RefreshResults();
        }
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ExportToCsv();
    }

    private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is DataBrowserRecord record)
        {
            DetailPanel.Visibility = Visibility.Visible;
            DetailSymbol.Text = record.Symbol;
            DetailVenue.Text = record.Venue;
            DetailDataType.Text = record.DataType;
            DetailTimestamp.Text = record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
            DetailPrice.Text = record.Price.ToString("N2");
            DetailSize.Text = record.Size.ToString("N0");
        }
        else
        {
            DetailPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void CopyJson_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not DataBrowserRecord record)
            return;

        var json = JsonSerializer.Serialize(new
        {
            record.Symbol,
            record.DataType,
            record.Venue,
            Timestamp = record.Timestamp.ToString("O"),
            record.Price,
            record.Size
        }, new JsonSerializerOptions { WriteIndented = true });

        Clipboard.SetText(json);
    }
}
