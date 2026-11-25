using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>
/// Desktop state for reciprocal intercompany balance confirmation over the routes the browser uses. Balances are
/// pinned by the server from posted journals; this book's operator confirms or disputes only this book's side. The other
/// book may belong to another company of the tenant; then each company records its own resolution of a dispute.
/// Confirmation is advisory unless the book adopts a reviewed close policy (ADR-032, see the ClosePolicy part); nothing
/// is calculated here.
/// </summary>
public sealed partial class IntercompanyBalanceConfirmationWorkspace : ObservableObject, IDisposable
{
    private const string Idle = "Enter the ledger book and date, then load its confirmations.";
    private readonly IWorkstationIntercompanyAgreementApiClient _client;
    private CancellationTokenSource _sessionCancellation = new();
    private int _sessionGeneration;
    private bool _disposed, _busy, _canManageLedger;
    private string _ledgerBook = "", _asOf = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private string _reason = "", _selectedProfile = "", _status = Idle, _summaryText = "";
    private Guid _loadedBook;
    private DateOnly _loadedAsOf;
    private IntercompanyBalanceConfirmationRow? _selected;
    private string? _commandKey;
    private Guid _commandId;

    public IntercompanyBalanceConfirmationWorkspace(IWorkstationIntercompanyAgreementApiClient client)
    {
        _client = client;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(LedgerBook) && !string.IsNullOrWhiteSpace(AsOf));
        foreach (var choice in BlockingCodeChoices)
            choice.PropertyChanged += (_, _) => NotifyClosePolicyCommands();
        ProposeCommand = new AsyncRelayCommand(ProposeAsync, () => !IsBusy && CanManageLedger && Loaded && !string.IsNullOrWhiteSpace(SelectedProfile));
        ConfirmCommand = new AsyncRelayCommand(() => ActAsync(IntercompanyBalanceConfirmationActionDto.Confirm),
            () => Actionable && SelectedConfirmation!.CanConfirm);
        DisputeCommand = new AsyncRelayCommand(() => ActAsync(IntercompanyBalanceConfirmationActionDto.Dispute),
            () => Actionable && SelectedConfirmation!.CanDispute && !string.IsNullOrWhiteSpace(Reason));
        ResolveCommand = new AsyncRelayCommand(() => ActAsync(IntercompanyBalanceConfirmationActionDto.Resolve),
            () => Actionable && SelectedConfirmation!.CanResolve && !string.IsNullOrWhiteSpace(Reason));
    }

    public ObservableCollection<IntercompanyBalanceConfirmationRow> Confirmations { get; } = [];
    public ObservableCollection<string> Findings { get; } = [];
    /// <summary>Approved accounting profiles that map the loaded book on the loaded date.</summary>
    public ObservableCollection<IntercompanyChoiceRow> Profiles { get; } = [];
    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand ProposeCommand { get; }
    public IAsyncRelayCommand ConfirmCommand { get; }
    public IAsyncRelayCommand DisputeCommand { get; }
    public IAsyncRelayCommand ResolveCommand { get; }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); NotifyCommands(); } }
    public bool CanManageLedger { get => _canManageLedger; private set { if (SetProperty(ref _canManageLedger, value)) { OnPropertyChanged(nameof(PermissionNote)); NotifyCommands(); } } }
    public string PermissionNote => CanManageLedger
        ? "Ledger management is granted. Each side is confirmed or disputed only through its own book, checked by the server."
        : "Read-only: ledger management permission is required to pin, confirm, dispute or resolve.";
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>The advisory position, stated as advisory: close eligibility is unchanged.</summary>
    public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }
    public string LedgerBook { get => _ledgerBook; set => ChangeScope(ref _ledgerBook, value); }
    /// <summary>Confirmation date (yyyy-MM-dd), normally the period end.</summary>
    public string AsOf { get => _asOf; set => ChangeScope(ref _asOf, value); }
    public string Reason { get => _reason; set { if (SetProperty(ref _reason, value ?? "")) NotifyCommands(); } }
    public string SelectedProfile { get => _selectedProfile; set { if (SetProperty(ref _selectedProfile, value ?? "")) NotifyCommands(); } }
    public IntercompanyBalanceConfirmationRow? SelectedConfirmation
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) { Reason = ""; NotifyCommands(); } }
    }

    private bool Loaded => _loadedBook != Guid.Empty;
    private bool Actionable => !IsBusy && CanManageLedger && SelectedConfirmation is not null;

    // A loaded summary describes one book and date; changing either withdraws it.
    private void ChangeScope(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (!SetProperty(ref field, value ?? "", property))
            return;
        Invalidate();
        Clear();
        StatusText = "Scope changed. Load the confirmations for this book and date.";
        NotifyCommands();
    }

    private Task LoadAsync() => RunAsync(async token =>
    {
        var book = ParseId(LedgerBook, "Ledger book");
        var date = Date(AsOf);
        Clear();
        StatusText = "Reading posted balances and retained confirmations…";
        var capabilities = await _client.GetIntercompanyCapabilitiesAsync(token);
        CanManageLedger = capabilities.Success && capabilities.Data?.CanManageLedger == true;
        var summary = Require(await _client.GetBalanceConfirmationsAsync(book, date, token));
        token.ThrowIfCancellationRequested();
        _loadedBook = book;
        _loadedAsOf = date;
        Show(summary);
        await LoadClosePolicyAsync(book, token);
        if (CanManageLedger)
        {
            var profiles = await _client.GetCoverageProfilesAsync(book, date, token);
            token.ThrowIfCancellationRequested();
            foreach (var profile in (profiles.Data ?? []).OrderBy(item => item.ArrangementReference, StringComparer.CurrentCultureIgnoreCase))
                Profiles.Add(new(profile.ArrangementId.ToString("D"), $"{profile.ArrangementReference} · {profile.TemplateKind} · {profile.ArrangementId:D}"));
            SelectedProfile = Profiles.FirstOrDefault()?.Id ?? "";
            if (!profiles.Success)
                StatusText = $"Confirmations loaded; accounting profiles could not be listed: {profiles.ErrorMessage ?? "unavailable."}";
        }
    });

    private Task ProposeAsync() => RunAsync(async token =>
    {
        var profile = ParseId(SelectedProfile, "Accounting profile");
        var request = new ProposeIntercompanyBalanceConfirmationRequest(Identity($"propose|{profile:D}|{_loadedAsOf:yyyy-MM-dd}"), profile, _loadedAsOf);
        Require(await _client.ProposeBalanceConfirmationAsync(request, token));
        RetireIdentity();
        Show(Require(await _client.GetBalanceConfirmationsAsync(_loadedBook, _loadedAsOf, token)));
        StatusText = "Both sides' posted balances are pinned for confirmation. Nothing was posted.";
    });

    private Task ActAsync(IntercompanyBalanceConfirmationActionDto action) => RunAsync(async token =>
    {
        var row = SelectedConfirmation!;
        var reason = action == IntercompanyBalanceConfirmationActionDto.Confirm ? null : Reason.Trim();
        // Across companies each side records its own resolution; within one company a resolution settles both sides.
        var party = action == IntercompanyBalanceConfirmationActionDto.Resolve && !row.Confirmation.IsCrossCompany
            ? (IntercompanyBalanceConfirmationPartyDto?)null : row.ThisBookParty;
        var key = $"{row.ConfirmationId:D}|{row.Version}|{action}|{party}|{reason}";
        var request = new IntercompanyBalanceConfirmationActionRequest(Identity(key), action, party, row.Version, row.BalanceDigest, reason);
        var updated = Require(await _client.ActOnBalanceConfirmationAsync(row.ConfirmationId, request, token));
        RetireIdentity();
        Show(Require(await _client.GetBalanceConfirmationsAsync(_loadedBook, _loadedAsOf, token)));
        SelectedConfirmation = Confirmations.FirstOrDefault(item => item.ConfirmationId == updated.ConfirmationId);
        StatusText = action switch
        {
            IntercompanyBalanceConfirmationActionDto.Confirm => "This book's side confirmed the pinned balances.",
            IntercompanyBalanceConfirmationActionDto.Dispute => "Dispute retained with its reason.",
            _ when party is not null && updated.Status == IntercompanyBalanceConfirmationStatusDto.Disputed
                => "This book's resolution is retained. The dispute is resolved once the other company records its own.",
            _ => "Dispute resolved. Draft any correction, then pin the corrected balances for confirmation."
        };
    });

    private void Show(IntercompanyBalanceConfirmationSummaryDto summary)
    {
        Confirmations.Clear();
        Findings.Clear();
        foreach (var item in summary.Confirmations)
            Confirmations.Add(new(item, summary.LedgerBookId));
        foreach (var finding in summary.Findings)
            Findings.Add($"{finding.Message} ({finding.Code} · accounting profile {finding.AccountingArrangementId:D})");
        SummaryText = summary.Findings.Count == 0
            ? $"All agreement balances confirmed as of {summary.AsOf:yyyy-MM-dd}. Advisory only; close eligibility is unchanged."
            : $"{summary.Findings.Count} advisory finding(s) as of {summary.AsOf:yyyy-MM-dd}. Advisory only; close eligibility is unchanged.";
        StatusText = "Confirmations loaded.";
    }

    private void Clear()
    {
        _loadedBook = Guid.Empty;
        Confirmations.Clear();
        Findings.Clear();
        Profiles.Clear();
        SelectedConfirmation = null;
        SummaryText = "";
        ClearClosePolicy();
        RetireIdentity();
    }

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
                StatusText = $"Balance confirmation refused: {ex.Message} Your input has been retained.";
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
        SetProperty(ref _ledgerBook, "", nameof(LedgerBook));
        Reason = "";
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
            401 or 403 => "Your signed-in account cannot act on this book's side.",
            409 => response.ErrorMessage ?? "The confirmation or its balances changed. Load it again.",
            _ => response.ErrorMessage ?? "Balance confirmation is unavailable."
        });
    private static Guid ParseId(string value, string label) => Guid.TryParse(value?.Trim(), out var id) && id != Guid.Empty ? id : throw new InvalidOperationException($"{label} must be a valid nonempty ID.");
    private static DateOnly Date(string value) => DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date : throw new InvalidOperationException("The confirmation date must use yyyy-MM-dd.");

    private void NotifyCommands()
    {
        LoadCommand?.NotifyCanExecuteChanged();
        ProposeCommand?.NotifyCanExecuteChanged();
        ConfirmCommand?.NotifyCanExecuteChanged();
        DisputeCommand?.NotifyCanExecuteChanged();
        ResolveCommand?.NotifyCanExecuteChanged();
        _draftAdjustment?.NotifyCanExecuteChanged();
        NotifyClosePolicyCommands();
    }
}

/// <summary>One selectable identifier with a readable label.</summary>
public sealed record IntercompanyChoiceRow(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One retained confirmation as seen from the loaded book; amounts are the server's exact decimals.</summary>
public sealed class IntercompanyBalanceConfirmationRow(IntercompanyBalanceConfirmationDto confirmation, Guid book)
{
    public IntercompanyBalanceConfirmationDto Confirmation { get; } = confirmation;
    public Guid ConfirmationId => Confirmation.ConfirmationId;
    public long Version => Confirmation.Version;
    public string BalanceDigest => Confirmation.BalanceDigest;
    public IntercompanyBalanceConfirmationStatusDto Status => Confirmation.Status;
    public IntercompanyBalanceConfirmationPartyDto ThisBookParty => Confirmation.PartyOne.LedgerBookId == book
        ? IntercompanyBalanceConfirmationPartyDto.PartyOne : IntercompanyBalanceConfirmationPartyDto.PartyTwo;
    public string Profile => Confirmation.AccountingArrangementId.ToString("D");
    public string StatusText => $"{Status switch
    {
        IntercompanyBalanceConfirmationStatusDto.Proposed => "Awaiting both sides",
        IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne => "Party one confirmed",
        IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyTwo => "Party two confirmed",
        IntercompanyBalanceConfirmationStatusDto.Confirmed => "Confirmed by both sides",
        IntercompanyBalanceConfirmationStatusDto.Disputed => "Disputed",
        _ => "Dispute resolved · draft any correction, then pin again"
    }} · version {Version}";
    public string PartyOne => Side(Confirmation.PartyOne);
    public string PartyTwo => Side(Confirmation.PartyTwo);
    public string Net => IntercompanyExactAmount.Text(Confirmation.NetTransactionBalance, Confirmation.PartyOne.TransactionCurrency);
    public string History => string.Join(Environment.NewLine, Confirmation.Events.Select(step =>
        $"{step.RecordedAt:yyyy-MM-dd HH:mm} UTC · {step.Action}{(step.Party is { } party ? $" · {party}" : "")} · {step.Actor}{(step.Reason is { } reason ? $" · {reason}" : "")}"));
    public bool CanConfirm => Status == IntercompanyBalanceConfirmationStatusDto.Proposed
        || (Status == IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne && ThisBookParty == IntercompanyBalanceConfirmationPartyDto.PartyTwo)
        || (Status == IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyTwo && ThisBookParty == IntercompanyBalanceConfirmationPartyDto.PartyOne);
    /// <summary>Disputed, and (across companies) this book's side has not yet recorded its resolution.</summary>
    public bool CanResolve => Status == IntercompanyBalanceConfirmationStatusDto.Disputed && !ThisSideResolved;
    public bool ThisSideResolved
    {
        get
        {
            if (!Confirmation.IsCrossCompany)
                return false;
            var dispute = Confirmation.Events.ToList().FindLastIndex(step => step.Action == IntercompanyBalanceConfirmationActionDto.Dispute);
            return Confirmation.Events.Skip(dispute + 1).Any(step => step.Action == IntercompanyBalanceConfirmationActionDto.Resolve && step.Party == ThisBookParty);
        }
    }
    public bool CanDispute => Status is IntercompanyBalanceConfirmationStatusDto.Proposed or IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne
        or IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyTwo;

    private string Side(IntercompanyBalanceConfirmationSideDto side)
        => IntercompanyExactAmount.Text(side.TransactionSignedBalance, side.TransactionCurrency) + (side.LedgerBookId == book ? " (this book)" : "")
            + (Confirmation.IsCrossCompany ? $" · {(side == Confirmation.PartyOne ? Confirmation.PartyOneCompanyId : Confirmation.PartyTwoCompanyId) ?? "no company"}" : "");
}
