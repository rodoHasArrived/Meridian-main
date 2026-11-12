using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels.SecurityMaster;

/// <summary>Native C2 controls over exact server-owned cases, policy, entitlement and journal authority.</summary>
public sealed class CashDividendAttestationViewModel : BindableBase, IDisposable
{
    private readonly ICashDividendAttestationApiClient _client;
    private readonly CancellationTokenSource _cts = new();
    private Guid? _securityId;
    private CashDividendCaseChoice? _selectedCase;
    private CorporateActionCashDividendWorkspaceDto? _workspace;
    private PendingCommand? _pending;
    private int _revision;
    private bool _disposed;
    private bool _busy;
    private bool _confirmed;
    private bool _bindingConfirmed;
    private bool _postingConfirmed;
    private string _reason = string.Empty;
    private string _declarationReason = string.Empty;
    private string _postingReason = string.Empty;
    private string _status = "Load retained cases for the selected security. This is a preparer-attestation path, not independent approval.";

    public CashDividendAttestationViewModel(ICashDividendAttestationApiClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        LoadCasesCommand = new AsyncRelayCommand(() => _disposed ? Task.CompletedTask : LoadCasesAsync(_cts.Token), () => !_disposed && !IsBusy && _securityId.HasValue);
        RefreshWorkspaceCommand = new AsyncRelayCommand(() => _disposed ? Task.CompletedTask : RefreshWorkspaceAsync(_cts.Token), () => !_disposed && !IsBusy && SelectedCase is not null);
        ChangeDeclarationCommand = new AsyncRelayCommand(() => _disposed ? Task.CompletedTask : ChangeDeclarationAsync(_cts.Token), CanChangeDeclaration);
        AttestCommand = new AsyncRelayCommand(() => _disposed ? Task.CompletedTask : AttestAsync(_cts.Token), CanAttest);
        PostCommand = new AsyncRelayCommand(() => _disposed ? Task.CompletedTask : PostAsync(_cts.Token), CanPost);
        RetryCommand = new AsyncRelayCommand(() => _disposed ? Task.CompletedTask : RetryAsync(_cts.Token),
            () => !_disposed && !IsBusy && _pending is not null && _workspace is not null && ContextKey(_workspace) == _pending.Context);
    }

    public IAsyncRelayCommand LoadCasesCommand { get; }
    public IAsyncRelayCommand RefreshWorkspaceCommand { get; }
    public IAsyncRelayCommand ChangeDeclarationCommand { get; }
    public IAsyncRelayCommand AttestCommand { get; }
    public IAsyncRelayCommand PostCommand { get; }
    public IAsyncRelayCommand RetryCommand { get; }
    public ObservableCollection<CashDividendCaseChoice> Cases { get; } = [];
    public ObservableCollection<string> Blockers { get; } = [];
    public CorporateActionCashDividendWorkspaceDto? Workspace => _workspace;
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) NotifyCommands(); } }
    public bool CanEdit => !_disposed && !IsBusy && _pending is null && _workspace is not null;
    public bool CanChooseCase => !_disposed && !IsBusy;
    public bool HasPendingCommand => _pending is not null;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Confirmation => _workspace?.Confirmation ?? CorporateActionCashDividendAttestationPolicy.Confirmation;
    public string DeclarationAction => WantsEnableDeclaration ? "Enable/reaffirm this book's declaration" : "Withdraw this book's declaration";
    public string PendingCommandText => _pending is null ? string.Empty : $"Retained {_pending.Kind} command {_pending.Id}: case {_pending.CaseId:D}. Retry preserves the exact confirmed operands.";
    public string ScopeText => _workspace is null ? "Scope unavailable" :
        $"Tenant {_workspace.Scope.TenantId} / company {_workspace.Scope.CompanyId} / book {_workspace.Scope.LedgerBookId} / period {_workspace.Scope.PeriodId}; case {_workspace.Case.CaseId:D}, version {_workspace.Case.Version}.";
    public string LimitsText => _workspace is null ? "Policy limits unavailable" :
        $"Server limits: {Number(_workspace.RelatedEventGrossLimitUsd)} USD gross per related event; {Number(_workspace.BookCalendarMonthGrossLimitUsd)} USD gross per book/calendar month. Related population and retries are governed server-side.";
    public string ProjectionText => _workspace?.Preview is not { } preview ? "No eligible exact accounting preview was returned." :
        $"Projected (not posted): {preview.Projection.Currency}; debits {Number(preview.Projection.TotalDebits)}, credits {Number(preview.Projection.TotalCredits)}. "
        + $"This case gross {Number(preview.Eligibility.GrossImpactUsd)} USD; complete related-event gross {Number(preview.Eligibility.WholeEventGrossUsd)} USD; source ex-date {preview.Eligibility.ExDate:yyyy-MM-dd}; budget month {preview.Eligibility.BudgetMonth:yyyy-MM}. "
        + $"Projection {preview.Projection.ProjectionId:D}; eligibility evidence {_workspace.EvidenceHash}.";
    public string RoleBindingText
    {
        get
        {
            var roles = _workspace?.Preview?.Eligibility.AccountRoleBinding ?? _workspace?.Declaration?.AccountRoleBinding;
            return roles is null ? "No authoritative account-role binding was returned. Declaration enablement is unavailable."
                : $"Existing receivable role: {roles.DividendReceivableAccount} ({roles.DividendReceivableAccountName ?? "name unavailable"}); "
                + $"existing dividend-income role: {roles.DividendIncomeAccount} ({roles.DividendIncomeAccountName ?? "name unavailable"}). "
                + $"Promoted rule {roles.RulePackId}/{roles.RulePackVersion}, {roles.SelectedRuleId}/{roles.SelectedRuleVersion}; book {roles.LedgerBookId:D}. No account or policy is created by this panel.";
        }
    }
    public string DeclarationText => _workspace?.Declaration is not { } declaration ? "No economic declaration is retained."
        : $"Declaration revision {declaration.Revision}: {(declaration.Enabled ? "enabled" : "withdrawn")}; principal {declaration.PrincipalId}; selected book {declaration.LedgerBookId:D}; policy {declaration.PolicyVersion}.";
    public string ReceiptText => _workspace?.Attestation is not { } attestation ? "No preparer attestation is retained for this case."
        : $"{CorporateActionCashDividendAttestationPolicy.Disclosure}\nAttestation {attestation.AttestationId:D} by {attestation.AttestedBy} at {attestation.AttestedAtUtc:O}; "
        + $"declaration revision {attestation.DeclarationRevision}; policy {attestation.PolicyVersion}; "
        + $"{(attestation.IsActive ? "active historical decision" : "voided historical decision")}. {attestation.Reason}";
    public string PostingText => _workspace?.Posting is not { } posting ? "No attested journal posting is retained."
        : !HasExactPostedJournal(posting) ? $"Posting classification unavailable for this exact case and scope. Returned status: {posting.PostingStatus}; no posted financial fact is inferred."
        : $"Posted journal {posting.JournalEntryId:D}: {posting.Currency}; debits {Number(posting.TotalDebits)}, credits {Number(posting.TotalCredits)}; "
        + $"book {posting.LedgerBookId:D}, period {posting.PeriodId:D}, attestation {posting.AttestationId:D}. {CorporateActionCashDividendAttestationPolicy.Disclosure}";

    private bool HasExactPostedJournal(CorporateActionCashDividendPostingDto posting) => _workspace is { } workspace
        && posting.PostingStatus == "Posted" && posting.CaseId == workspace.Case.CaseId && posting.JournalEntryId != Guid.Empty
        && posting.LedgerBookId == BookId && Guid.TryParse(workspace.Scope.PeriodId, out var period) && posting.PeriodId == period
        && posting.AccountingBasis == workspace.Scope.AccountingBasis && posting.Currency == workspace.Scope.FunctionalCurrency
        && workspace.Attestation is { } attestation && posting.AttestationId == attestation.AttestationId && posting.ProjectionId == attestation.ProjectionId;

    public CashDividendCaseChoice? SelectedCase
    {
        get => _selectedCase;
        set
        {
            if (!CanChooseCase || (_pending is not null && value?.CaseId != _pending.CaseId)) return;
            if (SetProperty(ref _selectedCase, value)) { ++_revision; ClearWorkspace(); NotifyCommands(); }
        }
    }
    public string Reason { get => _reason; set { if (SetProperty(ref _reason, value)) NotifyCommands(); } }
    public string DeclarationReason { get => _declarationReason; set { if (SetProperty(ref _declarationReason, value)) NotifyCommands(); } }
    public string PostingReason { get => _postingReason; set { if (SetProperty(ref _postingReason, value)) NotifyCommands(); } }
    public bool IsConfirmed { get => _confirmed; set { if (SetProperty(ref _confirmed, value)) NotifyCommands(); } }
    public bool IsRoleBindingConfirmed { get => _bindingConfirmed; set { if (SetProperty(ref _bindingConfirmed, value)) NotifyCommands(); } }
    public bool IsPostingConfirmed { get => _postingConfirmed; set { if (SetProperty(ref _postingConfirmed, value)) NotifyCommands(); } }

    public void SetSecurity(Guid? securityId)
    {
        if (_disposed || securityId == _securityId) return;
        _securityId = securityId == Guid.Empty ? null : securityId;
        ++_revision;
        Cases.Clear();
        _selectedCase = null;
        OnPropertyChanged(nameof(SelectedCase));
        ClearWorkspace();
        Reason = DeclarationReason = PostingReason = string.Empty;
        Status = _pending is null ? "Security changed. Load its retained cases and inspect an exact C2 workspace."
            : "An original command remains uncertain. Return to its original security/case and inspect retained history before retrying.";
        NotifyCommands();
    }

    public async Task LoadCasesAsync(CancellationToken ct = default)
    {
        if (_disposed || IsBusy || _securityId is not { } securityId) return;
        var revision = ++_revision;
        ClearWorkspace();
        Cases.Clear();
        _selectedCase = null;
        OnPropertyChanged(nameof(SelectedCase));
        IsBusy = true;
        try
        {
            var response = await _client.ListCashDividendCasesAsync(securityId, ct).ConfigureAwait(true);
            if (_disposed || revision != _revision) return;
            if (!response.Success || response.Data is null || response.Data.Any(item => item.SecurityId != securityId))
            { Status = response.ErrorMessage ?? "Retained cases are unavailable or do not match the selected security."; return; }
            foreach (var item in response.Data) Cases.Add(new(item.CaseId, $"{item.State} · book {item.Scope.LedgerBookId ?? "unassigned"} · case {item.CaseId:D}"));
            Status = $"Loaded {Cases.Count} retained case(s), bounded to the first 100. Select a case and refresh its server-owned C2 workspace.";
        }
        catch (Exception) { Status = "Retained cases are unavailable. No authority was inferred."; }
        finally { IsBusy = false; }
    }

    public async Task RefreshWorkspaceAsync(CancellationToken ct = default)
    {
        if (_disposed || IsBusy || SelectedCase is not { } selected || _securityId is not { } securityId) return;
        var revision = ++_revision;
        ClearWorkspace();
        IsBusy = true;
        try
        {
            var response = await _client.GetCashDividendWorkspaceAsync(selected.CaseId, ct).ConfigureAwait(true);
            if (_disposed || revision != _revision) return;
            if (!response.Success || response.Data is not { } workspace)
            { Status = response.ErrorMessage ?? "The cash-dividend workspace is unavailable."; return; }
            if (workspace.SchemaVersion != 1 || workspace.Case.CaseId != selected.CaseId || workspace.Case.SecurityId != securityId
                || workspace.Scope != workspace.Case.Scope || string.IsNullOrWhiteSpace(workspace.Scope.TenantId)
                || string.IsNullOrWhiteSpace(workspace.Scope.CompanyId) || string.IsNullOrWhiteSpace(workspace.Actor))
            { Status = "The workspace does not match this exact case and scope. No command is available."; return; }
            _workspace = workspace;
            foreach (var blocker in workspace.Blockers) Blockers.Add(blocker);
            Status = workspace.Availability != "Available" ? "The C2 service is unavailable; retained facts establish no new authority."
                : workspace.PolicyEnabled ? "Server-owned workspace loaded. Inspect its exact projection, role binding and retained decision."
                : "Economic attestation policy is disabled. New attestations and postings remain unavailable.";
            NotifyWorkspace();
        }
        catch (Exception) { Status = "The exact C2 workspace is unavailable. No command was authorized."; }
        finally { IsBusy = false; }
    }

    private bool KnownPolicy => _workspace is { } workspace && workspace.PolicyVersion == CorporateActionCashDividendAttestationPolicy.Version
        && workspace.PolicyHash == CorporateActionCashDividendAttestationPolicy.PolicyHash
        && workspace.Disclosure == CorporateActionCashDividendAttestationPolicy.Disclosure
        && workspace.Confirmation == CorporateActionCashDividendAttestationPolicy.Confirmation;
    private bool Available => CanEdit && _workspace?.Availability == "Available";
    private Guid? BookId => Guid.TryParse(_workspace?.Scope.LedgerBookId, out var book) && book != Guid.Empty ? book : null;
    private bool WantsEnableDeclaration
    {
        get
        {
            if (_workspace is not { } workspace || workspace.Declaration is not { Enabled: true } declaration) return true;
            var sameBookPrincipal = declaration.LedgerBookId == BookId && SameActor(declaration.PrincipalId, workspace.Actor);
            if (sameBookPrincipal && (!workspace.PolicyEnabled || !KnownPolicy)) return false;
            return !sameBookPrincipal || declaration.PolicyVersion != workspace.PolicyVersion || declaration.PolicyHash != workspace.PolicyHash;
        }
    }
    private bool CanChangeDeclaration() => Available && _workspace is { CanAdministerDeclaration: true } workspace
        && BookId.HasValue && HasReason(DeclarationReason)
        && (!WantsEnableDeclaration || (KnownPolicy && workspace.PolicyEnabled && workspace.CanBindAccountingRoles
            && IsRoleBindingConfirmed && workspace.Preview?.Eligibility.AccountRoleBinding is { } binding && binding.LedgerBookId == BookId));
    private bool CanAttest() => Available && KnownPolicy && _workspace is { PolicyEnabled: true, CanAttestAccounting: true } workspace
        && workspace.Preview is { } preview && preview.Case.CaseId == workspace.Case.CaseId && preview.Case.Version == workspace.Case.Version
        && workspace.Declaration is { Enabled: true } declaration && declaration.LedgerBookId == BookId
        && declaration.PolicyVersion == workspace.PolicyVersion && declaration.PolicyHash == workspace.PolicyHash
        && SameActor(declaration.PrincipalId, workspace.Actor) && workspace.Attestation is not { IsActive: true }
        && !string.IsNullOrWhiteSpace(workspace.EvidenceReference) && !string.IsNullOrWhiteSpace(workspace.EvidenceHash)
        && workspace.Blockers is { Count: 0 } && IsConfirmed && HasReason(Reason);
    private bool CanPost() => Available && KnownPolicy && _workspace is { PolicyEnabled: true, CanPostAttestedAccounting: true, Posting: null } workspace
        && workspace.Attestation is { IsActive: true } attestation && attestation.CaseId == workspace.Case.CaseId
        && attestation.PolicyVersion == workspace.PolicyVersion && attestation.PolicyHash == workspace.PolicyHash
        && attestation.Disclosure == CorporateActionCashDividendAttestationPolicy.Disclosure
        && workspace.Blockers is { Count: 0 } && IsPostingConfirmed && HasReason(PostingReason);

    public Task ChangeDeclarationAsync(CancellationToken ct = default)
    {
        if (!CanChangeDeclaration() || _workspace is not { } workspace) return Task.CompletedTask;
        var enabled = WantsEnableDeclaration;
        var id = Guid.NewGuid();
        var request = new ChangeCashDividendAttestationDeclarationRequestDto(id, workspace.Declaration?.Revision ?? 0,
            workspace.Scope.TenantId, workspace.Scope.CompanyId, BookId!.Value, enabled, workspace.PolicyVersion, workspace.PolicyHash,
            DeclarationReason.Trim(), workspace.Actor, AccountRoleBinding: enabled ? workspace.Preview!.Eligibility.AccountRoleBinding : workspace.Declaration?.AccountRoleBinding);
        return SubmitAsync(new(workspace.Case.CaseId, workspace.Case.SecurityId, ContextKey(workspace), id.ToString("D"), "declaration", request), ct);
    }
    public Task AttestAsync(CancellationToken ct = default)
    {
        if (!CanAttest() || _workspace is not { } workspace) return Task.CompletedTask;
        var id = Guid.NewGuid().ToString("D");
        var request = new AttestCorporateActionCashDividendRequestDto(workspace.Case.CaseId, workspace.Case.Version, id,
            workspace.Scope.TenantId, workspace.Scope.CompanyId, workspace.Preview!.Projection.ProjectionId, workspace.Declaration!.Revision,
            workspace.EvidenceHash!, Reason.Trim(), workspace.EvidenceReference!, workspace.EvidenceHash!, workspace.Confirmation,
            workspace.Actor, workspace.Scope);
        return SubmitAsync(new(workspace.Case.CaseId, workspace.Case.SecurityId, ContextKey(workspace), id, "preparer-attestation", request), ct);
    }
    public Task PostAsync(CancellationToken ct = default)
    {
        if (!CanPost() || _workspace is not { } workspace) return Task.CompletedTask;
        var id = Guid.NewGuid().ToString("D");
        var request = new PostAttestedCorporateActionCashDividendRequestDto(workspace.Case.CaseId, workspace.Case.Version, id,
            workspace.Scope.TenantId, workspace.Scope.CompanyId, workspace.Attestation!.ProjectionId, workspace.Attestation.AttestationId,
            PostingReason.Trim(), workspace.Actor, workspace.Scope);
        return SubmitAsync(new(workspace.Case.CaseId, workspace.Case.SecurityId, ContextKey(workspace), id, "attested-posting", request), ct);
    }
    public Task RetryAsync(CancellationToken ct = default) => !_disposed && !IsBusy && _pending is { } pending
        && _workspace is not null && ContextKey(_workspace) == pending.Context ? SubmitAsync(pending, ct) : Task.CompletedTask;

    private async Task SubmitAsync(PendingCommand pending, CancellationToken ct)
    {
        if (_disposed || IsBusy || _workspace is null || ContextKey(_workspace) != pending.Context) return;
        var revision = _revision;
        _pending = pending;
        IsConfirmed = IsPostingConfirmed = IsRoleBindingConfirmed = false;
        IsBusy = true;
        try
        {
            var outcome = pending.Request switch
            {
                ChangeCashDividendAttestationDeclarationRequestDto request => Result(await _client.ChangeCashDividendDeclarationAsync(pending.CaseId, request, ct).ConfigureAwait(true)),
                AttestCorporateActionCashDividendRequestDto request => Result(await _client.AttestCashDividendAsync(request, ct).ConfigureAwait(true)),
                PostAttestedCorporateActionCashDividendRequestDto request => Result(await _client.PostAttestedCashDividendAsync(request, ct).ConfigureAwait(true)),
                _ => new Outcome(false, 400, "Unsupported C2 command")
            };
            if (_disposed) return;
            var uncertain = outcome.Status is 0 or 408 || outcome.Status >= 500;
            if (revision != _revision || _workspace is null || ContextKey(_workspace) != pending.Context)
            {
                if (!uncertain) _pending = null;
                Status = "The original case command completed or remains uncertain in its original context. No outcome was applied to the current case; inspect its original retained history.";
                return;
            }
            if (outcome.Success)
            {
                _pending = null;
                Reason = DeclarationReason = PostingReason = string.Empty;
                Status = "Server retained the exact command. Refresh this case to inspect its new decision or journal; no independent review is claimed.";
                ClearWorkspace();
            }
            else if (uncertain)
                Status = "Command outcome is uncertain. Retry the same retained identity and operands, or inspect this case's original history. " + outcome.Error;
            else
            {
                _pending = null;
                ClearWorkspace();
                Status = "The command was refused. Refresh this case and inspect a new exact workspace before confirming. " + outcome.Error;
            }
        }
        catch (Exception) { Status = "The original command outcome is uncertain. Retain and retry its exact identity in its original case context."; }
        finally { IsBusy = false; NotifyCommands(); }
    }

    private void ClearWorkspace()
    {
        _workspace = null;
        Blockers.Clear();
        IsConfirmed = IsPostingConfirmed = IsRoleBindingConfirmed = false;
        NotifyWorkspace();
    }
    private void NotifyWorkspace()
    {
        foreach (var name in new[] { nameof(Workspace), nameof(ScopeText), nameof(LimitsText), nameof(ProjectionText), nameof(RoleBindingText),
                     nameof(DeclarationText), nameof(ReceiptText), nameof(PostingText), nameof(Confirmation), nameof(DeclarationAction) }) OnPropertyChanged(name);
        NotifyCommands();
    }
    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanChooseCase)); OnPropertyChanged(nameof(HasPendingCommand)); OnPropertyChanged(nameof(PendingCommandText));
        LoadCasesCommand?.NotifyCanExecuteChanged(); RefreshWorkspaceCommand?.NotifyCanExecuteChanged(); ChangeDeclarationCommand?.NotifyCanExecuteChanged();
        AttestCommand?.NotifyCanExecuteChanged(); PostCommand?.NotifyCanExecuteChanged(); RetryCommand?.NotifyCanExecuteChanged();
    }
    public void Dispose() { if (_disposed) return; _disposed = true; ++_revision; _cts.Cancel(); _cts.Dispose(); NotifyCommands(); }
    private static bool SameActor(string? left, string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool HasReason(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4000;
    private static string Number(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
    private static string ContextKey(CorporateActionCashDividendWorkspaceDto workspace) => JsonSerializer.Serialize(new { workspace.Case.CaseId, workspace.Scope, workspace.Actor });
    private static Outcome Result<T>(ApiResponse<T> response) where T : class => new(response.Success && response.Data is not null,
        response.Success && response.Data is null ? 0 : response.StatusCode, response.ErrorMessage);
    private sealed record Outcome(bool Success, int Status, string? Error);
    private sealed record PendingCommand(Guid CaseId, Guid SecurityId, string Context, string Id, string Kind, object Request);
}

public sealed record CashDividendCaseChoice(Guid CaseId, string Label);
