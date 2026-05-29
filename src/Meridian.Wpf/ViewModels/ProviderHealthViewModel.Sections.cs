using System.Collections.ObjectModel;
using System.Windows.Media;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

internal sealed class ProviderHealthCollectionsSectionViewModel
{
    public ObservableCollection<ProviderStatusModel> StreamingProviders { get; } = new();
    public ObservableCollection<BackfillProviderModel> BackfillProviders { get; } = new();
    public ObservableCollection<ConnectionEventModel> ConnectionHistory { get; } = new();
    public ObservableCollection<ProviderManagementRowModel> ProviderManagementRows { get; } = new();
    public ObservableCollection<ProviderManagementSummaryCardModel> ProviderManagementSummaryCards { get; } = new();
    public ObservableCollection<WorkstationMetricModel> ProviderMetricTiles { get; } = new();
    public ObservableCollection<ActionEntry> Actions { get; } = new();
}

internal sealed class ProviderHealthPostureSectionViewModel
{
    public string ConnectedCount { get; set; } = "0";
    public string DisconnectedCount { get; set; } = "0";
    public string TotalProviders { get; set; } = "0";
    public string AvgLatency { get; set; } = "--";
    public string LastUpdateText { get; set; } = "Last updated: --";
    public bool IsLastUpdateStale { get; set; }
    public bool HasNoHistory { get; set; } = true;
    public string ProviderPostureTitle { get; set; } = "Provider posture loading";
    public string ProviderPostureDetail { get; set; } = "Refresh provider health to compute the active streaming and backfill posture.";
    public string ProviderPostureActionText { get; set; } = "Refresh provider data";
    public string ProviderPostureTargetText { get; set; } = "Provider health";
    public string ProviderPostureEvidenceText { get; set; } = "Awaiting provider snapshot";
    public string ProviderPostureIcon { get; set; } = "\uE946";
    public Brush ProviderPostureAccentBrush { get; set; }
    public Brush ProviderPostureBackgroundBrush { get; set; }
    public WorkstationStateModel ProviderPostureState { get; set; } = WorkstationStateModel.Loading(
        "Provider posture loading",
        "Refresh provider health to compute the active streaming and backfill posture.");
    public WorkstationBadgeModel ProviderPostureBadge { get; set; } = new("Posture", "Loading", "\uE946", WorkspaceTone.Info);

    public ProviderHealthPostureSectionViewModel(Brush neutralPostureBrush, Brush neutralPostureBackgroundBrush)
    {
        ProviderPostureAccentBrush = neutralPostureBrush;
        ProviderPostureBackgroundBrush = neutralPostureBackgroundBrush;
    }
}

internal sealed class ProviderHealthManagementSectionViewModel
{
    public ProviderManagementRowModel? SelectedProviderManagementRow { get; set; }
    public string ProviderManagementStatusText { get; set; } = "Provider management evidence is loading.";
    public string ProviderVerificationStatusText { get; set; } = "Credential verification can be run from Diagnostics.";
    public WorkstationCommandGroupModel ProviderManagementCommandGroup { get; set; } = new();
    public WorkstationTableModel<ProviderManagementRowModel> ProviderManagementTable { get; set; }
    public InspectorPanelModel SelectedProviderInspector { get; set; }
    public DiagnosticsChecklistModel SelectedProviderDiagnostics { get; set; }
    public RoutingMatrixModel SelectedProviderRoutingMatrix { get; set; }

    public ProviderHealthManagementSectionViewModel(
        WorkstationTableModel<ProviderManagementRowModel> providerManagementTable,
        InspectorPanelModel selectedProviderInspector,
        DiagnosticsChecklistModel selectedProviderDiagnostics,
        RoutingMatrixModel selectedProviderRoutingMatrix)
    {
        ProviderManagementTable = providerManagementTable;
        SelectedProviderInspector = selectedProviderInspector;
        SelectedProviderDiagnostics = selectedProviderDiagnostics;
        SelectedProviderRoutingMatrix = selectedProviderRoutingMatrix;
    }
}
