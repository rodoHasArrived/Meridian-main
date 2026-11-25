using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Services.SecurityMaster;

namespace Meridian.Wpf.ViewModels.SecurityMaster;

/// <summary>One pending revision as the browser inbox shows it.</summary>
public sealed record SecurityMasterPendingRevisionRow(
    Guid RevisionId,
    Guid SecurityId,
    SecurityMasterRevisionStateDto State,
    string Field,
    string ProposedValue,
    string DraftedBy,
    string Waiting,
    string? Justification,
    bool CanDiscard)
{
    public static SecurityMasterPendingRevisionRow From(SecurityMasterPendingRevisionDto dto) => new(
        dto.RevisionId,
        dto.SecurityId,
        dto.State,
        string.IsNullOrWhiteSpace(dto.FieldPath) ? "—" : dto.FieldPath,
        dto.FieldValue ?? "—",
        dto.Actor,
        SecurityMasterReviewQueuePresentation.FormatAge(dto.AgeHours),
        dto.Justification,
        SecurityMasterReviewQueuePresentation.CanDiscard(dto.State));
}

/// <summary>
/// Desktop parity for the browser review inbox (Security Master review P5/D1): governed revisions awaiting
/// action across every security, oldest first. Opening a row hands the security and revision to the
/// passport editor so a reviewer who is not the drafter can approve or publish it; discarding withdraws an
/// abandoned Draft or gate-rejected Submitted revision. Rows come from the shared route as served.
/// </summary>
public sealed class SecurityMasterReviewInboxViewModel : BindableBase, IDisposable
{
    public const string DiscardReason = "Discarded from the review inbox.";

    private readonly ISecurityMasterReviewQueueApiClient _client;
    private readonly Func<SecurityMasterPendingRevisionRow, Task>? _openRevision;
    private CancellationTokenSource? _load;
    private bool _isBusy;
    private bool _isLoaded;
    private string _statusText = "Load the inbox to see revisions awaiting action.";
    private string? _errorText;

    public SecurityMasterReviewInboxViewModel(
        ISecurityMasterReviewQueueApiClient client,
        Func<SecurityMasterPendingRevisionRow, Task>? openRevision = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _openRevision = openRevision;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        DiscardCommand = new AsyncRelayCommand<SecurityMasterPendingRevisionRow>(DiscardAsync, row => row is { CanDiscard: true } && !IsBusy);
        OpenCommand = new AsyncRelayCommand<SecurityMasterPendingRevisionRow>(OpenAsync, row => row is not null && _openRevision is not null);
    }

    public ObservableCollection<SecurityMasterPendingRevisionRow> Rows { get; } = new();
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<SecurityMasterPendingRevisionRow> DiscardCommand { get; }
    public IAsyncRelayCommand<SecurityMasterPendingRevisionRow> OpenCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                DiscardCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLoaded { get => _isLoaded; private set { if (SetProperty(ref _isLoaded, value)) RaisePropertyChanged(nameof(IsEmpty)); } }
    public bool IsEmpty => IsLoaded && Rows.Count == 0;
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string? ErrorText { get => _errorText; private set { if (SetProperty(ref _errorText, value)) RaisePropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public Task RefreshAsync() => LoadAsync(notice: null);

    private async Task LoadAsync(string? notice)
    {
        _load?.Cancel();
        var cts = new CancellationTokenSource();
        _load = cts;
        IsBusy = true;
        ErrorText = null;
        StatusText = "Loading pending revisions…";
        try
        {
            var response = await _client.ListPendingRevisionsAsync(SecurityMasterReviewQueuePresentation.InboxStates, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (!response.Success || response.Data is null)
            {
                ErrorText = SecurityMasterWorkbenchFailure.Parse(response.StatusCode, response.ErrorMessage)
                    .DescribeQueueAction("view the Security Master review inbox");
                StatusText = notice ?? string.Empty;
                return;
            }

            Rows.Clear();
            foreach (var dto in response.Data)
            {
                Rows.Add(SecurityMasterPendingRevisionRow.From(dto));
            }

            IsLoaded = true;
            RaisePropertyChanged(nameof(IsEmpty));
            var summary = Rows.Count == 0 ? "No revisions are awaiting action." : $"{Rows.Count} revision(s) awaiting action, oldest first.";
            StatusText = notice is null ? summary : notice + " " + summary;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorText = $"The review inbox could not be loaded: {ex.Message}";
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

    private async Task DiscardAsync(SecurityMasterPendingRevisionRow? row)
    {
        if (row is not { CanDiscard: true })
        {
            return;
        }

        IsBusy = true;
        ErrorText = null;
        StatusText = "Discarding revision…";
        string? notice = null;
        string? error = null;
        try
        {
            var response = await _client.DiscardRevisionAsync(row.SecurityId, row.RevisionId, DiscardReason).ConfigureAwait(true);
            if (response.Success)
            {
                notice = $"Discarded revision on {(row.Field == "—" ? "the security" : row.Field)}.";
            }
            else
            {
                error = SecurityMasterWorkbenchFailure.Parse(response.StatusCode, response.ErrorMessage)
                    .DescribeQueueAction("discard Security Master revisions");
            }
        }
        catch (Exception ex)
        {
            error = $"The revision could not be discarded: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        // Reload either way: a refused discard usually means the revision moved on (approved, published or
        // discarded by someone else), and the inbox should show its current state.
        await LoadAsync(notice).ConfigureAwait(true);
        if (error is not null)
        {
            ErrorText = error;
        }
    }

    private Task OpenAsync(SecurityMasterPendingRevisionRow? row)
        => row is null || _openRevision is null ? Task.CompletedTask : _openRevision(row);

    public void Dispose()
    {
        _load?.Cancel();
        _load = null;
    }
}
