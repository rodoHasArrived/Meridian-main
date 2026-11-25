using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>Shared desktop presentation over the same durable lifecycle case API as the browser.</summary>
public sealed class IntercompanyAgreementWorkQueueWorkspace : ObservableObject, IDisposable
{
    private const string Idle = "Choose a book, as-of date and expiry horizon, then load the lifecycle queue.";
    private readonly IWorkstationIntercompanyAgreementApiClient _client;
    private readonly Func<IntercompanyAgreementWorkItemDto, DateOnly, CancellationToken, Task>? _openAgreement;
    private CancellationTokenSource _session = new();
    private int _generation;
    private bool _disposed, _busy, _requiresRefresh;
    private string _book = "", _date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private string _horizon = "30", _owner = "", _reason = "", _note = "", _evidence = "", _status = Idle;
    private IntercompanyAgreementWorkQueueDto? _queue;
    private IntercompanyAgreementWorkItemRow? _selected;
    private string? _editingCaseId;
    private IntercompanyAgreementWorkItemActionDto _action = IntercompanyAgreementWorkItemActionDto.AssignOwner;
    private (string Key, Guid Id)? _command;

    public IntercompanyAgreementWorkQueueWorkspace(IWorkstationIntercompanyAgreementApiClient client,
        Func<IntercompanyAgreementWorkItemDto, DateOnly, CancellationToken, Task>? openAgreement = null)
    {
        _client = client;
        _openAgreement = openAgreement;
        LoadBooksCommand = new AsyncRelayCommand(LoadBooksAsync, () => !IsBusy);
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy && ValidScope);
        ObserveCommand = new AsyncRelayCommand(ObserveAsync,
            () => CanMutate && SelectedCase?.Item.IsCurrentlyObserved == true && !string.IsNullOrWhiteSpace(Reason));
        UpdateCommand = new AsyncRelayCommand(UpdateAsync, () => CanUpdate);
        OpenAgreementCommand = new AsyncRelayCommand(OpenAgreementAsync,
            () => !IsBusy && SelectedCase is not null && _openAgreement is not null);
    }

    public ObservableCollection<IntercompanyChoice> BookChoices { get; } = [];
    public ObservableCollection<IntercompanyAgreementWorkItemRow> Cases { get; } = [];
    public ObservableCollection<IntercompanyGuaranteeDisclosureRow> Guarantees { get; } = [];
    public IReadOnlyList<IntercompanyWorkQueueActionChoice> Actions { get; } =
    [
        new(IntercompanyAgreementWorkItemActionDto.AssignOwner, "Assign or clear owner"),
        new(IntercompanyAgreementWorkItemActionDto.AddNote, "Add note or evidence"),
        new(IntercompanyAgreementWorkItemActionDto.AwaitReview, "Await review"),
        new(IntercompanyAgreementWorkItemActionDto.Resolve, "Resolve with evidence"),
        new(IntercompanyAgreementWorkItemActionDto.Reopen, "Reopen resolved case")
    ];
    public IAsyncRelayCommand LoadBooksCommand { get; }
    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand ObserveCommand { get; }
    public IAsyncRelayCommand UpdateCommand { get; }
    public IAsyncRelayCommand OpenAgreementCommand { get; }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); NotifyCommands(); } }
    public string LedgerBook { get => _book; set { if (SetProperty(ref _book, value ?? "")) ResetScope(); } }
    public string AsOf { get => _date; set { if (SetProperty(ref _date, value ?? "")) ResetScope(); } }
    public string ExpiryHorizonDays { get => _horizon; set { if (SetProperty(ref _horizon, value ?? "")) ResetScope(); } }
    public string Owner { get => _owner; set { SetProperty(ref _owner, value ?? ""); NotifyCommands(); } }
    public string Reason { get => _reason; set { SetProperty(ref _reason, value ?? ""); NotifyCommands(); } }
    public string Note { get => _note; set { SetProperty(ref _note, value ?? ""); NotifyCommands(); } }
    public string EvidenceReferences { get => _evidence; set { SetProperty(ref _evidence, value ?? ""); NotifyCommands(); } }
    public IntercompanyAgreementWorkItemActionDto Action { get => _action; set { SetProperty(ref _action, value); NotifyCommands(); } }
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    public bool CanManage => _queue?.CanManage == true;
    public bool IsCurrent => _queue is { IsComplete: true, Status: "Current", SourceFingerprint.Length: > 0 };
    public string ObservationText => _queue is null ? "No lifecycle observation is loaded."
        : $"{_queue.Status} · {(_queue.IsComplete ? "complete" : "incomplete")} evidence · as of {_queue.AsOf:yyyy-MM-dd} · evaluated {_queue.EvaluatedAtUtc:O}\n"
            + "A missing finding never resolves retained work.\n" + string.Join(Environment.NewLine, _queue.Diagnostics);
    public string PermissionText => _queue is null ? "Permissions are resolved when the queue is loaded."
        : !CanManage ? "Read-only: ledger management permission is required for case actions."
        : !IsCurrent ? "Case actions are unavailable until a complete current source observation is loaded."
        : _requiresRefresh ? "Reload the queue before another action. The last request was not confirmed."
        : "Case actions record follow-up history only. Each command rechecks source and entity authority.";
    public string ObservationFingerprint => _queue?.SourceFingerprint ?? "";
    public string SelectedHistory => SelectedCase?.History ?? "Select a case to inspect retained history.";
    public string SelectedGuidance => SelectedCase is null ? "Select a finding to track it or update its retained case."
        : $"{SelectedCase.Item.SuggestedAction}\nLegal agreement {SelectedCase.Item.AgreementId:D} · version {SelectedCase.Item.AgreementVersion}\n"
            + "Resolving a case records a follow-up decision; it does not amend the agreement or clear its ledger balance.";
    public string ObserveLabel => SelectedCase?.Item.IsTracked == true ? "Retain current observation" : "Track this finding";
    public IntercompanyAgreementWorkItemRow? SelectedCase
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
                return;
            if (value is not null)
                _editingCaseId = value.Item.CaseId;
            Owner = value?.Item.Owner ?? "";
            Reason = Note = EvidenceReferences = "";
            Action = value?.Item.State == IntercompanyAgreementWorkItemStateDto.Resolved
                ? IntercompanyAgreementWorkItemActionDto.Reopen : IntercompanyAgreementWorkItemActionDto.AssignOwner;
            OnPropertyChanged(nameof(SelectedHistory));
            OnPropertyChanged(nameof(SelectedGuidance));
            OnPropertyChanged(nameof(ObserveLabel));
            NotifyCommands();
        }
    }

    private bool ValidScope => Guid.TryParse(LedgerBook, out var book) && book != Guid.Empty
        && DateOnly.TryParseExact(AsOf, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
        && int.TryParse(ExpiryHorizonDays, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days is >= 0 and <= 3660;
    private bool CanMutate => !IsBusy && !_requiresRefresh && CanManage && IsCurrent && SelectedCase is not null;
    private bool CanUpdate => CanMutate && SelectedCase!.Item.IsTracked && !string.IsNullOrWhiteSpace(Reason)
        && (Action != IntercompanyAgreementWorkItemActionDto.Resolve || !string.IsNullOrWhiteSpace(EvidenceReferences))
        && (Action != IntercompanyAgreementWorkItemActionDto.AddNote || !string.IsNullOrWhiteSpace(Note) || !string.IsNullOrWhiteSpace(EvidenceReferences))
        && (Action != IntercompanyAgreementWorkItemActionDto.Reopen || SelectedCase.Item.State == IntercompanyAgreementWorkItemStateDto.Resolved)
        && (Action is not (IntercompanyAgreementWorkItemActionDto.AwaitReview or IntercompanyAgreementWorkItemActionDto.Resolve)
            || SelectedCase.Item.State != IntercompanyAgreementWorkItemStateDto.Resolved);
    private IntercompanyObligationMonitorRequestDto Scope() => new(null, Guid.Parse(LedgerBook),
        DateOnly.ParseExact(AsOf, "yyyy-MM-dd", CultureInfo.InvariantCulture), int.Parse(ExpiryHorizonDays, CultureInfo.InvariantCulture));

    private Task LoadBooksAsync() => RunAsync(async token =>
    {
        var result = Require(await _client.GetLedgerBooksAsync(token));
        token.ThrowIfCancellationRequested();
        IntercompanyChoice.Replace(BookChoices, result.Select(IntercompanyChoice.Book));
        StatusText = BookChoices.Count == 0 ? "No ledger books are available in this session." : "Choose an authorized ledger book, then load its queue.";
    });

    private Task LoadAsync() => RunAsync(async token =>
    {
        var scope = Scope();
        var selection = SelectedCase?.Item.CaseId ?? _editingCaseId;
        var input = (Owner, Reason, Note, EvidenceReferences, Action);
        ClearObservation();
        try
        {
            var result = Require(await _client.GetAgreementWorkQueueAsync(scope, token));
            token.ThrowIfCancellationRequested();
            Show(result);
            SelectedCase = Cases.FirstOrDefault(item => item.Item.CaseId == selection);
            _requiresRefresh = false;
            StatusText = $"Loaded {Cases.Count} lifecycle case(s) and {Guarantees.Count} guarantee disclosure(s).";
        }
        finally
        {
            // Withdraw old authority even when a reload fails, while preserving unsent operator input.
            // A new scope cancels this token and owns its own empty editor.
            if (!token.IsCancellationRequested)
                (Owner, Reason, Note, EvidenceReferences, Action) = input;
        }
    });

    private Task ObserveAsync() => RunAsync(async token =>
    {
        var item = SelectedCase!.Item;
        var scope = Scope();
        var request = new IntercompanyAgreementWorkQueueObserveRequestDto(Guid.Empty, item.CaseId,
            _queue!.SourceFingerprint, Reason.Trim(), item.Version, item.RecordHash);
        request = request with { CommandId = Identity("observe", request) };
        var updated = Require(await _client.ObserveAgreementWorkQueueAsync(scope, request, token));
        token.ThrowIfCancellationRequested();
        await ReloadAfterMutationAsync(scope, updated, token);
    });

    private Task UpdateAsync() => RunAsync(async token =>
    {
        var item = SelectedCase!.Item;
        var scope = Scope();
        var request = new IntercompanyAgreementWorkQueueUpdateRequestDto(Guid.Empty, item.CaseId, Action,
            item.Version, item.RecordHash!, _queue!.SourceFingerprint, Reason.Trim(),
            Action == IntercompanyAgreementWorkItemActionDto.AssignOwner ? Null(Owner) : null,
            Null(Note), EvidenceReferences.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        request = request with { CommandId = Identity("update", request) };
        var updated = Require(await _client.UpdateAgreementWorkQueueAsync(scope, request, token));
        token.ThrowIfCancellationRequested();
        await ReloadAfterMutationAsync(scope, updated, token);
    });

    private async Task ReloadAfterMutationAsync(IntercompanyObligationMonitorRequestDto scope, IntercompanyAgreementWorkItemDto updated, CancellationToken token)
    {
        _command = null;
        ClearObservation();
        _requiresRefresh = true;
        StatusText = $"Case action retained at version {updated.Version}; reloading current authority.";
        try
        {
            var result = Require(await _client.GetAgreementWorkQueueAsync(scope, token));
            token.ThrowIfCancellationRequested();
            Show(result);
            SelectedCase = Cases.FirstOrDefault(item => item.Item.CaseId == updated.CaseId);
            _requiresRefresh = false;
            StatusText = $"Case action retained at version {updated.Version}. No legal terms or journal changed.";
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            StatusText = $"Case action retained at version {updated.Version}. The current queue could not be reloaded: {error.Message} Load the queue before another action.";
        }
    }

    private Task OpenAgreementAsync() => RunAsync(async token =>
    {
        await _openAgreement!(SelectedCase!.Item, Scope().AsOf, token);
        token.ThrowIfCancellationRequested();
        StatusText = "Opened the existing agreement workflow. No amendment or termination was created.";
    });

    private void Show(IntercompanyAgreementWorkQueueDto queue)
    {
        _queue = queue;
        Cases.Clear();
        Guarantees.Clear();
        foreach (var item in queue.Cases) Cases.Add(new(item));
        foreach (var item in queue.Guarantees) Guarantees.Add(new(item));
        NotifyObservation();
    }

    private void ClearObservation()
    {
        SelectedCase = null;
        _queue = null;
        Cases.Clear();
        Guarantees.Clear();
        NotifyObservation();
    }

    private void ResetScope()
    {
        _generation++;
        _session.Cancel();
        _session.Dispose();
        _session = new();
        _command = null;
        _editingCaseId = null;
        _requiresRefresh = false;
        ClearObservation();
        Owner = Reason = Note = EvidenceReferences = "";
        IsBusy = false;
        StatusText = Idle;
    }

    public void ResetSession()
    {
        ResetScope();
        BookChoices.Clear();
        _book = "";
        OnPropertyChanged(nameof(LedgerBook));
        NotifyCommands();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _generation++;
        _session.Cancel();
        _session.Dispose();
    }

    private async Task RunAsync(Func<CancellationToken, Task> body)
    {
        if (IsBusy || _disposed) return;
        var generation = _generation;
        var token = _session.Token;
        IsBusy = true;
        try { await body(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (generation == _generation && !token.IsCancellationRequested)
            {
                _requiresRefresh = true;
                StatusText = $"{error.Message} Reload before another action; local reason and evidence are retained.";
            }
        }
        finally
        {
            if (generation == _generation && !token.IsCancellationRequested)
            {
                IsBusy = false;
                NotifyObservation();
            }
        }
    }

    private Guid Identity<T>(string action, T request)
    {
        var key = action + "|" + LedgerBook + "|" + AsOf + "|" + ExpiryHorizonDays + "|" + JsonSerializer.Serialize(request);
        if (_command?.Key != key) _command = (key, Guid.NewGuid());
        return _command.Value.Id;
    }
    private static T Require<T>(ApiResponse<T> response) where T : class => response.Success && response.Data is not null ? response.Data
        : throw new InvalidOperationException(response.StatusCode is 401 or 403 ? "Your signed-in account cannot access or manage this book and agreement."
            : response.ErrorMessage ?? "The lifecycle queue is unavailable.");
    private static string? Null(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private void NotifyObservation()
    {
        OnPropertyChanged(nameof(CanManage)); OnPropertyChanged(nameof(IsCurrent)); OnPropertyChanged(nameof(ObservationText));
        OnPropertyChanged(nameof(PermissionText)); OnPropertyChanged(nameof(ObservationFingerprint)); NotifyCommands();
    }
    private void NotifyCommands()
    {
        LoadBooksCommand?.NotifyCanExecuteChanged(); LoadCommand?.NotifyCanExecuteChanged();
        ObserveCommand?.NotifyCanExecuteChanged(); UpdateCommand?.NotifyCanExecuteChanged(); OpenAgreementCommand?.NotifyCanExecuteChanged();
    }
}

public sealed record IntercompanyWorkQueueActionChoice(IntercompanyAgreementWorkItemActionDto Value, string Label);
public sealed class IntercompanyAgreementWorkItemRow(IntercompanyAgreementWorkItemDto item)
{
    public IntercompanyAgreementWorkItemDto Item { get; } = item;
    public string Message => Item.Message;
    public string State => Item.IsTracked ? (Item.State == IntercompanyAgreementWorkItemStateDto.AwaitingReview ? "Awaiting review" : Item.State.ToString()) : "Not tracked";
    public string Observation => Item.IsCurrentlyObserved ? "Observed now" : "Not observed at this cutoff";
    public string Owner => Item.Owner ?? "Unassigned";
    public string FirstSeen => Item.FirstSeenAtUtc?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "Not tracked";
    public string LastObserved => Item.LastObservedAtUtc?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "Not tracked";
    public string Age => Item.AgeDays is { } days ? $"{days} calendar days" : "Age unavailable";
    public string Due => Item.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Not dated";
    public string History => $"Case {Item.CaseId}\nVersion {Item.Version} · retained hash {Item.RecordHash ?? "None"}\n"
        + string.Join(Environment.NewLine + Environment.NewLine, Item.History.Select(step =>
            $"{step.Action} · {step.State} · {step.Actor} · {step.RecordedAtUtc:O} · version {step.Version}\n{step.Reason}\n"
            + $"Owner: {step.Owner ?? "Unassigned"}\n{step.Note}\n{string.Join(Environment.NewLine, step.EvidenceReferences)}\n"
            + $"Source as of {step.ObservedAsOf:yyyy-MM-dd}: {step.SourceFingerprint}\n"
            + string.Join(Environment.NewLine, step.SourceVersions.Select(source =>
                $"{source.SourceKind} · {source.SourceId} · version {source.Version} · {source.ContentHash}"))));
}
public sealed class IntercompanyGuaranteeDisclosureRow(IntercompanyGuaranteeDisclosureDto item)
{
    public IntercompanyGuaranteeDisclosureDto Item { get; } = item;
    public string Agreement => $"{Item.Code} · {Item.Name}";
    public string Parties => $"{Item.PartyOneRole}: {Item.PartyOneId:D}\n{Item.PartyTwoRole}: {Item.PartyTwoId:D}";
    public string Cap => Item.LimitAmount is { } value ? IntercompanyExactAmount.Text(value, Item.Currency) : "Not declared";
    public string Exposure => Item.CurrentExposure is { } value ? IntercompanyExactAmount.Text(value, Item.Currency) : "Unavailable";
    public string ExposureReason => Item.ExposureUnavailableReason;
    public string Coverage => $"{Item.EffectiveFrom:yyyy-MM-dd} – {Item.EffectiveTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "open ended"} (end exclusive) · {Item.EffectiveStatus}";
    public string Evidence => $"Version {Item.AgreementVersion} · {Item.PayloadHash}\nBeneficiary: {Item.BeneficiaryReference ?? "Not declared"}\nTermination: {Item.TerminationEffectiveDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Not declared"}";
}
