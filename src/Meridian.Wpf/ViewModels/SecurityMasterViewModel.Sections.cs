using System.Collections.ObjectModel;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

internal sealed class SecurityMasterSearchSectionViewModel
{
    public SecurityMasterSearchSectionViewModel(string allAssetClassesLabel, string allProvidersLabel)
    {
        SelectedAssetClassFilter = allAssetClassesLabel;
        SelectedProviderFilter = allProvidersLabel;
    }

    public ObservableCollection<SecurityMasterWorkstationDto> Results { get; } = new();
    public ObservableCollection<SecurityMasterWorkstationDto> FilteredResults { get; } = new();
    public ObservableCollection<string> AssetClassFilterOptions { get; } = new();
    public ObservableCollection<string> ProviderFilterOptions { get; } = new();
    public string SearchQuery { get; set; } = string.Empty;
    public bool ActiveOnly { get; set; } = true;
    public string SelectedAssetClassFilter { get; set; }
    public string SelectedProviderFilter { get; set; }
    public bool ShowMappingGapsOnly { get; set; }
    public bool IsLoading { get; set; }
    public string StatusText { get; set; } = "Enter a query and press Search.";
    public bool HasSearchAttempted { get; set; }
}

internal sealed class SecurityMasterConflictSectionViewModel
{
    public ObservableCollection<SecurityMasterConflict> OpenConflicts { get; } = new();
    public ObservableCollection<SecurityConflictLaneGroup> ConflictGroups { get; } = new();
    public ObservableCollection<SecurityMasterSourceCandidateDto> ProvenanceCandidates { get; } = new();
    public ObservableCollection<SecurityMasterConflict> FilteredConflicts { get; } = new();
    public ObservableCollection<SecurityMasterRecommendedActionDto> RecommendedActions { get; } = new();
    public ObservableCollection<SecurityMasterImpactLinkDto> DownstreamImpactLinks { get; } = new();
}

internal sealed class SecurityMasterScheduleAndOpenLotSectionViewModel
{
    public ObservableCollection<SecurityValidationIssueDto> ValidationIssues { get; } = new();
    public ObservableCollection<SecurityMasterChangeHistoryItemDto> ChangeHistoryItems { get; } = new();
    public ObservableCollection<SecurityMasterPresentationField> ScheduleBookFields { get; } = new();
    public ObservableCollection<SecurityMasterScheduleEventDto> ScheduleBookEvents { get; } = new();
    public ObservableCollection<SecurityMasterFactorPointDto> ScheduleBookFactorHistory { get; } = new();
    public ObservableCollection<SecurityMasterScheduleProvenanceDto> ScheduleBookProvenanceHistory { get; } = new();
    public ObservableCollection<SecurityMasterPresentationField> OpenLotReadModelFields { get; } = new();
    public ObservableCollection<SecurityMasterOpenLotDto> OpenLotRows { get; } = new();
    public ObservableCollection<SecurityMasterOpenLotProvenanceDto> OpenLotProvenanceHistory { get; } = new();
}

internal sealed class SecurityMasterPrintSectionViewModel
{
    public bool IsRecordCorpActionVisible { get; set; }
    public string CorpActType { get; set; } = "Dividend";
    public string CorpActExDate { get; set; } = string.Empty;
    public decimal CorpActAmount { get; set; }
    public string CorpActCurrency { get; set; } = "USD";
    public ObservableCollection<CorporateActionDto> CorporateActions { get; } = new();
    public ObservableCollection<SecurityMasterPresentationField> CompanyProfileFields { get; } = new();
    public ObservableCollection<SecurityMasterPresentationField> CompanyCoverageFields { get; } = new();
    public ObservableCollection<SecurityMasterPresentationField> InstrumentPassportFields { get; } = new();
    public ObservableCollection<SecurityMasterPrintSectionItem> PrintSections { get; } = new();
    public ObservableCollection<SecurityMasterChecklistItem> PrintChecklistItems { get; } = new();
    public ObservableCollection<SecurityMasterEvidenceItem> PrintEvidenceItems { get; } = new();
}

internal sealed class SecurityMasterWorkflowSectionViewModel
{
    public int ImportTotal { get; set; }
    public int ImportProcessed { get; set; }
    public int ImportImported { get; set; }
    public int ImportFailed { get; set; }
    public bool IsImporting { get; set; }
    public bool IsImportResultVisible { get; set; }
    public string ImportResultSummary { get; set; } = string.Empty;
    public string ImportSessionSummary { get; set; } = "No import activity recorded by the workstation service.";
    public string WorkflowStatusText { get; set; } = "Polling Security Master ingest and conflict posture.";
    public string WorkflowRetrievedAtText { get; set; } = "-";
    public int OpenConflictCount { get; set; }
}
