using System.Globalization;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Collectors;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Strategies.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Trading workspace payload composition for the workstation API surface: builds the live trading
/// payload (positions, orders, fills, risk, brokerage, readiness). When neither the execution layer
/// nor the strategy run read service is registered the builder returns null and the route responds
/// 503 — fabricated fixture data is never served as live. The inline trading route lambda and the
/// shared helpers (GetTradingOperatorReadinessAsync, NormalizeOperatorInboxToken,
/// BuildTradingBrokerageNotes, ResolveLiveMark, ResolveRuntimeRiskDescriptorAsync,
/// BuildModeComparisons, BuildRunDrillInLinks, FormatCurrency/FormatPercent) remain in core and are
/// reached across the partial.
/// </summary>
public static partial class WorkstationEndpoints
{
    // PR-03: returns typed DTO instead of anonymous object.
    // Returns null when neither the execution layer nor the strategy run read service is
    // registered so the route can respond 503 instead of serving fabricated positions and fills.
    private static async Task<WorkstationTradingPayload?> BuildTradingPayloadAsync(HttpContext context, Guid? fundAccountId = null)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        var portfolio = context.RequestServices.GetService<IPortfolioState>();
        var oms = context.RequestServices.GetService<IOrderManager>();
        var brokerageConfiguration = context.RequestServices.GetService<BrokerageConfiguration>();
        var quoteCollector = context.RequestServices.GetService<QuoteCollector>();
        var tradeCollector = context.RequestServices.GetService<TradeDataCollector>();

        if (portfolio is null && oms is null && readService is null)
        {
            return null;
        }

        // Resolve the most relevant paper run (for run-level metadata)
        StrategyRunSummary? run = null;
        if (readService is not null)
        {
            var runs = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false)).ToArray();
            run = runs.FirstOrDefault(static candidate => candidate.Mode == StrategyRunMode.Paper) ?? runs.FirstOrDefault();
        }

        var brokerageValidation = BrokerageValidationEvaluator.Evaluate(brokerageConfiguration);

        // --- Metrics (prefer live data, fall back to run-level metrics) ---
        var realisedPnl = portfolio?.RealisedPnl ?? run?.NetPnl ?? 0m;
        var unrealisedPnl = portfolio?.UnrealisedPnl ?? 0m;
        var totalPnl = realisedPnl + unrealisedPnl;
        var openOrderCount = oms?.GetOpenOrders().Count ?? 0;
        var pnlTone = totalPnl >= 0m ? "success" : "warning";

        // --- Positions (live execution layer when available) — PR-03: typed rows ---
        // Live marks (BBO mid → last trade → cost basis) drive MarkPrice, UnrealizedPnl,
        // and Exposure so operators see real-time PnL as quotes update.
        WorkstationTradingPositionRow[] positions;
        if (portfolio is not null && portfolio.Positions.Count > 0)
        {
            positions = portfolio.Positions.Values.Select(pos =>
            {
                var mark = ResolveLiveMark(pos.Symbol, quoteCollector, tradeCollector);
                var hasMark = mark.HasValue && mark.Value > 0m;
                var effectiveMark = hasMark ? mark!.Value : pos.AverageCostBasis;
                var liveUnrealized = (effectiveMark - pos.AverageCostBasis) * pos.Quantity;
                var liveExposure = Math.Abs(pos.Quantity * effectiveMark);

                return new WorkstationTradingPositionRow(
                    PositionKey: pos.Symbol,
                    Symbol: pos.Symbol,
                    Side: pos.Quantity >= 0 ? "Long" : "Short",
                    Quantity: Math.Abs(pos.Quantity).ToString(CultureInfo.InvariantCulture),
                    AveragePrice: pos.AverageCostBasis.ToString("F2", CultureInfo.InvariantCulture),
                    MarkPrice: hasMark ? effectiveMark.ToString("F2", CultureInfo.InvariantCulture) : "—",
                    DayPnl: "—",
                    UnrealizedPnl: FormatCurrency(hasMark ? liveUnrealized : pos.UnrealizedPnl),
                    Exposure: hasMark ? FormatCurrency(liveExposure) : "—");
            }).ToArray();
        }
        else
        {
            // No live positions yet — show an informational placeholder row
            positions =
            [
                new WorkstationTradingPositionRow("—", "—", "—", "—", "—", "—", "—", "—", "No open positions")
            ];
        }

        // --- Open orders (live OMS when available) — PR-03: typed rows ---
        WorkstationTradingOrderRow[] openOrders;
        if (oms is not null)
        {
            openOrders = oms.GetOpenOrders().Select(static order => new WorkstationTradingOrderRow(
                OrderId: order.OrderId.ToString(),
                Symbol: order.Symbol,
                Side: order.Side.ToString(),
                Type: order.Type.ToString(),
                Quantity: order.Quantity.ToString(CultureInfo.InvariantCulture),
                LimitPrice: order.LimitPrice.HasValue ? order.LimitPrice.Value.ToString("F2", CultureInfo.InvariantCulture) : "—",
                Status: order.Status.ToString(),
                SubmittedAt: order.CreatedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC")).ToArray();
        }
        else
        {
            openOrders = [];
        }

        // --- Risk state (derived from live portfolio when available) ---
        var riskState = "Healthy";
        var riskSummary = "Portfolio and order-book exposure are within configured paper thresholds.";
        // Guardrails come exclusively from the live rule registry below — no synthetic
        // placeholder entries when the runtime service is unavailable.
        IReadOnlyList<string> activeGuardrails = [];
        IReadOnlyList<WorkstationRiskGuardrail> guardrails = [];
        var grossExposure = 0m;
        var netExposureValue = 0m;

        if (portfolio is not null)
        {
            foreach (var pos in portfolio.Positions.Values)
            {
                var mark = ResolveLiveMark(pos.Symbol, quoteCollector, tradeCollector);
                var px = mark.HasValue && mark.Value > 0m ? mark.Value : pos.AverageCostBasis;
                grossExposure += Math.Abs(pos.Quantity * px);
                netExposureValue += pos.Quantity * px;
            }
            var drawdownPct = portfolio.PortfolioValue > 0m
                ? totalPnl / portfolio.PortfolioValue
                : 0m;

            if (drawdownPct < -0.05m)
            {
                riskState = "Constrained";
                riskSummary = "Portfolio has breached the 5% drawdown threshold. Promotion to live is blocked.";
            }
            else if (drawdownPct < -0.02m)
            {
                riskState = "Observe";
                riskSummary = "Exposure nearing guardrail limits. Monitoring intraday drawdown closely.";
            }
        }
        else if (run is not null && run.NetPnl.HasValue && run.NetPnl < 0m)
        {
            riskState = "Observe";
            riskSummary = "Strategy is running at a loss. Monitoring active.";
        }

        var runtimeRisk = await ResolveRuntimeRiskDescriptorAsync(context).ConfigureAwait(false);
        if (runtimeRisk is not null)
        {
            riskState = runtimeRisk.State;
            riskSummary = runtimeRisk.Summary;
            activeGuardrails = runtimeRisk.ActiveGuardrails;
            guardrails = runtimeRisk.Guardrails;
        }

        // The guardrail meters measure the same snapshot the pre-trade rules enforce
        // against, which reserves accepted-but-unfilled orders. Displaying filled-only
        // exposure beside them would show, say, $100k gross next to a gross guardrail
        // reading $160k and Constrained — so the headline figures come from that snapshot
        // whenever it is available, and fall back to filled positions when it is not.
        if (context.RequestServices.GetService<Meridian.Risk.IPortfolioExposureProvider>() is { } exposureProvider)
        {
            var exposureSnapshot = exposureProvider.GetSnapshot();
            grossExposure = exposureSnapshot.GrossExposure;
            netExposureValue = exposureSnapshot.NetExposure;
        }

        var maxDrawdownDisplay = portfolio is not null && portfolio.PortfolioValue > 0m
            ? FormatPercent(totalPnl / portfolio.PortfolioValue)
            : "—";

        var buyingPowerUsedDisplay = portfolio is not null && portfolio.BuyingPower > 0m
            ? FormatPercent(grossExposure / portfolio.BuyingPower)
            : "—";

        // --- Fills (completed orders from OMS) — PR-03: typed rows ---
        WorkstationTradingFillRow[] fills;
        if (oms is not null)
        {
            fills = oms.GetCompletedOrders(20).Select(static order => new WorkstationTradingFillRow(
                FillId: order.OrderId.ToString(),
                OrderId: order.OrderId.ToString(),
                Symbol: order.Symbol,
                Side: order.Side.ToString(),
                Quantity: order.FilledQuantity.ToString(CultureInfo.InvariantCulture),
                Price: order.AverageFillPrice.HasValue
                    ? order.AverageFillPrice.Value.ToString("F2", CultureInfo.InvariantCulture)
                    : "—",
                Venue: "Paper",
                Timestamp: (order.LastUpdatedAt ?? order.CreatedAt).ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC")).ToArray();
        }
        else
        {
            fills = Array.Empty<WorkstationTradingFillRow>();
        }

        var readiness = await GetTradingOperatorReadinessAsync(fundAccountId, context).ConfigureAwait(false);

        // PR-03: return typed DTO
        return new WorkstationTradingPayload(
            Metrics:
            [
                new WorkstationMetricCard("trading-net-pnl", "Net P&L", FormatCurrency(totalPnl), totalPnl >= 0m ? "+session" : "-session", pnlTone),
                new WorkstationMetricCard("trading-open-orders", "Open Orders", openOrderCount.ToString(CultureInfo.InvariantCulture), openOrderCount == 0 ? "0" : $"+{openOrderCount}", "default"),
                new WorkstationMetricCard("trading-cash", "Cash", portfolio is not null ? FormatCurrency(portfolio.Cash) : "—", "0%", "default"),
                new WorkstationMetricCard("trading-portfolio-value", "Portfolio Value", portfolio is not null ? FormatCurrency(portfolio.PortfolioValue) : "—", "0%", "default")
            ],
            Positions: positions,
            OpenOrders: openOrders,
            Fills: fills,
            Risk: new WorkstationTradingRiskState(
                State: riskState,
                Summary: riskSummary,
                NetExposure: portfolio is not null ? FormatCurrency(netExposureValue) : "—",
                GrossExposure: portfolio is not null ? FormatCurrency(grossExposure) : "—",
                Var95: "—",
                MaxDrawdown: maxDrawdownDisplay,
                BuyingPowerUsed: buyingPowerUsedDisplay,
                ActiveGuardrails: activeGuardrails,
                Guardrails: guardrails),
            Brokerage: new WorkstationTradingBrokerageState(
                Provider: brokerageValidation.GatewayDisplayName,
                Account: run is not null && !string.IsNullOrWhiteSpace(run.PortfolioId) ? run.PortfolioId : "—",
                Environment: run?.Mode == StrategyRunMode.Live ? "live" : "paper",
                Connection: portfolio is not null ? "Connected" : "Disconnected",
                LastHeartbeat: portfolio is not null ? "live" : "—",
                OrderIngress: oms is not null ? "healthy" : "—",
                FillFeed: portfolio is not null ? "healthy" : "—",
                Notes: [BuildTradingBrokerageNotes(run, portfolio is not null, brokerageConfiguration)]),
            Readiness: readiness,
            Comparisons: run is null ? Array.Empty<WorkstationModeComparisonGroup>() : BuildModeComparisons([run]),
            DrillIn: run is null ? null : BuildRunDrillInLinks(run));
    }

}
