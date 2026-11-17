using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>
/// Desktop presentation state for requesting a governed loan-interest accrual. The operator supplies
/// selections only; every rate, principal, amount and permission comes from the shared intercompany API,
/// and a draft is bound to the evidence digest of the preview the operator saw. Nothing is calculated here.
/// </summary>
public sealed partial class IntercompanyInterestAccrualWorkspace : ObservableObject, IDisposable
{
    private const string Idle = "Enter the source book, both accounting profiles and the accrual window, then preview.";
    private const string NoPreview = "No accrual is previewed.";
    private readonly IWorkstationIntercompanyAgreementApiClient _client;
    private CancellationTokenSource _sessionCancellation = new();
    private int _sessionGeneration;
    private bool _disposed, _busy, _canManageLedger, _permissionKnown;
    private string _sourceBook = "", _interestProfile = "", _principalProfile = "", _accrualFrom = "", _accrualTo = "";
    private string _status = Idle, _summary = NoPreview, _evidence = "", _draftStatus = "";
    private IntercompanyAccrualRequestDto? _previewRequest;
    private IntercompanyAccrualProposalDto? _preview;
    private ManualJournalEntryDraftDto? _draft;

    public IntercompanyInterestAccrualWorkspace(IWorkstationIntercompanyAgreementApiClient client)
    {
        _client = client;
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !IsBusy && SelectionEntered);
        CreateDraftCommand = new AsyncRelayCommand(CreateDraftAsync, () => !IsBusy && CanManageLedger && _preview is { IsReady: true } && _draft is null);
    }

    public ObservableCollection<IntercompanyInterestAccrualSliceRow> Slices { get; } = [];
    public ObservableCollection<IntercompanyInterestAccrualLineRow> Lines { get; } = [];
    public ObservableCollection<string> Refusals { get; } = [];
    public IAsyncRelayCommand PreviewCommand { get; }
    public IAsyncRelayCommand CreateDraftCommand { get; }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); NotifyCommands(); } }
    /// <summary>The server's answer for the signed-in account. Source-book authority is still checked per command.</summary>
    public bool CanManageLedger { get => _canManageLedger; private set { if (SetProperty(ref _canManageLedger, value)) { OnPropertyChanged(nameof(PermissionNote)); NotifyCommands(); } } }
    public string PermissionNote => !_permissionKnown ? "Permission is resolved by the server when you preview."
        : CanManageLedger ? "Ledger management is granted. Source-book authorization is checked separately for every command."
        : "Read-only: ledger management permission is required to preview or draft an interest accrual.";
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>The previewed amount and journal date, or the reason no amount is shown.</summary>
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string Evidence { get => _evidence; private set => SetProperty(ref _evidence, value); }
    public string DraftStatus { get => _draftStatus; private set => SetProperty(ref _draftStatus, value); }
    public string SourceBook { get => _sourceBook; set => ChangeSelection(ref _sourceBook, value); }
    public string InterestProfile { get => _interestProfile; set => ChangeSelection(ref _interestProfile, value); }
    public string PrincipalProfile { get => _principalProfile; set => ChangeSelection(ref _principalProfile, value); }
    /// <summary>First accrued day (yyyy-MM-dd).</summary>
    public string AccrualFrom { get => _accrualFrom; set => ChangeSelection(ref _accrualFrom, value); }
    /// <summary>First day not accrued (yyyy-MM-dd); the journal is dated the day before.</summary>
    public string AccrualTo { get => _accrualTo; set => ChangeSelection(ref _accrualTo, value); }

    private bool SelectionEntered => !string.IsNullOrWhiteSpace(SourceBook) && !string.IsNullOrWhiteSpace(InterestProfile)
        && !string.IsNullOrWhiteSpace(PrincipalProfile) && !string.IsNullOrWhiteSpace(AccrualFrom) && !string.IsNullOrWhiteSpace(AccrualTo);

    // A preview describes one exact selection; any change withdraws it rather than drafting a stale amount.
    private void ChangeSelection(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (!SetProperty(ref field, value ?? "", property))
            return;
        Invalidate();
        var hadPreview = _preview is not null || _draft is not null;
        ClearPreview();
        if (hadPreview)
            StatusText = "Selection changed. Preview the accrual again before drafting.";
        NotifyCommands();
    }

    private Task PreviewAsync() => RunAsync(async token =>
    {
        var request = new IntercompanyAccrualRequestDto(ParseId(SourceBook, "Source ledger book"),
            ParseId(InterestProfile, "Loan-interest accounting profile"), ParseId(PrincipalProfile, "Loan principal accounting profile"),
            Date(AccrualFrom, "First accrued day"), Date(AccrualTo, "Accrue-up-to date"));
        ClearPreview();
        StatusText = "Resolving approved terms and posted principal…";
        var capabilities = await _client.GetIntercompanyCapabilitiesAsync(token);
        token.ThrowIfCancellationRequested();
        _permissionKnown = capabilities.Success;
        OnPropertyChanged(nameof(PermissionNote));
        CanManageLedger = capabilities.Success && capabilities.Data?.CanManageLedger == true;
        var proposal = Require(await _client.PreviewInterestAccrualAsync(request, token));
        token.ThrowIfCancellationRequested();
        var computation = proposal.Computation;
        if (!proposal.IsReady)
        {
            foreach (var reason in (proposal.Diagnostics.Count > 0 ? proposal.Diagnostics : computation.Diagnostics).Distinct(StringComparer.Ordinal))
                Refusals.Add(reason);
            Summary = "Accrual unavailable. No amount is estimated.";
            StatusText = Refusals.LastOrDefault() ?? "The accrual is unavailable.";
            return;
        }
        var evidence = proposal.Evidence!;
        _previewRequest = request;
        _preview = proposal;
        foreach (var slice in computation.Slices)
            Slices.Add(new(slice));
        foreach (var line in proposal.Draft!.Lines)
            Lines.Add(new(line));
        Summary = $"{IntercompanyExactAmount.Text(computation.Amount!.Value, computation.Currency!)} interest for {computation.AccrualFrom:yyyy-MM-dd} up to {computation.AccrualTo:yyyy-MM-dd} · journal date {evidence.PostingDate:yyyy-MM-dd}"
            + (evidence.FunctionalCurrency == computation.Currency ? ""
                : $"\nBooked as {IntercompanyExactAmount.Text(evidence.FunctionalAmount, evidence.FunctionalCurrency)} through the governed FX authority.");
        Evidence = $"Calculation rule {computation.RuleVersion} · kernel {computation.KernelVersion}\nEvidence digest {proposal.EvidenceDigest}\n"
            + $"Intercompany transaction {evidence.TransactionId}\nLegal agreement {evidence.AgreementId:D} version {evidence.PostingAgreementVersion}\n"
            + $"Posting mapping revision {evidence.PostingArrangementRevisionId:D}\n"
            + $"Opening posted principal {IntercompanyExactAmount.Text(evidence.Principal.OpeningPrincipal)} from {evidence.Principal.OpeningJournalCount} posted journal(s); {evidence.Principal.Movements.Count} movement(s) inside the window"
            + string.Concat(computation.Calendars.Select(calendar => $"\nCalendar {calendar.CalendarId} {calendar.ContentHash}"))
            + string.Concat(computation.Slices.Select(slice => slice.Fixing).OfType<IntercompanyAccrualFixingPinDto>().DistinctBy(pin => pin.FixingId)
                .Select(pin => $"\nApproved {pin.IndexReference} fixing {pin.FixingDate:yyyy-MM-dd} ({pin.FixingLagBusinessDays} business days before its period) {IntercompanyExactAmount.Text(pin.IndexRate)} · value {pin.ValueDigest}"));
        StatusText = CanManageLedger
            ? "Preview ready. Review the slices and proposed lines, then create the draft. Nothing is approved or posted from here."
            : "Preview ready. Creating the draft requires ledger management permission.";
    });

    private Task CreateDraftAsync() => RunAsync(async token =>
    {
        var draft = Require(await _client.CreateInterestAccrualDraftAsync(new(_previewRequest!, _preview!.EvidenceDigest!), token));
        token.ThrowIfCancellationRequested();
        _draft = draft;
        DraftStatus = $"Accrual draft {draft.JournalEntryId:D} · {draft.Status} · version {draft.Version} in book {draft.LedgerBookId:D}";
        StatusText = "Accrual draft retained in the source book's journal review. Nothing was approved or posted; the counterpart is proposed to the other entity only after this journal posts.";
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
                StatusText = $"Accrual request refused: {ex.Message} Your input has been retained.";
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
        _permissionKnown = false;
        CanManageLedger = false;
        OnPropertyChanged(nameof(PermissionNote));
        SetProperty(ref _sourceBook, "", nameof(SourceBook));
        SetProperty(ref _interestProfile, "", nameof(InterestProfile));
        SetProperty(ref _principalProfile, "", nameof(PrincipalProfile));
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
        => response.Success && response.Data is not null ? response.Data : throw new InvalidOperationException(Refusal(response));
    private static string Refusal<T>(ApiResponse<T> response) where T : class
        => response.StatusCode == 409 ? response.ErrorMessage ?? "The approved terms, profile, principal or FX inputs changed. Preview the accrual again."
            : response.StatusCode is 401 or 403 ? "Your signed-in account cannot accrue interest in this book."
            : response.ErrorMessage ?? "The governed intercompany accrual service is unavailable.";
    private static Guid ParseId(string value, string label) => Guid.TryParse(value?.Trim(), out var id) && id != Guid.Empty ? id : throw new InvalidOperationException($"{label} must be a valid nonempty ID.");
    private static DateOnly Date(string value, string label) => DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new InvalidOperationException($"{label} must use yyyy-MM-dd.");

    private void NotifyCommands()
    {
        PreviewCommand?.NotifyCanExecuteChanged();
        CreateDraftCommand?.NotifyCanExecuteChanged();
    }
}

/// <summary>One homogeneous accrual slice; every figure is the server's exact decimal.</summary>
public sealed class IntercompanyInterestAccrualSliceRow(IntercompanyAccrualSliceDto slice)
{
    public string Days => $"{slice.From:yyyy-MM-dd} up to {slice.To:yyyy-MM-dd}";
    public string Principal => IntercompanyExactAmount.Text(slice.Principal);
    public string AnnualRate => IntercompanyExactAmount.Text(slice.AnnualRate);
    /// <summary>The approved fixing that set a floating slice's rate (rule v2), or "Fixed".</summary>
    public string Fixing => slice.Fixing is { } pin
        ? $"{pin.IndexReference} {pin.FixingDate:yyyy-MM-dd} · {IntercompanyExactAmount.Text(pin.IndexRate)} + {IntercompanyExactAmount.Text(pin.SpreadBps)} bps"
        : "Fixed";
    public string DayCount => $"{slice.DayCount} · {IntercompanyExactAmount.Text(slice.YearFraction)}";
    public string UnroundedAmount => IntercompanyExactAmount.Text(slice.UnroundedAmount);
    public string Authority => $"Legal version {slice.AgreementVersion} · mapping revision {slice.AccountingArrangementRevisionId:D}";
    public string PrincipalEvidence => slice.PrincipalEvidenceReference;
}

/// <summary>One line of the proposed source journal, exactly as the server would retain it.</summary>
public sealed class IntercompanyInterestAccrualLineRow(ManualJournalEntryLineDto line)
{
    public string Account => line.AccountPath;
    public string Side => line.Side.ToString();
    public string Amount => IntercompanyExactAmount.Text(line.Amount, line.Currency);
    public string Original => line.TransactionCurrency is { } original
        ? $"{IntercompanyExactAmount.Text(original.Amount, original.Currency)} · rate {IntercompanyExactAmount.Text(original.FxRateToFunctional)}" : "";
}
