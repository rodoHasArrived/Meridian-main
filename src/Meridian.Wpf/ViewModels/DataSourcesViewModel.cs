using System;
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

    // ── Internal form state (set by code-behind for non-bindable controls)
    private string? _editingSourceId;

    /// <summary>Provider tag for the current edit form (e.g. "IB", "Alpaca", "Polygon"). Set by code-behind.</summary>
    internal string SelectedProvider { get; set; } = "IB";

    /// <summary>Type tag for the current edit form (e.g. "RealTime", "Historical", "Both"). Set by code-behind.</summary>
    internal string SelectedType { get; set; } = "RealTime";

    /// <summary>Alpaca feed tag (e.g. "iex", "sip"). Set by code-behind.</summary>
    internal string AlpacaFeed { get; set; } = "iex";

    /// <summary>Polygon feed tag (e.g. "stocks", "options"). Set by code-behind.</summary>
    internal string PolygonFeed { get; set; } = "stocks";

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
        SaveSourceCommand = new AsyncRelayCommand(SaveSourceAsync);
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
        private set => SetProperty(ref _isEditPanelVisible, value);
    }

    public string EditPanelTitle
    {
        get => _editPanelTitle;
        private set => SetProperty(ref _editPanelTitle, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set => SetProperty(ref _isSaving, value);
    }

    // ── Form field properties ─────────────────────────────────────────────

    public string SourceNameText
    {
        get => _sourceNameText;
        set => SetProperty(ref _sourceNameText, value);
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
        set => SetProperty(ref _priorityText, value);
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
        set => SetProperty(ref _symbolsText, value);
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

    public bool AlpacaSandbox { get => _alpacaSandbox; set => SetProperty(ref _alpacaSandbox, value); }
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

    /// <summary>Called by code-behind when the Provider ComboBox selection changes.</summary>
    internal void OnProviderSelected(string provider)
    {
        SelectedProvider = provider;
        IsIBPanelVisible = provider == "IB";
        IsAlpacaPanelVisible = provider == "Alpaca";
        IsPolygonPanelVisible = provider == "Polygon";
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
        if (sourceId == null) return;
        var source = DataSources.FirstOrDefault(s => s.Id == sourceId);
        if (source == null) return;

        _editingSourceId = sourceId;
        EditPanelTitle = "Edit Data Source";
        PopulateEditForm(source);
        IsEditPanelVisible = true;
    }

    private async Task DeleteSourceAsync(string? sourceId, CancellationToken ct = default)
    {
        if (sourceId == null) return;
        var source = DataSources.FirstOrDefault(s => s.Id == sourceId);
        if (source == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete '{source.Name}'?",
            "Delete Data Source",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

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
        if (!ValidateEditForm()) return;

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
    }

    // ── Fire-and-forget wrappers (property setters cannot be async) ───────

    private async void SaveFailoverSettingsFireAndForget()
    {
        try { await SaveFailoverSettingsAsync(); }
        catch (Exception ex) { ShowStatus($"Failed to update failover settings: {ex.Message}", isError: true); }
    }

    private async void SetDefaultSourceFireAndForget(string id, bool isHistorical)
    {
        try { await SetDefaultSourceAsync(id, isHistorical); }
        catch (Exception ex) { ShowStatus($"Failed to set default source: {ex.Message}", isError: true); }
    }

    private async Task SaveFailoverSettingsAsync(CancellationToken ct = default)
    {        IsFailoverTimeoutErrorVisible = false;
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
        if (source == null) return;
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
            source.Symbols = SymbolsText
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
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

        OnProviderSelected(source.Provider ?? "IB");

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
        OnProviderSelected("IB");
    }

    private bool ValidateEditForm()
    {
        ClearValidationErrors();
        var hasError = false;

        if (string.IsNullOrWhiteSpace(SourceNameText))
        {
            SourceNameError = "Name is required.";
            IsSourceNameErrorVisible = true;
            hasError = true;
        }

        if (!int.TryParse(PriorityText, out var priority) || priority is < 1 or > 1000)
        {
            PriorityError = "Priority must be between 1 and 1000.";
            IsPriorityErrorVisible = true;
            hasError = true;
        }

        return !hasError;
    }

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
