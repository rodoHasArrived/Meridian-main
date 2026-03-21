using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class EventReplayPage : Page
{
    private readonly EventReplayViewModel _viewModel = new();

    public EventReplayPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Initialize();
    }

    private void StartReplay_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StartReplay();
    }

    private void PauseReplay_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PauseReplay();
    }

    private void StopReplay_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StopReplay();
    }
}

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

    public bool CanStart => SelectedReplay != null && SelectedReplay.Status != "Running";

    public bool CanPause => SelectedReplay != null && SelectedReplay.Status == "Running";

    public bool CanStop => SelectedReplay != null && SelectedReplay.Status != "Stopped";

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
        UpdateStatusFlags();
    }

    public void StartReplay()
    {
        if (SelectedReplay == null)
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
        if (SelectedReplay == null)
        {
            return;
        }

        SelectedReplay.Status = "Paused";
        StatusMessage = $"Replay \"{SelectedReplay.Name}\" paused.";
        UpdateStatusFlags();
    }

    public void StopReplay()
    {
        if (SelectedReplay == null)
        {
            return;
        }

        SelectedReplay.Status = "Stopped";
        StatusMessage = $"Replay \"{SelectedReplay.Name}\" stopped.";
        UpdateStatusFlags();
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
