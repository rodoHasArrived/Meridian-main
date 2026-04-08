using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class FundProfileSelectionViewModel : BindableBase
{
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService _operatingContextService;
    private readonly ObservableCollection<FundProfileDetail> _profiles = [];
    private readonly ObservableCollection<WorkstationOperatingContext> _operatingContexts = [];
    private WorkstationOperatingContext? _selectedOperatingContext;
    private FundProfileDetail? _selectedProfile;
    private string _newProfileName = string.Empty;
    private string _newLegalEntityName = string.Empty;
    private string _newBaseCurrency = "USD";
    private bool _manageProfilesVisible;
    private string _statusText = "Choose an operating context before entering the workstation.";

    public FundProfileSelectionViewModel(
        FundContextService fundContextService,
        WorkstationOperatingContextService operatingContextService)
    {
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _operatingContextService = operatingContextService ?? throw new ArgumentNullException(nameof(operatingContextService));

        Profiles = new ReadOnlyObservableCollection<FundProfileDetail>(_profiles);
        OperatingContexts = new ReadOnlyObservableCollection<WorkstationOperatingContext>(_operatingContexts);

        EnterFundCommand = new AsyncRelayCommand(EnterFundAsync, CanEnterFund);
        ToggleManageProfilesCommand = new RelayCommand(() => ManageProfilesVisible = !ManageProfilesVisible);
        CreateProfileCommand = new AsyncRelayCommand(CreateProfileAsync);
        DeleteSelectedProfileCommand = new AsyncRelayCommand(DeleteSelectedProfileAsync, () => SelectedProfile is not null);
        SeedProfilesCommand = new AsyncRelayCommand(SeedProfilesAsync);
    }

    public ReadOnlyObservableCollection<FundProfileDetail> Profiles { get; }

    public ReadOnlyObservableCollection<WorkstationOperatingContext> OperatingContexts { get; }

    public IAsyncRelayCommand EnterFundCommand { get; }

    public IRelayCommand ToggleManageProfilesCommand { get; }

    public IAsyncRelayCommand CreateProfileCommand { get; }

    public IAsyncRelayCommand DeleteSelectedProfileCommand { get; }

    public IAsyncRelayCommand SeedProfilesCommand { get; }

    public WorkstationOperatingContext? SelectedOperatingContext
    {
        get => _selectedOperatingContext;
        set
        {
            if (SetProperty(ref _selectedOperatingContext, value))
            {
                RaisePropertyChanged(nameof(SelectedContextLastOpenedText));
                RaisePropertyChanged(nameof(SelectedContextScopeText));
                RaisePropertyChanged(nameof(SelectedContextBusinessKindText));
                RaisePropertyChanged(nameof(SelectedContextCompatibilityText));
                RaisePropertyChanged(nameof(SelectedContextEntityCountText));
                RaisePropertyChanged(nameof(SelectedContextPortfolioCountText));
                RaisePropertyChanged(nameof(SelectedContextLedgerScopeText));
                RaisePropertyChanged(nameof(SelectedContextDefaultWorkspaceText));
                EnterFundCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public FundProfileDetail? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                RaisePropertyChanged(nameof(SelectedProfileLastOpenedText));
                DeleteSelectedProfileCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string SelectedContextLastOpenedText =>
        SelectedOperatingContext?.LastOpenedAt is null
            ? "No previous session"
            : $"Last opened {SelectedOperatingContext.LastOpenedAt.Value.LocalDateTime:g}";

    public string SelectedContextScopeText =>
        SelectedOperatingContext is null ? "-" : SelectedOperatingContext.ScopeKind.ToDisplayName();

    public string SelectedContextBusinessKindText =>
        SelectedOperatingContext?.BusinessKind switch
        {
            Meridian.Contracts.FundStructure.BusinessKindDto.FinancialAdvisor => "Advisory",
            Meridian.Contracts.FundStructure.BusinessKindDto.FundManager => "Fund Manager",
            Meridian.Contracts.FundStructure.BusinessKindDto.Hybrid => "Hybrid",
            _ => "-"
        };

    public string SelectedContextCompatibilityText =>
        string.IsNullOrWhiteSpace(SelectedOperatingContext?.CompatibilityFundProfileId)
            ? "No fund-compatibility projection"
            : $"Fund compatibility: {SelectedOperatingContext.CompatibilityFundProfileId}";

    public string SelectedContextEntityCountText =>
        SelectedOperatingContext is null ? "-" : SelectedOperatingContext.EntityIds.Count.ToString();

    public string SelectedContextPortfolioCountText =>
        SelectedOperatingContext is null ? "-" : SelectedOperatingContext.PortfolioIds.Count.ToString();

    public string SelectedContextLedgerScopeText =>
        SelectedOperatingContext is null
            ? "-"
            : SelectedOperatingContext.LedgerGroupIds.Count == 0
                ? "Account-level"
                : $"{SelectedOperatingContext.LedgerGroupIds.Count} ledger group(s)";

    public string SelectedContextDefaultWorkspaceText =>
        SelectedOperatingContext is null
            ? "-"
            : $"{SelectedOperatingContext.DefaultWorkspaceId} · {SelectedOperatingContext.DefaultLandingPageTag}";

    public string SelectedProfileLastOpenedText =>
        SelectedProfile?.LastOpenedAt is null
            ? "No previous session"
            : $"Last opened {SelectedProfile.LastOpenedAt.Value.LocalDateTime:g}";

    public string NewProfileName
    {
        get => _newProfileName;
        set => SetProperty(ref _newProfileName, value);
    }

    public string NewLegalEntityName
    {
        get => _newLegalEntityName;
        set => SetProperty(ref _newLegalEntityName, value);
    }

    public string NewBaseCurrency
    {
        get => _newBaseCurrency;
        set => SetProperty(ref _newBaseCurrency, value);
    }

    public bool ManageProfilesVisible
    {
        get => _manageProfilesVisible;
        set => SetProperty(ref _manageProfilesVisible, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _operatingContextService.LoadAsync(ct);
        await RefreshContextsAsync(ct);
        await RefreshProfilesAsync(ct);
        ManageProfilesVisible = _profiles.Count == 0;

        StatusText = _operatingContexts.Count == 0
            ? "No operating contexts exist yet. Create a fund profile or seed sample hybrid contexts to continue."
            : "Choose an operating context before entering the workstation.";
    }

    private bool CanEnterFund() => SelectedOperatingContext is not null;

    private async Task EnterFundAsync()
    {
        if (SelectedOperatingContext is null)
        {
            return;
        }

        await _operatingContextService.SelectContextAsync(SelectedOperatingContext.ContextKey);
    }

    private async Task CreateProfileAsync()
    {
        var displayName = string.IsNullOrWhiteSpace(NewProfileName) ? "New Fund" : NewProfileName.Trim();
        var profileId = displayName.ToLowerInvariant().Replace(' ', '-');
        var profile = new FundProfileDetail(
            FundProfileId: profileId,
            DisplayName: displayName,
            LegalEntityName: string.IsNullOrWhiteSpace(NewLegalEntityName) ? displayName : NewLegalEntityName.Trim(),
            BaseCurrency: string.IsNullOrWhiteSpace(NewBaseCurrency) ? "USD" : NewBaseCurrency.Trim().ToUpperInvariant(),
            DefaultWorkspaceId: "governance",
            DefaultLandingPageTag: "GovernanceShell",
            DefaultLedgerScope: FundLedgerScope.Consolidated,
            IsDefault: _profiles.Count == 0);

        await _fundContextService.UpsertProfileAsync(profile);
        await _operatingContextService.RefreshContextsAsync();
        await RefreshProfilesAsync();
        await RefreshContextsAsync();

        NewProfileName = string.Empty;
        NewLegalEntityName = string.Empty;
        NewBaseCurrency = "USD";
        ManageProfilesVisible = true;
        StatusText = $"Created fund profile '{displayName}'.";
    }

    private async Task DeleteSelectedProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var deletedProfileId = SelectedProfile.FundProfileId;
        await _fundContextService.DeleteProfileAsync(deletedProfileId);
        await _operatingContextService.RefreshContextsAsync();
        await RefreshProfilesAsync();
        await RefreshContextsAsync();
        ManageProfilesVisible = _profiles.Count == 0;
        StatusText = $"Removed fund profile '{deletedProfileId}'.";
    }

    private async Task SeedProfilesAsync()
    {
        await _fundContextService.UpsertProfileAsync(new FundProfileDetail(
            FundProfileId: "alpha-credit",
            DisplayName: "Alpha Credit",
            LegalEntityName: "Alpha Credit Master Fund LP",
            BaseCurrency: "USD",
            DefaultWorkspaceId: "governance",
            DefaultLandingPageTag: "GovernanceShell",
            DefaultLedgerScope: FundLedgerScope.Consolidated,
            IsDefault: true));

        await _fundContextService.UpsertProfileAsync(new FundProfileDetail(
            FundProfileId: "macro-ops",
            DisplayName: "Macro Ops",
            LegalEntityName: "Macro Ops Trading Fund Ltd",
            BaseCurrency: "USD",
            DefaultWorkspaceId: "trading",
            DefaultLandingPageTag: "TradingShell",
            DefaultLedgerScope: FundLedgerScope.Consolidated));

        await _operatingContextService.SeedHybridSampleAsync();
        await RefreshProfilesAsync();
        await RefreshContextsAsync();
        StatusText = "Seeded sample hybrid advisory and fund operating contexts.";
    }

    private Task RefreshProfilesAsync(CancellationToken ct = default)
    {
        _profiles.Clear();
        foreach (var profile in _fundContextService.Profiles
                     .OrderByDescending(profile => profile.IsDefault)
                     .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            _profiles.Add(profile);
        }

        SelectedProfile = _profiles.FirstOrDefault(profile =>
                              string.Equals(profile.FundProfileId, _fundContextService.LastSelectedFundProfileId, StringComparison.OrdinalIgnoreCase))
                          ?? _profiles.FirstOrDefault();

        DeleteSelectedProfileCommand.NotifyCanExecuteChanged();
        return Task.CompletedTask;
    }

    private Task RefreshContextsAsync(CancellationToken ct = default)
    {
        _operatingContexts.Clear();
        foreach (var context in _operatingContextService.Contexts)
        {
            _operatingContexts.Add(context);
        }

        SelectedOperatingContext = _operatingContexts.FirstOrDefault(context =>
                                      string.Equals(context.ContextKey, _operatingContextService.CurrentContext?.ContextKey, StringComparison.OrdinalIgnoreCase))
                                  ?? _operatingContexts.FirstOrDefault(context =>
                                      string.Equals(context.ContextKey, _operatingContextService.LastSelectedOperatingContextKey, StringComparison.OrdinalIgnoreCase))
                                  ?? _operatingContexts.FirstOrDefault();

        EnterFundCommand.NotifyCanExecuteChanged();
        return Task.CompletedTask;
    }
}
