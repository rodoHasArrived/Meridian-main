import { buildOperatorReadinessConsoleState } from "@/screens/operator-readiness-console.view-model";
import type {
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  OperatorInbox,
  OperatorWorkItem,
  ResearchWorkspaceResponse,
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
    requiredOwners: ["Data Operations", "Trading"],
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

const research: ResearchWorkspaceResponse = {
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

const dataOperations: DataOperationsWorkspaceResponse = {
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

const governance: GovernanceWorkspaceResponse = {
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

const cleanGovernance: GovernanceWorkspaceResponse = {
  ...governance,
  reconciliationQueue: [],
  breakQueue: []
};

const noReportPackGovernance: GovernanceWorkspaceResponse = {
  ...cleanGovernance,
  reporting: {
    profileCount: 0,
    recommendedProfiles: [],
    profiles: [],
    reportPackTargets: [],
    summary: "No governed report-pack targets are configured."
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
  it("builds the API-first readiness console from shared workstation payloads", () => {
    const state = buildOperatorReadinessConsoleState({
      research,
      trading,
      dataOperations,
      governance,
      operatorInbox: inbox,
      inboxLoading: false,
      inboxError: null
    });

    expect(state.title).toBe("Operator Readiness Console");
    expect(state.overallLevel).toBe("blocked");
    expect(state.overallLabel).toBe("Blocked");
    expect(state.latestRuns[0]).toEqual(expect.objectContaining({ id: "run-1", value: "Needs Review" }));
    expect(state.activeSessionFacts[0]).toEqual(expect.objectContaining({ value: "paper-1", level: "ready" }));
    expect(state.providerTrustRows.some((row) => row.label === "DK1 provider trust")).toBe(true);
    expect(state.reconciliationRows[0]).toEqual(expect.objectContaining({ id: "run-1:cash", level: "blocked" }));
    expect(state.promotionRows.some((row) => row.label === "Promotion checklist")).toBe(true);
    expect(state.reportPackFacts[0]).toEqual(expect.objectContaining({ value: "Targets present", level: "ready" }));
    expect(state.apiSources.map((source) => source.endpoint)).toContain("/api/workstation/operator/inbox");
    expect(state.workItems.find((item) => item.id === "promotion-review-run-1")?.action).toEqual({
      label: "Open promotion review",
      route: "/trading/readiness",
      ariaLabel: "Open promotion review: Promotion checklist incomplete",
      variant: "outline"
    });
  });

  it("derives safe work-item routes and fallback actions in the view model", () => {
    const state = buildOperatorReadinessConsoleState({
      research: null,
      trading: null,
      dataOperations: null,
      governance: null,
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
            targetRoute: "/data/providers",
            targetPageTag: "ProviderTrust"
          }
        ],
        warningCount: 1,
        criticalCount: 1,
        reviewCount: 2,
        summary: "2 review items need attention."
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
    expect(state.workItems[1].action).toEqual({
      label: "Open provider trust",
      route: "/data/providers",
      ariaLabel: "Open provider trust: Provider trust gate review",
      variant: "outline"
    });
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
      research: null,
      trading: null,
      dataOperations: null,
      governance: null,
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
    expect(state.workItems.map((item) => item.label)).not.toContain("Info item");
    expect(state.workItemsSummary).toBe("Showing 6 of 8 operator work items; 1 critical item, 6 warnings, 1 info item. Critical items sort first.");
    expect(state.workItemsOverflowText).toBe("2 additional work items hidden from this view after priority sorting.");
  });

  it("surfaces operator inbox failures while keeping payload fallbacks visible", () => {
    const state = buildOperatorReadinessConsoleState({
      research,
      trading,
      dataOperations,
      governance,
      operatorInbox: null,
      inboxLoading: false,
      inboxError: "Request failed for /api/workstation/operator/inbox (503)"
    });

    expect(state.inboxErrorText).toContain("503");
    expect(state.workItems).toHaveLength(1);
    expect(state.overallDetail).toContain("operator inbox failed to load");
    expect(state.statusAnnouncement).toContain("Operator inbox failed");
  });

  it("keeps overall readiness in review while the operator inbox is still loading", () => {
    const state = buildOperatorReadinessConsoleState({
      research,
      trading: readyTrading,
      dataOperations,
      governance: cleanGovernance,
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
      research,
      trading: readyTrading,
      dataOperations,
      governance: cleanGovernance,
      operatorInbox: null,
      inboxLoading: false,
      inboxError: "Request failed for /api/workstation/operator/inbox (503)"
    });

    const readyState = buildOperatorReadinessConsoleState({
      research,
      trading: readyTrading,
      dataOperations,
      governance: cleanGovernance,
      operatorInbox: cleanInbox,
      inboxLoading: false,
      inboxError: null
    });

    expect(loadingState.overallLevel).toBe("review");
    expect(loadingState.overallLabel).toBe("Review pending");
    expect(readyState.overallLevel).toBe("ready");
    expect(readyState.overallLabel).toBe("Ready");
  });

  it("keeps the headline in review when governed report-pack readiness is missing", () => {
    const state = buildOperatorReadinessConsoleState({
      research,
      trading: readyTrading,
      dataOperations,
      governance: noReportPackGovernance,
      operatorInbox: cleanInbox,
      inboxLoading: false,
      inboxError: null
    });

    expect(state.overallLevel).toBe("review");
    expect(state.overallLabel).toBe("Review pending");
    expect(state.overallDetail).toContain("report-pack readiness item(s) still need review");
    expect(state.reportPackFacts[0]).toEqual(expect.objectContaining({ value: "No targets", level: "review" }));
  });
});
