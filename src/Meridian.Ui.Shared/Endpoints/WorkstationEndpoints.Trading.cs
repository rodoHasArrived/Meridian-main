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
/// payload (positions, orders, fills, risk, brokerage, readiness) and the fixture fallback payload /
/// readiness. Split out of the WorkstationEndpoints core partial as a behavior-preserving relocation;
/// the inline trading route lambda and the shared helpers (GetTradingOperatorReadinessAsync,
/// NormalizeOperatorInboxToken, BuildTradingBrokerageNotes, ResolveLiveMark,
/// ResolveRuntimeRiskDescriptorAsync, BuildModeComparisons, BuildRunDrillInLinks, FormatCurrency/
/// FormatPercent) remain in core and are reached across the partial.
/// </summary>
public static partial class WorkstationEndpoints
{
    // PR-03: returns typed DTO instead of anonymous object
    private static async Task<WorkstationTradingPayload> BuildTradingPayloadAsync(HttpContext context, Guid? fundAccountId = null)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        var portfolio = context.RequestServices.GetService<IPortfolioState>();
        var oms = context.RequestServices.GetService<IOrderManager>();
        var brokerageConfiguration = context.RequestServices.GetService<BrokerageConfiguration>();
        var quoteCollector = context.RequestServices.GetService<QuoteCollector>();
        var tradeCollector = context.RequestServices.GetService<TradeDataCollector>();

        // When neither execution layer nor strategy run service is active, use fixture data
        if (portfolio is null && oms is null && readService is null)
        {
            return BuildTradingFallbackPayload();
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
        IReadOnlyList<string> activeGuardrails =
        [
            "Single-name concentration cap set at 30% notional.",
            "Auto-throttle activates above 70% intraday buying power.",
            "Strategy promotion to live blocked while state is Observe or Constrained."
        ];
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
                ActiveGuardrails: activeGuardrails),
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

    // PR-03: returns typed DTO
    private static WorkstationTradingPayload BuildTradingFallbackPayload()
    {
        return new WorkstationTradingPayload(
            Metrics:
            [
                new WorkstationMetricCard("trading-net-pnl", "Net P&L", "+$3,918", "+2.4%", "success"),
                new WorkstationMetricCard("trading-open-orders", "Open Orders", "5", "+1", "default"),
                new WorkstationMetricCard("trading-fills", "Fills Today", "27", "+7", "success"),
                new WorkstationMetricCard("trading-risk-state", "Risk State", "Healthy", "0%", "success")
            ],
            Positions:
            [
                new WorkstationTradingPositionRow("AAPL", "AAPL", "Long", "300", "188.22", "189.30", "+$324", "+$1,126", "$56,790"),
                new WorkstationTradingPositionRow("MSFT", "MSFT", "Long", "150", "416.10", "414.80", "-$195", "-$195", "$62,220")
            ],
            OpenOrders:
            [
                new WorkstationTradingOrderRow("PO-24812", "AMZN", "Buy", "Limit", "100", "184.00", "Working", "09:35:12 ET"),
                new WorkstationTradingOrderRow("PO-24814", "QQQ", "Sell", "Stop", "40", "442.30", "Pending Routing", "09:36:48 ET")
            ],
            Fills:
            [
                new WorkstationTradingFillRow("FL-90071", "PO-24810", "AAPL", "Buy", "50", "188.12", "NASDAQ", "09:33:04 ET"),
                new WorkstationTradingFillRow("FL-90077", "PO-24811", "MSFT", "Sell", "25", "414.88", "IEX", "09:34:26 ET")
            ],
            Risk: new WorkstationTradingRiskState(
                State: "Healthy",
                Summary: "Portfolio and order-book exposure are within configured paper thresholds.",
                NetExposure: "$119,010",
                GrossExposure: "$156,432",
                Var95: "$9,874",
                MaxDrawdown: "-0.9%",
                BuyingPowerUsed: "44%",
                ActiveGuardrails:
                [
                    "Daily loss guard set to -$12,000.",
                    "Max position notional guard set to $120,000.",
                    "Kill-switch can be engaged manually from Accounting review."
                ]),
            Brokerage: new WorkstationTradingBrokerageState(
                Provider: "Interactive Brokers",
                Account: "DU1009034",
                Environment: "paper",
                Connection: "Connected",
                LastHeartbeat: "1s ago",
                OrderIngress: "healthy (p50 19ms)",
                FillFeed: "healthy (p50 31ms)",
                Notes: ["Paper execution routing is synchronized with run-level reconciliation wiring."]),
            Readiness: BuildTradingFallbackReadiness(),
            Comparisons: Array.Empty<WorkstationModeComparisonGroup>(),
            DrillIn: null);
    }

    private static TradingOperatorReadinessDto BuildTradingFallbackReadiness()
    {
        var asOf = DateTimeOffset.UtcNow;
        return new TradingOperatorReadinessDto(
            AsOf: asOf,
            ActiveSession: null,
            Sessions: Array.Empty<TradingPaperSessionReadinessDto>(),
            Replay: null,
            Controls: new TradingControlReadinessDto(
                CircuitBreakerOpen: false,
                CircuitBreakerReason: null,
                CircuitBreakerChangedBy: null,
                CircuitBreakerChangedAt: null,
                ManualOverrideCount: 0,
                SymbolLimitCount: 0,
                DefaultMaxPositionSize: null),
            Promotion: null,
            TrustGate: new TradingTrustGateReadinessDto(
                GateId: "dk1-trust-gate",
                Status: "Blocked",
                ReadyForOperatorReview: false,
                OperatorSignoffRequired: true,
                OperatorSignoffStatus: "missing",
                GeneratedAt: asOf,
                PacketPath: null,
                SourceSummary: null,
                RequiredSampleCount: 0,
                ReadySampleCount: 0,
                ValidatedEvidenceDocumentCount: 0,
                RequiredOwners: Array.Empty<string>(),
                Blockers: ["Trading readiness service is unavailable in fallback mode."],
                Detail: "Trading readiness is unavailable in fallback mode."),
            BrokerageSync: null,
            WorkItems: Array.Empty<OperatorWorkItemDto>(),
            Warnings: ["Trading readiness service is unavailable in fallback mode."])
        {
            OverallStatus = TradingAcceptanceGateStatusDto.Blocked,
            ReadyForPaperOperation = false,
            SnapshotMaterializedAt = asOf,
            SnapshotVersion = "fallback-unavailable"
        };
    }
}
