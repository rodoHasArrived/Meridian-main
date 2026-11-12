using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

public sealed partial class EvidenceWorkbenchViewModel
{
    private SinglePrincipalAttestationWorkspaceDto? _attestationWorkspace;
    private SinglePrincipalAttestationSubjectSnapshotDto? _attestationPreview;
    private EvidenceAttestationDocumentChoice? _selectedAttestationDocument;
    private PreparerAttestationHistoryRowModel? _selectedAttestationReceipt;
    private PendingAttestationCommand? _pendingAttestationCommand;
    private int _attestationPreviewRevision;
    private bool _isAttestationBusy;
    private bool _isAttestationConfirmed;
    private string _attestationReason = string.Empty;
    private string _declarationReason = string.Empty;
    private string _attestationRevocationReason = string.Empty;
    private string _attestationActionStatus = "Inspect an exact retained source before recording a preparer assertion.";
    private string _attestationPreviewText = "No exact source snapshot inspected.";
    private string _packetAttestationSummaryText = "No packet-specific preparer evidence assessment was returned.";
    private string _attestationExportText = string.Empty;

    public IAsyncRelayCommand PreviewAttestationCommand { get; private set; } = null!;
    public IAsyncRelayCommand ToggleAttestationDeclarationCommand { get; private set; } = null!;
    public IAsyncRelayCommand RecordAttestationCommand { get; private set; } = null!;
    public IAsyncRelayCommand RevokeAttestationCommand { get; private set; } = null!;
    public IAsyncRelayCommand RetryAttestationCommand { get; private set; } = null!;
    public IAsyncRelayCommand ExportAttestationProofCommand { get; private set; } = null!;
    public ObservableCollection<EvidenceAttestationDocumentChoice> AttestationDocuments { get; } = [];
    public ObservableCollection<string> AttestationChecks { get; } = [];
    public ObservableCollection<string> PacketAttestationDisclosures { get; } = [];
    public string PacketAttestationSummaryText { get => _packetAttestationSummaryText; private set => SetProperty(ref _packetAttestationSummaryText, value); }
    public string AttestationConfirmationText => _attestationWorkspace?.State.Confirmation ?? SinglePrincipalAttestationPolicy.Confirmation;
    public string DeclarationActionText => RequiresDeclarationRefresh ? "Reaffirm declaration for current policy"
        : _attestationWorkspace?.State.Declaration?.Enabled == true ? "Withdraw declaration" : "Enable declaration for myself";
    private bool RequiresDeclarationRefresh => _attestationWorkspace is { State.PolicyEnabled: true } workspace && workspace.State.Declaration is { Enabled: true } declaration
        && (declaration.PolicyVersion != workspace.State.PolicyVersion || declaration.PolicyHash != workspace.State.PolicyHash);
    public string AttestationExportText { get => _attestationExportText; private set => SetProperty(ref _attestationExportText, value); }
    public string AttestationActionStatus { get => _attestationActionStatus; private set => SetProperty(ref _attestationActionStatus, value); }
    public string AttestationPreviewText { get => _attestationPreviewText; private set => SetProperty(ref _attestationPreviewText, value); }
    public bool HasPendingAttestationCommand => _pendingAttestationCommand is not null;
    public bool CanEditAttestationInputs => !_isDisposed && !IsBusy && !HasPendingAttestationCommand && _attestationWorkspace is not null;
    public string PendingAttestationCommandText => _attestationWorkspace is null ? string.Empty : _pendingAttestationCommand?.Request switch
    {
        RecordPreparerAttestationRequestDto request => $"Command {request.CommandId:D}: evidence {request.SubjectId}, revision {request.SubjectRevision}; declaration revision {request.ExpectedDeclarationRevision}. {request.Reason}",
        ChangeSinglePrincipalDeclarationRequestDto request => $"Command {request.CommandId:D}: {(request.Enabled ? "enable/reaffirm" : "withdraw")} declaration revision {request.ExpectedRevision} for {request.PrincipalId}. {request.Reason}",
        RevokePreparerAttestationRequestDto request => $"Command {request.CommandId:D}: revoke receipt {request.ReceiptId:D}, revision {request.ExpectedReceiptRevision}. {request.Reason}",
        _ => string.Empty
    };

    public bool IsAttestationBusy
    {
        get => _isAttestationBusy;
        private set { if (SetProperty(ref _isAttestationBusy, value)) NotifyBusyChanged(); }
    }

    public bool IsAttestationConfirmed
    {
        get => _isAttestationConfirmed;
        set { if (SetProperty(ref _isAttestationConfirmed, value)) NotifyAttestationCommands(); }
    }

    public string AttestationReason
    {
        get => _attestationReason;
        set { if (SetProperty(ref _attestationReason, value)) NotifyAttestationCommands(); }
    }

    public string DeclarationReason
    {
        get => _declarationReason;
        set { if (SetProperty(ref _declarationReason, value)) NotifyAttestationCommands(); }
    }

    public string AttestationRevocationReason
    {
        get => _attestationRevocationReason;
        set { if (SetProperty(ref _attestationRevocationReason, value)) NotifyAttestationCommands(); }
    }

    public EvidenceAttestationDocumentChoice? SelectedAttestationDocument
    {
        get => _selectedAttestationDocument;
        set
        {
            if (!CanEditAttestationInputs) return;
            if (SetProperty(ref _selectedAttestationDocument, value)) ClearAttestationPreview();
        }
    }

    public PreparerAttestationHistoryRowModel? SelectedAttestationReceipt
    {
        get => _selectedAttestationReceipt;
        set { if (CanEditAttestationInputs && SetProperty(ref _selectedAttestationReceipt, value)) NotifyAttestationCommands(); }
    }

    private void InitializeAttestationCommands()
    {
        PreviewAttestationCommand = new AsyncRelayCommand(() => _isDisposed ? Task.CompletedTask : PreviewAttestationAsync(_cts.Token),
            CanPreviewAttestation);
        ToggleAttestationDeclarationCommand = new AsyncRelayCommand(() => _isDisposed ? Task.CompletedTask : ChangeAttestationDeclarationAsync(_cts.Token), CanChangeAttestationDeclaration);
        RecordAttestationCommand = new AsyncRelayCommand(() => _isDisposed ? Task.CompletedTask : RecordAttestationAsync(_cts.Token), CanRecordAttestation);
        RevokeAttestationCommand = new AsyncRelayCommand(() => _isDisposed ? Task.CompletedTask : RevokeAttestationAsync(_cts.Token), CanRevokeAttestation);
        RetryAttestationCommand = new AsyncRelayCommand(() => _isDisposed ? Task.CompletedTask : RetryAttestationAsync(_cts.Token),
            () => !_isDisposed && !IsBusy && HasPendingAttestationCommand && _attestationWorkspace is not null);
        ExportAttestationProofCommand = new AsyncRelayCommand(() => _isDisposed ? Task.CompletedTask : ReadAttestationProofExportAsync(_cts.Token),
            CanReadAttestationProofExport);
    }

    private void NotifyAttestationCommands()
    {
        OnPropertyChanged(nameof(CanEditAttestationInputs));
        OnPropertyChanged(nameof(HasPendingAttestationCommand));
        OnPropertyChanged(nameof(PendingAttestationCommandText));
        OnPropertyChanged(nameof(AttestationConfirmationText));
        OnPropertyChanged(nameof(DeclarationActionText));
        PreviewAttestationCommand?.NotifyCanExecuteChanged();
        ToggleAttestationDeclarationCommand?.NotifyCanExecuteChanged();
        RecordAttestationCommand?.NotifyCanExecuteChanged();
        RevokeAttestationCommand?.NotifyCanExecuteChanged();
        RetryAttestationCommand?.NotifyCanExecuteChanged();
        ExportAttestationProofCommand?.NotifyCanExecuteChanged();
    }

    private static bool SameAttestationActor(string? left, string? right) => !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right) && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool HasAttestationReason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Length <= 4000;
    private static bool KnownAttestationPolicy(string version) => version == SinglePrincipalAttestationPolicy.Version
        || version == SinglePrincipalAttestationPolicy.DraftVersion;
    private bool CanPreviewAttestation() => CanEditAttestationInputs && SelectedAttestationDocument is not null;
    private bool CanReadAttestationProofExport() => CanEditAttestationInputs && SelectedAttestationReceipt is not null
        && Guid.TryParse(SelectedAttestationReceipt.ReceiptId, out _);
    private static string AttestationScopeKey(SinglePrincipalAttestationWorkspaceDto workspace) =>
        $"{workspace.State.Scope.TenantId}\n{workspace.State.Scope.CompanyId}\n{workspace.State.Scope.LedgerBookId}\n{workspace.Actor}";

    private bool CanChangeAttestationDeclaration() => CanEditAttestationInputs && HasAttestationReason(DeclarationReason)
        && _attestationWorkspace is { CanAdministerDeclaration: true } workspace
        && KnownAttestationPolicy(workspace.State.PolicyVersion)
        && (workspace.State.PolicyEnabled || workspace.State.Declaration?.Enabled == true)
        && (workspace.State.Declaration is not { Enabled: true } || SameAttestationActor(workspace.Actor, workspace.State.Declaration.PrincipalId));

    private bool CanRecordAttestation() => CanEditAttestationInputs && IsAttestationConfirmed && HasAttestationReason(AttestationReason)
        && _attestationWorkspace is { CanAttest: true, State.PolicyEnabled: true } workspace
        && KnownAttestationPolicy(workspace.State.PolicyVersion)
        && workspace.State.Disclosure == SinglePrincipalAttestationPolicy.Disclosure
        && workspace.State.Confirmation == SinglePrincipalAttestationPolicy.Confirmation
        && workspace.State.AllowedActions.Contains(SinglePrincipalAttestationPolicy.EvidenceAcknowledgment)
        && workspace.State.Declaration is { Enabled: true } declaration && SameAttestationActor(declaration.PrincipalId, workspace.Actor)
        && declaration.PolicyVersion == workspace.State.PolicyVersion && declaration.PolicyHash == workspace.State.PolicyHash
        && _attestationPreview is { Eligible: true } && SelectedAttestationDocument is { } document
        && _attestationPreview.SubjectKind == "evidence-document" && _attestationPreview.SubjectId == document.SubjectId;

    private bool CanRevokeAttestation()
    {
        if (!CanEditAttestationInputs || !HasAttestationReason(AttestationRevocationReason)
            || _attestationWorkspace is not { } workspace || SelectedAttestationReceipt is null
            || !Guid.TryParse(SelectedAttestationReceipt.ReceiptId, out var receiptId)) return false;
        var receipt = workspace.State.Receipts.FirstOrDefault(item => item.ReceiptId == receiptId);
        var mayModify = receipt?.ActionId == SinglePrincipalAttestationPolicy.DraftCheck
            ? workspace.CanRevokeDrafts : receipt?.ActionId == SinglePrincipalAttestationPolicy.EvidenceAcknowledgment && workspace.CanRevoke;
        return mayModify && receipt is not null && receipt.SchemaVersion == SinglePrincipalAttestationPolicy.SchemaVersion
            && receipt.DecisionKind == SinglePrincipalAttestationPolicy.DecisionKind
            && receipt.Disclosure == SinglePrincipalAttestationPolicy.Disclosure && SameAttestationActor(receipt.AttestedBy, workspace.Actor)
            && !workspace.State.Revocations.Any(item => item.ReceiptId == receiptId);
    }

    private void ClearAttestationPreview()
    {
        ++_attestationPreviewRevision;
        _attestationPreview = null;
        IsAttestationConfirmed = false;
        AttestationChecks.Clear();
        AttestationPreviewText = "No exact source snapshot inspected. Inspect again after a document or scope change.";
        NotifyAttestationCommands();
    }

    private void ResetAttestationForRefresh()
    {
        _attestationWorkspace = null;
        _selectedAttestationDocument = null;
        _selectedAttestationReceipt = null;
        AttestationDocuments.Clear();
        AttestationExportText = string.Empty;
        OnPropertyChanged(nameof(SelectedAttestationDocument));
        OnPropertyChanged(nameof(SelectedAttestationReceipt));
        ClearAttestationPreview();
    }

    private void ApplyAttestationWorkspace(SinglePrincipalAttestationWorkspaceDto? workspace)
    {
        _attestationWorkspace = workspace;
        if (workspace is not null && _pendingAttestationCommand is { } pending && pending.ScopeKey != AttestationScopeKey(workspace))
        {
            _pendingAttestationCommand = null;
            AttestationActionStatus = "Scope or principal changed. Inspect the original scope's history for the uncertain command before making another assertion.";
        }
        NotifyAttestationCommands();
    }

    private void ApplyAttestationDocuments(IReadOnlyList<EvidenceVaultDocumentEntryDto> documents)
    {
        AttestationDocuments.Clear();
        foreach (var entry in documents)
            AttestationDocuments.Add(new(entry.VaultId, entry.Document.DocumentId, entry.Document.FileName));
        NotifyAttestationCommands();
    }

    private void ApplyPacketAttestationProjection(PreparerAttestationEvidenceProjectionDto? projection)
    {
        PacketAttestationDisclosures.Clear();
        if (projection is not null && (_attestationWorkspace is not { } workspace
            || !PreparerAttestationEvidencePresentation.MatchesScope(projection, workspace.State.Scope.TenantId,
                workspace.State.Scope.CompanyId, SelectedSubject?.LedgerBookId)))
        {
            PacketAttestationSummaryText = "Preparer evidence classification is unavailable for this exact visible scope. No financial approval or Verified Coverage is established.";
            return;
        }
        var details = PreparerAttestationEvidencePresentation.Describe(projection, out var summary);
        PacketAttestationSummaryText = summary;
        foreach (var detail in details) PacketAttestationDisclosures.Add(detail);
    }

    public async Task PreviewAttestationAsync(CancellationToken ct = default)
    {
        if (_isDisposed || !CanPreviewAttestation() || SelectedAttestationDocument is not { } document) return;
        ClearAttestationPreview();
        var revision = _attestationPreviewRevision;
        IsAttestationBusy = true;
        try
        {
            var response = await _apiClient.GetAttestationSubjectAsync(document.VaultId, document.DocumentId, ct).ConfigureAwait(true);
            if (_isDisposed || revision != _attestationPreviewRevision) return;
            if (!response.Success || response.Data is not { } snapshot)
            {
                AttestationActionStatus = response.ErrorMessage ?? "The exact evidence snapshot is unavailable.";
                return;
            }
            if (snapshot.SubjectKind != "evidence-document" || snapshot.SubjectId != document.SubjectId
                || string.IsNullOrWhiteSpace(snapshot.Hash) || string.IsNullOrWhiteSpace(snapshot.Revision)
                || string.IsNullOrWhiteSpace(snapshot.SourceHash) || string.IsNullOrWhiteSpace(snapshot.SourceArtifactId)
                || string.IsNullOrWhiteSpace(snapshot.SourceProvenance) || snapshot.Checks is not { Count: > 0 })
            {
                AttestationActionStatus = "The returned snapshot does not match the selected document. Refresh and inspect again.";
                return;
            }
            _attestationPreview = snapshot;
            AttestationPreviewText = $"{snapshot.SubjectId}\nRevision: {snapshot.Revision}\nSource SHA-256: {snapshot.SourceHash}\nSource provenance: {snapshot.SourceProvenance}\nSource actor: {snapshot.SourceActor ?? "unrecorded"}";
            foreach (var check in snapshot.Checks) AttestationChecks.Add(check);
            AttestationActionStatus = snapshot.Eligible
                ? "Exact retained source inspected. Confirm preparer responsibility; this does not approve the document or any financial action."
                : "This source is not eligible. Resolve its retained evidence checks before attesting.";
        }
        catch (OperationCanceledException) { AttestationActionStatus = "Evidence inspection was cancelled. Inspect again before confirming."; }
        catch (Exception) { AttestationActionStatus = "The evidence preview is unavailable. Inspect again before confirming."; }
        finally { IsAttestationBusy = false; }
    }

    public Task ChangeAttestationDeclarationAsync(CancellationToken ct = default)
    {
        if (_isDisposed || !CanChangeAttestationDeclaration() || _attestationWorkspace is not { } workspace) return Task.CompletedTask;
        return SubmitAttestationCommandAsync(new(AttestationScopeKey(workspace), new ChangeSinglePrincipalDeclarationRequestDto(
            Guid.NewGuid(), workspace.State.Declaration?.Revision ?? 0, RequiresDeclarationRefresh || workspace.State.Declaration?.Enabled != true,
            workspace.Actor, workspace.State.PolicyVersion, DeclarationReason.Trim())), ct);
    }

    public Task RecordAttestationAsync(CancellationToken ct = default)
    {
        if (_isDisposed || !CanRecordAttestation() || _attestationWorkspace is not { } workspace || _attestationPreview is not { } preview) return Task.CompletedTask;
        return SubmitAttestationCommandAsync(new(AttestationScopeKey(workspace), new RecordPreparerAttestationRequestDto(
            Guid.NewGuid(), workspace.State.Declaration!.Revision, SinglePrincipalAttestationPolicy.EvidenceAcknowledgment,
            preview.SubjectKind, preview.SubjectId, preview.Revision, preview.Hash, AttestationReason.Trim(), workspace.State.Confirmation)), ct);
    }

    public Task RevokeAttestationAsync(CancellationToken ct = default)
    {
        if (_isDisposed || !CanRevokeAttestation() || _attestationWorkspace is not { } workspace) return Task.CompletedTask;
        return SubmitAttestationCommandAsync(new(AttestationScopeKey(workspace), new RevokePreparerAttestationRequestDto(
            Guid.NewGuid(), Guid.Parse(SelectedAttestationReceipt!.ReceiptId), 1, AttestationRevocationReason.Trim())), ct);
    }

    public Task RetryAttestationAsync(CancellationToken ct = default) => !_isDisposed && !IsBusy && _pendingAttestationCommand is { } pending
        ? SubmitAttestationCommandAsync(pending, ct) : Task.CompletedTask;

    public async Task ReadAttestationProofExportAsync(CancellationToken ct = default)
    {
        if (_isDisposed || !CanReadAttestationProofExport()
            || !Guid.TryParse(SelectedAttestationReceipt?.ReceiptId, out var receiptId)) return;
        var revision = _loadRevision;
        AttestationExportText = string.Empty;
        IsAttestationBusy = true;
        try
        {
            var response = await _apiClient.ReadAttestationProofExportAsync(receiptId, ct).ConfigureAwait(true);
            if (_isDisposed || revision != _loadRevision) return;
            if (response.Success && response.Data is not null)
            {
                AttestationExportText = response.Data;
                AttestationActionStatus = "Original retained receipt proof loaded. Current revocation and applicability remain separate from this frozen JSON.";
            }
            else AttestationActionStatus = response.ErrorMessage ?? "Original retained proof is unavailable.";
        }
        catch (Exception) { AttestationActionStatus = "Original retained proof is unavailable. No history was changed."; }
        finally { IsAttestationBusy = false; }
    }

    private async Task SubmitAttestationCommandAsync(PendingAttestationCommand pending, CancellationToken ct)
    {
        if (IsBusy || _attestationWorkspace is not { } workspace || AttestationScopeKey(workspace) != pending.ScopeKey) return;
        _pendingAttestationCommand = pending;
        var revision = _loadRevision;
        IsAttestationBusy = true;
        ClearAttestationPreview();
        try
        {
            var outcome = pending.Request switch
            {
                ChangeSinglePrincipalDeclarationRequestDto request => Outcome(await _apiClient.ChangeAttestationDeclarationAsync(request, ct).ConfigureAwait(true)),
                RecordPreparerAttestationRequestDto request => Outcome(await _apiClient.RecordAttestationAsync(request, ct).ConfigureAwait(true)),
                RevokePreparerAttestationRequestDto request => Outcome(await _apiClient.RevokeAttestationAsync(request, ct).ConfigureAwait(true)),
                _ => new AttestationMutationOutcome(false, 400, "Unsupported preparer command.")
            };
            if (_isDisposed) return;
            if (revision != _loadRevision || _attestationWorkspace is null || AttestationScopeKey(_attestationWorkspace) != pending.ScopeKey)
            {
                var uncertain = outcome.StatusCode is 0 or 408 || outcome.StatusCode >= 500;
                if (!uncertain || (_attestationWorkspace is not null && AttestationScopeKey(_attestationWorkspace) != pending.ScopeKey))
                    _pendingAttestationCommand = null;
                AttestationActionStatus = outcome.Success
                    ? "The original command completed in its original scope. The current context was preserved; refresh the original history to inspect it."
                    : uncertain
                        ? "The original command's outcome is uncertain. Inspect or retry it in the original scope; no result was applied to the current context."
                        : "The original command was refused. No result was applied to the current context.";
                return;
            }
            if (outcome.Success)
            {
                _pendingAttestationCommand = null;
                AttestationReason = DeclarationReason = AttestationRevocationReason = string.Empty;
                AttestationActionStatus = "Server retained the command. Original receipts remain distinct from approval and current support.";
                await RefreshAsync(ct).ConfigureAwait(true);
            }
            else if (outcome.StatusCode is 0 or 408 || outcome.StatusCode >= 500)
                AttestationActionStatus = "The outcome is uncertain. Retry the same retained command; do not submit a new assertion. " + outcome.Error;
            else
            {
                _pendingAttestationCommand = null;
                AttestationActionStatus = "The command was refused. Refresh authority and inspect the exact source again. " + outcome.Error;
                // A definite refusal must never leave stale declaration authority available locally.
                await RefreshAsync(ct).ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            AttestationActionStatus = "The command outcome is uncertain. Retry the same retained command or inspect its original scope's history.";
        }
        finally { IsAttestationBusy = false; }
    }

    private static AttestationMutationOutcome Outcome<T>(ApiResponse<T> response) where T : class =>
        new(response.Success && response.Data is not null, response.Success && response.Data is null ? 0 : response.StatusCode, response.ErrorMessage);
    private sealed record PendingAttestationCommand(string ScopeKey, object Request);
    private sealed record AttestationMutationOutcome(bool Success, int StatusCode, string? Error);
}

public sealed record EvidenceAttestationDocumentChoice(string VaultId, string DocumentId, string Label)
{
    public string SubjectId => $"{VaultId}/{DocumentId}";
}
