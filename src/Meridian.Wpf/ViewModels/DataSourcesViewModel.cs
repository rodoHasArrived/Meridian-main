using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Configuration;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Data Sources configuration page.
/// Owns all state and commands so that the code-behind is thinned to
/// lifecycle wiring and PasswordBox/ComboBox-tag helpers only.
/// </summary>
public sealed class DataSourcesViewModel : BindableBase
{
    private readonly WpfServices.ConfigService _configService;
    private bool _isLoading;

    // ── Failover ────────────────────────────────────────────────────────
    private bool _failoverEnabled;
    private string _failoverTimeoutText = "30";
    private string _failoverTimeoutError = string.Empty;
    private bool _isFailoverTimeoutErrorVisible;

    // ── Source list ─────────────────────────────────────────────────────
    private string _sourceCountText = "(0)";
    private bool _isNoSourcesVisible;

    // ── Edit panel ──────────────────────────────────────────────────────
    private bool _isEditPanelVisible;
    private string _editPanelTitle = "Add Data Source";
    private bool _isSaving;
    private bool _canSaveSource;
    private string _sourceSetupReadinessTitle = "Data source setup incomplete";
    private string _sourceSetupReadinessDetail = "Name the source and choose a valid priority before saving.";
    private string _sourceSetupScopeText = "Interactive Brokers - Real-time - Global symbol list";

    // ── Form fields ─────────────────────────────────────────────────────
    private string _sourceNameText = string.Empty;
    private string _sourceNameError = string.Empty;
    private bool _isSourceNameErrorVisible;
    private string _priorityText = "100";
    private string _priorityError = string.Empty;
    private bool _isPriorityErrorVisible;
    private string _descriptionText = string.Empty;
    private string _symbolsText = string.Empty;

    // ── Provider panels ─────────────────────────────────────────────────
    private bool _isIBPanelVisible = true;
    private bool _isAlpacaPanelVisible;
    private bool _isPolygonPanelVisible;

    // ── IB settings ─────────────────────────────────────────────────────
    private string _ibHost = "127.0.0.1";
    private string _ibPort = "7497";
    private string _ibClientId = "1";
    private bool _ibPaperTrading = true;
    private bool _ibSubscribeDepth = true;
    private bool _ibTickByTick = true;

    // ── Alpaca settings ─────────────────────────────────────────────────
    private bool _alpacaSandbox;
    private bool _alpacaSubscribeQuotes;

    // ── Polygon settings ─────────────────────────────────────────────────
    private bool _polygonDelayed;
    private bool _polygonSubscribeTrades = true;
    private bool _polygonSubscribeQuotes;
    private bool _polygonSubscribeAggregates;

    // ── Status message ───────────────────────────────────────────────────
    private string _statusMessage = string.Empty;
    private bool _isStatusError;
    private bool _isStatusVisible;

    // ── Default sources ──────────────────────────────────────────────────
    private DataSourceConfigDto? _selectedDefaultRealTimeSource;
    private DataSourceConfigDto? _selectedDefaultHistoricalSource;

    // ── Internal form state
    private string? _editingSourceId;
    private string _selectedProvider = "IB";
    private string _selectedType = "RealTime";
    private string _alpacaFeed = "iex";
    private string _polygonFeed = "stocks";

    /// <summary>Polygon API key read from the PasswordBox by code-behind before Save.</summary>
    internal string PolygonApiKey { get; set; } = string.Empty;

    public DataSourcesViewModel(WpfServices.ConfigService configService)
    {
        _configService = configService;

        // Load
        LoadCommand = new AsyncRelayCommand(LoadAsync);

        // Source list
        AddSourceCommand = new RelayCommand(BeginAddSource);
        EditSourceCommand = new RelayCommand<string>(BeginEditSource);
        DeleteSourceCommand = new AsyncRelayCommand<string>(DeleteSourceAsync);
        ToggleSourceEnabledCommand = new AsyncRelayCommand<DataSourceConfigDto>(ToggleSourceEnabledAsync);

        // Edit form
        SaveSourceCommand = new AsyncRelayCommand(SaveSourceAsync, () => CanSaveSource);
        CancelEditCommand = new RelayCommand(CancelEdit);
    }

    // ── Collections ──────────────────────────────────────────────────────

    public ObservableCollection<DataSourceConfigDto> DataSources { get; } = new();
    public ObservableCollection<DataSourceConfigDto> RealTimeSources { get; } = new();
    public ObservableCollection<DataSourceConfigDto> HistoricalSources { get; } = new();

    // ── Commands ─────────────────────────────────────────────────────────

    public IAsyncRelayCommand LoadCommand { get; }
    public IRelayCommand AddSourceCommand { get; }
    public IRelayCommand<string> EditSourceCommand { get; }
    public IAsyncRelayCommand<string> DeleteSourceCommand { get; }
    public IAsyncRelayCommand<DataSourceConfigDto> ToggleSourceEnabledCommand { get; }
    public IAsyncRelayCommand SaveSourceCommand { get; }
    public IRelayCommand CancelEditCommand { get; }

    // ── Failover properties ───────────────────────────────────────────────

    public bool FailoverEnabled
    {
        get => _failoverEnabled;
        set
        {
            if (SetProperty(ref _failoverEnabled, value) && !_isLoading)
                SaveFailoverSettingsFireAndForget();
        }
    }

    public string FailoverTimeoutText
    {
        get => _failoverTimeoutText;
        set
        {
            if (SetProperty(ref _failoverTimeoutText, value) && !_isLoading)
                SaveFailoverSettingsFireAndForget();
        }
    }

    public string FailoverTimeoutError
    {
        get => _failoverTimeoutError;
        private set => SetProperty(ref _failoverTimeoutError, value);
    }

    public bool IsFailoverTimeoutErrorVisible
    {
        get => _isFailoverTimeoutErrorVisible;
        private set => SetProperty(ref _isFailoverTimeoutErrorVisible, value);
    }

    // ── Source list properties ────────────────────────────────────────────

    public string SourceCountText
    {
        get => _sourceCountText;
        private set => SetProperty(ref _sourceCountText, value);
    }

    public bool IsNoSourcesVisible
    {
        get => _isNoSourcesVisible;
        private set => SetProperty(ref _isNoSourcesVisible, value);
    }

    // ── Edit panel properties ─────────────────────────────────────────────

    public bool IsEditPanelVisible
    {
        get => _isEditPanelVisible;
        private set
        {
            if (SetProperty(ref _isEditPanelVisible, value))
                RefreshEditReadiness();
        }
    }

    public string EditPanelTitle
    {
        get => _editPanelTitle;
        private set => SetProperty(ref _editPanelTitle, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
                RefreshEditReadiness();
        }
    }

    public bool CanSaveSource
    {
        get => _canSaveSource;
        private set
        {
            if (SetProperty(ref _canSaveSource, value))
                SaveSourceCommand.NotifyCanExecuteChanged();
        }
    }

    public string SourceSetupReadinessTitle
    {
        get => _sourceSetupReadinessTitle;
        private set => SetProperty(ref _sourceSetupReadinessTitle, value);
    }

    public string SourceSetupReadinessDetail
    {
        get => _sourceSetupReadinessDetail;
        private set => SetProperty(ref _sourceSetupReadinessDetail, value);
    }

    public string SourceSetupScopeText
    {
        get => _sourceSetupScopeText;
        private set => SetProperty(ref _sourceSetupScopeText, value);
    }

    // ── Form field properties ─────────────────────────────────────────────

    public string SourceNameText
    {
        get => _sourceNameText;
        set
        {
            if (SetProperty(ref _sourceNameText, value))
                RefreshEditReadiness();
        }
    }

    public string SourceNameError
    {
        get => _sourceNameError;
        private set => SetProperty(ref _sourceNameError, value);
    }

    public bool IsSourceNameErrorVisible
    {
        get => _isSourceNameErrorVisible;
        private set => SetProperty(ref _isSourceNameErrorVisible, value);
    }

    public string PriorityText
    {
        get => _priorityText;
        set
        {
            if (SetProperty(ref _priorityText, value))
                RefreshEditReadiness();
        }
    }

    public string PriorityError
    {
        get => _priorityError;
        private set => SetProperty(ref _priorityError, value);
    }

    public bool IsPriorityErrorVisible
    {
        get => _isPriorityErrorVisible;
        private set => SetProperty(ref _isPriorityErrorVisible, value);
    }

    public string DescriptionText
    {
        get => _descriptionText;
        set => SetProperty(ref _descriptionText, value);
    }

    public string SymbolsText
    {
        get => _symbolsText;
        set
        {
            if (SetProperty(ref _symbolsText, value))
                RefreshEditReadiness();
        }
    }

    public string SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            var provider = string.IsNullOrWhiteSpace(value) ? "IB" : value;
            if (SetProperty(ref _selectedProvider, provider))
            {
                IsIBPanelVisible = provider == "IB";
                IsAlpacaPanelVisible = provider == "Alpaca";
                IsPolygonPanelVisible = provider == "Polygon";
                RefreshEditReadiness();
            }
        }
    }

    public string SelectedType
    {
        get => _selectedType;
        set
        {
            var type = string.IsNullOrWhiteSpace(value) ? "RealTime" : value;
            if (SetProperty(ref _selectedType, type))
                RefreshEditReadiness();
        }
    }

    public string AlpacaFeed
    {
        get => _alpacaFeed;
        set
        {
            var feed = string.IsNullOrWhiteSpace(value) ? "iex" : value;
            if (SetProperty(ref _alpacaFeed, feed))
                RefreshEditReadiness();
        }
    }

    public string AlpacaEnvironmentTag
    {
        get => AlpacaSandbox ? "true" : "false";
        set => AlpacaSandbox = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public string PolygonFeed
    {
        get => _polygonFeed;
        set
        {
            var feed = string.IsNullOrWhiteSpace(value) ? "stocks" : value;
            if (SetProperty(ref _polygonFeed, feed))
                RefreshEditReadiness();
        }
    }

    // ── Provider panel visibility ─────────────────────────────────────────

    public bool IsIBPanelVisible
    {
        get => _isIBPanelVisible;
        private set => SetProperty(ref _isIBPanelVisible, value);
    }

    public bool IsAlpacaPanelVisible
    {
        get => _isAlpacaPanelVisible;
        private set => SetProperty(ref _isAlpacaPanelVisible, value);
    }

    public bool IsPolygonPanelVisible
    {
        get => _isPolygonPanelVisible;
        private set => SetProperty(ref _isPolygonPanelVisible, value);
    }

    // ── IB settings ───────────────────────────────────────────────────────

    public string IBHost { get => _ibHost; set => SetProperty(ref _ibHost, value); }
    public string IBPort { get => _ibPort; set => SetProperty(ref _ibPort, value); }
    public string IBClientId { get => _ibClientId; set => SetProperty(ref _ibClientId, value); }
    public bool IBPaperTrading { get => _ibPaperTrading; set => SetProperty(ref _ibPaperTrading, value); }
    public bool IBSubscribeDepth { get => _ibSubscribeDepth; set => SetProperty(ref _ibSubscribeDepth, value); }
    public bool IBTickByTick { get => _ibTickByTick; set => SetProperty(ref _ibTickByTick, value); }

    // ── Alpaca settings ───────────────────────────────────────────────────

    public bool AlpacaSandbox
    {
        get => _alpacaSandbox;
        set
        {
            if (SetProperty(ref _alpacaSandbox, value))
                OnPropertyChanged(nameof(AlpacaEnvironmentTag));
        }
    }
    public bool AlpacaSubscribeQuotes { get => _alpacaSubscribeQuotes; set => SetProperty(ref _alpacaSubscribeQuotes, value); }

    // ── Polygon settings ──────────────────────────────────────────────────

    public bool PolygonDelayed { get => _polygonDelayed; set => SetProperty(ref _polygonDelayed, value); }
    public bool PolygonSubscribeTrades { get => _polygonSubscribeTrades; set => SetProperty(ref _polygonSubscribeTrades, value); }
    public bool PolygonSubscribeQuotes { get => _polygonSubscribeQuotes; set => SetProperty(ref _polygonSubscribeQuotes, value); }
    public bool PolygonSubscribeAggregates { get => _polygonSubscribeAggregates; set => SetProperty(ref _polygonSubscribeAggregates, value); }

    // ── Default source selection ──────────────────────────────────────────

    public DataSourceConfigDto? SelectedDefaultRealTimeSource
    {
        get => _selectedDefaultRealTimeSource;
        set
        {
            if (SetProperty(ref _selectedDefaultRealTimeSource, value) && !_isLoading && value != null)
                SetDefaultSourceFireAndForget(value.Id, isHistorical: false);
        }
    }

    public DataSourceConfigDto? SelectedDefaultHistoricalSource
    {
        get => _selectedDefaultHistoricalSource;
        set
        {
            if (SetProperty(ref _selectedDefaultHistoricalSource, value) && !_isLoading && value != null)
                SetDefaultSourceFireAndForget(value.Id, isHistorical: true);
        }
    }

    // ── Status message ────────────────────────────────────────────────────

    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool IsStatusError { get => _isStatusError; private set => SetProperty(ref _isStatusError, value); }
    public bool IsStatusVisible { get => _isStatusVisible; private set => SetProperty(ref _isStatusVisible, value); }

    // ── Internal access for code-behind ───────────────────────────────────

    /// <summary>Returns the ID of the source being edited, or null when adding.</summary>
    internal string? EditingSourceId => _editingSourceId;

    // ── Public async methods ──────────────────────────────────────────────

    public async Task LoadAsync(CancellationToken ct = default)
    {
        _isLoading = true;
        try
        {
            var config = await _configService.GetDataSourcesConfigAsync();

            _failoverEnabled = config.EnableFailover;
            _failoverTimeoutText = config.FailoverTimeoutSeconds.ToString();
            OnPropertyChanged(nameof(FailoverEnabled));
            OnPropertyChanged(nameof(FailoverTimeoutText));

            DataSources.Clear();
            foreach (var source in config.Sources ?? Array.Empty<DataSourceConfigDto>())
                DataSources.Add(source);

            SourceCountText = $"({DataSources.Count})";
            IsNoSourcesVisible = DataSources.Count == 0;

            UpdateDefaultSourceCollections(config);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to load data sources: {ex.Message}", isError: true);
        }
        finally
        {
            _isLoading = false;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private void UpdateDefaultSourceCollections(DataSourcesConfigDto config)
    {
        RealTimeSources.Clear();
        HistoricalSources.Clear();

        foreach (var s in DataSources.Where(s => s.Type is "RealTime" or "Both"))
            RealTimeSources.Add(s);
        foreach (var s in DataSources.Where(s => s.Type is "Historical" or "Both"))
            HistoricalSources.Add(s);

        _selectedDefaultRealTimeSource = RealTimeSources.FirstOrDefault(s => s.Id == config.DefaultRealTimeSourceId);
        _selectedDefaultHistoricalSource = HistoricalSources.FirstOrDefault(s => s.Id == config.DefaultHistoricalSourceId);
        OnPropertyChanged(nameof(SelectedDefaultRealTimeSource));
        OnPropertyChanged(nameof(SelectedDefaultHistoricalSource));
    }

    private void BeginAddSource()
    {
        _editingSourceId = null;
        EditPanelTitle = "Add Data Source";
        ResetEditForm();
        IsEditPanelVisible = true;
    }

    private void BeginEditSource(string? sourceId)
    {
        if (sourceId == null)
            return;
        var source = DataSources.FirstOrDefault(s => s.Id == sourceId);
        if (source == null)
            return;

        _editingSourceId = sourceId;
        EditPanelTitle = "Edit Data Source";
        PopulateEditForm(source);
        IsEditPanelVisible = true;
    }

    private async Task DeleteSourceAsync(string? sourceId, CancellationToken ct = default)
    {
        if (sourceId == null)
            return;
        var source = DataSources.FirstOrDefault(s => s.Id == sourceId);
        if (source == null)
            return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete '{source.Name}'?",
            "Delete Data Source",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _configService.DeleteDataSourceAsync(sourceId, ct);
            await LoadAsync(ct);
            ShowStatus("Data source deleted successfully.");
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to delete data source: {ex.Message}", isError: true);
        }
    }

    private async Task SaveSourceAsync(CancellationToken ct = default)
    {
        if (!ValidateEditForm())
            return;

        IsSaving = true;
        try
        {
            var source = BuildDataSourceFromForm();
            await _configService.AddOrUpdateDataSourceAsync(source, ct);
            IsEditPanelVisible = false;
            await LoadAsync(ct);
            ShowStatus(_editingSourceId == null ? "Data source added successfully." : "Data source updated successfully.");
            _editingSourceId = null;
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to save data source: {ex.Message}", isError: true);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void CancelEdit()
    {
        IsEditPanelVisible = false;
        _editingSourceId = null;
        ClearValidationErrors();
    }

    // ── Fire-and-forget wrappers (property setters cannot be async) ───────

    private async void SaveFailoverSettingsFireAndForget()
    {
        try
        { await SaveFailoverSettingsAsync(); }
        catch (Exception ex) { ShowStatus($"Failed to update failover settings: {ex.Message}", isError: true); }
    }

    private async void SetDefaultSourceFireAndForget(string id, bool isHistorical)
    {
        try
        { await SetDefaultSourceAsync(id, isHistorical); }
        catch (Exception ex) { ShowStatus($"Failed to set default source: {ex.Message}", isError: true); }
    }

    private async Task SaveFailoverSettingsAsync(CancellationToken ct = default)
    {
        IsFailoverTimeoutErrorVisible = false;
        if (!int.TryParse(FailoverTimeoutText, out var timeout) || timeout is < 5 or > 300)
        {
            FailoverTimeoutError = "Timeout must be between 5 and 300 seconds.";
            IsFailoverTimeoutErrorVisible = true;
            return;
        }

        try
        {
            await _configService.UpdateFailoverSettingsAsync(FailoverEnabled, timeout, ct);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to update failover settings: {ex.Message}", isError: true);
        }
    }

    private async Task SetDefaultSourceAsync(string id, bool isHistorical, CancellationToken ct = default)
    {
        try
        {
            await _configService.SetDefaultDataSourceAsync(id, isHistorical, ct);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to set default source: {ex.Message}", isError: true);
        }
    }

    private async Task ToggleSourceEnabledAsync(DataSourceConfigDto? source, CancellationToken ct = default)
    {
        if (source == null)
            return;
        try
        {
            await _configService.AddOrUpdateDataSourceAsync(source, ct);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to update data source: {ex.Message}", isError: true);
        }
    }

    private DataSourceConfigDto BuildDataSourceFromForm()
    {
        var source = new DataSourceConfigDto
        {
            Id = _editingSourceId ?? Guid.NewGuid().ToString("N"),
            Name = SourceNameText.Trim(),
            Provider = SelectedProvider,
            Type = SelectedType,
            Priority = int.TryParse(PriorityText, out var p) ? p : 100,
            Description = DescriptionText.Trim(),
            Enabled = true
        };

        if (!string.IsNullOrWhiteSpace(SymbolsText))
        {
            source.Symbols = ParseSymbols(SymbolsText);
        }

        switch (SelectedProvider)
        {
            case "IB":
                source.IB = new IBOptionsDto
                {
                    Host = IBHost,
                    Port = int.TryParse(IBPort, out var port) ? port : 7497,
                    ClientId = int.TryParse(IBClientId, out var cid) ? cid : 1,
                    UsePaperTrading = IBPaperTrading,
                    SubscribeDepth = IBSubscribeDepth,
                    TickByTick = IBTickByTick
                };
                break;
            case "Alpaca":
                source.Alpaca = new AlpacaOptionsDto
                {
                    Feed = AlpacaFeed,
                    UseSandbox = AlpacaSandbox,
                    SubscribeQuotes = AlpacaSubscribeQuotes
                };
                break;
            case "Polygon":
                source.Polygon = new PolygonOptionsDto
                {
                    ApiKey = PolygonApiKey,
                    Feed = PolygonFeed,
                    UseDelayed = PolygonDelayed,
                    SubscribeTrades = PolygonSubscribeTrades,
                    SubscribeQuotes = PolygonSubscribeQuotes,
                    SubscribeAggregates = PolygonSubscribeAggregates
                };
                break;
        }

        return source;
    }

    private void PopulateEditForm(DataSourceConfigDto source)
    {
        SourceNameText = source.Name;
        SelectedType = source.Type ?? "RealTime";
        PriorityText = source.Priority.ToString();
        DescriptionText = source.Description ?? string.Empty;
        SymbolsText = source.Symbols != null ? string.Join(", ", source.Symbols) : string.Empty;

        SelectedProvider = source.Provider ?? "IB";

        if (source.IB != null)
        {
            IBHost = source.IB.Host ?? "127.0.0.1";
            IBPort = source.IB.Port.ToString();
            IBClientId = source.IB.ClientId.ToString();
            IBPaperTrading = source.IB.UsePaperTrading;
            IBSubscribeDepth = source.IB.SubscribeDepth;
            IBTickByTick = source.IB.TickByTick;
        }

        if (source.Alpaca != null)
        {
            AlpacaFeed = source.Alpaca.Feed ?? "iex";
            AlpacaSandbox = source.Alpaca.UseSandbox;
            AlpacaSubscribeQuotes = source.Alpaca.SubscribeQuotes;
        }

        if (source.Polygon != null)
        {
            PolygonApiKey = source.Polygon.ApiKey ?? string.Empty;
            PolygonFeed = source.Polygon.Feed ?? "stocks";
            PolygonDelayed = source.Polygon.UseDelayed;
            PolygonSubscribeTrades = source.Polygon.SubscribeTrades;
            PolygonSubscribeQuotes = source.Polygon.SubscribeQuotes;
            PolygonSubscribeAggregates = source.Polygon.SubscribeAggregates;
        }
    }

    private void ResetEditForm()
    {
        SourceNameText = string.Empty;
        SelectedProvider = "IB";
        SelectedType = "RealTime";
        PriorityText = "100";
        DescriptionText = string.Empty;
        SymbolsText = string.Empty;

        IBHost = "127.0.0.1";
        IBPort = "7497";
        IBClientId = "1";
        IBPaperTrading = true;
        IBSubscribeDepth = true;
        IBTickByTick = true;

        AlpacaFeed = "iex";
        AlpacaSandbox = false;
        AlpacaSubscribeQuotes = false;

        PolygonApiKey = string.Empty;
        PolygonFeed = "stocks";
        PolygonDelayed = false;
        PolygonSubscribeTrades = true;
        PolygonSubscribeQuotes = false;
        PolygonSubscribeAggregates = false;

        ClearValidationErrors();
        SelectedProvider = "IB";
    }

    private bool ValidateEditForm()
    {
        var state = RefreshEditReadiness();
        return state.CanSave;
    }

    internal DataSourceEditReadinessState RefreshEditReadiness()
    {
        var state = BuildEditReadinessState(
            SourceNameText,
            PriorityText,
            SelectedProvider,
            SelectedType,
            SymbolsText);

        SourceNameError = state.SourceNameError;
        IsSourceNameErrorVisible = state.IsSourceNameErrorVisible;
        PriorityError = state.PriorityError;
        IsPriorityErrorVisible = state.IsPriorityErrorVisible;
        SourceSetupReadinessTitle = state.Title;
        SourceSetupReadinessDetail = state.Detail;
        SourceSetupScopeText = state.ScopeText;
        CanSaveSource = IsEditPanelVisible && state.CanSave && !IsSaving;
        return state;
    }

    internal static DataSourceEditReadinessState BuildEditReadinessState(
        string? sourceName,
        string? priorityText,
        string? provider,
        string? type,
        string? symbolsText)
    {
        var nameError = string.Empty;
        var priorityError = string.Empty;
        var blockers = new List<string>();

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            nameError = "Name is required.";
            blockers.Add("Name the source.");
        }

        if (!int.TryParse(priorityText, out var priority) || priority is < 1 or > 1000)
        {
            priorityError = "Priority must be between 1 and 1000.";
            blockers.Add("Set a priority between 1 and 1000.");
        }

        var providerLabel = GetProviderDisplayName(provider);
        var typeLabel = GetTypeDisplayName(type);
        var symbolScope = BuildSymbolScopeText(symbolsText);
        var canSave = blockers.Count == 0;

        return new DataSourceEditReadinessState(
            canSave,
            canSave ? "Data source ready" : "Data source setup incomplete",
            canSave
                ? $"{providerLabel} {typeLabel.ToLowerInvariant()} source is ready to save."
                : string.Join(" ", blockers),
            $"{providerLabel} - {typeLabel} - {symbolScope}",
            nameError,
            !string.IsNullOrEmpty(nameError),
            priorityError,
            !string.IsNullOrEmpty(priorityError));
    }

    private static string GetProviderDisplayName(string? provider) => provider switch
    {
        "Alpaca" => "Alpaca",
        "Polygon" => "Polygon.io",
        "Robinhood" => "Robinhood",
        _ => "Interactive Brokers"
    };

    private static string GetTypeDisplayName(string? type) => type switch
    {
        "Historical" => "Historical",
        "Both" => "Real-time and historical",
        _ => "Real-time"
    };

    private static string BuildSymbolScopeText(string? symbolsText)
    {
        var symbols = ParseSymbols(symbolsText);
        return symbols.Length == 0
            ? "Global symbol list"
            : symbols.Length == 1
                ? "1 symbol: " + symbols[0]
                : $"{symbols.Length} symbols: {string.Join(", ", symbols.Take(4))}{(symbols.Length > 4 ? ", ..." : string.Empty)}";
    }

    private static string[] ParseSymbols(string? symbolsText)
        => string.IsNullOrWhiteSpace(symbolsText)
            ? Array.Empty<string>()
            : symbolsText
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private void ClearValidationErrors()
    {
        SourceNameError = string.Empty;
        IsSourceNameErrorVisible = false;
        PriorityError = string.Empty;
        IsPriorityErrorVisible = false;
        FailoverTimeoutError = string.Empty;
        IsFailoverTimeoutErrorVisible = false;
    }

    private void ShowStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        IsStatusError = isError;
        IsStatusVisible = true;
    }
}

internal sealed record DataSourceEditReadinessState(
    bool CanSave,
    string Title,
    string Detail,
    string ScopeText,
    string SourceNameError,
    bool IsSourceNameErrorVisible,
    string PriorityError,
    bool IsPriorityErrorVisible);
