using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace Meridian.Wpf.ViewModels;

public sealed class EventReplayViewModel : BindableBase, IDataErrorInfo
{
    private EventReplaySession? _selectedReplay;
    private string _selectedSpeed = "1x";
    private string _selectedTarget = "Development";
    private string _filter = string.Empty;
    private string _validationSummary = string.Empty;
    private string _statusMessage = string.Empty;

    public EventReplayViewModel()
    {
        Replays = new ObservableCollection<EventReplaySession>();
        Speeds = new ObservableCollection<string> { "0.5x", "1x", "2x", "5x" };
        Targets = new ObservableCollection<string> { "Development", "Staging", "Production" };
    }

    public ObservableCollection<EventReplaySession> Replays { get; }

    public ObservableCollection<string> Speeds { get; }

    public ObservableCollection<string> Targets { get; }

    public EventReplaySession? SelectedReplay
    {
        get => _selectedReplay;
        set
        {
            if (SetProperty(ref _selectedReplay, value))
            {
                UpdateStatusFlags();
            }
        }
    }

    public string SelectedSpeed
    {
        get => _selectedSpeed;
        set => SetProperty(ref _selectedSpeed, value);
    }

    public string SelectedTarget
    {
        get => _selectedTarget;
        set => SetProperty(ref _selectedTarget, value);
    }

    public string Filter
    {
        get => _filter;
        set => SetProperty(ref _filter, value);
    }

    /// <summary>
    /// Why the replay controls are disabled on this page. The sessions listed here are sample
    /// fixtures, and no replay service is composed into this workstation page, so a Start, Pause,
    /// or Stop that flipped a local status string would be a control that lies: an operator would
    /// read "Replay stopped" while nothing had been asked of any replay engine. The exit criterion
    /// for desk safety controls permits exactly two states -- wired to the real service, or
    /// disabled with an explicit reason -- and this is the second.
    /// </summary>
    public const string NotWiredReason =
        "Replay controls are not wired to the replay service from this page. "
        + "Start, pause, and stop replay sessions from the browser workstation's Trading replay panel.";

    /// <summary>
    /// True once this page drives a real replay session through the shared replay API. Until
    /// then every control below is disabled, and the reason travels with it.
    /// </summary>
    public bool IsReplayControlWired => false;

    public string ControlDisabledReason => IsReplayControlWired ? string.Empty : NotWiredReason;

    public bool CanStart => IsReplayControlWired && SelectedReplay != null && SelectedReplay.Status != "Running";

    public bool CanPause => IsReplayControlWired && SelectedReplay != null && SelectedReplay.Status == "Running";

    public bool CanStop => IsReplayControlWired && SelectedReplay != null && SelectedReplay.Status != "Stopped";

    public string ValidationSummary
    {
        get => _validationSummary;
        private set => SetProperty(ref _validationSummary, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string Error => string.Empty;

    public string this[string columnName] => string.Empty;

    public void Initialize()
    {
        if (Replays.Count == 0)
        {
            Replays.Add(new EventReplaySession("Market Open Replay", "Ready", "Today 08:00"));
            Replays.Add(new EventReplaySession("Latency Incident", "Stopped", "Yesterday 16:42"));
            Replays.Add(new EventReplaySession("Depth Burst", "Running", "Today 09:15"));
        }

        SelectedReplay ??= Replays.FirstOrDefault();
        if (!IsReplayControlWired)
        {
            StatusMessage = NotWiredReason;
        }

        UpdateStatusFlags();
    }

    public void StartReplay()
    {
        if (!TryEnsureReplayControlWired() || SelectedReplay == null)
        {
            return;
        }

        SelectedReplay.Status = "Running";
        SelectedReplay.LastRun = "Just now";
        StatusMessage = $"Replay \"{SelectedReplay.Name}\" started at {SelectedSpeed}.";
        UpdateStatusFlags();
    }

    public void PauseReplay()
    {
        if (!TryEnsureReplayControlWired() || SelectedReplay == null)
        {
            return;
        }

        SelectedReplay.Status = "Paused";
        StatusMessage = $"Replay \"{SelectedReplay.Name}\" paused.";
        UpdateStatusFlags();
    }

    public void StopReplay()
    {
        if (!TryEnsureReplayControlWired() || SelectedReplay == null)
        {
            return;
        }

        SelectedReplay.Status = "Stopped";
        StatusMessage = $"Replay \"{SelectedReplay.Name}\" stopped.";
        UpdateStatusFlags();
    }

    /// <summary>
    /// A command that reaches this method with the controls unwired (a stale binding, a keyboard
    /// accelerator) must not touch session state or write confirmation copy. It reports the
    /// not-wired reason instead, so the page never claims an action it did not perform.
    /// </summary>
    private bool TryEnsureReplayControlWired()
    {
        if (IsReplayControlWired)
        {
            return true;
        }

        StatusMessage = NotWiredReason;
        return false;
    }

    private void UpdateStatusFlags()
    {
        RaisePropertyChanged(nameof(CanStart));
        RaisePropertyChanged(nameof(CanPause));
        RaisePropertyChanged(nameof(CanStop));
    }
}

public sealed class EventReplaySession : BindableBase
{
    private string _status;
    private string _lastRun;

    public EventReplaySession(string name, string status, string lastRun)
    {
        Name = name;
        _status = status;
        _lastRun = lastRun;
    }

    public string Name { get; }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string LastRun
    {
        get => _lastRun;
        set => SetProperty(ref _lastRun, value);
    }
}
