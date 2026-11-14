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

    private static readonly TimeSpan LivePortfolioViewFreshnessWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LivePortfolioViewStaleWindow = TimeSpan.FromHours(24);

    private static IReadOnlyList<PortfolioReportingLiveViewDto> BuildPortfolioReportingLiveViews(
        int accountSourceCount,
        IReadOnlyList<PortfolioReportingCutDto> cuts,
        IReadOnlyList<PortfolioReportingRunSource> runSources,
        DateTimeOffset asOf)
    {
        var strategySources = runSources
            .GroupBy(static source => string.IsNullOrWhiteSpace(source.StrategyId) ? source.StrategyName : source.StrategyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => $"strategy:{NormalizeCutId(group.Key)}",
                group =>
                {
                    var ordered = group.OrderBy(static source => source.RunId, StringComparer.OrdinalIgnoreCase).ToArray();
                    var latestSourceAsOf = ordered.Max(static source => source.Portfolio.AsOf);
                    var cashLadderRoute = ordered.Length == 1
                        ? UiApiRoutes.WithParam(UiApiRoutes.PortfolioCashFlows, "runId", ordered[0].RunId)
                        : null;
                    return (StrategyId: ordered[0].StrategyId, LatestSourceAsOf: latestSourceAsOf, CashLadderRoute: cashLadderRoute);
                },
                StringComparer.OrdinalIgnoreCase);
        var latestFundSourceAsOf = runSources.Count == 0
            ? (DateTimeOffset?)null
            : runSources.Max(static source => source.Portfolio.AsOf);

        return cuts
            .Select(cut =>
            {
                strategySources.TryGetValue(cut.CutId, out var strategySource);
                var actualSourceCount = cut.Kind == PortfolioReportingCutKindDto.Fund
                    ? accountSourceCount + runSources.Count
                    : cut.SourceCount;
                var sourceAsOf = cut.Kind switch
                {
                    PortfolioReportingCutKindDto.Fund => latestFundSourceAsOf ?? (accountSourceCount > 0 ? asOf : null),
                    PortfolioReportingCutKindDto.Strategy => strategySource.LatestSourceAsOf == default ? null : strategySource.LatestSourceAsOf,
                    PortfolioReportingCutKindDto.UserTag => actualSourceCount > 0 ? asOf : null,
                    _ => null
                };
                var state = ResolveLivePortfolioViewState(actualSourceCount, sourceAsOf, asOf);
                var route = BuildLivePortfolioViewRoute(cut, strategySource.StrategyId);
                var marketTickAgeSeconds = CalculateLivePortfolioTickAgeSeconds(sourceAsOf, asOf);

                return new PortfolioReportingLiveViewDto(
                    ViewId: $"live:{cut.CutId}",
                    Label: cut.Label,
                    Kind: cut.Kind,
                    State: state,
                    Currency: cut.Currency,
                    GrossExposure: cut.GrossExposure,
                    NetExposure: cut.NetExposure,
                    TotalCash: cut.TotalCash,
                    PendingSettlement: cut.PendingSettlement,
                    TotalPnl: cut.TotalPnl,
                    ShadowNav: cut.ShadowNav,
                    AsOf: cut.AsOf,
                    SourceAsOfUtc: sourceAsOf,
                    SourceCount: actualSourceCount,
                    Route: route,
                    LiquiditySummary: BuildLivePortfolioLiquiditySummary(cut),
                    CashLadderSummary: BuildLivePortfolioCashLadderSummary(cut, strategySource.CashLadderRoute),
                    TelemetrySummary: BuildLivePortfolioTelemetrySummary(state, sourceAsOf, asOf),
                    Tags: cut.Tags,
                    CashLadderRoute: strategySource.CashLadderRoute,
                    VersionStamp: cut.VersionStamp,
                    ReadinessBlockers: BuildLivePortfolioReadinessBlockers(state, sourceAsOf, asOf),
                    MarketTickAsOfUtc: sourceAsOf,
                    MarketTickAgeSeconds: marketTickAgeSeconds,
                    MarketTickSequence: sourceAsOf?.ToUnixTimeMilliseconds(),
                    MarketDataProvider: BuildLivePortfolioMarketDataProvider(state, cut.Kind),
                    TickFreshnessSummary: BuildLivePortfolioTickFreshnessSummary(state, sourceAsOf, asOf, marketTickAgeSeconds),
                    IsMarketTickLinked: state == PortfolioReportingLiveViewStateDto.LiveLinked,
                    FreshnessPolicy: BuildLivePortfolioFreshnessPolicy(state, sourceAsOf, asOf, marketTickAgeSeconds));
            })
            .OrderBy(static view => view.Kind)
            .ThenBy(static view => view.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PortfolioReportingLiveViewStateDto ResolveLivePortfolioViewState(
        int sourceCount,
        DateTimeOffset? sourceAsOf,
        DateTimeOffset requestedAsOf)
    {
        if (sourceCount <= 0 || sourceAsOf is null)
        {
            return PortfolioReportingLiveViewStateDto.Blocked;
        }

        var age = requestedAsOf - sourceAsOf.Value;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age <= LivePortfolioViewFreshnessWindow)
        {
            return PortfolioReportingLiveViewStateDto.LiveLinked;
        }

        if (age > LivePortfolioViewStaleWindow)
        {
            return PortfolioReportingLiveViewStateDto.Stale;
        }

        return PortfolioReportingLiveViewStateDto.SourceBacked;
    }

    private static string BuildLivePortfolioViewRoute(PortfolioReportingCutDto cut, string? strategyId)
    {
        var query = cut.Kind switch
        {
            PortfolioReportingCutKindDto.Strategy when !string.IsNullOrWhiteSpace(strategyId) => string.Join('&',
                "fundAccountId=all",
                $"strategyId={Uri.EscapeDataString(strategyId.Trim())}",
                "entity=portfolio"),
            PortfolioReportingCutKindDto.UserTag => string.Join('&',
                "fundAccountId=all",
                "strategyId=all",
                $"entity={Uri.EscapeDataString(cut.Label)}"),
            _ => "fundAccountId=all&strategyId=all&entity=portfolio"
        };

        return UiApiRoutes.WithQuery(UiApiRoutes.WorkstationPortfolioSummary, query);
    }

    private static string BuildLivePortfolioLiquiditySummary(PortfolioReportingCutDto cut)
        => cut.PendingSettlement == 0m
            ? $"{cut.TotalCash:N2} {cut.Currency} cash is available with no pending settlement in this reporting cut."
            : $"{cut.TotalCash:N2} {cut.Currency} cash with {cut.PendingSettlement:N2} pending settlement in this reporting cut.";

    private static string BuildLivePortfolioCashLadderSummary(PortfolioReportingCutDto cut, string? cashLadderRoute)
        => string.IsNullOrWhiteSpace(cashLadderRoute)
            ? "Open the live portfolio summary route for consolidated cash and liquidity posture."
            : $"Run cash-ladder evidence is available for {cut.Label}.";

    private static string BuildLivePortfolioTelemetrySummary(
        PortfolioReportingLiveViewStateDto state,
        DateTimeOffset? sourceAsOf,
        DateTimeOffset requestedAsOf)
        => state switch
        {
            PortfolioReportingLiveViewStateDto.Blocked => "No source-backed portfolio records are available for this live reporting view.",
            PortfolioReportingLiveViewStateDto.Stale => $"Latest source snapshot is older than 24 hours as of {FormatStructuredDateTime(requestedAsOf)}.",
            PortfolioReportingLiveViewStateDto.LiveLinked => sourceAsOf is null
                ? "Live-linked portfolio telemetry is available through the live portfolio summary route."
                : $"Live-linked portfolio telemetry is current through {FormatStructuredDateTime(sourceAsOf.Value)}; open the route for the latest portfolio-summary telemetry.",
            _ => sourceAsOf is null
                ? "Source-backed portfolio telemetry is available through the live portfolio summary route."
                : $"Source-backed portfolio telemetry is current through {FormatStructuredDateTime(sourceAsOf.Value)}; open the route for latest portfolio-summary telemetry."
        };

    private static int? CalculateLivePortfolioTickAgeSeconds(DateTimeOffset? sourceAsOf, DateTimeOffset requestedAsOf)
    {
        if (sourceAsOf is null)
        {
            return null;
        }

        var age = requestedAsOf - sourceAsOf.Value;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return (int)Math.Min(int.MaxValue, Math.Round(age.TotalSeconds, MidpointRounding.AwayFromZero));
    }

    private static string BuildLivePortfolioMarketDataProvider(
        PortfolioReportingLiveViewStateDto state,
        PortfolioReportingCutKindDto kind)
        => state switch
        {
            PortfolioReportingLiveViewStateDto.LiveLinked => kind == PortfolioReportingCutKindDto.Strategy
                ? "portfolio-summary-live-strategy"
                : "portfolio-summary-live",
            PortfolioReportingLiveViewStateDto.SourceBacked => "retained-portfolio-snapshot",
            PortfolioReportingLiveViewStateDto.Stale => "stale-portfolio-snapshot",
            _ => "unavailable"
        };

    private static string BuildLivePortfolioTickFreshnessSummary(
        PortfolioReportingLiveViewStateDto state,
        DateTimeOffset? sourceAsOf,
        DateTimeOffset requestedAsOf,
        int? marketTickAgeSeconds)
        => state switch
        {
            PortfolioReportingLiveViewStateDto.Blocked => "No market tick snapshot is available for this reporting live view.",
            PortfolioReportingLiveViewStateDto.LiveLinked => marketTickAgeSeconds is null
                ? "Market tick snapshot is live-linked, but its source timestamp is unavailable."
                : $"Market tick snapshot is {marketTickAgeSeconds} second(s) old and inside the 5-minute live-link window.",
            PortfolioReportingLiveViewStateDto.Stale => sourceAsOf is null
                ? "Market tick snapshot timestamp is missing and cannot satisfy the 24-hour freshness window."
                : $"Market tick snapshot from {FormatStructuredDateTime(sourceAsOf.Value)} is outside the 24-hour freshness window as of {FormatStructuredDateTime(requestedAsOf)}.",
            _ => marketTickAgeSeconds is null
                ? "Source-backed tick snapshot timestamp is unavailable."
                : $"Source-backed tick snapshot is {marketTickAgeSeconds} second(s) old; refresh the live portfolio route for current ticks."
        };

    private static PortfolioReportingLiveViewFreshnessPolicyDto BuildLivePortfolioFreshnessPolicy(
        PortfolioReportingLiveViewStateDto state,
        DateTimeOffset? sourceAsOf,
        DateTimeOffset requestedAsOf,
        int? sourceAgeSeconds)
    {
        var isWithinLiveLinkWindow = sourceAgeSeconds.HasValue && sourceAgeSeconds.Value <= (int)LivePortfolioViewFreshnessWindow.TotalSeconds;
        var isBeyondStaleWindow = sourceAsOf is null || (sourceAgeSeconds.HasValue && sourceAgeSeconds.Value > (int)LivePortfolioViewStaleWindow.TotalSeconds);

        return new PortfolioReportingLiveViewFreshnessPolicyDto(
            PolicyName: "LivePortfolioView",
            EvaluatedAtUtc: requestedAsOf,
            SourceAgeSeconds: sourceAgeSeconds,
            LiveLinkWindowSeconds: (int)LivePortfolioViewFreshnessWindow.TotalSeconds,
            StaleWindowSeconds: (int)LivePortfolioViewStaleWindow.TotalSeconds,
            IsWithinLiveLinkWindow: state == PortfolioReportingLiveViewStateDto.LiveLinked && isWithinLiveLinkWindow,
            IsBeyondStaleWindow: state == PortfolioReportingLiveViewStateDto.Stale && isBeyondStaleWindow,
            Reason: BuildLivePortfolioFreshnessPolicyReason(state, sourceAsOf, requestedAsOf, sourceAgeSeconds));
    }

    private static string BuildLivePortfolioFreshnessPolicyReason(
        PortfolioReportingLiveViewStateDto state,
        DateTimeOffset? sourceAsOf,
        DateTimeOffset requestedAsOf,
        int? sourceAgeSeconds)
        => state switch
        {
            PortfolioReportingLiveViewStateDto.Blocked => "No source timestamp is available, so live portfolio freshness fails closed.",
            PortfolioReportingLiveViewStateDto.LiveLinked => sourceAgeSeconds is null
                ? "The source timestamp is unavailable even though the view is marked live-linked."
                : $"Source age is {sourceAgeSeconds} second(s), inside the {LivePortfolioViewFreshnessWindow.TotalMinutes:0}-minute live-link window.",
            PortfolioReportingLiveViewStateDto.Stale => sourceAsOf is null
                ? "The source timestamp is missing and cannot satisfy the stale-window policy."
                : $"Source timestamp {FormatStructuredDateTime(sourceAsOf.Value)} is older than the {LivePortfolioViewStaleWindow.TotalHours:0}-hour stale window at {FormatStructuredDateTime(requestedAsOf)}.",
            _ => sourceAgeSeconds is null
                ? "Source-backed view has no source timestamp, so the latest source evidence should be refreshed."
                : $"Source age is {sourceAgeSeconds} second(s), outside the live-link window but inside the {LivePortfolioViewStaleWindow.TotalHours:0}-hour stale window."
        };

    private static IReadOnlyList<string> BuildLivePortfolioReadinessBlockers(
        PortfolioReportingLiveViewStateDto state,
        DateTimeOffset? sourceAsOf,
        DateTimeOffset requestedAsOf)
    {
        if (state == PortfolioReportingLiveViewStateDto.Blocked)
        {
            return ["No source-backed portfolio records are available for this live reporting view."];
        }

        if (state == PortfolioReportingLiveViewStateDto.Stale)
        {
            return sourceAsOf is null
                ? ["Latest source snapshot timestamp is missing for this live reporting view."]
                : [$"Latest source snapshot is older than 24 hours as of {FormatStructuredDateTime(requestedAsOf)}."];
        }

        return [];
    }
}
