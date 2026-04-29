import {
  buildOrderSubmitRequest,
  buildOrderTicketState,
  buildPromotionApprovalRequest,
  buildPromotionGateState,
  buildPromotionRejectionRequest,
  buildTradingReadinessState,
  emptyOrderTicketForm,
  emptyPromotionGateForm,
  formatReadinessStatusValue,
  mapBrokerageSyncLevel,
  mapReadinessStatusLevel,
  updateOrderTicketForm,
  validateOrderTicketForm,
  validatePromotionApproval,
  validatePromotionRejection
} from "@/screens/trading-screen.view-model";
import type { PromotionEvaluationResult, TradingOperatorReadiness } from "@/types";

const eligibleEvaluation: PromotionEvaluationResult = {
  runId: "run-1",
  strategyId: "strat-1",
  strategyName: "S1",
  sourceMode: "backtest",
  targetMode: "paper",
  isEligible: true,
  sharpeRatio: 1.2,
  maxDrawdownPercent: 5,
  totalReturn: 10,
  reason: "Eligible",
  found: true,
  ready: true
};

const blockedReadiness: TradingOperatorReadiness = {
  asOf: "2026-04-26T16:05:00Z",
  overallStatus: "Blocked",
  readyForPaperOperation: false,
  acceptanceGates: [],
  activeSession: null,
  sessions: [],
  replay: null,
  controls: {
    circuitBreakerOpen: false,
    circuitBreakerReason: null,
    circuitBreakerChangedBy: null,
    circuitBreakerChangedAt: null,
    manualOverrideCount: 0,
    symbolLimitCount: 0,
    defaultMaxPositionSize: null
  },
  promotion: null,
  trustGate: {
    gateId: "dk1",
    status: "ready-for-operator-review",
    readyForOperatorReview: true,
    operatorSignoffRequired: true,
    operatorSignoffStatus: "pending",
    generatedAt: "2026-04-26T15:00:00Z",
    packetPath: "artifacts/provider-validation/_automation/2026-04-26/dk1-pilot-parity-packet.json",
    sourceSummary: "wave1-validation-summary.json",
    requiredSampleCount: 4,
    readySampleCount: 4,
    validatedEvidenceDocumentCount: 2,
    requiredOwners: ["ops"],
    blockers: [],
    detail: "Awaiting owner sign-off.",
    operatorSignoff: null
  },
  brokerageSync: {
    fundAccountId: "fund-1",
    providerId: "alpaca",
    externalAccountId: "PA-404",
    health: "Failed",
    isLinked: true,
    isStale: true,
    lastAttemptedSyncAt: "2026-04-26T15:58:00Z",
    lastSuccessfulSyncAt: null,
    lastError: "Alpaca credentials are missing.",
    positionCount: 0,
    openOrderCount: 0,
    fillCount: 0,
    cashTransactionCount: 0,
    securityMissingCount: 0,
    warnings: ["Portfolio snapshot failed."]
  },
  workItems: [
    {
      workItemId: "brokerage-sync-failed-fund-1",
      kind: "BrokerageSync",
      label: "Brokerage sync failed",
      detail: "Sync broker credentials before paper operation.",
      tone: "Critical",
      createdAt: "2026-04-26T16:05:00Z",
      runId: null,
      fundAccountId: "fund-1",
      auditReference: null,
      workspace: "Trading",
      targetRoute: "/api/fund-accounts/fund-1/brokerage-sync",
      targetPageTag: "AccountPortfolio"
    }
  ],
  warnings: ["Portfolio snapshot failed."]
};

describe("trading readiness view model", () => {
  it("derives contract summary rows, tones, and assistive labels", () => {
    const state = buildTradingReadinessState({
      readiness: blockedReadiness,
      refreshing: false,
      errorText: null
    });

    expect(state.summaryLabel).toBe("Trading readiness contract summary");
    expect(state.summaryRows).toEqual([
      expect.objectContaining({ id: "overall", label: "Overall", value: "Blocked", level: "atRisk", ariaLabel: "Overall readiness: Blocked" }),
      expect.objectContaining({ id: "paper", label: "Paper", value: "Not paper ready", level: "review" }),
      expect.objectContaining({ id: "brokerage", label: "Brokerage", value: "Failed stale", level: "atRisk" }),
      expect.objectContaining({ id: "as-of", label: "As of", value: "2026-04-26T16:05:00Z", level: "review" })
    ]);
    expect(state.workItems).toHaveLength(1);
    expect(state.warnings).toEqual(["Portfolio snapshot failed."]);
    expect(state.statusAnnouncement).toBe("Trading readiness blocked as of 2026-04-26T16:05:00Z.");
  });

  it("derives refresh and error copy for readiness commands", () => {
    const refreshing = buildTradingReadinessState({
      readiness: blockedReadiness,
      refreshing: true,
      errorText: null
    });

    expect(refreshing.refreshButtonLabel).toBe("Refreshing...");
    expect(refreshing.refreshAriaLabel).toBe("Refreshing trading readiness");
    expect(refreshing.statusAnnouncement).toBe("Refreshing trading readiness.");

    const failed = buildTradingReadinessState({
      readiness: null,
      refreshing: false,
      errorText: "Network failed."
    });

    expect(failed.summaryRows).toEqual([]);
    expect(failed.statusAnnouncement).toBe("Trading readiness refresh failed: Network failed.");
  });

  it("normalizes readiness and brokerage status levels", () => {
    expect(formatReadinessStatusValue("ReviewRequired")).toBe("Review required");
    expect(mapReadinessStatusLevel("Ready")).toBe("ready");
    expect(mapReadinessStatusLevel("Blocked")).toBe("atRisk");
    expect(mapBrokerageSyncLevel({ ...blockedReadiness.brokerageSync!, health: "Healthy", isStale: false })).toBe("ready");
    expect(mapBrokerageSyncLevel(blockedReadiness.brokerageSync!)).toBe("atRisk");
  });
});

describe("trading order ticket view model", () => {
  it("normalizes order input and clears price for market orders", () => {
    const form = updateOrderTicketForm(
      {
        ...emptyOrderTicketForm,
        symbol: " aapl ",
        type: "Limit",
        quantity: 10,
        limitPrice: 189.44
      },
      "type",
      "Market"
    );

    expect(form.limitPrice).toBeNull();
    expect(buildOrderSubmitRequest(form)).toEqual({
      symbol: "AAPL",
      side: "Buy",
      type: "Market",
      quantity: 10
    });
  });

  it("validates required symbol, quantity, and limit price fields", () => {
    expect(validateOrderTicketForm(emptyOrderTicketForm)).toBe("Enter a symbol before submitting an order.");
    expect(validateOrderTicketForm({ ...emptyOrderTicketForm, symbol: "SPY" }))
      .toBe("Enter an order quantity greater than zero.");
    expect(validateOrderTicketForm({
      ...emptyOrderTicketForm,
      symbol: "SPY",
      type: "Stop",
      quantity: 5,
      limitPrice: null
    })).toBe("Enter a stop price greater than zero.");
  });

  it("derives order command labels, disabled state, and announcements", () => {
    const invalid = buildOrderTicketState({
      form: { ...emptyOrderTicketForm, symbol: "MSFT" },
      open: true,
      phase: "idle",
      orderId: null,
      errorText: null
    });

    expect(invalid.canSubmit).toBe(false);
    expect(invalid.invalidField).toBe("quantity");
    expect(invalid.requirementText).toBe("Enter an order quantity greater than zero.");

    const submitting = buildOrderTicketState({
      form: { ...emptyOrderTicketForm, symbol: "MSFT", quantity: 2 },
      open: true,
      phase: "submitting",
      orderId: null,
      errorText: null
    });

    expect(submitting.submitButtonLabel).toBe("Submitting...");
    expect(submitting.statusAnnouncement).toBe("Submitting order request.");

    const submitted = buildOrderTicketState({
      form: emptyOrderTicketForm,
      open: false,
      phase: "submitted",
      orderId: "ord-42",
      errorText: null
    });

    expect(submitted.successText).toBe("Order submitted - ord-42.");
    expect(submitted.statusAnnouncement).toBe("Order submitted with id ord-42.");
  });
});

describe("trading promotion gate view model", () => {
  it("keeps promotion approval disabled until evaluation and rationale are ready", () => {
    const initial = buildPromotionGateState({
      form: { ...emptyPromotionGateForm, runId: " run-1 " },
      busy: false,
      phase: "idle",
      errorText: null,
      outcome: null,
      evaluation: null,
      history: []
    });

    expect(initial.canEvaluate).toBe(true);
    expect(initial.canPromote).toBe(false);
    expect(initial.approvalRequirementText).toBe("Approval remains disabled until gate checks return an eligible result.");

    const ready = buildPromotionGateState({
      form: {
        ...emptyPromotionGateForm,
        runId: " run-1 ",
        approvedBy: " operator-7 ",
        approvalReason: " Meets risk constraints "
      },
      busy: false,
      phase: "idle",
      errorText: null,
      outcome: null,
      evaluation: eligibleEvaluation,
      history: []
    });

    expect(ready.canPromote).toBe(true);
    expect(ready.nextActionText).toBe("Promotion trace is ready for confirmation.");
    expect(validatePromotionApproval(ready.form, eligibleEvaluation)).toBeNull();
  });

  it("derives rejection readiness from run id, operator, and rejection reason", () => {
    expect(validatePromotionRejection({ ...emptyPromotionGateForm, runId: "run-1" })).toBe(
      "Run id, operator, and rejection reason are required."
    );

    const state = buildPromotionGateState({
      form: {
        ...emptyPromotionGateForm,
        runId: "run-1",
        approvedBy: "operator-7",
        rejectionReason: "Risk review failed on drawdown stability."
      },
      busy: false,
      phase: "idle",
      errorText: null,
      outcome: null,
      evaluation: eligibleEvaluation,
      history: []
    });

    expect(state.canReject).toBe(true);
    expect(state.rejectionRequirementText).toBe("Rejection request is ready to write an audit-linked decision.");
  });

  it("builds trimmed approval and rejection requests without empty optional fields", () => {
    const form = {
      ...emptyPromotionGateForm,
      runId: " run-1 ",
      approvedBy: " operator-7 ",
      approvalReason: " Meets risk constraints ",
      rejectionReason: " Drawdown instability ",
      reviewNotes: " ",
      manualOverrideId: " override-9 "
    };

    expect(buildPromotionApprovalRequest(form)).toEqual({
      runId: "run-1",
      approvedBy: "operator-7",
      approvalReason: "Meets risk constraints",
      reviewNotes: undefined,
      manualOverrideId: "override-9"
    });
    expect(buildPromotionRejectionRequest(form)).toEqual({
      runId: "run-1",
      reason: "Drawdown instability",
      rejectedBy: "operator-7",
      reviewNotes: undefined,
      manualOverrideId: "override-9"
    });
  });

  it("announces busy and error states for assistive technology", () => {
    const busy = buildPromotionGateState({
      form: { ...emptyPromotionGateForm, runId: "run-1" },
      busy: true,
      phase: "evaluating",
      errorText: null,
      outcome: null,
      evaluation: null,
      history: []
    });

    expect(busy.evaluateButtonLabel).toBe("Evaluating...");
    expect(busy.statusAnnouncement).toBe("Evaluating promotion gate checks.");

    const failed = buildPromotionGateState({
      form: { ...emptyPromotionGateForm, runId: "run-1" },
      busy: false,
      phase: "idle",
      errorText: "eval failed",
      outcome: null,
      evaluation: null,
      history: []
    });

    expect(failed.statusAnnouncement).toBe("Promotion gate failed: eval failed");
  });
});
