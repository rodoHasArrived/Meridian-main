using Meridian.Contracts.Integrity;
using System.Text;
using Meridian.Contracts.Ledger;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class FactorPaydownViewModel
{
    private AssetAccountingDraftCapabilitiesDto? _capabilities;
    private string? _capabilityActor;
    private long _principalGeneration;
    private JournalEntryLifecycleActionRequestDto? _approvalCommand;
    private JournalEntryLifecycleActionRequestDto? _postCommand;
    private PostedLedgerJournalEntryResultDto? _postedJournal;
    private bool _approvalConfirmed;
    private bool _postingConfirmed;
    private string _approvalNotes = string.Empty;
    private string _approvalStateText = "Load current server authorization before approving or posting. Missing support is read-only.";
    private string _approvalProofText = "No retained approval receipt loaded.";
    private bool _approvalProofVerified;

    public IAsyncRelayCommand ReloadApprovalStateCommand { get; }
    public IAsyncRelayCommand ApproveRetainedDraftCommand { get; }
    public IAsyncRelayCommand PostRetainedDraftCommand { get; }
    public string ApprovalStateText { get => _approvalStateText; private set => SetProperty(ref _approvalStateText, value); }
    public string ApprovalProofText { get => _approvalProofText; private set => SetProperty(ref _approvalProofText, value); }
    public string ApprovalNotes
    {
        get => _approvalNotes;
        set
        {
            if (_approvalCommand is not null) { RaisePropertyChanged(nameof(ApprovalNotes)); return; }
            if (!SetProperty(ref _approvalNotes, value)) return;
            _approvalCommand = null;
            ApprovalConfirmed = false;
        }
    }
    public bool ApprovalConfirmed { get => _approvalConfirmed; set { SetProperty(ref _approvalConfirmed, value); NotifyCommands(); } }
    public bool PostingConfirmed { get => _postingConfirmed; set { SetProperty(ref _postingConfirmed, value); NotifyCommands(); } }
    public bool CanEditApprovalNotes => !IsBusy && _approvalCommand is null;
    public bool CanApproveRetainedDraft => !IsBusy && CurrentCapabilitiesMatch() && _capabilities!.CanApprove
        && ApprovalConfirmed && !string.IsNullOrWhiteSpace(ApprovalNotes);
    public bool CanPostRetainedDraft => !IsBusy && CurrentCapabilitiesMatch() && _capabilities!.CanPost
        && _approvalProofVerified && PostingConfirmed;
    public string PendingCommandText => (_postCommand ?? _approvalCommand) is { } command
        ? $"{command.Action} · command {command.CorrelationId} · draft version {command.Version}"
        : "No pending approval or posting command.";
    public string CanonicalPostingText => _postedJournal is { } posted
        ? $"Canonical journal {posted.JournalEntryId:D} · book {posted.LedgerBookId:D} · period {posted.PeriodId:D}\n"
          + $"Command {posted.CommandId:D} · source event {posted.SourceEventId:D} · sequence {posted.GlobalSequence}\nPosted {posted.PostedAtUtc:O} · idempotency {posted.IdempotencyKey}"
        : "No canonical posting receipt loaded. Draft and approval states do not establish a posted journal.";

    private static bool SamePrincipal(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private bool CurrentCapabilitiesMatch() => _capabilities is { ProtocolVersion: 1 } capability && SelectedRetainedDraft is { } draft
        && capability.JournalEntryId == draft.JournalEntryId && capability.DraftVersion == draft.Version
        && capability.FundProfileId == draft.FundProfileId && capability.LedgerBookId == draft.LedgerBookId
        && capability.PeriodId.ToString("D") == draft.PeriodId
        && !string.IsNullOrWhiteSpace(capability.AuthenticatedActor)
        && SamePrincipal(capability.AuthenticatedActor, _capabilityActor)
        && !string.IsNullOrWhiteSpace(capability.CandidateFingerprint)
        && capability.CandidateFingerprint == draft.AssetAccountingDraft?.Spine.DraftedCandidateFingerprint;

    private void InvalidateApprovalState(bool clearCommands = false)
    {
        _capabilities = null;
        _approvalProofVerified = false;
        _approvalConfirmed = false;
        _postingConfirmed = false;
        ApprovalStateText = "Load current server authorization before approving or posting. Missing support is read-only.";
        ApprovalProofText = "No retained approval receipt loaded.";
        if (clearCommands)
        {
            _approvalCommand = null;
            _postCommand = null;
            _postedJournal = null;
            _capabilityActor = null;
            _approvalNotes = string.Empty;
            Interlocked.Increment(ref _principalGeneration);
            RaisePropertyChanged(nameof(ApprovalNotes));
        }
        RaisePropertyChanged(nameof(ApprovalConfirmed));
        RaisePropertyChanged(nameof(PostingConfirmed));
        RaisePropertyChanged(nameof(PendingCommandText));
        RaisePropertyChanged(nameof(CanonicalPostingText));
        NotifyCommands();
    }

    private async Task ReloadApprovalStateAsync()
    {
        if (SelectedRetainedDraft is not { } draft || _client is null) return;
        InvalidateApprovalState();
        var generation = _generation;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        IsBusy = true;
        try { await LoadApprovalStateCoreAsync(draft, generation, operation.Token); }
        catch (OperationCanceledException) { if (generation == _generation) ApprovalStateText = "Authorization read cancelled. Reload before acting."; }
        catch (Exception ex) { if (generation == _generation) { InvalidateApprovalState(); ApprovalStateText = $"Approval and posting remain read-only: {ex.Message}"; } }
        finally { if (generation == _generation) { _operation = null; IsBusy = false; } }
    }

    private async Task LoadApprovalStateCoreAsync(ManualJournalEntryDraftDto draft, long generation, CancellationToken ct)
    {
        var capability = await _client!.GetCapabilitiesAsync(draft.FundProfileId, draft.LedgerBookId!.Value, draft.JournalEntryId, ct);
        if (generation != _generation || SelectedRetainedDraft?.JournalEntryId != draft.JournalEntryId) return;
        if (capability is null)
        {
            ApprovalStateText = "This server does not report approval/posting support. The selected draft is read-only.";
            return;
        }
        if (string.IsNullOrWhiteSpace(capability.AuthenticatedActor))
            throw new InvalidOperationException("The server did not report the authenticated principal. Approval and posting remain read-only.");
        if ((_capabilityActor is { } actor && !SamePrincipal(actor, capability.AuthenticatedActor))
            || (_approvalCommand is { } approved && !SamePrincipal(approved.Actor, capability.AuthenticatedActor))
            || (_postCommand is { } postedCommand && !SamePrincipal(postedCommand.Actor, capability.AuthenticatedActor)))
        {
            _approvalCommand = null;
            _postCommand = null;
            _approvalNotes = string.Empty;
            _approvalConfirmed = false;
            _postingConfirmed = false;
            Interlocked.Increment(ref _principalGeneration);
            RaisePropertyChanged(nameof(ApprovalNotes));
            RaisePropertyChanged(nameof(ApprovalConfirmed));
            RaisePropertyChanged(nameof(PostingConfirmed));
        }
        _capabilityActor = capability.AuthenticatedActor;
        _capabilities = capability;
        if (!CurrentCapabilitiesMatch())
        {
            _capabilities = null;
            throw new InvalidOperationException("Authorization protocol, identity or version differs from the selected draft. Reload retained drafts and authorization.");
        }
        if (capability.HasPendingPost != (capability.PendingPostCommand is not null))
            throw new InvalidOperationException("The retained pending-post state is incomplete. Posting remains blocked.");
        if (capability.PendingPostCommand is { } pending)
        {
            if (pending.Action != JournalEntryLifecycleActionDto.Post || pending.JournalEntryId != draft.JournalEntryId
                || pending.FundProfileId != draft.FundProfileId || pending.LedgerBookId != draft.LedgerBookId
                || string.IsNullOrWhiteSpace(pending.CorrelationId) || !SamePrincipal(pending.Actor, capability.AuthenticatedActor))
                throw new InvalidOperationException("The pending posting command does not match the selected retained draft.");
            _postCommand = pending;
        }
        if (capability.Approval is { } approval)
        {
            var bytes = Convert.FromBase64String(approval.CanonicalContentBase64);
            var hash = Sha256Digest.Compute(bytes);
            _approvalProofVerified = Sha256Digest.FixedEquals(hash, approval.ContentHashSha256)
                && !string.IsNullOrWhiteSpace(approval.SubjectId) && !string.IsNullOrWhiteSpace(approval.ApprovalId)
                && !string.IsNullOrWhiteSpace(approval.Approver) && approval.ApprovedAtUtc != default
                && approval.CandidateFingerprint == capability.CandidateFingerprint;
            if (!_approvalProofVerified)
                throw new InvalidOperationException("The retained approval content digest or candidate binding does not verify.");
            ApprovalProofText = $"Approval {approval.ApprovalId}\nIndependent approver {approval.Approver} · {approval.ApprovedAtUtc:O}\n"
                + $"Subject {approval.SubjectId}\nCandidate {approval.CandidateFingerprint}\nSHA-256 {approval.ContentHashSha256}\n"
                + Encoding.UTF8.GetString(bytes);
        }
        if (capability.PostedJournal is { } posted)
        {
            if (posted.JournalEntryId != draft.JournalEntryId || posted.LedgerBookId != draft.LedgerBookId || posted.PeriodId != capability.PeriodId)
                throw new InvalidOperationException("The canonical posting receipt differs from the selected draft scope.");
            _postedJournal = posted;
            RaisePropertyChanged(nameof(CanonicalPostingText));
        }
        ApprovalStateText = $"Authenticated actor {capability.AuthenticatedActor}\nApprove: {(capability.CanApprove ? "authorized for the current session" : capability.ApproveBlockedReason ?? "not authorized")}\n"
            + $"Post: {(capability.CanPost ? "authorized for the retained approver" : capability.PostBlockedReason ?? "not authorized")}";
        RaisePropertyChanged(nameof(PendingCommandText));
        NotifyCommands();
    }

    private async Task ApplyRetainedDraftActionAsync(JournalEntryLifecycleActionDto action)
    {
        if ((action == JournalEntryLifecycleActionDto.Approve ? !CanApproveRetainedDraft : !CanPostRetainedDraft)
            || SelectedRetainedDraft is not { } draft) return;
        var command = action == JournalEntryLifecycleActionDto.Approve
            ? (_approvalCommand ??= NewCommand(draft, action, ApprovalNotes.Trim()))
            : (_postCommand ??= NewCommand(draft, action, "Post the retained approved factor paydown through the atomic asset boundary."));
        RaisePropertyChanged(nameof(PendingCommandText));
        var generation = _generation;
        var principalGeneration = _principalGeneration;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        _submittingRetainedDraft = true;
        IsBusy = true;
        try
        {
            var result = await _client!.ApplyLifecycleAsync(command, operation.Token);
            if (generation != _generation || principalGeneration != _principalGeneration
                || !SamePrincipal(_capabilities?.AuthenticatedActor, command.Actor) || SelectedRetainedDraft?.JournalEntryId != draft.JournalEntryId) return;
            if (result.JournalEntry.JournalEntryId != draft.JournalEntryId || result.JournalEntry.Version < draft.Version
                || result.JournalEntry.FundProfileId != draft.FundProfileId
                || result.JournalEntry.LedgerBookId != draft.LedgerBookId
                || result.JournalEntry.AssetAccountingDraft?.Spine.EventId != draft.AssetAccountingDraft?.Spine.EventId)
                throw new InvalidOperationException("The lifecycle result differs from the selected retained draft.");
            var expectedStatus = action == JournalEntryLifecycleActionDto.Approve ? ManualJournalEntryStatusDto.Approved : ManualJournalEntryStatusDto.Posted;
            if (result.JournalEntry.Status != expectedStatus)
                throw new InvalidOperationException("The server did not return the requested approval or posting state.");
            if (action == JournalEntryLifecycleActionDto.Post && (result.PostedJournal is null
                || result.PostedJournal.JournalEntryId != draft.JournalEntryId
                || result.PostedJournal.LedgerBookId != draft.LedgerBookId
                || result.PostedJournal.PeriodId.ToString("D") != draft.PeriodId))
                throw new InvalidOperationException("The server returned no canonical posting receipt. Reload retained state before relying on posting.");
            var index = RetainedDrafts.IndexOf(draft);
            RetainedDrafts[index] = result.JournalEntry;
            SelectedRetainedDraft = result.JournalEntry;
            _postedJournal = result.PostedJournal;
            RaisePropertyChanged(nameof(CanonicalPostingText));
            await LoadApprovalStateCoreAsync(result.JournalEntry, generation, operation.Token);
            if (generation != _generation) return;
            if (!SamePrincipal(_capabilities?.AuthenticatedActor, command.Actor))
            {
                ClearRetainedDraftSelection();
                StatusText = "The authenticated principal changed or could not be rebound. Reload retained drafts and review authorization before another command.";
                return;
            }
            if (action == JournalEntryLifecycleActionDto.Approve && !_approvalProofVerified)
                throw new InvalidOperationException("The approval response has no verified retained approval receipt. Reload authorization before relying on approval.");
            StatusText = action == JournalEntryLifecycleActionDto.Post
                ? $"Factor paydown posted as canonical journal {result.PostedJournal!.JournalEntryId:D}. Lot changes and journal effects share the atomic posting boundary."
                : "Independent approval retained with verified receipt. Posting remains subject to current server authorization.";
        }
        catch (OperationCanceledException) { if (generation == _generation) StatusText = $"{action} request cancelled. Reload authorization; retry preserves the retained command identity."; }
        catch (Exception ex)
        {
            if (generation != _generation) return;
            InvalidateApprovalState();
            StatusText = $"{action} outcome requires review: {ex.Message}. Reload authorization before retrying the same command.";
        }
        finally { _submittingRetainedDraft = false; if (generation == _generation) { _operation = null; IsBusy = false; } }
    }

    private JournalEntryLifecycleActionRequestDto NewCommand(ManualJournalEntryDraftDto draft, JournalEntryLifecycleActionDto action, string notes)
        => new(draft.JournalEntryId, draft.FundProfileId, action, _capabilities!.AuthenticatedActor!, draft.Version, notes,
            CorrelationId: Guid.NewGuid().ToString("D"), LedgerBookId: draft.LedgerBookId);
}
