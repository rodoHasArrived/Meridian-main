import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useLocation } from "react-router-dom";
import { ReportingScreen } from "@/screens/reporting-screen";
import {
  getCommandPaletteActionsSnapshot,
  useCommandPaletteActions
} from "@/components/meridian/command-palette.actions";
import { encodeViewStateEnvelope } from "@/lib/view-state-envelope";
import { renderWithRouter } from "@/test/render";
import type { AccountingWorkspaceResponse, FinancialRecordExplorerDto, ReportingStarterKitProvisionResult, ReportingTemplateMetadata } from "@/types";

function LocationSearchProbe() {
  const { search } = useLocation();
  return <output aria-label="location search">{search}</output>;
}

const accounting: AccountingWorkspaceResponse = {
  metrics: [],
  reconciliationQueue: [],
  breakQueue: [],
  cashFlow: {
    totalCash: 0,
    totalLedgerCash: 0,
    netVariance: 0,
    totalFinancing: 0,
    runsWithCashSignals: 0,
    runsWithCashVariance: 0,
    tone: "success",
    summary: "Cash is balanced."
  },
  reporting: {
    profileCount: 2,
    fundProfileId: "fund-alpha",
    selectedFundProfileId: "fund-alpha",
    recommendedProfiles: ["excel"],
    profiles: [
      {
        id: "excel",
        name: "Excel",
        targetTool: "Excel",
        format: "Xlsx",
        description: "Board-ready workbook export.",
        loaderScript: false,
        dataDictionary: true
      },
      {
        id: "audit-pack",
        name: "Audit Pack",
        targetTool: "Audit portal",
        format: "Markdown",
        description: "Audit evidence packet.",
        loaderScript: true,
        dataDictionary: true
      }
    ],
    reportPackDistributions: [
      {
        distributionId: "board-reporting-committee",
        recipient: "Board reporting committee",
        recipientRole: "Board",
        channel: "Board portal",
        state: "Pending approval",
        pendingItems: 1,
        pendingSummary: "1 report pack still needs approval before Board reporting committee delivery.",
        owner: "fund-controller",
        dueAtUtc: "2026-05-03T20:00:00Z",
        lastSentAtUtc: null,
        route: "/reporting/report-packs?recipient=board"
      },
      {
        distributionId: "compliance-archive",
        recipient: "Compliance archive",
        recipientRole: "Compliance",
        channel: "Retained evidence vault",
        state: "No package queued",
        pendingItems: 0,
        pendingSummary: "No governed report pack is queued for Compliance archive.",
        owner: "compliance-reviewer",
        dueAtUtc: null,
        lastSentAtUtc: "2026-05-02T18:00:00Z",
        route: "/reporting/evidence?subject=report-pack"
      }
    ],
    summary: "2 export profiles available.",
    reportWriterDatasetSources: [
      {
        sourceId: "retained-reporting-rows",
        label: "Retained reporting rows",
        description: "Source-backed portfolio cuts, Top-N analytics, and cross-fund consolidation rows.",
        rowCount: 2,
        fields: [
          { name: "dataset", label: "Dataset", role: "dimension", dataType: "string", dataset: "All", description: "Dataset family." },
          { name: "kind", label: "Kind", role: "dimension", dataType: "string", dataset: "Portfolio cuts", description: "Cut kind." },
          { name: "strategy", label: "Strategy", role: "dimension", dataType: "string", dataset: "Portfolio cuts", description: "Strategy tag." },
          { name: "grossExposure", label: "Gross exposure", role: "metric", dataType: "decimal", dataset: "Portfolio cuts", description: "Gross exposure." },
          { name: "totalPnl", label: "Total P&L", role: "metric", dataType: "decimal", dataset: "Portfolio cuts", description: "Total P&L." }
        ],
        rows: [
          {
            dataset: "portfolio-cut",
            kind: "Fund",
            strategy: "All",
            grossExposure: "400",
            totalPnl: "50",
            versionStamp: "portfolio-cut:fixture:fund"
          },
          {
            dataset: "portfolio-cut",
            kind: "Strategy",
            strategy: "Carry Strategy",
            grossExposure: "400",
            totalPnl: "50",
            versionStamp: "portfolio-cut:fixture:strategy"
          }
        ],
        tags: ["portfolio-cuts", "source-backed"]
      },
      {
        sourceId: "portfolio-reporting-cuts",
        label: "Portfolio reporting cuts",
        description: "Fund, strategy, and user-tag exposure, cash, P&L, and shadow-NAV rows.",
        rowCount: 2,
        fields: [
          { name: "dataset", label: "Dataset", role: "dimension", dataType: "string", dataset: "All", description: "Dataset family." },
          { name: "kind", label: "Kind", role: "dimension", dataType: "string", dataset: "Portfolio cuts", description: "Cut kind." },
          { name: "strategy", label: "Strategy", role: "dimension", dataType: "string", dataset: "Portfolio cuts", description: "Strategy tag." },
          { name: "grossExposure", label: "Gross exposure", role: "metric", dataType: "decimal", dataset: "Portfolio cuts", description: "Gross exposure." },
          { name: "shadowNav", label: "Shadow NAV", role: "metric", dataType: "decimal", dataset: "Portfolio cuts", description: "Parallel shadow NAV." },
          { name: "totalPnl", label: "Total P&L", role: "metric", dataType: "decimal", dataset: "Portfolio cuts", description: "Total P&L." }
        ],
        rows: [
          {
            dataset: "portfolio-cut",
            kind: "Fund",
            strategy: "All",
            grossExposure: "400",
            shadowNav: "450",
            totalPnl: "50",
            versionStamp: "portfolio-cut:fixture:fund"
          },
          {
            dataset: "portfolio-cut",
            kind: "Strategy",
            strategy: "Carry Strategy",
            grossExposure: "400",
            shadowNav: "450",
            totalPnl: "50",
            versionStamp: "portfolio-cut:fixture:strategy"
          }
        ],
        tags: ["portfolio-cuts", "shadow-nav"]
      },
      {
        sourceId: "topn-contribution-analytics",
        label: "Top-N contribution analytics",
        description: "Security and factor analytics rows with Top-N ranking and P&L contribution.",
        rowCount: 1,
        fields: [
          { name: "dataset", label: "Dataset", role: "dimension", dataType: "string", dataset: "All", description: "Dataset family." },
          { name: "security", label: "Security", role: "dimension", dataType: "string", dataset: "Top-N analytics", description: "Security label." },
          { name: "rank", label: "Rank", role: "dimension", dataType: "integer", dataset: "Top-N analytics", description: "Top-N rank." },
          { name: "pnl", label: "P&L", role: "metric", dataType: "decimal", dataset: "Top-N analytics", description: "Profit and loss." },
          { name: "contributionPercent", label: "Contribution %", role: "generated", dataType: "decimal", dataset: "Top-N analytics", description: "Contribution percent." }
        ],
        rows: [
          {
            dataset: "portfolio-analytics",
            security: "Alpha Corp",
            rank: "1",
            pnl: "50",
            contributionPercent: "100",
            versionStamp: "analytics:fixture:winner"
          }
        ],
        tags: ["top-n", "contribution"]
      }
    ],
    accessAudit: {
      evaluationScope: "CallerScoped",
      summary: "Reporting access policy hid 4 object(s) outside the caller's user, group, or company scope.",
      principalScopes: ["user:viewer.user", "group:ops-control", "company:company-alpha"],
      visibleTemplateCount: 2,
      hiddenTemplateCount: 1,
      visibleReportPackCount: 3,
      hiddenReportPackCount: 1,
      visibleScheduleCount: 1,
      hiddenScheduleCount: 0,
      visibleDeliveryAttemptCount: 2,
      hiddenDeliveryAttemptCount: 1,
      visibleStructuredExportCount: 5,
      hiddenStructuredExportCount: 1,
      denialReasons: [
        "Some report templates are private or restricted to other users, groups, or companies.",
        "Some delivery attempts are hidden because their report pack or template is outside the caller's report access scope."
      ]
    },
    portfolioCuts: [
      {
        cutId: "fund:consolidated",
        label: "Consolidated fund",
        kind: "Fund",
        currency: "USD",
        grossExposure: 400,
        netExposure: 400,
        longMarketValue: 400,
        shortMarketValue: 0,
        totalCash: 3250,
        pendingSettlement: 150,
        realizedPnl: 20,
        unrealizedPnl: 30,
        totalPnl: 50,
        shadowNav: 3650,
        shadowNavVariance: 2500,
        sourceCount: 3,
        tags: ["fund", "consolidated"],
        asOf: "2026-04-11T16:00:00Z",
        evidenceRoute: "/api/fund-structure/report-packs",
        shadowNavNote: "Shadow NAV is sourced from the shared NAV attribution service.",
        versionStamp: "portfolio-cut:20260411160000:runs-1:accounts-2"
      },
      {
        cutId: "strategy:carry-1",
        label: "Carry Strategy",
        kind: "Strategy",
        currency: "USD",
        grossExposure: 400,
        netExposure: 400,
        longMarketValue: 400,
        shortMarketValue: 0,
        totalCash: 750,
        pendingSettlement: 0,
        realizedPnl: 20,
        unrealizedPnl: 30,
        totalPnl: 50,
        shadowNav: 1150,
        shadowNavVariance: 0,
        sourceCount: 1,
        tags: ["run-governance-001"],
        asOf: "2026-04-11T16:00:00Z",
        evidenceRoute: "/api/fund-structure/report-packs",
        shadowNavNote: "Strategy shadow NAV uses the latest contributing run equity.",
        versionStamp: "portfolio-cut:20260411160000:runs-1:accounts-0"
      }
    ],
    livePortfolioViews: [
      {
        viewId: "live:fund:consolidated",
        label: "Consolidated fund",
        kind: "Fund",
        state: "LiveLinked",
        currency: "USD",
        grossExposure: 400,
        netExposure: 400,
        totalCash: 3250,
        pendingSettlement: 150,
        totalPnl: 50,
        shadowNav: 3650,
        asOf: "2026-04-11T16:00:00Z",
        sourceAsOfUtc: "2026-04-11T15:58:00Z",
        sourceCount: 3,
        route: "/api/workstation/portfolio/summary?fundAccountId=all&strategyId=all&entity=portfolio",
        liquiditySummary: "3,250.00 USD cash with 150.00 pending settlement in this reporting cut.",
        cashLadderSummary: "Open the live portfolio summary route for consolidated cash and liquidity posture.",
        telemetrySummary: "Live-linked portfolio telemetry is current through 2026-04-11T15:58:00.0000000Z; open the route for the latest portfolio-summary telemetry.",
        tickFreshnessSummary: "Market tick snapshot is 120 second(s) old and inside the 5-minute live-link window.",
        marketTickAsOfUtc: "2026-04-11T15:58:00Z",
        marketTickAgeSeconds: 120,
        marketTickSequence: 1775923080000,
        marketDataProvider: "portfolio-summary-live",
        isMarketTickLinked: true,
        freshnessPolicy: {
          policyName: "LivePortfolioView",
          evaluatedAtUtc: "2026-04-11T16:00:00Z",
          sourceAgeSeconds: 120,
          liveLinkWindowSeconds: 300,
          staleWindowSeconds: 86400,
          isWithinLiveLinkWindow: true,
          isBeyondStaleWindow: false,
          reason: "Source age is 120 second(s), inside the 5-minute live-link window."
        },
        tags: ["fund", "consolidated"],
        cashLadderRoute: null,
        versionStamp: "portfolio-cut:20260411160000:runs-1:accounts-2",
        readinessBlockers: ["Pending settlement of 150.00 USD requires cash-ladder evidence before treating this live view as fully delivery-ready."]
      },
      {
        viewId: "live:strategy:carry-1",
        label: "Carry Strategy",
        kind: "Strategy",
        state: "SourceBacked",
        currency: "USD",
        grossExposure: 400,
        netExposure: 400,
        totalCash: 750,
        pendingSettlement: 0,
        totalPnl: 50,
        shadowNav: 1150,
        asOf: "2026-04-11T16:00:00Z",
        sourceAsOfUtc: "2026-04-11T14:30:00Z",
        sourceCount: 1,
        route: "/api/workstation/portfolio/summary?fundAccountId=all&strategyId=carry-1&entity=portfolio",
        liquiditySummary: "750.00 USD cash is available with no pending settlement in this reporting cut.",
        cashLadderSummary: "Run cash-ladder evidence is available for Carry Strategy.",
        telemetrySummary: "Source-backed portfolio telemetry is current through 2026-04-11T14:30:00.0000000Z; open the route for latest portfolio-summary telemetry.",
        tickFreshnessSummary: "Source-backed tick snapshot is 5400 second(s) old; refresh the live portfolio route for current ticks.",
        marketTickAsOfUtc: "2026-04-11T14:30:00Z",
        marketTickAgeSeconds: 5400,
        marketTickSequence: 1775917800000,
        marketDataProvider: "retained-portfolio-snapshot",
        isMarketTickLinked: false,
        freshnessPolicy: {
          policyName: "LivePortfolioView",
          evaluatedAtUtc: "2026-04-11T16:00:00Z",
          sourceAgeSeconds: 5400,
          liveLinkWindowSeconds: 300,
          staleWindowSeconds: 86400,
          isWithinLiveLinkWindow: false,
          isBeyondStaleWindow: false,
          reason: "Source age is 5400 second(s), outside the live-link window but inside the 24-hour stale window."
        },
        tags: ["run-governance-001"],
        cashLadderRoute: "/api/portfolio/run-governance-001/cash-flows",
        versionStamp: "portfolio-cut:20260411160000:runs-1:accounts-0",
        readinessBlockers: ["Latest source snapshot is outside the 5-minute live-link window."]
      }
    ],
    pnlSlices: [
      {
        sliceId: "pnl:daily",
        period: "Daily",
        label: "Daily P&L",
        currency: "USD",
        startDate: "2026-04-11",
        endDate: "2026-04-11",
        realizedPnl: 20,
        unrealizedPnl: 30,
        totalPnl: 50,
        priorTotalPnl: 0,
        pnlChange: 50,
        sourceCount: 1,
        asOf: "2026-04-11T16:00:00Z",
        route: "/api/workstation/reporting?pnlSlice=daily",
        readinessSummary: "1 source-backed run(s) in the daily window; no prior-period source run is available for comparison.",
        tags: ["pnl", "daily", "source-backed"],
        versionStamp: "pnl-slice:20260411160000:daily:sources-1:prior-0"
      },
      {
        sliceId: "pnl:weekly",
        period: "Weekly",
        label: "Weekly P&L",
        currency: "USD",
        startDate: "2026-04-05",
        endDate: "2026-04-11",
        realizedPnl: 20,
        unrealizedPnl: 30,
        totalPnl: 50,
        priorTotalPnl: 0,
        pnlChange: 50,
        sourceCount: 1,
        asOf: "2026-04-11T16:00:00Z",
        route: "/api/workstation/reporting?pnlSlice=weekly",
        readinessSummary: "1 source-backed run(s) in the weekly window; no prior-period source run is available for comparison.",
        tags: ["pnl", "weekly", "source-backed"],
        versionStamp: "pnl-slice:20260411160000:weekly:sources-1:prior-0"
      },
      {
        sliceId: "pnl:monthly",
        period: "Monthly",
        label: "Monthly P&L",
        currency: "USD",
        startDate: "2026-04-01",
        endDate: "2026-04-11",
        realizedPnl: 20,
        unrealizedPnl: 30,
        totalPnl: 50,
        priorTotalPnl: 0,
        pnlChange: 50,
        sourceCount: 1,
        asOf: "2026-04-11T16:00:00Z",
        route: "/api/workstation/reporting?pnlSlice=monthly",
        readinessSummary: "1 source-backed run(s) in the monthly window; no prior-period source run is available for comparison.",
        tags: ["pnl", "monthly", "source-backed"],
        versionStamp: "pnl-slice:20260411160000:monthly:sources-1:prior-0"
      },
      {
        sliceId: "pnl:yearly",
        period: "Yearly",
        label: "Yearly P&L",
        currency: "USD",
        startDate: "2026-01-01",
        endDate: "2026-04-11",
        realizedPnl: 20,
        unrealizedPnl: 30,
        totalPnl: 50,
        priorTotalPnl: 0,
        pnlChange: 50,
        sourceCount: 1,
        asOf: "2026-04-11T16:00:00Z",
        route: "/api/workstation/reporting?pnlSlice=yearly",
        readinessSummary: "1 source-backed run(s) in the yearly window; no prior-period source run is available for comparison.",
        tags: ["pnl", "yearly", "source-backed"],
        versionStamp: "pnl-slice:20260411160000:yearly:sources-1:prior-0"
      }
    ],
    analyticsRows: [
      {
        analyticsId: "analytics:topwinner:security:aapl",
        kind: "TopWinner",
        scope: "Security",
        rank: 1,
        label: "Apple Inc.",
        symbol: "AAPL",
        classification: "Equity",
        currency: "USD",
        realizedPnl: 20,
        unrealizedPnl: 30,
        totalPnl: 50,
        contributionPercent: 125,
        heatMapIntensity: 83.3333,
        sourceCount: 1,
        asOf: "2026-04-11T16:00:00Z",
        route: "/api/workstation/reporting?analyticsId=analytics%3Atopwinner%3Asecurity%3Aaapl",
        readinessSummary: "Top-N winner from 1 source-backed run(s); contributes 125% of portfolio P&L.",
        tags: ["analytics", "topwinner", "security", "equity"],
        versionStamp: "analytics:20260411160000:topwinner:security:sources-1"
      },
      {
        analyticsId: "analytics:toplaggard:security:hedge",
        kind: "TopLaggard",
        scope: "Security",
        rank: 1,
        label: "Hedge Overlay",
        symbol: "HEDGE",
        classification: "Derivative",
        currency: "USD",
        realizedPnl: -10,
        unrealizedPnl: -5,
        totalPnl: -15,
        contributionPercent: -37.5,
        heatMapIntensity: 25,
        sourceCount: 1,
        asOf: "2026-04-11T16:00:00Z",
        route: "/api/workstation/reporting?analyticsId=analytics%3Atoplaggard%3Asecurity%3Ahedge",
        readinessSummary: "Top-N laggard from 1 source-backed run(s); contributes -37.5% of portfolio P&L.",
        tags: ["analytics", "toplaggard", "security", "derivative"],
        versionStamp: "analytics:20260411160000:toplaggard:security:sources-1"
      },
      {
        analyticsId: "analytics:contribution:strategy:carry-1",
        kind: "Contribution",
        scope: "Strategy",
        rank: 1,
        label: "Carry Strategy",
        symbol: null,
        classification: "Strategy",
        currency: "USD",
        realizedPnl: 20,
        unrealizedPnl: 30,
        totalPnl: 50,
        contributionPercent: 125,
        heatMapIntensity: 83.3333,
        sourceCount: 1,
        asOf: "2026-04-11T16:00:00Z",
        route: "/api/workstation/reporting?analyticsId=analytics%3Acontribution%3Astrategy%3Acarry-1",
        readinessSummary: "1 source-backed run(s); contribution is 125% of portfolio P&L with 83.33% heat-map intensity.",
        tags: ["analytics", "contribution", "strategy", "strategy"],
        versionStamp: "analytics:20260411160000:contribution:strategy:sources-1"
      }
    ],
    crossFundConsolidations: [
      {
        consolidationId: "cross-fund:company",
        label: "Company-wide consolidation",
        scope: "Company",
        currency: "USD",
        isReady: true,
        fundCount: 2,
        entityCount: 1,
        accountCount: 3,
        runCount: 2,
        grossExposure: 1200,
        netExposure: 1200,
        longMarketValue: 1200,
        shortMarketValue: 0,
        totalCash: 4750,
        pendingSettlement: 150,
        totalPnl: 95,
        shadowNav: 5950,
        shadowNavVariance: 4750,
        sourceCount: 5,
        asOf: "2026-04-11T16:00:00Z",
        route: "/api/workstation/reporting?consolidationId=cross-fund%3Acompany",
        readinessSummary: "5 source record(s) across 2 fund(s), 1 entity row(s), 3 account(s), and 2 run(s).",
        tags: ["company", "cross-fund", "consolidated"],
        versionStamp: "cross-fund:20260411160000:funds-2:entities-1:sources-5"
      },
      {
        consolidationId: "cross-fund:fund:fund-alpha",
        label: "fund-alpha",
        scope: "Fund",
        currency: "USD",
        isReady: true,
        fundCount: 1,
        entityCount: 1,
        accountCount: 2,
        runCount: 1,
        grossExposure: 800,
        netExposure: 800,
        longMarketValue: 800,
        shortMarketValue: 0,
        totalCash: 4000,
        pendingSettlement: 150,
        totalPnl: 50,
        shadowNav: 4800,
        shadowNavVariance: 4000,
        sourceCount: 3,
        asOf: "2026-04-11T16:00:00Z",
        route: "/api/workstation/reporting?consolidationId=cross-fund%3Afund%3Afund-alpha",
        readinessSummary: "3 source record(s) across 1 fund(s), 1 entity row(s), 2 account(s), and 1 run(s).",
        tags: ["fund", "cross-fund", "consolidated"],
        versionStamp: "cross-fund:20260411160000:funds-1:entities-1:sources-3"
      }
    ],
    structuredExports: [
      {
        exportId: "regulatory-trial-balance",
        label: "Regulatory trial balance",
        purpose: "Regulatory",
        format: "Csv",
        dataset: "fund-trial-balance",
        consumer: "Regulatory and compliance reporting",
        schemaVersion: 1,
        rowCount: 12,
        fieldCount: 10,
        sourceCount: 18,
        currency: "USD",
        asOf: "2026-04-11T16:00:00Z",
        isReady: true,
        retainedPath: "exports/reporting/fund-alpha/20260411160000/regulatory-trial-balance.csv",
        route: "/api/workstation/reporting/structured-exports/regulatory-trial-balance?format=csv",
        dataDictionaryRoute: "/api/workstation/reporting",
        validationSummary: "12 row(s), 10 field(s), and 18 source record(s) are ready.",
        evidenceRoute: "/api/fund-structure/report-packs",
        versionStamp: "structured-export:20260411160000:rows-12:sources-18:schema-1",
        tags: ["regulatory", "trial-balance", "ledger"]
      },
      {
        exportId: "investment-portfolio-cuts",
        label: "Investment portfolio cuts",
        purpose: "InvestmentDecision",
        format: "Xlsx",
        dataset: "portfolio-reporting-cuts",
        consumer: "Investment and risk decision workflows",
        schemaVersion: 1,
        rowCount: 2,
        fieldCount: 13,
        sourceCount: 4,
        currency: "USD",
        asOf: "2026-04-11T16:00:00Z",
        isReady: true,
        retainedPath: "exports/reporting/fund-alpha/20260411160000/investment-portfolio-cuts.xlsx",
        route: "/api/workstation/reporting/structured-exports/investment-portfolio-cuts",
        dataDictionaryRoute: "/api/workstation/reporting",
        validationSummary: "2 row(s), 13 field(s), and 4 source record(s) are ready.",
        evidenceRoute: "/api/fund-structure/report-packs",
        versionStamp: "structured-export:20260411160000:rows-2:sources-4:schema-1",
        tags: ["investment", "portfolio-cuts", "shadow-nav"]
      },
      {
        exportId: "warehouse-ledger-facts",
        label: "Warehouse ledger facts",
        purpose: "DataWarehouse",
        format: "Json",
        dataset: "ledger-reconciliation-facts",
        consumer: "Data warehouse and lakehouse ingestion",
        schemaVersion: 1,
        rowCount: 6,
        fieldCount: 9,
        sourceCount: 18,
        currency: "USD",
        asOf: "2026-04-11T16:00:00Z",
        isReady: true,
        retainedPath: "exports/reporting/fund-alpha/20260411160000/warehouse-ledger-facts.json",
        route: "/api/workstation/reporting/structured-exports/warehouse-ledger-facts",
        dataDictionaryRoute: "/api/workstation/reporting",
        validationSummary: "Exports consolidated ledger snapshot facts for downstream warehouse loading. 6 row(s), 9 field(s), and 18 source record(s) are ready.",
        evidenceRoute: "/api/fund-structure/report-packs",
        versionStamp: "structured-export:20260411160000:rows-6:sources-18:schema-1",
        tags: ["warehouse", "ledger", "reconciliation"],
        retainedManifestPath: "exports/reporting/fund-alpha/20260411160000/warehouse-ledger-facts.manifest.json",
        integrityHashSha256: "a".repeat(64),
        integritySummary: `6 row(s), 9 field(s), and 18 source record(s) retained with SHA-256 ${"a".repeat(64)}.`,
        rowLineageCount: 6
      },
      {
        exportId: "investment-topn-contribution-analytics",
        label: "Top-N contribution analytics",
        purpose: "InvestmentDecision",
        format: "Csv",
        dataset: "portfolio-topn-contribution-analytics",
        consumer: "Investment and risk decision workflows",
        schemaVersion: 1,
        rowCount: 3,
        fieldCount: 18,
        sourceCount: 4,
        currency: "USD",
        asOf: "2026-04-11T16:00:00Z",
        isReady: true,
        retainedPath: "exports/reporting/fund-alpha/20260411160000/investment-topn-contribution-analytics.csv",
        route: "/api/workstation/reporting/structured-exports/investment-topn-contribution-analytics?format=csv",
        dataDictionaryRoute: "/api/workstation/reporting",
        validationSummary: "Exports source-backed Top-N winners, laggards, and contribution rows with P&L percentages and heat-map intensities. 3 row(s), 18 field(s), and 4 source record(s) are ready.",
        evidenceRoute: "/api/fund-structure/report-packs",
        versionStamp: "structured-export:20260411160000:rows-3:sources-4:schema-1",
        tags: ["investment", "top-n", "contribution", "analytics"]
      },
      {
        exportId: "cross-fund-consolidation",
        label: "Cross-fund consolidation",
        purpose: "InvestmentDecision",
        format: "Xlsx",
        dataset: "cross-fund-reporting-consolidation",
        consumer: "Investment and operating committee reporting",
        schemaVersion: 1,
        rowCount: 2,
        fieldCount: 19,
        sourceCount: 5,
        currency: "USD",
        asOf: "2026-04-11T16:00:00Z",
        isReady: true,
        retainedPath: "exports/reporting/fund-alpha/20260411160000/cross-fund-consolidation.xlsx",
        route: "/api/workstation/reporting/structured-exports/cross-fund-consolidation",
        dataDictionaryRoute: "/api/workstation/reporting",
        validationSummary: "2 row(s), 19 field(s), and 5 source record(s) are ready.",
        evidenceRoute: "/api/fund-structure/report-packs",
        versionStamp: "structured-export:20260411160000:rows-2:sources-5:schema-1",
        tags: ["investment", "cross-fund", "consolidation"]
      }
    ],
    brandingThemes: [
      {
        themeId: "meridian-standard",
        name: "Meridian Standard",
        firmName: "Meridian",
        primaryColor: "#195E63",
        accentColor: "#2F8F83",
        textColor: "#1F2933",
        backgroundColor: "#F8FAFC",
        logoUri: null,
        footerText: "Generated by Meridian Reporting",
        disclaimer: "Confidential report pack generated from retained Meridian source evidence.",
        isBuiltIn: true
      },
      {
        themeId: "lpcustomtheme",
        name: "LP Custom Theme",
        firmName: "Northstar Capital",
        primaryColor: "#123456",
        accentColor: "#AA5500",
        textColor: "#111111",
        backgroundColor: "#FAFAFA",
        logoUri: "https://example.test/northstar.png",
        footerText: "Northstar investor reporting",
        disclaimer: "Prepared for authorized allocator review.",
        isBuiltIn: false
      }
    ],
    templates: [
      {
        templateId: "investor-monthly-statement",
        family: "InvestorStatement",
        name: "Investor Monthly Statement",
        version: "1.0.0",
        sections: ["cover", "performance"],
        lifecycleStatus: "Approved",
        isBuiltIn: true,
        isLatestApproved: true,
        approvalSummary: "Built-in approved template for InvestorStatement.",
        authoringRoute: "/api/fund-structure/reporting/templates/investor-monthly-statement/versions/1",
        createdBy: "system",
        createdAt: "2026-05-01T10:00:00Z",
        updatedBy: "controller.admin",
        updatedAt: "2026-05-02T11:30:00Z",
        approvedBy: "controller.admin",
        approvedAt: "2026-05-02T11:30:00Z",
        decisionRationale: "Controller approved investor statement baseline.",
        approvalReference: "APP-TPL-INVESTOR-1",
        auditTrail: [
          {
            at: "2026-05-01T10:00:00Z",
            actor: "system",
            action: "seed-built-in",
            fromStatus: "Approved",
            toStatus: "Approved",
            note: "Built-in template catalog"
          },
          {
            at: "2026-05-02T11:30:00Z",
            actor: "controller.admin",
            action: "approve",
            fromStatus: "InReview",
            toStatus: "Approved",
            note: "Controller approved investor statement baseline."
          }
        ],
        validationIssues: [],
        reportWriterGrids: [
          {
            gridId: "sector-pivot",
            title: "Sector Pivot",
            kind: "Pivot",
            dimensionCount: 2,
            metricCount: 2,
            formulaCount: 1,
            rowFields: ["sector"],
            columnFields: ["strategy"],
            metrics: [
              { name: "marketValue", sourceField: "marketValue", function: "Sum", label: "Market value" },
              { name: "pnl", sourceField: "pnl", function: "Sum", label: "P&L" }
            ],
            formulas: [
              { name: "returnPct", expression: "{pnl} / {marketValue} * 100", label: "Return %" }
            ],
            topN: null,
            sortBy: "pnl",
            sortDescending: true,
            filters: [
              { field: "strategy", operator: "Equals", value: "Core", label: "Core strategy" }
            ],
            sourceFields: [
              { name: "sector", label: "sector", role: "dimension", dataType: "string", dataset: "Portfolio cuts", description: "Sector bucket" },
              { name: "strategy", label: "strategy", role: "dimension", dataType: "string", dataset: "Portfolio cuts", description: "Strategy tag" },
              { name: "marketValue", label: "Market value", role: "metric", dataType: "decimal", dataset: "Portfolio cuts", description: "Market value" },
              { name: "pnl", label: "pnl", role: "metric", dataType: "decimal", dataset: "Portfolio cuts", description: "Profit and loss" },
              { name: "grossExposure", label: "Gross exposure", role: "metric", dataType: "decimal", dataset: "Portfolio cuts", description: "Dataset-wide authoring field" },
              { name: "contributionPercent", label: "Contribution %", role: "generated", dataType: "decimal", dataset: "Contribution grids", description: "Generated by the shared renderer" }
            ]
          }
        ]
      }
    ]
  }
};

function withReportTemplate(template: ReportingTemplateMetadata): AccountingWorkspaceResponse {
  return {
    ...accounting,
    reporting: {
      ...accounting.reporting,
      templates: [
        ...(accounting.reporting.templates ?? []),
        template
      ]
    }
  };
}

function withReportingStarterKit(): AccountingWorkspaceResponse {
  return {
    ...accounting,
    reporting: {
      ...accounting.reporting,
      starterKits: [
        {
          kitId: "emerging-manager",
          archetype: "Emerging Manager",
          displayName: "Emerging Manager",
          description: "Investor-ready monthly statements, capital accounts, and shadow NAV packs.",
          templateIds: [
            "investor-monthly-statement",
            "capital-account-statement",
            "shadow-nav-daily-pack"
          ],
          defaultLayoutId: "reporting-hub.emerging-manager.v1",
          defaultPeriod: "CurrentMonth",
          seedSchedules: [
            {
              scheduleId: "starter-emerging-manager-investor-monthly",
              templateId: "investor-monthly-statement",
              cronExpression: "0 9 5 * *",
              cadence: "Monthly",
              description: "Draft monthly investor statement schedule.",
              state: "Draft",
              deliveryTargets: [
                {
                  distributionId: "investor-relations",
                  formats: ["Pdf", "Xlsx"],
                  deliveryMode: "SecurePortal",
                  note: "Starter target"
                }
              ]
            }
          ]
        }
      ],
      starterKitState: {
        isProvisioned: false,
        selectedKitId: null,
        archetype: null,
        enabledTemplateIds: [],
        defaultLayoutId: null,
        defaultPeriod: null,
        seedScheduleIds: []
      }
    }
  };
}

function withPrivateCapitalReportReview(): AccountingWorkspaceResponse {
  const evidenceCategories = [
    {
      categoryId: "source-evidence",
      label: "Source evidence",
      isReady: true,
      summary: "Capital call notice and journal packet are retained.",
      evidenceLinkCount: 2,
      evidenceLinks: ["/evidence/capital-call-notice", "/evidence/manual-journal"],
      requiredEvidence: ["Capital call notice", "Posted journal entry"]
    },
    {
      categoryId: "report-output-evidence",
      label: "Report output evidence",
      isReady: false,
      summary: "Published report output is visible, but retained report-line evidence is missing.",
      evidenceLinkCount: 0,
      evidenceLinks: [],
      requiredEvidence: ["Report manifest", "Report line provenance"]
    }
  ];
  const validationIssue = {
    code: "private-capital.report-output-evidence-missing",
    severity: "Warning" as const,
    message: "Report output requires retained manifest and report-line provenance before report-ready.",
    targetId: "report-output:capital-call-001",
    suggestedAction: "Attach report output evidence."
  };
  const fundEvent = {
    fundEventId: "fund-event:capital-call-001",
    fundEventType: "CapitalCall",
    entryType: "CapitalCall" as const,
    journalStatus: "Approved" as const,
    journalEntryId: "mj-capital-call-001",
    effectiveDate: "2026-06-30",
    capitalAccountId: "capital-account:fund-alpha:lp-1",
    investorId: "investor:lp-1",
    currency: "USD",
    grossAmount: 125000,
    netCapitalActivity: 125000,
    memo: "Q2 capital call",
    paymentIntentId: "intent:capital-call-001",
    settlementReference: "settlement:capital-call-001",
    evidenceLinks: ["/evidence/capital-call-notice", "/evidence/manual-journal"],
    validationIssues: [],
    updatedAtUtc: "2026-06-30T18:00:00Z",
    isPosted: true,
    approvalId: "approval:capital-call-001"
  };
  const subledgerEntry = {
    subledgerEntryId: "subledger-entry:capital-call-001",
    capitalAccountId: fundEvent.capitalAccountId,
    investorId: fundEvent.investorId,
    currency: fundEvent.currency,
    fundEventId: fundEvent.fundEventId,
    fundEventType: fundEvent.fundEventType,
    entryType: fundEvent.entryType,
    approvalState: "Approved" as const,
    journalEntryId: fundEvent.journalEntryId,
    effectiveDate: fundEvent.effectiveDate,
    grossAmount: fundEvent.grossAmount,
    netCapitalActivity: fundEvent.netCapitalActivity,
    runningNetActivity: 125000,
    memo: fundEvent.memo,
    evidenceLinks: fundEvent.evidenceLinks,
    validationIssues: [],
    updatedAtUtc: fundEvent.updatedAtUtc,
    isPosted: true
  };
  const ledgerImpact = {
    ledgerImpactId: "ledger-impact:capital-call-001",
    journalEntryId: fundEvent.journalEntryId,
    fundEventId: fundEvent.fundEventId,
    fundEventType: fundEvent.fundEventType,
    capitalAccountId: fundEvent.capitalAccountId,
    investorId: fundEvent.investorId,
    approvalState: "Approved" as const,
    effectiveDate: fundEvent.effectiveDate,
    currency: fundEvent.currency,
    totalDebits: 125000,
    totalCredits: 125000,
    imbalance: 0,
    lineCount: 2,
    isBalanced: true,
    isPostingReady: true,
    evidenceLinks: fundEvent.evidenceLinks,
    lines: [
      {
        lineId: "line:cash",
        accountPath: "Assets:Cash",
        side: "Debit" as const,
        amount: 125000,
        currency: fundEvent.currency,
        entityId: "entity:fund-alpha",
        securityId: null,
        securityDisplayName: null,
        evidenceLink: "/evidence/manual-journal"
      },
      {
        lineId: "line:capital",
        accountPath: "Capital:LP Contributions",
        side: "Credit" as const,
        amount: 125000,
        currency: fundEvent.currency,
        entityId: "entity:fund-alpha",
        securityId: null,
        securityDisplayName: null,
        evidenceLink: "/evidence/manual-journal"
      }
    ],
    validationIssues: []
  };
  const reportOutput = {
    reportOutputId: "report-output:capital-call-001",
    reportOutputType: "CapitalAccountStatement",
    displayName: "LP-1 Capital Account Statement",
    reportRoute: "/api/ledger/private-capital/report-output/report-output%3Acapital-call-001",
    fundEventId: fundEvent.fundEventId,
    fundEventType: fundEvent.fundEventType,
    capitalAccountId: fundEvent.capitalAccountId,
    investorId: fundEvent.investorId,
    approvalState: "Approved" as const,
    effectiveDate: fundEvent.effectiveDate,
    currency: fundEvent.currency,
    netCapitalActivity: fundEvent.netCapitalActivity,
    evidenceLinkCount: 0,
    evidenceLinks: [],
    isReportReady: false,
    validationIssues: [validationIssue],
    isPublished: true,
    reportPackId: "report-pack:capital-call-001",
    reportWorkflowState: "Published",
    publicationManifestId: "manifest:capital-call-001",
    retainedManifestPath: "reports/fund-alpha/capital-call-001/manifest.json",
    publicationEvidenceHash: "sha256:report-output",
    publishedAtUtc: "2026-06-30T20:00:00Z",
    publishedBy: "fund-controller",
    reportLineProvenanceCount: 0,
    reportOutputRoute: "/api/ledger/private-capital/report-output/report-output%3Acapital-call-001",
    fundEventRecordRoute: "/api/ledger/private-capital/fund-event-record/fund-event%3Acapital-call-001",
    capitalAccountSubledgerRoute: "/api/ledger/private-capital/capital-account-subledger/capital-account%3Afund-alpha%3Alp-1",
    evidenceRoute: "/api/ledger/private-capital/evidence/fund-event%3Acapital-call-001",
    approvalRoute: "/api/accounting/approvals/approval%3Acapital-call-001",
    readinessLabel: "Report evidence review",
    readinessReason: "Published output is missing retained report-line provenance.",
    nextAction: "Attach report output evidence.",
    nextActionRoute: "/api/ledger/private-capital/evidence/fund-event%3Acapital-call-001"
  };
  const fundEventRecord = {
    fundEventRecordId: "fund-event-record:capital-call-001",
    fundEventId: fundEvent.fundEventId,
    fundEventType: fundEvent.fundEventType,
    capitalAccountId: fundEvent.capitalAccountId,
    investorId: fundEvent.investorId,
    approvalState: "Approved" as const,
    journalEntryId: fundEvent.journalEntryId,
    effectiveDate: fundEvent.effectiveDate,
    currency: fundEvent.currency,
    grossAmount: fundEvent.grossAmount,
    netCapitalActivity: fundEvent.netCapitalActivity,
    capitalAccountOpeningNetActivity: 0,
    capitalAccountEndingNetActivity: 125000,
    memo: fundEvent.memo,
    paymentIntentId: fundEvent.paymentIntentId,
    settlementReference: fundEvent.settlementReference,
    activityRoute: "/api/ledger/private-capital/activity?fundEventId=fund-event%3Acapital-call-001",
    evidenceRoute: reportOutput.evidenceRoute,
    approvalId: fundEvent.approvalId,
    approvalRoute: reportOutput.approvalRoute,
    isPosted: true,
    isPostingReady: true,
    isReportReady: false,
    isPublished: true,
    readiness: "ReportReview" as const,
    readinessLabel: "Report review",
    readinessReason: "Published report output is missing retained report-line evidence.",
    nextAction: "Attach report output evidence.",
    nextActionRoute: reportOutput.evidenceRoute,
    evidenceLinkCount: 2,
    capitalAccountSubledgerEntryCount: 1,
    ledgerImpactCount: 1,
    reportOutputCount: 1,
    validationIssueCount: 1,
    primaryReportOutputId: reportOutput.reportOutputId,
    primaryReportOutputType: reportOutput.reportOutputType,
    primaryReportRoute: reportOutput.reportOutputRoute,
    reportWorkflowState: reportOutput.reportWorkflowState,
    publicationManifestId: reportOutput.publicationManifestId,
    retainedManifestPath: reportOutput.retainedManifestPath,
    reportLineProvenanceCount: 0,
    evidenceLinks: fundEvent.evidenceLinks,
    evidenceCategories,
    fundEvent,
    capitalAccountSubledgerEntries: [subledgerEntry],
    ledgerImpacts: [ledgerImpact],
    reportOutputs: [reportOutput],
    validationIssues: [validationIssue]
  };
  const subledger = {
    subledgerId: "capital-account-subledger:lp-1",
    fundProfileId: "fund-alpha",
    ledgerBookId: "00000000-0000-0000-0000-000000000001",
    projectedAtUtc: "2026-06-30T20:15:00Z",
    capitalAccountId: fundEvent.capitalAccountId,
    investorId: fundEvent.investorId,
    currency: fundEvent.currency,
    activityRoute: reportOutput.capitalAccountSubledgerRoute,
    contributions: 125000,
    distributions: 0,
    subscriptions: 0,
    redemptions: 0,
    managementFees: 0,
    openingNetActivity: 0,
    endingNetActivity: 125000,
    netCapitalActivity: 125000,
    fundEventCount: 1,
    approvalQueueCount: 0,
    postedFundEventCount: 1,
    publishedReportOutputCount: 1,
    evidenceLinkCount: 2,
    validationIssueCount: 1,
    firstEffectiveDate: fundEvent.effectiveDate,
    lastEffectiveDate: fundEvent.effectiveDate,
    lastFundEventType: fundEvent.fundEventType,
    readiness: "ReportReview" as const,
    readinessLabel: "Report review",
    readinessReason: "Capital-account subledger is posted, but report-output evidence is incomplete.",
    nextAction: "Attach report output evidence.",
    nextActionRoute: reportOutput.evidenceRoute,
    evidenceLinks: fundEvent.evidenceLinks,
    evidenceCategories,
    capitalAccount: {
      capitalAccountId: fundEvent.capitalAccountId,
      investorId: fundEvent.investorId,
      currency: fundEvent.currency,
      contributions: 125000,
      distributions: 0,
      subscriptions: 0,
      redemptions: 0,
      managementFees: 0,
      netActivity: 125000,
      fundEventCount: 1,
      lastEffectiveDate: fundEvent.effectiveDate,
      lastFundEventType: fundEvent.fundEventType,
      fundEventIds: [fundEvent.fundEventId]
    },
    fundEventRecords: [fundEventRecord],
    subledgerEntries: [subledgerEntry],
    ledgerImpacts: [ledgerImpact],
    reportOutputs: [reportOutput],
    validationIssues: [validationIssue]
  };

  return {
    ...accounting,
    manualJournalWorkbench: {
      fundProfileId: "fund-alpha",
      ledgerBookId: "00000000-0000-0000-0000-000000000001",
      loadedAtUtc: "2026-06-30T20:15:00Z",
      ledgerBooks: [],
      chartOfAccounts: [],
      drafts: [],
      auditTrail: [],
      privateCapitalActivity: {
        fundProfileId: "fund-alpha",
        ledgerBookId: "00000000-0000-0000-0000-000000000001",
        projectedAtUtc: "2026-06-30T20:15:00Z",
        fundEventCount: 1,
        capitalAccountCount: 1,
        submittedFundEventCount: 1,
        approvalQueueCount: 0,
        postedFundEventCount: 1,
        publishedReportOutputCount: 1,
        netCapitalActivity: 125000,
        currency: "USD",
        fundEvents: [fundEvent],
        capitalAccounts: [subledger.capitalAccount],
        capitalAccountSubledgerEntries: [subledgerEntry],
        ledgerImpacts: [ledgerImpact],
        reportOutputs: [reportOutput],
        fundEventRecords: [fundEventRecord],
        capitalAccountSubledgers: [subledger],
        validationIssues: []
      }
    }
  };
}

function reportTemplateGovernanceResponse(options: {
  name: string;
  version: number;
  displayName: string;
  status: string;
  family?: string;
  rationale?: string | null;
  approvalReference?: string | null;
}) {
  return {
    definition: {
      templateId: { name: options.name, version: options.version },
      displayName: options.displayName,
      parameters: [],
      sections: [],
      grids: [],
      accessPolicy: null
    },
    status: options.status,
    family: options.family ?? "CustomReport",
    isBuiltIn: false,
    isLatestApproved: options.status === "Approved",
    createdBy: "controller.admin",
    createdAt: "2026-06-08T00:00:00Z",
    updatedBy: "controller.admin",
    updatedAt: "2026-06-08T00:00:00Z",
    validationIssues: [],
    auditTrail: [],
    decisionRationale: options.rationale ?? null,
    approvalReference: options.approvalReference ?? null
  };
}

function withReportLineProvenanceExplorer(): AccountingWorkspaceResponse {
  return {
    ...accounting,
    reporting: {
      ...accounting.reporting,
      reportLineProvenanceExplorer: createReportLineProvenanceExplorer()
    }
  };
}

function createReportLineProvenanceExplorer(): FinancialRecordExplorerDto {
  const sourceHref = "/api/workstation/financial-record-explorers/portfolio?lineKey=trial-balance.cash&sourceId=position-aapl&evidenceId=ledger-evidence-1&runId=run-1";
  const instrumentHref = "/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111";
  const activityHref = "/api/workstation/evidence/subjects/provider-event/provider-event-position-aapl/packet";
  const reconciliationHref = "/api/workstation/reconciliation/runs/recon-run-1";
  const journalHref = "/api/workstation/runs/run-1/ledger/journal?ledgerEntryId=ledger-entry-1";
  const approvalHref = "/api/workstation/evidence/subjects/approval/approval-1/packet";
  const auditHref = "/api/workstation/evidence/subjects/report-line/ledger-evidence-1/packet";
  const deliveryHref = "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries";
  const deliveryGraphHref = "/api/workstation/evidence/subjects/report-pack-delivery/11111111-1111-1111-1111-111111111111%3A22222222-2222-2222-2222-222222222222/graph";
  const restatementHref = "/evidence/restatement-evidence-1";

  return {
    explorerId: "report-line-provenance",
    title: "Report-Line Provenance Explorer",
    description: "Drill from governed report lines into retained instruments, positions or transactions, reconciliations, journals, reports, evidence, and audit links.",
    sourceState: "1 governed report line across 1 report pack retains instrument, position or transaction, reconciliation, journal, report, evidence, and audit drill-through links.",
    isBlocked: false,
    blockedReason: "",
    scopeItems: [
      { label: "Report packs", value: "1", tone: "Info" },
      { label: "Latest period", value: "2026-03", tone: "Default" },
      { label: "Fund", value: "fund-alpha", tone: "Default" },
      { label: "Delivery attempts", value: "1", tone: "Success" }
    ],
    savedViews: [
      {
        viewId: "system-report-line-provenance-default",
        label: "Report lines",
        description: "Governed report lines with retained source provenance.",
        isSystem: true,
        isActive: true,
        filters: [],
        searchText: ""
      }
    ],
    summaryItems: [
      { label: "Report lines", value: "1", detail: "Governed report lines with retained provenance.", tone: "Success" },
      { label: "Instruments", value: "1", detail: "Lines linked to retained Security Master or instrument definition evidence.", tone: "Default" },
      { label: "Positions / transactions", value: "1", detail: "Lines linked to provider positions, transactions, source sessions, or retained source rows.", tone: "Default" },
      { label: "Source records", value: "1", detail: "Distinct retained source records linked to report lines.", tone: "Default" },
      { label: "Reconciliations", value: "1", detail: "Lines linked to reconciliation runs or break cases.", tone: "Default" },
      { label: "Journals", value: "1", detail: "Lines with ledger or journal drill-through evidence.", tone: "Default" },
      { label: "Approvals", value: "1", detail: "Line-level and workflow approval evidence.", tone: "Default" },
      { label: "Deliveries", value: "1", detail: "Retained report-pack delivery attempts and packages.", tone: "Success" },
      { label: "Audit links", value: "1", detail: "Lines with retained evidence ids that route to audit packets.", tone: "Default" },
      { label: "Restatements", value: "1", detail: "Changed report lines with restatement evidence.", tone: "Warning" }
    ],
    filters: [
      { filterId: "state-restated", label: "Workflow state", value: "Restated", operator: "equals", tone: "Warning" },
      { filterId: "period-2026-03", label: "Period", value: "2026-03", operator: "equals", tone: "Default" }
    ],
    columns: [
      { columnId: "lineKey", header: "Report Line", cellKind: "text", width: 200, isRightAligned: false },
      { columnId: "report", header: "Report Pack", cellKind: "text", width: 190, isRightAligned: false },
      { columnId: "value", header: "Value", cellKind: "money", width: 110, isRightAligned: true },
      { columnId: "source", header: "Source", cellKind: "text", width: 170, isRightAligned: false },
      { columnId: "instrument", header: "Instrument", cellKind: "text", width: 160, isRightAligned: false },
      { columnId: "activity", header: "Position / Transaction", cellKind: "text", width: 190, isRightAligned: false },
      { columnId: "reconciliation", header: "Reconciliation", cellKind: "text", width: 140, isRightAligned: false },
      { columnId: "journal", header: "Journal", cellKind: "text", width: 120, isRightAligned: false },
      { columnId: "approval", header: "Approval", cellKind: "text", width: 120, isRightAligned: false },
      { columnId: "delivery", header: "Delivery", cellKind: "text", width: 140, isRightAligned: false },
      { columnId: "restatement", header: "Restatement", cellKind: "text", width: 140, isRightAligned: false }
    ],
    rows: [
      {
        recordId: "report-line:11111111111111111111111111111111:trial-balance-cash:0",
        recordType: "report-line",
        label: "trial-balance.cash",
        source: "position:position-aapl",
        status: "Restated",
        tone: "Warning",
        cells: [
          { columnId: "lineKey", displayValue: "trial-balance.cash", rawValue: "", tone: "Default", linkHref: sourceHref },
          { columnId: "report", displayValue: "2026-03 - board-pack v1", rawValue: "", tone: "Default", linkHref: deliveryHref },
          { columnId: "value", displayValue: "100.00", rawValue: "", tone: "Default", linkHref: "" },
          { columnId: "source", displayValue: "position:position-aapl", rawValue: "", tone: "Default", linkHref: sourceHref },
          { columnId: "instrument", displayValue: "11111111-1111-1111-1111-111111111111", rawValue: "", tone: "Success", linkHref: instrumentHref },
          { columnId: "activity", displayValue: "provider-event-position-aapl", rawValue: "", tone: "Info", linkHref: activityHref },
          { columnId: "reconciliation", displayValue: "matched", rawValue: "", tone: "Success", linkHref: reconciliationHref },
          { columnId: "journal", displayValue: "ledger-entry-1", rawValue: "", tone: "Info", linkHref: journalHref },
          { columnId: "approval", displayValue: "approval-1", rawValue: "", tone: "Success", linkHref: approvalHref },
          { columnId: "delivery", displayValue: "Delivered #1", rawValue: "", tone: "Success", linkHref: deliveryHref },
          { columnId: "restatement", displayValue: "1 changed line", rawValue: "", tone: "Warning", linkHref: restatementHref }
        ],
        detail: {
          recordId: "report-line:11111111111111111111111111111111:trial-balance-cash:0",
          recordType: "Report line",
          title: "trial-balance.cash",
          subtitle: "2026-03 - board-pack v1",
          description: "Governed report line with retained source, reconciliation, journal, approval, delivery, and restatement drill-through evidence.",
          tone: "Warning",
          fields: [
            { label: "Source record", value: "position:position-aapl", detail: sourceHref, tone: "Default" },
            { label: "Instrument", value: "11111111-1111-1111-1111-111111111111", detail: instrumentHref, tone: "Success" },
            { label: "Position / transaction", value: "provider-event-position-aapl", detail: activityHref, tone: "Info" },
            { label: "Reconciliation", value: "matched", detail: reconciliationHref, tone: "Success" },
            { label: "Journal", value: "ledger-entry-1", detail: journalHref, tone: "Info" },
            { label: "Approval", value: "approval-1", detail: approvalHref, tone: "Success" },
            { label: "Evidence and audit links", value: "ledger-evidence-1", detail: auditHref, tone: "Info" },
            { label: "Delivery history", value: "Delivered #1", detail: "board-delivery-202603", tone: "Success" },
            { label: "Restatement", value: "1 changed line", detail: "source-correction", tone: "Warning" }
          ],
          proofActions: [
            { actionId: "open-source-record", label: "Open source record", description: "Open retained source record.", href: sourceHref, isEnabled: true, disabledReason: "", tone: "Info" },
            { actionId: "open-instrument", label: "Open instrument", description: "Open retained instrument evidence.", href: instrumentHref, isEnabled: true, disabledReason: "", tone: "Success" },
            { actionId: "open-position-transaction", label: "Open position/transaction evidence", description: "Open retained position or transaction evidence.", href: activityHref, isEnabled: true, disabledReason: "", tone: "Info" },
            { actionId: "open-reconciliation", label: "Open reconciliation", description: "Open reconciliation run.", href: reconciliationHref, isEnabled: true, disabledReason: "", tone: "Info" },
            { actionId: "open-journal", label: "Open journal", description: "Open ledger journal.", href: journalHref, isEnabled: true, disabledReason: "", tone: "Info" },
            { actionId: "open-approval-evidence", label: "Open approval evidence", description: "Open approval evidence.", href: approvalHref, isEnabled: true, disabledReason: "", tone: "Success" },
            { actionId: "open-evidence-audit-links", label: "Open evidence and audit links", description: "Open evidence and audit links.", href: auditHref, isEnabled: true, disabledReason: "", tone: "Info" },
            { actionId: "open-delivery-history", label: "Open delivery history", description: "Open delivery history.", href: deliveryHref, isEnabled: true, disabledReason: "", tone: "Success" },
            { actionId: "open-delivery-evidence-graph", label: "Open delivery evidence graph", description: "Open delivery evidence graph.", href: deliveryGraphHref, isEnabled: true, disabledReason: "", tone: "Info" },
            { actionId: "open-restatement-evidence", label: "Open restatement evidence", description: "Open restatement evidence.", href: restatementHref, isEnabled: true, disabledReason: "", tone: "Warning" }
          ],
          usedIn: [
            { relationshipId: "report-pack:11111111-1111-1111-1111-111111111111", label: "Published report pack", description: "board-pack v1 for 2026-03 is Restated.", href: deliveryHref, tone: "Warning" },
            { relationshipId: "report-delivery:22222222-2222-2222-2222-222222222222", label: "Delivery history", description: "Board delivery attempt #1 is Delivered.", href: deliveryHref, tone: "Success" }
          ],
          impacts: [
            { relationshipId: "source-record", label: "Source record", description: "Source record supports the report line value.", href: sourceHref, tone: "Info" },
            { relationshipId: "instrument", label: "Instrument", description: "Instrument identity anchors this report line.", href: instrumentHref, tone: "Success" },
            { relationshipId: "position-transaction", label: "Position / transaction", description: "Provider event feeds reconciliation before journal support.", href: activityHref, tone: "Info" },
            { relationshipId: "reconciliation", label: "Reconciliation", description: "Reconciliation outcome is matched.", href: reconciliationHref, tone: "Success" },
            { relationshipId: "journal", label: "Journal", description: "Ledger entry supports this line.", href: journalHref, tone: "Info" },
            { relationshipId: "approval", label: "Approval", description: "Approval is retained as line evidence.", href: approvalHref, tone: "Success" },
            { relationshipId: "delivery-history", label: "Delivery history", description: "Delivery evidence carries the line.", href: deliveryGraphHref, tone: "Success" },
            { relationshipId: "evidence-audit-links", label: "Evidence and audit links", description: "Evidence id anchors the retained audit packet for this report line.", href: auditHref, tone: "Info" },
            { relationshipId: "restatement-evidence", label: "Restatement evidence", description: "Restatement evidence exists for this report line.", href: restatementHref, tone: "Warning" }
          ],
          fullRecordHref: sourceHref
        }
      }
    ],
    selectedRecord: null,
    proofActions: [
      { actionId: "open-report-pack-workflows", label: "Open report-pack workflows", description: "Open governed workflows.", href: "/api/fund-structure/reporting/packs", isEnabled: true, disabledReason: "", tone: "Info" }
    ],
    recordGraph: {
      nodes: [
        { nodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:instrument", label: "Instrument", nodeType: "instrument", tone: "Success", href: instrumentHref },
        { nodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:position-transaction", label: "Position / transaction", nodeType: "position-transaction", tone: "Info", href: activityHref },
        { nodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:reconciliation", label: "Reconciliation", nodeType: "reconciliation", tone: "Success", href: reconciliationHref },
        { nodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:journal", label: "Journal", nodeType: "journal", tone: "Info", href: journalHref },
        { nodeId: "report-line:11111111111111111111111111111111:trial-balance-cash:0", label: "trial-balance.cash", nodeType: "report-line", tone: "Warning", href: sourceHref },
        { nodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:report-pack:11111111-1111-1111-1111-111111111111", label: "Published report pack", nodeType: "report-pack:11111111-1111-1111-1111-111111111111", tone: "Warning", href: deliveryHref },
        { nodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:evidence-audit-links", label: "Evidence and audit links", nodeType: "evidence-audit-links", tone: "Info", href: auditHref }
      ],
      edges: [
        { sourceNodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:instrument", targetNodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:position-transaction", label: "feeds", tone: "Info" },
        { sourceNodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:position-transaction", targetNodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:reconciliation", label: "reconciles", tone: "Success" },
        { sourceNodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:reconciliation", targetNodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:journal", label: "posts", tone: "Info" },
        { sourceNodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:journal", targetNodeId: "report-line:11111111111111111111111111111111:trial-balance-cash:0", label: "reports", tone: "Warning" },
        { sourceNodeId: "report-line:11111111111111111111111111111111:trial-balance-cash:0", targetNodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:report-pack:11111111-1111-1111-1111-111111111111", label: "included in", tone: "Warning" },
        { sourceNodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:report-pack:11111111-1111-1111-1111-111111111111", targetNodeId: "rel:report-line:11111111111111111111111111111111:trial-balance-cash:0:evidence-audit-links", label: "retains audit", tone: "Info" }
      ]
    }
  };
}

function withReportingSchedule(state: "Active" | "Paused" = "Active"): AccountingWorkspaceResponse {
  return {
    ...accounting,
    reporting: {
      ...accounting.reporting,
      schedules: [
        {
          scheduleId: "sched-investor",
          templateId: "investor-monthly-statement",
          cronExpression: "0 8 1 * *",
          nextAsOfDate: "2026-06-01",
          dueAtUtc: "2026-06-01T08:00:00Z",
          maxRetries: 2,
          requestedBy: "fund-controller",
          state,
          createdAtUtc: "2026-05-01T08:00:00Z",
          updatedAtUtc: "2026-05-03T08:00:00Z",
          lastRunAtUtc: "2026-05-01T08:05:00Z",
          lastRunId: "investor-monthly-statement-20260501",
          runCount: 3,
          description: "Monthly investor statement close packet.",
          deliveryTargets: [
            {
              distributionId: "board-reporting-committee",
              formats: ["Pdf", "Xlsx", "Csv"],
              deliveryMode: "SecurePortal",
              note: "Board package."
            }
          ]
        }
      ]
    }
  };
}

function formatExpectedScheduleTimestamp(value: string): string {
  const timestamp = new Date(value);
  return timestamp.toLocaleString("en-US", {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
    timeZone: "UTC",
    timeZoneName: "short"
  });
}

function CommandPaletteActionsSubscriber() {
  useCommandPaletteActions();
  return null;
}

describe("ReportingScreen", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({
      ok: true,
      text: async () => JSON.stringify({
        jobId: "export-1",
        success: true,
        status: "completed",
        profileId: "audit-pack",
        symbols: [],
        filesGenerated: 2,
        totalRecords: 20,
        totalBytes: 1200,
        outputDirectory: "exports",
        durationSeconds: 1,
        error: null,
        warnings: [],
        files: [
          {
            path: "audit/export-1.md",
            symbol: "SPY",
            format: "markdown",
            sizeBytes: 1200,
            recordCount: 20
          }
        ],
        timestamp: "2026-05-01T00:00:00Z"
      })
    });
    vi.stubGlobal("fetch", fetchMock);
  });

  it("renders loading copy when reporting data is unavailable", () => {
    renderWithRouter(<ReportingScreen data={null} />, { initialEntries: ["/reporting"] });

    const loading = screen.getByRole("status", { name: "Loading Reporting" });
    expect(loading).toHaveAttribute("aria-busy", "true");
    expect(loading).toHaveAttribute("aria-live", "polite");
    expect(loading).toHaveClass("border-[var(--state-pending-bd)]", "bg-[var(--state-pending-bg)]");
    expect(screen.getByText(/waiting for governed report-pack and export evidence/i)).toBeInTheDocument();
    expect(screen.getByLabelText("Route Daily Reporting Cockpit")).toBeInTheDocument();
  });

  it("renders route-aware loading copy for report packs", () => {
    renderWithRouter(<ReportingScreen data={null} />, { initialEntries: ["/reporting/report-packs"] });

    const loading = screen.getByRole("status", { name: "Loading Reporting" });
    expect(loading).toHaveAttribute("aria-busy", "true");
    expect(screen.getByLabelText("Route Delivery Evidence")).toBeInTheDocument();
  });

  it("provisions a reporting starter kit from the first-visit chooser", async () => {
    const user = userEvent.setup();
    const provisioned: ReportingStarterKitProvisionResult = {
      kit: withReportingStarterKit().reporting.starterKits![0],
      state: {
        isProvisioned: true,
        selectedKitId: "emerging-manager",
        archetype: "Emerging Manager",
        enabledTemplateIds: [
          "investor-monthly-statement",
          "capital-account-statement",
          "shadow-nav-daily-pack"
        ],
        defaultLayoutId: "reporting-hub.emerging-manager.v1",
        defaultPeriod: "CurrentMonth",
        seedScheduleIds: ["starter-emerging-manager-investor-monthly"],
        provisionedAtUtc: "2026-07-06T16:00:00Z",
        provisionedBy: "controller.admin"
      },
      seededSchedules: [
        {
          scheduleId: "starter-emerging-manager-investor-monthly",
          templateId: "investor-monthly-statement",
          cronExpression: "0 9 5 * *",
          nextAsOfDate: "2026-07-31",
          dueAtUtc: "2026-08-05T14:00:00Z",
          maxRetries: 1,
          requestedBy: "controller.admin",
          state: "Draft",
          createdAtUtc: "2026-07-06T16:00:00Z",
          updatedAtUtc: "2026-07-06T16:00:00Z",
          lastRunAtUtc: null,
          lastRunId: null,
          runCount: 0,
          description: "Draft monthly investor statement schedule."
        }
      ]
    };
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify(provisioned)
    });

    renderWithRouter(<ReportingScreen data={withReportingStarterKit()} />, { initialEntries: ["/reporting/report-builder"] });

    expect(screen.getByRole("region", { name: "Set up your reporting desk" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Use Emerging Manager starter kit" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/starter-kits/emerging-manager/provision",
      expect.objectContaining({ method: "POST" })
    ));
    expect(await screen.findByText("Emerging Manager reporting desk provisioned.")).toBeInTheDocument();
    expect(screen.getByText("Templates enabled: investor-monthly-statement, capital-account-statement, shadow-nav-daily-pack")).toBeInTheDocument();
    expect(screen.getByText("Draft schedules: starter-emerging-manager-investor-monthly (Draft)")).toBeInTheDocument();
  });

  it("renders private-capital fund event report readiness from the shared workbench projection", () => {
    renderWithRouter(<ReportingScreen data={withPrivateCapitalReportReview()} />, { initialEntries: ["/reporting/report-builder"] });

    const readiness = screen.getByRole("region", { name: "Private-capital report readiness" });
    expect(within(readiness).getByText("Fund event ledger and capital account subledger")).toBeInTheDocument();
    expect(within(readiness).getByText("Source data")).toBeInTheDocument();
    expect(within(readiness).getAllByText("Report review").length).toBeGreaterThan(1);
    expect(within(readiness).getByText("Not report-ready")).toBeInTheDocument();
    expect(within(readiness).getAllByText("Published").length).toBeGreaterThan(0);
    expect(within(readiness).getByText("Published report output is missing retained report-line evidence.")).toBeInTheDocument();
    expect(within(readiness).getAllByText("Report output evidence").length).toBeGreaterThan(0);
    expect(within(readiness).getAllByText("Published report output is visible, but retained report-line evidence is missing.").length).toBeGreaterThan(0);
    expect(within(readiness).getByText("Capital-account subledger is posted, but report-output evidence is incomplete.")).toBeInTheDocument();
    expect(within(readiness).getAllByRole("link", { name: /Attach report output evidence\.: \/api\/ledger\/private-capital\/evidence\/fund-event%3Acapital-call-001/i }).length).toBeGreaterThan(1);
    expect(within(readiness).getByText("1 ledger impact")).toBeInTheDocument();
    expect(within(readiness).getByText("1 subledger movement")).toBeInTheDocument();
    expect(within(readiness).getAllByText("capital-account:fund-alpha:lp-1").length).toBeGreaterThan(0);
    expect(within(readiness).getByRole("link", {
      name: /Command center: \/api\/ledger\/private-capital\/fund-event-command-center\?fundProfileId=fund-alpha&ledgerBookId=00000000-0000-0000-0000-000000000001&fundEventId=fund-event%3Acapital-call-001/i
    })).toBeInTheDocument();
    expect(within(readiness).getAllByRole("link", { name: /report-output%3Acapital-call-001/i }).length).toBeGreaterThan(0);
    expect(within(readiness).getByText("Report evidence review")).toBeInTheDocument();
    expect(within(readiness).getByText("Published output is missing retained report-line provenance.")).toBeInTheDocument();
    expect(within(readiness).getByRole("list", { name: "Private-capital report outputs" })).toHaveTextContent(
      "0 provenance lines"
    );
  });

  it("renders report-pack distribution recipients with accessible row labels", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-packs"] });

    const recipients = screen.getByRole("list", { name: "Report-pack distribution route recipients" });
    expect(recipients).toBeInTheDocument();
    expect(within(recipients).getByLabelText(
      "Board reporting committee report-pack distribution: 1 report pack still needs approval before Board reporting committee delivery."
    )).toHaveTextContent("State: Pending approval");
    const boardRow = within(recipients).getByLabelText(
      "Board reporting committee report-pack distribution: 1 report pack still needs approval before Board reporting committee delivery."
    );
    expect(boardRow).toHaveTextContent("Last sent: Not sent");
    expect(within(boardRow).getByRole("link", { name: "Open Board reporting committee report-pack distribution route" })).toHaveAttribute(
      "href",
      "/reporting/report-packs?recipient=board"
    );
    const complianceRow = within(recipients).getByLabelText(
      "Compliance archive report-pack distribution: No governed report pack is queued for Compliance archive."
    );
    expect(complianceRow).toHaveTextContent("State: No package queued");
    expect(complianceRow).toHaveTextContent("Last sent:");
    expect(within(complianceRow).getByRole("link", { name: "Open Compliance archive report-pack distribution route" })).toHaveAttribute(
      "href",
      "/reporting/evidence?subject=report-pack"
    );
  });

  it("makes the daily reporting cockpit the default landing surface instead of the full builder page", () => {
    const landingData: AccountingWorkspaceResponse = {
      ...accounting,
      metrics: [{ id: "reporting-metric", label: "Generic reporting metric", value: "7", delta: "stable", tone: "default" }],
      reporting: {
        ...accounting.reporting,
        dailyWork: [
          {
            workItemId: "delivery-failure:board",
            kind: "delivery-failure",
            title: "Board portal package failed",
            statusLabel: "Blocked",
            detail: "Secure portal rejected the latest board package.",
            tone: "danger",
            owner: "fund-controller",
            dueAtUtc: "2026-06-30T15:00:00Z",
            primaryActionLabel: "Review delivery",
            primaryActionHref: "/reporting/report-packs?recipient=board",
            secondaryActionLabel: "Evidence",
            secondaryActionHref: "/reporting/evidence?subjectKind=report-pack-delivery&subjectId=board",
            evidenceGaps: ["Delivery rejection lacks retained portal proof."],
            context: ["Board reporting committee", "SecurePortal"]
          }
        ]
      }
    };

    renderWithRouter(<ReportingScreen data={landingData} />, { initialEntries: ["/reporting"] });

    expect(screen.getByRole("heading", { name: "Daily Reporting Cockpit" })).toBeInTheDocument();
    const cockpit = screen.getByRole("region", { name: "Daily reporting cockpit" });
    expect(within(cockpit).getByLabelText("Board portal package failed decision facts")).toHaveTextContent("fund-controller");
    expect(within(cockpit).getByLabelText("Board portal package failed decision facts")).toHaveTextContent("1 evidence gap");
    const taskModes = screen.getByRole("navigation", { name: "Reporting task modes" });
    expect(within(taskModes).getByRole("link", { name: "Open Report Builder reporting task mode" })).toHaveAttribute(
      "href",
      "/reporting/report-builder"
    );
    expect(within(taskModes).getByRole("link", { name: "Open Run Status reporting task mode" })).toHaveAttribute(
      "href",
      "/reporting/run-status"
    );
    expect(within(taskModes).getByRole("link", { name: "Open Delivery Evidence reporting task mode" })).toHaveAttribute(
      "href",
      "/reporting/report-packs"
    );
    expect(screen.queryByRole("group", { name: /generic reporting metric/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Reporting access audit" })).not.toBeInTheDocument();
  });

  it("keeps focused reporting task routes scoped to their owning intent", () => {
    const builderRender = renderWithRouter(<ReportingScreen data={accounting} />, {
      initialEntries: ["/reporting/report-builder"]
    });
    expect(screen.getByRole("region", { name: "No-code report writer" })).toBeInTheDocument();
    expect(screen.getByText("Governed report templates")).toBeInTheDocument();
    expect(screen.queryByText("Report run audit and lineage")).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Report-pack approval task" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Structured reporting exports" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Reporting access audit" })).not.toBeInTheDocument();

    builderRender.unmount();
    const runStatusRender = renderWithRouter(<ReportingScreen data={accounting} />, {
      initialEntries: ["/reporting/run-status"]
    });
    expect(screen.getByText("Report run audit and lineage")).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "No-code report writer" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Report-pack approval task" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Structured reporting exports" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Reporting access audit" })).not.toBeInTheDocument();

    runStatusRender.unmount();
    const exportsRender = renderWithRouter(<ReportingScreen data={accounting} />, {
      initialEntries: ["/reporting/exports"]
    });
    expect(screen.getByRole("region", { name: "Structured reporting exports" })).toBeInTheDocument();
    expect(screen.getByText("Governed export queue")).toBeInTheDocument();
    expect(screen.queryByText("Report run audit and lineage")).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Report-pack approval task" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Reporting access audit" })).not.toBeInTheDocument();

    exportsRender.unmount();
    renderWithRouter(<ReportingScreen data={accounting} />, {
      initialEntries: ["/reporting/governance"]
    });
    expect(screen.getByRole("region", { name: "Reporting access audit" })).toBeInTheDocument();
    expect(screen.getByText("Governed report templates")).toBeInTheDocument();
    expect(screen.queryByText("Report run audit and lineage")).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "No-code report writer" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Report-pack approval task" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Structured reporting exports" })).not.toBeInTheDocument();
  });

  it("renders report-line provenance explorer drill-through from the shared reporting payload", () => {
    renderWithRouter(<ReportingScreen data={withReportLineProvenanceExplorer()} />, { initialEntries: ["/reporting/report-packs"] });

    const explorer = screen.getByRole("region", { name: "Report-Line Provenance Explorer" });
    expect(within(explorer).getByText("Report-line provenance")).toBeInTheDocument();
    expect(within(explorer).getByRole("link", { name: "trial-balance.cash" })).toHaveAttribute(
      "href",
      "/api/workstation/financial-record-explorers/portfolio?lineKey=trial-balance.cash&sourceId=position-aapl&evidenceId=ledger-evidence-1&runId=run-1"
    );
    expect(within(explorer).getByText("Instruments")).toBeInTheDocument();
    expect(within(explorer).getByText("Positions / transactions")).toBeInTheDocument();
    expect(within(explorer).getByText("Source records")).toBeInTheDocument();
    expect(within(explorer).getByText("Reconciliations")).toBeInTheDocument();
    expect(within(explorer).getByText("Journals")).toBeInTheDocument();
    expect(within(explorer).getAllByText("Approvals").length).toBeGreaterThan(0);
    expect(within(explorer).getByText("Audit links")).toBeInTheDocument();
    expect(within(explorer).getByText("Restatements")).toBeInTheDocument();
    expect(within(explorer).getAllByText("Instrument").length).toBeGreaterThan(0);
    expect(within(explorer).getAllByText("Position / transaction").length).toBeGreaterThan(0);
    expect(within(explorer).getAllByText("Published report pack").length).toBeGreaterThan(0);
    expect(within(explorer).getAllByText("Evidence and audit links").length).toBeGreaterThan(0);
    expect(within(explorer).getByText("Restatement evidence")).toBeInTheDocument();
    expect(within(explorer).getByRole("link", { name: "Open source record" })).toHaveAttribute(
      "href",
      expect.stringContaining("/api/workstation/financial-record-explorers/portfolio")
    );
    expect(within(explorer).getByRole("link", { name: "Open instrument" })).toHaveAttribute(
      "href",
      "/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111"
    );
    expect(within(explorer).getByRole("link", { name: "Open position/transaction evidence" })).toHaveAttribute(
      "href",
      "/api/workstation/evidence/subjects/provider-event/provider-event-position-aapl/packet"
    );
    expect(within(explorer).getByRole("link", { name: "Open reconciliation" })).toHaveAttribute(
      "href",
      "/api/workstation/reconciliation/runs/recon-run-1"
    );
    expect(within(explorer).getByRole("link", { name: "Open journal" })).toHaveAttribute(
      "href",
      "/api/workstation/runs/run-1/ledger/journal?ledgerEntryId=ledger-entry-1"
    );
    expect(within(explorer).getByRole("link", { name: "Open approval evidence" })).toHaveAttribute(
      "href",
      "/api/workstation/evidence/subjects/approval/approval-1/packet"
    );
    expect(within(explorer).getByRole("link", { name: "Open evidence and audit links" })).toHaveAttribute(
      "href",
      "/api/workstation/evidence/subjects/report-line/ledger-evidence-1/packet"
    );
    expect(within(explorer).getByRole("link", { name: "Open delivery history" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries"
    );
    expect(within(explorer).getByRole("link", { name: "Open delivery evidence graph" })).toHaveAttribute(
      "href",
      "/api/workstation/evidence/subjects/report-pack-delivery/11111111-1111-1111-1111-111111111111%3A22222222-2222-2222-2222-222222222222/graph"
    );
    expect(within(explorer).getByRole("link", { name: "Open restatement evidence" })).toHaveAttribute(
      "href",
      "/evidence/restatement-evidence-1"
    );
  });

  it("renders portfolio reporting cuts from the shared reporting payload", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    expect(screen.getByRole("region", { name: "Portfolio reporting cuts" })).toBeInTheDocument();
    expect(screen.getByLabelText("Consolidated fund Fund portfolio reporting cut")).toHaveTextContent("Gross");
    expect(screen.getByLabelText("Consolidated fund Fund portfolio reporting cut")).toHaveTextContent("$400.00");
    expect(screen.getByLabelText("Consolidated fund Fund portfolio reporting cut")).toHaveTextContent("P&L");
    expect(screen.getByLabelText("Consolidated fund Fund portfolio reporting cut")).toHaveTextContent("$50.00");
    expect(screen.getByLabelText("Consolidated fund Fund portfolio reporting cut")).toHaveTextContent("Shadow NAV");
    expect(screen.getByLabelText("Carry Strategy Strategy portfolio reporting cut")).toHaveTextContent(
      "portfolio-cut:20260411160000:runs-1:accounts-0"
    );
  });

  it("renders live portfolio views from the shared reporting payload", async () => {
    const user = userEvent.setup();
    const refreshLivePortfolioViews = vi.fn().mockResolvedValue(undefined);
    renderWithRouter(<ReportingScreen data={accounting} onRefreshLivePortfolioViews={refreshLivePortfolioViews} />, { initialEntries: ["/reporting/report-builder"] });

    expect(screen.getByRole("region", { name: "Live portfolio views" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Refresh live portfolio reporting views" }));
    expect(refreshLivePortfolioViews).toHaveBeenCalledTimes(1);
    await waitFor(() => {
      expect(screen.getByText("Live portfolio views refreshed.")).toBeInTheDocument();
    });
    expect(screen.getByText("Portfolio summary data was refreshed through the shared workstation portfolio route.")).toBeInTheDocument();
    const fundView = screen.getByRole("listitem", { name: "Consolidated fund Fund live portfolio view" });
    expect(within(fundView).getByText("LiveLinked")).toBeInTheDocument();
    expect(fundView).toHaveTextContent("Settlement");
    expect(fundView).toHaveTextContent("$150.00");
    expect(fundView).toHaveTextContent("Shadow NAV");
    expect(fundView).toHaveTextContent("$3,650");
    expect(fundView).toHaveTextContent("Freshness: LiveLinked · cut=2026-04-11T16:00:00Z · source=2026-04-11T15:58:00Z");
    expect(within(fundView).getByLabelText(/Consolidated fund source data/)).toBeInTheDocument();
    expect(fundView).toHaveTextContent("Market tick: linked · provider=portfolio-summary-live · age=120s · seq=1775923080000 · tick=2026-04-11T15:58:00Z");
    expect(fundView).toHaveTextContent("Policy: LivePortfolioView · evaluated=2026-04-11T16:00:00Z · sourceAge=120s · liveWindow=300s · staleWindow=86400s");
    expect(fundView).toHaveTextContent("Source age is 120 second(s), inside the 5-minute live-link window.");
    expect(fundView).toHaveTextContent("3,250.00 USD cash with 150.00 pending settlement");
    expect(fundView).toHaveTextContent("Live-linked portfolio telemetry is current through 2026-04-11T15:58:00.0000000Z");
    expect(fundView).toHaveTextContent("Market tick snapshot is 120 second(s) old and inside the 5-minute live-link window.");
    expect(within(fundView).getByRole("list", { name: "Consolidated fund readiness blockers" })).toHaveTextContent(
      "Pending settlement of 150.00 USD requires cash-ladder evidence"
    );
    expect(within(fundView).getByRole("link", { name: "Open Consolidated fund live portfolio view" })).toHaveAttribute(
      "href",
      "/api/workstation/portfolio/summary?fundAccountId=all&strategyId=all&entity=portfolio"
    );

    const strategyView = screen.getByRole("listitem", { name: "Carry Strategy Strategy live portfolio view" });
    expect(strategyView).toHaveTextContent("Market tick: snapshot · provider=retained-portfolio-snapshot · age=5400s · seq=1775917800000 · tick=2026-04-11T14:30:00Z");
    expect(strategyView).toHaveTextContent("Policy: LivePortfolioView · evaluated=2026-04-11T16:00:00Z · sourceAge=5400s · liveWindow=300s · staleWindow=86400s");
    expect(strategyView).toHaveTextContent("Source age is 5400 second(s), outside the live-link window but inside the 24-hour stale window.");
    expect(strategyView).toHaveTextContent("Source-backed tick snapshot is 5400 second(s) old; refresh the live portfolio route for current ticks.");
    expect(strategyView).toHaveTextContent("Run cash-ladder evidence is available for Carry Strategy.");
    expect(within(strategyView).getByRole("list", { name: "Carry Strategy readiness blockers" })).toHaveTextContent(
      "Latest source snapshot is outside the 5-minute live-link window."
    );
    expect(within(strategyView).getByRole("link", { name: "Open Carry Strategy cash ladder" })).toHaveAttribute(
      "href",
      "/api/portfolio/run-governance-001/cash-flows"
    );
  });

  it("auto-refreshes tick-linked live portfolio views on the market tick cadence", async () => {
    vi.useFakeTimers();
    const refreshLivePortfolioViews = vi.fn().mockResolvedValue(undefined);
    const rendered = renderWithRouter(
      <ReportingScreen data={accounting} onRefreshLivePortfolioViews={refreshLivePortfolioViews} />,
      { initialEntries: ["/reporting/report-builder"] }
    );

    try {
      expect(refreshLivePortfolioViews).not.toHaveBeenCalled();

      await act(async () => {
        vi.advanceTimersByTime(60_000);
        await Promise.resolve();
        await Promise.resolve();
      });

      expect(refreshLivePortfolioViews).toHaveBeenCalledTimes(1);
      expect(screen.getByText("Live portfolio views refreshed from market tick cadence.")).toBeInTheDocument();
      expect(screen.getByText("Portfolio summary data was refreshed through the shared workstation portfolio route.")).toBeInTheDocument();
    } finally {
      rendered.unmount();
      vi.useRealTimers();
    }
  });

  it("surfaces live portfolio refresh failures in the reporting workspace", async () => {
    const user = userEvent.setup();
    const refreshLivePortfolioViews = vi.fn().mockRejectedValue(new Error("Portfolio summary refresh failed."));
    renderWithRouter(<ReportingScreen data={accounting} onRefreshLivePortfolioViews={refreshLivePortfolioViews} />, { initialEntries: ["/reporting/report-builder"] });

    await user.click(screen.getByRole("button", { name: "Refresh live portfolio reporting views" }));

    expect(refreshLivePortfolioViews).toHaveBeenCalledTimes(1);
    await waitFor(() => {
      expect(screen.getByText("Portfolio summary refresh failed.")).toBeInTheDocument();
    });
  });

  it("renders P&L slices from retained portfolio run timestamps", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    expect(screen.getByRole("region", { name: "P&L slicing" })).toBeInTheDocument();
    const dailySlice = screen.getByRole("listitem", { name: "Daily P&L Daily P&L slice" });
    expect(within(dailySlice).getByText("Daily")).toBeInTheDocument();
    expect(within(dailySlice).getByText("Source-backed")).toBeInTheDocument();
    expect(dailySlice).toHaveTextContent("2026-04-11");
    expect(dailySlice).toHaveTextContent("$50.00");
    expect(dailySlice).toHaveTextContent("1 source-backed run(s) in the daily window");
    expect(within(dailySlice).getByRole("link", { name: "Open Daily P&L P&L slice" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting?pnlSlice=daily"
    );

    const yearlySlice = screen.getByRole("listitem", { name: "Yearly P&L Yearly P&L slice" });
    expect(yearlySlice).toHaveTextContent("2026-01-01 to 2026-04-11");
    expect(yearlySlice).toHaveTextContent("pnl-slice:20260411160000:yearly:sources-1:prior-0");
  });

  it("renders Top-N and contribution analytics rows from the shared reporting payload", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    expect(screen.getByRole("region", { name: "Top-N and contribution analytics" })).toBeInTheDocument();
    const winner = screen.getByRole("listitem", { name: "Apple Inc. TopWinner Security analytics row" });
    expect(within(winner).getByText("TopWinner")).toBeInTheDocument();
    expect(within(winner).getByText("Security")).toBeInTheDocument();
    expect(winner).toHaveTextContent("AAPL");
    expect(winner).toHaveTextContent("$50.00");
    expect(winner).toHaveTextContent("125%");
    expect(winner).toHaveTextContent("83.33% intensity");
    expect(within(winner).getByRole("link", { name: "Open Apple Inc. analytics row" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting?analyticsId=analytics%3Atopwinner%3Asecurity%3Aaapl"
    );

    const laggard = screen.getByRole("listitem", { name: "Hedge Overlay TopLaggard Security analytics row" });
    expect(laggard).toHaveTextContent("Derivative");
    expect(laggard).toHaveTextContent("-$15.00");

    const contribution = screen.getByRole("listitem", { name: "Carry Strategy Contribution Strategy analytics row" });
    expect(contribution).toHaveTextContent("1 source-backed run(s); contribution is 125% of portfolio P&L");
    expect(contribution).toHaveTextContent("analytics:20260411160000:contribution:strategy:sources-1");
  });

  it("renders cross-fund consolidations from the shared reporting payload", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    expect(screen.getByRole("region", { name: "Cross-fund consolidations" })).toBeInTheDocument();
    const companyRow = screen.getByRole("listitem", { name: "Company-wide consolidation Company cross-fund consolidation" });
    expect(within(companyRow).getByText("Ready")).toBeInTheDocument();
    expect(companyRow).toHaveTextContent("5 source record(s) across 2 fund(s)");
    expect(companyRow).toHaveTextContent("$1,200");
    expect(companyRow).toHaveTextContent("Shadow NAV");
    expect(companyRow).toHaveTextContent("$5,950");
    expect(companyRow).toHaveTextContent("Variance");
    expect(companyRow).toHaveTextContent("$4,750");
    expect(within(companyRow).getByRole("link", { name: "Open Company-wide consolidation cross-fund consolidation" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting?consolidationId=cross-fund%3Acompany"
    );

    const fundRow = screen.getByRole("listitem", { name: "fund-alpha Fund cross-fund consolidation" });
    expect(fundRow).toHaveTextContent("cross-fund:20260411160000:funds-1:entities-1:sources-3");
  });

  it("hydrates the exports draft from a valid view-state link", () => {
    const token = encodeViewStateEnvelope({
      v: 1,
      screen: "reporting-exports",
      state: { selectedExportsTemplateId: "investor-monthly-statement:1.0.0", asOfDate: "2026-06-30" }
    })!;

    renderWithRouter(<ReportingScreen data={accounting} />, {
      initialEntries: [`/reporting/exports?view=${token}`]
    });

    expect(screen.getByLabelText("Exports report as-of date")).toHaveValue("2026-06-30");
    expect(screen.getByLabelText("Exports report template")).toHaveValue("investor-monthly-statement:1.0.0");
  });

  it("ignores view-state links for unknown templates or foreign screens", () => {
    const unknownTemplate = encodeViewStateEnvelope({
      v: 1,
      screen: "reporting-exports",
      state: { selectedExportsTemplateId: "not-a-real-template:9.9.9", asOfDate: "2026-06-30" }
    })!;

    const { unmount } = renderWithRouter(<ReportingScreen data={accounting} />, {
      initialEntries: [`/reporting/exports?view=${unknownTemplate}`]
    });
    expect(screen.getByLabelText("Exports report as-of date")).not.toHaveValue("2026-06-30");
    unmount();

    const foreignScreen = encodeViewStateEnvelope({
      v: 1,
      screen: "trial-balance",
      state: { selectedExportsTemplateId: "investor-monthly-statement:1.0.0", asOfDate: "2026-06-30" }
    })!;

    renderWithRouter(<ReportingScreen data={accounting} />, {
      initialEntries: [`/reporting/exports?view=${foreignScreen}`]
    });
    expect(screen.getByLabelText("Exports report as-of date")).not.toHaveValue("2026-06-30");
  });

  it("strips a stale exports view query when the draft cannot encode", async () => {
    const token = encodeViewStateEnvelope({
      v: 1,
      screen: "reporting-exports",
      state: { selectedExportsTemplateId: "investor-monthly-statement:1.0.0", asOfDate: "2026-06-30" }
    })!;

    renderWithRouter(
      <>
        <ReportingScreen
          data={{
            ...accounting,
            reporting: {
              ...accounting.reporting,
              templates: []
            }
          }}
        />
        <LocationSearchProbe />
      </>,
      {
        initialEntries: [`/reporting/exports?fundAccountId=fund-alpha&view=${token}`]
      }
    );

    fireEvent.change(screen.getByLabelText("Exports report as-of date"), { target: { value: "2026-07-31" } });

    await waitFor(() => {
      const params = new URLSearchParams(screen.getByLabelText("location search").textContent ?? "");
      expect(params.get("view")).toBeNull();
      expect(params.get("fundAccountId")).toBe("fund-alpha");
    });
  });

  it("registers the exports-run palette action while mounted and unregisters on unmount", () => {
    const { unmount } = renderWithRouter(<ReportingScreen data={accounting} />, {
      initialEntries: ["/reporting/exports"]
    });

    const registered = getCommandPaletteActionsSnapshot().find((item) => item.id === "reporting-run-exports");
    expect(registered).toBeDefined();
    expect(registered?.confirm).toBe(true);
    expect(registered?.verbLabel).toMatch(/^Run exports report: /);

    unmount();
    expect(
      getCommandPaletteActionsSnapshot().some((item) => item.id === "reporting-run-exports")
    ).toBe(false);
  });

  it("does not loop while registering exports-run actions with a palette subscriber mounted", () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);
    try {
      const { unmount } = renderWithRouter(
        <>
          <CommandPaletteActionsSubscriber />
          <ReportingScreen data={accounting} />
        </>,
        { initialEntries: ["/reporting/exports"] }
      );

      expect(getCommandPaletteActionsSnapshot().some((item) => item.id === "reporting-run-exports")).toBe(true);
      expect(consoleError).not.toHaveBeenCalledWith(expect.stringContaining("Maximum update depth exceeded"));

      unmount();
    } finally {
      consoleError.mockRestore();
    }
  });

  it("renders structured exports from the shared reporting payload", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/exports"] });

    expect(screen.getByRole("region", { name: "Structured reporting exports" })).toBeInTheDocument();
    const regulatoryRow = screen.getByRole("listitem", { name: "Regulatory trial balance structured export" });
    expect(within(regulatoryRow).getByText("Regulatory")).toBeInTheDocument();
    expect(within(regulatoryRow).getByText("Csv")).toBeInTheDocument();
    expect(within(regulatoryRow).getAllByText("12")).toHaveLength(2);
    expect(regulatoryRow).toHaveTextContent("Dataset: fund-trial-balance");
    expect(regulatoryRow).toHaveTextContent("As of: 2026-04-11T16:00:00Z");
    expect(regulatoryRow).toHaveTextContent(
      "API route: /api/workstation/reporting/structured-exports/regulatory-trial-balance?format=csv"
    );
    expect(regulatoryRow).toHaveTextContent(
      "Retained path: exports/reporting/fund-alpha/20260411160000/regulatory-trial-balance.csv"
    );
    expect(within(regulatoryRow).getByRole("link", { name: "Open Regulatory trial balance data dictionary" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting"
    );
    expect(within(regulatoryRow).getByRole("link", { name: "Open Regulatory trial balance evidence" })).toHaveAttribute(
      "href",
      "/api/fund-structure/report-packs"
    );
    expect(within(regulatoryRow).getByRole("group", { name: "Regulatory trial balance export tags" })).toHaveTextContent(
      "regulatorytrial-balanceledger"
    );
    expect(within(regulatoryRow).getByRole("link", { name: "Download Regulatory trial balance structured export as JSON" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting/structured-exports/regulatory-trial-balance"
    );
    expect(within(regulatoryRow).getByRole("link", { name: "Download Regulatory trial balance structured export as CSV" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting/structured-exports/regulatory-trial-balance?format=csv"
    );
    expect(within(regulatoryRow).getByRole("link", { name: "Download Regulatory trial balance structured export as XLS" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting/structured-exports/regulatory-trial-balance?format=xls"
    );
    expect(within(regulatoryRow).getByRole("link", { name: "Download Regulatory trial balance structured export as XLSX" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting/structured-exports/regulatory-trial-balance?format=xlsx"
    );

    const investmentRow = screen.getByRole("listitem", { name: "Investment portfolio cuts structured export" });
    expect(within(investmentRow).getByText("InvestmentDecision")).toBeInTheDocument();
    expect(within(investmentRow).getByText("structured-export:20260411160000:rows-2:sources-4:schema-1")).toBeInTheDocument();
    const warehouseRow = screen.getByRole("listitem", { name: "Warehouse ledger facts structured export" });
    expect(within(warehouseRow).getByText("DataWarehouse")).toBeInTheDocument();
    expect(within(warehouseRow).getByText("Json")).toBeInTheDocument();
    expect(warehouseRow).toHaveTextContent("Data warehouse and lakehouse ingestion");
    expect(warehouseRow).toHaveTextContent("Exports consolidated ledger snapshot facts for downstream warehouse loading");
    expect(warehouseRow).toHaveTextContent("Dataset: ledger-reconciliation-facts");
    const warehouseLineageMetric = within(warehouseRow).getByText("Lineage").parentElement;
    expect(warehouseLineageMetric).toHaveTextContent("6");
    expect(warehouseRow).toHaveTextContent(
      "API route: /api/workstation/reporting/structured-exports/warehouse-ledger-facts"
    );
    expect(warehouseRow).toHaveTextContent(
      "Manifest: exports/reporting/fund-alpha/20260411160000/warehouse-ledger-facts.manifest.json"
    );
    expect(warehouseRow).toHaveTextContent(`Integrity: 6 row(s), 9 field(s), and 18 source record(s) retained with SHA-256 ${"a".repeat(64)}.`);
    expect(warehouseRow).toHaveTextContent(`SHA-256: ${"a".repeat(64)}`);
    expect(within(warehouseRow).getByRole("group", { name: "Warehouse ledger facts export tags" })).toHaveTextContent(
      "warehouseledgerreconciliation"
    );
    expect(within(warehouseRow).getByRole("link", { name: "Download Warehouse ledger facts structured export as JSON" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting/structured-exports/warehouse-ledger-facts"
    );
    expect(within(warehouseRow).getByRole("link", { name: "Download Warehouse ledger facts structured export as CSV" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting/structured-exports/warehouse-ledger-facts?format=csv"
    );
    expect(within(warehouseRow).getByRole("link", { name: "Download Warehouse ledger facts structured export as XLSX" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting/structured-exports/warehouse-ledger-facts?format=xlsx"
    );
    const analyticsExportRow = screen.getByRole("listitem", { name: "Top-N contribution analytics structured export" });
    expect(within(analyticsExportRow).getByText("Csv")).toBeInTheDocument();
    expect(within(analyticsExportRow).getByText("structured-export:20260411160000:rows-3:sources-4:schema-1")).toBeInTheDocument();
    expect(within(analyticsExportRow).getByRole("link", { name: "Download Top-N contribution analytics structured export as JSON" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting/structured-exports/investment-topn-contribution-analytics"
    );
    expect(within(analyticsExportRow).getByRole("link", { name: "Download Top-N contribution analytics structured export as CSV" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting/structured-exports/investment-topn-contribution-analytics?format=csv"
    );
    expect(within(analyticsExportRow).getByRole("link", { name: "Download Top-N contribution analytics structured export as XLS" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting/structured-exports/investment-topn-contribution-analytics?format=xls"
    );
    expect(within(analyticsExportRow).getByRole("link", { name: "Download Top-N contribution analytics structured export as XLSX" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting/structured-exports/investment-topn-contribution-analytics?format=xlsx"
    );
    const crossFundExportRow = screen.getByRole("listitem", { name: "Cross-fund consolidation structured export" });
    expect(crossFundExportRow).toHaveTextContent("structured-export:20260411160000:rows-2:sources-5:schema-1");
    expect(within(crossFundExportRow).getByRole("link", { name: "Download Cross-fund consolidation structured export as JSON" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting/structured-exports/cross-fund-consolidation"
    );
    expect(within(crossFundExportRow).getByRole("link", { name: "Download Cross-fund consolidation structured export as CSV" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting/structured-exports/cross-fund-consolidation?format=csv"
    );
    expect(within(crossFundExportRow).getByRole("link", { name: "Download Cross-fund consolidation structured export as XLSX" })).toHaveAttribute(
      "href",
      "/api/workstation/reporting/structured-exports/cross-fund-consolidation?format=xlsx"
    );
  });

  it("does not expose download links for blocked structured exports", () => {
    const blockedAccounting: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        ...accounting.reporting,
        structuredExports: [
          {
            ...accounting.reporting.structuredExports![0],
            isReady: false,
            validationSummary: "No normalized trial-balance rows are available for this as-of date.",
            readinessBlockers: [
              "No normalized trial-balance rows are available for this as-of date.",
              "Ledger evidence must be retained before regulatory export download is enabled."
            ]
          }
        ]
      }
    };

    renderWithRouter(<ReportingScreen data={blockedAccounting} />, { initialEntries: ["/reporting/exports"] });

    const regulatoryRow = screen.getByRole("listitem", { name: "Regulatory trial balance structured export" });
    expect(within(regulatoryRow).getByText("Blocked")).toBeInTheDocument();
    expect(within(regulatoryRow).getByRole("list", {
      name: "Regulatory trial balance structured export readiness blockers"
    })).toHaveTextContent("Ledger evidence must be retained before regulatory export download is enabled.");
    expect(within(regulatoryRow).queryByRole("link", { name: "Download Regulatory trial balance structured export as CSV" })).not.toBeInTheDocument();
    expect(within(regulatoryRow).getByRole("button", {
      name: "Regulatory trial balance structured export CSV download blocked"
    })).toBeDisabled();
    expect(within(regulatoryRow).queryByRole("link", { name: "Download Regulatory trial balance structured export as XLS" })).not.toBeInTheDocument();
    expect(within(regulatoryRow).getByRole("button", {
      name: "Regulatory trial balance structured export XLS download blocked"
    })).toBeDisabled();
  });

  it("renders report branding themes from the shared reporting payload", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    expect(screen.getByRole("region", { name: "Report branding themes" })).toBeInTheDocument();
    const standardTheme = screen.getByRole("listitem", { name: "Meridian Standard report branding theme" });
    expect(within(standardTheme).getByText("Built-in")).toBeInTheDocument();
    expect(within(standardTheme).getByText("#195E63")).toBeInTheDocument();
    expect(standardTheme).toHaveTextContent("Generated by Meridian Reporting");
    expect(within(standardTheme).getByRole("button", { name: "Preview Meridian Standard branded report pack" })).toBeEnabled();
    expect(within(standardTheme).getByRole("button", { name: "Generate Meridian Standard branded report pack" })).toBeEnabled();

    const customTheme = screen.getByRole("listitem", { name: "LP Custom Theme report branding theme" });
    expect(within(customTheme).getByText("Custom")).toBeInTheDocument();
    expect(within(customTheme).getByText("Northstar Capital")).toBeInTheDocument();
    expect(within(customTheme).getByText("#AA5500")).toBeInTheDocument();
    expect(customTheme).toHaveTextContent("Prepared for authorized allocator review.");
    expect(within(customTheme).getByText("https://example.test/northstar.png")).toBeInTheDocument();
  });

  it("previews governed report packs before branded artifact generation", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        reportId: "11111111-2222-3333-4444-555555555555",
        fundProfileId: "fund-alpha",
        displayName: "Fund Alpha",
        reportKind: "BoardPacket",
        currency: "USD",
        asOf: "2026-06-08T00:00:00Z",
        generatedAt: "2026-06-08T00:00:03Z",
        totalNetAssets: 1250000,
        trialBalanceLineCount: 18,
        assetClassSectionCount: 2,
        assetClassSections: [
          { assetClass: "Credit", total: 850000 },
          { assetClass: "Cash", total: 400000 }
        ],
        brandingTheme: {
          themeId: "lpcustomtheme",
          name: "LP Custom Theme",
          firmName: "Northstar Capital",
          primaryColor: "#101828",
          accentColor: "#AA5500",
          textColor: "#111827",
          backgroundColor: "#FFFFFF",
          logoUri: "https://example.test/northstar.png",
          footerText: "Northstar Capital confidential.",
          disclaimer: "Prepared for authorized allocator review.",
          isBuiltIn: false
        }
      })
    });

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });
    await user.click(screen.getByRole("button", { name: "Preview LP Custom Theme branded report pack" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/report-pack-preview",
      expect.objectContaining({ method: "POST" })
    ));
    const request = JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string);
    expect(request).toEqual({
      fundProfileId: "fund-alpha",
      reportKind: "BoardPacket",
      brandingThemeId: "lpcustomtheme"
    });

    const status = await screen.findByRole("status", { name: "Preview branded report pack status" });
    expect(status).toHaveTextContent("LP Custom Theme report pack preview rendered.");
    expect(status).toHaveTextContent("Preview ID: 11111111-2222-3333-4444-555555555555");
    expect(status).toHaveTextContent("Total net assets: $1,250,000");
    expect(status).toHaveTextContent("Trial balance lines: 18");
    expect(status).toHaveTextContent("Branding: LP Custom Theme · Northstar Capital · lpcustomtheme");
    expect(status).toHaveTextContent("Asset classes: Credit: $850,000; Cash: $400,000");
  });

  it("generates governed report packs with the selected branding theme", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        reportId: "report-branded-1",
        fundProfileId: "fund-alpha",
        displayName: "Fund Alpha",
        reportKind: "BoardPacket",
        currency: "USD",
        asOf: "2026-06-08T00:00:00Z",
        generatedAt: "2026-06-08T00:01:00Z",
        totalNetAssets: 1250000,
        auditActor: "browser.reporting",
        correlationId: "corr-branded-1",
        decisionRationale: "Generated from Reporting branding theme LP Custom Theme.",
        provenance: {},
        artifacts: [
          {
            artifactKind: "report-pack",
            format: "Pdf",
            relativePath: "fund-alpha/report-branded-1/board.pdf",
            sizeBytes: 2048,
            checksumSha256: "sha256:pdf",
            schemaVersion: 1
          },
          {
            artifactKind: "report-pack",
            format: "Xlsx",
            relativePath: "fund-alpha/report-branded-1/board.xlsx",
            sizeBytes: 1024,
            checksumSha256: "sha256:xlsx",
            schemaVersion: 1
          }
        ],
        warnings: [],
        contractName: "GovernedReportPack",
        schemaVersion: 1,
        brandingTheme: {
          themeId: "lpcustomtheme",
          name: "LP Custom Theme",
          firmName: "Northstar Capital",
          primaryColor: "#101828",
          accentColor: "#AA5500",
          textColor: "#111827",
          backgroundColor: "#FFFFFF",
          logoUri: "https://example.test/northstar.png",
          footerText: "Northstar Capital confidential.",
          disclaimer: "Prepared for authorized allocator review.",
          isBuiltIn: false
        },
        status: "Generated",
        validationIssues: [],
        lifecycleEvents: [],
        auditPackReadiness: null
      })
    });

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });
    await user.click(screen.getByRole("button", { name: "Generate LP Custom Theme branded report pack" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/report-packs",
      expect.objectContaining({ method: "POST" })
    ));
    const request = JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string);
    expect(request).toEqual({
      fundProfileId: "fund-alpha",
      auditActor: "browser.reporting",
      reportKind: "BoardPacket",
      formats: ["Pdf", "Xlsx", "Csv"],
      brandingThemeId: "lpcustomtheme",
      decisionRationale: "Generated from Reporting branding theme LP Custom Theme."
    });
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Generate branded report pack status" })).toHaveTextContent(
        "LP Custom Theme report pack generated."
      );
    });
    expect(screen.getByRole("status", { name: "Generate branded report pack status" })).toHaveTextContent("Artifacts: 2");
  });

  it("generates governed report packs with custom branding overrides", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        reportId: "report-custom-branding-1",
        fundProfileId: "fund-alpha",
        displayName: "Fund Alpha",
        reportKind: "BoardPacket",
        currency: "USD",
        asOf: "2026-06-08T00:00:00Z",
        generatedAt: "2026-06-08T00:01:00Z",
        totalNetAssets: 1250000,
        auditActor: "browser.reporting",
        correlationId: "corr-custom-branding-1",
        decisionRationale: "Generated from custom Reporting branding override Allocator Blue.",
        provenance: {},
        artifacts: [
          {
            artifactKind: "report-pack",
            format: "Pdf",
            relativePath: "fund-alpha/report-custom-branding-1/board.pdf",
            sizeBytes: 2048,
            checksumSha256: "sha256:pdf",
            schemaVersion: 1
          }
        ],
        warnings: [],
        contractName: "GovernedReportPack",
        schemaVersion: 1,
        brandingTheme: {
          themeId: "allocator-blue",
          name: "Allocator Blue",
          firmName: "Blue River Capital",
          primaryColor: "#204E8A",
          accentColor: "#F4B400",
          textColor: "#101828",
          backgroundColor: "#FFFFFF",
          logoUri: "https://example.test/blue-river.png",
          footerText: "Blue River confidential.",
          disclaimer: "For allocator review only.",
          isBuiltIn: false
        },
        status: "Generated",
        validationIssues: [],
        lifecycleEvents: [],
        auditPackReadiness: null
      })
    });

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    fireEvent.change(screen.getByLabelText("Custom branding theme ID"), { target: { value: "allocator-blue" } });
    fireEvent.change(screen.getByLabelText("Custom branding theme name"), { target: { value: "Allocator Blue" } });
    fireEvent.change(screen.getByLabelText("Custom branding firm name"), { target: { value: "Blue River Capital" } });
    fireEvent.change(screen.getByLabelText("Custom branding primary color"), { target: { value: "#204e8a" } });
    fireEvent.change(screen.getByLabelText("Custom branding accent color"), { target: { value: "#f4b400" } });
    fireEvent.change(screen.getByLabelText("Custom branding text color"), { target: { value: "#101828" } });
    fireEvent.change(screen.getByLabelText("Custom branding background color"), { target: { value: "#ffffff" } });
    fireEvent.change(screen.getByLabelText("Custom branding logo URI"), { target: { value: "https://example.test/blue-river.png" } });
    fireEvent.change(screen.getByLabelText("Custom branding footer text"), { target: { value: "Blue River confidential." } });
    fireEvent.change(screen.getByLabelText("Custom branding disclaimer"), { target: { value: "For allocator review only." } });
    await user.click(screen.getByRole("button", { name: "Generate custom branded report pack" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/report-packs",
      expect.objectContaining({ method: "POST" })
    ));
    const request = JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string);
    expect(request).toEqual({
      fundProfileId: "fund-alpha",
      auditActor: "browser.reporting",
      reportKind: "BoardPacket",
      formats: ["Pdf", "Xlsx", "Csv"],
      brandingThemeOverride: {
        themeId: "allocator-blue",
        name: "Allocator Blue",
        firmName: "Blue River Capital",
        primaryColor: "#204E8A",
        accentColor: "#F4B400",
        textColor: "#101828",
        backgroundColor: "#FFFFFF",
        logoUri: "https://example.test/blue-river.png",
        footerText: "Blue River confidential.",
        disclaimer: "For allocator review only.",
        isBuiltIn: false
      },
      decisionRationale: "Generated from custom Reporting branding override Allocator Blue."
    });
    expect(request).not.toHaveProperty("brandingThemeId");
    expect(await screen.findByRole("status", { name: "Generate custom branded report pack status" })).toHaveTextContent(
      "Allocator Blue report pack generated."
    );
  });

  it("previews custom branded report packs before generation", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        reportId: "preview-custom-branding-1",
        fundProfileId: "fund-alpha",
        displayName: "Fund Alpha",
        reportKind: "BoardPacket",
        currency: "USD",
        asOf: "2026-06-08T00:00:00Z",
        generatedAt: "2026-06-08T00:00:03Z",
        totalNetAssets: 2500000,
        trialBalanceLineCount: 21,
        assetClassSectionCount: 1,
        assetClassSections: [
          { assetClass: "Private credit", total: 2500000 }
        ],
        brandingTheme: {
          themeId: "lpcustomtheme",
          name: "Allocator Blue",
          firmName: "Northstar Capital",
          primaryColor: "#123456",
          accentColor: "#AA5500",
          textColor: "#111111",
          backgroundColor: "#FAFAFA",
          logoUri: "https://example.test/northstar.png",
          footerText: "Northstar investor reporting",
          disclaimer: "Prepared for authorized allocator review.",
          isBuiltIn: false
        }
      })
    });

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    fireEvent.change(screen.getByLabelText("Custom branding theme name"), { target: { value: "Allocator Blue" } });
    await user.click(screen.getByRole("button", { name: "Preview custom branded report pack" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/report-pack-preview",
      expect.objectContaining({ method: "POST" })
    ));
    expect(JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string)).toEqual({
      fundProfileId: "fund-alpha",
      reportKind: "BoardPacket",
      brandingThemeOverride: {
        themeId: "lpcustomtheme",
        name: "Allocator Blue",
        firmName: "Northstar Capital",
        primaryColor: "#123456",
        accentColor: "#AA5500",
        textColor: "#111111",
        backgroundColor: "#FAFAFA",
        logoUri: "https://example.test/northstar.png",
        footerText: "Northstar investor reporting",
        disclaimer: "Prepared for authorized allocator review.",
        isBuiltIn: false
      }
    });

    const status = await screen.findByRole("status", { name: "Preview custom branded report pack status" });
    expect(status).toHaveTextContent("Allocator Blue report pack preview rendered.");
    expect(status).toHaveTextContent("Total net assets: $2,500,000");
    expect(status).toHaveTextContent("Branding: Allocator Blue · Northstar Capital · lpcustomtheme");
    expect(status).toHaveTextContent("Asset classes: Private credit: $2,500,000");
  });

  it("keeps themed report-pack generation disabled without fund context", () => {
    renderWithRouter(
      <ReportingScreen
        data={{
          ...accounting,
          reporting: {
            ...accounting.reporting,
            fundProfileId: null,
            selectedFundProfileId: null,
            workflowRecords: []
          }
        }}
      />,
      { initialEntries: ["/reporting/report-builder"] }
    );

    expect(screen.getByRole("button", { name: "Generate Meridian Standard branded report pack" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Preview Meridian Standard branded report pack" })).toBeDisabled();
  });

  it("renders template designer lifecycle controls for governed versions", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    const lineage = screen.getByRole("group", { name: "Investor Monthly Statement template audit and version lineage" });
    const templateRow = lineage.parentElement;
    expect(templateRow).not.toBeNull();
    expect(within(templateRow!).getByText("Investor Monthly Statement")).toBeInTheDocument();
    expect(within(templateRow!).getByText("Approved")).toBeInTheDocument();
    expect(within(templateRow!).getByText("Built-in")).toBeInTheDocument();
    expect(lineage).toHaveTextContent("investor-monthly-statement@v1.0.0 no prior template");
    expect(lineage).toHaveTextContent("2 audit events");
    expect(lineage).toHaveTextContent("approve InReview->Approved by controller.admin");
    expect(lineage).toHaveTextContent("Latest approved");
    expect(lineage).toHaveTextContent("ref APP-TPL-INVESTOR-1");
    expect(lineage).toHaveTextContent("Controller approved investor statement baseline.");
    expect(lineage).toHaveTextContent("No validation issues");
    const accessGovernance = screen.getByRole("group", {
      name: "Investor Monthly Statement access governance Company-wide Runnable"
    });
    expect(accessGovernance).toHaveTextContent("Company-wide");
    expect(accessGovernance).toHaveTextContent("Company");
    expect(accessGovernance).toHaveTextContent("Runnable");
    expect(accessGovernance).toHaveTextContent("Company-wide access; run and lifecycle actions use the shared access evaluation.");
    expect(within(templateRow!).getByText("Built-in approved template for InvestorStatement.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Draft a revision of Investor Monthly Statement" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/templates/investor-monthly-statement/versions/1"
    );
    expect(screen.getByRole("button", { name: "Run Investor Monthly Statement report on demand" })).toBeEnabled();
  });

  it("submits custom report-template drafts for review", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify(reportTemplateGovernanceResponse({
        name: "custom-exposure-draft",
        version: 2,
        displayName: "Custom Exposure Draft",
        status: "InReview",
        rationale: "Ready for controller review."
      }))
    });
    const customDraft = withReportTemplate({
      templateId: "custom-exposure-draft",
      family: "CustomReport",
      name: "Custom Exposure Draft",
      version: "2",
      sections: ["exposures"],
      lifecycleStatus: "Draft",
      isBuiltIn: false,
      isLatestApproved: false,
      approvalSummary: "Draft by reporting.ops.",
      authoringRoute: "/api/fund-structure/reporting/templates/custom-exposure-draft/versions/2",
      createdBy: "reporting.ops",
      createdAt: "2026-06-08T10:00:00Z",
      updatedBy: "reporting.ops",
      updatedAt: "2026-06-08T10:15:00Z",
      basedOnTemplateId: { name: "custom-exposure-draft", version: 1 },
      auditTrail: [
        {
          at: "2026-06-08T10:15:00Z",
          actor: "reporting.ops",
          action: "draft",
          fromStatus: "Draft",
          toStatus: "Draft",
          note: "Added exposure columns."
        }
      ],
      validationIssues: ["Approval note required before publication."]
    });

    renderWithRouter(<ReportingScreen data={customDraft} />, { initialEntries: ["/reporting/report-builder"] });
    const lineage = screen.getByRole("group", { name: "Custom Exposure Draft template audit and version lineage" });
    expect(lineage).toHaveTextContent("custom-exposure-draft@v2 based on custom-exposure-draft@v1");
    expect(lineage).toHaveTextContent("1 audit event");
    expect(lineage).toHaveTextContent("draft Draft->Draft by reporting.ops");
    expect(lineage).toHaveTextContent("Not latest approved");
    expect(lineage).toHaveTextContent("1 validation issue");

    await user.click(screen.getByRole("button", {
      name: "Submit Custom Exposure Draft template version 2 for review"
    }));

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/templates/custom-exposure-draft/versions/2/submit",
      expect.objectContaining({ method: "POST" })
    );
    const [, request] = fetchMock.mock.calls[0];
    expect(JSON.parse((request as RequestInit).body as string)).toEqual({
      rationale: "Ready for controller review."
    });
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Report template lifecycle status" })).toHaveTextContent(
        "Custom Exposure Draft moved to InReview."
      );
    });
  });

  it("renders blocked access governance for inaccessible report templates", () => {
    const inaccessibleTemplate = withReportTemplate({
      templateId: "allocator-private-pack",
      family: "CustomReport",
      name: "Allocator Private Pack",
      version: "4",
      sections: ["summary"],
      lifecycleStatus: "Approved",
      isBuiltIn: false,
      isLatestApproved: true,
      approvalSummary: "Approved by allocator.owner.",
      authoringRoute: "/api/fund-structure/reporting/templates/allocator-private-pack/versions/4",
      accessMode: "Restricted",
      accessSummary: "Restricted to group allocator-relations",
      isAccessible: false,
      createdBy: "allocator.owner",
      createdAt: "2026-06-08T10:00:00Z",
      updatedBy: "allocator.owner",
      updatedAt: "2026-06-08T10:15:00Z",
      auditTrail: [],
      validationIssues: []
    });

    renderWithRouter(<ReportingScreen data={inaccessibleTemplate} />, { initialEntries: ["/reporting/report-builder"] });

    const accessGovernance = screen.getByRole("group", {
      name: "Allocator Private Pack access governance User/group Access blocked"
    });
    expect(accessGovernance).toHaveTextContent("User/group");
    expect(accessGovernance).toHaveTextContent("Group");
    expect(accessGovernance).toHaveTextContent("Access blocked");
    expect(accessGovernance).toHaveTextContent(
      "Restricted to group allocator-relations; run and lifecycle actions are disabled by the shared access evaluation."
    );
    expect(screen.getByRole("button", { name: "Run Allocator Private Pack report on demand" })).toBeDisabled();
  });

  it("approves and rejects custom report-template versions in review", async () => {
    const user = userEvent.setup();
    fetchMock
      .mockResolvedValueOnce({
        ok: true,
        text: async () => JSON.stringify(reportTemplateGovernanceResponse({
          name: "custom-exposure-review",
          version: 3,
          displayName: "Custom Exposure Review",
          status: "Approved",
          rationale: "Approved from browser Reporting workspace.",
          approvalReference: "browser-template-approval:custom-exposure-review:v3"
        }))
      })
      .mockResolvedValueOnce({
        ok: true,
        text: async () => JSON.stringify(reportTemplateGovernanceResponse({
          name: "custom-exposure-review",
          version: 3,
          displayName: "Custom Exposure Review",
          status: "Rejected",
          rationale: "Returned from browser Reporting workspace."
        }))
      });
    const customReview = withReportTemplate({
      templateId: "custom-exposure-review",
      family: "CustomReport",
      name: "Custom Exposure Review",
      version: "3",
      sections: ["exposures"],
      lifecycleStatus: "InReview",
      isBuiltIn: false,
      isLatestApproved: false,
      approvalSummary: "Submitted by reporting.ops.",
      authoringRoute: "/api/fund-structure/reporting/templates/custom-exposure-review/versions/3"
    });

    renderWithRouter(<ReportingScreen data={customReview} />, { initialEntries: ["/reporting/report-builder"] });
    await user.click(screen.getByRole("button", {
      name: "Approve Custom Exposure Review template version 3"
    }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    await user.click(screen.getByRole("button", {
      name: "Reject Custom Exposure Review template version 3"
    }));

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "/api/fund-structure/reporting/templates/custom-exposure-review/versions/3/approve",
      expect.objectContaining({ method: "POST" })
    );
    expect(JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string)).toEqual({
      rationale: "Approved from browser Reporting workspace.",
      approvalReference: "browser-template-approval:custom-exposure-review:v3"
    });
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "/api/fund-structure/reporting/templates/custom-exposure-review/versions/3/reject",
      expect.objectContaining({ method: "POST" })
    );
    expect(JSON.parse((fetchMock.mock.calls[1]?.[1] as RequestInit).body as string)).toEqual({
      rationale: "Returned from browser Reporting workspace."
    });
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Report template lifecycle status" })).toHaveTextContent(
        "Custom Exposure Review moved to Rejected."
      );
    });
  });

  it("renders no-code report-writer grid fields and supports local drag-and-drop drafts", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    const writer = screen.getByRole("region", { name: "No-code report writer" });
    const grid = within(writer).getByRole("group", {
      name: "Investor Monthly Statement Sector Pivot Pivot report-writer grid"
    });
    expect(grid).toHaveTextContent("Pivot grid with 2 dimensions, 2 metrics, 1 formula, and 1 saved filter.");
    expect(grid).toHaveTextContent("Descending by pnl");
    expect(grid).toHaveTextContent("1 saved filter");
    expect(within(grid).getByRole("status", { name: "Sector Pivot current report-writer layout" })).toHaveTextContent(
      "Pivot draft · 2 dimensions · 2 metrics · 1 formula · 2 filters including draft filter"
    );
    expect(within(grid).getByLabelText("Sector Pivot saved filters")).toHaveTextContent("strategy = Core");
    expect(within(grid).getByLabelText("Sector Pivot preview dataset")).toHaveValue("portfolioPositions");
    expect(within(grid).getByLabelText("Sector Pivot preview dataset")).toHaveTextContent("Portfolio positions");
    expect(within(grid).getByLabelText("Sector Pivot preview dataset")).toHaveTextContent("Ledger facts");
    expect(within(grid).getByLabelText("Sector Pivot preview dataset")).toHaveTextContent("Cash ladder");
    expect(within(grid).getByLabelText("Sector Pivot preview dataset")).toHaveTextContent("Retained reporting rows (2)");
    expect(within(grid).getByLabelText("Sector Pivot preview dataset")).toHaveTextContent("Portfolio reporting cuts (2)");
    expect(within(grid).getByLabelText("Sector Pivot preview dataset")).toHaveTextContent("Top-N contribution analytics (1)");
    expect(within(grid).getByLabelText("Sector Pivot filter field")).toHaveValue("strategy");
    expect(within(grid).getByLabelText("Sector Pivot filter operator")).toHaveValue("Equals");
    expect(within(grid).getByLabelText("Sector Pivot filter value")).toHaveValue("Core");
    expect(within(grid).getByRole("list", { name: "Sector Pivot Rows" })).toHaveTextContent("sector");
    expect(within(grid).getByRole("list", { name: "Sector Pivot Columns" })).toHaveTextContent("strategy");
    expect(within(grid).getByRole("list", { name: "Sector Pivot Metrics" })).toHaveTextContent("Market value");
    expect(within(grid).getByRole("list", { name: "Sector Pivot Formulas" })).toHaveTextContent("Return %");
    expect(within(grid).getByRole("list", { name: "Sector Pivot source fields" })).toHaveTextContent("Gross exposure");
    expect(within(grid).getByRole("list", { name: "Sector Pivot source fields" })).toHaveTextContent("Contribution %");

    const sourceField = within(within(grid).getByRole("list", { name: "Sector Pivot source fields" }))
      .getByText("pnl")
      .closest("[role='listitem']");
    expect(sourceField).not.toBeNull();
    const formulas = within(grid).getByRole("list", { name: "Sector Pivot Formulas" });
    const transferStore = new Map<string, string>();
    const dataTransfer = {
      effectAllowed: "",
      setData: vi.fn((type: string, value: string) => transferStore.set(type, value)),
      getData: vi.fn((type: string) => transferStore.get(type) ?? "")
    };

    fireEvent.dragStart(sourceField!, { dataTransfer });
    fireEvent.drop(formulas, { dataTransfer });

    expect(within(formulas).getByText("pnl")).toBeInTheDocument();
    expect(within(grid).getByRole("status", { name: "Sector Pivot current report-writer layout" })).toHaveTextContent(
      "Pivot draft · 2 dimensions · 2 metrics · 2 formulas · 2 filters including draft filter"
    );
    let formulaTokenLabels = within(formulas).getAllByRole("listitem").map((item) => item.textContent ?? "");
    expect(formulaTokenLabels[0]).toContain("Return %");
    expect(formulaTokenLabels[1]).toContain("pnl");
    await user.click(within(formulas).getByRole("button", { name: "Move pnl left" }));
    formulaTokenLabels = within(formulas).getAllByRole("listitem").map((item) => item.textContent ?? "");
    expect(formulaTokenLabels[0]).toContain("pnl");
    expect(formulaTokenLabels[1]).toContain("Return %");
    await user.click(within(formulas).getByRole("button", { name: "Move pnl right" }));
    formulaTokenLabels = within(formulas).getAllByRole("listitem").map((item) => item.textContent ?? "");
    expect(formulaTokenLabels[0]).toContain("Return %");
    expect(formulaTokenLabels[1]).toContain("pnl");
    const movedFormulaToken = within(formulas).getByText("pnl").closest("[role='listitem']");
    expect(movedFormulaToken).not.toBeNull();
    const metrics = within(grid).getByRole("list", { name: "Sector Pivot Metrics" });
    fireEvent.dragStart(movedFormulaToken!, { dataTransfer });
    fireEvent.drop(metrics, { dataTransfer });
    expect(within(formulas).queryByText("pnl")).not.toBeInTheDocument();
    expect(within(metrics).getByText("pnl")).toBeInTheDocument();
    expect(within(grid).getByRole("status", { name: "Sector Pivot current report-writer layout" })).toHaveTextContent(
      "Pivot draft · 2 dimensions · 3 metrics · 1 formula · 2 filters including draft filter"
    );
    await user.click(within(metrics).getByRole("button", { name: "Remove pnl" }));
    expect(within(metrics).queryByText("pnl")).not.toBeInTheDocument();
    expect(within(grid).getByRole("status", { name: "Sector Pivot current report-writer layout" })).toHaveTextContent(
      "Pivot draft · 2 dimensions · 2 metrics · 1 formula · 2 filters including draft filter"
    );
    fireEvent.dragStart(sourceField!, { dataTransfer });
    fireEvent.drop(formulas, { dataTransfer });
    expect(within(formulas).getByText("pnl")).toBeInTheDocument();
    await user.click(within(formulas).getByRole("button", { name: "Remove pnl" }));
    expect(within(formulas).queryByText("pnl")).not.toBeInTheDocument();
    fireEvent.dragStart(sourceField!, { dataTransfer });
    fireEvent.drop(formulas, { dataTransfer });
    expect(within(formulas).getByText("pnl")).toBeInTheDocument();
    await user.click(within(grid).getByRole("button", { name: "Reset Sector Pivot report-writer draft" }));
    expect(within(formulas).queryByText("pnl")).not.toBeInTheDocument();
  });

  it("saves no-code report-writer drafts through the governed template endpoint", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        definition: {
          templateId: { name: "investor-monthly-statement", version: 2 },
          displayName: "Sector Pivot Draft",
          parameters: [],
          sections: [],
          grids: [],
          accessPolicy: null
        },
        status: "Draft",
        family: "InvestorStatement",
        isBuiltIn: false,
        isLatestApproved: false,
        createdBy: "controller.admin",
        createdAt: "2026-06-08T00:00:00Z",
        updatedBy: "controller.admin",
        updatedAt: "2026-06-08T00:00:00Z",
        validationIssues: [],
        auditTrail: []
      })
    });
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    const writer = screen.getByRole("region", { name: "No-code report writer" });
    const grid = within(writer).getByRole("group", {
      name: "Investor Monthly Statement Sector Pivot Pivot report-writer grid"
    });

    await user.selectOptions(within(grid).getByLabelText("Sector Pivot draft access mode"), "Restricted");
    expect(within(grid).getByLabelText("Sector Pivot draft principal kind")).toBeEnabled();
    expect(grid).toHaveTextContent("Access policy: group reporting-ops.");
    expect(within(grid).getByLabelText("Sector Pivot draft top-n count")).toBeDisabled();
    await user.selectOptions(within(grid).getByLabelText("Sector Pivot draft grid type"), "TopN");
    const topNInput = within(grid).getByLabelText("Sector Pivot draft top-n count");
    expect(topNInput).toBeEnabled();
    fireEvent.change(topNInput, {
      target: { value: "5" }
    });
    expect(grid).toHaveTextContent("Top 5");
    fireEvent.change(within(grid).getByLabelText("Sector Pivot custom formula name"), {
      target: { value: "gainLossRatio" }
    });
    fireEvent.change(within(grid).getByLabelText("Sector Pivot custom formula label"), {
      target: { value: "Gain/loss ratio" }
    });
    fireEvent.change(within(grid).getByLabelText("Sector Pivot custom formula expression"), {
      target: { value: "{pnl} / {marketValue}" }
    });
    await user.click(within(grid).getByRole("button", { name: "Save Sector Pivot as governed report template draft" }));

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/templates/drafts",
      expect.objectContaining({
        method: "POST"
      })
    );
    const [, request] = fetchMock.mock.calls[0];
    const body = JSON.parse((request as RequestInit).body as string);
    expect(body).toMatchObject({
      name: "investor-monthly-statement",
      displayName: "Sector Pivot Draft",
      sections: [],
      parameters: [],
      family: "InvestorStatement",
      basedOnVersion: 1,
      grids: [
        {
          gridId: "sector-pivot",
          title: "Sector Pivot",
          kind: "TopN",
          rowFields: ["sector"],
          columnFields: ["strategy"],
          metrics: [
            { name: "marketValue", sourceField: "marketValue", function: "Sum", label: "Market value" },
            { name: "pnl", sourceField: "pnl", function: "Sum", label: "P&L" }
          ],
          formulas: [
            { name: "returnPct", expression: "{pnl} / {marketValue} * 100", label: "Return %" },
            { name: "gainLossRatio", expression: "{pnl} / {marketValue}", label: "Gain/loss ratio" }
          ],
          topN: 5,
          sortBy: "pnl",
          sortDescending: true,
          filters: [
            { field: "strategy", operator: "Equals", value: "Core", label: "strategy = Core" }
          ]
        }
      ],
      accessPolicy: {
        mode: "Restricted",
        ownerPrincipalId: null,
        principals: [
          { kind: "Group", principalId: "reporting-ops", displayName: "reporting-ops" }
        ],
        allowOwnerAccess: true
      }
    });
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Save report-writer draft status" })).toHaveTextContent(
        "Sector Pivot Draft draft saved."
      );
    });
  });

  it("locks private no-code report-writer drafts to a user owner", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        definition: {
          templateId: { name: "investor-monthly-statement", version: 2 },
          displayName: "Sector Pivot Draft",
          parameters: [],
          sections: [],
          grids: [],
          accessPolicy: null
        },
        status: "Draft",
        family: "InvestorStatement",
        isBuiltIn: false,
        isLatestApproved: false,
        createdBy: "controller.admin",
        createdAt: "2026-06-08T00:00:00Z",
        updatedBy: "controller.admin",
        updatedAt: "2026-06-08T00:00:00Z",
        validationIssues: [],
        auditTrail: []
      })
    });
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    const writer = screen.getByRole("region", { name: "No-code report writer" });
    const grid = within(writer).getByRole("group", {
      name: "Investor Monthly Statement Sector Pivot Pivot report-writer grid"
    });

    await user.selectOptions(within(grid).getByLabelText("Sector Pivot draft access mode"), "Private");
    const principalKind = within(grid).getByLabelText("Sector Pivot draft principal kind");
    expect(principalKind).toBeDisabled();
    expect(principalKind).toHaveValue("User");
    expect(grid).toHaveTextContent("Owner ID");

    fireEvent.change(within(grid).getByLabelText("Sector Pivot draft principal id"), {
      target: { value: "controller.admin" }
    });
    expect(grid).toHaveTextContent("Access policy: user-locked to controller.admin.");
    await user.click(within(grid).getByRole("button", { name: "Save Sector Pivot as governed report template draft" }));

    const [, request] = fetchMock.mock.calls[0];
    const body = JSON.parse((request as RequestInit).body as string);
    expect(body.accessPolicy).toEqual({
      mode: "Private",
      ownerPrincipalId: "controller.admin",
      principals: [
        { kind: "User", principalId: "controller.admin", displayName: "controller.admin" }
      ],
      allowOwnerAccess: true
    });
  });

  it("scopes restricted no-code report-writer drafts to company principals", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        definition: {
          templateId: { name: "investor-monthly-statement", version: 2 },
          displayName: "Sector Pivot Draft",
          parameters: [],
          sections: [],
          grids: [],
          accessPolicy: null
        },
        status: "Draft",
        family: "InvestorStatement",
        isBuiltIn: false,
        isLatestApproved: false,
        createdBy: "controller.admin",
        createdAt: "2026-06-08T00:00:00Z",
        updatedBy: "controller.admin",
        updatedAt: "2026-06-08T00:00:00Z",
        validationIssues: [],
        auditTrail: []
      })
    });
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    const writer = screen.getByRole("region", { name: "No-code report writer" });
    const grid = within(writer).getByRole("group", {
      name: "Investor Monthly Statement Sector Pivot Pivot report-writer grid"
    });

    await user.selectOptions(within(grid).getByLabelText("Sector Pivot draft access mode"), "Restricted");
    await user.selectOptions(within(grid).getByLabelText("Sector Pivot draft principal kind"), "Company");
    fireEvent.change(within(grid).getByLabelText("Sector Pivot draft principal id"), {
      target: { value: "company-alpha" }
    });
    expect(grid).toHaveTextContent("Access policy: company company-alpha.");
    await user.click(within(grid).getByRole("button", { name: "Save Sector Pivot as governed report template draft" }));

    const [, request] = fetchMock.mock.calls[0];
    const body = JSON.parse((request as RequestInit).body as string);
    expect(body.accessPolicy).toEqual({
      mode: "Restricted",
      ownerPrincipalId: null,
      principals: [
        { kind: "Company", principalId: "company-alpha", displayName: "company-alpha" }
      ],
      allowOwnerAccess: true
    });
  });

  it("previews no-code report-writer drafts through the shared render endpoint", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        templateId: { name: "investor-monthly-statement", version: 1 },
        renderedContent: "template:investor-monthly-statement@v1;grids=sector-pivot:2r",
        missingRequiredParameters: [],
        grids: [
          {
            gridId: "sector-pivot",
            title: "Sector Pivot",
            kind: "Pivot",
            columns: [
              { key: "sector", label: "sector", role: "dimension" },
              { key: "marketValue", label: "Market value", role: "metric" },
              { key: "pnl", label: "P&L", role: "metric" },
              { key: "returnPct", label: "Return %", role: "formula" },
              { key: "gainLossRatio", label: "Gain/loss ratio", role: "formula" }
            ],
            rows: [
              {
                rowKey: "Technology",
                values: {
                  sector: "Technology",
                  marketValue: "150",
                  pnl: "15",
                  returnPct: "10",
                  gainLossRatio: "0.1"
                }
              },
              {
                rowKey: "Rates",
                values: {
                  sector: "Rates",
                  marketValue: "75",
                  pnl: "-2",
                  returnPct: "-2.666667",
                  gainLossRatio: "-0.026667"
                }
              }
            ],
            warnings: ["Preview capped to 5 displayed rows."],
            lineage: {
              inputRowCount: 4,
              filteredInputRowCount: 2,
              outputRowCount: 2,
              sourceFields: ["marketValue", "pnl", "sector", "strategy"],
              metrics: [
                { name: "marketValue", sourceField: "marketValue", function: "Sum" },
                { name: "pnl", sourceField: "pnl", function: "Sum" }
              ],
              formulas: [
                { name: "returnPct", expression: "{pnl} / {marketValue} * 100", sourceFields: ["marketValue", "pnl"] },
                { name: "gainLossRatio", expression: "{pnl} / {marketValue}", sourceFields: ["marketValue", "pnl"] }
              ],
              filters: [
                { field: "strategy", operator: "Equals", value: "Core", label: "strategy = Core" }
              ]
            },
            dataDictionary: [
              {
                key: "sector",
                label: "Sector",
                role: "dimension",
                sourceField: "sector",
                dataType: "string",
                isGenerated: false,
                description: "Portfolio sector dimension."
              },
              {
                key: "gainLossRatio",
                label: "Gain/loss ratio",
                role: "formula",
                sourceField: "gainLossRatio",
                dataType: "decimal",
                isGenerated: true,
                description: "Custom browser-authored formula."
              }
            ],
            validationChecks: [
              {
                checkId: "metric-source-fields",
                status: "Passed",
                detail: "All metrics resolve to retained source fields."
              },
              {
                checkId: "custom-formula-lineage",
                status: "Review",
                detail: "Formula references marketValue and pnl from the preview dataset."
              }
            ]
          }
        ],
        warnings: []
      })
    });
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    const writer = screen.getByRole("region", { name: "No-code report writer" });
    const grid = within(writer).getByRole("group", {
      name: "Investor Monthly Statement Sector Pivot Pivot report-writer grid"
    });

    await user.selectOptions(within(grid).getByLabelText("Sector Pivot preview dataset"), "ledgerFacts");
    fireEvent.change(within(grid).getByLabelText("Sector Pivot custom formula name"), {
      target: { value: "gainLossRatio" }
    });
    fireEvent.change(within(grid).getByLabelText("Sector Pivot custom formula label"), {
      target: { value: "Gain/loss ratio" }
    });
    fireEvent.change(within(grid).getByLabelText("Sector Pivot custom formula expression"), {
      target: { value: "{pnl} / {marketValue}" }
    });
    await user.click(within(grid).getByRole("button", { name: "Preview Sector Pivot report-writer grid" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/templates/render",
      expect.objectContaining({
        method: "POST",
        body: expect.any(String)
      })
    ));
    const request = JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string);
    expect(request).toMatchObject({
      templateId: { name: "investor-monthly-statement", version: 1 },
      parameters: {
        preview: "browser-report-writer",
        previewDataset: "ledgerFacts"
      },
      grids: [
        {
          gridId: "sector-pivot",
          title: "Sector Pivot",
          kind: "Pivot",
          rowFields: ["sector"],
          columnFields: ["strategy"],
          metrics: [
            { name: "marketValue", sourceField: "marketValue", function: "Sum", label: "Market value" },
            { name: "pnl", sourceField: "pnl", function: "Sum", label: "P&L" }
          ],
          formulas: [
            { name: "returnPct", expression: "{pnl} / {marketValue} * 100", label: "Return %" },
            { name: "gainLossRatio", expression: "{pnl} / {marketValue}", label: "Gain/loss ratio" }
          ],
          filters: [
            { field: "strategy", operator: "Equals", value: "Core", label: "strategy = Core" }
          ]
        }
      ]
    });
    expect(request.datasetRows).toHaveLength(4);
    expect(request.datasetRows[0]).toMatchObject({
      previewDataset: "ledgerFacts",
      sector: "Operating expense",
      strategy: "Core",
      marketValue: "250",
      pnl: "25"
    });
    expect(request.datasetRows[1]).toMatchObject({
      strategy: "Core"
    });
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Preview report-writer grid status" })).toHaveTextContent(
        "Sector Pivot preview rendered."
      );
    });
    const preview = within(grid).getByLabelText("Sector Pivot live preview");
    expect(within(preview).getByText("Technology")).toBeInTheDocument();
    expect(within(preview).getByText("150")).toBeInTheDocument();
    expect(within(preview).getByText("Return %")).toBeInTheDocument();
    expect(within(preview).getAllByText("Gain/loss ratio").length).toBeGreaterThan(1);
    expect(within(preview).getAllByText("dimension").length).toBeGreaterThan(0);
    expect(within(preview).getAllByText("metric").length).toBeGreaterThan(0);
    expect(within(preview).getAllByText("formula").length).toBeGreaterThan(0);
    const auditTrace = within(preview).getByLabelText("Sector Pivot preview audit trace");
    expect(auditTrace).toHaveTextContent("4 input / 2 filtered / 2 output");
    expect(auditTrace).toHaveTextContent("marketValue, pnl, sector, strategy");
    expect(auditTrace).toHaveTextContent("marketValue=Sum(marketValue)");
    expect(auditTrace).toHaveTextContent("gainLossRatio={pnl} / {marketValue} [marketValue, pnl]");
    expect(auditTrace).toHaveTextContent("strategy = Core");
    const dataDictionary = within(preview).getByLabelText("Sector Pivot preview data dictionary");
    expect(dataDictionary).toHaveTextContent("Sector");
    expect(dataDictionary).toHaveTextContent("Portfolio sector dimension.");
    expect(dataDictionary).toHaveTextContent("Gain/loss ratio");
    expect(dataDictionary).toHaveTextContent("Custom browser-authored formula.");
    const validationChecks = within(preview).getByLabelText("Sector Pivot preview validation checks");
    expect(validationChecks).toHaveTextContent("metric-source-fields");
    expect(validationChecks).toHaveTextContent("All metrics resolve to retained source fields.");
    expect(validationChecks).toHaveTextContent("custom-formula-lineage");
    expect(validationChecks).toHaveTextContent("Formula references marketValue and pnl from the preview dataset.");
    expect(within(preview).getByRole("list", { name: "Sector Pivot preview warnings" })).toHaveTextContent(
      "Preview capped to 5 displayed rows."
    );
  });

  it("previews no-code report-writer grids from retained source-backed dataset rows", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        templateId: { name: "investor-monthly-statement", version: 1 },
        renderedContent: "template:investor-monthly-statement@v1;grids=sector-pivot:2r",
        missingRequiredParameters: [],
        grids: [
          {
            gridId: "sector-pivot",
            title: "Sector Pivot",
            kind: "Pivot",
            columns: [
              { key: "strategy", label: "strategy", role: "dimension" },
              { key: "grossExposure", label: "Gross exposure", role: "metric" },
              { key: "totalPnl", label: "Total P&L", role: "metric" }
            ],
            rows: [
              {
                rowKey: "All",
                values: {
                  strategy: "All",
                  grossExposure: "400",
                  totalPnl: "50"
                }
              }
            ],
            warnings: [],
            lineage: {
              inputRowCount: 2,
              filteredInputRowCount: 2,
              outputRowCount: 1,
              sourceFields: ["grossExposure", "strategy", "totalPnl"],
              metrics: [
                { name: "grossExposure", sourceField: "grossExposure", function: "Sum" },
                { name: "totalPnl", sourceField: "totalPnl", function: "Sum" }
              ],
              formulas: [],
              filters: []
            },
            dataDictionary: [],
            validationChecks: []
          }
        ],
        warnings: []
      })
    });
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    const writer = screen.getByRole("region", { name: "No-code report writer" });
    const grid = within(writer).getByRole("group", {
      name: "Investor Monthly Statement Sector Pivot Pivot report-writer grid"
    });

    await user.selectOptions(within(grid).getByLabelText("Sector Pivot preview dataset"), "source:portfolio-reporting-cuts");
    expect(within(grid).getByLabelText("Sector Pivot retained dataset source")).toHaveTextContent("Portfolio reporting cuts");
    expect(within(grid).getByLabelText("Sector Pivot retained dataset source")).toHaveTextContent("2 rows");
    const fieldCatalog = within(grid).getByRole("list", { name: "Sector Pivot retained dataset field catalog" });
    expect(fieldCatalog).toHaveTextContent("Dataset");
    expect(fieldCatalog).toHaveTextContent("dataset · dimension · string");
    expect(fieldCatalog).toHaveTextContent("Gross exposure");
    expect(fieldCatalog).toHaveTextContent("grossExposure · metric · decimal");
    expect(fieldCatalog).toHaveTextContent("Shadow NAV");
    expect(fieldCatalog).toHaveTextContent("Total P&L");
    await user.click(within(grid).getByRole("button", { name: "Preview Sector Pivot report-writer grid" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/templates/render",
      expect.objectContaining({
        method: "POST",
        body: expect.any(String)
      })
    ));
    const request = JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string);
    expect(request.parameters.previewDataset).toBe("source:portfolio-reporting-cuts");
    expect(request.datasetRows).toEqual(accounting.reporting.reportWriterDatasetSources![1].rows);
    expect(request.datasetRows[0]).toMatchObject({
      dataset: "portfolio-cut",
      kind: "Fund",
      grossExposure: "400",
      shadowNav: "450",
      totalPnl: "50"
    });
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Preview report-writer grid status" })).toHaveTextContent(
        "Sector Pivot preview rendered."
      );
    });
  });

  it("previews contribution drafts with P&L-first metrics and generated contribution formulas", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        templateId: { name: "investor-monthly-statement", version: 1 },
        renderedContent: "template:investor-monthly-statement@v1;grids=sector-pivot:3r",
        missingRequiredParameters: [],
        grids: [
          {
            gridId: "sector-pivot",
            title: "Sector Pivot",
            kind: "Contribution",
            columns: [
              { key: "sector", label: "sector", role: "dimension" },
              { key: "strategy", label: "strategy", role: "dimension" },
              { key: "pnl", label: "P&L", role: "metric" },
              { key: "marketValue", label: "Market value", role: "metric" },
              { key: "contributionPercent", label: "Contribution %", role: "formula" },
              { key: "contributionAbsPercent", label: "Abs Contribution %", role: "formula" },
              { key: "signedContributionCheck", label: "Signed contribution check", role: "formula" }
            ],
            rows: [
              {
                rowKey: "Operating expense|Core",
                values: {
                  sector: "Operating expense",
                  strategy: "Core",
                  pnl: "150",
                  marketValue: "250",
                  contributionPercent: "75",
                  contributionAbsPercent: "75",
                  signedContributionCheck: "75"
                }
              },
              {
                rowKey: "Capital activity|Investor activity",
                values: {
                  sector: "Capital activity",
                  strategy: "Investor activity",
                  pnl: "-50",
                  marketValue: "125",
                  contributionPercent: "-25",
                  contributionAbsPercent: "25",
                  signedContributionCheck: "-25"
                }
              },
              {
                rowKey: "Financing|Cash financing",
                values: {
                  sector: "Financing",
                  strategy: "Cash financing",
                  pnl: "0",
                  marketValue: "80",
                  contributionPercent: "0",
                  contributionAbsPercent: "0",
                  signedContributionCheck: "0"
                }
              }
            ],
            warnings: [],
            lineage: {
              inputRowCount: 4,
              filteredInputRowCount: 4,
              outputRowCount: 3,
              sourceFields: ["marketValue", "pnl", "sector", "strategy"],
              metrics: [
                { name: "pnl", sourceField: "pnl", function: "Sum" },
                { name: "marketValue", sourceField: "marketValue", function: "Sum" }
              ],
              formulas: [
                { name: "returnPct", expression: "{pnl} / {marketValue} * 100", sourceFields: ["marketValue", "pnl"] },
                { name: "signedContributionCheck", expression: "{contributionPercent}", sourceFields: ["contributionPercent"] }
              ],
              filters: []
            }
          }
        ],
        warnings: []
      })
    });
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    const writer = screen.getByRole("region", { name: "No-code report writer" });
    const grid = within(writer).getByRole("group", {
      name: "Investor Monthly Statement Sector Pivot Pivot report-writer grid"
    });

    await user.selectOptions(within(grid).getByLabelText("Sector Pivot preview dataset"), "ledgerFacts");
    await user.selectOptions(within(grid).getByLabelText("Sector Pivot draft grid type"), "Contribution");
    fireEvent.change(within(grid).getByLabelText("Sector Pivot filter field"), {
      target: { value: "" }
    });
    fireEvent.change(within(grid).getByLabelText("Sector Pivot custom formula name"), {
      target: { value: "returnScore" }
    });
    fireEvent.change(within(grid).getByLabelText("Sector Pivot custom formula label"), {
      target: { value: "Return score" }
    });
    fireEvent.change(within(grid).getByLabelText("Sector Pivot custom formula expression"), {
      target: { value: "round(percent({pnl}, abs(riskLimit), 0) + basisPoints({pnl}, abs(riskLimit), 0), 0)" }
    });
    await user.click(within(grid).getByRole("button", { name: "Preview Sector Pivot report-writer grid" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/templates/render",
      expect.objectContaining({
        method: "POST",
        body: expect.any(String)
      })
    ));
    const request = JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string);
    expect(request.grids[0]).toMatchObject({
      kind: "Contribution",
      sortBy: "contributionAbsPercent",
      metrics: [
        { name: "pnl", sourceField: "pnl", function: "Sum", label: "P&L" },
        { name: "marketValue", sourceField: "marketValue", function: "Sum", label: "Market value" }
      ],
      formulas: expect.arrayContaining([
        {
          name: "returnScore",
          expression: "round(percent({pnl}, abs(riskLimit), 0) + basisPoints({pnl}, abs(riskLimit), 0), 0)",
          label: "Return score"
        }
      ]),
      filters: null
    });
    expect(request.datasetRows.map((row: Record<string, string>) => row.pnl)).toEqual(["150", "-50", "0", "25"]);
    expect(request.datasetRows.map((row: Record<string, string>) => row.riskLimit)).toEqual(["10", "20", "30", "40"]);
    expect(request.datasetRows[0]).not.toHaveProperty("round");
    expect(request.datasetRows[0]).not.toHaveProperty("percent");
    expect(request.datasetRows[0]).not.toHaveProperty("basisPoints");
    expect(request.datasetRows[0]).not.toHaveProperty("abs");
    expect(request.datasetRows[0]).not.toHaveProperty("contributionPercent");
    expect(request.datasetRows[0]).not.toHaveProperty("contributionAbsPercent");

    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Preview report-writer grid status" })).toHaveTextContent(
        "Sector Pivot preview rendered."
      );
    });
    const preview = within(grid).getByLabelText("Sector Pivot live preview");
    expect(within(preview).getByText("Contribution %")).toBeInTheDocument();
    expect(within(preview).getByText("Abs Contribution %")).toBeInTheDocument();
    expect(within(preview).getAllByText("-25").length).toBeGreaterThanOrEqual(2);
    expect(within(preview).getAllByText("75").length).toBeGreaterThan(0);
    expect(within(preview).getByLabelText("Sector Pivot preview audit trace")).toHaveTextContent(
      "pnl=Sum(pnl), marketValue=Sum(marketValue)"
    );
  });

  it("previews no-code report-writer drafts against custom pasted dataset rows", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        templateId: { name: "investor-monthly-statement", version: 1 },
        renderedContent: "template:investor-monthly-statement@v1;grids=sector-pivot:1r",
        missingRequiredParameters: [],
        grids: [
          {
            gridId: "sector-pivot",
            title: "Sector Pivot",
            kind: "Pivot",
            columns: [
              { key: "sector", label: "sector", role: "dimension" },
              { key: "marketValue", label: "Market value", role: "metric" },
              { key: "pnl", label: "P&L", role: "metric" }
            ],
            rows: [
              {
                rowKey: "Private Credit",
                values: {
                  sector: "Private Credit",
                  marketValue: "425",
                  pnl: "37"
                }
              }
            ],
            warnings: ["Preview capped to 5 displayed rows."],
            lineage: {
              inputRowCount: 2,
              filteredInputRowCount: 1,
              outputRowCount: 1,
              sourceFields: ["marketValue", "pnl", "sector", "strategy"],
              metrics: [
                { name: "marketValue", sourceField: "marketValue", function: "Sum" },
                { name: "pnl", sourceField: "pnl", function: "Sum" }
              ],
              formulas: [],
              filters: [
                { field: "strategy", operator: "Equals", value: "Core", label: "strategy = Core" }
              ]
            }
          }
        ],
        warnings: ["Preview capped to 5 displayed rows."]
      })
    });
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    const writer = screen.getByRole("region", { name: "No-code report writer" });
    const grid = within(writer).getByRole("group", {
      name: "Investor Monthly Statement Sector Pivot Pivot report-writer grid"
    });

    await user.selectOptions(within(grid).getByLabelText("Sector Pivot preview dataset"), "customRows");
    fireEvent.change(within(grid).getByLabelText("Sector Pivot custom dataset rows"), {
      target: {
        value: JSON.stringify([
          { sector: "Private Credit", strategy: "Core", marketValue: 425, pnl: 37 },
          { sector: "Rates", strategy: "Hedge", marketValue: 100, pnl: -4 }
        ])
      }
    });
    await user.click(within(grid).getByRole("button", { name: "Preview Sector Pivot report-writer grid" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/templates/render",
      expect.objectContaining({
        method: "POST",
        body: expect.any(String)
      })
    ));
    const request = JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string);
    expect(request.parameters.previewDataset).toBe("customRows");
    expect(request.datasetRows).toEqual([
      { sector: "Private Credit", strategy: "Core", marketValue: "425", pnl: "37" },
      { sector: "Rates", strategy: "Hedge", marketValue: "100", pnl: "-4" }
    ]);
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Preview report-writer grid status" })).toHaveTextContent(
        "Dataset rows: 2"
      );
    });
    expect(within(grid).getByLabelText("Sector Pivot live preview")).toHaveTextContent("Private Credit");
  });

  it("previews no-code report-writer drafts against custom CSV dataset rows", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        templateId: { name: "investor-monthly-statement", version: 1 },
        renderedContent: "template:investor-monthly-statement@v1;grids=sector-pivot:1r",
        missingRequiredParameters: [],
        grids: [
          {
            gridId: "sector-pivot",
            title: "Sector Pivot",
            kind: "Pivot",
            columns: [
              { key: "sector", label: "sector", role: "dimension" },
              { key: "marketValue", label: "Market value", role: "metric" }
            ],
            rows: [
              {
                rowKey: "Private Credit, Direct Lending",
                values: {
                  sector: "Private Credit, Direct Lending",
                  marketValue: "425"
                }
              }
            ],
            warnings: []
          }
        ],
        warnings: []
      })
    });
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    const writer = screen.getByRole("region", { name: "No-code report writer" });
    const grid = within(writer).getByRole("group", {
      name: "Investor Monthly Statement Sector Pivot Pivot report-writer grid"
    });

    await user.selectOptions(within(grid).getByLabelText("Sector Pivot preview dataset"), "customRows");
    fireEvent.change(within(grid).getByLabelText("Sector Pivot custom dataset rows"), {
      target: {
        value: [
          "sector,strategy,marketValue,pnl",
          "\"Private Credit, Direct Lending\",Core,425,37",
          "Rates,Hedge,100,-4"
        ].join("\n")
      }
    });
    await user.click(within(grid).getByRole("button", { name: "Preview Sector Pivot report-writer grid" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/templates/render",
      expect.objectContaining({
        method: "POST",
        body: expect.any(String)
      })
    ));
    const request = JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string);
    expect(request.parameters.previewDataset).toBe("customRows");
    expect(request.datasetRows).toEqual([
      { sector: "Private Credit, Direct Lending", strategy: "Core", marketValue: "425", pnl: "37" },
      { sector: "Rates", strategy: "Hedge", marketValue: "100", pnl: "-4" }
    ]);
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Preview report-writer grid status" })).toHaveTextContent(
        "Dataset rows: 2"
      );
    });
    expect(within(grid).getByLabelText("Sector Pivot live preview")).toHaveTextContent(
      "Private Credit, Direct Lending"
    );
  });

  it("blocks custom report-writer preview when pasted dataset rows are invalid", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    const writer = screen.getByRole("region", { name: "No-code report writer" });
    const grid = within(writer).getByRole("group", {
      name: "Investor Monthly Statement Sector Pivot Pivot report-writer grid"
    });

    await user.selectOptions(within(grid).getByLabelText("Sector Pivot preview dataset"), "customRows");
    fireEvent.change(within(grid).getByLabelText("Sector Pivot custom dataset rows"), {
      target: { value: "[not-json" }
    });
    await user.click(within(grid).getByRole("button", { name: "Preview Sector Pivot report-writer grid" }));

    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.getByRole("status", { name: "Preview report-writer grid status" })).toHaveTextContent(
      "Sector Pivot custom dataset is invalid."
    );
    expect(screen.getByRole("status", { name: "Preview report-writer grid status" })).toHaveTextContent(
      "Custom JSON dataset must be an array of row objects."
    );
  });

  it("runs an approved report template on demand", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        run: {
          runId: "adhoc-investor-monthly-statement-20260607",
          templateId: "investor-monthly-statement",
          family: "InvestorStatement",
          status: "Draft",
          trigger: "AdHoc",
          asOfDate: "2026-06-07",
          attemptCount: 1,
          sectionCount: 2,
          lineageLinkedSections: 2,
          artifacts: ["adhoc-investor-monthly-statement-20260607.manifest.json"],
          auditActions: ["RunGenerated"],
          failureReason: null,
          drilldownLinks: [],
          nextActions: []
        }
      })
    });

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });
    await user.click(screen.getByRole("button", { name: "Run Investor Monthly Statement report on demand" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/runs",
      expect.objectContaining({
        method: "POST",
        body: expect.any(String)
      })
    ));
    const request = JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string);
    expect(request).toEqual(expect.objectContaining({
      templateId: "investor-monthly-statement",
      maxRetries: 0
    }));
    expect(screen.getByRole("status", { name: "Run report status" })).toHaveTextContent(
      "Investor Monthly Statement run created."
    );
    expect(screen.getByText("Run ID: adhoc-investor-monthly-statement-20260607")).toBeInTheDocument();
  });

  it("runs an approved custom report-writer template on demand", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        run: {
          runId: "adhoc-custom-exposure-pack-20260607",
          templateId: "custom-exposure-pack",
          family: "CustomReport",
          status: "Draft",
          trigger: "AdHoc",
          asOfDate: "2026-06-07",
          attemptCount: 1,
          sectionCount: 1,
          lineageLinkedSections: 1,
          artifacts: ["adhoc-custom-exposure-pack-20260607.manifest.json"],
          auditActions: ["RunGenerated"],
          failureReason: null,
          drilldownLinks: [],
          nextActions: []
        }
      })
    });
    const customApproved = withReportTemplate({
      templateId: "custom-exposure-pack",
      family: "CustomReport",
      name: "Custom Exposure Pack",
      version: "3",
      sections: ["summary"],
      lifecycleStatus: "Approved",
      isBuiltIn: false,
      isLatestApproved: true,
      approvalSummary: "Approved by controller.admin (APP-CUSTOM-RUN).",
      authoringRoute: "/api/fund-structure/reporting/templates/custom-exposure-pack/versions/3",
      accessMode: "Restricted",
      accessSummary: "Restricted to reporting-ops",
      isAccessible: true,
      createdBy: "reporting.ops",
      createdAt: "2026-06-06T10:00:00Z",
      updatedBy: "controller.admin",
      updatedAt: "2026-06-07T10:00:00Z",
      approvedBy: "controller.admin",
      approvedAt: "2026-06-07T10:00:00Z",
      decisionRationale: "Approved custom exposure report-writer pack.",
      approvalReference: "APP-CUSTOM-RUN",
      auditTrail: [
        {
          at: "2026-06-07T10:00:00Z",
          actor: "controller.admin",
          action: "approve",
          fromStatus: "InReview",
          toStatus: "Approved",
          note: "Approved custom exposure report-writer pack."
        }
      ],
      validationIssues: [],
      reportWriterGrids: [
        {
          gridId: "custom-exposure-pivot",
          title: "Custom Exposure Pivot",
          kind: "Pivot",
          dimensionCount: 1,
          metricCount: 1,
          formulaCount: 0,
          rowFields: ["strategy"],
          columnFields: [],
          metrics: [
            { name: "marketValue", sourceField: "marketValue", function: "Sum", label: "Market value" }
          ],
          formulas: [],
          topN: null,
          sortBy: "marketValue",
          sortDescending: true,
          filters: []
        }
      ]
    });

    renderWithRouter(<ReportingScreen data={customApproved} />, { initialEntries: ["/reporting/report-builder"] });
    await user.selectOptions(
      screen.getByLabelText("Custom Exposure Pack on-demand report-writer dataset source"),
      "topn-contribution-analytics"
    );
    await user.click(screen.getByRole("button", { name: "Run Custom Exposure Pack report on demand" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/runs",
      expect.objectContaining({
        method: "POST",
        body: expect.any(String)
      })
    ));
    const request = JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string);
    expect(request).toEqual(expect.objectContaining({
      templateId: "custom-exposure-pack",
      maxRetries: 0,
      datasetSourceId: "topn-contribution-analytics"
    }));
    expect(screen.getByRole("status", { name: "Run report status" })).toHaveTextContent(
      "Custom Exposure Pack run created."
    );
    expect(screen.getByText("Run ID: adhoc-custom-exposure-pack-20260607")).toBeInTheDocument();
  });

  it("runs a report from Exports with explicit on-demand controls", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        run: {
          runId: "exports-investor-monthly-statement-20260630",
          templateId: "investor-monthly-statement",
          family: "InvestorStatement",
          status: "Draft",
          trigger: "AdHoc",
          asOfDate: "2026-06-30",
          attemptCount: 2,
          sectionCount: 2,
          lineageLinkedSections: 2,
          artifacts: ["exports-investor-monthly-statement-20260630.manifest.json"],
          auditActions: ["RunGenerated"],
          failureReason: null,
          drilldownLinks: [],
          nextActions: [],
          reportWriterDatasetSourceId: "portfolio-reporting-cuts",
          reportWriterDatasetSourceLabel: "Portfolio reporting cuts",
          reportWriterDatasetRowCount: 2
        }
      })
    });

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/exports"] });

    expect(screen.getByRole("region", { name: "Exports report runner" })).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Exports report template"), "investor-monthly-statement:1.0.0");
    fireEvent.change(screen.getByLabelText("Exports report as-of date"), { target: { value: "2026-06-30" } });
    fireEvent.change(screen.getByLabelText("Exports report max retries"), { target: { value: "2" } });
    fireEvent.change(screen.getByLabelText("Exports report requested by"), { target: { value: "controller.user" } });
    await user.selectOptions(screen.getByLabelText("Exports report dataset source"), "portfolio-reporting-cuts");

    await user.click(screen.getByRole("button", { name: "Run Investor Monthly Statement from Exports" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/runs",
      expect.objectContaining({
        method: "POST",
        body: expect.any(String)
      })
    ));
    const request = JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string);
    expect(request).toEqual(expect.objectContaining({
      templateId: "investor-monthly-statement",
      asOfDate: "2026-06-30",
      maxRetries: 2,
      requestedBy: "controller.user",
      datasetSourceId: "portfolio-reporting-cuts"
    }));
    expect(screen.getByRole("status", { name: "Exports report run status" })).toHaveTextContent(
      "Investor Monthly Statement run created."
    );
    expect(screen.getByRole("status", { name: "Exports report run status" })).toHaveTextContent(
      "Dataset: Portfolio reporting cuts (2 rows)"
    );
    expect(screen.getByText("Run ID: exports-investor-monthly-statement-20260630")).toBeInTheDocument();
  });
  it("renders typed report run drilldown links and executable next action buttons", () => {
    const accountingWithRunLinks: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        ...accounting.reporting,
        recentRuns: [
          {
            runId: "investor-monthly-statement-20260501",
            templateId: "investor-monthly-statement",
            family: "InvestorStatement",
            status: "InReview",
            trigger: "Scheduled",
            asOfDate: "2026-05-01",
            attemptCount: 1,
            sectionCount: 2,
            lineageLinkedSections: 2,
            artifacts: ["manifest.json", "investor-monthly-statement.pdf"],
            generatedReportWriterGrids: [
              {
                gridId: "sector-pivot",
                title: "Sector Pivot",
                kind: "Pivot",
                artifact: "report-writer://investor-monthly-statement-20260501/grids/sector-pivot",
                dimensionCount: 2,
                metricCount: 2,
                formulaCount: 1,
                validationSummary: "4 passed / 4 checks",
                validationPassedCount: 4,
                validationWarningCount: 0,
                validationFailedCount: 0
              }
            ],
            reportWriterDatasetSourceId: "portfolio-reporting-cuts",
            reportWriterDatasetSourceLabel: "Portfolio reporting cuts",
            reportWriterDatasetRowCount: 2,
            auditActions: ["RunGenerated", "ApprovalTransition"],
            failureReason: null,
            drilldownLinks: [
              {
                id: "investor-monthly-statement-20260501:evidence",
                kind: "evidence",
                label: "Evidence bundle",
                href: "/api/fund-structure/report-packs/report-1/evidence-bundle",
                method: "GET",
                isBrowserNavigable: true,
                source: "ReportPackWorkflow"
              },
              {
                id: "investor-monthly-statement-20260501:audit",
                kind: "audit",
                label: "Approval audit trail",
                href: "reporting-run://investor-monthly-statement-20260501/audit",
                method: "GET",
                isBrowserNavigable: false,
                source: "ReportingOrchestration"
              }
            ],
            nextActions: [
              {
                id: "investor-monthly-statement-20260501:approve",
                kind: "approval",
                label: "Approve reporting run",
                href: "reporting-run://investor-monthly-statement-20260501/approval/approve",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false
              }
            ]
          }
        ]
      }
    };

    renderWithRouter(<ReportingScreen data={accountingWithRunLinks} />, { initialEntries: ["/reporting/run-status"] });

    expect(screen.getByLabelText("investor-monthly-statement-20260501 audit metadata")).toHaveTextContent(
      "Run IDinvestor-monthly-statement-20260501"
    );
    expect(screen.getByLabelText("investor-monthly-statement-20260501 audit metadata")).toHaveTextContent(
      "Templateinvestor-monthly-statement"
    );
    expect(screen.getByLabelText("investor-monthly-statement-20260501 audit metadata")).toHaveTextContent(
      "As of2026-05-01"
    );
    expect(screen.getByLabelText("investor-monthly-statement-20260501 audit metadata")).toHaveTextContent(
      "Attempts1 attempt"
    );
    expect(screen.getByLabelText("investor-monthly-statement-20260501 audit metadata")).toHaveTextContent(
      "Lineage2/2 linked"
    );
    expect(screen.getByLabelText("investor-monthly-statement-20260501 audit metadata")).toHaveTextContent(
      "Artifacts2 artifacts: manifest.json, investor-monthly-statement.pdf"
    );
    expect(screen.getByLabelText("investor-monthly-statement-20260501 audit metadata")).toHaveTextContent(
      "Dataset sourcePortfolio reporting cuts (2 rows)"
    );
    expect(screen.getByLabelText("investor-monthly-statement-20260501 audit metadata")).toHaveTextContent(
      "Generated grids1 generated grid with 1 formula; validation 4 passed / 4 checks: Sector Pivot (Pivot, 2d/2m/1f, validation 4 passed / 4 checks)"
    );
    const gridExports = screen.getByLabelText("investor-monthly-statement-20260501 report-writer grid exports");
    expect(within(gridExports).getByRole("link", { name: "JSON" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/runs/investor-monthly-statement-20260501/report-writer-grids/sector-pivot"
    );
    expect(within(gridExports).getByRole("link", { name: "CSV" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/runs/investor-monthly-statement-20260501/report-writer-grids/sector-pivot?format=csv"
    );
    expect(within(gridExports).getByRole("link", { name: "PDF" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/runs/investor-monthly-statement-20260501/report-writer-grids/sector-pivot?format=pdf"
    );
    expect(within(gridExports).getByRole("link", { name: "XLS" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/runs/investor-monthly-statement-20260501/report-writer-grids/sector-pivot?format=xls"
    );
    expect(within(gridExports).getByRole("link", { name: "XLSX" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/runs/investor-monthly-statement-20260501/report-writer-grids/sector-pivot?format=xlsx"
    );
    expect(screen.getByRole("link", {
      name: "Open Evidence bundle service reference"
    })).toHaveAttribute("href", "/api/fund-structure/report-packs/report-1/evidence-bundle");
    expect(screen.getByRole("group", {
      name: "Approval audit trail service reference retained for diagnostics"
    })).toHaveTextContent("Approval audit trail");
    expect(screen.getByRole("button", {
      name: "Approve reporting run service reference retained for diagnostics"
    })).toHaveTextContent("Approve reporting run");
  });

  it("saves operator-authored report schedules through the shared schedule endpoint", async () => {
    userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        scheduleId: "sched-investor-email",
        templateId: "investor-monthly-statement",
        cronExpression: "0 9 * * 1",
        nextAsOfDate: "2026-06-30",
        dueAtUtc: "2026-07-01T15:00:00Z",
        maxRetries: 3,
        requestedBy: "fund-controller",
        state: "Active",
        createdAtUtc: "2026-06-09T16:00:00Z",
        updatedAtUtc: "2026-06-09T16:05:00Z",
        lastRunAtUtc: null,
        lastRunId: null,
        runCount: 0,
        description: "Weekly client distribution.",
        brandingThemeId: "lpcustomtheme",
        brandingThemeOverride: {
          themeId: "lpcustomtheme",
          name: "LP Custom Theme",
          firmName: "Northstar Capital",
          primaryColor: "#123456",
          accentColor: "#AA5500",
          textColor: "#111111",
          backgroundColor: "#FAFAFA",
          logoUri: "https://example.test/northstar.png",
          footerText: "Northstar investor reporting",
          disclaimer: "Prepared for authorized allocator review.",
          isBuiltIn: false
        },
        deliveryTargets: [
          {
            distributionId: "compliance-archive",
            formats: ["Pdf", "Csv"],
            deliveryMode: "EmailLink",
            note: "Email link pack."
          }
        ]
      })
    });

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    fireEvent.change(screen.getByLabelText("Reporting schedule ID"), {
      target: { value: "sched-investor-email" }
    });
    fireEvent.change(screen.getByLabelText("Reporting schedule cron expression"), {
      target: { value: "0 9 * * 1" }
    });
    fireEvent.change(screen.getByLabelText("Reporting schedule next as-of date"), {
      target: { value: "2026-06-30" }
    });
    fireEvent.change(screen.getByLabelText("Reporting schedule due at UTC"), {
      target: { value: "2026-07-01T15:00:00Z" }
    });
    fireEvent.change(screen.getByLabelText("Reporting schedule max retries"), {
      target: { value: "3" }
    });
    fireEvent.change(screen.getByLabelText("Reporting schedule requested by"), {
      target: { value: "fund-controller" }
    });
    fireEvent.change(screen.getByLabelText("Reporting schedule dataset source"), {
      target: { value: "portfolio-reporting-cuts" }
    });
    fireEvent.change(screen.getByLabelText("Reporting schedule distribution"), {
      target: { value: "compliance-archive" }
    });
    fireEvent.change(screen.getByLabelText("Reporting schedule delivery mode"), {
      target: { value: "EmailLink" }
    });
    fireEvent.change(screen.getByLabelText("Reporting schedule description"), {
      target: { value: "Weekly client distribution." }
    });
    fireEvent.change(screen.getByLabelText("Reporting schedule delivery note"), {
      target: { value: "Email link pack." }
    });
    fireEvent.click(screen.getByLabelText("Reporting schedule Xlsx format"));
    fireEvent.click(screen.getByRole("button", { name: "Save reporting schedule" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/schedules",
      expect.objectContaining({
        method: "POST",
        body: expect.any(String)
      })
    ));
    expect(JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string)).toEqual({
      scheduleId: "sched-investor-email",
      templateId: "investor-monthly-statement",
      cronExpression: "0 9 * * 1",
      nextAsOfDate: "2026-06-30",
      dueAtUtc: "2026-07-01T15:00:00Z",
      maxRetries: 3,
      requestedBy: "fund-controller",
      description: "Weekly client distribution.",
      state: "Active",
      datasetSourceId: "portfolio-reporting-cuts",
      brandingThemeId: "lpcustomtheme",
      brandingThemeOverride: {
        themeId: "lpcustomtheme",
        name: "LP Custom Theme",
        firmName: "Northstar Capital",
        primaryColor: "#123456",
        accentColor: "#AA5500",
        textColor: "#111111",
        backgroundColor: "#FAFAFA",
        logoUri: "https://example.test/northstar.png",
        footerText: "Northstar investor reporting",
        disclaimer: "Prepared for authorized allocator review.",
        isBuiltIn: false
      },
      deliveryTargets: [
        {
          distributionId: "compliance-archive",
          deliveryMode: "EmailLink",
          formats: ["Pdf", "Csv"],
          note: "Email link pack."
        }
      ]
    });
    expect(screen.getByRole("status", { name: "Save reporting schedule status" })).toHaveTextContent(
      "Reporting schedule sched-investor-email saved."
    );
    expect(screen.getByRole("status", { name: "Save reporting schedule status" })).toHaveTextContent(
      "Delivery targets: compliance-archive via EmailLink"
    );
    expect(screen.getByRole("status", { name: "Save reporting schedule status" })).toHaveTextContent(
      "Branding: LP Custom Theme · Northstar Capital · lpcustomtheme"
    );
  });

  it("saves schedules with multiple staged delivery targets", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        scheduleId: "sched-multi-target",
        templateId: "investor-monthly-statement",
        cronExpression: "0 8 1 * *",
        nextAsOfDate: "2026-06-30",
        dueAtUtc: "2026-07-01T20:00:00Z",
        maxRetries: 1,
        requestedBy: "browser-workstation",
        state: "Active",
        createdAtUtc: "2026-06-09T16:00:00Z",
        updatedAtUtc: "2026-06-09T16:05:00Z",
        lastRunAtUtc: null,
        lastRunId: null,
        runCount: 0,
        description: "Scheduled governed report pack.",
        deliveryTargets: [
          {
            distributionId: "board-reporting-committee",
            formats: ["Pdf", "Xlsx", "Csv"],
            deliveryMode: "SecurePortal",
            note: "Board portal pack."
          },
          {
            distributionId: "compliance-archive",
            formats: ["Pdf", "Csv"],
            deliveryMode: "EmailLink",
            note: "Email link archive."
          }
        ]
      })
    });

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-builder"] });

    fireEvent.change(screen.getByLabelText("Reporting schedule ID"), {
      target: { value: "sched-multi-target" }
    });
    fireEvent.change(screen.getByLabelText("Reporting schedule delivery note"), {
      target: { value: "Board portal pack." }
    });
    await user.click(screen.getByRole("button", { name: "Stage reporting schedule delivery target" }));
    expect(screen.getByRole("list", { name: "Staged reporting schedule delivery targets" })).toHaveTextContent(
      "board-reporting-committee"
    );

    fireEvent.change(screen.getByLabelText("Reporting schedule distribution"), {
      target: { value: "compliance-archive" }
    });
    fireEvent.change(screen.getByLabelText("Reporting schedule delivery mode"), {
      target: { value: "EmailLink" }
    });
    fireEvent.change(screen.getByLabelText("Reporting schedule delivery note"), {
      target: { value: "Email link archive." }
    });
    fireEvent.click(screen.getByLabelText("Reporting schedule Xlsx format"));
    await user.click(screen.getByRole("button", { name: "Save reporting schedule" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/schedules",
      expect.objectContaining({
        method: "POST",
        body: expect.any(String)
      })
    ));
    expect(JSON.parse((fetchMock.mock.calls[0]?.[1] as RequestInit).body as string)).toEqual(expect.objectContaining({
      scheduleId: "sched-multi-target",
      deliveryTargets: [
        {
          distributionId: "board-reporting-committee",
          deliveryMode: "SecurePortal",
          formats: ["Pdf", "Xlsx", "Csv"],
          note: "Board portal pack."
        },
        {
          distributionId: "compliance-archive",
          deliveryMode: "EmailLink",
          formats: ["Pdf", "Csv"],
          note: "Email link archive."
        }
      ]
    }));
    expect(screen.getByRole("status", { name: "Save reporting schedule status" })).toHaveTextContent(
      "Delivery targets: board-reporting-committee via SecurePortal; compliance-archive via EmailLink"
    );
  });

  it("renders schedule controls and retained delivery history", () => {
    const scheduledAccounting: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        ...accounting.reporting,
        schedules: [
          {
            scheduleId: "sched-investor",
            templateId: "investor-monthly-statement",
            cronExpression: "0 8 1 * *",
            nextAsOfDate: "2026-06-01",
            dueAtUtc: "2026-06-01T08:00:00Z",
            maxRetries: 2,
            requestedBy: "fund-controller",
            state: "Active",
            createdAtUtc: "2026-05-01T08:00:00Z",
            updatedAtUtc: "2026-05-03T08:00:00Z",
            lastRunAtUtc: "2026-05-01T08:05:00Z",
            lastRunId: "investor-monthly-statement-20260501",
            runCount: 3,
            description: "Monthly investor statement close packet.",
            datasetSourceId: "portfolio-reporting-cuts",
            deliveryTargets: [
              {
                distributionId: "board-reporting-committee",
                formats: ["Pdf", "Xlsx", "Csv"],
                deliveryMode: "SecurePortal",
                note: "Board package."
              },
              {
                distributionId: "investor-relations",
                formats: ["Pdf", "Csv"],
                deliveryMode: "EmailLink",
                note: "Investor package."
              }
            ]
          }
        ],
        scheduleDeliveryPlans: [
          {
            planId: "schedule-delivery:sched-investor:board-reporting-committee",
            scheduleId: "sched-investor",
            templateId: "investor-monthly-statement",
            distributionId: "board-reporting-committee",
            recipient: "Board reporting committee",
            recipientRole: "Board",
            channel: "Board portal",
            deliveryMode: "SecurePortal",
            formats: ["Pdf", "Xlsx", "Csv"],
            isReady: true,
            readinessSummary: "Will deliver Pdf/Xlsx/Csv by SecurePortal to Board reporting committee when schedule 'sched-investor' runs.",
            route: "/reporting/report-packs?recipient=board",
            dueAtUtc: "2026-06-01T08:00:00Z",
            nextAsOfDate: "2026-06-01",
            owner: "fund-controller",
            note: "Board package.",
            lastDeliveryAttemptId: "attempt-1",
            lastDeliveryState: "Delivered",
            lastDeliveryAtUtc: "2026-05-03T20:15:00Z",
            lastDeliveryPackageRoute: "/reporting/report-packs/11111111-1111-1111-1111-111111111111/packages/pkg-board-1",
            lastDeliverySecureLink: "/portal/reporting/packages/pkg-board-1?token=abc123",
            lastDeliveryAccessLinks: [
              {
                kind: "secure-portal",
                label: "Secure portal package",
                href: "/portal/reporting/packages/pkg-board-1?token=abc123",
                requiresToken: true,
                expiresAtUtc: "2026-05-17T20:15:00Z",
                description: "Secure-portal package is available through the token-gated portal route /portal/reporting/packages/pkg-board-1?token=abc123."
              },
              {
                kind: "artifact-pdf",
                label: "Pdf download",
                href: "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries/22222222-2222-2222-2222-222222222222/artifacts/board-pack.pdf?token=abc123",
                requiresToken: true,
                expiresAtUtc: "2026-05-17T20:15:00Z",
                description: "board-pack.pdf retained as application/pdf."
              },
              {
                kind: "artifact-xls",
                label: "XLS compatibility download",
                href: "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries/22222222-2222-2222-2222-222222222222/artifacts/board-pack.xlsx?token=abc123&format=xls",
                requiresToken: true,
                expiresAtUtc: "2026-05-17T20:15:00Z",
                description: "board-pack.xlsx served through the XLS compatibility alias as the canonical XLSX workbook."
              }
            ],
            lastDeliveryAccessExpiresAtUtc: "2026-05-17T20:15:00Z",
            lastDeliveryAccessSummary: "Email-link package is available through the token-gated route /reporting/package/board.",
            lastDeliveryChannelSummary: "EmailLink delivery to Board reporting committee via Board portal.",
            lastDeliveryDownloadSummary: "3 artifact(s) retained as Pdf/Xlsx/Csv; manifest workstation/reporting/deliveries/board/manifest.json.",
            lastDeliveryNotificationCount: 1,
            lastDeliveryNotificationSummary: "1 notification retained: ReadyToSend via EmailLink.",
            lastDeliveryGeneratedReportWriterGridCount: 1,
            lastDeliveryRenderedReportWriterGridCount: 1,
            lastDeliveryReportWriterDatasetSummary: "Portfolio reporting cuts (2 rows)",
            lastDeliveryReportWriterGridSummary: "1 generated / 1 rendered report-writer grids: generated: Sector Pivot (Pivot, 1d/1m/1f, validation 2 passed / 2 checks); rendered: Sector Pivot (2r/2c).",
            versionStamp: "schedule-delivery-plan:sched-investor:board-reporting-committee:20260503080000:formats-3",
            lastDeliveryArtifactCount: 3,
            lastDeliveryIntegritySummary: "3 artifact(s) retained with SHA-256 checksums against publication evidence hash sha256:board-pack.",
            lastDeliveryEntitlementScope: "Restricted principals=Group:investor-relations",
            brandingThemeId: "allocator-quarterly",
            brandingTheme: {
              themeId: "allocator-quarterly",
              name: "Allocator Quarterly",
              firmName: "Northstar Capital",
              primaryColor: "#123456",
              accentColor: "#55AA99",
              textColor: "#111111",
              backgroundColor: "#FFFFFF",
              logoUri: "/branding/northstar.svg",
              footerText: "Northstar Capital confidential",
              disclaimer: "For approved recipients only.",
              isBuiltIn: false
            }
          },
          {
            planId: "schedule-delivery:sched-investor:investor-relations",
            scheduleId: "sched-investor",
            templateId: "investor-monthly-statement",
            distributionId: "investor-relations",
            recipient: "Investor relations",
            recipientRole: "Investor communications",
            channel: "Investor portal",
            deliveryMode: "EmailLink",
            formats: ["Pdf", "Csv"],
            isReady: true,
            readinessSummary: "Will deliver Pdf/Csv by EmailLink to Investor relations when schedule 'sched-investor' runs.",
            route: "/reporting/report-packs?recipient=investor-relations",
            dueAtUtc: "2026-06-01T08:00:00Z",
            nextAsOfDate: "2026-06-01",
            owner: "investor-relations",
            note: "Investor package.",
            lastDeliveryAttemptId: null,
            lastDeliveryState: null,
            lastDeliveryAtUtc: null,
            lastDeliveryPackageRoute: null,
            lastDeliverySecureLink: null,
            versionStamp: "schedule-delivery-plan:sched-investor:investor-relations:20260503080000:formats-2",
            lastDeliveryArtifactCount: 0,
            lastDeliveryIntegritySummary: null
          }
        ],
        deliveryAttempts: [
          {
            attemptId: "22222222-2222-2222-2222-222222222222",
            reportId: "11111111-1111-1111-1111-111111111111",
            distributionId: "board-reporting-committee",
            recipient: "Board reporting committee",
            recipientRole: "Board",
            channel: "Board portal",
            state: "Delivered",
            attemptedAtUtc: "2026-05-03T20:15:00Z",
            actor: "fund-controller",
            attemptNumber: 1,
            deliveryReference: "board-portal:packet-1",
            note: "Delivered after approval.",
            failureReason: null,
            evidenceLinks: [],
            package: {
              packageId: "pkg-board-1",
              reportId: "11111111-1111-1111-1111-111111111111",
              distributionId: "board-reporting-committee",
              deliveryMode: "SecurePortal",
              secureLink: "/portal/reporting/packages/pkg-board-1?token=abc123",
              portalRoute: "/reporting/report-packs/11111111-1111-1111-1111-111111111111/packages/pkg-board-1",
              formats: ["Pdf", "Xlsx", "Csv"],
              deliveryAccessSummary: "Secure-portal package is available through the token-gated portal route /portal/reporting/packages/pkg-board-1?token=abc123.",
              deliveryChannelSummary: "SecurePortal delivery to Board reporting committee via Board portal.",
              downloadSummary: "3 artifact(s) retained as Csv/Pdf/Xlsx; manifest workstation/reporting/deliveries/report-1/manifest.json.",
              accessExpiresAtUtc: "2026-05-17T20:15:00Z",
              accessLinks: [
                {
                  kind: "secure-portal",
                  label: "Secure portal package",
                  href: "/portal/reporting/packages/pkg-board-1?token=abc123",
                  requiresToken: true,
                  expiresAtUtc: "2026-05-17T20:15:00Z",
                  description: "Secure-portal package is available through the token-gated portal route /portal/reporting/packages/pkg-board-1?token=abc123."
                },
                {
                  kind: "operator-route",
                  label: "Operator package route",
                  href: "/reporting/report-packs/11111111-1111-1111-1111-111111111111/packages/pkg-board-1",
                  requiresToken: false,
                  description: "Internal operator route for inspecting the retained package metadata."
                },
                {
                  kind: "artifact-pdf",
                  label: "Pdf download",
                  href: "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries/22222222-2222-2222-2222-222222222222/artifacts/board-pack.pdf?token=abc123",
                  requiresToken: true,
                  expiresAtUtc: "2026-05-17T20:15:00Z",
                  description: "board-pack.pdf retained as application/pdf."
                },
                {
                  kind: "artifact-xls",
                  label: "XLS compatibility download",
                  href: "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries/22222222-2222-2222-2222-222222222222/artifacts/board-pack.xlsx?token=abc123&format=xls",
                  requiresToken: true,
                  expiresAtUtc: "2026-05-17T20:15:00Z",
                  description: "board-pack.xlsx served through the XLS compatibility alias as the canonical XLSX workbook."
                }
              ],
              notifications: [
                {
                  notificationId: "delivery-notification:pkg-board-1:secure-portal",
                  channel: "SecurePortal",
                  recipient: "Board reporting committee",
                  recipientRole: "Board",
                  deliveryMode: "SecurePortal",
                  subject: "Secure portal package published for Board reporting committee",
                  body: "A secure portal package is published for Board reporting committee via Board portal. Access expires at 2026-05-17T20:15:00Z.",
                  href: "/portal/reporting/packages/pkg-board-1?token=abc123",
                  requiresToken: true,
                  createdAtUtc: "2026-05-03T20:15:00Z",
                  expiresAtUtc: "2026-05-17T20:15:00Z",
                  status: "PortalPublished"
                }
              ],
              artifacts: [
                {
                  format: "Pdf",
                  artifactName: "board-pack.pdf",
                  contentType: "application/pdf",
                  retainedPath: "workstation/reporting/deliveries/report-1/board-reporting-committee/pkg-1/board-pack.pdf",
                  byteSize: 1024,
                  evidenceId: "delivery-artifact-board-pdf",
                  checksumSha256: "f".repeat(64),
                  versionStamp: "delivery-artifact:11111111111111111111111111111111:board-reporting-committee:1:pdf",
                  downloadRoute: "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries/22222222-2222-2222-2222-222222222222/artifacts/board-pack.pdf?token=abc123"
                }
              ],
              createdAtUtc: "2026-05-03T20:15:00Z",
              retainedManifestPath: "workstation/reporting/deliveries/report-1/manifest.json",
              publicationEvidenceHash: "sha256:board-pack",
              integritySummary: "3 artifact(s) retained with SHA-256 checksums against publication evidence hash sha256:board-pack.",
              reportingRunId: "investor-monthly-statement-20260501",
              reportingTemplateId: "investor-monthly-statement",
              reportingScheduleId: "sched-investor",
              reportingRunAsOfDate: "2026-05-01",
              reportingRunStatus: "Draft",
              reportingRunTrigger: "Scheduled",
              reportingRunAttemptCount: 1,
              reportingRunSectionCount: 4,
              reportingRunLineageLinkedSections: 4,
              reportWriterDatasetSourceId: "portfolio-reporting-cuts",
              reportWriterDatasetSourceLabel: "Portfolio reporting cuts",
              reportWriterDatasetRowCount: 2,
              sourceArtifacts: ["workstation/reporting/runs/investor-monthly-statement-20260501/manifest.json"],
              generatedReportWriterGrids: [
                {
                  gridId: "sector-pivot",
                  title: "Sector Pivot",
                  kind: "Pivot",
                  artifact: "report-writer://investor-monthly-statement-20260501/grids/sector-pivot",
                  dimensionCount: 1,
                  metricCount: 1,
                  formulaCount: 1,
                  validationSummary: "2 passed / 2 checks",
                  validationPassedCount: 2,
                  validationWarningCount: 0,
                  validationFailedCount: 0
                }
              ],
              renderedReportWriterGrids: [
                {
                  gridId: "sector-pivot",
                  title: "Sector Pivot",
                  kind: "Pivot",
                  columns: [
                    { key: "sector", label: "sector", role: "dimension" },
                    { key: "marketValue", label: "Market value", role: "metric" }
                  ],
                  rows: [
                    {
                      rowKey: "Credit",
                      values: {
                        sector: "Credit",
                        marketValue: "200"
                      }
                    },
                    {
                      rowKey: "Equity",
                      values: {
                        sector: "Equity",
                        marketValue: "50"
                      }
                    }
                  ],
                  warnings: [],
                  lineage: {
                    inputRowCount: 3,
                    outputRowCount: 2,
                    filteredInputRowCount: 3,
                    sourceFields: ["sector", "marketValue"],
                    metrics: [
                      {
                        name: "marketValue",
                        sourceField: "marketValue",
                        function: "Sum"
                      }
                    ],
                    formulas: []
                  },
                  dataDictionary: [
                    {
                      key: "sector",
                      label: "sector",
                      role: "dimension",
                      sourceField: "sector",
                      dataType: "string",
                      isGenerated: false,
                      description: "Source field sector rendered as dimension."
                    },
                    {
                      key: "marketValue",
                      label: "Market value",
                      role: "metric",
                      sourceField: "marketValue",
                      dataType: "decimal",
                      isGenerated: false,
                      description: "Source field marketValue rendered as metric."
                    },
                    {
                      key: "returnPct",
                      label: "Return %",
                      role: "formula",
                      sourceField: "",
                      dataType: "decimal",
                      isGenerated: true,
                      description: "Generated formula returnPct."
                    }
                  ],
                  validationChecks: [
                    {
                      checkId: "row-count",
                      status: "Passed",
                      detail: "Rendered grid has 2 retained rows."
                    },
                    {
                      checkId: "source-field-lineage",
                      status: "Passed",
                      detail: "2 source field(s) retained."
                    }
                  ]
                }
              ],
              lineProvenance: [
                {
                  lineKey: "trial-balance.cash",
                  sourceKind: "ledger",
                  sourceId: "ledger-entry-1",
                  evidenceId: "delivery-artifact:pdf",
                  runId: "run-1",
                  ledgerEntryId: "ledger-entry-1",
                  reconciliationCaseId: "case-1",
                  reportValue: "100.00",
                  sourceSessionId: "provider-session-1",
                  reconciliationRunId: "recon-run-1",
                  providerEventId: "provider-event-1",
                  securityMasterId: "security-1",
                  securityDefinitionId: "definition-1",
                  reconciliationOutcome: "matched",
                  approvalId: "approval-1",
                  financialRecordExplorerId: "ledger",
                  financialRecordHref: "/api/workstation/financial-record-explorers/ledger?lineKey=trial-balance.cash&sourceId=ledger-entry-1&evidenceId=delivery-artifact%3Apdf&runId=run-1"
                }
              ],
              deliveryEvidencePacket: {
                packetId: "reporting-run-delivery:pkg-board-1",
                packetKind: "ReportingRunDelivery",
                packageId: "pkg-board-1",
                reportId: "11111111-1111-1111-1111-111111111111",
                fundProfileId: "reporting-run",
                fundAccountId: "investor-monthly-statement",
                period: "2026-05-01",
                packageContents: [
                  "board-pack.pdf",
                  "source-artifact:report-writer://investor-monthly-statement-20260501/grids/sector-pivot"
                ],
                supportEvidenceIds: [
                  "delivery-artifact-board-pdf",
                  "reporting-run-source:report-writer://investor-monthly-statement-20260501/grids/sector-pivot"
                ],
                recipientList: [
                  {
                    distributionId: "board-reporting-committee",
                    recipient: "Board reporting committee",
                    recipientRole: "Board",
                    channel: "Board portal"
                  }
                ],
                entitlementScope: "CompanyWide",
                approvalChain: [
                  {
                    at: "2026-05-03T19:45:00Z",
                    actor: "controller",
                    action: "Approved delivery",
                    fromState: "PendingApproval",
                    toState: "Approved",
                    note: "Board portal package cleared."
                  }
                ],
                datasetVersion: "investor-monthly-statement-20260501",
                templateVersion: "investor-monthly-statement",
                deliveryChannel: "SecurePortal via Board portal",
                deliveredAtUtc: "2026-05-03T20:15:00Z",
                deliveryEvidence: [
                  {
                    evidenceId: "delivery-artifact-board-pdf",
                    label: "board-pack.pdf",
                    route: "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries/22222222-2222-2222-2222-222222222222/artifacts/board-pack.pdf?token=abc123",
                    source: "reporting-run-delivery",
                    capturedAtUtc: "2026-05-03T20:15:00Z"
                  }
                ],
                requestHistory: [
                  "reporting-run:investor-monthly-statement-20260501:Scheduled:Draft",
                  "schedule:sched-investor",
                  "delivery-request:board-reporting-committee"
                ],
                amendmentReason: "Allocator requested board watermark refresh.",
                restatementLineage: "prior-report:00000000-0000-0000-0000-000000000001",
                auditEventReferences: ["investor-monthly-statement-20260501:1:RunGenerated"],
                blockedDownstreamOutputs: ["warehouse-export:waiting-for-watermark-signoff"]
              },
              brandingTheme: {
                themeId: "allocator-quarterly",
                name: "Allocator Quarterly",
                firmName: "Northstar Capital",
                primaryColor: "#123456",
                accentColor: "#55AA99",
                textColor: "#111111",
                backgroundColor: "#FFFFFF",
                logoUri: "/branding/northstar.svg",
                footerText: "Northstar Capital confidential",
                disclaimer: "For approved recipients only.",
                isBuiltIn: false
              }
            }
          }
        ]
      }
    };

    renderWithRouter(<ReportingScreen data={scheduledAccounting} />, { initialEntries: ["/reporting/report-builder"] });

    expect(screen.getByRole("list", { name: "Reporting schedules" })).toBeInTheDocument();
    const scheduleRow = screen.getByLabelText("sched-investor investor-monthly-statement reporting schedule is Active");
    expect(scheduleRow).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Run now" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Pause" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Resume" })).toBeDisabled();
    expect(screen.getByText("board-reporting-committee via SecurePortal (Pdf/Xlsx/Csv); investor-relations via EmailLink (Pdf/Csv)")).toBeInTheDocument();
    expect(scheduleRow).toHaveTextContent("Portfolio reporting cuts (2)");
    expect(screen.getByRole("list", { name: "Reporting schedule delivery plans" })).toBeInTheDocument();
    const boardPlan = screen.getByRole("listitem", {
      name: "Board reporting committee SecurePortal scheduled delivery plan for sched-investor"
    });
    expect(boardPlan).toHaveTextContent("Will deliver Pdf/Xlsx/Csv by SecurePortal to Board reporting committee");
    expect(boardPlan).toHaveTextContent("Pdf, Xlsx, Csv");
    expect(boardPlan).toHaveTextContent(formatExpectedScheduleTimestamp("2026-05-17T20:15:00Z"));
    expect(boardPlan).toHaveTextContent("Email-link package is available through the token-gated route /reporting/package/board.");
    expect(boardPlan).toHaveTextContent("EmailLink delivery to Board reporting committee via Board portal.");
    expect(boardPlan).toHaveTextContent("3 artifact(s) retained as Pdf/Xlsx/Csv; manifest workstation/reporting/deliveries/board/manifest.json.");
    expect(boardPlan).toHaveTextContent("1 notification retained: ReadyToSend via EmailLink.");
    expect(boardPlan).toHaveTextContent("Portfolio reporting cuts (2 rows)");
    expect(boardPlan).toHaveTextContent("1 generated / 1 rendered report-writer grids");
    expect(boardPlan).toHaveTextContent("Sector Pivot (Pivot, 1d/1m/1f, validation 2 passed / 2 checks)");
    expect(boardPlan).toHaveTextContent("rendered: Sector Pivot (2r/2c)");
    expect(boardPlan).toHaveTextContent("3 artifacts with SHA-256");
    expect(boardPlan).toHaveTextContent("Restricted principals=Group:investor-relations");
    expect(boardPlan).toHaveTextContent("Allocator Quarterly · Northstar Capital · allocator-quarterly");
    expect(boardPlan).toHaveTextContent("3 artifact(s) retained with SHA-256 checksums against publication evidence hash sha256:board-pack.");
    expect(boardPlan).toHaveTextContent("Board package.");
    expect(boardPlan).toHaveTextContent("schedule-delivery-plan:sched-investor:board-reporting-committee:20260503080000:formats-3");
    expect(within(boardPlan).getByRole("link", { name: /Secure portal package token gated .*pkg-board-1/ })).toHaveAttribute(
      "href",
      "/portal/reporting/packages/pkg-board-1?token=abc123"
    );
    expect(within(boardPlan).getByRole("link", { name: /Pdf download token gated .*board-pack\.pdf/ })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries/22222222-2222-2222-2222-222222222222/artifacts/board-pack.pdf?token=abc123"
    );
    expect(within(boardPlan).getByRole("link", { name: /XLS compatibility download token gated .*format=xls/ })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries/22222222-2222-2222-2222-222222222222/artifacts/board-pack.xlsx?token=abc123&format=xls"
    );
    const investorPlan = screen.getByRole("listitem", {
      name: "Investor relations EmailLink scheduled delivery plan for sched-investor"
    });
    expect(investorPlan).toHaveTextContent("No retained delivery yet");
    expect(investorPlan).toHaveTextContent("No retained access expiry");
    expect(investorPlan).toHaveTextContent("No retained access summary");
    expect(investorPlan).toHaveTextContent("No retained channel summary");
    expect(investorPlan).toHaveTextContent("No retained download summary");
    expect(investorPlan).toHaveTextContent("No retained notification proof");
    expect(investorPlan).toHaveTextContent("No retained report-writer dataset");
    expect(investorPlan).toHaveTextContent("No retained report-writer grids");
    expect(investorPlan).toHaveTextContent("No retained artifact checksums");
    expect(investorPlan).toHaveTextContent("No retained delivery entitlement");

    expect(screen.getByRole("list", { name: "Report-pack delivery attempts" })).toBeInTheDocument();
    const boardAttempt = screen.getByLabelText("Board reporting committee delivery attempt Delivered");
    expect(boardAttempt).toHaveTextContent(
      "board-portal:packet-1"
    );
    expect(boardAttempt).toHaveTextContent(
      "SecurePortal package · Pdf, Xlsx, Csv"
    );
    expect(boardAttempt).toHaveTextContent("SecurePortal delivery to Board reporting committee via Board portal.");
    expect(boardAttempt).toHaveTextContent(
      "Secure-portal package is available through the token-gated portal route /portal/reporting/packages/pkg-board-1?token=abc123."
    );
    expect(boardAttempt).toHaveTextContent(
      "3 artifact(s) retained as Csv/Pdf/Xlsx; manifest workstation/reporting/deliveries/report-1/manifest.json."
    );
    expect(boardAttempt).toHaveTextContent("Access expires: 2026-05-17T20:15:00Z");
    expect(boardAttempt).toHaveTextContent(
      "Branding: Allocator Quarterly · Northstar Capital · allocator-quarterly"
    );
    expect(boardAttempt).toHaveTextContent(
      "/portal/reporting/packages/pkg-board-1?token=abc123"
    );
    expect(within(boardAttempt).getByRole("link", { name: /Operator package route internal route .*pkg-board-1/ })).toHaveAttribute(
      "href",
      "/reporting/report-packs/11111111-1111-1111-1111-111111111111/packages/pkg-board-1"
    );
    expect(within(boardAttempt).getByRole("link", { name: /Pdf download token gated .*board-pack\.pdf/ })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries/22222222-2222-2222-2222-222222222222/artifacts/board-pack.pdf?token=abc123"
    );
    expect(within(boardAttempt).getByRole("link", { name: /XLS compatibility download token gated .*format=xls/ })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries/22222222-2222-2222-2222-222222222222/artifacts/board-pack.xlsx?token=abc123&format=xls"
    );
    expect(within(boardAttempt).getByRole("link", { name: /Secure portal package token gated .*pkg-board-1/ })).toHaveAttribute(
      "href",
      "/portal/reporting/packages/pkg-board-1?token=abc123"
    );
    const notifications = within(boardAttempt).getByRole("list", { name: "Board reporting committee delivery notifications" });
    expect(notifications).toHaveTextContent("Secure portal package published for Board reporting committee");
    expect(notifications).toHaveTextContent("delivery-notification:pkg-board-1:secure-portal");
    expect(notifications).toHaveTextContent("PortalPublished");
    expect(notifications).toHaveTextContent("Board reporting committee · Board");
    expect(notifications).toHaveTextContent("2026-05-17T20:15:00Z");
    expect(within(notifications).getByRole("link", {
      name: /Secure portal package published for Board reporting committee token gated .*pkg-board-1/
    })).toHaveAttribute("href", "/portal/reporting/packages/pkg-board-1?token=abc123");
    expect(boardAttempt).toHaveTextContent("Reporting run: investor-monthly-statement-20260501");
    expect(boardAttempt).toHaveTextContent("Template: investor-monthly-statement");
    expect(boardAttempt).toHaveTextContent("Schedule: sched-investor");
    expect(boardAttempt).toHaveTextContent(
      "Run metadata: asOf=2026-05-01 · status=Draft · trigger=Scheduled · attempt=1 · sections=4 · lineage=4"
    );
    expect(boardAttempt).toHaveTextContent("Dataset source: Portfolio reporting cuts (2 rows)");
    expect(boardAttempt).toHaveTextContent("Source artifacts: workstation/reporting/runs/investor-monthly-statement-20260501/manifest.json");
    expect(boardAttempt).toHaveTextContent("Generated grids: Sector Pivot (Pivot, 1d/1m/1f, validation 2 passed / 2 checks)");
    expect(within(boardAttempt).getByRole("link", { name: "XLS" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/runs/investor-monthly-statement-20260501/report-writer-grids/sector-pivot?format=xls"
    );
    expect(boardAttempt).toHaveTextContent("Rendered grid rows: Sector Pivot (2r/2c)");
    expect(boardAttempt).toHaveTextContent("Sector Pivot dictionary: 3 fields, 1 generated");
    expect(boardAttempt).toHaveTextContent("Market value:marketValue:decimal");
    expect(boardAttempt).toHaveTextContent("Return %:generated:decimal:generated");
    expect(boardAttempt).toHaveTextContent("Sector Pivot validation: 2 passed / 2 checks");
    expect(boardAttempt).toHaveTextContent("source-field-lineage:Passed:2 source field(s) retained.");
    expect(boardAttempt).toHaveTextContent("Report-line provenance: 1 line");
    expect(boardAttempt).toHaveTextContent("trial-balance.cash · ledger · 100.00");
    expect(within(boardAttempt).getByRole("link", { name: "Open Financial Record Explorer for trial-balance.cash" })).toHaveAttribute(
      "href",
      "/api/workstation/financial-record-explorers/ledger?lineKey=trial-balance.cash&sourceId=ledger-entry-1&evidenceId=delivery-artifact%3Apdf&runId=run-1"
    );
    expect(boardAttempt).toHaveTextContent("Evidence packet: ReportingRunDelivery · investor-monthly-statement-20260501");
    expect(boardAttempt).toHaveTextContent("Template: investor-monthly-statement · Channel: SecurePortal via Board portal");
    expect(boardAttempt).toHaveTextContent("Contents: 2 · Support evidence: 2 · Delivery evidence: 1");
    expect(boardAttempt).toHaveTextContent(
      "Package contents: board-pack.pdf | source-artifact:report-writer://investor-monthly-statement-20260501/grids/sector-pivot"
    );
    expect(boardAttempt).toHaveTextContent(
      "Support evidence IDs: delivery-artifact-board-pdf | reporting-run-source:report-writer://investor-monthly-statement-20260501/grids/sector-pivot"
    );
    const packetEvidenceLinks = within(boardAttempt).getByRole("list", {
      name: "Board reporting committee delivery packet evidence links"
    });
    expect(packetEvidenceLinks).toHaveTextContent("delivery-artifact-board-pdf · board-pack.pdf · reporting-run-delivery · 2026-05-03T20:15:00Z");
    expect(within(packetEvidenceLinks).getByRole("link", { name: "Open delivery evidence delivery-artifact-board-pdf" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries/22222222-2222-2222-2222-222222222222/artifacts/board-pack.pdf?token=abc123"
    );
    expect(boardAttempt).toHaveTextContent(
      "Entitlement: CompanyWide · Fund: reporting-run · Account: investor-monthly-statement · Period: 2026-05-01"
    );
    const packetRecipients = within(boardAttempt).getByRole("list", {
      name: "Board reporting committee delivery packet recipients"
    });
    expect(packetRecipients).toHaveTextContent(
      "board-reporting-committee: Board reporting committee · Board · Board portal"
    );
    const packetApprovalChain = within(boardAttempt).getByRole("list", {
      name: "Board reporting committee delivery packet approval chain"
    });
    expect(packetApprovalChain).toHaveTextContent(
      "2026-05-03T19:45:00Z: controller Approved delivery PendingApproval to Approved - Board portal package cleared."
    );
    expect(boardAttempt).toHaveTextContent(
      "Request history: reporting-run:investor-monthly-statement-20260501:Scheduled:Draft | schedule:sched-investor | delivery-request:board-reporting-committee"
    );
    expect(boardAttempt).toHaveTextContent("Amendment: Allocator requested board watermark refresh.");
    expect(boardAttempt).toHaveTextContent(
      "Restatement lineage: prior-report:00000000-0000-0000-0000-000000000001"
    );
    expect(boardAttempt).toHaveTextContent(
      "Audit references: investor-monthly-statement-20260501:1:RunGenerated"
    );
    expect(boardAttempt).toHaveTextContent(
      "Blocked downstream outputs: warehouse-export:waiting-for-watermark-signoff"
    );
    expect(within(boardAttempt).getByRole("link", { name: "Open delivery evidence graph for Board reporting committee delivery attempt" })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=report-pack-delivery&subjectId=11111111-1111-1111-1111-111111111111%3A22222222-2222-2222-2222-222222222222"
    );
    const artifactIntegrity = within(boardAttempt).getByRole("list", {
      name: "Board reporting committee package artifact integrity"
    });
    expect(artifactIntegrity).toHaveTextContent("board-pack.pdf");
    expect(artifactIntegrity).toHaveTextContent("Pdf");
    expect(artifactIntegrity).toHaveTextContent(
      "workstation/reporting/deliveries/report-1/board-reporting-committee/pkg-1/board-pack.pdf"
    );
    expect(artifactIntegrity).toHaveTextContent("1,024 bytes");
    expect(artifactIntegrity).toHaveTextContent("delivery-artifact-board-pdf");
    expect(artifactIntegrity).toHaveTextContent("f".repeat(64));
    expect(artifactIntegrity).toHaveTextContent(
      "delivery-artifact:11111111111111111111111111111111:board-reporting-committee:1:pdf"
    );
    expect(within(artifactIntegrity).getByRole("link", { name: "Download board-pack.pdf" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries/22222222-2222-2222-2222-222222222222/artifacts/board-pack.pdf?token=abc123"
    );
  });

  it("pauses active reporting schedules through the shared schedule endpoint", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify(withReportingSchedule("Paused").reporting.schedules![0])
    });

    renderWithRouter(<ReportingScreen data={withReportingSchedule("Active")} />, { initialEntries: ["/reporting/report-builder"] });

    await user.click(screen.getByRole("button", { name: "Pause" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/schedules/sched-investor/pause",
      expect.objectContaining({ method: "POST" })
    ));
    const status = screen.getByRole("status", { name: "Pause sched-investor status" });
    expect(status).toHaveTextContent("Pause sched-investor completed.");
  });

  it("resumes paused reporting schedules and surfaces shared endpoint failures", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 409,
      statusText: "Conflict",
      text: async () => JSON.stringify({
        error: "Schedule cannot resume until distribution targets are ready.",
        details: ["board-reporting-committee is missing retained portal entitlement evidence."]
      })
    });

    renderWithRouter(<ReportingScreen data={withReportingSchedule("Paused")} />, { initialEntries: ["/reporting/report-builder"] });

    await user.click(screen.getByRole("button", { name: "Resume" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/schedules/sched-investor/resume",
      expect.objectContaining({ method: "POST" })
    ));
    const status = screen.getByRole("status", { name: "Resume sched-investor status" });
    expect(status).toHaveTextContent("Schedule cannot resume until distribution targets are ready.");
    expect(status).toHaveTextContent("board-reporting-committee is missing retained portal entitlement evidence.");
  });

  it("shows schedule delivery counts after running a configured reporting schedule", async () => {
    const user = userEvent.setup();
    const scheduledAccounting: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        ...accounting.reporting,
        schedules: [
          {
            scheduleId: "sched-investor",
            templateId: "investor-monthly-statement",
            cronExpression: "0 8 1 * *",
            nextAsOfDate: "2026-06-01",
            dueAtUtc: "2026-06-01T08:00:00Z",
            maxRetries: 2,
            requestedBy: "fund-controller",
            state: "Active",
            createdAtUtc: "2026-05-01T08:00:00Z",
            updatedAtUtc: "2026-05-03T08:00:00Z",
            lastRunAtUtc: "2026-05-01T08:05:00Z",
            lastRunId: "investor-monthly-statement-20260501",
            runCount: 3,
            description: "Monthly investor statement close packet.",
            deliveryTargets: [
              {
                distributionId: "board-reporting-committee",
                formats: ["Pdf", "Xlsx", "Csv"],
                deliveryMode: "SecurePortal",
                note: "Board package."
              }
            ]
          }
        ]
      }
    };
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        schedule: scheduledAccounting.reporting.schedules![0],
        run: {
          runId: "sched-investor-20260601",
          templateId: "investor-monthly-statement",
          family: "InvestorStatement",
          status: "Released",
          trigger: "Scheduled",
          asOfDate: "2026-06-01",
          attemptCount: 1,
          sectionCount: 4,
          lineageCount: 4,
          artifacts: ["sched-investor-20260601.manifest.json"],
          auditTrail: ["RunGenerated"],
          failureReason: null,
          drilldownLinks: [],
          nextActions: []
        },
        deliveryAttempts: [
          {
            attemptId: "attempt-1",
            reportId: "11111111-1111-1111-1111-111111111111",
            distributionId: "board-reporting-committee",
            recipient: "Board reporting committee",
            recipientRole: "Board",
            channel: "Board portal",
            state: "Delivered",
            attemptedAtUtc: "2026-06-01T08:10:00Z",
            actor: "fund-controller",
            attemptNumber: 1,
            deliveryReference: "schedule:investor-monthly-statement:11111111111111111111111111111111:board-reporting-committee",
            note: "Board package.",
            failureReason: null,
            evidenceLinks: [],
            package: null
          }
        ],
        deliveryWarnings: []
      })
    });

    renderWithRouter(<ReportingScreen data={scheduledAccounting} />, { initialEntries: ["/reporting/report-builder"] });
    await user.click(screen.getByRole("button", { name: "Run now" }));

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/schedules/sched-investor/run",
      expect.objectContaining({ method: "POST" })
    );
    const status = await screen.findByRole("status", { name: "Run sched-investor status" });
    expect(status).toHaveTextContent("Run sched-investor completed.");
    expect(status).toHaveTextContent("Run ID: sched-investor-20260601");
    expect(status).toHaveTextContent("Deliveries: 1");
  });

  it("records report-pack delivery failures from retained delivery history", async () => {
    const user = userEvent.setup();
    const deliveryAccounting: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        ...accounting.reporting,
        deliveryAttempts: [
          {
            attemptId: "attempt-board-1",
            reportId: "11111111-1111-1111-1111-111111111111",
            distributionId: "board-reporting-committee",
            recipient: "Board reporting committee",
            recipientRole: "Board",
            channel: "Board portal",
            state: "Delivered",
            attemptedAtUtc: "2026-06-01T08:10:00Z",
            actor: "fund-controller",
            attemptNumber: 1,
            deliveryReference: "board-portal:packet-1",
            note: "Delivered after approval.",
            failureReason: null,
            evidenceLinks: [],
            package: {
              packageId: "pkg-board-1",
              reportId: "11111111-1111-1111-1111-111111111111",
              distributionId: "board-reporting-committee",
              deliveryMode: "SecurePortal",
              secureLink: "/portal/reporting/packages/pkg-board-1?token=abc123",
              portalRoute: "/reporting/report-packs/11111111-1111-1111-1111-111111111111/packages/pkg-board-1",
              formats: ["Pdf", "Csv"],
              deliveryAccessSummary: "Secure-portal package is available through the token-gated portal route.",
              deliveryChannelSummary: "SecurePortal delivery to Board reporting committee via Board portal.",
              downloadSummary: "2 artifact(s) retained as Csv/Pdf.",
              accessExpiresAtUtc: "2026-06-15T08:10:00Z",
              accessLinks: [],
              artifacts: [],
              createdAtUtc: "2026-06-01T08:10:00Z",
              retainedManifestPath: "workstation/reporting/deliveries/report-1/manifest.json",
              publicationEvidenceHash: "sha256:board-pack"
            }
          }
        ]
      }
    };
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        attemptId: "attempt-board-failure-2",
        reportId: "11111111-1111-1111-1111-111111111111",
        distributionId: "board-reporting-committee",
        recipient: "Board reporting committee",
        recipientRole: "Board",
        channel: "Board portal",
        state: "Failed",
        attemptedAtUtc: "2026-06-01T08:20:00Z",
        actor: "browser-workstation",
        attemptNumber: 2,
        deliveryReference: "delivery-failure:attempt-board-1",
        note: "Delivery failure recorded from Reporting workspace for Board reporting committee.",
        failureReason: "Operator recorded delivery failure for Board reporting committee after attempt 1.",
        evidenceLinks: [],
        package: null
      })
    });

    renderWithRouter(<ReportingScreen data={deliveryAccounting} />, { initialEntries: ["/reporting/report-packs"] });

    const attempt = screen.getByLabelText("Board reporting committee delivery attempt Delivered");
    await user.click(within(attempt).getByRole("button", { name: "Record delivery failure" }));

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries/failures",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          distributionId: "board-reporting-committee",
          deliveryReference: "delivery-failure:attempt-board-1",
          note: "Delivery failure recorded from Reporting workspace for Board reporting committee.",
          failureReason: "Operator recorded delivery failure for Board reporting committee after attempt 1.",
          evidenceLinks: [
            {
              evidenceId: "attempt-board-1",
              label: "Board reporting committee delivery attempt 1",
              route: "/reporting/report-packs/11111111-1111-1111-1111-111111111111/packages/pkg-board-1",
              source: "report-pack-delivery",
              capturedAtUtc: "2026-06-01T08:10:00Z"
            }
          ]
        })
      })
    );
    const status = await screen.findByRole("status", { name: "Record Board reporting committee delivery failure status" });
    expect(status).toHaveTextContent("Board reporting committee delivery failure recorded.");
    expect(status).toHaveTextContent("Attempt ID: attempt-board-failure-2");
    expect(status).toHaveTextContent("State: Failed");
    expect(status).toHaveTextContent("Reason: Operator recorded delivery failure for Board reporting committee after attempt 1.");
  });

  it("runs a reporting schedule from a recipient delivery-plan row", async () => {
    const user = userEvent.setup();
    const scheduledAccounting: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        ...accounting.reporting,
        schedules: [
          {
            scheduleId: "sched-investor",
            templateId: "investor-monthly-statement",
            cronExpression: "0 8 1 * *",
            nextAsOfDate: "2026-06-01",
            dueAtUtc: "2026-06-01T08:00:00Z",
            maxRetries: 2,
            requestedBy: "fund-controller",
            state: "Active",
            createdAtUtc: "2026-05-01T08:00:00Z",
            updatedAtUtc: "2026-05-03T08:00:00Z",
            lastRunAtUtc: null,
            lastRunId: null,
            runCount: 0,
            description: "Monthly investor statement close packet.",
            deliveryTargets: [
              {
                distributionId: "board-reporting-committee",
                formats: ["Pdf", "Xlsx", "Csv"],
                deliveryMode: "SecurePortal",
                note: "Board package."
              }
            ]
          }
        ],
        scheduleDeliveryPlans: [
          {
            planId: "schedule-delivery:sched-investor:board-reporting-committee",
            scheduleId: "sched-investor",
            templateId: "investor-monthly-statement",
            distributionId: "board-reporting-committee",
            recipient: "Board reporting committee",
            recipientRole: "Board",
            channel: "Board portal",
            deliveryMode: "SecurePortal",
            formats: ["Pdf", "Xlsx", "Csv"],
            isReady: true,
            readinessSummary: "Will deliver Pdf/Xlsx/Csv by SecurePortal to Board reporting committee when schedule 'sched-investor' runs.",
            route: "/reporting/report-packs?recipient=board",
            dueAtUtc: "2026-06-01T08:00:00Z",
            nextAsOfDate: "2026-06-01",
            owner: "fund-controller",
            note: "Board package.",
            lastDeliveryAttemptId: null,
            lastDeliveryState: null,
            lastDeliveryAtUtc: null,
            lastDeliveryPackageRoute: null,
            lastDeliverySecureLink: null,
            versionStamp: "schedule-delivery-plan:sched-investor:board-reporting-committee:20260601080000:formats-3",
            lastDeliveryArtifactCount: 0,
            lastDeliveryIntegritySummary: null
          }
        ]
      }
    };
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        schedule: scheduledAccounting.reporting.schedules![0],
        run: {
          runId: "sched-investor-20260601",
          templateId: "investor-monthly-statement",
          family: "InvestorStatement",
          status: "Released",
          trigger: "Scheduled",
          asOfDate: "2026-06-01",
          attemptCount: 1,
          sectionCount: 4,
          lineageCount: 4,
          artifacts: ["sched-investor-20260601.manifest.json"],
          auditTrail: ["RunGenerated"],
          failureReason: null,
          drilldownLinks: [],
          nextActions: []
        },
        deliveryAttempts: [
          {
            attemptId: "attempt-1",
            reportId: "11111111-1111-1111-1111-111111111111",
            distributionId: "board-reporting-committee",
            recipient: "Board reporting committee",
            recipientRole: "Board",
            channel: "Board portal",
            state: "Delivered",
            attemptedAtUtc: "2026-06-01T08:10:00Z",
            actor: "fund-controller",
            attemptNumber: 1,
            deliveryReference: "schedule:investor-monthly-statement:11111111111111111111111111111111:board-reporting-committee",
            note: "Board package.",
            failureReason: null,
            evidenceLinks: [],
            package: null
          }
        ],
        deliveryWarnings: []
      })
    });

    renderWithRouter(<ReportingScreen data={scheduledAccounting} />, { initialEntries: ["/reporting/report-builder"] });

    const plan = screen.getByRole("listitem", {
      name: "Board reporting committee SecurePortal scheduled delivery plan for sched-investor"
    });
    await user.click(within(plan).getByRole("button", { name: "Run schedule for recipient" }));

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/schedules/sched-investor/run",
      expect.objectContaining({ method: "POST" })
    );
    const status = await screen.findByRole("status", { name: "Run sched-investor for Board reporting committee status" });
    expect(status).toHaveTextContent("Run sched-investor for Board reporting committee completed.");
    expect(status).toHaveTextContent("Run ID: sched-investor-20260601");
    expect(status).toHaveTextContent("Deliveries: 1");
    expect(status).toHaveTextContent("Recipient deliveries: 1");
    expect(status).toHaveTextContent("Target: Board reporting committee via SecurePortal");
  });

  it("runs due reporting schedules through the shared batch endpoint", async () => {
    const user = userEvent.setup();
    const scheduledAccounting: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        ...accounting.reporting,
        schedules: [
          {
            scheduleId: "sched-investor",
            templateId: "investor-monthly-statement",
            cronExpression: "0 8 1 * *",
            nextAsOfDate: "2026-06-01",
            dueAtUtc: "2026-06-01T08:00:00Z",
            maxRetries: 2,
            requestedBy: "fund-controller",
            state: "Active",
            createdAtUtc: "2026-05-01T08:00:00Z",
            updatedAtUtc: "2026-05-03T08:00:00Z",
            lastRunAtUtc: null,
            lastRunId: null,
            runCount: 0,
            description: "Monthly investor statement close packet.",
            deliveryTargets: [
              {
                distributionId: "board-reporting-committee",
                formats: ["Pdf", "Xlsx", "Csv"],
                deliveryMode: "SecurePortal",
                note: "Board package."
              }
            ]
          }
        ]
      }
    };
    fetchMock.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({
        evaluatedAtUtc: "2026-06-01T08:01:00Z",
        runs: [
          {
            schedule: scheduledAccounting.reporting.schedules![0],
            run: {
              runId: "sched-investor-20260601",
              templateId: "investor-monthly-statement",
              family: "InvestorStatement",
              status: "Released",
              trigger: "Scheduled",
              asOfDate: "2026-06-01",
              attemptCount: 1,
              sectionCount: 4,
              lineageCount: 4,
              artifacts: ["sched-investor-20260601.manifest.json"],
              auditTrail: ["RunGenerated"],
              failureReason: null,
              drilldownLinks: [],
              nextActions: []
            },
            deliveryAttempts: [
              {
                attemptId: "attempt-1",
                reportId: "11111111-1111-1111-1111-111111111111",
                distributionId: "board-reporting-committee",
                recipient: "Board reporting committee",
                recipientRole: "Board",
                channel: "Board portal",
                state: "Delivered",
                attemptedAtUtc: "2026-06-01T08:10:00Z",
                actor: "fund-controller",
                attemptNumber: 1,
                deliveryReference: "schedule:investor-monthly-statement:11111111111111111111111111111111:board-reporting-committee",
                note: "Board package.",
                failureReason: null,
                evidenceLinks: [],
                package: null
              }
            ],
            deliveryWarnings: ["Compliance archive has no package queued."]
          }
        ]
      })
    });

    renderWithRouter(<ReportingScreen data={scheduledAccounting} />, { initialEntries: ["/reporting/report-builder"] });
    await user.click(screen.getByRole("button", { name: "Run due reporting schedules" }));

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/fund-structure/reporting/schedules/run-due",
      expect.objectContaining({ method: "POST" })
    );
    const status = await screen.findByRole("status", { name: "Run due reporting schedules status" });
    expect(status).toHaveTextContent("Due schedule run completed for 1 schedule.");
    expect(status).toHaveTextContent("Evaluated: 2026-06-01T08:01:00Z");
    expect(status).toHaveTextContent("Deliveries: 1");
    expect(status).toHaveTextContent("Delivery warning: Compliance archive has no package queued.");
  });

  it("renders the VM-owned no-target state with warning token classes", () => {
    const missingTargets: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        ...accounting.reporting,
        reportPackDistributions: []
      }
    };

    renderWithRouter(<ReportingScreen data={missingTargets} />, { initialEntries: ["/reporting/report-packs"] });

    const emptyState = screen.getByRole("status", { name: "No report-pack recipients loaded" });
    expect(emptyState).toHaveTextContent(
      "No report-pack recipients loaded. Configure distribution records before approving this packet."
    );
    expect(emptyState).toHaveClass("border-warning/30", "bg-warning/10", "text-warning");
    expect(screen.queryByRole("list", { name: "Report-pack distribution recipients" })).not.toBeInTheDocument();
  });

  it("renders the dedicated report-pack approval workflow panel", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-packs"] });

    const task = screen.getByRole("region", { name: "Report-pack approval task" });
    expect(task).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Delivery Evidence" })).toBeInTheDocument();
    expect(screen.getByLabelText("Task mode Delivery Evidence")).toBeInTheDocument();
    expect(within(task).getByText("Report-pack approval")).toBeInTheDocument();
    expect(within(task).getByRole("list", { name: "Report-pack distribution recipients" })).toBeInTheDocument();
    expect(within(task).getByRole("list", { name: "Selected report-pack export actions" })).toBeInTheDocument();
    const excelPreviewLink = within(task).getByRole("link", { name: "Preview Excel export" });
    expect(excelPreviewLink).toHaveAttribute("href", "/api/export/preview?profile=excel");
    expect(excelPreviewLink).toHaveAttribute("aria-describedby", "reporting-action-excel-preview-report-pack-task-status");
    expect(within(task).getByText("Opens the current export preview in a new browser tab.")).toHaveAttribute(
      "id",
      "reporting-action-excel-preview-report-pack-task-status"
    );
    const excelProfile = within(task).getByRole("button", { name: "Select Excel for report-pack approval" });
    const auditProfile = within(task).getByRole("button", { name: "Select Audit Pack for report-pack approval" });
    expect(excelProfile).toHaveAttribute(
      "aria-controls",
      "report-pack-profile-selected-summary report-pack-profile-actions report-pack-profile-backend-links"
    );
    expect(excelProfile).toHaveAttribute("aria-expanded", "true");
    expect(excelProfile).toHaveAttribute("aria-describedby", "report-pack-profile-excel-description");
    expect(excelProfile).toHaveAttribute("tabindex", "0");
    expect(auditProfile).toHaveAttribute("aria-expanded", "false");
    expect(auditProfile).toHaveAttribute("tabindex", "-1");
    expect(within(task).getByLabelText("Excel export preview uses GET")).toHaveTextContent("GET");
    const gatedExcelExport = within(task).getByRole("button", {
      name: "Run Excel export analysis unavailable until required evidence is attached"
    });
    expect(gatedExcelExport).toBeDisabled();
    expect(gatedExcelExport).toHaveAttribute(
      "title",
      "Excel export requires loader automation evidence before running governed export analysis. Preview remains available."
    );
    expect(within(task).getByLabelText("Excel export analysis is gated by missing evidence")).toHaveTextContent("Gated");
    expect(within(task).getByRole("link", { name: "Open Report-pack catalog service reference" })).toHaveAttribute(
      "href",
      "/api/fund-structure/report-packs"
    );
    expect(within(task).queryByRole("link", { name: "POST /api/export/analysis for Excel export analysis" })).toBeNull();
    expect(within(task).getByRole("group", { name: "Excel export analysis service reference retained for diagnostics" })).toHaveTextContent(
      "Service reference"
    );

    excelProfile.focus();
    expect(excelProfile).toHaveFocus();
    await user.keyboard("{ArrowRight}");

    expect(auditProfile).toHaveFocus();
    expect(auditProfile).toHaveAttribute("aria-pressed", "true");
    expect(auditProfile).toHaveAttribute("aria-expanded", "true");
    expect(excelProfile).toHaveAttribute("tabindex", "-1");
    expect(auditProfile).toHaveAttribute("tabindex", "0");

    expect(within(task).getByRole("status", { name: "Selected report-pack profile" })).toHaveTextContent(
      "Audit Pack is selected for report-pack approval using Markdown output to Audit portal."
    );
    expect(within(task).getByRole("link", { name: "Open Audit Pack export preview service reference" })).toHaveAttribute(
      "href",
      "/api/export/preview?profile=audit-pack"
    );
    const auditPreviewLink = within(task).getByRole("link", { name: "Preview Audit Pack export" });
    expect(auditPreviewLink).toHaveAttribute("href", "/api/export/preview?profile=audit-pack");
    expect(auditPreviewLink).toHaveAttribute("aria-describedby", "reporting-action-audit-pack-preview-report-pack-task-status");
    expect(within(task).getByRole("group", { name: "Audit Pack export analysis service reference retained for diagnostics" })).toHaveTextContent(
      "Service reference"
    );
    expect(within(task).getByRole("button", { name: "Run Audit Pack export analysis" })).toBeEnabled();

    await user.click(within(task).getByRole("button", { name: "Run Audit Pack export analysis" }));

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/export/analysis",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ profileId: "audit-pack" })
      })
    );
  });

  it("renders report-pack restatement review from shared workflow records", () => {
    const restatedAccounting: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        ...accounting.reporting,
        workflowRecords: [
          {
            reportId: "report-restated-1",
            fundProfileId: "fund-alpha",
            fundAccountId: "account-alpha",
            period: "2026-05",
            templateId: { name: "monthly-board-pack", version: 1 },
            state: "Restated",
            version: 2,
            createdAt: "2026-05-27T10:00:00Z",
            createdBy: "reporter",
            updatedAt: "2026-05-28T12:00:00Z",
            auditTrail: [
              {
                at: "2026-05-28T12:00:00Z",
                actor: "approver",
                action: "restated",
                fromState: "Published",
                toState: "Restated",
                note: "pricing-correction"
              }
            ],
            restatement: {
              reasonCode: "pricing-correction",
              approver: "fund-controller",
              priorVersionReportId: "report-published-1",
              changedLines: [
                {
                  lineKey: "nav.total",
                  previousValue: "1250000",
                  currentValue: "1249500",
                  evidenceLinks: [
                    {
                      evidenceId: "pricing-evidence-1",
                      label: "Pricing override",
                      route: "/reporting/evidence?subject=pricing",
                      source: "pricing",
                      capturedAtUtc: "2026-05-28T11:59:00Z"
                    }
                  ]
                }
              ],
              evidenceLinks: null
            },
            lineProvenance: [
              {
                lineKey: "nav.total",
                sourceKind: "ledger",
                sourceId: "ledger-entry-nav",
                evidenceId: "publication-evidence-1",
                runId: "run-nav-1",
                ledgerEntryId: "ledger-entry-nav",
                reconciliationCaseId: "case-nav-1",
                reportValue: "1249500",
                sourceSessionId: "source-session-nav",
                reconciliationRunId: "recon-nav-1",
                providerEventId: "provider-nav-1",
                securityMasterId: "security-nav-1",
                securityDefinitionId: "definition-nav-1",
                reconciliationOutcome: "matched",
                approvalId: "approval-nav-1",
                financialRecordExplorerId: "ledger",
                financialRecordHref: "/api/workstation/financial-record-explorers/ledger?lineKey=nav.total&sourceId=ledger-entry-nav&evidenceId=publication-evidence-1&runId=run-nav-1"
              }
            ],
            publication: {
              manifestId: "manifest-restated-1",
              retainedManifestPath: "vault/report-packs/manifest-restated-1.json",
              evidenceHash: "sha256:restated123",
              signedOffBy: "reporting-ops",
              signedOffAt: "2026-05-28T15:20:00Z",
              signedOffRole: "Controller",
              signOffReason: "Approved NAV correction package.",
              signOffContext: "Authenticated actor 'reporting-ops' with role 'Controller' approved publication via HumanOperator.",
              actionOrigin: "HumanOperator",
              evidenceLinks: [
                {
                  evidenceId: "publication-evidence-1",
                  label: "Publication manifest",
                  route: "/reporting/manifests/manifest-restated-1",
                  source: "reporting",
                  capturedAtUtc: "2026-05-28T15:20:00Z"
                },
                {
                  evidenceId: "signoff-evidence-1",
                  label: "Sign-off approval packet",
                  route: "/reporting/evidence?subject=approval&approvalId=approval-nav-1",
                  source: "approval",
                  capturedAtUtc: "2026-05-28T15:19:00Z"
                }
              ]
            }
          }
        ]
      }
    };

    renderWithRouter(<ReportingScreen data={restatedAccounting} />, { initialEntries: ["/reporting/report-packs"] });

    const restatement = screen.getByRole("region", { name: "Report-pack restatement review" });
    expect(within(restatement).getByText("Restatement review")).toBeInTheDocument();
    expect(within(restatement).getByText("pricing-correction approved by fund-controller.")).toBeInTheDocument();
    expect(within(restatement).getByText("report-restated-1")).toBeInTheDocument();
    expect(within(restatement).getByRole("list", { name: "Restatement changed lines" })).toBeInTheDocument();
    expect(within(restatement).getByLabelText("nav.total changed from 1250000 to 1249500 with 1 evidence link")).toHaveTextContent(
      "1250000 -> 1249500"
    );
    expect(within(restatement).getByRole("link", { name: "Open evidence for nav.total" })).toHaveAttribute(
      "href",
      "/reporting/evidence?subject=pricing"
    );

    const publication = screen.getByRole("region", { name: "Report-pack publication review" });
    expect(within(publication).getByText("Publication review")).toBeInTheDocument();
    expect(within(publication).getByText("manifest-restated-1 signed off by reporting-ops at 2026-05-28T15:20:00Z.")).toBeInTheDocument();
    expect(within(publication).getByText("Controller")).toBeInTheDocument();
    expect(within(publication).getByText("Approved NAV correction package.")).toBeInTheDocument();
    expect(within(publication).getByText("Authenticated actor 'reporting-ops' with role 'Controller' approved publication via HumanOperator.")).toBeInTheDocument();
    expect(within(publication).getByText("sha256:restated123")).toBeInTheDocument();
    expect(within(publication).getByText("vault/report-packs/manifest-restated-1.json")).toBeInTheDocument();
    const publicationEvidence = within(publication).getByRole("list", { name: "Publication evidence links" });
    expect(within(publicationEvidence).getByLabelText("Publication manifest publication evidence from reporting")).toHaveTextContent(
      "publication-evidence-1 · reporting"
    );
    expect(within(publicationEvidence).getByRole("link", { name: "Open Publication manifest publication evidence" })).toHaveAttribute(
      "href",
      "/reporting/manifests/manifest-restated-1"
    );
    expect(within(publicationEvidence).getByLabelText("Sign-off approval packet publication evidence from approval")).toHaveTextContent(
      "signoff-evidence-1 · approval"
    );
    expect(within(publicationEvidence).getByRole("link", { name: "Open Sign-off approval packet publication evidence" })).toHaveAttribute(
      "href",
      "/reporting/evidence?subject=approval&approvalId=approval-nav-1"
    );
    expect(within(publication).getByRole("list", { name: "Report-line provenance drill-through" })).toBeInTheDocument();
    expect(within(publication).getByLabelText("nav.total provenance from ledger with Open Ledger Explorer")).toHaveTextContent(
      "ledger · ledger-entry-nav · ledger=ledger-entry-nav · recon=matched"
    );
    expect(within(publication).getByRole("link", { name: "Open Ledger Explorer for nav.total" })).toHaveAttribute(
      "href",
      "/api/workstation/financial-record-explorers/ledger?lineKey=nav.total&sourceId=ledger-entry-nav&evidenceId=publication-evidence-1&runId=run-nav-1"
    );
    expect(within(publication).getByRole("link", { name: "Open retained evidence for nav.total" })).toHaveAttribute(
      "href",
      "/reporting/manifests/manifest-restated-1"
    );
  });

  it("keeps report-pack task and inspector action descriptions uniquely identified", async () => {
    const user = userEvent.setup();
    const { container, unmount } = renderWithRouter(<ReportingScreen data={accounting} />, {
      initialEntries: ["/reporting/report-packs"]
    });

    const task = screen.getByRole("region", { name: "Report-pack approval task" });
    expect(within(task).getByRole("link", { name: "Preview Excel export" })).toHaveAttribute(
      "aria-describedby",
      "reporting-action-excel-preview-report-pack-task-status"
    );
    expect(screen.getByRole("link", { name: "Open Excel report-pack evidence" })).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Excel export actions" })).not.toBeInTheDocument();

    const ids = Array.from(container.querySelectorAll<HTMLElement>("[id]")).map((element) => element.id);
    const duplicateIds = ids.filter((id, index) => ids.indexOf(id) !== index);
    expect(duplicateIds).toEqual([]);
    expect(container.querySelector("#reporting-action-excel-preview-profile-detail-status")).not.toBeInTheDocument();
    expect(container.querySelector("#reporting-action-excel-preview-report-pack-task-status")).toBeInTheDocument();

    unmount();
    const exportsRender = renderWithRouter(<ReportingScreen data={accounting} />, {
      initialEntries: ["/reporting/exports"]
    });
    await user.click(screen.getByRole("row", { name: "Select Excel export profile" }));
    expect(screen.getByRole("list", { name: "Excel export actions" }))
      .toHaveTextContent("Opens the current export preview in a new browser tab.");

    const exportIds = Array.from(exportsRender.container.querySelectorAll<HTMLElement>("[id]")).map((element) => element.id);
    const duplicateExportIds = exportIds.filter((id, index) => exportIds.indexOf(id) !== index);
    expect(duplicateExportIds).toEqual([]);
    expect(exportsRender.container.querySelector("#reporting-action-excel-preview-profile-detail-status")).toBeInTheDocument();
    expect(exportsRender.container.querySelector("#reporting-action-excel-preview-report-pack-task-status")).not.toBeInTheDocument();
  });

  it("surfaces VM-owned running export feedback in the report-pack task", async () => {
    const user = userEvent.setup();
    let releaseFetch!: () => void;
    fetchMock.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          releaseFetch = () =>
            resolve({
              ok: true,
              text: async () => JSON.stringify({
                jobId: "export-running",
                success: true,
                status: "completed",
                profileId: "excel",
                symbols: [],
                filesGenerated: 1,
                totalRecords: 1,
                totalBytes: 1,
                outputDirectory: "exports",
                durationSeconds: 1,
                error: null,
                warnings: [],
                files: [],
                timestamp: "2026-05-01T00:00:00Z"
              })
            });
        })
    );

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-packs"] });

    const task = screen.getByRole("region", { name: "Report-pack approval task" });
    await user.click(within(task).getByRole("button", { name: "Select Audit Pack for report-pack approval" }));
    await user.click(within(task).getByRole("button", { name: "Run Audit Pack export analysis" }));

    const runningButton = within(task).getByRole("button", { name: "Run Audit Pack export analysis" });
    expect(runningButton).toBeDisabled();
    expect(runningButton).toHaveAttribute("aria-busy", "true");
    expect(runningButton).toHaveAttribute("title", "Audit Pack export is already running.");
    expect(runningButton).toHaveTextContent("Running export…");
    expect(within(task).getByLabelText("Audit Pack export is running")).toHaveTextContent("Running");
    expect(within(task).getByText("Audit Pack export is running. Wait for the result before starting another export.")).toBeInTheDocument();

    releaseFetch();
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Reporting export status" })).toHaveTextContent(
        "Audit Pack export completed — 1 file generated."
      );
    });
  });

  it("updates selected profile detail and profile-scoped actions", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/exports"] });

    const profileTable = screen.getByRole("treegrid", { name: "Export profiles" });
    const auditRow = within(profileTable).getByRole("row", { name: /select audit pack export profile/i });
    await user.click(auditRow);

    await waitFor(() => expect(within(profileTable).getByRole("row", { name: /select audit pack export profile/i })).toHaveAttribute("aria-selected", "true"));
    const selectedAuditRow = within(profileTable).getByRole("row", { name: /select audit pack export profile/i });
    expect(selectedAuditRow).toHaveAttribute("aria-controls", "reporting-profile-detail");
    expect(selectedAuditRow).toHaveAttribute("aria-expanded", "true");
    expect(within(profileTable).getByRole("row", { name: /select excel export profile/i })).toHaveAttribute("aria-expanded", "false");
    const inspector = screen.getByRole("region", { name: /audit pack selected/i });
    expect(inspector).toBeInTheDocument();
    expect(screen.getByRole("status", { name: /audit pack readiness/i })).toHaveTextContent(
      "Loader and dictionary evidence are ready"
    );
    expect(within(inspector).getByText("Profile ID")).toBeInTheDocument();
    expect(within(inspector).getByText("audit-pack")).toBeInTheDocument();

    const preview = screen.getByRole("link", { name: "Preview Audit Pack export" });
    const run = screen.getByRole("button", { name: "Run Audit Pack export analysis" });

    expect(preview).toHaveAttribute("href", "/api/export/preview?profile=audit-pack");
    expect(run).toBeEnabled();
  });

  it("renders report access audit scope and hidden object counts", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/governance"] });

    const audit = screen.getByRole("region", { name: "Reporting access audit" });
    expect(within(audit).getByText("User, group, and company scope")).toBeInTheDocument();
    expect(within(audit).getByText("CallerScoped")).toBeInTheDocument();
    expect(within(audit).getByText("4 hidden")).toBeInTheDocument();
    expect(within(audit).getByText("user:viewer.user · group:ops-control · company:company-alpha")).toBeInTheDocument();
    expect(within(audit).getByText("Templates")).toBeInTheDocument();
    expect(within(audit).getAllByText("1 hidden")).toHaveLength(4);
    expect(within(audit).getByText("Structured exports")).toBeInTheDocument();
    expect(within(audit).getByText("Some delivery attempts are hidden because their report pack or template is outside the caller's report access scope.")).toBeInTheDocument();
  });

  it("surfaces VM-owned running export feedback in the selected profile inspector", async () => {
    const user = userEvent.setup();
    let releaseFetch!: () => void;
    fetchMock.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          releaseFetch = () =>
            resolve({
              ok: true,
              text: async () => JSON.stringify({
                jobId: "export-running",
                success: true,
                status: "completed",
                profileId: "audit-pack",
                symbols: [],
                filesGenerated: 1,
                totalRecords: 1,
                totalBytes: 1,
                outputDirectory: "exports",
                durationSeconds: 1,
                error: null,
                warnings: [],
                files: [],
                timestamp: "2026-05-01T00:00:00Z"
              })
            });
        })
    );

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/exports"] });

    await user.click(screen.getByRole("row", { name: /select audit pack export profile/i }));
    const inspector = screen.getByRole("region", { name: /audit pack selected/i });
    await user.click(within(inspector).getByRole("button", { name: "Run Audit Pack export analysis" }));

    const runningButton = within(inspector).getByRole("button", { name: "Run Audit Pack export analysis" });
    expect(runningButton).toBeDisabled();
    expect(runningButton).toHaveAttribute("aria-busy", "true");
    expect(runningButton).toHaveAttribute("title", "Audit Pack export is already running.");
    expect(runningButton).toHaveTextContent("Running export…");
    expect(within(inspector).getByLabelText("Audit Pack export is running")).toHaveTextContent("Running");
    expect(within(inspector).getByText("Audit Pack export is running. Wait for the result before starting another export.")).toBeInTheDocument();

    releaseFetch();
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Reporting export status" })).toHaveTextContent(
        "Audit Pack export completed — 1 file generated."
      );
    });
  });

  it("posts selected profile when running export analysis", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/exports"] });

    await user.click(screen.getByRole("row", { name: /select audit pack export profile/i }));
    await user.click(screen.getByRole("button", { name: "Run Audit Pack export analysis" }));

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/export/analysis",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ profileId: "audit-pack" })
      })
    );
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Reporting export status" })).toHaveTextContent(
        "Audit Pack export completed — 2 files generated."
      );
    });
    const exportStatus = screen.getByRole("status", { name: "Reporting export status" });
    expect(within(exportStatus).getByText("Job ID")).toBeInTheDocument();
    expect(within(exportStatus).getByText("export-1")).toBeInTheDocument();
    expect(within(exportStatus).getByText("Output")).toBeInTheDocument();
    expect(within(exportStatus).getByText("exports")).toBeInTheDocument();
    expect(within(exportStatus).getByText("Records")).toBeInTheDocument();
    expect(within(exportStatus).getByText("20")).toBeInTheDocument();
    expect(within(exportStatus).getByText("SPY markdown")).toBeInTheDocument();
    expect(within(exportStatus).getByText(/audit\/export-1\.md/)).toBeInTheDocument();
  });

  it("renders structured backend validation detail for export failures", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 400,
      text: async () => JSON.stringify({
        title: "Validation failed",
        detail: "One or more validation errors occurred.",
        errors: {
          profileId: ["Profile is required."],
          approvalReason: ["Approval reason must cite packet evidence."]
        }
      })
    });

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/exports"] });

    await user.click(screen.getByRole("row", { name: /select audit pack export profile/i }));
    await user.click(screen.getByRole("button", { name: "Run Audit Pack export analysis" }));

    const exportStatus = await screen.findByRole("status", { name: "Reporting export status" });
    expect(within(exportStatus).getByText("Audit Pack export failed. One or more validation errors occurred.")).toBeInTheDocument();
    expect(within(exportStatus).getByText("Meridian service returned 400. Open diagnostics for technical details.")).toBeInTheDocument();
    expect(within(exportStatus).getByText("Validation failed")).toBeInTheDocument();
    expect(within(exportStatus).getByText("profileId: Profile is required.")).toBeInTheDocument();
    expect(within(exportStatus).getByText("approvalReason: Approval reason must cite packet evidence.")).toBeInTheDocument();
  });

  it("renders explicit empty states for missing reporting profiles and recipients", () => {
    const emptyAccounting: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        profileCount: 0,
        recommendedProfiles: [],
        profiles: [],
        reportPackDistributions: [],
        summary: "No reporting profiles configured."
      }
    };

    const { unmount } = renderWithRouter(<ReportingScreen data={emptyAccounting} />, { initialEntries: ["/reporting/exports"] });

    expect(screen.getByText(/no export profiles are configured/i)).toBeInTheDocument();
    unmount();
    renderWithRouter(<ReportingScreen data={emptyAccounting} />, { initialEntries: ["/reporting/report-packs"] });
    expect(screen.getByRole("status", { name: "No report-pack recipients loaded" })).toHaveTextContent(
      "No report-pack recipients loaded. Configure distribution records before approving this packet."
    );
  });

  it("renders report-pack approval task empty recipients and profiles as accessible status panels", () => {
    const emptyAccounting: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        profileCount: 0,
        recommendedProfiles: [],
        profiles: [],
        reportPackDistributions: [],
        summary: "No reporting profiles configured."
      }
    };

    renderWithRouter(<ReportingScreen data={emptyAccounting} />, { initialEntries: ["/reporting/report-packs"] });

    const task = screen.getByRole("region", { name: "Report-pack approval task" });
    expect(within(task).getByRole("status", { name: "No report-pack distribution recipients" })).toHaveTextContent(
      "No report-pack recipients loaded. Configure distribution records before approving this packet."
    );
    expect(within(task).getByRole("status", { name: "No report-pack export profiles" })).toHaveTextContent(
      "No export profiles are configured. Add a governed profile before report-pack approval."
    );
    expect(within(task).queryByRole("list", { name: "Report-pack export profiles" })).not.toBeInTheDocument();
  });
});
