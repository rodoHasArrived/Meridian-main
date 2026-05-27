using System.Collections.ObjectModel;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

internal sealed class SecurityMasterSearchSectionViewModel
{
    public ObservableCollection<SecurityMasterWorkstationDto> Results { get; } = new();
    public ObservableCollection<SecurityMasterWorkstationDto> FilteredResults { get; } = new();
    public ObservableCollection<string> AssetClassFilterOptions { get; } = new();
    public ObservableCollection<string> ProviderFilterOptions { get; } = new();
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
    public ObservableCollection<CorporateActionDto> CorporateActions { get; } = new();
    public ObservableCollection<SecurityMasterPresentationField> CompanyProfileFields { get; } = new();
    public ObservableCollection<SecurityMasterPresentationField> CompanyCoverageFields { get; } = new();
    public ObservableCollection<SecurityMasterPrintSectionItem> PrintSections { get; } = new();
    public ObservableCollection<SecurityMasterChecklistItem> PrintChecklistItems { get; } = new();
    public ObservableCollection<SecurityMasterEvidenceItem> PrintEvidenceItems { get; } = new();
}
