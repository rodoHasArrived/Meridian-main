using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Desktop parity for the Security Master passport governed-write editor. Mirrors the browser editor's
/// lifecycle rules over the same command DTOs — there are no WPF-local governance rules; the server
/// re-validates every transition. The acting principal is server-derived, so the desktop posts the
/// business fields only and leaves identity to the session.
/// </summary>
public sealed partial class SecurityPassportEditorViewModel : BindableBase
{
    private readonly IWorkstationSecurityMasterApiClient _client;

    public SecurityPassportEditorViewModel(IWorkstationSecurityMasterApiClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Navigation hook. The shell sets this when the editor is opened contextually for a selected
    /// passport (e.g. from the Security Master page); a <see cref="SecurityPassportEditorParameter"/>
    /// hydrates the editor identity and optimistic-concurrency token. Opening the route without a
    /// parameter (command palette) leaves the editor unloaded and every governed write disabled.
    /// </summary>
    public object? Parameter
    {
        set
        {
            if (value is SecurityPassportEditorParameter context)
            {
                LoadPassportContext(context);
            }
        }
    }

    private void LoadPassportContext(SecurityPassportEditorParameter context)
    {
        SecurityId = context.SecurityId;
        Version = context.Version;
        Symbol = context.Symbol ?? string.Empty;
        AssetClass = context.AssetClass;
        TrustPosture = context.TrustPosture;
        FundProfileId = context.FundProfileId;

        // A freshly-loaded passport has no working revision yet.
        RevisionId = null;
        RevisionState = null;
        BannerText = null;
        BannerIsError = false;

        // The editor instance is reused across contextual launches, so clear every write input — a
        // prior security's draft, conflict, or approval data must never post against this passport.
        ResetWriteInputs();

        StatusText = SecurityId == Guid.Empty
            ? string.Empty
            : $"Editing {(string.IsNullOrWhiteSpace(Symbol) ? SecurityId.ToString("D") : Symbol)} at v{Version}.";
    }

    /// <summary>Clears every field-edit, source-conflict, and approval write input to its default.</summary>
    private void ResetWriteInputs()
    {
        FieldPath = string.Empty;
        NewValue = null;
        EffectiveFrom = null;
        Justification = string.Empty;
        ConflictId = null;
        ChosenWinnerSource = string.Empty;
        ConflictReason = string.Empty;
        AcknowledgePolicyDeviation = false;
        WorkflowId = null;
        ExpectedWorkflowVersion = 0;
        Reviewer = string.Empty;
        ReportPackId = string.Empty;
    }

    // ── Header / identity ────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadedPassport))]
    [NotifyCanExecuteChangedFor(nameof(SaveDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResolveConflictCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private Guid _securityId;

    [ObservableProperty] private string _symbol = string.Empty;
    [ObservableProperty] private string? _assetClass;
    [ObservableProperty] private string? _trustPosture;
    [ObservableProperty] private string? _fundProfileId;

    /// <summary>The loaded passport version, used as the optimistic-concurrency token.</summary>
    [ObservableProperty] private long _version;

    // ── Working revision ──────────────────────────────────────────────────────
    [ObservableProperty] private Guid? _revisionId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private SecurityMasterRevisionStateDto? _revisionState;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResolveConflictCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBanner))]
    private string? _bannerText;

    [ObservableProperty] private bool _bannerIsError;

    public bool HasBanner => !string.IsNullOrWhiteSpace(BannerText);

    // ── Field-edit inputs ─────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveDraftCommand))]
    private string _fieldPath = string.Empty;

    [ObservableProperty] private string? _newValue;
    [ObservableProperty] private DateTime? _effectiveFrom;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveDraftCommand))]
    private string _justification = string.Empty;

    // ── Approval inputs ───────────────────────────────────────────────────────
    [ObservableProperty] private Guid? _workflowId;
    [ObservableProperty] private long _expectedWorkflowVersion;
    [ObservableProperty] private string _reviewer = string.Empty;
    [ObservableProperty] private string _reportPackId = string.Empty;

    // ── Source-conflict resolution inputs ─────────────────────────────────────
    // Governed Accept/Override over the same passport stream: the operator picks a winning source for
    // an open conflict. Choosing a winner other than the policy default requires acknowledging the
    // deviation, which is retained as the audited artifact (the server re-validates this).
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResolveConflictCommand))]
    private Guid? _conflictId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResolveConflictCommand))]
    private string _chosenWinnerSource = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResolveConflictCommand))]
    private string _conflictReason = string.Empty;

    [ObservableProperty] private bool _acknowledgePolicyDeviation;

    // ── Command guards ────────────────────────────────────────────────────────
    // A passport must be loaded (non-empty security identity) before any governed write is allowed.
    // The editor can be opened from shell navigation without a passport context; in that state every
    // write stays disabled so an all-zero security id is never posted.
    public bool HasLoadedPassport => SecurityId != Guid.Empty;

    /// <summary>A working revision that is still editable (not a terminal published/rejected session).</summary>
    private bool HasActiveRevision =>
        RevisionId is not null
        && RevisionState is SecurityMasterRevisionStateDto.Draft
            or SecurityMasterRevisionStateDto.Submitted
            or SecurityMasterRevisionStateDto.Approved;

    /// <summary>
    /// True when the editor holds a still-editable revision or any unsaved operator input. A host reusing
    /// this editor across contextual launches must not silently discard such work by re-hydrating; a
    /// terminal published/rejected session is not preserve-worthy.
    /// </summary>
    public bool HasUnsavedWork =>
        HasActiveRevision
        || !string.IsNullOrWhiteSpace(FieldPath)
        || !string.IsNullOrWhiteSpace(NewValue)
        || EffectiveFrom is not null
        || !string.IsNullOrWhiteSpace(Justification)
        || ConflictId is not null
        || !string.IsNullOrWhiteSpace(ChosenWinnerSource)
        || !string.IsNullOrWhiteSpace(ConflictReason)
        || AcknowledgePolicyDeviation
        || WorkflowId is not null
        || !string.IsNullOrWhiteSpace(Reviewer)
        || !string.IsNullOrWhiteSpace(ReportPackId);

    private bool CanSaveDraft()
        => HasLoadedPassport && !IsBusy && !string.IsNullOrWhiteSpace(FieldPath) && !string.IsNullOrWhiteSpace(Justification);

    private bool CanResolveConflict()
        => HasLoadedPassport
           && !IsBusy
           && ConflictId is { } id && id != Guid.Empty
           && !string.IsNullOrWhiteSpace(ChosenWinnerSource)
           && !string.IsNullOrWhiteSpace(ConflictReason);

    private bool CanSubmit() => HasLoadedPassport && !IsBusy && RevisionState == SecurityMasterRevisionStateDto.Draft;

    private bool CanApprove() => HasLoadedPassport && !IsBusy && RevisionState == SecurityMasterRevisionStateDto.Submitted;

    private bool CanPublish() => HasLoadedPassport && !IsBusy && RevisionState == SecurityMasterRevisionStateDto.Approved;

    // ── Commands ──────────────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanSaveDraft))]
    private async Task SaveDraftAsync(CancellationToken ct)
    {
        await RunAsync("Saving draft…", () =>
        {
            var request = new UpdateSecurityFieldRequest(
                SecurityId: SecurityId,
                ExpectedVersion: Version,
                FieldPath: FieldPath.Trim(),
                NewValue: NewValue,
                EffectiveFrom: EffectiveFrom.HasValue
                    ? new DateTimeOffset(DateTime.SpecifyKind(EffectiveFrom.Value, DateTimeKind.Utc))
                    : DateTimeOffset.UtcNow,
                Actor: string.Empty,
                Justification: Justification.Trim(),
                FundProfileId: FundProfileId);
            return _client.UpdateFieldAsync(SecurityId, request, ct);
        }, ApplyEditResult, ct);
    }

    [RelayCommand(CanExecute = nameof(CanResolveConflict))]
    private async Task ResolveConflictAsync(CancellationToken ct)
    {
        await RunAsync("Resolving source conflict…", () =>
        {
            var request = new ResolveSourceConflictRequest(
                SecurityId: SecurityId,
                ConflictId: ConflictId ?? Guid.Empty,
                ExpectedVersion: Version,
                ChosenWinnerSource: ChosenWinnerSource.Trim(),
                Actor: string.Empty,
                Reason: ConflictReason.Trim(),
                AcknowledgePolicyDeviation: AcknowledgePolicyDeviation);
            return _client.ResolveConflictAsync(SecurityId, request, ct);
        }, ApplyConflictResolutionResult, ct);
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync(CancellationToken ct)
    {
        await RunAsync("Submitting for approval…", () =>
        {
            var request = new SubmitSecurityMasterRevisionRequest(
                SecurityId: SecurityId,
                RevisionId: RevisionId ?? Guid.Empty,
                Actor: string.Empty,
                Note: null,
                FundProfileId: FundProfileId,
                WorkflowId: WorkflowId,
                ExpectedWorkflowVersion: ExpectedWorkflowVersion,
                Reviewer: string.IsNullOrWhiteSpace(Reviewer) ? null : Reviewer.Trim(),
                ReportPackId: string.IsNullOrWhiteSpace(ReportPackId) ? null : ReportPackId.Trim());
            return _client.SubmitRevisionAsync(SecurityId, request, ct);
        }, ApplyLifecycleResult, ct);
    }

    [RelayCommand(CanExecute = nameof(CanApprove))]
    private async Task ApproveAsync(CancellationToken ct)
    {
        await RunAsync("Approving…", () =>
        {
            var request = new ApproveSecurityMasterRevisionRequest(
                SecurityId: SecurityId,
                RevisionId: RevisionId ?? Guid.Empty,
                WorkflowId: WorkflowId ?? Guid.Empty,
                ExpectedWorkflowVersion: ExpectedWorkflowVersion,
                Actor: string.Empty,
                Reviewer: string.Empty,
                Rationale: string.IsNullOrWhiteSpace(Justification) ? "Approved via passport workbench." : Justification.Trim(),
                ReportPackId: string.IsNullOrWhiteSpace(ReportPackId) ? string.Empty : ReportPackId.Trim());
            return _client.ApproveRevisionAsync(SecurityId, request, ct);
        }, ApplyLifecycleResult, ct);
    }

    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishAsync(CancellationToken ct)
    {
        await RunAsync("Publishing…", () =>
        {
            var request = new PublishSecurityMasterRevisionRequest(
                SecurityId: SecurityId,
                RevisionId: RevisionId ?? Guid.Empty,
                Actor: string.Empty,
                ApproverActor: string.Empty);
            return _client.PublishRevisionAsync(SecurityId, request, ct);
        }, ApplyPublishResult, ct);
    }

    // ── Result handling ───────────────────────────────────────────────────────
    // A field edit returns the passport stream version (the optimistic-concurrency token).
    private void ApplyEditResult(SecurityMasterEditResultDto result)
    {
        RevisionId = result.RevisionId;
        RevisionState = result.State;
        Version = result.NewVersion;
        StatusText = $"Revision {result.State} at v{result.NewVersion}.";
    }

    // A workflow-backed submit/approve returns the operations-approval *workflow* version, not the
    // passport version. Store it as the next ExpectedWorkflowVersion so the following gate command
    // matches, and leave the passport Version (the optimistic token) untouched.
    private void ApplyLifecycleResult(SecurityMasterEditResultDto result)
    {
        RevisionId = result.RevisionId;
        RevisionState = result.State;
        ExpectedWorkflowVersion = result.NewVersion;
        StatusText = $"Revision {result.State} (approval workflow v{result.NewVersion}).";
    }

    // Resolving a source conflict is a passport-level governed write (Accept/Override over the same
    // stream); it advances the passport version but does not touch the draft revision lifecycle.
    private void ApplyConflictResolutionResult(SecurityMasterConflictResolutionDto result)
    {
        Version = result.NewVersion;
        // The conflict is resolved (a terminal passport-level write); clear its inputs so the completed
        // request is not treated as preserve-worthy work and cannot re-execute against the new version.
        ConflictId = null;
        ChosenWinnerSource = string.Empty;
        ConflictReason = string.Empty;
        AcknowledgePolicyDeviation = false;
        StatusText = result.IsPolicyDeviation
            ? $"Conflict resolved to {result.ChosenWinnerSource} (policy deviation from {result.PolicyWinnerSource}) at v{result.NewVersion}."
            : $"Conflict resolved to {result.ChosenWinnerSource} at v{result.NewVersion}.";
    }

    private void ApplyPublishResult(SecurityMasterPublishResultDto result)
    {
        Version = result.NewVersion;
        // Publishing completes the session: drop the working revision and clear every write input so the
        // editor reflects a clean, current passport. This keeps the just-published edit from being
        // re-staged, and lets a reused editor re-hydrate to the new version instead of preserving it.
        RevisionId = null;
        RevisionState = SecurityMasterRevisionStateDto.Published;
        ResetWriteInputs();
        StatusText = result.RestatementRequired
            ? $"Published at v{result.NewVersion}; {result.RestatementCandidates.Count} restatement candidate(s) proposed."
            : $"Published at v{result.NewVersion}.";
    }

    private async Task RunAsync<T>(string busyText, Func<Task<ApiResponse<T>>> action, Action<T> onSuccess, CancellationToken ct)
        where T : class
    {
        IsBusy = true;
        BannerText = null;
        BannerIsError = false;
        StatusText = busyText;
        try
        {
            var response = await action();
            if (response.Success && response.Data is not null)
            {
                onSuccess(response.Data);
            }
            else
            {
                SetErrorBanner(ClassifyWorkbenchError(response.StatusCode, response.ErrorMessage));
                StatusText = string.Empty;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Genuine command cancellation (the operator navigated away / the token was cancelled).
            StatusText = string.Empty;
        }
        catch (Exception ex)
        {
            // Anything else — including an HttpClient transport timeout surfaced as a
            // (Task)OperationCanceledException whose token is NOT the command token — is a real failure.
            SetErrorBanner($"The request could not be completed: {ex.Message}");
            StatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetErrorBanner(string message)
    {
        BannerText = message;
        BannerIsError = true;
    }

    /// <summary>
    /// Maps a workbench write failure to an operator-facing message, mirroring the browser classifier:
    /// 409 version-conflict (with current version) vs revision-state conflict, 422 workflow-required vs
    /// unprocessable, and 401/403.
    /// </summary>
    internal static string ClassifyWorkbenchError(int statusCode, string? errorBody)
    {
        var (code, message, currentVersion) = ParseErrorBody(errorBody);
        return statusCode switch
        {
            401 => "Your session is no longer authenticated. Sign in again to continue editing.",
            403 => "You don't have permission to edit this security passport.",
            409 when code == "version-conflict" => currentVersion is { } v
                ? $"This passport changed (now v{v}). Reload before retrying so your edit is not lost."
                : "This passport changed. Reload before retrying so your edit is not lost.",
            409 => message ?? "This revision is no longer in a state that allows the action. Reload the revision.",
            422 when code == "workflow-required" => "Select an approval workflow and an independent reviewer before submitting.",
            422 => message ?? "The request was understood but could not be processed. Check the required fields.",
            0 => message ?? "The workbench could not be reached.",
            _ => message ?? $"The request failed ({statusCode})."
        };
    }

    private static (string? Code, string? Message, long? CurrentVersion) ParseErrorBody(string? errorBody)
    {
        if (string.IsNullOrWhiteSpace(errorBody))
        {
            return (null, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(errorBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, errorBody, null);
            }

            string? code = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;
            string? message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString()
                : null;
            long? currentVersion = root.TryGetProperty("currentVersion", out var versionElement)
                && versionElement.ValueKind == JsonValueKind.Number
                && versionElement.TryGetInt64(out var parsed)
                ? parsed
                : null;
            return (code, message, currentVersion);
        }
        catch (JsonException)
        {
            return (null, errorBody, null);
        }
    }
}

/// <summary>
/// Navigation context for opening the passport editor against a selected security. Supplied by the
/// shell when launching the editor contextually (e.g. from the Security Master page); the editor
/// hydrates its identity and optimistic-concurrency token from it.
/// </summary>
public sealed record SecurityPassportEditorParameter(
    Guid SecurityId,
    long Version,
    string? Symbol = null,
    string? AssetClass = null,
    string? TrustPosture = null,
    string? FundProfileId = null);
