using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Symbol subscription management page — thin code-behind.
/// All state, business logic, watchlist event handling, and backend sync live in
/// <see cref="SymbolsPageViewModel"/>.
/// </summary>
public partial class SymbolsPage : Page
{
    private const string PageTag = "Symbols";

    private readonly SymbolsPageViewModel _vm;
    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.WorkspaceService _workspaceService;
    private SymbolViewModel? _selectedSymbol;
    private bool _isEditMode;

    public SymbolsPage(
        WpfServices.ConfigService configService,
        WpfServices.WatchlistService watchlistService,
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService,
        WpfServices.NavigationService navigationService)
    {
        _navigationService = navigationService;
        _workspaceService = WpfServices.WorkspaceService.Instance;
        _vm = new SymbolsPageViewModel(
            configService,
            watchlistService,
            loggingService,
            notificationService,
            navigationService,
            SymbolManagementService.Instance,
            CommandPaletteService.Instance);

        InitializeComponent();
        DataContext = _vm;

        SymbolsListView.ItemsSource = _vm.FilteredSymbols;
        WatchlistsView.ItemsSource = _vm.Watchlists;

        Unloaded += OnPageUnloaded;
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.Stop();
        SavePageFilterState();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await _vm.StartAsync();
        RestorePageFilterState();
        CallApplyFilters();
    }

    private void CallApplyFilters()
    {
        if (SymbolSearchBox is null || FilterCombo is null || ExchangeFilterCombo is null)
            return;

        _vm.ApplyFilters(
            SymbolSearchBox.Text?.ToUpper() ?? string.Empty,
            GetComboSelectedTag(FilterCombo) ?? "All",
            GetComboSelectedTag(ExchangeFilterCombo) ?? "All");
    }

    private void SymbolSearch_TextChanged(object sender, TextChangedEventArgs e) => CallApplyFilters();

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => CallApplyFilters();

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        SymbolSearchBox.Text = "";
        SelectComboItemByTag(FilterCombo, "All");
        SelectComboItemByTag(ExchangeFilterCombo, "All");
        CallApplyFilters();
    }

    private void SecurityType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SecurityTypeCombo is null || OptionsExpander is null)
            return;

        if (SecurityTypeCombo?.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag?.ToString() ?? "STK";
            var isOption = tag is "OPT" or "IND_OPT" or "FOP";
            if (OptionsExpander != null)
                OptionsExpander.Visibility = isOption ? Visibility.Visible : Visibility.Collapsed;
            if (isOption && tag == "IND_OPT" && OptionStyleCombo != null)
                SelectComboItemByTag(OptionStyleCombo, "European");
        }
    }

    private void SelectAll_Changed(object sender, RoutedEventArgs e)
    {
        var isChecked = SelectAllCheck.IsChecked ?? false;
        foreach (var symbol in _vm.FilteredSymbols)
            symbol.IsSelected = isChecked;
        _vm.UpdateSelectionCount();
    }

    private void SymbolCheckbox_Changed(object sender, RoutedEventArgs e) =>
        _vm.UpdateSelectionCount();

    private void BulkEnableTrades_Click(object sender, RoutedEventArgs e)
    {
        _vm.BulkEnableTrades();
        CallApplyFilters();
    }

    private void BulkEnableDepth_Click(object sender, RoutedEventArgs e)
    {
        _vm.BulkEnableDepth();
        CallApplyFilters();
    }

    private async void BulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = _vm.FilteredSymbols.Where(s => s.IsSelected).ToList();
        var result = MessageBox.Show(
            $"Are you sure you want to delete {selected.Count} symbols?",
            "Delete Symbols", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        await _vm.BulkDeleteSymbolsAsync(selected);
        CallApplyFilters();
    }

    private void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CSV Files|*.csv|Text Files|*.txt|All Files|*.*",
            Title = "Import Symbols"
        };
        dialog.ShowDialog();
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV Files|*.csv",
            Title = "Export Symbols",
            FileName = $"symbols_{System.DateTime.Now:yyyyMMdd}"
        };
        dialog.ShowDialog();
    }

    private void SymbolsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SymbolsListView.SelectedItem is SymbolViewModel symbol)
        {
            _selectedSymbol = symbol;
            _isEditMode = true;

            // Update the ViewModel's selected item for action strip
            _vm.SelectedItem = symbol;

            SymbolBox.Text = symbol.Symbol;
            SubscribeTradesToggle.IsChecked = symbol.SubscribeTrades;
            SubscribeDepthToggle.IsChecked = symbol.SubscribeDepth;
            DepthLevelsBox.Text = symbol.DepthLevels.ToString();
            ExchangeBox.Text = symbol.Exchange ?? "SMART";
            PrimaryExchangeBox.Text = string.Empty;
            LocalSymbolBox.Text = symbol.LocalSymbol ?? string.Empty;

            SelectComboItemByTag(SecurityTypeCombo, symbol.SecurityType ?? "STK");
            var isOption = symbol.SecurityType is "OPT" or "IND_OPT" or "FOP";
            OptionsExpander.Visibility = isOption ? Visibility.Visible : Visibility.Collapsed;

            if (isOption)
            {
                StrikeBox.Text = symbol.Strike?.ToString() ?? string.Empty;
                SelectComboItemByTag(RightCombo, symbol.Right ?? "Call");
                SelectComboItemByTag(OptionStyleCombo, symbol.OptionStyle ?? "American");
                MultiplierBox.Text = (symbol.Multiplier ?? 100).ToString();
                if (System.DateTime.TryParse(symbol.LastTradeDateOrContractMonth, out var expDate))
                    ExpirationPicker.SelectedDate = expDate;
            }

            FormTitle.Text = "Edit Symbol";
            SaveSymbolButton.Content = "Update Symbol";
            DeleteSymbolButton.Visibility = Visibility.Visible;
        }
    }

    private async void SaveSymbol_Click(object sender, RoutedEventArgs e)
    {
        var symbolName = SymbolBox.Text?.Trim().ToUpper();
        if (string.IsNullOrEmpty(symbolName))
            return;

        var securityType = GetComboSelectedTag(SecurityTypeCombo) ?? "STK";
        var isOption = securityType is "OPT" or "IND_OPT" or "FOP";

        decimal? strike = null;
        string? right = null;
        string? lastTradeDateOrContractMonth = null;
        string? optionStyle = null;
        int? multiplier = null;

        if (isOption)
        {
            if (decimal.TryParse(StrikeBox.Text, out var s) && s > 0)
                strike = s;
            right = GetComboSelectedTag(RightCombo) ?? "Call";
            if (ExpirationPicker.SelectedDate is System.DateTime expDate)
                lastTradeDateOrContractMonth = expDate.ToString("yyyyMMdd");
            optionStyle = GetComboSelectedTag(OptionStyleCombo) ?? "American";
            multiplier = int.TryParse(MultiplierBox.Text, out var m) && m > 0 ? m : 100;
        }

        var error = await _vm.SaveSymbolAsync(
            symbolName,
            SubscribeTradesToggle.IsChecked ?? false,
            SubscribeDepthToggle.IsChecked ?? false,
            int.TryParse(DepthLevelsBox.Text, out var levels) ? levels : 10,
            ExchangeBox.Text ?? "SMART",
            LocalSymbolBox.Text,
            securityType,
            strike, right, lastTradeDateOrContractMonth, optionStyle, multiplier,
            _isEditMode ? _selectedSymbol : null);

        if (error == null)
        {
            ClearForm();
            CallApplyFilters();
        }
    }

    private async void DeleteSymbol_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSymbol == null)
            return;
        var result = MessageBox.Show(
            $"Are you sure you want to delete {_selectedSymbol.Symbol}?",
            "Delete Symbol", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        await _vm.DeleteSymbolAsync(_selectedSymbol);
        ClearForm();
        CallApplyFilters();
    }

    private void ClearForm_Click(object sender, RoutedEventArgs e) => ClearForm();

    private void ClearForm()
    {
        _selectedSymbol = null;
        _isEditMode = false;
        _vm.SelectedItem = null;
        _vm.SelectedSymbolTicker = string.Empty; // Clear the Security Master bridge state
        SymbolBox.Text = string.Empty;
        SubscribeTradesToggle.IsChecked = true;
        SubscribeDepthToggle.IsChecked = false;
        DepthLevelsBox.Text = "10";
        ExchangeBox.Text = "SMART";
        PrimaryExchangeBox.Text = string.Empty;
        LocalSymbolBox.Text = string.Empty;
        SelectComboItemByTag(SecurityTypeCombo, "STK");
        OptionsExpander.Visibility = Visibility.Collapsed;
        OptionsExpander.IsExpanded = false;
        StrikeBox.Text = string.Empty;
        SelectComboItemByTag(RightCombo, "Call");
        ExpirationPicker.SelectedDate = null;
        SelectComboItemByTag(OptionStyleCombo, "American");
        MultiplierBox.Text = "100";
        FormTitle.Text = "Add Symbol";
        SaveSymbolButton.Content = "Add Symbol";
        DeleteSymbolButton.Visibility = Visibility.Collapsed;
        SymbolsListView.SelectedItem = null;
    }

    private void AddTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string templateName)
        {
            _vm.AddTemplate(templateName);
            CallApplyFilters();
        }
    }

    private async void LoadWatchlist_Click(object sender, RoutedEventArgs e)
    {
        var watchlistId = (sender is Button b && b.Tag is string id) ? id : null;
        await _vm.LoadWatchlistSymbolsAsync(watchlistId);
        CallApplyFilters();
    }

    private async void SaveWatchlist_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Symbols.Count == 0)
            return;
        var dialog = new SaveWatchlistDialog(_vm.Watchlists.Select(w => w.Name).ToList());
        if (dialog.ShowDialog() != true)
            return;
        await _vm.SaveWatchlistAsync(dialog.WatchlistName, dialog.SaveAsNew, dialog.SelectedWatchlistId);
    }

    private void ManageWatchlists_Click(object sender, RoutedEventArgs e) =>
        _navigationService.NavigateTo(typeof(WatchlistPage));

    private async void RefreshList_Click(object sender, RoutedEventArgs e)
    {
        await _vm.LoadSymbolsFromConfigAsync();
        LastRefreshText.Text = "Last refreshed: just now";
        CallApplyFilters();
    }

    private void SavePageFilterState()
    {
        if (FilterCombo is null || ExchangeFilterCombo is null)
            return;

        _workspaceService.UpdatePageFilterState(PageTag, "SearchText", SymbolSearchBox.Text);
        _workspaceService.UpdatePageFilterState(PageTag, "FilterCombo", GetComboSelectedTag(FilterCombo) ?? "All");
        _workspaceService.UpdatePageFilterState(PageTag, "ExchangeFilter", GetComboSelectedTag(ExchangeFilterCombo) ?? "All");
    }

    private void RestorePageFilterState()
    {
        if (FilterCombo is null || ExchangeFilterCombo is null)
            return;

        var searchText = _workspaceService.GetPageFilterState(PageTag, "SearchText");
        if (searchText is not null)
            SymbolSearchBox.Text = searchText;
        var filter = _workspaceService.GetPageFilterState(PageTag, "FilterCombo");
        if (filter is not null)
            SelectComboItemByTag(FilterCombo, filter);
        var exchange = _workspaceService.GetPageFilterState(PageTag, "ExchangeFilter");
        if (exchange is not null)
            SelectComboItemByTag(ExchangeFilterCombo, exchange);
    }

    private static void SelectComboItemByTag(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items)
            if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == tag)
            { combo.SelectedItem = item; return; }
    }

    private static string? GetComboSelectedTag(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
}
