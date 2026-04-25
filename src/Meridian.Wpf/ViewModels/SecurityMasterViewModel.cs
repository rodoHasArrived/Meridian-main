using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Ui.Services;
using ISmQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ISmService = Meridian.Contracts.SecurityMaster.ISecurityMasterService;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Security Master workstation page.
/// Wraps <see cref="SecurityMasterWorkstationDto"/>-backed search and detail
/// surfaced by the <c>/api/workstation/security-master</c> endpoints.
/// </summary>
public sealed class SecurityMasterViewModel : BindableBase, IDisposable
{
    private readonly WpfServices.LoggingService _loggingService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly ITradingParametersBackfillService _backfillService;
    private readonly ISecurityMasterImportService _importService;
    private readonly ISecurityMasterRuntimeStatus _securityMasterRuntimeStatus;
    private readonly WpfServices.ISecurityMasterOperatorWorkflowClient _workflowClient;
    private readonly WpfServices.IWorkstationSecurityMasterApiClient _workstationSecurityMasterApiClient;
    private readonly WpfServices.FundContextService _fundContextService;
    private readonly WpfServices.NavigationService _navigationService;
    private readonly ISmQueryService _queryService;
    private readonly ISmService _service;
    private readonly bool _hasPolygonApiKey;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _workflowCts;
    private Task? _workflowPollingTask;

    // ── Public collections ──────────────────────────────────────────────────
    public ObservableCollection<SecurityMasterWorkstationDto> Results { get; } = new();
    public ObservableCollection<CorporateActionDto> CorporateActions { get; } = new();
    public ObservableCollection<SecurityMasterConflict> OpenConflicts { get; } = new();
    public ObservableCollection<SecurityConflictLaneGroup> ConflictGroups { get; } = new();
    public ObservableCollection<SecurityMasterSourceCandidateDto> ProvenanceCandidates { get; } = new();
    public ObservableCollection<SecurityMasterConflict> FilteredConflicts { get; } = new();
    public ObservableCollection<SecurityMasterRecommendedActionDto> RecommendedActions { get; } = new();
    public ObservableCollection<SecurityMasterImpactLinkDto> DownstreamImpactLinks { get; } = new();

    /// <summary>
    /// Static list of corporate action types available for recording.
    /// </summary>
    public IReadOnlyList<string> CorpActTypes => new[] { "Dividend", "StockSplit" };

    // ── Bindable properties ─────────────────────────────────────────────────
    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                RaiseSearchDerivedStateChanged();
            }
        }
    }

    private bool _activeOnly = true;
    public bool ActiveOnly
    {
        get => _activeOnly;
        set
        {
            if (SetProperty(ref _activeOnly, value))
            {
                RaiseSearchDerivedStateChanged();
            }
        }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    private string _statusText = "Enter a query and press Search.";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private SecurityMasterWorkstationDto? _selectedSecurity;
    public SecurityMasterWorkstationDto? SelectedSecurity
    {
        get => _selectedSecurity;
        set
        {
            var previousSecurityId = _selectedSecurity?.SecurityId;
            if (SetProperty(ref _selectedSecurity, value))
            {
                if (value is null || previousSecurityId != value.SecurityId)
                {
                    ClearSelectedSecurityAssuranceState();
                }

                if (value is not null)
                {
                    _conflictSecurityContextCache[value.SecurityId] = ToSecurityContext(value);
                }

                RaiseSelectionDerivedStateChanged();
                RebuildConflictLaneGroups();
            }
        }
    }

    private string _historyText = string.Empty;
    public string HistoryText
    {
        get => _historyText;
        private set
        {
            if (SetProperty(ref _historyText, value))
            {
                RaisePropertyChanged(nameof(LatestHistoryEventText));
            }
        }
    }

    private bool _isEditPanelVisible;
    public bool IsEditPanelVisible
    {
        get => _isEditPanelVisible;
        set => SetProperty(ref _isEditPanelVisible, value);
    }

    private bool _isDeactivatePanelVisible;
    public bool IsDeactivatePanelVisible
    {
        get => _isDeactivatePanelVisible;
        set => SetProperty(ref _isDeactivatePanelVisible, value);
    }

    private SecurityMasterEditViewModel? _editVm;
    public SecurityMasterEditViewModel? EditVm
    {
        get => _editVm;
        private set => SetProperty(ref _editVm, value);
    }

    private SecurityMasterDeactivateViewModel? _deactivateVm;
    public SecurityMasterDeactivateViewModel? DeactivateVm
    {
        get => _deactivateVm;
        private set => SetProperty(ref _deactivateVm, value);
    }

    private int _selectedDetailTab;
    public int SelectedDetailTab
    {
        get => _selectedDetailTab;
        set => SetProperty(ref _selectedDetailTab, value);
    }

    private bool _isRecordCorpActionVisible;
    public bool IsRecordCorpActionVisible
    {
        get => _isRecordCorpActionVisible;
        set => SetProperty(ref _isRecordCorpActionVisible, value);
    }

    private string _corpActType = "Dividend";
    public string CorpActType
    {
        get => _corpActType;
        set => SetProperty(ref _corpActType, value);
    }

    private string _corpActExDate = string.Empty;
    public string CorpActExDate
    {
        get => _corpActExDate;
        set => SetProperty(ref _corpActExDate, value);
    }

    private decimal _corpActAmount;
    public decimal CorpActAmount
    {
        get => _corpActAmount;
        set => SetProperty(ref _corpActAmount, value);
    }

    private string _corpActCurrency = "USD";
    public string CorpActCurrency
    {
        get => _corpActCurrency;
        set => SetProperty(ref _corpActCurrency, value);
    }

    private bool _isBackfillingTradingParams;
    public bool IsBackfillingTradingParams
    {
        get => _isBackfillingTradingParams;
        private set
        {
            if (SetProperty(ref _isBackfillingTradingParams, value))
            {
                RaisePropertyChanged(nameof(RuntimeStatusDetail));
            }
        }
    }

    private string _backfillStatus = string.Empty;
    public string BackfillStatus
    {
        get => _backfillStatus;
        private set
        {
            if (SetProperty(ref _backfillStatus, value))
            {
                RaisePropertyChanged(nameof(RuntimeStatusDetail));
            }
        }
    }

    // ── Import properties ───────────────────────────────────────────────────────
    private int _importTotal;
    public int ImportTotal
    {
        get => _importTotal;
        private set
        {
            if (SetProperty(ref _importTotal, value))
            {
                RaisePropertyChanged(nameof(ImportStatus));
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    private int _importProcessed;
    public int ImportProcessed
    {
        get => _importProcessed;
        private set
        {
            if (SetProperty(ref _importProcessed, value))
            {
                RaisePropertyChanged(nameof(ImportStatus));
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    private int _importImported;
    public int ImportImported
    {
        get => _importImported;
        private set
        {
            if (SetProperty(ref _importImported, value))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    private int _importFailed;
    public int ImportFailed
    {
        get => _importFailed;
        private set
        {
            if (SetProperty(ref _importFailed, value))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    private bool _isImporting;
    public bool IsImporting
    {
        get => _isImporting;
        private set
        {
            if (SetProperty(ref _isImporting, value))
            {
                ImportFromFileCommand.NotifyCanExecuteChanged();
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    public string ImportStatus
    {
        get
        {
            if (ImportTotal == 0)
                return string.Empty;
            return $"Importing {ImportProcessed}/{ImportTotal} ({ImportFailed} failed)";
        }
    }

    private bool _isImportResultVisible;
    public bool IsImportResultVisible
    {
        get => _isImportResultVisible;
        private set
        {
            if (SetProperty(ref _isImportResultVisible, value))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    private string _importResultSummary = string.Empty;
    public string ImportResultSummary
    {
        get => _importResultSummary;
        private set
        {
            if (SetProperty(ref _importResultSummary, value))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    private string _importSessionSummary = "No import activity recorded by the workstation service.";
    public string ImportSessionSummary
    {
        get => _importSessionSummary;
        private set
        {
            if (SetProperty(ref _importSessionSummary, value))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    private string _workflowStatusText = "Polling Security Master ingest and conflict posture.";
    public string WorkflowStatusText
    {
        get => _workflowStatusText;
        private set
        {
            if (SetProperty(ref _workflowStatusText, value))
            {
                RaisePropertyChanged(nameof(RuntimeStatusDetail));
            }
        }
    }

    private string _workflowRetrievedAtText = "-";
    public string WorkflowRetrievedAtText
    {
        get => _workflowRetrievedAtText;
        private set => SetProperty(ref _workflowRetrievedAtText, value);
    }

    private string _conflictOperatorText = "desktop-user";
    public string ConflictOperatorText
    {
        get => _conflictOperatorText;
        set
        {
            if (SetProperty(ref _conflictOperatorText, value))
            {
                NotifyConflictWorkflowCommandsChanged();
            }
        }
    }

    private string _conflictNoteText = string.Empty;
    public string ConflictNoteText
    {
        get => _conflictNoteText;
        set => SetProperty(ref _conflictNoteText, value);
    }

    private SecurityMasterConflict? _selectedConflict;
    public SecurityMasterConflict? SelectedConflict
    {
        get => _selectedConflict;
        set
        {
            if (SetProperty(ref _selectedConflict, value))
            {
                UpdateSelectedConflictAssessmentState();
                RebuildRecommendedActions();
                NotifyConflictWorkflowCommandsChanged();
            }
        }
    }

    private SecurityConflictLaneGroup? _selectedConflictGroup;
    public SecurityConflictLaneGroup? SelectedConflictGroup
    {
        get => _selectedConflictGroup;
        set
        {
            if (SetProperty(ref _selectedConflictGroup, value))
            {
                if (!_suppressConflictLaneSelectionSync &&
                    value is not null &&
                    (SelectedConflictEntry is null || value.SecurityId != SelectedConflictEntry.Conflict.SecurityId))
                {
                    SelectedConflictEntry = value.Conflicts.FirstOrDefault();
                }

                NotifyConflictWorkflowCommandsChanged();
            }
        }
    }

    private SecurityConflictLaneEntry? _selectedConflictEntry;
    public SecurityConflictLaneEntry? SelectedConflictEntry
    {
        get => _selectedConflictEntry;
        set
        {
            if (SetProperty(ref _selectedConflictEntry, value))
            {
                SelectedConflict = value?.Conflict;

                if (!_suppressConflictLaneSelectionSync &&
                    value is not null &&
                    SelectedConflictGroup?.SecurityId != value.Conflict.SecurityId)
                {
                    SelectedConflictGroup = ConflictGroups.FirstOrDefault(group => group.SecurityId == value.Conflict.SecurityId);
                }

                if (!_suppressConflictDrivenSelectionLoad &&
                    value is not null &&
                    HasSelectedSecurity &&
                    SelectedSecurity?.SecurityId != value.Conflict.SecurityId)
                {
                    _ = LoadDetailAsync(value.Conflict.SecurityId);
                }

                NotifyConflictWorkflowCommandsChanged();
            }
        }
    }

    private SecurityEconomicDefinitionRecord? _selectedEconomicDefinition;
    private TradingParametersDto? _selectedTradingParameters;
    private SecurityMasterEventEnvelope? _latestHistoryEvent;
    private readonly Dictionary<Guid, SecurityConflictSecurityContext> _conflictSecurityContextCache = new();
    private readonly Dictionary<Guid, SecurityMasterConflictAssessmentDto> _conflictAssessmentById = new();
    private bool _suppressConflictLaneSelectionSync;
    private bool _suppressConflictDrivenSelectionLoad;

    private SecurityMasterTrustSnapshotDto? _selectedTrustSnapshot;
    public SecurityMasterTrustSnapshotDto? SelectedTrustSnapshot
    {
        get => _selectedTrustSnapshot;
        private set => SetProperty(ref _selectedTrustSnapshot, value);
    }

    private bool _isTrustSnapshotLoading;
    public bool IsTrustSnapshotLoading
    {
        get => _isTrustSnapshotLoading;
        private set => SetProperty(ref _isTrustSnapshotLoading, value);
    }

    private bool _showOnlySelectedSecurityConflicts = true;
    public bool ShowOnlySelectedSecurityConflicts
    {
        get => _showOnlySelectedSecurityConflicts;
        set
        {
            if (SetProperty(ref _showOnlySelectedSecurityConflicts, value))
            {
                RebuildFilteredConflicts();
            }
        }
    }

    private bool _showHighImpactConflictsOnly;
    public bool ShowHighImpactConflictsOnly
    {
        get => _showHighImpactConflictsOnly;
        set
        {
            if (SetProperty(ref _showHighImpactConflictsOnly, value))
            {
                RebuildFilteredConflicts();
            }
        }
    }

    private bool _showBulkEligibleConflictsOnly;
    public bool ShowBulkEligibleConflictsOnly
    {
        get => _showBulkEligibleConflictsOnly;
        set
        {
            if (SetProperty(ref _showBulkEligibleConflictsOnly, value))
            {
                RebuildFilteredConflicts();
            }
        }
    }

    private string _trustScoreText = "Trust score unavailable.";
    public string TrustScoreText
    {
        get => _trustScoreText;
        private set => SetProperty(ref _trustScoreText, value);
    }

    private string _trustSummaryText = "Select a security to review trust posture.";
    public string TrustSummaryText
    {
        get => _trustSummaryText;
        private set => SetProperty(ref _trustSummaryText, value);
    }

    private string _goldenCopySourceText = "No golden copy selected.";
    public string GoldenCopySourceText
    {
        get => _goldenCopySourceText;
        private set => SetProperty(ref _goldenCopySourceText, value);
    }

    private string _goldenCopyRuleText = "Preserve winner unless the current winner is blank or equivalent.";
    public string GoldenCopyRuleText
    {
        get => _goldenCopyRuleText;
        private set => SetProperty(ref _goldenCopyRuleText, value);
    }

    private string _tradingParametersStatusText = "Trading parameter posture appears after selection.";
    public string TradingParametersStatusText
    {
        get => _tradingParametersStatusText;
        private set => SetProperty(ref _tradingParametersStatusText, value);
    }

    private string _provenanceSummaryText = "Winning-source provenance appears after selection.";
    public string ProvenanceSummaryText
    {
        get => _provenanceSummaryText;
        private set => SetProperty(ref _provenanceSummaryText, value);
    }

    private string _downstreamImpactSummaryText = "Downstream impact appears after selection.";
    public string DownstreamImpactSummaryText
    {
        get => _downstreamImpactSummaryText;
        private set => SetProperty(ref _downstreamImpactSummaryText, value);
    }

    private string _conflictFilterSummaryText = "Conflict filter summary unavailable.";
    public string ConflictFilterSummaryText
    {
        get => _conflictFilterSummaryText;
        private set => SetProperty(ref _conflictFilterSummaryText, value);
    }

    private string _selectedConflictRecommendedActionText = "Select a conflict to see the recommended action.";
    public string SelectedConflictRecommendedActionText
    {
        get => _selectedConflictRecommendedActionText;
        private set => SetProperty(ref _selectedConflictRecommendedActionText, value);
    }

    private string _selectedConflictWinnerText = "Select a conflict to compare the current winner.";
    public string SelectedConflictWinnerText
    {
        get => _selectedConflictWinnerText;
        private set => SetProperty(ref _selectedConflictWinnerText, value);
    }

    private string _selectedConflictImpactText = "Select a conflict to review downstream impact.";

    private string _corporateActionReadinessText = "Corporate-action readiness appears after selection.";
    public string CorporateActionReadinessText
    {
        get => _corporateActionReadinessText;
        private set => SetProperty(ref _corporateActionReadinessText, value);
    }

    private string _corporateActionImpactSummaryText = "Corporate-action impact appears after selection.";
    public string CorporateActionImpactSummaryText
    {
        get => _corporateActionImpactSummaryText;
        private set => SetProperty(ref _corporateActionImpactSummaryText, value);
    }

    private string _bulkPreviewSummaryText = "Preview low-risk bulk resolutions to inspect scope.";
    public string BulkPreviewSummaryText
    {
        get => _bulkPreviewSummaryText;
        private set => SetProperty(ref _bulkPreviewSummaryText, value);
    }

    private bool _canApplyRecommendedConflictResolution;
    public bool CanApplyRecommendedConflictResolution
    {
        get => _canApplyRecommendedConflictResolution;
        private set => SetProperty(ref _canApplyRecommendedConflictResolution, value);
    }

    private bool _canApplyBulkRecommendedResolutions;
    public bool CanApplyBulkRecommendedResolutions
    {
        get => _canApplyBulkRecommendedResolutions;
        private set => SetProperty(ref _canApplyBulkRecommendedResolutions, value);
    }

    // ── Derived display helpers ─────────────────────────────────────────────
    public bool HasSelectedSecurity => SelectedSecurity is not null;

    public int ResultCount => Results.Count;

    public string SearchScopeText => string.IsNullOrWhiteSpace(SearchQuery)
        ? ActiveOnly
            ? "Active-only scope ready. Enter a symbol, name, or identifier."
            : "All-status scope ready. Enter a symbol, name, or identifier."
        : $"{(ActiveOnly ? "Active-only" : "All-status")} scope • query \"{SearchQuery.Trim()}\"";

    public string SelectedAssetClass =>
        SelectedSecurity?.Classification.AssetClass ?? string.Empty;

    public string SelectedCurrency =>
        SelectedSecurity?.EconomicDefinition.Currency ?? string.Empty;

    public string SelectedStatusBadge =>
        SelectedSecurity?.Status.ToString() ?? string.Empty;

    public string SelectedIdentifier =>
        SelectedSecurity?.Classification.PrimaryIdentifierValue is { } v
            ? $"{SelectedSecurity!.Classification.PrimaryIdentifierKind}: {v}"
            : string.Empty;

    public string RuntimeStatusLabel => _hasPolygonApiKey
        ? "Polygon enrichment available"
        : "Manual enrichment only";

    public string RuntimeStatusDetail => IsBackfillingTradingParams
        ? BackfillStatus
        : $"{_securityMasterRuntimeStatus.AvailabilityDescription} {WorkflowStatusText}".Trim();

    public string ConflictSummaryText => HasOpenConflicts
        ? $"{OpenConflictCount} open conflict{(OpenConflictCount == 1 ? string.Empty : "s")} across {ConflictGroupCount} securit{(ConflictGroupCount == 1 ? "y" : "ies")} require triage."
        : "No open identifier conflicts detected.";

    public string ConflictQueueSummaryText => HasOpenConflicts
        ? $"{ConflictGroupCount} securit{(ConflictGroupCount == 1 ? "y" : "ies")} remain in the operator queue. Review the grouped impact before accepting a provider value."
        : "Conflict queue is clear. New ingest mismatches will appear here.";

    public string SelectionSummaryText => SelectedSecurity is null
        ? "Select a security to inspect identifiers, runtime state, history, and corporate actions."
        : $"{SelectedSecurity.DisplayName} • {SelectedAssetClass} • {SelectedCurrency}";

    public string SelectionLifecycleText => SelectedSecurity is null
        ? "No security selected."
        : $"Status {SelectedStatusBadge} • version {SelectedSecurity.EconomicDefinition?.Version ?? 0}";

    public string SelectedSecurityConflictSummaryText => SelectedSecurity is null
        ? "Conflict posture appears after a security is selected."
        : (SelectedTrustSnapshot?.TrustPosture.OpenConflictCount ?? GetSelectedSecurityConflictCount()) switch
        {
            0 => "No open conflicts currently affect this security.",
            1 => "1 open conflict still affects this security.",
            var count => $"{count} open conflicts still affect this security."
        };

    public string SelectedTrustPostureText
    {
        get
        {
            if (SelectedTrustSnapshot is not null)
            {
                return TrustSummaryText;
            }

            if (SelectedSecurity is null)
            {
                return "Select a security to determine whether the definition is ready for downstream use.";
            }

            var selectedConflictCount = GetSelectedSecurityConflictCount();
            if (selectedConflictCount > 0)
            {
                return "Do not trust this instrument definition downstream yet. Resolve the open conflict queue first.";
            }

            var missingTradingFields = GetMissingTradingParameterFields();
            if (missingTradingFields.Count > 0)
            {
                return "Reference data is stable, but downstream trading readiness is incomplete.";
            }

            return _selectedEconomicDefinition is null
                ? "Reference data is usable, but provenance details are unavailable from the workstation query surface."
                : "Definition looks ready for downstream portfolio, ledger, reconciliation, and report-pack use.";
        }
    }

    public string SelectedProvenanceText
    {
        get
        {
            if (SelectedTrustSnapshot is not null)
            {
                return ProvenanceSummaryText;
            }

            if (SelectedSecurity is null)
            {
                return "Identifier provenance appears after a security is selected.";
            }

            if (_selectedEconomicDefinition is null)
            {
                return "Identifier provenance is unavailable for the selected security.";
            }

            var provenance = _selectedEconomicDefinition.Provenance;
            var sourceSystem = TryGetJsonString(provenance, "sourceSystem") ?? "Unknown source";
            var updatedBy = TryGetJsonString(provenance, "updatedBy") ?? "Unknown actor";
            var sourceRecordId = TryGetJsonString(provenance, "sourceRecordId");
            var reason = TryGetJsonString(provenance, "reason");
            var asOf = TryGetJsonDateTimeOffset(provenance, "asOf");

            var sourceRecordText = string.IsNullOrWhiteSpace(sourceRecordId)
                ? string.Empty
                : $" • record {sourceRecordId}";
            var reasonText = string.IsNullOrWhiteSpace(reason)
                ? string.Empty
                : $" • {reason}";
            var asOfText = asOf.HasValue
                ? $" • {asOf.Value.LocalDateTime:g}"
                : string.Empty;

            return $"Source {sourceSystem}{sourceRecordText} • updated by {updatedBy}{asOfText}{reasonText}";
        }
    }

    public string SelectedTradingParameterCoverageText
    {
        get
        {
            if (SelectedTrustSnapshot is not null)
            {
                return TradingParametersStatusText;
            }

            if (SelectedSecurity is null)
            {
                return "Trading readiness appears after a security is selected.";
            }

            var missingTradingFields = GetMissingTradingParameterFields();
            if (missingTradingFields.Count == 0)
            {
                return _selectedTradingParameters is null
                    ? "Trading-parameter coverage could not be confirmed."
                    : $"Trading parameters complete as of {_selectedTradingParameters.AsOf.LocalDateTime:g}.";
            }

            return $"Trading parameters incomplete: missing {string.Join(", ", missingTradingFields)}.";
        }
    }

    public string CorporateActionSummaryText => SelectedTrustSnapshot is not null
        ? CorporateActionImpactSummaryText
        : SelectedSecurity is null
            ? "Corporate action timeline appears after a security is selected."
            : CorporateActions.Count == 0
                ? "No corporate actions recorded for the selected security."
                : $"{CorporateActions.Count} corporate action(s) loaded for the selected security.";

    public string LatestHistoryEventText
    {
        get
        {
            if (_latestHistoryEvent is not null)
            {
                return $"{_latestHistoryEvent.EventType} • v{_latestHistoryEvent.StreamVersion} • {_latestHistoryEvent.EventTimestamp.LocalDateTime:g} by {_latestHistoryEvent.Actor}";
            }

            if (string.IsNullOrWhiteSpace(HistoryText) || HistoryText == "(no history)")
            {
                return "No audit history loaded.";
            }

            return HistoryText
                       .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                       .FirstOrDefault()
                   ?? "No audit history loaded.";
        }
    }

    public string ImportSessionText => IsImporting
        ? ImportStatus
        : ImportSessionSummary;

    public int ConflictGroupCount => ConflictGroups.Count;

    public bool HasConflictGroups => ConflictGroups.Count > 0;

    public bool HasSelectedConflictGroup => SelectedConflictGroup is not null;

    public bool HasSelectedConflictEntry => SelectedConflictEntry is not null;

    public string SelectedConflictSummaryText => SelectedConflictEntry is null
        ? "Select a conflict to review the ingest-time mismatch and choose a resolution."
        : $"{SelectedConflictEntry.FieldLabel} mismatch on {SelectedConflictEntry.SecurityLabel}";

    public string SelectedConflictSeverityText => SelectedConflictEntry?.SeverityLabel ?? "Severity will appear after a conflict is selected.";

    public string SelectedConflictConfidenceText => SelectedConflictEntry?.ConfidenceLabel ?? "Confidence hints appear after a conflict is selected.";

    public string SelectedConflictImpactText
        => !string.IsNullOrWhiteSpace(_selectedConflictImpactText)
            ? _selectedConflictImpactText
            : SelectedConflictEntry?.ImpactDetail ?? "Downstream impact appears after a conflict is selected.";

    public string SelectedConflictAutoResolveText => SelectedConflictEntry?.AutoResolveHint ?? "Auto-resolve guidance appears after a conflict is selected.";

    public string SelectedConflictDetectedText => SelectedConflictEntry is null
        ? "Detection time appears after a conflict is selected."
        : $"Detected {SelectedConflictEntry.Conflict.DetectedAt.LocalDateTime:g} • {SelectedConflictEntry.Conflict.ConflictKind}";

    public string AcceptPrimaryConflictLabel => SelectedConflictEntry is null
        ? "Accept A"
        : $"Accept {SelectedConflictEntry.Conflict.ProviderA}";

    public string AcceptSecondaryConflictLabel => SelectedConflictEntry is null
        ? "Accept B"
        : $"Accept {SelectedConflictEntry.Conflict.ProviderB}";

    public string ActionInspectorLeadText
    {
        get
        {
            var recommendedAction = RecommendedActions.FirstOrDefault(action => action.IsEnabled);
            if (recommendedAction is not null)
            {
                return recommendedAction.Title;
            }

            var activeConflict = GetActiveConflictContextEntry();
            if (activeConflict is not null)
            {
                return activeConflict.NextStepSummary;
            }

            return HasSelectedSecurity
                ? SelectedTrustPostureText
                : "Select a security or open conflict to surface downstream operator paths.";
        }
    }

    public string ActionInspectorDetailText
    {
        get
        {
            var recommendedAction = RecommendedActions.FirstOrDefault(action => action.IsEnabled);
            if (recommendedAction is not null)
            {
                return recommendedAction.Detail;
            }

            var activeConflict = GetActiveConflictContextEntry();
            if (activeConflict is not null)
            {
                return activeConflict.ImpactDetail;
            }

            return HasSelectedSecurity
                ? SelectedTradingParameterCoverageText
                : ConflictSummaryText;
        }
    }

    public string ActionInspectorNoActionText => HasSelectedSecurity
        ? "No downstream fund-review, reconciliation, or report-pack jump is currently required for this selection."
        : "No downstream jump is required until a security or conflict is selected.";

    public bool ShowFundReviewActions => GetActiveConflictContextEntry()?.RoutesToFundReview == true;

    public bool ShowReconciliationAction => GetActiveConflictContextEntry()?.RoutesToReconciliation == true;

    public bool ShowCashFlowAction => GetActiveConflictContextEntry()?.RoutesToCashFlow == true;

    public bool ShowReportPackAction => GetActiveConflictContextEntry()?.RoutesToReportPack == true;

    public bool ShowBackfillTradingParamsAction => SelectedTrustSnapshot?.TrustPosture.TradingParametersComplete == false ||
        GetMissingTradingParameterFields().Count > 0 ||
        GetActiveConflictContextEntry()?.RequiresTradingBackfill == true;

    public bool ShowAnyDownstreamAction =>
        ShowFundReviewActions ||
        ShowReconciliationAction ||
        ShowCashFlowAction ||
        ShowReportPackAction ||
        ShowBackfillTradingParamsAction;

    public bool HasPortfolioImpactLink => HasActiveImpactLink("portfolio");

    public bool HasLedgerImpactLink => HasActiveImpactLink("ledger");

    public bool HasReconciliationImpactLink => HasActiveImpactLink("reconciliation");

    public bool HasReportPackImpactLink => HasActiveImpactLink("reportPack");

    // ── Commands ────────────────────────────────────────────────────────────
    public IRelayCommand CreateNewCommand { get; }
    public IRelayCommand EditSelectedCommand { get; }
    public IRelayCommand DeactivateSelectedCommand { get; }
    public IRelayCommand LoadCorporateActionsCommand { get; }
    public IRelayCommand ShowRecordCorpActionCommand { get; }
    public IRelayCommand CancelRecordCorpActionCommand { get; }
    public IRelayCommand RecordCorpActionCommand { get; }
    public IAsyncRelayCommand BackfillTradingParamsCommand { get; }
    public IAsyncRelayCommand ImportFromFileCommand { get; }
    public IRelayCommand CloseImportResultCommand { get; }
    public IAsyncRelayCommand RefreshConflictCountCommand { get; }
    public IAsyncRelayCommand RefreshWorkflowCommand { get; }
    public IAsyncRelayCommand RefreshSelectedTrustSnapshotCommand { get; }
    public IAsyncRelayCommand AcceptPrimaryConflictCommand { get; }
    public IAsyncRelayCommand AcceptSecondaryConflictCommand { get; }
    public IAsyncRelayCommand DismissConflictCommand { get; }
    public IAsyncRelayCommand ApplyRecommendedConflictResolutionCommand { get; }
    public IRelayCommand ResetConflictFiltersCommand { get; }
    public IRelayCommand PreviewBulkRecommendedResolutionsCommand { get; }
    public IAsyncRelayCommand ApplyBulkRecommendedResolutionsCommand { get; }
    public IRelayCommand OpenFundPortfolioCommand { get; }
    public IRelayCommand OpenFundLedgerCommand { get; }
    public IRelayCommand OpenFundReconciliationCommand { get; }
    public IRelayCommand OpenFundCashFlowCommand { get; }
    public IRelayCommand OpenFundReportPackCommand { get; }
    public IRelayCommand OpenSelectedSecurityPortfolioImpactCommand { get; }
    public IRelayCommand OpenSelectedSecurityLedgerImpactCommand { get; }
    public IRelayCommand OpenSelectedSecurityReconciliationImpactCommand { get; }
    public IRelayCommand OpenSelectedSecurityReportPackImpactCommand { get; }
    public IRelayCommand CopySelectedIdentifierCommand { get; }

    // ── Conflict badge ───────────────────────────────────────────────────────
    private int _openConflictCount;
    /// <summary>Number of open identifier conflicts detected in Security Master. Drives the badge.</summary>
    public int OpenConflictCount
    {
        get => _openConflictCount;
        private set
        {
            if (SetProperty(ref _openConflictCount, value))
            {
                RaisePropertyChanged(nameof(HasOpenConflicts));
                RaiseConflictDerivedStateChanged();
            }
        }
    }

    /// <summary>True when at least one open conflict exists. Drives badge visibility.</summary>
    public bool HasOpenConflicts => _openConflictCount > 0;

    public bool CanResolveSelectedConflict =>
        SelectedConflict is not null &&
        !string.IsNullOrWhiteSpace(ConflictOperatorText);

    // ── Constructor ─────────────────────────────────────────────────────────
    public SecurityMasterViewModel(
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService,
        ITradingParametersBackfillService backfillService,
        ISecurityMasterImportService importService,
        ISecurityMasterRuntimeStatus securityMasterRuntimeStatus,
        WpfServices.ISecurityMasterOperatorWorkflowClient workflowClient,
        WpfServices.IWorkstationSecurityMasterApiClient workstationSecurityMasterApiClient,
        WpfServices.FundContextService fundContextService,
        WpfServices.NavigationService navigationService,
        ISmQueryService queryService,
        ISmService service)
    {
        _loggingService = loggingService;
        _notificationService = notificationService;
        _backfillService = backfillService;
        _importService = importService;
        _securityMasterRuntimeStatus = securityMasterRuntimeStatus ?? throw new ArgumentNullException(nameof(securityMasterRuntimeStatus));
        _workflowClient = workflowClient ?? throw new ArgumentNullException(nameof(workflowClient));
        _workstationSecurityMasterApiClient = workstationSecurityMasterApiClient ?? throw new ArgumentNullException(nameof(workstationSecurityMasterApiClient));
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _queryService = queryService;
        _service = service;
        _hasPolygonApiKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POLYGON_API_KEY"));

        CreateNewCommand = new RelayCommand(OnCreateNew);
        EditSelectedCommand = new RelayCommand(OnEditSelected, () => HasSelectedSecurity);
        DeactivateSelectedCommand = new RelayCommand(OnDeactivateSelected, () => HasSelectedSecurity && IsSelectedSecurityActive());
        LoadCorporateActionsCommand = new AsyncRelayCommand(OnLoadCorporateActions, () => HasSelectedSecurity);
        ShowRecordCorpActionCommand = new RelayCommand(OnShowRecordCorpAction, () => HasSelectedSecurity);
        CancelRecordCorpActionCommand = new RelayCommand(OnCancelRecordCorpAction);
        RecordCorpActionCommand = new AsyncRelayCommand(OnRecordCorpAction);
        BackfillTradingParamsCommand = new AsyncRelayCommand(OnBackfillTradingParams);
        ImportFromFileCommand = new AsyncRelayCommand(OnImportFromFile, () => !IsImporting);
        CloseImportResultCommand = new RelayCommand(OnCloseImportResult);
        RefreshConflictCountCommand = new AsyncRelayCommand(RefreshConflictCountAsync);
        RefreshWorkflowCommand = new AsyncRelayCommand(RefreshOperatorWorkflowAsync);
        RefreshSelectedTrustSnapshotCommand = new AsyncRelayCommand(
            ct => HasSelectedSecurity && SelectedSecurity is not null
                ? LoadSelectedTrustSnapshotAsync(SelectedSecurity.SecurityId, ct)
                : Task.CompletedTask,
            () => HasSelectedSecurity && !IsTrustSnapshotLoading);
        AcceptPrimaryConflictCommand = new AsyncRelayCommand(ct => ResolveSelectedConflictAsync("AcceptA", ct), () => CanResolveSelectedConflict);
        AcceptSecondaryConflictCommand = new AsyncRelayCommand(ct => ResolveSelectedConflictAsync("AcceptB", ct), () => CanResolveSelectedConflict);
        DismissConflictCommand = new AsyncRelayCommand(ct => ResolveSelectedConflictAsync("Dismiss", ct), () => CanResolveSelectedConflict);
        ApplyRecommendedConflictResolutionCommand = new AsyncRelayCommand(
            ApplyRecommendedConflictResolutionAsync,
            () => CanApplyRecommendedConflictResolution);
        ResetConflictFiltersCommand = new RelayCommand(ResetConflictFilters);
        PreviewBulkRecommendedResolutionsCommand = new RelayCommand(PreviewBulkRecommendedResolutions);
        ApplyBulkRecommendedResolutionsCommand = new AsyncRelayCommand(
            ApplyBulkRecommendedResolutionsAsync,
            () => CanApplyBulkRecommendedResolutions);
        OpenFundPortfolioCommand = new RelayCommand(() => _navigationService.NavigateTo("FundPortfolio"));
        OpenFundLedgerCommand = new RelayCommand(() => _navigationService.NavigateTo("FundLedger"));
        OpenFundReconciliationCommand = new RelayCommand(() => _navigationService.NavigateTo("FundReconciliation"));
        OpenFundCashFlowCommand = new RelayCommand(() => _navigationService.NavigateTo("FundCashFinancing"));
        OpenFundReportPackCommand = new RelayCommand(() => _navigationService.NavigateTo("FundReportPack"));
        OpenSelectedSecurityPortfolioImpactCommand = new RelayCommand(
            () => NavigateToFundOperations("FundPortfolio", FundOperationsTab.Portfolio),
            () => HasActiveImpactLink("portfolio"));
        OpenSelectedSecurityLedgerImpactCommand = new RelayCommand(
            () => NavigateToFundOperations("FundLedger", FundOperationsTab.Journal),
            () => HasActiveImpactLink("ledger"));
        OpenSelectedSecurityReconciliationImpactCommand = new RelayCommand(
            () => NavigateToFundOperations("FundReconciliation", FundOperationsTab.Reconciliation),
            () => HasActiveImpactLink("reconciliation"));
        OpenSelectedSecurityReportPackImpactCommand = new RelayCommand(
            () => NavigateToFundOperations("FundReportPack", FundOperationsTab.ReportPack),
            () => HasActiveImpactLink("reportPack"));
        CopySelectedIdentifierCommand = new RelayCommand(CopySelectedIdentifier, () => !string.IsNullOrWhiteSpace(SelectedIdentifier));

        Results.CollectionChanged += (_, _) => RaiseSearchDerivedStateChanged();
        CorporateActions.CollectionChanged += (_, _) => RaiseSelectionDerivedStateChanged();
        OpenConflicts.CollectionChanged += (_, _) => RaiseConflictDerivedStateChanged();
        ConflictGroups.CollectionChanged += (_, _) => RaiseConflictDerivedStateChanged();
        ProvenanceCandidates.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(ProvenanceSummaryText));
        FilteredConflicts.CollectionChanged += (_, _) => RaiseConflictDerivedStateChanged();
        RecommendedActions.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(RecommendedActions));
        DownstreamImpactLinks.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(DownstreamImpactLinks));

        StartWorkflowPolling();
    }

    private void OnCreateNew()
    {
        EditVm = SecurityMasterEditViewModel.CreateNew(_loggingService, _notificationService, _service);
        WireEditVmEvents();
        IsEditPanelVisible = true;
    }

    private void OnEditSelected()
    {
        if (SelectedSecurity is null)
            return;

        // Fetch the full detail so we have all the required information
        _ = LoadAndEditAsync();
    }

    private async Task LoadAndEditAsync()
    {
        if (SelectedSecurity?.SecurityId is not { } id)
            return;

        try
        {
            var detail = await _queryService.GetByIdAsync(id, CancellationToken.None)
                .ConfigureAwait(false);

            if (detail is not null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    EditVm = new SecurityMasterEditViewModel(_loggingService, _notificationService, _service);
                    EditVm.LoadForEdit(detail);
                    WireEditVmEvents();
                    IsEditPanelVisible = true;
                });
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Failed to load security {id} for edit", ex);
            StatusText = "Failed to load security for editing.";
            _notificationService.ShowNotification("Security Master", "Failed to load security.", NotificationType.Error);
        }
    }

    private void OnDeactivateSelected()
    {
        if (SelectedSecurity is null)
            return;

        DeactivateVm = new SecurityMasterDeactivateViewModel(_loggingService, _notificationService, _service)
        {
            SecurityName = SelectedSecurity.DisplayName,
            SecurityId = SelectedSecurity.SecurityId,
            Version = SelectedSecurity.EconomicDefinition?.Version ?? 0
        };
        WireDeactivateVmEvents();
        IsDeactivatePanelVisible = true;
    }

    private bool IsSelectedSecurityActive()
    {
        return SelectedSecurity?.Status == SecurityStatusDto.Active;
    }

    private void RaiseSearchDerivedStateChanged()
    {
        RaisePropertyChanged(nameof(ResultCount));
        RaisePropertyChanged(nameof(SearchScopeText));
    }

    private void RaiseSelectionDerivedStateChanged()
    {
        RaisePropertyChanged(nameof(HasSelectedSecurity));
        RaisePropertyChanged(nameof(SelectedAssetClass));
        RaisePropertyChanged(nameof(SelectedCurrency));
        RaisePropertyChanged(nameof(SelectedStatusBadge));
        RaisePropertyChanged(nameof(SelectedIdentifier));
        RaisePropertyChanged(nameof(SelectionSummaryText));
        RaisePropertyChanged(nameof(SelectionLifecycleText));
        RaisePropertyChanged(nameof(SelectedSecurityConflictSummaryText));
        RaisePropertyChanged(nameof(SelectedTrustPostureText));
        RaisePropertyChanged(nameof(SelectedProvenanceText));
        RaisePropertyChanged(nameof(SelectedTradingParameterCoverageText));
        RaisePropertyChanged(nameof(CorporateActionSummaryText));
        RaisePropertyChanged(nameof(LatestHistoryEventText));
        RaisePropertyChanged(nameof(TrustScoreText));
        RaisePropertyChanged(nameof(TrustSummaryText));
        RaisePropertyChanged(nameof(GoldenCopySourceText));
        RaisePropertyChanged(nameof(GoldenCopyRuleText));
        RaisePropertyChanged(nameof(TradingParametersStatusText));
        RaisePropertyChanged(nameof(ProvenanceSummaryText));
        RaisePropertyChanged(nameof(DownstreamImpactSummaryText));
        RaisePropertyChanged(nameof(CorporateActionReadinessText));
        RaisePropertyChanged(nameof(CorporateActionImpactSummaryText));
        RaisePropertyChanged(nameof(ActionInspectorLeadText));
        RaisePropertyChanged(nameof(ActionInspectorDetailText));
        RaisePropertyChanged(nameof(ActionInspectorNoActionText));
        RaisePropertyChanged(nameof(ShowBackfillTradingParamsAction));
        RaisePropertyChanged(nameof(ShowAnyDownstreamAction));
        RaisePropertyChanged(nameof(HasPortfolioImpactLink));
        RaisePropertyChanged(nameof(HasLedgerImpactLink));
        RaisePropertyChanged(nameof(HasReconciliationImpactLink));
        RaisePropertyChanged(nameof(HasReportPackImpactLink));
        AlignConflictLaneToSelectedSecurity();
        NotifySelectionCommandsChanged();
    }

    private void NotifySelectionCommandsChanged()
    {
        EditSelectedCommand.NotifyCanExecuteChanged();
        DeactivateSelectedCommand.NotifyCanExecuteChanged();
        LoadCorporateActionsCommand.NotifyCanExecuteChanged();
        ShowRecordCorpActionCommand.NotifyCanExecuteChanged();
        RefreshSelectedTrustSnapshotCommand.NotifyCanExecuteChanged();
        CopySelectedIdentifierCommand.NotifyCanExecuteChanged();
        OpenSelectedSecurityPortfolioImpactCommand.NotifyCanExecuteChanged();
        OpenSelectedSecurityLedgerImpactCommand.NotifyCanExecuteChanged();
        OpenSelectedSecurityReconciliationImpactCommand.NotifyCanExecuteChanged();
        OpenSelectedSecurityReportPackImpactCommand.NotifyCanExecuteChanged();
    }

    private void NotifyConflictWorkflowCommandsChanged()
    {
        RaiseConflictDerivedStateChanged();
        RaisePropertyChanged(nameof(SelectedConflictSummaryText));
        RaisePropertyChanged(nameof(CanResolveSelectedConflict));
        AcceptPrimaryConflictCommand.NotifyCanExecuteChanged();
        AcceptSecondaryConflictCommand.NotifyCanExecuteChanged();
        DismissConflictCommand.NotifyCanExecuteChanged();
        ApplyRecommendedConflictResolutionCommand.NotifyCanExecuteChanged();
        ApplyBulkRecommendedResolutionsCommand.NotifyCanExecuteChanged();
    }

    private void RaiseConflictDerivedStateChanged()
    {
        RaisePropertyChanged(nameof(ConflictSummaryText));
        RaisePropertyChanged(nameof(ConflictQueueSummaryText));
        RaisePropertyChanged(nameof(ConflictGroupCount));
        RaisePropertyChanged(nameof(HasConflictGroups));
        RaisePropertyChanged(nameof(HasSelectedConflictGroup));
        RaisePropertyChanged(nameof(HasSelectedConflictEntry));
        RaisePropertyChanged(nameof(SelectedConflictSummaryText));
        RaisePropertyChanged(nameof(SelectedConflictSeverityText));
        RaisePropertyChanged(nameof(SelectedConflictConfidenceText));
        RaisePropertyChanged(nameof(SelectedConflictImpactText));
        RaisePropertyChanged(nameof(SelectedConflictAutoResolveText));
        RaisePropertyChanged(nameof(SelectedConflictDetectedText));
        RaisePropertyChanged(nameof(ConflictFilterSummaryText));
        RaisePropertyChanged(nameof(SelectedConflictRecommendedActionText));
        RaisePropertyChanged(nameof(SelectedConflictWinnerText));
        RaisePropertyChanged(nameof(BulkPreviewSummaryText));
        RaisePropertyChanged(nameof(CanApplyRecommendedConflictResolution));
        RaisePropertyChanged(nameof(CanApplyBulkRecommendedResolutions));
        RaisePropertyChanged(nameof(AcceptPrimaryConflictLabel));
        RaisePropertyChanged(nameof(AcceptSecondaryConflictLabel));
        RaisePropertyChanged(nameof(SelectedSecurityConflictSummaryText));
        RaisePropertyChanged(nameof(SelectedTrustPostureText));
        RaisePropertyChanged(nameof(ActionInspectorLeadText));
        RaisePropertyChanged(nameof(ActionInspectorDetailText));
        RaisePropertyChanged(nameof(ActionInspectorNoActionText));
        RaisePropertyChanged(nameof(ShowFundReviewActions));
        RaisePropertyChanged(nameof(ShowReconciliationAction));
        RaisePropertyChanged(nameof(ShowCashFlowAction));
        RaisePropertyChanged(nameof(ShowReportPackAction));
        RaisePropertyChanged(nameof(ShowBackfillTradingParamsAction));
        RaisePropertyChanged(nameof(ShowAnyDownstreamAction));
        RaisePropertyChanged(nameof(HasPortfolioImpactLink));
        RaisePropertyChanged(nameof(HasLedgerImpactLink));
        RaisePropertyChanged(nameof(HasReconciliationImpactLink));
        RaisePropertyChanged(nameof(HasReportPackImpactLink));
    }

    private void ClearSelectedSecurityAssuranceState()
    {
        SelectedTrustSnapshot = null;
        _selectedEconomicDefinition = null;
        _selectedTradingParameters = null;
        _latestHistoryEvent = null;
        _conflictAssessmentById.Clear();
        ProvenanceCandidates.Clear();
        FilteredConflicts.Clear();
        RecommendedActions.Clear();
        DownstreamImpactLinks.Clear();
        TrustScoreText = "Trust score unavailable.";
        TrustSummaryText = "Select a security to review trust posture.";
        GoldenCopySourceText = "No golden copy selected.";
        GoldenCopyRuleText = "Preserve winner unless the current winner is blank or equivalent.";
        TradingParametersStatusText = "Trading parameter posture appears after selection.";
        ProvenanceSummaryText = "Winning-source provenance appears after selection.";
        DownstreamImpactSummaryText = "Downstream impact appears after selection.";
        ConflictFilterSummaryText = "Conflict filter summary unavailable.";
        SelectedConflictRecommendedActionText = "Select a conflict to see the recommended action.";
        SelectedConflictWinnerText = "Select a conflict to compare the current winner.";
        _selectedConflictImpactText = "Select a conflict to review downstream impact.";
        CorporateActionReadinessText = "Corporate-action readiness appears after selection.";
        CorporateActionImpactSummaryText = "Corporate-action impact appears after selection.";
        BulkPreviewSummaryText = "Preview low-risk bulk resolutions to inspect scope.";
        CanApplyRecommendedConflictResolution = false;
        CanApplyBulkRecommendedResolutions = false;

        RaisePropertyChanged(nameof(SelectedTrustPostureText));
        RaisePropertyChanged(nameof(SelectedProvenanceText));
        RaisePropertyChanged(nameof(SelectedTradingParameterCoverageText));
        RaisePropertyChanged(nameof(LatestHistoryEventText));
        RaisePropertyChanged(nameof(TrustScoreText));
        RaisePropertyChanged(nameof(TrustSummaryText));
        RaisePropertyChanged(nameof(GoldenCopySourceText));
        RaisePropertyChanged(nameof(GoldenCopyRuleText));
        RaisePropertyChanged(nameof(TradingParametersStatusText));
        RaisePropertyChanged(nameof(ProvenanceSummaryText));
        RaisePropertyChanged(nameof(DownstreamImpactSummaryText));
        RaisePropertyChanged(nameof(ConflictFilterSummaryText));
        RaisePropertyChanged(nameof(SelectedConflictRecommendedActionText));
        RaisePropertyChanged(nameof(SelectedConflictWinnerText));
        RaisePropertyChanged(nameof(SelectedConflictImpactText));
        RaisePropertyChanged(nameof(CorporateActionReadinessText));
        RaisePropertyChanged(nameof(CorporateActionImpactSummaryText));
        RaisePropertyChanged(nameof(BulkPreviewSummaryText));
        RaisePropertyChanged(nameof(ActionInspectorLeadText));
        RaisePropertyChanged(nameof(ActionInspectorDetailText));
        RaisePropertyChanged(nameof(ShowBackfillTradingParamsAction));
        RaisePropertyChanged(nameof(ShowAnyDownstreamAction));
        RaisePropertyChanged(nameof(HasPortfolioImpactLink));
        RaisePropertyChanged(nameof(HasLedgerImpactLink));
        RaisePropertyChanged(nameof(HasReconciliationImpactLink));
        RaisePropertyChanged(nameof(HasReportPackImpactLink));
    }

    private void AlignConflictLaneToSelectedSecurity()
    {
        if (SelectedSecurity is null || ConflictGroups.Count == 0)
        {
            return;
        }

        var matchingGroup = ConflictGroups.FirstOrDefault(group => group.SecurityId == SelectedSecurity.SecurityId);
        if (matchingGroup is null)
        {
            return;
        }

        if (SelectedConflictEntry is not null && SelectedConflictEntry.Conflict.SecurityId == matchingGroup.SecurityId)
        {
            if (SelectedConflictGroup != matchingGroup)
            {
                _suppressConflictLaneSelectionSync = true;
                try
                {
                    SelectedConflictGroup = matchingGroup;
                }
                finally
                {
                    _suppressConflictLaneSelectionSync = false;
                }
            }

            return;
        }

        _suppressConflictLaneSelectionSync = true;
        try
        {
            SelectedConflictGroup = matchingGroup;
            SelectedConflictEntry = matchingGroup.Conflicts.FirstOrDefault();
        }
        finally
        {
            _suppressConflictLaneSelectionSync = false;
        }
    }

    private void WireEditVmEvents()
    {
        if (EditVm is null)
            return;

        EditVm.CancelRequested += OnEditCancelled;
        EditVm.SaveCompleted += OnEditSaveCompleted;
    }

    private void UnwireEditVmEvents()
    {
        if (EditVm is null)
            return;

        EditVm.CancelRequested -= OnEditCancelled;
        EditVm.SaveCompleted -= OnEditSaveCompleted;
    }

    private void OnEditCancelled()
    {
        UnwireEditVmEvents();
        IsEditPanelVisible = false;
    }

    private void OnEditSaveCompleted(SecurityDetailDto result)
    {
        UnwireEditVmEvents();
        IsEditPanelVisible = false;
        StatusText = "Security saved successfully.";

        // Refresh search results
        _ = SearchAsync();
    }

    private void WireDeactivateVmEvents()
    {
        if (DeactivateVm is null)
            return;

        DeactivateVm.CancelRequested += OnDeactivateCancelled;
        DeactivateVm.DeactivateCompleted += OnDeactivateCompleted;
    }

    private void UnwireDeactivateVmEvents()
    {
        if (DeactivateVm is null)
            return;

        DeactivateVm.CancelRequested -= OnDeactivateCancelled;
        DeactivateVm.DeactivateCompleted -= OnDeactivateCompleted;
    }

    private void OnDeactivateCancelled()
    {
        UnwireDeactivateVmEvents();
        IsDeactivatePanelVisible = false;
    }

    private void OnDeactivateCompleted()
    {
        UnwireDeactivateVmEvents();
        IsDeactivatePanelVisible = false;
        StatusText = "Security deactivated successfully.";

        // Refresh search results
        _ = SearchAsync();
    }

    private async Task OnBackfillTradingParams()
    {
        try
        {
            IsBackfillingTradingParams = true;
            BackfillStatus = "Starting trading parameters backfill…";

            await _backfillService.BackfillAllAsync().ConfigureAwait(false);

            BackfillStatus = "Trading parameters backfill completed successfully.";
            _notificationService.ShowNotification("Security Master",
                "Trading parameters backfilled successfully.", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Trading parameters backfill failed", ex);
            BackfillStatus = "Backfill failed. Check logs for details.";
            _notificationService.ShowNotification("Security Master",
                "Trading parameters backfill failed.", NotificationType.Error);
        }
        finally
        {
            IsBackfillingTradingParams = false;
        }
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────
    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _workflowCts?.Cancel();
        _workflowCts?.Dispose();
        _workflowCts = null;
        _workflowPollingTask = null;
    }

    // ── Search ──────────────────────────────────────────────────────────────
    public async Task SearchAsync(CancellationToken ct = default)
    {
        var query = SearchQuery.Trim();
        if (string.IsNullOrEmpty(query))
        {
            StatusText = "Enter a query and press Search.";
            return;
        }

        if (!_securityMasterRuntimeStatus.IsAvailable)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            Results.Clear();
            SelectedSecurity = null;
            HistoryText = string.Empty;
            ClearSelectedSecurityAssuranceState();
            StatusText = _securityMasterRuntimeStatus.AvailabilityDescription;
            return;
        }

        _cts?.Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linked = _cts.Token;

        IsLoading = true;
        Results.Clear();
        SelectedSecurity = null;
        HistoryText = string.Empty;
        ClearSelectedSecurityAssuranceState();
        StatusText = "Searching…";

        try
        {
            var endpoint = $"/api/workstation/security-master/securities" +
                           $"?query={Uri.EscapeDataString(query)}&take=50&activeOnly={ActiveOnly}";

            var results = await ApiClientService.Instance
                .GetAsync<SecurityMasterWorkstationDto[]>(endpoint, linked)
                .ConfigureAwait(false);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Results.Clear();
                if (results is { Length: > 0 })
                {
                    foreach (var r in results)
                        Results.Add(r);
                    StatusText = $"{results.Length} result{(results.Length == 1 ? "" : "s")} found.";
                }
                else
                {
                    StatusText = "No securities matched the query.";
                }
            });
        }
        catch (OperationCanceledException)
        {
            StatusText = "Search cancelled.";
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Security Master search failed", ex);
            StatusText = "Search failed. Check connection to backend.";
            _notificationService.ShowNotification("Security Master", "Search failed.", NotificationType.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Detail + history ─────────────────────────────────────────────────────
    public async Task LoadDetailAsync(Guid securityId, CancellationToken ct = default)
        => await LoadSelectedTrustSnapshotAsync(securityId, ct).ConfigureAwait(false);

    public async Task LoadSelectedTrustSnapshotAsync(Guid securityId, CancellationToken ct = default)
    {
        if (securityId == Guid.Empty)
        {
            return;
        }

        IsLoading = true;
        IsTrustSnapshotLoading = true;
        StatusText = "Loading selected security trust snapshot…";

        try
        {
            await _fundContextService.LoadAsync(ct).ConfigureAwait(false);
            var snapshot = await _workstationSecurityMasterApiClient
                .GetTrustSnapshotAsync(securityId, GetCurrentFundProfileId(), ct)
                .ConfigureAwait(false);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (snapshot is null)
                {
                    ClearSelectedSecurityAssuranceState();
                    StatusText = "Selected security snapshot is unavailable.";
                    return;
                }

                ApplyTrustSnapshot(snapshot);
                StatusText = $"Loaded trust snapshot for {snapshot.Security.DisplayName}.";
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Security Master trust snapshot load failed for {securityId}", ex);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StatusText = "Failed to load selected security trust snapshot.";
                _notificationService.ShowNotification("Security Master", "Trust snapshot load failed.", NotificationType.Error);
            });
        }
        finally
        {
            IsLoading = false;
            IsTrustSnapshotLoading = false;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => RefreshSelectedTrustSnapshotCommand.NotifyCanExecuteChanged());
        }
    }

    private void ApplyTrustSnapshot(SecurityMasterTrustSnapshotDto snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var preferredConflictId = SelectedConflict?.ConflictId;
        SyncLegacySelectionStateFromSnapshot(snapshot);

        SelectedTrustSnapshot = snapshot;
        _conflictAssessmentById.Clear();
        foreach (var assessment in snapshot.ConflictAssessments)
        {
            _conflictAssessmentById[assessment.Conflict.ConflictId] = assessment;
        }

        ProvenanceCandidates.Clear();
        foreach (var candidate in snapshot.ProvenanceCandidates
                     .OrderByDescending(candidate => candidate.IsWinningSource)
                     .ThenByDescending(candidate => candidate.AsOf))
        {
            ProvenanceCandidates.Add(candidate);
        }

        DownstreamImpactLinks.Clear();
        foreach (var link in snapshot.DownstreamImpact.Links.OrderBy(GetImpactLinkOrder))
        {
            DownstreamImpactLinks.Add(link);
        }

        TrustScoreText = $"{snapshot.TrustPosture.TrustScore}/100 • {snapshot.TrustPosture.Tone}";
        TrustSummaryText = snapshot.TrustPosture.Summary;
        GoldenCopySourceText = FormatGoldenCopySourceText(snapshot.EconomicDefinition);
        GoldenCopyRuleText = snapshot.TrustPosture.GoldenCopyRule;
        TradingParametersStatusText = snapshot.TrustPosture.TradingParametersStatus;
        ProvenanceSummaryText = BuildProvenanceSummaryText(snapshot);
        DownstreamImpactSummaryText = snapshot.DownstreamImpact.Summary;
        CorporateActionReadinessText = snapshot.TrustPosture.CorporateActionReadiness;
        CorporateActionImpactSummaryText = BuildCorporateActionImpactSummary(snapshot);

        RebuildFilteredConflicts(preferredConflictId);
        RebuildRecommendedActions();
        UpdateSelectedConflictAssessmentState();
        RaiseSelectionDerivedStateChanged();
        RaiseConflictDerivedStateChanged();
    }

    private void RebuildFilteredConflicts()
        => RebuildFilteredConflicts(SelectedConflict?.ConflictId);

    private void RebuildFilteredConflicts(Guid? preferredConflictId)
    {
        IEnumerable<SecurityMasterConflict> source = SelectedTrustSnapshot?.ConflictAssessments.Select(assessment => assessment.Conflict)
            ?? OpenConflicts;

        if (ShowOnlySelectedSecurityConflicts && SelectedSecurity is not null)
        {
            source = source.Where(conflict => conflict.SecurityId == SelectedSecurity.SecurityId);
        }

        var baseConflicts = source.ToArray();
        var filtered = baseConflicts
            .Where(conflict => !ShowHighImpactConflictsOnly ||
                               GetSelectedConflictAssessment(conflict.ConflictId)?.ImpactSeverity == SecurityMasterImpactSeverity.High)
            .Where(conflict => !ShowBulkEligibleConflictsOnly ||
                               GetSelectedConflictAssessment(conflict.ConflictId)?.IsBulkEligible == true)
            .OrderByDescending(conflict => GetImpactSeverityRank(GetSelectedConflictAssessment(conflict.ConflictId)?.ImpactSeverity ?? SecurityMasterImpactSeverity.Unknown))
            .ThenByDescending(conflict => conflict.DetectedAt)
            .ToArray();

        FilteredConflicts.Clear();
        foreach (var conflict in filtered)
        {
            FilteredConflicts.Add(conflict);
        }

        ConflictFilterSummaryText = BuildConflictFilterSummary(baseConflicts.Length, filtered.Length);
        BulkPreviewSummaryText = BuildBulkPreviewSummary();
        CanApplyBulkRecommendedResolutions = GetVisibleConflictAssessments().Any(assessment => assessment.IsBulkEligible);

        var nextSelectedConflict = filtered.FirstOrDefault(conflict => preferredConflictId.HasValue && conflict.ConflictId == preferredConflictId.Value)
            ?? filtered.FirstOrDefault();
        SelectedConflict = nextSelectedConflict;

        if (nextSelectedConflict is null)
        {
            UpdateSelectedConflictAssessmentState();
        }
    }

    private void RebuildRecommendedActions()
    {
        RecommendedActions.Clear();
        if (SelectedTrustSnapshot is null)
        {
            return;
        }

        var selectedConflictAssessment = GetSelectedConflictAssessment();
        if (selectedConflictAssessment is not null)
        {
            RecommendedActions.Add(CreateSelectedConflictAction(selectedConflictAssessment));
        }

        foreach (var action in SelectedTrustSnapshot.RecommendedActions
                     .Where(action => action.Kind != SecurityMasterRecommendedActionKind.ResolveSelectedConflict)
                     .OrderBy(action => GetRecommendedActionOrder(action.Kind)))
        {
            RecommendedActions.Add(action);
        }

        if (selectedConflictAssessment is null)
        {
            var defaultResolveAction = SelectedTrustSnapshot.RecommendedActions
                .FirstOrDefault(action => action.Kind == SecurityMasterRecommendedActionKind.ResolveSelectedConflict);
            if (defaultResolveAction is not null)
            {
                RecommendedActions.Insert(0, defaultResolveAction);
            }
        }

        RaisePropertyChanged(nameof(ActionInspectorLeadText));
        RaisePropertyChanged(nameof(ActionInspectorDetailText));
        RaisePropertyChanged(nameof(ActionInspectorNoActionText));
    }

    private void SyncLegacySelectionStateFromSnapshot(SecurityMasterTrustSnapshotDto snapshot)
    {
        SelectedSecurity = snapshot.Security;
        _selectedEconomicDefinition = null;
        _selectedTradingParameters = null;
        _latestHistoryEvent = snapshot.History
            .OrderByDescending(item => item.EventTimestamp)
            .FirstOrDefault();

        HistoryText = BuildHistoryText(snapshot.History);

        CorporateActions.Clear();
        foreach (var action in snapshot.CorporateActions.OrderByDescending(action => action.ExDate))
        {
            CorporateActions.Add(action);
        }

        _conflictSecurityContextCache[snapshot.Security.SecurityId] = ToSecurityContext(snapshot.Security);
    }

    private void UpdateSelectedConflictAssessmentState()
    {
        var assessment = GetSelectedConflictAssessment();
        if (assessment is null)
        {
            SelectedConflictRecommendedActionText = "Select a conflict to see the recommended action.";
            SelectedConflictWinnerText = "Select a conflict to compare the current winner.";
            _selectedConflictImpactText = "Select a conflict to review downstream impact.";
            CanApplyRecommendedConflictResolution = false;
            RaisePropertyChanged(nameof(SelectedConflictImpactText));
            return;
        }

        SelectedConflictRecommendedActionText = assessment.Recommendation switch
        {
            SecurityMasterConflictRecommendationKind.DismissAsEquivalent =>
                "Dismiss as equivalent because the normalized values match.",
            SecurityMasterConflictRecommendationKind.Challenger =>
                $"Accept {assessment.ChallengerSource} because the current winning value is blank.",
            SecurityMasterConflictRecommendationKind.PreserveWinner =>
                $"Preserve {assessment.CurrentWinningSource} as the current winner.",
            _ =>
                "Manual review required before applying a resolution."
        };
        SelectedConflictWinnerText = assessment.RecommendedWinner;
        _selectedConflictImpactText = $"{assessment.ImpactSummary} {assessment.ImpactDetail}".Trim();
        CanApplyRecommendedConflictResolution = !string.IsNullOrWhiteSpace(assessment.RecommendedResolution);
        RaisePropertyChanged(nameof(SelectedConflictImpactText));
    }

    private SecurityMasterConflictAssessmentDto? GetSelectedConflictAssessment(Guid? conflictId = null)
    {
        var effectiveConflictId = conflictId ?? SelectedConflict?.ConflictId;
        return effectiveConflictId.HasValue && _conflictAssessmentById.TryGetValue(effectiveConflictId.Value, out var assessment)
            ? assessment
            : null;
    }

    private IEnumerable<SecurityMasterConflictAssessmentDto> GetVisibleConflictAssessments()
    {
        if (SelectedTrustSnapshot is null || FilteredConflicts.Count == 0)
        {
            return [];
        }

        var visibleIds = FilteredConflicts
            .Select(conflict => conflict.ConflictId)
            .ToHashSet();
        return SelectedTrustSnapshot.ConflictAssessments
            .Where(assessment => visibleIds.Contains(assessment.Conflict.ConflictId));
    }

    private void ResetConflictFilters()
    {
        var changed = ShowOnlySelectedSecurityConflicts != true ||
                      ShowHighImpactConflictsOnly ||
                      ShowBulkEligibleConflictsOnly;

        ShowOnlySelectedSecurityConflicts = true;
        ShowHighImpactConflictsOnly = false;
        ShowBulkEligibleConflictsOnly = false;

        if (!changed)
        {
            RebuildFilteredConflicts();
        }
    }

    private void PreviewBulkRecommendedResolutions()
    {
        BulkPreviewSummaryText = BuildBulkPreviewSummary();
        CanApplyBulkRecommendedResolutions = GetVisibleConflictAssessments().Any(assessment => assessment.IsBulkEligible);
    }

    private async Task ApplyBulkRecommendedResolutionsAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ConflictOperatorText))
        {
            _notificationService.ShowNotification("Security Master", "Resolved By is required for bulk resolution.", NotificationType.Warning);
            return;
        }

        var eligibleAssessments = GetVisibleConflictAssessments()
            .Where(assessment => assessment.IsBulkEligible)
            .ToArray();
        if (eligibleAssessments.Length == 0)
        {
            PreviewBulkRecommendedResolutions();
            _notificationService.ShowNotification("Security Master", "No low-risk conflicts qualify for bulk assist in the current filter.", NotificationType.Info);
            return;
        }

        try
        {
            var response = await _workstationSecurityMasterApiClient
                .BulkResolveConflictsAsync(
                    new BulkResolveSecurityMasterConflictsRequest(
                        ConflictIds: eligibleAssessments.Select(assessment => assessment.Conflict.ConflictId).ToArray(),
                        ResolvedBy: ConflictOperatorText.Trim(),
                        Reason: string.IsNullOrWhiteSpace(ConflictNoteText) ? null : ConflictNoteText.Trim(),
                        FundProfileId: GetCurrentFundProfileId()),
                    ct)
                .ConfigureAwait(false);

            if (!response.Success || response.Data is null)
            {
                var errorMessage = string.IsNullOrWhiteSpace(response.ErrorMessage)
                    ? "Bulk conflict resolution failed."
                    : $"Bulk conflict resolution failed: {response.ErrorMessage}";
                _notificationService.ShowNotification("Security Master", errorMessage, NotificationType.Error);
                return;
            }

            var result = response.Data;

            var summary = $"{result.Resolved} of {result.Requested} requested conflict(s) resolved.";
            if (result.Skipped > 0)
            {
                var skipDetail = string.Join("; ", result.SkippedReasons.Values.Take(2));
                summary = $"{summary} {result.Skipped} skipped{(string.IsNullOrWhiteSpace(skipDetail) ? "." : $": {skipDetail}.")}";
            }

            await RefreshOperatorWorkflowAsync(ct).ConfigureAwait(false);
            if (SelectedSecurity is not null)
            {
                await LoadSelectedTrustSnapshotAsync(SelectedSecurity.SecurityId, ct).ConfigureAwait(false);
            }

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ConflictNoteText = string.Empty;
                BulkPreviewSummaryText = summary;
                _notificationService.ShowNotification(
                    "Security Master",
                    summary,
                    result.Resolved > 0 && result.Skipped == 0 ? NotificationType.Success : NotificationType.Warning);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to apply bulk Security Master resolutions", ex);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                _notificationService.ShowNotification("Security Master", "Bulk conflict resolution failed.", NotificationType.Error));
        }
    }

    private async Task ApplyRecommendedConflictResolutionAsync(CancellationToken ct = default)
    {
        var assessment = GetSelectedConflictAssessment();
        if (assessment is null)
        {
            var recommendedAction = RecommendedActions
                .FirstOrDefault(action => action.Kind == SecurityMasterRecommendedActionKind.ResolveSelectedConflict && action.ConflictId.HasValue);
            assessment = recommendedAction?.ConflictId is Guid recommendedConflictId
                ? GetSelectedConflictAssessment(recommendedConflictId)
                : SelectedTrustSnapshot?.ConflictAssessments.FirstOrDefault();
        }

        if (assessment is null)
        {
            _notificationService.ShowNotification("Security Master", "No recommended conflict resolution is available.", NotificationType.Info);
            return;
        }

        if (string.IsNullOrWhiteSpace(assessment.RecommendedResolution))
        {
            _notificationService.ShowNotification("Security Master", "This conflict requires manual review.", NotificationType.Warning);
            return;
        }

        SelectedConflict = FilteredConflicts.FirstOrDefault(conflict => conflict.ConflictId == assessment.Conflict.ConflictId)
            ?? assessment.Conflict;
        await ResolveSelectedConflictAsync(assessment.RecommendedResolution, ct).ConfigureAwait(false);
    }

    private void NavigateToFundOperations(string pageTag, FundOperationsTab tab)
    {
        _navigationService.NavigateTo(
            pageTag,
            new FundOperationsNavigationContext(
                Tab: tab,
                FundProfileId: GetCurrentFundProfileId()));
    }

    private bool HasActiveImpactLink(string target)
        => DownstreamImpactLinks.Any(link =>
            link.IsActive &&
            string.Equals(link.Target, target, StringComparison.OrdinalIgnoreCase));

    private string? GetCurrentFundProfileId()
        => string.IsNullOrWhiteSpace(_fundContextService.CurrentFundProfile?.FundProfileId)
            ? _fundContextService.LastSelectedFundProfileId
            : _fundContextService.CurrentFundProfile?.FundProfileId;

    private void CopySelectedIdentifier()
    {
        if (string.IsNullOrWhiteSpace(SelectedIdentifier))
        {
            return;
        }

        try
        {
            Clipboard.SetText(SelectedIdentifier);
            _notificationService.ShowNotification("Security Master", "Selected identifier copied to clipboard.", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to copy selected identifier", ex);
            _notificationService.ShowNotification("Security Master", "Failed to copy the selected identifier.", NotificationType.Error);
        }
    }

    private static string BuildHistoryText(IReadOnlyList<SecurityMasterEventEnvelope> history)
    {
        var ordered = history
            .OrderByDescending(item => item.EventTimestamp)
            .ToArray();
        return ordered.Length == 0
            ? "(no history)"
            : string.Join(Environment.NewLine, ordered.Select(item =>
                $"[{item.EventTimestamp:yyyy-MM-dd HH:mm}] {item.EventType}  v{item.StreamVersion} • {item.Actor}"));
    }

    private static string FormatGoldenCopySourceText(SecurityMasterEconomicDefinitionDrillInDto economicDefinition)
    {
        var source = string.IsNullOrWhiteSpace(economicDefinition.WinningSourceSystem)
            ? "Unknown source"
            : economicDefinition.WinningSourceSystem;
        var recordIdText = string.IsNullOrWhiteSpace(economicDefinition.WinningSourceRecordId)
            ? string.Empty
            : $" • record {economicDefinition.WinningSourceRecordId}";
        var asOfText = economicDefinition.WinningSourceAsOf.HasValue
            ? $" • {economicDefinition.WinningSourceAsOf.Value.LocalDateTime:g}"
            : string.Empty;

        return $"{source}{recordIdText}{asOfText}";
    }

    private static string BuildProvenanceSummaryText(SecurityMasterTrustSnapshotDto snapshot)
    {
        var challengerCount = snapshot.ProvenanceCandidates.Count(candidate => !candidate.IsWinningSource);
        var sourceText = FormatGoldenCopySourceText(snapshot.EconomicDefinition);
        var reasonText = string.IsNullOrWhiteSpace(snapshot.EconomicDefinition.WinningSourceReason)
            ? string.Empty
            : $" • {snapshot.EconomicDefinition.WinningSourceReason}";

        return challengerCount == 0
            ? $"Winning source {sourceText}{reasonText}"
            : $"Winning source {sourceText}{reasonText} • {challengerCount} challenger row(s) remain open";
    }

    private static string BuildCorporateActionImpactSummary(SecurityMasterTrustSnapshotDto snapshot)
    {
        if (snapshot.CorporateActions.Count == 0)
        {
            return "No corporate actions recorded for the selected security.";
        }

        var upcomingCount = snapshot.CorporateActions.Count(action => action.ExDate >= DateOnly.FromDateTime(DateTime.UtcNow));
        return upcomingCount == 0
            ? $"{snapshot.CorporateActions.Count} corporate action(s) are recorded with no upcoming events in the current review window."
            : $"{upcomingCount} upcoming corporate action(s) require review across {snapshot.CorporateActions.Count} recorded event(s).";
    }

    private string BuildConflictFilterSummary(int baseCount, int filteredCount)
    {
        var activeFilters = new List<string>();
        if (ShowOnlySelectedSecurityConflicts)
        {
            activeFilters.Add("selected security");
        }

        if (ShowHighImpactConflictsOnly)
        {
            activeFilters.Add("high impact");
        }

        if (ShowBulkEligibleConflictsOnly)
        {
            activeFilters.Add("bulk eligible");
        }

        var filterText = activeFilters.Count == 0
            ? "all open conflicts"
            : string.Join(", ", activeFilters);

        return filteredCount == 0
            ? $"No conflicts match the current filter set ({filterText})."
            : $"{filteredCount} of {baseCount} conflict(s) shown for {filterText}.";
    }

    private string BuildBulkPreviewSummary()
    {
        if (SelectedTrustSnapshot is null)
        {
            return "Load a selected security to preview low-risk bulk resolutions.";
        }

        var visibleAssessments = GetVisibleConflictAssessments().ToArray();
        if (visibleAssessments.Length == 0)
        {
            return "No conflicts match the current filters.";
        }

        var eligibleCount = visibleAssessments.Count(assessment => assessment.IsBulkEligible);
        if (eligibleCount == 0)
        {
            var firstReason = visibleAssessments
                .Select(assessment => assessment.BulkIneligibilityReason)
                .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
            return string.IsNullOrWhiteSpace(firstReason)
                ? $"0 of {visibleAssessments.Length} filtered conflict(s) qualify for low-risk bulk assist."
                : $"0 of {visibleAssessments.Length} filtered conflict(s) qualify for low-risk bulk assist. {firstReason}";
        }

        return $"{eligibleCount} of {visibleAssessments.Length} filtered conflict(s) qualify for low-risk bulk assist.";
    }

    private static SecurityMasterRecommendedActionDto CreateSelectedConflictAction(SecurityMasterConflictAssessmentDto assessment)
        => new(
            Kind: SecurityMasterRecommendedActionKind.ResolveSelectedConflict,
            Title: $"Resolve {FormatFieldLabel(assessment.Conflict.FieldPath)}",
            Detail: $"{assessment.RecommendedWinner} {assessment.ImpactSummary}",
            IsPrimary: true,
            IsEnabled: !string.IsNullOrWhiteSpace(assessment.RecommendedResolution),
            ConflictId: assessment.Conflict.ConflictId);

    private static int GetImpactSeverityRank(SecurityMasterImpactSeverity severity)
        => severity switch
        {
            SecurityMasterImpactSeverity.High => 3,
            SecurityMasterImpactSeverity.Medium => 2,
            SecurityMasterImpactSeverity.Low => 1,
            SecurityMasterImpactSeverity.None => 0,
            _ => -1
        };

    private static int GetImpactLinkOrder(SecurityMasterImpactLinkDto link)
        => link.Target switch
        {
            "reconciliation" => 0,
            "ledger" => 1,
            "reportPack" => 2,
            "portfolio" => 3,
            _ => 4
        };

    private static int GetRecommendedActionOrder(SecurityMasterRecommendedActionKind kind)
        => kind switch
        {
            SecurityMasterRecommendedActionKind.BulkResolveLowRiskConflicts => 1,
            SecurityMasterRecommendedActionKind.BackfillTradingParameters => 2,
            SecurityMasterRecommendedActionKind.ReviewCorporateActions => 3,
            SecurityMasterRecommendedActionKind.OpenReconciliationImpact => 4,
            SecurityMasterRecommendedActionKind.OpenLedgerImpact => 5,
            SecurityMasterRecommendedActionKind.OpenReportPackImpact => 6,
            SecurityMasterRecommendedActionKind.OpenPortfolioImpact => 7,
            SecurityMasterRecommendedActionKind.EditSelectedSecurity => 8,
            _ => 9
        };

    public void Dispose() => Stop();

    // ── Navigation parameter handling ───────────────────────────────────────
    private object? _parameter;
    public object? Parameter
    {
        get => _parameter;
        set
        {
            if (SetProperty(ref _parameter, value))
            {
                OnNavigationParameterReceived(value);
            }
        }
    }

    private async void OnNavigationParameterReceived(object? parameter)
    {
        try
        {
            if (parameter is string ticker)
            {
                // Pre-fill search with the ticker and execute search
                SearchQuery = ticker;
                await SearchAsync();
            }
            else if (parameter is Guid securityId)
            {
                // Load the specific security detail
                await LoadDetailAsync(securityId);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Navigation parameter handling failed: {parameter}", ex);
        }
    }

    // ── Corporate Actions ───────────────────────────────────────────────────
    private async Task OnLoadCorporateActions(CancellationToken ct = default)
    {
        if (SelectedSecurity?.SecurityId is not { } id)
            return;

        try
        {
            var actions = await (_queryService.GetCorporateActionsAsync(id, ct)
                    ?? Task.FromResult<IReadOnlyList<CorporateActionDto>>([]))
                .ConfigureAwait(false);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CorporateActions.Clear();
                foreach (var action in actions.OrderByDescending(a => a.ExDate))
                    CorporateActions.Add(action);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load corporate actions", ex);
        }
    }

    private void OnShowRecordCorpAction()
    {
        IsRecordCorpActionVisible = true;
        CorpActType = "Dividend";
        CorpActExDate = string.Empty;
        CorpActAmount = 0m;
        CorpActCurrency = "USD";
    }

    private void OnCancelRecordCorpAction()
    {
        IsRecordCorpActionVisible = false;
        CorpActType = "Dividend";
        CorpActExDate = string.Empty;
        CorpActAmount = 0m;
        CorpActCurrency = "USD";
    }

    private async Task OnRecordCorpAction(CancellationToken ct = default)
    {
        if (SelectedSecurity?.SecurityId is not { } securityId)
            return;

        if (string.IsNullOrWhiteSpace(CorpActExDate))
        {
            _notificationService.ShowNotification("Corporate Actions", "Please enter an ex-date.", NotificationType.Warning);
            return;
        }

        if (CorpActAmount <= 0)
        {
            _notificationService.ShowNotification("Corporate Actions", "Please enter a valid amount/ratio.", NotificationType.Warning);
            return;
        }

        try
        {
            // Parse the ex-date
            if (!DateOnly.TryParse(CorpActExDate, out var exDate))
            {
                _notificationService.ShowNotification("Corporate Actions", "Invalid date format. Use yyyy-MM-dd.", NotificationType.Warning);
                return;
            }

            // Build the CorporateActionDto
            var dto = new CorporateActionDto(
                CorpActId: Guid.NewGuid(),
                SecurityId: securityId,
                EventType: CorpActType,
                ExDate: exDate,
                PayDate: null,
                DividendPerShare: CorpActType == "Dividend" ? CorpActAmount : null,
                Currency: CorpActType == "Dividend" ? CorpActCurrency : null,
                SplitRatio: CorpActType == "StockSplit" ? CorpActAmount : null,
                NewSecurityId: null,
                DistributionRatio: null,
                AcquirerSecurityId: null,
                ExchangeRatio: null,
                SubscriptionPricePerShare: null,
                RightsPerShare: null);

            var result = await ApiClientService.Instance
                .PostAsync<CorporateActionDto>($"/api/workstation/security-master/securities/{securityId}/corporate-actions", dto, ct)
                .ConfigureAwait(false);

            if (result is not null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsRecordCorpActionVisible = false;
                    CorpActType = "Dividend";
                    CorpActExDate = string.Empty;
                    CorpActAmount = 0m;
                    CorpActCurrency = "USD";
                    _notificationService.ShowNotification("Corporate Actions", "Corporate action recorded successfully.", NotificationType.Success);
                });

                await LoadSelectedTrustSnapshotAsync(securityId, ct).ConfigureAwait(false);
            }
            else
            {
                _notificationService.ShowNotification("Corporate Actions", "Failed to record corporate action.", NotificationType.Error);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to record corporate action", ex);
            _notificationService.ShowNotification("Corporate Actions", "An error occurred while recording the corporate action.", NotificationType.Error);
        }
    }

    // ── Conflict badge ───────────────────────────────────────────────────────
    private async Task RefreshConflictCountAsync(CancellationToken ct = default)
    {
        await RefreshOperatorWorkflowAsync(ct).ConfigureAwait(false);
    }

    private void StartWorkflowPolling()
    {
        _workflowCts?.Cancel();
        _workflowCts?.Dispose();
        _workflowCts = new CancellationTokenSource();
        var token = _workflowCts.Token;

        _workflowPollingTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                await RefreshOperatorWorkflowAsync(token).ConfigureAwait(false);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    private async Task RefreshOperatorWorkflowAsync(CancellationToken ct = default)
    {
        try
        {
            var statusTask = _workflowClient.GetIngestStatusAsync(ct);
            var conflictsTask = _workflowClient.GetOpenConflictsAsync(ct);
            await Task.WhenAll(statusTask, conflictsTask).ConfigureAwait(false);

            var status = await statusTask.ConfigureAwait(false);
            var conflicts = (await conflictsTask.ConfigureAwait(false))
                .OrderBy(conflict => conflict.DetectedAt)
                .ToArray();
            await PrimeConflictSecurityContextCacheAsync(conflicts, ct).ConfigureAwait(false);

            var previousSelectedConflictId = SelectedConflict?.ConflictId;
            var previousSelectedGroupSecurityId = SelectedConflictGroup?.SecurityId;
            var preferredSecurityId = SelectedSecurity?.SecurityId;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _suppressConflictLaneSelectionSync = true;
                _suppressConflictDrivenSelectionLoad = true;

                try
                {
                    OpenConflictCount = conflicts.Length;
                    OpenConflicts.Clear();
                    foreach (var conflict in conflicts)
                    {
                        OpenConflicts.Add(conflict);
                    }

                    RebuildConflictLaneGroups();

                    var selectedGroup = ConflictGroups.FirstOrDefault(group =>
                            previousSelectedGroupSecurityId.HasValue && group.SecurityId == previousSelectedGroupSecurityId.Value)
                        ?? ConflictGroups.FirstOrDefault(group =>
                            preferredSecurityId.HasValue && group.SecurityId == preferredSecurityId.Value)
                        ?? ConflictGroups.FirstOrDefault();

                    SelectedConflictGroup = selectedGroup;
                    SelectedConflictEntry = selectedGroup?.Conflicts.FirstOrDefault(entry =>
                            previousSelectedConflictId.HasValue && entry.Conflict.ConflictId == previousSelectedConflictId.Value)
                        ?? selectedGroup?.Conflicts.FirstOrDefault();
                    SelectedConflict = SelectedConflictEntry?.Conflict;
                }
                finally
                {
                    _suppressConflictLaneSelectionSync = false;
                    _suppressConflictDrivenSelectionLoad = false;
                }

                if (SelectedSecurity is not null)
                {
                    AlignConflictLaneToSelectedSecurity();
                }

                RebuildFilteredConflicts();
                ApplyIngestStatus(status);
                NotifyConflictWorkflowCommandsChanged();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to refresh Security Master operator workflow status", ex);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                WorkflowStatusText = "Operator workflow polling surface is unavailable.";
                WorkflowRetrievedAtText = "-";
            });
        }
    }

    private void ApplyIngestStatus(SecurityMasterIngestStatusResponse? status)
    {
        if (status is null)
        {
            WorkflowStatusText = "Ingest polling surface unavailable.";
            WorkflowRetrievedAtText = "-";
            ImportSessionSummary = "Security Master ingest status is unavailable from the workstation service.";
            return;
        }

        WorkflowRetrievedAtText = status.RetrievedAtUtc.LocalDateTime.ToString("g");
        if (status.IsImportActive && status.ActiveImport is not null)
        {
            WorkflowStatusText = $"Active ingest {status.ActiveImport.Processed}/{status.ActiveImport.Total} via {status.ActiveImport.FileExtension}.";
            ImportSessionSummary =
                $"Active ingest: {status.ActiveImport.Processed}/{status.ActiveImport.Total} processed • {status.ActiveImport.Imported} imported • {status.ActiveImport.Skipped} skipped • {status.ActiveImport.Failed} failed.";
            return;
        }

        if (status.LastCompleted is not null)
        {
            WorkflowStatusText = $"Last ingest completed {status.LastCompleted.CompletedAtUtc.LocalDateTime:g}.";
            ImportSessionSummary =
                $"Last ingest: {status.LastCompleted.Imported} imported • {status.LastCompleted.Skipped} skipped • {status.LastCompleted.Failed} failed • {status.LastCompleted.ConflictsDetected} conflicts.";
            return;
        }

        WorkflowStatusText = "No ingest activity has been recorded yet.";
        ImportSessionSummary = "No Security Master ingest has completed yet.";
    }

    private async Task PrimeConflictSecurityContextCacheAsync(IReadOnlyList<SecurityMasterConflict> conflicts, CancellationToken ct)
    {
        if (SelectedSecurity is not null)
        {
            _conflictSecurityContextCache[SelectedSecurity.SecurityId] = ToSecurityContext(SelectedSecurity);
        }

        var missingSecurityIds = conflicts
            .Select(conflict => conflict.SecurityId)
            .Distinct()
            .Where(securityId => !_conflictSecurityContextCache.ContainsKey(securityId))
            .ToArray();
        if (missingSecurityIds.Length == 0)
        {
            return;
        }

        var lookupTasks = new List<Task<KeyValuePair<Guid, SecurityConflictSecurityContext>?>>(missingSecurityIds.Length);
        foreach (var securityId in missingSecurityIds)
        {
            lookupTasks.Add(LoadConflictSecurityContextAsync(securityId, ct));
        }

        var lookups = await Task.WhenAll(lookupTasks).ConfigureAwait(false);
        foreach (var lookup in lookups)
        {
            if (lookup.HasValue)
            {
                _conflictSecurityContextCache[lookup.Value.Key] = lookup.Value.Value;
            }
        }
    }

    private async Task<KeyValuePair<Guid, SecurityConflictSecurityContext>?> LoadConflictSecurityContextAsync(
        Guid securityId,
        CancellationToken ct)
    {
        try
        {
            var detailTask = _queryService.GetByIdAsync(securityId, ct);
            if (detailTask is null)
            {
                return null;
            }

            var detail = await detailTask.ConfigureAwait(false);
            return detail is null
                ? null
                : new KeyValuePair<Guid, SecurityConflictSecurityContext>(securityId, ToSecurityContext(detail));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private void RebuildConflictLaneGroups()
    {
        var groups = OpenConflicts
            .GroupBy(conflict => conflict.SecurityId)
            .Select(group =>
            {
                var context = GetConflictSecurityContext(group.Key);
                var entries = group
                    .Select(conflict => BuildConflictLaneEntry(conflict, context))
                    .OrderByDescending(entry => entry.SeverityRank)
                    .ThenBy(entry => entry.Conflict.DetectedAt)
                    .ToArray();
                var highestSeverity = entries.FirstOrDefault();
                var groupSummary = BuildConflictGroupSummary(entries);

                return new SecurityConflictLaneGroup(
                    SecurityId: group.Key,
                    SecurityLabel: context.DisplayName,
                    SecurityIdentifier: context.PrimaryIdentifier,
                    GroupSummary: groupSummary,
                    HighestSeverityLabel: highestSeverity?.SeverityLabel ?? "Review",
                    HighestSeverityTone: highestSeverity?.SeverityTone ?? "Info",
                    SafeAutoResolveCount: entries.Count(entry => entry.IsAutoResolveSafe),
                    Conflicts: entries);
            })
            .OrderByDescending(group => group.Conflicts.FirstOrDefault()?.SeverityRank ?? 0)
            .ThenByDescending(group => group.ConflictCount)
            .ThenBy(group => group.SecurityLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ConflictGroups.Clear();
        foreach (var group in groups)
        {
            ConflictGroups.Add(group);
        }
    }

    private SecurityConflictLaneEntry BuildConflictLaneEntry(
        SecurityMasterConflict conflict,
        SecurityConflictSecurityContext context)
    {
        var assessment = AssessConflict(conflict);

        return new SecurityConflictLaneEntry(
            Conflict: conflict,
            SecurityLabel: context.DisplayName,
            SecurityIdentifier: context.PrimaryIdentifier,
            FieldLabel: FormatFieldLabel(conflict.FieldPath),
            SeverityLabel: assessment.SeverityLabel,
            SeverityTone: assessment.SeverityTone,
            SeverityRank: assessment.SeverityRank,
            ConfidenceLabel: assessment.ConfidenceLabel,
            AutoResolveHint: assessment.AutoResolveHint,
            ImpactSummary: assessment.ImpactSummary,
            ImpactDetail: assessment.ImpactDetail,
            NextStepSummary: assessment.NextStepSummary,
            RoutesToFundReview: assessment.RoutesToFundReview,
            RoutesToReconciliation: assessment.RoutesToReconciliation,
            RoutesToCashFlow: assessment.RoutesToCashFlow,
            RoutesToReportPack: assessment.RoutesToReportPack,
            RequiresTradingBackfill: assessment.RequiresTradingBackfill,
            IsAutoResolveSafe: assessment.IsAutoResolveSafe);
    }

    private SecurityConflictSecurityContext GetConflictSecurityContext(Guid securityId)
    {
        if (_conflictSecurityContextCache.TryGetValue(securityId, out var cachedContext))
        {
            return cachedContext;
        }

        if (SelectedSecurity?.SecurityId == securityId)
        {
            return ToSecurityContext(SelectedSecurity);
        }

        var shortId = securityId.ToString("N")[..8].ToUpperInvariant();
        return new SecurityConflictSecurityContext(
            DisplayName: $"Security {shortId}",
            PrimaryIdentifier: $"SecurityId {shortId}",
            AssetClass: "Unknown");
    }

    private SecurityConflictLaneEntry? GetActiveConflictContextEntry()
    {
        if (SelectedConflictEntry is not null)
        {
            return SelectedConflictEntry;
        }

        if (SelectedSecurity is not null)
        {
            return ConflictGroups
                .FirstOrDefault(group => group.SecurityId == SelectedSecurity.SecurityId)?
                .Conflicts
                .FirstOrDefault();
        }

        return SelectedConflictGroup?.Conflicts.FirstOrDefault();
    }

    private int GetSelectedSecurityConflictCount()
        => SelectedSecurity is null
            ? 0
            : OpenConflicts.Count(conflict => conflict.SecurityId == SelectedSecurity.SecurityId);

    private IReadOnlyList<string> GetMissingTradingParameterFields()
    {
        if (SelectedSecurity is null)
        {
            return [];
        }

        var missingFields = new List<string>();
        if (_selectedTradingParameters?.LotSize is null or <= 0)
        {
            missingFields.Add("lot size");
        }

        if (_selectedTradingParameters?.TickSize is null or <= 0)
        {
            missingFields.Add("tick size");
        }

        if (string.IsNullOrWhiteSpace(_selectedTradingParameters?.TradingHoursUtc))
        {
            missingFields.Add("trading hours");
        }

        if (RequiresContractMultiplier(SelectedAssetClass) &&
            _selectedTradingParameters?.ContractMultiplier is null or <= 0)
        {
            missingFields.Add("contract multiplier");
        }

        return missingFields;
    }

    private static SecurityConflictSecurityContext ToSecurityContext(SecurityMasterWorkstationDto detail)
        => new(
            DisplayName: detail.DisplayName,
            PrimaryIdentifier: string.IsNullOrWhiteSpace(detail.Classification.PrimaryIdentifierValue)
                ? detail.SecurityId.ToString("N")[..8].ToUpperInvariant()
                : $"{detail.Classification.PrimaryIdentifierKind}: {detail.Classification.PrimaryIdentifierValue}",
            AssetClass: detail.Classification.AssetClass);

    private static SecurityConflictSecurityContext ToSecurityContext(SecurityDetailDto detail)
    {
        var primaryIdentifier = detail.Identifiers.FirstOrDefault(identifier => identifier.IsPrimary);
        return new SecurityConflictSecurityContext(
            DisplayName: detail.DisplayName,
            PrimaryIdentifier: primaryIdentifier is null
                ? detail.SecurityId.ToString("N")[..8].ToUpperInvariant()
                : $"{primaryIdentifier.Kind}: {primaryIdentifier.Value}",
            AssetClass: detail.AssetClass);
    }

    private static SecurityConflictAssessment AssessConflict(SecurityMasterConflict conflict)
    {
        var fieldKey = $"{conflict.FieldPath} {conflict.ConflictKind}".ToLowerInvariant();
        var touchesIdentifier = fieldKey.Contains("identifier") || fieldKey.Contains("isin") || fieldKey.Contains("cusip") || fieldKey.Contains("figi") || fieldKey.Contains("ticker") || fieldKey.Contains("ric") || fieldKey.Contains("bbgid");
        var touchesLifecycle = fieldKey.Contains("status") || fieldKey.Contains("effective") || fieldKey.Contains("lifecycle");
        var touchesTradingParameters = fieldKey.Contains("tick") || fieldKey.Contains("lot") || fieldKey.Contains("margin") || fieldKey.Contains("multiplier") || fieldKey.Contains("tradinghour");
        var touchesCorporateAction = fieldKey.Contains("dividend") || fieldKey.Contains("split") || fieldKey.Contains("rights") || fieldKey.Contains("distribution") || fieldKey.Contains("exdate") || fieldKey.Contains("paydate");
        var touchesClassification = fieldKey.Contains("assetclass") || fieldKey.Contains("classification") || fieldKey.Contains("currency") || fieldKey.Contains("subtype") || fieldKey.Contains("issuer") || fieldKey.Contains("type");
        var touchesDisplay = fieldKey.Contains("displayname") || fieldKey.Contains("name") || fieldKey.Contains("alias");

        var normalizedValueA = NormalizeConflictValue(conflict.ValueA);
        var normalizedValueB = NormalizeConflictValue(conflict.ValueB);
        var sameAfterNormalization =
            normalizedValueA.Length > 0 &&
            normalizedValueA == normalizedValueB;
        var oneSideMissing = string.IsNullOrWhiteSpace(conflict.ValueA) ^ string.IsNullOrWhiteSpace(conflict.ValueB);
        var looksLikeMinorTypo = LooksLikeMinorTypo(normalizedValueA, normalizedValueB);

        var routesToFundReview = true;
        var routesToReconciliation = false;
        var routesToCashFlow = false;
        var routesToReportPack = false;
        var requiresTradingBackfill = false;
        var severityRank = 1;
        var severityLabel = "Review";
        var severityTone = "Info";
        var impactDetail = "Review this mismatch before publishing the selected security into downstream governance workflows.";

        if (touchesIdentifier || touchesLifecycle || touchesTradingParameters || fieldKey.Contains("currency"))
        {
            severityRank = 3;
            severityLabel = "Critical";
            severityTone = "Error";
            routesToReconciliation = true;
            routesToReportPack = true;
            routesToCashFlow = touchesCorporateAction;
            requiresTradingBackfill = touchesTradingParameters;
            impactDetail = touchesTradingParameters
                ? "Order validation, fill-price rounding, and execution readiness can drift until the authoritative trading parameters are restored."
                : "Portfolio and ledger joins can resolve to the wrong instrument, leaving reconciliation and report-pack coverage open.";
        }
        else if (touchesCorporateAction || touchesClassification)
        {
            severityRank = 2;
            severityLabel = "Elevated";
            severityTone = "Warning";
            routesToReconciliation = touchesCorporateAction || fieldKey.Contains("currency");
            routesToCashFlow = touchesCorporateAction;
            routesToReportPack = true;
            impactDetail = touchesCorporateAction
                ? "Cash-flow timing, holdings continuity, and report-pack timelines can drift until the corporate action definition is confirmed."
                : "Classification, valuation context, and reporting labels can drift across fund review and report-pack workflows.";
        }
        else if (touchesDisplay)
        {
            routesToReportPack = true;
            impactDetail = "Display-name and alias differences usually do not break joins, but they can leak inconsistent labels into review and reporting workflows.";
        }

        var confidenceLabel = "Manual review required";
        var autoResolveHint = "No safe auto-resolve signal detected. Compare provenance and downstream impact before choosing a provider value.";
        var isAutoResolveSafe = false;
        if (sameAfterNormalization)
        {
            confidenceLabel = "High-confidence normalization match";
            autoResolveHint = "Safe to auto-resolve after confirming provider precedence. The values differ only by casing, whitespace, or punctuation.";
            isAutoResolveSafe = true;
        }
        else if (oneSideMissing)
        {
            confidenceLabel = "Medium-confidence populated value";
            autoResolveHint = "One provider is blank. Keeping the populated side is often safe if provenance confirms that source is authoritative.";
        }
        else if (looksLikeMinorTypo)
        {
            confidenceLabel = "Low-confidence near match";
            autoResolveHint = "Values are close but not equivalent. Treat this as a likely typo and verify against provenance before resolving.";
        }

        var impactSummary = BuildImpactLaneSummary(routesToFundReview, routesToReconciliation, routesToCashFlow, routesToReportPack);
        var nextStepSummary = severityRank >= 3
            ? $"Resolve this before {impactSummary}."
            : $"Review this before {impactSummary}.";

        return new SecurityConflictAssessment(
            SeverityLabel: severityLabel,
            SeverityTone: severityTone,
            SeverityRank: severityRank,
            ConfidenceLabel: confidenceLabel,
            AutoResolveHint: autoResolveHint,
            ImpactSummary: impactSummary,
            ImpactDetail: impactDetail,
            NextStepSummary: nextStepSummary,
            RoutesToFundReview: routesToFundReview,
            RoutesToReconciliation: routesToReconciliation,
            RoutesToCashFlow: routesToCashFlow,
            RoutesToReportPack: routesToReportPack,
            RequiresTradingBackfill: requiresTradingBackfill,
            IsAutoResolveSafe: isAutoResolveSafe);
    }

    private static string BuildConflictGroupSummary(IReadOnlyList<SecurityConflictLaneEntry> entries)
    {
        if (entries.Count == 0)
        {
            return "No open mismatches remain.";
        }

        var routesToFundReview = entries.Any(entry => entry.RoutesToFundReview);
        var routesToReconciliation = entries.Any(entry => entry.RoutesToReconciliation);
        var routesToCashFlow = entries.Any(entry => entry.RoutesToCashFlow);
        var routesToReportPack = entries.Any(entry => entry.RoutesToReportPack);
        var safeHints = entries.Count(entry => entry.IsAutoResolveSafe);
        var routeSummary = BuildImpactLaneSummary(routesToFundReview, routesToReconciliation, routesToCashFlow, routesToReportPack);
        var safeHintText = safeHints > 0
            ? $" • {safeHints} safe normalization hint{(safeHints == 1 ? string.Empty : "s")}"
            : string.Empty;

        return $"{entries.Count} open mismatch{(entries.Count == 1 ? string.Empty : "es")} • {routeSummary}{safeHintText}";
    }

    private static string BuildImpactLaneSummary(
        bool routesToFundReview,
        bool routesToReconciliation,
        bool routesToCashFlow,
        bool routesToReportPack)
    {
        var lanes = new List<string>();
        if (routesToFundReview)
        {
            lanes.Add("fund review");
        }

        if (routesToReconciliation)
        {
            lanes.Add("reconciliation");
        }

        if (routesToCashFlow)
        {
            lanes.Add("cash-flow review");
        }

        if (routesToReportPack)
        {
            lanes.Add("report pack");
        }

        return lanes.Count switch
        {
            0 => "manual review",
            1 => lanes[0],
            2 => $"{lanes[0]} and {lanes[1]}",
            _ => $"{string.Join(", ", lanes.Take(lanes.Count - 1))}, and {lanes[^1]}"
        };
    }

    private static string FormatFieldLabel(string fieldPath)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return "Unknown field";
        }

        var normalized = fieldPath.Trim();
        if (normalized.Contains("Identifiers.Primary", StringComparison.OrdinalIgnoreCase))
        {
            return "Primary identifier";
        }

        if (normalized.Contains("DisplayName", StringComparison.OrdinalIgnoreCase))
        {
            return "Display name";
        }

        if (normalized.Contains("Currency", StringComparison.OrdinalIgnoreCase))
        {
            return "Currency";
        }

        return normalized.Replace(".", " > ", StringComparison.Ordinal);
    }

    private static bool RequiresContractMultiplier(string assetClass)
        => assetClass.Contains("Option", StringComparison.OrdinalIgnoreCase) ||
           assetClass.Contains("Future", StringComparison.OrdinalIgnoreCase) ||
           assetClass.Contains("Derivative", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeConflictValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static bool LooksLikeMinorTypo(string valueA, string valueB)
    {
        if (string.IsNullOrWhiteSpace(valueA) || string.IsNullOrWhiteSpace(valueB))
        {
            return false;
        }

        if (Math.Abs(valueA.Length - valueB.Length) > 1)
        {
            return false;
        }

        if (valueA.Length == valueB.Length)
        {
            var differences = 0;
            for (var index = 0; index < valueA.Length; index++)
            {
                if (valueA[index] != valueB[index] && ++differences > 2)
                {
                    return false;
                }
            }

            return differences > 0;
        }

        var shorter = valueA.Length < valueB.Length ? valueA : valueB;
        var longer = valueA.Length < valueB.Length ? valueB : valueA;
        var shortIndex = 0;
        var longIndex = 0;
        var edits = 0;

        while (shortIndex < shorter.Length && longIndex < longer.Length)
        {
            if (shorter[shortIndex] == longer[longIndex])
            {
                shortIndex++;
                longIndex++;
                continue;
            }

            if (++edits > 1)
            {
                return false;
            }

            longIndex++;
        }

        return true;
    }

    private static string? TryGetJsonString(JsonElement json, string propertyName)
        => json.ValueKind == JsonValueKind.Object &&
           json.TryGetProperty(propertyName, out var property) &&
           property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static DateTimeOffset? TryGetJsonDateTimeOffset(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object ||
            !json.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(property.GetString(), out var value) ? value : null;
    }

    private async Task ResolveSelectedConflictAsync(string resolution, CancellationToken ct = default)
    {
        if (SelectedConflict is null || string.IsNullOrWhiteSpace(ConflictOperatorText))
        {
            return;
        }

        try
        {
            var selectedSecurityId = SelectedSecurity?.SecurityId;
            var updated = await _workflowClient
                .ResolveConflictAsync(
                    SelectedConflict.ConflictId,
                    resolution,
                    ConflictOperatorText.Trim(),
                    string.IsNullOrWhiteSpace(ConflictNoteText) ? null : ConflictNoteText.Trim(),
                    ct)
                .ConfigureAwait(false);

            if (updated is null)
            {
                _notificationService.ShowNotification("Security Master", "Conflict no longer exists.", NotificationType.Warning);
                return;
            }

            ConflictNoteText = string.Empty;
            await RefreshOperatorWorkflowAsync(ct).ConfigureAwait(false);
            if (selectedSecurityId.HasValue && updated.SecurityId == selectedSecurityId.Value)
            {
                await LoadSelectedTrustSnapshotAsync(selectedSecurityId.Value, ct).ConfigureAwait(false);
            }

            _notificationService.ShowNotification(
                "Security Master",
                resolution.Equals("Dismiss", StringComparison.OrdinalIgnoreCase)
                    ? "Conflict dismissed."
                    : "Conflict marked resolved.",
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to resolve Security Master conflict", ex);
            _notificationService.ShowNotification("Security Master", "Conflict resolution failed.", NotificationType.Error);
        }
    }

    // ── Bulk Import ──────────────────────────────────────────────────────────
    private async Task OnImportFromFile(CancellationToken ct = default)
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CSV/JSON Files|*.csv;*.json",
            DefaultExt = ".csv",
            Title = "Import Securities"
        };

        if (openDialog.ShowDialog() != true)
            return;

        try
        {
            IsImporting = true;
            ImportTotal = 0;
            ImportProcessed = 0;
            ImportImported = 0;
            ImportFailed = 0;
            IsImportResultVisible = false;

            var fileContent = await System.IO.File.ReadAllTextAsync(openDialog.FileName, ct).ConfigureAwait(false);
            var fileExtension = System.IO.Path.GetExtension(openDialog.FileName);

            var progress = new Progress<SecurityMasterImportProgress>(p =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    ImportTotal = p.Total;
                    ImportProcessed = p.Processed;
                    ImportImported = p.Imported;
                    ImportFailed = p.Failed;
                    RaisePropertyChanged(nameof(ImportStatus));
                });
            });

            var result = await _importService.ImportAsync(fileContent, fileExtension, progress, ct).ConfigureAwait(false);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ImportTotal = result.Imported + result.Skipped + result.Failed;
                ImportImported = result.Imported;
                ImportFailed = result.Failed;

                var summary = $"Imported {result.Imported} securities, Skipped {result.Skipped}, Failed {result.Failed}.";
                if (result.Errors.Any())
                {
                    summary += $"\r\nErrors:\r\n{string.Join("\r\n", result.Errors.Take(10))}";
                    if (result.Errors.Count > 10)
                        summary += $"\r\n... and {result.Errors.Count - 10} more errors.";
                }

                ImportResultSummary = summary;
                IsImportResultVisible = true;
                RaisePropertyChanged(nameof(ImportStatus));

                _notificationService.ShowNotification(
                    "Security Master Import",
                    $"Import completed: {result.Imported} imported, {result.Failed} failed.",
                    result.Failed == 0 ? NotificationType.Success : NotificationType.Warning);
            });

            // Refresh search results
            _ = SearchAsync();
            _ = RefreshOperatorWorkflowAsync();
        }
        catch (OperationCanceledException)
        {
            _notificationService.ShowNotification("Security Master Import", "Import cancelled.", NotificationType.Info);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Security Master import failed", ex);
            _notificationService.ShowNotification("Security Master Import", $"Import failed: {ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsImporting = false;
        }
    }

    private void OnCloseImportResult()
    {
        IsImportResultVisible = false;
        ImportResultSummary = string.Empty;
    }
}
