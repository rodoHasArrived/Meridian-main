using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

public interface ISymbolMappingClient
{
    IReadOnlyList<MappingProviderInfo> Providers { get; }

    Task LoadAsync(CancellationToken ct = default);

    IReadOnlyList<SymbolMapping> GetMappings();

    SymbolMapping? GetMapping(string canonicalSymbol);

    Dictionary<string, string> TestMapping(string canonicalSymbol);

    Task AddOrUpdateMappingAsync(SymbolMapping mapping, CancellationToken ct = default);

    Task RemoveMappingAsync(string canonicalSymbol, CancellationToken ct = default);

    Task<int> ImportFromCsvAsync(string csvContent, CancellationToken ct = default);

    string ExportToCsv();
}

public sealed class SymbolMappingServiceClient : ISymbolMappingClient
{
    private readonly SymbolMappingService _service;

    public SymbolMappingServiceClient()
        : this(SymbolMappingService.Instance)
    {
    }

    public SymbolMappingServiceClient(SymbolMappingService service)
    {
        _service = service;
    }

    public IReadOnlyList<MappingProviderInfo> Providers => SymbolMappingService.KnownProviders;

    public Task LoadAsync(CancellationToken ct = default) => _service.LoadAsync(ct);

    public IReadOnlyList<SymbolMapping> GetMappings() => _service.GetMappings();

    public SymbolMapping? GetMapping(string canonicalSymbol) => _service.GetMapping(canonicalSymbol);

    public Dictionary<string, string> TestMapping(string canonicalSymbol) => _service.TestMapping(canonicalSymbol);

    public Task AddOrUpdateMappingAsync(SymbolMapping mapping, CancellationToken ct = default)
        => _service.AddOrUpdateMappingAsync(mapping, ct);

    public Task RemoveMappingAsync(string canonicalSymbol, CancellationToken ct = default)
        => _service.RemoveMappingAsync(canonicalSymbol, ct);

    public Task<int> ImportFromCsvAsync(string csvContent, CancellationToken ct = default)
        => _service.ImportFromCsvAsync(csvContent, ct);

    public string ExportToCsv() => _service.ExportToCsv();
}

public sealed class SymbolMappingViewModel : BindableBase
{
    private readonly ISymbolMappingClient _mappingClient;
    private string _testSymbol = string.Empty;
    private string _newCanonicalSymbol = string.Empty;
    private string _selectedProviderId = string.Empty;
    private string _newProviderSymbol = string.Empty;
    private string _statusMessage = "Load mappings to inspect provider-specific symbol translations.";
    private string _testScopeText = "Enter a canonical symbol to preview all provider mappings.";
    private string _mappingReadinessTitle = "Mapping setup incomplete";
    private string _mappingReadinessDetail = "Enter a canonical symbol, provider, and provider-specific symbol before saving.";
    private string _mappingCountText = "0 mappings";
    private string _pendingRemoveCanonicalSymbol = string.Empty;
    private Visibility _mappingsVisibility = Visibility.Collapsed;
    private Visibility _emptyMappingsVisibility = Visibility.Visible;
    private Visibility _testResultsVisibility = Visibility.Collapsed;
    private Visibility _removeConfirmationVisibility = Visibility.Collapsed;
    private bool _isBusy;

    public SymbolMappingViewModel()
        : this(new SymbolMappingServiceClient())
    {
    }

    public SymbolMappingViewModel(ISymbolMappingClient mappingClient)
    {
        _mappingClient = mappingClient ?? throw new ArgumentNullException(nameof(mappingClient));
        TestMappingCommand = new RelayCommand(TestMapping, CanTestMapping);
        AddMappingCommand = new AsyncRelayCommand(() => AddMappingAsync(), CanAddMapping);
        RequestRemoveMappingCommand = new RelayCommand<string>(RequestRemoveMapping, CanRequestRemoveMapping);
        ConfirmRemoveMappingCommand = new AsyncRelayCommand(() => ConfirmRemoveMappingAsync(), CanConfirmRemoveMapping);
        CancelRemoveMappingCommand = new RelayCommand(CancelRemoveMapping, CanConfirmRemoveMapping);
    }

    public ObservableCollection<SymbolMappingProviderInfo> Providers { get; } = new();

    public ObservableCollection<SymbolMappingProviderOption> ProviderOptions { get; } = new();

    public ObservableCollection<SymbolMappingItem> Mappings { get; } = new();

    public ObservableCollection<SymbolMappingTestResultItem> TestResults { get; } = new();

    public RelayCommand TestMappingCommand { get; }

    public IAsyncRelayCommand AddMappingCommand { get; }

    public RelayCommand<string> RequestRemoveMappingCommand { get; }

    public IAsyncRelayCommand ConfirmRemoveMappingCommand { get; }

    public RelayCommand CancelRemoveMappingCommand { get; }

    public string TestSymbol
    {
        get => _testSymbol;
        set
        {
            if (SetProperty(ref _testSymbol, value))
            {
                TestMappingCommand.NotifyCanExecuteChanged();
                RefreshTestScope();
            }
        }
    }

    public string NewCanonicalSymbol
    {
        get => _newCanonicalSymbol;
        set
        {
            if (SetProperty(ref _newCanonicalSymbol, value))
            {
                RefreshAddMappingPresentation();
            }
        }
    }

    public string SelectedProviderId
    {
        get => _selectedProviderId;
        set
        {
            if (SetProperty(ref _selectedProviderId, value))
            {
                RefreshAddMappingPresentation();
            }
        }
    }

    public string NewProviderSymbol
    {
        get => _newProviderSymbol;
        set
        {
            if (SetProperty(ref _newProviderSymbol, value))
            {
                RefreshAddMappingPresentation();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string TestScopeText
    {
        get => _testScopeText;
        private set => SetProperty(ref _testScopeText, value);
    }

    public string MappingReadinessTitle
    {
        get => _mappingReadinessTitle;
        private set => SetProperty(ref _mappingReadinessTitle, value);
    }

    public string MappingReadinessDetail
    {
        get => _mappingReadinessDetail;
        private set => SetProperty(ref _mappingReadinessDetail, value);
    }

    public string MappingCountText
    {
        get => _mappingCountText;
        private set => SetProperty(ref _mappingCountText, value);
    }

    public string PendingRemoveCanonicalSymbol
    {
        get => _pendingRemoveCanonicalSymbol;
        private set
        {
            if (SetProperty(ref _pendingRemoveCanonicalSymbol, value))
            {
                ConfirmRemoveMappingCommand.NotifyCanExecuteChanged();
                CancelRemoveMappingCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Visibility MappingsVisibility
    {
        get => _mappingsVisibility;
        private set => SetProperty(ref _mappingsVisibility, value);
    }

    public Visibility EmptyMappingsVisibility
    {
        get => _emptyMappingsVisibility;
        private set => SetProperty(ref _emptyMappingsVisibility, value);
    }

    public Visibility TestResultsVisibility
    {
        get => _testResultsVisibility;
        private set => SetProperty(ref _testResultsVisibility, value);
    }

    public Visibility RemoveConfirmationVisibility
    {
        get => _removeConfirmationVisibility;
        private set => SetProperty(ref _removeConfirmationVisibility, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommandStates();
                RefreshAddMappingPresentation();
            }
        }
    }

    public bool CanTestMapping() => !string.IsNullOrWhiteSpace(TestSymbol);

    public bool CanAddMapping()
        => !IsBusy &&
           !string.IsNullOrWhiteSpace(NewCanonicalSymbol) &&
           !string.IsNullOrWhiteSpace(SelectedProviderId) &&
           !string.IsNullOrWhiteSpace(NewProviderSymbol);

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        StatusMessage = "Loading symbol mappings...";

        try
        {
            await _mappingClient.LoadAsync(ct).ConfigureAwait(true);
            LoadProviderPresentation();
            RefreshMappings();
            StatusMessage = $"Loaded {Mappings.Count} custom mapping{(Mappings.Count == 1 ? "" : "s")}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Failed to load mappings: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void TestMapping()
    {
        var symbol = NormalizeSymbol(TestSymbol);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            StatusMessage = "Enter a canonical symbol before testing provider mappings.";
            return;
        }

        TestResults.Clear();
        foreach (var (providerId, mappedSymbol) in _mappingClient.TestMapping(symbol))
        {
            var provider = _mappingClient.Providers.FirstOrDefault(
                candidate => string.Equals(candidate.Id, providerId, StringComparison.OrdinalIgnoreCase));
            TestResults.Add(new SymbolMappingTestResultItem(
                provider?.DisplayName ?? providerId,
                mappedSymbol));
        }

        TestSymbol = symbol;
        TestResultsVisibility = TestResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TestScopeText = TestResults.Count == 0
            ? $"No provider mappings were returned for {symbol}."
            : $"{symbol} maps across {TestResults.Count} provider{(TestResults.Count == 1 ? "" : "s")}.";
        StatusMessage = $"Tested mappings for {symbol}.";
    }

    public async Task AddMappingAsync(CancellationToken ct = default)
    {
        if (!CanAddMapping())
        {
            StatusMessage = "Enter a canonical symbol, provider, and provider-specific symbol before saving.";
            return;
        }

        var canonical = NormalizeSymbol(NewCanonicalSymbol);
        var providerSymbol = NewProviderSymbol.Trim();
        var providerId = SelectedProviderId.Trim();

        IsBusy = true;
        StatusMessage = $"Saving mapping for {canonical}...";

        try
        {
            var mapping = _mappingClient.GetMapping(canonical) ?? new SymbolMapping { CanonicalSymbol = canonical };
            mapping.ProviderSymbols ??= new Dictionary<string, string>();
            mapping.ProviderSymbols[providerId] = providerSymbol;
            mapping.IsCustomMapping = true;

            await _mappingClient.AddOrUpdateMappingAsync(mapping, ct).ConfigureAwait(true);

            NewCanonicalSymbol = string.Empty;
            NewProviderSymbol = string.Empty;
            PendingRemoveCanonicalSymbol = string.Empty;
            RemoveConfirmationVisibility = Visibility.Collapsed;
            RefreshMappings();
            StatusMessage = $"Saved {canonical} mapping for {providerId}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Failed to save mapping: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void RequestRemoveMapping(string? canonicalSymbol)
    {
        if (!CanRequestRemoveMapping(canonicalSymbol))
        {
            return;
        }

        PendingRemoveCanonicalSymbol = canonicalSymbol!.Trim().ToUpperInvariant();
        RemoveConfirmationVisibility = Visibility.Visible;
        StatusMessage = $"Review removal for {PendingRemoveCanonicalSymbol}.";
    }

    public async Task ConfirmRemoveMappingAsync(CancellationToken ct = default)
    {
        if (!CanConfirmRemoveMapping())
        {
            return;
        }

        var canonical = PendingRemoveCanonicalSymbol;
        IsBusy = true;
        StatusMessage = $"Removing mapping for {canonical}...";

        try
        {
            await _mappingClient.RemoveMappingAsync(canonical, ct).ConfigureAwait(true);
            PendingRemoveCanonicalSymbol = string.Empty;
            RemoveConfirmationVisibility = Visibility.Collapsed;
            RefreshMappings();
            StatusMessage = $"Removed mapping for {canonical}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Failed to remove mapping: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void CancelRemoveMapping()
    {
        PendingRemoveCanonicalSymbol = string.Empty;
        RemoveConfirmationVisibility = Visibility.Collapsed;
        StatusMessage = "Mapping removal canceled.";
    }

    public async Task<int> ImportCsvContentAsync(string csvContent, CancellationToken ct = default)
    {
        IsBusy = true;
        StatusMessage = "Importing symbol mappings...";

        try
        {
            var imported = await _mappingClient.ImportFromCsvAsync(csvContent, ct).ConfigureAwait(true);
            RefreshMappings();
            StatusMessage = imported == 1
                ? "Imported 1 symbol mapping."
                : $"Imported {imported} symbol mappings.";
            return imported;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Import failed: {ex.Message}";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string ExportCsv()
    {
        var csv = _mappingClient.ExportToCsv();
        StatusMessage = $"Exported {Mappings.Count} mapping{(Mappings.Count == 1 ? "" : "s")} to CSV.";
        return csv;
    }

    private void LoadProviderPresentation()
    {
        Providers.Clear();
        ProviderOptions.Clear();

        foreach (var provider in _mappingClient.Providers)
        {
            Providers.Add(new SymbolMappingProviderInfo(
                provider.Id,
                provider.DisplayName,
                provider.TransformDescription));
            ProviderOptions.Add(new SymbolMappingProviderOption(
                provider.Id,
                $"{provider.Id} - {provider.DisplayName}"));
        }

        if (string.IsNullOrWhiteSpace(SelectedProviderId) && ProviderOptions.Count > 0)
        {
            SelectedProviderId = ProviderOptions[0].Id;
        }
    }

    private void RefreshMappings()
    {
        Mappings.Clear();
        foreach (var mapping in _mappingClient.GetMappings())
        {
            Mappings.Add(ProjectMapping(mapping));
        }

        MappingCountText = $"{Mappings.Count} mapping{(Mappings.Count == 1 ? "" : "s")}";
        MappingsVisibility = Mappings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyMappingsVisibility = Mappings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshAddMappingPresentation()
    {
        AddMappingCommand.NotifyCanExecuteChanged();

        if (IsBusy)
        {
            MappingReadinessTitle = "Mapping update in progress";
            MappingReadinessDetail = "Saving or loading mapping changes. Controls will re-enable when the operation finishes.";
            return;
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(NewCanonicalSymbol))
        {
            missing.Add("canonical symbol");
        }

        if (string.IsNullOrWhiteSpace(SelectedProviderId))
        {
            missing.Add("provider");
        }

        if (string.IsNullOrWhiteSpace(NewProviderSymbol))
        {
            missing.Add("provider symbol");
        }

        if (missing.Count == 0)
        {
            var provider = ProviderOptions.FirstOrDefault(option =>
                string.Equals(option.Id, SelectedProviderId, StringComparison.OrdinalIgnoreCase));
            MappingReadinessTitle = "Mapping ready";
            MappingReadinessDetail =
                $"{NormalizeSymbol(NewCanonicalSymbol)} will map to {NewProviderSymbol.Trim()} for {provider?.Label ?? SelectedProviderId}.";
            return;
        }

        MappingReadinessTitle = "Mapping setup incomplete";
        MappingReadinessDetail = $"Missing {string.Join(", ", missing)}.";
    }

    private void RefreshTestScope()
    {
        if (TestResults.Count == 0)
        {
            TestResultsVisibility = Visibility.Collapsed;
        }

        TestScopeText = string.IsNullOrWhiteSpace(TestSymbol)
            ? "Enter a canonical symbol to preview all provider mappings."
            : $"Ready to test {NormalizeSymbol(TestSymbol)} across {Providers.Count} provider{(Providers.Count == 1 ? "" : "s")}.";
    }

    private void RefreshCommandStates()
    {
        TestMappingCommand.NotifyCanExecuteChanged();
        AddMappingCommand.NotifyCanExecuteChanged();
        RequestRemoveMappingCommand.NotifyCanExecuteChanged();
        ConfirmRemoveMappingCommand.NotifyCanExecuteChanged();
        CancelRemoveMappingCommand.NotifyCanExecuteChanged();
    }

    private bool CanRequestRemoveMapping(string? canonicalSymbol)
        => !IsBusy && !string.IsNullOrWhiteSpace(canonicalSymbol);

    private bool CanConfirmRemoveMapping()
        => !IsBusy && !string.IsNullOrWhiteSpace(PendingRemoveCanonicalSymbol);

    private static SymbolMappingItem ProjectMapping(SymbolMapping mapping)
    {
        var providerTexts = new List<string>();
        if (mapping.ProviderSymbols != null)
        {
            foreach (var (providerId, symbol) in mapping.ProviderSymbols)
            {
                if (!string.IsNullOrWhiteSpace(symbol))
                {
                    providerTexts.Add($"{providerId}: {symbol}");
                }
            }
        }

        return new SymbolMappingItem(
            NormalizeSymbol(mapping.CanonicalSymbol),
            providerTexts.Count > 0 ? string.Join(", ", providerTexts) : "(defaults only)",
            mapping.UpdatedAt.ToString("MMM dd, yyyy"));
    }

    private static string NormalizeSymbol(string symbol)
        => symbol.Trim().ToUpperInvariant();
}

public sealed record SymbolMappingProviderInfo(
    string Id,
    string DisplayName,
    string Description);

public sealed record SymbolMappingProviderOption(
    string Id,
    string Label);

public sealed record SymbolMappingItem(
    string CanonicalSymbol,
    string ProviderMappingsText,
    string UpdatedText);

public sealed record SymbolMappingTestResultItem(
    string ProviderName,
    string MappedSymbol);
