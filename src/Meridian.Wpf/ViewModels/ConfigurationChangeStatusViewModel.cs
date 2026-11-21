using Meridian.Contracts.Configuration;

namespace Meridian.Wpf.ViewModels;

/// <summary>Displays the host's shared activation evidence without inferring that a disk save applied.</summary>
public sealed class ConfigurationChangeStatusViewModel : BindableBase
{
    private readonly Func<CancellationToken, Task<ConfigurationChangeStatus?>> _load;
    private int _version;
    private string _title = "Setting activation not checked";
    private string _detail = "Refresh the host's saved-setting status.";
    private string _paths = string.Empty;
    private bool _isBusy;

    public ConfigurationChangeStatusViewModel(Func<CancellationToken, Task<ConfigurationChangeStatus?>> load)
        => _load = load ?? throw new ArgumentNullException(nameof(load));

    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string Detail { get => _detail; private set => SetProperty(ref _detail, value); }
    public string Paths { get => _paths; private set => SetProperty(ref _paths, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public async Task RefreshAsync(bool hasLocalSave, CancellationToken ct = default)
    {
        var version = Interlocked.Increment(ref _version);
        IsBusy = true;
        Title = "Checking setting activation…";
        Detail = "Reading saved versus applied status from the host.";
        Paths = string.Empty;
        try
        {
            var status = await _load(ct);
            ct.ThrowIfCancellationRequested();
            if (version != _version) return;
            if (status is null) throw new InvalidOperationException("Host setting status unavailable.");
            Paths = string.Join(Environment.NewLine, status.PendingChanges.Select(change => $"{change.Path}: {change.Impact}"));
            if (status.RequiresRestart)
            {
                Title = "Pending host restart";
                Detail = "Changes are saved. Restart the host before relying on these settings; reconnecting alone does not rebuild providers in this host.";
            }
            else if (status.RequiresReconnect)
            {
                Title = "Pending provider reconnect";
                Detail = "Changes are saved and await provider reconnection. The host has not confirmed application yet.";
            }
            else
            {
                Title = "No host-reported pending settings";
                Detail = "The host reports no settings awaiting activation.";
            }
            if (hasLocalSave)
            {
                Title = status.PendingChanges.Count > 0 ? Title : "Local save awaits host verification";
                Detail += " Desktop settings were also written directly to disk; this host response does not verify that local save. Restart Meridian before relying on changed provider settings.";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception)
        {
            if (version != _version) return;
            Title = "Setting activation unavailable";
            Detail = hasLocalSave
                ? "Desktop settings are saved locally, but host application is unverified. Restore the host connection and refresh; restart Meridian before relying on changed provider settings."
                : "The host's setting status could not be read. Restore the host connection or permissions and refresh; applied status is unknown.";
            Paths = string.Empty;
        }
        finally { if (version == _version) IsBusy = false; }
    }

    public void CancelPending()
    {
        Interlocked.Increment(ref _version);
        IsBusy = false;
        Title = "Setting activation not checked";
        Detail = "Refresh the host's saved-setting status.";
        Paths = string.Empty;
    }
}
