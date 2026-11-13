using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>Desktop presentation state over the same governed agreement API as the browser.</summary>
public sealed class IntercompanyAgreementWorkspace : ObservableObject, IDisposable
{
    private readonly IWorkstationIntercompanyAgreementApiClient _client;
    private CancellationTokenSource _sessionCancellation = new();
    private int _sessionGeneration;
    private bool _disposed;
    private bool _busy, _dirty, _editing, _canCreate;
    private string _status = "Load agreements for an entity to begin.";
    private string _entity = "", _asOf = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private string _code = "", _name = "", _one = "", _two = "", _roleOne = "Lender", _roleTwo = "Borrower", _currency = "";
    private string _from = "", _to = "", _cutoff = "", _rate = "", _rateDescription = "", _schedule = "", _allocation = "", _days = "", _notes = "", _purpose = "", _evidence = "", _artifact = "", _rationale = "";
    private IntercompanyAgreementTypeDto _type;
    private IntercompanyAgreementActionDto _action;
    private IntercompanyAgreementDetailDto? _selectedRow, _detail;
    private Guid _agreementId, _proposalId;
    private string? _cursor, _retryKey, _journalCursor;
    private string _ledgerBook = "", _journalStatus = "Choose a ledger book to inspect source journal lines.";
    private Guid _retryId;
    private IReadOnlyList<IntercompanyAgreementEvidenceDto> _additionalEvidence = [];
    private IntercompanyAgreementRevisionDto? _selectedRevision;
    private IntercompanyAgreementDetailDto? _conflictDetail;

    public IntercompanyAgreementWorkspace(IWorkstationIntercompanyAgreementApiClient client)
    {
        _client = client;
        LoadCommand = new AsyncRelayCommand(() => LoadAsync(false), () => !IsBusy && !IsDirty);
        MoreCommand = new AsyncRelayCommand(() => LoadAsync(true), () => !IsBusy && !IsDirty && _cursor is not null);
        OpenCommand = new AsyncRelayCommand(OpenAsync, () => !IsBusy && !IsDirty && SelectedRow is not null);
        CompareLatestCommand = new AsyncRelayCommand(CompareLatestAsync, () => !IsBusy && _agreementId != Guid.Empty);
        NewCommand = new RelayCommand(New, () => !IsBusy && !IsDirty && _canCreate);
        ReplaceProposalCommand = new RelayCommand(ReplaceProposal, () => !IsBusy && !IsDirty && Detail?.Capabilities.CanCreateProposal == true && Detail.LatestApprovedVersion is null);
        AmendCommand = new RelayCommand(() => BeginProposal(IntercompanyAgreementActionDto.Amend), () => !IsBusy && !IsDirty && Detail?.Capabilities.CanAmend == true);
        TerminateCommand = new RelayCommand(() => BeginProposal(IntercompanyAgreementActionDto.Terminate), () => !IsBusy && !IsDirty && Detail?.Capabilities.CanTerminate == true);
        DiscardCommand = new RelayCommand(Discard, () => !IsBusy && IsEditing);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && IsEditing);
        SubmitCommand = Decision("submit", () => Detail?.Capabilities.CanSubmit == true);
        ApproveCommand = Decision("approve", () => Detail?.Capabilities.CanApprove == true);
        RejectCommand = Decision("reject", () => Detail?.Capabilities.CanReject == true);
        WithdrawCommand = Decision("withdraw", () => Detail?.Capabilities.CanWithdraw == true);
        LoadJournalLinesCommand = new AsyncRelayCommand(() => LoadJournalLinesAsync(false), () => !IsBusy && !IsDirty && Detail is not null);
        MoreJournalLinesCommand = new AsyncRelayCommand(() => LoadJournalLinesAsync(true), () => !IsBusy && !IsDirty && Detail is not null && _journalCursor is not null);
    }

    public ObservableCollection<IntercompanyAgreementDetailDto> Agreements { get; } = [];
    public ObservableCollection<IntercompanyAgreementJournalLineDto> JournalLines { get; } = [];
    public IReadOnlyList<IntercompanyAgreementTypeDto> AgreementTypes { get; } = Enum.GetValues<IntercompanyAgreementTypeDto>();
    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand MoreCommand { get; }
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand CompareLatestCommand { get; }
    public IRelayCommand NewCommand { get; }
    public IRelayCommand ReplaceProposalCommand { get; }
    public IRelayCommand AmendCommand { get; }
    public IRelayCommand TerminateCommand { get; }
    public IRelayCommand DiscardCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand SubmitCommand { get; }
    public IAsyncRelayCommand ApproveCommand { get; }
    public IAsyncRelayCommand RejectCommand { get; }
    public IAsyncRelayCommand WithdrawCommand { get; }
    public IAsyncRelayCommand LoadJournalLinesCommand { get; }
    public IAsyncRelayCommand MoreJournalLinesCommand { get; }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); NotifyCommands(); } }
    public bool IsDirty { get => _dirty; private set { SetProperty(ref _dirty, value); NotifyCommands(); } }
    public bool IsEditing { get => _editing; private set { SetProperty(ref _editing, value); NotifyCommands(); } }
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    public string EntityFilter { get => _entity; set => ChangeScope(ref _entity, value); }
    public string AsOf { get => _asOf; set => ChangeScope(ref _asOf, value); }
    public IntercompanyAgreementDetailDto? SelectedRow { get => _selectedRow; set { SetProperty(ref _selectedRow, value); NotifyCommands(); } }
    public IntercompanyAgreementDetailDto? Detail { get => _detail; private set { SetProperty(ref _detail, value); OnPropertyChanged(nameof(MigrationWarnings)); NotifyCommands(); } }
    public string MigrationWarnings => string.Join(Environment.NewLine, Detail?.MigrationWarnings ?? []);
    public IntercompanyAgreementRevisionDto? SelectedRevision
    {
        get => _selectedRevision;
        set { SetProperty(ref _selectedRevision, value); OnPropertyChanged(nameof(SelectedRevisionSummary)); OnPropertyChanged(nameof(PreviousRevisionSummary)); }
    }
    public string SelectedRevisionSummary => RevisionSummary(SelectedRevision);
    public string PreviousRevisionSummary => RevisionSummary(Detail?.Revisions.FirstOrDefault(r => r.AgreementVersion == SelectedRevision?.PredecessorAgreementVersion));
    public string ConflictSummary => _conflictDetail is null ? "No comparison loaded. Local edits remain in the editor."
        : $"Latest retained aggregate version: {_conflictDetail.AggregateVersion}; status: {_conflictDetail.EffectiveStatus}\n"
          + (_conflictDetail.Proposals.LastOrDefault(p => p.State is IntercompanyAgreementProposalStateDto.Draft or IntercompanyAgreementProposalStateDto.Submitted) is { } proposal
              ? $"Open proposal: {proposal.State}; base approved version {proposal.BaseAgreementVersion}\n" + DraftSummary(proposal.Draft)
              : RevisionSummary(_conflictDetail.ApplicableRevision ?? _conflictDetail.Revisions.LastOrDefault()));
    public string LedgerBook { get => _ledgerBook; set { if (SetProperty(ref _ledgerBook, value)) ResetJournalLines(); } }
    public string JournalStatus { get => _journalStatus; private set => SetProperty(ref _journalStatus, value); }
    public string Code { get => _code; set => Edit(ref _code, value); }
    public string Name { get => _name; set => Edit(ref _name, value); }
    public string PartyOne { get => _one; set => Edit(ref _one, value); }
    public string PartyTwo { get => _two; set => Edit(ref _two, value); }
    public string PartyOneRole { get => _roleOne; set => Edit(ref _roleOne, value); }
    public string PartyTwoRole { get => _roleTwo; set => Edit(ref _roleTwo, value); }
    public string Currency { get => _currency; set => Edit(ref _currency, value); }
    public IntercompanyAgreementTypeDto AgreementType { get => _type; set { if (SetProperty(ref _type, value) && IsEditing) IsDirty = true; } }
    public string EffectiveFrom { get => _from; set => Edit(ref _from, value); }
    public string EffectiveTo { get => _to; set => Edit(ref _to, value); }
    public string TerminationDate { get => _cutoff; set => Edit(ref _cutoff, value); }
    public string FixedAnnualRate { get => _rate; set => Edit(ref _rate, value); }
    public string RateDescription { get => _rateDescription; set => Edit(ref _rateDescription, value); }
    public string Schedule { get => _schedule; set => Edit(ref _schedule, value); }
    public string Allocation { get => _allocation; set => Edit(ref _allocation, value); }
    public string SettlementDays { get => _days; set => Edit(ref _days, value); }
    public string Notes { get => _notes; set => Edit(ref _notes, value); }
    public string Purpose { get => _purpose; set => Edit(ref _purpose, value); }
    public string EvidenceReference { get => _evidence; set => Edit(ref _evidence, value); }
    public string EvidenceArtifact { get => _artifact; set => Edit(ref _artifact, value); }
    public string Rationale { get => _rationale; set => Edit(ref _rationale, value); }
    public string EditorAction => _action.ToString();

    private void Edit(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (SetProperty(ref field, value ?? "", property) && IsEditing)
            IsDirty = true;
    }

    private void ChangeScope(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (IsDirty)
        { StatusText = "Save or discard local edits before changing the agreement scope."; OnPropertyChanged(property); return; }
        if (!SetProperty(ref field, value ?? "", property))
            return;
        _cursor = null;
        _canCreate = false;
        Agreements.Clear();
        SelectedRow = null;
        Detail = null;
        _agreementId = Guid.Empty;
        _proposalId = Guid.Empty;
        _conflictDetail = null;
        OnPropertyChanged(nameof(ConflictSummary));
        IsEditing = false;
        ResetJournalLines();
        SelectedRevision = null;
        StatusText = "Scope changed. Load agreements for this entity and date.";
        NotifyCommands();
    }

    private async Task LoadAsync(bool more)
    {
        await RunAsync(async token =>
        {
            _canCreate = false;
            var entity = string.IsNullOrWhiteSpace(EntityFilter) ? (Guid?)null : ParseId(EntityFilter, "Entity filter");
            var response = await _client.ListAsync(new(EntityId: entity, AsOf: Date(AsOf, "As-of date"), Cursor: more ? _cursor : null), token);
            token.ThrowIfCancellationRequested();
            var list = Require(response);
            if (!more)
                Agreements.Clear();
            foreach (var item in list.Items)
                Agreements.Add(item);
            _cursor = list.NextCursor;
            _canCreate = list.CanCreate;
            StatusText = Agreements.Count == 0 ? "No agreements match this scope and date." : $"{Agreements.Count} agreement(s) loaded; select one and open its retained detail.";
        });
    }

    private async Task LoadJournalLinesAsync(bool more)
    {
        if (Detail is not { } detail)
            return;
        await RunAsync(async token =>
        {
            try
            {
                var response = await _client.GetJournalLinesAsync(detail.AgreementId, ParseId(LedgerBook, "Ledger book"), Date(AsOf, "As-of date"), more ? _journalCursor : null, token);
                token.ThrowIfCancellationRequested();
                var result = Require(response);
                if (!more)
                    JournalLines.Clear();
                foreach (var line in result.Lines)
                    JournalLines.Add(line);
                _journalCursor = result.NextCursor;
                JournalStatus = $"{result.CoverageState}: {JournalLines.Count} source line(s). Coverage is advisory; it is not a settlement or elimination result.";
            }
            catch
            {
                if (!token.IsCancellationRequested)
                    JournalStatus = "Coverage unknown: source journal lines could not be refreshed. Any retained rows are from the previous successful read.";
                throw;
            }
        });
    }

    private void ResetJournalLines()
    {
        JournalLines.Clear();
        _journalCursor = null;
        JournalStatus = "Choose a ledger book to inspect source journal lines.";
        NotifyCommands();
    }

    private async Task OpenAsync()
    {
        if (SelectedRow is not { } row)
            return;
        await RunAsync(async token =>
        {
            var response = await _client.GetAsync(row.AgreementId, Date(AsOf, "As-of date"), token);
            token.ThrowIfCancellationRequested();
            Apply(Require(response));
        });
    }

    private Task CompareLatestAsync() => RunAsync(async token =>
    {
        var response = await _client.GetAsync(_agreementId, Date(AsOf, "As-of date"), token);
        token.ThrowIfCancellationRequested();
        _conflictDetail = Require(response);
        OnPropertyChanged(nameof(ConflictSummary));
        StatusText = "Latest retained terms loaded for comparison. Your local editor is unchanged; discard explicitly to reload the retained version.";
    });

    private void New()
    {
        Detail = null;
        _conflictDetail = null;
        OnPropertyChanged(nameof(ConflictSummary));
        ResetJournalLines();
        SelectedRevision = null;
        _agreementId = Guid.NewGuid();
        _proposalId = Guid.NewGuid();
        _action = IntercompanyAgreementActionDto.Activate;
        Rationale = "";
        Fill(new("", "", Guid.Empty, "Lender", Guid.Empty, "Borrower", IntercompanyAgreementTypeDto.Loan, "", DateOnly.FromDateTime(DateTime.UtcNow)));
        if (Guid.TryParse(EntityFilter, out var entity))
            PartyOne = entity.ToString("D");
        IsEditing = IsDirty = true;
        StatusText = "New local draft. Save to retain it; evidence and independent review are required before activation.";
        OnPropertyChanged(nameof(EditorAction));
    }

    private void BeginProposal(IntercompanyAgreementActionDto action)
    {
        if (Detail is not { } detail)
            return;
        var basis = detail.ApplicableRevision ?? detail.Revisions
            .Where(r => r.Action != IntercompanyAgreementActionDto.Terminate)
            .OrderBy(r => r.Terms.EffectiveFrom).ThenBy(r => r.AgreementVersion).LastOrDefault();
        if (basis is null)
            return;
        _proposalId = Guid.NewGuid();
        _action = action;
        Rationale = "";
        Fill(basis.Terms with { Action = action });
        EffectiveFrom = AsOf;
        TerminationDate = AsOf;
        IsEditing = IsDirty = true;
        StatusText = "Edit the proposal and save it. The server validates the applicable base version and effective timeline.";
        OnPropertyChanged(nameof(EditorAction));
    }

    private void ReplaceProposal()
    {
        var previous = Detail?.Proposals.LastOrDefault();
        if (previous is null)
            return;
        _proposalId = Guid.NewGuid();
        _action = IntercompanyAgreementActionDto.Activate;
        Fill(previous.Draft with { Action = IntercompanyAgreementActionDto.Activate });
        Rationale = "";
        IsEditing = IsDirty = true;
        StatusText = "Replacement activation proposal. The rejected or withdrawn proposal remains in history.";
        OnPropertyChanged(nameof(EditorAction));
    }

    private void Discard()
    {
        if (_conflictDetail is { } latest)
            Apply(latest);
        else if (Detail is { } detail)
            Apply(detail);
        else
        { IsEditing = false; IsDirty = false; }
        StatusText = "Local edits discarded; retained records are unchanged.";
    }

    private async Task SaveAsync()
    {
        await RunAsync(async token =>
        {
            var draft = BuildDraft();
            var action = Detail is null ? "create" : Detail.Proposals.Any(p => p.ProposalId == _proposalId) ? "save" : "propose";
            await ExecuteAsync(action, draft, token);
        });
    }

    private AsyncRelayCommand Decision(string action, Func<bool> capability) => new(
        () => RunAsync(token => ExecuteAsync(action, null, token)), () => !IsBusy && !IsDirty && capability());

    private async Task ExecuteAsync(string action, IntercompanyAgreementDraftDto? draft, CancellationToken token)
    {
        var proposal = Detail?.Proposals.LastOrDefault(p => p.State is IntercompanyAgreementProposalStateDto.Draft or IntercompanyAgreementProposalStateDto.Submitted);
        var command = new IntercompanyAgreementCommandDto(Guid.Empty, _agreementId, Detail?.AggregateVersion ?? 0,
            draft is null ? proposal?.ProposalId ?? _proposalId : _proposalId, draft, Rationale, proposal?.SubmittedHash);
        var key = action + JsonSerializer.Serialize(command);
        if (key != _retryKey)
        { _retryKey = key; _retryId = Guid.NewGuid(); }
        var response = await _client.ExecuteAsync(action, command with { CommandId = _retryId }, token);
        token.ThrowIfCancellationRequested();
        var result = Require(response);
        Apply(result.Detail);
        var old = Agreements.FirstOrDefault(a => a.AgreementId == result.Detail.AgreementId);
        if (old is not null)
            Agreements[Agreements.IndexOf(old)] = result.Detail;
        else
            Agreements.Add(result.Detail);
        StatusText = $"{result.Receipt.Action} retained at version {result.Detail.AggregateVersion}.";
    }

    private IntercompanyAgreementDraftDto BuildDraft()
    {
        decimal? rate = string.IsNullOrWhiteSpace(FixedAnnualRate) ? null : decimal.TryParse(FixedAnnualRate, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsedRate)
            ? parsedRate : throw new InvalidOperationException("Annual rate must be a decimal fraction, for example 0.05.");
        int? days = string.IsNullOrWhiteSpace(SettlementDays) ? null : int.TryParse(SettlementDays, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedDays)
            ? parsedDays : throw new InvalidOperationException("Settlement days must be a nonnegative whole number.");
        var evidence = new List<IntercompanyAgreementEvidenceDto>(_additionalEvidence);
        if (!string.IsNullOrWhiteSpace(EvidenceReference))
            evidence.Insert(0, new(EvidenceReference.Trim(), string.IsNullOrWhiteSpace(EvidenceArtifact) ? null : EvidenceArtifact.Trim()));
        return new(Code, Name, ParseId(PartyOne, "First party"), PartyOneRole, ParseId(PartyTwo, "Second party"), PartyTwoRole,
            AgreementType, Currency, Date(EffectiveFrom, "Effective start"), OptionalDate(EffectiveTo, "Effective end"),
            new(rate, RateDescription, Schedule, Allocation, days, Notes), evidence, _action,
            _action == IntercompanyAgreementActionDto.Terminate ? Date(TerminationDate, "Termination date") : null, Purpose);
    }

    private void Apply(IntercompanyAgreementDetailDto detail)
    {
        Detail = detail;
        _conflictDetail = null;
        OnPropertyChanged(nameof(ConflictSummary));
        ResetJournalLines();
        SelectedRevision = detail.ApplicableRevision ?? detail.Revisions.LastOrDefault();
        _agreementId = detail.AgreementId;
        var pending = detail.Proposals.LastOrDefault(p => p.State is IntercompanyAgreementProposalStateDto.Draft or IntercompanyAgreementProposalStateDto.Submitted);
        var draft = pending?.Draft ?? detail.ApplicableRevision?.Terms ?? detail.Revisions.LastOrDefault()?.Terms;
        IsEditing = false;
        Rationale = pending?.Rationale ?? "";
        if (draft is not null)
            Fill(draft);
        _proposalId = pending?.ProposalId ?? Guid.Empty;
        _action = pending?.Action ?? IntercompanyAgreementActionDto.Amend;
        IsEditing = pending?.State == IntercompanyAgreementProposalStateDto.Draft && detail.Capabilities.CanEdit;
        IsDirty = false;
        OnPropertyChanged(nameof(EditorAction));
        StatusText = detail.Capabilities.BlockingReason ?? $"{detail.Code}: {detail.EffectiveStatus}; inspect the retained versions and evidence before review.";
    }

    private void Fill(IntercompanyAgreementDraftDto draft)
    {
        var wasEditing = IsEditing;
        IsEditing = false;
        Code = draft.Code;
        Name = draft.Name;
        PartyOne = draft.PartyOneId == Guid.Empty ? "" : draft.PartyOneId.ToString("D");
        PartyTwo = draft.PartyTwoId == Guid.Empty ? "" : draft.PartyTwoId.ToString("D");
        PartyOneRole = draft.PartyOneRole;
        PartyTwoRole = draft.PartyTwoRole;
        Currency = draft.Currency;
        AgreementType = draft.AgreementType;
        EffectiveFrom = draft.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        EffectiveTo = draft.EffectiveTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        TerminationDate = draft.TerminationEffectiveDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? AsOf;
        FixedAnnualRate = draft.Terms?.FixedAnnualRate?.ToString(CultureInfo.InvariantCulture) ?? "";
        RateDescription = draft.Terms?.RateDescription ?? "";
        Schedule = draft.Terms?.ScheduleDescription ?? "";
        Allocation = draft.Terms?.AllocationDescription ?? "";
        SettlementDays = draft.Terms?.SettlementDays?.ToString(CultureInfo.InvariantCulture) ?? "";
        Notes = draft.Terms?.Notes ?? "";
        Purpose = draft.Purpose ?? "";
        var first = draft.Evidence?.FirstOrDefault();
        EvidenceReference = first?.Reference ?? "";
        EvidenceArtifact = first?.ArtifactId ?? "";
        _additionalEvidence = draft.Evidence?.Skip(1).ToArray() ?? [];
        IsEditing = wasEditing;
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
                StatusText = $"Agreement operation refused: {ex.Message} Your local input has been retained.";
        }
        finally { if (generation == _sessionGeneration) IsBusy = false; }
    }

    /// <summary>Clear prior-session evidence and invalidate every outstanding response on account changes.</summary>
    public void ResetSession()
    {
        if (_disposed)
            return;
        _sessionGeneration++;
        _sessionCancellation.Cancel();
        _sessionCancellation.Dispose();
        _sessionCancellation = new CancellationTokenSource();
        IsEditing = false;
        IsDirty = false;
        _canCreate = false;
        Agreements.Clear();
        SelectedRow = null;
        Detail = null;
        SelectedRevision = null;
        _agreementId = _proposalId = _retryId = Guid.Empty;
        _cursor = _retryKey = null;
        _conflictDetail = null;
        OnPropertyChanged(nameof(ConflictSummary));
        SetProperty(ref _entity, "", nameof(EntityFilter));
        LedgerBook = "";
        Fill(new("", "", Guid.Empty, "", Guid.Empty, "", IntercompanyAgreementTypeDto.Loan, "", DateOnly.FromDateTime(DateTime.UtcNow)));
        Rationale = "";
        ResetJournalLines();
        IsBusy = false;
        StatusText = "Session changed. Load agreements for the signed-in account.";
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
            response.StatusCode == 409 ? "The retained version changed. Discard local edits and reopen to compare before retrying."
            : response.StatusCode is 401 or 403 ? "Your signed-in account cannot perform this action."
            : response.StatusCode == 404 ? "The agreement or evidence is unavailable in this scope."
            : response.ErrorMessage ?? "The agreement service is unavailable.");
    private static Guid ParseId(string value, string label) => Guid.TryParse(value, out var id) && id != Guid.Empty ? id : throw new InvalidOperationException($"{label} must be a valid nonempty ID.");
    private static DateOnly Date(string value, string label) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new InvalidOperationException($"{label} must use yyyy-MM-dd.");
    private static DateOnly? OptionalDate(string value, string label) => string.IsNullOrWhiteSpace(value) ? null : Date(value, label);
    private static string RevisionSummary(IntercompanyAgreementRevisionDto? revision)
    {
        if (revision is null)
            return "No retained revision selected.";
        return $"Version {revision.AgreementVersion} — {revision.Action}\n" + DraftSummary(revision.Terms)
            + $"\nApproved UTC: {revision.ApprovedAtUtc:O}\nApprover: {revision.Approver}\nRationale: {revision.Rationale}";
    }

    private static string DraftSummary(IntercompanyAgreementDraftDto draft)
        => $"{draft.Code}: {draft.Name}\n"
            + $"{draft.PartyOneRole}: {draft.PartyOneId:D}\n{draft.PartyTwoRole}: {draft.PartyTwoId:D}\n"
            + $"Currency: {draft.Currency}\nEffective: {draft.EffectiveFrom:yyyy-MM-dd} to {draft.EffectiveTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "open"} (exclusive)\n"
            + $"Termination: {draft.TerminationEffectiveDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none"}\n"
            + $"Annual rate fraction: {draft.Terms?.FixedAnnualRate?.ToString(CultureInfo.InvariantCulture) ?? "not stated"}\n{draft.Terms?.RateDescription}\n"
            + $"Schedule: {draft.Terms?.ScheduleDescription}\nAllocation: {draft.Terms?.AllocationDescription}\nSettlement days: {draft.Terms?.SettlementDays}\n"
            + $"Terms: {draft.Terms?.Notes}\nPurpose: {draft.Purpose}\n"
            + string.Join("\n", (draft.Evidence ?? []).Select(e => $"Evidence: {e.Reference}; artifact {e.ArtifactId}; hash {e.ContentHash}"));
    private void NotifyCommands()
    {
        LoadCommand?.NotifyCanExecuteChanged();
        MoreCommand?.NotifyCanExecuteChanged();
        OpenCommand?.NotifyCanExecuteChanged();
        CompareLatestCommand?.NotifyCanExecuteChanged();
        NewCommand?.NotifyCanExecuteChanged();
        AmendCommand?.NotifyCanExecuteChanged();
        TerminateCommand?.NotifyCanExecuteChanged();
        ReplaceProposalCommand?.NotifyCanExecuteChanged();
        LoadJournalLinesCommand?.NotifyCanExecuteChanged();
        MoreJournalLinesCommand?.NotifyCanExecuteChanged();
        DiscardCommand?.NotifyCanExecuteChanged();
        SaveCommand?.NotifyCanExecuteChanged();
        SubmitCommand?.NotifyCanExecuteChanged();
        ApproveCommand?.NotifyCanExecuteChanged();
        RejectCommand?.NotifyCanExecuteChanged();
        WithdrawCommand?.NotifyCanExecuteChanged();
    }
}
