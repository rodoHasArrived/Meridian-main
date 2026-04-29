import {
  buildOrderSubmitRequest,
  buildOrderTicketState,
  buildPromotionApprovalRequest,
  buildPromotionGateState,
  buildPromotionRejectionRequest,
  emptyOrderTicketForm,
  emptyPromotionGateForm,
  updateOrderTicketForm,
  validateOrderTicketForm,
  validatePromotionApproval,
  validatePromotionRejection
} from "@/screens/trading-screen.view-model";
import type { PromotionEvaluationResult } from "@/types";

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
