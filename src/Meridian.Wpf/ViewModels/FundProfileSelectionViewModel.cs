using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class FundProfileSelectionViewModel : BindableBase
{
    private readonly FundContextService _fundContextService;
    private readonly ObservableCollection<FundProfileDetail> _profiles = [];
    private FundProfileDetail? _selectedProfile;
    private string _newProfileName = string.Empty;
    private string _newLegalEntityName = string.Empty;
    private string _newBaseCurrency = "USD";
    private bool _manageProfilesVisible;
    private string _statusText = "Choose a fund context before entering the workstation.";

    public FundProfileSelectionViewModel(FundContextService fundContextService)
    {
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        Profiles = new ReadOnlyObservableCollection<FundProfileDetail>(_profiles);

        EnterFundCommand = new AsyncRelayCommand(EnterFundAsync, CanEnterFund);
        ToggleManageProfilesCommand = new RelayCommand(() => ManageProfilesVisible = !ManageProfilesVisible);
        CreateProfileCommand = new AsyncRelayCommand(CreateProfileAsync);
        DeleteSelectedProfileCommand = new AsyncRelayCommand(DeleteSelectedProfileAsync, () => SelectedProfile is not null);
        SeedProfilesCommand = new AsyncRelayCommand(SeedProfilesAsync);
    }

    public ReadOnlyObservableCollection<FundProfileDetail> Profiles { get; }

    public IAsyncRelayCommand EnterFundCommand { get; }

    public IRelayCommand ToggleManageProfilesCommand { get; }

    public IAsyncRelayCommand CreateProfileCommand { get; }

    public IAsyncRelayCommand DeleteSelectedProfileCommand { get; }

    public IAsyncRelayCommand SeedProfilesCommand { get; }

    public FundProfileDetail? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                RaisePropertyChanged(nameof(SelectedProfileLastOpenedText));
                EnterFundCommand.NotifyCanExecuteChanged();
                DeleteSelectedProfileCommand.NotifyCanExecuteChanged();
            }
        }
    }

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
        await _fundContextService.LoadAsync(ct);
        await RefreshProfilesAsync(ct);
        ManageProfilesVisible = _profiles.Count == 0;

        StatusText = _profiles.Count == 0
            ? "No fund profiles exist yet. Create one or seed sample profiles to continue."
            : "Choose a fund context before entering the workstation.";
    }

    private bool CanEnterFund() => SelectedProfile is not null;

    private async Task EnterFundAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await _fundContextService.SelectFundProfileAsync(SelectedProfile.FundProfileId);
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
        await RefreshProfilesAsync();

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
        await RefreshProfilesAsync();
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

        await RefreshProfilesAsync();
        StatusText = "Seeded sample fund profiles.";
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

        EnterFundCommand.NotifyCanExecuteChanged();
        DeleteSelectedProfileCommand.NotifyCanExecuteChanged();
        return Task.CompletedTask;
    }
}
