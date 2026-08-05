using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Read-only release path from source data through the accounting record to report-pack
/// publication, composed from the most recently updated continuity workflow like the browser
/// record-release screen. Step tones come from the workflow's own gates; a blocked step blocks the
/// release summary and an unknown step keeps it out of Ready.
/// </summary>
public sealed class OperationsRecordReleaseViewModel : BindableBase, IDisposable
{
    private readonly IOperationsControlCenterClient? _client;
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;
    private bool _hasLoaded;
    private int _loadRevision;
    private bool _isRefreshing;
    private string _statusText = "Waiting for the continuity workflow.";
    private string _workflowErrorText = string.Empty;
    private string _workflowLabel = "No workflow selected";
    private OperationsRecordReleaseStepModel? _selectedStep;
    private OperationsRecordReleaseSummaryModel _summary =
        OperationsRecordReleaseMapper.BuildSummary(OperationsRecordReleaseMapper.BuildReleaseSteps(null));

    public OperationsRecordReleaseViewModel(IOperationsControlCenterClient? client = null)
    {
        _client = client;
        RefreshCommand = new AsyncRelayCommand(
            () => _isDisposed ? Task.CompletedTask : RefreshAsync(_cts.Token),
            () => !IsRefreshing);
        SelectStepCommand = new RelayCommand<OperationsRecordReleaseStepModel>(
            step => SelectedStep = step ?? SelectedStep);
        OpenPageCommand = new RelayCommand<string>(
            OpenPage,
            static tag => !string.IsNullOrWhiteSpace(tag));
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand<OperationsRecordReleaseStepModel> SelectStepCommand { get; }

    public IRelayCommand<string> OpenPageCommand { get; }

    public ObservableCollection<OperationsRecordReleaseStepModel> Steps { get; } = [];

    public string Title => "Operations record release";

    public string Subtitle => "Follow the release path from source data through the accounting record to the report pack.";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string WorkflowLabel
    {
        get => _workflowLabel;
        private set => SetProperty(ref _workflowLabel, value);
    }

    public OperationsRecordReleaseSummaryModel Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public OperationsRecordReleaseStepModel? SelectedStep
    {
        get => _selectedStep;
        private set => SetProperty(ref _selectedStep, value);
    }

    public string WorkflowErrorText
    {
        get => _workflowErrorText;
        private set
        {
            if (SetProperty(ref _workflowErrorText, value))
            {
                OnPropertyChanged(nameof(HasWorkflowError));
            }
        }
    }

    public bool HasWorkflowError => !string.IsNullOrWhiteSpace(WorkflowErrorText);

    public void Activate()
    {
        if (!_hasLoaded && !IsRefreshing && !_isDisposed)
        {
            _ = RefreshAsync(_cts.Token);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            LoggingService.Instance.LogDebug(
                "Ignored cancel on already-disposed token source.",
                ("view", nameof(OperationsRecordReleaseViewModel)));
        }

        _cts.Dispose();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_isDisposed)
        {
            return;
        }

        var revision = ++_loadRevision;
        IsRefreshing = true;
        StatusText = "Loading the continuity workflow.";
        try
        {
            OperationsContinuityWorkflowDto? detail = null;
            var error = string.Empty;
            if (_client is null)
            {
                error = "Operations continuity client is not available in this session.";
            }
            else
            {
                var workflows = await _client.GetWorkflowsAsync(ct).ConfigureAwait(true);
                if (workflows is null)
                {
                    error = "Continuity workflows failed to load from the shared workstation API.";
                }
                else if (workflows.Count == 0)
                {
                    error = "No operations continuity workflow exists yet; the release path has nothing to follow.";
                }
                else
                {
                    var latest = workflows.OrderByDescending(static workflow => workflow.UpdatedAtUtc).First();
                    detail = await _client.GetWorkflowAsync(latest.WorkflowId, ct).ConfigureAwait(true);
                    if (detail is null)
                    {
                        error = "The latest workflow detail failed to load from the shared workstation API.";
                    }
                }
            }

            if (IsStale(revision))
            {
                return;
            }

            WorkflowErrorText = error;
            WorkflowLabel = detail is null
                ? "No workflow selected"
                : $"{detail.PeriodId} · {detail.BrokerSource} · {SettingsViewModel.FormatIdentifier(detail.Status.ToString())}";

            var steps = OperationsRecordReleaseMapper.BuildReleaseSteps(detail);
            Steps.Clear();
            foreach (var step in steps)
            {
                Steps.Add(step);
            }

            SelectedStep = Steps.FirstOrDefault(step => step.StepId == SelectedStep?.StepId) ?? Steps.FirstOrDefault();
            Summary = OperationsRecordReleaseMapper.BuildSummary(steps);
            StatusText = $"Release path refreshed {OperationsContinuityMapper.FormatTimestamp(DateTimeOffset.UtcNow)}.";
            _hasLoaded = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Navigation or disposal cancelled the in-flight refresh; leave current state as-is.
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Operations record release refresh failed.", ex);
            if (!IsStale(revision))
            {
                StatusText = "Operations record release failed to load.";
                WorkflowErrorText = ex.Message;
            }
        }
        finally
        {
            if (revision == _loadRevision)
            {
                IsRefreshing = false;
            }
        }
    }

    private bool IsStale(int revision) => _isDisposed || revision != _loadRevision;

    private void OpenPage(string? pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return;
        }

        NavigationService.Instance.NavigateTo(pageTag);
    }
}
