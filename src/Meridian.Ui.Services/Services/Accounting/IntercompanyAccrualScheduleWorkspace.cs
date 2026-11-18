using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>
/// Desktop presentation state for opt-in, reviewed intercompany accrual schedules. A schedule only drafts governed
/// loan-interest accruals for ordinary journal review; nothing posts from here. Every schedule, period, outcome and
/// permission comes from the shared intercompany API, and the server enforces that a proposer cannot approve.
/// </summary>
public sealed partial class IntercompanyAccrualScheduleWorkspace : ObservableObject, IDisposable
{
    private const string Idle = "Choose a ledger book to list its accrual schedules. A schedule drafts accruals for review only; nothing posts.";
    private const string ChoicesIdle = "Load the books you can access to choose a ledger book.";
    private const string NoWindows = "Select a schedule and preview its due periods.";
    private readonly IWorkstationIntercompanyAgreementApiClient _client;
    private CancellationTokenSource _sessionCancellation = new();
    private int _sessionGeneration, _booksGeneration;
    private bool _disposed, _busy, _canManageLedger, _permissionKnown, _loadingBooks;
    private string _sourceBook = "", _interestProfile = "", _principalProfile = "", _proposeReason = "", _decisionReason = "";
    private string _status = Idle, _choicesStatus = ChoicesIdle, _windowStatus = NoWindows;
    private IntercompanyAccrualScheduleRow? _selected;
    private string? _commandKey;
    private Guid _commandId;
    private readonly List<ResolvedIntercompanyAccountingArrangementDto> _bookProfiles = [];

    public IntercompanyAccrualScheduleWorkspace(IWorkstationIntercompanyAgreementApiClient client)
    {
        _client = client;
        LoadChoicesCommand = new AsyncRelayCommand(LoadBooksAsync, () => !_loadingBooks);
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(SourceBook));
        ProposeCommand = new AsyncRelayCommand(ProposeAsync, () => !IsBusy && CanManageLedger && !string.IsNullOrWhiteSpace(SourceBook)
            && !string.IsNullOrWhiteSpace(InterestProfile) && !string.IsNullOrWhiteSpace(PrincipalProfile) && !string.IsNullOrWhiteSpace(ProposeReason));
        ApproveCommand = new AsyncRelayCommand(() => DecideAsync(IntercompanyAccrualScheduleActionDto.Approve),
            () => Actionable && SelectedSchedule!.Status == IntercompanyAccrualScheduleStatusDto.Proposed);
        RejectCommand = new AsyncRelayCommand(() => DecideAsync(IntercompanyAccrualScheduleActionDto.Reject),
            () => Actionable && SelectedSchedule!.Status == IntercompanyAccrualScheduleStatusDto.Proposed && !string.IsNullOrWhiteSpace(DecisionReason));
        DisableCommand = new AsyncRelayCommand(() => DecideAsync(IntercompanyAccrualScheduleActionDto.Disable),
            () => Actionable && SelectedSchedule!.Status == IntercompanyAccrualScheduleStatusDto.Active && !string.IsNullOrWhiteSpace(DecisionReason));
        PreviewDueCommand = new AsyncRelayCommand(PreviewDueAsync, () => !IsBusy && SelectedSchedule is
        { Status: IntercompanyAccrualScheduleStatusDto.Proposed or IntercompanyAccrualScheduleStatusDto.Active });
        RunCommand = new AsyncRelayCommand(RunScheduleAsync, () => Actionable && SelectedSchedule!.Status == IntercompanyAccrualScheduleStatusDto.Active);
    }

    /// <summary>Books the signed-in account can see; choosing one sets <see cref="SourceBook"/> and lists its schedules.</summary>
    public ObservableCollection<IntercompanyChoice> BookChoices { get; } = [];
    /// <summary>Approved LoanInterest profiles that map the chosen book today (ledger managers only).</summary>
    public ObservableCollection<IntercompanyChoice> InterestProfileChoices { get; } = [];
    /// <summary>Approved Loan profiles of the same legal agreement as the chosen interest profile.</summary>
    public ObservableCollection<IntercompanyChoice> PrincipalProfileChoices { get; } = [];
    public ObservableCollection<IntercompanyAccrualScheduleRow> Schedules { get; } = [];
    public ObservableCollection<IntercompanyAccrualScheduleWindowRow> Windows { get; } = [];
    public IAsyncRelayCommand LoadChoicesCommand { get; }
    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand ProposeCommand { get; }
    public IAsyncRelayCommand ApproveCommand { get; }
    public IAsyncRelayCommand RejectCommand { get; }
    public IAsyncRelayCommand DisableCommand { get; }
    public IAsyncRelayCommand PreviewDueCommand { get; }
    public IAsyncRelayCommand RunCommand { get; }
    /// <summary>The latest read for the chosen book; completed when no read is outstanding.</summary>
    public Task BookLoad { get; private set; } = Task.CompletedTask;

    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); NotifyCommands(); } }
    /// <summary>The server's answer for the signed-in account. Book authority and maker-checker are still checked per command.</summary>
    public bool CanManageLedger { get => _canManageLedger; private set { if (SetProperty(ref _canManageLedger, value)) { OnPropertyChanged(nameof(PermissionNote)); NotifyCommands(); } } }
    public string PermissionNote => !_permissionKnown ? "Permission is resolved by the server when the book's schedules are listed."
        : CanManageLedger ? "Ledger management is granted. The server checks book authority for every command, and a proposer cannot approve their own schedule."
        : "Read-only: ledger management permission is required to propose, approve, reject, disable or run a schedule. Due periods can still be previewed.";
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>What the selection lists hold, or why one is empty.</summary>
    public string ChoicesStatus { get => _choicesStatus; private set => SetProperty(ref _choicesStatus, value); }
    /// <summary>What the period list shows: a dry run, an executed run, or nothing.</summary>
    public string WindowStatus { get => _windowStatus; private set => SetProperty(ref _windowStatus, value); }
    public string SourceBook
    {
        get => _sourceBook;
        set
        {
            if (!SetProperty(ref _sourceBook, value ?? ""))
                return;
            Invalidate();
            Clear();
            StatusText = Idle;
            NotifyCommands();
            if (!string.IsNullOrWhiteSpace(_sourceBook))
                BookLoad = LoadAsync();
        }
    }
    public string InterestProfile
    {
        get => _interestProfile;
        set
        {
            if (!SetProperty(ref _interestProfile, value ?? ""))
                return;
            RefreshPrincipalChoices();
            NotifyCommands();
        }
    }
    public string PrincipalProfile { get => _principalProfile; set { if (SetProperty(ref _principalProfile, value ?? "")) NotifyCommands(); } }
    public string ProposeReason { get => _proposeReason; set { if (SetProperty(ref _proposeReason, value ?? "")) NotifyCommands(); } }
    /// <summary>Required to reject or disable; an approval carries no reason.</summary>
    public string DecisionReason { get => _decisionReason; set { if (SetProperty(ref _decisionReason, value ?? "")) NotifyCommands(); } }
    public IntercompanyAccrualScheduleRow? SelectedSchedule
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
                return;
            DecisionReason = "";
            ClearWindows();
            OnPropertyChanged(nameof(History));
            OnPropertyChanged(nameof(SelectedGuidance));
            NotifyCommands();
        }
    }
    /// <summary>The selected schedule's retained steps, oldest first.</summary>
    public string History => SelectedSchedule?.History ?? "";
    public string SelectedGuidance => SelectedSchedule?.Status switch
    {
        null => "Select a schedule to review, preview or run it.",
        IntercompanyAccrualScheduleStatusDto.Proposed => "Awaiting approval by an operator other than its proposer. It drafts nothing until approved; preview shows what would be due if approved today.",
        IntercompanyAccrualScheduleStatusDto.Active => "Active: completed periods ending on or after approval are drafted for ordinary review. Disabling stops future runs and leaves existing drafts untouched.",
        IntercompanyAccrualScheduleStatusDto.Rejected => "Rejected. It never drafted anything; propose a new schedule if one is needed.",
        _ => "Disabled. Future runs are stopped; drafts it already created stay in ordinary review."
    };

    private bool Actionable => !IsBusy && CanManageLedger && SelectedSchedule is not null;

    private async Task LoadBooksAsync()
    {
        var generation = ++_booksGeneration;
        _loadingBooks = true;
        LoadChoicesCommand.NotifyCanExecuteChanged();
        try
        {
            var response = await _client.GetLedgerBooksAsync(_sessionCancellation.Token);
            if (generation != _booksGeneration || _disposed)
                return;
            if (!response.Success || response.Data is null)
            {
                ChoicesStatus = $"Books could not be listed: {response.ErrorMessage ?? "the ledger book list is unavailable."}";
                return;
            }
            IntercompanyChoice.Replace(BookChoices, response.Data.Select(IntercompanyChoice.Book));
            ChoicesStatus = BookChoices.Count == 0 ? "No ledger book is visible to your signed-in account."
                : $"{BookChoices.Count} book(s) available. Choosing one lists its accrual schedules.";
        }
        catch (OperationCanceledException) { }
        finally
        {
            _loadingBooks = false;
            LoadChoicesCommand.NotifyCanExecuteChanged();
        }
    }

    private Task LoadAsync() => RunAsync(async token =>
    {
        var book = ParseId(SourceBook, "Ledger book");
        var keep = SelectedSchedule?.ScheduleId;
        StatusText = "Listing the book's accrual schedules…";
        try
        {
            var capabilities = await _client.GetIntercompanyCapabilitiesAsync(token);
            token.ThrowIfCancellationRequested();
            _permissionKnown = capabilities.Success;
            OnPropertyChanged(nameof(PermissionNote));
            CanManageLedger = capabilities.Success && capabilities.Data?.CanManageLedger == true;
            if (CanManageLedger)
                await LoadProfilesAsync(book, token);
            var schedules = Require(await _client.GetAccrualSchedulesAsync(book, token));
            token.ThrowIfCancellationRequested();
            Schedules.Clear();
            foreach (var schedule in schedules.OrderBy(item => item.Events.FirstOrDefault()?.RecordedAt))
                Schedules.Add(Row(schedule));
            SelectedSchedule = Schedules.FirstOrDefault(row => row.ScheduleId == keep);
            StatusText = Schedules.Count == 0
                ? "No accrual schedule is retained for this book. A proposed schedule drafts nothing until another operator approves it."
                : $"{Schedules.Count} accrual schedule(s): {Schedules.Count(row => row.Status == IntercompanyAccrualScheduleStatusDto.Active)} active, "
                  + $"{Schedules.Count(row => row.Status == IntercompanyAccrualScheduleStatusDto.Proposed)} awaiting approval. Schedules draft for review only; nothing posts.";
        }
        catch
        {
            if (!token.IsCancellationRequested)
            {
                Schedules.Clear();
                SelectedSchedule = null;
            }
            throw;
        }
    });

    // Profiles that map the chosen book today. A failure leaves the schedule listing usable and says why proposing is unavailable.
    private async Task LoadProfilesAsync(Guid book, CancellationToken token)
    {
        _bookProfiles.Clear();
        InterestProfileChoices.Clear();
        PrincipalProfileChoices.Clear();
        var response = await _client.GetCoverageProfilesAsync(book, DateOnly.FromDateTime(DateTime.UtcNow), token);
        token.ThrowIfCancellationRequested();
        if (!response.Success || response.Data is null)
        {
            ChoicesStatus = $"Accounting profiles could not be listed: {response.ErrorMessage ?? "profile authority is unavailable."}";
            return;
        }
        _bookProfiles.AddRange(response.Data);
        IntercompanyChoice.Replace(InterestProfileChoices, _bookProfiles
            .Where(profile => profile.TemplateKind == IntercompanyAccountingTemplateKindDto.LoanInterest).Select(IntercompanyChoice.Profile));
        ChoicesStatus = InterestProfileChoices.Count == 0
            ? "No approved loan-interest profile maps this book today, so no schedule can be proposed."
            : "Choose the loan-interest profile to schedule; its loan principal profiles follow.";
        if (InterestProfileChoices.All(choice => !string.Equals(choice.Id, InterestProfile, StringComparison.OrdinalIgnoreCase)))
            SetProperty(ref _interestProfile, "", nameof(InterestProfile));
        RefreshPrincipalChoices();
    }

    // A principal profile qualifies only when it is a Loan profile pinned to the same legal agreement.
    private void RefreshPrincipalChoices()
    {
        var interest = _bookProfiles.FirstOrDefault(profile => string.Equals(profile.ArrangementId.ToString("D"), InterestProfile, StringComparison.OrdinalIgnoreCase));
        IntercompanyChoice.Replace(PrincipalProfileChoices, interest is null ? [] : _bookProfiles
            .Where(profile => profile.TemplateKind == IntercompanyAccountingTemplateKindDto.Loan && profile.CanonicalAgreementId == interest.CanonicalAgreementId)
            .Select(IntercompanyChoice.Profile));
        if (interest is not null && PrincipalProfileChoices.Count == 0)
            ChoicesStatus = "No approved loan principal profile of the same legal agreement maps this book today.";
        if (PrincipalProfileChoices.Count > 0 && PrincipalProfileChoices.All(choice => !string.Equals(choice.Id, PrincipalProfile, StringComparison.OrdinalIgnoreCase)))
            PrincipalProfile = PrincipalProfileChoices[0].Id;
    }

    private Task ProposeAsync() => RunAsync(async token =>
    {
        var book = ParseId(SourceBook, "Ledger book");
        var interest = ParseId(InterestProfile, "Loan-interest accounting profile");
        var principal = ParseId(PrincipalProfile, "Loan principal accounting profile");
        var reason = ProposeReason.Trim();
        var schedule = Require(await _client.ProposeAccrualScheduleAsync(new(Identity($"propose|{book:D}|{interest:D}|{principal:D}|{reason}"), book,
            IntercompanyAccrualScheduleKindDto.LoanInterest, interest, principal, reason), token));
        token.ThrowIfCancellationRequested();
        RetireIdentity();
        Upsert(schedule);
        ProposeReason = "";
        StatusText = $"Schedule proposed as version {schedule.Version}. It drafts nothing until another operator approves it; the proposer cannot approve their own schedule.";
    });

    private Task DecideAsync(IntercompanyAccrualScheduleActionDto action) => RunAsync(async token =>
    {
        var row = SelectedSchedule!;
        var reason = action == IntercompanyAccrualScheduleActionDto.Approve ? null : DecisionReason.Trim();
        // The decision names the exact version the reviewer saw; a changed schedule is refused rather than decided blind.
        var request = new DecideIntercompanyAccrualScheduleRequest(Identity($"{action}|{row.ScheduleId:D}|{row.Version}|{reason}"), action, row.Version, reason);
        var updated = Require(await _client.DecideAccrualScheduleAsync(row.ScheduleId, row.LedgerBookId, request, token));
        token.ThrowIfCancellationRequested();
        RetireIdentity();
        Upsert(updated);
        StatusText = action switch
        {
            IntercompanyAccrualScheduleActionDto.Approve => $"Schedule approved as version {updated.Version}; active from {updated.ActiveFrom:yyyy-MM-dd}. Runs draft completed periods for ordinary review; nothing posts.",
            IntercompanyAccrualScheduleActionDto.Reject => $"Schedule rejected as version {updated.Version}. It drafted nothing.",
            _ => $"Schedule disabled as version {updated.Version}. Future runs are stopped; drafts it already created stay in ordinary review."
        };
    });

    private Task PreviewDueAsync() => RunAsync(async token =>
    {
        var row = SelectedSchedule!;
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        ClearWindows();
        WindowStatus = "Listing the completed periods a run would draft…";
        try
        {
            var run = Require(await _client.GetAccrualScheduleDueAsync(row.ScheduleId, row.LedgerBookId, asOf, token));
            token.ThrowIfCancellationRequested();
            Show(run);
            WindowStatus = $"Dry run as of {run.AsOf:yyyy-MM-dd}: {Count(run, IntercompanyAccrualScheduleOutcomeDto.Due)} period(s) due, "
                + $"{Count(run, IntercompanyAccrualScheduleOutcomeDto.AlreadyDrafted)} already drafted, {Count(run, IntercompanyAccrualScheduleOutcomeDto.Refused)} refused. Nothing was created.";
        }
        catch
        {
            if (!token.IsCancellationRequested)
                WindowStatus = "Due periods unavailable. No period is estimated.";
            throw;
        }
    });

    private Task RunScheduleAsync() => RunAsync(async token =>
    {
        var row = SelectedSchedule!;
        ClearWindows();
        WindowStatus = "Running the schedule…";
        try
        {
            var run = Require(await _client.RunAccrualScheduleAsync(row.ScheduleId, row.LedgerBookId, token));
            token.ThrowIfCancellationRequested();
            Show(run);
            WindowStatus = $"Run as of {run.AsOf:yyyy-MM-dd}: {Count(run, IntercompanyAccrualScheduleOutcomeDto.Drafted)} period(s) drafted, "
                + $"{Count(run, IntercompanyAccrualScheduleOutcomeDto.AlreadyDrafted)} already drafted, {Count(run, IntercompanyAccrualScheduleOutcomeDto.Refused)} refused.";
            StatusText = "Run complete. Drafts enter ordinary journal review in the book; nothing was approved or posted.";
        }
        catch
        {
            if (!token.IsCancellationRequested)
                WindowStatus = "Run outcome unknown. Preview the due periods to observe what was drafted.";
            throw;
        }
    });

    private void Show(IntercompanyAccrualScheduleRunDto run)
    {
        foreach (var window in run.Windows)
            Windows.Add(new(window));
    }

    private static int Count(IntercompanyAccrualScheduleRunDto run, IntercompanyAccrualScheduleOutcomeDto outcome)
        => run.Windows.Count(window => window.Outcome == outcome);

    /// <summary>An identical command keeps its request identity so a lost response can be retried safely.</summary>
    private Guid Identity(string key)
    {
        if (key != _commandKey)
        { _commandKey = key; _commandId = Guid.NewGuid(); }
        return _commandId;
    }

    /// <summary>A confirmed command is finished: the same input entered again later is a new request, not a replay.</summary>
    private void RetireIdentity()
    {
        _commandKey = null;
        _commandId = Guid.Empty;
    }

    private IntercompanyAccrualScheduleRow Row(IntercompanyAccrualScheduleDto schedule)
        => new(schedule, ProfileLabel(schedule.ProfileArrangementId), schedule.PrincipalArrangementId is { } principal ? ProfileLabel(principal) : "None");

    private string ProfileLabel(Guid id)
        => _bookProfiles.FirstOrDefault(profile => profile.ArrangementId == id) is { } profile ? IntercompanyChoice.Profile(profile).Label : id.ToString("D");

    private void Upsert(IntercompanyAccrualScheduleDto schedule)
    {
        var row = Row(schedule);
        var old = Schedules.FirstOrDefault(item => item.ScheduleId == row.ScheduleId);
        if (old is null)
            Schedules.Add(row);
        else
            Schedules[Schedules.IndexOf(old)] = row;
        SelectedSchedule = row;
    }

    private void ClearWindows()
    {
        Windows.Clear();
        WindowStatus = NoWindows;
    }

    private void Clear()
    {
        Schedules.Clear();
        SelectedSchedule = null;
        ClearWindows();
        _bookProfiles.Clear();
        InterestProfileChoices.Clear();
        PrincipalProfileChoices.Clear();
        SetProperty(ref _interestProfile, "", nameof(InterestProfile));
        SetProperty(ref _principalProfile, "", nameof(PrincipalProfile));
        ProposeReason = DecisionReason = "";
        BookLoad = Task.CompletedTask;
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
                StatusText = $"Accrual schedule request refused: {ex.Message} Your input has been retained.";
        }
        finally { if (generation == _sessionGeneration) { IsBusy = false; NotifyCommands(); } }
    }

    /// <summary>Clear prior-session schedules and invalidate every outstanding response on account changes.</summary>
    public void ResetSession()
    {
        if (_disposed)
            return;
        Invalidate();
        Clear();
        _booksGeneration++;
        BookChoices.Clear();
        ChoicesStatus = ChoicesIdle;
        _permissionKnown = false;
        CanManageLedger = false;
        OnPropertyChanged(nameof(PermissionNote));
        SetProperty(ref _sourceBook, "", nameof(SourceBook));
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
        => response.Success && response.Data is not null ? response.Data : throw new InvalidOperationException(Refusal(response));
    private static string Refusal<T>(ApiResponse<T> response) where T : class
        => response.StatusCode == 409 ? response.ErrorMessage ?? "The schedule changed or the request conflicts with its state. Reload before deciding again."
            : response.StatusCode is 401 or 403 ? "Your signed-in account cannot perform this action on this book."
            : response.StatusCode == 404 ? "The accrual schedule is unavailable in this scope."
            : response.ErrorMessage ?? "The intercompany accrual schedule service is unavailable.";
    private static Guid ParseId(string value, string label) => Guid.TryParse(value?.Trim(), out var id) && id != Guid.Empty ? id : throw new InvalidOperationException($"{label} must be a valid nonempty ID.");

    private void NotifyCommands()
    {
        LoadCommand?.NotifyCanExecuteChanged();
        ProposeCommand?.NotifyCanExecuteChanged();
        ApproveCommand?.NotifyCanExecuteChanged();
        RejectCommand?.NotifyCanExecuteChanged();
        DisableCommand?.NotifyCanExecuteChanged();
        PreviewDueCommand?.NotifyCanExecuteChanged();
        RunCommand?.NotifyCanExecuteChanged();
    }
}

/// <summary>One retained schedule version, exactly as the server returned it.</summary>
public sealed class IntercompanyAccrualScheduleRow(IntercompanyAccrualScheduleDto schedule, string profile, string principal)
{
    public IntercompanyAccrualScheduleDto Schedule { get; } = schedule;
    public Guid ScheduleId => Schedule.ScheduleId;
    public Guid LedgerBookId => Schedule.LedgerBookId;
    public long Version => Schedule.Version;
    public IntercompanyAccrualScheduleStatusDto Status => Schedule.Status;
    public string Kind => Schedule.Kind.ToString();
    public string Profile => profile;
    public string Principal => principal;
    public string ActiveFrom => Schedule.ActiveFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Not approved";
    public string ProposedBy => Schedule.Events.FirstOrDefault(step => step.Action == IntercompanyAccrualScheduleActionDto.Propose)?.Actor ?? "Not retained";
    public string History => string.Join("\n", Schedule.Events.Select(step =>
        $"{step.RecordedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}Z · {step.Action} · {step.Actor}"
        + (string.IsNullOrWhiteSpace(step.Reason) ? "" : $" · {step.Reason}") + $" · request {step.RequestId:D}"));
}

/// <summary>One completed schedule period and what a run did, or would do, with it.</summary>
public sealed class IntercompanyAccrualScheduleWindowRow(IntercompanyAccrualScheduleWindowDto window)
{
    public IntercompanyAccrualScheduleWindowDto Window { get; } = window;
    public string Period => $"{Window.AccrualFrom:yyyy-MM-dd} up to {Window.AccrualTo:yyyy-MM-dd}";
    public string Outcome => Window.Outcome switch
    {
        IntercompanyAccrualScheduleOutcomeDto.AlreadyDrafted => "Already drafted",
        var outcome => outcome.ToString()
    };
    public string Draft => Window.DraftId is { } id ? $"{id:D}" + (Window.DraftStatus is { } status ? $" · {status}" : "") : "";
    public string Reason => Window.Reason ?? "";
}
