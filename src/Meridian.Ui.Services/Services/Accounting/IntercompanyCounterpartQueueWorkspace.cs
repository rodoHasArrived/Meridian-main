using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>
/// Desktop presentation state for counterpart delivery records and booking breaks. Every record,
/// permission and amount comes from the shared intercompany API; nothing is calculated or retained here.
/// </summary>
public sealed partial class IntercompanyCounterpartQueueWorkspace : ObservableObject, IDisposable
{
    private const string Idle = "Enter a source ledger book and load its counterpart delivery records.";
    private const string NoRepricing = "Select a drafted counterpart and preview to compare its retained and repriced amounts.";
    private readonly IWorkstationIntercompanyAgreementApiClient _client;
    private CancellationTokenSource _sessionCancellation = new();
    private int _sessionGeneration;
    private bool _disposed, _busy, _canManageLedger;
    private string _sourceBook = "", _asOf = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private string _status = Idle, _breakStatus = "Booking breaks are not loaded.", _repricingStatus = NoRepricing;
    private string _retryReason = "", _targetBook = "", _targetJournal = "", _linkReason = "", _repricingReason = "";
    private IntercompanyCounterpartProposalRow? _selected;
    private IntercompanyRefreshPreviewDto? _preview;
    private Guid _previewProposal;
    private string? _commandKey;
    private Guid _commandId;

    public IntercompanyCounterpartQueueWorkspace(IWorkstationIntercompanyAgreementApiClient client)
    {
        _client = client;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(SourceBook));
        RetryCommand = new AsyncRelayCommand(RetryAsync, () => Actionable && SelectedProposal!.CanRetry && !string.IsNullOrWhiteSpace(RetryReason));
        LinkCommand = new AsyncRelayCommand(LinkAsync, () => Actionable && !SelectedProposal!.IsResolved
            && !string.IsNullOrWhiteSpace(TargetBook) && !string.IsNullOrWhiteSpace(TargetJournal) && !string.IsNullOrWhiteSpace(LinkReason));
        PreviewRepricingCommand = new AsyncRelayCommand(PreviewRepricingAsync, () => Actionable && SelectedProposal!.CanReprice);
        ApplyRepricingCommand = new AsyncRelayCommand(ApplyRepricingAsync, () => Actionable && SelectedProposal!.CanReprice
            && _preview?.CurrentDraft is not null && _previewProposal == SelectedProposal.ProposalId && !string.IsNullOrWhiteSpace(RepricingReason));
    }

    public ObservableCollection<IntercompanyCounterpartProposalRow> Proposals { get; } = [];
    public ObservableCollection<IntercompanyCounterpartBreakRow> Breaks { get; } = [];
    public ObservableCollection<IntercompanyCounterpartRepricingRow> RepricingRows { get; } = [];
    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand RetryCommand { get; }
    public IAsyncRelayCommand LinkCommand { get; }
    public IAsyncRelayCommand PreviewRepricingCommand { get; }
    public IAsyncRelayCommand ApplyRepricingCommand { get; }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); NotifyCommands(); } }
    /// <summary>The server's answer for the signed-in account. Target-book authority is still checked per command.</summary>
    public bool CanManageLedger { get => _canManageLedger; private set { if (SetProperty(ref _canManageLedger, value)) { OnPropertyChanged(nameof(PermissionNote)); NotifyCommands(); } } }
    public string PermissionNote => CanManageLedger
        ? "Ledger management is granted. Target-book authorization is checked separately for every command."
        : "Read-only: ledger management permission is required to retry, reprice or link a counterpart.";
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    public string BreakStatus { get => _breakStatus; private set => SetProperty(ref _breakStatus, value); }
    public string RepricingStatus { get => _repricingStatus; private set => SetProperty(ref _repricingStatus, value); }
    public string SourceBook { get => _sourceBook; set => ChangeScope(ref _sourceBook, value); }
    public string AsOf { get => _asOf; set => ChangeScope(ref _asOf, value); }
    public string RetryReason { get => _retryReason; set { if (SetProperty(ref _retryReason, value ?? "")) NotifyCommands(); } }
    public string TargetBook { get => _targetBook; set { if (SetProperty(ref _targetBook, value ?? "")) NotifyCommands(); } }
    public string TargetJournal { get => _targetJournal; set { if (SetProperty(ref _targetJournal, value ?? "")) NotifyCommands(); } }
    public string LinkReason { get => _linkReason; set { if (SetProperty(ref _linkReason, value ?? "")) NotifyCommands(); } }
    public string RepricingReason { get => _repricingReason; set { if (SetProperty(ref _repricingReason, value ?? "")) NotifyCommands(); } }
    public IntercompanyCounterpartProposalRow? SelectedProposal
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
                return;
            RetryReason = TargetBook = TargetJournal = LinkReason = "";
            ResetRepricing();
            OnPropertyChanged(nameof(SelectedGuidance));
            NotifyCommands();
        }
    }
    public string SelectedGuidance => SelectedProposal switch
    {
        null => "Select a delivery record to retry, reprice or link it.",
        { IsResolved: true } => "An explicit counterpart resolution is retained. The original delivery receipt remains in provenance; retries, repricing and replacement links are closed.",
        { CanRetry: true } => "Retry schedules background delivery against the retained source identity. The other entity still reviews its own draft.",
        { CanReprice: true } => "Repricing preserves the draft's accounts and sides and clears its prior review. To link an existing posted counterpart instead, retain the generated draft's rejection first.",
        _ => "Link an existing counterpart only after checking its journal identity."
    };

    private bool Actionable => !IsBusy && CanManageLedger && SelectedProposal is not null;

    private void ChangeScope(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (!SetProperty(ref field, value ?? "", property))
            return;
        Invalidate();
        Clear();
        StatusText = "Scope changed. Load counterpart delivery records for this book and date.";
        NotifyCommands();
    }

    private Task LoadAsync() => RunAsync(async token =>
    {
        var book = ParseId(SourceBook, "Source ledger book");
        var asOf = Date(AsOf, "As-of date");
        var keep = SelectedProposal?.ProposalId;
        try
        {
            var capabilities = await _client.GetIntercompanyCapabilitiesAsync(token);
            token.ThrowIfCancellationRequested();
            CanManageLedger = capabilities.Success && capabilities.Data?.CanManageLedger == true;
            var proposals = Require(await _client.GetCounterpartProposalsAsync(book, token));
            token.ThrowIfCancellationRequested();
            Proposals.Clear();
            foreach (var proposal in proposals)
                Proposals.Add(new(proposal));
            SelectedProposal = Proposals.FirstOrDefault(x => x.ProposalId == keep);
            StatusText = Proposals.Count == 0 ? "No counterpart proposals originate in this book."
                : $"{Proposals.Count} counterpart delivery record(s); {Proposals.Count(x => !x.IsResolved && x.Status == IntercompanyProposalStatusDto.NeedsFix)} need repair. Nothing posts automatically.";
        }
        catch
        {
            if (!token.IsCancellationRequested)
            { Clear(); CanManageLedger = false; }
            throw;
        }
        // Breaks are a separate observation: an outage must read as unknown, never as "no breaks".
        var breaks = await _client.GetCounterpartBreaksAsync(book, asOf, token);
        token.ThrowIfCancellationRequested();
        Breaks.Clear();
        if (!breaks.Success || breaks.Data is null)
        { BreakStatus = $"Booking breaks unknown: {Refusal(breaks)}"; return; }
        foreach (var item in breaks.Data)
            Breaks.Add(new(item));
        BreakStatus = Breaks.Count == 0 ? $"No reported counterpart booking breaks as of {asOf:yyyy-MM-dd}."
            : $"{Breaks.Count} booking break(s) as of {asOf:yyyy-MM-dd}. Aging uses each row's retained basis; FX availability, translation and rounding differences are reported separately in consolidation.";
    });

    private Task RetryAsync() => RunAsync(async token =>
    {
        var row = SelectedProposal!;
        var reason = RetryReason.Trim();
        var updated = Require(await _client.RetryCounterpartProposalAsync(row.ProposalId, new(Identity($"retry|{row.ProposalId:D}|{reason}"), reason), token));
        token.ThrowIfCancellationRequested();
        Replace(updated);
        StatusText = "A retry was scheduled against the retained source identity. Background delivery retains the target draft or repair reason; reload to observe it.";
    });

    private Task LinkAsync() => RunAsync(async token =>
    {
        var row = SelectedProposal!;
        var book = ParseId(TargetBook, "Existing counterpart book");
        var journal = ParseId(TargetJournal, "Existing counterpart journal");
        var reason = LinkReason.Trim();
        var updated = Require(await _client.LinkCounterpartProposalAsync(row.ProposalId,
            new(Identity($"link|{row.ProposalId:D}|{book:D}|{journal:D}|{reason}"), book, journal, reason), token));
        token.ThrowIfCancellationRequested();
        Replace(updated);
        StatusText = "Existing counterpart journal linked after source and target validation.";
    });

    private Task PreviewRepricingAsync() => RunAsync(async token =>
    {
        var row = SelectedProposal!;
        ResetRepricing();
        RepricingStatus = "Preparing the retained before-and-after counterpart amounts…";
        try
        {
            var preview = Require(await _client.PreviewCounterpartRefreshAsync(row.ProposalId, token));
            token.ThrowIfCancellationRequested();
            _preview = preview;
            _previewProposal = row.ProposalId;
            var current = preview.CurrentDraft;
            foreach (var id in (current?.Lines ?? []).Select(x => x.LineId).Concat(preview.Draft.Lines.Select(x => x.LineId)).Distinct(StringComparer.Ordinal))
                RepricingRows.Add(new(current?.Lines.FirstOrDefault(x => x.LineId == id), preview.Draft.Lines.FirstOrDefault(x => x.LineId == id)));
            var fx = preview.Draft.IntercompanyFx;
            RepricingStatus = current is null
                ? "The server did not return the retained current draft. Repricing cannot be applied without its before-state and version."
                : $"Target draft version {current.Version} · {current.Status} · original journal date {current.AccountingDate:yyyy-MM-dd}\n"
                  + $"Retained source digest {row.SourceDigest}\nPreview fingerprint {preview.Fingerprint}"
                  + (fx is null ? "" : $"\nApproved rate set {fx.RateSetId:D} revision {fx.RateSetRevision} · policy {fx.PolicyId} revision {fx.PolicyRevision} · configuration {fx.ConfigurationHash}");
        }
        catch
        {
            if (!token.IsCancellationRequested)
                RepricingStatus = "Repricing preview unavailable. No before-and-after amounts are shown.";
            throw;
        }
        finally { NotifyCommands(); }
    });

    private Task ApplyRepricingAsync() => RunAsync(async token =>
    {
        var row = SelectedProposal!;
        var preview = _preview!;
        var reason = RepricingReason.Trim();
        var version = preview.CurrentDraft!.Version;
        Require(await _client.RefreshCounterpartProposalAsync(row.ProposalId,
            new(Identity($"refresh|{row.ProposalId:D}|{version}|{preview.Fingerprint}|{reason}"), version, preview.Fingerprint, reason), token));
        token.ThrowIfCancellationRequested();
        ResetRepricing();
        RepricingReason = "";
        StatusText = "Counterpart repricing retained against the reviewed source and FX evidence. Its previous review was cleared; obtain fresh independent journal approval before posting.";
    });

    /// <summary>An identical command keeps its request identity so a lost response can be retried safely.</summary>
    private Guid Identity(string key)
    {
        if (key != _commandKey)
        { _commandKey = key; _commandId = Guid.NewGuid(); }
        return _commandId;
    }

    private void Replace(IntercompanyProposalDto updated)
    {
        var row = new IntercompanyCounterpartProposalRow(updated);
        var old = Proposals.FirstOrDefault(x => x.ProposalId == row.ProposalId);
        if (old is null)
            Proposals.Add(row);
        else
            Proposals[Proposals.IndexOf(old)] = row;
        SelectedProposal = row;
        // The retained break list describes the superseded delivery state.
        Breaks.Clear();
        BreakStatus = "Booking breaks changed with this command. Reload to observe the current classification.";
    }

    private void ResetRepricing()
    {
        _preview = null;
        _previewProposal = Guid.Empty;
        RepricingRows.Clear();
        RepricingStatus = NoRepricing;
    }

    private void Clear()
    {
        Proposals.Clear();
        Breaks.Clear();
        SelectedProposal = null;
        ResetRepricing();
        BreakStatus = "Booking breaks are not loaded.";
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
                StatusText = $"Counterpart operation refused: {ex.Message} Your input has been retained.";
        }
        finally { if (generation == _sessionGeneration) IsBusy = false; }
    }

    /// <summary>Clear prior-session evidence and invalidate every outstanding response on account changes.</summary>
    public void ResetSession()
    {
        if (_disposed)
            return;
        Invalidate();
        Clear();
        CanManageLedger = false;
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
        => response.StatusCode == 409 ? response.ErrorMessage ?? "The retained delivery, draft or FX authority changed. Reload before retrying."
            : response.StatusCode is 401 or 403 ? "Your signed-in account cannot perform this action on these books."
            : response.StatusCode == 404 ? "The counterpart delivery record is unavailable in this scope."
            : response.ErrorMessage ?? "The intercompany proposal service is unavailable.";
    private static Guid ParseId(string value, string label) => Guid.TryParse(value?.Trim(), out var id) && id != Guid.Empty ? id : throw new InvalidOperationException($"{label} must be a valid nonempty ID.");
    private static DateOnly Date(string value, string label) => DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new InvalidOperationException($"{label} must use yyyy-MM-dd.");

    private void NotifyCommands()
    {
        LoadCommand?.NotifyCanExecuteChanged();
        RetryCommand?.NotifyCanExecuteChanged();
        LinkCommand?.NotifyCanExecuteChanged();
        PreviewRepricingCommand?.NotifyCanExecuteChanged();
        ApplyRepricingCommand?.NotifyCanExecuteChanged();
    }
}

/// <summary>Exact server decimals rendered without a binary floating-point round trip.</summary>
internal static class IntercompanyExactAmount
{
    public static string Text(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    public static string Text(decimal value, string currency) => $"{Text(value)} {currency}";
}

/// <summary>One retained counterpart delivery record with the browser queue's resolution rules.</summary>
public sealed class IntercompanyCounterpartProposalRow(IntercompanyProposalDto proposal)
{
    public IntercompanyProposalDto Proposal { get; } = proposal;
    public Guid ProposalId => Proposal.Source.ProposalId;
    public string TransactionId => Proposal.Source.TransactionId;
    public DateOnly EffectiveDate => Proposal.Source.EffectiveDate;
    public IntercompanyProposalStatusDto Status => Proposal.Status;
    public int Attempts => Proposal.Attempts;
    public string SourceDigest => Proposal.SourceDigest;
    public Guid CounterpartyEntityId => Proposal.Source.CounterpartyEntityId;
    public bool IsResolved => Proposal.LatestLink is not null || Proposal.Status == IntercompanyProposalStatusDto.Linked;
    public bool CanRetry => !IsResolved && Proposal.Status is IntercompanyProposalStatusDto.Pending or IntercompanyProposalStatusDto.NeedsFix;
    public bool CanReprice => !IsResolved && Proposal.Status == IntercompanyProposalStatusDto.Drafted && Proposal.TargetJournalId.HasValue;
    public string Delivery => Proposal.LatestLink is not null ? "Resolved" : Proposal.Status.ToString();
    public string DeliveryDetail => Proposal.LatestLink is not null ? $"Original delivery receipt: {Proposal.Status}" : Proposal.LastError ?? "";
    public string Target => Proposal.ResolvedTargetJournalId is { } journal
        ? $"{(IsResolved ? "Resolved counterpart" : "Target draft")} {journal:D} in book {Proposal.ResolvedTargetBookId:D}" : "Awaiting counterpart draft";
    public IReadOnlyList<IntercompanyCounterpartSourceLineRow> Lines { get; } = [.. (proposal.Source.Lines ?? []).Select(x => new IntercompanyCounterpartSourceLineRow(x))];
    public string Provenance
    {
        get
        {
            var source = Proposal.Source;
            var text = $"Source journal {source.SourceJournalId:D}\nAccounting arrangement {source.AccountingArrangementId:D}\nAccounting mapping revision {source.AccountingArrangementRevisionId:D}\n"
                + $"Canonical legal agreement {(source.IntercompanyAgreementId == Guid.Empty ? "Not retained" : source.IntercompanyAgreementId.ToString("D"))}\n"
                + $"Approved legal agreement version {(source.IntercompanyAgreementVersion > 0 ? source.IntercompanyAgreementVersion.ToString(CultureInfo.InvariantCulture) : "Not retained")}\n"
                + $"Legal agreement payload hash {(string.IsNullOrEmpty(source.IntercompanyAgreementPayloadHash) ? "Not retained" : source.IntercompanyAgreementPayloadHash)}\n"
                + $"Source digest {Proposal.SourceDigest}\nRule {source.RuleVersion}\n"
                + $"Original delivered book {Proposal.TargetBookId?.ToString("D") ?? "No original delivery receipt"}\nOriginal delivered journal {Proposal.TargetJournalId?.ToString("D") ?? "No original delivery receipt"}";
            if (Proposal.LatestLink is { } link)
                text += $"\nResolved posted counterpart {link.TargetBookId:D} · {link.TargetJournalId:D}\nResolution actor and time {link.Actor} · {link.RecordedAt:O}\nResolution reason {link.Reason}"
                    + (link.RejectedDraftId is { } rejected ? $"\nRejected generated draft {rejected:D} · version {link.RejectedDraftVersion}" : "");
            return text;
        }
    }
}

public sealed class IntercompanyCounterpartSourceLineRow(IntercompanyProposalSourceLineDto line)
{
    public string AccountName => line.AccountName;
    public string AccountType => line.AccountType;
    public string Currency => line.Currency;
    public string TransactionDebit => IntercompanyExactAmount.Text(line.TransactionDebit);
    public string TransactionCredit => IntercompanyExactAmount.Text(line.TransactionCredit);
    public string FunctionalDebit => IntercompanyExactAmount.Text(line.Debit);
    public string FunctionalCredit => IntercompanyExactAmount.Text(line.Credit);
}

public sealed class IntercompanyCounterpartBreakRow(IntercompanyBreakDto item)
{
    public IntercompanyBreakDto Break { get; } = item;
    public string Classification => Break.Classification;
    public int AgeDays => Break.AgeDays;
    public string Aging => $"{Break.AgingBasis} from {Break.AgeFrom:yyyy-MM-dd}";
    public string TransactionId => Break.TransactionId;
    public string Detail => Break.Detail;
    public Guid AccountingArrangementId => Break.AccountingArrangementId;
    public Guid SourceJournalId => Break.SourceJournalId;
    public string TargetJournal => Break.TargetJournalId?.ToString("D") ?? "None";
}

/// <summary>One counterpart line before and after explicit repricing; amounts are the server's exact decimals.</summary>
public sealed class IntercompanyCounterpartRepricingRow(ManualJournalEntryLineDto? before, ManualJournalEntryLineDto? after)
{
    public string Account => after?.AccountPath ?? before?.AccountPath ?? "Unavailable";
    public string Side => before?.Side == after?.Side ? before?.Side.ToString() ?? "Missing" : $"{before?.Side.ToString() ?? "Missing"} → {after?.Side.ToString() ?? "Missing"}";
    public string CurrentAmount => before is null ? "Not retained" : IntercompanyExactAmount.Text(before.Amount, before.Currency);
    public string ProposedAmount => after is null ? "Not retained" : IntercompanyExactAmount.Text(after.Amount, after.Currency);
    public string OriginalEvidence => after?.TransactionCurrency is { } original
        ? $"{IntercompanyExactAmount.Text(original.Amount, original.Currency)} · rate {IntercompanyExactAmount.Text(original.FxRateToFunctional)}" : "Not retained";
}
