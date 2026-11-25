using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Services.SecurityMaster;

namespace Meridian.Wpf.ViewModels.SecurityMaster;

/// <summary>One exception case with the server's SLA verdict and age.</summary>
public sealed record SecurityMasterExceptionCaseRow(
    Guid CaseId,
    Guid? SecurityId,
    string Category,
    string RuleCode,
    string Severity,
    string Title,
    string Detail,
    string Status,
    string Assignee,
    string Sla,
    bool SlaBreached,
    string Age,
    bool IsOpen)
{
    public static SecurityMasterExceptionCaseRow From(SecurityMasterExceptionCaseQueueItemDto item) => new(
        item.Row.CaseId,
        item.Row.SecurityId,
        item.Row.Category,
        item.Row.RuleCode,
        item.Row.Severity,
        item.Row.Title,
        item.Row.Detail,
        item.Row.Status,
        string.IsNullOrWhiteSpace(item.Row.Assignee) ? "Unassigned" : item.Row.Assignee,
        SecurityMasterReviewQueuePresentation.DescribeSla(item),
        item.SlaBreached,
        SecurityMasterReviewQueuePresentation.FormatAge(item.AgeHours),
        string.Equals(item.Row.Status, SecurityMasterExceptionCaseStatusDto.Open, StringComparison.Ordinal));

    public bool HasSecurity => SecurityId is { } id && id != Guid.Empty;
}

/// <summary>
/// The deployment-global Security Master exception queue (review D2): conflicts, identifier ambiguities
/// and data-quality violations with SLA due dates and assignment. Sync asks the server to reconcile open
/// cases with current conditions; the SLA verdict and age are the server's, never recomputed here.
/// </summary>
public sealed class SecurityMasterExceptionQueueViewModel : BindableBase, IDisposable
{
    private readonly ISecurityMasterReviewQueueApiClient _client;
    private readonly Func<Guid, Task>? _openSecurity;
    private CancellationTokenSource? _load;
    private bool _isBusy;
    private bool _isLoaded;
    private string _statusFilter = SecurityMasterExceptionCaseStatusDto.Open;
    private string _assignee;
    private SecurityMasterExceptionCaseRow? _selectedCase;
    private string _statusText = "Load the queue to see open Security Master exceptions.";
    private string? _errorText;

    public SecurityMasterExceptionQueueViewModel(
        ISecurityMasterReviewQueueApiClient client,
        Func<Guid, Task>? openSecurity = null,
        string? defaultAssignee = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _openSecurity = openSecurity;
        _assignee = defaultAssignee?.Trim() ?? string.Empty;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        SyncCommand = new AsyncRelayCommand(SyncAsync, () => !IsBusy);
        AssignCommand = new AsyncRelayCommand(AssignAsync, CanAssign);
        OpenSecurityCommand = new AsyncRelayCommand<SecurityMasterExceptionCaseRow>(
            row => row?.SecurityId is { } id && _openSecurity is not null ? _openSecurity(id) : Task.CompletedTask,
            row => row is { HasSecurity: true } && _openSecurity is not null);
    }

    public IReadOnlyList<string> StatusFilters { get; } = [SecurityMasterExceptionCaseStatusDto.Open, SecurityMasterExceptionCaseStatusDto.Closed];
    public ObservableCollection<SecurityMasterExceptionCaseRow> Rows { get; } = new();
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand SyncCommand { get; }
    public IAsyncRelayCommand AssignCommand { get; }
    public IAsyncRelayCommand<SecurityMasterExceptionCaseRow> OpenSecurityCommand { get; }

    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (!SetProperty(ref _statusFilter, string.IsNullOrWhiteSpace(value) ? SecurityMasterExceptionCaseStatusDto.Open : value))
            {
                return;
            }

            _load?.Cancel();
            Rows.Clear();
            SelectedCase = null;
            IsLoaded = false;
            RaisePropertyChanged(nameof(BreachedCount));
            StatusText = $"Load the queue to see {_statusFilter.ToLowerInvariant()} cases.";
        }
    }

    public SecurityMasterExceptionCaseRow? SelectedCase
    {
        get => _selectedCase;
        set { if (SetProperty(ref _selectedCase, value)) AssignCommand.NotifyCanExecuteChanged(); }
    }

    /// <summary>The principal to assign the selected case to.</summary>
    public string Assignee
    {
        get => _assignee;
        set { if (SetProperty(ref _assignee, value ?? string.Empty)) AssignCommand.NotifyCanExecuteChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                SyncCommand.NotifyCanExecuteChanged();
                AssignCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int BreachedCount => Rows.Count(row => row.SlaBreached);
    public bool IsLoaded { get => _isLoaded; private set { if (SetProperty(ref _isLoaded, value)) RaisePropertyChanged(nameof(IsEmpty)); } }
    public bool IsEmpty => IsLoaded && Rows.Count == 0;
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string? ErrorText { get => _errorText; private set { if (SetProperty(ref _errorText, value)) RaisePropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    private bool CanAssign() => !IsBusy && SelectedCase is { IsOpen: true } && !string.IsNullOrWhiteSpace(Assignee);

    public Task RefreshAsync() => LoadAsync(notice: null);

    private async Task LoadAsync(string? notice)
    {
        _load?.Cancel();
        var cts = new CancellationTokenSource();
        _load = cts;
        var filter = StatusFilter;
        var selectedCaseId = SelectedCase?.CaseId;
        IsBusy = true;
        ErrorText = null;
        StatusText = "Loading exception cases…";
        try
        {
            var response = await _client.ListExceptionCasesAsync(filter, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (!response.Success || response.Data is null)
            {
                ErrorText = SecurityMasterWorkbenchFailure.Parse(response.StatusCode, response.ErrorMessage)
                    .DescribeQueueAction("view the Security Master exception queue");
                StatusText = notice ?? string.Empty;
                return;
            }

            Rows.Clear();
            foreach (var item in response.Data)
            {
                Rows.Add(SecurityMasterExceptionCaseRow.From(item));
            }

            SelectedCase = selectedCaseId is { } keep ? Rows.FirstOrDefault(row => row.CaseId == keep) : null;
            IsLoaded = true;
            RaisePropertyChanged(nameof(IsEmpty));
            RaisePropertyChanged(nameof(BreachedCount));
            var summary = Rows.Count == 0
                ? $"No {filter.ToLowerInvariant()} cases."
                : $"{Rows.Count} {filter.ToLowerInvariant()} case(s); {BreachedCount} past SLA.";
            StatusText = notice is null ? summary : notice + " " + summary;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorText = $"The exception queue could not be loaded: {ex.Message}";
            StatusText = notice ?? string.Empty;
        }
        finally
        {
            if (ReferenceEquals(_load, cts))
            {
                _load = null;
                IsBusy = false;
            }

            cts.Dispose();
        }
    }

    private async Task SyncAsync()
    {
        IsBusy = true;
        ErrorText = null;
        StatusText = "Reconciling cases with current conditions…";
        string? notice = null;
        string? error = null;
        try
        {
            var response = await _client.SyncExceptionCasesAsync().ConfigureAwait(true);
            if (response.Success && response.Data is { } result)
            {
                notice = $"Sync opened {result.Opened}, refreshed {result.Refreshed} and closed {result.Closed} case(s).";
            }
            else
            {
                error = SecurityMasterWorkbenchFailure.Parse(response.StatusCode, response.ErrorMessage)
                    .DescribeQueueAction("sync the Security Master exception queue");
            }
        }
        catch (Exception ex)
        {
            error = $"The exception queue could not be synced: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        if (error is not null)
        {
            ErrorText = error;
            StatusText = string.Empty;
            return;
        }

        await LoadAsync(notice).ConfigureAwait(true);
    }

    private async Task AssignAsync()
    {
        if (SelectedCase is not { IsOpen: true } row || string.IsNullOrWhiteSpace(Assignee))
        {
            return;
        }

        var assignee = Assignee.Trim();
        IsBusy = true;
        ErrorText = null;
        StatusText = "Assigning case…";
        string? notice = null;
        string? error = null;
        try
        {
            var response = await _client.AssignExceptionCaseAsync(row.CaseId, assignee).ConfigureAwait(true);
            if (response.Success)
            {
                notice = $"Assigned \"{row.Title}\" to {assignee}.";
            }
            else
            {
                error = SecurityMasterWorkbenchFailure.Parse(response.StatusCode, response.ErrorMessage)
                    .DescribeQueueAction("assign Security Master exception cases");
            }
        }
        catch (Exception ex)
        {
            error = $"The case could not be assigned: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        // A 409 means the case closed meanwhile; reload so the queue shows it.
        await LoadAsync(notice).ConfigureAwait(true);
        if (error is not null)
        {
            ErrorText = error;
        }
    }

    public void Dispose()
    {
        _load?.Cancel();
        _load = null;
    }
}
