using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>One opening break or period difference an operator can classify with retained evidence.</summary>
public sealed record OnboardingExplainableDifference(
    string ComparisonId, string DifferenceKey, string Label, string Kind, string AccountId, string? InstrumentId,
    decimal? MeridianAmount, decimal? ExternalAmount, string? SourceKind)
{
    public string Summary => $"{Label} · {Kind} · {AccountId}{(InstrumentId is null ? "" : " " + InstrumentId)}"
        + $"{(SourceKind is null ? "" : " · " + SourceKind)} · Meridian {Amount(MeridianAmount)} vs incumbent {Amount(ExternalAmount)}";

    internal static string Amount(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "Missing";
}

/// <summary>
/// Desktop presentation state for the shadow-mode parallel run of one bounded onboarding workspace. It mirrors the
/// browser shadow panel over the same API: designation, opening-balance import and report, difference explanations,
/// the parallel-run certificate, promotion and withdrawal. All decisions stay on the server; every mutation carries
/// the workspace version it was made against, and late responses from an earlier selection are discarded.
/// </summary>
public sealed class OnboardingShadowWorkspace : ObservableObject, IDisposable
{
    private readonly IWorkstationOnboardingApiClient? _client;
    private CancellationTokenSource _session = new();
    private int _generation;
    private bool _busy, _disposed;
    private string _status;
    private OnboardingWorkspaceDto? _selected, _workspace;
    private OnboardingExplainableDifference? _selectedDifference;
    private OnboardingParallelRunCertificateDto? _selectedCertificate;
    private string _shadowRationale = "", _withdrawalReason = "";
    private string _openingSourceKind = OnboardingOpeningSourceKinds.AdministratorPack;
    private string _openingFileName = "", _openingContent = "", _providerId = "", _importId = "";
    private string _mappingProfileId = "", _mappingVersion = "", _accountMappings = "", _openingNotes = "";
    private string _classification = OnboardingDifferenceClassifications.Timing, _reversalDate = "", _explanationNotes = "", _explanationEvidence = "";
    private string _promotionRationale = "", _promotionEvidence = "";

    public OnboardingShadowWorkspace(IWorkstationOnboardingApiClient? client)
    {
        _client = client;
        _status = client is null
            ? "Shadow-mode onboarding is unavailable: this desktop session has no workstation API connection."
            : "Load onboarding workspaces to review a shadow-mode parallel run.";
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => IsAvailable && !IsBusy);
        RefreshCommand = new AsyncRelayCommand(() => OpenAsync(_selected), () => IsAvailable && !IsBusy && _selected is not null);
        DesignateCommand = new AsyncRelayCommand(DesignateAsync, () => Ready && Workspace!.ShadowDesignation is null && Has(ShadowRationale));
        WithdrawCommand = new AsyncRelayCommand(WithdrawAsync, () => Ready && IsInShadow && Has(WithdrawalReason));
        ImportOpeningCommand = new AsyncRelayCommand(ImportOpeningAsync, () => Ready && IsInShadow && Has(MappingProfileId) && Has(MappingVersion)
            && (IsGlTrialBalance ? Has(OpeningProviderId) && Has(OpeningImportId) : Has(_openingContent)));
        ExplainCommand = new AsyncRelayCommand(ExplainAsync, () => Ready && IsInShadow && SelectedDifference is not null
            && Has(ExplanationNotes) && Has(ExplanationEvidence)
            && (Classification != OnboardingDifferenceClassifications.Timing || TryDate(ExpectedReversalDate, out _)));
        IssueCertificateCommand = new AsyncRelayCommand(IssueCertificateAsync, () => Ready && IsInShadow && Workspace!.Readiness.IsReady);
        PromoteCommand = new AsyncRelayCommand(PromoteAsync, () => Ready && IsInShadow && SelectedCertificate is not null && Has(PromotionRationale));
    }

    public IReadOnlyList<string> OpeningSourceKinds { get; } = Meridian.Contracts.Workstation.OnboardingOpeningSourceKinds.All;
    public IReadOnlyList<string> Classifications { get; } = OnboardingDifferenceClassifications.All;
    public ObservableCollection<OnboardingWorkspaceDto> Workspaces { get; } = [];
    public ObservableCollection<OnboardingOpeningBalanceLineDto> OpeningLines { get; } = [];
    public ObservableCollection<string> OpeningSources { get; } = [];
    public ObservableCollection<OnboardingPeriodSummaryDto> Periods { get; } = [];
    public ObservableCollection<OnboardingExplainableDifference> Explainable { get; } = [];
    public ObservableCollection<OnboardingParallelRunCertificateDto> Certificates { get; } = [];
    public ObservableCollection<OnboardingShadowAuditEntryDto> AuditEntries { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand DesignateCommand { get; }
    public IAsyncRelayCommand WithdrawCommand { get; }
    public IAsyncRelayCommand ImportOpeningCommand { get; }
    public IAsyncRelayCommand ExplainCommand { get; }
    public IAsyncRelayCommand IssueCertificateCommand { get; }
    public IAsyncRelayCommand PromoteCommand { get; }

    public bool IsAvailable => _client is not null && !_disposed;
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) NotifyCommands(); } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public OnboardingWorkspaceDto? SelectedWorkspace
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
                return;
            NotifyCommands();
            if (value is null)
                Show(null);
            else
                _ = OpenAsync(value);
        }
    }

    /// <summary>The server's current copy of the selected workspace; every mutation is made against its version.</summary>
    public OnboardingWorkspaceDto? Workspace { get => _workspace; private set => Show(value); }

    public string? DesignationState => Workspace?.ShadowDesignation?.State;
    public bool IsNotDesignated => Workspace is not null && Workspace.ShadowDesignation is null;
    public bool IsInShadow => DesignationState == OnboardingShadowStates.Shadow;
    public bool HasCertificates => Certificates.Count > 0;

    public string DesignationTitle => Workspace?.ShadowDesignation?.State switch
    {
        null when Workspace is null => "Select an onboarding workspace",
        null => "Not in shadow mode",
        OnboardingShadowStates.Shadow => "Shadow book — not the book of record",
        OnboardingShadowStates.Promoted => "Promoted to book of record",
        _ => "Shadow trial withdrawn"
    };

    public string DesignationDetail => Workspace?.ShadowDesignation is not { } designation
        ? Workspace is null ? "" : "Designate this ledger book as a shadow book to run Meridian beside the incumbent administrator or GL. "
            + "A shadow book cannot release reports or hard-close as the book of record until it is promoted."
        : designation.State switch
        {
            OnboardingShadowStates.Shadow => $"Designated by {designation.DesignatedBy} on {Stamp(designation.DesignatedAtUtc)}. "
                + "Report release and period hard-close are refused until promotion.",
            OnboardingShadowStates.Promoted => $"Promoted by {designation.ResolvedBy} on {Stamp(designation.ResolvedAtUtc)} "
                + $"with certificate {Workspace.Promotion?.CertificateId}.",
            _ => $"{designation.WithdrawalReason} The book stays non-authoritative."
        };

    public string OpeningSummary => Workspace?.Readiness.Opening is not { } opening
        ? "No incumbent opening balances have been imported."
        : $"{(opening.IsReconciled ? "Opening balances reconciled" : "Opening balances not reconciled")}: "
            + $"{opening.MatchedCount} matched · {opening.ExplainedCount} explained · {opening.BreakCount} breaks · {opening.MissingCount} missing";

    public string UncoveredKindsMessage => Workspace?.Readiness.Opening?.UncoveredKinds is { Count: > 0 } kinds
        ? $"No incumbent opening source covers {string.Join(", ", kinds)} for every selected account. "
            + $"Import a source that supplies {(kinds.Count == 1 ? "it" : "them")}."
        : "";

    public bool HasUncoveredKinds => UncoveredKindsMessage.Length > 0;

    public string ReadinessSummary => Workspace?.Readiness is not { } readiness ? ""
        : $"Readiness {readiness.Status}: {readiness.UnresolvedDifferenceCount} unexplained, {readiness.ExplainedDifferenceCount} explained, "
            + $"{readiness.ApprovedReviewers} of {readiness.RequiredReviewers} reviewer approval(s)."
            + (readiness.Blockers.Count == 0 ? "" : " " + string.Join(" ", readiness.Blockers));

    public string OpeningSourceKind
    {
        get => _openingSourceKind;
        set { if (SetProperty(ref _openingSourceKind, value)) { OnPropertyChanged(nameof(IsGlTrialBalance)); ClearOpeningFile(); } }
    }

    public bool IsGlTrialBalance => OpeningSourceKind == Meridian.Contracts.Workstation.OnboardingOpeningSourceKinds.GlTrialBalance;
    public string OpeningFileName { get => _openingFileName; private set => SetProperty(ref _openingFileName, value); }
    public string ShadowRationale { get => _shadowRationale; set => Input(ref _shadowRationale, value); }
    public string WithdrawalReason { get => _withdrawalReason; set => Input(ref _withdrawalReason, value); }
    public string OpeningProviderId { get => _providerId; set => Input(ref _providerId, value); }
    public string OpeningImportId { get => _importId; set => Input(ref _importId, value); }
    public string MappingProfileId { get => _mappingProfileId; set => Input(ref _mappingProfileId, value); }
    public string MappingVersion { get => _mappingVersion; set => Input(ref _mappingVersion, value); }
    public string AccountMappings { get => _accountMappings; set => Input(ref _accountMappings, value); }
    public string OpeningNotes { get => _openingNotes; set => Input(ref _openingNotes, value); }
    public OnboardingExplainableDifference? SelectedDifference { get => _selectedDifference; set => Input(ref _selectedDifference, value); }
    public string Classification { get => _classification; set => Input(ref _classification, value); }
    public string ExpectedReversalDate { get => _reversalDate; set => Input(ref _reversalDate, value); }
    public string ExplanationNotes { get => _explanationNotes; set => Input(ref _explanationNotes, value); }
    public string ExplanationEvidence { get => _explanationEvidence; set => Input(ref _explanationEvidence, value); }
    public string PromotionRationale { get => _promotionRationale; set => Input(ref _promotionRationale, value); }
    public string PromotionEvidence { get => _promotionEvidence; set => Input(ref _promotionEvidence, value); }

    public OnboardingParallelRunCertificateDto? SelectedCertificate
    {
        get => _selectedCertificate;
        set { if (Input(ref _selectedCertificate, value)) OnPropertyChanged(nameof(CertificateDetail)); }
    }

    public string CertificateDetail => SelectedCertificate is not { } certificate ? ""
        : $"Certificate {certificate.CertificateId}{Environment.NewLine}{certificate.Content.Statement}{Environment.NewLine}"
            + $"Clean periods: {string.Join(", ", certificate.Content.CleanPeriods.Select(date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))} · "
            + $"{certificate.Content.Explanations.Count} explained difference(s){Environment.NewLine}"
            + $"{certificate.HashAlgorithm} {certificate.ContentHash} · packet {certificate.Content.PacketContentHash}";

    /// <summary>Attaches the incumbent opening file; its bytes travel base64-encoded like the browser upload.</summary>
    public void SetOpeningFile(string fileName, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        // Keep only the file name, whichever separator the picker used; the server never sees local paths.
        OpeningFileName = (fileName ?? "")[((fileName ?? "").LastIndexOfAny(['/', '\\']) + 1)..];
        _openingContent = content.Length == 0 ? "" : Convert.ToBase64String(content);
        NotifyCommands();
    }

    /// <summary>Parses <c>EXTERNAL=account-id</c> entries, one per line or comma-separated.</summary>
    public static IReadOnlyList<OnboardingOpeningAccountMappingDto> ParseAccountMappings(string? value)
        => (value ?? "").Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry =>
            {
                var index = entry.IndexOf('=');
                if (index <= 0 || index == entry.Length - 1)
                    throw new FormatException($"Account mapping '{entry}' must be EXTERNAL=account-id.");
                return new OnboardingOpeningAccountMappingDto(entry[..index].Trim(), entry[(index + 1)..].Trim());
            })
            .ToArray();

    /// <summary>Discards in-flight responses, for example when the hosting page unloads.</summary>
    public void Cancel()
    {
        Interlocked.Increment(ref _generation);
        _session.Cancel();
        _session.Dispose();
        _session = new CancellationTokenSource();
        IsBusy = false;
    }

    /// <summary>Forgets every workspace read under the previous session, for example after sign-out.</summary>
    public void ResetSession()
    {
        Cancel();
        SetProperty(ref _selected, null, nameof(SelectedWorkspace));
        Workspaces.Clear();
        Show(null);
        if (IsAvailable)
            Status = "Load onboarding workspaces to review a shadow-mode parallel run.";
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Cancel();
        _disposed = true;
        _session.Dispose();
        NotifyCommands();
    }

    private bool Ready => IsAvailable && !IsBusy && Workspace is not null;

    private async Task LoadAsync()
    {
        var response = await RunAsync((client, ct) => client.ListWorkspacesAsync(ct)).ConfigureAwait(true);
        if (response is null)
            return;
        Workspaces.Clear();
        foreach (var workspace in response)
            Workspaces.Add(workspace);
        Status = Workspaces.Count == 0 ? "No onboarding workspaces are retained for this company." : $"{Workspaces.Count} onboarding workspace(s) loaded.";
        if (_selected is not null && Workspaces.FirstOrDefault(item => item.WorkspaceId == _selected.WorkspaceId) is { } current)
            Show(current);
    }

    private async Task OpenAsync(OnboardingWorkspaceDto? selection)
    {
        if (selection is null)
            return;
        Show(null);
        var response = await RunAsync((client, ct) => client.GetWorkspaceAsync(selection.WorkspaceId, ct), restart: true).ConfigureAwait(true);
        if (response is not null && _selected?.WorkspaceId == selection.WorkspaceId)
        {
            Show(response);
            Status = $"Workspace {response.Name}, version {response.Version}.";
        }
    }

    private Task DesignateAsync() => MutateAsync((client, workspace, ct) => client.DesignateShadowAsync(workspace.WorkspaceId,
        new(workspace.Version, ShadowRationale.Trim()), ct), "Ledger book designated as a shadow book.", () => ShadowRationale = "");

    private Task WithdrawAsync() => MutateAsync((client, workspace, ct) => client.WithdrawShadowAsync(workspace.WorkspaceId,
        new(workspace.Version, WithdrawalReason.Trim()), ct), "Shadow trial withdrawn.", () => WithdrawalReason = "");

    private Task ImportOpeningAsync()
    {
        IReadOnlyList<OnboardingOpeningAccountMappingDto> mappings;
        try
        {
            mappings = ParseAccountMappings(AccountMappings);
        }
        catch (FormatException ex)
        {
            Status = ex.Message;
            return Task.CompletedTask;
        }

        var notes = string.IsNullOrWhiteSpace(OpeningNotes) ? null : OpeningNotes.Trim();
        return MutateAsync((client, workspace, ct) => client.ImportOpeningBalancesAsync(workspace.WorkspaceId, IsGlTrialBalance
                ? new(workspace.Version, OpeningSourceKind, MappingProfileId.Trim(), MappingVersion.Trim(), mappings,
                    ProviderId: OpeningProviderId.Trim(), ImportId: OpeningImportId.Trim(), Notes: notes)
                : new(workspace.Version, OpeningSourceKind, MappingProfileId.Trim(), MappingVersion.Trim(), mappings,
                    FileName: OpeningFileName, ContentBase64: _openingContent, Notes: notes), ct),
            "Opening balances imported and reconciled against Meridian's records.", ClearOpeningFile);
    }

    private Task ExplainAsync()
    {
        var difference = SelectedDifference!;
        DateOnly? reversal = Classification == OnboardingDifferenceClassifications.Timing && TryDate(ExpectedReversalDate, out var date) ? date : null;
        return MutateAsync((client, workspace, ct) => client.ExplainDifferenceAsync(workspace.WorkspaceId, difference.DifferenceKey,
                new(workspace.Version, difference.ComparisonId, Classification, ExplanationNotes.Trim(), Split(ExplanationEvidence), reversal), ct),
            "Difference explanation retained.", () => { ExplanationNotes = ""; ExplanationEvidence = ""; ExpectedReversalDate = ""; });
    }

    private async Task IssueCertificateAsync()
    {
        var workspace = Workspace!;
        var issued = await RunAsync((client, ct) => client.IssueCertificateAsync(workspace.WorkspaceId, new(workspace.Version), ct)).ConfigureAwait(true);
        if (issued is null)
            return;
        var current = await RunAsync((client, ct) => client.GetWorkspaceAsync(workspace.WorkspaceId, ct)).ConfigureAwait(true);
        if (current is not null)
            Show(current);
        SelectedCertificate = Certificates.FirstOrDefault(item => item.CertificateId == issued.CertificateId) ?? issued;
        Status = "Parallel-run certificate issued.";
    }

    private Task PromoteAsync()
    {
        var certificateId = SelectedCertificate!.CertificateId;
        return MutateAsync((client, workspace, ct) => client.PromoteAsync(workspace.WorkspaceId,
                new(workspace.Version, certificateId, PromotionRationale.Trim(), Split(PromotionEvidence)), ct),
            "Shadow book promoted to book of record.", () => { PromotionRationale = ""; PromotionEvidence = ""; });
    }

    private async Task MutateAsync(Func<IWorkstationOnboardingApiClient, OnboardingWorkspaceDto, CancellationToken, Task<ApiResponse<OnboardingWorkspaceDto>>> action,
        string success, Action clearInputs)
    {
        var workspace = Workspace!;
        var conflict = false;
        var result = await RunAsync((client, ct) => action(client, workspace, ct), onConflict: () => conflict = true).ConfigureAwait(true);
        if (result is not null)
        {
            Show(result);
            clearInputs();
            Status = success;
            return;
        }

        if (conflict)
        {
            // Keep the operator's inputs; reload the server's copy so the next attempt uses its current version.
            var message = Status;
            var current = await RunAsync((client, ct) => client.GetWorkspaceAsync(workspace.WorkspaceId, ct)).ConfigureAwait(true);
            if (current is not null)
                Show(current);
            Status = $"{message} The workspace was reloaded at version {Workspace?.Version}; review it and retry.";
        }
    }

    private async Task<T?> RunAsync<T>(Func<IWorkstationOnboardingApiClient, CancellationToken, Task<ApiResponse<T>>> call,
        bool restart = false, Action? onConflict = null) where T : class
    {
        if (_client is null || _disposed)
            return null;
        if (restart)
            Cancel();
        var generation = Volatile.Read(ref _generation);
        var ct = _session.Token;
        IsBusy = true;
        try
        {
            var response = await call(_client, ct).ConfigureAwait(true);
            if (generation != Volatile.Read(ref _generation))
                return null;
            if (response.Success && response.Data is not null)
                return response.Data;
            if (response.StatusCode == 409)
                onConflict?.Invoke();
            Status = response.IsConnectionError
                ? "The workstation API is unreachable. Retry when the connection is restored."
                : response.StatusCode switch
                {
                    403 => $"Refused: {response.ErrorMessage ?? "this session may not change onboarding decisions."}",
                    409 => $"Conflict: {response.ErrorMessage ?? "the workspace changed."}",
                    404 => "The onboarding workspace or record was not found.",
                    _ => $"Unavailable: {response.ErrorMessage ?? "the onboarding request failed."}"
                };
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            if (generation == Volatile.Read(ref _generation))
                IsBusy = false;
        }
    }

    private void Show(OnboardingWorkspaceDto? workspace)
    {
        SetProperty(ref _workspace, workspace, nameof(Workspace));
        var opening = workspace?.Readiness.Opening;
        Replace(OpeningLines, opening?.Lines ?? []);
        Replace(OpeningSources, (opening?.Sources ?? []).Select(source =>
            $"{source.SourceKind} · {string.Join(", ", source.Kinds)} · {source.SourceContentHash[..Math.Min(12, source.SourceContentHash.Length)]}"));
        Replace(Periods, workspace?.Readiness.Periods ?? []);
        // Missing lines are coverage gaps the server refuses to explain; only numeric breaks are explainable.
        Replace(Explainable, workspace is null ? [] : (opening?.Lines ?? [])
            .Where(line => line.Status == "Break" && line.ComparisonId is not null && line.Difference is not null)
            .Select(line => new OnboardingExplainableDifference(line.ComparisonId!, line.DifferenceKey, "Opening", line.Kind,
                line.AccountId, line.InstrumentId, line.MeridianAmount, line.IncumbentAmount, line.SourceKind))
            .Concat(workspace.Readiness.UnresolvedDifferences.Where(item => item.Difference.Difference is not null)
                .Select(item => new OnboardingExplainableDifference(item.ComparisonId, item.Difference.DifferenceKey,
                    item.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), item.Difference.Kind, item.Difference.AccountId,
                    item.Difference.InstrumentId, item.Difference.MeridianAmount, item.Difference.ExternalAmount, null))));
        Replace(Certificates, workspace?.Certificates ?? []);
        Replace(AuditEntries, workspace?.ShadowAudit ?? []);
        if (_selectedDifference is not null && !Explainable.Contains(_selectedDifference))
            SelectedDifference = null;
        SelectedCertificate = _selectedCertificate is { } certificate && Certificates.FirstOrDefault(item => item.CertificateId == certificate.CertificateId) is { } kept
            ? kept : Certificates.LastOrDefault();
        foreach (var name in new[] { nameof(DesignationState), nameof(IsNotDesignated), nameof(IsInShadow), nameof(HasCertificates),
                     nameof(DesignationTitle), nameof(DesignationDetail), nameof(OpeningSummary), nameof(UncoveredKindsMessage),
                     nameof(HasUncoveredKinds), nameof(ReadinessSummary) })
            OnPropertyChanged(name);
        NotifyCommands();
    }

    private void ClearOpeningFile()
    {
        OpeningFileName = "";
        _openingContent = "";
        NotifyCommands();
    }

    private bool Input<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name))
            return false;
        NotifyCommands();
        return true;
    }

    private void NotifyCommands()
    {
        foreach (var command in new IRelayCommand[] { LoadCommand, RefreshCommand, DesignateCommand, WithdrawCommand,
                     ImportOpeningCommand, ExplainCommand, IssueCertificateCommand, PromoteCommand })
            command?.NotifyCanExecuteChanged();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    private static bool Has(string? value) => !string.IsNullOrWhiteSpace(value);

    private static IReadOnlyList<string> Split(string? value)
        => (value ?? "").Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryDate(string? value, out DateOnly date)
        => DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string Stamp(DateTimeOffset? value) => value?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "";
}
