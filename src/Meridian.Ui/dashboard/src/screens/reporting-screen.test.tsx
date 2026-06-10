import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ReportingScreen } from "@/screens/reporting-screen";
import { renderWithRouter } from "@/test/render";
import type { AccountingWorkspaceResponse, ReportingTemplateMetadata } from "@/types";

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
        lastSentAtUtc: null,
        route: "/reporting/evidence?subject=report-pack"
      }
    ],
    summary: "2 export profiles available.",
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
        route: "/api/fund-structure/reporting/structured-exports/regulatory-trial-balance?fundProfileId=fund-alpha&format=csv",
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
        route: "/api/fund-structure/reporting/structured-exports/investment-portfolio-cuts?fundProfileId=fund-alpha",
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
        route: "/api/fund-structure/reporting/structured-exports/warehouse-ledger-facts?fundProfileId=fund-alpha",
        dataDictionaryRoute: "/api/workstation/reporting",
        validationSummary: "Exports consolidated ledger snapshot facts for downstream warehouse loading. 6 row(s), 9 field(s), and 18 source record(s) are ready.",
        evidenceRoute: "/api/fund-structure/report-packs",
        versionStamp: "structured-export:20260411160000:rows-6:sources-18:schema-1",
        tags: ["warehouse", "ledger", "reconciliation"]
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
        route: "/api/fund-structure/reporting/structured-exports/investment-topn-contribution-analytics?fundProfileId=fund-alpha&format=csv",
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
        route: "/api/fund-structure/reporting/structured-exports/cross-fund-consolidation?fundProfileId=fund-alpha",
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
    expect(screen.getByLabelText("Route Reporting")).toBeInTheDocument();
  });

  it("renders route-aware loading copy for report packs", () => {
    renderWithRouter(<ReportingScreen data={null} />, { initialEntries: ["/reporting/report-packs"] });

    const loading = screen.getByRole("status", { name: "Loading Reporting" });
    expect(loading).toHaveAttribute("aria-busy", "true");
    expect(screen.getByLabelText("Route Report packs")).toBeInTheDocument();
  });

  it("renders private-capital fund event report readiness from the shared workbench projection", () => {
    renderWithRouter(<ReportingScreen data={withPrivateCapitalReportReview()} />, { initialEntries: ["/reporting"] });

    const readiness = screen.getByRole("region", { name: "Private-capital report readiness" });
    expect(within(readiness).getByText("Fund event ledger and capital account subledger")).toBeInTheDocument();
    expect(within(readiness).getByText("Source projection")).toBeInTheDocument();
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
    expect(within(readiness).getAllByRole("link", { name: /report-output%3Acapital-call-001/i }).length).toBeGreaterThan(0);
    expect(within(readiness).getByText("Report evidence review")).toBeInTheDocument();
    expect(within(readiness).getByText("Published output is missing retained report-line provenance.")).toBeInTheDocument();
    expect(within(readiness).getByRole("list", { name: "Private-capital report outputs" })).toHaveTextContent(
      "0 provenance lines"
    );
  });

  it("renders report-pack distribution recipients with accessible row labels", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    expect(screen.getByRole("list", { name: "Report-pack distribution recipients" })).toBeInTheDocument();
    expect(screen.getByLabelText(
      "Board reporting committee report-pack distribution: 1 report pack still needs approval before Board reporting committee delivery."
    )).toBeInTheDocument();
    expect(screen.getByLabelText(
      "Compliance archive report-pack distribution: No governed report pack is queued for Compliance archive."
    )).toBeInTheDocument();
  });

  it("renders portfolio reporting cuts from the shared reporting payload", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

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

  it("renders live portfolio views from the shared reporting payload", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    expect(screen.getByRole("region", { name: "Live portfolio views" })).toBeInTheDocument();
    const fundView = screen.getByRole("listitem", { name: "Consolidated fund Fund live portfolio view" });
    expect(within(fundView).getByText("LiveLinked")).toBeInTheDocument();
    expect(fundView).toHaveTextContent("3,250.00 USD cash with 150.00 pending settlement");
    expect(fundView).toHaveTextContent("Live-linked portfolio telemetry is current through 2026-04-11T15:58:00.0000000Z");
    expect(within(fundView).getByRole("list", { name: "Consolidated fund readiness blockers" })).toHaveTextContent(
      "Pending settlement of 150.00 USD requires cash-ladder evidence"
    );
    expect(within(fundView).getByRole("link", { name: "Open Consolidated fund live portfolio view" })).toHaveAttribute(
      "href",
      "/api/workstation/portfolio/summary?fundAccountId=all&strategyId=all&entity=portfolio"
    );

    const strategyView = screen.getByRole("listitem", { name: "Carry Strategy Strategy live portfolio view" });
    expect(strategyView).toHaveTextContent("Run cash-ladder evidence is available for Carry Strategy.");
    expect(within(strategyView).getByRole("list", { name: "Carry Strategy readiness blockers" })).toHaveTextContent(
      "Latest source snapshot is outside the 5-minute live-link window."
    );
    expect(within(strategyView).getByRole("link", { name: "Open Carry Strategy cash ladder" })).toHaveAttribute(
      "href",
      "/api/portfolio/run-governance-001/cash-flows"
    );
  });

  it("renders P&L slices from retained portfolio run timestamps", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

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
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

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
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

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

  it("renders structured exports from the shared reporting payload", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    expect(screen.getByRole("region", { name: "Structured reporting exports" })).toBeInTheDocument();
    const regulatoryRow = screen.getByRole("listitem", { name: "Regulatory trial balance structured export" });
    expect(within(regulatoryRow).getByText("Regulatory")).toBeInTheDocument();
    expect(within(regulatoryRow).getByText("Csv")).toBeInTheDocument();
    expect(within(regulatoryRow).getByText("12")).toBeInTheDocument();
    expect(within(regulatoryRow).getByText("exports/reporting/fund-alpha/20260411160000/regulatory-trial-balance.csv")).toBeInTheDocument();
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
      "/api/fund-structure/reporting/structured-exports/regulatory-trial-balance?fundProfileId=fund-alpha"
    );
    expect(within(regulatoryRow).getByRole("link", { name: "Download Regulatory trial balance structured export as CSV" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/structured-exports/regulatory-trial-balance?fundProfileId=fund-alpha&format=csv"
    );
    expect(within(regulatoryRow).getByRole("link", { name: "Download Regulatory trial balance structured export as XLSX" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/structured-exports/regulatory-trial-balance?fundProfileId=fund-alpha&format=xlsx"
    );

    const investmentRow = screen.getByRole("listitem", { name: "Investment portfolio cuts structured export" });
    expect(within(investmentRow).getByText("InvestmentDecision")).toBeInTheDocument();
    expect(within(investmentRow).getByText("structured-export:20260411160000:rows-2:sources-4:schema-1")).toBeInTheDocument();
    const warehouseRow = screen.getByRole("listitem", { name: "Warehouse ledger facts structured export" });
    expect(within(warehouseRow).getByText("DataWarehouse")).toBeInTheDocument();
    expect(within(warehouseRow).getByText("Json")).toBeInTheDocument();
    expect(warehouseRow).toHaveTextContent("Data warehouse and lakehouse ingestion");
    expect(warehouseRow).toHaveTextContent("Exports consolidated ledger snapshot facts for downstream warehouse loading");
    expect(within(warehouseRow).getByRole("group", { name: "Warehouse ledger facts export tags" })).toHaveTextContent(
      "warehouseledgerreconciliation"
    );
    expect(within(warehouseRow).getByRole("link", { name: "Download Warehouse ledger facts structured export as JSON" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/structured-exports/warehouse-ledger-facts?fundProfileId=fund-alpha"
    );
    expect(within(warehouseRow).getByRole("link", { name: "Download Warehouse ledger facts structured export as CSV" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/structured-exports/warehouse-ledger-facts?fundProfileId=fund-alpha&format=csv"
    );
    expect(within(warehouseRow).getByRole("link", { name: "Download Warehouse ledger facts structured export as XLSX" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/structured-exports/warehouse-ledger-facts?fundProfileId=fund-alpha&format=xlsx"
    );
    const analyticsExportRow = screen.getByRole("listitem", { name: "Top-N contribution analytics structured export" });
    expect(within(analyticsExportRow).getByText("Csv")).toBeInTheDocument();
    expect(within(analyticsExportRow).getByText("structured-export:20260411160000:rows-3:sources-4:schema-1")).toBeInTheDocument();
    expect(within(analyticsExportRow).getByRole("link", { name: "Download Top-N contribution analytics structured export as JSON" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/structured-exports/investment-topn-contribution-analytics?fundProfileId=fund-alpha"
    );
    expect(within(analyticsExportRow).getByRole("link", { name: "Download Top-N contribution analytics structured export as CSV" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/structured-exports/investment-topn-contribution-analytics?fundProfileId=fund-alpha&format=csv"
    );
    expect(within(analyticsExportRow).getByRole("link", { name: "Download Top-N contribution analytics structured export as XLSX" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/structured-exports/investment-topn-contribution-analytics?fundProfileId=fund-alpha&format=xlsx"
    );
    const crossFundExportRow = screen.getByRole("listitem", { name: "Cross-fund consolidation structured export" });
    expect(crossFundExportRow).toHaveTextContent("structured-export:20260411160000:rows-2:sources-5:schema-1");
    expect(within(crossFundExportRow).getByRole("link", { name: "Download Cross-fund consolidation structured export as JSON" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/structured-exports/cross-fund-consolidation?fundProfileId=fund-alpha"
    );
    expect(within(crossFundExportRow).getByRole("link", { name: "Download Cross-fund consolidation structured export as CSV" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/structured-exports/cross-fund-consolidation?fundProfileId=fund-alpha&format=csv"
    );
    expect(within(crossFundExportRow).getByRole("link", { name: "Download Cross-fund consolidation structured export as XLSX" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/structured-exports/cross-fund-consolidation?fundProfileId=fund-alpha&format=xlsx"
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
            validationSummary: "No normalized trial-balance rows are available for this as-of date."
          }
        ]
      }
    };

    renderWithRouter(<ReportingScreen data={blockedAccounting} />, { initialEntries: ["/reporting"] });

    const regulatoryRow = screen.getByRole("listitem", { name: "Regulatory trial balance structured export" });
    expect(within(regulatoryRow).getByText("Blocked")).toBeInTheDocument();
    expect(within(regulatoryRow).queryByRole("link", { name: "Download Regulatory trial balance structured export as CSV" })).not.toBeInTheDocument();
    expect(within(regulatoryRow).getByRole("button", {
      name: "Regulatory trial balance structured export CSV download blocked"
    })).toBeDisabled();
  });

  it("renders report branding themes from the shared reporting payload", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    expect(screen.getByRole("region", { name: "Report branding themes" })).toBeInTheDocument();
    const standardTheme = screen.getByRole("listitem", { name: "Meridian Standard report branding theme" });
    expect(within(standardTheme).getByText("Built-in")).toBeInTheDocument();
    expect(within(standardTheme).getByText("#195E63")).toBeInTheDocument();
    expect(standardTheme).toHaveTextContent("Generated by Meridian Reporting");
    expect(within(standardTheme).getByRole("button", { name: "Generate Meridian Standard branded report pack" })).toBeEnabled();

    const customTheme = screen.getByRole("listitem", { name: "LP Custom Theme report branding theme" });
    expect(within(customTheme).getByText("Custom")).toBeInTheDocument();
    expect(within(customTheme).getByText("Northstar Capital")).toBeInTheDocument();
    expect(within(customTheme).getByText("#AA5500")).toBeInTheDocument();
    expect(customTheme).toHaveTextContent("Prepared for authorized allocator review.");
    expect(within(customTheme).getByText("https://example.test/northstar.png")).toBeInTheDocument();
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

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });
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

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

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
      { initialEntries: ["/reporting"] }
    );

    expect(screen.getByRole("button", { name: "Generate Meridian Standard branded report pack" })).toBeDisabled();
  });

  it("renders template designer lifecycle controls for governed versions", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    expect(screen.getByText("Investor Monthly Statement")).toBeInTheDocument();
    const templateRow = screen.getByText("Investor Monthly Statement").closest("div");
    expect(templateRow).not.toBeNull();
    expect(within(templateRow!).getByText("Approved")).toBeInTheDocument();
    expect(within(templateRow!).getByText("Built-in")).toBeInTheDocument();
    const lineage = screen.getByRole("group", { name: "Investor Monthly Statement template audit and version lineage" });
    expect(lineage).toHaveTextContent("investor-monthly-statement@v1.0.0 no prior template");
    expect(lineage).toHaveTextContent("2 audit events");
    expect(lineage).toHaveTextContent("approve InReview->Approved by controller.admin");
    expect(lineage).toHaveTextContent("Latest approved");
    expect(lineage).toHaveTextContent("ref APP-TPL-INVESTOR-1");
    expect(lineage).toHaveTextContent("Controller approved investor statement baseline.");
    expect(lineage).toHaveTextContent("No validation issues");
    expect(screen.getByText("Built-in approved template for InvestorStatement.")).toBeInTheDocument();
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

    renderWithRouter(<ReportingScreen data={customDraft} />, { initialEntries: ["/reporting"] });
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

    renderWithRouter(<ReportingScreen data={customReview} />, { initialEntries: ["/reporting"] });
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
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    const writer = screen.getByRole("region", { name: "No-code report writer" });
    const grid = within(writer).getByRole("group", {
      name: "Investor Monthly Statement Sector Pivot Pivot report-writer grid"
    });
    expect(grid).toHaveTextContent("Pivot grid with 2 dimensions, 2 metrics, 1 formula, and 1 saved filter.");
    expect(grid).toHaveTextContent("Descending by pnl");
    expect(grid).toHaveTextContent("1 saved filter");
    expect(within(grid).getByLabelText("Sector Pivot saved filters")).toHaveTextContent("strategy = Core");
    expect(within(grid).getByLabelText("Sector Pivot preview dataset")).toHaveValue("portfolioPositions");
    expect(within(grid).getByLabelText("Sector Pivot preview dataset")).toHaveTextContent("Portfolio positions");
    expect(within(grid).getByLabelText("Sector Pivot preview dataset")).toHaveTextContent("Ledger facts");
    expect(within(grid).getByLabelText("Sector Pivot preview dataset")).toHaveTextContent("Cash ladder");
    expect(within(grid).getByLabelText("Sector Pivot filter field")).toHaveValue("strategy");
    expect(within(grid).getByLabelText("Sector Pivot filter operator")).toHaveValue("Equals");
    expect(within(grid).getByLabelText("Sector Pivot filter value")).toHaveValue("Core");
    expect(within(grid).getByRole("list", { name: "Sector Pivot Rows" })).toHaveTextContent("sector");
    expect(within(grid).getByRole("list", { name: "Sector Pivot Columns" })).toHaveTextContent("strategy");
    expect(within(grid).getByRole("list", { name: "Sector Pivot Metrics" })).toHaveTextContent("Market value");
    expect(within(grid).getByRole("list", { name: "Sector Pivot Formulas" })).toHaveTextContent("Return %");

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
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

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
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

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
            warnings: [],
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
            }
          }
        ],
        warnings: []
      })
    });
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

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
    expect(within(preview).getByText("Gain/loss ratio")).toBeInTheDocument();
    const auditTrace = within(preview).getByLabelText("Sector Pivot preview audit trace");
    expect(auditTrace).toHaveTextContent("4 input / 2 filtered / 2 output");
    expect(auditTrace).toHaveTextContent("marketValue, pnl, sector, strategy");
    expect(auditTrace).toHaveTextContent("marketValue=Sum(marketValue)");
    expect(auditTrace).toHaveTextContent("gainLossRatio=[marketValue, pnl]");
    expect(auditTrace).toHaveTextContent("strategy = Core");
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

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });
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

    renderWithRouter(<ReportingScreen data={customApproved} />, { initialEntries: ["/reporting"] });
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
      maxRetries: 0
    }));
    expect(screen.getByRole("status", { name: "Run report status" })).toHaveTextContent(
      "Custom Exposure Pack run created."
    );
    expect(screen.getByText("Run ID: adhoc-custom-exposure-pack-20260607")).toBeInTheDocument();
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
            attemptCount: 1,
            sectionCount: 2,
            lineageLinkedSections: 2,
            artifacts: ["manifest.json"],
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

    renderWithRouter(<ReportingScreen data={accountingWithRunLinks} />, { initialEntries: ["/reporting"] });

    expect(screen.getByRole("link", {
      name: "GET /api/fund-structure/report-packs/report-1/evidence-bundle for Evidence bundle"
    })).toHaveAttribute("href", "/api/fund-structure/report-packs/report-1/evidence-bundle");
    expect(screen.getByRole("group", {
      name: "Reference-only GET reporting-run://investor-monthly-statement-20260501/audit for Approval audit trail"
    })).toHaveTextContent("Approval audit trail");
    expect(screen.getByRole("button", {
      name: "POST reporting-run://investor-monthly-statement-20260501/approval/approve for Approve reporting run"
    })).toHaveTextContent("Approve reporting run");
  });

  it("saves operator-authored report schedules through the shared schedule endpoint", async () => {
    const user = userEvent.setup();
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

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

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
      "Delivery: compliance-archive via EmailLink"
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
            versionStamp: "schedule-delivery-plan:sched-investor:board-reporting-committee:20260503080000:formats-3",
            lastDeliveryArtifactCount: 3,
            lastDeliveryIntegritySummary: "3 artifact(s) retained with SHA-256 checksums against publication evidence hash sha256:board-pack."
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
            attemptId: "attempt-1",
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
              artifacts: [
                {
                  format: "Pdf",
                  artifactName: "board-pack.pdf",
                  contentType: "application/pdf",
                  retainedPath: "workstation/reporting/deliveries/report-1/board-pack.pdf",
                  byteSize: 128,
                  evidenceId: "delivery-artifact:pdf",
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
              sourceArtifacts: ["workstation/reporting/runs/investor-monthly-statement-20260501/manifest.json"],
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
                  "delivery-artifact:pdf",
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
                approvalChain: [],
                datasetVersion: "investor-monthly-statement-20260501",
                templateVersion: "investor-monthly-statement",
                deliveryChannel: "SecurePortal via Board portal",
                deliveredAtUtc: "2026-05-03T20:15:00Z",
                deliveryEvidence: [
                  {
                    evidenceId: "delivery-artifact:pdf",
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
                auditEventReferences: ["investor-monthly-statement-20260501:1:RunGenerated"],
                blockedDownstreamOutputs: []
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

    renderWithRouter(<ReportingScreen data={scheduledAccounting} />, { initialEntries: ["/reporting"] });

    expect(screen.getByRole("list", { name: "Reporting schedules" })).toBeInTheDocument();
    expect(screen.getByLabelText("sched-investor investor-monthly-statement reporting schedule is Active")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Run now" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Pause" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Resume" })).toBeDisabled();
    expect(screen.getByText("board-reporting-committee via SecurePortal (Pdf/Xlsx/Csv); investor-relations via EmailLink (Pdf/Csv)")).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Reporting schedule delivery plans" })).toBeInTheDocument();
    const boardPlan = screen.getByRole("listitem", {
      name: "Board reporting committee SecurePortal scheduled delivery plan for sched-investor"
    });
    expect(boardPlan).toHaveTextContent("Will deliver Pdf/Xlsx/Csv by SecurePortal to Board reporting committee");
    expect(boardPlan).toHaveTextContent("Pdf, Xlsx, Csv");
    expect(boardPlan).toHaveTextContent("3 artifacts with SHA-256");
    expect(boardPlan).toHaveTextContent("3 artifact(s) retained with SHA-256 checksums against publication evidence hash sha256:board-pack.");
    expect(boardPlan).toHaveTextContent("Board package.");
    expect(boardPlan).toHaveTextContent("schedule-delivery-plan:sched-investor:board-reporting-committee:20260503080000:formats-3");
    expect(within(boardPlan).getByRole("link", { name: "/portal/reporting/packages/pkg-board-1?token=abc123" })).toHaveAttribute(
      "href",
      "/portal/reporting/packages/pkg-board-1?token=abc123"
    );
    const investorPlan = screen.getByRole("listitem", {
      name: "Investor relations EmailLink scheduled delivery plan for sched-investor"
    });
    expect(investorPlan).toHaveTextContent("No retained delivery yet");
    expect(investorPlan).toHaveTextContent("No retained artifact checksums");

    expect(screen.getByRole("list", { name: "Report-pack delivery attempts" })).toBeInTheDocument();
    const boardAttempt = screen.getByLabelText("Board reporting committee delivery attempt Delivered");
    expect(boardAttempt).toHaveTextContent(
      "board-portal:packet-1"
    );
    expect(boardAttempt).toHaveTextContent(
      "SecurePortal package · Pdf, Xlsx, Csv"
    );
    expect(boardAttempt).toHaveTextContent(
      "Branding: Allocator Quarterly · Northstar Capital · allocator-quarterly"
    );
    expect(boardAttempt).toHaveTextContent(
      "/portal/reporting/packages/pkg-board-1?token=abc123"
    );
    expect(within(boardAttempt).getByRole("link", { name: "/portal/reporting/packages/pkg-board-1?token=abc123" })).toHaveAttribute(
      "href",
      "/portal/reporting/packages/pkg-board-1?token=abc123"
    );
    expect(boardAttempt).toHaveTextContent("Reporting run: investor-monthly-statement-20260501");
    expect(boardAttempt).toHaveTextContent("Template: investor-monthly-statement");
    expect(boardAttempt).toHaveTextContent("Schedule: sched-investor");
    expect(boardAttempt).toHaveTextContent("Source artifacts: workstation/reporting/runs/investor-monthly-statement-20260501/manifest.json");
    expect(boardAttempt).toHaveTextContent("Evidence packet: ReportingRunDelivery · investor-monthly-statement-20260501");
    expect(boardAttempt).toHaveTextContent("Template: investor-monthly-statement · Channel: SecurePortal via Board portal");
    expect(boardAttempt).toHaveTextContent("Contents: 2 · Support evidence: 2 · Delivery evidence: 1");
    expect(boardAttempt).toHaveTextContent(
      "Request history: reporting-run:investor-monthly-statement-20260501:Scheduled:Draft | schedule:sched-investor | delivery-request:board-reporting-committee"
    );
    expect(within(boardAttempt).getByRole("link", { name: "Download board-pack.pdf" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/packs/11111111-1111-1111-1111-111111111111/deliveries/22222222-2222-2222-2222-222222222222/artifacts/board-pack.pdf?token=abc123"
    );
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

    renderWithRouter(<ReportingScreen data={scheduledAccounting} />, { initialEntries: ["/reporting"] });
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

    renderWithRouter(<ReportingScreen data={scheduledAccounting} />, { initialEntries: ["/reporting"] });
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

    renderWithRouter(<ReportingScreen data={missingTargets} />, { initialEntries: ["/reporting"] });

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
    expect(within(task).getByText("Report-pack approval")).toBeInTheDocument();
    expect(within(task).getByRole("list", { name: "Report-pack distribution recipients" })).toBeInTheDocument();
    expect(within(task).getByRole("list", { name: "Selected report-pack export actions" })).toBeInTheDocument();
    const excelPreviewLink = within(task).getByRole("link", { name: "Preview Excel export payload" });
    expect(excelPreviewLink).toHaveAttribute("href", "/api/export/preview?profile=excel");
    expect(excelPreviewLink).toHaveAttribute("aria-describedby", "reporting-action-excel-preview-report-pack-task-status");
    expect(within(task).getByText("Opens the current export payload preview in a new browser tab.")).toHaveAttribute(
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
      "Excel export requires loader automation evidence before running a governed POST export. Preview remains available."
    );
    expect(within(task).getByLabelText("Excel export analysis is gated by missing evidence")).toHaveTextContent("Gated");
    expect(within(task).getByRole("link", { name: "GET /api/fund-structure/report-packs for Report-pack catalog" })).toHaveAttribute(
      "href",
      "/api/fund-structure/report-packs"
    );
    expect(within(task).queryByRole("link", { name: "POST /api/export/analysis for Excel export analysis" })).toBeNull();
    expect(within(task).getByRole("group", { name: "Reference-only POST /api/export/analysis for Excel export analysis" })).toHaveTextContent(
      "Reference"
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
    expect(within(task).getByRole("link", { name: "GET /api/export/preview?profile=audit-pack for Audit Pack export preview" })).toHaveAttribute(
      "href",
      "/api/export/preview?profile=audit-pack"
    );
    const auditPreviewLink = within(task).getByRole("link", { name: "Preview Audit Pack export payload" });
    expect(auditPreviewLink).toHaveAttribute("href", "/api/export/preview?profile=audit-pack");
    expect(auditPreviewLink).toHaveAttribute("aria-describedby", "reporting-action-audit-pack-preview-report-pack-task-status");
    expect(within(task).getByRole("group", { name: "Reference-only POST /api/export/analysis for Audit Pack export analysis" })).toHaveTextContent(
      "Reference"
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
            lineProvenance: [],
            publication: {
              manifestId: "manifest-restated-1",
              retainedManifestPath: "vault/report-packs/manifest-restated-1.json",
              evidenceHash: "sha256:restated123",
              signedOffBy: "reporting-ops",
              signedOffAt: "2026-05-28T15:20:00Z",
              evidenceLinks: [
                {
                  evidenceId: "publication-evidence-1",
                  label: "Publication manifest",
                  route: "/reporting/manifests/manifest-restated-1",
                  source: "reporting",
                  capturedAtUtc: "2026-05-28T15:20:00Z"
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
    expect(within(publication).getByText("sha256:restated123")).toBeInTheDocument();
    expect(within(publication).getByText("vault/report-packs/manifest-restated-1.json")).toBeInTheDocument();
  });

  it("keeps report-pack task and inspector action descriptions uniquely identified", () => {
    const { container } = renderWithRouter(<ReportingScreen data={accounting} />, {
      initialEntries: ["/reporting/report-packs"]
    });

    const task = screen.getByRole("region", { name: "Report-pack approval task" });
    expect(within(task).getByRole("link", { name: "Preview Excel export payload" })).toHaveAttribute(
      "aria-describedby",
      "reporting-action-excel-preview-report-pack-task-status"
    );
    expect(screen.getByRole("link", { name: "Open Excel report-pack evidence" })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Excel export actions" }))
      .toHaveTextContent("Opens the current export payload preview in a new browser tab.");

    const ids = Array.from(container.querySelectorAll<HTMLElement>("[id]")).map((element) => element.id);
    const duplicateIds = ids.filter((id, index) => ids.indexOf(id) !== index);
    expect(duplicateIds).toEqual([]);
    expect(container.querySelector("#reporting-action-excel-preview-profile-detail-status")).toBeInTheDocument();
    expect(container.querySelector("#reporting-action-excel-preview-report-pack-task-status")).toBeInTheDocument();
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
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    const profileTable = screen.getByRole("table", { name: "Export profiles" });
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

    const preview = screen.getByRole("link", { name: "Preview Audit Pack export payload" });
    const run = screen.getByRole("button", { name: "Run Audit Pack export analysis" });

    expect(preview).toHaveAttribute("href", "/api/export/preview?profile=audit-pack");
    expect(run).toBeEnabled();
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

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

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
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

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

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    await user.click(screen.getByRole("row", { name: /select audit pack export profile/i }));
    await user.click(screen.getByRole("button", { name: "Run Audit Pack export analysis" }));

    const exportStatus = await screen.findByRole("status", { name: "Reporting export status" });
    expect(within(exportStatus).getByText("Audit Pack export failed. One or more validation errors occurred.")).toBeInTheDocument();
    expect(within(exportStatus).getByText("Endpoint returned 400 for /api/export/analysis.")).toBeInTheDocument();
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

    renderWithRouter(<ReportingScreen data={emptyAccounting} />, { initialEntries: ["/reporting"] });

    expect(screen.getByText(/no export profiles are configured/i)).toBeInTheDocument();
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
