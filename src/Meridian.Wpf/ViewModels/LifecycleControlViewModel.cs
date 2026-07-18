using Meridian.Contracts.Lifecycle;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class LifecycleControlViewModel : BindableBase
{
    private static readonly Brush InfoBrush = CreateBrush(0x2F, 0x6F, 0x8F);
    private static readonly Brush SuccessBrush = CreateBrush(0x16, 0x88, 0x5F);
    private static readonly Brush WarningBrush = CreateBrush(0xC2, 0x75, 0x20);
    private static readonly Brush ErrorBrush = CreateBrush(0xBA, 0x3F, 0x55);
    private readonly ILifecycleControlClient _client;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private RuntimeLifecycleSnapshotDto? _snapshot;
    private LifecycleShutdownReason? _pendingReason;
    private string _readinessText = "Loading";
    private string _stateText = "Unknown";
    private string _phaseText = "Waiting for lifecycle evidence";
    private string _uptimeText = "—";
    private string _sessionText = "—";
    private string _acceptingWorkText = "Readiness is being evaluated.";
    private string _latestReceiptText = "No prior shutdown receipt is available.";
    private string _statusText = string.Empty;
    private Brush _statusBrush = InfoBrush;
    private Visibility _statusVisibility = Visibility.Collapsed;
    private Visibility _confirmationVisibility = Visibility.Collapsed;
    private string _confirmationTitle = string.Empty;
    private string _confirmationDetail = string.Empty;
    private string _confirmActionLabel = string.Empty;
    private bool _isBusy;

    public LifecycleControlViewModel(ILifecycleControlClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        BeginRestartCommand = new RelayCommand(
            () => BeginAction(LifecycleShutdownReason.Restart),
            CanRequestLifecycleAction);
        BeginShutdownCommand = new RelayCommand(
            () => BeginAction(LifecycleShutdownReason.Operator),
            CanRequestLifecycleAction);
        ConfirmActionCommand = new AsyncRelayCommand(ConfirmActionAsync, CanConfirmAction);
        CancelActionCommand = new RelayCommand(CancelAction, () => !IsBusy && _pendingReason.HasValue);
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand BeginRestartCommand { get; }

    public IRelayCommand BeginShutdownCommand { get; }

    public IAsyncRelayCommand ConfirmActionCommand { get; }

    public IRelayCommand CancelActionCommand { get; }

    public ObservableCollection<LifecycleCheckRowViewModel> Checks { get; } = [];

    public string ReadinessText
    {
        get => _readinessText;
        private set => SetProperty(ref _readinessText, value);
    }

    public string StateText
    {
        get => _stateText;
        private set => SetProperty(ref _stateText, value);
    }

    public string PhaseText
    {
        get => _phaseText;
        private set => SetProperty(ref _phaseText, value);
    }

    public string UptimeText
    {
        get => _uptimeText;
        private set => SetProperty(ref _uptimeText, value);
    }

    public string SessionText
    {
        get => _sessionText;
        private set => SetProperty(ref _sessionText, value);
    }

    public string AcceptingWorkText
    {
        get => _acceptingWorkText;
        private set => SetProperty(ref _acceptingWorkText, value);
    }

    public string LatestReceiptText
    {
        get => _latestReceiptText;
        private set => SetProperty(ref _latestReceiptText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetProperty(ref _statusBrush, value);
    }

    public Visibility StatusVisibility
    {
        get => _statusVisibility;
        private set => SetProperty(ref _statusVisibility, value);
    }

    public Visibility ConfirmationVisibility
    {
        get => _confirmationVisibility;
        private set => SetProperty(ref _confirmationVisibility, value);
    }

    public string ConfirmationTitle
    {
        get => _confirmationTitle;
        private set => SetProperty(ref _confirmationTitle, value);
    }

    public string ConfirmationDetail
    {
        get => _confirmationDetail;
        private set => SetProperty(ref _confirmationDetail, value);
    }

    public string ConfirmActionLabel
    {
        get => _confirmActionLabel;
        private set => SetProperty(ref _confirmActionLabel, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            RefreshCommand.NotifyCanExecuteChanged();
            BeginRestartCommand.NotifyCanExecuteChanged();
            BeginShutdownCommand.NotifyCanExecuteChanged();
            ConfirmActionCommand.NotifyCanExecuteChanged();
            CancelActionCommand.NotifyCanExecuteChanged();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
            return;

        IsBusy = true;
        try
        {
            var snapshotTask = _client.GetSnapshotAsync(cancellationToken);
            var receiptTask = _client.GetLatestReceiptAsync(cancellationToken);
            await Task.WhenAll(snapshotTask, receiptTask).ConfigureAwait(true);
            var snapshot = await snapshotTask.ConfigureAwait(true);
            if (snapshot is null)
            {
                ApplyUnavailable("The Meridian host did not return lifecycle status.");
                return;
            }

            ApplySnapshot(snapshot);
            ApplyReceipt(await receiptTask.ConfigureAwait(true));
            SetStatus("Lifecycle evidence refreshed.", SuccessBrush);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ApplyUnavailable($"Lifecycle status is unavailable: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _refreshGate.Release();
        }
    }

    private bool CanRequestLifecycleAction()
        => !IsBusy &&
           _pendingReason is null &&
           _snapshot is { AcceptingWork: true, ShutdownRequested: false };

    private void BeginAction(LifecycleShutdownReason reason)
    {
        if (!CanRequestLifecycleAction())
            return;

        _pendingReason = reason;
        ConfirmationTitle = reason == LifecycleShutdownReason.Restart
            ? "Restart Meridian?"
            : "Shut down Meridian?";
        ConfirmationDetail = reason == LifecycleShutdownReason.Restart
            ? "New work will stop, active work will drain, the dedicated database will stop, and the supervisor will start a new session."
            : "New work will stop, active work will drain, and the supervisor will stop the host and its dedicated database.";
        ConfirmActionLabel = reason == LifecycleShutdownReason.Restart
            ? "Confirm restart"
            : "Confirm shutdown";
        ConfirmationVisibility = Visibility.Visible;
        NotifyCommandState();
    }

    private bool CanConfirmAction() => !IsBusy && _pendingReason.HasValue;

    private async Task ConfirmActionAsync(CancellationToken cancellationToken)
    {
        if (_pendingReason is not { } reason)
            return;

        IsBusy = true;
        try
        {
            var accepted = await _client.RequestShutdownAsync(
                new LifecycleShutdownRequestDto
                {
                    Reason = reason,
                    Detail = reason == LifecycleShutdownReason.Restart
                        ? "Restart requested from the WPF lifecycle control page."
                        : "Shutdown requested from the WPF lifecycle control page.",
                    RequestedBy = "wpf-workstation"
                },
                cancellationToken).ConfigureAwait(true);

            if (accepted is null || !accepted.Accepted)
            {
                SetStatus("The host did not accept the lifecycle request.", ErrorBrush);
                return;
            }

            _snapshot = _snapshot is null
                ? null
                : _snapshot with { ShutdownRequested = true, State = accepted.State };
            StateText = accepted.State.ToString();
            AcceptingWorkText = "The host is draining and no longer accepts new work.";
            SetStatus(
                $"{(reason == LifecycleShutdownReason.Restart ? "Restart" : "Shutdown")} accepted. Operation {ShortIdentifier(accepted.OperationId)} is supervised.",
                WarningBrush);
            CancelAction();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetStatus($"Lifecycle request failed: {ex.Message}", ErrorBrush);
        }
        finally
        {
            IsBusy = false;
            NotifyCommandState();
        }
    }

    private void CancelAction()
    {
        _pendingReason = null;
        ConfirmationVisibility = Visibility.Collapsed;
        ConfirmationTitle = string.Empty;
        ConfirmationDetail = string.Empty;
        ConfirmActionLabel = string.Empty;
        NotifyCommandState();
    }

    private void ApplySnapshot(RuntimeLifecycleSnapshotDto snapshot)
    {
        _snapshot = snapshot;
        ReadinessText = snapshot.Readiness.ToString();
        StateText = snapshot.State.ToString();
        PhaseText = snapshot.ActivePhase;
        UptimeText = FormatUptime(snapshot.UptimeSeconds);
        SessionText = ShortIdentifier(snapshot.SessionId);
        AcceptingWorkText = snapshot.AcceptingWork
            ? "The host is accepting operator work."
            : "The host is not accepting new operator work.";
        Checks.Clear();
        foreach (var check in snapshot.Checks
                     .OrderBy(CheckRank)
                     .ThenBy(check => check.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            Checks.Add(new LifecycleCheckRowViewModel(
                check.DisplayName,
                check.Status.ToString(),
                check.Requirement.ToString(),
                check.Message));
        }

        NotifyCommandState();
    }

    private void ApplyReceipt(LifecycleShutdownReceiptDto? receipt)
    {
        LatestReceiptText = receipt is null
            ? "No prior shutdown receipt is available."
            : $"{receipt.Outcome} · {receipt.Reason} · {receipt.CompletedAtUtc.ToLocalTime():g} · forced: {(receipt.ForcedTermination ? "yes" : "no")}";
    }

    private void ApplyUnavailable(string message)
    {
        _snapshot = null;
        ReadinessText = "Unavailable";
        StateText = "Unknown";
        PhaseText = "Lifecycle evidence unavailable";
        UptimeText = "—";
        SessionText = "—";
        AcceptingWorkText = "Do not assume the host is ready while lifecycle evidence is unavailable.";
        Checks.Clear();
        SetStatus(message, ErrorBrush);
        NotifyCommandState();
    }

    private void SetStatus(string message, Brush brush)
    {
        StatusText = message;
        StatusBrush = brush;
        StatusVisibility = Visibility.Visible;
    }

    private void NotifyCommandState()
    {
        BeginRestartCommand.NotifyCanExecuteChanged();
        BeginShutdownCommand.NotifyCanExecuteChanged();
        ConfirmActionCommand.NotifyCanExecuteChanged();
        CancelActionCommand.NotifyCanExecuteChanged();
    }

    private static int CheckRank(RuntimeLifecycleCheckDto check) => check.Status switch
    {
        LifecycleCheckStatus.Failing => 0,
        LifecycleCheckStatus.Degraded => 1,
        LifecycleCheckStatus.Pending => 2,
        _ => 3
    };

    private static string FormatUptime(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{duration.Minutes}m {duration.Seconds}s";
    }

    private static string ShortIdentifier(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "—"
            : value.Length > 12
                ? $"{value[..12]}…"
                : value;

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

public sealed record LifecycleCheckRowViewModel(
    string DisplayName,
    string Status,
    string Requirement,
    string Message);
