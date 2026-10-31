using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.DataIntegration.Credentials;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels.Settings;

/// <summary>
/// Desktop parity for the browser ownership administration panel: create an owned connection, preview a
/// provider-wide record's non-secret vault status, migrate it to the selected owned connection, roll that
/// exact migration back, and bind the host runtime credential. Tenant and actor are server-resolved and
/// no secret is read or sent. A migration keeps its client-generated identity so retry and rollback
/// address the same immutable receipt.
/// </summary>
public sealed class ProviderOwnershipAdministrationViewModel : BindableBase
{
    private readonly IWorkstationProviderOwnershipApiClient _client;
    private long _generation;
    private CancellationTokenSource? _operation;
    private bool _busy;
    private string _status = "Enter a provider id and load its owned connections.";
    private string _providerId = string.Empty;
    private ProviderConnectionDto? _selectedConnection;
    private string _newConnectionId = string.Empty;
    private string _newDisplayName = string.Empty;
    private string _newExternalAccountId = string.Empty;
    private string _newEnvironment = "paper";
    private ProviderCredentialStoreStatus? _preview;
    private ProviderCredentialMigrationReceipt? _receipt;
    private string _migrationId = Guid.NewGuid().ToString("N");

    public ProviderOwnershipAdministrationViewModel(IWorkstationProviderOwnershipApiClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        LoadConnectionsCommand = new AsyncRelayCommand(LoadConnectionsAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ProviderId));
        CreateConnectionCommand = new AsyncRelayCommand(CreateConnectionAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ProviderId));
        PreviewMigrationCommand = new AsyncRelayCommand(PreviewMigrationAsync, () => !IsBusy && SelectedConnection is not null);
        MigrateCommand = new AsyncRelayCommand(MigrateAsync, () => !IsBusy && SelectedConnection is not null && ExpectedGeneration is not null);
        RollbackCommand = new AsyncRelayCommand(RollbackAsync, () => !IsBusy && SelectedConnection is not null && _receipt is { RolledBack: false });
        BindCommand = new AsyncRelayCommand(BindAsync, () => !IsBusy && SelectedConnection is not null);
    }

    public ObservableCollection<ProviderConnectionDto> OwnedConnections { get; } = new();
    public IAsyncRelayCommand LoadConnectionsCommand { get; }
    public IAsyncRelayCommand CreateConnectionCommand { get; }
    public IAsyncRelayCommand PreviewMigrationCommand { get; }
    public IAsyncRelayCommand MigrateCommand { get; }
    public IAsyncRelayCommand RollbackCommand { get; }
    public IAsyncRelayCommand BindCommand { get; }

    public string ProviderId
    {
        get => _providerId;
        set
        {
            if (!SetProperty(ref _providerId, value)) return;
            OwnedConnections.Clear();
            _selectedConnection = null;
            RaisePropertyChanged(nameof(SelectedConnection));
            Reset("Load owned connections for the provider.");
        }
    }

    public ProviderConnectionDto? SelectedConnection
    {
        get => _selectedConnection;
        set { if (SetProperty(ref _selectedConnection, value)) Reset(value is null ? "Select an owned connection." : "Preview the retained vault status before migrating."); }
    }

    public string NewConnectionId { get => _newConnectionId; set => SetProperty(ref _newConnectionId, value); }
    public string NewDisplayName { get => _newDisplayName; set => SetProperty(ref _newDisplayName, value); }
    public string NewExternalAccountId { get => _newExternalAccountId; set => SetProperty(ref _newExternalAccountId, value); }
    public string NewEnvironment { get => _newEnvironment; set => SetProperty(ref _newEnvironment, value); }

    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) NotifyCommands(); } }
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>The previewed credential generation a migrate command must echo; null when the preview carries none.</summary>
    public string? ExpectedGeneration
        => _preview?.AuditMetadata is { } metadata && metadata.TryGetValue("credentialGeneration", out var generation) && !string.IsNullOrWhiteSpace(generation)
            ? generation
            : null;

    public ProviderCredentialMigrationReceipt? Receipt => _receipt;

    public string PreviewText => _preview is { } preview
        ? $"{preview.CredentialState} · {preview.CredentialSource} · environment {preview.Environment ?? "Unspecified"} · generation {ExpectedGeneration ?? "None"}"
        : string.Empty;

    public string ReceiptText => _receipt is { } receipt
        ? $"Receipt {receipt.MigrationId} · {(receipt.RolledBack ? "rolled back" : "migrated")} · generation {receipt.CredentialGeneration} · {receipt.Scope.ConnectionId} · {receipt.Actor}"
        : string.Empty;

    private async Task LoadConnectionsAsync()
    {
        var generation = Begin("Loading owned connections…");
        try
        {
            var response = await _client.GetConnectionsAsync(_operation!.Token).ConfigureAwait(true);
            if (generation != _generation) return;
            if (!response.Success || response.Data is null) { StatusText = Refused("Owned connections are unavailable", response); return; }
            var family = ProviderCredentialCatalog.NormalizeProviderId(ProviderId);
            var owned = response.Data
                .Where(row => string.Equals(ProviderCredentialCatalog.NormalizeProviderId(row.ProviderFamilyId), family, StringComparison.Ordinal)
                    && row.Enabled && !string.IsNullOrWhiteSpace(row.ConnectionId) && !string.IsNullOrWhiteSpace(row.ExternalAccountId))
                .GroupBy(row => row.ConnectionId, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() == 1)
                .Select(group => group.First())
                .ToArray();
            OwnedConnections.Clear();
            foreach (var row in owned) OwnedConnections.Add(row);
            SelectedConnection = null;
            StatusText = owned.Length == 0 ? "No complete owned connection exists for this provider. Create one below." : "Select an owned connection.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _generation) StatusText = $"Owned connections are unavailable: {ex.Message}"; }
        finally { End(generation); }
    }

    private async Task CreateConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewConnectionId) || string.IsNullOrWhiteSpace(NewDisplayName) || string.IsNullOrWhiteSpace(NewExternalAccountId) || string.IsNullOrWhiteSpace(NewEnvironment))
        {
            StatusText = "Enter the connection id, display name, the provider's actual account identity and the credential environment.";
            return;
        }
        var generation = Begin("Creating owned connection…");
        try
        {
            var request = new CreateOwnedProviderConnectionRequest(
                new CreateProviderConnectionRequest(NewConnectionId.Trim(), ProviderId.Trim(), NewDisplayName.Trim(), ExternalAccountId: NewExternalAccountId.Trim()),
                NewEnvironment.Trim());
            var response = await _client.CreateOwnedConnectionAsync(request, _operation!.Token).ConfigureAwait(true);
            if (generation != _generation) return;
            if (!response.Success || response.Data is null) { StatusText = Refused("The owned connection was not created", response); return; }
            NewConnectionId = string.Empty;
            NewDisplayName = string.Empty;
            NewExternalAccountId = string.Empty;
            StatusText = $"Owned connection {response.Data.ConnectionId} created for the authenticated tenant. Load owned connections to select it.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _generation) StatusText = $"The owned connection was not created: {ex.Message}"; }
        finally { End(generation); }
    }

    private async Task PreviewMigrationAsync()
    {
        if (SelectedConnection is not { } connection) return;
        var generation = Begin("Previewing retained vault status…");
        try
        {
            var response = await _client.PreviewMigrationAsync(connection.ConnectionId, ProviderId.Trim(), _operation!.Token).ConfigureAwait(true);
            if (generation != _generation) return;
            if (!response.Success || response.Data is null) { StatusText = Refused("Migration preview is unavailable", response); return; }
            _preview = response.Data;
            _receipt = null;
            RaiseResultChanged();
            StatusText = ExpectedGeneration is null
                ? "No retained provider-wide record carries a credential generation; nothing to migrate."
                : $"Retained record found; generation {ExpectedGeneration} can be migrated to {connection.ConnectionId}.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _generation) StatusText = $"Migration preview is unavailable: {ex.Message}"; }
        finally { End(generation); }
    }

    private async Task MigrateAsync()
    {
        if (SelectedConnection is not { } connection || ExpectedGeneration is not { } expected) return;
        var generation = Begin("Migrating credential ownership…");
        try
        {
            var request = new ProviderCredentialMigrationRequest(ProviderId.Trim(), _migrationId, expected);
            var response = await _client.MigrateAsync(connection.ConnectionId, request, _operation!.Token).ConfigureAwait(true);
            if (generation != _generation) return;
            if (!response.Success || response.Data is null) { StatusText = Refused("The migration was refused", response); return; }
            if (!string.Equals(response.Data.Scope.ConnectionId, connection.ConnectionId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The receipt names another connection.");
            _receipt = response.Data;
            RaiseResultChanged();
            StatusText = $"Migration {response.Data.MigrationId} retained by {response.Data.Actor} at generation {response.Data.CredentialGeneration}. The record is unverified under its new ownership; re-verify before use. Running instances keep their current credentials until restarted.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _generation) StatusText = $"The migration was refused: {ex.Message}"; }
        finally { End(generation); }
    }

    private async Task RollbackAsync()
    {
        if (SelectedConnection is not { } connection || _receipt is not { RolledBack: false } receipt) return;
        var generation = Begin("Rolling back migration…");
        try
        {
            var request = new ProviderCredentialMigrationRequest(ProviderId.Trim(), receipt.MigrationId);
            var response = await _client.RollbackAsync(connection.ConnectionId, request, _operation!.Token).ConfigureAwait(true);
            if (generation != _generation) return;
            if (!response.Success || response.Data is null) { StatusText = Refused("The rollback was refused", response); return; }
            _receipt = response.Data;
            _migrationId = Guid.NewGuid().ToString("N");
            RaiseResultChanged();
            StatusText = $"Migration {response.Data.MigrationId} rolled back by {response.Data.RolledBackBy ?? "the server"}; the provider-wide record is restored and unverified.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _generation) StatusText = $"The rollback was refused: {ex.Message}"; }
        finally { End(generation); }
    }

    private async Task BindAsync()
    {
        if (SelectedConnection is not { } connection) return;
        var generation = Begin("Binding host runtime credential…");
        try
        {
            var response = await _client.BindRuntimeCredentialAsync(connection.ConnectionId, new BindProviderRuntimeCredentialRequest(ProviderId.Trim()), _operation!.Token).ConfigureAwait(true);
            if (generation != _generation) return;
            if (!response.Success || response.Data is null) { StatusText = Refused("The binding was refused", response); return; }
            if (!string.Equals(response.Data.ConnectionId, connection.ConnectionId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The binding names another connection.");
            StatusText = $"Host runtime credentials for {response.Data.ProviderId} now select {response.Data.ConnectionId} in tenant {response.Data.TenantId}. Running instances keep their current credentials until restarted.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _generation) StatusText = $"The binding was refused: {ex.Message}"; }
        finally { End(generation); }
    }

    private static string Refused<T>(string prefix, ApiResponse<T> response) where T : class
        => response.StatusCode == 403
            ? $"{prefix}: ownership administration requires credential, provider and configuration permissions and a tenant-scoped session."
            : $"{prefix}: {response.ErrorMessage ?? "no response"}.";

    private long Begin(string status)
    {
        _operation?.Cancel();
        _operation = new CancellationTokenSource();
        StatusText = status;
        IsBusy = true;
        return _generation;
    }

    private void End(long generation)
    {
        if (generation != _generation) return;
        _operation = null;
        IsBusy = false;
    }

    private void Reset(string status)
    {
        Interlocked.Increment(ref _generation);
        _operation?.Cancel();
        _operation = null;
        _preview = null;
        _receipt = null;
        _busy = false;
        RaisePropertyChanged(nameof(IsBusy));
        StatusText = status;
        RaiseResultChanged();
    }

    private void RaiseResultChanged()
    {
        RaisePropertyChanged(nameof(PreviewText));
        RaisePropertyChanged(nameof(ReceiptText));
        RaisePropertyChanged(nameof(ExpectedGeneration));
        RaisePropertyChanged(nameof(Receipt));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        LoadConnectionsCommand.NotifyCanExecuteChanged();
        CreateConnectionCommand.NotifyCanExecuteChanged();
        PreviewMigrationCommand.NotifyCanExecuteChanged();
        MigrateCommand.NotifyCanExecuteChanged();
        RollbackCommand.NotifyCanExecuteChanged();
        BindCommand.NotifyCanExecuteChanged();
    }
}
