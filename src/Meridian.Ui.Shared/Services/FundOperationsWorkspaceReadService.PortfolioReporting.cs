using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Export;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FundOperationsWorkspaceReadService
{

    private sealed record PortfolioReportingRunSource(
        string RunId,
        string StrategyId,
        string StrategyName,
        string? FundProfileId,
        string? FundDisplayName,
        PortfolioSummary Portfolio);

    private sealed record PortfolioReportingAnalyticsSource(
        PortfolioReportingAnalyticsScopeDto Scope,
        string Key,
        string Label,
        string? Symbol,
        string? Classification,
        string RunId,
        decimal RealizedPnl,
        decimal UnrealizedPnl);

    private sealed record PortfolioReportingAnalyticsGroup(
        PortfolioReportingAnalyticsScopeDto Scope,
        string Key,
        string Label,
        string? Symbol,
        string? Classification,
        decimal RealizedPnl,
        decimal UnrealizedPnl,
        decimal TotalPnl,
        int SourceCount);

    private sealed record CrossFundReportingSource(
        string FundKey,
        string FundLabel,
        string? EntityKey,
        string? EntityLabel,
        int AccountCount,
        int RunCount,
        decimal GrossExposure,
        decimal NetExposure,
        decimal LongMarketValue,
        decimal ShortMarketValue,
        decimal TotalCash,
        decimal PendingSettlement,
        decimal TotalPnl,
        decimal ShadowNav,
        DateTimeOffset? SourceAsOfUtc);

    private FundReportingSummaryDto BuildReportingSummary(
        string fundProfileId,
        IReadOnlyList<FundAccountSummary> accounts,
        FundLedgerSummary ledger,
        FundLedgerReconciliationSnapshot ledgerReconciliationSnapshot,
        CashFinancingSummary cashFinancing,
        FundNavAttributionSummaryDto nav,
        IReadOnlyList<PortfolioReportingRunSource> runSources,
        IReadOnlyList<CrossFundReportingConsolidationDto> crossFundConsolidations,
        DateTimeOffset asOf,
        ReportAccessQueryContext? accessContext = null)
    {
        var profiles = ExportProfile.GetBuiltInProfiles()
            .Select(static profile => new FundReportingProfileDto(
                Id: profile.Id,
                Name: profile.Name,
                TargetTool: profile.TargetTool,
                Format: profile.Format.ToString(),
                Description: profile.Description ?? string.Empty,
                LoaderScript: profile.IncludeLoaderScript,
                DataDictionary: profile.IncludeDataDictionary))
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var recommended = profiles
            .Where(static profile => profile.Id is "excel" or "python-pandas" or "postgresql" or "arrow-feather")
            .Select(static profile => profile.Id)
            .ToArray();
        var workflowRecords = FilterReportPackWorkflowRecords(BuildReportPackWorkflowRecords(accounts, asOf), accessContext);
        var scopedReportingPayload = accessContext is not null
            ? _reportPackRunReadService?.BuildPayload(accessContext)
            : null;
        var dailyWorkPayload = scopedReportingPayload ?? _reportPackRunReadService?.BuildPayload();
        var deliveryAttempts = scopedReportingPayload?.DeliveryAttempts
            ?? ReportingDeliveryReadModelSecurity.FilterVisibleAttempts(
                _reportPackDeliveryService?.ListAttempts(500) ?? [],
                accessContext,
                workflowRecords);
        var schedules = scopedReportingPayload?.Schedules
            ?? _reportingScheduleService?.ListSchedules(100)
            ?? [];
        var scheduleDeliveryPlans = scopedReportingPayload?.ScheduleDeliveryPlans
            ?? ReportPackRunReadService.BuildScheduleDeliveryPlans(schedules, deliveryAttempts);
        var distributions = scopedReportingPayload?.ReportPackDistributions
            ?? ReportPackRunReadService.BuildDistributionRecords(workflowRecords, deliveryAttempts);
        var reportLineProvenanceExplorer = scopedReportingPayload?.ReportLineProvenanceExplorer
            ?? FinancialRecordExplorerReadService.BuildReportLineProvenanceExplorer(
                workflowRecords,
                deliveryAttempts,
                accessContext: accessContext);
        var portfolioCuts = BuildPortfolioReportingCuts(accounts, cashFinancing, nav, runSources, asOf);
        var livePortfolioViews = BuildPortfolioReportingLiveViews(accounts.Count, portfolioCuts, runSources, asOf);
        var pnlSlices = BuildPortfolioReportingPnlSlices(runSources, cashFinancing.Currency, asOf);
        var analyticsRows = BuildPortfolioReportingAnalyticsRows(runSources, cashFinancing.Currency, asOf);
        var reportWriterDatasetSources = BuildReportWriterDatasetSources(
            portfolioCuts,
            analyticsRows,
            crossFundConsolidations);
        var structuredExports = BuildStructuredReportingExports(
            fundProfileId,
            ledger,
            ledgerReconciliationSnapshot,
            portfolioCuts,
            analyticsRows,
            crossFundConsolidations,
            cashFinancing.Currency,
            asOf);

        return new FundReportingSummaryDto(
            ProfileCount: profiles.Length,
            RecommendedProfiles: recommended,
            ReportPackDistributions: distributions,
            Profiles: profiles,
            Summary: $"{profiles.Length} export/reporting profiles are available for Accounting and Reporting workflows.",
            WorkflowRecords: workflowRecords,
            Schedules: schedules,
            DeliveryAttempts: deliveryAttempts,
            PortfolioCuts: portfolioCuts,
            StructuredExports: structuredExports,
            BrandingThemes: FundReportArtifactRenderer.BuiltInReportBrandingThemes,
            LivePortfolioViews: livePortfolioViews,
            CrossFundConsolidations: crossFundConsolidations,
            PnlSlices: pnlSlices,
            AnalyticsRows: analyticsRows,
            FundProfileId: fundProfileId,
            ScheduleDeliveryPlans: scheduleDeliveryPlans,
            ReportLineProvenanceExplorer: reportLineProvenanceExplorer,
            ReportWriterDatasetSources: reportWriterDatasetSources,
            AccessAudit: scopedReportingPayload?.AccessAudit,
            DailyWork: dailyWorkPayload?.DailyWork,
            StarterKits: dailyWorkPayload?.StarterKits,
            StarterKitState: dailyWorkPayload?.StarterKitState);
    }

    private static IReadOnlyList<WorkstationReportWriterDatasetSourcePayload> BuildReportWriterDatasetSources(
        IReadOnlyList<PortfolioReportingCutDto> portfolioCuts,
        IReadOnlyList<PortfolioReportingAnalyticsRowDto> analyticsRows,
        IReadOnlyList<CrossFundReportingConsolidationDto> crossFundConsolidations) =>
        ReportWriterDatasetSourceService.BuildDatasetSources(
            portfolioCuts,
            analyticsRows,
            crossFundConsolidations);

    private async Task<IReadOnlyList<CrossFundReportingConsolidationDto>> BuildCrossFundReportingConsolidationsAsync(
        string currentFundProfileId,
        string currency,
        IReadOnlyList<AccountWorkspaceProjection> allAccountProjections,
        IReadOnlyList<StrategyRunEntry> allRuns,
        DateTimeOffset asOf,
        CancellationToken ct)
    {
        var sources = new List<CrossFundReportingSource>(allAccountProjections.Count + allRuns.Count);
        var currentFundId = TranslateFundProfileId(currentFundProfileId);
        var fundProfileByFundId = allRuns
            .Where(static run => !string.IsNullOrWhiteSpace(run.FundProfileId))
            .GroupBy(run => TranslateFundProfileId(run.FundProfileId!.Trim()))
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static run => run.FundProfileId!.Trim())
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .First(),
                EqualityComparer<Guid>.Default);

        foreach (var projection in allAccountProjections)
        {
            ct.ThrowIfCancellationRequested();
            var account = projection.Summary;
            var fundKey = ResolveCrossFundAccountKey(projection.FundId, currentFundProfileId, currentFundId, fundProfileByFundId);
            var entityKey = account.EntityId?.ToString("D");
            var securitiesMarketValue = account.SecuritiesMarketValue;
            sources.Add(new CrossFundReportingSource(
                FundKey: fundKey,
                FundLabel: BuildCrossFundFundLabel(fundKey, currentFundProfileId),
                EntityKey: entityKey,
                EntityLabel: entityKey is null ? null : $"Entity {entityKey}",
                AccountCount: 1,
                RunCount: 0,
                GrossExposure: Math.Abs(securitiesMarketValue),
                NetExposure: securitiesMarketValue,
                LongMarketValue: Math.Max(securitiesMarketValue, 0m),
                ShortMarketValue: Math.Min(securitiesMarketValue, 0m),
                TotalCash: account.CashBalance,
                PendingSettlement: projection.LatestSnapshot?.PendingSettlement ?? 0m,
                TotalPnl: 0m,
                ShadowNav: account.NetAssetValue,
                SourceAsOfUtc: projection.LatestSnapshot is null
                    ? null
                    : new DateTimeOffset(projection.LatestSnapshot.AsOfDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)));
        }

        foreach (var runSource in await BuildPortfolioReportingRunSourcesAsync(allRuns, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            var fundKey = string.IsNullOrWhiteSpace(runSource.FundProfileId)
                ? "fund:unassigned-runs"
                : runSource.FundProfileId.Trim();
            var portfolio = runSource.Portfolio;
            sources.Add(new CrossFundReportingSource(
                FundKey: fundKey,
                FundLabel: string.IsNullOrWhiteSpace(runSource.FundDisplayName)
                    ? fundKey
                    : runSource.FundDisplayName.Trim(),
                EntityKey: null,
                EntityLabel: null,
                AccountCount: 0,
                RunCount: 1,
                GrossExposure: portfolio.GrossExposure,
                NetExposure: portfolio.NetExposure,
                LongMarketValue: portfolio.LongMarketValue,
                ShortMarketValue: portfolio.ShortMarketValue,
                TotalCash: portfolio.Cash,
                PendingSettlement: 0m,
                TotalPnl: portfolio.RealizedPnl + portfolio.UnrealizedPnl,
                ShadowNav: portfolio.TotalEquity,
                SourceAsOfUtc: portfolio.AsOf));
        }

        if (sources.Count == 0)
        {
            return
            [
                BuildCrossFundConsolidationRow(
                    "cross-fund:company",
                    "Company-wide consolidation",
                    CrossFundReportingConsolidationScopeDto.Company,
                    currency,
                    [],
                    asOf,
                    ["company", "cross-fund", "blocked"])
            ];
        }

        var rows = new List<CrossFundReportingConsolidationDto>
        {
            BuildCrossFundConsolidationRow(
                "cross-fund:company",
                "Company-wide consolidation",
                CrossFundReportingConsolidationScopeDto.Company,
                currency,
                sources,
                asOf,
                ["company", "cross-fund", "consolidated"])
        };

        rows.AddRange(sources
            .GroupBy(static source => source.FundKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildCrossFundConsolidationRow(
                $"cross-fund:fund:{NormalizeCutId(group.Key)}",
                group.OrderByDescending(static source => source.RunCount)
                    .Select(static source => source.FundLabel)
                    .FirstOrDefault(static label => !string.IsNullOrWhiteSpace(label))
                    ?? group.Key,
                CrossFundReportingConsolidationScopeDto.Fund,
                currency,
                group.ToArray(),
                asOf,
                ["fund", "cross-fund", "consolidated"])));

        rows.AddRange(sources
            .Where(static source => !string.IsNullOrWhiteSpace(source.EntityKey))
            .GroupBy(static source => source.EntityKey!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildCrossFundConsolidationRow(
                $"cross-fund:entity:{NormalizeCutId(group.Key)}",
                group.Select(static source => source.EntityLabel).FirstOrDefault(static label => !string.IsNullOrWhiteSpace(label)) ?? $"Entity {group.Key}",
                CrossFundReportingConsolidationScopeDto.Entity,
                currency,
                group.ToArray(),
                asOf,
                ["entity", "cross-fund", "consolidated"])));

        return rows;
    }

    private async Task<IReadOnlyList<PortfolioReportingRunSource>> BuildPortfolioReportingRunSourcesAsync(
        IReadOnlyList<StrategyRunEntry> runs,
        CancellationToken ct)
    {
        var runSources = new List<PortfolioReportingRunSource>(runs.Count);
        foreach (var run in runs)
        {
            ct.ThrowIfCancellationRequested();
            var portfolio = await _portfolioReadService.BuildSummaryAsync(run, ct).ConfigureAwait(false);
            if (portfolio is null)
            {
                continue;
            }

            runSources.Add(new PortfolioReportingRunSource(
                run.RunId,
                run.StrategyId,
                run.StrategyName,
                run.FundProfileId,
                run.FundDisplayName,
                portfolio));
        }

        return runSources;
    }

    private static CrossFundReportingConsolidationDto BuildCrossFundConsolidationRow(
        string consolidationId,
        string label,
        CrossFundReportingConsolidationScopeDto scope,
        string currency,
        IReadOnlyList<CrossFundReportingSource> sources,
        DateTimeOffset asOf,
        IReadOnlyList<string> tags)
    {
        var fundCount = sources.Select(static source => source.FundKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var entityCount = sources
            .Where(static source => !string.IsNullOrWhiteSpace(source.EntityKey))
            .Select(static source => source.EntityKey!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var accountCount = sources.Sum(static source => source.AccountCount);
        var runCount = sources.Sum(static source => source.RunCount);
        var sourceCount = accountCount + runCount;
        var grossExposure = sources.Sum(static source => source.GrossExposure);
        var netExposure = sources.Sum(static source => source.NetExposure);
        var shadowNav = sources.Sum(static source => source.ShadowNav);
        var shadowNavVariance = shadowNav - netExposure;
        var isReady = sourceCount > 0;

        return new CrossFundReportingConsolidationDto(
            ConsolidationId: consolidationId,
            Label: label,
            Scope: scope,
            Currency: currency,
            IsReady: isReady,
            FundCount: fundCount,
            EntityCount: entityCount,
            AccountCount: accountCount,
            RunCount: runCount,
            GrossExposure: grossExposure,
            NetExposure: netExposure,
            LongMarketValue: sources.Sum(static source => source.LongMarketValue),
            ShortMarketValue: sources.Sum(static source => source.ShortMarketValue),
            TotalCash: sources.Sum(static source => source.TotalCash),
            PendingSettlement: sources.Sum(static source => source.PendingSettlement),
            TotalPnl: sources.Sum(static source => source.TotalPnl),
            ShadowNav: shadowNav,
            ShadowNavVariance: shadowNavVariance,
            SourceCount: sourceCount,
            AsOf: asOf,
            Route: BuildCrossFundConsolidationRoute(consolidationId),
            ReadinessSummary: isReady
                ? $"{sourceCount} source record(s) across {fundCount} fund(s), {entityCount} entity row(s), {accountCount} account(s), and {runCount} run(s)."
                : "No active fund accounts or fund-scoped runs are available for cross-fund consolidation.",
            Tags: tags,
            VersionStamp: BuildCrossFundConsolidationVersionStamp(asOf, fundCount, entityCount, sourceCount));
    }

    private static string ResolveCrossFundAccountKey(
        Guid? accountFundId,
        string currentFundProfileId,
        Guid currentFundId,
        IReadOnlyDictionary<Guid, string> fundProfileByFundId)
    {
        if (accountFundId == currentFundId)
        {
            return currentFundProfileId;
        }

        if (accountFundId.HasValue && fundProfileByFundId.TryGetValue(accountFundId.Value, out var fundProfileId))
        {
            return fundProfileId;
        }

        return accountFundId?.ToString("D") ?? "fund:unassigned-accounts";
    }

    private static string BuildCrossFundFundLabel(string fundKey, string currentFundProfileId)
        => string.Equals(fundKey, currentFundProfileId, StringComparison.OrdinalIgnoreCase)
            ? currentFundProfileId
            : fundKey.StartsWith("fund:", StringComparison.OrdinalIgnoreCase)
                ? fundKey
                : $"Fund {fundKey}";

    private static string BuildCrossFundConsolidationRoute(string consolidationId)
        => UiApiRoutes.WithQuery(
            UiApiRoutes.WorkstationReporting,
            $"consolidationId={Uri.EscapeDataString(consolidationId)}");

    private static string BuildCrossFundConsolidationVersionStamp(
        DateTimeOffset asOf,
        int fundCount,
        int entityCount,
        int sourceCount)
        => $"cross-fund:{asOf.UtcDateTime:yyyyMMddHHmmss}:funds-{fundCount}:entities-{entityCount}:sources-{sourceCount}";

    private static IReadOnlyList<PortfolioReportingCutDto> BuildPortfolioReportingCuts(
        IReadOnlyList<FundAccountSummary> accounts,
        CashFinancingSummary cashFinancing,
        FundNavAttributionSummaryDto nav,
        IReadOnlyList<PortfolioReportingRunSource> runSources,
        DateTimeOffset asOf)
    {
        var cuts = new List<PortfolioReportingCutDto>
        {
            new(
                "fund:consolidated",
                "Consolidated fund",
                PortfolioReportingCutKindDto.Fund,
                cashFinancing.Currency,
                cashFinancing.GrossExposure,
                cashFinancing.NetExposure,
                cashFinancing.LongMarketValue,
                cashFinancing.ShortMarketValue,
                cashFinancing.TotalCash,
                cashFinancing.PendingSettlement,
                cashFinancing.RealizedPnl,
                cashFinancing.UnrealizedPnl,
                cashFinancing.RealizedPnl + cashFinancing.UnrealizedPnl,
                nav.TotalNav,
                nav.TotalNav - cashFinancing.TotalEquity,
                Math.Max(1, accounts.Count + runSources.Count),
                ["fund", "consolidated"],
                asOf,
                UiApiRoutes.FundReportPacks,
                "Shadow NAV is sourced from the shared NAV attribution service and compared with run equity.",
                BuildPortfolioCutVersionStamp(asOf, runSources.Count, accounts.Count))
        };

        cuts.AddRange(runSources
            .GroupBy(static source => string.IsNullOrWhiteSpace(source.StrategyId) ? source.StrategyName : source.StrategyId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group.OrderBy(static source => source.RunId, StringComparer.OrdinalIgnoreCase).ToArray();
                var first = ordered[0];
                var summary = ordered.Select(static source => source.Portfolio).ToArray();
                return new PortfolioReportingCutDto(
                    $"strategy:{NormalizeCutId(group.Key)}",
                    first.StrategyName,
                    PortfolioReportingCutKindDto.Strategy,
                    cashFinancing.Currency,
                    summary.Sum(static item => item.GrossExposure),
                    summary.Sum(static item => item.NetExposure),
                    summary.Sum(static item => item.LongMarketValue),
                    summary.Sum(static item => item.ShortMarketValue),
                    summary.Sum(static item => item.Cash),
                    0m,
                    summary.Sum(static item => item.RealizedPnl),
                    summary.Sum(static item => item.UnrealizedPnl),
                    summary.Sum(static item => item.RealizedPnl + item.UnrealizedPnl),
                    summary.Sum(static item => item.TotalEquity),
                    0m,
                    ordered.Length,
                    ordered.Select(static source => source.RunId).ToArray(),
                    asOf,
                    UiApiRoutes.FundReportPacks,
                    "Strategy shadow NAV uses the latest contributing run equity.",
                    BuildPortfolioCutVersionStamp(asOf, ordered.Length, 0));
            }));

        cuts.AddRange(accounts
            .GroupBy(static account => string.IsNullOrWhiteSpace(account.StructureLabel) ? "Unassigned" : account.StructureLabel.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var groupedAccounts = group.ToArray();
                var securitiesMarketValue = groupedAccounts.Sum(static account => account.SecuritiesMarketValue);
                var cash = groupedAccounts.Sum(static account => account.CashBalance);
                var shadowNav = groupedAccounts.Sum(static account => account.NetAssetValue);
                return new PortfolioReportingCutDto(
                    $"tag:{NormalizeCutId(group.Key)}",
                    group.Key,
                    PortfolioReportingCutKindDto.UserTag,
                    cashFinancing.Currency,
                    Math.Abs(securitiesMarketValue),
                    securitiesMarketValue,
                    Math.Max(securitiesMarketValue, 0m),
                    Math.Min(securitiesMarketValue, 0m),
                    cash,
                    0m,
                    0m,
                    0m,
                    0m,
                    shadowNav,
                    0m,
                    groupedAccounts.Length,
                    groupedAccounts.Select(static account => account.AccountCode).ToArray(),
                    asOf,
                    UiApiRoutes.FundReportPacks,
                    "Tag shadow NAV uses account-level net asset value until administrator NAV statements are linked.",
                    BuildPortfolioCutVersionStamp(asOf, 0, groupedAccounts.Length));
            }));

        return cuts
            .OrderBy(static cut => cut.Kind)
            .ThenBy(static cut => cut.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PortfolioReportingPnlSliceDto> BuildPortfolioReportingPnlSlices(
        IReadOnlyList<PortfolioReportingRunSource> runSources,
        string currency,
        DateTimeOffset asOf)
    {
        var endDate = DateOnly.FromDateTime(asOf.UtcDateTime);
        var boundedSources = runSources
            .Where(source => source.Portfolio.AsOf <= asOf)
            .ToArray();

        return
        [
            BuildPortfolioReportingPnlSlice(
                boundedSources,
                PortfolioReportingPnlSlicePeriodDto.Daily,
                "Daily P&L",
                currency,
                endDate,
                endDate,
                asOf),
            BuildPortfolioReportingPnlSlice(
                boundedSources,
                PortfolioReportingPnlSlicePeriodDto.Weekly,
                "Weekly P&L",
                currency,
                endDate.AddDays(-6),
                endDate,
                asOf),
            BuildPortfolioReportingPnlSlice(
                boundedSources,
                PortfolioReportingPnlSlicePeriodDto.Monthly,
                "Monthly P&L",
                currency,
                new DateOnly(endDate.Year, endDate.Month, 1),
                endDate,
                asOf),
            BuildPortfolioReportingPnlSlice(
                boundedSources,
                PortfolioReportingPnlSlicePeriodDto.Yearly,
                "Yearly P&L",
                currency,
                new DateOnly(endDate.Year, 1, 1),
                endDate,
                asOf)
        ];
    }

    private static PortfolioReportingPnlSliceDto BuildPortfolioReportingPnlSlice(
        IReadOnlyList<PortfolioReportingRunSource> runSources,
        PortfolioReportingPnlSlicePeriodDto period,
        string label,
        string currency,
        DateOnly startDate,
        DateOnly endDate,
        DateTimeOffset asOf)
    {
        var currentSources = SelectPortfolioReportingSourcesInWindow(runSources, startDate, endDate);
        var priorWindow = BuildPriorPnlWindow(period, startDate, endDate);
        var priorSources = SelectPortfolioReportingSourcesInWindow(runSources, priorWindow.Start, priorWindow.End);
        var realized = currentSources.Sum(static source => source.Portfolio.RealizedPnl);
        var unrealized = currentSources.Sum(static source => source.Portfolio.UnrealizedPnl);
        var total = realized + unrealized;
        var priorTotal = priorSources.Sum(static source => source.Portfolio.RealizedPnl + source.Portfolio.UnrealizedPnl);
        var normalizedPeriod = period.ToString().ToLowerInvariant();
        var sourceCount = currentSources.Count;

        return new PortfolioReportingPnlSliceDto(
            SliceId: $"pnl:{normalizedPeriod}",
            Period: period,
            Label: label,
            Currency: currency,
            StartDate: startDate,
            EndDate: endDate,
            RealizedPnl: realized,
            UnrealizedPnl: unrealized,
            TotalPnl: total,
            PriorTotalPnl: priorTotal,
            PnlChange: total - priorTotal,
            SourceCount: sourceCount,
            AsOf: asOf,
            Route: UiApiRoutes.WithQuery(UiApiRoutes.WorkstationReporting, $"pnlSlice={Uri.EscapeDataString(normalizedPeriod)}"),
            ReadinessSummary: BuildPortfolioReportingPnlSliceReadiness(period, sourceCount, priorSources.Count),
            Tags: sourceCount == 0
                ? ["pnl", normalizedPeriod, "blocked"]
                : ["pnl", normalizedPeriod, "source-backed"],
            VersionStamp: BuildPortfolioReportingPnlSliceVersionStamp(asOf, period, sourceCount, priorSources.Count));
    }

    private static (DateOnly Start, DateOnly End) BuildPriorPnlWindow(
        PortfolioReportingPnlSlicePeriodDto period,
        DateOnly startDate,
        DateOnly endDate)
        => period switch
        {
            PortfolioReportingPnlSlicePeriodDto.Daily => (startDate.AddDays(-1), endDate.AddDays(-1)),
            PortfolioReportingPnlSlicePeriodDto.Weekly => (startDate.AddDays(-7), startDate.AddDays(-1)),
            PortfolioReportingPnlSlicePeriodDto.Monthly => (startDate.AddMonths(-1), startDate.AddDays(-1)),
            PortfolioReportingPnlSlicePeriodDto.Yearly => (startDate.AddYears(-1), startDate.AddDays(-1)),
            _ => (startDate, endDate)
        };

    private static IReadOnlyList<PortfolioReportingRunSource> SelectPortfolioReportingSourcesInWindow(
        IReadOnlyList<PortfolioReportingRunSource> sources,
        DateOnly startDate,
        DateOnly endDate)
        => sources
            .Where(source =>
            {
                var sourceDate = DateOnly.FromDateTime(source.Portfolio.AsOf.UtcDateTime);
                return sourceDate.DayNumber >= startDate.DayNumber && sourceDate.DayNumber <= endDate.DayNumber;
            })
            .OrderBy(static source => source.Portfolio.AsOf)
            .ThenBy(static source => source.RunId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildPortfolioReportingPnlSliceReadiness(
        PortfolioReportingPnlSlicePeriodDto period,
        int sourceCount,
        int priorSourceCount)
    {
        var periodLabel = period.ToString().ToLowerInvariant();
        if (sourceCount <= 0)
        {
            return $"Blocked: no source-backed P&L runs are available for the {periodLabel} window.";
        }

        return priorSourceCount == 0
            ? $"{sourceCount} source-backed run(s) in the {periodLabel} window; no prior-period source run is available for comparison."
            : $"{sourceCount} source-backed run(s) in the {periodLabel} window; compared with {priorSourceCount} prior-period run(s).";
    }

    private static IReadOnlyList<PortfolioReportingAnalyticsRowDto> BuildPortfolioReportingAnalyticsRows(
        IReadOnlyList<PortfolioReportingRunSource> runSources,
        string currency,
        DateTimeOffset asOf)
    {
        var boundedSources = runSources
            .Where(source => source.Portfolio.AsOf <= asOf)
            .ToArray();
        var positionSources = boundedSources
            .SelectMany(source => source.Portfolio.Positions.Select(position =>
            {
                var classification = ResolvePortfolioAnalyticsClassification(position);
                return new PortfolioReportingAnalyticsSource(
                    Scope: PortfolioReportingAnalyticsScopeDto.Security,
                    Key: position.Symbol,
                    Label: position.Security?.DisplayName ?? position.Symbol,
                    Symbol: position.Symbol,
                    Classification: classification,
                    RunId: source.RunId,
                    RealizedPnl: position.RealizedPnl,
                    UnrealizedPnl: position.UnrealizedPnl);
            }))
            .ToArray();

        if (positionSources.Length == 0)
        {
            return
            [
                BuildPortfolioReportingAnalyticsBlockedRow(boundedSources.Length, currency, asOf)
            ];
        }

        var strategySources = boundedSources
            .Select(source => new PortfolioReportingAnalyticsSource(
                Scope: PortfolioReportingAnalyticsScopeDto.Strategy,
                Key: string.IsNullOrWhiteSpace(source.StrategyId) ? source.StrategyName : source.StrategyId,
                Label: source.StrategyName,
                Symbol: null,
                Classification: "Strategy",
                RunId: source.RunId,
                RealizedPnl: source.Portfolio.RealizedPnl,
                UnrealizedPnl: source.Portfolio.UnrealizedPnl))
            .ToArray();
        var assetClassSources = positionSources
            .Select(source => source with
            {
                Scope = PortfolioReportingAnalyticsScopeDto.AssetClass,
                Key = source.Classification ?? "Unclassified",
                Label = source.Classification ?? "Unclassified",
                Symbol = null
            })
            .ToArray();
        var securityGroups = GroupPortfolioReportingAnalyticsSources(positionSources);
        var allGroups = securityGroups
            .Concat(GroupPortfolioReportingAnalyticsSources(strategySources))
            .Concat(GroupPortfolioReportingAnalyticsSources(assetClassSources))
            .ToArray();
        var totalPnl = securityGroups.Sum(static group => group.TotalPnl);
        var absolutePnl = securityGroups.Sum(static group => Math.Abs(group.TotalPnl));
        var rows = new List<PortfolioReportingAnalyticsRowDto>();

        rows.AddRange(BuildPortfolioReportingAnalyticsRows(
            PortfolioReportingAnalyticsKindDto.TopWinner,
            securityGroups
                .Where(static group => group.TotalPnl > 0m)
                .OrderByDescending(static group => group.TotalPnl)
                .ThenBy(static group => group.Label, StringComparer.OrdinalIgnoreCase)
                .Take(5),
            totalPnl,
            absolutePnl,
            currency,
            asOf));
        rows.AddRange(BuildPortfolioReportingAnalyticsRows(
            PortfolioReportingAnalyticsKindDto.TopLaggard,
            securityGroups
                .Where(static group => group.TotalPnl < 0m)
                .OrderBy(static group => group.TotalPnl)
                .ThenBy(static group => group.Label, StringComparer.OrdinalIgnoreCase)
                .Take(5),
            totalPnl,
            absolutePnl,
            currency,
            asOf));
        rows.AddRange(BuildPortfolioReportingAnalyticsRows(
            PortfolioReportingAnalyticsKindDto.Contribution,
            allGroups
                .Where(static group => group.TotalPnl != 0m)
                .OrderByDescending(static group => Math.Abs(group.TotalPnl))
                .ThenBy(static group => group.Scope)
                .ThenBy(static group => group.Label, StringComparer.OrdinalIgnoreCase)
                .Take(12),
            totalPnl,
            absolutePnl,
            currency,
            asOf));

        return rows.Count == 0
            ?
            [
                BuildPortfolioReportingAnalyticsBlockedRow(boundedSources.Length, currency, asOf)
            ]
            : rows;
    }

    private static IReadOnlyList<PortfolioReportingAnalyticsGroup> GroupPortfolioReportingAnalyticsSources(
        IReadOnlyList<PortfolioReportingAnalyticsSource> sources)
        => sources
            .GroupBy(
                static source => (source.Scope, Key: source.Key.Trim()),
                source => source,
                (key, group) =>
                {
                    var ordered = group
                        .OrderBy(static source => source.Label, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(static source => source.RunId, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var first = ordered[0];
                    var realized = ordered.Sum(static source => source.RealizedPnl);
                    var unrealized = ordered.Sum(static source => source.UnrealizedPnl);
                    return new PortfolioReportingAnalyticsGroup(
                        Scope: key.Scope,
                        Key: key.Key,
                        Label: first.Label,
                        Symbol: first.Symbol,
                        Classification: first.Classification,
                        RealizedPnl: realized,
                        UnrealizedPnl: unrealized,
                        TotalPnl: realized + unrealized,
                        SourceCount: ordered.Select(static source => source.RunId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                })
            .ToArray();

    private static IReadOnlyList<PortfolioReportingAnalyticsRowDto> BuildPortfolioReportingAnalyticsRows(
        PortfolioReportingAnalyticsKindDto kind,
        IEnumerable<PortfolioReportingAnalyticsGroup> groups,
        decimal totalPnl,
        decimal absolutePnl,
        string currency,
        DateTimeOffset asOf)
    {
        var rank = 0;
        return groups
            .Select(group =>
            {
                rank++;
                var contributionPercent = totalPnl == 0m
                    ? 0m
                    : decimal.Round(group.TotalPnl / totalPnl * 100m, 4, MidpointRounding.AwayFromZero);
                var heatMapIntensity = absolutePnl == 0m
                    ? 0m
                    : decimal.Round(Math.Abs(group.TotalPnl) / absolutePnl * 100m, 4, MidpointRounding.AwayFromZero);
                var analyticsId = BuildPortfolioReportingAnalyticsId(kind, group.Scope, group.Key);
                return new PortfolioReportingAnalyticsRowDto(
                    AnalyticsId: analyticsId,
                    Kind: kind,
                    Scope: group.Scope,
                    Rank: rank,
                    Label: group.Label,
                    Symbol: group.Symbol,
                    Classification: group.Classification,
                    Currency: currency,
                    RealizedPnl: group.RealizedPnl,
                    UnrealizedPnl: group.UnrealizedPnl,
                    TotalPnl: group.TotalPnl,
                    ContributionPercent: contributionPercent,
                    HeatMapIntensity: heatMapIntensity,
                    SourceCount: group.SourceCount,
                    AsOf: asOf,
                    Route: UiApiRoutes.WithQuery(UiApiRoutes.WorkstationReporting, $"analyticsId={Uri.EscapeDataString(analyticsId)}"),
                    ReadinessSummary: BuildPortfolioReportingAnalyticsReadiness(kind, group, contributionPercent, heatMapIntensity),
                    Tags: BuildPortfolioReportingAnalyticsTags(kind, group),
                    VersionStamp: BuildPortfolioReportingAnalyticsVersionStamp(asOf, kind, group.Scope, group.SourceCount));
            })
            .ToArray();
    }

    private static PortfolioReportingAnalyticsRowDto BuildPortfolioReportingAnalyticsBlockedRow(
        int sourceCount,
        string currency,
        DateTimeOffset asOf)
    {
        const string analyticsId = "analytics:blocked";
        return new PortfolioReportingAnalyticsRowDto(
            AnalyticsId: analyticsId,
            Kind: PortfolioReportingAnalyticsKindDto.Contribution,
            Scope: PortfolioReportingAnalyticsScopeDto.Security,
            Rank: 0,
            Label: "No source-backed position P&L",
            Symbol: null,
            Classification: null,
            Currency: currency,
            RealizedPnl: 0m,
            UnrealizedPnl: 0m,
            TotalPnl: 0m,
            ContributionPercent: 0m,
            HeatMapIntensity: 0m,
            SourceCount: sourceCount,
            AsOf: asOf,
            Route: UiApiRoutes.WithQuery(UiApiRoutes.WorkstationReporting, $"analyticsId={Uri.EscapeDataString(analyticsId)}"),
            ReadinessSummary: sourceCount == 0
                ? "Blocked: no source-backed portfolio runs are available for Top-N and contribution analytics."
                : $"Blocked: {sourceCount} portfolio run(s) are available, but none include position-level P&L rows for analytics.",
            Tags: ["analytics", "blocked"],
            VersionStamp: BuildPortfolioReportingAnalyticsVersionStamp(
                asOf,
                PortfolioReportingAnalyticsKindDto.Contribution,
                PortfolioReportingAnalyticsScopeDto.Security,
                sourceCount));
    }

    private static string ResolvePortfolioAnalyticsClassification(PortfolioPositionSummary position)
        => string.IsNullOrWhiteSpace(position.Security?.AssetClass)
            ? "Unclassified"
            : position.Security.AssetClass.Trim();

    private static string BuildPortfolioReportingAnalyticsReadiness(
        PortfolioReportingAnalyticsKindDto kind,
        PortfolioReportingAnalyticsGroup group,
        decimal contributionPercent,
        decimal heatMapIntensity)
        => kind switch
        {
            PortfolioReportingAnalyticsKindDto.TopWinner => $"Top-N winner from {group.SourceCount} source-backed run(s); contributes {FormatAnalyticsPercent(contributionPercent)} of portfolio P&L.",
            PortfolioReportingAnalyticsKindDto.TopLaggard => $"Top-N laggard from {group.SourceCount} source-backed run(s); contributes {FormatAnalyticsPercent(contributionPercent)} of portfolio P&L.",
            _ => $"{group.SourceCount} source-backed run(s); contribution is {FormatAnalyticsPercent(contributionPercent)} of portfolio P&L with {FormatAnalyticsPercent(heatMapIntensity)} heat-map intensity."
        };

    private static IReadOnlyList<string> BuildPortfolioReportingAnalyticsTags(
        PortfolioReportingAnalyticsKindDto kind,
        PortfolioReportingAnalyticsGroup group)
        =>
        [
            "analytics",
            kind.ToString().ToLowerInvariant(),
            group.Scope.ToString().ToLowerInvariant(),
            NormalizeCutId(group.Classification ?? group.Label)
        ];

    private static string BuildPortfolioReportingAnalyticsId(
        PortfolioReportingAnalyticsKindDto kind,
        PortfolioReportingAnalyticsScopeDto scope,
        string key)
        => $"analytics:{kind.ToString().ToLowerInvariant()}:{scope.ToString().ToLowerInvariant()}:{NormalizeCutId(key)}";

    private static string FormatAnalyticsPercent(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string BuildPortfolioCutVersionStamp(DateTimeOffset asOf, int runCount, int accountCount) =>
        $"portfolio-cut:{asOf.UtcDateTime:yyyyMMddHHmmss}:runs-{runCount}:accounts-{accountCount}";

    private static string BuildPortfolioReportingPnlSliceVersionStamp(
        DateTimeOffset asOf,
        PortfolioReportingPnlSlicePeriodDto period,
        int sourceCount,
        int priorSourceCount)
        => $"pnl-slice:{asOf.UtcDateTime:yyyyMMddHHmmss}:{period.ToString().ToLowerInvariant()}:sources-{sourceCount}:prior-{priorSourceCount}";

    private static string BuildPortfolioReportingAnalyticsVersionStamp(
        DateTimeOffset asOf,
        PortfolioReportingAnalyticsKindDto kind,
        PortfolioReportingAnalyticsScopeDto scope,
        int sourceCount)
        => $"analytics:{asOf.UtcDateTime:yyyyMMddHHmmss}:{kind.ToString().ToLowerInvariant()}:{scope.ToString().ToLowerInvariant()}:sources-{sourceCount}";
}
