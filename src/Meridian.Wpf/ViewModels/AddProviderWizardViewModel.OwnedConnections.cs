using ProviderConnectionDto = Meridian.Contracts.Api.ProviderConnectionDto;

namespace Meridian.Wpf.ViewModels;

public sealed partial class AddProviderWizardViewModel
{
    private ProviderConnectionDto? _selectedOwnedConnection;
    private string _ownedConnectionStatusText = "Select an existing owned connection. Creation and ownership migration are managed separately.";
    private bool _canUseOwnedCredentials;
    private bool _removeCredentialsConfirmed;
    public ObservableCollection<ProviderConnectionDto> OwnedConnections { get; } = [];
    public ProviderConnectionDto? SelectedOwnedConnection { get => _selectedOwnedConnection; set => SetProperty(ref _selectedOwnedConnection, value); }
    public string OwnedConnectionStatusText { get => _ownedConnectionStatusText; set => SetProperty(ref _ownedConnectionStatusText, value); }
    public bool CanUseOwnedCredentials
    {
        get => _canUseOwnedCredentials;
        set { if (SetProperty(ref _canUseOwnedCredentials, value)) RaisePropertyChanged(nameof(CanRemoveOwnedCredentials)); }
    }
    public bool RemoveCredentialsConfirmed
    {
        get => _removeCredentialsConfirmed;
        set { if (SetProperty(ref _removeCredentialsConfirmed, value)) RaisePropertyChanged(nameof(CanRemoveOwnedCredentials)); }
    }
    public bool CanRemoveOwnedCredentials => CanUseOwnedCredentials && RemoveCredentialsConfirmed;
}
