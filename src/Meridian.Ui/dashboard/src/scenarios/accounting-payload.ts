import type { AccountingWorkspaceResponse } from "../types";

// Single source for development previews, Vitest, and screenshot capture.
export const accountingPayload: AccountingWorkspaceResponse = {
  metrics: [
    { id: "breaks", label: "Open Breaks", value: "2", delta: "+1", tone: "warning" },
    { id: "drift", label: "Timing Drift", value: "1", delta: "0%", tone: "warning" },
    { id: "coverage", label: "Security Gaps", value: "0", delta: "0%", tone: "success" },
    { id: "audit", label: "Audit Ready", value: "4", delta: "+2", tone: "success" }
  ],
  reconciliationQueue: [
    {
      runId: "run-42",
      strategyName: "Paper Index Mean Reversion",
      mode: "paper",
      status: "Running",
      lastUpdated: "3m ago",
      breakCount: 2,
      openBreakCount: 1,
      reconciliationStatus: "BreaksOpen"
    },
    {
      runId: "run-57",
      strategyName: "Intraday Vol Carry",
      mode: "paper",
      status: "Paused",
      lastUpdated: "7m ago",
      breakCount: 1,
      openBreakCount: 0,
      reconciliationStatus: "Resolved"
    }
  ],
  breakQueue: [
    {
      breakId: "run-42:cash",
      runId: "run-42",
      strategyName: "Paper Index Mean Reversion",
      category: "AmountMismatch",
      status: "Open",
      variance: 500,
      reason: "Cash variance over tolerance.",
      assignedTo: null,
      detectedAt: "2026-01-01T00:00:00Z",
      lastUpdatedAt: "2026-01-01T00:00:00Z",
      reviewedBy: null,
      reviewedAt: null,
      resolvedBy: null,
      resolvedAt: null,
      resolutionNote: null
    }
  ],
  cashFlow: {
    totalCash: 120000,
    totalLedgerCash: 120500,
    netVariance: 500,
    totalFinancing: 1400,
    runsWithCashSignals: 4,
    runsWithCashVariance: 1,
    tone: "warning",
    summary: "Cash-flow coverage is available for 4 runs; 1 run needs variance review."
  },
  reporting: {
    profileCount: 4,
    fundProfileId: "default-fund",
    selectedFundProfileId: "default-fund",
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
      }
    ],
    pnlSlices: [
      {
        sliceId: "pnl:daily",
        period: "Daily",
        label: "Daily P&L",
        currency: "USD",
        startDate: "2026-05-03",
        endDate: "2026-05-03",
        realizedPnl: 3200,
        unrealizedPnl: 1200,
        totalPnl: 4400,
        priorTotalPnl: 2800,
        pnlChange: 1600,
        sourceCount: 2,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?pnlSlice=daily",
        readinessSummary: "2 source-backed run(s) in the daily window; compared with 1 prior-period run(s).",
        tags: ["pnl", "daily", "source-backed"],
        versionStamp: "pnl-slice:20260503200000:daily:sources-2:prior-1"
      },
      {
        sliceId: "pnl:weekly",
        period: "Weekly",
        label: "Weekly P&L",
        currency: "USD",
        startDate: "2026-04-27",
        endDate: "2026-05-03",
        realizedPnl: 5200,
        unrealizedPnl: 4300,
        totalPnl: 9500,
        priorTotalPnl: 6100,
        pnlChange: 3400,
        sourceCount: 5,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?pnlSlice=weekly",
        readinessSummary: "5 source-backed run(s) in the weekly window; compared with 3 prior-period run(s).",
        tags: ["pnl", "weekly", "source-backed"],
        versionStamp: "pnl-slice:20260503200000:weekly:sources-5:prior-3"
      },
      {
        sliceId: "pnl:monthly",
        period: "Monthly",
        label: "Monthly P&L",
        currency: "USD",
        startDate: "2026-05-01",
        endDate: "2026-05-03",
        realizedPnl: 5200,
        unrealizedPnl: 4300,
        totalPnl: 9500,
        priorTotalPnl: 7800,
        pnlChange: 1700,
        sourceCount: 5,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?pnlSlice=monthly",
        readinessSummary: "5 source-backed run(s) in the monthly window; compared with 8 prior-period run(s).",
        tags: ["pnl", "monthly", "source-backed"],
        versionStamp: "pnl-slice:20260503200000:monthly:sources-5:prior-8"
      },
      {
        sliceId: "pnl:yearly",
        period: "Yearly",
        label: "Yearly P&L",
        currency: "USD",
        startDate: "2026-01-01",
        endDate: "2026-05-03",
        realizedPnl: 5200,
        unrealizedPnl: 4300,
        totalPnl: 9500,
        priorTotalPnl: 0,
        pnlChange: 9500,
        sourceCount: 5,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?pnlSlice=yearly",
        readinessSummary: "5 source-backed run(s) in the yearly window; no prior-period source run is available for comparison.",
        tags: ["pnl", "yearly", "source-backed"],
        versionStamp: "pnl-slice:20260503200000:yearly:sources-5:prior-0"
      }
    ],
    analyticsRows: [
      {
        analyticsId: "analytics:topwinner:security:bdc-a",
        kind: "TopWinner",
        scope: "Security",
        rank: 1,
        label: "BDC Alpha",
        symbol: "BDC-A",
        classification: "Equity",
        currency: "USD",
        realizedPnl: 2800,
        unrealizedPnl: 1900,
        totalPnl: 4700,
        contributionPercent: 49.4737,
        heatMapIntensity: 49.4737,
        sourceCount: 2,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?analyticsId=analytics%3Atopwinner%3Asecurity%3Abdc-a",
        readinessSummary: "Top-N winner from 2 source-backed run(s); contributes 49.47% of portfolio P&L.",
        tags: ["analytics", "topwinner", "security", "equity"],
        versionStamp: "analytics:20260503200000:topwinner:security:sources-2"
      },
      {
        analyticsId: "analytics:toplaggard:security:hedge-overlay",
        kind: "TopLaggard",
        scope: "Security",
        rank: 1,
        label: "Hedge Overlay",
        symbol: "HEDGE",
        classification: "Derivative",
        currency: "USD",
        realizedPnl: -900,
        unrealizedPnl: -350,
        totalPnl: -1250,
        contributionPercent: -13.1579,
        heatMapIntensity: 13.1579,
        sourceCount: 1,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?analyticsId=analytics%3Atoplaggard%3Asecurity%3Ahedge-overlay",
        readinessSummary: "Top-N laggard from 1 source-backed run(s); contributes -13.16% of portfolio P&L.",
        tags: ["analytics", "toplaggard", "security", "derivative"],
        versionStamp: "analytics:20260503200000:toplaggard:security:sources-1"
      },
      {
        analyticsId: "analytics:contribution:strategy:paper-income",
        kind: "Contribution",
        scope: "Strategy",
        rank: 1,
        label: "Paper Income",
        symbol: null,
        classification: "Strategy",
        currency: "USD",
        realizedPnl: 5200,
        unrealizedPnl: 4300,
        totalPnl: 9500,
        contributionPercent: 100,
        heatMapIntensity: 100,
        sourceCount: 5,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?analyticsId=analytics%3Acontribution%3Astrategy%3Apaper-income",
        readinessSummary: "5 source-backed run(s); contribution is 100% of portfolio P&L with 100% heat-map intensity.",
        tags: ["analytics", "contribution", "strategy", "strategy"],
        versionStamp: "analytics:20260503200000:contribution:strategy:sources-5"
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
        grossExposure: 425000,
        netExposure: 398000,
        longMarketValue: 425000,
        shortMarketValue: -27000,
        totalCash: 120000,
        pendingSettlement: 1400,
        totalPnl: 9500,
        shadowNav: 518000,
        shadowNavVariance: 120000,
        sourceCount: 5,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?consolidationId=cross-fund%3Acompany",
        readinessSummary: "5 source record(s) across 2 fund(s), 1 entity row(s), 3 account(s), and 2 run(s).",
        tags: ["company", "cross-fund", "consolidated"],
        versionStamp: "cross-fund:20260503200000:funds-2:entities-1:sources-5"
      },
      {
        consolidationId: "cross-fund:fund:demo-fund",
        label: "Demo Income Fund",
        scope: "Fund",
        currency: "USD",
        isReady: true,
        fundCount: 1,
        entityCount: 1,
        accountCount: 2,
        runCount: 1,
        grossExposure: 310000,
        netExposure: 301000,
        longMarketValue: 310000,
        shortMarketValue: -9000,
        totalCash: 82500,
        pendingSettlement: 900,
        totalPnl: 6200,
        shadowNav: 383500,
        shadowNavVariance: 82500,
        sourceCount: 3,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?consolidationId=cross-fund%3Afund%3Ademo-fund",
        readinessSummary: "3 source record(s) across 1 fund(s), 1 entity row(s), 2 account(s), and 1 run(s).",
        tags: ["fund", "cross-fund", "consolidated"],
        versionStamp: "cross-fund:20260503200000:funds-1:entities-1:sources-3"
      }
    ],
    structuredExports: [
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
        sourceCount: 8,
        currency: "USD",
        asOf: "2026-05-03T20:00:00Z",
        isReady: true,
        retainedPath: "exports/reporting/default-fund/20260503200000/investment-topn-contribution-analytics.csv",
        route: "/api/workstation/reporting/structured-exports/investment-topn-contribution-analytics",
        dataDictionaryRoute: "/api/workstation/reporting",
        validationSummary: "Exports source-backed Top-N winners, laggards, and contribution rows with P&L percentages and heat-map intensities. 3 row(s), 18 field(s), and 8 source record(s) are ready.",
        evidenceRoute: "/api/fund-structure/report-packs",
        versionStamp: "structured-export:20260503200000:rows-3:sources-8:schema-1",
        tags: ["investment", "top-n", "contribution", "analytics"]
      }
    ],
    schedules: [
      {
        scheduleId: "sched-monthly-board-pack",
        templateId: "monthly-board-pack",
        cronExpression: "0 8 1 * *",
        nextAsOfDate: "2026-06-01",
        dueAtUtc: "2026-06-01T08:00:00Z",
        maxRetries: 2,
        requestedBy: "fund-controller",
        state: "Active",
        createdAtUtc: "2026-05-01T08:00:00Z",
        updatedAtUtc: "2026-05-28T12:00:00Z",
        lastRunAtUtc: "2026-05-01T08:05:00Z",
        lastRunId: "sched-monthly-board-pack-20260501",
        runCount: 1,
        description: "Monthly board packet with portal and email-link delivery.",
        deliveryTargets: [
          {
            distributionId: "board-reporting-committee",
            formats: ["Pdf", "Xlsx", "Csv"],
            deliveryMode: "SecurePortal",
            note: "Board portal delivery."
          },
          {
            distributionId: "investor-relations",
            formats: ["Pdf", "Csv"],
            deliveryMode: "EmailLink",
            note: "Investor email-link delivery."
          }
        ]
      }
    ],
    scheduleDeliveryPlans: [
      {
        planId: "schedule-delivery:sched-monthly-board-pack:board-reporting-committee",
        scheduleId: "sched-monthly-board-pack",
        templateId: "monthly-board-pack",
        distributionId: "board-reporting-committee",
        recipient: "Board reporting committee",
        recipientRole: "Board",
        channel: "Board portal",
        deliveryMode: "SecurePortal",
        formats: ["Pdf", "Xlsx", "Csv"],
        isReady: true,
        readinessSummary: "Will deliver Pdf/Xlsx/Csv by SecurePortal to Board reporting committee when schedule 'sched-monthly-board-pack' runs.",
        route: "/reporting/report-packs?recipient=board",
        dueAtUtc: "2026-06-01T08:00:00Z",
        nextAsOfDate: "2026-06-01",
        owner: "fund-controller",
        note: "Board portal delivery.",
        lastDeliveryAttemptId: null,
        lastDeliveryState: null,
        lastDeliveryAtUtc: null,
        lastDeliveryPackageRoute: null,
        lastDeliverySecureLink: null,
        versionStamp: "schedule-delivery-plan:sched-monthly-board-pack:board-reporting-committee:20260528120000:formats-3"
      },
      {
        planId: "schedule-delivery:sched-monthly-board-pack:investor-relations",
        scheduleId: "sched-monthly-board-pack",
        templateId: "monthly-board-pack",
        distributionId: "investor-relations",
        recipient: "Investor relations",
        recipientRole: "Investor communications",
        channel: "Investor portal",
        deliveryMode: "EmailLink",
        formats: ["Pdf", "Csv"],
        isReady: true,
        readinessSummary: "Will deliver Pdf/Csv by EmailLink to Investor relations when schedule 'sched-monthly-board-pack' runs.",
        route: "/reporting/report-packs?recipient=investor-relations",
        dueAtUtc: "2026-06-01T08:00:00Z",
        nextAsOfDate: "2026-06-01",
        owner: "investor-relations",
        note: "Investor email-link delivery.",
        lastDeliveryAttemptId: null,
        lastDeliveryState: null,
        lastDeliveryAtUtc: null,
        lastDeliveryPackageRoute: null,
        lastDeliverySecureLink: null,
        versionStamp: "schedule-delivery-plan:sched-monthly-board-pack:investor-relations:20260528120000:formats-2"
      }
    ],
    brandingThemes: [
      {
        themeId: "meridianstandard",
        name: "Meridian Standard",
        firmName: "Meridian",
        primaryColor: "#195E63",
        accentColor: "#2F9C95",
        textColor: "#102A2D",
        backgroundColor: "#FFFFFF",
        logoUri: null,
        footerText: "Generated by Meridian Reporting",
        disclaimer: "For authorized recipients only.",
        isBuiltIn: true
      },
      {
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
    ],
    summary: "4 export/reporting profiles are available for Accounting and Reporting workflows.",
    templates: [
      {
        templateId: "investor-monthly-statement",
        family: "InvestorStatement",
        name: "Investor Monthly Statement",
        version: "1.0.0",
        sections: ["cover", "performance", "positions", "flows"],
        lifecycleStatus: "Approved",
        isBuiltIn: true,
        isLatestApproved: true,
        approvalSummary: "Built-in approved template for InvestorStatement.",
        authoringRoute: "/api/fund-structure/reporting/templates/investor-monthly-statement/versions/1",
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
            sortDescending: true
          }
        ]
      },
      {
        templateId: "investor-monthly-statement",
        family: "InvestorStatement",
        name: "Investor Monthly Statement Draft",
        version: "2",
        sections: ["cover", "performance", "positions", "flows", "fees"],
        lifecycleStatus: "InReview",
        isBuiltIn: false,
        isLatestApproved: false,
        approvalSummary: "Custom v2 revision is waiting for controller approval.",
        authoringRoute: "/api/fund-structure/reporting/templates/investor-monthly-statement/versions/2"
      }
    ],
    workflowRecords: [
      {
        reportId: "report-restated-demo",
        fundProfileId: "demo-fund",
        fundAccountId: "demo-account",
        period: "2026-05",
        templateId: { name: "monthly-board-pack", version: 1 },
        state: "Restated",
        version: 2,
        createdAt: "2026-05-27T10:00:00Z",
        createdBy: "demo.reporter",
        updatedAt: "2026-05-28T12:00:00Z",
        auditTrail: [
          {
            at: "2026-05-28T12:00:00Z",
            actor: "demo.approver",
            action: "restated",
            fromState: "Published",
            toState: "Restated",
            note: "pricing-correction"
          }
        ],
        restatement: {
          reasonCode: "pricing-correction",
          approver: "fund-controller",
          priorVersionReportId: "report-published-demo",
          changedLines: [
            {
              lineKey: "nav.total",
              previousValue: "1250000",
              currentValue: "1249500",
              evidenceLinks: [
                {
                  evidenceId: "pricing-evidence-1",
                  label: "Pricing override",
                  route: "/reporting/evidence?subject=pricing-evidence-1",
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
          manifestId: "manifest-restated-demo",
          retainedManifestPath: "vault/report-packs/manifest-restated-demo.json",
          evidenceHash: "sha256:restated-demo",
          signedOffBy: "demo.publisher",
          signedOffAt: "2026-05-28T15:20:00Z",
          evidenceLinks: [
            {
              evidenceId: "publication-evidence-demo",
              label: "Publication manifest",
              route: "/reporting/manifests/manifest-restated-demo",
              source: "reporting",
              capturedAtUtc: "2026-05-28T15:20:00Z"
            }
          ]
        }
      }
    ]
  }
};
