using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Reporting;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Export;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ReportPackRunReadService
{
    private const int DefaultRecentRunLimit = 12;
    private const int StructuredReportingExportSchemaVersion = 1;
    private const string RegulatoryTrialBalanceExportId = "regulatory-trial-balance";
    private const string WarehouseLedgerFactsExportId = "warehouse-ledger-facts";
    private const string InvestmentPortfolioCutsExportId = "investment-portfolio-cuts";
    private const string InvestmentTopNContributionAnalyticsExportId = "investment-topn-contribution-analytics";
    private const string CrossFundConsolidationExportId = "cross-fund-consolidation";

    private static readonly StructuredReportingExportColumnDto[] RegulatoryReportPackAuditColumns =
    [
        new("runId", "string", "Reporting run or workflow record identifier."),
        new("templateId", "string", "Template or report-pack family identifier."),
        new("family", "string", "Report family or retained workflow classification."),
        new("status", "string", "Current governed run or workflow status."),
        new("trigger", "string", "Run trigger or workflow source."),
        new("asOfDate", "string", "Run as-of date or retained workflow timestamp."),
        new("attemptCount", "integer", "Generation or workflow attempt count."),
        new("sectionCount", "integer", "Rendered section count."),
        new("lineageLinkedSections", "integer", "Rendered sections linked to retained lineage."),
        new("failureReason", "string", "Failure reason when the run is not ready.")
    ];

    private static readonly StructuredReportingExportColumnDto[] WarehouseReportPackFactColumns =
    [
        new("distributionId", "string", "Stable distribution recipient identifier."),
        new("recipient", "string", "Distribution recipient display name."),
        new("recipientRole", "string", "Recipient role or stakeholder class."),
        new("channel", "string", "Delivery channel such as email link, secure portal, or vault."),
        new("state", "string", "Current distribution state."),
        new("pendingItems", "integer", "Pending package or delivery item count."),
        new("owner", "string", "Owning user, group, or company principal."),
        new("dueAtUtc", "datetime", "Scheduled due timestamp when present."),
        new("lastSentAtUtc", "datetime", "Latest retained delivery timestamp when present.")
    ];

    private static readonly StructuredReportingExportColumnDto[] InvestmentPortfolioCutColumns =
    [
        new("cutId", "string", "Stable fund, strategy, or user-tag cut identifier."),
        new("label", "string", "Human-readable cut label."),
        new("kind", "string", "Cut kind: Fund, Strategy, or UserTag."),
        new("currency", "string", "Reporting currency."),
        new("grossExposure", "decimal", "Gross exposure for the cut."),
        new("netExposure", "decimal", "Net exposure for the cut."),
        new("totalCash", "decimal", "Cash balance included in the cut."),
        new("pendingSettlement", "decimal", "Pending settlement amount."),
        new("totalPnl", "decimal", "Realized plus unrealized P&L."),
        new("shadowNav", "decimal", "Shadow NAV for the cut."),
        new("shadowNavVariance", "decimal", "Shadow NAV variance against source equity."),
        new("sourceCount", "integer", "Number of contributing source records."),
        new("versionStamp", "string", "Deterministic export/cut version marker.")
    ];

    private static readonly StructuredReportingExportColumnDto[] InvestmentTopNContributionAnalyticsColumns =
    [
        new("analyticsId", "string", "Stable Top-N or contribution analytics row identifier."),
        new("kind", "string", "Analytics row kind: TopWinner, TopLaggard, or Contribution."),
        new("scope", "string", "Analytics scope: Security, Strategy, or AssetClass."),
        new("rank", "integer", "Rank within the analytics kind and scope."),
        new("label", "string", "Human-readable row label."),
        new("symbol", "string", "Security symbol when the row is security-scoped."),
        new("classification", "string", "Security asset class, strategy, or asset-class classification."),
        new("currency", "string", "Reporting currency."),
        new("realizedPnl", "decimal", "Realized P&L included in the analytics row."),
        new("unrealizedPnl", "decimal", "Unrealized P&L included in the analytics row."),
        new("totalPnl", "decimal", "Realized plus unrealized P&L."),
        new("contributionPercent", "decimal", "Percent contribution to total portfolio P&L."),
        new("heatMapIntensity", "decimal", "Absolute P&L intensity used by reporting heat maps."),
        new("sourceCount", "integer", "Number of contributing source runs."),
        new("asOfUtc", "datetime", "Analytics as-of timestamp in UTC."),
        new("readinessSummary", "string", "Source-backed readiness description."),
        new("versionStamp", "string", "Deterministic analytics version marker."),
        new("tags", "string", "Pipe-delimited analytics tags.")
    ];

    private static readonly StructuredReportingExportColumnDto[] CrossFundConsolidationColumns =
    [
        new("consolidationId", "string", "Stable cross-fund consolidation row identifier."),
        new("label", "string", "Human-readable consolidation label."),
        new("scope", "string", "Consolidation scope: Company, Fund, or Entity."),
        new("currency", "string", "Reporting currency."),
        new("isReady", "boolean", "Whether the row has source-backed records."),
        new("fundCount", "integer", "Distinct fund count represented by the row."),
        new("entityCount", "integer", "Distinct legal/entity count represented by the row."),
        new("accountCount", "integer", "Contributing account source count."),
        new("runCount", "integer", "Contributing strategy-run source count."),
        new("grossExposure", "decimal", "Aggregated gross exposure."),
        new("netExposure", "decimal", "Aggregated net exposure."),
        new("totalCash", "decimal", "Aggregated cash balance."),
        new("pendingSettlement", "decimal", "Aggregated pending settlement amount."),
        new("totalPnl", "decimal", "Aggregated realized plus unrealized P&L."),
        new("shadowNav", "decimal", "Aggregated source-backed shadow NAV."),
        new("shadowNavVariance", "decimal", "Shadow NAV variance against aggregate net exposure."),
        new("sourceCount", "integer", "Contributing account plus run source count."),
        new("readinessSummary", "string", "Source-backed readiness description."),
        new("versionStamp", "string", "Deterministic export/consolidation version marker.")
    ];

    private readonly IReportingTemplateCatalog _templateCatalog;
    private readonly IReportingRunStore? _runStore;
    private readonly ReportPackWorkflowService? _workflowService;
    private readonly ReportTemplateRegistryService? _templateRegistry;
    private readonly ReportPackDeliveryService? _deliveryService;
    private readonly ReportingScheduleService? _scheduleService;
    private readonly ReportingStarterKitService? _starterKitService;

    private static readonly ReportBrandingThemeDto[] BuiltInReportBrandingThemes =
    [
        new(
            "meridian-standard",
            "Meridian Standard",
            "Meridian",
            "#195E63",
            "#2F8F83",
            "#1F2933",
            "#F8FAFC",
            FooterText: "Generated by Meridian Reporting",
            Disclaimer: "Confidential report pack generated from retained Meridian source evidence."),
        new(
            "allocator-clean",
            "Allocator Clean",
            "Meridian",
            "#243B53",
            "#3E7C59",
            "#102A43",
            "#FFFFFF",
            FooterText: "Investor-ready governed reporting",
            Disclaimer: "For authorized recipients only; values remain subject to approved report-pack workflow state."),
        new(
            "compliance-file",
            "Compliance File",
            "Meridian Compliance",
            "#334E68",
            "#B7791F",
            "#1A202C",
            "#F7FAFC",
            FooterText: "Retained compliance export",
            Disclaimer: "Retain with the generated manifest, provenance, and approval evidence.")
    ];

    public ReportPackRunReadService(
        IReportingTemplateCatalog templateCatalog,
        IReportingRunStore? runStore = null,
        ReportPackWorkflowService? workflowService = null,
        ReportTemplateRegistryService? templateRegistry = null,
        ReportPackDeliveryService? deliveryService = null,
        ReportingScheduleService? scheduleService = null,
        ReportingStarterKitService? starterKitService = null)
    {
        _templateCatalog = templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
        _runStore = runStore;
        _workflowService = workflowService;
        _templateRegistry = templateRegistry;
        _deliveryService = deliveryService;
        _scheduleService = scheduleService;
        _starterKitService = starterKitService;
    }

    public WorkstationReportingPayload BuildPayload(int recentRunLimit = DefaultRecentRunLimit) =>
        BuildPayload(accessContext: null, recentRunLimit);

    public WorkstationReportingPayload BuildPayload(
        ReportAccessQueryContext? accessContext,
        int recentRunLimit = DefaultRecentRunLimit)
    {
        var profiles = BuildProfiles();
        var recommended = profiles
            .Where(static profile => profile.Id is "excel" or "python-pandas" or "postgresql" or "arrow-feather")
            .Select(static profile => profile.Id)
            .ToArray();
        var allWorkflowRecords = _workflowService?.ListRecords(200) ?? [];
        var allDeliveryAttempts = _deliveryService?.ListAttempts(500) ?? [];
        var allSchedules = _scheduleService?.ListSchedules(100) ?? [];
        var totalTemplateCount = CountTemplates();
        var starterKits = _starterKitService?.ListKits() ?? [];
        var starterKitState = _starterKitService?.GetState(accessContext);
        var templates = ApplyStarterKitTemplateFilter(BuildTemplates(accessContext), starterKitState);
        var familyByTemplate = templates
            .GroupBy(static template => template.TemplateId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Family, StringComparer.OrdinalIgnoreCase);
        var templatesById = templates
            .GroupBy(static template => template.TemplateId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var workflowRecords = FilterWorkflowRecords(allWorkflowRecords, accessContext);
        var runs = BuildRecentRuns(
            Math.Clamp(recentRunLimit, 1, 200),
            familyByTemplate,
            templatesById,
            workflowRecords,
            accessContext);
        var deliveryAttempts = FilterDeliveryAttempts(
            allDeliveryAttempts,
            accessContext,
            templates,
            workflowRecords,
            runs.Select(static run => run.Payload.RunId).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var schedules = FilterSchedules(
            allSchedules,
            accessContext,
            templates);
        var scheduleDeliveryPlans = BuildScheduleDeliveryPlans(schedules, deliveryAttempts);
        var distributions = BuildDistributionRecords(workflowRecords, deliveryAttempts);
        var pendingDistributionCount = distributions.Count(static distribution => distribution.PendingItems > 0);
        var selectedFundProfileId = ResolveSelectedFundProfileId(workflowRecords);
        var reportLineProvenanceExplorer = FinancialRecordExplorerReadService.BuildReportLineProvenanceExplorer(
            workflowRecords,
            deliveryAttempts);
        var portfolioCuts = BuildPortfolioReportingCuts(workflowRecords);
        var livePortfolioViews = BuildPortfolioReportingLiveViews(portfolioCuts);
        var crossFundConsolidations = BuildCrossFundConsolidations(portfolioCuts);
        var pnlSlices = BuildPortfolioReportingPnlSlices(portfolioCuts);
        var analyticsRows = BuildPortfolioReportingAnalyticsRows(portfolioCuts);
        var reportWriterDatasetSources = BuildReportWriterDatasetSources(
            portfolioCuts,
            analyticsRows,
            crossFundConsolidations);
        var structuredExports = BuildStructuredReportingExports(
            selectedFundProfileId,
            workflowRecords,
            deliveryAttempts,
            portfolioCuts,
            analyticsRows,
            crossFundConsolidations);
        var allPortfolioCuts = accessContext is null ? portfolioCuts : BuildPortfolioReportingCuts(allWorkflowRecords);
        var allAnalyticsRows = accessContext is null ? analyticsRows : BuildPortfolioReportingAnalyticsRows(allPortfolioCuts);
        var allCrossFundConsolidations = accessContext is null ? crossFundConsolidations : BuildCrossFundConsolidations(allPortfolioCuts);
        var visibleStructuredExportCount = structuredExports.Count;
        var hiddenStructuredExportCount = accessContext is null
            ? 0
            : Math.Max(
                0,
                BuildStructuredReportingExports(
                    ResolveSelectedFundProfileId(allWorkflowRecords),
                    allWorkflowRecords,
                    allDeliveryAttempts,
                    allPortfolioCuts,
                    allAnalyticsRows,
                    allCrossFundConsolidations).Count - visibleStructuredExportCount);
        var accessAudit = BuildAccessAuditSummary(
            accessContext,
            totalTemplateCount,
            templates.Length,
            allWorkflowRecords.Count,
            workflowRecords.Count,
            allSchedules.Count,
            schedules.Count,
            allDeliveryAttempts.Count,
            deliveryAttempts.Count,
            visibleStructuredExportCount,
            hiddenStructuredExportCount);
        var recentRuns = runs.Select(static run => run.Payload).ToArray();
        var dailyWork = BuildDailyWorkItems(
            workflowRecords,
            recentRuns,
            schedules,
            scheduleDeliveryPlans,
            deliveryAttempts);

        return new WorkstationReportingPayload(
            ProfileCount: profiles.Length,
            RecommendedProfiles: recommended,
            Profiles: profiles,
            ReportPackDistributions: distributions,
            Summary: $"{profiles.Length} export/reporting profiles are available for Accounting and Reporting workflows; {distributions.Length} distribution recipients are visible; {pendingDistributionCount} have pending work.",
            Templates: templates,
            RecentRuns: recentRuns,
            Schedules: schedules,
            DeliveryAttempts: deliveryAttempts,
            SelectedFundProfileId: selectedFundProfileId,
            ScheduleDeliveryPlans: scheduleDeliveryPlans,
            ReportLineProvenanceExplorer: reportLineProvenanceExplorer,
            PortfolioCuts: portfolioCuts,
            LivePortfolioViews: livePortfolioViews,
            CrossFundConsolidations: crossFundConsolidations,
            PnlSlices: pnlSlices,
            AnalyticsRows: analyticsRows,
            StructuredExports: structuredExports,
            BrandingThemes: BuiltInReportBrandingThemes,
            ReportWriterDatasetSources: reportWriterDatasetSources,
            AccessAudit: accessAudit,
            DailyWork: dailyWork,
            StarterKits: starterKits,
            StarterKitState: starterKitState);
    }

    public static WorkstationReportingPayload BuildFallbackPayload() =>
        new ReportPackRunReadService(new DefaultReportingTemplateCatalog()).BuildPayload();

    public StructuredReportingExportPayloadDto GetStructuredReportingExport(
        string exportId,
        ReportAccessQueryContext? accessContext = null,
        int recentRunLimit = DefaultRecentRunLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportId);
        var reporting = BuildPayload(accessContext, recentRunLimit);
        var normalizedExportId = NormalizeStructuredExportId(exportId);
        var descriptor = (reporting.StructuredExports ?? [])
            .FirstOrDefault(export => string.Equals(
                export.ExportId,
                normalizedExportId,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Structured reporting export '{exportId}' is not available.");

        var columns = GetStructuredExportColumns(normalizedExportId);
        var rows = BuildStructuredExportRows(normalizedExportId, reporting);
        var warnings = descriptor.IsReady ? [] : descriptor.ReadinessBlockers ?? [];
        return new StructuredReportingExportPayloadDto(
            descriptor,
            columns,
            rows,
            warnings,
            DateTimeOffset.UtcNow,
            BuildStructuredExportDataDictionary(columns),
            BuildStructuredExportValidationChecks(descriptor, columns, rows),
            GeneratedByPrincipalId: accessContext?.ActorPrincipalId,
            GeneratedForCompanyId: accessContext?.CompanyId,
            GeneratedForGroupPrincipalIds: accessContext?.GroupPrincipalIds?.ToArray() ?? [],
            RowLineage: BuildStructuredExportRowLineage(descriptor.ExportId, columns, rows));
    }

    public IReadOnlyList<IReadOnlyDictionary<string, string>> BuildReportWriterDatasetRows(
        ReportAccessQueryContext? accessContext = null,
        int recentRunLimit = DefaultRecentRunLimit) =>
        ReportWriterDatasetSourceService.BuildDatasetRows(BuildPayload(accessContext, recentRunLimit));

    private static IReadOnlyList<WorkstationReportWriterDatasetSourcePayload> BuildReportWriterDatasetSources(
        IReadOnlyList<PortfolioReportingCutDto> portfolioCuts,
        IReadOnlyList<PortfolioReportingAnalyticsRowDto> analyticsRows,
        IReadOnlyList<CrossFundReportingConsolidationDto> crossFundConsolidations) =>
        ReportWriterDatasetSourceService.BuildDatasetSources(
            portfolioCuts,
            analyticsRows,
            crossFundConsolidations);

    public static ReportPackDistributionPolicy ResolveDistributionPolicy(string distributionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distributionId);
        return DistributionPolicies.FirstOrDefault(policy =>
                   string.Equals(policy.DistributionId, distributionId.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException($"Unknown report-pack distribution '{distributionId}'.");
    }

    private static WorkstationReportingProfilePayload[] BuildProfiles() =>
        ExportProfile.GetBuiltInProfiles()
            .Select(static profile => new WorkstationReportingProfilePayload(
                Id: profile.Id,
                Name: profile.Name,
                TargetTool: profile.TargetTool,
                Format: profile.Format.ToString(),
                Description: profile.Description ?? string.Empty,
                LoaderScript: profile.IncludeLoaderScript,
                DataDictionary: profile.IncludeDataDictionary))
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? ResolveSelectedFundProfileId(IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords) =>
        workflowRecords
            .Where(static record => !string.IsNullOrWhiteSpace(record.FundProfileId))
            .OrderByDescending(static record => record.UpdatedAt)
            .Select(static record => record.FundProfileId.Trim())
            .FirstOrDefault();

    internal static IReadOnlyList<PortfolioReportingCutDto> BuildPortfolioReportingCuts(
        IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords)
    {
        var cuts = new List<PortfolioReportingCutDto>();
        foreach (var group in workflowRecords
                     .Where(static record => record.LineProvenance is { Count: > 0 })
                     .GroupBy(static record => string.IsNullOrWhiteSpace(record.FundProfileId) ? "unknown-fund" : record.FundProfileId.Trim(), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var records = group
                .OrderByDescending(static record => record.UpdatedAt)
                .ThenBy(static record => record.ReportId)
                .ToArray();
            var latest = records[0];
            var sources = records
                .SelectMany(static record => record.LineProvenance ?? [])
                .Select(TryClassifyPortfolioLine)
                .Where(static source => source is not null)
                .Select(static source => source!)
                .ToArray();
            if (sources.Length == 0)
            {
                continue;
            }

            var grossExposure = SumSources(sources, PortfolioLineMetric.GrossExposure);
            var longMarketValue = SumSources(sources, PortfolioLineMetric.LongMarketValue);
            var shortMarketValue = SumSources(sources, PortfolioLineMetric.ShortMarketValue);
            var netExposure = SumSources(sources, PortfolioLineMetric.NetExposure);
            var cash = SumSources(sources, PortfolioLineMetric.Cash);
            var pendingSettlement = SumSources(sources, PortfolioLineMetric.PendingSettlement);
            var realizedPnl = SumSources(sources, PortfolioLineMetric.RealizedPnl);
            var unrealizedPnl = SumSources(sources, PortfolioLineMetric.UnrealizedPnl);
            var totalPnl = SumSources(sources, PortfolioLineMetric.TotalPnl);
            if (totalPnl == 0m)
            {
                totalPnl = realizedPnl + unrealizedPnl;
            }

            var shadowNav = SumSources(sources, PortfolioLineMetric.ShadowNav);
            var reportedNav = SumSources(sources, PortfolioLineMetric.ReportedNav);
            var shadowNavVariance = reportedNav == 0m ? 0m : shadowNav - reportedNav;
            var asOf = records.Max(static record => record.UpdatedAt);
            var evidenceRoute = sources
                .Select(static source => source.Line.FinancialRecordHref)
                .FirstOrDefault(static route => !string.IsNullOrWhiteSpace(route));

            cuts.Add(new PortfolioReportingCutDto(
                CutId: $"reporting-portfolio-cut:{group.Key}",
                Label: $"{group.Key} retained portfolio reporting cut",
                Kind: PortfolioReportingCutKindDto.Fund,
                Currency: "USD",
                GrossExposure: grossExposure,
                NetExposure: netExposure,
                LongMarketValue: longMarketValue,
                ShortMarketValue: shortMarketValue,
                TotalCash: cash,
                PendingSettlement: pendingSettlement,
                RealizedPnl: realizedPnl,
                UnrealizedPnl: unrealizedPnl,
                TotalPnl: totalPnl,
                ShadowNav: shadowNav,
                ShadowNavVariance: shadowNavVariance,
                SourceCount: sources.Length,
                Tags: BuildPortfolioCutTags(records, sources),
                AsOf: asOf,
                EvidenceRoute: evidenceRoute,
                ShadowNavNote: shadowNav == 0m ? "No retained shadow-NAV line was present." : "Shadow NAV is retained from report-pack line provenance.",
                VersionStamp: $"portfolio-cut:{group.Key}:{latest.Version}:{asOf.UtcDateTime:yyyyMMddHHmmss}"));
        }

        return cuts;
    }

    private static IReadOnlyList<PortfolioReportingLiveViewDto> BuildPortfolioReportingLiveViews(
        IReadOnlyList<PortfolioReportingCutDto> portfolioCuts) =>
        portfolioCuts
            .Select(cut => new PortfolioReportingLiveViewDto(
                ViewId: $"reporting-live-view:{cut.CutId}",
                Label: cut.Label,
                Kind: cut.Kind,
                State: cut.SourceCount > 0 ? PortfolioReportingLiveViewStateDto.SourceBacked : PortfolioReportingLiveViewStateDto.Blocked,
                Currency: cut.Currency,
                GrossExposure: cut.GrossExposure,
                NetExposure: cut.NetExposure,
                TotalCash: cut.TotalCash,
                PendingSettlement: cut.PendingSettlement,
                TotalPnl: cut.TotalPnl,
                ShadowNav: cut.ShadowNav,
                AsOf: cut.AsOf,
                SourceAsOfUtc: cut.AsOf,
                SourceCount: cut.SourceCount,
                Route: cut.EvidenceRoute ?? "/api/workstation/reporting",
                LiquiditySummary: $"Cash {FormatCurrency(cut.TotalCash, cut.Currency)}; pending settlement {FormatCurrency(cut.PendingSettlement, cut.Currency)}.",
                CashLadderSummary: cut.TotalCash == 0m ? "No retained cash ladder source is present." : "Cash ladder is source-backed by retained report-pack provenance.",
                TelemetrySummary: $"Retained exposure {FormatCurrency(cut.GrossExposure, cut.Currency)} and P&L {FormatCurrency(cut.TotalPnl, cut.Currency)}.",
                Tags: cut.Tags,
                VersionStamp: $"live-view:{cut.VersionStamp}",
                ReadinessBlockers: cut.SourceCount == 0 ? ["No retained portfolio reporting provenance is available."] : [],
                MarketTickAsOfUtc: cut.SourceCount > 0 ? cut.AsOf : null,
                MarketTickAgeSeconds: cut.SourceCount > 0 ? 0 : null,
                MarketTickSequence: cut.SourceCount > 0 ? cut.AsOf.ToUnixTimeMilliseconds() : null,
                MarketDataProvider: cut.SourceCount > 0 ? "retained-portfolio-snapshot" : "unavailable",
                TickFreshnessSummary: cut.SourceCount > 0
                    ? "Source-backed tick snapshot is retained from report-pack line provenance."
                    : "No market tick snapshot is available for this reporting live view.",
                IsMarketTickLinked: false,
                FreshnessPolicy: BuildRetainedPortfolioFreshnessPolicy(cut)))
            .ToArray();

    private static PortfolioReportingLiveViewFreshnessPolicyDto BuildRetainedPortfolioFreshnessPolicy(PortfolioReportingCutDto cut)
        => new(
            PolicyName: "RetainedReportPackProvenance",
            EvaluatedAtUtc: cut.AsOf,
            SourceAgeSeconds: cut.SourceCount > 0 ? 0 : null,
            LiveLinkWindowSeconds: 300,
            StaleWindowSeconds: 86_400,
            IsWithinLiveLinkWindow: false,
            IsBeyondStaleWindow: false,
            Reason: cut.SourceCount > 0
                ? "Retained report-pack provenance is source-backed and not classified as live-linked market telemetry."
                : "No retained portfolio reporting provenance is available, so freshness fails closed.");

    internal static IReadOnlyList<CrossFundReportingConsolidationDto> BuildCrossFundConsolidations(
        IReadOnlyList<PortfolioReportingCutDto> portfolioCuts)
    {
        if (portfolioCuts.Count == 0)
        {
            return [];
        }

        var asOf = portfolioCuts.Max(static cut => cut.AsOf);
        return
        [
            new CrossFundReportingConsolidationDto(
                ConsolidationId: "reporting-cross-fund:company",
                Label: "Company retained reporting consolidation",
                Scope: CrossFundReportingConsolidationScopeDto.Company,
                Currency: portfolioCuts[0].Currency,
                IsReady: portfolioCuts.All(static cut => cut.SourceCount > 0),
                FundCount: portfolioCuts.Count,
                EntityCount: portfolioCuts.Count,
                AccountCount: portfolioCuts.Count,
                RunCount: portfolioCuts.Sum(static cut => cut.SourceCount),
                GrossExposure: portfolioCuts.Sum(static cut => cut.GrossExposure),
                NetExposure: portfolioCuts.Sum(static cut => cut.NetExposure),
                LongMarketValue: portfolioCuts.Sum(static cut => cut.LongMarketValue),
                ShortMarketValue: portfolioCuts.Sum(static cut => cut.ShortMarketValue),
                TotalCash: portfolioCuts.Sum(static cut => cut.TotalCash),
                PendingSettlement: portfolioCuts.Sum(static cut => cut.PendingSettlement),
                TotalPnl: portfolioCuts.Sum(static cut => cut.TotalPnl),
                ShadowNav: portfolioCuts.Sum(static cut => cut.ShadowNav),
                ShadowNavVariance: portfolioCuts.Sum(static cut => cut.ShadowNavVariance),
                SourceCount: portfolioCuts.Sum(static cut => cut.SourceCount),
                AsOf: asOf,
                Route: "/api/workstation/reporting",
                ReadinessSummary: "Cross-fund consolidation is derived from retained report-pack portfolio cuts.",
                Tags: portfolioCuts.SelectMany(static cut => cut.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                VersionStamp: $"cross-fund:company:{asOf.UtcDateTime:yyyyMMddHHmmss}")
        ];
    }

    private static IReadOnlyList<PortfolioReportingPnlSliceDto> BuildPortfolioReportingPnlSlices(
        IReadOnlyList<PortfolioReportingCutDto> portfolioCuts)
    {
        if (portfolioCuts.Count == 0)
        {
            return [];
        }

        var asOf = portfolioCuts.Max(static cut => cut.AsOf);
        var endDate = DateOnly.FromDateTime(asOf.UtcDateTime);
        var totalPnl = portfolioCuts.Sum(static cut => cut.TotalPnl);
        var realizedPnl = portfolioCuts.Sum(static cut => cut.RealizedPnl);
        var unrealizedPnl = portfolioCuts.Sum(static cut => cut.UnrealizedPnl);
        var sourceCount = portfolioCuts.Sum(static cut => cut.SourceCount);
        return
        [
            BuildPortfolioReportingPnlSlice(PortfolioReportingPnlSlicePeriodDto.Daily, endDate, endDate, realizedPnl, unrealizedPnl, totalPnl, sourceCount, asOf),
            BuildPortfolioReportingPnlSlice(PortfolioReportingPnlSlicePeriodDto.Weekly, endDate.AddDays(-6), endDate, realizedPnl, unrealizedPnl, totalPnl, sourceCount, asOf),
            BuildPortfolioReportingPnlSlice(PortfolioReportingPnlSlicePeriodDto.Monthly, new DateOnly(endDate.Year, endDate.Month, 1), endDate, realizedPnl, unrealizedPnl, totalPnl, sourceCount, asOf),
            BuildPortfolioReportingPnlSlice(PortfolioReportingPnlSlicePeriodDto.Yearly, new DateOnly(endDate.Year, 1, 1), endDate, realizedPnl, unrealizedPnl, totalPnl, sourceCount, asOf)
        ];
    }

    private static PortfolioReportingPnlSliceDto BuildPortfolioReportingPnlSlice(
        PortfolioReportingPnlSlicePeriodDto period,
        DateOnly startDate,
        DateOnly endDate,
        decimal realizedPnl,
        decimal unrealizedPnl,
        decimal totalPnl,
        int sourceCount,
        DateTimeOffset asOf) =>
        new(
            SliceId: $"reporting-pnl:{period.ToString().ToLowerInvariant()}:{endDate:yyyyMMdd}",
            Period: period,
            Label: $"{period} retained P&L",
            Currency: "USD",
            StartDate: startDate,
            EndDate: endDate,
            RealizedPnl: realizedPnl,
            UnrealizedPnl: unrealizedPnl,
            TotalPnl: totalPnl,
            PriorTotalPnl: 0m,
            PnlChange: totalPnl,
            SourceCount: sourceCount,
            AsOf: asOf,
            Route: "/api/workstation/reporting",
            ReadinessSummary: $"Retained {period} P&L slice is derived from {sourceCount} report-pack portfolio provenance line(s).",
            Tags: ["retained-report-pack", "portfolio-reporting", period.ToString()],
            VersionStamp: $"pnl-slice:{period}:{asOf.UtcDateTime:yyyyMMddHHmmss}");

    internal static IReadOnlyList<PortfolioReportingAnalyticsRowDto> BuildPortfolioReportingAnalyticsRows(
        IReadOnlyList<PortfolioReportingCutDto> portfolioCuts)
    {
        if (portfolioCuts.Count == 0)
        {
            return [];
        }

        var totalAbsPnl = portfolioCuts.Sum(static cut => Math.Abs(cut.TotalPnl));
        if (totalAbsPnl == 0m)
        {
            totalAbsPnl = 1m;
        }

        var winners = portfolioCuts
            .Where(static cut => cut.TotalPnl >= 0m)
            .OrderByDescending(static cut => cut.TotalPnl)
            .ThenBy(static cut => cut.CutId, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select((cut, index) => BuildPortfolioReportingAnalyticsRow(
                cut,
                PortfolioReportingAnalyticsKindDto.TopWinner,
                index + 1,
                totalAbsPnl));
        var laggards = portfolioCuts
            .Where(static cut => cut.TotalPnl < 0m)
            .OrderBy(static cut => cut.TotalPnl)
            .ThenBy(static cut => cut.CutId, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select((cut, index) => BuildPortfolioReportingAnalyticsRow(
                cut,
                PortfolioReportingAnalyticsKindDto.TopLaggard,
                index + 1,
                totalAbsPnl));
        var contribution = portfolioCuts
            .OrderByDescending(static cut => Math.Abs(cut.TotalPnl))
            .ThenBy(static cut => cut.CutId, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select((cut, index) => BuildPortfolioReportingAnalyticsRow(
                cut,
                PortfolioReportingAnalyticsKindDto.Contribution,
                index + 1,
                totalAbsPnl));

        return winners
            .Concat(laggards)
            .Concat(contribution)
            .ToArray();
    }

    private static PortfolioReportingAnalyticsRowDto BuildPortfolioReportingAnalyticsRow(
        PortfolioReportingCutDto cut,
        PortfolioReportingAnalyticsKindDto kind,
        int rank,
        decimal totalAbsPnl)
    {
        var contributionPercent = totalAbsPnl == 0m ? 0m : cut.TotalPnl / totalAbsPnl * 100m;
        var heatMapIntensity = Math.Min(100m, Math.Abs(contributionPercent));
        return new PortfolioReportingAnalyticsRowDto(
            AnalyticsId: $"reporting-analytics:{kind.ToString().ToLowerInvariant()}:{cut.CutId}",
            Kind: kind,
            Scope: PortfolioReportingAnalyticsScopeDto.Strategy,
            Rank: rank,
            Label: cut.Label,
            Symbol: null,
            Classification: cut.Kind.ToString(),
            Currency: cut.Currency,
            RealizedPnl: cut.RealizedPnl,
            UnrealizedPnl: cut.UnrealizedPnl,
            TotalPnl: cut.TotalPnl,
            ContributionPercent: contributionPercent,
            HeatMapIntensity: heatMapIntensity,
            SourceCount: cut.SourceCount,
            AsOf: cut.AsOf,
            Route: cut.EvidenceRoute ?? "/api/workstation/reporting",
            ReadinessSummary: $"{kind} row is derived from retained report-pack portfolio cut {cut.CutId}.",
            Tags: cut.Tags.Append($"analytics:{kind}").Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            VersionStamp: $"analytics:{kind}:{cut.VersionStamp}");
    }

    private static IReadOnlyList<StructuredReportingExportDto> BuildStructuredReportingExports(
        string? selectedFundProfileId,
        IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts,
        IReadOnlyList<PortfolioReportingCutDto> portfolioCuts,
        IReadOnlyList<PortfolioReportingAnalyticsRowDto> analyticsRows,
        IReadOnlyList<CrossFundReportingConsolidationDto> crossFundConsolidations)
    {
        if (string.IsNullOrWhiteSpace(selectedFundProfileId))
        {
            return [];
        }

        var fundProfileId = selectedFundProfileId.Trim();
        var asOf = ResolveStructuredExportAsOf(workflowRecords, deliveryAttempts, portfolioCuts, analyticsRows, crossFundConsolidations);
        var currency = portfolioCuts.FirstOrDefault()?.Currency ?? analyticsRows.FirstOrDefault()?.Currency ?? "USD";
        var workflowSourceCount = workflowRecords.Count;
        var lineSourceCount = workflowRecords.Sum(static record => record.LineProvenance?.Count ?? 0);
        var deliverySourceCount = deliveryAttempts.Count;
        var portfolioSourceCount = portfolioCuts.Sum(static cut => cut.SourceCount);
        var analyticsSourceCount = analyticsRows.Sum(static row => row.SourceCount);
        var crossFundSourceCount = crossFundConsolidations.Sum(static row => row.SourceCount);
        var hasGovernedWorkflowEvidence = workflowSourceCount > 0 || lineSourceCount > 0 || deliverySourceCount > 0;

        return
        [
            BuildStructuredExportDescriptor(
                fundProfileId,
                RegulatoryTrialBalanceExportId,
                "Regulatory trial balance",
                StructuredReportingExportPurposeDto.Regulatory,
                GovernanceReportArtifactFormatDto.Csv,
                "fund-trial-balance",
                "Regulatory and compliance reporting",
                Math.Max(1, workflowSourceCount),
                RegulatoryReportPackAuditColumns.Length,
                workflowSourceCount + lineSourceCount,
                currency,
                asOf,
                hasGovernedWorkflowEvidence,
                "Regulatory export is linked to retained report-pack workflow, approval, and line-provenance evidence.",
                "No retained workflow, approval, or report-line provenance is available for regulatory export evidence.",
                ["regulatory", "trial-balance", "report-pack"]),
            BuildStructuredExportDescriptor(
                fundProfileId,
                WarehouseLedgerFactsExportId,
                "Warehouse ledger facts",
                StructuredReportingExportPurposeDto.DataWarehouse,
                GovernanceReportArtifactFormatDto.Json,
                "ledger-reconciliation-facts",
                "Data warehouse and lakehouse ingestion",
                Math.Max(1, deliverySourceCount + workflowSourceCount),
                WarehouseReportPackFactColumns.Length,
                workflowSourceCount + deliverySourceCount,
                currency,
                asOf,
                hasGovernedWorkflowEvidence,
                "Warehouse export is linked to retained report-pack workflow and distribution delivery records.",
                "No retained workflow or delivery records are available for warehouse export evidence.",
                ["warehouse", "ledger", "distribution"]),
            BuildStructuredExportDescriptor(
                fundProfileId,
                InvestmentPortfolioCutsExportId,
                "Investment portfolio cuts",
                StructuredReportingExportPurposeDto.InvestmentDecision,
                GovernanceReportArtifactFormatDto.Xlsx,
                "portfolio-reporting-cuts",
                "Investment and risk decision workflows",
                portfolioCuts.Count,
                InvestmentPortfolioCutColumns.Length,
                portfolioSourceCount,
                currency,
                asOf,
                portfolioCuts.Count > 0 && portfolioSourceCount > 0,
                "Investment portfolio cuts export retained exposure, cash, P&L, and shadow-NAV rows.",
                "No source-backed portfolio cut rows are available from retained report-pack evidence.",
                ["investment", "portfolio-cuts", "shadow-nav"]),
            BuildStructuredExportDescriptor(
                fundProfileId,
                InvestmentTopNContributionAnalyticsExportId,
                "Top-N contribution analytics",
                StructuredReportingExportPurposeDto.InvestmentDecision,
                GovernanceReportArtifactFormatDto.Csv,
                "portfolio-topn-contribution-analytics",
                "Investment and risk decision workflows",
                analyticsRows.Count,
                InvestmentTopNContributionAnalyticsColumns.Length,
                analyticsSourceCount,
                currency,
                asOf,
                analyticsRows.Count > 0 && analyticsSourceCount > 0,
                "Top-N and contribution export is derived from retained portfolio P&L cuts.",
                "No source-backed Top-N or contribution analytics rows are available.",
                ["investment", "top-n", "contribution", "analytics"]),
            BuildStructuredExportDescriptor(
                fundProfileId,
                CrossFundConsolidationExportId,
                "Cross-fund consolidation",
                StructuredReportingExportPurposeDto.InvestmentDecision,
                GovernanceReportArtifactFormatDto.Xlsx,
                "cross-fund-reporting-consolidation",
                "Investment and operating committee reporting",
                crossFundConsolidations.Count,
                CrossFundConsolidationColumns.Length,
                crossFundSourceCount,
                currency,
                asOf,
                crossFundConsolidations.Any(static row => row.IsReady),
                "Cross-fund consolidation export aggregates retained fund-level portfolio cuts.",
                "No source-backed cross-fund consolidation rows are available.",
                ["investment", "cross-fund", "consolidation"])
        ];
    }

    private static StructuredReportingExportDto BuildStructuredExportDescriptor(
        string fundProfileId,
        string exportId,
        string label,
        StructuredReportingExportPurposeDto purpose,
        GovernanceReportArtifactFormatDto format,
        string dataset,
        string consumer,
        int rowCount,
        int fieldCount,
        int sourceCount,
        string currency,
        DateTimeOffset asOf,
        bool isReady,
        string readySummary,
        string blockedSummary,
        IReadOnlyList<string> tags)
    {
        var retainedPath = BuildStructuredExportRetainedPath(fundProfileId, exportId, asOf, format);
        var retainedManifestPath = BuildStructuredExportRetainedManifestPath(fundProfileId, exportId, asOf);
        var versionStamp = $"reporting-structured-export:{exportId}:{asOf.UtcDateTime:yyyyMMddHHmmss}:rows-{rowCount}:sources-{sourceCount}:schema-{StructuredReportingExportSchemaVersion}";
        var integrityHash = BuildStructuredExportIntegrityHash(
            exportId,
            dataset,
            consumer,
            currency,
            asOf,
            rowCount,
            fieldCount,
            sourceCount,
            retainedPath,
            retainedManifestPath,
            versionStamp);

        return new StructuredReportingExportDto(
            exportId,
            label,
            purpose,
            format,
            dataset,
            consumer,
            StructuredReportingExportSchemaVersion,
            rowCount,
            fieldCount,
            sourceCount,
            currency,
            asOf,
            isReady,
            retainedPath,
            BuildStructuredExportRoute(exportId, asOf, currency),
            UiApiRoutes.WorkstationReporting,
            isReady
                ? $"{readySummary} {rowCount} row(s), {fieldCount} field(s), and {sourceCount} source record(s) are ready."
                : blockedSummary,
            UiApiRoutes.FundReportPacks,
            versionStamp,
            tags,
            isReady ? [] : [blockedSummary],
            retainedManifestPath,
            integrityHash,
            BuildStructuredExportIntegritySummary(rowCount, fieldCount, sourceCount, integrityHash),
            rowCount);
    }

    private static DateTimeOffset ResolveStructuredExportAsOf(
        IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts,
        IReadOnlyList<PortfolioReportingCutDto> portfolioCuts,
        IReadOnlyList<PortfolioReportingAnalyticsRowDto> analyticsRows,
        IReadOnlyList<CrossFundReportingConsolidationDto> crossFundConsolidations)
    {
        var timestamps = workflowRecords.Select(static record => record.UpdatedAt)
            .Concat(deliveryAttempts.Select(static attempt => attempt.AttemptedAtUtc))
            .Concat(portfolioCuts.Select(static cut => cut.AsOf))
            .Concat(analyticsRows.Select(static row => row.AsOf))
            .Concat(crossFundConsolidations.Select(static row => row.AsOf))
            .ToArray();
        return timestamps.Length == 0 ? DateTimeOffset.UtcNow : timestamps.Max();
    }

    private static string BuildStructuredExportRetainedPath(
        string fundProfileId,
        string exportId,
        DateTimeOffset asOf,
        GovernanceReportArtifactFormatDto format)
    {
        var extension = format switch
        {
            GovernanceReportArtifactFormatDto.Xlsx => "xlsx",
            GovernanceReportArtifactFormatDto.Csv => "csv",
            _ => "json"
        };
        return $"vault/reporting/structured-exports/{fundProfileId}/{exportId}/{asOf.UtcDateTime:yyyyMMddHHmmss}.{extension}";
    }

    private static string BuildStructuredExportRetainedManifestPath(
        string fundProfileId,
        string exportId,
        DateTimeOffset asOf) =>
        $"vault/reporting/structured-exports/{fundProfileId}/{exportId}/{asOf.UtcDateTime:yyyyMMddHHmmss}.manifest.json";

    private static string BuildStructuredExportIntegrityHash(
        string exportId,
        string dataset,
        string consumer,
        string currency,
        DateTimeOffset asOf,
        int rowCount,
        int fieldCount,
        int sourceCount,
        string retainedPath,
        string retainedManifestPath,
        string versionStamp)
    {
        var input = string.Join('|',
            exportId,
            dataset,
            consumer,
            currency,
            asOf.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            rowCount.ToString(CultureInfo.InvariantCulture),
            fieldCount.ToString(CultureInfo.InvariantCulture),
            sourceCount.ToString(CultureInfo.InvariantCulture),
            retainedPath,
            retainedManifestPath,
            versionStamp);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static string BuildStructuredExportIntegritySummary(
        int rowCount,
        int fieldCount,
        int sourceCount,
        string integrityHash) =>
        $"{rowCount.ToString(CultureInfo.InvariantCulture)} row(s), {fieldCount.ToString(CultureInfo.InvariantCulture)} field(s), and {sourceCount.ToString(CultureInfo.InvariantCulture)} source record(s) retained with SHA-256 {integrityHash}.";

    private static string BuildStructuredExportRoute(
        string exportId,
        DateTimeOffset asOf,
        string currency)
    {
        var query = string.Join(
            '&',
            $"asOf={Uri.EscapeDataString(asOf.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}",
            $"currency={Uri.EscapeDataString(currency)}");
        var route = UiApiRoutes.WorkstationReportingStructuredExport.Replace(
            "{exportId}",
            Uri.EscapeDataString(exportId),
            StringComparison.Ordinal);
        return $"{route}?{query}";
    }

    private static string NormalizeStructuredExportId(string exportId) =>
        exportId.Trim().ToLowerInvariant();

    private static IReadOnlyList<StructuredReportingExportColumnDto> GetStructuredExportColumns(string exportId) =>
        NormalizeStructuredExportId(exportId) switch
        {
            RegulatoryTrialBalanceExportId => RegulatoryReportPackAuditColumns,
            WarehouseLedgerFactsExportId => WarehouseReportPackFactColumns,
            InvestmentPortfolioCutsExportId => InvestmentPortfolioCutColumns,
            InvestmentTopNContributionAnalyticsExportId => InvestmentTopNContributionAnalyticsColumns,
            CrossFundConsolidationExportId => CrossFundConsolidationColumns,
            _ => throw new KeyNotFoundException($"Structured reporting export '{exportId}' is not available.")
        };

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildStructuredExportRows(
        string exportId,
        WorkstationReportingPayload reporting) =>
        NormalizeStructuredExportId(exportId) switch
        {
            RegulatoryTrialBalanceExportId => BuildRegulatoryReportPackAuditRows(reporting),
            WarehouseLedgerFactsExportId => BuildWarehouseReportPackFactRows(reporting),
            InvestmentPortfolioCutsExportId => BuildInvestmentPortfolioCutRows(reporting),
            InvestmentTopNContributionAnalyticsExportId => BuildInvestmentTopNContributionAnalyticsRows(reporting),
            CrossFundConsolidationExportId => BuildCrossFundConsolidationRows(reporting),
            _ => throw new KeyNotFoundException($"Structured reporting export '{exportId}' is not available.")
        };

    internal static IReadOnlyDictionary<string, string> BuildReportWriterPortfolioCutRow(PortfolioReportingCutDto cut) =>
        BuildReportWriterRow(
            ("dataset", "portfolio-cut"),
            ("cutId", cut.CutId),
            ("label", cut.Label),
            ("kind", cut.Kind.ToString()),
            ("scope", cut.Kind.ToString()),
            ("strategy", cut.Kind == PortfolioReportingCutKindDto.Strategy ? cut.Label : ""),
            ("security", ""),
            ("sector", cut.Kind.ToString()),
            ("classification", cut.Kind.ToString()),
            ("currency", cut.Currency),
            ("grossExposure", FormatStructuredDecimal(cut.GrossExposure)),
            ("netExposure", FormatStructuredDecimal(cut.NetExposure)),
            ("longMarketValue", FormatStructuredDecimal(cut.LongMarketValue)),
            ("shortMarketValue", FormatStructuredDecimal(cut.ShortMarketValue)),
            ("marketValue", FormatStructuredDecimal(cut.GrossExposure)),
            ("totalCash", FormatStructuredDecimal(cut.TotalCash)),
            ("pendingSettlement", FormatStructuredDecimal(cut.PendingSettlement)),
            ("realizedPnl", FormatStructuredDecimal(cut.RealizedPnl)),
            ("unrealizedPnl", FormatStructuredDecimal(cut.UnrealizedPnl)),
            ("totalPnl", FormatStructuredDecimal(cut.TotalPnl)),
            ("shadowNav", FormatStructuredDecimal(cut.ShadowNav)),
            ("shadowNavVariance", FormatStructuredDecimal(cut.ShadowNavVariance)),
            ("sourceCount", cut.SourceCount.ToString(CultureInfo.InvariantCulture)),
            ("asOfUtc", FormatStructuredDateTime(cut.AsOf) ?? ""),
            ("readinessSummary", cut.ShadowNavNote ?? ""),
            ("versionStamp", cut.VersionStamp),
            ("tags", string.Join("|", cut.Tags)));

    internal static IReadOnlyDictionary<string, string> BuildReportWriterAnalyticsRow(PortfolioReportingAnalyticsRowDto row) =>
        BuildReportWriterRow(
            ("dataset", "portfolio-analytics"),
            ("analyticsId", row.AnalyticsId),
            ("label", row.Label),
            ("kind", row.Kind.ToString()),
            ("scope", row.Scope.ToString()),
            ("strategy", row.Scope == PortfolioReportingAnalyticsScopeDto.Strategy ? row.Label : ""),
            ("security", row.Scope == PortfolioReportingAnalyticsScopeDto.Security ? row.Label : row.Symbol ?? ""),
            ("sector", row.Classification ?? row.Scope.ToString()),
            ("classification", row.Classification ?? ""),
            ("symbol", row.Symbol ?? ""),
            ("currency", row.Currency),
            ("rank", row.Rank.ToString(CultureInfo.InvariantCulture)),
            ("realizedPnl", FormatStructuredDecimal(row.RealizedPnl)),
            ("unrealizedPnl", FormatStructuredDecimal(row.UnrealizedPnl)),
            ("totalPnl", FormatStructuredDecimal(row.TotalPnl)),
            ("contributionPercent", FormatStructuredDecimal(row.ContributionPercent)),
            ("heatMapIntensity", FormatStructuredDecimal(row.HeatMapIntensity)),
            ("sourceCount", row.SourceCount.ToString(CultureInfo.InvariantCulture)),
            ("asOfUtc", FormatStructuredDateTime(row.AsOf) ?? ""),
            ("readinessSummary", row.ReadinessSummary),
            ("versionStamp", row.VersionStamp),
            ("tags", string.Join("|", row.Tags)));

    internal static IReadOnlyDictionary<string, string> BuildReportWriterCrossFundRow(CrossFundReportingConsolidationDto row) =>
        BuildReportWriterRow(
            ("dataset", "cross-fund-consolidation"),
            ("consolidationId", row.ConsolidationId),
            ("label", row.Label),
            ("kind", "CrossFundConsolidation"),
            ("scope", row.Scope.ToString()),
            ("sector", row.Scope.ToString()),
            ("classification", row.Scope.ToString()),
            ("currency", row.Currency),
            ("isReady", row.IsReady ? "true" : "false"),
            ("fundCount", row.FundCount.ToString(CultureInfo.InvariantCulture)),
            ("entityCount", row.EntityCount.ToString(CultureInfo.InvariantCulture)),
            ("accountCount", row.AccountCount.ToString(CultureInfo.InvariantCulture)),
            ("runCount", row.RunCount.ToString(CultureInfo.InvariantCulture)),
            ("grossExposure", FormatStructuredDecimal(row.GrossExposure)),
            ("netExposure", FormatStructuredDecimal(row.NetExposure)),
            ("marketValue", FormatStructuredDecimal(row.GrossExposure)),
            ("totalCash", FormatStructuredDecimal(row.TotalCash)),
            ("pendingSettlement", FormatStructuredDecimal(row.PendingSettlement)),
            ("totalPnl", FormatStructuredDecimal(row.TotalPnl)),
            ("shadowNav", FormatStructuredDecimal(row.ShadowNav)),
            ("shadowNavVariance", FormatStructuredDecimal(row.ShadowNavVariance)),
            ("sourceCount", row.SourceCount.ToString(CultureInfo.InvariantCulture)),
            ("readinessSummary", row.ReadinessSummary),
            ("versionStamp", row.VersionStamp));

    private static IReadOnlyDictionary<string, string> BuildReportWriterRow(params (string Key, string? Value)[] values)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            row[key] = value ?? "";
        }

        return row;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildRegulatoryReportPackAuditRows(
        WorkstationReportingPayload reporting) =>
        reporting.RecentRuns
            .OrderByDescending(static run => run.AsOfDate, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static run => run.RunId, StringComparer.OrdinalIgnoreCase)
            .Select(static run => BuildStructuredRow(
                ("runId", run.RunId),
                ("templateId", run.TemplateId),
                ("family", run.Family),
                ("status", run.Status),
                ("trigger", run.Trigger),
                ("asOfDate", run.AsOfDate),
                ("attemptCount", run.AttemptCount.ToString(CultureInfo.InvariantCulture)),
                ("sectionCount", run.SectionCount.ToString(CultureInfo.InvariantCulture)),
                ("lineageLinkedSections", run.LineageLinkedSections.ToString(CultureInfo.InvariantCulture)),
                ("failureReason", run.FailureReason)))
            .ToArray();

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildWarehouseReportPackFactRows(
        WorkstationReportingPayload reporting) =>
        reporting.ReportPackDistributions
            .OrderBy(static distribution => distribution.DistributionId, StringComparer.OrdinalIgnoreCase)
            .Select(static distribution => BuildStructuredRow(
                ("distributionId", distribution.DistributionId),
                ("recipient", distribution.Recipient),
                ("recipientRole", distribution.RecipientRole),
                ("channel", distribution.Channel),
                ("state", distribution.State),
                ("pendingItems", distribution.PendingItems.ToString(CultureInfo.InvariantCulture)),
                ("owner", distribution.Owner),
                ("dueAtUtc", FormatStructuredDateTime(distribution.DueAtUtc)),
                ("lastSentAtUtc", FormatStructuredDateTime(distribution.LastSentAtUtc))))
            .ToArray();

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildInvestmentPortfolioCutRows(
        WorkstationReportingPayload reporting) =>
        (reporting.PortfolioCuts ?? [])
            .OrderBy(static cut => cut.Kind)
            .ThenBy(static cut => cut.Label, StringComparer.OrdinalIgnoreCase)
            .Select(static cut => BuildStructuredRow(
                ("cutId", cut.CutId),
                ("label", cut.Label),
                ("kind", cut.Kind.ToString()),
                ("currency", cut.Currency),
                ("grossExposure", FormatStructuredDecimal(cut.GrossExposure)),
                ("netExposure", FormatStructuredDecimal(cut.NetExposure)),
                ("totalCash", FormatStructuredDecimal(cut.TotalCash)),
                ("pendingSettlement", FormatStructuredDecimal(cut.PendingSettlement)),
                ("totalPnl", FormatStructuredDecimal(cut.TotalPnl)),
                ("shadowNav", FormatStructuredDecimal(cut.ShadowNav)),
                ("shadowNavVariance", FormatStructuredDecimal(cut.ShadowNavVariance)),
                ("sourceCount", cut.SourceCount.ToString(CultureInfo.InvariantCulture)),
                ("versionStamp", cut.VersionStamp)))
            .ToArray();

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildInvestmentTopNContributionAnalyticsRows(
        WorkstationReportingPayload reporting) =>
        (reporting.AnalyticsRows ?? [])
            .OrderBy(static row => row.Kind)
            .ThenBy(static row => row.Scope)
            .ThenBy(static row => row.Rank)
            .ThenBy(static row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Select(static row => BuildStructuredRow(
                ("analyticsId", row.AnalyticsId),
                ("kind", row.Kind.ToString()),
                ("scope", row.Scope.ToString()),
                ("rank", row.Rank.ToString(CultureInfo.InvariantCulture)),
                ("label", row.Label),
                ("symbol", row.Symbol),
                ("classification", row.Classification),
                ("currency", row.Currency),
                ("realizedPnl", FormatStructuredDecimal(row.RealizedPnl)),
                ("unrealizedPnl", FormatStructuredDecimal(row.UnrealizedPnl)),
                ("totalPnl", FormatStructuredDecimal(row.TotalPnl)),
                ("contributionPercent", FormatStructuredDecimal(row.ContributionPercent)),
                ("heatMapIntensity", FormatStructuredDecimal(row.HeatMapIntensity)),
                ("sourceCount", row.SourceCount.ToString(CultureInfo.InvariantCulture)),
                ("asOfUtc", FormatStructuredDateTime(row.AsOf)),
                ("readinessSummary", row.ReadinessSummary),
                ("versionStamp", row.VersionStamp),
                ("tags", string.Join("|", row.Tags))))
            .ToArray();

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildCrossFundConsolidationRows(
        WorkstationReportingPayload reporting) =>
        (reporting.CrossFundConsolidations ?? [])
            .OrderBy(static row => row.Scope)
            .ThenBy(static row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Select(static row => BuildStructuredRow(
                ("consolidationId", row.ConsolidationId),
                ("label", row.Label),
                ("scope", row.Scope.ToString()),
                ("currency", row.Currency),
                ("isReady", row.IsReady ? "true" : "false"),
                ("fundCount", row.FundCount.ToString(CultureInfo.InvariantCulture)),
                ("entityCount", row.EntityCount.ToString(CultureInfo.InvariantCulture)),
                ("accountCount", row.AccountCount.ToString(CultureInfo.InvariantCulture)),
                ("runCount", row.RunCount.ToString(CultureInfo.InvariantCulture)),
                ("grossExposure", FormatStructuredDecimal(row.GrossExposure)),
                ("netExposure", FormatStructuredDecimal(row.NetExposure)),
                ("totalCash", FormatStructuredDecimal(row.TotalCash)),
                ("pendingSettlement", FormatStructuredDecimal(row.PendingSettlement)),
                ("totalPnl", FormatStructuredDecimal(row.TotalPnl)),
                ("shadowNav", FormatStructuredDecimal(row.ShadowNav)),
                ("shadowNavVariance", FormatStructuredDecimal(row.ShadowNavVariance)),
                ("sourceCount", row.SourceCount.ToString(CultureInfo.InvariantCulture)),
                ("readinessSummary", row.ReadinessSummary),
                ("versionStamp", row.VersionStamp)))
            .ToArray();

    private static IReadOnlyDictionary<string, string?> BuildStructuredRow(params (string Key, string? Value)[] values)
    {
        var row = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            row[key] = value;
        }

        return row;
    }

    private static IReadOnlyList<StructuredReportingExportDataDictionaryFieldDto> BuildStructuredExportDataDictionary(
        IReadOnlyList<StructuredReportingExportColumnDto> columns) =>
        columns
            .Select((column, index) => new StructuredReportingExportDataDictionaryFieldDto(
                column.Name,
                column.DataType,
                column.Description,
                index + 1))
            .ToArray();

    private static IReadOnlyList<StructuredReportingExportValidationCheckDto> BuildStructuredExportValidationChecks(
        StructuredReportingExportDto descriptor,
        IReadOnlyList<StructuredReportingExportColumnDto> columns,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows) =>
    [
        new StructuredReportingExportValidationCheckDto(
            "readiness",
            descriptor.IsReady ? "Passed" : "Warning",
            descriptor.ValidationSummary ?? (descriptor.IsReady
                ? "Structured export is ready."
                : "Structured export readiness is blocked.")),
        new StructuredReportingExportValidationCheckDto(
            "column-count",
            descriptor.FieldCount == columns.Count ? "Passed" : "Failed",
            $"Descriptor declares {descriptor.FieldCount.ToString(CultureInfo.InvariantCulture)} field(s); payload contains {columns.Count.ToString(CultureInfo.InvariantCulture)} column(s)."),
        new StructuredReportingExportValidationCheckDto(
            "row-count",
            descriptor.RowCount == rows.Count ? "Passed" : descriptor.IsReady ? "Failed" : "Warning",
            $"Descriptor declares {descriptor.RowCount.ToString(CultureInfo.InvariantCulture)} row(s); payload contains {rows.Count.ToString(CultureInfo.InvariantCulture)} row(s)."),
        new StructuredReportingExportValidationCheckDto(
            "source-count",
            descriptor.SourceCount > 0 ? "Passed" : "Warning",
            descriptor.SourceCount > 0
                ? $"Structured export is backed by {descriptor.SourceCount.ToString(CultureInfo.InvariantCulture)} source record(s)."
                : "No source records are linked to this structured export.")
    ];

    private static IReadOnlyList<StructuredReportingExportRowLineageDto> BuildStructuredExportRowLineage(
        string exportId,
        IReadOnlyList<StructuredReportingExportColumnDto> columns,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows) =>
        rows
            .Select((row, index) => new StructuredReportingExportRowLineageDto(
                index + 1,
                ResolveStructuredExportRowKey(row, index),
                ComputeStructuredExportRowHash(exportId, columns, row)))
            .ToArray();

    private static string ResolveStructuredExportRowKey(
        IReadOnlyDictionary<string, string?> row,
        int index)
    {
        foreach (var key in new[] { "cutId", "analyticsId", "consolidationId", "accountName", "scope", "label" })
        {
            if (row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return $"row-{(index + 1).ToString(CultureInfo.InvariantCulture)}";
    }

    private static string ComputeStructuredExportRowHash(
        string exportId,
        IReadOnlyList<StructuredReportingExportColumnDto> columns,
        IReadOnlyDictionary<string, string?> row)
    {
        var builder = new StringBuilder();
        builder.Append(exportId);
        foreach (var column in columns)
        {
            builder.Append('|');
            builder.Append(column.Name);
            builder.Append('=');
            builder.Append(row.TryGetValue(column.Name, out var value) ? value : string.Empty);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static IReadOnlyList<string> BuildPortfolioCutTags(
        IReadOnlyList<ReportPackWorkflowRecordDto> records,
        IReadOnlyList<PortfolioLineSource> sources) =>
        records.Select(static record => $"fund:{record.FundProfileId}")
            .Concat(records.Select(static record => $"account:{record.FundAccountId}"))
            .Concat(records.Select(static record => $"template:{record.TemplateId.Name}"))
            .Concat(sources.Select(static source => $"metric:{source.Metric}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static decimal SumSources(IReadOnlyList<PortfolioLineSource> sources, PortfolioLineMetric metric) =>
        sources.Where(source => source.Metric == metric).Sum(static source => source.Value);

    private static PortfolioLineSource? TryClassifyPortfolioLine(ReportPackLineProvenanceDto line)
    {
        if (string.IsNullOrWhiteSpace(line.ReportValue) ||
            !decimal.TryParse(line.ReportValue.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var key = line.LineKey.ToLowerInvariant();
        var metric = key switch
        {
            _ when key.Contains("gross", StringComparison.Ordinal) && key.Contains("exposure", StringComparison.Ordinal) => PortfolioLineMetric.GrossExposure,
            _ when key.Contains("net", StringComparison.Ordinal) && key.Contains("exposure", StringComparison.Ordinal) => PortfolioLineMetric.NetExposure,
            _ when key.Contains("long", StringComparison.Ordinal) && (key.Contains("market", StringComparison.Ordinal) || key.Contains("exposure", StringComparison.Ordinal)) => PortfolioLineMetric.LongMarketValue,
            _ when key.Contains("short", StringComparison.Ordinal) && (key.Contains("market", StringComparison.Ordinal) || key.Contains("exposure", StringComparison.Ordinal)) => PortfolioLineMetric.ShortMarketValue,
            _ when key.Contains("pending", StringComparison.Ordinal) && key.Contains("settlement", StringComparison.Ordinal) => PortfolioLineMetric.PendingSettlement,
            _ when key.Contains("cash", StringComparison.Ordinal) => PortfolioLineMetric.Cash,
            _ when key.Contains("unrealized", StringComparison.Ordinal) && key.Contains("pnl", StringComparison.Ordinal) => PortfolioLineMetric.UnrealizedPnl,
            _ when key.Contains("realized", StringComparison.Ordinal) && key.Contains("pnl", StringComparison.Ordinal) => PortfolioLineMetric.RealizedPnl,
            _ when key.Contains("total", StringComparison.Ordinal) && key.Contains("pnl", StringComparison.Ordinal) => PortfolioLineMetric.TotalPnl,
            _ when key.Contains("shadow", StringComparison.Ordinal) && key.Contains("nav", StringComparison.Ordinal) => PortfolioLineMetric.ShadowNav,
            _ when key.Contains("reported", StringComparison.Ordinal) && key.Contains("nav", StringComparison.Ordinal) => PortfolioLineMetric.ReportedNav,
            _ => (PortfolioLineMetric?)null
        };

        return metric is null ? null : new PortfolioLineSource(metric.Value, value, line);
    }

    private static string FormatCurrency(decimal value, string currency) =>
        $"{currency} {value:N2}";

    private static string FormatStructuredDecimal(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string? FormatStructuredDateTime(DateTimeOffset? value) =>
        value?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private enum PortfolioLineMetric
    {
        GrossExposure,
        NetExposure,
        LongMarketValue,
        ShortMarketValue,
        Cash,
        PendingSettlement,
        RealizedPnl,
        UnrealizedPnl,
        TotalPnl,
        ShadowNav,
        ReportedNav
    }

    private sealed record PortfolioLineSource(
        PortfolioLineMetric Metric,
        decimal Value,
        ReportPackLineProvenanceDto Line);

    private WorkstationReportingTemplatePayload[] BuildTemplates(ReportAccessQueryContext? accessContext)
    {
        var reportWriterSourceFields = new ReportWriterDatasetSourceService(_workflowService)
            .BuildFieldCatalog(accessContext);
        if (_templateRegistry is not null)
        {
            return _templateRegistry
                .List()
                .Where(record => IsAccessible(record.Definition.AccessPolicy, accessContext))
                .Select(record => ProjectTemplateRecord(record, accessContext, reportWriterSourceFields))
                .OrderBy(static template => template.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static template => template.Version, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return _templateCatalog
            .ListTemplates()
            .Select(template => ProjectCatalogTemplate(template, accessContext))
            .OrderBy(static template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private int CountTemplates() =>
        _templateRegistry?.List().Count()
        ?? _templateCatalog.ListTemplates().Count();

    private static WorkstationReportingTemplatePayload[] ApplyStarterKitTemplateFilter(
        WorkstationReportingTemplatePayload[] templates,
        ReportingStarterKitStateDto? starterKitState)
    {
        if (starterKitState?.IsProvisioned != true || starterKitState.EnabledTemplateIds.Count == 0)
        {
            return templates;
        }

        var enabled = starterKitState.EnabledTemplateIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return templates
            .Where(template => enabled.Contains(template.TemplateId))
            .ToArray();
    }

    private static WorkstationReportAccessAuditSummaryDto BuildAccessAuditSummary(
        ReportAccessQueryContext? accessContext,
        int totalTemplateCount,
        int visibleTemplateCount,
        int totalReportPackCount,
        int visibleReportPackCount,
        int totalScheduleCount,
        int visibleScheduleCount,
        int totalDeliveryAttemptCount,
        int visibleDeliveryAttemptCount,
        int visibleStructuredExportCount,
        int hiddenStructuredExportCount)
    {
        var hiddenTemplateCount = CalculateHiddenCount(totalTemplateCount, visibleTemplateCount);
        var hiddenReportPackCount = CalculateHiddenCount(totalReportPackCount, visibleReportPackCount);
        var hiddenScheduleCount = CalculateHiddenCount(totalScheduleCount, visibleScheduleCount);
        var hiddenDeliveryAttemptCount = CalculateHiddenCount(totalDeliveryAttemptCount, visibleDeliveryAttemptCount);
        var hiddenTotal = hiddenTemplateCount
            + hiddenReportPackCount
            + hiddenScheduleCount
            + hiddenDeliveryAttemptCount
            + hiddenStructuredExportCount;
        var scope = accessContext is null
            ? "Unscoped"
            : accessContext.HasGlobalOverride
                ? "AdministratorOverride"
                : "CallerScoped";
        var summary = hiddenTotal == 0
            ? "Reporting access policy did not hide any reporting objects for this request."
            : $"Reporting access policy hid {hiddenTotal} object(s) outside the caller's user, group, or company scope.";

        return new WorkstationReportAccessAuditSummaryDto(
            scope,
            summary,
            BuildPrincipalScopes(accessContext),
            visibleTemplateCount,
            hiddenTemplateCount,
            visibleReportPackCount,
            hiddenReportPackCount,
            visibleScheduleCount,
            hiddenScheduleCount,
            visibleDeliveryAttemptCount,
            hiddenDeliveryAttemptCount,
            visibleStructuredExportCount,
            hiddenStructuredExportCount,
            BuildAccessDenialReasons(
                hiddenTemplateCount,
                hiddenReportPackCount,
                hiddenScheduleCount,
                hiddenDeliveryAttemptCount,
                hiddenStructuredExportCount));
    }

    private static int CalculateHiddenCount(int totalCount, int visibleCount) =>
        Math.Max(0, totalCount - visibleCount);

    private static IReadOnlyList<string> BuildPrincipalScopes(ReportAccessQueryContext? accessContext)
    {
        if (accessContext is null)
        {
            return ["scope:unfiltered"];
        }

        var scopes = new List<string>();
        if (!string.IsNullOrWhiteSpace(accessContext.ActorPrincipalId))
        {
            scopes.Add($"user:{accessContext.ActorPrincipalId.Trim()}");
        }

        foreach (var groupId in accessContext.GroupPrincipalIds ?? [])
        {
            if (!string.IsNullOrWhiteSpace(groupId))
            {
                scopes.Add($"group:{groupId.Trim()}");
            }
        }

        if (!string.IsNullOrWhiteSpace(accessContext.CompanyId))
        {
            scopes.Add($"company:{accessContext.CompanyId.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(accessContext.TenantId))
        {
            scopes.Add($"tenant:{accessContext.TenantId.Trim()}");
        }

        if (accessContext.HasGlobalOverride)
        {
            scopes.Add("override:reporting-admin");
        }

        return scopes.Count == 0 ? ["scope:anonymous"] : scopes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> BuildAccessDenialReasons(
        int hiddenTemplateCount,
        int hiddenReportPackCount,
        int hiddenScheduleCount,
        int hiddenDeliveryAttemptCount,
        int hiddenStructuredExportCount)
    {
        var reasons = new List<string>();
        if (hiddenTemplateCount > 0)
        {
            reasons.Add("Some report templates are private or restricted to other users, groups, or companies.");
        }

        if (hiddenReportPackCount > 0)
        {
            reasons.Add("Some report packs are private or restricted to other users, groups, or companies.");
        }

        if (hiddenScheduleCount > 0)
        {
            reasons.Add("Some schedules are hidden because their templates are outside the caller's report access scope.");
        }

        if (hiddenDeliveryAttemptCount > 0)
        {
            reasons.Add("Some delivery attempts are hidden because their report pack or template is outside the caller's report access scope.");
        }

        if (hiddenStructuredExportCount > 0)
        {
            reasons.Add("Some structured exports are unavailable because the caller has no visible source report packs or deliveries.");
        }

        return reasons.ToArray();
    }

    private static WorkstationReportingTemplatePayload ProjectCatalogTemplate(
        ReportingTemplateMetadata template,
        ReportAccessQueryContext? accessContext)
    {
        var accessPolicy = ReportAccessPolicyEvaluator.Normalize(null);
        var accessEvaluation = EvaluateForProjection(accessPolicy, accessContext);
        return new WorkstationReportingTemplatePayload(
            template.TemplateId,
            template.Family.ToString(),
            template.Name,
            template.Version,
            template.Sections.ToArray(),
            LifecycleStatus: ReportTemplateLifecycleStatusDto.Approved.ToString(),
            IsBuiltIn: true,
            IsLatestApproved: true,
            ApprovalSummary: $"Built-in approved template for {template.Family}.",
            AuthoringRoute: $"/api/fund-structure/reporting/templates/{template.TemplateId}/versions/1",
            ReportWriterGrids: [],
            AccessMode: accessPolicy.Mode.ToString(),
            AccessSummary: ReportAccessPolicyEvaluator.BuildSummary(accessPolicy),
            IsAccessible: accessEvaluation.IsAccessible);
    }

    private static WorkstationReportingTemplatePayload ProjectTemplateRecord(
        ReportTemplateGovernanceRecordDto record,
        ReportAccessQueryContext? accessContext,
        IReadOnlyList<WorkstationReportWriterFieldPayload> reportWriterSourceFields)
    {
        var definition = record.Definition;
        var accessPolicy = ReportAccessPolicyEvaluator.Normalize(definition.AccessPolicy);
        var accessEvaluation = EvaluateForProjection(accessPolicy, accessContext);
        return new WorkstationReportingTemplatePayload(
            definition.TemplateId.Name,
            record.Family,
            definition.DisplayName,
            definition.TemplateId.Version.ToString(),
            definition.Sections?.ToArray() ?? [],
            LifecycleStatus: record.Status.ToString(),
            IsBuiltIn: record.IsBuiltIn,
            IsLatestApproved: record.IsLatestApproved,
            ApprovalSummary: BuildTemplateApprovalSummary(record),
            AuthoringRoute: $"/api/fund-structure/reporting/templates/{Uri.EscapeDataString(definition.TemplateId.Name)}/versions/{definition.TemplateId.Version}",
            ReportWriterGrids: ProjectReportWriterGrids(definition, reportWriterSourceFields),
            AccessMode: accessPolicy.Mode.ToString(),
            AccessSummary: ReportAccessPolicyEvaluator.BuildSummary(accessPolicy),
            IsAccessible: accessEvaluation.IsAccessible,
            CreatedBy: record.CreatedBy,
            CreatedAt: record.CreatedAt,
            UpdatedBy: record.UpdatedBy,
            UpdatedAt: record.UpdatedAt,
            SubmittedBy: record.SubmittedBy,
            SubmittedAt: record.SubmittedAt,
            ApprovedBy: record.ApprovedBy,
            ApprovedAt: record.ApprovedAt,
            RejectedBy: record.RejectedBy,
            RejectedAt: record.RejectedAt,
            DecisionRationale: record.DecisionRationale,
            ApprovalReference: record.ApprovalReference,
            BasedOnTemplateId: record.BasedOnTemplateId,
            AuditTrail: record.AuditTrail,
            ValidationIssues: record.ValidationIssues);
    }

    private static ReportAccessEvaluationDto EvaluateForProjection(
        ReportAccessPolicyDto accessPolicy,
        ReportAccessQueryContext? accessContext) =>
        accessContext is null
            ? new ReportAccessEvaluationDto(true, ReportAccessPolicyEvaluator.BuildSummary(accessPolicy), [])
            : ReportAccessPolicyEvaluator.Evaluate(accessPolicy, accessContext);

    private static IReadOnlyList<WorkstationReportWriterGridPayload> ProjectReportWriterGrids(
        ReportTemplateDefinitionDto definition,
        IReadOnlyList<WorkstationReportWriterFieldPayload> sourceFields) =>
        definition.Grids?
            .Select(grid => new WorkstationReportWriterGridPayload(
                grid.GridId,
                grid.Title,
                grid.Kind.ToString(),
                (grid.RowFields?.Count ?? 0) + (grid.ColumnFields?.Count ?? 0),
                grid.Metrics?.Count ?? 0,
                grid.Formulas?.Count ?? 0,
                RowFields: grid.RowFields?.ToArray() ?? [],
                ColumnFields: grid.ColumnFields?.ToArray() ?? [],
                Metrics: grid.Metrics?
                    .Select(static metric => new WorkstationReportWriterMetricPayload(
                        metric.Name,
                        metric.SourceField,
                        metric.Function.ToString(),
                        metric.Label))
                    .ToArray() ?? [],
                Formulas: grid.Formulas?
                    .Select(static formula => new WorkstationReportWriterFormulaPayload(
                        formula.Name,
                        formula.Expression,
                        formula.Label))
                    .ToArray() ?? [],
                TopN: grid.TopN,
                SortBy: grid.SortBy,
                SortDescending: grid.SortDescending,
                Filters: grid.Filters?
                    .Select(static filter => new WorkstationReportWriterFilterPayload(
                        filter.Field,
                        filter.Operator.ToString(),
                        filter.Value,
                        filter.Label))
                    .ToArray() ?? [],
                SourceFields: sourceFields))
            .ToArray() ?? [];

    private static string BuildTemplateApprovalSummary(ReportTemplateGovernanceRecordDto record) =>
        record.Status switch
        {
            ReportTemplateLifecycleStatusDto.Approved when !string.IsNullOrWhiteSpace(record.ApprovalReference) =>
                $"Approved by {record.ApprovedBy ?? record.UpdatedBy} ({record.ApprovalReference}).",
            ReportTemplateLifecycleStatusDto.Approved =>
                $"Approved by {record.ApprovedBy ?? record.UpdatedBy}.",
            ReportTemplateLifecycleStatusDto.InReview =>
                $"In review after submission by {record.SubmittedBy ?? record.UpdatedBy}.",
            ReportTemplateLifecycleStatusDto.Rejected =>
                $"Rejected by {record.RejectedBy ?? record.UpdatedBy}.",
            ReportTemplateLifecycleStatusDto.Draft =>
                $"Draft by {record.CreatedBy}.",
            _ => record.DecisionRationale ?? record.Status.ToString()
        };

    private UnifiedReportingRun[] BuildRecentRuns(
        int limit,
        IReadOnlyDictionary<string, string> familyByTemplate,
        IReadOnlyDictionary<string, WorkstationReportingTemplatePayload> templatesById,
        IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords,
        ReportAccessQueryContext? accessContext)
    {
        IReadOnlyList<ReportingRunSnapshot> genericSnapshots = _runStore?.ListRuns(200) ?? [];
        if (accessContext is not null)
        {
            genericSnapshots = genericSnapshots
                .Where(snapshot => ReportAccessPolicyEvaluator.Evaluate(snapshot.Manifest, accessContext).IsAccessible)
                .ToArray();
        }

        var genericRuns = ProjectGenericRuns(genericSnapshots, familyByTemplate, templatesById);
        var workflowRuns = workflowRecords
            .Take(limit)
            .Select(ProjectWorkflowRun);

        return genericRuns
            .Concat(workflowRuns)
            .OrderByDescending(static run => run.UpdatedAtUtc)
            .ThenBy(static run => run.Payload.RunId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public static WorkstationReportPackDistributionPayload[] BuildDistributionRecords(
        IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords,
        IReadOnlyList<ReportPackDeliveryAttemptDto>? deliveryAttempts = null)
    {
        var records = workflowRecords
            .OrderByDescending(static record => record.UpdatedAt)
            .ThenBy(static record => record.ReportId)
            .ToArray();
        var latestActionAt = records.Select(static record => (DateTimeOffset?)record.UpdatedAt).FirstOrDefault();
        var latestPublishedAt = records
            .Where(static record => record.Publication is not null)
            .Select(static record => (DateTimeOffset?)record.Publication!.SignedOffAt)
            .OrderByDescending(static value => value)
            .FirstOrDefault();
        var blockedCount = records.Count(static record => record.State == ReportPackWorkflowStateDto.Rejected);
        var pendingPublicationCount = records.Count(static record => record.State == ReportPackWorkflowStateDto.Approved);
        var pendingApprovalCount = records.Count(static record =>
            record.State is ReportPackWorkflowStateDto.Draft
                or ReportPackWorkflowStateDto.Validated
                or ReportPackWorkflowStateDto.InReview
                or ReportPackWorkflowStateDto.PendingApproval);
        var pendingDeliveryCount = records.Count(static record =>
            record.Publication is not null
            && record.State is ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated);

        return DistributionPolicies
            .Select(policy => BuildDistribution(
                policy,
                blockedCount,
                pendingApprovalCount,
                pendingPublicationCount,
                pendingDeliveryCount,
                latestActionAt,
                latestPublishedAt,
                deliveryAttempts ?? []))
            .ToArray();
    }

    public static ReportingScheduleDeliveryPlanDto[] BuildScheduleDeliveryPlans(
        IReadOnlyList<ReportingScheduleRecordDto> schedules,
        IReadOnlyList<ReportPackDeliveryAttemptDto>? deliveryAttempts = null)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        var attempts = deliveryAttempts ?? [];
        return schedules
            .OrderBy(static schedule => schedule.DueAtUtc)
            .ThenBy(static schedule => schedule.ScheduleId, StringComparer.OrdinalIgnoreCase)
            .SelectMany(schedule => BuildScheduleDeliveryPlans(schedule, attempts))
            .ToArray();
    }

    private static IReadOnlyList<WorkstationReportingDailyWorkItemDto> BuildDailyWorkItems(
        IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords,
        IReadOnlyList<WorkstationReportingRunPayload> runs,
        IReadOnlyList<ReportingScheduleRecordDto> schedules,
        IReadOnlyList<ReportingScheduleDeliveryPlanDto> scheduleDeliveryPlans,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts)
    {
        var items = new List<WorkstationReportingDailyWorkItemDto>();
        var nowUtc = DateTimeOffset.UtcNow;
        var dueWindowUtc = nowUtc.AddDays(1);

        foreach (var schedule in schedules
            .Where(static schedule => schedule.State == ReportingScheduleStateDto.Active)
            .Where(schedule => schedule.DueAtUtc <= dueWindowUtc))
        {
            var isOverdue = schedule.DueAtUtc <= nowUtc;
            items.Add(new WorkstationReportingDailyWorkItemDto(
                WorkItemId: $"due-package:{schedule.ScheduleId}",
                Kind: "due-package",
                Title: $"Due package: {schedule.TemplateId}",
                StatusLabel: isOverdue ? "Due now" : "Due within 24h",
                Detail: $"Next as-of {schedule.NextAsOfDate:yyyy-MM-dd}; {CountUnit(schedule.DeliveryTargets?.Count ?? 0, "delivery target")}.",
                Tone: isOverdue ? "warning" : "muted",
                Owner: schedule.RequestedBy,
                DueAtUtc: schedule.DueAtUtc,
                PrimaryActionLabel: "Review schedule",
                PrimaryActionHref: $"/reporting?schedule={Uri.EscapeDataString(schedule.ScheduleId)}",
                Context: schedule.DeliveryTargets?
                    .Select(static target => target.DistributionId)
                    .Where(static distributionId => !string.IsNullOrWhiteSpace(distributionId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()));
        }

        foreach (var record in workflowRecords)
        {
            var reportId = record.ReportId.ToString("D");
            var reportLabel = BuildReportPackLabel(record);
            var reportHref = $"/reporting?reportPack={reportId}";
            switch (record.State)
            {
                case ReportPackWorkflowStateDto.InReview:
                case ReportPackWorkflowStateDto.PendingApproval:
                    items.Add(new WorkstationReportingDailyWorkItemDto(
                        WorkItemId: $"approval-needed:{reportId}",
                        Kind: "approval-needed",
                        Title: $"Approval needed: {reportLabel}",
                        StatusLabel: record.State == ReportPackWorkflowStateDto.PendingApproval ? "Pending approval" : "In review",
                        Detail: $"{record.FundAccountId} / {record.Period}; version {record.Version.ToString(CultureInfo.InvariantCulture)}.",
                        Tone: "warning",
                        Owner: record.CreatedBy,
                        DueAtUtc: null,
                        PrimaryActionLabel: "Review approval",
                        PrimaryActionHref: reportHref,
                        EvidenceGaps: BuildWorkflowEvidenceGaps(record)));
                    break;
                case ReportPackWorkflowStateDto.Rejected:
                    items.Add(new WorkstationReportingDailyWorkItemDto(
                        WorkItemId: $"blocked-package:{reportId}",
                        Kind: "blocked-package",
                        Title: $"Blocked package: {reportLabel}",
                        StatusLabel: "Rejected",
                        Detail: record.Rejection?.Reason ?? "Reviewer returned the package before publication.",
                        Tone: "danger",
                        Owner: record.Rejection?.Actor ?? record.CreatedBy,
                        DueAtUtc: record.Rejection?.RejectedAt,
                        PrimaryActionLabel: "Review rejection",
                        PrimaryActionHref: reportHref,
                        EvidenceGaps: BuildWorkflowEvidenceGaps(record),
                        Context: BuildWorkflowContext(record)));
                    break;
                case ReportPackWorkflowStateDto.Restated:
                    items.Add(new WorkstationReportingDailyWorkItemDto(
                        WorkItemId: $"restatement:{reportId}",
                        Kind: "restatement",
                        Title: $"Restatement: {reportLabel}",
                        StatusLabel: "Restated",
                        Detail: BuildRestatementDetail(record),
                        Tone: "warning",
                        Owner: record.Restatement?.Approver ?? record.CreatedBy,
                        DueAtUtc: record.UpdatedAt,
                        PrimaryActionLabel: "Compare restatement",
                        PrimaryActionHref: reportHref,
                        EvidenceGaps: BuildRestatementEvidenceGaps(record),
                        Context: BuildWorkflowContext(record)));
                    break;
            }
        }

        foreach (var attempt in deliveryAttempts.Where(static attempt =>
            attempt.State is ReportPackDeliveryStateDto.Failed or ReportPackDeliveryStateDto.Blocked))
        {
            items.Add(new WorkstationReportingDailyWorkItemDto(
                WorkItemId: $"delivery-failure:{attempt.AttemptId:D}",
                Kind: "delivery-failure",
                Title: $"Delivery failure: {attempt.Recipient}",
                StatusLabel: attempt.State.ToString(),
                Detail: attempt.FailureReason ?? attempt.Note ?? $"Delivery reference {attempt.DeliveryReference} requires review.",
                Tone: "danger",
                Owner: attempt.Actor,
                DueAtUtc: attempt.AttemptedAtUtc,
                PrimaryActionLabel: "Review delivery",
                PrimaryActionHref: $"/reporting?deliveryAttempt={attempt.AttemptId:D}",
                EvidenceGaps: attempt.EvidenceLinks is { Count: > 0 }
                    ? []
                    : ["Delivery failure has no retained evidence link."],
                Context:
                [
                    attempt.DistributionId,
                    attempt.Channel,
                    attempt.RecipientRole
                ]));
        }

        foreach (var run in runs.Where(static run => string.Equals(run.Status, "Failed", StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(new WorkstationReportingDailyWorkItemDto(
                WorkItemId: $"blocked-run:{run.RunId}",
                Kind: "blocked-package",
                Title: $"Blocked run: {run.TemplateId}",
                StatusLabel: "Failed",
                Detail: run.FailureReason ?? $"Run {run.RunId} failed before report-package readiness.",
                Tone: "danger",
                Owner: "Reporting",
                DueAtUtc: null,
                PrimaryActionLabel: "Open run",
                PrimaryActionHref: ResolveRunPrimaryHref(run),
                EvidenceGaps: BuildRunEvidenceGaps(run),
                Context: [run.Family, run.AsOfDate]));
        }

        foreach (var run in runs.Where(static run => run.SectionCount > 0 && run.LineageLinkedSections < run.SectionCount))
        {
            items.Add(new WorkstationReportingDailyWorkItemDto(
                WorkItemId: $"evidence-gap:{run.RunId}",
                Kind: "evidence-gap",
                Title: $"Evidence gap: {run.TemplateId}",
                StatusLabel: "Lineage incomplete",
                Detail: $"{(run.SectionCount - run.LineageLinkedSections).ToString(CultureInfo.InvariantCulture)} of {run.SectionCount.ToString(CultureInfo.InvariantCulture)} section(s) are missing retained lineage.",
                Tone: "warning",
                Owner: "Reporting",
                DueAtUtc: null,
                PrimaryActionLabel: "Open evidence",
                PrimaryActionHref: ResolveRunPrimaryHref(run),
                EvidenceGaps: BuildRunEvidenceGaps(run),
                Context: [run.Family, run.AsOfDate]));
        }

        foreach (var plan in scheduleDeliveryPlans.Where(static plan => !plan.IsReady))
        {
            items.Add(new WorkstationReportingDailyWorkItemDto(
                WorkItemId: $"readiness-warning:{plan.PlanId}",
                Kind: "evidence-gap",
                Title: $"Readiness warning: {plan.TemplateId}",
                StatusLabel: "Delivery not ready",
                Detail: plan.ReadinessSummary,
                Tone: plan.DueAtUtc <= nowUtc ? "danger" : "warning",
                Owner: plan.Owner,
                DueAtUtc: plan.DueAtUtc,
                PrimaryActionLabel: "Review readiness",
                PrimaryActionHref: $"/reporting?schedule={Uri.EscapeDataString(plan.ScheduleId)}",
                EvidenceGaps: BuildReadinessEvidenceGaps(plan),
                Context:
                [
                    plan.DistributionId,
                    plan.Recipient,
                    plan.Channel
                ]));
        }

        return items
            .GroupBy(static item => item.WorkItemId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static item => DailyWorkToneRank(item.Tone))
            .ThenBy(static item => item.DueAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
    }

    private static string BuildReportPackLabel(ReportPackWorkflowRecordDto record) =>
        $"{record.TemplateId.Name} v{record.TemplateId.Version.ToString(CultureInfo.InvariantCulture)}";

    private static string BuildRestatementDetail(ReportPackWorkflowRecordDto record)
    {
        var changedLineCount = record.Restatement?.ChangedLines.Count ?? 0;
        var reason = string.IsNullOrWhiteSpace(record.Restatement?.ReasonCode)
            ? "Restatement retained"
            : record.Restatement!.ReasonCode;
        return $"{reason}; {CountUnit(changedLineCount, "changed line")}.";
    }

    private static IReadOnlyList<string> BuildWorkflowEvidenceGaps(ReportPackWorkflowRecordDto record)
    {
        var gaps = new List<string>();
        if (record.LineProvenance is null || record.LineProvenance.Count == 0)
        {
            gaps.Add("Report pack has no retained line-provenance rows.");
        }
        else
        {
            var missingEvidenceCount = record.LineProvenance.Count(static line => string.IsNullOrWhiteSpace(line.EvidenceId));
            if (missingEvidenceCount > 0)
            {
                gaps.Add($"{missingEvidenceCount.ToString(CultureInfo.InvariantCulture)} line-provenance row(s) are missing evidence IDs.");
            }
        }

        if (record.Publication is null && record.State is ReportPackWorkflowStateDto.Approved or ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated)
        {
            gaps.Add("Publication manifest is not retained.");
        }

        return gaps;
    }

    private static IReadOnlyList<string> BuildRestatementEvidenceGaps(ReportPackWorkflowRecordDto record)
    {
        var gaps = BuildWorkflowEvidenceGaps(record).ToList();
        var changedLines = record.Restatement?.ChangedLines ?? [];
        var changedLinesWithoutEvidence = changedLines.Count(static line => line.EvidenceLinks is null || line.EvidenceLinks.Count == 0);
        if (changedLinesWithoutEvidence > 0)
        {
            gaps.Add($"{changedLinesWithoutEvidence.ToString(CultureInfo.InvariantCulture)} restatement line(s) are missing evidence links.");
        }

        return gaps;
    }

    private static IReadOnlyList<string> BuildRunEvidenceGaps(WorkstationReportingRunPayload run)
    {
        var gaps = new List<string>();
        if (run.SectionCount > 0 && run.LineageLinkedSections < run.SectionCount)
        {
            gaps.Add($"{(run.SectionCount - run.LineageLinkedSections).ToString(CultureInfo.InvariantCulture)} of {run.SectionCount.ToString(CultureInfo.InvariantCulture)} section(s) are missing retained lineage.");
        }

        if (!string.IsNullOrWhiteSpace(run.FailureReason))
        {
            gaps.Add(run.FailureReason);
        }

        return gaps;
    }

    private static IReadOnlyList<string> BuildReadinessEvidenceGaps(ReportingScheduleDeliveryPlanDto plan)
    {
        var blockers = plan.ReadinessBlockers?
            .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .Select(static blocker => blocker.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (blockers is { Length: > 0 })
        {
            return blockers;
        }

        return string.IsNullOrWhiteSpace(plan.ReadinessSummary)
            ? []
            : [plan.ReadinessSummary.Trim()];
    }

    private static IReadOnlyList<string> BuildWorkflowContext(ReportPackWorkflowRecordDto record) =>
    [
        record.FundProfileId,
        record.FundAccountId,
        record.Period
    ];

    private static string? ResolveRunPrimaryHref(WorkstationReportingRunPayload run) =>
        run.DrilldownLinks?
            .FirstOrDefault(static link => link.IsBrowserNavigable && !string.IsNullOrWhiteSpace(link.Href))
            ?.Href;

    private static int DailyWorkToneRank(string tone) =>
        tone.Trim().ToLowerInvariant() switch
        {
            "danger" => 0,
            "warning" => 1,
            "success" => 2,
            _ => 3
        };

    private static string CountUnit(int count, string singular) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {singular}{(count == 1 ? string.Empty : "s")}";

    private static IEnumerable<ReportingScheduleDeliveryPlanDto> BuildScheduleDeliveryPlans(
        ReportingScheduleRecordDto schedule,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts)
    {
        foreach (var target in schedule.DeliveryTargets ?? [])
        {
            if (string.IsNullOrWhiteSpace(target.DistributionId))
            {
                continue;
            }

            var distributionId = target.DistributionId.Trim();
            var formats = ResolveScheduleDeliveryFormats(target.Formats);
            var latestAttempt = FindLatestScheduleDeliveryAttempt(schedule, distributionId, deliveryAttempts);
            ReportPackDistributionPolicy? policy = null;
            ReportingScheduleDeliveryPlanDto? fallbackPlan = null;
            try
            {
                policy = ResolveDistributionPolicy(distributionId);
            }
            catch (KeyNotFoundException)
            {
                fallbackPlan = new ReportingScheduleDeliveryPlanDto(
                    PlanId: BuildScheduleDeliveryPlanId(schedule.ScheduleId, distributionId),
                    ScheduleId: schedule.ScheduleId,
                    TemplateId: schedule.TemplateId,
                    DistributionId: distributionId,
                    Recipient: distributionId,
                    RecipientRole: "Unknown recipient",
                    Channel: "Unknown channel",
                    DeliveryMode: target.DeliveryMode ?? ReportPackDeliveryModeDto.InternalRoute,
                    Formats: formats,
                    IsReady: false,
                    ReadinessSummary: $"Delivery target '{distributionId}' is not backed by a known report-pack distribution policy.",
                    Route: "/api/workstation/reporting",
                    DueAtUtc: schedule.DueAtUtc,
                    NextAsOfDate: schedule.NextAsOfDate,
                    Owner: schedule.RequestedBy,
                    Note: NormalizeOptional(target.Note),
                    LastDeliveryAttemptId: latestAttempt?.AttemptId,
                    LastDeliveryState: latestAttempt?.State,
                    LastDeliveryAtUtc: latestAttempt?.AttemptedAtUtc,
                    LastDeliveryPackageRoute: latestAttempt?.Package?.PortalRoute,
                    LastDeliverySecureLink: latestAttempt?.Package?.SecureLink,
                    LastDeliveryAccessLinks: latestAttempt?.Package?.AccessLinks,
                    LastDeliveryAccessExpiresAtUtc: latestAttempt?.Package?.AccessExpiresAtUtc,
                    LastDeliveryAccessSummary: latestAttempt?.Package?.DeliveryAccessSummary,
                    LastDeliveryChannelSummary: latestAttempt?.Package?.DeliveryChannelSummary,
                    LastDeliveryDownloadSummary: latestAttempt?.Package?.DownloadSummary,
                    LastDeliveryNotificationCount: latestAttempt?.Package?.Notifications?.Count ?? 0,
                    LastDeliveryNotificationSummary: BuildScheduleDeliveryPlanNotificationSummary(latestAttempt),
                    LastDeliveryGeneratedReportWriterGridCount: latestAttempt?.Package?.GeneratedReportWriterGrids?.Count ?? 0,
                    LastDeliveryRenderedReportWriterGridCount: latestAttempt?.Package?.RenderedReportWriterGrids?.Count ?? 0,
                    LastDeliveryReportWriterDatasetSummary: BuildScheduleDeliveryPlanReportWriterDatasetSummary(latestAttempt),
                    LastDeliveryReportWriterGridSummary: BuildScheduleDeliveryPlanReportWriterGridSummary(latestAttempt),
                    VersionStamp: BuildScheduleDeliveryPlanVersionStamp(schedule, distributionId, formats),
                    LastDeliveryArtifactCount: latestAttempt?.Package?.Artifacts.Count ?? 0,
                    LastDeliveryIntegritySummary: latestAttempt?.Package?.IntegritySummary,
                    BrandingThemeId: ResolveScheduleBrandingThemeId(schedule, latestAttempt),
                    BrandingTheme: latestAttempt?.Package?.BrandingTheme ?? schedule.BrandingThemeOverride,
                    LastDeliveryEntitlementScope: ResolveLastDeliveryEntitlementScope(latestAttempt));
            }

            if (fallbackPlan is not null)
            {
                yield return fallbackPlan;
                continue;
            }

            if (policy is null)
            {
                continue;
            }

            var deliveryMode = target.DeliveryMode ?? InferScheduleDeliveryMode(policy.Channel);
            var readiness = EvaluateScheduleDeliveryReadiness(schedule, policy, deliveryMode, formats, latestAttempt);
            yield return new ReportingScheduleDeliveryPlanDto(
                PlanId: BuildScheduleDeliveryPlanId(schedule.ScheduleId, policy.DistributionId),
                ScheduleId: schedule.ScheduleId,
                TemplateId: schedule.TemplateId,
                DistributionId: policy.DistributionId,
                Recipient: policy.Recipient,
                RecipientRole: policy.RecipientRole,
                Channel: policy.Channel,
                DeliveryMode: deliveryMode,
                Formats: formats,
                IsReady: readiness.IsReady,
                ReadinessSummary: readiness.Summary,
                Route: policy.Route,
                DueAtUtc: schedule.DueAtUtc,
                NextAsOfDate: schedule.NextAsOfDate,
                Owner: policy.Owner,
                Note: NormalizeOptional(target.Note),
                LastDeliveryAttemptId: latestAttempt?.AttemptId,
                LastDeliveryState: latestAttempt?.State,
                LastDeliveryAtUtc: latestAttempt?.AttemptedAtUtc,
                LastDeliveryPackageRoute: latestAttempt?.Package?.PortalRoute,
                LastDeliverySecureLink: latestAttempt?.Package?.SecureLink,
                LastDeliveryAccessLinks: latestAttempt?.Package?.AccessLinks,
                LastDeliveryAccessExpiresAtUtc: latestAttempt?.Package?.AccessExpiresAtUtc,
                LastDeliveryAccessSummary: latestAttempt?.Package?.DeliveryAccessSummary,
                LastDeliveryChannelSummary: latestAttempt?.Package?.DeliveryChannelSummary,
                LastDeliveryDownloadSummary: latestAttempt?.Package?.DownloadSummary,
                LastDeliveryNotificationCount: latestAttempt?.Package?.Notifications?.Count ?? 0,
                LastDeliveryNotificationSummary: BuildScheduleDeliveryPlanNotificationSummary(latestAttempt),
                LastDeliveryGeneratedReportWriterGridCount: latestAttempt?.Package?.GeneratedReportWriterGrids?.Count ?? 0,
                LastDeliveryRenderedReportWriterGridCount: latestAttempt?.Package?.RenderedReportWriterGrids?.Count ?? 0,
                LastDeliveryReportWriterDatasetSummary: BuildScheduleDeliveryPlanReportWriterDatasetSummary(latestAttempt),
                LastDeliveryReportWriterGridSummary: BuildScheduleDeliveryPlanReportWriterGridSummary(latestAttempt),
                VersionStamp: BuildScheduleDeliveryPlanVersionStamp(schedule, policy.DistributionId, formats),
                LastDeliveryArtifactCount: latestAttempt?.Package?.Artifacts.Count ?? 0,
                LastDeliveryIntegritySummary: latestAttempt?.Package?.IntegritySummary,
                ReadinessBlockers: readiness.Blockers,
                BrandingThemeId: ResolveScheduleBrandingThemeId(schedule, latestAttempt),
                BrandingTheme: latestAttempt?.Package?.BrandingTheme ?? schedule.BrandingThemeOverride,
                LastDeliveryEntitlementScope: ResolveLastDeliveryEntitlementScope(latestAttempt));
        }
    }

    private static string? ResolveLastDeliveryEntitlementScope(ReportPackDeliveryAttemptDto? latestAttempt)
    {
        var entitlementScope = NormalizeOptional(latestAttempt?.Package?.DeliveryEvidencePacket?.EntitlementScope);
        return string.IsNullOrWhiteSpace(entitlementScope) ? null : entitlementScope;
    }

    private static string? BuildScheduleDeliveryPlanNotificationSummary(ReportPackDeliveryAttemptDto? latestAttempt)
    {
        var notifications = latestAttempt?.Package?.Notifications;
        if (notifications is null || notifications.Count == 0)
        {
            return null;
        }

        var statusSummary = string.Join(
            "; ",
            notifications.Select(static notification => $"{notification.Status} via {notification.Channel}"));
        return $"{notifications.Count} notification{(notifications.Count == 1 ? string.Empty : "s")} retained: {statusSummary}.";
    }

    private static string? BuildScheduleDeliveryPlanReportWriterDatasetSummary(ReportPackDeliveryAttemptDto? latestAttempt)
    {
        var package = latestAttempt?.Package;
        if (package is null)
        {
            return null;
        }

        var label = NormalizeOptional(package.ReportWriterDatasetSourceLabel)
            ?? NormalizeOptional(package.ReportWriterDatasetSourceId)
            ?? (package.ReportWriterDatasetRowCount.HasValue ? "Custom report-writer dataset" : null);
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        return package.ReportWriterDatasetRowCount.HasValue
            ? $"{label} ({package.ReportWriterDatasetRowCount.Value} row{(package.ReportWriterDatasetRowCount.Value == 1 ? string.Empty : "s")})"
            : label;
    }

    private static string? BuildScheduleDeliveryPlanReportWriterGridSummary(ReportPackDeliveryAttemptDto? latestAttempt)
    {
        var package = latestAttempt?.Package;
        var generatedGrids = package?.GeneratedReportWriterGrids ?? [];
        var renderedGrids = package?.RenderedReportWriterGrids ?? [];
        if (generatedGrids.Count == 0 && renderedGrids.Count == 0)
        {
            return null;
        }

        var generatedSummary = generatedGrids.Count == 0
            ? null
            : "generated: " + string.Join(", ", generatedGrids.Select(FormatGeneratedReportWriterGrid));
        var renderedSummary = renderedGrids.Count == 0
            ? null
            : "rendered: " + string.Join(", ", renderedGrids.Select(FormatRenderedReportWriterGrid));
        var details = string.Join("; ", new[] { generatedSummary, renderedSummary }.Where(static item => !string.IsNullOrWhiteSpace(item)));
        var prefix = $"{generatedGrids.Count} generated / {renderedGrids.Count} rendered report-writer grid{(generatedGrids.Count + renderedGrids.Count == 1 ? string.Empty : "s")}";
        return string.IsNullOrWhiteSpace(details) ? prefix : $"{prefix}: {details}.";
    }

    private static string FormatGeneratedReportWriterGrid(WorkstationGeneratedReportWriterGridPayload grid)
    {
        var validation = NormalizeOptional(grid.ValidationSummary);
        var title = NormalizeOptional(grid.Title) ?? grid.GridId;
        return validation is null
            ? $"{title} ({grid.Kind}, {grid.DimensionCount}d/{grid.MetricCount}m/{grid.FormulaCount}f)"
            : $"{title} ({grid.Kind}, {grid.DimensionCount}d/{grid.MetricCount}m/{grid.FormulaCount}f, validation {validation})";
    }

    private static string FormatRenderedReportWriterGrid(ReportWriterGridRenderDto grid)
    {
        var title = NormalizeOptional(grid.Title) ?? grid.GridId;
        return $"{title} ({grid.Rows.Count}r/{grid.Columns.Count}c)";
    }

    private static string? ResolveScheduleBrandingThemeId(
        ReportingScheduleRecordDto schedule,
        ReportPackDeliveryAttemptDto? latestAttempt)
    {
        var packageThemeId = NormalizeOptional(latestAttempt?.Package?.BrandingTheme?.ThemeId);
        if (!string.IsNullOrWhiteSpace(packageThemeId))
        {
            return packageThemeId;
        }

        var overrideThemeId = NormalizeOptional(schedule.BrandingThemeOverride?.ThemeId);
        return string.IsNullOrWhiteSpace(overrideThemeId) ? NormalizeOptional(schedule.BrandingThemeId) : overrideThemeId;
    }

    private static ReportPackDeliveryAttemptDto? FindLatestScheduleDeliveryAttempt(
        ReportingScheduleRecordDto schedule,
        string distributionId,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts)
    {
        var referencePrefix = $"schedule:{schedule.TemplateId.Trim()}:";
        return deliveryAttempts
            .Where(attempt => string.Equals(attempt.DistributionId, distributionId, StringComparison.OrdinalIgnoreCase))
            .Where(attempt => attempt.DeliveryReference.StartsWith(referencePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static attempt => attempt.AttemptedAtUtc)
            .ThenByDescending(static attempt => attempt.AttemptNumber)
            .FirstOrDefault();
    }

    private static IReadOnlyList<GovernanceReportArtifactFormatDto> ResolveScheduleDeliveryFormats(
        IReadOnlyList<GovernanceReportArtifactFormatDto>? formats)
    {
        IReadOnlyList<GovernanceReportArtifactFormatDto> requested = formats is { Count: > 0 }
            ? formats
            : [GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Xlsx, GovernanceReportArtifactFormatDto.Csv];
        return requested
            .Distinct()
            .ToArray();
    }

    private static ScheduleDeliveryReadiness EvaluateScheduleDeliveryReadiness(
        ReportingScheduleRecordDto schedule,
        ReportPackDistributionPolicy policy,
        ReportPackDeliveryModeDto deliveryMode,
        IReadOnlyList<GovernanceReportArtifactFormatDto> formats,
        ReportPackDeliveryAttemptDto? latestAttempt)
    {
        var blockers = new List<string>();
        if (schedule.State != ReportingScheduleStateDto.Active)
        {
            blockers.Add($"Schedule '{schedule.ScheduleId}' is {schedule.State}; delivery will not run until it is active.");
        }

        if (formats.Count == 0)
        {
            blockers.Add($"Schedule '{schedule.ScheduleId}' has no requested delivery formats.");
        }

        if (!IsDeliveryModeCompatible(policy.Channel, deliveryMode))
        {
            blockers.Add($"Delivery mode {deliveryMode} is not compatible with {policy.Channel} for {policy.Recipient}.");
        }

        if (schedule.DueAtUtc <= DateTimeOffset.UtcNow &&
            latestAttempt?.State != ReportPackDeliveryStateDto.Delivered)
        {
            blockers.Add($"Schedule '{schedule.ScheduleId}' is due and has no successful retained delivery package for {policy.Recipient}.");
        }

        if (latestAttempt?.State == ReportPackDeliveryStateDto.Delivered)
        {
            var packageIssues = ReportPackDeliveryService.ValidateDeliverablePackage(latestAttempt);
            if (packageIssues.Count > 0)
            {
                blockers.Add($"Latest delivery package for {policy.Recipient} is incomplete: {string.Join(" ", packageIssues)}");
            }
        }

        var missingFormats = FindMissingDeliveryFormats(formats, latestAttempt);
        if (missingFormats.Length > 0)
        {
            blockers.Add($"Latest delivery package for {policy.Recipient} is missing requested artifact format(s): {FormatScheduleDeliveryFormats(missingFormats)}.");
        }

        var summary = blockers.Count == 0
            ? $"Ready to deliver {FormatScheduleDeliveryFormats(formats)} by {deliveryMode} to {policy.Recipient} when schedule '{schedule.ScheduleId}' runs."
            : string.Join(" ", blockers);
        return new ScheduleDeliveryReadiness(blockers.Count == 0, summary, blockers.ToArray());
    }

    private static bool IsDeliveryModeCompatible(string channel, ReportPackDeliveryModeDto deliveryMode)
    {
        if (deliveryMode == ReportPackDeliveryModeDto.EmailLink)
        {
            return true;
        }

        if (channel.Contains("portal", StringComparison.OrdinalIgnoreCase))
        {
            return deliveryMode == ReportPackDeliveryModeDto.SecurePortal;
        }

        if (channel.Contains("vault", StringComparison.OrdinalIgnoreCase))
        {
            return deliveryMode == ReportPackDeliveryModeDto.EvidenceVault;
        }

        return deliveryMode == ReportPackDeliveryModeDto.InternalRoute;
    }

    private static GovernanceReportArtifactFormatDto[] FindMissingDeliveryFormats(
        IReadOnlyList<GovernanceReportArtifactFormatDto> formats,
        ReportPackDeliveryAttemptDto? latestAttempt)
    {
        if (latestAttempt?.Package is null || latestAttempt.State != ReportPackDeliveryStateDto.Delivered)
        {
            return [];
        }

        var deliveredFormats = latestAttempt.Package.Artifacts
            .Select(static artifact => artifact.Format)
            .ToHashSet();
        return formats
            .Where(format => !deliveredFormats.Contains(format))
            .Distinct()
            .OrderBy(static format => format)
            .ToArray();
    }

    private static string BuildScheduleDeliveryPlanId(string scheduleId, string distributionId) =>
        $"schedule-delivery:{scheduleId}:{distributionId}";

    private static string BuildScheduleDeliveryPlanVersionStamp(
        ReportingScheduleRecordDto schedule,
        string distributionId,
        IReadOnlyList<GovernanceReportArtifactFormatDto> formats) =>
        $"schedule-delivery-plan:{schedule.ScheduleId}:{distributionId}:{schedule.UpdatedAtUtc.UtcDateTime:yyyyMMddHHmmss}:formats-{formats.Count}";

    private static string FormatScheduleDeliveryFormats(IReadOnlyList<GovernanceReportArtifactFormatDto> formats) =>
        string.Join("/", formats.Select(static format => format.ToString()));

    private static ReportPackDeliveryModeDto InferScheduleDeliveryMode(string channel)
    {
        if (channel.Contains("email", StringComparison.OrdinalIgnoreCase))
        {
            return ReportPackDeliveryModeDto.EmailLink;
        }

        if (channel.Contains("portal", StringComparison.OrdinalIgnoreCase))
        {
            return ReportPackDeliveryModeDto.SecurePortal;
        }

        if (channel.Contains("vault", StringComparison.OrdinalIgnoreCase))
        {
            return ReportPackDeliveryModeDto.EvidenceVault;
        }

        return ReportPackDeliveryModeDto.InternalRoute;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static WorkstationReportPackDistributionPayload BuildDistribution(
        ReportPackDistributionPolicy policy,
        int blockedCount,
        int pendingApprovalCount,
        int pendingPublicationCount,
        int pendingDeliveryCount,
        DateTimeOffset? latestActionAt,
        DateTimeOffset? latestPublishedAt,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts)
    {
        var latestDelivery = deliveryAttempts
            .Where(attempt =>
                string.Equals(attempt.DistributionId, policy.DistributionId, StringComparison.OrdinalIgnoreCase)
                && ReportPackDeliveryService.ValidateDeliverablePackage(attempt).Count == 0)
            .OrderByDescending(static attempt => attempt.AttemptedAtUtc)
            .FirstOrDefault();
        var (state, pendingItems, pendingSummary, dueAtUtc) = ResolveDistributionState(
            policy,
            blockedCount,
            pendingApprovalCount,
            pendingPublicationCount,
            pendingDeliveryCount,
            latestActionAt,
            latestPublishedAt,
            latestDelivery?.AttemptedAtUtc);

        return new WorkstationReportPackDistributionPayload(
            policy.DistributionId,
            policy.Recipient,
            policy.RecipientRole,
            policy.Channel,
            state,
            pendingItems,
            pendingSummary,
            policy.Owner,
            dueAtUtc,
            latestDelivery?.AttemptedAtUtc,
            policy.Route);
    }

    private static (string State, int PendingItems, string PendingSummary, DateTimeOffset? DueAtUtc) ResolveDistributionState(
        ReportPackDistributionPolicy policy,
        int blockedCount,
        int pendingApprovalCount,
        int pendingPublicationCount,
        int pendingDeliveryCount,
        DateTimeOffset? latestActionAt,
        DateTimeOffset? latestPublishedAt,
        DateTimeOffset? latestDeliveryAt)
    {
        if (blockedCount > 0)
        {
            return (
                "Blocked",
                blockedCount,
                $"{blockedCount} rejected report pack{Plural(blockedCount)} must be corrected before {policy.Recipient} receives a package.",
                latestActionAt?.Add(policy.CorrectionSla));
        }

        if (pendingPublicationCount > 0)
        {
            return (
                "Pending publication",
                pendingPublicationCount,
                $"{pendingPublicationCount} approved report pack{Plural(pendingPublicationCount)} must be published before {policy.Recipient} delivery.",
                latestActionAt?.Add(policy.PublicationSla));
        }

        if (pendingApprovalCount > 0)
        {
            return (
                "Pending approval",
                pendingApprovalCount,
                $"{pendingApprovalCount} report pack{Plural(pendingApprovalCount)} still need review or approval before {policy.Recipient} delivery.",
                latestActionAt?.Add(policy.ApprovalSla));
        }

        if (pendingDeliveryCount > 0)
        {
            if (latestPublishedAt is not null && latestDeliveryAt is not null && latestDeliveryAt >= latestPublishedAt)
            {
                return (
                    "Delivered",
                    0,
                    $"{policy.Recipient} received the latest governed report pack by {policy.Channel}.",
                    null);
            }

            return (
                "Pending delivery",
                pendingDeliveryCount,
                $"{pendingDeliveryCount} published report pack{Plural(pendingDeliveryCount)} are ready for {policy.Channel} delivery to {policy.Recipient}.",
                latestPublishedAt?.Add(policy.DeliverySla));
        }

        return (
            "No package queued",
            0,
            $"No governed report pack is queued for {policy.Recipient}.",
            null);
    }

    private static IEnumerable<UnifiedReportingRun> ProjectGenericRuns(
        IReadOnlyList<ReportingRunSnapshot> runs,
        IReadOnlyDictionary<string, string> familyByTemplate,
        IReadOnlyDictionary<string, WorkstationReportingTemplatePayload> templatesById)
    {
        var latestGeneratedBySeries = runs
            .GroupBy(static run => ResolveRunSeriesId(run.Manifest), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderByDescending(static run => ResolveRunAttemptOrdinal(run.Manifest))
                    .ThenByDescending(static run => run.UpdatedAtUtc)
                    .First().Manifest.RunId,
                StringComparer.OrdinalIgnoreCase);
        var latestApprovedBySeries = runs
            .Where(static run => IsApprovedReportingRun(run.Manifest))
            .GroupBy(static run => ResolveRunSeriesId(run.Manifest), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderByDescending(static run => ResolveRunAttemptOrdinal(run.Manifest))
                    .ThenByDescending(static run => run.UpdatedAtUtc)
                    .First().Manifest.RunId,
                StringComparer.OrdinalIgnoreCase);

        foreach (var run in runs)
        {
            var runSeriesId = ResolveRunSeriesId(run.Manifest);
            latestGeneratedBySeries.TryGetValue(runSeriesId, out var latestGeneratedRunId);
            latestApprovedBySeries.TryGetValue(runSeriesId, out var latestApprovedRunId);
            yield return ProjectGenericRun(
                run,
                familyByTemplate,
                templatesById,
                runSeriesId,
                latestGeneratedRunId,
                latestApprovedRunId);
        }
    }

    private static UnifiedReportingRun ProjectGenericRun(
        ReportingRunSnapshot run,
        IReadOnlyDictionary<string, string> familyByTemplate,
        IReadOnlyDictionary<string, WorkstationReportingTemplatePayload> templatesById,
        string runSeriesId,
        string? latestGeneratedRunId,
        string? latestApprovedRunId)
    {
        var manifest = run.Manifest;
        var family = familyByTemplate.TryGetValue(manifest.TemplateId, out var templateFamily)
            ? templateFamily
            : "ReportingRun";
        templatesById.TryGetValue(manifest.TemplateId, out var template);
        var comparisonCounts = CountReportWriterLineChanges(manifest);

        return new UnifiedReportingRun(
            new WorkstationReportingRunPayload(
                RunId: manifest.RunId,
                TemplateId: manifest.TemplateId,
                Family: family,
                Status: manifest.Status.ToString(),
                Trigger: manifest.Trigger.ToString(),
                AsOfDate: manifest.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                AttemptCount: manifest.AttemptCount,
                SectionCount: manifest.Sections.Length,
                LineageLinkedSections: manifest.Sections.Count(static section => section.Lineage is not null),
                Artifacts: manifest.Artifacts
                    .Concat(BuildGenericRunDrilldownArtifacts(manifest))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                AuditActions: run.AuditTrail
                    .OrderBy(static audit => audit.TimestampUtc)
                    .Select(static audit => audit.Action)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                FailureReason: manifest.FailureReason,
                DrilldownLinks: BuildGenericRunDrilldownLinks(manifest).ToArray(),
                NextActions: BuildGenericRunNextActions(manifest).ToArray(),
                GeneratedReportWriterGrids: BuildGeneratedReportWriterGrids(manifest, template).ToArray(),
                ReportWriterDatasetSourceId: manifest.ReportWriterDatasetSourceId,
                ReportWriterDatasetSourceLabel: manifest.ReportWriterDatasetSourceLabel,
                ReportWriterDatasetRowCount: manifest.ReportWriterDatasetRowCount,
                RunSeriesId: runSeriesId,
                RunAttemptOrdinal: ResolveRunAttemptOrdinal(manifest),
                PriorRunId: manifest.PriorRunId,
                RetryReason: manifest.RetryReason,
                LatestGeneratedRunId: latestGeneratedRunId,
                LatestApprovedRunId: latestApprovedRunId,
                IsLatestGenerated: string.Equals(manifest.RunId, latestGeneratedRunId, StringComparison.OrdinalIgnoreCase),
                IsLatestApproved: latestApprovedRunId is not null &&
                    string.Equals(manifest.RunId, latestApprovedRunId, StringComparison.OrdinalIgnoreCase),
                ComparisonSummary: BuildRunComparisonSummary(manifest, comparisonCounts),
                ChangedLineCount: comparisonCounts.Changed,
                AddedLineCount: comparisonCounts.Added,
                RemovedLineCount: comparisonCounts.Removed),
            run.UpdatedAtUtc);
    }

    private static UnifiedReportingRun ProjectWorkflowRun(ReportPackWorkflowRecordDto record)
    {
        var auditActions = record.AuditTrail
            .OrderBy(static audit => audit.At)
            .Select(static audit => audit.Action)
            .Concat(BuildWorkflowStatusActions(record))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var artifacts = BuildWorkflowArtifacts(record)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new UnifiedReportingRun(
            new WorkstationReportingRunPayload(
                RunId: $"report-pack:{record.ReportId:D}",
                TemplateId: $"{record.TemplateId.Name}:v{record.TemplateId.Version}",
                Family: "GovernedReportPack",
                Status: record.State.ToString(),
                Trigger: "Workflow",
                AsOfDate: record.Period,
                AttemptCount: Math.Max(1, record.Version),
                SectionCount: record.LineProvenance?.Count ?? record.Restatement?.ChangedLines.Count ?? 0,
                LineageLinkedSections: record.LineProvenance?.Count(static line => !string.IsNullOrWhiteSpace(line.EvidenceId)) ?? 0,
                Artifacts: artifacts,
                AuditActions: auditActions,
                FailureReason: record.Rejection?.Reason,
                DrilldownLinks: BuildWorkflowDrilldownLinks(record).ToArray(),
                NextActions: BuildWorkflowNextActions(record).ToArray(),
                GeneratedReportWriterGrids: []),
            record.UpdatedAt);
    }

    private static string ResolveRunSeriesId(ReportingOutputManifest manifest) =>
        string.IsNullOrWhiteSpace(manifest.RunSeriesId) ? manifest.RunId : manifest.RunSeriesId.Trim();

    private static int ResolveRunAttemptOrdinal(ReportingOutputManifest manifest) =>
        manifest.RunAttemptOrdinal is > 0 ? manifest.RunAttemptOrdinal.Value : 1;

    private static bool IsApprovedReportingRun(ReportingOutputManifest manifest) =>
        manifest.Status is ReportingRunStatus.Approved or ReportingRunStatus.Released;

    private static ReportingLineChangeCounts CountReportWriterLineChanges(ReportingOutputManifest manifest)
    {
        if (manifest.ReportWriterGridDiffs.IsDefaultOrEmpty)
        {
            return new ReportingLineChangeCounts(0, 0, 0);
        }

        return new ReportingLineChangeCounts(
            manifest.ReportWriterGridDiffs.Sum(static diff => diff.ChangedRowCount),
            manifest.ReportWriterGridDiffs.Sum(static diff => diff.AddedRowCount),
            manifest.ReportWriterGridDiffs.Sum(static diff => diff.RemovedRowCount));
    }

    private static string? BuildRunComparisonSummary(
        ReportingOutputManifest manifest,
        ReportingLineChangeCounts comparisonCounts)
    {
        if (string.IsNullOrWhiteSpace(manifest.PriorRunId))
        {
            return "First generated attempt in this run series.";
        }

        return manifest.ReportWriterGridDiffs.IsDefaultOrEmpty
            ? $"Compared with {manifest.PriorRunId}; no report-writer line comparison was retained."
            : $"{comparisonCounts.Changed} changed, {comparisonCounts.Added} added, {comparisonCounts.Removed} removed lines compared with {manifest.PriorRunId}.";
    }

    private static IEnumerable<WorkstationGeneratedReportWriterGridPayload> BuildGeneratedReportWriterGrids(
        ReportingOutputManifest manifest,
        WorkstationReportingTemplatePayload? template)
    {
        if (!manifest.ReportWriterGrids.IsDefaultOrEmpty)
        {
            foreach (var grid in manifest.ReportWriterGrids)
            {
                var validation = BuildGeneratedGridValidationSummary(grid.GridId, manifest.RenderedReportWriterGrids);
                yield return new WorkstationGeneratedReportWriterGridPayload(
                    grid.GridId,
                    grid.Title,
                    grid.Kind,
                    grid.Artifact,
                    grid.DimensionCount,
                    grid.MetricCount,
                    grid.FormulaCount,
                    validation.Summary,
                    validation.Passed,
                    validation.Warning,
                    validation.Failed);
            }

            yield break;
        }

        var templateGrids = template?.ReportWriterGrids?
            .Where(static grid => !string.IsNullOrWhiteSpace(grid.GridId))
            .GroupBy(static grid => grid.GridId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? [];

        foreach (var artifact in manifest.Artifacts.Where(static artifact => artifact.StartsWith("report-writer://", StringComparison.OrdinalIgnoreCase)))
        {
            var gridId = ExtractReportWriterGridId(artifact);
            if (gridId is null)
            {
                continue;
            }

            if (templateGrids.TryGetValue(gridId, out var grid))
            {
                var validation = BuildGeneratedGridValidationSummary(grid.GridId, manifest.RenderedReportWriterGrids);
                yield return new WorkstationGeneratedReportWriterGridPayload(
                    grid.GridId,
                    grid.Title,
                    grid.Kind,
                    artifact,
                    grid.DimensionCount,
                    grid.MetricCount,
                    grid.FormulaCount,
                    validation.Summary,
                    validation.Passed,
                    validation.Warning,
                    validation.Failed);
                continue;
            }

            var generatedValidation = BuildGeneratedGridValidationSummary(gridId, manifest.RenderedReportWriterGrids);
            yield return new WorkstationGeneratedReportWriterGridPayload(
                gridId,
                gridId,
                "Generated",
                artifact,
                0,
                0,
                0,
                generatedValidation.Summary,
                generatedValidation.Passed,
                generatedValidation.Warning,
                generatedValidation.Failed);
        }
    }

    private static (string? Summary, int? Passed, int? Warning, int? Failed) BuildGeneratedGridValidationSummary(
        string gridId,
        System.Collections.Immutable.ImmutableArray<ReportWriterGridRenderDto> renderedGrids)
    {
        if (renderedGrids.IsDefaultOrEmpty)
        {
            return (null, null, null, null);
        }

        var renderedGrid = renderedGrids.FirstOrDefault(grid =>
            string.Equals(grid.GridId, gridId, StringComparison.OrdinalIgnoreCase));
        if (renderedGrid is null)
        {
            return (null, null, null, null);
        }

        var checks = ResolveReportWriterGridValidationChecks(renderedGrid);
        if (checks.Count == 0)
        {
            return (null, null, null, null);
        }

        var passed = checks.Count(static check => string.Equals(check.Status, "Passed", StringComparison.OrdinalIgnoreCase));
        var warning = checks.Count(static check => string.Equals(check.Status, "Warning", StringComparison.OrdinalIgnoreCase));
        var failed = checks.Count - passed - warning;
        var summary = failed > 0
            ? $"{passed} passed / {checks.Count} checks, {failed} failed"
            : warning > 0
                ? $"{passed} passed / {checks.Count} checks, {warning} review"
                : $"{passed} passed / {checks.Count} checks";
        return (summary, passed, warning, failed);
    }

    private static IReadOnlyList<ReportWriterGridValidationCheckDto> ResolveReportWriterGridValidationChecks(
        ReportWriterGridRenderDto grid) =>
        grid.ValidationChecks is { Count: > 0 }
            ? grid.ValidationChecks
            :
            [
                new ReportWriterGridValidationCheckDto(
                    "row-count",
                    grid.Lineage is null || grid.Lineage.OutputRowCount == grid.Rows.Count ? "Passed" : "Failed",
                    grid.Lineage is null
                        ? $"Rendered {grid.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} row(s); no lineage row count was retained."
                        : $"Rendered {grid.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} row(s); lineage expected {grid.Lineage.OutputRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}."),
                new ReportWriterGridValidationCheckDto(
                    "column-dictionary",
                    "Passed",
                    $"Dictionary covers {grid.Columns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} of {grid.Columns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} rendered column(s)."),
                new ReportWriterGridValidationCheckDto(
                    "source-field-lineage",
                    grid.Lineage is { SourceFields.Count: > 0 } ? "Passed" : "Warning",
                    grid.Lineage is { SourceFields.Count: > 0 }
                        ? $"Lineage retains {grid.Lineage.SourceFields.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} source field(s)."
                        : "No source field lineage was retained with this grid."),
                new ReportWriterGridValidationCheckDto(
                    "warnings",
                    grid.Warnings.Count == 0 ? "Passed" : "Warning",
                    grid.Warnings.Count == 0
                        ? "No render warnings were retained."
                        : $"{grid.Warnings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} render warning(s) retained: {string.Join("; ", grid.Warnings)}")
            ];

    private static string? ExtractReportWriterGridId(string artifact)
    {
        const string marker = "/grids/";
        var index = artifact.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var gridId = artifact[(index + marker.Length)..].Trim();
        return string.IsNullOrWhiteSpace(gridId) ? null : gridId;
    }

    private static IEnumerable<string> BuildGenericRunDrilldownArtifacts(ReportingOutputManifest manifest)
    {
        yield return $"reporting-run://{manifest.RunId}/manifest";
        yield return $"reporting-run://{manifest.RunId}/audit";

        if (!string.IsNullOrWhiteSpace(manifest.ScheduleId))
        {
            yield return $"schedule:{manifest.ScheduleId}";
        }
    }

    private static IEnumerable<string> BuildWorkflowArtifacts(ReportPackWorkflowRecordDto record)
    {
        var reportId = Uri.EscapeDataString(record.ReportId.ToString("D"));
        yield return $"/api/fund-structure/report-packs/{reportId}/evidence-bundle";
        yield return $"/api/fund-structure/reporting/packs/history?period={Uri.EscapeDataString(record.Period)}&fundAccountId={Uri.EscapeDataString(record.FundAccountId)}";
        yield return $"/reporting/report-packs/{reportId}";
        yield return $"fund-account:{record.FundAccountId}";
        yield return $"period:{record.Period}";

        foreach (var line in record.LineProvenance ?? [])
        {
            if (!string.IsNullOrWhiteSpace(line.LineKey))
            {
                yield return $"/api/fund-structure/report-packs/{reportId}/ledger-provenance?scopeKey={Uri.EscapeDataString(line.LineKey)}";
            }

            if (!string.IsNullOrWhiteSpace(line.EvidenceId))
            {
                yield return $"evidence:{line.EvidenceId}";
            }
        }

        if (record.Publication is { } publication)
        {
            yield return $"publication-manifest:{publication.ManifestId}";
            yield return publication.RetainedManifestPath;
            yield return $"evidence-hash:{publication.EvidenceHash}";
            foreach (var link in publication.EvidenceLinks)
            {
                if (!string.IsNullOrWhiteSpace(link.Route))
                {
                    yield return link.Route!;
                }
            }
        }

        if (record.Restatement is { } restatement)
        {
            yield return $"restatement:{restatement.ReasonCode}";
            yield return $"prior-report:{restatement.PriorVersionReportId:D}";
            foreach (var link in restatement.EvidenceLinks ?? [])
            {
                if (!string.IsNullOrWhiteSpace(link.Route))
                {
                    yield return link.Route!;
                }
            }
        }
    }

    private static IEnumerable<WorkstationReportingRunLinkPayload> BuildGenericRunDrilldownLinks(
        ReportingOutputManifest manifest)
    {
        yield return BuildRunLink(
            id: $"{manifest.RunId}:manifest",
            kind: "manifest",
            label: "Run manifest",
            href: $"reporting-run://{manifest.RunId}/manifest",
            method: "GET",
            isBrowserNavigable: false,
            source: "ReportingOrchestration");
        yield return BuildRunLink(
            id: $"{manifest.RunId}:audit",
            kind: "audit",
            label: "Approval audit trail",
            href: BuildRunAuditRoute(manifest.RunId),
            method: "GET",
            isBrowserNavigable: true,
            source: "ReportingOrchestration");

        if (!string.IsNullOrWhiteSpace(manifest.ScheduleId))
        {
            yield return BuildRunLink(
                id: $"{manifest.RunId}:schedule",
                kind: "schedule",
                label: "Schedule source",
                href: $"schedule:{manifest.ScheduleId}",
                method: "GET",
                isBrowserNavigable: false,
                source: "ReportingOrchestration");
        }
    }

    private static IEnumerable<WorkstationReportingRunNextActionPayload> BuildGenericRunNextActions(
        ReportingOutputManifest manifest)
    {
        if (manifest.Status == ReportingRunStatus.Draft)
        {
            yield return BuildRunAction(
                id: $"{manifest.RunId}:submit",
                kind: "approval-submit",
                label: "Submit run for review",
                href: $"reporting-run://{manifest.RunId}/approval/submit",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }
        else if (manifest.Status == ReportingRunStatus.InReview)
        {
            yield return BuildRunAction(
                id: $"{manifest.RunId}:approve",
                kind: "approval",
                label: "Approve reporting run",
                href: $"reporting-run://{manifest.RunId}/approval/approve",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }
        else if (manifest.Status == ReportingRunStatus.Approved)
        {
            yield return BuildRunAction(
                id: $"{manifest.RunId}:release",
                kind: "publication",
                label: "Release reporting run",
                href: $"reporting-run://{manifest.RunId}/publication/release",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }
        else if (manifest.Status == ReportingRunStatus.Released)
        {
            yield return BuildRunAction(
                id: $"{manifest.RunId}:review-release",
                kind: "publication-review",
                label: "Review released artifacts",
                href: $"reporting-run://{manifest.RunId}/manifest",
                method: "GET",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }
        else if (manifest.Status == ReportingRunStatus.Failed)
        {
            yield return BuildRunAction(
                id: $"{manifest.RunId}:inspect-failure",
                kind: "failure-review",
                label: "Inspect failed run",
                href: BuildRunAuditRoute(manifest.RunId),
                method: "GET",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: true);
        }
    }

    private static string BuildRunAuditRoute(string runId) =>
        UiApiRoutes.WithParam(UiApiRoutes.ReportingRunAuditTrail, "runId", runId);

    private static IEnumerable<WorkstationReportingRunLinkPayload> BuildWorkflowDrilldownLinks(
        ReportPackWorkflowRecordDto record)
    {
        var reportId = EscapeReportId(record.ReportId);
        yield return BuildRunLink(
            id: $"{record.ReportId:D}:report-pack",
            kind: "report-pack",
            label: "Report-pack manifest",
            href: $"/api/fund-structure/report-packs/{reportId}",
            method: "GET",
            isBrowserNavigable: true,
            source: "ReportPackWorkflow");
        yield return BuildRunLink(
            id: $"{record.ReportId:D}:evidence-bundle",
            kind: "evidence",
            label: "Evidence bundle",
            href: $"/api/fund-structure/report-packs/{reportId}/evidence-bundle",
            method: "GET",
            isBrowserNavigable: true,
            source: "ReportPackWorkflow");
        yield return BuildRunLink(
            id: $"{record.ReportId:D}:history",
            kind: "history",
            label: "Workflow history",
            href: $"/api/fund-structure/reporting/packs/history?period={Uri.EscapeDataString(record.Period)}&fundAccountId={Uri.EscapeDataString(record.FundAccountId)}",
            method: "GET",
            isBrowserNavigable: true,
            source: "ReportPackWorkflow");

        foreach (var line in record.LineProvenance ?? [])
        {
            if (!string.IsNullOrWhiteSpace(line.LineKey))
            {
                yield return BuildRunLink(
                    id: $"{record.ReportId:D}:ledger:{line.LineKey}",
                    kind: "ledger-provenance",
                    label: $"Ledger provenance: {line.LineKey}",
                    href: $"/api/fund-structure/report-packs/{reportId}/ledger-provenance?scopeKey={Uri.EscapeDataString(line.LineKey)}",
                    method: "GET",
                    isBrowserNavigable: true,
                    source: "ReportPackWorkflow");
            }

            if (!string.IsNullOrWhiteSpace(line.EvidenceId))
            {
                yield return BuildRunLink(
                    id: $"{record.ReportId:D}:line-evidence:{line.EvidenceId}",
                    kind: "evidence",
                    label: $"Line evidence: {line.LineKey}",
                    href: $"evidence:{line.EvidenceId}",
                    method: "GET",
                    isBrowserNavigable: false,
                    source: "ReportPackWorkflow");
            }
        }

        foreach (var link in BuildPublicationLinks(record))
        {
            yield return link;
        }

        foreach (var link in BuildRestatementLinks(record))
        {
            yield return link;
        }
    }

    private static IEnumerable<WorkstationReportingRunLinkPayload> BuildPublicationLinks(
        ReportPackWorkflowRecordDto record)
    {
        if (record.Publication is not { } publication)
        {
            yield break;
        }

        yield return BuildRunLink(
            id: $"{record.ReportId:D}:publication-manifest",
            kind: "publication",
            label: "Publication manifest",
            href: publication.RetainedManifestPath,
            method: "GET",
            isBrowserNavigable: false,
            source: "ReportPackWorkflow");

        foreach (var link in publication.EvidenceLinks)
        {
            if (!string.IsNullOrWhiteSpace(link.Route))
            {
                yield return BuildRunLink(
                    id: $"{record.ReportId:D}:publication:{link.EvidenceId}",
                    kind: "publication-evidence",
                    label: string.IsNullOrWhiteSpace(link.Label) ? link.EvidenceId : link.Label,
                    href: SanitizeDeliveryHref(link.Route!),
                    method: "GET",
                    isBrowserNavigable: IsHttpRoute(link.Route),
                    source: string.IsNullOrWhiteSpace(link.Source) ? "publication" : link.Source!);
            }
        }
    }

    private static IEnumerable<WorkstationReportingRunLinkPayload> BuildRestatementLinks(
        ReportPackWorkflowRecordDto record)
    {
        if (record.Restatement is not { } restatement)
        {
            yield break;
        }

        var priorReportId = EscapeReportId(restatement.PriorVersionReportId);
        yield return BuildRunLink(
            id: $"{record.ReportId:D}:prior-report",
            kind: "restatement",
            label: "Prior report version",
            href: $"/api/fund-structure/report-packs/{priorReportId}",
            method: "GET",
            isBrowserNavigable: true,
            source: "ReportPackWorkflow");

        foreach (var link in restatement.EvidenceLinks ?? [])
        {
            if (!string.IsNullOrWhiteSpace(link.Route))
            {
                yield return BuildRunLink(
                    id: $"{record.ReportId:D}:restatement:{link.EvidenceId}",
                    kind: "restatement-evidence",
                    label: string.IsNullOrWhiteSpace(link.Label) ? link.EvidenceId : link.Label,
                    href: SanitizeDeliveryHref(link.Route!),
                    method: "GET",
                    isBrowserNavigable: IsHttpRoute(link.Route),
                    source: string.IsNullOrWhiteSpace(link.Source) ? "restatement" : link.Source!);
            }
        }
    }

    private static IEnumerable<WorkstationReportingRunNextActionPayload> BuildWorkflowNextActions(
        ReportPackWorkflowRecordDto record)
    {
        var reportId = EscapeReportId(record.ReportId);
        if (record.State is ReportPackWorkflowStateDto.Draft or ReportPackWorkflowStateDto.Validated or ReportPackWorkflowStateDto.Rejected)
        {
            yield return BuildRunAction(
                id: $"{record.ReportId:D}:submit",
                kind: "approval-submit",
                label: record.State == ReportPackWorkflowStateDto.Rejected ? "Return pack to review" : "Submit pack for review",
                href: $"/api/fund-structure/reporting/packs/{reportId}/submit",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }

        if (record.State is ReportPackWorkflowStateDto.InReview or ReportPackWorkflowStateDto.PendingApproval)
        {
            yield return BuildRunAction(
                id: $"{record.ReportId:D}:approve",
                kind: "approval",
                label: "Approve report pack",
                href: $"/api/fund-structure/reporting/packs/{reportId}/approve",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
            yield return BuildRunAction(
                id: $"{record.ReportId:D}:reject",
                kind: "approval-reject",
                label: "Reject for correction",
                href: $"/api/fund-structure/reporting/packs/{reportId}/reject",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }

        if (record.State == ReportPackWorkflowStateDto.Approved)
        {
            yield return BuildRunAction(
                id: $"{record.ReportId:D}:publish",
                kind: "publication",
                label: "Publish retained report pack",
                href: $"/api/fund-structure/reporting/packs/{reportId}/publish",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }

        if (record.State == ReportPackWorkflowStateDto.Published)
        {
            foreach (var action in BuildDeliveryActions(record))
            {
                yield return action;
            }

            yield return BuildRunAction(
                id: $"{record.ReportId:D}:restate",
                kind: "restatement",
                label: "Create restatement",
                href: $"/api/fund-structure/reporting/packs/{reportId}/restatements",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
            yield return BuildRunAction(
                id: $"{record.ReportId:D}:archive",
                kind: "archive",
                label: "Archive published pack",
                href: $"/api/fund-structure/reporting/packs/{reportId}/archive",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }

        if (record.State == ReportPackWorkflowStateDto.Restated)
        {
            foreach (var action in BuildDeliveryActions(record))
            {
                yield return action;
            }

            yield return BuildRunAction(
                id: $"{record.ReportId:D}:archive",
                kind: "archive",
                label: "Archive restated pack",
                href: $"/api/fund-structure/reporting/packs/{reportId}/archive",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }
    }

    private static IEnumerable<WorkstationReportingRunNextActionPayload> BuildDeliveryActions(ReportPackWorkflowRecordDto record)
    {
        var reportId = EscapeReportId(record.ReportId);
        foreach (var policy in DistributionPolicies)
        {
            yield return BuildRunAction(
                id: $"{record.ReportId:D}:delivery:{policy.DistributionId}",
                kind: $"delivery:{policy.DistributionId}",
                label: $"Deliver to {policy.Recipient}",
                href: $"/api/fund-structure/reporting/packs/{reportId}/deliveries",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false);
        }
    }

    private static IEnumerable<string> BuildWorkflowStatusActions(ReportPackWorkflowRecordDto record)
    {
        if (record.Publication is not null)
        {
            yield return "published";
        }

        if (record.Restatement is not null)
        {
            yield return "restated";
        }

        if (record.Rejection is not null)
        {
            yield return "rejected";
        }
    }

    private static WorkstationReportingRunLinkPayload BuildRunLink(
        string id,
        string kind,
        string label,
        string href,
        string method,
        bool isBrowserNavigable,
        string source) =>
        new(id, kind, label, href, method, isBrowserNavigable, source);

    private static WorkstationReportingRunNextActionPayload BuildRunAction(
        string id,
        string kind,
        string label,
        string href,
        string method,
        bool isEnabled,
        string? disabledReason,
        bool isBrowserNavigable) =>
        new(id, kind, label, href, method, isEnabled, disabledReason, isBrowserNavigable);

    private static string EscapeReportId(Guid reportId) => Uri.EscapeDataString(reportId.ToString("D"));

    private static bool IsHttpRoute(string? route) => !string.IsNullOrWhiteSpace(route) && route.StartsWith("/", StringComparison.Ordinal);

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    private static readonly ReportPackDistributionPolicy[] DistributionPolicies =
    [
        new(
            "board-reporting-committee",
            "Board reporting committee",
            "Board",
            "Board portal",
            "fund-controller",
            "/reporting/report-packs?recipient=board",
            TimeSpan.FromHours(24),
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(4)),
        new(
            "investor-relations",
            "Investor relations",
            "Investor communications",
            "Investor portal",
            "investor-relations",
            "/reporting/report-packs?recipient=investor-relations",
            TimeSpan.FromHours(24),
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(4)),
        new(
            "compliance-archive",
            "Compliance archive",
            "Compliance",
            "Retained evidence vault",
            "compliance-reviewer",
            "/reporting/evidence?subject=report-pack",
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(2)),
        new(
            "fund-operations",
            "Fund operations",
            "Operations",
            "Operations close packet",
            "fund-operations",
            "/accounting/report-pack",
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(2))
    ];

    public sealed record ReportPackDistributionPolicy(
        string DistributionId,
        string Recipient,
        string RecipientRole,
        string Channel,
        string Owner,
        string Route,
        TimeSpan ApprovalSla,
        TimeSpan PublicationSla,
        TimeSpan DeliverySla,
        TimeSpan CorrectionSla);

    private sealed record UnifiedReportingRun(WorkstationReportingRunPayload Payload, DateTimeOffset UpdatedAtUtc);

    private sealed record ReportingLineChangeCounts(int Changed, int Added, int Removed);

    private sealed record ScheduleDeliveryReadiness(
        bool IsReady,
        string Summary,
        IReadOnlyList<string> Blockers);
}
