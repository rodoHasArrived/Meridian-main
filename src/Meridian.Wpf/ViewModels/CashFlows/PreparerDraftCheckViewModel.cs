using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels.CashFlows;

/// <summary>Checks only an already-retained, server-verified calculation; never recalculates or posts.</summary>
public sealed class PreparerDraftCheckViewModel : BindableBase
{
    private readonly IPreparerDraftCheckApiClient _client;
    private Guid? _securityId;
    private Guid? _runId;
    private int _revision;
    private bool _busy;
    private bool _confirmed;
    private string _reason = string.Empty;
    private string _revocationReason = string.Empty;
    private string _status = "Retain or explicitly load an existing Security calculation to inspect its exact non-postable draft.";
    private string _snapshotText = string.Empty;
    private SinglePrincipalAttestationWorkspaceDto? _workspace;
    private SinglePrincipalAttestationSubjectSnapshotDto? _snapshot;
    private object? _pending;
    private string? _pendingContext;
    private DraftCheckReceiptChoice? _selectedReceipt;

    public PreparerDraftCheckViewModel(IPreparerDraftCheckApiClient client)
    {
        _client = client;
        InspectCommand = new AsyncRelayCommand(InspectAsync, () => !IsBusy && _securityId.HasValue && _runId.HasValue);
        RecordCommand = new AsyncRelayCommand(RecordAsync, CanRecord);
        RevokeCommand = new AsyncRelayCommand(RevokeAsync, CanRevoke);
        RetryCommand = new AsyncRelayCommand(RetryAsync, () => !IsBusy && _pending is not null && CurrentContext == _pendingContext);
        OpenDeclarationCommand = new RelayCommand(() => NavigationService.Instance.NavigateTo("EvidenceWorkbench"));
    }
    public IAsyncRelayCommand InspectCommand { get; }
    public IAsyncRelayCommand RecordCommand { get; }
    public IAsyncRelayCommand RevokeCommand { get; }
    public IAsyncRelayCommand RetryCommand { get; }
    public IRelayCommand OpenDeclarationCommand { get; }
    public ObservableCollection<string> Checks { get; } = [];
    public ObservableCollection<string> History { get; } = [];
    public ObservableCollection<DraftCheckReceiptChoice> RevocableReceipts { get; } = [];
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) Notify(); } }
    public bool CanEdit => !IsBusy && _pending is null && _workspace is not null;
    public bool HasPendingCommand => _pending is not null;
    public string PendingCommandText => _pending switch
    {
        RecordPreparerAttestationRequestDto request => $"Original command {request.CommandId:D}; retained target {request.SubjectId}; revision {request.SubjectRevision}.",
        RevokePreparerAttestationRequestDto request => $"Original corrective command {request.CommandId:D}; receipt {request.ReceiptId:D}, revision {request.ExpectedReceiptRevision}.",
        _ => string.Empty
    };
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string SnapshotText { get => _snapshotText; private set => SetProperty(ref _snapshotText, value); }
    public string Confirmation => SinglePrincipalAttestationPolicy.DraftConfirmation;
    public string Reason { get => _reason; set { if (SetProperty(ref _reason, value)) Notify(); } }
    public string RevocationReason { get => _revocationReason; set { if (SetProperty(ref _revocationReason, value)) Notify(); } }
    public bool IsConfirmed { get => _confirmed; set { if (SetProperty(ref _confirmed, value)) Notify(); } }
    public DraftCheckReceiptChoice? SelectedReceipt { get => _selectedReceipt; set { if (SetProperty(ref _selectedReceipt, value)) Notify(); } }
    public string TargetText => _securityId.HasValue && _runId.HasValue ? $"Retained Security run {_runId:D}; security {_securityId:D}. Current scenario edits do not become attestation operands."
        : "No exact retained Security run is selected.";
    private string? SubjectId => _securityId.HasValue && _runId.HasValue ? $"Security/{_securityId:D}/{_runId:D}" : null;
    private string? CurrentContext => _workspace is null || SubjectId is null ? null
        : $"{_workspace.State.Scope.TenantId}\n{_workspace.State.Scope.CompanyId}\n{_workspace.State.Scope.LedgerBookId}\n{_workspace.Actor}\n{SubjectId}";

    public void SetRetainedTarget(Guid? securityId, Guid? runId)
    {
        ++_revision;
        _securityId = securityId == Guid.Empty ? null : securityId;
        _runId = runId == Guid.Empty ? null : runId;
        ClearRead();
        Status = _pending is null ? "Inspect the exact retained run. This does not recalculate, approve or post."
            : "An original draft-check command is uncertain. Return to that retained run and inspect its history before retrying.";
        OnPropertyChanged(nameof(TargetText));
        Notify();
    }
    private void ClearRead()
    {
        _workspace = null; _snapshot = null; IsConfirmed = false; SnapshotText = string.Empty;
        Checks.Clear(); History.Clear(); RevocableReceipts.Clear(); SelectedReceipt = null;
        Notify();
    }
    public async Task InspectAsync()
    {
        if (IsBusy || _securityId is not { } security || _runId is not { } run) return;
        var revision = ++_revision;
        ClearRead();
        IsBusy = true;
        try
        {
            var workspaceTask = _client.GetDraftCheckWorkspaceAsync();
            var snapshotTask = _client.InspectDraftCheckAsync(security, run);
            var workspace = await ReadOrUnavailable(workspaceTask, "Draft-check authority unavailable.").ConfigureAwait(true);
            var snapshot = await ReadOrUnavailable(snapshotTask, "Exact retained draft is unavailable.").ConfigureAwait(true);
            if (revision != _revision) return;
            if (!workspace.Success || workspace.Data is null) { Status = workspace.ErrorMessage ?? "Draft-check authority unavailable."; return; }
            _workspace = workspace.Data;
            if (snapshot.Success && snapshot.Data is { } source && source.SubjectKind == "contractual-cashflow-draft"
                && source.SubjectId == SubjectId && !string.IsNullOrWhiteSpace(source.Hash) && !string.IsNullOrWhiteSpace(source.Revision)
                && source.Checks is { Count: > 0 } && Same(source.PreparedBy, _workspace.Actor))
            {
                _snapshot = source;
                SnapshotText = $"Exact retained draft {source.SubjectId}; revision {source.Revision}; hash {source.Hash}; original preparer {source.PreparedBy}; provenance {source.SourceProvenance}.";
                foreach (var check in source.Checks) Checks.Add(check);
            }
            var receipts = _workspace.State.Receipts.Where(item => item.ActionId == SinglePrincipalAttestationPolicy.DraftCheck
                && item.SubjectKind == "contractual-cashflow-draft" && item.SubjectId == SubjectId && item.Scope == _workspace.State.Scope);
            foreach (var receipt in receipts)
            {
                var known = receipt.SchemaVersion == SinglePrincipalAttestationPolicy.SchemaVersion && receipt.DecisionKind == SinglePrincipalAttestationPolicy.DecisionKind
                    && receipt.Disclosure == SinglePrincipalAttestationPolicy.Disclosure;
                var revoked = _workspace.State.Revocations.FirstOrDefault(item => item.ReceiptId == receipt.ReceiptId);
                History.Add($"{(known ? SinglePrincipalAttestationPolicy.Disclosure : "Review classification unavailable")}\nReceipt {receipt.ReceiptId:D} by {receipt.AttestedBy}; revision {receipt.SubjectRevision}; "
                    + $"source provenance {receipt.SourceProvenance}. " + (revoked is null ? "Historical assertion; current support is separately assessed." : $"Revoked: {revoked.Reason}. Original record retained."));
                if (known && revoked is null && Same(receipt.AttestedBy, _workspace.Actor) && _workspace.CanRevokeDrafts)
                    RevocableReceipts.Add(new(receipt.ReceiptId, $"{receipt.ReceiptId:D} · {receipt.RecordedAt:O}"));
            }
            Status = _snapshot is null ? snapshot.ErrorMessage ?? "Exact draft support is unavailable; existing receipts remain historical only."
                : _snapshot.Eligible ? "Exact retained non-postable draft inspected. A preparer check does not satisfy financial approval or coverage."
                : "The retained draft is not eligible for a successful preparer check.";
        }
        catch (Exception) { if (revision == _revision) Status = "Retained draft inspection is unavailable. No source or authority was inferred."; }
        finally { IsBusy = false; Notify(); }
    }
    private bool CanRecord() => CanEdit && _workspace is { CanAttestDrafts: true, State.PolicyEnabled: true } workspace
        && workspace.State.PolicyVersion == SinglePrincipalAttestationPolicy.DraftVersion
        && workspace.State.Disclosure == SinglePrincipalAttestationPolicy.Disclosure
        && workspace.State.AllowedActions.Contains(SinglePrincipalAttestationPolicy.DraftCheck)
        && workspace.State.Declaration is { Enabled: true } declaration && Same(declaration.PrincipalId, workspace.Actor)
        && declaration.PolicyVersion == SinglePrincipalAttestationPolicy.DraftVersion && declaration.PolicyHash == workspace.State.PolicyHash
        && _snapshot is { Eligible: true, Confirmation: SinglePrincipalAttestationPolicy.DraftConfirmation }
        && Same(_snapshot.PreparedBy, workspace.Actor)
        && IsConfirmed && ReasonValid(Reason);
    private bool CanRevoke() => CanEdit && _workspace?.CanRevokeDrafts == true && SelectedReceipt is not null
        && RevocableReceipts.Contains(SelectedReceipt) && ReasonValid(RevocationReason);
    public Task RecordAsync() => !CanRecord() || _workspace is null || _snapshot is null ? Task.CompletedTask
        : SubmitAsync(new RecordPreparerAttestationRequestDto(Guid.NewGuid(), _workspace.State.Declaration!.Revision, SinglePrincipalAttestationPolicy.DraftCheck,
            _snapshot.SubjectKind, _snapshot.SubjectId, _snapshot.Revision, _snapshot.Hash, Reason.Trim(), SinglePrincipalAttestationPolicy.DraftConfirmation), CurrentContext!);
    public Task RevokeAsync() => !CanRevoke() ? Task.CompletedTask : SubmitAsync(
        new RevokePreparerAttestationRequestDto(Guid.NewGuid(), SelectedReceipt!.ReceiptId, 1, RevocationReason.Trim()), CurrentContext!);
    public Task RetryAsync() => !IsBusy && _pending is not null && CurrentContext == _pendingContext && _pendingContext is not null
        ? SubmitAsync(_pending, _pendingContext) : Task.CompletedTask;
    private async Task SubmitAsync(object command, string context)
    {
        if (IsBusy || CurrentContext != context) return;
        var revision = _revision;
        _pending = command; _pendingContext = context; IsConfirmed = false; IsBusy = true;
        try
        {
            var outcome = command switch
            {
                RecordPreparerAttestationRequestDto request => Result(await _client.RecordDraftCheckAsync(request).ConfigureAwait(true)),
                RevokePreparerAttestationRequestDto request => Result(await _client.RevokeDraftCheckAsync(request).ConfigureAwait(true)),
                _ => (Success: false, Code: 400, Error: (string?)"Unknown draft-check command")
            };
            var uncertain = outcome.Code is 0 or 408 || outcome.Code >= 500;
            if (revision != _revision || CurrentContext != context)
            {
                if (!uncertain) _pending = null;
                Status = "The original retained-run command completed or is uncertain in its original context. Inspect that run's retained history; the new context is unchanged.";
                return;
            }
            if (outcome.Success)
            {
                _pending = null; _pendingContext = null;
                Reason = RevocationReason = string.Empty; ClearRead();
                Status = "Server retained the exact non-posting command. Inspect the same run again for its updated historical proof.";
            }
            else if (uncertain) Status = "Command outcome is uncertain. Retry its same retained identity and exact original operands. " + outcome.Error;
            else { _pending = null; _pendingContext = null; ClearRead(); Status = "Draft-check command refused. Inspect fresh authority and the same retained run again. " + outcome.Error; }
        }
        catch (Exception) { Status = "The original draft-check command outcome is uncertain. Inspect its original run and retry the same identity."; }
        finally { IsBusy = false; Notify(); }
    }
    private void Notify()
    {
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(HasPendingCommand)); OnPropertyChanged(nameof(PendingCommandText));
        InspectCommand?.NotifyCanExecuteChanged(); RecordCommand?.NotifyCanExecuteChanged(); RevokeCommand?.NotifyCanExecuteChanged(); RetryCommand?.NotifyCanExecuteChanged();
    }
    private static bool Same(string? left, string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool ReasonValid(string reason) => !string.IsNullOrWhiteSpace(reason) && reason.Length <= 4000;
    private static (bool Success, int Code, string? Error) Result<T>(ApiResponse<T> result) where T : class
        => (result.Success && result.Data is not null, result.Success && result.Data is null ? 0 : result.StatusCode, result.ErrorMessage);
    private static async Task<ApiResponse<T>> ReadOrUnavailable<T>(Task<ApiResponse<T>> task, string message) where T : class
    {
        try { return await task.ConfigureAwait(true); }
        catch (Exception) { return ApiResponse<T>.Fail(message); }
    }
}

public sealed record DraftCheckReceiptChoice(Guid ReceiptId, string Label);
