using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;
using Meridian.Ui.Services.ProviderDiagnostics;

namespace Meridian.Wpf.ViewModels;

/// <summary>Displays provider activation evidence and confirms a supervised host restart.</summary>
public sealed class ProviderActivationViewModel : BindableBase
{
    private readonly ProviderConfigurationApiClient _client;
    private readonly Func<CancellationToken, Task> _afterRestart;
    private ProviderActivationResponse? _snapshot;
    private int _requestVersion;
    private bool _isBusy;
    private bool _isConfirming;
    private string _message = "Refresh to read provider activation from the host.";

    public ProviderActivationViewModel(ProviderConfigurationApiClient client, Func<CancellationToken, Task> afterRestart)
    {
        _client = client;
        _afterRestart = afterRestart;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && !IsConfirming);
        RequestRestartCommand = new RelayCommand(() => IsConfirming = true, () => CanRestart && !IsBusy && !IsConfirming);
        ConfirmRestartCommand = new AsyncRelayCommand(ConfirmRestartAsync, () => CanRestart && IsConfirming && !IsBusy);
        CancelRestartCommand = new RelayCommand(() => IsConfirming = false, () => IsConfirming && !IsBusy);
    }

    public event EventHandler? StatusChanged;
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand RequestRestartCommand { get; }
    public IAsyncRelayCommand ConfirmRestartCommand { get; }
    public IRelayCommand CancelRestartCommand { get; }

    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool CanRestart => _snapshot is { CanRestart: true, IsReady: true, PendingPaths.Count: > 0 };
    public IReadOnlyList<string> PendingPaths => _snapshot?.PendingPaths ?? [];

    public bool IsConfirming
    {
        get => _isConfirming;
        private set
        {
            if (SetProperty(ref _isConfirming, value))
                NotifyCommands();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                NotifyCommands();
        }
    }

    public static string DescribeState(ProviderActivationEntry? entry) => entry?.State switch
    {
        ProviderActivationStates.Active => "Active",
        ProviderActivationStates.SavedPending => "Saved, pending",
        ProviderActivationStates.Disabled => "Disabled",
        ProviderActivationStates.FailedToStart => "Failed to start",
        _ => "Unavailable"
    };

    public static string DescribeDetail(ProviderActivationEntry? entry)
    {
        var activeSince = entry?.ActiveSinceUtc is { } time ? $"Active since {FormatHelpers.FormatDisplayTimestamp(time)}." : null;
        return (entry?.Reason, activeSince) switch
        {
            ({ } reason, { } since) => $"{reason} {since}",
            ({ } reason, null) => reason,
            (null, { } since) => since,
            _ => "The host has no activation evidence for this provider."
        };
    }

    public ProviderActivationEntry? Find(string providerId, string surface)
        => _snapshot?.Providers.FirstOrDefault(entry => entry.Surface == surface
            && string.Equals(entry.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (ConfirmRestartCommand.IsRunning)
            return;

        var request = ++_requestVersion;
        IsBusy = true;
        IsConfirming = false;
        try
        {
            var status = await _client.GetActivationAsync(ct);
            if (request == _requestVersion)
                Apply(status);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (InvalidOperationException ex)
        {
            if (request == _requestVersion)
            {
                Apply(null);
                Message = $"Provider activation unavailable. {ex.Message}";
            }
        }
        finally
        {
            if (request == _requestVersion)
                IsBusy = false;
        }
    }

    public void CancelPending()
    {
        _requestVersion++;
        RefreshCommand.Cancel();
        ConfirmRestartCommand.Cancel();
        IsConfirming = false;
        IsBusy = false;
        Apply(null);
    }

    private async Task ConfirmRestartAsync(CancellationToken ct)
    {
        var reviewed = _snapshot;
        if (reviewed is null || !CanRestart || !IsConfirming)
            return;

        var request = ++_requestVersion;
        IsBusy = true;
        IsConfirming = false;
        try
        {
            var current = await _client.GetActivationAsync(ct);
            if (!current.CanRestart || !current.IsReady || current.RuntimeSessionId != reviewed.RuntimeSessionId
                || !current.PendingPaths.SequenceEqual(reviewed.PendingPaths, StringComparer.Ordinal))
            {
                Apply(current);
                Message = "The host status changed. Review the current pending paths before restarting.";
                return;
            }

            await _client.RestartAsync(ct);
            Apply(null);
            Message = "Restart requested. Waiting for the new host session to become ready.";

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                await WaitForReadinessAsync(reviewed.RuntimeSessionId, deadline.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Message = "The new host session has yet to report readiness. Refresh after the host comes back.";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (InvalidOperationException ex)
        {
            Message = $"Host restart could not be verified. {ex.Message}";
        }
        finally
        {
            if (request == _requestVersion)
                IsBusy = false;
        }
    }

    private async Task WaitForReadinessAsync(string? previousSession, CancellationToken ct)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            ProviderActivationResponse current;
            try
            {
                current = await _client.GetActivationAsync(ct);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (!current.IsReady || string.IsNullOrWhiteSpace(current.RuntimeSessionId) || current.RuntimeSessionId == previousSession)
                continue;

            Apply(current);
            await _afterRestart(ct);
            return;
        }
    }

    private void Apply(ProviderActivationResponse? status)
    {
        _snapshot = status;
        Message = status is null ? "Provider activation unavailable. Refresh to read the current host status."
            : status.PendingPaths.Count > 0 ? status.RestartDetail ?? "Saved provider changes await a host restart."
            : "The host reports no provider settings awaiting restart.";
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(PendingPaths));
        NotifyCommands();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        RequestRestartCommand.NotifyCanExecuteChanged();
        ConfirmRestartCommand.NotifyCanExecuteChanged();
        CancelRestartCommand.NotifyCanExecuteChanged();
    }
}
