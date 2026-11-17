using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>
/// Desktop presentation state for requesting a governed fixed management-fee accrual (rule intercompany-fee-v1).
/// The operator chooses the book, the approved management-fee profile and the window; the fee, its basis, the
/// direction and the amount come from the shared intercompany API, and a draft is bound to the evidence digest of
/// the preview the operator saw. Nothing is calculated here.
/// </summary>
public sealed partial class IntercompanyManagementFeeWorkspace : ObservableObject, IDisposable
{
    private const string Idle = "Choose the source book, the management-fee profile and the fee window, then preview.";
    private const string NoPreview = "No fee is previewed.";
    private readonly IWorkstationIntercompanyAgreementApiClient _client;
    private CancellationTokenSource _sessionCancellation = new();
    private int _sessionGeneration, _choicesGeneration, _booksGeneration;
    private bool _disposed, _busy, _canManageLedger, _permissionKnown, _loadingBooks;
    private string _sourceBook = "", _feeProfile = "", _accrualFrom = "", _accrualTo = "";
    private string _status = Idle, _summary = NoPreview, _evidence = "", _draftStatus = "";
    private string _choicesStatus = "Load the books you can access to choose a source book.";
    private IntercompanyFeeAccrualRequestDto? _previewRequest;
    private IntercompanyFeeAccrualProposalDto? _preview;
    private ManualJournalEntryDraftDto? _draft;

    public IntercompanyManagementFeeWorkspace(IWorkstationIntercompanyAgreementApiClient client)
    {
        _client = client;
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !IsBusy && SelectionEntered);
        CreateDraftCommand = new AsyncRelayCommand(CreateDraftAsync, () => !IsBusy && CanManageLedger && _preview is { IsReady: true } && _draft is null);
        LoadChoicesCommand = new AsyncRelayCommand(LoadBooksAsync, () => !_loadingBooks);
    }

    /// <summary>Books the signed-in account can see; choosing one sets <see cref="SourceBook"/>.</summary>
    public ObservableCollection<IntercompanyChoice> BookChoices { get; } = [];
    /// <summary>Approved ManagementFee profiles that map the chosen book today.</summary>
    public ObservableCollection<IntercompanyChoice> FeeProfileChoices { get; } = [];
    public ObservableCollection<IntercompanyFeeSliceRow> Slices { get; } = [];
    public ObservableCollection<IntercompanyInterestAccrualLineRow> Lines { get; } = [];
    public ObservableCollection<string> Refusals { get; } = [];
    public IAsyncRelayCommand LoadChoicesCommand { get; }
    public IAsyncRelayCommand PreviewCommand { get; }
    public IAsyncRelayCommand CreateDraftCommand { get; }
    /// <summary>The latest profile read for the chosen book; completed when no read is outstanding.</summary>
    public Task ProfileChoicesLoad { get; private set; } = Task.CompletedTask;
    public string ChoicesStatus { get => _choicesStatus; private set => SetProperty(ref _choicesStatus, value); }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); NotifyCommands(); } }
    public bool CanManageLedger { get => _canManageLedger; private set { if (SetProperty(ref _canManageLedger, value)) { OnPropertyChanged(nameof(PermissionNote)); NotifyCommands(); } } }
    public string PermissionNote => !_permissionKnown ? "Permission is resolved by the server when you preview."
        : CanManageLedger ? "Ledger management is granted. Source-book authorization is checked separately for every command."
        : "Read-only: ledger management permission is required to preview or draft a management fee.";
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>The previewed amount, direction and journal date, or the reason no amount is shown.</summary>
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string Evidence { get => _evidence; private set => SetProperty(ref _evidence, value); }
    public string DraftStatus { get => _draftStatus; private set => SetProperty(ref _draftStatus, value); }
    public string SourceBook
    {
        get => _sourceBook;
        set
        {
            if (ChangeSelection(ref _sourceBook, value))
                ProfileChoicesLoad = LoadProfilesAsync();
        }
    }
    public string FeeProfile { get => _feeProfile; set => ChangeSelection(ref _feeProfile, value); }
    /// <summary>First charged day (yyyy-MM-dd).</summary>
    public string AccrualFrom { get => _accrualFrom; set => ChangeSelection(ref _accrualFrom, value); }
    /// <summary>First day not charged (yyyy-MM-dd); the journal is dated the day before.</summary>
    public string AccrualTo { get => _accrualTo; set => ChangeSelection(ref _accrualTo, value); }

    private bool SelectionEntered => !string.IsNullOrWhiteSpace(SourceBook) && !string.IsNullOrWhiteSpace(FeeProfile)
        && !string.IsNullOrWhiteSpace(AccrualFrom) && !string.IsNullOrWhiteSpace(AccrualTo);

    // A preview describes one exact selection; any change withdraws it rather than drafting a stale amount.
    private bool ChangeSelection(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (!SetProperty(ref field, value ?? "", property))
            return false;
        Invalidate();
        var hadPreview = _preview is not null || _draft is not null;
        ClearPreview();
        if (hadPreview)
            StatusText = "Selection changed. Preview the fee again before drafting.";
        NotifyCommands();
        return true;
    }

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
                : $"{BookChoices.Count} book(s) available. Choosing one lists its approved management-fee profiles.";
        }
        catch (OperationCanceledException) { }
        finally
        {
            _loadingBooks = false;
            LoadChoicesCommand.NotifyCanExecuteChanged();
        }
    }

    // A read that finishes after another book was chosen is ignored rather than mixed into the new book's list.
    private async Task LoadProfilesAsync()
    {
        var generation = ++_choicesGeneration;
        FeeProfileChoices.Clear();
        if (!Guid.TryParse(SourceBook, out var book) || book == Guid.Empty)
            return;
        ChoicesStatus = "Listing the management-fee profiles that map this book…";
        try
        {
            var response = await _client.GetCoverageProfilesAsync(book, DateOnly.FromDateTime(DateTime.UtcNow), _sessionCancellation.Token);
            if (generation != _choicesGeneration || _disposed)
                return;
            if (!response.Success || response.Data is null)
            {
                ChoicesStatus = $"Accounting profiles could not be listed: {response.ErrorMessage ?? "profile authority is unavailable."}";
                return;
            }
            IntercompanyChoice.Replace(FeeProfileChoices, response.Data
                .Where(profile => profile.TemplateKind == IntercompanyAccountingTemplateKindDto.ManagementFee).Select(IntercompanyChoice.Profile));
            ChoicesStatus = FeeProfileChoices.Count == 0
                ? "No approved management-fee profile maps this book today."
                : "Choose the management-fee profile, then the window.";
            if (FeeProfileChoices.Count > 0 && FeeProfileChoices.All(choice => !string.Equals(choice.Id, FeeProfile, StringComparison.OrdinalIgnoreCase)))
                FeeProfile = FeeProfileChoices[0].Id;
        }
        catch (OperationCanceledException) { }
    }

    private Task PreviewAsync() => RunAsync(async token =>
    {
        var request = new IntercompanyFeeAccrualRequestDto(ParseId(SourceBook, "Source ledger book"),
            ParseId(FeeProfile, "Management-fee accounting profile"), Date(AccrualFrom, "First charged day"), Date(AccrualTo, "Charge-up-to date"));
        ClearPreview();
        StatusText = "Resolving approved fee terms and profile authority…";
        var capabilities = await _client.GetIntercompanyCapabilitiesAsync(token);
        token.ThrowIfCancellationRequested();
        _permissionKnown = capabilities.Success;
        OnPropertyChanged(nameof(PermissionNote));
        CanManageLedger = capabilities.Success && capabilities.Data?.CanManageLedger == true;
        var proposal = Require(await _client.PreviewFeeAccrualAsync(request, token));
        token.ThrowIfCancellationRequested();
        var computation = proposal.Computation;
        if (!proposal.IsReady)
        {
            foreach (var reason in (proposal.Diagnostics.Count > 0 ? proposal.Diagnostics : computation.Diagnostics).Distinct(StringComparer.Ordinal))
                Refusals.Add(reason);
            Summary = "Fee unavailable. No amount is estimated.";
            StatusText = Refusals.LastOrDefault() ?? "The fee is unavailable.";
            return;
        }
        var evidence = proposal.Evidence!;
        _previewRequest = request;
        _preview = proposal;
        foreach (var slice in computation.Slices)
            Slices.Add(new(slice));
        foreach (var line in proposal.Draft!.Lines)
            Lines.Add(new(line));
        var side = evidence.SourceRole == IntercompanyAccountRoleDto.Receivable
            ? "this book is the service provider (receivable and fee revenue)"
            : "this book is the recipient (fee expense and payable)";
        Summary = $"{IntercompanyExactAmount.Text(computation.Amount!.Value, computation.Currency!)} management fee for {computation.AccrualFrom:yyyy-MM-dd} up to {computation.AccrualTo:yyyy-MM-dd} · journal date {evidence.PostingDate:yyyy-MM-dd} · {side}"
            + (evidence.FunctionalCurrency == computation.Currency ? ""
                : $"\nBooked as {IntercompanyExactAmount.Text(evidence.FunctionalAmount, evidence.FunctionalCurrency)} through the governed FX authority.");
        Evidence = $"Calculation rule {computation.RuleVersion} · kernel {computation.KernelVersion}\nEvidence digest {proposal.EvidenceDigest}\n"
            + $"Intercompany transaction {evidence.TransactionId}\nLegal agreement {evidence.AgreementId:D} version {evidence.PostingAgreementVersion}\n"
            + $"Posting mapping revision {evidence.PostingArrangementRevisionId:D}"
            + string.Concat(computation.Calendars.Select(calendar => $"\nCalendar {calendar.CalendarId} {calendar.ContentHash}"));
        StatusText = CanManageLedger
            ? "Preview ready. Review the periods and proposed lines, then create the draft. Nothing is approved or posted from here."
            : "Preview ready. Creating the draft requires ledger management permission.";
    });

    private Task CreateDraftAsync() => RunAsync(async token =>
    {
        var draft = Require(await _client.CreateFeeAccrualDraftAsync(new(_previewRequest!, _preview!.EvidenceDigest!), token));
        token.ThrowIfCancellationRequested();
        _draft = draft;
        DraftStatus = $"Fee draft {draft.JournalEntryId:D} · {draft.Status} · version {draft.Version} in book {draft.LedgerBookId:D}";
        StatusText = "Fee draft retained in the source book's journal review. Nothing was approved or posted; the counterpart is proposed to the other entity only after this journal posts.";
    });

    private void ClearPreview()
    {
        _previewRequest = null;
        _preview = null;
        _draft = null;
        Slices.Clear();
        Lines.Clear();
        Refusals.Clear();
        Summary = NoPreview;
        Evidence = DraftStatus = "";
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
                StatusText = $"Fee request refused: {ex.Message} Your input has been retained.";
        }
        finally { if (generation == _sessionGeneration) { IsBusy = false; NotifyCommands(); } }
    }

    /// <summary>Clear prior-session evidence and invalidate every outstanding response on account changes.</summary>
    public void ResetSession()
    {
        if (_disposed)
            return;
        Invalidate();
        ClearPreview();
        _choicesGeneration++;
        _booksGeneration++;
        BookChoices.Clear();
        FeeProfileChoices.Clear();
        ProfileChoicesLoad = Task.CompletedTask;
        ChoicesStatus = "Load the books you can access to choose a source book.";
        _permissionKnown = false;
        CanManageLedger = false;
        OnPropertyChanged(nameof(PermissionNote));
        SetProperty(ref _sourceBook, "", nameof(SourceBook));
        SetProperty(ref _feeProfile, "", nameof(FeeProfile));
        SetProperty(ref _accrualFrom, "", nameof(AccrualFrom));
        SetProperty(ref _accrualTo, "", nameof(AccrualTo));
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
        => response.Success && response.Data is not null ? response.Data : throw new InvalidOperationException(
            response.StatusCode == 409 ? response.ErrorMessage ?? "The approved terms, profile or FX inputs changed. Preview the fee again."
            : response.StatusCode is 401 or 403 ? "Your signed-in account cannot charge a fee in this book."
            : response.ErrorMessage ?? "The governed intercompany fee service is unavailable.");
    private static Guid ParseId(string value, string label) => Guid.TryParse(value?.Trim(), out var id) && id != Guid.Empty ? id : throw new InvalidOperationException($"{label} must be a valid nonempty ID.");
    private static DateOnly Date(string value, string label) => DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new InvalidOperationException($"{label} must use yyyy-MM-dd.");

    private void NotifyCommands()
    {
        PreviewCommand?.NotifyCanExecuteChanged();
        CreateDraftCommand?.NotifyCanExecuteChanged();
    }
}

/// <summary>One homogeneous fee slice; every figure is the server's exact decimal.</summary>
public sealed class IntercompanyFeeSliceRow(IntercompanyFeeAccrualSliceDto slice)
{
    public string Days => $"{slice.From:yyyy-MM-dd} up to {slice.To:yyyy-MM-dd}";
    public string Period => $"#{slice.SchedulePeriodSequence} · {slice.PeriodStart:yyyy-MM-dd} up to {slice.PeriodEnd:yyyy-MM-dd}";
    public string DeclaredFee => $"{IntercompanyExactAmount.Text(slice.DeclaredFee)} {(slice.Basis == IntercompanyFixedFeeBasisDto.PerAnnum ? "per year" : "per period")}";
    public string Fraction => slice.DayCount is { } dayCount
        ? $"{IntercompanyExactAmount.Text(slice.Fraction)} ({dayCount})" : IntercompanyExactAmount.Text(slice.Fraction);
    public string UnroundedAmount => IntercompanyExactAmount.Text(slice.UnroundedAmount);
    public string Authority => $"Legal version {slice.AgreementVersion} · mapping revision {slice.AccountingArrangementRevisionId:D}";
}
