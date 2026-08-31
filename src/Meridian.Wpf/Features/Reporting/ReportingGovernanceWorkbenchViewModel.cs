using System.Globalization;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.Reporting;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Features.Reporting;

/// <summary>One exact normalized parameter retained with a governed reporting revision.</summary>
public sealed record ReportingGovernedParameterRow(
    string Group,
    string Name,
    string Value);

/// <summary>Fail-visible projection of one retained governed-readiness check.</summary>
public sealed record ReportingGovernedReadinessCheckRow(
    string CheckId,
    string Status,
    string Detail,
    string EvidenceIds);

/// <summary>
/// Token-free desktop projection of a scheduled release handoff and the access-policy snapshot
/// that governed it. A row is also retained for schedules that have not produced a handoff yet.
/// </summary>
public sealed record ReportingScheduleReleaseHandoffRow(
    string ScheduleId,
    string AccessPolicySnapshotHash,
    string HandoffId,
    string RunId,
    string State,
    string Recipient,
    string TransportId,
    string DeliveryJobId,
    string CreatedAtUtc,
    string EnqueuedAtUtc);

/// <summary>
/// Presentation-only reporting workbench. It builds shared API DTOs, projects server responses,
/// and mirrors server lifecycle state for command availability. It never evaluates authority,
/// maker-checker, readiness, release, artifact-integrity, or distribution policy locally.
/// </summary>
public sealed class ReportingGovernanceWorkbenchViewModel : BindableBase
{
    private const string ValidateRunAction = "ValidateRun";
    private const string SubmitRunAction = "SubmitRun";
    private const string ApproveRunAction = "ApproveRun";
    private const string ReleaseRunAction = "ReleaseRun";
    private const string RequestRestatementAction = "RequestRestatement";
    private const string ApproveRestatementAction = "ApproveRestatement";

    private static readonly IReadOnlyList<ReportingEntityScopeKindDto> EntityScopeOptionsValue =
        Enum.GetValues<ReportingEntityScopeKindDto>();
    private static readonly IReadOnlyList<ReportingAccountingBasisDto> AccountingBasisOptionsValue =
        Enum.GetValues<ReportingAccountingBasisDto>();
    private static readonly IReadOnlyList<ReportingConsolidationLevelDto> ConsolidationOptionsValue =
        Enum.GetValues<ReportingConsolidationLevelDto>();
    private static readonly IReadOnlyList<ReportingOutputFormatDto> OutputFormatOptionsValue =
        Enum.GetValues<ReportingOutputFormatDto>();
    private static readonly IReadOnlyList<ReportingFinalityDto> FinalityOptionsValue =
        Enum.GetValues<ReportingFinalityDto>();

    private readonly IReportingGovernanceApiClient _apiClient;

    private string _templateName = "investor-monthly-statement";
    private int _templateVersion = 1;
    private string _fundProfileId = string.Empty;
    private ReportingEntityScopeKindDto _entityScopeKind = ReportingEntityScopeKindDto.AllEntities;
    private string _entityId = string.Empty;
    private string _portfolioId = string.Empty;
    private string _investorId = string.Empty;
    private string _dimensionOverridesText = string.Empty;
    private string _periodId = DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    private string _asOfDateText = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private string _ledgerBookIdText = string.Empty;
    private string _ledgerBookCode = "primary";
    private ReportingAccountingBasisDto _accountingBasis = ReportingAccountingBasisDto.Gaap;
    private string _presentationCurrency = "USD";
    private ReportingConsolidationLevelDto _consolidationLevel = ReportingConsolidationLevelDto.Fund;
    private ReportingOutputFormatDto _outputFormat = ReportingOutputFormatDto.Pdf;
    private ReportingFinalityDto _finality = ReportingFinalityDto.Draft;
    private bool _includeSupportingSchedules = true;
    private bool _includeEvidenceAppendix;
    private string _templateParametersText = string.Empty;

    private string _currentRunId = string.Empty;
    private GovernedReportingRunDto? _currentRun;
    private ReportingRunReadinessDto? _readiness;
    private ReportingGovernanceRestatementDto? _currentRestatement;
    private SecureReportingDeliveryResponse? _lastDelivery;
    private SecureReportingDistributionCapabilityCatalog? _distributionCapabilities;
    private IReadOnlyList<SecureReportingDeliveryResponse> _deliveryHistory = [];
    private IReadOnlyList<SecureReportingAccessGrantSummaryResponse> _accessGrants = [];
    private IReadOnlyList<ReportingScheduleReleaseHandoffRow> _scheduleReleaseHandoffs = [];
    private bool _isBusy;
    private string _statusText = "Complete the certified run scope, then ask the server to assess readiness.";
    private string _errorText = string.Empty;
    private string _approvalDecisionNote = string.Empty;
    private string _restatementReason = string.Empty;

    private string _distributionId = string.Empty;
    private string _transportId = string.Empty;
    private string _recipientPrincipalId = string.Empty;
    private ReportingAccessPrincipalKind _recipientPrincipalKind = ReportingAccessPrincipalKind.User;
    private string _destination = string.Empty;
    private string _deliverySubject = "Meridian governed report package";
    private string _deliveryBody = "A released reporting package is available through the secure Meridian portal.";
    private string _grantLifetimeSecondsText = "1800";
    private string _grantMaxUsesText = "1";
    private int _maxDeliveryAttempts = 3;
    private string _selectedAccessGrantId = string.Empty;
    private string _grantRevocationReason = string.Empty;
    private string _lastIssuedRecipientAccessUri = string.Empty;
    private string _lastIssuedGrantStatus = "No recipient access grant has been issued in this desktop session.";

    public ReportingGovernanceWorkbenchViewModel(IReportingGovernanceApiClient apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));

        AssessReadinessCommand = new AsyncRelayCommand(AssessReadinessAsync, CanAssessReadiness);
        GenerateGovernedRunCommand = new AsyncRelayCommand(GenerateGovernedRunAsync, CanGenerateGovernedRun);
        LoadGovernedRunCommand = new AsyncRelayCommand(LoadGovernedRunAsync, CanLoadGovernedRun);
        GovernCompletedRunCommand = new AsyncRelayCommand(GovernCompletedRunAsync, CanGovernCompletedRun);
        ValidateCommand = new AsyncRelayCommand(ValidateAsync, CanValidate);
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, CanSubmit);
        ApproveCommand = new AsyncRelayCommand(ApproveAsync, CanApprove);
        ReleaseCommand = new AsyncRelayCommand(ReleaseAsync, CanRelease);
        RequestRestatementCommand = new AsyncRelayCommand(RequestRestatementAsync, CanRequestRestatement);
        ApproveRestatementCommand = new AsyncRelayCommand(ApproveRestatementAsync, CanApproveRestatement);
        RefreshDistributionCapabilitiesCommand = new AsyncRelayCommand(RefreshDistributionCapabilitiesAsync, CanRefreshDistributionCapabilities);
        QueueDeliveryCommand = new AsyncRelayCommand(QueueDeliveryAsync, CanQueueDelivery);
        IssueAccessGrantCommand = new AsyncRelayCommand(IssueAccessGrantAsync, CanIssueAccessGrantCommand);
        RevokeAccessGrantCommand = new AsyncRelayCommand(RevokeAccessGrantAsync, CanRevokeAccessGrantCommand);
    }

    public IReadOnlyList<ReportingEntityScopeKindDto> EntityScopeOptions => EntityScopeOptionsValue;
    public IReadOnlyList<ReportingAccountingBasisDto> AccountingBasisOptions => AccountingBasisOptionsValue;
    public IReadOnlyList<ReportingConsolidationLevelDto> ConsolidationOptions => ConsolidationOptionsValue;
    public IReadOnlyList<ReportingOutputFormatDto> OutputFormatOptions => OutputFormatOptionsValue;
    public IReadOnlyList<ReportingFinalityDto> FinalityOptions => FinalityOptionsValue;
    public IReadOnlyList<SecureReportingTransportCapability> TransportOptions =>
        DistributionCapabilities?.Transports ?? [];

    public IAsyncRelayCommand AssessReadinessCommand { get; }
    public IAsyncRelayCommand GenerateGovernedRunCommand { get; }
    public IAsyncRelayCommand LoadGovernedRunCommand { get; }
    public IAsyncRelayCommand GovernCompletedRunCommand { get; }
    public IAsyncRelayCommand ValidateCommand { get; }
    public IAsyncRelayCommand SubmitCommand { get; }
    public IAsyncRelayCommand ApproveCommand { get; }
    public IAsyncRelayCommand ReleaseCommand { get; }
    public IAsyncRelayCommand RequestRestatementCommand { get; }
    public IAsyncRelayCommand ApproveRestatementCommand { get; }
    public IAsyncRelayCommand RefreshDistributionCapabilitiesCommand { get; }
    public IAsyncRelayCommand QueueDeliveryCommand { get; }
    public IAsyncRelayCommand IssueAccessGrantCommand { get; }
    public IAsyncRelayCommand RevokeAccessGrantCommand { get; }

    public string TemplateName
    {
        get => _templateName;
        set => SetRunParameter(ref _templateName, value ?? string.Empty);
    }

    public int TemplateVersion
    {
        get => _templateVersion;
        set => SetRunParameter(ref _templateVersion, value);
    }

    public string FundProfileId
    {
        get => _fundProfileId;
        set => SetRunParameter(ref _fundProfileId, value ?? string.Empty);
    }

    public ReportingEntityScopeKindDto EntityScopeKind
    {
        get => _entityScopeKind;
        set => SetRunParameter(ref _entityScopeKind, value);
    }

    public string EntityId
    {
        get => _entityId;
        set => SetRunParameter(ref _entityId, value ?? string.Empty);
    }

    public string PortfolioId
    {
        get => _portfolioId;
        set => SetRunParameter(ref _portfolioId, value ?? string.Empty);
    }

    public string InvestorId
    {
        get => _investorId;
        set => SetRunParameter(ref _investorId, value ?? string.Empty);
    }

    public string DimensionOverridesText
    {
        get => _dimensionOverridesText;
        set => SetRunParameter(ref _dimensionOverridesText, value ?? string.Empty);
    }

    public string PeriodId
    {
        get => _periodId;
        set => SetRunParameter(ref _periodId, value ?? string.Empty);
    }

    public string AsOfDateText
    {
        get => _asOfDateText;
        set => SetRunParameter(ref _asOfDateText, value ?? string.Empty);
    }

    public string LedgerBookIdText
    {
        get => _ledgerBookIdText;
        set => SetRunParameter(ref _ledgerBookIdText, value ?? string.Empty);
    }

    public string LedgerBookCode
    {
        get => _ledgerBookCode;
        set => SetRunParameter(ref _ledgerBookCode, value ?? string.Empty);
    }

    public ReportingAccountingBasisDto AccountingBasis
    {
        get => _accountingBasis;
        set => SetRunParameter(ref _accountingBasis, value);
    }

    public string PresentationCurrency
    {
        get => _presentationCurrency;
        set => SetRunParameter(ref _presentationCurrency, value ?? string.Empty);
    }

    public ReportingConsolidationLevelDto ConsolidationLevel
    {
        get => _consolidationLevel;
        set => SetRunParameter(ref _consolidationLevel, value);
    }

    public ReportingOutputFormatDto OutputFormat
    {
        get => _outputFormat;
        set => SetRunParameter(ref _outputFormat, value);
    }

    public ReportingFinalityDto Finality
    {
        get => _finality;
        set => SetRunParameter(ref _finality, value);
    }

    public bool IncludeSupportingSchedules
    {
        get => _includeSupportingSchedules;
        set => SetRunParameter(ref _includeSupportingSchedules, value);
    }

    public bool IncludeEvidenceAppendix
    {
        get => _includeEvidenceAppendix;
        set => SetRunParameter(ref _includeEvidenceAppendix, value);
    }

    public string TemplateParametersText
    {
        get => _templateParametersText;
        set => SetRunParameter(ref _templateParametersText, value ?? string.Empty);
    }

    public string CurrentRunId
    {
        get => _currentRunId;
        set
        {
            var normalized = value ?? string.Empty;
            if (!SetProperty(ref _currentRunId, normalized))
            {
                return;
            }

            if (_currentRun is not null && !string.Equals(_currentRun.RunId, normalized.Trim(), StringComparison.Ordinal))
            {
                _currentRestatement = null;
                _lastDelivery = null;
                _distributionId = string.Empty;
                _recipientPrincipalId = string.Empty;
                _destination = string.Empty;
                OnPropertyChanged(nameof(CurrentRestatement));
                OnPropertyChanged(nameof(LastDelivery));
                OnPropertyChanged(nameof(DistributionId));
                OnPropertyChanged(nameof(RecipientPrincipalId));
                OnPropertyChanged(nameof(Destination));
                OnPropertyChanged(nameof(DistributionStatusText));
                ClearIssuedRecipientLink();
                ApplyDistributionHistory([], []);
                ApplyDistributionCapabilities(null);
                ApplyCurrentRun(null);
            }

            NotifyCommandStates();
        }
    }

    public string ApprovalDecisionNote
    {
        get => _approvalDecisionNote;
        set
        {
            if (SetProperty(ref _approvalDecisionNote, value ?? string.Empty))
            {
                ApproveCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(ApproveTooltip));
            }
        }
    }

    public string RestatementReason
    {
        get => _restatementReason;
        set
        {
            if (SetProperty(ref _restatementReason, value ?? string.Empty))
            {
                RequestRestatementCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(RestatementTooltip));
            }
        }
    }

    public string DistributionId
    {
        get => _distributionId;
        set => SetDistributionField(ref _distributionId, value ?? string.Empty);
    }

    public string TransportId
    {
        get => _transportId;
        set
        {
            if (!SetDistributionField(ref _transportId, value ?? string.Empty))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedTransport));
            OnPropertyChanged(nameof(DistributionCapabilityStatusText));
            OnPropertyChanged(nameof(DestinationFieldLabel));
            OnPropertyChanged(nameof(DestinationFieldHelp));
        }
    }

    public string RecipientPrincipalId
    {
        get => _recipientPrincipalId;
        set => SetDistributionField(ref _recipientPrincipalId, value ?? string.Empty);
    }

    public IReadOnlyList<ReportingAccessPrincipalKind> RecipientPrincipalKinds { get; } =
        Enum.GetValues<ReportingAccessPrincipalKind>();

    public ReportingAccessPrincipalKind RecipientPrincipalKind
    {
        get => _recipientPrincipalKind;
        set => SetDistributionField(ref _recipientPrincipalKind, value);
    }

    public string Destination
    {
        get => _destination;
        set => SetDistributionField(ref _destination, value ?? string.Empty);
    }

    public string DeliverySubject
    {
        get => _deliverySubject;
        set => SetDistributionField(ref _deliverySubject, value ?? string.Empty);
    }

    public string DeliveryBody
    {
        get => _deliveryBody;
        set => SetDistributionField(ref _deliveryBody, value ?? string.Empty);
    }

    public string GrantLifetimeSecondsText
    {
        get => _grantLifetimeSecondsText;
        set => SetDistributionField(ref _grantLifetimeSecondsText, value ?? string.Empty);
    }

    public string GrantMaxUsesText
    {
        get => _grantMaxUsesText;
        set => SetDistributionField(ref _grantMaxUsesText, value ?? string.Empty);
    }

    public int MaxDeliveryAttempts
    {
        get => _maxDeliveryAttempts;
        set => SetDistributionField(ref _maxDeliveryAttempts, value);
    }

    public string SelectedAccessGrantId
    {
        get => _selectedAccessGrantId;
        set
        {
            if (!SetProperty(ref _selectedAccessGrantId, value ?? string.Empty))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedAccessGrant));
            RevokeAccessGrantCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(RevokeAccessGrantTooltip));
        }
    }

    public string GrantRevocationReason
    {
        get => _grantRevocationReason;
        set
        {
            if (!SetProperty(ref _grantRevocationReason, value ?? string.Empty))
            {
                return;
            }

            RevokeAccessGrantCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(RevokeAccessGrantTooltip));
        }
    }

    public ReportingRunReadinessDto? Readiness
    {
        get => _readiness;
        private set
        {
            if (!SetProperty(ref _readiness, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ReadinessChecks));
            OnPropertyChanged(nameof(ReadinessStatusText));
            OnPropertyChanged(nameof(ReadinessBlockerText));
            OnPropertyChanged(nameof(HasReadiness));
            NotifyCommandStates();
        }
    }

    public IReadOnlyList<ReportingRunReadinessCheckDto> ReadinessChecks => Readiness?.Checks ?? [];
    public bool HasReadiness => Readiness is not null;

    public string ReadinessStatusText => Readiness is null
        ? "Not assessed"
        : $"{Readiness.Status} · draft {(Readiness.CanGenerateDraft ? "ready" : "blocked")} · final {(Readiness.CanGenerateFinal ? "ready" : "blocked")}";

    public string ReadinessBlockerText => Readiness is null
        ? "Server readiness has not been assessed for the current parameter set."
        : Readiness.BlockingReasons.Count == 0
            ? $"Certified readiness {Readiness.EvaluationId}; evidence {ShortHash(Readiness.EvidenceHash)}."
            : string.Join(" · ", Readiness.BlockingReasons);

    public string AssessReadinessTooltip => IsBusy
        ? "Wait for the current reporting operation."
        : TryBuildRunRequest(out _, out var error)
            ? "Ask the server to validate entity, book, period, basis, currency, finality, outputs, reconciliations, and evidence."
            : error;

    public string GenerateRunTooltip => IsBusy
        ? "Wait for the current reporting operation."
        : Readiness is null
            ? "Assess server readiness for the current parameters first."
            : Finality == ReportingFinalityDto.Final && !Readiness.CanGenerateFinal
                ? ReadinessBlockerText
                : Finality == ReportingFinalityDto.Draft && !Readiness.CanGenerateDraft
                    ? ReadinessBlockerText
                    : "Generate from the certified server snapshot and attach canonical governance.";

    public GovernedReportingRunDto? CurrentRun => _currentRun;
    public bool HasCurrentRun => CurrentRun is not null;
    public string LifecycleStateText => CurrentRun is null
        ? "No governed run loaded"
        : $"{CurrentRun.GovernanceState} · execution {CurrentRun.ExecutionState} · revision {CurrentRun.Revision} · version {CurrentRun.Version}";
    public string LifecyclePathText => "Draft → Validated → InReview → Approved → Released";
    public string ScopeSnapshotText => CurrentRun is null
        ? "Tenant, organization, company, fund, book, period, and access snapshots appear after governance attachment."
        : $"Tenant {CurrentRun.Scope.TenantId} · organization {CurrentRun.Scope.OrganizationId} · company {CurrentRun.Scope.CompanyId ?? "—"} · fund {CurrentRun.Scope.FundId ?? "—"} · book {CurrentRun.Scope.BookId} · period {CurrentRun.Scope.PeriodId} · access {CurrentRun.Access.PolicyId}/{CurrentRun.Access.PolicyVersion} {CurrentRun.Access.Mode} {ShortHash(CurrentRun.Access.PolicyHash)}";
    public string CertifiedSnapshotText => CurrentRun is null
        ? "No immutable certified snapshot loaded."
        : $"Snapshot {CurrentRun.Snapshot.SnapshotId} · {ShortHash(CurrentRun.Snapshot.SnapshotHash)} · reconciliation {CurrentRun.Snapshot.ReconciliationCheckpointId} · {CurrentRun.Snapshot.CapturedAtUtc:u}";
    public ReportingGovernanceReadinessDto? RetainedReadiness => CurrentRun?.Readiness;
    public string RetainedReadinessStatusText => CurrentRun is null
        ? "Load a governed run to inspect its retained readiness receipt."
        : RetainedReadiness is null
            ? "No retained governed-readiness receipt was returned; lifecycle actions remain server controlled."
            : RetainedReadiness.IsReady
                ? "Ready · retained governed-readiness receipt"
                : "Blocked · retained governed-readiness receipt";
    public string RetainedReadinessReceiptText => RetainedReadiness is null
        ? "Receipt id, hash, evaluation time, checks, and evidence are unavailable."
        : $"Receipt {RetainedReadiness.ReceiptId} · hash {RetainedReadiness.ReceiptHash} · evaluated {RetainedReadiness.EvaluatedAtUtc:u}";
    public IReadOnlyList<ReportingGovernedReadinessCheckRow> RetainedReadinessChecks =>
        RetainedReadiness?.Checks.Select(static check => new ReportingGovernedReadinessCheckRow(
            check.CheckId,
            check.Passed ? "Passed" : "Blocked",
            string.IsNullOrWhiteSpace(check.FailureReason) ? "No failure reason retained." : check.FailureReason,
            check.EvidenceIds.Count == 0 ? "No evidence ids retained." : string.Join(" · ", check.EvidenceIds)))
        .ToArray() ?? [];
    public IReadOnlyList<ReportingGovernedParameterRow> RetainedParameterRows =>
        BuildRetainedParameterRows(CurrentRun?.NormalizedParameters);
    public string RetainedParameterStatusText => CurrentRun is null
        ? "Load a governed run to inspect its exact normalized parameters."
        : RetainedParameterRows.Count == 0
            ? "No normalized parameter projection was returned for this governed revision."
            : $"{RetainedParameterRows.Count} normalized parameter value(s) retained; hash {CurrentRun.Snapshot.ParametersHash ?? "unavailable"}.";
    public string RetainedAccessPolicyText => CurrentRun is null
        ? "Load a governed run to inspect its retained access policy."
        : $"{CurrentRun.Access.PolicyId}/{CurrentRun.Access.PolicyVersion} · {CurrentRun.Access.Mode} · hash {CurrentRun.Access.PolicyHash}";
    public string RetainedAccessOwnerText => CurrentRun is null
        ? "No retained owner projection loaded."
        : $"Owner {CurrentRun.Access.OwnerPrincipalId ?? "not retained"} · owner access {(CurrentRun.Access.AllowOwnerAccess ? "enabled" : "disabled")}";
    public IReadOnlyList<ReportingGovernanceAccessPrincipalDto> RetainedAccessPrincipals =>
        CurrentRun?.Access.Principals ?? [];
    public string ArtifactSummaryText => CurrentRun?.Release is not { } release
        ? "Artifacts remain unreleased; downloads and delivery stay locked."
        : $"Manifest {release.ManifestId} · {ShortHash(release.ManifestHash)} · {release.Artifacts.Count} immutable artifact(s) · {release.Artifacts.Sum(static artifact => artifact.ByteLength):N0} bytes";
    public IReadOnlyList<ReportingGovernanceArtifactDto> ReleasedArtifacts =>
        CurrentRun?.Release?.Artifacts ?? [];
    public string MakerCheckerStatusText => CurrentRun is null
        ? "Maker-checker state is unavailable until a governed run is loaded."
        : CurrentRun.Approval is null
            ? $"Maker {CurrentRun.CreationAuthority.ActorId}; server requires a different authorized checker before approval."
            : $"Maker {CurrentRun.CreationAuthority.ActorId} · checker {CurrentRun.Approval.Authority.ActorId} · approved {CurrentRun.Approval.ApprovedAtUtc:u}";

    public string ValidateTooltip =>
        RunActionTooltip(ValidateRunAction, "Have the server verify retained readiness and artifact evidence.");
    public string SubmitTooltip =>
        RunActionTooltip(SubmitRunAction, "Submit the validated run for independent review.");
    public string ApproveTooltip => !IsRunActionAllowed(ApproveRunAction)
        ? RunActionBlockedReason(ApproveRunAction)
        : string.IsNullOrWhiteSpace(ApprovalDecisionNote)
            ? "Enter an independent approval rationale."
            : "The server authorizes this caller and enforces maker-checker separation again on submission.";
    public string ReleaseTooltip =>
        RunActionTooltip(ReleaseRunAction, "Have the server verify and release the immutable manifest and artifacts.");

    public ReportingGovernanceRestatementDto? CurrentRestatement => _currentRestatement;
    public string RestatementStatusText => CurrentRestatement is null
        ? CurrentRun?.RestatementOfRunId is { Length: > 0 } predecessor
            ? $"This is a governed revision of {predecessor}."
            : "No restatement request is active. Released output is never changed in place."
        : CurrentRestatement.DraftRunId is { Length: > 0 } draftRunId
            ? $"Request {CurrentRestatement.RequestId} · {CurrentRestatement.State} · new governed revision {draftRunId} · version {CurrentRestatement.Version}"
            : $"Request {CurrentRestatement.RequestId} · {CurrentRestatement.State} · predecessor revision {CurrentRestatement.PredecessorRevision} · version {CurrentRestatement.Version}";
    public string RestatementTooltip => !IsRunActionAllowed(RequestRestatementAction)
        ? RunActionBlockedReason(RequestRestatementAction)
        : string.IsNullOrWhiteSpace(RestatementReason)
            ? "Enter a reason for the new immutable revision."
            : "Open a server-governed restatement request; released bytes are never overwritten.";
    public string ApproveRestatementTooltip => IsRestatementActionAllowed(ApproveRestatementAction)
        ? "The server enforces independent approval and creates a new Draft revision."
        : RestatementActionBlockedReason(ApproveRestatementAction);

    public SecureReportingDistributionCapabilityCatalog? DistributionCapabilities => _distributionCapabilities;
    public bool HasDistributionCapabilities => DistributionCapabilities is not null;
    public SecureReportingTransportCapability? SelectedTransport =>
        DistributionCapabilities?.Transports?.FirstOrDefault(transport =>
            string.Equals(transport.TransportId, TransportId, StringComparison.OrdinalIgnoreCase));
    public string DestinationFieldLabel => SelectedTransport switch
    {
        { RequiresDestination: true } => "Destination (required)",
        { IsExternal: true } => "Destination assertion (optional)",
        _ => "Destination (optional)"
    };
    public string DestinationFieldHelp => SelectedTransport switch
    {
        { RequiresDestination: true } => "The selected transport requires a caller-supplied destination.",
        { IsExternal: true } => "The server resolves the governed recipient destination; a nonblank value is only an equality assertion.",
        _ => "The secure portal uses the governed recipient when this field is blank."
    };
    public bool CanIssueAccessGrant => DistributionCapabilities?.CanIssueAccessGrant == true;
    public bool CanRevokeAccessGrant => DistributionCapabilities?.CanRevokeAccessGrant == true;
    public IReadOnlyList<SecureReportingDeliveryResponse> DeliveryHistory => _deliveryHistory;
    public IReadOnlyList<SecureReportingAccessGrantSummaryResponse> AccessGrants => _accessGrants;
    public SecureReportingAccessGrantSummaryResponse? SelectedAccessGrant =>
        AccessGrants.FirstOrDefault(grant =>
            string.Equals(grant.GrantId, SelectedAccessGrantId, StringComparison.Ordinal));
    public string DistributionHistoryStatusText =>
        $"{DeliveryHistory.Count} retained delivery job(s) · {AccessGrants.Count} non-secret access grant record(s)";
    public IReadOnlyList<ReportingScheduleReleaseHandoffRow> ScheduleReleaseHandoffs =>
        _scheduleReleaseHandoffs;
    public string ScheduleReleaseHandoffStatusText => ScheduleReleaseHandoffs.Count == 0
        ? "No reporting schedules are loaded for the active fund context."
        : $"{ScheduleReleaseHandoffs.Count} schedule release-handoff row(s); access-policy hashes and queue state come from the shared reporting read model.";
    public string LastIssuedRecipientAccessUri => _lastIssuedRecipientAccessUri;
    public bool HasLastIssuedRecipientAccessUri => !string.IsNullOrWhiteSpace(LastIssuedRecipientAccessUri);
    public string LastIssuedGrantStatus => _lastIssuedGrantStatus;
    public string DistributionCapabilityStatusText => BuildDistributionCapabilityStatusText();

    public SecureReportingDeliveryResponse? LastDelivery => _lastDelivery;
    public string DistributionStatusText => LastDelivery is null
        ? "No release-gated delivery has been queued in this desktop session."
        : $"{LastDelivery.State} · job {LastDelivery.JobId} · transport {LastDelivery.TransportId} · attempt {LastDelivery.AttemptCount}/{LastDelivery.MaxAttempts} · receipts {LastDelivery.Receipts.Count}";
    public string QueueDeliveryTooltip => BuildQueueDeliveryTooltip();
    public string IssueAccessGrantTooltip => BuildIssueAccessGrantTooltip();
    public string RevokeAccessGrantTooltip => BuildRevokeAccessGrantTooltip();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public void SetFundContext(string? fundProfileId, string? presentationCurrency)
    {
        FundProfileId = fundProfileId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(presentationCurrency))
        {
            PresentationCurrency = presentationCurrency.Trim().ToUpperInvariant();
        }
    }

    /// <summary>
    /// Projects schedule handoffs already loaded by the shared fund-operations read model. No
    /// scheduling or release decision is recomputed in the desktop process.
    /// </summary>
    public void ApplyScheduleRecords(IReadOnlyList<ReportingScheduleRecordDto>? schedules)
    {
        _scheduleReleaseHandoffs = BuildScheduleReleaseHandoffRows(schedules ?? []);
        OnPropertyChanged(nameof(ScheduleReleaseHandoffs));
        OnPropertyChanged(nameof(ScheduleReleaseHandoffStatusText));
    }

    public bool TryBuildRunRequest(out ReportingRunRequestDto? request, out string error) =>
        ReportingRunRequestBuilder.TryBuild(
            new ReportingRunInput(
                TemplateName,
                TemplateVersion,
                FundProfileId,
                EntityScopeKind,
                EntityId,
                PortfolioId,
                InvestorId,
                DimensionOverridesText,
                PeriodId,
                AsOfDateText,
                LedgerBookIdText,
                LedgerBookCode,
                AccountingBasis,
                PresentationCurrency,
                ConsolidationLevel,
                OutputFormat,
                Finality,
                IncludeSupportingSchedules,
                IncludeEvidenceAppendix,
                TemplateParametersText),
            out request,
            out error);

    private async Task AssessReadinessAsync(CancellationToken ct)
    {
        if (!TryBuildRunRequest(out var request, out var error))
        {
            SetError(error);
            return;
        }

        await ExecuteAsync(
            "Assessing server-owned readiness…",
            async () =>
            {
                var result = await _apiClient.AssessReadinessAsync(request!, ct).ConfigureAwait(true);
                Readiness = RequireData(result, "Readiness assessment");
                StatusText = Readiness.CanGenerateFinal
                    ? "Server readiness permits draft and final generation."
                    : Readiness.CanGenerateDraft
                        ? "Server readiness permits a draft; final generation remains blocked."
                        : "Server readiness blocks generation for this parameter set.";
            }).ConfigureAwait(true);
    }

    private async Task GenerateGovernedRunAsync(CancellationToken ct)
    {
        if (!TryBuildRunRequest(out var request, out var error))
        {
            SetError(error);
            return;
        }

        await ExecuteAsync(
            "Generating from the certified server snapshot…",
            async () =>
            {
                var runResult = RequireData(await _apiClient.RunAsync(request!, ct).ConfigureAwait(true), "Reporting run");
                CurrentRunId = runResult.Run.RunId;
                StatusText = "Certified run completed; attaching canonical governance…";
                var governed = RequireData(
                    await _apiClient.GovernCompletedRunAsync(CurrentRunId, ct).ConfigureAwait(true),
                    "Governance attachment");
                ApplyCurrentRun(governed);
                await RefreshDistributionCapabilitiesCoreAsync(ct).ConfigureAwait(true);
                StatusText = $"Governed reporting run {governed.RunId} is {governed.GovernanceState}.";
            }).ConfigureAwait(true);
    }

    private async Task LoadGovernedRunAsync(CancellationToken ct) =>
        await ExecuteAsync(
            "Loading governed reporting state…",
            async () =>
            {
                var governed = RequireData(
                    await _apiClient.GetGovernedRunAsync(CurrentRunId.Trim(), ct).ConfigureAwait(true),
                    "Governed run load");
                ApplyCurrentRun(governed);
                await RefreshDistributionCapabilitiesCoreAsync(ct).ConfigureAwait(true);
                StatusText = $"Loaded {governed.RunId} at version {governed.Version}.";
            }).ConfigureAwait(true);

    private async Task GovernCompletedRunAsync(CancellationToken ct) =>
        await ExecuteAsync(
            "Attaching canonical governance…",
            async () =>
            {
                var governed = RequireData(
                    await _apiClient.GovernCompletedRunAsync(CurrentRunId.Trim(), ct).ConfigureAwait(true),
                    "Governance attachment");
                ApplyCurrentRun(governed);
                await RefreshDistributionCapabilitiesCoreAsync(ct).ConfigureAwait(true);
                StatusText = $"Governed reporting run {governed.RunId} is {governed.GovernanceState}.";
            }).ConfigureAwait(true);

    private Task ValidateAsync(CancellationToken ct) =>
        ExecuteRunTransitionAsync(
            "Validating retained readiness and artifacts…",
            "Validation",
            ValidateRunAction,
            _apiClient.ValidateAsync,
            ct);

    private Task SubmitAsync(CancellationToken ct) =>
        ExecuteRunTransitionAsync(
            "Submitting the validated run for independent review…",
            "Submission",
            SubmitRunAction,
            _apiClient.SubmitAsync,
            ct);

    private async Task ApproveAsync(CancellationToken ct) =>
        await ExecuteAsync(
            "Requesting independent approval…",
            async () =>
            {
                var run = RequireCurrentRun();
                var action = RequireAllowedRunAction(ApproveRunAction);
                var updated = RequireData(
                    await _apiClient.ApproveAsync(
                            run.RunId,
                            action.ExpectedVersion,
                            ApprovalDecisionNote.Trim(),
                            ct)
                        .ConfigureAwait(true),
                    "Approval");
                ApplyCurrentRun(updated);
                StatusText = $"Run {updated.RunId} is {updated.GovernanceState}; maker-checker evidence is retained by the server.";
            }).ConfigureAwait(true);

    private Task ReleaseAsync(CancellationToken ct) =>
        ExecuteRunTransitionAsync(
            "Verifying and releasing immutable artifacts…",
            "Release",
            ReleaseRunAction,
            _apiClient.ReleaseAsync,
            ct);

    private async Task RequestRestatementAsync(CancellationToken ct) =>
        await ExecuteAsync(
            "Opening a governed restatement request…",
            async () =>
            {
                var run = RequireCurrentRun();
                ClearIssuedRecipientLink();
                var action = RequireAllowedRunAction(RequestRestatementAction);
                _currentRestatement = RequireData(
                    await _apiClient.RequestRestatementAsync(
                            run.RunId,
                            action.ExpectedVersion,
                            RestatementReason.Trim(),
                            ct)
                        .ConfigureAwait(true),
                    "Restatement request");
                OnPropertyChanged(nameof(CurrentRestatement));
                OnPropertyChanged(nameof(RestatementStatusText));
                StatusText = $"Restatement {_currentRestatement.RequestId} is {_currentRestatement.State}.";
                NotifyCommandStates();
            }).ConfigureAwait(true);

    private async Task ApproveRestatementAsync(CancellationToken ct) =>
        await ExecuteAsync(
            "Approving the restatement into a new immutable revision…",
            async () =>
            {
                var restatement = _currentRestatement
                    ?? throw new InvalidOperationException("No restatement request is loaded.");
                ClearIssuedRecipientLink();
                var action = RequireAllowedRestatementAction(ApproveRestatementAction);
                var approved = RequireData(
                    await _apiClient.ApproveRestatementAsync(
                            restatement.RequestId,
                            action.ExpectedVersion,
                            ct)
                        .ConfigureAwait(true),
                    "Restatement approval");
                _currentRestatement = approved.Request;
                OnPropertyChanged(nameof(CurrentRestatement));
                ApplyCurrentRun(approved.DraftRun);
                await RefreshDistributionCapabilitiesCoreAsync(ct).ConfigureAwait(true);
                StatusText = $"Restatement approved; new draft revision {approved.DraftRun.Revision} is active.";
            }).ConfigureAwait(true);

    private Task RefreshDistributionCapabilitiesAsync(CancellationToken ct) =>
        ExecuteAsync(
            "Loading caller-specific distribution capabilities…",
            async () =>
            {
                ClearIssuedRecipientLink();
                await RefreshDistributionCapabilitiesCoreAsync(ct).ConfigureAwait(true);
                StatusText = "Server distribution capabilities and retained history refreshed.";
            });

    private async Task QueueDeliveryAsync(CancellationToken ct)
    {
        if (!TryBuildDeliveryCommand(out var request, out var error))
        {
            SetError(error);
            return;
        }

        await ExecuteAsync(
            "Queueing release-gated secure distribution…",
            async () =>
            {
                ClearIssuedRecipientLink();
                _lastDelivery = RequireData(
                    await _apiClient.QueueDeliveryAsync(request!, ct).ConfigureAwait(true),
                    "Secure distribution");
                OnPropertyChanged(nameof(LastDelivery));
                OnPropertyChanged(nameof(DistributionStatusText));
                await RefreshDistributionCapabilitiesCoreAsync(ct).ConfigureAwait(true);
                StatusText = $"Delivery {_lastDelivery.JobId} is {_lastDelivery.State}; provider receipts remain server-owned.";
            }).ConfigureAwait(true);
    }

    private async Task IssueAccessGrantAsync(CancellationToken ct)
    {
        if (!TryBuildGrantIssueCommand(out var request, out var error))
        {
            SetError(error);
            return;
        }

        await ExecuteAsync(
            "Issuing one-time recipient access…",
            async () =>
            {
                ClearIssuedRecipientLink();
                var issued = RequireData(
                    await _apiClient.IssueAccessGrantAsync(request!, ct).ConfigureAwait(true),
                    "Recipient access grant");
                _lastIssuedRecipientAccessUri = ValidateOneTimeRecipientAccessUri(issued.RecipientAccessUri);
                _lastIssuedGrantStatus = $"Grant {issued.GrantId} issued once for {issued.Audience}; expires {issued.ExpiresAtUtc:u}.";
                OnPropertyChanged(nameof(LastIssuedRecipientAccessUri));
                OnPropertyChanged(nameof(HasLastIssuedRecipientAccessUri));
                OnPropertyChanged(nameof(LastIssuedGrantStatus));
                await RefreshDistributionCapabilitiesCoreAsync(ct).ConfigureAwait(true);
                StatusText = $"Recipient access grant {issued.GrantId} issued; its bearer exists only in the one-time fragment link.";
            }).ConfigureAwait(true);
    }

    private async Task RevokeAccessGrantAsync(CancellationToken ct)
    {
        var grant = SelectedAccessGrant;
        if (grant is null)
        {
            SetError("Select a retained access grant before revocation.");
            return;
        }

        await ExecuteAsync(
            "Revoking recipient access…",
            async () =>
            {
                ClearIssuedRecipientLink();
                var response = RequireData(
                    await _apiClient.RevokeAccessGrantAsync(
                            grant.GrantId,
                            GrantRevocationReason.Trim(),
                            ct)
                        .ConfigureAwait(true),
                    "Access grant revocation");
                if (!response.Revoked)
                {
                    throw new InvalidOperationException("The server did not confirm access-grant revocation.");
                }

                _grantRevocationReason = string.Empty;
                OnPropertyChanged(nameof(GrantRevocationReason));
                await RefreshDistributionCapabilitiesCoreAsync(ct).ConfigureAwait(true);
                StatusText = $"Access grant {response.GrantId} is revoked; retained state was refreshed.";
            }).ConfigureAwait(true);
    }

    private async Task ExecuteRunTransitionAsync(
        string progress,
        string operation,
        string actionName,
        Func<string, long, CancellationToken, Task<ApiResponse<GovernedReportingRunDto>>> transition,
        CancellationToken ct) =>
        await ExecuteAsync(
            progress,
            async () =>
            {
                var run = RequireCurrentRun();
                ClearIssuedRecipientLink();
                var action = RequireAllowedRunAction(actionName);
                var updated = RequireData(
                    await transition(run.RunId, action.ExpectedVersion, ct).ConfigureAwait(true),
                    operation);
                ApplyCurrentRun(updated);
                StatusText = $"Run {updated.RunId} is {updated.GovernanceState} at version {updated.Version}.";
            }).ConfigureAwait(true);

    private async Task RefreshDistributionCapabilitiesCoreAsync(CancellationToken ct)
    {
        var run = RequireCurrentRun();
        try
        {
            var capabilities = RequireData(
                await _apiClient.GetDistributionCapabilitiesAsync(ct).ConfigureAwait(true),
                "Distribution capability catalog");
            var deliveries = RequireData(
                await _apiClient.ListDeliveriesAsync(run.RunId, ct).ConfigureAwait(true),
                "Delivery history");
            IReadOnlyList<SecureReportingAccessGrantSummaryResponse> grants =
                capabilities.CanIssueAccessGrant || capabilities.CanRevokeAccessGrant
                ? RequireData(
                    await _apiClient.ListAccessGrantsAsync(run.RunId, ct).ConfigureAwait(true),
                    "Access grant history")
                : [];
            ApplyDistributionCapabilities(capabilities);
            ApplyDistributionHistory(deliveries, grants);
        }
        catch
        {
            ApplyDistributionCapabilities(null);
            ApplyDistributionHistory([], []);
            throw;
        }
    }

    private async Task ExecuteAsync(string progress, Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        ClearIssuedRecipientLink();
        IsBusy = true;
        ErrorText = string.Empty;
        StatusText = progress;
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Reporting operation cancelled.";
            throw;
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string BuildDistributionCapabilityStatusText()
    {
        var capabilities = DistributionCapabilities;
        if (capabilities is null)
        {
            return "Server distribution capabilities have not been loaded; queueing, grant issuance, and revocation fail closed.";
        }

        var transport = SelectedTransport;
        var selection = transport is null
            ? ". Select a server-advertised transport."
            : $" · selected {transport.DisplayName}: {(transport.IsReady ? "ready" : transport.DisabledReasonCode ?? "blocked")}";
        return $"Server authorization · queue {AllowedText(capabilities.CanQueueDelivery)} · issue grant {AllowedText(capabilities.CanIssueAccessGrant)} · revoke grant {AllowedText(capabilities.CanRevokeAccessGrant)} · {TransportOptions.Count} configured transport(s){selection}";
    }

    private string BuildQueueDeliveryTooltip()
    {
        var capabilities = DistributionCapabilities;
        if (capabilities is null)
        {
            return "Load the caller-specific server distribution capability catalog.";
        }

        if (!capabilities.CanQueueDelivery)
        {
            return capabilities.ActionDisabledReasonCode
                ?? "The server did not authorize delivery queueing for this caller.";
        }

        var transport = SelectedTransport;
        if (transport is null)
        {
            return "Select a server-advertised transport.";
        }

        if (!transport.IsReady)
        {
            return transport.DisabledReasonCode ?? "The selected transport is not ready.";
        }

        return TryBuildDeliveryCommand(out _, out var error)
            ? "Queue a durable, idempotent delivery with server-owned grants, retries, and provider receipts."
            : error;
    }

    private string BuildIssueAccessGrantTooltip()
    {
        var capabilities = DistributionCapabilities;
        if (capabilities is null)
        {
            return "Load the caller-specific server distribution capability catalog.";
        }

        if (!capabilities.CanIssueAccessGrant)
        {
            return capabilities.ActionDisabledReasonCode
                ?? "The server did not authorize access-grant issuance for this caller.";
        }

        return TryBuildGrantIssueCommand(out _, out var error)
            ? "Issue a scoped, expiring recipient link; its bearer is returned once in the URI fragment and is never persisted by the desktop."
            : error;
    }

    private string BuildRevokeAccessGrantTooltip()
    {
        var capabilities = DistributionCapabilities;
        if (capabilities is null)
        {
            return "Load the caller-specific server distribution capability catalog.";
        }

        if (!capabilities.CanRevokeAccessGrant)
        {
            return capabilities.ActionDisabledReasonCode
                ?? "The server did not authorize access-grant revocation for this caller.";
        }

        var grant = SelectedAccessGrant;
        if (grant is null)
        {
            return "Select a retained access grant.";
        }

        if (grant.RevokedAtUtc is not null
            || string.Equals(grant.State, "Revoked", StringComparison.OrdinalIgnoreCase))
        {
            return "The selected access grant is already revoked.";
        }

        return string.IsNullOrWhiteSpace(GrantRevocationReason)
            ? "Enter a retained revocation reason."
            : "Revoke the selected grant; the server will enforce tenant, run, audience, and current grant state.";
    }

    private bool TryBuildGrantIssueCommand(
        out SecureReportingGrantIssueCommand? request,
        out string error)
    {
        request = null;
        if (CurrentRun?.Release is not { Artifacts.Count: > 0 } release)
        {
            error = "A server-retained release receipt with immutable artifacts is required before recipient access can be issued.";
            return false;
        }

        var capabilities = DistributionCapabilities;
        if (capabilities is null)
        {
            error = "The caller-specific server distribution capability catalog is unavailable.";
            return false;
        }

        if (!capabilities.CanIssueAccessGrant)
        {
            error = capabilities.ActionDisabledReasonCode
                ?? "The server did not authorize access-grant issuance for this caller.";
            return false;
        }

        if (!TryParseOptionalPositiveInt(GrantLifetimeSecondsText, "Grant lifetime", out var grantLifetime, out error)
            || !TryParseOptionalPositiveInt(GrantMaxUsesText, "Grant max uses", out var grantMaxUses, out error))
        {
            return false;
        }

        request = new SecureReportingGrantIssueCommand(
            CurrentRun.RunId,
            TrimOrNull(RecipientPrincipalId),
            release.Artifacts.Select(static artifact => artifact.ArtifactId).ToArray(),
            grantLifetime,
            grantMaxUses,
            string.IsNullOrWhiteSpace(RecipientPrincipalId) ? null : RecipientPrincipalKind);
        error = string.Empty;
        return true;
    }

    private bool TryBuildDeliveryCommand(
        out SecureReportingDeliveryQueueCommand? request,
        out string error)
    {
        request = null;
        if (CurrentRun?.Release is not { Artifacts.Count: > 0 } release)
        {
            error = "A server-retained release receipt with immutable artifacts is required before distribution.";
            return false;
        }

        if (DistributionCapabilities is null)
        {
            error = "The caller-specific server distribution capability catalog is unavailable.";
            return false;
        }

        if (!DistributionCapabilities.CanQueueDelivery)
        {
            error = DistributionCapabilities.ActionDisabledReasonCode
                ?? "The server did not authorize delivery queueing for this caller.";
            return false;
        }

        var transport = SelectedTransport;
        if (transport is null)
        {
            error = "Select a server-advertised transport.";
            return false;
        }

        if (!transport.IsReady)
        {
            error = transport.DisabledReasonCode ?? "The selected transport is not ready.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(DistributionId)
            || string.IsNullOrWhiteSpace(TransportId)
            || string.IsNullOrWhiteSpace(DeliverySubject)
            || string.IsNullOrWhiteSpace(DeliveryBody))
        {
            error = "Distribution id, transport, subject, and body are required.";
            return false;
        }

        if (transport.RequiresDestination && string.IsNullOrWhiteSpace(Destination))
        {
            error = $"Destination is required for {transport.DisplayName}.";
            return false;
        }

        if (!TryParseOptionalPositiveInt(GrantLifetimeSecondsText, "Grant lifetime", out var grantLifetime, out error)
            || !TryParseOptionalPositiveInt(GrantMaxUsesText, "Grant max uses", out var grantMaxUses, out error))
        {
            return false;
        }

        if (MaxDeliveryAttempts <= 0)
        {
            error = "Maximum delivery attempts must be positive.";
            return false;
        }

        request = new SecureReportingDeliveryQueueCommand(
            CurrentRun.RunId,
            DistributionId.Trim(),
            TransportId.Trim(),
            TrimOrNull(RecipientPrincipalId),
            Destination.Trim(),
            DeliverySubject.Trim(),
            DeliveryBody.Trim(),
            release.Artifacts.Select(static artifact => artifact.ArtifactId).ToArray(),
            grantLifetime,
            grantMaxUses,
            MaxDeliveryAttempts,
            string.IsNullOrWhiteSpace(RecipientPrincipalId) ? null : RecipientPrincipalKind);
        error = string.Empty;
        return true;
    }

    private void ApplyCurrentRun(GovernedReportingRunDto? run)
    {
        var runChanged = _currentRun is not null
            && run is not null
            && !string.Equals(_currentRun.RunId, run.RunId, StringComparison.Ordinal);
        _currentRun = run;
        if (runChanged)
        {
            _lastDelivery = null;
            _distributionId = string.Empty;
            _recipientPrincipalId = string.Empty;
            _recipientPrincipalKind = ReportingAccessPrincipalKind.User;
            _destination = string.Empty;
            OnPropertyChanged(nameof(LastDelivery));
            OnPropertyChanged(nameof(DistributionId));
            OnPropertyChanged(nameof(RecipientPrincipalId));
            OnPropertyChanged(nameof(Destination));
            OnPropertyChanged(nameof(DistributionStatusText));
            ClearIssuedRecipientLink();
            ApplyDistributionHistory([], []);
            ApplyDistributionCapabilities(null);
        }

        if (run is not null)
        {
            _currentRunId = run.RunId;
            OnPropertyChanged(nameof(CurrentRunId));
            if (string.IsNullOrWhiteSpace(DistributionId))
            {
                _distributionId = $"desktop-{run.RunId}";
                OnPropertyChanged(nameof(DistributionId));
            }
        }

        OnPropertyChanged(nameof(CurrentRun));
        OnPropertyChanged(nameof(HasCurrentRun));
        OnPropertyChanged(nameof(LifecycleStateText));
        OnPropertyChanged(nameof(LifecyclePathText));
        OnPropertyChanged(nameof(ScopeSnapshotText));
        OnPropertyChanged(nameof(CertifiedSnapshotText));
        OnPropertyChanged(nameof(RetainedReadiness));
        OnPropertyChanged(nameof(RetainedReadinessStatusText));
        OnPropertyChanged(nameof(RetainedReadinessReceiptText));
        OnPropertyChanged(nameof(RetainedReadinessChecks));
        OnPropertyChanged(nameof(RetainedParameterRows));
        OnPropertyChanged(nameof(RetainedParameterStatusText));
        OnPropertyChanged(nameof(RetainedAccessPolicyText));
        OnPropertyChanged(nameof(RetainedAccessOwnerText));
        OnPropertyChanged(nameof(RetainedAccessPrincipals));
        OnPropertyChanged(nameof(ArtifactSummaryText));
        OnPropertyChanged(nameof(ReleasedArtifacts));
        OnPropertyChanged(nameof(MakerCheckerStatusText));
        OnPropertyChanged(nameof(RestatementStatusText));
        NotifyCommandStates();
    }

    private void ApplyDistributionCapabilities(SecureReportingDistributionCapabilityCatalog? capabilities)
    {
        _distributionCapabilities = capabilities;
        IReadOnlyList<SecureReportingTransportCapability> transports = capabilities?.Transports ?? [];
        if (transports.All(transport =>
                !string.Equals(transport.TransportId, _transportId, StringComparison.OrdinalIgnoreCase)))
        {
            _transportId = transports.FirstOrDefault(static transport => transport.IsReady)?.TransportId
                ?? transports.FirstOrDefault()?.TransportId
                ?? string.Empty;
            OnPropertyChanged(nameof(TransportId));
        }

        OnPropertyChanged(nameof(DistributionCapabilities));
        OnPropertyChanged(nameof(HasDistributionCapabilities));
        OnPropertyChanged(nameof(TransportOptions));
        OnPropertyChanged(nameof(SelectedTransport));
        OnPropertyChanged(nameof(DestinationFieldLabel));
        OnPropertyChanged(nameof(DestinationFieldHelp));
        OnPropertyChanged(nameof(CanIssueAccessGrant));
        OnPropertyChanged(nameof(CanRevokeAccessGrant));
        OnPropertyChanged(nameof(DistributionCapabilityStatusText));
        NotifyCommandStates();
    }

    private void ApplyDistributionHistory(
        IReadOnlyList<SecureReportingDeliveryResponse> deliveries,
        IReadOnlyList<SecureReportingAccessGrantSummaryResponse> grants)
    {
        _deliveryHistory = deliveries ?? [];
        _accessGrants = grants ?? [];
        if (_accessGrants.All(grant => !string.Equals(grant.GrantId, _selectedAccessGrantId, StringComparison.Ordinal)))
        {
            _selectedAccessGrantId = _accessGrants
                .FirstOrDefault(static grant =>
                    grant.RevokedAtUtc is null
                    && !string.Equals(grant.State, "Revoked", StringComparison.OrdinalIgnoreCase))
                ?.GrantId
                ?? _accessGrants.FirstOrDefault()?.GrantId
                ?? string.Empty;
            OnPropertyChanged(nameof(SelectedAccessGrantId));
        }

        OnPropertyChanged(nameof(DeliveryHistory));
        OnPropertyChanged(nameof(AccessGrants));
        OnPropertyChanged(nameof(SelectedAccessGrant));
        OnPropertyChanged(nameof(DistributionHistoryStatusText));
        NotifyCommandStates();
    }

    private void ClearIssuedRecipientLink()
    {
        _lastIssuedRecipientAccessUri = string.Empty;
        _lastIssuedGrantStatus = "No one-time recipient link is retained after the latest desktop action.";
        OnPropertyChanged(nameof(LastIssuedRecipientAccessUri));
        OnPropertyChanged(nameof(HasLastIssuedRecipientAccessUri));
        OnPropertyChanged(nameof(LastIssuedGrantStatus));
    }

    private bool CanAssessReadiness() => !IsBusy && TryBuildRunRequest(out _, out _);
    private bool CanGenerateGovernedRun() =>
        !IsBusy
        && Readiness is not null
        && (Finality == ReportingFinalityDto.Final ? Readiness.CanGenerateFinal : Readiness.CanGenerateDraft)
        && TryBuildRunRequest(out _, out _);
    private bool CanLoadGovernedRun() => !IsBusy && !string.IsNullOrWhiteSpace(CurrentRunId);
    private bool CanGovernCompletedRun() => !IsBusy && CurrentRun is null && !string.IsNullOrWhiteSpace(CurrentRunId);
    private bool CanValidate() => !IsBusy && IsRunActionAllowed(ValidateRunAction);
    private bool CanSubmit() => !IsBusy && IsRunActionAllowed(SubmitRunAction);
    private bool CanApprove() =>
        !IsBusy
        && IsRunActionAllowed(ApproveRunAction)
        && !string.IsNullOrWhiteSpace(ApprovalDecisionNote);
    private bool CanRelease() => !IsBusy && IsRunActionAllowed(ReleaseRunAction);
    private bool CanRequestRestatement() =>
        !IsBusy
        && IsRunActionAllowed(RequestRestatementAction)
        && !string.IsNullOrWhiteSpace(RestatementReason);
    private bool CanApproveRestatement() =>
        !IsBusy
        && IsRestatementActionAllowed(ApproveRestatementAction);
    private bool CanRefreshDistributionCapabilities() => !IsBusy && CurrentRun is not null;
    private bool CanQueueDelivery() => !IsBusy && TryBuildDeliveryCommand(out _, out _);
    private bool CanIssueAccessGrantCommand() => !IsBusy && TryBuildGrantIssueCommand(out _, out _);
    private bool CanRevokeAccessGrantCommand() =>
        !IsBusy
        && CanRevokeAccessGrant
        && SelectedAccessGrant is { RevokedAtUtc: null } grant
        && !string.Equals(grant.State, "Revoked", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(GrantRevocationReason);

    private ReportingGovernanceActionAvailabilityDto? RunActionCandidate(string action) =>
        CurrentRun?.ActionAvailability?.FirstOrDefault(candidate =>
            string.Equals(candidate.Action, action, StringComparison.OrdinalIgnoreCase));

    private ReportingGovernanceActionAvailabilityDto? RestatementActionCandidate(string action) =>
        CurrentRestatement?.ActionAvailability?.FirstOrDefault(candidate =>
            string.Equals(candidate.Action, action, StringComparison.OrdinalIgnoreCase));

    private ReportingGovernanceActionAvailabilityDto? RunAction(string action) =>
        RunActionCandidate(action) is { } candidate
        && CurrentRun is { } run
        && candidate.ExpectedVersion == run.Version
            ? candidate
            : null;

    private ReportingGovernanceActionAvailabilityDto? RestatementAction(string action) =>
        RestatementActionCandidate(action) is { } candidate
        && CurrentRestatement is { } restatement
        && candidate.ExpectedVersion == restatement.Version
            ? candidate
            : null;

    private bool IsRunActionAllowed(string action) => RunAction(action)?.IsAllowed == true;

    private bool IsRestatementActionAllowed(string action) => RestatementAction(action)?.IsAllowed == true;

    private string RunActionTooltip(string action, string allowedText) =>
        IsRunActionAllowed(action) ? allowedText : RunActionBlockedReason(action);

    private string RunActionBlockedReason(string action)
    {
        if (CurrentRun is null)
        {
            return "Load a governed run to obtain server-owned action availability.";
        }

        var candidate = RunActionCandidate(action);
        if (candidate is not null && candidate.ExpectedVersion != CurrentRun.Version)
        {
            return $"The server action projection targets retained version {candidate.ExpectedVersion}, but the loaded run is version {CurrentRun.Version}. Refresh before continuing.";
        }

        return candidate?.BlockedReason
            ?? $"The server did not advertise {action} as available for this caller and run.";
    }

    private string RestatementActionBlockedReason(string action)
    {
        if (CurrentRestatement is null)
        {
            return "Load a governed restatement request to obtain server-owned action availability.";
        }

        var candidate = RestatementActionCandidate(action);
        if (candidate is not null && candidate.ExpectedVersion != CurrentRestatement.Version)
        {
            return $"The server action projection targets retained version {candidate.ExpectedVersion}, but the loaded restatement is version {CurrentRestatement.Version}. Refresh before continuing.";
        }

        return candidate?.BlockedReason
            ?? $"The server did not advertise {action} as available for this caller and request.";
    }

    private ReportingGovernanceActionAvailabilityDto RequireAllowedRunAction(string action) =>
        RunAction(action) is { IsAllowed: true } availability
            ? availability
            : throw new InvalidOperationException(RunActionBlockedReason(action));

    private ReportingGovernanceActionAvailabilityDto RequireAllowedRestatementAction(string action) =>
        RestatementAction(action) is { IsAllowed: true } availability
            ? availability
            : throw new InvalidOperationException(RestatementActionBlockedReason(action));

    private GovernedReportingRunDto RequireCurrentRun() =>
        CurrentRun ?? throw new InvalidOperationException("No governed reporting run is loaded.");

    private void NotifyCommandStates()
    {
        AssessReadinessCommand.NotifyCanExecuteChanged();
        GenerateGovernedRunCommand.NotifyCanExecuteChanged();
        LoadGovernedRunCommand.NotifyCanExecuteChanged();
        GovernCompletedRunCommand.NotifyCanExecuteChanged();
        ValidateCommand.NotifyCanExecuteChanged();
        SubmitCommand.NotifyCanExecuteChanged();
        ApproveCommand.NotifyCanExecuteChanged();
        ReleaseCommand.NotifyCanExecuteChanged();
        RequestRestatementCommand.NotifyCanExecuteChanged();
        ApproveRestatementCommand.NotifyCanExecuteChanged();
        RefreshDistributionCapabilitiesCommand.NotifyCanExecuteChanged();
        QueueDeliveryCommand.NotifyCanExecuteChanged();
        IssueAccessGrantCommand.NotifyCanExecuteChanged();
        RevokeAccessGrantCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(AssessReadinessTooltip));
        OnPropertyChanged(nameof(GenerateRunTooltip));
        OnPropertyChanged(nameof(ValidateTooltip));
        OnPropertyChanged(nameof(SubmitTooltip));
        OnPropertyChanged(nameof(ApproveTooltip));
        OnPropertyChanged(nameof(ReleaseTooltip));
        OnPropertyChanged(nameof(RestatementTooltip));
        OnPropertyChanged(nameof(ApproveRestatementTooltip));
        OnPropertyChanged(nameof(DistributionCapabilityStatusText));
        OnPropertyChanged(nameof(QueueDeliveryTooltip));
        OnPropertyChanged(nameof(IssueAccessGrantTooltip));
        OnPropertyChanged(nameof(RevokeAccessGrantTooltip));
    }

    private bool SetRunParameter<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref storage, value, propertyName))
        {
            return false;
        }

        Readiness = null;
        StatusText = "Parameters changed; server readiness must be reassessed.";
        NotifyCommandStates();
        return true;
    }

    private bool SetDistributionField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref storage, value, propertyName))
        {
            return false;
        }

        QueueDeliveryCommand.NotifyCanExecuteChanged();
        IssueAccessGrantCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(QueueDeliveryTooltip));
        OnPropertyChanged(nameof(IssueAccessGrantTooltip));
        return true;
    }

    private void SetError(string message)
    {
        ErrorText = string.IsNullOrWhiteSpace(message) ? "Reporting operation failed." : message.Trim();
        StatusText = "Action failed; review the server response and retry after correcting the blocker.";
    }

    private static T RequireData<T>(ApiResponse<T> response, string operation) where T : class
    {
        if (response.Success && response.Data is not null)
        {
            return response.Data;
        }

        var detail = string.IsNullOrWhiteSpace(response.ErrorMessage)
            ? $"HTTP {response.StatusCode}"
            : response.ErrorMessage.Trim();
        if (detail.Length > 1_000)
        {
            detail = detail[..1_000];
        }

        throw new InvalidOperationException($"{operation} failed: {detail}");
    }

    private static bool TryParseOptionalPositiveInt(
        string value,
        string label,
        out int? parsed,
        out string error)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = string.Empty;
            return true;
        }

        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0)
        {
            parsed = number;
            error = string.Empty;
            return true;
        }

        error = $"{label} must be a positive whole number.";
        return false;
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<ReportingGovernedParameterRow> BuildRetainedParameterRows(
        ReportingRunParametersDto? parameters)
    {
        if (parameters is null)
        {
            return [];
        }

        var rows = new List<ReportingGovernedParameterRow>();
        void Add(string group, string name, string value) =>
            rows.Add(new ReportingGovernedParameterRow(group, name, value));
        void AddOptional(string group, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Add(group, name, value);
            }
        }

        Add("Scope", "Fund profile", parameters.Scope.FundProfileId);
        Add("Scope", "Entity scope", parameters.Scope.EntityScopeKind.ToString());
        AddOptional("Scope", "Entity id", parameters.Scope.EntityId);
        AddOptional("Scope", "Portfolio id", parameters.Scope.PortfolioId);
        AddOptional("Scope", "Investor id", parameters.Scope.InvestorId);
        Add("Period", "Period id", parameters.PeriodId);
        Add("Period", "As-of date", parameters.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("Ledger", "Ledger book id", parameters.LedgerBook.LedgerBookId?.ToString("D") ?? "None");
        Add("Ledger", "Ledger book code", parameters.LedgerBook.LedgerBookCode ?? "None");
        Add("Presentation", "Accounting basis", parameters.AccountingBasis.ToString());
        Add("Presentation", "Currency", parameters.PresentationCurrency);
        Add("Presentation", "Consolidation", parameters.ConsolidationLevel.ToString());
        Add("Output", "Format", parameters.OutputFormat.ToString());
        Add("Output", "Finality", parameters.Finality.ToString());
        Add("Output", "Supporting schedules", parameters.IncludeSupportingSchedules ? "Included" : "Excluded");
        Add("Output", "Evidence appendix", parameters.IncludeEvidenceAppendix ? "Included" : "Excluded");

        if (parameters.Scope.Dimensions is { } dimensions)
        {
            AddOptional("Dimension", "Fund", dimensions.FundId);
            AddOptional("Dimension", "Organization", dimensions.OrganizationId);
            AddOptional("Dimension", "Entity", dimensions.EntityId);
            AddOptional("Dimension", "Portfolio", dimensions.PortfolioId);
            AddOptional("Dimension", "Sleeve", dimensions.SleeveId);
            AddOptional("Dimension", "Strategy", dimensions.StrategyId);
            AddOptional("Dimension", "Investor", dimensions.InvestorId);
            AddOptional("Dimension", "Capital account", dimensions.CapitalAccountId);
            AddOptional("Dimension", "Instrument", dimensions.InstrumentId?.ToString("D"));
            AddOptional("Dimension", "Position", dimensions.PositionId?.ToString("D"));
            AddOptional("Dimension", "Tax lot", dimensions.TaxLotId);
            AddOptional("Dimension", "Cost center", dimensions.CostCenterId);
            AddOptional("Dimension", "Counterparty", dimensions.CounterpartyId);
            AddOptional("Dimension", "Book", dimensions.BookId);
            AddOptional("Dimension", "Account", dimensions.AccountId);
            AddOptional("Dimension", "Customer", dimensions.CustomerId);
            AddOptional("Dimension", "Vendor", dimensions.VendorId);
            AddOptional("Dimension", "Project", dimensions.ProjectId);
            foreach (var dimension in dimensions.ExternalGlDimensions.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                Add("External GL dimension", dimension.Key, dimension.Value);
            }
        }

        foreach (var parameter in parameters.TemplateParameters.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            Add("Template parameter", parameter.Key, parameter.Value);
        }

        return rows;
    }

    private static IReadOnlyList<ReportingScheduleReleaseHandoffRow> BuildScheduleReleaseHandoffRows(
        IReadOnlyList<ReportingScheduleRecordDto> schedules)
    {
        var rows = new List<ReportingScheduleReleaseHandoffRow>();
        foreach (var schedule in schedules.OrderBy(static schedule => schedule.ScheduleId, StringComparer.OrdinalIgnoreCase))
        {
            var policyHash = string.IsNullOrWhiteSpace(schedule.AccessPolicySnapshotHash)
                ? "Unavailable"
                : schedule.AccessPolicySnapshotHash;
            var handoffs = schedule.ReleaseDeliveryHandoffs ?? [];
            if (handoffs.Count == 0)
            {
                rows.Add(new ReportingScheduleReleaseHandoffRow(
                    schedule.ScheduleId,
                    policyHash,
                    "No retained handoff",
                    schedule.LastRunId ?? "No retained run",
                    "Not created",
                    "No governed recipient handoff retained",
                    "Not resolved",
                    "Not enqueued",
                    "Not created",
                    "Not enqueued"));
                continue;
            }

            foreach (var handoff in handoffs.OrderBy(static handoff => handoff.CreatedAtUtc))
            {
                var recipient = string.IsNullOrWhiteSpace(handoff.RecipientPrincipalId)
                    ? "Server-resolved governed audience"
                    : $"{(string.IsNullOrWhiteSpace(handoff.RecipientPrincipalKind) ? "Unspecified" : handoff.RecipientPrincipalKind)}:{handoff.RecipientPrincipalId}";
                rows.Add(new ReportingScheduleReleaseHandoffRow(
                    schedule.ScheduleId,
                    policyHash,
                    handoff.HandoffId,
                    handoff.RunId,
                    handoff.State.ToString(),
                    recipient,
                    handoff.TransportId,
                    handoff.EnqueuedDeliveryJobId ?? "Not enqueued",
                    handoff.CreatedAtUtc.ToString("u", CultureInfo.InvariantCulture),
                    handoff.EnqueuedAtUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "Not enqueued"));
            }
        }

        return rows;
    }

    private static string ValidateOneTimeRecipientAccessUri(string? value)
    {
        var uri = value?.Trim();
        var fragmentIndex = uri?.IndexOf('#') ?? -1;
        var path = fragmentIndex > 0 ? uri![..fragmentIndex] : string.Empty;
        var fragment = fragmentIndex > 0 ? uri![(fragmentIndex + 1)..] : string.Empty;
        if (string.IsNullOrWhiteSpace(uri)
            || !path.StartsWith("/", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains('\\')
            || uri.Any(char.IsControl)
            || uri.Contains('?')
            || uri.IndexOf('#', fragmentIndex + 1) >= 0
            || !fragment.StartsWith("token=", StringComparison.OrdinalIgnoreCase)
            || fragment.Length == "token=".Length
            || fragment.Contains('&'))
        {
            throw new InvalidOperationException(
                "The server returned an unsafe recipient link. Access grants must use an application-root path with the bearer only in the URI fragment.");
        }

        return uri;
    }

    private static string AllowedText(bool allowed) => allowed ? "allowed" : "blocked";

    private static string ShortHash(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unavailable"
            : value.Length <= 16
                ? value
                : value[..16];
}
