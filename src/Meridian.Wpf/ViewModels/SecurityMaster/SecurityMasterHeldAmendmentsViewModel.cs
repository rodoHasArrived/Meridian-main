using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Services.SecurityMaster;

namespace Meridian.Wpf.ViewModels.SecurityMaster;

/// <summary>One held amendment as served.</summary>
public sealed record SecurityMasterHeldAmendmentRow(
    Guid HeldAmendmentId,
    Guid SecurityId,
    string SourceSystem,
    string DominatedFields,
    int ConflictCount,
    long HeldAtVersion,
    string HeldAt,
    string Status,
    string ClosedBy,
    bool CanDiscard)
{
    public static SecurityMasterHeldAmendmentRow From(SecurityMasterHeldAmendmentDto dto) => new(
        dto.HeldAmendmentId,
        dto.SecurityId,
        dto.SourceSystem,
        dto.DominatedFieldPaths.Count == 0 ? "—" : string.Join(", ", dto.DominatedFieldPaths),
        dto.ConflictIds.Count,
        dto.HeldAtVersion,
        dto.HeldAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC",
        dto.Status,
        dto.ClosedBy ?? string.Empty,
        string.Equals(dto.Status, SecurityMasterHeldAmendmentStatusDto.Held, StringComparison.Ordinal));
}

/// <summary>
/// Ingest amendments held by source ranking (Security Master review A2): a lower-ranked source tried to
/// overwrite a value a higher-ranked source supplied, so the whole amendment was held. The operator adopts
/// the held value through the conflict's governed resolution (open the security and resolve the named
/// conflict, which stages a draft) or discards the row. The desktop decides nothing about ranking.
/// </summary>
public sealed class SecurityMasterHeldAmendmentsViewModel : BindableBase, IDisposable
{
    private readonly ISecurityMasterReviewQueueApiClient _client;
    private readonly Func<Guid, Task>? _openSecurity;
    private CancellationTokenSource? _load;
    private bool _isBusy;
    private bool _isLoaded;
    private string _statusFilter = SecurityMasterHeldAmendmentStatusDto.Held;
    private string _statusText = "Load the queue to see amendments held by source ranking.";
    private string? _errorText;

    public SecurityMasterHeldAmendmentsViewModel(ISecurityMasterReviewQueueApiClient client, Func<Guid, Task>? openSecurity = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _openSecurity = openSecurity;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        DiscardCommand = new AsyncRelayCommand<SecurityMasterHeldAmendmentRow>(DiscardAsync, row => row is { CanDiscard: true } && !IsBusy);
        OpenSecurityCommand = new AsyncRelayCommand<SecurityMasterHeldAmendmentRow>(
            row => row is null || _openSecurity is null ? Task.CompletedTask : _openSecurity(row.SecurityId),
            row => row is not null && _openSecurity is not null);
    }

    public IReadOnlyList<string> StatusFilters { get; } =
        [SecurityMasterHeldAmendmentStatusDto.Held, SecurityMasterHeldAmendmentStatusDto.Replayed, SecurityMasterHeldAmendmentStatusDto.Discarded];

    public ObservableCollection<SecurityMasterHeldAmendmentRow> Rows { get; } = new();
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<SecurityMasterHeldAmendmentRow> DiscardCommand { get; }
    public IAsyncRelayCommand<SecurityMasterHeldAmendmentRow> OpenSecurityCommand { get; }

    /// <summary>A changed filter discards the loaded rows; reload to see the new population.</summary>
    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (!SetProperty(ref _statusFilter, string.IsNullOrWhiteSpace(value) ? SecurityMasterHeldAmendmentStatusDto.Held : value))
            {
                return;
            }

            _load?.Cancel();
            Rows.Clear();
            IsLoaded = false;
            StatusText = $"Load the queue to see {_statusFilter.ToLowerInvariant()} amendments.";
        }
    }

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
        var filter = StatusFilter;
        IsBusy = true;
        ErrorText = null;
        StatusText = "Loading held amendments…";
        try
        {
            var response = await _client.ListHeldAmendmentsAsync(securityId: null, status: filter, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (!response.Success || response.Data is null)
            {
                ErrorText = SecurityMasterWorkbenchFailure.Parse(response.StatusCode, response.ErrorMessage)
                    .DescribeQueueAction("view held Security Master amendments");
                StatusText = notice ?? string.Empty;
                return;
            }

            Rows.Clear();
            foreach (var dto in response.Data)
            {
                Rows.Add(SecurityMasterHeldAmendmentRow.From(dto));
            }

            IsLoaded = true;
            RaisePropertyChanged(nameof(IsEmpty));
            var summary = Rows.Count == 0
                ? $"No {filter.ToLowerInvariant()} amendments."
                : $"{Rows.Count} {filter.ToLowerInvariant()} amendment(s).";
            StatusText = notice is null ? summary : notice + " " + summary;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorText = $"Held amendments could not be loaded: {ex.Message}";
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

    private async Task DiscardAsync(SecurityMasterHeldAmendmentRow? row)
    {
        if (row is not { CanDiscard: true })
        {
            return;
        }

        IsBusy = true;
        ErrorText = null;
        StatusText = "Discarding held amendment…";
        string? notice = null;
        string? error = null;
        try
        {
            var response = await _client.DiscardHeldAmendmentAsync(row.HeldAmendmentId).ConfigureAwait(true);
            if (response.Success)
            {
                notice = $"Discarded the held amendment from {row.SourceSystem}.";
            }
            else
            {
                error = SecurityMasterWorkbenchFailure.Parse(response.StatusCode, response.ErrorMessage)
                    .DescribeQueueAction("discard held Security Master amendments");
            }
        }
        catch (Exception ex)
        {
            error = $"The held amendment could not be discarded: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        // A refused discard (409) means the row was replayed or discarded elsewhere; reload to show it.
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
