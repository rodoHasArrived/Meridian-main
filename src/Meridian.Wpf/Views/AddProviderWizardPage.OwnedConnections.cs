using System.Threading;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services.Services;
using ProviderConnectionDto = Meridian.Contracts.Api.ProviderConnectionDto;
using ProviderIdentity = Meridian.Infrastructure.Adapters.Core.ProviderIdentity;
using ProviderCatalogEntry = Meridian.Ui.Services.Services.ProviderCatalogEntry;

namespace Meridian.Wpf.Views;

public partial class AddProviderWizardPage
{
    private IReadOnlyList<ProviderConnectionDto> _ownedConnections = [];
    private ProviderConnectionDto? _selectedOwnedConnection;
    private long _credentialScopeGeneration;
    private CancellationTokenSource _credentialScopeCts = new();
    private bool _updatingConnectionSelection;

    private async Task LoadOwnedConnectionsAsync(long? generation = null)
    {
        IReadOnlyList<ProviderConnectionDto> connections;
        try { connections = await _settingsConfigService.GetOwnedCredentialConnectionsAsync(); }
        catch { connections = []; }
        if (!generation.HasValue || IsCurrentCredentialScope(generation.Value)) _ownedConnections = connections;
    }

    private void ResetOwnedConnectionScope()
    {
        Interlocked.Increment(ref _credentialScopeGeneration);
        _credentialScopeCts.Cancel();
        _credentialScopeCts.Dispose();
        _credentialScopeCts = new();
        _operationInProgress = false;
        _selectedOwnedConnection = null;
        _credentialStatuses = [];
        _serviceFields = null;
        ClearCredentialEditors();
        _viewModel.CanUseOwnedCredentials = false;
        _viewModel.RemoveCredentialsConfirmed = false;
        _viewModel.ResetConnectionTest();
        _viewModel.SaveStatusText = string.Empty;
        _updatingConnectionSelection = true;
        try { _viewModel.SelectedOwnedConnection = null; }
        finally { _updatingConnectionSelection = false; }
    }

    private void ClearCredentialEditors()
    {
        _clearingPersistedFields = true;
        try
        {
            foreach (var child in CredentialFieldsPanel.Children)
            {
                if (child is SecretInputControl secret) secret.ClearSecret();
                else if (child is TextBox text) text.Clear();
            }
            CredentialFieldsPanel.Children.Clear();
        }
        finally { _clearingPersistedFields = false; }
    }

    private void ApplyOwnedConnectionChoices()
    {
        _viewModel.OwnedConnections.Clear();
        foreach (var connection in _ownedConnections.Where(connection =>
                     _selectedProvider is not null && ProviderIdentity.EqualsId(connection.ProviderFamilyId, _selectedProvider.Id)))
            _viewModel.OwnedConnections.Add(connection);
        if (_selectedProvider is not null && FindStatus(_catalogCredentialStatuses, _selectedProvider)?.CredentialFields is { Count: 0 })
        {
            _viewModel.OwnedConnectionStatusText = "This provider requires no managed credentials. Connection and runtime setup are managed separately.";
            return;
        }
        _viewModel.OwnedConnectionStatusText = _viewModel.OwnedConnections.Count == 0
            ? "No owned connection is available for this provider. Complete connection ownership onboarding separately; this wizard cannot assign ownership or use provider-wide credentials."
            : "Select an existing owned connection explicitly. Credentials stay scoped to that retained connection.";
    }

    private async void OwnedConnectionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingConnectionSelection || sender is not ComboBox selector) return;
        await ApplyOwnedConnectionSelectionAsync(selector.SelectedItem as ProviderConnectionDto);
    }

    internal Task ApplyOwnedConnectionSelectionAsync(ProviderConnectionDto? selection)
    {
        // Binding notifications can arrive after the synchronous update guard has dropped.
        // Compare with the accepted/pending scope, not the already-updated two-way binding.
        if (SameOwnedCredentialScope(_selectedOwnedConnection, selection)) return Task.CompletedTask;
        return SelectOwnedConnectionAsync(selection?.ConnectionId);
    }

    private static bool SameOwnedCredentialScope(ProviderConnectionDto? current, ProviderConnectionDto? incoming)
        => current is null || incoming is null ? current is null && incoming is null
            : string.Equals(current.ConnectionId, incoming.ConnectionId, StringComparison.OrdinalIgnoreCase)
              && ProviderIdentity.EqualsId(current.ProviderFamilyId, incoming.ProviderFamilyId)
              && string.Equals(current.TenantId, incoming.TenantId, StringComparison.Ordinal)
              && string.Equals(current.ExternalAccountId, incoming.ExternalAccountId, StringComparison.Ordinal)
              && string.Equals(current.CredentialEnvironment, incoming.CredentialEnvironment, StringComparison.Ordinal);

    internal async Task SelectOwnedConnectionAsync(string? connectionId)
    {
        ResetOwnedConnectionScope();
        var provider = _selectedProvider;
        var matches = _ownedConnections.Where(connection => connection.ConnectionId.Equals(connectionId, StringComparison.OrdinalIgnoreCase)
            && provider is not null && ProviderIdentity.EqualsId(connection.ProviderFamilyId, provider.Id)).ToArray();
        if (provider is null || matches.Length != 1)
        {
            _viewModel.OwnedConnectionStatusText = "Select one existing owned connection for this provider. Missing or mismatched selections cannot use credentials.";
            return;
        }
        var connection = matches[0];
        _selectedOwnedConnection = connection;
        _updatingConnectionSelection = true;
        try { _viewModel.SelectedOwnedConnection = connection; }
        finally { _updatingConnectionSelection = false; }
        var generation = _credentialScopeGeneration;
        var ct = _credentialScopeCts.Token;
        _viewModel.OwnedConnectionStatusText = "Checking retained connection ownership and credential status…";
        try
        {
            await ValidateOwnedConnectionAsync(provider, connection, ct);
            if (!IsCurrentCredentialScope(generation)) return;
            var statuses = await _settingsConfigService.GetProviderCredentialStatusesAsync(ct, connection.ConnectionId);
            if (!IsCurrentCredentialScope(generation)) return;
            _credentialStatuses = statuses;
            BuildCredentialFields();
            _viewModel.CanUseOwnedCredentials = _serviceFields is { Count: > 0 };
            _viewModel.OwnedConnectionStatusText = _serviceFields is not null
                ? $"Owned connection {connection.ConnectionId} · {connection.CredentialEnvironment} · account {connection.ExternalAccountId}."
                : "The selected connection's credential schema is unavailable. No provider-wide fallback is permitted.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!IsCurrentCredentialScope(generation)) return;
            _viewModel.CanUseOwnedCredentials = false;
            _viewModel.OwnedConnectionStatusText = "Selected connection ownership or credential status is unavailable. Reload existing connections; no fallback was used.";
        }
    }

    private async void ReloadOwnedConnections_Click(object sender, RoutedEventArgs e)
    {
        ResetOwnedConnectionScope();
        var generation = _credentialScopeGeneration;
        await LoadOwnedConnectionsAsync(generation);
        if (IsCurrentCredentialScope(generation)) ApplyOwnedConnectionChoices();
    }

    private async Task ValidateOwnedConnectionAsync(ProviderCatalogEntry provider, ProviderConnectionDto expected, CancellationToken ct)
    {
        var current = await _settingsConfigService.GetOwnedCredentialConnectionsAsync(ct);
        ct.ThrowIfCancellationRequested();
        var matches = current.Where(connection => connection.ConnectionId.Equals(expected.ConnectionId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || !ProviderIdentity.EqualsId(matches[0].ProviderFamilyId, provider.Id)
            || matches[0].TenantId != expected.TenantId || matches[0].ExternalAccountId != expected.ExternalAccountId
            || matches[0].CredentialEnvironment != expected.CredentialEnvironment)
        {
            ClearCredentialEditors();
            _viewModel.CanUseOwnedCredentials = false;
            _viewModel.OwnedConnectionStatusText = "The selected connection was removed or changed ownership. Reload existing connections; no fallback was used.";
            throw new InvalidOperationException("The selected owned connection was removed, changed scope, or no longer belongs to this provider. Reload existing connections.");
        }
    }

    private bool TryGetOwnedCredentialScope(ProviderCatalogEntry provider, out ProviderConnectionDto? connection)
    {
        connection = _selectedOwnedConnection;
        if (connection is not null && _viewModel.CanUseOwnedCredentials && _serviceFields is not null
            && ProviderIdentity.EqualsId(connection.ProviderFamilyId, provider.Id)) return true;
        _viewModel.SetConnectionTestError("Select an existing owned connection with an available credential schema first.");
        _viewModel.SetSaveError("Credentials were not changed. Missing ownership cannot fall back to provider-wide credentials.");
        return false;
    }

    private bool IsCurrentCredentialScope(long generation) => generation == Volatile.Read(ref _credentialScopeGeneration);

    private async void RemoveOwnedCredentials_Click(object sender, RoutedEventArgs e) => await RemoveOwnedCredentialsAsync();
    internal async Task RemoveOwnedCredentialsAsync()
    {
        if (_selectedProvider is not { } provider || _operationInProgress || !_viewModel.RemoveCredentialsConfirmed
            || !TryGetOwnedCredentialScope(provider, out var connection)) return;
        var generation = _credentialScopeGeneration;
        var ct = _credentialScopeCts.Token;
        _operationInProgress = true;
        try
        {
            await ValidateOwnedConnectionAsync(provider, connection!, ct);
            if (!IsCurrentCredentialScope(generation)) return;
            await _settingsConfigService.RemoveProviderCredentialsAsync(provider.Id, connection!.ConnectionId, ct);
            if (!IsCurrentCredentialScope(generation)) return;
            await SelectOwnedConnectionAsync(connection.ConnectionId);
            if (_credentialScopeGeneration != generation + 1) return;
            _viewModel.SaveStatusText = $"Credentials removed from selected connection {connection.ConnectionId}.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { if (IsCurrentCredentialScope(generation)) _viewModel.SetSaveError(ex.Message); }
        finally { if (IsCurrentCredentialScope(generation)) _operationInProgress = false; }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e) => ResetOwnedConnectionScope();
}
