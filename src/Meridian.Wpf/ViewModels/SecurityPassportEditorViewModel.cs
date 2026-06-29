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

    // ── Header / identity ────────────────────────────────────────────────────
    [ObservableProperty] private Guid _securityId;
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

    // ── Command guards ────────────────────────────────────────────────────────
    private bool CanSaveDraft()
        => !IsBusy && !string.IsNullOrWhiteSpace(FieldPath) && !string.IsNullOrWhiteSpace(Justification);

    private bool CanSubmit() => !IsBusy && RevisionState == SecurityMasterRevisionStateDto.Draft;

    private bool CanApprove() => !IsBusy && RevisionState == SecurityMasterRevisionStateDto.Submitted;

    private bool CanPublish() => !IsBusy && RevisionState == SecurityMasterRevisionStateDto.Approved;

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

    private void ApplyPublishResult(SecurityMasterPublishResultDto result)
    {
        RevisionState = SecurityMasterRevisionStateDto.Published;
        Version = result.NewVersion;
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
