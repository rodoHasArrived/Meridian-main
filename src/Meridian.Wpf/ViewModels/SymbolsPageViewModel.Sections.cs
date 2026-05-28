using System.Collections.ObjectModel;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

public sealed class SymbolsPageSectionViewModel : BindableBase
{
    private string _searchText = string.Empty;
    private string _selectedSubscriptionFilter = "All";
    private string _selectedExchangeFilter = "All";
    private string _visibleSymbolScopeText = "No configured symbols";
    private bool _hasActiveFilters;
    private bool _hasVisibleSymbols;
    private bool _isSymbolsEmptyStateVisible = true;
    private string _symbolsEmptyStateTitle = "No symbols configured yet";
    private string _symbolsEmptyStateDetail = "Add a symbol, load a watchlist, or apply a template to start collecting market data.";
    private SymbolViewModel? _selectedItem;

    public ObservableCollection<SymbolViewModel> Symbols { get; } = new();
    public ObservableCollection<SymbolViewModel> FilteredSymbols { get; } = new();
    public ObservableCollection<WatchlistInfo> Watchlists { get; } = new();

    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value ?? string.Empty); }
    public string SelectedSubscriptionFilter { get => _selectedSubscriptionFilter; set => SetProperty(ref _selectedSubscriptionFilter, string.IsNullOrWhiteSpace(value) ? "All" : value); }
    public string SelectedExchangeFilter { get => _selectedExchangeFilter; set => SetProperty(ref _selectedExchangeFilter, string.IsNullOrWhiteSpace(value) ? "All" : value); }
    public string VisibleSymbolScopeText { get => _visibleSymbolScopeText; set => SetProperty(ref _visibleSymbolScopeText, value); }
    public bool HasActiveFilters { get => _hasActiveFilters; set => SetProperty(ref _hasActiveFilters, value); }
    public bool HasVisibleSymbols { get => _hasVisibleSymbols; set => SetProperty(ref _hasVisibleSymbols, value); }
    public bool IsSymbolsEmptyStateVisible { get => _isSymbolsEmptyStateVisible; set => SetProperty(ref _isSymbolsEmptyStateVisible, value); }
    public string SymbolsEmptyStateTitle { get => _symbolsEmptyStateTitle; set => SetProperty(ref _symbolsEmptyStateTitle, value); }
    public string SymbolsEmptyStateDetail { get => _symbolsEmptyStateDetail; set => SetProperty(ref _symbolsEmptyStateDetail, value); }
    public SymbolViewModel? SelectedItem { get => _selectedItem; set => SetProperty(ref _selectedItem, value); }
}
