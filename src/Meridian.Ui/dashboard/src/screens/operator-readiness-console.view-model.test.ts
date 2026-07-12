import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  buildOperatorReadinessConsoleState,
  useOperatorReadinessConsoleViewModel,
  type OperatorReadinessConsoleServices
} from "@/screens/operator-readiness-console.view-model";
import type { ApiRequestOptions } from "@/lib/api";
import type {
  DataWorkspaceResponse,
  AccountingWorkspaceResponse,
  OperatorInbox,
  OperatorWorkItem,
  StrategyWorkspaceResponse,
  TradingOperatorReadiness,
  TradingWorkspaceResponse
} from "@/types";

const readiness: TradingOperatorReadiness = {
  asOf: "2026-04-29T12:00:00Z",
  overallStatus: "ReviewRequired",
  readyForPaperOperation: false,
  activeSession: {
    sessionId: "paper-1",
    strategyId: "strategy-1",
    strategyName: "Index Momentum",
    isActive: true,
    initialCash: 100000,
    createdAt: "2026-04-29T11:00:00Z",
    closedAt: null,
    symbolCount: 3,
    orderCount: 2,
    positionCount: 1,
    portfolioValue: 100500
  },
  sessions: [],
  replay: {
    sessionId: "paper-1",
    replaySource: "paper-1.jsonl",
    isConsistent: true,
    comparedFillCount: 4,
    comparedOrderCount: 2,
    comparedLedgerEntryCount: 5,
    verifiedAt: "2026-04-29T11:30:00Z",
    lastPersistedFillAt: "2026-04-29T11:25:00Z",
    lastPersistedOrderUpdateAt: "2026-04-29T11:26:00Z",
    verificationAuditId: "audit-1",
    mismatchReasons: []
  },
  controls: {
    circuitBreakerOpen: false,
    circuitBreakerReason: null,
    circuitBreakerChangedBy: null,
    circuitBreakerChangedAt: null,
    manualOverrideCount: 0,
    symbolLimitCount: 2,
    defaultMaxPositionSize: 50000
  },
  promotion: {
    state: "ReviewRequired",
    reason: "Portfolio continuity review is missing.",
    requiresReview: true,
    sourceRunId: "run-1",
    targetRunId: null,
    suggestedNextMode: "paper",
    auditReference: "audit-promotion-1",
    approvalStatus: "pending",
    manualOverrideId: null,
    approvedBy: null,
    approvalChecklist: ["Portfolio continuity"]
  },
  trustGate: {
    gateId: "dk1",
    status: "signed",
    readyForOperatorReview: true,
    operatorSignoffRequired: true,
    operatorSignoffStatus: "signed",
    generatedAt: "2026-04-27T20:00:00Z",
    packetPath: "artifacts/provider-validation/dk1-packet.json",
    sourceSummary: "wave1-validation-summary.json",
    requiredSampleCount: 4,
    readySampleCount: 4,
    validatedEvidenceDocumentCount: 4,
    requiredOwners: ["Data", "Trading"],
    blockers: [],
    detail: "Signed DK1 packet is attached.",
    operatorSignoff: null
  },
  brokerageSync: {
    fundAccountId: "fund-1",
    providerId: "alpaca",
    externalAccountId: "PA-1",
    health: "Healthy",
    isLinked: true,
    isStale: false,
    lastAttemptedSyncAt: "2026-04-29T11:50:00Z",
    lastSuccessfulSyncAt: "2026-04-29T11:50:00Z",
    lastError: null,
    positionCount: 1,
    openOrderCount: 1,
    fillCount: 4,
    cashTransactionCount: 2,
    securityMissingCount: 0,
    warnings: []
  },
  acceptanceGates: [
    {
      gateId: "promotion",
      label: "Promotion checklist",
      status: "ReviewRequired",
      detail: "Continuity review is still open.",
      sessionId: "paper-1",
      runId: "run-1",
      auditReference: "audit-promotion-1"
    }
  ],
  workItems: [
    {
      workItemId: "promotion-review-run-1",
      kind: "PromotionReview",
      label: "Promotion checklist incomplete",
      detail: "Finish continuity review.",
      tone: "Warning",
      createdAt: "2026-04-29T12:00:00Z",
      runId: "run-1",
      fundAccountId: "fund-1",
      auditReference: "audit-promotion-1",
      workspace: "Trading",
      targetRoute: "/trading/readiness",
      targetPageTag: "TradingReadinessConsole"
    }
  ],
  warnings: []
};

const strategy: StrategyWorkspaceResponse = {
  metrics: [],
  runs: [
    {
      id: "run-1",
      strategyName: "Index Momentum",
      engine: "Meridian",
      mode: "paper",
      status: "Needs Review",
      dataset: "US equities",
      window: "30d",
      pnl: "+1.1%",
      sharpe: "1.2",
      lastUpdated: "2m ago",
      notes: "Ready for paper review."
    }
  ]
};

const trading = {
  metrics: [],
  positions: [],
  openOrders: [],
  fills: [],
  risk: {
    state: "Observe",
    summary: "Review",
    netExposure: "$1",
    grossExposure: "$1",
    var95: "$1",
    maxDrawdown: "-1%",
    buyingPowerUsed: "10%",
    activeGuardrails: []
  },
  brokerage: {
    provider: "Alpaca",
    account: "PA-1",
    environment: "paper",
    connection: "Connected",
    lastHeartbeat: "1s ago",
    orderIngress: "healthy",
    fillFeed: "healthy",
    notes: "ok"
  },
  readiness
} as TradingWorkspaceResponse;

const data: DataWorkspaceResponse = {
  metrics: [],
  providers: [
    {
      provider: "Polygon",
      status: "Healthy",
      capability: "Streaming equities",
      latency: "18ms",
      note: "Ready",
      trustScore: "0.96",
      signalSource: "wave1",
      reasonCode: "provider-ready",
      recommendedAction: "Keep active.",
      gateImpact: "Supports DK1"
    }
  ],
  backfills: [],
  exports: []
};

const accounting: AccountingWorkspaceResponse = {
  metrics: [],
  reconciliationQueue: [],
  breakQueue: [
    {
      breakId: "run-1:cash",
      runId: "run-1",
      strategyName: "Index Momentum",
      category: "AmountMismatch",
      status: "Open",
      variance: 500,
      reason: "Cash variance over tolerance.",
      assignedTo: null,
      detectedAt: "2026-04-29T12:00:00Z",
      lastUpdatedAt: "2026-04-29T12:00:00Z",
      reviewedBy: null,
      reviewedAt: null,
      resolvedBy: null,
      resolvedAt: null,
      resolutionNote: null
    }
  ],
  cashFlow: {
    totalCash: 100,
    totalLedgerCash: 101,
    netVariance: 1,
    totalFinancing: 0,
    runsWithCashSignals: 1,
    runsWithCashVariance: 1,
    tone: "warning",
    summary: "Review cash variance."
  },
  reporting: {
    profileCount: 2,
    recommendedProfiles: ["excel"],
    profiles: [
      {
        id: "excel",
        name: "Excel",
        targetTool: "Excel",
        format: "Xlsx",
        description: "Board-ready workbook.",
        loaderScript: false,
        dataDictionary: true
      }
    ],
    reportPackTargets: ["board"],
    summary: "Report packs can be prepared after reconciliation review."
  }
};

const inbox: OperatorInbox = {
  asOf: "2026-04-29T12:01:00Z",
  items: [
    ...readiness.workItems,
    {
      workItemId: "reconciliation-break-run-1-cash",
      kind: "ReconciliationBreak",
      label: "Cash break open",
      detail: "Cash variance over tolerance.",
      tone: "Warning",
      createdAt: "2026-04-29T12:01:00Z",
      runId: "run-1",
      fundAccountId: null,
      auditReference: null,
      workspace: "Accounting",
      targetRoute: "/accounting/reconciliation",
      targetPageTag: "FundReconciliation"
    }
  ],
  criticalCount: 0,
  warningCount: 2,
  reviewCount: 2,
  summary: "2 review items need attention."
};

const readyReadiness: TradingOperatorReadiness = {
  ...readiness,
  overallStatus: "Ready",
  readyForPaperOperation: true,
  readyForLiveOperation: true,
  liveOperationBlockers: [],
  promotion: {
    ...readiness.promotion,
    state: "Ready",
    reason: "Required operator evidence is complete.",
    requiresReview: false,
    approvalStatus: "approved",
    approvalChecklist: []
  },
  acceptanceGates: [],
  workItems: [],
  warnings: []
};

const readyTrading: TradingWorkspaceResponse = {
  ...trading,
  readiness: readyReadiness
};

const cleanGovernance: AccountingWorkspaceResponse = {
  ...accounting,
  reconciliationQueue: [],
  breakQueue: []
};

const noReportPackGovernance: AccountingWorkspaceResponse = {
  ...cleanGovernance,
  reporting: {
    profileCount: 0,
    recommendedProfiles: [],
    profiles: [],
    reportPackTargets: [],
    summary: "No governed report-pack recipients are configured."
  }
};

const cleanInbox: OperatorInbox = {
  asOf: "2026-04-29T12:01:00Z",
  items: [],
  criticalCount: 0,
  warningCount: 0,
  reviewCount: 0,
  summary: "No operator work items need attention."
};

describe("operator readiness console view model", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("builds the API-first readiness console from shared workstation payloads", () => {
    const state = buildOperatorReadinessConsoleState({
      strategy,
      trading,
      data,
      accounting,
      operatorInbox: inbox,
      inboxLoading: false,
      inboxError: null
    });

    expect(state.title).toBe("Operator Readiness Console");
    expect(state.overallLevel).toBe("blocked");
    expect(state.overallLabel).toBe("Blocked");
    expect(state.asOf).toBe("Apr 29, 12:01 UTC");
    expect(state.statusAnnouncement).toContain("as of Apr 29, 12:01 UTC");
    expect(state.latestRuns[0]).toEqual(expect.objectContaining({ id: "run-1", value: "Needs Review" }));
    expect(state.activeSessionFacts[0]).toEqual(expect.objectContaining({ value: "paper-1", level: "ready" }));
    expect(state.activeSessionFacts.find((row) => row.id === "paper-equity")?.meta).toBe("Created Apr 29, 11:00 UTC");
    expect(state.providerTrustRows.some((row) => row.label === "DK1 provider trust")).toBe(true);
    expect(state.reconciliationRows[0]).toEqual(expect.objectContaining({ id: "run-1:cash", level: "blocked" }));
    expect(state.promotionRows.some((row) => row.label === "Promotion checklist")).toBe(true);
    expect(state.reportPackFacts[0]).toEqual(expect.objectContaining({ value: "Recipients present", level: "ready" }));
    expect(state.apiSources.map((source) => source.endpoint)).toContain("/api/workstation/operator/inbox");
    expect(state.apiSources.map((source) => source.id)).toEqual(expect.arrayContaining([
      "strategy-runs",
      "data-confidence",
      "accounting",
      "reporting"
    ]));
    expect(state.apiSources.map((source) => source.id)).not.toContain("governance");
    expect(state.apiSources.map((source) => source.endpoint)).toEqual(expect.arrayContaining([
      "/api/workstation/strategy",
      "/api/workstation/data",
      "/api/workstation/accounting",
      "/api/workstation/reporting"
    ]));
    expect(state.apiSources.map((source) => source.endpoint)).not.toEqual(expect.arrayContaining([
      "/api/workstation/strategy",
      "/api/workstation/data-operations"
    ]));
    expect(state.apiSourcesLabel).toBe("Shared readiness API sources");
    expect(state.apiSources[0].ariaLabel).toContain("/api/workstation/trading/readiness");
    expect(state.metricsLabel).toBe("Operator readiness metrics");
    expect(state.metrics[0]).toMatchObject({
      id: "latest-runs",
      delta: "Ready",
      tone: "success",
      detailId: "readiness-metric-latest-runs-detail",
      statusAriaLabel: "Latest runs status Ready"
    });
    expect(state.metrics.find((metric) => metric.id === "provider-trust")).toMatchObject({
      value: "3/3",
      delta: "Ready",
      tone: "success"
    });
    expect(state.latestRuns[0].ariaLabel).toContain("Index Momentum: Needs Review");
    expect(state.latestRuns[0].detailId).toBe("readiness-row-latest-runs-run-1-detail");
    expect(state.panels.map((panel) => panel.id)).toEqual([
      "latest-runs",
      "active-paper-session",
      "provider-trust",
      "reconciliation-breaks",
      "promotion-blockers",
      "reporting-report-packs"
    ]);
    expect(state.panels[0]).toEqual(expect.objectContaining({
      ariaLabel: "Latest runs readiness evidence",
      listLabel: "Latest runs rows",
      tableLabel: "Latest runs readiness evidence table",
      detailLabel: "Selected latest runs evidence detail",
      detailPanelId: "operator-readiness-latest-runs-evidence-detail",
      selectedRowId: "run-1",
      selectedDetail: expect.objectContaining({
        id: "run-1",
        title: "Index Momentum",
        ariaLabel: "Selected latest runs evidence: Index Momentum"
      })
    }));
    expect(state.checkpointGatesLabel).toBe("Full-console readiness checkpoints");
    expect(state.checkpointGates.map((gate) => gate.label)).toEqual([
      "Session active",
      "Replay verified",
      "Risk state explainable",
      "Promotion review trace complete",
      "Live operation gate",
      "Brokerage sync healthy",
      "Reconciliation clear",
      "Report pack approval ready",
      "Operator work items settled"
    ]);
    expect(state.checkpointGates.find((gate) => gate.id === "live-operation-ready")).toEqual(expect.objectContaining({
      value: "Review required",
      level: "review",
      action: expect.objectContaining({ route: "/trading/readiness" })
    }));
    expect(state.checkpointGates.find((gate) => gate.id === "reconciliation-clear")).toEqual(expect.objectContaining({
      value: "1 open",
      level: "blocked",
      action: expect.objectContaining({ route: "/accounting/reconciliation" })
    }));
    const detailIds = [...state.promotionRows, ...state.workItems].map((row) => row.detailId);
    expect(new Set(detailIds).size).toBe(detailIds.length);
    expect(state.workItemsRegionLabel).toBe("Operator inbox review work items");
    expect(state.workItemsListLabel).toBe("Prioritized operator work items");
    expect(state.workItemsTableLabel).toBe("Prioritized operator work items table");
    expect(state.workItemsDetailLabel).toBe("Selected operator work item detail");
    expect(state.selectedWorkItemId).toBe("reconciliation-break-run-1-cash");
    expect(state.selectedWorkItemDetail).toEqual(expect.objectContaining({
      title: "Cash break open",
      statusLabel: "Warning",
      action: expect.objectContaining({ route: "/accounting/reconciliation" })
    }));
    expect(state.selectedWorkItemDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Created", value: "Apr 29, 12:01 UTC" }
    ]));
    expect(state.selectedWorkItemDetail?.ariaLabel).toBe("Selected operator work item: Cash break open");
    expect(state.nextAction).toEqual(expect.objectContaining({
      title: "Reconciliation clear",
      label: "Open break queue",
      route: "/accounting/reconciliation",
      level: "blocked",
      actionAriaLabel: "Open break queue: Reconciliation clear"
    }));
    expect(state.workItems.find((item) => item.id === "promotion-review-run-1")?.action).toEqual({
      label: "Open promotion review",
      route: "/trading/readiness",
      ariaLabel: "Open promotion review: Promotion checklist incomplete",
      variant: "outline"
    });
    expect(state.workItems.find((item) => item.id === "promotion-review-run-1")).toEqual(expect.objectContaining({
      createdAtLabel: "Apr 29, 12:00 UTC",
      ariaLabel: expect.stringContaining("Created Apr 29, 12:00 UTC")
    }));
    expect(state.panels.find((panel) => panel.id === "reporting-report-packs")).toEqual(expect.objectContaining({
      title: "Reporting report packs",
      ariaLabel: "Reporting report packs readiness evidence",
      listLabel: "Reporting report packs rows",
      tableLabel: "Reporting report packs readiness evidence table",
      detailPanelId: "operator-readiness-reporting-report-packs-evidence-detail"
    }));
  });

  it("surfaces broker execution reconciliation as a live-readiness checkpoint and work item", () => {
    const brokerWorkItem: OperatorWorkItem = {
      workItemId: "broker-execution-reconciliation-alpaca",
      kind: "BrokerExecutionReconciliation",
      label: "Broker execution reconciliation",
      detail: "1 broker/OMS open-order reconciliation break requires review before live operation.",
      tone: "Critical",
      createdAt: "2026-04-29T12:02:00Z",
      runId: null,
      fundAccountId: null,
      auditReference: null,
      workspace: "Trading",
      targetRoute: "/api/workstation/trading/readiness",
      targetPageTag: "TradingShell"
    };
    const readinessWithBrokerBreak: TradingOperatorReadiness = {
      ...readiness,
      overallStatus: "Blocked",
      executionReconciliation: {
        status: "Blocked",
        gatewayId: "alpaca",
        brokerDisplayName: "Alpaca Markets",
        brokerHealthy: true,
        brokerConnected: true,
        matchedOpenOrderCount: 0,
        breakCount: 1,
        reconciledAt: "2026-04-29T12:02:00Z",
        detail: "1 broker/OMS open-order reconciliation break requires review before live operation.",
        breaks: [
          {
            kind: "MissingInBrokerage",
            description: "OMS tracks an open order that the broker did not report.",
            localOrderId: "order-1",
            brokerOrderId: null,
            clientOrderId: "order-1",
            symbol: "AAPL",
            localValue: "Accepted Buy 10 AAPL",
            brokerValue: null
          }
        ]
      },
      acceptanceGates: [
        ...readiness.acceptanceGates,
        {
          gateId: "broker-execution-reconciliation",
          label: "Broker execution reconciliation",
          status: "Blocked",
          detail: "1 broker/OMS open-order reconciliation break requires review before live operation.",
          sessionId: null,
          runId: null,
          auditReference: "alpaca",
          requiredNextAction: "Review broker/OMS open-order breaks before live operation."
        }
      ],
      workItems: [brokerWorkItem],
      warnings: []
    };
    const state = buildOperatorReadinessConsoleState({
      strategy,
      trading: {
        ...trading,
        readiness: readinessWithBrokerBreak
      },
      data,
      accounting: {
        ...accounting,
        breakQueue: [],
        reconciliationQueue: []
      },
      operatorInbox: {
        ...cleanInbox,
        criticalCount: 1,
        reviewCount: 1,
        items: [brokerWorkItem],
        summary: "Broker execution reconciliation needs attention."
      },
      inboxLoading: false,
      inboxError: null
    });

    expect(state.overallLevel).toBe("blocked");
    expect(state.checkpointGates.find((gate) => gate.id === "broker-execution-reconciliation")).toEqual(expect.objectContaining({
      label: "Broker execution reconciliation",
      value: "Blocked",
      detail: "1 broker/OMS open-order reconciliation break requires review before live operation.",
      meta: "alpaca",
      level: "blocked",
      action: expect.objectContaining({
        label: "Review broker orders",
        route: "/trading/readiness"
      })
    }));
    expect(state.workItems.find((item) => item.id === "broker-execution-reconciliation-alpaca")).toEqual(expect.objectContaining({
      label: "Broker execution reconciliation",
      value: "Critical",
      action: expect.objectContaining({
        label: "Review broker orders",
        route: "/trading"
      })
    }));
    expect(state.nextAction).toEqual(expect.objectContaining({
      title: "Broker execution reconciliation",
      label: "Review broker orders",
      route: "/trading"
    }));
  });

  it("surfaces live operation blockers as a distinct console checkpoint", () => {
    const state = buildOperatorReadinessConsoleState({
      strategy,
      trading: {
        ...trading,
        readiness: {
          ...readiness,
          readyForLiveOperation: false,
          liveOperationBlockers: ["promotion:approved-live-trace", "brokerageSync:account-scope-required"]
        }
      },
      data,
      accounting,
      operatorInbox: inbox,
      inboxLoading: false,
      inboxError: null
    });

    expect(state.checkpointGates.find((gate) => gate.id === "live-operation-ready")).toEqual(expect.objectContaining({
      label: "Live operation gate",
      value: "2 blockers",
      detail: "Live operation remains blocked by promotion:approved-live-trace, brokerageSync:account-scope-required.",
      meta: "promotion:approved-live-trace",
      level: "blocked",
      action: expect.objectContaining({
        label: "Open live readiness",
        route: "/trading/readiness"
      })
    }));
  });

  it("derives safe work-item routes and fallback actions in the view model", () => {
    const state = buildOperatorReadinessConsoleState({
      strategy: null,
      trading: null,
      data: null,
      accounting: null,
      operatorInbox: {
        ...cleanInbox,
        items: [
          {
            workItemId: "security-master-gap",
            kind: "SecurityMasterCoverage",
            label: "Security coverage open",
            detail: "Resolve missing identifier coverage.",
            tone: "Critical",
            createdAt: "2026-04-29T12:02:00Z",
            runId: null,
            fundAccountId: null,
            auditReference: null,
            workspace: "Accounting",
            targetRoute: "https://example.test/accounting/security-master",
            targetPageTag: "SecurityMaster"
          },
          {
            workItemId: "provider-trust",
            kind: "ProviderTrustGate",
            label: "Provider trust gate review",
            detail: "Review provider evidence.",
            tone: "Warning",
            createdAt: "2026-04-29T12:03:00Z",
            runId: null,
            fundAccountId: null,
            auditReference: null,
            workspace: "Data",
            targetRoute: "/api/workstation/provider-validation/dk1",
            targetPageTag: "ProviderTrust"
          },
          {
            workItemId: "legacy-break-route",
            kind: "ReconciliationBreak",
            label: "Legacy break route",
            detail: "Review a break from a legacy accounting route.",
            tone: "Info",
            createdAt: "2026-04-29T12:04:00Z",
            runId: "run-1",
            fundAccountId: null,
            auditReference: null,
            workspace: "Accounting",
            targetRoute: "/accounting/reconciliation?runId=run-1#cash",
            targetPageTag: "FundReconciliation"
          },
          {
            workItemId: "api-route",
            kind: "ReportPackApproval",
            label: "API route should not render",
            detail: "Fallback to the Reporting workspace.",
            tone: "Success",
            createdAt: "2026-04-29T12:05:00Z",
            runId: null,
            fundAccountId: null,
            auditReference: null,
            workspace: "Reporting",
            targetRoute: "/api/workstation/operator/inbox",
            targetPageTag: "ReportPackApproval"
          },
          {
            workItemId: "workstation-prefixed-brokerage",
            kind: "BrokerageSync",
            label: "Brokerage sync route",
            detail: "Review brokerage sync from a host-served workstation route.",
            tone: "Warning",
            createdAt: "2026-04-29T12:06:00Z",
            runId: null,
            fundAccountId: "fund-1",
            auditReference: null,
            workspace: "Portfolio",
            targetRoute: "/workstation/portfolio/brokerage-sync?fundAccountId=fund-1",
            targetPageTag: "AccountPortfolio"
          },
          {
            workItemId: "api-brokerage-fallback",
            kind: "BrokerageSync",
            label: "API brokerage route should fallback",
            detail: "Fallback to the browser brokerage sync panel.",
            tone: "Info",
            createdAt: "2026-04-29T12:07:00Z",
            runId: null,
            fundAccountId: "fund-1",
            auditReference: null,
            workspace: "Portfolio",
            targetRoute: "/api/fund-accounts/fund-1/brokerage-sync",
            targetPageTag: "AccountPortfolio"
          }
        ],
        warningCount: 2,
        criticalCount: 1,
        reviewCount: 4,
        summary: "4 review items need attention."
      },
      inboxLoading: false,
      inboxError: null
    });

    expect(state.workItems[0].action).toEqual({
      label: "Open Security Master",
      route: "/accounting/security-master",
      ariaLabel: "Open Security Master: Security coverage open",
      variant: "secondary"
    });
    expect(state.nextAction).toEqual(expect.objectContaining({
      title: "Security coverage open",
      label: "Open Security Master",
      route: "/accounting/security-master",
      level: "blocked"
    }));
    expect(state.overallLevel).toBe("blocked");
    expect(state.overallLabel).toBe("Blocked");
    expect(state.overallDetail).toContain("Trading readiness has not loaded yet");
    expect(state.overallDetail).toContain("1 critical item");
    expect(state.workItems.find((item) => item.id === "provider-trust")?.action).toEqual({
      label: "Open provider trust",
      route: "/data/providers",
      ariaLabel: "Open provider trust: Provider trust gate review",
      variant: "outline"
    });
    expect(state.workItems.find((item) => item.id === "legacy-break-route")?.action).toEqual({
      label: "Open break queue",
      route: "/accounting/reconciliation?runId=run-1#cash",
      ariaLabel: "Open break queue: Legacy break route",
      variant: "outline"
    });
    expect(state.workItems.find((item) => item.id === "api-route")?.action).toEqual({
      label: "Open report packs",
      route: "/reporting/report-packs",
      ariaLabel: "Open report packs: API route should not render",
      variant: "outline"
    });
    expect(state.workItems.find((item) => item.id === "workstation-prefixed-brokerage")?.action).toEqual({
      label: "Fix provider setup",
      route: "/settings#alpaca-provider-setup",
      ariaLabel: "Fix provider setup: Brokerage sync route",
      variant: "outline"
    });
    expect(state.workItems.find((item) => item.id === "api-brokerage-fallback")?.action).toEqual({
      label: "Fix provider setup",
      route: "/settings#alpaca-provider-setup",
      ariaLabel: "Fix provider setup: API brokerage route should fallback",
      variant: "outline"
    });
  });

  it("routes brokerage-sync work items to provider setup repair before portfolio review", () => {
    const state = buildOperatorReadinessConsoleState({
      strategy: null,
      trading: null,
      data: null,
      accounting: null,
      operatorInbox: {
        ...cleanInbox,
        items: [
          {
            workItemId: "brokerage-sync-failed",
            kind: "BrokerageSync",
            label: "Brokerage sync failed",
            detail: "Alpaca account sync failed because credentials require review.",
            tone: "Critical",
            createdAt: "2026-04-29T12:08:00Z",
            runId: null,
            fundAccountId: "fund-1",
            auditReference: null,
            workspace: "Portfolio",
            targetRoute: "/api/fund-accounts/fund-1/brokerage-sync",
            targetPageTag: "AccountPortfolio"
          }
        ],
        criticalCount: 1,
        warningCount: 0,
        reviewCount: 1,
        summary: "1 critical item needs attention."
      },
      inboxLoading: false,
      inboxError: null
    });

    expect(state.workItems[0].action).toEqual({
      label: "Fix provider setup",
      route: "/settings#alpaca-provider-setup",
      ariaLabel: "Fix provider setup: Brokerage sync failed",
      variant: "secondary"
    });
    expect(state.selectedWorkItemDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Route", value: "/settings#alpaca-provider-setup" }
    ]));
    expect(state.nextAction).toEqual(expect.objectContaining({
      title: "Brokerage sync failed",
      label: "Fix provider setup",
      route: "/settings#alpaca-provider-setup",
      level: "blocked"
    }));
  });

  it("routes ledger-period close work items to the accounting reconciliation lane", () => {
    const state = buildOperatorReadinessConsoleState({
      strategy: null,
      trading: null,
      data: null,
      accounting: null,
      operatorInbox: {
        ...cleanInbox,
        items: [
          {
            workItemId: "ledger-period-close-p02",
            kind: "LedgerPeriodClose",
            label: "SoftClosed sign-off required",
            detail: "Alpha Fund 2026-P02 is in SoftClosed. Open FundReconciliation before approving the close.",
            tone: "Warning",
            createdAt: "2026-04-29T12:08:00Z",
            runId: null,
            fundAccountId: null,
            auditReference: "period-p02",
            workspace: "Accounting",
            targetRoute: "/api/workstation/reconciliation/break-queue",
            targetPageTag: "FundReconciliation",
            scope: "ledger-period:p02",
            requiredSignoffRole: "Fund Controller",
            toleranceProfileId: "month-end-25bp",
            signoffStatus: "Pending"
          }
        ],
        warningCount: 1,
        criticalCount: 0,
        reviewCount: 1,
        summary: "1 warning work item needs review."
      },
      inboxLoading: false,
      inboxError: null
    });

    expect(state.workItems[0].action).toEqual({
      label: "Open reconciliation",
      route: "/accounting/reconciliation",
      ariaLabel: "Open reconciliation: SoftClosed sign-off required",
      variant: "outline"
    });
    expect(state.selectedWorkItemDetail).toEqual(expect.objectContaining({
      id: "ledger-period-close-p02",
      action: expect.objectContaining({ route: "/accounting/reconciliation" })
    }));
    expect(state.selectedWorkItemDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Required sign-off role", value: "Fund Controller" },
      { label: "Sign-off status", value: "Pending" },
      { label: "Tolerance profile", value: "month-end-25bp" },
      { label: "Route", value: "/accounting/reconciliation" },
      { label: "Evidence", value: "Accounting - FundReconciliation - period-p02 - Fund Controller sign-off Pending" }
    ]));
    expect(state.nextAction).toEqual(expect.objectContaining({
      title: "SoftClosed sign-off required",
      label: "Open reconciliation",
      route: "/accounting/reconciliation",
      level: "review"
    }));
  });

  it("degrades unknown work-item kinds to a safe default action and route", () => {
    const unknownItem = {
      ...readiness.workItems[0],
      workItemId: "unknown-kind",
      kind: "FutureKindFromBackend" as OperatorWorkItem["kind"],
      label: "Unknown kind should not break rendering"
    };
    const state = buildOperatorReadinessConsoleState({
      trading: {
        ...trading,
        readiness: {
          ...readiness,
          workItems: [unknownItem]
        }
      },
      operatorInbox: null,
      inboxLoading: false,
      inboxError: null,
      strategy,
      data,
      accounting
    });

    expect(state.workItems[0]?.action).toEqual({
      label: "Open operator item",
      route: "/trading/readiness",
      ariaLabel: "Open operator item: Unknown kind should not break rendering",
      variant: "outline"
    });
  });

  it("blocks the headline from a critical inbox item when trading readiness is missing", () => {
    const state = buildOperatorReadinessConsoleState({
      strategy: null,
      trading: null,
      data: null,
      accounting: null,
      operatorInbox: {
        ...cleanInbox,
        items: [{
          workItemId: "paper-replay-mismatch-paper-1",
          kind: "PaperReplay",
          label: "Replay mismatch",
          detail: "Order history diverged after restart.",
          tone: "Critical",
          createdAt: "2026-04-29T12:08:00Z",
          runId: "run-1",
          fundAccountId: null,
          auditReference: "audit-replay-2",
          workspace: "Trading",
          targetRoute: "/trading/readiness",
          targetPageTag: "TradingReadinessConsole"
        }],
        criticalCount: 1,
        warningCount: 0,
        reviewCount: 1,
        summary: "1 critical item needs attention."
      },
      inboxLoading: false,
      inboxError: null
    });

    expect(state.overallLevel).toBe("blocked");
    expect(state.overallLabel).toBe("Blocked");
    expect(state.nextAction).toEqual(expect.objectContaining({
      title: "Replay mismatch",
      route: "/trading/readiness",
      level: "blocked"
    }));
  });

  it("maps replay mismatch and stale replay gates into the normalized checkpoints", () => {
    const mismatchState = buildOperatorReadinessConsoleState({
      strategy,
      trading: {
        ...readyTrading,
        readiness: {
          ...readyReadiness,
          overallStatus: "Blocked",
          readyForPaperOperation: false,
          replay: {
            ...readyReadiness.replay!,
            isConsistent: false,
            mismatchReasons: ["order-history-count-mismatch"]
          },
          acceptanceGates: [{
            gateId: "replay",
            label: "Replay confidence",
            status: "Blocked",
            detail: "Order history count diverged from the verification audit.",
            sessionId: "paper-1",
            runId: "run-1",
            auditReference: "audit-replay-2"
          }]
        }
      },
      data,
      accounting: cleanGovernance,
      operatorInbox: cleanInbox,
      inboxLoading: false,
      inboxError: null
    });
    const staleState = buildOperatorReadinessConsoleState({
      strategy,
      trading: {
        ...readyTrading,
        readiness: {
          ...readyReadiness,
          overallStatus: "ReviewRequired",
          readyForPaperOperation: false,
          acceptanceGates: [{
            gateId: "replay",
            label: "Replay confidence",
            status: "ReviewRequired",
            detail: "Active order count changed after the latest verification.",
            sessionId: "paper-1",
            runId: "run-1",
            auditReference: "audit-replay-1"
          }],
          workItems: [{
            ...readiness.workItems[0],
            workItemId: "paper-replay-stale-paper-1",
            kind: "PaperReplay",
            label: "Replay verification stale",
            detail: "Rerun replay verification before accepting readiness.",
            tone: "Warning",
            targetRoute: "/trading/readiness",
            targetPageTag: "TradingReadinessConsole"
          }]
        }
      },
      data,
      accounting: cleanGovernance,
      operatorInbox: cleanInbox,
      inboxLoading: false,
      inboxError: null
    });

    expect(mismatchState.checkpointGates.find((gate) => gate.id === "replay-verified")).toEqual(expect.objectContaining({
      value: "Blocked",
      level: "blocked"
    }));
    expect(mismatchState.nextAction).toEqual(expect.objectContaining({
      title: "Replay verified",
      label: "Open replay evidence",
      level: "blocked"
    }));
    expect(staleState.checkpointGates.find((gate) => gate.id === "replay-verified")).toEqual(expect.objectContaining({
      value: "Review required",
      level: "review"
    }));
    expect(staleState.nextAction).toEqual(expect.objectContaining({
      title: "Replay verification stale",
      level: "review"
    }));
  });

  it("maps brokerage sync health to blocked, review, and ready checkpoint states", () => {
    const buildState = (health: NonNullable<TradingOperatorReadiness["brokerageSync"]>["health"], isStale: boolean) => buildOperatorReadinessConsoleState({
      strategy,
      trading: {
        ...readyTrading,
        readiness: {
          ...readyReadiness,
          brokerageSync: {
            ...readyReadiness.brokerageSync!,
            health,
            isStale,
            lastError: health === "Failed" ? "Brokerage sync failed." : null
          }
        }
      },
      data,
      accounting: cleanGovernance,
      operatorInbox: cleanInbox,
      inboxLoading: false,
      inboxError: null
    });

    expect(buildState("Failed", false).checkpointGates.find((gate) => gate.id === "brokerage-sync")).toEqual(expect.objectContaining({
      value: "Failed",
      level: "blocked"
    }));
    expect(buildState("Degraded", false).checkpointGates.find((gate) => gate.id === "brokerage-sync")).toEqual(expect.objectContaining({
      value: "Degraded",
      level: "review"
    }));
    expect(buildState("Healthy", true).checkpointGates.find((gate) => gate.id === "brokerage-sync")).toEqual(expect.objectContaining({
      value: "Healthy",
      level: "review"
    }));
    expect(buildState("Healthy", false).checkpointGates.find((gate) => gate.id === "brokerage-sync")).toEqual(expect.objectContaining({
      value: "Healthy",
      level: "ready"
    }));
  });

  it("dedupes readiness and inbox work items by stable id while preserving highest severity", () => {
    const duplicateReadiness: OperatorWorkItem = {
      ...readiness.workItems[0],
      workItemId: "shared-promotion-item",
      tone: "Warning",
      detail: "Readiness fallback item.",
      createdAt: "2026-04-29T12:00:00Z"
    };
    const duplicateInbox: OperatorWorkItem = {
      ...duplicateReadiness,
      tone: "Critical",
      detail: "Inbox escalated item.",
      createdAt: "2026-04-29T12:05:00Z"
    };

    const state = buildOperatorReadinessConsoleState({
      strategy,
      trading: {
        ...readyTrading,
        readiness: {
          ...readyReadiness,
          overallStatus: "ReviewRequired",
          readyForPaperOperation: false,
          workItems: [duplicateReadiness]
        }
      },
      data,
      accounting: cleanGovernance,
      operatorInbox: {
        ...cleanInbox,
        items: [duplicateInbox],
        criticalCount: 1,
        reviewCount: 1,
        summary: "1 critical item needs attention."
      },
      inboxLoading: false,
      inboxError: null
    });

    expect(state.workItems.filter((item) => item.id === "shared-promotion-item")).toHaveLength(1);
    expect(state.workItems[0]).toEqual(expect.objectContaining({
      id: "shared-promotion-item",
      value: "Critical",
      detail: "Inbox escalated item."
    }));
    expect(state.workItemsSummary).toBe("Showing 1 of 1 operator work item; 1 critical item. Critical items sort first.");
  });

  it("keeps report-pack ownership aligned to Reporting when payloads are unavailable", () => {
    const state = buildOperatorReadinessConsoleState({
      strategy: null,
      trading: null,
      data: null,
      accounting: null,
      reporting: null,
      operatorInbox: null,
      inboxLoading: false,
      inboxError: null
    });

    expect(state.overallDetail).toContain("Strategy, Data, Accounting, or Reporting payloads");
    expect(state.reportPackFacts[0]).toEqual(expect.objectContaining({
      label: "Report-pack readiness",
      detail: "Reporting payload has not loaded.",
      meta: "Wait for Accounting/Reporting bootstrap recovery.",
      level: "review"
    }));
    expect(state.panels.find((panel) => panel.id === "reporting-report-packs")).toEqual(expect.objectContaining({
      title: "Reporting report packs",
      emptyText: "No reporting readiness payload loaded."
    }));
  });

  it("prioritizes critical operator inbox work items before truncating the visible queue", () => {
    const warningItems: OperatorWorkItem[] = Array.from({ length: 6 }, (_, index) => ({
      ...readiness.workItems[0],
      workItemId: `warning-${index}`,
      kind: "ReportPackApproval",
      label: `Warning item ${index}`,
      detail: `Warning detail ${index}`,
      tone: "Warning",
      createdAt: `2026-04-29T12:0${index}:00Z`,
      workspace: "Reporting",
      targetRoute: "/reporting",
      targetPageTag: "ReportPackApproval"
    }));
    const criticalItem: OperatorWorkItem = {
      ...readiness.workItems[0],
      workItemId: "critical-security-gap",
      kind: "SecurityMasterCoverage",
      label: "Critical security coverage gap",
      detail: "Resolve missing identifier coverage before accepting readiness.",
      tone: "Critical",
      createdAt: "2026-04-29T11:00:00Z",
      workspace: "Accounting",
      targetRoute: "/accounting/security-master",
      targetPageTag: "SecurityMaster"
    };
    const infoItem: OperatorWorkItem = {
      ...readiness.workItems[0],
      workItemId: "info-item",
      kind: "BrokerageSync",
      label: "Info item",
      detail: "Brokerage heartbeat recorded.",
      tone: "Info",
      createdAt: "2026-04-29T12:10:00Z",
      workspace: "Trading",
      targetRoute: "/trading/readiness",
      targetPageTag: "BrokerageSync"
    };

    const state = buildOperatorReadinessConsoleState({
      strategy: null,
      trading: null,
      data: null,
      accounting: null,
      operatorInbox: {
        ...cleanInbox,
        items: [...warningItems, criticalItem, infoItem],
        criticalCount: 1,
        warningCount: 6,
        reviewCount: 7,
        summary: "7 review items need attention."
      },
      inboxLoading: false,
      inboxError: null
    });

    expect(state.workItems).toHaveLength(6);
    expect(state.workItems[0]).toEqual(expect.objectContaining({
      label: "Critical security coverage gap",
      level: "blocked"
    }));
    expect(state.selectedWorkItemId).toBe("critical-security-gap");
    expect(state.selectedWorkItemDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Route", value: "/accounting/security-master" },
      { label: "Attention", value: "Blocked" }
    ]));
    expect(state.workItems.map((item) => item.label)).not.toContain("Info item");
    expect(state.workItemsSummary).toBe("Showing 6 of 8 operator work items; 1 critical item, 6 warnings, 1 info item. Critical items sort first.");
    expect(state.workItemsOverflowText).toBe("2 additional work items hidden from this view after priority sorting.");
    expect(state.nextAction).toEqual(expect.objectContaining({
      title: "Critical security coverage gap",
      label: "Open Security Master",
      route: "/accounting/security-master",
      level: "blocked"
    }));
  });

  it("surfaces operator inbox failures while keeping payload fallbacks visible", () => {
    const state = buildOperatorReadinessConsoleState({
      strategy,
      trading,
      data,
      accounting,
      operatorInbox: null,
      inboxLoading: false,
      inboxError: "Request failed for /api/workstation/operator/inbox (503)"
    });

    expect(state.inboxErrorText).toContain("503");
    expect(state.workItems).toHaveLength(1);
    expect(state.overallDetail).toContain("operator inbox failed to load");
    expect(state.statusAnnouncement).toContain("Operator inbox failed");
    expect(state.inboxErrorRecovery).toEqual({
      title: "Operator inbox unavailable",
      detail: "Request failed for /api/workstation/operator/inbox (503). Retry the shared inbox before accepting readiness; fallback rows remain visible for triage.",
      actionLabel: "Retry inbox",
      actionAriaLabel: "Retry loading operator inbox work items after failure"
    });
    expect(state.workItemsEmptyText).toContain("Operator inbox work items are unavailable");
    expect(state.workItemsEmptyAction).toEqual(expect.objectContaining({
      label: "Open break queue",
      route: "/accounting/reconciliation"
    }));
  });

  it("keeps overall readiness in review while the operator inbox is still loading", () => {
    const state = buildOperatorReadinessConsoleState({
      strategy,
      trading: readyTrading,
      data,
      accounting: cleanGovernance,
      operatorInbox: null,
      inboxLoading: true,
      inboxError: null
    });

    expect(state.overallLabel).toBe("Review pending");
    expect(state.overallLevel).toBe("review");
    expect(state.overallDetail).toContain("operator inbox is still loading");
    expect(state.inboxLoadingLabel).toBe("Loading operator inbox...");
  });

  it("requires a settled clean operator inbox before reporting overall ready", () => {
    const loadingState = buildOperatorReadinessConsoleState({
      strategy,
      trading: readyTrading,
      data,
      accounting: cleanGovernance,
      operatorInbox: null,
      inboxLoading: false,
      inboxError: "Request failed for /api/workstation/operator/inbox (503)"
    });

    const readyState = buildOperatorReadinessConsoleState({
      strategy,
      trading: readyTrading,
      data,
      accounting: cleanGovernance,
      operatorInbox: cleanInbox,
      inboxLoading: false,
      inboxError: null
    });

    expect(loadingState.overallLevel).toBe("review");
    expect(loadingState.overallLabel).toBe("Review pending");
    expect(readyState.overallLevel).toBe("ready");
    expect(readyState.overallLabel).toBe("Ready");
    expect(readyState.nextAction).toEqual(expect.objectContaining({
      title: "No blocking review items",
      label: "Open Trading cockpit",
      route: "/trading",
      level: "ready"
    }));
    expect(readyState.workItems).toHaveLength(0);
    expect(readyState.workItemsEmptyText).toContain("No operator work items need attention");
    expect(readyState.workItemsEmptyAction).toEqual({
      label: "Open Trading cockpit",
      route: "/trading",
      ariaLabel: "Open Trading cockpit from empty operator inbox",
      variant: "outline"
    });
  });

  it("keeps the headline in review when governed report-pack readiness is missing", () => {
    const state = buildOperatorReadinessConsoleState({
      strategy,
      trading: readyTrading,
      data,
      accounting: noReportPackGovernance,
      operatorInbox: cleanInbox,
      inboxLoading: false,
      inboxError: null
    });

    expect(state.overallLevel).toBe("review");
    expect(state.overallLabel).toBe("Review pending");
    expect(state.overallDetail).toContain("1 review checkpoint(s) still need attention");
    expect(state.checkpointGates.find((gate) => gate.id === "report-pack-ready")).toEqual(expect.objectContaining({
      value: "2 review",
      level: "review"
    }));
    expect(state.checkpointGates.find((gate) => gate.id === "report-pack-ready")?.action).toEqual(expect.objectContaining({
      label: "Open report packs",
      route: "/reporting/report-packs"
    }));
    expect(state.reportPackFacts[0]).toEqual(expect.objectContaining({ value: "No recipients", level: "review" }));
  });

  it("keeps mirrored run-handoff identity and route metadata in operator work items", () => {
    const state = buildOperatorReadinessConsoleState({
      strategy,
      trading,
      data,
      accounting,
      operatorInbox: inbox,
      inboxLoading: false,
      inboxError: null
    });

    const promotionItem = state.workItems.find((item) => item.id === "promotion-review-run-1");
    expect(promotionItem).toBeDefined();
    expect(promotionItem?.meta).toContain("run-1");
    expect(promotionItem?.action).toEqual(expect.objectContaining({ route: "/trading/readiness" }));

    const reconciliationItem = state.workItems.find((item) => item.id === "reconciliation-break-run-1-cash");
    expect(reconciliationItem).toBeDefined();
    expect(reconciliationItem?.meta).toContain("run-1");
    expect(reconciliationItem?.action).toEqual(expect.objectContaining({ route: "/accounting/reconciliation" }));
  });

  it("preserves route-aware packet continuity for blocker rows across state slices", () => {
    const state = buildOperatorReadinessConsoleState({
      strategy,
      trading,
      data,
      accounting,
      operatorInbox: inbox,
      inboxLoading: false,
      inboxError: null
    });

    const queueRoutes = state.workItems
      .filter((row) => row.action)
      .map((row) => `${row.id}:${row.action?.route}`);
    const queueIds = new Set(state.workItems.map((row) => row.id));

    expect(queueRoutes).toContain("promotion-review-run-1:/trading/readiness");
    expect(queueRoutes).toContain("reconciliation-break-run-1-cash:/accounting/reconciliation");
    expect(queueIds.has("promotion-review-run-1")).toBe(true);
    expect(queueIds.has("reconciliation-break-run-1-cash")).toBe(true);
  });

  it("clears stale account-scoped inbox rows while loading a new fund account inbox", async () => {
    let resolveFundTwoInbox: ((value: OperatorInbox) => void) | null = null;
    const fundTwoReadiness: TradingOperatorReadiness = {
      ...readyReadiness,
      brokerageSync: {
        ...readyReadiness.brokerageSync,
        fundAccountId: "fund-2"
      }
    };
    const fundTwoTrading: TradingWorkspaceResponse = {
      ...readyTrading,
      readiness: fundTwoReadiness
    };
    const getOperatorInbox = vi.fn((fundAccountId?: string, _options?: ApiRequestOptions) => {
      if (fundAccountId === "fund-1") {
        return Promise.resolve(inbox);
      }

      return new Promise<OperatorInbox>((resolve) => {
        resolveFundTwoInbox = resolve;
      });
    });
    const services = { getOperatorInbox };

    const { result, rerender } = renderHook(
      ({ tradingPayload }: { tradingPayload: TradingWorkspaceResponse }) => useOperatorReadinessConsoleViewModel({
        strategy,
        trading: tradingPayload,
        data,
        accounting: cleanGovernance,
        reporting: accounting
      }, services),
      { initialProps: { tradingPayload: readyTrading } }
    );

    await waitFor(() => expect(result.current.inboxSummary).toBe("2 review items need attention."));
    await waitFor(() => expect(result.current.workItems.length).toBeGreaterThan(0));
    expect(result.current.workItems.map((item) => item.id)).toContain("reconciliation-break-run-1-cash");

    rerender({ tradingPayload: fundTwoTrading });

    await waitFor(() => expect(getOperatorInbox).toHaveBeenLastCalledWith(
      "fund-2",
      expect.objectContaining({ signal: expect.any(Object) })
    ));
    await waitFor(() => expect(result.current.inboxLoadingLabel).toBe("Loading operator inbox..."));
    expect(result.current.inboxSummary).toBe("Operator inbox not loaded; using workstation payload fallbacks where available.");
    expect(result.current.workItems.map((item) => item.id)).not.toContain("reconciliation-break-run-1-cash");

    resolveFundTwoInbox?.(cleanInbox);
    await waitFor(() => expect(result.current.inboxSummary).toBe("No operator work items need attention."));
  });

  it("uses valid operating fund account scope before readiness brokerage fallback for inbox refresh", async () => {
    const fundAccountId = "53bf0251-17f6-4fb7-8dbe-6fb4966e2749";
    const tradingWithoutScopedReadiness: TradingWorkspaceResponse = {
      ...readyTrading,
      readiness: {
        ...readyReadiness,
        brokerageSync: null
      }
    };
    const getOperatorInbox = vi.fn<OperatorReadinessConsoleServices["getOperatorInbox"]>()
      .mockResolvedValue(cleanInbox);
    const services = { getOperatorInbox };

    renderHook(() => useOperatorReadinessConsoleViewModel({
      strategy,
      trading: tradingWithoutScopedReadiness,
      data,
      accounting: cleanGovernance,
      reporting: accounting,
      fundAccountId
    }, services));

    await waitFor(() => expect(getOperatorInbox).toHaveBeenCalledWith(
      fundAccountId,
      expect.objectContaining({ signal: expect.any(Object) })
    ));
  });

  it("aborts superseded operator inbox loads when the fund account changes", async () => {
    const fundOneInbox = new Promise<OperatorInbox>(() => undefined);
    let resolveFundTwoInbox: ((value: OperatorInbox) => void) | null = null;
    const fundTwoReadiness: TradingOperatorReadiness = {
      ...readyReadiness,
      brokerageSync: {
        ...readyReadiness.brokerageSync,
        fundAccountId: "fund-2"
      }
    };
    const getOperatorInbox = vi.fn((fundAccountId?: string, _options?: ApiRequestOptions) => {
      if (fundAccountId === "fund-1") {
        return fundOneInbox;
      }

      return new Promise<OperatorInbox>((resolve) => {
        resolveFundTwoInbox = resolve;
      });
    });
    const services = { getOperatorInbox };

    const { result, rerender } = renderHook(
      ({ tradingPayload }: { tradingPayload: TradingWorkspaceResponse }) => useOperatorReadinessConsoleViewModel({
        strategy,
        trading: tradingPayload,
        data,
        accounting: cleanGovernance,
        reporting: accounting
      }, services),
      { initialProps: { tradingPayload: readyTrading } }
    );

    await waitFor(() => expect(getOperatorInbox).toHaveBeenCalledWith(
      "fund-1",
      expect.objectContaining({ signal: expect.any(Object) })
    ));
    const fundOneSignal = getOperatorInbox.mock.calls[0]?.[1]?.signal;
    expect(fundOneSignal?.aborted).toBe(false);

    rerender({
      tradingPayload: {
        ...readyTrading,
        readiness: fundTwoReadiness
      }
    });

    await waitFor(() => expect(getOperatorInbox).toHaveBeenLastCalledWith(
      "fund-2",
      expect.objectContaining({ signal: expect.any(Object) })
    ));
    expect(fundOneSignal?.aborted).toBe(true);

    resolveFundTwoInbox?.(cleanInbox);
    await waitFor(() => expect(result.current.inboxSummary).toBe("No operator work items need attention."));
  });

  it("exposes a guarded inbox refresh command for recovering failed loads", async () => {
    const getOperatorInbox = vi.fn()
      .mockRejectedValueOnce(new Error("Operator inbox 503"))
      .mockResolvedValueOnce(inbox);
    const services = { getOperatorInbox };

    const { result } = renderHook(() => useOperatorReadinessConsoleViewModel({
      strategy,
      trading: readyTrading,
      data,
      accounting: cleanGovernance,
      reporting: accounting
    }, services));

    await waitFor(() => expect(result.current.inboxErrorText).toBe("Operator inbox 503"));
    expect(result.current.inboxRefreshLabel).toBe("Refresh inbox");
    expect(result.current.inboxRefreshAriaLabel).toBe("Refresh operator inbox work items");
    expect(result.current.inboxRefreshDisabled).toBe(false);

    await act(async () => {
      await result.current.refreshInbox();
    });

    await waitFor(() => expect(result.current.inboxSummary).toBe("2 review items need attention."));
    expect(result.current.inboxErrorText).toBeNull();
    expect(result.current.workItems.map((item) => item.id)).toContain("reconciliation-break-run-1-cash");
    expect(getOperatorInbox).toHaveBeenCalledTimes(2);
  });

  it("keeps selected operator work-item detail in the view model", () => {
    const state = buildOperatorReadinessConsoleState({
      strategy: null,
      trading: null,
      data: null,
      accounting: null,
      operatorInbox: {
        ...cleanInbox,
        items: [
          {
            workItemId: "critical-security-gap",
            kind: "SecurityMasterCoverage",
            label: "Critical security coverage gap",
            detail: "Resolve missing identifier coverage before accepting readiness.",
            tone: "Critical",
            createdAt: "2026-04-29T11:00:00Z",
            runId: null,
            fundAccountId: null,
            auditReference: null,
            workspace: "Accounting",
            targetRoute: "/accounting/security-master",
            targetPageTag: "SecurityMaster"
          },
          {
            workItemId: "report-pack-warning",
            kind: "ReportPackApproval",
            label: "Report pack warning",
            detail: "Attach owner sign-off before distribution.",
            tone: "Warning",
            createdAt: "2026-04-29T12:00:00Z",
            runId: null,
            fundAccountId: null,
            auditReference: "audit-report-pack",
            workspace: "Reporting",
            targetRoute: "/reporting",
            targetPageTag: "ReportPackApproval"
          }
        ],
        criticalCount: 1,
        warningCount: 1,
        reviewCount: 2,
        summary: "2 review items need attention."
      },
      inboxLoading: false,
      inboxError: null,
      selectedWorkItemId: "report-pack-warning"
    });

    expect(state.selectedWorkItemId).toBe("report-pack-warning");
    expect(state.selectedWorkItemDetail).toEqual(expect.objectContaining({
      id: "report-pack-warning",
      title: "Report pack warning",
      statusLabel: "Warning",
      action: expect.objectContaining({
        label: "Open report packs",
        route: "/reporting/report-packs"
      })
    }));
    expect(state.selectedWorkItemDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Attention", value: "Review" },
      { label: "Route", value: "/reporting/report-packs" },
      { label: "Evidence", value: "Reporting - ReportPackApproval - audit-report-pack" }
    ]));
  });

  it("keeps selected evidence-panel detail in the view model", () => {
    const selectedRows: Partial<Record<"active-paper-session", string>> = {
      "active-paper-session": "paper-equity"
    };
    const state = buildOperatorReadinessConsoleState({
      strategy,
      trading,
      data,
      accounting,
      operatorInbox: inbox,
      inboxLoading: false,
      inboxError: null,
      selectedPanelRowIds: selectedRows
    });

    const activeSessionPanel = state.panels.find((panel) => panel.id === "active-paper-session");

    expect(activeSessionPanel).toEqual(expect.objectContaining({
      tableLabel: "Active paper session readiness evidence table",
      detailLabel: "Selected active paper session evidence detail",
      selectedRowId: "paper-equity"
    }));
    expect(activeSessionPanel?.selectedDetail).toEqual(expect.objectContaining({
      id: "paper-equity",
      title: "Paper portfolio value",
      statusLabel: "$100,500.00",
      ariaLabel: "Selected active paper session evidence: Paper portfolio value"
    }));
    expect(activeSessionPanel?.selectedDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Evidence ID", value: "paper-equity" },
      { label: "Attention", value: "Ready" },
      { label: "Evidence", value: "Created Apr 29, 11:00 UTC" }
    ]));
  });
});
