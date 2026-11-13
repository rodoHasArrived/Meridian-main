using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using ISmQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ISmService = Meridian.Contracts.SecurityMaster.ISecurityMasterService;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Security Master workstation page.
/// Wraps <see cref="SecurityMasterWorkstationDto"/>-backed search and detail
/// surfaced by the <c>/api/workstation/security-master</c> endpoints.
/// </summary>
public sealed partial class SecurityMasterViewModel : BindableBase, IDisposable
{
    private const string AllAssetClassesFilterLabel = "All asset classes";

    private const string AllProvidersFilterLabel = "All providers";

    private readonly WpfServices.LoggingService _loggingService;

    private readonly WpfServices.NotificationService _notificationService;

    private readonly ITradingParametersBackfillService _backfillService;

    private readonly ISecurityMasterImportService _importService;

    private readonly ISecurityMasterRuntimeStatus _securityMasterRuntimeStatus;

    private readonly WpfServices.ISecurityMasterOperatorWorkflowClient _workflowClient;

    private readonly WpfServices.IWorkstationSecurityMasterApiClient _workstationSecurityMasterApiClient;

    private readonly WpfServices.IWorkstationContractualCashFlowApiClient? _cashFlowClient;

    private readonly WpfServices.FundContextService _fundContextService;

    private readonly WpfServices.NavigationService _navigationService;

    private readonly ISmQueryService _queryService;

    private readonly ISmService _service;

    private readonly WpfServices.DesktopAuthenticationSession? _authenticationSession;

    private readonly WpfServices.IDesktopMutationAuthorization _mutationAuthorization;

    private readonly Meridian.Application.SecurityMaster.IGovernedTermAmendmentPolicy? _governedTermAmendmentPolicy;

    private readonly bool _hasPolygonApiKey;

    private readonly object _selectedSecurityLoadGate = new();

    private bool _isRefreshingSearchWorkspaceFilters;

    private bool _suppressSelectedSecurityLoadCancellation;

    private bool _disposed;

    private CancellationTokenSource? _cts;

    private CancellationTokenSource? _workflowCts;

    private CancellationTokenSource? _selectedSecurityLoadCts;

    private Task? _workflowPollingTask;

    private int _selectedSecurityLoadRevision;

    private Guid? _selectedSecurityLoadTargetId;

    private readonly SecurityMasterSearchSectionViewModel _searchSection = new(AllAssetClassesFilterLabel, AllProvidersFilterLabel);

    private readonly SecurityMasterConflictSectionViewModel _conflictSection = new();

    private readonly SecurityMasterScheduleAndOpenLotSectionViewModel _scheduleSection = new();

    private readonly SecurityMasterPrintSectionViewModel _printSection = new();

    private readonly SecurityMasterWorkflowSectionViewModel _workflowSection = new();

    // ── Public collections ──────────────────────────────────────────────────
    public ObservableCollection<SecurityMasterWorkstationDto> Results => _searchSection.Results;

    public ObservableCollection<SecurityMasterWorkstationDto> FilteredResults => _searchSection.FilteredResults;

    public ObservableCollection<string> AssetClassFilterOptions => _searchSection.AssetClassFilterOptions;

    public ObservableCollection<string> ProviderFilterOptions => _searchSection.ProviderFilterOptions;

    public ObservableCollection<CorporateActionDto> CorporateActions => _printSection.CorporateActions;

    public ObservableCollection<SecurityMasterConflict> OpenConflicts => _conflictSection.OpenConflicts;

    public ObservableCollection<SecurityConflictLaneGroup> ConflictGroups => _conflictSection.ConflictGroups;

    public ObservableCollection<SecurityMasterSourceCandidateDto> ProvenanceCandidates => _conflictSection.ProvenanceCandidates;

    public ObservableCollection<SecurityMasterConflict> FilteredConflicts => _conflictSection.FilteredConflicts;

    public ObservableCollection<SecurityMasterRecommendedActionDto> RecommendedActions => _conflictSection.RecommendedActions;

    public ObservableCollection<SecurityMasterImpactLinkDto> DownstreamImpactLinks => _conflictSection.DownstreamImpactLinks;

    public ObservableCollection<SecurityMasterPresentationField> CompanyProfileFields => _printSection.CompanyProfileFields;

    public ObservableCollection<SecurityMasterPresentationField> CompanyCoverageFields => _printSection.CompanyCoverageFields;

    public ObservableCollection<SecurityMasterPresentationField> InstrumentPassportFields => _printSection.InstrumentPassportFields;

    public ObservableCollection<SecurityValidationIssueDto> ValidationIssues => _scheduleSection.ValidationIssues;

    public ObservableCollection<SecurityMasterChangeHistoryItemDto> ChangeHistoryItems => _scheduleSection.ChangeHistoryItems;

    public ObservableCollection<SecurityMasterPresentationField> ScheduleBookFields => _scheduleSection.ScheduleBookFields;

    public ObservableCollection<SecurityMasterScheduleEventDto> ScheduleBookEvents => _scheduleSection.ScheduleBookEvents;

    public ObservableCollection<SecurityMasterFactorPointDto> ScheduleBookFactorHistory => _scheduleSection.ScheduleBookFactorHistory;

    public ObservableCollection<SecurityMasterScheduleProvenanceDto> ScheduleBookProvenanceHistory => _scheduleSection.ScheduleBookProvenanceHistory;

    public ObservableCollection<SecurityMasterPresentationField> OpenLotReadModelFields => _scheduleSection.OpenLotReadModelFields;

    public ObservableCollection<SecurityMasterOpenLotDto> OpenLotRows => _scheduleSection.OpenLotRows;

    public ObservableCollection<SecurityMasterOpenLotProvenanceDto> OpenLotProvenanceHistory => _scheduleSection.OpenLotProvenanceHistory;

    public ObservableCollection<SecurityMasterPrintSectionItem> PrintSections => _printSection.PrintSections;

    public ObservableCollection<SecurityMasterChecklistItem> PrintChecklistItems => _printSection.PrintChecklistItems;

    public ObservableCollection<SecurityMasterEvidenceItem> PrintEvidenceItems => _printSection.PrintEvidenceItems;

    /// <summary>
    /// Static list of corporate action types available for recording.
    /// </summary>
    public IReadOnlyList<string> CorpActTypes => new[] { "Dividend", "StockSplit" };

    // ── Bindable properties ─────────────────────────────────────────────────
    public string SearchQuery
    {
        get => _searchSection.SearchQuery;
        set
        {
            if (SetSectionProperty(_searchSection.SearchQuery, value, next => _searchSection.SearchQuery = next))
            {
                RaiseSearchDerivedStateChanged();
            }
        }
    }

    public bool ActiveOnly
    {
        get => _searchSection.ActiveOnly;
        set
        {
            if (SetSectionProperty(_searchSection.ActiveOnly, value, next => _searchSection.ActiveOnly = next))
            {
                RaiseSearchDerivedStateChanged();
            }
        }
    }

    public string SelectedAssetClassFilter
    {
        get => _searchSection.SelectedAssetClassFilter;
        set
        {
            if (SetSectionProperty(_searchSection.SelectedAssetClassFilter, value, next => _searchSection.SelectedAssetClassFilter = next) && !_isRefreshingSearchWorkspaceFilters)
            {
                ApplySearchWorkspaceFilters();
            }
        }
    }

    public string SelectedProviderFilter
    {
        get => _searchSection.SelectedProviderFilter;
        set
        {
            if (SetSectionProperty(_searchSection.SelectedProviderFilter, value, next => _searchSection.SelectedProviderFilter = next) && !_isRefreshingSearchWorkspaceFilters)
            {
                ApplySearchWorkspaceFilters();
            }
        }
    }

    public bool ShowMappingGapsOnly
    {
        get => _searchSection.ShowMappingGapsOnly;
        set
        {
            if (SetSectionProperty(_searchSection.ShowMappingGapsOnly, value, next => _searchSection.ShowMappingGapsOnly = next) && !_isRefreshingSearchWorkspaceFilters)
            {
                ApplySearchWorkspaceFilters();
            }
        }
    }

    public bool IsLoading
    {
        get => _searchSection.IsLoading;
        private set
        {
            if (SetSectionProperty(_searchSection.IsLoading, value, next => _searchSection.IsLoading = next))
            {
                RaiseSearchDerivedStateChanged();
            }
        }
    }

    public string StatusText
    {
        get => _searchSection.StatusText;
        private set => SetSectionProperty(_searchSection.StatusText, value, next => _searchSection.StatusText = next);
    }

    private SecurityMasterWorkstationDto? _selectedSecurity;

    public SecurityMasterWorkstationDto? SelectedSecurity
    {
        get => _selectedSecurity;
        set
        {
            if (_disposed)
            {
                return;
            }

            var previousSecurityId = _selectedSecurity?.SecurityId;
            if (SetProperty(ref _selectedSecurity, value))
            {
                if (value is null || previousSecurityId != value.SecurityId)
                {
                    if (!_suppressSelectedSecurityLoadCancellation && CancelSelectedSecurityLoad())
                    {
                        IsLoading = false;
                        IsTrustSnapshotLoading = false;
                        RefreshSelectedTrustSnapshotCommand?.NotifyCanExecuteChanged();
                    }

                    ClearSelectedSecurityAssuranceState();
                    CashDividendAttestation?.SetSecurity(value?.SecurityId);
                }

                if (value is not null)
                {
                    _conflictSecurityContextCache[value.SecurityId] = SecurityMasterPresentation.ToSecurityContext(value);
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
        internal set => SetProperty(ref _editVm, value);
    }

    private SecurityMasterDeactivateViewModel? _deactivateVm;

    public SecurityMasterDeactivateViewModel? DeactivateVm
    {
        get => _deactivateVm;
        internal set => SetProperty(ref _deactivateVm, value);
    }

    private int _selectedDetailTab;

    public int SelectedDetailTab
    {
        get => _selectedDetailTab;
        set => SetProperty(ref _selectedDetailTab, value);
    }

    public bool IsRecordCorpActionVisible
    {
        get => _printSection.IsRecordCorpActionVisible;
        set => SetSectionProperty(_printSection.IsRecordCorpActionVisible, value, next => _printSection.IsRecordCorpActionVisible = next);
    }

    public string CorpActType
    {
        get => _printSection.CorpActType;
        set => SetSectionProperty(_printSection.CorpActType, value, next => _printSection.CorpActType = next);
    }

    public string CorpActExDate
    {
        get => _printSection.CorpActExDate;
        set => SetSectionProperty(_printSection.CorpActExDate, value, next => _printSection.CorpActExDate = next);
    }

    public decimal CorpActAmount
    {
        get => _printSection.CorpActAmount;
        set => SetSectionProperty(_printSection.CorpActAmount, value, next => _printSection.CorpActAmount = next);
    }

    public string CorpActCurrency
    {
        get => _printSection.CorpActCurrency;
        set => SetSectionProperty(_printSection.CorpActCurrency, value, next => _printSection.CorpActCurrency = next);
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
                BackfillTradingParamsCommand?.NotifyCanExecuteChanged();
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
    public int ImportTotal
    {
        get => _workflowSection.ImportTotal;
        private set
        {
            if (SetSectionProperty(_workflowSection.ImportTotal, value, next => _workflowSection.ImportTotal = next))
            {
                RaisePropertyChanged(nameof(ImportStatus));
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    public int ImportProcessed
    {
        get => _workflowSection.ImportProcessed;
        private set
        {
            if (SetSectionProperty(_workflowSection.ImportProcessed, value, next => _workflowSection.ImportProcessed = next))
            {
                RaisePropertyChanged(nameof(ImportStatus));
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    public int ImportImported
    {
        get => _workflowSection.ImportImported;
        private set
        {
            if (SetSectionProperty(_workflowSection.ImportImported, value, next => _workflowSection.ImportImported = next))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    public int ImportFailed
    {
        get => _workflowSection.ImportFailed;
        private set
        {
            if (SetSectionProperty(_workflowSection.ImportFailed, value, next => _workflowSection.ImportFailed = next))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    public bool IsImporting
    {
        get => _workflowSection.IsImporting;
        private set
        {
            if (SetSectionProperty(_workflowSection.IsImporting, value, next => _workflowSection.IsImporting = next))
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

    public bool IsImportResultVisible
    {
        get => _workflowSection.IsImportResultVisible;
        private set
        {
            if (SetSectionProperty(_workflowSection.IsImportResultVisible, value, next => _workflowSection.IsImportResultVisible = next))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    public string ImportResultSummary
    {
        get => _workflowSection.ImportResultSummary;
        private set
        {
            if (SetSectionProperty(_workflowSection.ImportResultSummary, value, next => _workflowSection.ImportResultSummary = next))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    public string ImportSessionSummary
    {
        get => _workflowSection.ImportSessionSummary;
        private set
        {
            if (SetSectionProperty(_workflowSection.ImportSessionSummary, value, next => _workflowSection.ImportSessionSummary = next))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    public string WorkflowStatusText
    {
        get => _workflowSection.WorkflowStatusText;
        private set
        {
            if (SetSectionProperty(_workflowSection.WorkflowStatusText, value, next => _workflowSection.WorkflowStatusText = next))
            {
                RaisePropertyChanged(nameof(RuntimeStatusDetail));
            }
        }
    }

    public string WorkflowRetrievedAtText
    {
        get => _workflowSection.WorkflowRetrievedAtText;
        private set => SetSectionProperty(_workflowSection.WorkflowRetrievedAtText, value, next => _workflowSection.WorkflowRetrievedAtText = next);
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

    private InstrumentPassportDto? _selectedInstrumentPassport;

    public InstrumentPassportDto? SelectedInstrumentPassport
    {
        get => _selectedInstrumentPassport;
        private set
        {
            if (SetProperty(ref _selectedInstrumentPassport, value))
            {
                RaisePropertyChanged(nameof(InstrumentPassportSummaryText));
                RaisePropertyChanged(nameof(InstrumentPassportProviderConfidenceText));
            }
        }
    }

    public string InstrumentPassportSummaryText => SelectedInstrumentPassport is null
        ? "Instrument passport evidence is unavailable."
        : $"{SelectedInstrumentPassport.Identity.DisplayName} - {SelectedInstrumentPassport.TrustPosture.Tone} - {SelectedInstrumentPassport.ProviderConfidence.Count} provider confidence row(s)";

    public string InstrumentPassportProviderConfidenceText => SelectedInstrumentPassport is null
        ? "No provider confidence loaded."
        : $"{SelectedInstrumentPassport.ProviderConfidence.Count(item => item.IsActive)} active / {SelectedInstrumentPassport.ProviderConfidence.Count} total provider mapping(s).";

    private string _instrumentPassportErrorText = string.Empty;

    public string InstrumentPassportErrorText
    {
        get => _instrumentPassportErrorText;
        private set
        {
            if (SetProperty(ref _instrumentPassportErrorText, value))
            {
                RaisePropertyChanged(nameof(HasInstrumentPassportError));
            }
        }
    }

    public bool HasInstrumentPassportError => !string.IsNullOrWhiteSpace(InstrumentPassportErrorText);

    private SecurityMasterTrustSnapshotDto? _selectedTrustSnapshot;

    public SecurityMasterTrustSnapshotDto? SelectedTrustSnapshot
    {
        get => _selectedTrustSnapshot;
        private set
        {
            if (SetProperty(ref _selectedTrustSnapshot, value))
            {
                RaiseScheduleAndOpenLotStateChanged();
                OpenPassportEditorCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isTrustSnapshotLoading;

    public bool IsTrustSnapshotLoading
    {
        get => _isTrustSnapshotLoading;
        private set
        {
            if (SetProperty(ref _isTrustSnapshotLoading, value))
            {
                RaiseScheduleAndOpenLotStateChanged();
                OpenPassportEditorCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Governed passport editor hosted contextually for the selected security. A fresh instance is built
    /// for each launch (hydrated from the loaded trust snapshot's securityId + optimistic-concurrency
    /// version); the instance is reused only to keep an in-progress draft for the same security alive, so a
    /// different security, a refreshed version, or an in-flight write from a prior security can never bleed in.
    /// </summary>
    private SecurityPassportEditorViewModel _passportEditor;

    /// <summary>Issuer and maturity ladders over the bounded keyset routes (web-UI parity).</summary>
    public SecurityMaster.BondReferenceLadderViewModel ReferenceLadders { get; }
    public SecurityMaster.CashDividendAttestationViewModel CashDividendAttestation { get; }

    public SecurityPassportEditorViewModel PassportEditor
    {
        get => _passportEditor;
        private set => SetProperty(ref _passportEditor, value);
    }

    private bool _isPassportEditorOpen;

    public bool IsPassportEditorOpen
    {
        get => _isPassportEditorOpen;
        private set => SetProperty(ref _isPassportEditorOpen, value);
    }

    private string _trustSnapshotErrorText = string.Empty;

    public string TrustSnapshotErrorText
    {
        get => _trustSnapshotErrorText;
        private set
        {
            if (SetProperty(ref _trustSnapshotErrorText, value))
            {
                RaisePropertyChanged(nameof(HasTrustSnapshotError));
                RaiseScheduleAndOpenLotStateChanged();
            }
        }
    }

    public bool HasTrustSnapshotError => !string.IsNullOrWhiteSpace(TrustSnapshotErrorText);

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

    public int LoadedResultCount => Results.Count;

    public int ResultCount => FilteredResults.Count;

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);

    public bool HasSearchResults => FilteredResults.Count > 0;

    public bool HasLoadedResults => Results.Count > 0;

    public bool HasActiveSearchWorkspaceFilters =>
        !string.Equals(SelectedAssetClassFilter, AllAssetClassesFilterLabel, StringComparison.Ordinal) ||
        !string.Equals(SelectedProviderFilter, AllProvidersFilterLabel, StringComparison.Ordinal) ||
        ShowMappingGapsOnly;

    public bool IsSearchRecoveryVisible => !IsLoading && _searchSection.HasSearchAttempted && ResultCount == 0;

    public string SearchRecoveryTitle
    {
        get
        {
            if (!_securityMasterRuntimeStatus.IsAvailable)
            {
                return "Security Master unavailable";
            }

            if (HasLoadedResults && HasActiveSearchWorkspaceFilters)
            {
                return "No results match the current filters";
            }

            return HasSearchQuery
                ? $"No match for \"{SearchQuery.Trim()}\""
                : "Enter a security query";
        }
    }

    public string SearchRecoveryDetail
    {
        get
        {
            if (!_securityMasterRuntimeStatus.IsAvailable)
            {
                return _securityMasterRuntimeStatus.AvailabilityDescription;
            }

            if (HasLoadedResults && HasActiveSearchWorkspaceFilters)
            {
                return "Reset the asset-class or provider filters, or turn off mapping-gap mode to bring loaded results back into scope.";
            }

            return ActiveOnly
                ? "Try all-status search, check the identifier, or import the security universe."
                : "Check the identifier, broaden the symbol or name, or import the security universe.";
        }
    }

    public string SearchScopeText => string.IsNullOrWhiteSpace(SearchQuery)
        ? ActiveOnly
            ? "Active-only scope ready. Enter a symbol, name, or identifier."
            : "All-status scope ready. Enter a symbol, name, or identifier."
        : $"{(ActiveOnly ? "Active-only" : "All-status")} scope - query \"{SearchQuery.Trim()}\"";

    public string SearchResultCountLabel => ResultCount switch
    {
        0 => _searchSection.HasSearchAttempted
            ? HasLoadedResults && HasActiveSearchWorkspaceFilters
                ? $"0 of {LoadedResultCount} matches shown"
                : "No matches loaded"
            : "Ready to search",
        1 => HasActiveSearchWorkspaceFilters && LoadedResultCount != 1
            ? "1 filtered match shown"
            : "1 match loaded",
        _ => HasActiveSearchWorkspaceFilters
            ? $"{ResultCount} of {LoadedResultCount} matches shown"
            : $"{ResultCount} matches loaded"
    };

    public string SearchMetaLabel
    {
        get
        {
            if (!_searchSection.HasSearchAttempted)
            {
                return "Search issuers, identifiers, venues, and provider-linked aliases from one desktop command deck.";
            }

            if (HasLoadedResults && HasActiveSearchWorkspaceFilters)
            {
                return ResultCount > 0
                    ? "Local desktop filters are narrowing the loaded result set without re-querying the backend."
                    : "Loaded results are currently hidden by the active asset-class, provider, or mapping-gap filters.";
            }

            if (HasSearchResults)
            {
                return ActiveOnly
                    ? "Active-only results keep workstation-ready definitions in focus."
                    : "All-status results expose active and inactive definitions for review.";
            }

            return ActiveOnly
                ? "No active matches yet. Broaden to all statuses or import the universe."
                : "No matches yet. Refine the issuer, identifier, or provider alias.";
        }
    }

    public string SearchStatusChipLabel => HasActiveSearchWorkspaceFilters
        ? "Filtered view"
        : ActiveOnly ? "Active only" : "All statuses";

    public string SearchFilterSummaryText
    {
        get
        {
            if (!HasLoadedResults)
            {
                return "Load search results to refine by asset class, provider mapping, or mapping health.";
            }

            if (!HasActiveSearchWorkspaceFilters)
            {
                return $"Showing all {LoadedResultCount} loaded result{(LoadedResultCount == 1 ? string.Empty : "s")}.";
            }

            var parts = new List<string>();
            if (!string.Equals(SelectedAssetClassFilter, AllAssetClassesFilterLabel, StringComparison.Ordinal))
            {
                parts.Add($"asset class: {SelectedAssetClassFilter}");
            }

            if (!string.Equals(SelectedProviderFilter, AllProvidersFilterLabel, StringComparison.Ordinal))
            {
                parts.Add($"provider: {SelectedProviderFilter}");
            }

            if (ShowMappingGapsOnly)
            {
                parts.Add("mapping gaps only");
            }

            return $"Showing {ResultCount} of {LoadedResultCount} loaded results - {string.Join(" - ", parts)}";
        }
    }

    public string MappingHealthSummaryText
    {
        get
        {
            if (!HasLoadedResults)
            {
                return "Provider mapping health appears after a search.";
            }

            var mappedCount = Results.Count(SecurityMasterPresentation.HasProviderMapping);
            var missingCount = LoadedResultCount - mappedCount;
            return missingCount == 0
                ? $"{mappedCount} result{(mappedCount == 1 ? string.Empty : "s")} mapped across provider aliases."
                : $"{mappedCount} mapped - {missingCount} with provider or identifier coverage gaps.";
        }
    }

    public string SearchDeckLeadText => SelectedSecurity is null
        ? "Search, validate, and publish Meridian-ready security profiles without leaving the Data workspace."
        : $"{SelectedSecurity.DisplayName} is loaded into the desktop Security Master deck.";

    public string SearchDeckDetailText => SelectedSecurity is null
        ? "Company context, corporate actions, and print packet evidence stay on one desktop workstation surface."
        : $"{SelectedTrustPostureText} {SelectedTradingParameterCoverageText}".Trim();

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

    public string CompanyCardTitle => SelectedTrustSnapshot?.Identity.IssuerName
        ?? SelectedSecurity?.DisplayName
        ?? "Company context unavailable";

    public string CompanyCardSubtitle
    {
        get
        {
            if (SelectedSecurity is null && SelectedTrustSnapshot is null)
            {
                return "Select a security to review issuer context.";
            }

            var economic = SelectedTrustSnapshot?.EconomicDefinition;
            var identity = SelectedTrustSnapshot?.Identity;
            var assetFamily = SecurityMasterTextHelpers.FirstNonEmpty(economic?.AssetFamily, economic?.AssetClass, SelectedSecurity?.Classification.AssetClass, "Security");
            var issuerType = SecurityMasterTextHelpers.FirstNonEmpty(economic?.IssuerType, SelectedSecurity?.Classification.IssuerType, "Issuer");
            var country = SecurityMasterTextHelpers.FirstNonEmpty(identity?.CountryOfRisk, economic?.RiskCountry, SelectedSecurity?.Classification.RiskCountry, "Country unavailable");
            return $"{assetFamily} - {issuerType} - {country}";
        }
    }

    public string CompanyCardDescription
    {
        get
        {
            if (SelectedSecurity is null && SelectedTrustSnapshot is null)
            {
                return "Issuer profile, listing context, and downstream coverage appear after selection.";
            }

            var identity = SelectedTrustSnapshot?.Identity;
            var listing = string.IsNullOrWhiteSpace(identity?.PrimaryListingMic)
                ? "primary listing not supplied"
                : $"primary listing {identity.PrimaryListingMic}";
            var settlement = identity?.SettlementCycleDays is int days
                ? $"settlement cycle T+{days}"
                : "settlement cycle unavailable";
            var scope = SelectedTrustSnapshot?.DownstreamImpact.IsScoped == true
                ? $"scoped to {SelectedTrustSnapshot.DownstreamImpact.FundProfileId}"
                : "operator-wide review";
            return $"{listing}, {settlement}, and {scope} stay visible beside the command deck.";
        }
    }

    public string PrintPacketIdText => SelectedTrustSnapshot is null
        ? "Packet unavailable"
        : $"SM-{SelectedTrustSnapshot.Security.SecurityId.ToString()[..8].ToUpperInvariant()}-V{SelectedTrustSnapshot.EconomicDefinition.Version}";

    public string PrintGeneratedAtText => FormatHelpers.FormatUtcTimestamp(SelectedTrustSnapshot?.RetrievedAtUtc, "Not generated");

    public string PrintDistributionText => SelectedTrustSnapshot?.DownstreamImpact.IsScoped == true
        ? $"Deliver to {SelectedTrustSnapshot.DownstreamImpact.FundProfileId} packet queue"
        : "Deliver to operator packet queue";

    public string PrintSummaryText
    {
        get
        {
            if (SelectedTrustSnapshot is null)
            {
                return "Packet summary appears after a security snapshot is loaded.";
            }

            var readiness = SelectedTrustSnapshot.TrustPosture.Tone switch
            {
                SecurityMasterTrustTone.Trusted => "Packet is ready for downstream reference use.",
                SecurityMasterTrustTone.Review => "Packet needs operator review before release.",
                SecurityMasterTrustTone.Blocked => "Packet is blocked until open conflicts are resolved.",
                _ => "Packet readiness is still being determined."
            };

            return $"{readiness} {PrintDistributionText}.";
        }
    }

    public bool HasScheduleBookEvents => ScheduleBookEvents.Count > 0;

    public bool HasOpenLotRows => OpenLotRows.Count > 0;

    public bool HasChangeHistoryItems => ChangeHistoryItems.Count > 0;

    public string ValidationIssuesStatusText
    {
        get
        {
            if (IsTrustSnapshotLoading)
            {
                return "Loading validation coverage from the workstation trust snapshot.";
            }

            if (HasTrustSnapshotError)
            {
                return TrustSnapshotErrorText;
            }

            if (SelectedSecurity is null && SelectedTrustSnapshot is null)
            {
                return "Select a security to inspect validation blockers, advisory issues, and suggested fixes.";
            }

            var report = SelectedTrustSnapshot?.ValidationReport;
            if (report is null)
            {
                return "Validation report unavailable for the selected security.";
            }

            if (!ValidationIssues.Any(static issue => issue.Severity != SecurityValidationSeverityDto.Info))
            {
                return "Validation report loaded with no blocking or advisory issues.";
            }

            var blockingCount = report.CriticalIssueCount + report.ErrorIssueCount;
            var advisoryCount = Math.Max(0, ValidationIssues.Count(static issue => issue.Severity != SecurityValidationSeverityDto.Info) - blockingCount);
            return $"{blockingCount} blocking issue{(blockingCount == 1 ? string.Empty : "s")} and {advisoryCount} advisory issue{(advisoryCount == 1 ? string.Empty : "s")} loaded.";
        }
    }

    public string ChangeHistoryStatusText
    {
        get
        {
            if (IsTrustSnapshotLoading)
            {
                return "Loading structured audit history from the workstation trust snapshot.";
            }

            if (HasTrustSnapshotError)
            {
                return TrustSnapshotErrorText;
            }

            if (SelectedSecurity is null && SelectedTrustSnapshot is null)
            {
                return "Select a security to inspect versioned change history, actors, and source provenance.";
            }

            return HasChangeHistoryItems
                ? $"{ChangeHistoryItems.Count} structured change entr{(ChangeHistoryItems.Count == 1 ? "y" : "ies")} loaded."
                : "Structured change history is unavailable for the selected security.";
        }
    }

    public string ScheduleBookStatusText
    {
        get
        {
            if (IsTrustSnapshotLoading)
            {
                return "Loading schedule book from the workstation trust snapshot.";
            }

            if (HasTrustSnapshotError)
            {
                return TrustSnapshotErrorText;
            }

            if (SelectedSecurity is null && SelectedTrustSnapshot is null)
            {
                return "Select a security to inspect cash-flow schedules, factor history, and source provenance.";
            }

            if (SelectedTrustSnapshot?.ScheduleBook is null)
            {
                return "Schedule book payload is unavailable for the selected security.";
            }

            return HasScheduleBookEvents
                ? $"{ScheduleBookEvents.Count} schedule event{(ScheduleBookEvents.Count == 1 ? string.Empty : "s")} loaded with {ScheduleBookFactorHistory.Count} factor point{(ScheduleBookFactorHistory.Count == 1 ? string.Empty : "s")} and {ScheduleBookProvenanceHistory.Count} provenance row{(ScheduleBookProvenanceHistory.Count == 1 ? string.Empty : "s")}."
                : "Schedule book loaded with no cash-flow events.";
        }
    }

    public string OpenLotReadModelStatusText
    {
        get
        {
            if (IsTrustSnapshotLoading)
            {
                return "Loading open lot read model from the workstation trust snapshot.";
            }

            if (HasTrustSnapshotError)
            {
                return TrustSnapshotErrorText;
            }

            if (SelectedSecurity is null && SelectedTrustSnapshot is null)
            {
                return "Select a security to inspect open lots, factor-adjusted exposure, and ledger provenance.";
            }

            if (SelectedTrustSnapshot?.OpenLotReadModel is null)
            {
                return "Open lot read-model payload is unavailable for the selected security.";
            }

            return HasOpenLotRows
                ? $"{OpenLotRows.Count} open lot{(OpenLotRows.Count == 1 ? string.Empty : "s")} loaded with {OpenLotProvenanceHistory.Count} provenance row{(OpenLotProvenanceHistory.Count == 1 ? string.Empty : "s")}."
                : "Open lot read model loaded with no open lots.";
        }
    }

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
        : $"{SelectedSecurity.DisplayName} - {SelectedAssetClass} - {SelectedCurrency}";

    public string SelectionLifecycleText => SelectedSecurity is null
        ? "No security selected."
        : $"Status {SelectedStatusBadge} - version {SelectedSecurity.EconomicDefinition?.Version ?? 0}";

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
            var sourceSystem = SecurityMasterPresentation.TryGetJsonString(provenance, "sourceSystem") ?? "Unknown source";
            var updatedBy = SecurityMasterPresentation.TryGetJsonString(provenance, "updatedBy") ?? "Unknown actor";
            var sourceRecordId = SecurityMasterPresentation.TryGetJsonString(provenance, "sourceRecordId");
            var reason = SecurityMasterPresentation.TryGetJsonString(provenance, "reason");
            var asOf = SecurityMasterPresentation.TryGetJsonDateTimeOffset(provenance, "asOf");

            var sourceRecordText = string.IsNullOrWhiteSpace(sourceRecordId)
                ? string.Empty
                : $" - record {sourceRecordId}";
            var reasonText = string.IsNullOrWhiteSpace(reason)
                ? string.Empty
                : $" - {reason}";
            var asOfText = asOf.HasValue
                ? $" - {FormatHelpers.FormatUtcTimestamp(asOf.Value)}"
                : string.Empty;

            return $"Source {sourceSystem}{sourceRecordText} - updated by {updatedBy}{asOfText}{reasonText}";
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
                    : $"Trading parameters complete as of {FormatHelpers.FormatUtcTimestamp(_selectedTradingParameters.AsOf)}.";
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
                return $"{_latestHistoryEvent.EventType} - v{_latestHistoryEvent.StreamVersion} - {FormatHelpers.FormatUtcTimestamp(_latestHistoryEvent.EventTimestamp)} by {_latestHistoryEvent.Actor}";
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
        : $"Detected {FormatHelpers.FormatUtcTimestamp(SelectedConflictEntry.Conflict.DetectedAt)} - {SelectedConflictEntry.Conflict.ConflictKind}";

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
    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand ClearSearchCommand { get; }
    public IRelayCommand ResetSearchFiltersCommand { get; }
    public IAsyncRelayCommand RefreshConflictCountCommand { get; }
    public IAsyncRelayCommand RefreshWorkflowCommand { get; }
    public IAsyncRelayCommand RefreshSelectedTrustSnapshotCommand { get; }
    public IRelayCommand OpenPassportEditorCommand { get; }
    public IRelayCommand ClosePassportEditorCommand { get; }
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
    /// <summary>Number of open identifier conflicts detected in Security Master. Drives the badge.</summary>
    public int OpenConflictCount
    {
        get => _workflowSection.OpenConflictCount;
        private set
        {
            if (SetSectionProperty(_workflowSection.OpenConflictCount, value, next => _workflowSection.OpenConflictCount = next))
            {
                RaisePropertyChanged(nameof(HasOpenConflicts));
                RaiseConflictDerivedStateChanged();
            }
        }
    }

    /// <summary>True when at least one open conflict exists. Drives badge visibility.</summary>
    public bool HasOpenConflicts => OpenConflictCount > 0;

    public bool CanResolveSelectedConflict =>
        SelectedConflict is not null &&
        !string.IsNullOrWhiteSpace(ConflictOperatorText);

    private bool SetSectionProperty<T>(
        T currentValue,
        T newValue,
        Action<T> assign,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, newValue))
        {
            return false;
        }

        assign(newValue);
        RaisePropertyChanged(propertyName);
        return true;
    }

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
        ISmService service,
        WpfServices.DesktopAuthenticationSession? authenticationSession = null,
        WpfServices.IWorkstationContractualCashFlowApiClient? cashFlowClient = null,
        Meridian.Application.SecurityMaster.IGovernedTermAmendmentPolicy? governedTermAmendmentPolicy = null)
    {
        _loggingService = loggingService;
        _cashFlowClient = cashFlowClient;
        _governedTermAmendmentPolicy = governedTermAmendmentPolicy;
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
        _authenticationSession = authenticationSession;
        _mutationAuthorization = new WpfServices.DesktopMutationAuthorization(authenticationSession);
        if (authenticationSession?.CurrentActor is { Length: > 0 } sessionActor)
        {
            // Pre-fills the operator text box only. Governed writes resolve their actor through
            // DesktopAuthenticationSession.TryGetAuthenticatedActor, which validates the session.
            _conflictOperatorText = sessionActor;
        }
        _hasPolygonApiKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POLYGON_API_KEY"));

        _passportEditor = new SecurityPassportEditorViewModel(_workstationSecurityMasterApiClient, _cashFlowClient);
        ReferenceLadders = new SecurityMaster.BondReferenceLadderViewModel(_workstationSecurityMasterApiClient);
        CashDividendAttestation = new SecurityMaster.CashDividendAttestationViewModel(_workstationSecurityMasterApiClient);

        CreateNewCommand = new RelayCommand(OnCreateNew, () => CanModifySecurityMaster);
        EditSelectedCommand = new RelayCommand(OnEditSelected, () => HasSelectedSecurity && CanModifySecurityMaster);
        DeactivateSelectedCommand = new RelayCommand(OnDeactivateSelected, () => HasSelectedSecurity && IsSelectedSecurityActive() && CanModifySecurityMaster);
        LoadCorporateActionsCommand = new AsyncRelayCommand(OnLoadCorporateActions, () => HasSelectedSecurity);
        ShowRecordCorpActionCommand = new RelayCommand(OnShowRecordCorpAction, () => HasSelectedSecurity);
        CancelRecordCorpActionCommand = new RelayCommand(OnCancelRecordCorpAction);
        RecordCorpActionCommand = new AsyncRelayCommand(OnRecordCorpAction);
        BackfillTradingParamsCommand = new AsyncRelayCommand(
            OnBackfillTradingParams,
            () => !IsBackfillingTradingParams && CanTriggerSecurityMasterBackfill());
        ImportFromFileCommand = new AsyncRelayCommand(
            OnImportFromFile,
            () => !IsImporting && CanModifySecurityMaster);
        CloseImportResultCommand = new RelayCommand(OnCloseImportResult);
        SearchCommand = new AsyncRelayCommand(ct => SearchAsync(ct), CanSearch);
        ClearSearchCommand = new RelayCommand(OnClearSearch, CanClearSearch);
        ResetSearchFiltersCommand = new RelayCommand(ResetSearchWorkspaceFilters, () => HasActiveSearchWorkspaceFilters);
        RefreshConflictCountCommand = new AsyncRelayCommand(RefreshConflictCountAsync);
        RefreshWorkflowCommand = new AsyncRelayCommand(RefreshOperatorWorkflowAsync);
        RefreshSelectedTrustSnapshotCommand = new AsyncRelayCommand(
            ct => HasSelectedSecurity && SelectedSecurity is not null
                ? LoadSelectedTrustSnapshotAsync(SelectedSecurity.SecurityId, ct)
                : Task.CompletedTask,
            () => HasSelectedSecurity && !IsTrustSnapshotLoading);
        OpenPassportEditorCommand = new RelayCommand(OnOpenPassportEditor, () => CanOpenPassportEditor);
        ClosePassportEditorCommand = new RelayCommand(() => IsPassportEditorOpen = false);
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

        Results.CollectionChanged += (_, _) => RefreshSearchWorkspaceState();
        CorporateActions.CollectionChanged += (_, _) => RaiseSelectionDerivedStateChanged();
        OpenConflicts.CollectionChanged += (_, _) => RaiseConflictDerivedStateChanged();
        ConflictGroups.CollectionChanged += (_, _) => RaiseConflictDerivedStateChanged();
        ProvenanceCandidates.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(ProvenanceSummaryText));
        FilteredConflicts.CollectionChanged += (_, _) => RaiseConflictDerivedStateChanged();
        RecommendedActions.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(RecommendedActions));
        DownstreamImpactLinks.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(DownstreamImpactLinks));
        ValidationIssues.CollectionChanged += (_, _) => RaiseScheduleAndOpenLotStateChanged();
        ChangeHistoryItems.CollectionChanged += (_, _) => RaiseScheduleAndOpenLotStateChanged();
        ScheduleBookEvents.CollectionChanged += (_, _) => RaiseScheduleAndOpenLotStateChanged();
        ScheduleBookFactorHistory.CollectionChanged += (_, _) => RaiseScheduleAndOpenLotStateChanged();
        ScheduleBookProvenanceHistory.CollectionChanged += (_, _) => RaiseScheduleAndOpenLotStateChanged();
        OpenLotRows.CollectionChanged += (_, _) => RaiseScheduleAndOpenLotStateChanged();
        OpenLotProvenanceHistory.CollectionChanged += (_, _) => RaiseScheduleAndOpenLotStateChanged();

        if (_authenticationSession is not null)
        {
            // A weak subscription keeps the singleton session from rooting this transient
            // view model: the page's Unloaded calls only Stop() and nothing disposes
            // resolved instances, so a strong handler would accumulate every unloaded
            // instance in the invocation lists — while a journal-restored page must still
            // observe sign-outs AND sign-ins (the shell reuses the frame journal across a
            // logout, so this instance can be restored for a newly authorized operator), so
            // the handlers cannot simply be removed on unload. Dispose still removes them
            // deterministically.
            WeakEventManager<WpfServices.DesktopAuthenticationSession, EventArgs>.AddHandler(
                _authenticationSession,
                nameof(WpfServices.DesktopAuthenticationSession.SignedOut),
                OnAuthenticationSessionAuthenticationChanged);
            WeakEventManager<WpfServices.DesktopAuthenticationSession, EventArgs>.AddHandler(
                _authenticationSession,
                nameof(WpfServices.DesktopAuthenticationSession.SignedIn),
                OnAuthenticationSessionAuthenticationChanged);
        }

        StartWorkflowPolling();
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values)
        {
            collection.Add(value);
        }
    }

    public void Dispose()
    {
        lock (_selectedSecurityLoadGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (_authenticationSession is not null)
        {
            WeakEventManager<WpfServices.DesktopAuthenticationSession, EventArgs>.RemoveHandler(
                _authenticationSession,
                nameof(WpfServices.DesktopAuthenticationSession.SignedOut),
                OnAuthenticationSessionAuthenticationChanged);
            WeakEventManager<WpfServices.DesktopAuthenticationSession, EventArgs>.RemoveHandler(
                _authenticationSession,
                nameof(WpfServices.DesktopAuthenticationSession.SignedIn),
                OnAuthenticationSessionAuthenticationChanged);
        }

        CashDividendAttestation.Dispose();
        Stop();
    }

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
        catch (OperationCanceledException)
        {
            throw;
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
        catch (OperationCanceledException)
        {
            // Cancellation is expected when the view is torn down mid-operation; benign.
            _loggingService.LogDebug(
                "Security Master operation cancelled.",
                ("view", GetType().Name));
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load corporate actions", ex);
        }
    }
}
