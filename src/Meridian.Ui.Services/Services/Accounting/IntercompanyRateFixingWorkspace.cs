using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>
/// Desktop state for reviewed index fixings over the routes the browser uses. An operator records what a publisher
/// showed; a second person approves it. Only approved values set floating accrual rates; nothing is fetched,
/// interpolated or carried forward here, and a missing fixing is never inferred.
/// </summary>
public sealed partial class IntercompanyRateFixingWorkspace : ObservableObject, IDisposable
{
    private const string Idle = "Enter an index and date range, then load its fixings.";
    private readonly IWorkstationIntercompanyAgreementApiClient _client;
    private CancellationTokenSource _sessionCancellation = new();
    private int _sessionGeneration;
    private bool _disposed, _busy, _canManageLedger;
    private string _index = "", _from = "", _to = "", _fixingDate = "", _rate = "", _source = "", _justification = "", _reason = "", _status = Idle;
    private string? _loadedIndex;
    private DateOnly _loadedFrom, _loadedTo;
    private IntercompanyRateFixingRow? _selected;
    private string? _commandKey;
    private Guid _commandId;

    public IntercompanyRateFixingWorkspace(IWorkstationIntercompanyAgreementApiClient client)
    {
        _client = client;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(IndexReference)
            && !string.IsNullOrWhiteSpace(From) && !string.IsNullOrWhiteSpace(To));
        ProposeCommand = new AsyncRelayCommand(ProposeAsync, () => !IsBusy && CanManageLedger && _loadedIndex is not null
            && !string.IsNullOrWhiteSpace(FixingDate) && !string.IsNullOrWhiteSpace(RateFraction)
            && !string.IsNullOrWhiteSpace(SourceReference) && !string.IsNullOrWhiteSpace(Justification));
        ApproveCommand = new AsyncRelayCommand(() => DecideAsync(IntercompanyRateFixingActionDto.Approve),
            () => !IsBusy && CanManageLedger && SelectedFixing?.Fixing.Pending is not null);
        RejectCommand = new AsyncRelayCommand(() => DecideAsync(IntercompanyRateFixingActionDto.Reject),
            () => !IsBusy && CanManageLedger && SelectedFixing?.Fixing.Pending is not null && !string.IsNullOrWhiteSpace(Reason));
    }

    public ObservableCollection<IntercompanyRateFixingRow> Fixings { get; } = [];
    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand ProposeCommand { get; }
    public IAsyncRelayCommand ApproveCommand { get; }
    public IAsyncRelayCommand RejectCommand { get; }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); NotifyCommands(); } }
    public bool CanManageLedger { get => _canManageLedger; private set { if (SetProperty(ref _canManageLedger, value)) { OnPropertyChanged(nameof(PermissionNote)); NotifyCommands(); } } }
    public string PermissionNote => CanManageLedger
        ? "Ledger management is granted. A value you propose must be approved by someone else before any accrual uses it."
        : "Read-only: ledger management permission is required to propose, approve or reject fixings.";
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>Index reference as agreements declare it, for example SOFR. Matched ignoring case.</summary>
    public string IndexReference { get => _index; set => ChangeScope(ref _index, value); }
    public string From { get => _from; set => ChangeScope(ref _from, value); }
    public string To { get => _to; set => ChangeScope(ref _to, value); }
    public string FixingDate { get => _fixingDate; set => Edit(ref _fixingDate, value); }
    /// <summary>Annual decimal fraction typed exactly: 0.0425 means 4.25%.</summary>
    public string RateFraction { get => _rate; set => Edit(ref _rate, value); }
    public string SourceReference { get => _source; set => Edit(ref _source, value); }
    public string Justification { get => _justification; set => Edit(ref _justification, value); }
    public string Reason { get => _reason; set => Edit(ref _reason, value); }
    public IntercompanyRateFixingRow? SelectedFixing
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) { Reason = ""; NotifyCommands(); } }
    }

    private void Edit(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    { if (SetProperty(ref field, value ?? "", property)) NotifyCommands(); }

    // A loaded list describes one index and range; changing either withdraws it.
    private void ChangeScope(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (!SetProperty(ref field, value ?? "", property))
            return;
        Invalidate();
        Clear();
        StatusText = "Scope changed. Load the fixings for this index and range.";
        NotifyCommands();
    }

    private Task LoadAsync() => RunAsync(async token =>
    {
        var index = IndexReference.Trim();
        var from = Date(From, "From");
        var to = Date(To, "To");
        Clear();
        StatusText = "Reading retained fixings…";
        var capabilities = await _client.GetIntercompanyCapabilitiesAsync(token);
        CanManageLedger = capabilities.Success && capabilities.Data?.CanManageLedger == true;
        Show(Require(await _client.GetRateFixingsAsync(index, from, to, token)));
        token.ThrowIfCancellationRequested();
        (_loadedIndex, _loadedFrom, _loadedTo) = (index, from, to);
        StatusText = Fixings.Count == 0 ? "No fixing is retained for this index and range." : "Fixings loaded. Only approved values set accrual rates.";
    });

    private Task ProposeAsync() => RunAsync(async token =>
    {
        var date = Date(FixingDate, "Fixing date");
        var rate = decimal.TryParse(RateFraction.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value : throw new InvalidOperationException("The rate must be an exact decimal fraction using . as separator (0.0425 means 4.25%).");
        var existing = Fixings.FirstOrDefault(item => item.Fixing.FixingDate == date)?.Fixing.Version ?? 0;
        var source = SourceReference.Trim();
        var justification = Justification.Trim();
        var key = $"propose|{_loadedIndex}|{date:yyyy-MM-dd}|{rate.ToString(CultureInfo.InvariantCulture)}|{source}|{justification}|{existing}";
        var updated = Require(await _client.ProposeRateFixingAsync(new(Identity(key), _loadedIndex!, date, rate, source, justification, existing), token));
        RetireIdentity();
        await ReloadAsync(updated.FixingId, token);
        StatusText = "Proposed. It sets no accrual rate until someone else approves it.";
    });

    private Task DecideAsync(IntercompanyRateFixingActionDto action) => RunAsync(async token =>
    {
        var row = SelectedFixing!;
        var pending = row.Fixing.Pending!;
        var reason = action == IntercompanyRateFixingActionDto.Reject ? Reason.Trim() : null;
        var key = $"{row.Fixing.FixingId:D}|{row.Fixing.Version}|{action}|{pending.ValueDigest}|{reason}";
        var updated = Require(await _client.DecideRateFixingAsync(row.Fixing.FixingId,
            new(Identity(key), action, row.Fixing.Version, pending.ValueDigest, reason), token));
        RetireIdentity();
        await ReloadAsync(updated.FixingId, token);
        StatusText = action == IntercompanyRateFixingActionDto.Approve
            ? "Approved. Floating accruals that use this date now read this value; drafts computed on an earlier value must be recomputed."
            : "Rejected with its reason. Any previously approved value stays in force.";
    });

    private async Task ReloadAsync(Guid selected, CancellationToken token)
    {
        Show(Require(await _client.GetRateFixingsAsync(_loadedIndex!, _loadedFrom, _loadedTo, token)));
        SelectedFixing = Fixings.FirstOrDefault(item => item.Fixing.FixingId == selected);
    }

    private void Show(IReadOnlyList<IntercompanyRateFixingDto> fixings)
    {
        Fixings.Clear();
        foreach (var item in fixings.OrderBy(item => item.FixingDate))
            Fixings.Add(new(item));
    }

    private void Clear()
    {
        _loadedIndex = null;
        Fixings.Clear();
        SelectedFixing = null;
        RetireIdentity();
    }

    private Guid Identity(string key)
    {
        if (key != _commandKey)
        { _commandKey = key; _commandId = Guid.NewGuid(); }
        return _commandId;
    }

    /// <summary>A completed command is finished: the same input entered again later is a new request, not a replay.</summary>
    private void RetireIdentity()
    {
        _commandKey = null;
        _commandId = Guid.Empty;
    }

    private void Invalidate()
    {
        _sessionGeneration++;
        _sessionCancellation.Cancel();
        _sessionCancellation.Dispose();
        _sessionCancellation = new CancellationTokenSource();
        IsBusy = false;
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (_disposed)
            return;
        var generation = _sessionGeneration;
        var token = _sessionCancellation.Token;
        IsBusy = true;
        try
        { await operation(token); }
        catch (Exception ex)
        {
            if (generation == _sessionGeneration && !token.IsCancellationRequested)
                StatusText = $"Rate fixing refused: {ex.Message} Your input has been retained.";
        }
        finally { if (generation == _sessionGeneration) { IsBusy = false; NotifyCommands(); } }
    }

    /// <summary>Clear prior-session evidence and invalidate every outstanding response on account changes.</summary>
    public void ResetSession()
    {
        if (_disposed)
            return;
        Invalidate();
        Clear();
        CanManageLedger = false;
        SetProperty(ref _index, "", nameof(IndexReference));
        SetProperty(ref _from, "", nameof(From));
        SetProperty(ref _to, "", nameof(To));
        FixingDate = RateFraction = SourceReference = Justification = Reason = "";
        StatusText = Idle;
        NotifyCommands();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        ResetSession();
        _disposed = true;
        _sessionCancellation.Cancel();
        _sessionCancellation.Dispose();
    }

    private static T Require<T>(ApiResponse<T> response) where T : class
        => response.Success && response.Data is not null ? response.Data : throw new InvalidOperationException(response.StatusCode switch
        {
            401 or 403 => "Your signed-in account cannot manage fixings in this tenant.",
            409 => response.ErrorMessage ?? "The fixing changed since it was read. Load it again.",
            _ => response.ErrorMessage ?? "Intercompany rate fixings are unavailable."
        });

    private static DateOnly Date(string value, string label)
        => DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new InvalidOperationException($"{label} must use yyyy-MM-dd.");

    private void NotifyCommands()
    {
        LoadCommand?.NotifyCanExecuteChanged();
        ProposeCommand?.NotifyCanExecuteChanged();
        ApproveCommand?.NotifyCanExecuteChanged();
        RejectCommand?.NotifyCanExecuteChanged();
    }
}

/// <summary>One retained fixing; rates are the server's exact decimals.</summary>
public sealed class IntercompanyRateFixingRow(IntercompanyRateFixingDto fixing)
{
    public IntercompanyRateFixingDto Fixing { get; } = fixing;
    public string Date => Fixing.FixingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string Approved => Fixing.Approved is { } value ? IntercompanyExactAmount.Text(value.RateFraction) : "None approved";
    public string Pending => Fixing.Pending is { } value ? $"{IntercompanyExactAmount.Text(value.RateFraction)} proposed by {value.ProposedBy}" : "";
    public string Source => (Fixing.Pending ?? Fixing.Approved)?.SourceReference ?? "";
    public string StatusText => (Fixing.Approved, Fixing.Pending) switch
    {
        (null, not null) => $"Awaiting approval · version {Fixing.Version}",
        (not null, not null) => $"Correction awaiting approval · approved value in force · version {Fixing.Version}",
        (not null, null) => $"Approved · version {Fixing.Version}",
        _ => $"Rejected, nothing approved · version {Fixing.Version}"
    };
    public string History => string.Join(Environment.NewLine, Fixing.Events.Select(step =>
        $"{step.RecordedAt:yyyy-MM-dd HH:mm} UTC · {step.Action} · {step.Actor}{(step.Reason is { } reason ? $" · {reason}" : "")}"));
}
