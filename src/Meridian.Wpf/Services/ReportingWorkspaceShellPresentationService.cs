using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

public static class ReportingWorkspaceShellPresentationService
{
    public static IReadOnlyList<WorkspaceQueueItem> BuildFallbackDecisionItems() =>
    [
        new WorkspaceQueueItem
        {
            Title = "Pack assembly",
            Detail = "Review no-code report grids, branded pack output, shadow-NAV evidence, and downstream handoff readiness.",
            StatusLabel = "Report writer",
            CountLabel = "Draft",
            Tone = WorkspaceTone.Info,
            PrimaryActionId = "FundReportPack",
            PrimaryActionLabel = "Open",
            SecondaryActionId = "ExportPresets",
            SecondaryActionLabel = "Presets",
            AutomationName = "Reporting pack assembly decision"
        },
        new WorkspaceQueueItem
        {
            Title = "Approval gates",
            Detail = "Review scheduled runs, approval blockers, retry manifests, and immutable audit lineage.",
            StatusLabel = "Schedule controls",
            CountLabel = "Gate",
            Tone = WorkspaceTone.Warning,
            PrimaryActionId = "ReportRunStatus",
            PrimaryActionLabel = "Open",
            SecondaryActionId = "FundAuditTrail",
            SecondaryActionLabel = "Audit",
            AutomationName = "Reporting approval gates decision"
        },
        new WorkspaceQueueItem
        {
            Title = "Dashboard exceptions",
            Detail = "Inspect live exposure, cash, P&L, liquidity, Top-N, contribution, and data-quality signals.",
            StatusLabel = "Portfolio views",
            CountLabel = "Review",
            Tone = WorkspaceTone.Neutral,
            PrimaryActionId = "Dashboard",
            PrimaryActionLabel = "Open",
            SecondaryActionId = "DataQuality",
            SecondaryActionLabel = "Quality",
            AutomationName = "Reporting dashboard exceptions decision"
        },
        new WorkspaceQueueItem
        {
            Title = "Formula grid validation",
            Detail = $"Check {nameof(ReportWriterGridDefinitionDto.Formulas)}, {nameof(ReportWriterGridDefinitionDto.Metrics)}, {nameof(ReportWriterGridDefinitionDto.RowFields)}, and custom-formula lineage before publishing no-code report writer output.",
            StatusLabel = "Custom formulas",
            CountLabel = "Validate",
            Tone = WorkspaceTone.Info,
            PrimaryActionId = "FundReportPack",
            PrimaryActionLabel = "Open Pack",
            SecondaryActionId = "DataQuality",
            SecondaryActionLabel = "Quality",
            AutomationName = "Reporting formula grid validation decision"
        },
        new WorkspaceQueueItem
        {
            Title = "Cross-fund consolidation",
            Detail = $"Review {nameof(FundReportingSummaryDto.CrossFundConsolidations)}, {nameof(CrossFundReportingConsolidationDto.FundCount)}, {nameof(CrossFundReportingConsolidationDto.EntityCount)}, {nameof(CrossFundReportingConsolidationDto.ShadowNav)}, and drill-through routes before allocator or company-wide delivery.",
            StatusLabel = "Consolidation",
            CountLabel = "Roll-up",
            Tone = WorkspaceTone.Warning,
            PrimaryActionId = "Dashboard",
            PrimaryActionLabel = "Open Dashboard",
            SecondaryActionId = "AnalysisExport",
            SecondaryActionLabel = "Exports",
            AutomationName = "Reporting cross-fund consolidation decision"
        },
        new WorkspaceQueueItem
        {
            Title = "Export delivery",
            Detail = "Prepare scheduled PDF/XLSX/CSV packages for email-link, secure-portal, regulatory, and warehouse delivery.",
            StatusLabel = "Distribution",
            CountLabel = "Ready",
            Tone = WorkspaceTone.Success,
            PrimaryActionId = "AnalysisExport",
            PrimaryActionLabel = "Open",
            SecondaryActionId = "ExportPresets",
            SecondaryActionLabel = "Presets",
            AutomationName = "Reporting export delivery decision"
        }
    ];

    public static IReadOnlyList<WorkspaceQueueItem> BuildDecisionItems(FundReportingSummaryDto? reporting)
    {
        if (reporting is null)
        {
            return
            [
                new WorkspaceQueueItem
                {
                    Title = "Reporting context",
                    Detail = "Select a fund-linked context to load report-writer grids, schedules, portfolio cuts, exports, branding, and audit lineage.",
                    StatusLabel = "Locked",
                    CountLabel = "No fund",
                    Tone = WorkspaceTone.Warning,
                    IsBlocked = true,
                    PrimaryActionId = "FundAccounts",
                    PrimaryActionLabel = "Select Fund",
                    SecondaryActionId = "Diagnostics",
                    SecondaryActionLabel = "Diagnostics",
                    AutomationName = "Reporting workspace locked decision"
                }
            ];
        }

        var schedulePlans = reporting.ScheduleDeliveryPlans ?? [];
        var readyPlans = schedulePlans.Count(static plan => plan.IsReady);
        var lastGeneratedGrids = schedulePlans.Sum(static plan => plan.LastDeliveryGeneratedReportWriterGridCount);
        var lastRenderedGrids = schedulePlans.Sum(static plan => plan.LastDeliveryRenderedReportWriterGridCount);
        var writerSourceCount = reporting.ReportWriterDatasetSources?.Count ?? 0;
        var writerRowCount = reporting.ReportWriterDatasetSources?.Sum(static source => source.RowCount) ?? 0;
        var liveViewCount = reporting.LivePortfolioViews?.Count ?? 0;
        var portfolioCutCount = reporting.PortfolioCuts?.Count ?? 0;
        var analyticsCount = reporting.AnalyticsRows?.Count ?? 0;
        var pnlSliceCount = reporting.PnlSlices?.Count ?? 0;
        var consolidationCount = reporting.CrossFundConsolidations?.Count ?? 0;
        var readyConsolidations = reporting.CrossFundConsolidations?.Count(static row => row.IsReady) ?? 0;
        var structuredExportCount = reporting.StructuredExports?.Count ?? 0;
        var workflowCount = reporting.WorkflowRecords?.Count ?? 0;
        var themeCount = reporting.BrandingThemes?.Count ?? 0;
        var accessScope = reporting.AccessAudit?.Summary ?? "workspace access policy";

        return
        [
            new WorkspaceQueueItem
            {
                Title = "No-code report writer",
                Detail = $"{writerSourceCount} governed dataset source(s) with {writerRowCount:N0} retained row(s) feed drag-and-drop detail, pivot, Top-N, contribution, and custom-formula grids.",
                StatusLabel = writerSourceCount > 0 ? "Source-backed" : "Needs data",
                CountLabel = lastGeneratedGrids > 0 || lastRenderedGrids > 0
                    ? $"{lastGeneratedGrids} gen / {lastRenderedGrids} rendered"
                    : $"{writerSourceCount} sources",
                Tone = writerSourceCount > 0 ? WorkspaceTone.Info : WorkspaceTone.Warning,
                IsBlocked = writerSourceCount == 0,
                PrimaryActionId = "FundReportPack",
                PrimaryActionLabel = "Open Writer",
                SecondaryActionId = "DataQuality",
                SecondaryActionLabel = "Quality",
                AutomationName = "Reporting no-code report writer decision"
            },
            new WorkspaceQueueItem
            {
                Title = "Scheduled distribution",
                Detail = $"{schedulePlans.Count} schedule delivery plan(s), {readyPlans} ready, carry PDF/XLS/CSV package routing with email-link and secure-portal evidence.",
                StatusLabel = readyPlans == schedulePlans.Count && schedulePlans.Count > 0 ? "Ready" : "Review",
                CountLabel = schedulePlans.Count > 0 ? $"{readyPlans}/{schedulePlans.Count} ready" : "No plans",
                Tone = schedulePlans.Count == 0 ? WorkspaceTone.Warning : readyPlans == schedulePlans.Count ? WorkspaceTone.Success : WorkspaceTone.Warning,
                IsBlocked = schedulePlans.Count == 0 || readyPlans < schedulePlans.Count,
                PrimaryActionId = "ReportRunStatus",
                PrimaryActionLabel = "Run Status",
                SecondaryActionId = "FundAuditTrail",
                SecondaryActionLabel = "Audit",
                AutomationName = "Reporting scheduled distribution decision"
            },
            new WorkspaceQueueItem
            {
                Title = "Portfolio reporting views",
                Detail = $"{liveViewCount} live view(s), {portfolioCutCount} exposure/cash/shadow-NAV cut(s), {pnlSliceCount} P&L slice(s), and {analyticsCount} Top-N/contribution analytic row(s) are available from the shared reporting summary.",
                StatusLabel = liveViewCount > 0 ? "Live views" : "Snapshot",
                CountLabel = $"{portfolioCutCount} cuts",
                Tone = portfolioCutCount > 0 ? WorkspaceTone.Info : WorkspaceTone.Warning,
                IsBlocked = portfolioCutCount == 0,
                PrimaryActionId = "Dashboard",
                PrimaryActionLabel = "Dashboard",
                SecondaryActionId = "FundPortfolio",
                SecondaryActionLabel = "Portfolio",
                AutomationName = "Reporting portfolio views decision"
            },
            new WorkspaceQueueItem
            {
                Title = "Cross-fund consolidation",
                Detail = $"{readyConsolidations} of {consolidationCount} consolidation row(s) are source-backed with fund, entity, exposure, cash, P&L, and shadow-NAV rollups.",
                StatusLabel = consolidationCount > 0 ? "Roll-up" : "No roll-up",
                CountLabel = consolidationCount > 0 ? $"{readyConsolidations}/{consolidationCount} ready" : "No rows",
                Tone = consolidationCount == 0 ? WorkspaceTone.Warning : readyConsolidations == consolidationCount ? WorkspaceTone.Success : WorkspaceTone.Warning,
                IsBlocked = consolidationCount == 0 || readyConsolidations < consolidationCount,
                PrimaryActionId = "Dashboard",
                PrimaryActionLabel = "Open Dashboard",
                SecondaryActionId = "AnalysisExport",
                SecondaryActionLabel = "Exports",
                AutomationName = "Reporting cross-fund consolidation decision"
            },
            new WorkspaceQueueItem
            {
                Title = "Structured exports",
                Detail = $"{structuredExportCount} regulatory, data-warehouse, and investment-decision export preset(s) retain schemas, data dictionaries, validation checks, and lineage rows.",
                StatusLabel = structuredExportCount > 0 ? "Exportable" : "Needs preset",
                CountLabel = $"{structuredExportCount} presets",
                Tone = structuredExportCount > 0 ? WorkspaceTone.Success : WorkspaceTone.Warning,
                IsBlocked = structuredExportCount == 0,
                PrimaryActionId = "AnalysisExport",
                PrimaryActionLabel = "Exports",
                SecondaryActionId = "ExportPresets",
                SecondaryActionLabel = "Presets",
                AutomationName = "Reporting structured exports decision"
            },
            new WorkspaceQueueItem
            {
                Title = "Access, branding, and audit",
                Detail = $"{workflowCount} workflow record(s), {themeCount} branding theme(s), immutable delivery records, and {accessScope} keep user/group/company access visible before publication.",
                StatusLabel = workflowCount > 0 ? "Auditable" : "No records",
                CountLabel = $"{themeCount} themes",
                Tone = workflowCount > 0 ? WorkspaceTone.Info : WorkspaceTone.Warning,
                IsBlocked = workflowCount == 0,
                PrimaryActionId = "FundReportPack",
                PrimaryActionLabel = "Report Pack",
                SecondaryActionId = "FundAuditTrail",
                SecondaryActionLabel = "Audit",
                AutomationName = "Reporting access branding audit decision"
            }
        ];
    }

    public static WorkspaceCommandGroup BuildCommandGroup(bool hasFund) => hasFund
        ? new WorkspaceCommandGroup
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem { Id = "FundReportPack", Label = "Report Pack", Description = "Open governed report-pack preview.", Glyph = "\uE8A5" },
                new WorkspaceCommandItem { Id = "ReportRunStatus", Label = "Run Status", Description = "Review scheduled run manifests and retained delivery evidence.", Glyph = "\uE9D9" },
                new WorkspaceCommandItem { Id = "AnalysisExport", Label = "Exports", Description = "Open structured reporting export presets.", Glyph = "\uE8E5" }
            ],
            SecondaryCommands =
            [
                new WorkspaceCommandItem { Id = "Dashboard", Label = "Dashboard", Description = "Open portfolio reporting dashboard.", Glyph = "\uE71D" },
                new WorkspaceCommandItem { Id = "FundAuditTrail", Label = "Audit", Description = "Open retained audit lineage.", Glyph = "\uE8D7" }
            ]
        }
        : new WorkspaceCommandGroup
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem { Id = "FundAccounts", Label = "Select Fund", Description = "Select a fund-linked context before loading reporting telemetry.", Glyph = "\uE8D4" },
                new WorkspaceCommandItem { Id = "Diagnostics", Label = "Diagnostics", Description = "Open diagnostics while reporting context is locked.", Glyph = "\uE90F" }
            ]
        };
}
